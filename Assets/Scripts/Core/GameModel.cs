using System;
using System.Collections.Generic;

namespace PetTD
{
    [Serializable]
    public class GameConfig
    {
        public int initialGold, initialLives, drawCost, poolCapacity, maxLevel;
        public int sellBase, clearReward, clearRewardGrowth;
        public float levelDamageScale, skillCooldown, skillDamage, skillRadius;
        public float enemyBaseSpeed; // Zero uses legacy .085 units/s.
        public int[] killRewards; // Null uses legacy rewards, kind order 0..3.
        public PetSpec[] pets;
        public LevelSpec[] levels;
        public EnemySpec[] enemyTypes;
    }

    [Serializable]
    public class PetSpec
    {
        public string id, name, element, role, description, colorHex, effect;
        public float damage, range, interval, effectPower;
        public int atlasIndex;
        public StageSpec[] stages;
        public string basicName, passiveName;
    }

    [Serializable]
    public class StageSpec
    {
        public string name, ability, description, appearance;
        public float damageMultiplier, rangeMultiplier, intervalMultiplier;
        public float effectPower, effectDuration, executeThreshold, eliteMultiplier;
        public int targets;
        public AutoSkillSpec active;
    }

    [Serializable]
    public class LevelSpec
    {
        public string id, name, subtitle;
        public int waves, slots, baseCount, countGrowth;
        public float hpScale, speedScale, baseHp, hpGrowth, spawnInterval;
        public EnemyWave[] enemyWaves;
        public string bossId;
    }

    public struct V2
    {
        public float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
    }

    public enum RunStage { Preparing, Running, Won, Lost }

    public class Pet
    {
        public int Uid, Level, ReadyWave, Pad;
        public string Species;
        public float Cooldown;
        public float ActiveRemaining, ActiveBuffRemaining, ActiveDamageBonus, ActiveAttackSpeedBonus;
        public int AutoCasts;
        // Legacy fixture field only. All v0.2 draws and starters have ReadyWave = 0.
        public bool IsEgg { get { return ReadyWave > 0; } }
    }

    public class Enemy
    {
        public int Id, Kind, Reward, Leak;
        public float X, Y, Progress, Hp, MaxHp, Speed, Armor;
        public float SlowRemaining, PoisonRemaining, PoisonDps;
        public float BurnRemaining, BurnDps, RootRemaining, VulnerableRemaining, Vulnerability;
        public string Species;
        public int Generation;
        public float Shield, MaxShield, AbilityRemaining, PulseRemaining;
        public bool Enraged;
    }

    public class CombatFx
    {
        public float X, Y, ToX, ToY, Life, Duration;
        public int PetIndex;
        // Presentation snapshots only; no projectile collision or damage delay.
        public int SourceLevel, TargetId, TargetKind;
        public float TargetSize;
        public bool IsAuto;
        public float Radius;
    }

    public partial class GameModel
    {
        public GameConfig Config;
        public LevelSpec Level;
        public List<Pet> Pool, Towers;
        public List<Enemy> Enemies;
        public List<CombatFx> Effects;
        public List<V2> Path, Pads;
        public int Coins, Lives, Wave, Kills;
        public RunStage Stage;
        public bool Paused;
        public float Elapsed, SkillRemaining;
        public string LastMessage;

        // EXT constants absent from balance.json. Keep all such tuning here.
        // Kind 0 = ordinary, 1 = runner, 2 = armored, 3 = final-wave elite.
        // HP = baseHp * hpScale * (1 + hpGrowth * (Wave - 1)) * HpMultiplier.
        // Speed = BaseSpeed * speedScale * SpeedMultiplier (path units/second).
        // Each wave cycles the three ordinary kinds. Its final enemy is replaced
        // by an elite only on the last wave; the configured total is unchanged.
        private static class Rules
        {
            internal const double Step = 1.0 / 60.0;
            internal const double ClockEpsilon = 0.00000001;
            internal const float BaseSpeed = 0.085f;
            internal const float PoisonDuration = 3f;
            internal const float SlowDuration = 2f;
            internal const float MaximumSlow = 0.95f;
            internal const float ShotDuration = 0.16f;
            internal const float SkillDuration = 0.45f;
            internal const int TeamSlots = 5;
            internal const int StarterCount = 3;
            internal const int StageCount = 5;
            internal static readonly float[] HpMultiplier = { 1f, 0.65f, 1.6f, 5f };
            internal static readonly float[] SpeedMultiplier = { 1f, 1.65f, 0.78f, 0.65f };
            internal static readonly float[] Armor = { 0f, 0f, 60f, 85f };
            internal static readonly int[] Reward = { 10, 9, 15, 60 };
            internal static readonly int[] Leak = { 1, 1, 2, 4 };
        }

        private sealed class EnemyStatus
        {
            internal float Slow;
        }

        private readonly string[] team;
        private readonly Dictionary<string, PetSpec> specs;
        private readonly Dictionary<string, StageSpec[]> legacyStages = new Dictionary<string, StageSpec[]>(StringComparer.Ordinal);
        private readonly Dictionary<Enemy, EnemyStatus> statuses = new Dictionary<Enemy, EnemyStatus>();
        private readonly HashSet<Enemy> settled = new HashSet<Enemy>();
        private uint randomState;
        private int nextPetUid = 1, nextEnemyId = 1, remainingToSpawn, spawned;
        private double accumulator, spawnClock;
        public int RemainingToSpawn { get { return remainingToSpawn; } }

        public GameModel(GameConfig config, int levelIndex, string[] team, int seed)
        {
            ValidateConfig(config, levelIndex);
            Config = config;
            Level = config.levels[levelIndex];
            specs = new Dictionary<string, PetSpec>(StringComparer.Ordinal);
            foreach (PetSpec spec in config.pets)
            {
                if (spec == null || String.IsNullOrEmpty(spec.id) || specs.ContainsKey(spec.id)
                    || !Nonnegative(spec.damage) || !Nonnegative(spec.range)
                    || !Positive(spec.interval) || !Nonnegative(spec.effectPower))
                    throw new ArgumentException("宠物配置无效或物种重复。", "config");
                specs.Add(spec.id, spec);
                if (spec.stages != null && config.maxLevel != Rules.StageCount)
                    throw new ArgumentException("使用阶段配置时最高阶段必须为五阶。", "config");
                ValidateStages(spec);
                if (spec.stages == null)
                {
                    StageSpec[] fallback = new StageSpec[Rules.StageCount];
                    for (int i = 0; i < fallback.Length; i++)
                    {
                        float scale = (float)Math.Pow(config.levelDamageScale, i);
                        fallback[i] = new StageSpec { name = spec.name + "·" + (i + 1) + "阶",
                            ability = spec.role, description = spec.description, appearance = spec.description,
                            damageMultiplier = scale, rangeMultiplier = 1f, intervalMultiplier = 1f,
                            effectPower = spec.effect == "poison" ? spec.effectPower * scale : spec.effectPower,
                            effectDuration = spec.effect == "poison" ? Rules.PoisonDuration
                                : (spec.effect == "slow" ? Rules.SlowDuration : 0f),
                            targets = 1, eliteMultiplier = 1f };
                    }
                    legacyStages.Add(spec.id, fallback);
                }
            }
            if (team == null || team.Length == 0 || team.Length > Math.Min(Rules.TeamSlots, Level.slots))
                throw new ArgumentException("出战编队人数不符合关卡要求。", "team");
            HashSet<string> chosen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string species in team)
                if (species == null || !specs.ContainsKey(species) || !chosen.Add(species))
                    throw new ArgumentException("出战编队包含未知或重复的宠物。", "team");
            this.team = (string[])team.Clone();
            randomState = unchecked((uint)seed);
            if (randomState == 0) randomState = 0x9E3779B9u;
            Pool = new List<Pet>();
            Towers = new List<Pet>();
            Enemies = new List<Enemy>();
            Effects = new List<CombatFx>();
            // Normalized positions in a 1.6:1 world, shared by rendering and movement.
            Path = TrailGeometry.ForestTrail();
            Pads = TrailGeometry.ForestPads();
            Coins = config.initialGold;
            Lives = config.initialLives;
            Stage = RunStage.Preparing;
            for (int i = 0; i < Math.Min(Rules.StarterCount, this.team.Length); i++)
                Pool.Add(NewPet(this.team[i], 0));
            SortPool();
            LastMessage = "伙伴已就绪，请部署宠物后开始第一波。";
        }

        public PetSpec Spec(string species)
        {
            PetSpec result;
            return species != null && specs.TryGetValue(species, out result) ? result : null;
        }

        public Pet FindPet(int uid)
        {
            foreach (Pet pet in Pool) if (pet.Uid == uid) return pet;
            foreach (Pet pet in Towers) if (pet.Uid == uid) return pet;
            return null;
        }

        public bool DrawEgg()
        {
            return DrawPet();
        }

        public bool DrawPet()
        {
            if (!AllowAction()) return false;
            if (Coins < Config.drawCost) return Fail("金币不足，无法抽取宠物。");
            if (Pool.Count >= Config.poolCapacity) return Fail("待部署栏已满，无法抽取宠物。");
            // Validation precedes both RNG advancement and resource/UID allocation.
            string species = team[NextRandom(team.Length)];
            Pet pet = NewPet(species, 0);
            Coins -= Config.drawCost;
            Pool.Add(pet);
            SortPool();
            LastMessage = "获得" + StageInfo(pet).name + "，已直接孵化，可立即部署。";
            return true;
        }

        public bool StartWave()
        {
            if (!AllowAction()) return false;
            if (Stage != RunStage.Preparing) return Fail("当前波次尚未结束。");
            if (Wave >= Level.waves) return Fail("本关所有波次均已完成。");
            if (Enemies.Count != 0) return Fail("战场仍有敌人，无法开始下一波。");
            Wave++;
            SortPool();
            remainingToSpawn = Level.baseCount + Level.countGrowth * (Wave - 1);
            spawned = 0;
            spawnClock = 0;
            accumulator = 0;
            Stage = RunStage.Running;
            LastMessage = "第" + Wave + "波开始，守护基地！";
            return true;
        }

        public bool Deploy(int uid, int pad)
        {
            if (!AllowAction()) return false;
            Pet pet = FindPet(uid);
            if (pet == null) return Fail("未找到该宠物。");
            if (!Pool.Contains(pet)) return Fail("请先将场上宠物回收至池，再重新部署。");
            if (pet.IsEgg) return Fail("宠物蛋尚未孵化，无法部署。");
            if (!InTeam(pet.Species)) return Fail("该宠物不在本局出战编队中。");
            if (pad < 0 || pad >= Pads.Count) return Fail("请选择有效的部署地块。");
            foreach (Pet tower in Towers)
                if (tower.Pad == pad) return Fail("该地块已被占用。");
            if (Pool.Remove(pet)) Towers.Add(pet);
            pet.Pad = pad;
            Towers.Sort(CompareUid);
            SortPool();
            LastMessage = Spec(pet.Species).name + "已部署。";
            return true;
        }

        public bool Recall(int uid)
        {
            if (!AllowAction()) return false;
            Pet pet = FindPet(uid);
            if (pet == null || !Towers.Contains(pet)) return Fail("请选择场上的宠物进行回收。");
            if (Pool.Count >= Config.poolCapacity) return Fail("待部署栏已满，无法回收。");
            Towers.Remove(pet);
            pet.Pad = -1;
            ClearActiveBuff(pet);
            Pool.Add(pet);
            SortPool();
            LastMessage = "宠物已回收到待部署栏。";
            return true;
        }

        public int SellValue(Pet pet)
        {
            if (pet == null || pet.IsEgg || pet.Level < 1 || pet.Level > Config.maxLevel) return 0;
            return Saturate(Config.sellBase * Math.Pow(3d, pet.Level - 1));
        }

        public bool Sell(int uid)
        {
            if (!AllowAction()) return false;
            Pet pet = FindPet(uid);
            if (pet == null) return Fail("未找到可出售的宠物。");
            if (pet.IsEgg) return Fail("宠物蛋尚未孵化，无法出售。");
            int value = SellValue(pet);
            if ((long)Coins + value > Int32.MaxValue) return Fail("金币已达上限，无法出售。");
            Pool.Remove(pet);
            Towers.Remove(pet);
            Coins += value;
            SortPool();
            LastMessage = "出售宠物，获得" + value + "金币。";
            return true;
        }

        // Eligibility queries are observational: UI polling must not replace feedback.
        public bool CanEvolve(int uid)
        {
            Pet pet = FindPet(uid);
            return ActionsAvailable() && pet != null && Towers.Contains(pet)
                && !pet.IsEgg && pet.Level < Config.maxLevel && EvolutionMaterials(pet).Count >= 2;
        }

        public bool EvolveTower(int uid)
        {
            if (!AllowAction()) return false;
            Pet pet = FindPet(uid);
            if (pet == null || !Towers.Contains(pet)) return Fail("请选择场上的主体宠物。");
            if (pet.IsEgg) return Fail("宠物蛋无法进化。");
            if (pet.Level >= Config.maxLevel) return Fail("该宠物已达到最高等级。");
            List<Pet> materials = EvolutionMaterials(pet);
            if (materials.Count < 2) return Fail("需要另外两只同种同等级宠物。");
            for (int i = 0; i < 2; i++)
            {
                Pool.Remove(materials[i]);
                Towers.Remove(materials[i]);
            }
            Upgrade(pet);
            SortPool();
            LastMessage = "进化成功：" + StageInfo(pet).name + "（" + pet.Level + "阶）。";
            return true;
        }

        public bool CanEvolvePool()
        {
            return ActionsAvailable() && PoolEvolutionPlan().Count != 0;
        }

        public int EvolvePool()
        {
            if (!AllowAction()) return 0;
            // Build the complete plan before mutating any pet. Newly promoted pets
            // cannot enter a higher-level group during this button click.
            List<Pet[]> groups = PoolEvolutionPlan();
            if (groups.Count == 0) { Fail("池中没有三只可进化的同种同等级宠物。"); return 0; }
            foreach (Pet[] group in groups)
            {
                Pool.Remove(group[1]);
                Pool.Remove(group[2]);
                Upgrade(group[0]);
            }
            SortPool();
            LastMessage = "完成" + groups.Count + "次进化，每组提升一级。";
            return groups.Count;
        }

        public float Damage(Pet pet)
        {
            StageSpec stage = StageInfo(pet);
            // Public display value is base level-scaled damage, before support/armor.
            return stage == null ? 0f : Spec(pet.Species).damage * stage.damageMultiplier;
        }

        public StageSpec StageInfo(Pet pet)
        {
            if (pet == null || pet.IsEgg || pet.Level < 1 || pet.Level > Config.maxLevel
                || pet.Level > Rules.StageCount) return null;
            PetSpec spec = Spec(pet.Species);
            if (spec == null) return null;
            StageSpec[] stages = spec.stages;
            if (stages == null && !legacyStages.TryGetValue(spec.id, out stages)) return null;
            return stages != null && stages.Length == Rules.StageCount ? stages[pet.Level - 1] : null;
        }

        public float Interval(Pet pet)
        {
            StageSpec stage = StageInfo(pet);
            return stage == null ? 0f : Spec(pet.Species).interval * stage.intervalMultiplier;
        }

        private float AuraMultiplier(Pet pet)
        {
            float aura = 0f;
            if (Towers.Contains(pet) && ValidPad(pet.Pad))
                foreach (Pet tower in Towers)
                {
                    PetSpec support = Spec(tower.Species);
                    StageSpec stage = StageInfo(tower);
                    if (tower != pet && stage != null && support.effect == "aura"
                        && ValidPad(tower.Pad) && Distance(Pads[tower.Pad], Pads[pet.Pad]) <= Range(tower))
                        aura = Math.Max(aura, stage.effectPower);
                }
            // Auras help other deployed pets only, and only the strongest applies.
            return 1f + aura + (pet.ActiveBuffRemaining > 0f ? pet.ActiveDamageBonus : 0f);
        }

        public float Range(Pet pet)
        {
            StageSpec stage = StageInfo(pet);
            return stage == null ? 0f : Spec(pet.Species).range * stage.rangeMultiplier;
        }

        public float Distance(V2 a, V2 b)
        {
            return TrailGeometry.Distance(a, b);
        }

        public bool CastSkill(float x, float y)
        {
            if (!AllowAction()) return false;
            if (Stage != RunStage.Running) return Fail("星雨只能在战斗中释放。");
            if (!Finite(x) || !Finite(y) || x < 0f || x > 1f || y < 0f || y > 1f)
                return Fail("请选择地图内的技能落点。");
            if (SkillRemaining > 0f) return Fail("星雨正在冷却中。");
            SkillRemaining = Config.skillCooldown;
            Effects.Add(new CombatFx { X = x, Y = y, ToX = x, ToY = y,
                PetIndex = -1, Radius = Config.skillRadius, Life = Rules.SkillDuration, Duration = Rules.SkillDuration });
            V2 point = new V2(x, y);
            foreach (Enemy enemy in Enemies.ToArray())
                if (Distance(point, new V2(enemy.X, enemy.Y)) <= Config.skillRadius)
                    Hurt(enemy, Config.skillDamage); // EXT: star rain and poison ignore armor.
            LastMessage = "星雨已释放。";
            ResolveWave();
            return true;
        }

        public void Tick(float dt)
        {
            if (Paused || Stage == RunStage.Won || Stage == RunStage.Lost) return;
            if (Lives <= 0) { Lose(); return; }
            if (!Nonnegative(dt)) { Fail("模拟时间必须是有效的非负数。"); return; }
            if (Stage != RunStage.Running) return;
            accumulator += dt;
            while (accumulator + Rules.ClockEpsilon >= Rules.Step)
            {
                accumulator -= Rules.Step;
                if (accumulator < 0) accumulator = 0;
                RunStage before = Stage;
                Step((float)Rules.Step);
                // Never spend the rest of a long frame in the next manual phase.
                if (Stage != before) { accumulator = 0; break; }
            }
        }

        private void Step(float dt)
        {
            // All gameplay clocks run only during Running; preparing never recovers CD.
            Elapsed += dt;
            SkillRemaining = Math.Max(0f, SkillRemaining - dt);
            for (int i = Effects.Count - 1; i >= 0; i--)
            {
                Effects[i].Life = Math.Max(0f, Effects[i].Life - dt);
                if (Effects[i].Life <= 0f) Effects.RemoveAt(i);
            }
            foreach (Pet pet in Pool) pet.Cooldown = Math.Max(0f, pet.Cooldown - dt);
            TickAutoClocks(dt);
            spawnClock -= Rules.Step;
            while (remainingToSpawn > 0 && spawnClock <= Rules.ClockEpsilon)
            {
                SpawnEnemy();
                spawnClock += Level.spawnInterval;
            }
            // Stable order: spawn -> poison -> attacks -> movement/leak -> clear.
            // A target killed this step cannot also leak. Fatal leaks precede clear.
            foreach (Enemy enemy in Enemies.ToArray())
            {
                if (enemy.Hp <= 0f) { Defeat(enemy); continue; }
                TickPoison(enemy, dt);
                if (Enemies.Contains(enemy)) TickBurn(enemy, dt);
                enemy.VulnerableRemaining = Math.Max(0f, enemy.VulnerableRemaining - dt);
                if (enemy.VulnerableRemaining == 0f) enemy.Vulnerability = 0f;
            }
            TickEnemyAbilities(dt);
            RunAutoSkills();
            foreach (Pet tower in Towers)
            {
                if (tower.IsEgg || !ValidPad(tower.Pad)) continue;
                PetSpec spec = Spec(tower.Species);
                StageSpec stage = StageInfo(tower);
                if (spec == null || stage == null) continue;
                tower.Cooldown -= dt;
                while (tower.Cooldown <= 0f)
                {
                    int count = spec.effect == "armor" || spec.effect == "slow" || spec.effect == "rapid"
                        ? stage.targets : 1;
                    List<Enemy> targets = TargetSnapshot(tower, count);
                    if (targets.Count == 0) { tower.Cooldown = 0f; break; }
                    foreach (Enemy target in targets) Attack(tower, spec, stage, target);
                    tower.Cooldown += AttackInterval(tower);
                }
            }
            foreach (Enemy enemy in Enemies.ToArray())
            {
                EnemyStatus status;
                float slow = statuses.TryGetValue(enemy, out status) ? status.Slow : 0f;
                float slowedTime = Math.Min(dt, Math.Max(0f, enemy.SlowRemaining));
                float rootedTime = Math.Min(dt, Math.Max(0f, enemy.RootRemaining));
                enemy.Progress += Math.Max(0f, enemy.Speed) * (dt - rootedTime - Math.Max(0f, slowedTime - rootedTime) * slow);
                enemy.RootRemaining = Math.Max(0f, enemy.RootRemaining - dt);
                enemy.SlowRemaining = Math.Max(0f, enemy.SlowRemaining - dt);
                if (enemy.SlowRemaining == 0f && status != null) status.Slow = 0f;
                if (PlaceOnPath(enemy))
                {
                    Enemies.Remove(enemy);
                    statuses.Remove(enemy);
                    ClearStatus(enemy);
                    if (settled.Add(enemy)) Lives = Math.Max(0, Lives - Math.Max(0, enemy.Leak));
                    if (Lives <= 0) { Lose(); return; }
                }
            }
            RefreshFxTargets();
            ResolveWave();
        }

        private void RefreshFxTargets()
        {
            foreach (CombatFx fx in Effects)
            {
                if (fx.TargetId <= 0) continue; // Ground AOE stays at its actual cast location.
                foreach (Enemy enemy in Enemies)
                    if (enemy.Id == fx.TargetId)
                    {
                        fx.ToX = enemy.X; fx.ToY = enemy.Y;
                        break;
                    }
                // Removed targets keep the most recent hit position, never retarget
                // by array index and never snap back to the original shot location.
            }
        }

        private void SpawnEnemy()
        {
            if (Level.enemyWaves != null)
            {
                EnemySpec spec = EnemyInfo(WaveRoster(Wave)[spawned]);
                float health = Level.baseHp * Level.hpScale * (1f + Level.hpGrowth * (Wave - 1)) * spec.hpMultiplier;
                Enemies.Add(CreateEnemy(spec, health, 0));
                remainingToSpawn--; spawned++;
                return;
            }
            int kind = (spawned + Wave - 1) % 3;
            if (Wave == Level.waves && remainingToSpawn == 1) kind = 3;
            float hp = Level.baseHp * Level.hpScale * (1f + Level.hpGrowth * (Wave - 1))
                * Rules.HpMultiplier[kind];
            Enemies.Add(new Enemy { Id = nextEnemyId++, Kind = kind,
                Hp = hp, MaxHp = hp, Armor = Rules.Armor[kind],
Speed = (Config.enemyBaseSpeed > 0 ? Config.enemyBaseSpeed : Rules.BaseSpeed) * Level.speedScale * Rules.SpeedMultiplier[kind],
                Reward = Config.killRewards == null ? Rules.Reward[kind] : Config.killRewards[kind],
                Leak = Rules.Leak[kind], X = Path[0].X, Y = Path[0].Y });
            remainingToSpawn--;
            spawned++;
        }

        private bool PlaceOnPath(Enemy enemy)
        {
            V2 point, direction;
            bool reachedExit = TrailGeometry.Sample(Path, enemy.Progress, out point, out direction);
            enemy.X = point.X; enemy.Y = point.Y;
            return reachedExit;
        }

        private List<Enemy> TargetSnapshot(Pet tower, int count)
        {
            List<Enemy> candidates = new List<Enemy>();
            float range = Range(tower);
            foreach (Enemy enemy in Enemies)
                if (enemy.Hp > 0f && !candidates.Contains(enemy)
                    && Distance(Pads[tower.Pad], new V2(enemy.X, enemy.Y)) <= range) candidates.Add(enemy);
            candidates.Sort(delegate(Enemy a, Enemy b) {
                int progress = b.Progress.CompareTo(a.Progress);
                return progress != 0 ? progress : a.Id.CompareTo(b.Id);
            });
            if (candidates.Count > count) candidates.RemoveRange(count, candidates.Count - count);
            return candidates;
        }

        private void Attack(Pet tower, PetSpec spec, StageSpec stage, Enemy target)
        {
            if (!Enemies.Contains(target) || target.Hp <= 0f) return;
            float damage = Damage(tower) * AuraMultiplier(tower);
            float armor = Math.Max(0f, target.Armor);
            if (spec.effect == "armor") armor *= 1f - Math.Min(1f, stage.effectPower);
            if (spec.effect == "heavy" && target.Kind == 3) damage *= stage.eliteMultiplier;
            Enemy[] splashSnapshot = spec.effect == "splash" ? Enemies.ToArray() : null;
            V2 point = new V2(target.X, target.Y);
            Hurt(target, damage * 100f / (100f + armor));
            Effects.Add(new CombatFx { X = Pads[tower.Pad].X, Y = Pads[tower.Pad].Y,
                ToX = point.X, ToY = point.Y, PetIndex = spec.atlasIndex,
                SourceLevel = tower.Level, TargetId = target.Id, TargetKind = target.Kind,
                TargetSize = EnemyInfo(target) == null ? 0 : EnemyInfo(target).visualSize,
                Life = Rules.ShotDuration, Duration = Rules.ShotDuration });
            if (spec.effect == "splash")
            {
                // EXT: secondary victims take the same physical hit, once each.
                foreach (Enemy other in splashSnapshot)
                    if (other != target && Distance(point, new V2(other.X, other.Y)) <= stage.effectPower)
                        Hurt(other, damage * 100f / (100f + Math.Max(0f, other.Armor)));
            }
            if (!Enemies.Contains(target)) return;
            if (spec.effect == "rapid" && target.Kind != 3 && target.Shield <= 0f && target.Hp > 0f
                && target.Hp <= target.MaxHp * stage.executeThreshold)
            {
                Hurt(target, target.Hp);
            }
            else if (spec.effect == "slow" && stage.effectDuration > 0f)
            {
                EnemyStatus state = Status(target);
                float power = spec.stages == null ? Math.Min(Rules.MaximumSlow, stage.effectPower) : stage.effectPower;
                state.Slow = target.SlowRemaining > 0f ? Math.Max(state.Slow, power) : power;
                target.SlowRemaining = Math.Max(target.SlowRemaining, stage.effectDuration);
            }
            else if (spec.effect == "poison" && stage.effectDuration > 0f)
            {
                // GDD: the wood species has ONE poison, shared by all its towers.
                // Never add DPS. A weaker hit refreshes the stronger active poison.
                // Stage effectPower is already the final base DPS, never scale twice.
                float dps = stage.effectPower * AuraMultiplier(tower);
                target.PoisonDps = target.PoisonRemaining > 0f ? Math.Max(target.PoisonDps, dps) : dps;
                target.PoisonRemaining = Math.Max(target.PoisonRemaining, stage.effectDuration);
            }
        }

        private void TickPoison(Enemy enemy, float dt)
        {
            if (enemy.PoisonRemaining > 0f)
            {
                float activeTime = Math.Min(dt, enemy.PoisonRemaining);
                float dps = enemy.PoisonDps;
                enemy.PoisonRemaining = Math.Max(0f, enemy.PoisonRemaining - dt);
                if (enemy.PoisonRemaining == 0f) enemy.PoisonDps = 0f;
                if (activeTime > 0f) DotHurt(enemy, activeTime, dps);
            }
        }

        private static void ClearStatus(Enemy enemy)
        {
            enemy.SlowRemaining = 0f;
            enemy.PoisonDps = 0f;
            enemy.PoisonRemaining = 0f;
            enemy.BurnRemaining = enemy.BurnDps = enemy.RootRemaining = 0f;
            enemy.VulnerableRemaining = enemy.Vulnerability = 0f;
        }

        private EnemyStatus Status(Enemy enemy)
        {
            EnemyStatus value;
            if (!statuses.TryGetValue(enemy, out value))
            {
                value = new EnemyStatus();
                statuses.Add(enemy, value);
            }
            return value;
        }

        private void Hurt(Enemy enemy, float damage, bool applyVulnerability = true)
        {
            if (!Enemies.Contains(enemy) || settled.Contains(enemy)) return;
            if (applyVulnerability && enemy.VulnerableRemaining > 0f) damage *= 1f + enemy.Vulnerability;
            float absorbed = Math.Min(enemy.Shield, Math.Max(0, damage));
            enemy.Shield -= absorbed;
            damage -= absorbed;
            enemy.Hp = Math.Max(0f, enemy.Hp - Math.Max(0f, damage));
            if (enemy.Hp <= 0f) Defeat(enemy);
        }

        private void Defeat(Enemy enemy)
        {
            if (!Enemies.Remove(enemy)) return;
            statuses.Remove(enemy);
            ClearStatus(enemy);
            if (!settled.Add(enemy)) return;
            enemy.Hp = 0f;
            Kills++;
            Coins = Saturate((double)Coins + Math.Max(0, enemy.Reward));
            SpawnChildren(enemy);
        }

        private void ResolveWave()
        {
            if (Lives <= 0) { Lose(); return; }
            if (Stage != RunStage.Running || remainingToSpawn != 0 || Enemies.Count != 0) return;
            int reward = Saturate((double)Config.clearReward + (double)Config.clearRewardGrowth * (Wave - 1));
            Coins = Saturate((double)Coins + reward);
            Effects.Clear();
            Stage = Wave == Level.waves ? RunStage.Won : RunStage.Preparing;
            LastMessage = Stage == RunStage.Won ? "守护成功！所有敌人已被击退。"
                : "第" + Wave + "波完成，获得" + reward + "金币，请准备下一波。";
        }

        private void Lose()
        {
            Lives = 0;
            Stage = RunStage.Lost;
            LastMessage = "基地生命耗尽，守护失败。";
        }

        private Pet NewPet(string species, int readyWave)
        {
            while (FindPet(nextPetUid) != null) nextPetUid++;
            Pet pet = new Pet { Uid = nextPetUid++, Species = species, Level = 1, ReadyWave = readyWave, Pad = -1 };
            AutoSkillSpec skill = AutoSkill(pet);
            pet.ActiveRemaining = skill == null ? 0f : skill.initialDelay;
            return pet;
        }

        private void Upgrade(Pet pet)
        {
            pet.Level++;
            pet.Cooldown = Math.Min(pet.Cooldown, Interval(pet));
            AutoSkillSpec skill = AutoSkill(pet);
            if (skill != null) pet.ActiveRemaining = Math.Min(pet.ActiveRemaining, skill.cooldown);
        }

        private List<Pet> EvolutionMaterials(Pet subject)
        {
            List<Pet> result = Pool.FindAll(delegate(Pet pet) { return Matches(pet, subject); });
            result.Sort(CompareUid);
            List<Pet> field = Towers.FindAll(delegate(Pet pet) { return pet != subject && Matches(pet, subject); });
            field.Sort(CompareUid);
            result.AddRange(field);
            return result;
        }

        private List<Pet[]> PoolEvolutionPlan()
        {
            List<Pet[]> result = new List<Pet[]>();
            List<Pet> snapshot = new List<Pet>(Pool);
            snapshot.Sort(CompareUid);
            while (snapshot.Count > 0)
            {
                Pet first = snapshot[0];
                snapshot.RemoveAt(0);
                if (first.IsEgg || first.Level >= Config.maxLevel) continue;
                List<Pet> matching = snapshot.FindAll(delegate(Pet pet) { return Matches(pet, first); });
                if (matching.Count < 2) continue;
                snapshot.Remove(matching[0]);
                snapshot.Remove(matching[1]);
                result.Add(new Pet[] { first, matching[0], matching[1] });
            }
            return result;
        }

        private static bool Matches(Pet pet, Pet other)
        {
            return !pet.IsEgg && pet.Level == other.Level && pet.Species == other.Species;
        }

        private void SortPool()
        {
            Pool.Sort(delegate(Pet a, Pet b) {
                if (a.IsEgg != b.IsEgg) return a.IsEgg ? 1 : -1;
                if (!a.IsEgg)
                {
                    int level = b.Level.CompareTo(a.Level);
                    if (level != 0) return level;
                    int element = ElementOrder(a.Species).CompareTo(ElementOrder(b.Species));
                    if (element != 0) return element;
                }
                return a.Uid.CompareTo(b.Uid);
            });
        }

        private int ElementOrder(string species)
        {
            PetSpec spec = Spec(species);
            if (spec == null || String.IsNullOrEmpty(spec.element)) return 7;
            int rank = "金木水火土光暗".IndexOf(spec.element, StringComparison.Ordinal);
            return rank < 0 ? 7 : rank;
        }

        private static int CompareUid(Pet a, Pet b) { return a.Uid.CompareTo(b.Uid); }
        private bool ValidPad(int pad) { return pad >= 0 && pad < Pads.Count; }
        private bool InTeam(string species) { return Array.IndexOf(team, species) >= 0; }
        private bool ActionsAvailable()
        {
            return !Paused && Lives > 0 && (Stage == RunStage.Preparing || Stage == RunStage.Running);
        }
        private bool AllowAction()
        {
            if (Paused) return Fail("游戏已暂停，请继续后操作。");
            if (Stage == RunStage.Won) return Fail("本关已胜利，无法继续操作。");
            if (Stage == RunStage.Lost || Lives <= 0) return Fail("基地已失守，无法继续操作。");
            return true;
        }
        private bool Fail(string message) { LastMessage = message; return false; }

        private uint RandomWord()
        {
            // Explicit xorshift32 avoids platform/runtime-specific System.Random changes.
            uint value = randomState;
            value ^= value << 13; value ^= value >> 17; value ^= value << 5;
            randomState = value;
            return value;
        }
        private int NextRandom(int count)
        {
            // xorshift visits every nonzero uint; rejection gives uniform selection.
            uint bound = (uint)count, limit = UInt32.MaxValue - UInt32.MaxValue % bound;
            uint value;
            do { value = RandomWord() - 1u; } while (value >= limit);
            return (int)(value % bound);
        }
        private static int Saturate(double value) { return (int)Math.Max(0d, Math.Min(Int32.MaxValue, Math.Floor(value))); }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static bool Nonnegative(float value) { return Finite(value) && value >= 0f; }
        private static bool Positive(float value) { return Finite(value) && value > 0f; }

        private static void ValidateStages(PetSpec spec)
        {
            if (spec.stages == null) return;
            if (spec.stages.Length != Rules.StageCount)
                throw new ArgumentException("宠物阶段配置必须恰好五项：" + spec.id, "config");
            for (int i = 0; i < spec.stages.Length; i++)
            {
                StageSpec stage = spec.stages[i];
                if (stage == null || String.IsNullOrWhiteSpace(stage.name) || String.IsNullOrWhiteSpace(stage.ability)
                    || String.IsNullOrWhiteSpace(stage.description) || String.IsNullOrWhiteSpace(stage.appearance)
                    || !Nonnegative(stage.damageMultiplier) || !Positive(stage.rangeMultiplier)
                    || !Positive(stage.intervalMultiplier) || !Nonnegative(stage.effectPower)
                    || !Nonnegative(stage.effectDuration) || !Nonnegative(stage.executeThreshold)
                    || stage.executeThreshold > .25f || !Finite(stage.eliteMultiplier) || stage.eliteMultiplier < 1f
                    || stage.targets < 1 || stage.targets > 3
                    || ((spec.effect == "armor" || spec.effect == "slow") && stage.effectPower > 1f)
                    || !Nonnegative(spec.damage * stage.damageMultiplier)
                    || !Nonnegative(spec.range * stage.rangeMultiplier)
                    || !Positive(spec.interval * stage.intervalMultiplier))
                    throw new ArgumentException("宠物第" + (i + 1) + "阶配置数值无效：" + spec.id, "config");
                ValidateAutoSkill(spec, stage);
            }
        }

        private static void ValidateConfig(GameConfig config, int levelIndex)
        {
            if (config == null || config.pets == null || config.pets.Length == 0 || config.levels == null
                || levelIndex < 0 || levelIndex >= config.levels.Length)
                throw new ArgumentException("游戏或关卡配置无效。", "config");
            if (config.initialGold < 0 || config.initialLives <= 0 || config.drawCost < 0
                || config.poolCapacity < Math.Min(Rules.StarterCount, config.pets.Length)
                || config.maxLevel < 1 || config.maxLevel > Rules.StageCount
                || config.sellBase < 0 || config.clearReward < 0 || config.clearRewardGrowth < 0
                || !Positive(config.levelDamageScale) || !Nonnegative(config.skillCooldown)
                || !Nonnegative(config.skillDamage) || !Nonnegative(config.skillRadius))
                throw new ArgumentException("经济、技能或成长配置无效。", "config");
            if (!Nonnegative(config.enemyBaseSpeed) || (config.killRewards != null
                && (config.killRewards.Length != 4 || Array.Exists(config.killRewards, n => n < 0))))
                throw new ArgumentException("敌人速度或金币奖励配置无效。", "config");
            LevelSpec level = config.levels[levelIndex];
            if (level != null) ValidateEnemyRoster(config, level);
            if (level == null || level.waves < 1 || level.slots < 1 || level.slots > Rules.TeamSlots
                || level.baseCount < 1 || level.countGrowth < 0
                || (long)level.baseCount + (long)level.countGrowth * (level.waves - 1) > Int32.MaxValue
                || !Positive(level.hpScale) || !Positive(level.speedScale) || !Positive(level.baseHp)
                || !Nonnegative(level.hpGrowth) || !Positive(level.spawnInterval))
                throw new ArgumentException("关卡波次配置无效。", "config");
        }
    }
}
