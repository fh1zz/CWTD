using System;
using System.Collections.Generic;
using System.Text;

namespace PetTD
{
    public static class TrailChecks
    {
        public static string Run(GameConfig config) { return new Suite(config).Run(); }

        sealed class Suite
        {
            readonly GameConfig config;
            readonly StringBuilder report = new StringBuilder();
            int assertions, groups;
            public Suite(GameConfig config) { this.config = config; }
            void Assert(bool ok, string reason)
            {
                assertions++;
                if (!ok) throw new Exception("TRAIL_CHECK_FAILED: " + reason);
            }
            void Check(string name, Action action) { action(); groups++; report.AppendLine("PASS trail: " + name); }
            GameModel Model()
            {
                return new GameModel(config, 0, new[] { config.pets[0].id }, 713);
            }
            Enemy Unit(GameModel model, float speed)
            {
                model.Stage = RunStage.Running;
                var enemy = new Enemy { Id = 800, Hp = 10000, MaxHp = 10000, Speed = speed,
                    X = model.Path[0].X, Y = model.Path[0].Y, Leak = 2 };
                model.Enemies.Add(enemy);
                return enemy;
            }
            public string Run()
            {
                Check("continuous bounded bends, no crossings or zero-length segments", Geometry);
                Check("pad clearance and useful first-stage road coverage", Pads);
                Check("arc-distance sampling and exact entry/exit boundaries", Sampling);
                Check("short/duplicate corners, custom routes and deterministic generation", EdgeCases);
                Check("all enemy speeds follow road, pause/root freeze, leak once", Movement);
                report.AppendLine("Trail groups=" + groups + " assertions=" + assertions);
                return "\n" + report;
            }
            void Geometry()
            {
                GameModel model = Model();
                List<V2> path = model.Path;
                float length = TrailGeometry.Length(path), maxTurn = 0;
                Assert(path.Count == 163, "28 image-traced knots, six samples per spline span");
                Assert(length > 2.308f && length < 2.310f, "image-traced travel distance");
                for (int i = 0; i < path.Count; i++)
                {
                    V2 p = path[i];
                    Assert(p.X >= 0 && p.X <= 1 && p.Y >= 0 && p.Y <= 1, "bounded route");
                    if (i > 0) Assert(model.Distance(path[i - 1], p) > .000001f, "nonzero segment");
                    if (i > 0 && i < path.Count - 1)
                    {
                        V2 a = path[i - 1], b = path[i + 1];
                        double dx1 = (p.X - a.X) * TrailGeometry.Aspect, dy1 = p.Y - a.Y;
                        double dx2 = (b.X - p.X) * TrailGeometry.Aspect, dy2 = b.Y - p.Y;
                        double dot = (dx1 * dx2 + dy1 * dy2) / Math.Sqrt((dx1 * dx1 + dy1 * dy1) * (dx2 * dx2 + dy2 * dy2));
                        maxTurn = Math.Max(maxTurn, (float)(Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180 / Math.PI));
                    }
                    for (int j = i + 2; i > 0 && j < path.Count; j++)
                        Assert(!Crosses(path[i - 1], p, path[j - 1], path[j]), "no self-intersection");
                }
                Assert(maxTurn < 16, "smooth organic road without abrupt turns");
                report.AppendLine("Trail length=" + length.ToString("F4") + " max tangent step=" + maxTurn.ToString("F2") + "deg; base ordinary traversal=" + (length / (config.enemyBaseSpeed > 0 ? config.enemyBaseSpeed : .085f)).ToString("F2") + "s before level speedScale");
            }
            void Pads()
            {
                GameModel model = Model();
                double nearest = 1;
                foreach (V2 pad in model.Pads)
                {
                    double distance = double.MaxValue;
                    for (int i = 1; i < model.Path.Count; i++)
                        distance = Math.Min(distance, SegmentDistance(pad, model.Path[i - 1], model.Path[i]));
                    nearest = Math.Min(nearest, distance);
                    // Painted half-road ~45px + 24px footprint + 6px ground separation.
                    Assert(distance * 940 > 75, "pad footprint remains outside painted road");
                    foreach (PetSpec spec in config.pets)
                    {
                        float range = model.Range(new Pet { Species = spec.id, Level = 1 });
                        Assert(distance + .015 < range, "every first-stage species can reach road from each pad");
                    }
                }
                report.AppendLine("Minimum pad-centre clearance=" + (nearest * 940).ToString("F1") + "px at reference resolution");
            }
            void Sampling()
            {
                GameModel model = Model();
                V2 point, direction;
                Assert(!TrailGeometry.Sample(model.Path, -1, out point, out direction)
                    && model.Distance(point, model.Path[0]) < .000001, "negative progress clamps to entrance");
                float length = TrailGeometry.Length(model.Path);
                Assert(!TrailGeometry.Sample(model.Path, length - .0001f, out point, out direction), "just before exit stays alive");
                Assert(TrailGeometry.Sample(model.Path, length, out point, out direction)
                    && model.Distance(point, model.Path[model.Path.Count - 1]) < .000001, "exact length reaches exit");
                Assert(TrailGeometry.Sample(model.Path, length + 100, out point, out direction), "large overshoot stops at exit");
                for (int n = 0; n < 500; n++)
                {
                    float progress = length * n / 500;
                    TrailGeometry.Sample(model.Path, progress, out point, out direction);
                    double nearest = 100;
                    for (int i = 1; i < model.Path.Count; i++)
                        nearest = Math.Min(nearest, SegmentDistance(point, model.Path[i - 1], model.Path[i]));
                    Assert(nearest < .000002, "sample exactly on rendered centreline");
                    Assert(Math.Abs(model.Distance(new V2(0, 0), direction) - 1) < .00001, "metric-unit tangent");
                }
            }
            void EdgeCases()
            {
                V2 point, direction;
                var corners = new[] { new V2(0, 0), new V2(0, 0), new V2(.002f, 0), new V2(.002f, .002f), new V2(1, .002f) };
                List<V2> small = TrailGeometry.RoundCorners(corners, .08f, 12);
                for (int i = 1; i < small.Count; i++)
                    Assert(TrailGeometry.Distance(small[i - 1], small[i]) > .000001, "short corners are clamped without degenerate points");
                var custom = new List<V2> { new V2(0, 0), new V2(0, 0), new V2(1, 0) };
                TrailGeometry.Sample(custom, .8f, out point, out direction);
                Assert(Math.Abs(point.X - .5f) < .000001, "duplicate waypoint skipped");
                custom[2] = new V2(0, 1);
                TrailGeometry.Sample(custom, .8f, out point, out direction);
                Assert(point.X == 0 && Math.Abs(point.Y - .8f) < .000001, "edited route does not reuse stale cache");
                List<V2> a = TrailGeometry.ForestTrail(), b = TrailGeometry.ForestTrail();
                for (int i = 0; i < a.Count; i++) Assert(a[i].X == b[i].X && a[i].Y == b[i].Y, "deterministic route");
                a[0] = new V2(.5f, .5f);
                Assert(b[0].X == 0, "models own independent route instances");
            }
            void Movement()
            {
                foreach (float factor in new[] { 1f, 1.65f, .78f, .65f })
                {
                    GameModel model = Model();
                    Enemy enemy = Unit(model, .085f * factor);
                    float length = TrailGeometry.Length(model.Path), maxError = 0, maxStepError = 0;
                    int guard = 0, initialLives = model.Lives;
                    while (model.Enemies.Count > 0 && guard++ < 6000)
                    {
                        float before = enemy.Progress;
                        model.Tick(1f / 60);
                        V2 point, direction;
                        TrailGeometry.Sample(model.Path, enemy.Progress, out point, out direction);
                        maxError = Math.Max(maxError, model.Distance(point, new V2(enemy.X, enemy.Y)));
                        maxStepError = Math.Max(maxStepError, Math.Abs(enemy.Progress - before - enemy.Speed / 60));
                    }
                    Assert(guard < 6000 && maxError < .000002, "whole traversal exactly follows display path");
                    Assert(maxStepError < .000002, "arc speed constant on straights and curves");
                    Assert(Math.Abs(guard / 60f - length / enemy.Speed) < .03, "arrival matches arc travel time");
                    Assert(model.Lives == initialLives - 2, "arrival deducts one leak");
                    model.Tick(2);
                    Assert(model.Lives == initialLives - 2, "settled enemy never leaks twice");
                }
                GameModel frozen = Model();
                Enemy held = Unit(frozen, .085f);
                held.Progress = .32f;
                frozen.Tick(1f / 60);
                frozen.Paused = true;
                float beforePause = held.Progress, x = held.X, y = held.Y;
                frozen.Tick(2);
                Assert(held.Progress == beforePause && held.X == x && held.Y == y, "pause freezes on bend");
                frozen.Paused = false;
                held.RootRemaining = .5f;
                frozen.Tick(.5f);
                Assert(Math.Abs(held.Progress - beforePause) < .000001, "root freezes movement on bend");
                frozen.Tick(.5f);
                Assert(Math.Abs(held.Progress - beforePause - .0425f) < .00001, "resume retains progress without snapping");
                GameModel a = Model(), b = Model();
                Enemy one = Unit(a, .14f), two = Unit(b, .14f);
                for (int i = 0; i < 20; i++) a.Tick(.5f);
                for (int i = 0; i < 320; i++) b.Tick(.03125f);
                Assert(one.Progress == two.Progress && one.X == two.X && one.Y == two.Y, "low-FPS time slicing agrees through bends");
            }
            static double SegmentDistance(V2 p, V2 a, V2 b)
            {
                double dx = (b.X - a.X) * TrailGeometry.Aspect, dy = b.Y - a.Y;
                double px = (p.X - a.X) * TrailGeometry.Aspect, py = p.Y - a.Y;
                double t = Math.Max(0, Math.Min(1, (px * dx + py * dy) / (dx * dx + dy * dy)));
                return Math.Sqrt((px - t * dx) * (px - t * dx) + (py - t * dy) * (py - t * dy));
            }
            static double Side(V2 a, V2 b, V2 p) { return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X); }
            static bool Crosses(V2 a, V2 b, V2 c, V2 d)
            {
                return Side(a, b, c) * Side(a, b, d) < -1e-12 && Side(c, d, a) * Side(c, d, b) < -1e-12;
            }
        }
    }
}
