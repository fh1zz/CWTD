using System;
using System.Collections.Generic;

namespace PetTD
{
    /// <summary>One metric and one sampled centreline for movement, roads and route hints.</summary>
    public static class TrailGeometry
    {
        public const float Aspect = 1.6f;
        public const float CornerTrim = .08f;
        public const int CornerSteps = 12;

        public static float Distance(V2 a, V2 b)
        {
            float x = (b.X - a.X) * Aspect, y = b.Y - a.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        // Traced from forest-battlefield-v04.png, top-left normalized image coordinates.
        public static List<V2> ForestTrail()
        {
            V2[] knots = {
                new V2(0,.463f), new V2(.08f,.468f), new V2(.12f,.51f),
                new V2(.155f,.583f), new V2(.206f,.612f), new V2(.25f,.605f),
                new V2(.29f,.545f), new V2(.32f,.506f), new V2(.385f,.439f),
                new V2(.40f,.38f), new V2(.392f,.328f), new V2(.368f,.257f),
                new V2(.373f,.205f), new V2(.414f,.151f), new V2(.475f,.13f),
                new V2(.531f,.157f), new V2(.582f,.215f), new V2(.606f,.295f),
                new V2(.611f,.373f), new V2(.64f,.44f), new V2(.702f,.476f),
                new V2(.745f,.53f), new V2(.787f,.594f), new V2(.849f,.611f),
                new V2(.899f,.58f), new V2(.94f,.509f), new V2(.968f,.466f),
                new V2(1,.445f)
            };
            var result = new List<V2> { knots[0] };
            for (int i = 0; i < knots.Length - 1; i++)
            {
                V2 b = knots[i], c = knots[i + 1];
                V2 a = i > 0 ? knots[i-1] : Lerp(c,b,2);
                V2 d = i+2 < knots.Length ? knots[i+2] : Lerp(b,c,2);
                for (int j=1;j<=6;j++)
                {
                    float t=j/6f, t2=t*t, t3=t2*t;
                    Append(result,new V2(
                        .5f*((2*b.X)+(-a.X+c.X)*t+(2*a.X-5*b.X+4*c.X-d.X)*t2+(-a.X+3*b.X-3*c.X+d.X)*t3),
                        .5f*((2*b.Y)+(-a.Y+c.Y)*t+(2*a.Y-5*b.Y+4*c.Y-d.Y)*t2+(-a.Y+3*b.Y-3*c.Y+d.Y)*t3)));
                }
            }
            return result;
        }

        public static List<V2> ForestPads()
        {
            return new List<V2> {
                new V2(.10f,.365f), new V2(.16f,.70f), new V2(.235f,.45f),
                new V2(.32f,.66f), new V2(.28f,.295f), new V2(.465f,.33f),
                new V2(.50f,.24f), new V2(.535f,.43f), new V2(.535f,.585f),
                new V2(.68f,.265f), new V2(.75f,.38f), new V2(.775f,.71f),
                new V2(.865f,.465f), new V2(.95f,.65f)
            };
        }

        // Trim in world-distance units, not normalized X/Y. Quadratic bends stay
        // inside the authored corner and join adjacent straights tangentially.
        public static List<V2> RoundCorners(IList<V2> corners, float trim, int steps)
        {
            if (corners == null || corners.Count < 2 || float.IsNaN(trim)
                || float.IsInfinity(trim) || trim < 0 || steps < 2)
                throw new ArgumentException("Invalid trail geometry.");
            var clean = new List<V2>();
            foreach (V2 point in corners)
            {
                if (float.IsNaN(point.X) || float.IsNaN(point.Y)
                    || float.IsInfinity(point.X) || float.IsInfinity(point.Y))
                    throw new ArgumentException("Trail coordinates must be finite.");
                Append(clean, point);
            }
            if (clean.Count < 2) throw new ArgumentException("Trail must have length.");
            var result = new List<V2> { clean[0] };
            for (int i = 1; i < clean.Count - 1; i++)
            {
                V2 a = clean[i - 1], b = clean[i], c = clean[i + 1];
                float incoming = Distance(a, b), outgoing = Distance(b, c);
                float amount = Math.Min(trim, Math.Min(incoming, outgoing) * .45f);
                V2 enter = Lerp(b, a, amount / incoming), leave = Lerp(b, c, amount / outgoing);
                Append(result, enter);
                for (int j = 1; j <= steps; j++)
                {
                    float t = j / (float)steps;
                    Append(result, Lerp(Lerp(enter, b, t), Lerp(b, leave, t), t));
                }
            }
            Append(result, clean[clean.Count - 1]);
            return result;
        }

        public static float Length(IList<V2> path)
        {
            float length = 0;
            for (int i = 1; i < path.Count; i++) length += Distance(path[i - 1], path[i]);
            return length;
        }

        // Progress is arc distance, never a Bezier parameter: turns do not change
        // speed. Read the supplied path directly so custom/test routes cannot use
        // stale cached segments. True means the enemy has reached the exit.
        public static bool Sample(IList<V2> path, float progress, out V2 point, out V2 direction)
        {
            if (path == null || path.Count < 2) throw new ArgumentException("Missing trail.");
            progress = Math.Max(0, progress);
            direction = new V2(1f / Aspect, 0);
            float traversed = 0;
            for (int i = 1; i < path.Count; i++)
            {
                V2 a = path[i - 1], b = path[i];
                float length = Distance(a, b);
                if (length <= .000001f) continue;
                direction = new V2((b.X - a.X) / length, (b.Y - a.Y) / length);
                float end = traversed + length;
                if (progress < end)
                {
                    point = Lerp(a, b, (progress - traversed) / length);
                    return false;
                }
                traversed = end;
            }
            point = path[path.Count - 1];
            return true;
        }

        static V2 Lerp(V2 a, V2 b, float t)
        {
            return new V2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        static void Append(List<V2> points, V2 point)
        {
            if (points.Count == 0 || Distance(points[points.Count - 1], point) > .000001f)
                points.Add(point);
        }
    }
}
