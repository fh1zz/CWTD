using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PetTD
{
    /// <summary>Offline, deterministic regression checks. No engine or test package required.</summary>
    public static class CoreChecks
    {
        public static string Run(GameConfig config)
        {
            if (config == null) throw new Exception("核心回归失败：缺少配置。");
            string before = Describe(config);
            Suite suite = new Suite(config);
            string result = suite.Run();
            result += AutoSkillChecks.Run(config);
            result += TrailChecks.Run(config);
            result += CombatFxChecks.Run(config);
            result += EnemyRosterChecks.Run(config);
            if (Describe(config) != before) throw new Exception("核心回归失败：测试修改了传入配置。");
            return result;
        }

        // Optional offline executable, excluded from the Unity/.NET Standard build.
        // mono mcs.exe -define:CORE_CHECKS_EXE -r:System.Runtime.Serialization
        //   -out:<temporary>/CoreChecks.exe GameModel.cs CoreChecks.cs
        // mono <temporary>/CoreChecks.exe <path>/balance.json
#if CORE_CHECKS_EXE
        public static int Main(string[] args)
        {
            if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] != "--legacy"))
                throw new Exception("请提供 balance.json 路径，可选 --legacy 在内存中测试旧配置。");
            using (System.IO.FileStream stream = System.IO.File.OpenRead(args[0]))
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer reader =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(GameConfig));
                GameConfig config = (GameConfig)reader.ReadObject(stream);
                Console.WriteLine(Run(args.Length == 2 ? Legacy(config) : config));
            }
            return 0;
        }
#endif

        private sealed class Suite
        {
            private readonly GameConfig source;
            private readonly StringBuilder report = new StringBuilder();
            private string test;
            private int groups, assertions;

            internal Suite(GameConfig source) { this.source = source; }

            internal string Run()
            {
                Check("公开合同与纯 System 依赖", Contract);
                Check("归一化路径、14 地块与道路避让", Geometry);
                Check("赠送宠物、编队限定与输入复制", Team);
                Check("池内九合三、无连跳与最高等级", PoolEvolution);
                Check("场上主体保留、池材料优先与 UID 顺序", TowerEvolution);
                Check("满池和金币不足的原子事务及随机流", Transactions);
                Check("部署、占位、回收再部署和宠物蛋限制", Deployment);
                Check("出售价格、重复出售与回收无金币", Sale);
                Check("池排序：等级、元素、UID、蛋置后", Sorting);
                Check("准备、运行、末波即抽即用与旧方法别名", DirectDraw);
                Check("出怪结束、清场、手动开波与清波奖励", Waves);
                Check("暂停全部计时与恢复同一模拟状态", Pause);
                Check("暂停和终局拒绝全部局内操作", LockedActions);
                Check("基地归零立即失败且优先于清波", Failure);
                Check("死亡、毒与技能只结算一次奖励", DeathReward);
                Check("最大路径进度寻敌、同进度 UID 和穿甲", Targeting);
                Check("同物种毒取强不叠加、弱毒续时与到期", Poison);
                Check("减速取最强、不相加与精确到期", Slow);
                Check("火狐溅射范围和单次命中", Splash);
                Check("光环范围、伙伴限定和不叠加", Aura);
                Check("重击、疾射频率与等级成长", HeavyAndRapid);
                Check("星雨范围、护甲、冷却和非法落点", Skill);
                Check("三种敌人、末波精英与关卡参数推导", EnemyVariants);
                Check("数据化怪物速度、击杀奖励及旧配置回退", EnemyBalance);
                Check("无效时间输入与非法配置", InvalidInputs);
                Check("旧阶段回退、图鉴预览与35阶段数据", StageData);
                Check("阶段数组、有限数值和范围错误校验", StageValidation);
                Check("七物种逐阶三合一与81只一阶合成五阶", AllEvolutions);
                Check("穿甲、减速、疾射阶段多目标快照与去重", StageTargets);
                Check("阶段攻击间隔和射程实际参与战斗", StageTiming);
                Check("跨阶段毒最终DPS、保强保时与光环快照", StagePoison);
                Check("跨阶段减速保强保时及到期", StageSlow);
                Check("阶段溅射半径且targets不复制群伤", StageSplash);
                Check("阶段光环范围与最强增益", StageAura);
                Check("五阶段精英增伤且普通敌人无加成", StageHeavy);
                Check("非精英处决阈值、精英免疫及一次奖励", StageExecution);
                Check("相同种子重现和时间分片一致", Determinism);
                Check("三关九次普通策略模拟及完整胜败", Batch);
                report.Insert(0, "核心回归通过：" + groups + " 组，" + assertions + " 项断言。\n");
                return report.ToString();
            }

            private void Check(string name, Action action)
            {
                test = name;
                action();
                groups++;
                report.Append("通过：").Append(name).Append('\n');
            }
            private void Assert(bool condition, string message)
            {
                assertions++;
                if (!condition) throw new Exception("核心回归失败【" + test + "】：" + message);
            }
            private void Near(float actual, float expected, string message, float tolerance = .003f)
            {
                Assert(!Single.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance,
                    message + "；实际 " + F(actual) + "，预期 " + F(expected));
            }
            private void Unchanged(GameModel model, Func<bool> action, string message)
            {
                string before = Snapshot(model);
                Assert(!action(), message + "应失败");
                Assert(before == Snapshot(model), message + "不应产生状态副作用");
                Assert(HasChinese(model.LastMessage), message + "应有中文反馈");
            }
            private GameModel New(params string[] team)
            {
                // Keep the original rule suite exercising stages=null explicitly.
                // Dedicated stage checks and the nine full runs use the real input.
                return new GameModel(Legacy(source), 0, team.Length == 0 ? FirstTeam(source, 0) : team, 73);
            }
            private GameModel Short(int waves = 3, int count = 1)
            {
                GameConfig config = Legacy(source);
                config.levels[0].waves = waves;
                config.levels[0].baseCount = count;
                config.levels[0].countGrowth = 0;
                config.levels[0].spawnInterval = .1f;
                return new GameModel(config, 0, FirstTeam(config, 0), 73);
            }
            // Fixed straight laboratory lane: distance/radius checks must not depend on artwork bends.
            private static void FixtureGeometry(GameModel model)
            {
                model.Path=new List<V2>{new V2(0,.72f),new V2(1,.72f)};
                model.Pads=new List<V2>{
                    new V2(.07f,.56f),new V2(.08f,.88f),new V2(.30f,.85f),new V2(.30f,.57f),
                    new V2(.09f,.31f),new V2(.20f,.17f),new V2(.37f,.21f),new V2(.58f,.24f),
                    new V2(.38f,.47f),new V2(.59f,.49f),new V2(.59f,.89f),new V2(.87f,.87f),
                    new V2(.89f,.56f),new V2(.88f,.32f)};
            }

            private GameModel Arena(params string[] team)
            {
                GameModel model = New(team);
                FixtureGeometry(model);
                model.Level.waves = 2;
                model.Level.baseCount = 1;
                model.Level.countGrowth = 0;
                Assert(model.StartWave(), "战斗场应能开波");
                model.Tick(1f / 60f);
                model.Enemies.Clear();
                model.Pool.Clear();
                // A stationary distant survivor prevents accidental wave settlement.
                AddEnemy(model, 9900, Length(model) - .05f, 1000000f);
                return model;
            }
            private Pet AddPet(GameModel model, string species, int uid, int level = 1, int pad = -1, int ready = 0)
            {
                Pet pet = new Pet { Uid = uid, Species = species, Level = level, Pad = pad, ReadyWave = ready };
                if (pad < 0) model.Pool.Add(pet); else model.Towers.Add(pet);
                return pet;
            }
            private Enemy AddEnemy(GameModel model, int id, float progress, float hp = 1000f)
            {
                V2 point = Point(model, progress);
                Enemy enemy = new Enemy { Id = id, Progress = progress, X = point.X, Y = point.Y,
                    Hp = hp, MaxHp = hp, Speed = 0f, Reward = 17, Leak = 1 };
                model.Enemies.Add(enemy);
                return enemy;
            }
            private void Drain(GameModel model)
            {
                int guard = 0;
                while (model.Stage == RunStage.Running && guard++ < 2000)
                {
                    foreach (Enemy enemy in model.Enemies) enemy.Hp = 0f;
                    model.Tick(.05f);
                }
                Assert(guard < 2000, "波次应在有限时间内完成");
            }

            private void Contract()
            {
                Fields(typeof(GameConfig), "initialGold initialLives drawCost poolCapacity maxLevel sellBase clearReward clearRewardGrowth levelDamageScale skillCooldown skillDamage skillRadius enemyBaseSpeed killRewards pets levels enemyTypes");
                Fields(typeof(PetSpec), "id name element role description colorHex effect damage range interval effectPower atlasIndex stages basicName passiveName");
                Fields(typeof(StageSpec), "name ability description appearance damageMultiplier rangeMultiplier intervalMultiplier effectPower effectDuration executeThreshold eliteMultiplier targets active");
                Fields(typeof(LevelSpec), "id name subtitle waves slots baseCount countGrowth hpScale speedScale baseHp hpGrowth spawnInterval enemyWaves bossId");
                Fields(typeof(Pet), "Uid Level ReadyWave Pad Species Cooldown ActiveRemaining ActiveBuffRemaining ActiveDamageBonus ActiveAttackSpeedBonus AutoCasts");
                Fields(typeof(Enemy), "Id Kind Reward Leak X Y Progress Hp MaxHp Speed Armor SlowRemaining PoisonRemaining PoisonDps BurnRemaining BurnDps RootRemaining VulnerableRemaining Vulnerability Species Generation Shield MaxShield AbilityRemaining PulseRemaining Enraged");
                Fields(typeof(CombatFx), "X Y ToX ToY Life Duration PetIndex SourceLevel TargetId TargetKind TargetSize IsAuto Radius");
                Fields(typeof(GameModel), "Config Level Pool Towers Enemies Effects Path Pads Coins Lives Wave Kills Stage Paused Elapsed SkillRemaining LastMessage");
                Assert(typeof(GameConfig).IsSerializable && typeof(PetSpec).IsSerializable
                    && typeof(LevelSpec).IsSerializable && typeof(StageSpec).IsSerializable, "配置类型必须可序列化");
                Assert(typeof(Pet).GetProperty("IsEgg").PropertyType == typeof(bool)
                    && !typeof(Pet).GetProperty("IsEgg").CanWrite, "IsEgg 应为只读布尔属性");
                Assert(typeof(GameModel).GetProperty("RemainingToSpawn").PropertyType == typeof(int), "剩余出怪属性类型");
                Method("Spec", typeof(PetSpec), typeof(string));
                Method("DrawEgg", typeof(bool)); Method("StartWave", typeof(bool));
                Method("DrawPet", typeof(bool)); Method("StageInfo", typeof(StageSpec), typeof(Pet));
                Method("Interval", typeof(float), typeof(Pet));
                Method("Tick", typeof(void), typeof(float));
                Method("Deploy", typeof(bool), typeof(int), typeof(int));
                Method("Recall", typeof(bool), typeof(int)); Method("Sell", typeof(bool), typeof(int));
                Method("SellValue", typeof(int), typeof(Pet)); Method("CanEvolve", typeof(bool), typeof(int));
                Method("EvolveTower", typeof(bool), typeof(int)); Method("EvolvePool", typeof(int));
                Method("CanEvolvePool", typeof(bool)); Method("CastSkill", typeof(bool), typeof(float), typeof(float));
                Method("Damage", typeof(float), typeof(Pet)); Method("Range", typeof(float), typeof(Pet));
                Method("FindPet", typeof(Pet), typeof(int)); Method("Distance", typeof(float), typeof(V2), typeof(V2));
                foreach (AssemblyName assembly in typeof(GameModel).Assembly.GetReferencedAssemblies())
                    Assert(assembly.Name.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) < 0, "核心不可依赖 Unity");
            }
            private void Fields(Type type, string names)
            {
                string[] expected = names.Split(' ');
                FieldInfo[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                Assert(actual.Length == expected.Length, type.Name + "公开字段数量");
                foreach (string name in expected) Assert(type.GetField(name) != null, type.Name + "." + name);
            }
            private void Method(string name, Type result, params Type[] args)
            {
                MethodInfo method = typeof(GameModel).GetMethod(name, args);
                Assert(method != null && method.ReturnType == result, "方法签名 " + name);
            }

            private void Geometry()
            {
                GameModel model = New();
                Assert(model.Pads.Count == 14 && model.Path.Count >= 2, "必须提供十四个地块和有效路径");
                Near(model.Distance(new V2(0, 0), new V2(1, 0)), 1.6f, "横向距离比例");
                Near(model.Distance(new V2(0, 0), new V2(0, 1)), 1f, "纵向距离比例");
                Assert(model.Path[0].X == 0f && model.Path[model.Path.Count - 1].X == 1f
                    && model.Path[model.Path.Count - 1].Y < .5f, "路径从左侧至右上出口");
                foreach (V2 p in model.Path) Assert(Unit(p), "路径坐标归一化");
                for (int i = 0; i < model.Pads.Count; i++)
                {
                    V2 p = model.Pads[i];
                    Assert(Unit(p), "地块坐标归一化");
                    for (int j = 0; j < i; j++) Assert(model.Distance(p, model.Pads[j]) > .05f, "地块不得重合");
                    for (int j = 1; j < model.Path.Count; j++)
                        Assert(SegmentDistance(p, model.Path[j - 1], model.Path[j]) > .08f, "地块应避开道路");
                }
            }

            private void Team()
            {
                GameConfig config = Clone(source);
                string[] team = FirstTeam(config, 0);
                string[] saved = (string[])team.Clone();
                GameModel model = new GameModel(config, 0, team, 101);
                Assert(model.Pool.Count == Math.Min(3, team.Length), "赠送前三个编队物种");
                foreach (string id in saved) Assert(model.Pool.Exists(p => p.Species == id && !p.IsEgg && p.Level == 1), "赠送应为成熟一级宠物");
                team[0] = "调用方已修改数组";
                HashSet<string> observed = new HashSet<string>();
                model.Coins = 1000000;
                for (int i = 0; i < 240; i++)
                {
                    model.Pool.Clear();
                    Assert(model.DrawEgg(), "抽取应成功");
                    string id = model.Pool[0].Species;
                    Assert(Array.IndexOf(saved, id) >= 0, "只能抽取创建时的出战编队");
                    observed.Add(id);
                }
                Assert(observed.Count == saved.Length, "确定性样本应覆盖编队全部物种");
                Throws(() => new GameModel(config, 0, new string[] { saved[0], saved[0] }, 1), "重复物种");
                Throws(() => new GameModel(config, 0, new string[] { "未知" }, 1), "未知物种");
                Throws(() => new GameModel(config, 0, new string[0], 1), "空编队");
            }

            private void PoolEvolution()
            {
                GameModel model = New("falcon"); model.Pool.Clear();
                for (int i = 9; i >= 1; i--) AddPet(model, "falcon", 100 + i);
                Assert(model.CanEvolvePool() && model.EvolvePool() == 3, "九只一级应完成三次进化");
                Assert(model.Pool.Count == 3 && model.Pool.TrueForAll(p => p.Level == 2), "单次点击只能得到三只二级");
                Assert(model.CanEvolvePool() && model.EvolvePool() == 1 && model.Pool[0].Level == 3, "第二次点击才可合成三级");
                model.Pool.Clear();
                for (int i = 0; i < 3; i++) AddPet(model, "falcon", 200 + i);
                AddPet(model, "falcon", 210, 2); AddPet(model, "falcon", 211, 2);
                Assert(model.EvolvePool() == 1 && model.Pool.Count == 3 && model.Pool.TrueForAll(p => p.Level == 2), "新产出不得加入已有高一级材料");
                model.Pool.Clear();
                for (int i = 0; i < 3; i++) AddPet(model, "falcon", 300 + i, model.Config.maxLevel);
                for (int i = 0; i < 3; i++) AddPet(model, "falcon", 400 + i, 1, -1, 2);
                string before = Snapshot(model);
                Assert(!model.CanEvolvePool() && model.EvolvePool() == 0 && Snapshot(model) == before, "满级宠物与蛋不可进化");
            }

            private void TowerEvolution()
            {
                GameModel model = New("falcon"); model.Pool.Clear();
                Pet subject = AddPet(model, "falcon", 90, 1, 0); subject.Cooldown = 10f;
                AddPet(model, "falcon", 10, 1, 1); AddPet(model, "falcon", 20, 1, 2);
                AddPet(model, "falcon", 40); AddPet(model, "falcon", 50);
                int coins = model.Coins;
                Assert(model.CanEvolve(90) && model.EvolveTower(90), "主体应可进化");
                Assert(model.FindPet(90) == subject && subject.Level == 2 && subject.Pad == 0, "主体身份和地块不变");
                Assert(model.FindPet(40) == null && model.FindPet(50) == null
                    && model.FindPet(10) != null && model.FindPet(20) != null, "池内材料优先于较小 UID 的塔");
                Near(subject.Cooldown, model.Spec("falcon").interval, "冷却取当前与新间隔较小值");
                Assert(model.Coins == coins, "进化不产生未约定经济消耗");
                subject.Level = 1; subject.Cooldown = .2f;
                AddPet(model, "falcon", 60);
                Assert(model.EvolveTower(90), "池一只加场上一只可进化");
                Assert(model.FindPet(60) == null && model.FindPet(10) == null && model.FindPet(20) != null, "塔材料按 UID 从小到大");
                Near(subject.Cooldown, .2f, "进化不重置更短的剩余冷却");
                Unchanged(model, () => model.EvolveTower(90), "不足材料进化");
            }

            private void Transactions()
            {
                GameModel a = New(), b = New();
                a.Coins = a.Config.drawCost - 1; b.Coins = a.Coins;
                Unchanged(a, () => a.DrawEgg(), "金币不足抽取");
                a.Coins = b.Coins = 100000;
                Assert(a.DrawEgg() && b.DrawEgg() && Snapshot(a) == Snapshot(b), "失败抽取不得推进随机数或 UID");
                while (a.Pool.Count < a.Config.poolCapacity) { Assert(a.DrawEgg() && b.DrawEgg(), "填满池"); }
                Unchanged(a, () => a.DrawEgg(), "满池抽取");
                a.Pool.RemoveAt(a.Pool.Count - 1); b.Pool.RemoveAt(b.Pool.Count - 1);
                Assert(a.DrawEgg() && b.DrawEgg() && Snapshot(a) == Snapshot(b), "满池失败不得改变后续随机流");
                Pet tower = AddPet(a, a.Config.pets[0].id, 5000, 1, 0);
                Unchanged(a, () => a.Recall(tower.Uid), "满池回收");
            }

            private void Deployment()
            {
                GameModel model = New(); Pet pet = model.Pool[0];
                Unchanged(model, () => model.Deploy(pet.Uid, -1), "非法地块");
                Unchanged(model, () => model.Deploy(999999, 0), "未知宠物");
                Assert(model.Deploy(pet.Uid, 0), "成熟宠物应能部署");
                Pet other = model.Pool[0];
                Unchanged(model, () => model.Deploy(other.Uid, 0), "已占用地块");
                Unchanged(model, () => model.Deploy(pet.Uid, 1), "塔直接移动");
                Assert(model.Recall(pet.Uid) && model.Deploy(pet.Uid, 1) && pet.Pad == 1 && model.Towers.Count == 1, "回收后重新部署归属唯一");
                Assert(model.Recall(pet.Uid) && pet.Pad == -1 && model.Towers.Count == 0, "回收归属唯一");
                Pet egg = AddPet(model, pet.Species, 9000, 1, -1, 2); // Legacy malformed-state fixture only.
                Unchanged(model, () => model.Deploy(egg.Uid, 0), "蛋部署");
                Unchanged(model, () => model.Sell(egg.Uid), "蛋出售");
                Assert(!model.CanEvolve(egg.Uid), "蛋不可进化");
                model.Pool.Clear();
                for (int i = 0; i < model.Pads.Count; i++)
                {
                    Pet recruit = AddPet(model, pet.Species, 1000 + i);
                    Assert(model.Deploy(recruit.Uid, i), "编队物种数不应限制场上塔数");
                }
                Assert(model.Towers.Count == 14, "十四地块可各部署一只塔");
            }

            private void Sale()
            {
                GameModel model = New(); Pet pet = model.Pool[0];
                int expected = model.Config.sellBase;
                for (int level = 1; level <= model.Config.maxLevel; level++)
                {
                    pet.Level = level; Assert(model.SellValue(pet) == expected, "每级售价为基础乘三的整数幂");
                    expected *= 3;
                }
                int before = model.Coins, value = model.SellValue(pet);
                Assert(model.Deploy(pet.Uid, 0) && model.Recall(pet.Uid) && model.Coins == before, "回收不产生金币");
                Assert(model.Sell(pet.Uid) && model.Coins == before + value && model.FindPet(pet.Uid) == null, "出售应恰好结算一次");
                Unchanged(model, () => model.Sell(pet.Uid), "重复出售");
            }

            private void Sorting()
            {
                GameModel model = New("falcon"); model.Pool.Clear();
                AddPet(model, "bat", 301, 2); AddPet(model, "falcon", 300, 2);
                AddPet(model, "deer", 200, 3); AddPet(model, "sprout", 310, 2);
                AddPet(model, "falcon", 299, 2); AddPet(model, "falcon", 900, 5, -1, 2);
                AddPet(model, "falcon", 899, 1, -1, 2);
                Assert(model.DrawEgg(), "触发池排序");
                int[] mature = { 200, 299, 300, 310, 301, 2 };
                for (int i = 0; i < mature.Length; i++) Assert(model.Pool[i].Uid == mature[i], "成熟宠物排序");
                for (int i = mature.Length; i < model.Pool.Count; i++)
                {
                    Assert(model.Pool[i].IsEgg, "蛋必须在成熟宠物后");
                    if (i > mature.Length) Assert(model.Pool[i - 1].Uid < model.Pool[i].Uid, "蛋按 UID 排序");
                }
            }

            private void DirectDraw()
            {
                GameModel model = Staged();
                model.Coins = model.Config.drawCost * 5; // This rule fixture tests phase availability, not opening affordability.
                model.Level.waves = 3; model.Level.baseCount = 1; model.Level.countGrowth = 0; model.Level.spawnInterval = .1f;
                Immediate(model, 0, false);
                Assert(model.StartWave(), "第一波开波"); Immediate(model, 1, true); Drain(model);
                Immediate(model, 2, false);
                Assert(model.StartWave(), "第二波开波"); Drain(model);
                Assert(model.StartWave() && model.Wave == model.Level.waves, "末波开波");
                Immediate(model, 3, false); Immediate(model, 4, true);
                Assert(model.Pool.TrueForAll(p => !p.IsEgg) && model.Towers.TrueForAll(p => !p.IsEgg), "全流程不产生待孵蛋");
                GameModel a = New(), b = New();
                Assert(a.DrawPet() && b.DrawEgg() && Snapshot(a) == Snapshot(b), "新旧方法使用相同随机流与事务");
                a.Coins = 0; Unchanged(a, () => a.DrawPet(), "新方法金币不足");
                a.Coins = 100000;
                while (a.Pool.Count < a.Config.poolCapacity) Assert(a.DrawPet(), "新方法填满待部署栏");
                Unchanged(a, () => a.DrawPet(), "新方法容量不足");
            }

            private void Immediate(GameModel model, int pad, bool alias)
            {
                HashSet<int> before = new HashSet<int>(); foreach (Pet pet in model.Pool) before.Add(pet.Uid);
                int coins = model.Coins;
                Assert(alias ? model.DrawEgg() : model.DrawPet(), "该阶段允许即抽即用");
                Pet drawn = model.Pool.Find(p => !before.Contains(p.Uid));
                Assert(drawn != null && !drawn.IsEgg && drawn.ReadyWave == 0 && drawn.Level == 1 && drawn.Pad == -1,
                    "抽取直接产出待部署的一阶成熟宠物");
                Assert(model.Coins == coins - model.Config.drawCost, "一次抽取消耗一次价格");
                Assert(model.Deploy(drawn.Uid, pad) && model.Towers.Contains(drawn), "不需要Tick或开下一波即可部署");
            }

            private void Waves()
            {
                GameModel model = Short(2, 2);
                Assert(model.StartWave(), "开波");
                Unchanged(model, () => model.StartWave(), "波次重入");
                model.Tick(1f / 60f);
                Assert(model.RemainingToSpawn == 1 && model.Enemies.Count == 1, "第一次只生成首只");
                model.Enemies[0].Hp = 0; model.Tick(1f / 60f);
                Assert(model.Stage == RunStage.Running && model.Enemies.Count == 0, "还有未生成敌人时不能清波");
                model.Tick(.2f);
                Assert(model.RemainingToSpawn == 0 && model.Enemies.Count == 1 && model.Stage == RunStage.Running, "最后一只生成不等于清场");
                int reward = model.Enemies[0].Reward, before = model.Coins;
                Drain(model);
                Assert(model.Coins == before + reward + model.Config.clearReward, "击杀与清波分别结算一次");
                before = model.Coins; model.Tick(10f);
                Assert(model.Wave == 1 && model.Stage == RunStage.Preparing && model.Coins == before, "手动等待不重结算不开波");
                model.SkillRemaining = 7f; model.Pool[0].Cooldown = .8f;
                string preparing = Snapshot(model); model.Tick(30f);
                Assert(Snapshot(model) == preparing, "准备期不恢复技能、池冷却或增加战斗时间");
                Assert(model.StartWave(), "第二波手动开波"); Drain(model);
                Assert(model.Stage == RunStage.Won && model.Lives > 0 && model.Wave == 2, "末波清空且有生命才胜利");
                string won = Snapshot(model); model.Tick(100f); Assert(Snapshot(model) == won, "胜利后冻结模拟");
            }

            private GameModel StatusArena()
            {
                GameModel model = Arena("otter", "sprout");
                Enemy enemy = AddEnemy(model, 8000, .2f, 100000f); enemy.Speed = .03f;
                AddPet(model, "otter", 8100, 1, 0); AddPet(model, "sprout", 8101, 1, 1);
                AddPet(model, "sprout", 8200, 1, -1, 2).Cooldown = .7f;
                model.Tick(1f / 60f);
                Assert(enemy.SlowRemaining > 0 && enemy.PoisonRemaining > 0, "暂停场景应有真实状态效果");
                Assert(model.CastSkill(enemy.X, enemy.Y), "暂停场景应有真实技能冷却");
                model.Tick(.005f);
                return model;
            }
            private void Pause()
            {
                GameModel a = StatusArena(), b = StatusArena();
                a.Paused = true; string before = Snapshot(a);
                a.Tick(30f); a.Tick(Single.NaN);
                Assert(Snapshot(a) == before, "暂停冻结位置、生命、持续效果、塔/池冷却、技能、弹道、出怪和总时间");
                a.Paused = false; a.Tick(.025f); b.Tick(.025f);
                Assert(Snapshot(a) == Snapshot(b), "暂停期间不得积累隐藏时间债务");
                a = StageStatusArena(); b = StageStatusArena();
                a.Paused = true; before = Snapshot(a); a.Tick(30f);
                Assert(Snapshot(a) == before, "高阶多目标弹道、长毒和强减速也完全暂停");
                a.Paused = false; a.Tick(.025f); b.Tick(.025f);
                Assert(Snapshot(a) == Snapshot(b), "高阶效果恢复后沿同一确定性时间线继续");
            }

            private GameModel StageStatusArena()
            {
                GameModel model = StageArena("otter", "sprout");
                Enemy enemy = AddEnemy(model, 8000, .2f, 100000f); enemy.Speed = .01f;
                AddEnemy(model, 8001, .18f, 100000f);
                AddPet(model, "otter", 8100, 5, 0); AddPet(model, "sprout", 8101, 5, 1);
                AddPet(model, "sprout", 8200, 2).Cooldown = .7f;
                model.Tick(1f / 60f);
                Assert(enemy.SlowRemaining > 2f && enemy.PoisonRemaining >= 4f && model.Effects.Count >= 3,
                    "暂停场景包含真实高阶多目标和持续效果");
                Assert(model.CastSkill(enemy.X, enemy.Y), "高阶暂停场景技能冷却"); model.Tick(.005f);
                return model;
            }

            private void RejectAll(GameModel model)
            {
                int uid = model.Towers[0].Uid;
                Unchanged(model, () => model.DrawPet(), "锁定时新方法抽取");
                Unchanged(model, () => model.DrawEgg(), "锁定时抽取");
                Unchanged(model, () => model.StartWave(), "锁定时开波");
                Unchanged(model, () => model.Deploy(uid, 3), "锁定时部署");
                Unchanged(model, () => model.Recall(uid), "锁定时回收");
                Unchanged(model, () => model.Sell(uid), "锁定时出售");
                Unchanged(model, () => model.EvolveTower(uid), "锁定时场上进化");
                Unchanged(model, () => model.EvolvePool() != 0, "锁定时池进化");
                Unchanged(model, () => model.CastSkill(.2f, .7f), "锁定时技能");
                Assert(!model.CanEvolve(uid) && !model.CanEvolvePool(), "锁定时进化资格为否");
            }
            private void LockedActions()
            {
                GameModel model = StatusArena();
                for (int i = 0; i < 3; i++) AddPet(model, "otter", 8500 + i);
                model.Paused = true; RejectAll(model);
                model.Paused = false; model.Stage = RunStage.Won; RejectAll(model);
                model.Stage = RunStage.Lost; RejectAll(model);
                string before = Snapshot(model); model.Tick(30f);
                Assert(before == Snapshot(model), "失败后冻结计时");
            }

            private void Failure()
            {
                GameModel model = New(); model.Lives = 0; int before = model.Coins; model.Tick(0f);
                Assert(model.Stage == RunStage.Lost && model.Coins == before, "准备期基地归零也应立即失败");
                model = Short(1); Assert(model.StartWave(), "失败场景开波"); model.Tick(1f / 60f);
                model.Lives = 1; Enemy enemy = model.Enemies[0];
                enemy.Progress = Length(model) - .001f; enemy.Speed = 10f; enemy.Leak = 4;
                before = model.Coins; model.Tick(1f / 60f);
                Assert(model.Stage == RunStage.Lost && model.Lives == 0 && model.Kills == 0
                    && model.Coins == before, "末只敌人漏掉归零不能判胜或发清波奖励");
                model = Short(1); Assert(model.StartWave(), "空战场失败场景开波");
                model.Lives = 0; before = model.Coins; model.Tick(0f);
                Assert(model.Stage == RunStage.Lost && model.Coins == before, "无需等待敌人存在才失败");
            }

            private void DeathReward()
            {
                GameModel model = Arena("falcon", "bat");
                AddPet(model, "falcon", 101, 1, 0); AddPet(model, "bat", 102, 1, 1);
                Enemy victim = AddEnemy(model, 1001, .2f, 1f);
                int before = model.Coins; model.Tick(1f / 60f);
                Assert(model.Kills == 1 && model.Coins == before + victim.Reward, "同帧多塔不重复击杀");
                Assert(model.CastSkill(victim.X, victim.Y), "在已死亡目标位置释放技能");
                model.Tick(.5f); Assert(model.Kills == 1 && model.Coins == before + victim.Reward, "弹道和技能不追认重复奖励");
                model.Towers.Clear();
                Enemy poisoned = AddEnemy(model, 1002, .2f, 1f);
                poisoned.PoisonDps = 120f; poisoned.PoisonRemaining = 3f;
                before = model.Coins; model.Tick(.1f);
                Assert(model.Kills == 2 && model.Coins == before + poisoned.Reward, "毒死亡走同一结算通道");
                before = model.Coins; model.Tick(1f); Assert(model.Coins == before && model.Kills == 2, "残留毒不得重复给奖励");
            }

            private void Targeting()
            {
                GameModel model = Arena("falcon"); Pet tower = AddPet(model, "falcon", 100, 1, 0);
                Enemy behind = AddEnemy(model, 1101, .1f), ahead = AddEnemy(model, 1102, .2f);
                ahead.Armor = 100f;
                model.Tick(1f / 60f);
                float expected = model.Damage(tower) * 100f / (100f + 100f * (1f - model.Spec("falcon").effectPower));
                Near(1000f - ahead.Hp, expected, "优先打出口更近的目标并正确穿甲");
                Near(behind.Hp, 1000f, "后方敌人不受伤");
                behind.Progress = ahead.Progress; behind.X = ahead.X; behind.Y = ahead.Y;
                tower.Cooldown = 0; model.Tick(1f / 60f);
                Near(1000f - behind.Hp, model.Damage(tower), "同进度优先较小敌人 ID");
            }

            private void Poison()
            {
                GameModel model = Arena("sprout"); Pet tower = AddPet(model, "sprout", 100, 1, 0);
                Enemy enemy = AddEnemy(model, 1001, .2f);
                model.Tick(1f / 60f);
                float dps = model.Spec("sprout").effectPower;
                Near(enemy.PoisonDps, dps, "首次施毒");
                tower.Cooldown = 0; model.Tick(1f / 60f);
                Near(enemy.PoisonDps, dps, "同源刷新不加倍"); Near(enemy.PoisonRemaining, 3f, "同源刷新时长");
                tower.Cooldown = 10f; float before = enemy.Hp; model.Tick(.5f);
                Near(before - enemy.Hp, dps * .5f, "持续毒按实际经过时间结算");
                Pet second = AddPet(model, "sprout", 101, 1, 1); model.Tick(1f / 60f);
                Near(enemy.PoisonDps, dps, "同级双木塔不得叠加毒 DPS");
                second.Level = 2; second.Cooldown = 0f; model.Tick(1f / 60f);
                Near(enemy.PoisonDps, dps * model.Config.levelDamageScale, "较强毒覆盖较弱毒");
                second.Cooldown = 10f; tower.Cooldown = 0f; model.Tick(1f / 60f);
                Near(enemy.PoisonDps, dps * model.Config.levelDamageScale, "弱毒刷新保留已有强毒");
                Near(enemy.PoisonRemaining, 3f, "弱毒刷新持续三秒");
                Assert(model.Recall(tower.Uid) && model.Recall(second.Uid), "施毒后回收"); model.Tick(4f);
                Near(enemy.PoisonRemaining, 0f, "毒自然到期"); Near(enemy.PoisonDps, 0f, "到期清除 DPS");
                before = enemy.Hp; model.Tick(1f); Near(enemy.Hp, before, "到期后不继续扣血");
            }

            private void Slow()
            {
                GameConfig config = Legacy(source);
                PetSpec weak = Copy(config.pets[2]); weak.id = "weak-slow"; weak.effect = "slow"; weak.effectPower = .1f;
                List<PetSpec> pets = new List<PetSpec>(config.pets); pets.Add(weak); config.pets = pets.ToArray();
                GameModel model = new GameModel(config, 0, new string[] { "otter", weak.id }, 3);
                model.Level.baseCount = 1; model.Level.waves = 2;
                Assert(model.StartWave(), "减速场景开波"); model.Tick(1f / 60f); model.Enemies.Clear(); model.Pool.Clear();
                Enemy enemy = AddEnemy(model, 1001, .2f); enemy.Speed = .1f;
                Pet strong = AddPet(model, "otter", 100, 1, 0), weaker = AddPet(model, weak.id, 101, 1, 1);
                model.Tick(1f / 60f); strong.Cooldown = weaker.Cooldown = 100f;
                float progress = enemy.Progress; model.Tick(1f);
                Near(enemy.Progress - progress, .1f * (1f - model.Spec("otter").effectPower), "两份减速只用最强值", .0001f);
                enemy.SlowRemaining = .25f; progress = enemy.Progress; model.Tick(1f);
                Near(enemy.Progress - progress, .1f * (1f - model.Spec("otter").effectPower * .25f), "仅剩四分之一秒减速时按有效时段移动", .0002f);
                Near(enemy.SlowRemaining, 0f, "减速时间归零");
            }

            private void Splash()
            {
                GameModel model = Arena("fox"); Pet tower = AddPet(model, "fox", 100, 1, 0);
                Enemy target = AddEnemy(model, 1001, .2f), close = AddEnemy(model, 1002, .15f), far = AddEnemy(model, 1003, .01f);
                model.Tick(1f / 60f);
                Near(1000f - target.Hp, model.Damage(tower), "主目标只受一次命中");
                Near(1000f - close.Hp, model.Damage(tower), "半径内次目标受到溅射");
                Near(far.Hp, 1000f, "半径外不受溅射");
            }

            private void Aura()
            {
                GameModel model = Arena("falcon", "deer");
                Pet attacker = AddPet(model, "falcon", 100, 1, 3);
                Pet a = AddPet(model, "deer", 101, 1, 8), b = AddPet(model, "deer", 102, 1, 2);
                a.Cooldown = b.Cooldown = 100f;
                Enemy enemy = AddEnemy(model, 1001, .2f);
                float damage = model.Spec("falcon").damage, bonus = model.Spec("deer").effectPower;
                Near(model.Damage(attacker), damage, "公开 Damage 返回未含光环的基础伤害");
                float before = enemy.Hp; model.Tick(1f / 60f);
                Near(before - enemy.Hp, damage * (1f + bonus), "两个光环不叠加");
                Assert(model.Recall(a.Uid), "移除第一光环");
                before = enemy.Hp; attacker.Cooldown = 0f; model.Tick(1f / 60f);
                Near(before - enemy.Hp, damage * (1f + bonus), "仍有一个光环提供增益");
                Assert(model.Recall(b.Uid), "移除第二光环");
                before = enemy.Hp; attacker.Cooldown = 0f; model.Tick(1f / 60f);
                Near(before - enemy.Hp, damage, "离开所有光环后恢复");
                Assert(model.Deploy(a.Uid, 12), "远处放置光环");
                before = enemy.Hp; attacker.Cooldown = 0f; model.Tick(1f / 60f);
                Near(before - enemy.Hp, damage, "远处光环不影响");
                Near(model.Damage(a), model.Spec("deer").damage, "光环不增益自身");
            }

            private float Shots(string species)
            {
                GameModel model = Arena(species); Pet tower = AddPet(model, species, 100, 1, 0);
                Enemy enemy = AddEnemy(model, 1001, .2f, 10000f);
                model.Tick(5f);
                float shots = (10000f - enemy.Hp) / model.Spec(species).damage;
                Near(shots, (float)Math.Round(shots), "伤害由完整攻击次数构成", .01f);
                float expected = 1f + (float)Math.Floor(5f / model.Spec(species).interval + .00001f); // Instant first shot and inclusive end boundary.
                Near(shots, expected, species + "攻击间隔", .01f);
                tower.Level = 3;
                Near(model.Damage(tower), model.Spec(species).damage * model.Config.levelDamageScale * model.Config.levelDamageScale, "三级伤害成长", .01f);
                Near(model.Range(tower), model.Spec(species).range, "射程来自配置");
                return shots;
            }
            private void HeavyAndRapid()
            {
                float heavy = Shots("turtle"), rapid = Shots("bat");
                Assert(rapid > heavy, "暗蝠应拥有更多攻击次数");
                Assert(source.pets[4].damage > source.pets[6].damage, "土龟单发伤害应高于暗蝠");
            }

            private void Skill()
            {
                GameModel model = New(); Unchanged(model, () => model.CastSkill(.2f, .7f), "准备期技能");
                model = Arena("falcon"); Enemy enemy = AddEnemy(model, 1001, .2f);
                enemy.Armor = 10000f; Enemy distant = model.Enemies[0];
                Unchanged(model, () => model.CastSkill(-.1f, .7f), "地图外落点");
                Unchanged(model, () => model.CastSkill(Single.NaN, .7f), "无效落点");
                float farHp = distant.Hp;
                Assert(model.SkillRemaining == 0f && model.CastSkill(enemy.X, enemy.Y), "技能初始可用");
                Near(1000f - enemy.Hp, model.Config.skillDamage, "星雨忽略护甲");
                Near(distant.Hp, farHp, "技能半径外不受伤");
                Near(model.SkillRemaining, model.Config.skillCooldown, "成功释放进入完整冷却");
                Unchanged(model, () => model.CastSkill(enemy.X, enemy.Y), "冷却中重复技能");
                model.Tick(model.Config.skillCooldown + .1f); Near(model.SkillRemaining, 0f, "技能冷却可自然结束");
            }

            private void EnemyBalance()
            {
                GameConfig cfg=Legacy(source); cfg.enemyBaseSpeed=.06f;cfg.killRewards=new[]{2,3,4,9};
                cfg.levels[0].waves=1;cfg.levels[0].baseCount=4;cfg.levels[0].spawnInterval=.05f;cfg.levels[0].speedScale=.001f;
                var model=new GameModel(cfg,0,FirstTeam(cfg,0),1);model.StartWave();model.Tick(.25f);
                float[] factors={1,1.65f,.78f,.65f};
                for(int i=0;i<4;i++)
                {
                    Near(model.Enemies[i].Speed,.06f*.001f*factors[i],"configured enemy speed",.000001f);
                    Assert(model.Enemies[i].Reward==cfg.killRewards[i],"configured per-kind kill reward");
                }
                cfg=Clone(source);cfg.enemyBaseSpeed=float.NaN;
                Throws(()=>new GameModel(cfg,0,FirstTeam(cfg,0),1),"非有限基础速度");
                cfg=Clone(source);cfg.enemyBaseSpeed=-.1f;
                Throws(()=>new GameModel(cfg,0,FirstTeam(cfg,0),1),"负基础速度");
                cfg=Clone(source);cfg.killRewards=new[]{1,2,3};
                Throws(()=>new GameModel(cfg,0,FirstTeam(cfg,0),1),"奖励种类数错误");
                cfg=Clone(source);cfg.killRewards=new[]{1,-1,3,4};
                Throws(()=>new GameModel(cfg,0,FirstTeam(cfg,0),1),"负击杀金币");
                cfg=Legacy(source);cfg.enemyBaseSpeed=0;cfg.killRewards=null;
                model=new GameModel(cfg,0,FirstTeam(cfg,0),1);model.StartWave();model.Tick(1f/60);
                Near(model.Enemies[0].Speed,.085f*model.Level.speedScale,"old config speed fallback");
                Assert(model.Enemies[0].Reward==10,"old config kill reward fallback");
            }


            private void EnemyVariants()
            {
                GameModel model = Short(1, 4); model.Level.speedScale = .001f;
                Assert(model.StartWave(), "变体场景开波"); model.Tick(.5f);
                Assert(model.Enemies.Count == 4 && model.RemainingToSpawn == 0, "配置总数包含末波精英");
                for (int i = 0; i < 4; i++) Assert(model.Enemies[i].Kind == i, "前三种及最终精英顺序");
                Enemy normal = model.Enemies[0], fast = model.Enemies[1], armor = model.Enemies[2], elite = model.Enemies[3];
                Near(normal.MaxHp, model.Level.baseHp * model.Level.hpScale, "普通敌人血量由关卡推导");
                Assert(fast.Speed > normal.Speed && fast.MaxHp < normal.MaxHp, "快速敌人的速度和生命差异");
                Assert(armor.Armor > normal.Armor && armor.MaxHp > normal.MaxHp, "护甲敌人的防御差异");
                Assert(elite.MaxHp > armor.MaxHp && elite.Reward > armor.Reward && elite.Leak > armor.Leak, "精英威胁与奖励差异");
                GameModel ordinary = Short(2, 4); Assert(ordinary.StartWave(), "非末波开波"); ordinary.Tick(.5f);
                Assert(!ordinary.Enemies.Exists(e => e.Kind == 3), "精英仅在末波出现");
            }

            private void InvalidInputs()
            {
                GameModel model = New(); string before = Snapshot(model);
                model.Tick(-1f); model.Tick(Single.NaN); model.Tick(Single.PositiveInfinity);
                Assert(Snapshot(model) == before && HasChinese(model.LastMessage), "无效时间无副作用且有中文说明");
                GameConfig config = Clone(source); config.pets[0].interval = 0f;
                Throws(() => new GameModel(config, 0, FirstTeam(config, 0), 1), "零攻击间隔");
                config = Clone(source); config.levels[0].spawnInterval = 0f;
                Throws(() => new GameModel(config, 0, FirstTeam(config, 0), 1), "零出怪间隔");
            }

            private GameModel Staged(params string[] team)
            {
                GameConfig config = WithStages(source);
                return new GameModel(config, 0, team.Length == 0 ? FirstTeam(config, 0) : team, 73);
            }

            private GameModel StageArena(params string[] team)
            {
                GameModel model = Staged(team);
                FixtureGeometry(model);
                // Baseline v0.2 effect tests isolate basic/passive attacks.
                // New automatic skills are exercised independently by AutoSkillChecks.
                foreach (PetSpec species in model.Config.pets)
                    foreach (StageSpec phase in species.stages) phase.active = null;
                model.Level.waves = 2; model.Level.baseCount = 1; model.Level.countGrowth = 0;
                Assert(model.StartWave(), "阶段战斗场开波"); model.Tick(1f / 60f);
                model.Enemies.Clear(); model.Pool.Clear();
                AddEnemy(model, 9900, Length(model) - .05f, 1000000f);
                return model;
            }

            private void StageData()
            {
                GameModel current = Staged(), legacy = New();
                int actualStages = 0;
                foreach (PetSpec spec in source.pets)
                {
                    if (spec.stages != null) actualStages += spec.stages.Length;
                    for (int level = 1; level <= 5; level++)
                    {
                        // Unowned preview instances are exactly what the codex UI needs.
                        Pet preview = new Pet { Species = spec.id, Level = level, Pad = -1 };
                        StageSpec stage = current.StageInfo(preview);
                        Assert(stage != null && stage == current.Spec(spec.id).stages[level - 1], "图鉴使用实际阶段条目");
                        Assert(!String.IsNullOrEmpty(stage.name) && !String.IsNullOrEmpty(stage.ability)
                            && !String.IsNullOrEmpty(stage.description) && !String.IsNullOrEmpty(stage.appearance), "阶段名称技能说明外形资料完整");
                        Near(current.Damage(preview), spec.damage * stage.damageMultiplier, "图鉴攻击共用阶段倍率");
                        Near(current.Range(preview), spec.range * stage.rangeMultiplier, "图鉴射程共用阶段倍率");
                        Near(current.Interval(preview), spec.interval * stage.intervalMultiplier, "图鉴攻速共用阶段倍率");
                        StageSpec old = legacy.StageInfo(preview);
                        float scale = (float)Math.Pow(source.levelDamageScale, level - 1);
                        Near(legacy.Damage(preview), spec.damage * scale, "旧配置伤害回退");
                        Near(legacy.Range(preview), spec.range, "旧配置射程回退");
                        Near(legacy.Interval(preview), spec.interval, "旧配置攻击间隔回退");
                        Near(old.effectPower, spec.effectPower * (spec.effect == "poison" ? scale : 1f), "旧配置效果回退");
                        Assert(old.targets == 1 && old.executeThreshold == 0f && old.eliteMultiplier == 1f, "旧配置不凭空增加阶段技能");
                    }
                    Assert(current.Spec(spec.id).id == legacy.Spec(spec.id).id, "保存的编队物种ID保持兼容");
                }
                Pet[] invalid = { null, new Pet { Species = "unknown", Level = 1 },
                    new Pet { Species = "falcon", Level = 0 }, new Pet { Species = "falcon", Level = -1 },
                    new Pet { Species = "falcon", Level = 6 }, new Pet { Species = "falcon", Level = Int32.MaxValue },
                    new Pet { Species = "falcon", Level = 1, ReadyWave = 2 } };
                foreach (Pet pet in invalid)
                    Assert(current.StageInfo(pet) == null && current.Damage(pet) == 0f && current.Range(pet) == 0f
                        && current.Interval(pet) == 0f && legacy.StageInfo(pet) == null, "非法图鉴输入返回空且不得越界");
                report.Append("阶段数据：输入实配 ").Append(actualStages)
                    .Append(" 项；旧配置回退与35项阶段检查均执行，缺省阶段仅用内存测试夹具。\n");
                Immediate(current, 0, false);
            }

            private void InvalidStage(Action<PetSpec> mutate, string message)
            {
                GameConfig config = WithStages(source); mutate(config.pets[0]);
                string before = Describe(config);
                Throws(() => new GameModel(config, 0, FirstTeam(config, 0), 7), message);
                Assert(Describe(config) == before, "错误配置验证不得修改调用方数据");
            }

            private void StageValidation()
            {
                InvalidStage(p => p.stages = new StageSpec[0], "空阶段数组");
                InvalidStage(p => p.stages = new StageSpec[4], "少于五项");
                InvalidStage(p => p.stages = new StageSpec[6], "多于五项");
                InvalidStage(p => p.stages[2] = null, "空阶段条目");
                foreach (FieldInfo field in typeof(StageSpec).GetFields())
                    if (field.FieldType == typeof(string))
                        foreach (string value in new string[] { null, "", "  " })
                        {
                            FieldInfo captured = field; string invalid = value;
                            InvalidStage(p => captured.SetValue(p.stages[0], invalid), field.Name + "文本必须完整");
                        }
                foreach (FieldInfo field in typeof(StageSpec).GetFields())
                    if (field.FieldType == typeof(float))
                        foreach (float value in new float[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity })
                        {
                            FieldInfo captured = field; float invalid = value;
                            InvalidStage(p => captured.SetValue(p.stages[0], invalid), field.Name + "必须有限");
                        }
                InvalidStage(p => p.stages[0].targets = 0, "目标数零");
                InvalidStage(p => p.stages[0].targets = 4, "目标数超三");
                InvalidStage(p => p.stages[0].executeThreshold = -.01f, "负处决阈值");
                InvalidStage(p => p.stages[0].executeThreshold = .251f, "处决阈值超上限");
                InvalidStage(p => p.stages[0].effectDuration = -.01f, "负持续时间");
                InvalidStage(p => p.stages[0].eliteMultiplier = .99f, "精英倍率小于一");
                InvalidStage(p => p.stages[0].damageMultiplier = -1f, "负伤害倍率");
                InvalidStage(p => p.stages[0].rangeMultiplier = 0f, "零射程倍率");
                InvalidStage(p => p.stages[0].intervalMultiplier = 0f, "零攻击间隔倍率");
                InvalidStage(p => p.stages[0].intervalMultiplier = -1f, "负攻击间隔倍率");
                InvalidStage(p => p.stages[0].effectPower = -1f, "负效果强度");
                InvalidStage(p => p.stages[0].effectPower = 1.01f, "穿甲比例超过一");
                InvalidStage(p => p.stages[0].damageMultiplier = Single.MaxValue, "最终伤害溢出");
                foreach (int limit in new int[] { 1, 4, 6 })
                {
                    GameConfig invalidLimit = WithStages(source); invalidLimit.maxLevel = limit;
                    Throws(() => new GameModel(invalidLimit, 0, FirstTeam(invalidLimit, 0), 7), "存在阶段表时必须采用五阶上限");
                }
                GameConfig valid = WithStages(source);
                StageSpec boundary = valid.pets[0].stages[0];
                boundary.targets = 3; boundary.executeThreshold = .25f; boundary.effectDuration = 0f;
                boundary.eliteMultiplier = 1f; boundary.damageMultiplier = 0f; boundary.effectPower = 0f;
                Assert(new GameModel(valid, 0, FirstTeam(valid, 0), 7).StageInfo(new Pet { Species = valid.pets[0].id, Level = 1 }) != null,
                    "边界值三目标、25%阈值、零伤害、零持续时间合法");
                valid.pets[0].stages = null;
                Assert(new GameModel(valid, 0, FirstTeam(valid, 0), 7).Pool.Count > 0, "新旧物种阶段配置可以混用");
            }

            private void AllEvolutions()
            {
                foreach (PetSpec spec in source.pets)
                {
                    GameModel model = Staged(spec.id); model.Pool.Clear();
                    Pet subject = AddPet(model, spec.id, 1000, 1, 0);
                    for (int level = 1; level < 5; level++)
                    {
                        AddPet(model, spec.id, 2000 + level * 2, level);
                        AddPet(model, spec.id, 2001 + level * 2, level);
                        float oldCd = level % 2 == 0 ? .01f : 20f; subject.Cooldown = oldCd;
                        Assert(model.CanEvolve(subject.Uid) && model.EvolveTower(subject.Uid), spec.id + "逐阶三合一成功");
                        Assert(subject.Level == level + 1 && subject.Uid == 1000 && subject.Pad == 0 && model.Pool.Count == 0,
                            "只消费两个同阶材料，主体不变");
                        Near(subject.Cooldown, Math.Min(oldCd, model.Interval(subject)), "升级冷却使用新阶段间隔");
                        Assert(model.StageInfo(subject).name == model.Spec(spec.id).stages[level].name, "进化后的图鉴和实际阶段一致");
                    }
                    AddPet(model, spec.id, 3000, 5); AddPet(model, spec.id, 3001, 5);
                    Unchanged(model, () => model.EvolveTower(subject.Uid), "五阶禁止继续进化");
                    Assert(!model.CanEvolve(subject.Uid), "五阶资格关闭");
                    model = Staged(spec.id); model.Pool.Clear(); model.Coins = 81 * model.Config.drawCost;
                    int initial = model.Coins, draws = 0, evolutions = 0;
                    Craft(model, 5, ref draws, ref evolutions);
                    Assert(draws == 81 && evolutions == 40 && model.Pool.Count == 1 && model.Pool[0].Level == 5,
                        spec.id + "81只一阶经40次三合一得到唯一五阶");
                    Assert(model.Coins == initial - draws * model.Config.drawCost && model.Coins == 0, "整条合成链只消耗实际抽取费用");
                    Unchanged(model, () => model.EvolvePool() != 0, "五阶池产物禁止继续进化");
                }
            }

            private void Craft(GameModel model, int level, ref int draws, ref int evolutions)
            {
                if (level == 1)
                {
                    Assert(model.DrawPet(), "逐只抽取合成材料"); draws++;
                    Assert(model.Pool.Count <= model.Config.poolCapacity && model.Pool.TrueForAll(p => !p.IsEgg), "合成材料不越容量且全部成熟");
                    return;
                }
                for (int i = 0; i < 3; i++) Craft(model, level - 1, ref draws, ref evolutions);
                Assert(model.CanEvolvePool() && model.EvolvePool() == 1, "每组三合一只升一阶"); evolutions++;
            }

            private void StageTargets()
            {
                foreach (string species in new string[] { "falcon", "otter", "bat" })
                    for (int level = 1; level <= 5; level++)
                    {
                        GameModel model = StageArena(species); Pet tower = AddPet(model, species, 100, level, 0);
                        StageSpec stage = model.StageInfo(tower);
                        Enemy a = AddEnemy(model, 1004, .24f, 10000f), b = AddEnemy(model, 1002, .2f, 10000f);
                        Enemy c = AddEnemy(model, 1001, .2f, 10000f), d = AddEnemy(model, 1003, .1f, 10000f);
                        Enemy[] ordered = { a, c, b, d };
                        foreach (Enemy enemy in ordered) enemy.Armor = 100f;
                        model.Tick(1f / 60f);
                        float effectiveArmor = species == "falcon" ? 100f * (1f - stage.effectPower) : 100f;
                        float hit = model.Damage(tower) * 100f / (100f + effectiveArmor);
                        for (int i = 0; i < ordered.Length; i++)
                            Near(10000f - ordered[i].Hp, i < stage.targets ? hit : 0f, "阶段目标按进度、ID排序且每只命中一次", .005f);
                        Assert(model.Effects.Count == stage.targets && model.Effects.TrueForAll(f => f.PetIndex == model.Spec(species).atlasIndex),
                            "每个独立目标一个弹道，五阶仍使用基础效果身份");
                        model.Enemies.Clear(); model.Effects.Clear();
                        Enemy lone = AddEnemy(model, 1020, .2f, 1f); tower.Cooldown = 0f;
                        model.Tick(1f / 60f);
                        Assert(model.Kills == 1 && lone.Hp == 0f, "目标不足时不重复攻击或奖励唯一目标");
                    }
            }

            private void StageTiming()
            {
                GameModel model = StageArena("bat"); Pet tower = AddPet(model, "bat", 100, 1, 0);
                Enemy target = AddEnemy(model, 1001, .2f, 100000f);
                float distance = (model.Spec("bat").range + model.Spec("bat").range * model.Spec("bat").stages[4].rangeMultiplier) / 2f;
                model.Pads[0] = new V2(target.X, target.Y - distance);
                model.Tick(.5f); Near(target.Hp, 100000f, "一阶射程之外不攻击");
                tower.Level = 5; tower.Cooldown = 0f; model.Tick(5f);
                float damage = model.Damage(tower);
                float expected = 1f + (float)Math.Floor((5f - .001f) / model.Interval(tower));
                Near((100000f - target.Hp) / damage, expected, "五阶扩展射程和缩短间隔真实生效", .01f);
            }

            private void StagePoison()
            {
                GameModel model = StageArena("sprout", "deer");
                Pet strong = AddPet(model, "sprout", 100, 5, 0), support = AddPet(model, "deer", 200, 4, 1);
                support.Cooldown = 100f;
                Enemy enemy = AddEnemy(model, 1001, .2f, 10000f); enemy.Armor = 1000f;
                StageSpec strongInfo = model.StageInfo(strong);
                float dps = strongInfo.effectPower * (1f + model.StageInfo(support).effectPower);
                model.Tick(1f / 60f); strong.Cooldown = 100f;
                Near(enemy.PoisonDps, dps, "高阶毒直接采用阶段最终DPS，只乘施毒瞬间光环");
                Near(enemy.PoisonRemaining, strongInfo.effectDuration, "高阶毒读取自身持续时间");
                float before = enemy.Hp; model.Tick(.25f);
                Near(before - enemy.Hp, dps * .25f, "毒无视护甲按持续DPS扣血", .02f);
                Pet weak = AddPet(model, "sprout", 101, 1, 2);
                float remaining = enemy.PoisonRemaining; model.Tick(1f / 60f); weak.Cooldown = 100f;
                Near(enemy.PoisonDps, dps, "弱阶毒不降低已有强毒");
                Near(enemy.PoisonRemaining, Math.Max(remaining - 1f / 60f, model.StageInfo(weak).effectDuration), "弱阶毒不缩短较长剩余时间");
                Assert(model.Recall(support.Uid), "移除施毒时光环");
                Near(enemy.PoisonDps, dps, "光环离场不追溯改变毒快照");
                model.Tick(strongInfo.effectDuration + .1f);
                Near(enemy.PoisonRemaining, 0f, "强毒到期"); Near(enemy.PoisonDps, 0f, "强毒DPS清除");
                weak.Cooldown = 0f; model.Tick(1f / 60f);
                Near(enemy.PoisonDps, model.StageInfo(weak).effectPower, "到期后的弱毒使用自己的DPS");
            }

            private void StageSlow()
            {
                GameModel model = StageArena("otter");
                Pet strong = AddPet(model, "otter", 100, 5, 0);
                Enemy enemy = AddEnemy(model, 1001, .2f, 10000f);
                model.Tick(1f / 60f); strong.Cooldown = 100f;
                StageSpec info = model.StageInfo(strong);
                model.Tick(.25f); float remaining = enemy.SlowRemaining;
                Pet weak = AddPet(model, "otter", 101, 1, 1);
                model.Tick(1f / 60f); weak.Cooldown = 100f;
                Near(enemy.SlowRemaining, Math.Max(remaining, model.StageInfo(weak).effectDuration) - 1f / 60f,
                    "弱阶减速不缩短较长剩余时间");
                float progress = enemy.Progress; enemy.Speed = .1f; model.Tick(.5f);
                Near(enemy.Progress - progress, .05f * (1f - info.effectPower), "跨阶段保留最强减速且不相加", .0002f);
                enemy.SlowRemaining = .25f; progress = enemy.Progress; model.Tick(1f);
                Near(enemy.Progress - progress, .1f * (1f - .25f * info.effectPower), "高阶减速到期按有效时段移动", .0002f);
            }

            private void StageSplash()
            {
                GameModel model = StageArena("fox"); Pet tower = AddPet(model, "fox", 100, 5, 0);
                // Valid but deliberately irrelevant targets value: splash must not replicate AoE.
                model.Spec("fox").stages[4].targets = 3;
                Enemy target = AddEnemy(model, 1001, .25f, 10000f);
                float radius = model.StageInfo(tower).effectPower;
                Enemy near = AddEnemy(model, 1002, .25f - radius + .001f, 10000f);
                Enemy far = AddEnemy(model, 1003, .25f - radius - .001f, 10000f);
                near.Armor = 100f; model.Tick(1f / 60f);
                Near(10000f - target.Hp, model.Damage(tower), "高阶主目标不重复溅射");
                Near(10000f - near.Hp, model.Damage(tower) / 2f, "阶段半径内独立减甲");
                Near(far.Hp, 10000f, "阶段半径外不受伤"); Assert(model.Effects.Count == 1, "群伤不使用targets复制攻击");
            }

            private void StageAura()
            {
                GameModel model = StageArena("falcon", "deer");
                Pet attacker = AddPet(model, "falcon", 100, 1, 0), strong = AddPet(model, "deer", 101, 5, 1);
                Pet weak = AddPet(model, "deer", 102, 1, 2); strong.Cooldown = weak.Cooldown = 100f;
                Enemy target = AddEnemy(model, 1001, .2f, 10000f);
                model.Pads[0] = new V2(.12f, .7f);
                float beyondBaseRange = (model.Spec("deer").range + model.Range(strong)) / 2f;
                model.Pads[1] = new V2(.12f + beyondBaseRange / 1.6f, .7f);
                model.Pads[2] = new V2(.12f, .9f);
                float expected = model.Damage(attacker) * (1f + model.StageInfo(strong).effectPower);
                model.Tick(1f / 60f); Near(10000f - target.Hp, expected, "升级光环扩大范围并覆盖低阶光环", .005f);
                Assert(model.Recall(strong.Uid), "回收强光环");
                float before = target.Hp; attacker.Cooldown = 0f; model.Tick(1f / 60f);
                Near(before - target.Hp, model.Damage(attacker) * (1f + model.StageInfo(weak).effectPower), "强光环移除后读取弱光环", .005f);
                Assert(model.Recall(weak.Uid), "回收弱光环");
                before = target.Hp; attacker.Cooldown = 0f; model.Tick(1f / 60f);
                Near(before - target.Hp, model.Damage(attacker), "所有支援离开后恢复基础伤害");
            }

            private void StageHeavy()
            {
                for (int level = 1; level <= 5; level++)
                {
                    GameModel model = StageArena("turtle"); Pet tower = AddPet(model, "turtle", 100, level, 0);
                    Enemy elite = AddEnemy(model, 1001, .2f, 10000f); elite.Kind = 3; elite.Armor = 100f;
                    model.Tick(1f / 60f);
                    Near(10000f - elite.Hp, model.Damage(tower) * model.StageInfo(tower).eliteMultiplier / 2f, "各阶段精英增伤先于护甲结算", .005f);
                    model.Enemies.Remove(elite);
                    Enemy normal = AddEnemy(model, 1002, .2f, 10000f); normal.Kind = 2; normal.Armor = 100f;
                    tower.Cooldown = 0f; model.Tick(1f / 60f);
                    Near(10000f - normal.Hp, model.Damage(tower) / 2f, "非精英不获得精英增伤", .005f);
                    Assert(normal.SlowRemaining == 0f && normal.PoisonRemaining == 0f, "重击不附加眩晕或持续效果");
                }
            }

            private void StageExecution()
            {
                GameModel model = StageArena("bat"); Pet tower = AddPet(model, "bat", 100, 5, 0);
                float threshold = model.StageInfo(tower).executeThreshold * 1000f, damage = model.Damage(tower);
                Enemy normal = AddEnemy(model, 1001, .2f, 1000f), elite = AddEnemy(model, 1002, .19f, 1000f);
                normal.Hp = elite.Hp = threshold + damage; elite.Kind = 3;
                int coins = model.Coins; model.Tick(1f / 60f);
                Assert(normal.Hp == 0f && !model.Enemies.Contains(normal), "正常伤害后等于阈值的非精英被处决");
                Near(elite.Hp, threshold, "精英处决免疫但仍承受正常伤害");
                Assert(model.Kills == 1 && model.Coins == coins + normal.Reward, "处决只结算一次死亡奖励");
                tower.Cooldown = 100f; model.Tick(.5f);
                Assert(model.Kills == 1 && model.Coins == coins + normal.Reward, "处决弹道不会再次发奖");
                model.Enemies.Remove(elite);
                Enemy above = AddEnemy(model, 1003, .2f, 1000f); above.Hp = threshold + damage + 1f;
                tower.Cooldown = 0f; model.Tick(1f / 60f);
                Near(above.Hp, threshold + 1f, "高于阈值不能处决");
                model.Enemies.Remove(above);
                Enemy lethal = AddEnemy(model, 1004, .2f, 1f); coins = model.Coins;
                tower.Cooldown = 0f; model.Tick(1f / 60f);
                Assert(lethal.Hp == 0f && model.Coins == coins + lethal.Reward && model.Kills == 2, "直接致死不再重复执行处决");
            }

            private void Determinism()
            {
                GameModel a = New(), b = New();
                a.Coins=b.Coins=a.Config.drawCost*4; // Fund the four-draw RNG fixture independently of campaign economy.
                for (int i = 0; i < 4; i++) { Assert(a.DrawEgg() && b.DrawEgg(), "相同种子抽取"); }
                foreach (Pet pet in a.Pool.ToArray()) if (!pet.IsEgg) Assert(a.Deploy(pet.Uid, a.Towers.Count), "部署模型甲");
                foreach (Pet pet in b.Pool.ToArray()) if (!pet.IsEgg) Assert(b.Deploy(pet.Uid, b.Towers.Count), "部署模型乙");
                Assert(a.StartWave() && b.StartWave(), "同时开波");
                for (int i = 0; i < 320; i++)
                {
                    a.Tick(.125f); b.Tick(.125f);
                    Assert(Snapshot(a) == Snapshot(b), "相同输入序列在每个时点应相同");
                }
                a = Arena("bat"); b = Arena("bat");
                AddPet(a, "bat", 100, 1, 0); AddPet(b, "bat", 100, 1, 0);
                AddEnemy(a, 1001, .2f, 100000f); AddEnemy(b, 1001, .2f, 100000f);
                for (int i = 0; i < 20; i++) a.Tick(.5f);
                for (int i = 0; i < 320; i++) b.Tick(.03125f);
                Assert(Snapshot(a) == Snapshot(b), "同样总时间的不同分片应有相同战斗结果");
            }

            private void Batch()
            {
                int[] seeds = { 7, 19, 41 };
                for (int level = 0; level < source.levels.Length; level++)
                {
                    foreach (int seed in seeds)
                    {
                        GameModel model = new GameModel(Clone(source), level, FirstTeam(source, level), seed);
                        int[] starterPads = { 2, 5, 8 };
                        Pet[] starters = model.Pool.ToArray();
                        for (int i = 0; i < starters.Length; i++)
                            Assert(model.Deploy(starters[i].Uid, starterPads[i]), "按指定 2/5/8 地块部署前三基础宠");
                        Simulate(model, true);
                        report.Append("模拟：").Append(model.Level.id).Append(" seed=").Append(seed)
                            .Append(" ").Append(model.Stage).Append(" 波=").Append(model.Wave)
                            .Append(" HP=").Append(model.Lives).Append(" 击杀=").Append(model.Kills)
                            .Append(" 金币=").Append(model.Coins).Append(" 时长=").Append(F(model.Elapsed)).Append("秒\n");
                    }
                    GameModel strong = new GameModel(Clone(source), level, FirstTeam(source, level), 17);
                    strong.Pool.Clear();
                    for (int i = 0; i < strong.Pads.Count; i++)
                        AddPet(strong, "falcon", 2000 + i, source.maxLevel, i);
                    Simulate(strong, false);
                    int expected = strong.Level.baseCount * strong.Level.waves
                        + strong.Level.countGrowth * strong.Level.waves * (strong.Level.waves - 1) / 2;
                    if (strong.Config.enemyTypes != null)
                        for (int w = 1; w <= strong.Level.waves; w++)
                            foreach (string id in strong.WaveRoster(w))
                            { EnemySpec spec = strong.EnemyInfo(id); if (spec.behavior == "split") expected += spec.splitCount; }
                    Assert(strong.Stage == RunStage.Won && strong.Kills == expected && strong.Lives == source.initialLives,
                        "强阵容应击杀全部配置敌人并走完整胜利流程");
                }
                GameModel empty = New(); empty.Pool.Clear(); Simulate(empty, false);
                Assert(empty.Stage == RunStage.Lost && empty.Lives == 0 && empty.Kills == 0,
                    "无防守阵容应自然漏怪失败且无击杀奖励");
            }

            private void Simulate(GameModel model, bool manage)
            {
                int guard = 0;
                while ((model.Stage == RunStage.Preparing || model.Stage == RunStage.Running) && guard++ < 10000)
                {
                    if (manage) Manage(model);
                    if (model.Stage == RunStage.Preparing) Assert(model.StartWave(), "模拟应允许手动开波");
                    if (manage && model.SkillRemaining <= 0f && model.Enemies.Count > 0)
                    {
                        Enemy target = model.Enemies[0];
                        foreach (Enemy enemy in model.Enemies) if (enemy.Progress > target.Progress) target = enemy;
                        Assert(model.CastSkill(target.X, target.Y), "模拟应允许释放技能");
                    }
                    model.Tick(.25f);
                    Assert(model.Coins >= 0 && model.Lives >= 0 && model.Pool.Count <= model.Config.poolCapacity,
                        "整局模拟经济和容量不越界");
                }
                Assert(guard < 10000 && (model.Stage == RunStage.Won || model.Stage == RunStage.Lost), "模拟必须有限终止");
            }
            private void Manage(GameModel model)
            {
                if (model.CanEvolvePool()) Assert(model.EvolvePool() > 0, "策略池进化");
                foreach (Pet pet in model.Towers.ToArray())
                    if (model.CanEvolve(pet.Uid) && (model.Towers.Count >= 12
                        || model.Pool.FindAll(p => !p.IsEgg && p.Species == pet.Species && p.Level == pet.Level).Count >= 2))
                        Assert(model.EvolveTower(pet.Uid), "策略塔进化");
                // Deterministic deployment favours inner turns, then remaining pads.
                int[] order = { 3, 8, 9, 4, 0, 13, 6, 7, 2, 10, 12, 1, 5, 11 };
                foreach (Pet pet in model.Pool.ToArray())
                {
                    if (pet.IsEgg) continue;
                    foreach (int pad in order)
                        if (!model.Towers.Exists(t => t.Pad == pad)) { Assert(model.Deploy(pet.Uid, pad), "策略部署"); break; }
                }
                while (model.Pool.Count < model.Config.poolCapacity && model.Coins >= model.Config.drawCost)
                    Assert(model.DrawPet(), "策略即抽即用");
            }

            private void Throws(Action action, string message)
            {
                bool rejected = false;
                try { action(); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, message + "应被拒绝");
            }
        }

        private static GameConfig Clone(GameConfig config)
        {
            GameConfig copy = Copy(config);
            copy.pets = Array.ConvertAll(config.pets, p => Copy(p));
            foreach (PetSpec pet in copy.pets)
                if (pet.stages != null) pet.stages = Array.ConvertAll(pet.stages, s => {
                    if (s == null) return null;
                    StageSpec clone = Copy(s);
                    if (s.active != null) clone.active = Copy(s.active);
                    return clone;
                });
            copy.levels = Array.ConvertAll(config.levels, p => Copy(p));
            return copy;
        }
        private static GameConfig Legacy(GameConfig config)
        {
            GameConfig result = Clone(config);
            foreach (PetSpec pet in result.pets) pet.stages = null;
            result.enemyTypes = null;
            foreach (LevelSpec level in result.levels) { level.enemyWaves = null; level.bossId = null; }
            return result;
        }
        private static GameConfig WithStages(GameConfig config)
        {
            GameConfig result = Clone(config);
            // Only used when testing a legacy input. These are deterministic test
            // fixtures, never written to production JSON or used by GameModel fallback.
            float[] damage = { 1f, 2.15f, 4.7f, 10.3f, 22.5f };
            float[] range = { 1f, 1.03f, 1.06f, 1.1f, 1.15f };
            float[] interval = { 1f, .97f, .94f, .9f, .85f };
            foreach (PetSpec pet in result.pets)
            {
                if (pet.stages != null) continue;
                pet.stages = new StageSpec[5];
                for (int i = 0; i < 5; i++)
                {
                    StageSpec stage = new StageSpec { name = "回归阶段" + (i + 1), ability = "回归技能",
                        description = "内存回归夹具", appearance = "内存回归外形",
                        damageMultiplier = damage[i], rangeMultiplier = range[i], intervalMultiplier = interval[i],
                        effectPower = pet.effectPower, targets = 1, eliteMultiplier = 1f };
                    if (pet.effect == "armor") { stage.effectPower = new float[] { .5f, .55f, .65f, .8f, 1f }[i]; stage.targets = i == 4 ? 3 : (i >= 2 ? 2 : 1); }
                    if (pet.effect == "poison") { stage.effectPower = new float[] { 6f, 15f, 36f, 90f, 210f }[i]; stage.effectDuration = 3f + .25f * i; }
                    if (pet.effect == "slow") { stage.effectPower = .3f + .05f * i; stage.effectDuration = 2f + .25f * i; stage.targets = i >= 3 ? 2 : 1; }
                    if (pet.effect == "splash") stage.effectPower = new float[] { .11f, .12f, .14f, .16f, .18f }[i];
                    if (pet.effect == "heavy") stage.eliteMultiplier = new float[] { 1f, 1.15f, 1.3f, 1.5f, 1.8f }[i];
                    if (pet.effect == "aura") stage.effectPower = new float[] { .2f, .22f, .25f, .3f, .35f }[i];
                    if (pet.effect == "rapid") { stage.executeThreshold = new float[] { 0f, 0f, .1f, .15f, .2f }[i]; stage.targets = i >= 3 ? 2 : 1; }
                    pet.stages[i] = stage;
                }
            }
            return result;
        }
        private static T Copy<T>(T value) where T : new()
        {
            T result = new T();
            foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(result, field.GetValue(value));
            return result;
        }
        private static string[] FirstTeam(GameConfig config, int level)
        {
            int count = Math.Min(5, Math.Min(config.levels[level].slots, config.pets.Length));
            string[] result = new string[count];
            for (int i = 0; i < count; i++) result[i] = config.pets[i].id;
            return result;
        }
        private static bool HasChinese(string value)
        {
            if (value == null) return false;
            foreach (char c in value) if (c >= '\u4e00' && c <= '\u9fff') return true;
            return false;
        }
        private static bool Unit(V2 p) { return p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1; }
        private static float Length(GameModel model)
        {
            float total = 0;
            for (int i = 1; i < model.Path.Count; i++) total += model.Distance(model.Path[i - 1], model.Path[i]);
            return total;
        }
        private static V2 Point(GameModel model, float progress)
        {
            for (int i = 1; i < model.Path.Count; i++)
            {
                V2 a = model.Path[i - 1], b = model.Path[i]; float length = model.Distance(a, b);
                if (progress <= length) return new V2(a.X + (b.X - a.X) * progress / length, a.Y + (b.Y - a.Y) * progress / length);
                progress -= length;
            }
            return model.Path[model.Path.Count - 1];
        }
        private static float SegmentDistance(V2 point, V2 a, V2 b)
        {
            double x = point.X * 1.6, y = point.Y, ax = a.X * 1.6, ay = a.Y;
            double dx = (b.X - a.X) * 1.6, dy = b.Y - a.Y;
            double t = Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy)));
            double ex = x - ax - t * dx, ey = y - ay - t * dy;
            return (float)Math.Sqrt(ex * ex + ey * ey);
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Snapshot(GameModel model)
        {
            // Reflect all public state except feedback text. Includes list ordering,
            // cooldowns, projectile lifetime and every exposed status field.
            StringBuilder text = new StringBuilder();
            FieldInfo[] fields = typeof(GameModel).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => String.CompareOrdinal(a.Name, b.Name));
            foreach (FieldInfo field in fields)
                if (field.Name != "LastMessage" && field.Name != "Config" && field.Name != "Level")
                    text.Append(field.Name).Append('=').Append(Describe(field.GetValue(model))).Append(';');
            text.Append("RemainingToSpawn=").Append(model.RemainingToSpawn);
            return text.ToString();
        }
        private static string Describe(object value)
        {
            if (value == null) return "null";
            Type type = value.GetType();
            if (value is float) return F((float)value);
            if (type.IsPrimitive || type.IsEnum || value is string) return Convert.ToString(value, CultureInfo.InvariantCulture);
            StringBuilder text = new StringBuilder();
            System.Collections.IEnumerable sequence = value as System.Collections.IEnumerable;
            if (sequence != null)
            {
                text.Append('['); foreach (object item in sequence) text.Append(Describe(item)).Append('|'); text.Append(']');
            }
            else
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                Array.Sort(fields, (a, b) => String.CompareOrdinal(a.Name, b.Name));
                foreach (FieldInfo field in fields) text.Append(field.Name).Append(':').Append(Describe(field.GetValue(value))).Append(',');
            }
            return text.ToString();
        }
    }
}
