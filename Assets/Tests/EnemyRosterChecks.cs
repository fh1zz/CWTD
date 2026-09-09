using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace PetTD
{
    public static class EnemyRosterChecks
    {
        public static string Run(GameConfig source)
        {
            if(source.enemyTypes==null) return "Enemy roster: legacy config, new archetypes not configured.\n";
            int checks=0;
            Action<bool,string> check=(ok,name)=>{checks++;if(!ok)throw new Exception("ENEMY_CHECK_FAILED "+name);};
            check(source.enemyTypes.Length==12,"12 independently named/rendered archetypes");
            var atlas=new HashSet<int>();var roles=new HashSet<string>();
            foreach(var s in source.enemyTypes){check(atlas.Add(s.atlasIndex),"unique silhouette "+s.id);roles.Add(s.role);}
            check(roles.Count>=9,"distinct battlefield roles, not recolors");
            var encountered=new HashSet<string>();
            for(int l=0;l<source.levels.Length;l++)
                for(int w=1;w<=source.levels[l].waves;w++)
                {
                    var c=(GameConfig)Clone(source); c.levels[l].speedScale=.00001f;
                    var m=new GameModel(c,l,new[]{c.pets[0].id},41);m.Pool.Clear();m.Wave=w-1;
                    string[] preview=m.WaveRoster(w);int gold=m.Coins;
                    check(m.Wave==w-1&&m.Coins==gold&&m.Enemies.Count==0,"preview has no side effects");
                    m.StartWave();m.Tick(preview.Length*m.Level.spawnInterval+.1f);
                    check(m.RemainingToSpawn==0&&m.Enemies.Count==preview.Length,"configured count, boss replaces last unit");
                    for(int i=0;i<preview.Length;i++)
                    {
                        Enemy e=m.Enemies[i];EnemySpec s=m.EnemyInfo(e);encountered.Add(s.id);
                        check(e.Species==preview[i],"preview order equals actual spawn");
                        float hp=m.Level.baseHp*m.Level.hpScale*(1+m.Level.hpGrowth*(w-1))*s.hpMultiplier;
                        check(Math.Abs(e.MaxHp-hp)<.02f&&e.Reward==s.reward&&e.Leak==s.leak,"data drives HP, reward, leak");
                        check((e.Kind==3)==(w==m.Level.waves&&i==preview.Length-1),"boss appears only in final slot");
                    }
                }
            check(encountered.Count==11&&!encountered.Contains("bud"),"11 authored spawns plus split-only child");

            var shield=Arena(source,"guard");var guard=shield.Enemies[0];
            guard.MaxShield=guard.Shield=50;guard.Hp=guard.MaxHp=100;guard.Armor=0;
            shield.Config.skillDamage=40;shield.Config.skillCooldown=0;
            shield.CastSkill(guard.X,guard.Y);
            check(Near(guard.Hp,100)&&Near(guard.Shield,10),"shield takes first hit");
            shield.CastSkill(guard.X,guard.Y);
            check(Near(guard.Hp,70)&&Near(guard.Shield,0),"overflow damages HP exactly once");
            guard.Shield=10;guard.PoisonRemaining=1;guard.PoisonDps=600;
            shield.Tick(1f/60);
            check(Near(guard.Shield,0)&&Near(guard.Hp,70),"DOT uses same shield pipeline");
            guard.PoisonRemaining=0;guard.PoisonDps=0;shield.Tick(1);
            check(Near(guard.Shield,0),"broken shield never regenerates");

            var heal=Arena(source,"shaman");var shaman=heal.Enemies[0];
            var a=Add(heal,9001,shaman,.01f,100,50);var b=Add(heal,9002,shaman,.02f,200,20);
            shaman.AbilityRemaining=0;shaman.Hp*=.2f;
            heal.Tick(1f/60);
            check(Near(a.Hp,50)&&Near(b.Hp,36),"heals lowest proportion ally, not healer itself");
            float castHp=b.Hp;heal.Tick(1);check(Near(b.Hp,castHp),"heal obeys cooldown");
            shaman.RootRemaining=1;shaman.AbilityRemaining=0;heal.Tick(.5f);
            check(Near(b.Hp,castHp),"root prevents ready healer casting");
            shaman.RootRemaining=0;b.Hp=199;a.Hp=100;heal.Tick(1f/60);
            check(Near(b.Hp,200),"healing clamps at max HP");
            shaman.AbilityRemaining=0;b.Hp=0;heal.Tick(1f/60);
            check(!heal.Enemies.Contains(b),"cannot resurrect a dead ally");
            a.Hp=20;a.X=1;a.Y=1;shaman.AbilityRemaining=0;heal.Tick(1f/60);
            check(Near(a.Hp,20),"heal respects radius");

            var split=Arena(source,"brood");var mother=split.Enemies[0];float parentHp=mother.MaxHp;
            mother.Progress=.5f;int money=split.Coins;mother.Hp=0;split.Tick(1f/60);
            check(split.Enemies.Count==2&&split.Enemies.TrueForAll(e=>e.Species=="bud"),"death creates two actual child units");
            check(split.Coins==money+mother.Reward&&split.Kills==1,"mother pays exactly once");
            foreach(var child in split.Enemies)
                check(Near(child.MaxHp,parentHp*.28f)&&child.Generation==1&&child.Reward==0
                    &&child.Progress>=.48f&&child.Progress<.51f,"child HP/progress/generation/reward constraints");
            check(split.Stage==RunStage.Running,"children block premature wave clear");
            foreach(var child in split.Enemies) child.Hp=0;
            split.Tick(1f/60);
            check(split.Enemies.Count==0&&split.Kills==3&&split.Stage==RunStage.Preparing,"children do not recurse; clear only after all die");
            check(split.Coins==money+mother.Reward+split.Config.clearReward,"no child gold farming");
            split.Tick(1);check(split.Kills==3,"no repeated death settlement");
            var splash=Arena(source,"brood");var parent=splash.Enemies[0];
            splash.Path=new List<V2>{new V2(0,0),new V2(1,0)};
            splash.Pads=new List<V2>{new V2(0,0)};
            parent.Hp=1; parent.Progress=0;parent.X=parent.Y=0;
            splash.Towers.Add(new Pet{Uid=99,Species="fox",Level=1,Pad=0,ActiveRemaining=100});
            splash.Tick(1f/60);
            check(splash.Enemies.Count==2&&splash.Enemies.TrueForAll(e=>Near(e.Hp,e.MaxHp)),"one attack snapshots victims before death; no phantom hit on new children");
            var leak=Arena(source,"brood");leak.Enemies[0].Progress=TrailGeometry.Length(leak.Path);leak.Tick(1f/60);
            check(leak.Enemies.Count==0&&leak.Kills==0&&leak.Lives==source.initialLives-2,"leaked mother never splits");

            var rage=Arena(source,"bear");var bear=rage.Enemies[0];bear.Speed=.1f;
            bear.Hp=bear.MaxHp*.45f;rage.Tick(1f/60);
            check(bear.Enraged&&Near(bear.Speed,.14f),"rage threshold multiplies speed once");
            bear.Hp=bear.MaxHp;rage.Tick(1);bear.Hp=1;rage.Tick(1f/60);
            check(Near(bear.Speed,.14f),"healing cannot reset and repeatedly multiply rage");

            var frozen=Arena(source,"shaman");var mage=frozen.Enemies[0];mage.AbilityRemaining=1;mage.PulseRemaining=.5f;
            float time=frozen.Elapsed;frozen.Paused=true;frozen.Tick(30);
            check(Near(mage.AbilityRemaining,1)&&Near(mage.PulseRemaining,.5f)&&Near(frozen.Elapsed,time),"pause freezes enemy ability and visual clocks");
            frozen.Paused=false;frozen.Stage=RunStage.Preparing;frozen.Tick(30);
            check(Near(mage.AbilityRemaining,1),"preparing freezes enemy clocks");

            var ca=Arena(source,"shaman");var cb=Arena(source,"shaman");
            foreach(var m in new[]{ca,cb}){Add(m,99,m.Enemies[0],.01f,100,10);m.Enemies[0].AbilityRemaining=1;}
            for(int i=0;i<8;i++)ca.Tick(.25f);for(int i=0;i<32;i++)cb.Tick(.0625f);
            check(Near(ca.Enemies[1].Hp,cb.Enemies[1].Hp)&&Near(ca.Enemies[0].AbilityRemaining,cb.Enemies[0].AbilityRemaining),"fixed-step ability determinism");
            Action<Action<GameConfig>> invalid=mutate=>{var c=(GameConfig)Clone(source);mutate(c);bool threw=false;try{new GameModel(c,0,new[]{c.pets[0].id},1);}catch(ArgumentException){threw=true;}check(threw,"reject invalid enemy configuration");};
            invalid(c=>c.enemyTypes[0].hpMultiplier=float.NaN);
            invalid(c=>c.enemyTypes[7].splitId="brood");
            invalid(c=>c.enemyTypes[8].reward=1);
            invalid(c=>c.enemyTypes[6].abilityInterval=0);
            invalid(c=>c.levels[0].enemyWaves[0].roster[0]="unknown");
            invalid(c=>c.levels[0].bossId="moss");
            return "Enemy roster PASS: "+checks+" assertions; wave-preview/spawn parity, 12 silhouettes, shield, heal, split, rage, pause, determinism, invalid-data guards.\n";
        }
        static bool Near(float a,float b){return Math.Abs(a-b)<.003f;}
        static GameModel Arena(GameConfig source,string id)
        {
            var c=(GameConfig)Clone(source);var level=c.levels[0];level.waves=2;level.baseCount=1;level.countGrowth=0;
            level.enemyWaves=new[]{new EnemyWave{firstWave=1,roster=new[]{id}}};level.bossId=null;
            var m=new GameModel(c,0,new[]{c.pets[0].id},7);m.Pool.Clear();m.StartWave();m.Tick(1f/60);m.Enemies[0].Speed=0;
            return m;
        }
        static Enemy Add(GameModel m,int id,Enemy origin,float offset,float max,float hp)
        {
            var e=new Enemy{Id=id,X=origin.X+offset,Y=origin.Y,Progress=origin.Progress+offset*1.6f,
                Hp=hp,MaxHp=max,Speed=0,Leak=1};m.Enemies.Add(e);return e;
        }
        static object Clone(object value)
        {
            if(value==null||value is string||value.GetType().IsValueType)return value;
            if(value is Array){var a=(Array)value;var copy=Array.CreateInstance(value.GetType().GetElementType(),a.Length);for(int i=0;i<a.Length;i++)copy.SetValue(Clone(a.GetValue(i)),i);return copy;}
            var result=Activator.CreateInstance(value.GetType());foreach(var f in value.GetType().GetFields())f.SetValue(result,Clone(f.GetValue(value)));return result;
        }
    }
}
