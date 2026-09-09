using System;
using System.Collections.Generic;
using System.Text;

namespace PetTD
{
    public static class CombatFxChecks
    {
        public static string Run(GameConfig config) { return new Suite(config).Run(); }
        sealed class Suite
        {
            readonly GameConfig config;
            readonly StringBuilder report = new StringBuilder();
            int count, groups;
            public Suite(GameConfig config) { this.config = config; }
            void Assert(bool ok, string name) { count++; if (!ok) throw new Exception("FX_CHECK_FAILED " + name); }
            void Check(string name, Action body) { body(); groups++; report.AppendLine("PASS combat FX: " + name); }
            public string Run()
            {
                Check("seven pets, five stages, correct source/target snapshots and instant hit", Basics);
                Check("moving target ID, removal and frozen last location without retargeting", Tracking);
                Check("FX life, pause and visual events never deal additional damage", Lifecycle);
                if (config.pets[0].stages != null && config.pets[0].stages[0].active != null)
                    Check("automatic volley/assault track enemies; area/support remains on ground", Auto);
                Check("star rain has real ground radius and no phantom target", Star);
                return "\nCombat FX groups=" + groups + " assertions=" + count + "\n" + report;
            }
            GameModel Arena(string id, int level = 1)
            {
                var m = new GameModel(config, 0, new[] { id }, 17);
                m.Path = new List<V2> { new V2(0, 0), new V2(1, 0) };
                for (int i = 0; i < m.Pads.Count; i++) m.Pads[i] = new V2(.4f + i * .01f, 0);
                Assert(m.Deploy(m.Pool[0].Uid, 0), "test caster deployed");
                m.Towers[0].Level = level; m.Towers[0].ActiveRemaining = 9999;
                m.Stage = RunStage.Running;
                Add(m, 9900, 1.5f, 1000000);
                return m;
            }
            Enemy Add(GameModel m, int id, float progress = .65f, float hp = 100000)
            {
                var e = new Enemy { Id = id, Hp = hp, MaxHp = hp, Progress = progress,
                    X = progress / TrailGeometry.Aspect, Y = 0, Speed = .13f, Kind = 1, Leak = 1, Reward = 7 };
                m.Enemies.Add(e); return e;
            }
            static bool Same(float a, float b) { return Math.Abs(a - b) < .000002f; }
            void Basics()
            {
                foreach (PetSpec spec in config.pets) for (int level = 1; level <= 5; level++)
                {
                    GameModel m = Arena(spec.id, level);
                    Enemy e = Add(m, 10);
                    m.Tick(1f / 60);
                    CombatFx fx = m.Effects.Find(f => !f.IsAuto && f.TargetId == e.Id);
                    Assert(fx != null && fx.SourceLevel == level && fx.TargetKind == e.Kind
                        && fx.PetIndex == spec.atlasIndex, "attack snapshots the correct stage and enemy");
                    Assert(e.Hp < e.MaxHp && Same(fx.Life, fx.Duration), "damage already resolved on the first FX frame");
                    Assert(Same(fx.ToX, e.X) && Same(fx.ToY, e.Y), "feedback follows same-tick movement");
                    m.Towers[0].Level = level == 5 ? 1 : 5;
                    Assert(fx.SourceLevel == level, "later evolution does not move an existing muzzle");
                }
                GameModel lethal = Arena("falcon");
                Enemy victim = Add(lethal, 10, .65f, 1);
                lethal.Tick(1f / 60);
                CombatFx hit = lethal.Effects.Find(f => f.TargetId == victim.Id);
                Assert(!lethal.Enemies.Contains(victim) && hit != null
                    && Same(hit.ToX, .65f / TrailGeometry.Aspect), "lethal shot retains body anchor despite target removal");
            }
            void Tracking()
            {
                GameModel m = Arena("falcon");
                Enemy e = Add(m, 10);
                m.Tick(1f / 60);
                CombatFx fx = m.Effects.Find(f => f.TargetId == e.Id);
                m.Towers.Clear();
                float first = fx.ToX;
                m.Enemies.Reverse();
                m.Tick(1f / 60);
                Assert(fx.ToX > first && Same(fx.ToX, e.X), "lookup is by stable enemy ID, not list index");
                float lastX = fx.ToX, lastY = fx.ToY;
                e.Hp = 1;
                Assert(m.CastSkill(e.X, e.Y) && !m.Enemies.Contains(e), "target removed by actual lethal skill");
                Enemy replacement = Add(m, 88, .75f);
                m.Tick(1f / 60);
                Assert(fx.TargetId == 10 && Same(fx.ToX, lastX) && Same(fx.ToY, lastY)
                    && !Same(fx.ToX, replacement.X), "removed target freezes last point, never switches to another enemy");
            }
            void Lifecycle()
            {
                GameModel m = Arena("falcon");
                Enemy e = Add(m, 10);
                m.Tick(1f / 60);
                CombatFx fx = m.Effects.Find(f => f.TargetId == e.Id);
                m.Towers.Clear();
                m.Paused = true;
                float life = fx.Life, x = fx.ToX, hp = e.Hp;
                m.Tick(.5f);
                Assert(fx.Life == life && fx.ToX == x && e.Hp == hp, "pause freezes feedback and combat");
                m.Paused = false;
                m.Tick(1f / 60);
                Assert(fx.Life < life && fx.ToX > x, "resume advances existing feedback");
                int coins = m.Coins;
                m.Tick(.3f);
                Assert(!m.Effects.Contains(fx) && e.Hp == hp && m.Coins == coins, "feedback expiration never applies damage/reward again");
                m.Effects.Add(new CombatFx { PetIndex = 0, SourceLevel = 5, TargetId = e.Id, TargetKind = e.Kind,
                    Life = .1f, Duration = .1f });
                m.Tick(.2f);
                Assert(e.Hp == hp && m.Coins == coins, "injected visual-only event cannot change combat");
            }
            void Auto()
            {
                foreach (PetSpec spec in config.pets) foreach (int level in new[] { 1, 5 })
                {
                    GameModel m = Arena(spec.id, level);
                    Pet caster = m.Towers[0];
                    caster.Cooldown = 9999; caster.ActiveRemaining = 0;
                    if (spec.id == "deer") m.Towers.Add(new Pet { Uid = 700, Species = "fox", Level = 1,
                        Pad = 1, Cooldown = 9999, ActiveRemaining = 9999 });
                    Enemy target = Add(m, 10);
                    Add(m, 11, .66f); Add(m, 12, .67f); Add(m, 13, .68f);
                    m.Tick(1f / 60);
                    var effects = m.Effects.FindAll(f => f.IsAuto);
                    Assert(caster.AutoCasts == 1 && effects.Count > 0, "automatic skill emitted actual feedback");
                    string kind = m.AutoSkill(caster).effect;
                    foreach (CombatFx fx in effects)
                    {
                        Assert(fx.SourceLevel == level && fx.PetIndex == spec.atlasIndex, "automatic source snapshot");
                        if (kind == "volley" || kind == "assault")
                        {
                            Enemy tracked = m.Enemies.Find(e => e.Id == fx.TargetId);
                            Assert(fx.Radius == 0 && tracked != null && Same(fx.ToX, tracked.X), "targeted auto tracks its own victim");
                        }
                        else
                        {
                            Assert(fx.TargetId == 0 && fx.Radius > 0, "area/tempo has ground feedback, not a fake missile");
                            float x = fx.ToX, y = fx.ToY;
                            m.Tick(1f / 60);
                            Assert(fx.ToX == x && fx.ToY == y, "area centre is fixed despite moving enemies");
                        }
                    }
                    Assert(target.Hp <= target.MaxHp, "visual state cannot heal enemy");
                }
            }
            void Star()
            {
                GameModel m = Arena("falcon");
                m.Towers.Clear();
                Assert(m.CastSkill(.4f, .2f), "star accepted");
                CombatFx fx = m.Effects.Find(f => f.PetIndex < 0);
                Assert(fx != null && Same(fx.Radius, config.skillRadius)
                    && fx.TargetId == 0 && Same(fx.ToX, .4f) && Same(fx.ToY, .2f), "star feedback uses actual impact centre/radius");
            }
        }
    }
}
