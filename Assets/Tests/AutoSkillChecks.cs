using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace PetTD
{
    public static class AutoSkillChecks
    {
        public static string Run(GameConfig config)
        {
            if (config.pets[0].stages == null || config.pets[0].stages[0].active == null)
                return "自动技能：旧配置无active，保持原回归；新技能场景不适用。\n";
            return new Suite(config).Run();
        }
        sealed class Suite
        {
            readonly GameConfig source;
            int assertions, groups;
            readonly StringBuilder report = new StringBuilder();
            public Suite(GameConfig source) { this.source = source; }
            void Assert(bool ok, string message) { assertions++; if (!ok) throw new Exception("AUTO_CHECK_FAILED " + message); }
            void Near(float a, float b, string msg, float tolerance = .03f) { Assert(!Single.IsNaN(a) && Math.Abs(a-b)<=tolerance, msg + " actual=" + a + " expected=" + b); }
            void Check(string name, Action run) { run(); groups++; report.Append("通过：自动技 ").Append(name).Append('\n'); }
            public string Run()
            {
                Check("35配置与七技无人输入真实触发", Matrix);
                Check("首施CD、无目标保留、循环间隔", Clocks);
                Check("暂停、准备、回收重部署、进化", Lifecycle);
                Check("金隼真实伤害、多目标不重复", Volley);
                Check("木毒、易伤与火烧独立", DamageEffects);
                Check("霜潮精英半时长、定身精确移动", Control);
                Check("光鹿增益条件、叠加上限与到期", Tempo);
                Check("暗蝠低血优先、处决免疫和奖励一次", Assault);
                Check("自动释放不重置普攻、无主动被动递归", BasicSeparation);
                Check("非法配置与旧active回退", Invalid);
                Check("时间分片确定性", Deterministic);
                report.Insert(0, "自动技能专项通过：" + groups + "组，" + assertions + "项断言。\n");
                return report.ToString();
            }
            GameModel Arena(string id, int stage=1, bool ready=true)
            {
                GameConfig cfg=(GameConfig)Clone(source);
                GameModel model=new GameModel(cfg,0,new[]{id},17);
                model.Path=new List<V2>{new V2(0,0),new V2(1,0)};
                for(int i=0;i<model.Pads.Count;i++)model.Pads[i]=new V2(.4f+i*.01f,0);
                Assert(model.Deploy(model.Pool[0].Uid,0),"deploy test caster");
                Pet pet=model.Towers[0];pet.Level=stage;pet.Cooldown=9999;
                pet.ActiveRemaining=ready?0:model.AutoSkill(pet).initialDelay;
                model.Stage=RunStage.Running;
                Enemy sentinel=Enemy(model,900,1.5f,1000000);sentinel.X=.9375f;
                return model;
            }
            Enemy Enemy(GameModel m,int id,float progress=.65f,float hp=100000)
            {
                var e=new Enemy{Id=id,Kind=0,Hp=hp,MaxHp=hp,Progress=progress,X=progress/1.6f,Y=0,Speed=0,Reward=11,Leak=1};
                m.Enemies.Add(e);return e;
            }
            Pet Friend(GameModel m,string id="fox",int level=1,int pad=1)
            {
                var p=new Pet{Uid=100+pad,Species=id,Level=level,Pad=pad,Cooldown=9999,ActiveRemaining=9999};
                m.Towers.Add(p);return p;
            }
            void Step(GameModel m,float time=1f/60) { m.Tick(time); }
            void Matrix()
            {
                foreach(PetSpec p in source.pets)for(int level=1;level<=5;level++)
                {
                    var m=Arena(p.id,level);var caster=m.Towers[0];var a=m.AutoSkill(caster);
                    Assert(a!=null && a.initialDelay==3 && a.cooldown>=9,"phase active data");
                    Assert(m.ActiveRange(caster)>0 && m.AttackInterval(caster)>0,"public read API");
                    Enemy(m,1);if(p.id=="deer")Friend(m);
                    Step(m);
                    Assert(caster.AutoCasts==1 && caster.ActiveRemaining>0,"auto cast without input "+p.id+level);
                    Assert(m.Effects.Exists(f=>f.IsAuto && f.PetIndex==p.atlasIndex),"auto visual event");
                }
            }
            void Clocks()
            {
                var m=Arena("falcon",1,false);var p=m.Towers[0];
                Enemy(m,1);Step(m,2.9f);Assert(p.AutoCasts==0,"initial delay not early");
                Step(m,.12f);Assert(p.AutoCasts==1,"initial delay auto fire");
                float cd=p.ActiveRemaining;Step(m,cd-.05f);Assert(p.AutoCasts==1,"cooldown not early");
                Step(m,.1f);Assert(p.AutoCasts==2,"second cast on cooldown");
                var empty=Arena("fox");var q=empty.Towers[0];Step(empty,30);Assert(q.AutoCasts==0 && q.ActiveRemaining==0,"no target holds ready");
                Enemy(empty,2);Step(empty);Assert(q.AutoCasts==1,"ready does not accrue multiple casts");
                Near(q.ActiveRemaining,empty.AutoSkill(q).cooldown,"full CD after cast");
            }
            void Lifecycle()
            {
                var m=Arena("fox",1,false);var p=m.Towers[0];Enemy(m,1);
                m.Paused=true;Step(m,20);Near(p.ActiveRemaining,3,"pause freezes auto");Assert(p.AutoCasts==0,"pause no cast");
                m.Paused=false;m.Stage=RunStage.Preparing;Step(m,20);Near(p.ActiveRemaining,3,"prep freezes auto");
                m.Stage=RunStage.Running;Step(m,1);float before=p.ActiveRemaining;
                p.ActiveBuffRemaining=2;p.ActiveDamageBonus=.2f;p.ActiveAttackSpeedBonus=.3f;
                Assert(m.Recall(p.Uid),"recall");Step(m,1);Near(p.ActiveRemaining,before,"pool frozen");
                Assert(p.ActiveBuffRemaining==0 && p.ActiveDamageBonus==0,"recall clears own buff");
                Assert(m.Deploy(p.Uid,0),"redeploy");Near(p.ActiveRemaining,before,"redeploy not reset");
                m.Pool.Add(new Pet{Uid=20,Species=p.Species,Level=1,Pad=-1});
                m.Pool.Add(new Pet{Uid=21,Species=p.Species,Level=1,Pad=-1});
                Assert(m.EvolveTower(p.Uid),"evolve while charging");Near(p.ActiveRemaining,before,"evolve not refresh");
                Assert(p.Level==2 && m.AutoSkill(p)==m.Spec("fox").stages[1].active,"new phase ability");
                m.Stage=RunStage.Won;Step(m,20);Near(p.ActiveRemaining,before,"terminal frozen");
            }
            void Volley()
            {
                var m=Arena("falcon",5);var p=m.Towers[0];float expected=m.CombatDamage(p)*m.AutoSkill(p).damageRatio;
                var victims=new List<Enemy>();for(int i=0;i<5;i++){var e=Enemy(m,10+i,.65f+.01f*i);e.Armor=500;victims.Add(e);}
                Step(m);
                Near(victims[0].Hp,100000,"lowest progress spared");
                for(int i=1;i<5;i++)Near(100000-victims[i].Hp,expected,"true volley one hit",.1f);
                Assert(m.Effects.FindAll(f=>f.IsAuto).Count==4,"four projectiles");
            }
            void DamageEffects()
            {
                var m=Arena("sprout",5);var p=m.Towers[0];var a=m.AutoSkill(p);var e=Enemy(m,1);
                Step(m);Near(100000-e.Hp,m.Damage(p)*a.damageRatio,"bloom first hit before vulnerability");
                Near(e.PoisonDps,a.poisonDps,"poison final not scaled twice");
                Near(e.Vulnerability,a.vulnerability,"vulnerability installed");
                e.BurnDps=40;e.BurnRemaining=2;
                float hp=e.Hp;Step(m,1);Near(hp-e.Hp,(a.poisonDps+40)*(1+a.vulnerability),"poison plus burn and one vulnerability",.4f);
                e.PoisonRemaining=0;e.PoisonDps=0;e.BurnRemaining=.007f;e.BurnDps=100;
                e.VulnerableRemaining=.003f;e.Vulnerability=.2f;hp=e.Hp;Step(m);
                Near(hp-e.Hp,100*(.007f+.003f*.2f),"partial tick vulnerability no rounding",.02f);
                Assert(e.BurnRemaining==0 && e.Vulnerability==0,"dot and vulnerable expire");
                var f=Arena("fox",5);var fox=f.Towers[0];var b=f.AutoSkill(fox);var target=Enemy(f,1);
                Step(f);Near(target.BurnDps,f.Damage(fox)*b.burnRatio,"fire active burn snapshot");
                var weak=Friend(f,"fox",1);weak.ActiveRemaining=0;Step(f);Near(target.BurnDps,f.Damage(fox)*b.burnRatio,"weak burn preserves stronger");
                var lethal=Arena("fox");var victim=Enemy(lethal,1,hp:1);int gold=lethal.Coins;Step(lethal);
                Assert(!lethal.Enemies.Contains(victim) && lethal.Kills==1 && lethal.Coins==gold+11,"lethal active rewards once");
                Step(lethal,1);Assert(lethal.Kills==1,"no DoT reward twice");
            }
            void Control()
            {
                var ice=Arena("otter",5);var normal=Enemy(ice,1,.65f);var elite=Enemy(ice,2,.66f);elite.Kind=3;
                Step(ice);float d=ice.AutoSkill(ice.Towers[0]).duration;
                Near(normal.SlowRemaining,d-1f/60,"normal slow duration");
                Near(elite.SlowRemaining,d*.5f-1f/60,"elite slow duration");
                var rock=Arena("turtle",5);var n=Enemy(rock,1);var k=Enemy(rock,2,.66f);k.Kind=3;
                Step(rock);float root=rock.AutoSkill(rock.Towers[0]).rootDuration;
                Near(n.RootRemaining,root-1f/60,"root normal");Near(k.RootRemaining,root*.5f-1f/60,"root elite");
                n.RootRemaining=.007f;n.SlowRemaining=0;n.Speed=.1f;float pos=n.Progress;
                Step(rock);Near(n.Progress-pos,.1f*(1f/60-.007f),"partial root movement",.00001f);
                n.RootRemaining=.5f;rock.Paused=true;Step(rock,3);Near(n.RootRemaining,.5f,"pause root");
            }
            void Tempo()
            {
                var m=Arena("deer",5);var deer=m.Towers[0];Enemy(m,1);
                Step(m);Assert(deer.AutoCasts==0,"no friends no waste");
                var friend=Friend(m);Step(m);var a=m.AutoSkill(deer);
                Assert(deer.AutoCasts==1 && deer.ActiveBuffRemaining==0,"not self buff");
                Near(m.CombatDamage(friend),m.Damage(friend)*(1+m.StageInfo(deer).effectPower+a.allyDamageBonus),"aura and tempo additive");
                Near(m.AttackInterval(friend),m.Interval(friend)/(1+a.allyAttackSpeedBonus),"actual attack rate");
                var weak=Friend(m,"deer",1,2);weak.ActiveRemaining=0;Step(m);
                Near(friend.ActiveDamageBonus,a.allyDamageBonus,"weak tempo not stack or lower");
                Near(friend.ActiveAttackSpeedBonus,a.allyAttackSpeedBonus,"speed strongest");
                deer.ActiveRemaining=weak.ActiveRemaining=9999;
                Step(m,4.1f);Assert(friend.ActiveBuffRemaining==0,"tempo expires");
                Near(m.AttackInterval(friend),m.Interval(friend),"interval restores");
            }
            void Assault()
            {
                var m=Arena("bat",1);
                var low=Enemy(m,2,.64f,1);low.MaxHp=100;
                var high=Enemy(m,1,.7f,100);int money=m.Coins;Step(m);
                Assert(!m.Enemies.Contains(low) && high.Hp==100 && m.Coins==money+11,"lowest HP ratio before progress");
                var elite=Arena("bat",5);var boss=Enemy(elite,2,hp:1000);boss.Kind=3;boss.MaxHp=100000;
                Step(elite);Assert(elite.Enemies.Contains(boss) && boss.Hp>0,"elite immune to execute");
                var finisher=Arena("bat",5);var foe=Enemy(finisher,2,hp:1000);foe.MaxHp=100000;
                Step(finisher);Assert(!finisher.Enemies.Contains(foe) && finisher.Kills==1,"nonelite threshold execute once");
            }
            void BasicSeparation()
            {
                var m=Arena("fox",5);var p=m.Towers[0];var e=Enemy(m,1);float cd=p.Cooldown;
                var a=m.AutoSkill(p);Step(m);
                Near(cd-p.Cooldown,1f/60,"active does not reset basic timer",.002f);
                Near(100000-e.Hp,m.Damage(p)*a.damageRatio,"no basic splash recursion",.1f);
                Assert(p.AutoCasts==1,"one action each tick");
            }
            void Invalid()
            {
                Action<AutoSkillSpec>[] mutations={
                    a=>a.cooldown=0,a=>a.initialDelay=a.cooldown+1,a=>a.targets=5,a=>a.effect="manual",
                    a=>a.slowPower=.99f,a=>a.damageRatio=float.NaN,a=>a.radius=float.PositiveInfinity,
                    a=>a.name="",a=>a.eliteMultiplier=.5f,a=>a.executeThreshold=.3f};
                foreach(var mutate in mutations)
                {
                    var cfg=(GameConfig)Clone(source);mutate(cfg.pets[0].stages[0].active);bool threw=false;
                    try{new GameModel(cfg,0,new[]{"falcon"},1);}catch(ArgumentException){threw=true;}
                    Assert(threw,"invalid active rejected");
                }
                var legacy=(GameConfig)Clone(source);foreach(var pet in legacy.pets)foreach(var stage in pet.stages)stage.active=null;
                var old=new GameModel(legacy,0,new[]{"falcon"},1);Assert(old.AutoSkill(old.Pool[0])==null,"active-null fallback");
                Assert(old.AutoSkill(null)==null && old.AttackInterval(null)==0 && old.CombatDamage(null)==0,"null query safe");
            }
            void Deterministic()
            {
                foreach(string id in new[]{"fox","sprout","otter","turtle","deer","bat","falcon"})
                {
                    var a=Arena(id,5,false);var b=Arena(id,5,false);var c=Arena(id,5,false);
                    foreach(var m in new[]{a,b,c}){Enemy(m,1);Enemy(m,2,.7f);if(id=="deer")Friend(m);}
                    for(int i=0;i<600;i++)Step(a);
                    for(int i=0;i<100;i++)Step(b,.1f);
                    Step(c,10);
                    foreach(var other in new[]{b,c})
                    {
                        Assert(a.Towers[0].AutoCasts==other.Towers[0].AutoCasts,"cast count slices "+id);
                        Near(a.Towers[0].ActiveRemaining,other.Towers[0].ActiveRemaining,"CD slices "+id,.001f);
                        for(int j=0;j<a.Enemies.Count;j++)Near(a.Enemies[j].Hp,other.Enemies[j].Hp,"HP slices "+id,.2f);
                    }
                }
            }
        }
        static object Clone(object value)
        {
            if(value==null)return null;
            Type t=value.GetType();if(t.IsPrimitive || t.IsEnum || value is string)return value;
            if(t.IsArray){Array a=(Array)value,b=Array.CreateInstance(t.GetElementType(),a.Length);for(int i=0;i<a.Length;i++)b.SetValue(Clone(a.GetValue(i)),i);return b;}
            object copy=Activator.CreateInstance(t);
            foreach(FieldInfo f in t.GetFields(BindingFlags.Public|BindingFlags.Instance))f.SetValue(copy,Clone(f.GetValue(value)));
            return copy;
        }
    }
}
