using UnityEngine;

namespace PetTD
{
    /// <summary>Presentation geometry, in 1920x1080 reference coordinates.</summary>
    public static class CombatDrawing
    {
        public static Matrix4x4 ScreenMatrix(float width, float height)
        {
            float scale = Mathf.Min(width / 1920f, height / 1080f);
            return Matrix4x4.TRS(new Vector3((width - 1920 * scale) / 2,
                (height - 1080 * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        }

        public static bool TryLine(Matrix4x4 parent, Vector2 a, Vector2 b, float width,
            out Matrix4x4 transform, out float length)
        {
            transform = parent;
            length = 0;
            if (!Finite(a.x) || !Finite(a.y) || !Finite(b.x) || !Finite(b.y) || !Finite(width) || width <= 0)
                return false;
            length = Vector2.Distance(a, b);
            if (!Finite(length) || length <= .001f) return false;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            // Local origin -> local rotation -> parent screen transform. Do not
            // rotate a previously scaled GUI around an unscaled screen pivot.
            transform = parent * Matrix4x4.TRS(a, Quaternion.Euler(0, 0, angle), Vector3.one);
            return true;
        }

        // One artwork covers the entire reference canvas. Crop only unused grass
        // below 1080; preserve the same 1.6:1 world metric, never tile two maps.
        public static Rect Battlefield { get { return new Rect(0, 0, 1920, 1200); } }
        public static Rect InteractionField { get { return new Rect(0,120,1920,747); } }
        public static Rect Inspector { get { return new Rect(38,110,412,235); } }
        public static Rect HealthHud { get { return new Rect(35,25,355,78); } }
        public static Rect WaveHud { get { return new Rect(430,25,280,78); } }
        public static Rect UtilityHud { get { return new Rect(1230,38,622,63); } }
        public static Vector2 Project(V2 p) { Rect f=Battlefield; return new Vector2(f.x+p.X*f.width,f.y+p.Y*f.height); }
        public static float EnemySize(int kind) { return kind == 3 ? 78 : kind == 2 ? 54 : kind == 1 ? 45 : 39; }
        public static Rect PetFrame(Vector2 feet, int level)
        {
            float size=66+(Mathf.Clamp(level,1,5)-1)*6;
            return new Rect(feet.x-size/2,feet.y-size,size,size);
        }
        public static Vector2 EnemyAim(Vector2 feet, int kind)
        {
            // IMGUI Y increases downwards; Unity's Vector2.up would aim below feet.
            return feet - new Vector2(0, EnemySize(kind) * .4f);
        }
        public static Rect PetBounds(Rect frame, float sourceWidth, float sourceHeight, int level)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0) return frame;
            float scale = Mathf.Min(frame.width / sourceWidth, frame.height / sourceHeight)
                * (.70f + .075f * (Mathf.Clamp(level, 1, 5) - 1));
            return new Rect(frame.center.x - sourceWidth * scale / 2,
                frame.yMax - sourceHeight * scale, sourceWidth * scale, sourceHeight * scale);
        }
        public static Vector2 Muzzle(Rect body, Vector2 target)
        {
            return body.center + new Vector2((target.x < body.center.x ? -1 : 1) * body.width * .18f, 0);
        }
        public static float FeedbackAlpha(float life, float duration)
        {
            if (!Finite(life) || !Finite(duration) || duration <= 0) return 0;
            float remaining = Mathf.Clamp01(life / duration);
            return remaining * remaining;
        }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
