// Plants that grow over days (from half size towards full) and spread: now and then a clump
// sends out a runner and a young plant appears nearby (the web version's plants.js). Growth and
// runners are kept in the saved world, so they carry over between visits and versions.
using System.Collections.Generic;
using System.Linq;
using Aquarium.Life;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Aquarium.Fish
{
    public sealed class PlantSystem
    {
        const double Day = Aquarium.Life.World.Day;

        sealed class Spread { public string title; public float everyMin, everyMax, distMin, distMax; public int cap; }

        static readonly Dictionary<string, Spread> Spreading = new Dictionary<string, Spread>
        {
            ["vallisneria"] = new Spread { title = "Валлиснерия", everyMin = 5, everyMax = 9, cap = 14, distMin = 0.45f, distMax = 0.8f },
            ["crypt"] = new Spread { title = "Криптокорина", everyMin = 7, everyMax = 12, cap = 9, distMin = 0.35f, distMax = 0.6f },
            ["sword"] = new Spread { title = "Эхинодорус", everyMin = 12, everyMax = 20, cap = 6, distMin = 0.7f, distMax = 1.0f },
        };
        public static readonly Dictionary<string, (string node, int variants)> Nodes = new Dictionary<string, (string, int)>
        {
            ["vallisneria"] = ("SwayVallis", 3), ["sword"] = ("SwaySword", 2), ["crypt"] = ("SwayCrypt", 2), ["limnophila"] = ("SwayLimno", 2),
            ["cabomba"] = ("SwayCabomba", 2), ["ludwigia"] = ("SwayLudwigia", 2), ["anubias"] = ("SwayAnubias", 2),
        };

        sealed class Growing
        {
            public Transform obj;
            public string kind, id;
            public float x, z, baseScale;
            public PlantRecord rec;
        }

        readonly Ecosystem eco;
        readonly Aquarium.Life.World world;
        readonly PlantModelSet models;
        readonly Transform parent;
        readonly List<Growing> growing = new List<Growing>();

        /// <summary>
        /// Every plant model variant is drawn as one instanced batch (a draw call per variant and
        /// part rather than per clump, as in the web version); the plants' objects only carry
        /// their transforms.
        /// </summary>
        sealed class Batch
        {
            public Mesh mesh;
            public int sub;
            public Material mat;
            public Matrix4x4 local;
            public RenderParams rp;
            public readonly List<Transform> items = new List<Transform>();
            public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        }

        readonly Dictionary<GameObject, List<Batch>> batches = new Dictionary<GameObject, List<Batch>>();
        bool dirty = true;

        List<Batch> BatchesOf(GameObject prefab)
        {
            if (batches.TryGetValue(prefab, out var list)) return list;
            list = new List<Batch>();
            var rootT = prefab.transform;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh) continue;
                var local = rootT.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int s = 0; s < r.sharedMaterials.Length && s < mesh.subMeshCount; s++)
                    list.Add(new Batch
                    {
                        mesh = mesh, sub = s, mat = r.sharedMaterials[s], local = local,
                        rp = new RenderParams(r.sharedMaterials[s])
                        {
                            shadowCastingMode = r.shadowCastingMode, receiveShadows = true,
                            worldBounds = new Bounds(Vector3.up * 3, new Vector3(16, 10, 8)),
                        },
                    });
            }
            batches[prefab] = list;
            return list;
        }

        /// <summary>Every plant part with its matrix (tools draw them as ordinary objects for snapshots).</summary>
        public IEnumerable<(Mesh mesh, int sub, Material mat, Matrix4x4 matrix)> Instances()
        {
            foreach (var list in batches.Values)
                foreach (var b in list)
                    foreach (var t in b.items) yield return (b.mesh, b.sub, b.mat, t.localToWorldMatrix * b.local);
        }

        /// <summary>Queue the plants for drawing this frame (for one camera only, if given).</summary>
        public void Draw(Camera only = null)
        {
            foreach (var list in batches.Values)
                foreach (var b in list)
                {
                    if (dirty)
                    {
                        b.matrices.Clear();
                        foreach (var t in b.items) b.matrices.Add(t.localToWorldMatrix * b.local);
                    }
                    if (b.matrices.Count == 0) continue;
                    var rp = b.rp;
                    rp.camera = only;
                    Graphics.RenderMeshInstanced(rp, b.mesh, b.sub, b.matrices);
                }
            dirty = false;
        }
        readonly Dictionary<string, int> counter = new Dictionary<string, int>();

        static float Growth(PlantRecord rec, double t) => 0.5f + 0.5f * (1 - Mathf.Exp(-(float)(System.Math.Max(0, t - rec.Planted) / (5 * Day))));

        public PlantSystem(Ecosystem eco, PlantModelSet models, Transform parent)
        {
            this.eco = eco;
            world = eco.world;
            this.models = models;
            this.parent = parent;
            // the first plants of the aquarium, as in the web version (same ids, so the save matches)
            foreach (var (x, z) in new[] { (-5.3f, -2.0f), (-5.6f, -0.6f), (-2.6f, -2.05f), (-1.6f, -1.9f), (1.2f, -2.15f), (5.55f, -2.15f), (-0.4f, -2.1f), (3.5f, -2.15f) }) Planted("vallisneria", x, z);
            foreach (var (x, z) in new[] { (-2.95f, 0.4f), (-4.4f, 0.45f), (-0.5f, 0.95f), (5.0f, 0.35f) }) Planted("sword", x, z, 0.95f);
            foreach (var (x, z) in new[] { (-3.5f, 1.0f), (2.6f, 1.15f), (-1.9f, 1.35f), (4.4f, 1.6f) }) Planted("crypt", x, z, 1.1f);
            foreach (var (x, z, kind) in new[] { (-5.55f, -1.75f, "ludwigia"), (-1.25f, -2.15f, "limnophila"), (0.9f, -1.0f, "ludwigia"), (-5.4f, 0.9f, "limnophila") }) Planted(kind, x, z);
            foreach (var (x, z) in new[] { (-3.3f, -2.25f), (2.7f, -2.2f), (-1.1f, -1.4f) }) Planted("cabomba", x, z);
            // anubias tied to driftwood and stones: they sit on whatever is there
            foreach (var (x, z) in new[] { (-0.6f, -1.3f), (1.4f, -1.45f), (-3.0f, 1.15f), (3.0f, 1.6f) }) Planted("anubias", x, z, 1.3f, eco.SurfaceAt(x, z) - 0.04f);
            // young plants that sprouted from runners earlier
            foreach (var e in world.State.ExtraPlants)
            {
                string kind = (string)e.Extra?["kind"], id = (string)e.Extra?["id"];
                if (kind == null) continue;
                Place(kind, id, (float)e.Extra["x"], (float)e.Extra["z"], (float)(e.Extra.TryGetValue("scale", out var sc) ? sc : 1), e);
            }
        }

        void Planted(string kind, float x, float z, float scale = 1, float? y = null)
        {
            counter[kind] = (counter.TryGetValue(kind, out var n) ? n : 0) + 1;
            string id = $"{kind}-{counter[kind]}";
            var st = world.State;
            if (!st.Plants.TryGetValue(id, out var rec))
            {
                // the first plants were put in some days before the aquarium opened
                rec = new PlantRecord { Planted = st.Created - Rnd.R(3, 9) * Day, Gene = Rnd.R(0.85f, 1.15f), Variant = (int)Rnd.R(0, 9), Rot = Rnd.R(0, Mathf.PI * 2) };
                st.Plants[id] = rec;
            }
            // the first runners come after the aquarium opened
            if (Spreading.TryGetValue(kind, out var S) && rec.NextRunner == null) rec.NextRunner = System.Math.Max(rec.Planted, st.Created) + Rnd.R(S.everyMin, S.everyMax) * Day;
            Place(kind, id, x, z, scale, rec, y);
        }

        void Place(string kind, string id, float x, float z, float scale, PlantRecord rec, float? y = null)
        {
            var (node, variants) = Nodes[kind];
            var prefab = models ? models.Get($"{node}{rec.Variant % variants}") : null;
            if (!prefab) return;
            var go = new GameObject(id);
            var t = go.transform;
            t.SetParent(parent, false);
            foreach (var b in BatchesOf(prefab)) b.items.Add(t);
            dirty = true;
            t.localPosition = new Vector3(-x, y ?? eco.HeightAt(x, z) - 0.03f, z);
            t.localRotation = Aquarium.World.WebSpace.Yaw((float)rec.Rot);
            var g = new Growing { obj = t, kind = kind, id = id, x = x, z = z, baseScale = scale * (float)rec.Gene, rec = rec };
            growing.Add(g);
            t.localScale = Vector3.one * g.baseScale * Growth(rec, world.Now());
        }

        /// <summary>Growth and spreading up to time t (also catches up after an absence). Returns how many young plants appeared.</summary>
        public int Tick(double t)
        {
            int sprouted = 0;
            dirty = true;   // growth changes the sizes
            for (int i = 0; i < growing.Count; i++)   // new plants are appended and spread too
            {
                var g = growing[i];
                g.obj.localScale = Vector3.one * g.baseScale * Growth(g.rec, t);
                if (!Spreading.TryGetValue(g.kind, out var S)) continue;
                g.rec.NextRunner ??= g.rec.Planted + Rnd.R(S.everyMin, S.everyMax) * Day;
                while (g.rec.NextRunner <= t)
                {
                    double at = g.rec.NextRunner.Value;
                    g.rec.NextRunner += Rnd.R(S.everyMin, S.everyMax) * Day;
                    if (growing.Count(o => o.kind == g.kind) >= S.cap) break;
                    var spot = FreeSpot(g.x, g.z, S.distMin, S.distMax);
                    if (spot == null) continue;
                    string id = $"{g.kind}-r{world.State.ExtraPlants.Count + 1}";
                    var rec = new PlantRecord
                    {
                        Planted = at, Gene = Rnd.R(0.85f, 1.15f), Variant = (int)Rnd.R(0, 9), Rot = Rnd.R(0, Mathf.PI * 2),
                        Extra = new Dictionary<string, JToken> { ["id"] = id, ["kind"] = g.kind, ["x"] = spot.Value.x, ["z"] = spot.Value.y, ["scale"] = g.baseScale / g.rec.Gene },
                    };
                    world.State.ExtraPlants.Add(rec);
                    Place(g.kind, id, spot.Value.x, spot.Value.y, (float)(g.baseScale / g.rec.Gene), rec);
                    growing[growing.Count - 1].obj.localScale = Vector3.one * growing[growing.Count - 1].baseScale * Growth(rec, t);
                    world.Log("plant", $"{S.title} пустила новый побег — рядом вырос молодой куст", at);
                    sprouted++;
                }
            }
            return sprouted;
        }

        /// <summary>A free patch of sand near (x, z): inside the tank, clear of rocks and other plants.</summary>
        Vector2? FreeSpot(float x, float z, float d0, float d1)
        {
            for (int k = 0; k < 12; k++)
            {
                float a = Rnd.R(0, Mathf.PI * 2), d = Rnd.R(d0, d1);
                float px = x + Mathf.Cos(a) * d, pz = z + Mathf.Sin(a) * d * 0.7f;
                if (Mathf.Abs(px) > 5.5f || pz < -2.2f || pz > 2.0f) continue;
                if (eco.colliders.Any(c => c.center.y - c.radius < 0.7f && Mathf.Sqrt((c.center.x - px) * (c.center.x - px) + (c.center.z - pz) * (c.center.z - pz)) < c.radius * 0.9f + 0.2f)) continue;
                if (growing.Any(o => Mathf.Sqrt((o.x - px) * (o.x - px) + (o.z - pz) * (o.z - pz)) < 0.42f)) continue;
                return new Vector2(px, pz);
            }
            return null;
        }
    }
}
