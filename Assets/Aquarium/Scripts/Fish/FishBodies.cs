// Fish do not swim through each other. A body is a stick from the nose to the tip of the tail with
// an elliptic cross-section (tall and flat for an angelfish, round for a goldfish) that tapers towards
// both ends. Each frame, after the fish have moved, bodies that overlap are pushed apart, the light one
// further than the heavy one, and no fish keeps a speed that would carry it into another by the next
// frame. A fish can be thinner than its step at full speed and skip right over a body between two
// frames, overlapping in neither: so the way it came is looked along too, and if the bodies met there
// it goes back to where they did. Steering sees to it that this
// rarely has to happen (Fish.Steer: a course that leads into another fish turns aside in good time).
// The same goes for the shrimp, the snails and the crab, except that the fish does all the giving
// way: a critter holds on to its surface.
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    public static class FishBodies
    {
        /// <summary>For the tools: off leaves the fish to the soft steering alone, to measure the difference.</summary>
        public static bool On = true;

        /// <summary>For the tools: how far the fish have been pushed in all.</summary>
        public static float Pushed;

        const float Nose = 0.5f, Tail = 0.65f;  // from the middle of the fish, in shares of its length
        const float Room = 0.012f;              // what is left between two bodies
        const float MaxSpeed = 6;               // units/s: a deep overlap (a fish just born) comes undone, it does not jump
        const float LeastShare = 0.1f;          // even the heaviest fish gives way this much: the other may be against a wall
        const int Passes = 2;                   // one push can bring a body into a third
        const float Thin = 0.03f;               // a step along the way of a fish, about the thinnest body there is
        const int MostSteps = 6;
        const float Jump = 0.5f;                // moved further than this in a frame it was put there, it did not swim

        // how thick the body is along its length (of the thickest), the nose first
        static readonly (float at, float k)[] Shape =
        {
            (0.5f, 0.2f), (0.3f, 0.75f), (0.12f, 0.95f), (-0.08f, 0.9f), (-0.25f, 0.65f), (-0.47f, 0.3f), (-0.65f, 0.2f),
        };

        static float Thick(float at)
        {
            for (int i = 1; i < Shape.Length; i++)
                if (at >= Shape[i].at)
                    return Mathf.Lerp(Shape[i - 1].k, Shape[i].k, (Shape[i - 1].at - at) / (Shape[i - 1].at - Shape[i].at));
            return Shape[Shape.Length - 1].k;
        }

        /// <summary>The body's extent along a direction, at a point of its length.</summary>
        static float Radius(Fish f, Vector3 n, float at)
        {
            var side = Vector3.Cross(Vector3.up, f.heading).normalized;
            var up = Vector3.Cross(f.heading, side);
            float s = Vector3.Dot(n, side) * f.sp.width, u = Vector3.Dot(n, up) * f.sp.height;
            return 0.5f * f.scale * Thick(at) * Mathf.Sqrt(s * s + u * u);
        }

        /// <summary>The nearest points of two segments p + d * s, q + e * t, with s and t in 0..1.</summary>
        static void Nearest(Vector3 p, Vector3 d, Vector3 q, Vector3 e, out float s, out float t)
        {
            var r = p - q;
            float a = Vector3.Dot(d, d), c = Vector3.Dot(e, e), f = Vector3.Dot(e, r), g = Vector3.Dot(d, r), b = Vector3.Dot(d, e);
            if (c < 1e-8f) { t = 0; s = Mathf.Clamp01(-g / a); return; }   // a body as long as it is thick
            float denom = a * c - b * b;
            s = denom > 1e-8f ? Mathf.Clamp01((b * f - c * g) / denom) : 0.5f;
            t = (b * s + f) / c;
            if (t < 0) { t = 0; s = Mathf.Clamp01(-g / a); }
            else if (t > 1) { t = 1; s = Mathf.Clamp01((b - g) / a); }
        }

        /// <summary>The space between two bodies (negative: they overlap) and the direction from b to a at the nearest place.</summary>
        public static float Gap(Fish a, Fish b, out Vector3 n) => Gap(a, a.pos, b, b.pos, out n);

        static float Gap(Fish a, Vector3 atA, Fish b, Vector3 atB, out Vector3 n)
        {
            var pa = atA + a.heading * (Nose * a.size);
            var da = -a.heading * ((Nose + Tail) * a.size);
            var pb = atB + b.heading * (Nose * b.size);
            var db = -b.heading * ((Nose + Tail) * b.size);
            Nearest(pa, da, pb, db, out float s, out float t);
            var d = pa + da * s - (pb + db * t);
            float dist = d.magnitude;
            if (dist > 1e-5f) n = d / dist;
            else
            {
                // the two axes cross: apart across them
                n = Vector3.Cross(a.heading, b.heading);
                if (n.sqrMagnitude < 1e-6f) n = Vector3.up;
                n.Normalize();
            }
            return dist - Radius(a, n, Nose - (Nose + Tail) * s) - Radius(b, n, Nose - (Nose + Tail) * t);
        }

        /// <summary>The space between a fish and a critter (negative: they overlap) and the direction from the critter to the fish.</summary>
        public static float Gap(Fish f, Critter c, out Vector3 n) => Gap(f, f.pos, c, out n);

        static float Gap(Fish f, Vector3 at, Critter c, out Vector3 n)
        {
            var pa = at + f.heading * (Nose * f.size);
            var da = -f.heading * ((Nose + Tail) * f.size);
            c.BodyAxis(out var pb, out var db);
            Nearest(pa, da, pb, db, out float s, out float t);
            var d = pa + da * s - (pb + db * t);
            float dist = d.magnitude;
            n = dist > 1e-5f ? d / dist : Vector3.up;
            return dist - Radius(f, n, Nose - (Nose + Tail) * s) - c.Thick;
        }

        /// <summary>Further apart than this (from the middles of the fish) they cannot touch.</summary>
        static float Reach(Fish f) => Tail * f.size + 0.5f * f.scale * Mathf.Max(f.sp.height, f.sp.width);

        /// <summary>If the two met on their way here, between the frames: back to where they did.</summary>
        static void BackToTouch(Fish a, Fish b)
        {
            if (a.moved > Jump || b.moved > Jump) return;
            Vector3 ma = a.pos - a.before, mb = b.pos - b.before;
            float way = (ma - mb).magnitude;
            if (way < Thin) return;
            int steps = Mathf.Min(MostSteps, Mathf.CeilToInt(way / Thin));
            for (int k = 1; k < steps; k++)
            {
                float t = (float)k / steps;
                Vector3 atA = a.before + ma * t, atB = b.before + mb * t;
                if (Gap(a, atA, b, atB, out _) >= 0) continue;
                a.pos = atA;
                b.pos = atB;
                return;
            }
        }

        static void BackToTouch(Fish f, Critter c)
        {
            if (f.moved > Jump || f.moved < Thin) return;
            var m = f.pos - f.before;
            int steps = Mathf.Min(MostSteps, Mathf.CeilToInt(f.moved / Thin));
            for (int k = 1; k < steps; k++)
            {
                var at = f.before + m * ((float)k / steps);
                if (Gap(f, at, c, out _) >= 0) continue;
                f.pos = at;
                return;
            }
        }

        public static void Resolve(List<Fish> fish, List<Critter> critters, float dt)
        {
            if (!On) return;
            float most = MaxSpeed * dt;
            foreach (var f in fish)
            {
                f.pushLeft = most;
                f.moved = (f.pos - f.before).magnitude;
            }
            for (int pass = 0; pass < Passes; pass++)
                for (int i = 0; i < fish.Count; i++)
                {
                    var a = fish[i];
                    if (a.hidden) continue;
                    float ra = Reach(a);
                    for (int j = i + 1; j < fish.Count; j++)
                    {
                        var b = fish[j];
                        if (b.hidden) continue;
                        float reach = ra + Reach(b) + (a.vel - b.vel).magnitude * dt + a.moved + b.moved;
                        if ((a.pos - b.pos).sqrMagnitude > reach * reach) continue;
                        if (pass == 0) BackToTouch(a, b);
                        float space = Gap(a, b, out var n) - Room;
                        float closing = Vector3.Dot(a.vel - b.vel, n);
                        float tooFast = -closing - Mathf.Max(space, 0) / dt;
                        if (space >= 0 && tooFast <= 0) continue;
                        float ma = a.size * a.size * a.size, mb = b.size * b.size * b.size;
                        float wa = Mathf.Clamp(mb / (ma + mb), LeastShare, 1 - LeastShare), wb = 1 - wa;
                        if (space < 0) Pushed += a.Push(n, -space * wa) + b.Push(-n, -space * wb);
                        if (tooFast > 0)
                        {
                            a.vel += n * (tooFast * wa);
                            b.vel -= n * (tooFast * wb);
                        }
                    }
                    foreach (var c in critters)
                    {
                        float reach = ra + c.Reach + c.Thick + a.vel.magnitude * dt + a.moved;
                        if ((a.pos - c.pos).sqrMagnitude > reach * reach) continue;
                        if (pass == 0) BackToTouch(a, c);
                        float space = Gap(a, c, out var n) - Room;
                        float tooFast = -Vector3.Dot(a.vel, n) - Mathf.Max(space, 0) / dt;
                        if (space < 0) Pushed += a.Push(n, -space);
                        if (tooFast > 0) a.vel += n * tooFast;
                    }
                }
            foreach (var f in fish)
            {
                if (f.pushLeft < most) f.Confine();
                f.before = f.pos;
            }
        }
    }
}
