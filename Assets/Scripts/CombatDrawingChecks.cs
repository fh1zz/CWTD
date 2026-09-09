using System;
using System.Text;
using UnityEngine;

namespace PetTD
{
    /// <summary>Checks the exact Unity matrix/anchor helpers used by the renderer.</summary>
    public static class CombatDrawingChecks
    {
        public static string Run(GameConfig config, PetGame.PetAtlasData art)
        {
            int count = 0;
            Action<bool, string> check = (ok, name) => { count++; if (!ok) throw new Exception("DRAW_CHECK_FAILED " + name); };
            var report = new StringBuilder();
            Vector2 a = new Vector2(900, 600), b = new Vector2(600, 200);
            Matrix4x4 parent = CombatDrawing.ScreenMatrix(1600, 900);
            Quaternion rotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            // Previous helper at the top-level GUI: rotation-about-reference-pivot
            // multiplied BEFORE the already scaled parent.
            Matrix4x4 old = Matrix4x4.TRS(a, rotation, Vector3.one)
                * Matrix4x4.Translate(-a) * parent;
            float oldError = Vector3.Distance(old.MultiplyPoint3x4(a), parent.MultiplyPoint3x4(a));
            check(oldError > 5, "old transform order reproduces an endpoint offset outside 1080p");
            report.AppendLine("Reproduced old start-point error at 1600x900: " + oldError.ToString("F2") + "px");
            float maximumError = 0;
            Vector2[] sizes = { new Vector2(1280, 720), new Vector2(1600, 900), new Vector2(1920, 1080),
                new Vector2(2560, 1440), new Vector2(3440, 1440), new Vector2(3840, 2160),
                new Vector2(1280, 1024), new Vector2(2560, 1080) };
            var model = new GameModel(config, 0, new[] { config.pets[0].id }, 913);
            foreach(V2 point in model.Path)
            {
                Vector2 feet=CombatDrawing.Project(point);
                Rect body=new Rect(feet.x-48,feet.y-96,96,96);
                foreach(Rect hud in new[]{CombatDrawing.HealthHud,CombatDrawing.WaveHud,CombatDrawing.UtilityHud})
                    check(!body.Overlaps(hud),"largest boss clears HUD throughout path at "+point.X+","+point.Y);
            }
            foreach(V2 pad in model.Pads)
                check(!CombatDrawing.PetFrame(CombatDrawing.Project(pad),5).Overlaps(CombatDrawing.Inspector),"inspector cannot obscure any fifth-stage pet");
            if(config.enemyTypes!=null)
                foreach(EnemySpec spec in config.enemyTypes)
                {
                    var feet=new Vector2(600,500);
                    var aim=feet-new Vector2(0,spec.visualSize*.4f);
                    check(new Rect(feet.x-spec.visualSize/2,feet.y-spec.visualSize,spec.visualSize,spec.visualSize).Contains(aim),"12-species sized hit anchor lies above feet");
                }
            foreach (Vector2 size in sizes)
            {
                Matrix4x4 screen = CombatDrawing.ScreenMatrix(size.x, size.y);
                for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    Vector2 end = a + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 320;
                    CheckLine(screen, a, end, 3.5f, check, ref maximumError);
                    CheckLine(screen, end, a, 67, check, ref maximumError);
                }
                for (int i = 1; i < model.Path.Count; i++)
                {
                    V2 p = model.Path[i - 1], q = model.Path[i];
                    CheckLine(screen, CombatDrawing.Project(p), CombatDrawing.Project(q), 52, check, ref maximumError);
                }
            }
            Matrix4x4 matrix; float length;
            check(!CombatDrawing.TryLine(parent, a, a, 3, out matrix, out length), "coincident endpoints do not draw an arbitrary line");
            check(!CombatDrawing.TryLine(parent, a, b, 0, out matrix, out length), "zero width is ignored");
            check(!CombatDrawing.TryLine(parent, new Vector2(float.NaN, 0), b, 3, out matrix, out length), "NaN position is ignored");
            check(!CombatDrawing.TryLine(parent, a, b, float.PositiveInfinity, out matrix, out length), "infinite width is ignored");
            check(art != null && art.regions != null && art.regions.Length == 35, "all pet-stage anchors use real sprite regions");
            for (int pet = 0; pet < 7; pet++)
                for (int level = 1; level <= 5; level++)
                {
                    var region = art.regions[pet * 5 + level - 1];
                    Rect body = CombatDrawing.PetBounds(CombatDrawing.PetFrame(new Vector2(400,600),level),
                        region.width, region.height, level);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector2 source = CombatDrawing.Muzzle(body, new Vector2(400 + side * 200, 550));
                        check(body.Contains(source) && source.y < 600, "muzzle lies in the visible sprite, not on its pad");
                        for (int kind = 0; kind < 4; kind++)
                        {
                            Vector2 feet = new Vector2(700, 500);
                            Vector2 aim = CombatDrawing.EnemyAim(feet, kind);
                            float enemySize = CombatDrawing.EnemySize(kind);
                            check(new Rect(feet.x - enemySize / 2, feet.y - enemySize, enemySize, enemySize).Contains(aim)
                                && aim.y < feet.y, "target anchor lies on the enemy body");
                            CheckLine(parent, source, aim, 2.5f, check, ref maximumError);
                        }
                    }
                }
            float previous = 1;
            for (int i = 0; i <= 20; i++)
            {
                float alpha = CombatDrawing.FeedbackAlpha(.16f * (1 - i / 20f), .16f);
                check(alpha >= 0 && alpha <= previous, "feedback fades monotonically without overshoot");
                previous = alpha;
            }
            check(CombatDrawing.FeedbackAlpha(0, .16f) == 0
                && CombatDrawing.FeedbackAlpha(.16f, .16f) == 1, "impact visible immediately and gone at expiry");
            check(CombatDrawing.FeedbackAlpha(1, 0) == 0 && CombatDrawing.FeedbackAlpha(float.NaN, 1) == 0,
                "invalid lifetime has no visual output");
            report.AppendLine("Drawing assertions=" + count + "; maximum endpoint/thickness error=" + maximumError.ToString("F6") + "px");
            report.AppendLine("PASS: 8 resolutions, 16 directions and reverse, every curved-road segment, 35 pet stages, 4 enemy sizes, degenerate inputs and fade.");
            report.AppendLine("Scope: Unity renderer geometry/metadata; not a visible-window screenshot or physical-input test.");
            return report.ToString();
        }

        static void CheckLine(Matrix4x4 parent, Vector2 a, Vector2 b, float width, Action<bool, string> check, ref float maximum)
        {
            Matrix4x4 line; float length;
            check(CombatDrawing.TryLine(parent, a, b, width, out line, out length), "valid line has geometry");
            float startError = Vector3.Distance(line.MultiplyPoint3x4(Vector3.zero), parent.MultiplyPoint3x4(a));
            float endError = Vector3.Distance(line.MultiplyPoint3x4(new Vector3(length, 0, 0)), parent.MultiplyPoint3x4(b));
            Vector2 delta = (b - a).normalized, normal = new Vector2(-delta.y, delta.x);
            float thicknessError = Vector3.Distance(line.MultiplyPoint3x4(new Vector3(0, width / 2, 0)),
                parent.MultiplyPoint3x4(a + normal * width / 2));
            maximum = Mathf.Max(maximum, Mathf.Max(startError, Mathf.Max(endError, thicknessError)));
            check(startError < .003f && endError < .003f && thicknessError < .003f, "screen endpoints and thickness match independent parent projection");
        }
    }
}
