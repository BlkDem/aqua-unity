// Bubbles (the web version's particles.js Bubbles): sources that stream or burst, bubbles rising
// at a terminal speed that grows with their size, wobbling, drifting with the current, big ones
// splitting, neighbours merging, popping at the surface. Fish can poke them. Web coordinates.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.Fish
{
    public sealed class Bubble
    {
        public Vector3 pos, vel;
        public float r, ph, wob, age;
    }

    public sealed class BubbleSource
    {
        public string name;
        public Vector3 pos;
        public Vector3? vel;
        public float rate, rMin, rMax, spread, acc, burst;
    }

    public sealed class Bubbles
    {
        public const int Max = 700;
        public readonly List<Bubble> list = new List<Bubble>();
        public readonly List<BubbleSource> sources = new List<BubbleSource>();
        int frame;
        readonly Matrix4x4[] matrices = new Matrix4x4[Max];
        /// <summary>A bubble burst at the surface (for the sound).</summary>
        public System.Action<Bubble> onPop;

        public BubbleSource AddSource(string name, Vector3 pos, float rate, float rMin, float rMax, float spread = 0.03f)
        {
            var s = new BubbleSource { name = name, pos = pos, rate = rate, rMin = rMin, rMax = rMax, spread = spread };
            sources.Add(s);
            return s;
        }

        public Bubble Spawn(Vector3 pos, float r, Vector3? vel = null)
        {
            if (list.Count >= Max) return null;
            var b = new Bubble { pos = pos, vel = vel ?? new Vector3(0, 0.1f, 0), r = r, ph = Rnd.R() * 10, wob = Rnd.R(0.5f, 1.5f) };
            list.Add(b);
            return b;
        }

        public void Burst(Vector3 pos, int n = 20, float rMin = 0.01f, float rMax = 0.05f, float spread = 0.15f)
        {
            for (int i = 0; i < n; i++)
                Spawn(pos + new Vector3(Rnd.R(-spread, spread), Rnd.R(0, spread), Rnd.R(-spread, spread)), Rnd.R(rMin, rMax), new Vector3(Rnd.R(-0.2f, 0.2f), Rnd.R(0.1f, 0.5f), Rnd.R(-0.2f, 0.2f)));
        }

        public Bubble Nearest(Vector3 pos, float maxD)
        {
            Bubble best = null;
            float bd = maxD * maxD;
            foreach (var b in list) { float d = (b.pos - pos).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
            return best;
        }

        public void Poke(Bubble b, Vector3 dir)
        {
            b.vel += dir * 0.6f;
            if (b.r > 0.025f && Rnd.Chance(0.08f))
            {
                b.r /= 1.26f;
                Spawn(b.pos + new Vector3(b.r, 0, 0), b.r, b.vel + new Vector3(0.2f, 0, 0));
            }
        }

        public void Update(float dt, float t)
        {
            foreach (var s in sources)
            {
                if (s.rate <= 0 && s.burst <= 0) continue;
                s.acc += dt * (s.rate + (s.burst > 0 ? 40 : 0));
                s.burst -= dt;
                while (s.acc > 1)
                {
                    s.acc -= 1;
                    Spawn(s.pos + new Vector3(Rnd.R(-s.spread, s.spread), 0, Rnd.R(-s.spread, s.spread)), Rnd.R(s.rMin, s.rMax), s.vel);
                }
            }
            float currentX = Mathf.Sin(t * 0.05f) * 0.03f + Mathf.Sin(t * 0.13f) * 0.015f;
            float top = Aquarium.World.Terrain.WaterY;
            frame++;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var b = list[i];
                b.age += dt;
                float term = 0.35f + b.r * 9;
                b.vel.y += (term - b.vel.y) * Mathf.Min(1, dt * 2.5f);
                b.vel.x *= 1 - dt * 1.5f; b.vel.z *= 1 - dt * 1.5f;
                b.ph += dt * (6 + b.r * 40) * b.wob;
                b.pos += b.vel * dt;
                b.pos.x += Mathf.Sin(b.ph) * dt * (0.05f + b.r * 2) + currentX * dt * 3;
                b.pos.z += Mathf.Cos(b.ph * 0.8f) * dt * (0.04f + b.r * 1.5f);
                // the filter's current pushes bubbles near the outlet
                if (b.pos.x > 4.2f && b.pos.y > 4.4f) b.pos.x -= dt * 0.6f;
                if (b.r > 0.07f && Rnd.Chance(dt * 0.6f))
                {
                    b.r /= 1.26f;
                    Spawn(b.pos + new Vector3(b.r * 1.2f, 0, 0), b.r, new Vector3(0.15f, b.vel.y, 0));
                }
                if (b.pos.y > top - b.r * 0.5f) { list.RemoveAt(i); onPop?.Invoke(b); }
            }
            // merging (cheap, every other frame): sorted by height, only close neighbours are compared
            if (frame % 2 == 0)
            {
                list.Sort((p, q) => p.pos.y.CompareTo(q.pos.y));
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var b = list[j];
                        if (b.pos.y - a.pos.y > 0.2f) break;
                        float rr = (a.r + b.r) * 0.85f;
                        if ((a.pos - b.pos).sqrMagnitude < rr * rr)
                        {
                            a.r = Mathf.Pow(a.r * a.r * a.r + b.r * b.r * b.r, 1 / 3f);
                            a.vel = (a.vel + b.vel) * 0.5f;
                            list.RemoveAt(j);
                            j--;
                        }
                    }
                }
            }
        }

        public void Draw(Mesh mesh, Material material, Camera only = null)
        {
            if (!mesh || !material || list.Count == 0) return;
            int n = 0;
            foreach (var b in list)
            {
                float sq = 1 + Mathf.Sin(b.ph * 1.7f) * 0.08f * Mathf.Min(1, b.r * 30);
                matrices[n++] = Matrix4x4.TRS(new Vector3(-b.pos.x, b.pos.y, b.pos.z), Quaternion.identity, new Vector3(b.r * sq, b.r / sq * 0.92f, b.r * sq));
            }
            var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = only };
            Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, n);
        }
    }
}
