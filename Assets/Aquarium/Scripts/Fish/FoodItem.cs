// Food (the web version's particles.js Food): flakes float for a while and drift down slowly,
// pellets sink straight to the bottom (catfish love them), bloodworms are a red treat.
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    public sealed class FoodItem
    {
        public Vector3 pos, vel;
        public float floatT, sink, r, stretch, spin, age, ph, groundT;
        public Vector3 rot;
        public Color color;
        public bool eaten, grounded;
        public readonly HashSet<Fish> claim = new HashSet<Fish>();

        sealed class Kind { public float floatMin, floatMax, sinkMin, sinkMax, rMin, rMax, stretch; public string[] colors; }

        static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>
        {
            ["flakes"] = new Kind { floatMin = 1.5f, floatMax = 4.5f, sinkMin = 0.08f, sinkMax = 0.2f, rMin = 0.018f, rMax = 0.035f, stretch = 1, colors = new[] { "#ff8a3d", "#ffb347", "#e94b3c", "#8bc34a", "#ffd166", "#c0712f" } },
            ["pellets"] = new Kind { floatMin = 0, floatMax = 0.4f, sinkMin = 0.35f, sinkMax = 0.55f, rMin = 0.02f, rMax = 0.03f, stretch = 1, colors = new[] { "#8a5a2b", "#a0662e", "#6e4420", "#b07a3c" } },
            ["worms"] = new Kind { floatMin = 0.2f, floatMax = 0.9f, sinkMin = 0.14f, sinkMax = 0.26f, rMin = 0.012f, rMax = 0.018f, stretch = 3.5f, colors = new[] { "#c0182a", "#a5121f", "#d62a3a" } },
        };

        public const int Max = 260;

        public static void Drop(List<FoodItem> items, float x, float z, int n, string type)
        {
            if (!Kinds.TryGetValue(type ?? "flakes", out var F)) F = Kinds["flakes"];
            for (int i = 0; i < n && items.Count < Max; i++)
            {
                ColorUtility.TryParseHtmlString(Rnd.Pick(F.colors), out var c);
                items.Add(new FoodItem
                {
                    pos = new Vector3(x + Rnd.R(-0.6f, 0.6f), Aquarium.World.Terrain.WaterY - 0.01f, z + Rnd.R(-0.4f, 0.4f)),
                    vel = new Vector3(Rnd.R(-0.05f, 0.05f), 0, Rnd.R(-0.05f, 0.05f)),
                    floatT = Rnd.R(F.floatMin, F.floatMax), sink = Rnd.R(F.sinkMin, F.sinkMax), r = Rnd.R(F.rMin, F.rMax), stretch = F.stretch,
                    color = c, rot = new Vector3(Rnd.R(0, 3), Rnd.R(0, 3), Rnd.R(0, 3)), spin = Rnd.R(-2, 2), ph = Rnd.R(0, 10),
                });
            }
        }

        public static void Update(List<FoodItem> L, float dt, Ecosystem eco)
        {
            for (int i = L.Count - 1; i >= 0; i--)
            {
                var f = L[i];
                f.age += dt;
                if (f.eaten) { L.RemoveAt(i); continue; }
                if (f.floatT > 0)
                {
                    f.floatT -= dt;
                    f.pos.x += f.vel.x * dt + Mathf.Sin(f.age * 2 + f.ph) * 0.02f * dt;
                    f.pos.z += f.vel.z * dt;
                    f.pos.y = Aquarium.World.Terrain.WaterY - 0.02f + Mathf.Sin(f.age * 3 + f.ph) * 0.01f;
                }
                else if (!f.grounded)
                {
                    f.pos.y -= f.sink * dt;
                    f.pos.x += Mathf.Sin(f.age * 1.7f + f.ph) * 0.08f * dt;
                    f.pos.z += Mathf.Cos(f.age * 1.3f + f.ph) * 0.06f * dt;
                    f.rot.x += f.spin * dt; f.rot.z += f.spin * 0.7f * dt;
                    float floor = eco.SurfaceAt(f.pos.x, f.pos.z) + 0.01f;
                    if (f.pos.y <= floor) { f.pos.y = floor; f.grounded = true; f.groundT = 0; }
                }
                else
                {
                    f.groundT += dt;
                    if (f.groundT > 70) { L.RemoveAt(i); continue; }
                }
                f.pos.x = Mathf.Clamp(f.pos.x, -5.85f, 5.85f);
                f.pos.z = Mathf.Clamp(f.pos.z, -2.3f, 2.3f);
            }
        }
    }
}
