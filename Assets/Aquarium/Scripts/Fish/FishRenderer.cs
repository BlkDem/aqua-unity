// Draws every fish of a model with instanced calls for the bodies and the fins (the web
// version's FishBatch), a simpler body for fish that are small on the screen. The fish live in the web version's coordinates: position (x, y, z)
// becomes (-x, y, z), a rotation q becomes (qx, -qy, -qz, qw), and the body wave is mirrored
// with it (negative amplitude and turn bend).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.Fish
{
    public sealed class FishRenderer
    {
        const int Cap = 64;
        static readonly int WagId = Shader.PropertyToID("_Wag"), PectId = Shader.PropertyToID("_Pect"), TintId = Shader.PropertyToID("_Tint"), FinAId = Shader.PropertyToID("_FinA"), FinBId = Shader.PropertyToID("_FinB"), GlowId = Shader.PropertyToID("_Glow");

        /// <summary>The fish of one model drawn with one level of detail this frame.</summary>
        sealed class Level
        {
            public int n;
            public readonly Matrix4x4[] matrices = new Matrix4x4[Cap];
            public readonly Vector4[] wag = new Vector4[Cap], tint = new Vector4[Cap], finA = new Vector4[Cap], finB = new Vector4[Cap];
            public readonly float[] pect = new float[Cap];
            public readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
        }

        sealed class Batch
        {
            public FishModel model;
            public readonly List<Fish> fish = new List<Fish>();
            public readonly Level[] levels = { new Level(), new Level(), new Level() };
        }

        /// <summary>
        /// Body detail by how big the fish looks: its length over the distance to the camera.
        /// From the front of the tank most fish are a few dozen pixels long and need far fewer
        /// triangles; the shadows always use the simplest body.
        /// </summary>
        public static float Lod1 = 0.07f, Lod2 = 0.03f;

        readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
        readonly FishModelSet set;
        static readonly Bounds TankBounds = new Bounds(new Vector3(0, 3, 0), new Vector3(14, 8, 7));

        public FishRenderer(FishModelSet set) { this.set = set; }

        public void Add(Fish f)
        {
            if (!batches.TryGetValue(f.modelKey, out var b))
            {
                var model = set ? set.Get(f.modelKey) : null;
                if (model == null) { Debug.LogWarning($"no fish model {f.modelKey}"); return; }
                batches[f.modelKey] = b = new Batch { model = model };
            }
            if (b.fish.Count >= Cap) { Debug.LogWarning($"fish batch full: {f.modelKey}"); return; }
            b.fish.Add(f);
        }

        public static Quaternion ToUnity(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        /// <summary>Queue this frame's fish; only for one camera if given (tools rendering several shots in one frame).</summary>
        public void Draw(float night, Camera only = null)
        {
            var cam = only ? only : Camera.main;
            var eye = cam ? cam.transform.position : new Vector3(0, 3, 14);
            bool lods = LodsOn;
            foreach (var b in batches.Values)
            {
                var L = b.levels;
                L[0].n = L[1].n = L[2].n = 0;
                int count = 0;
                bool hasLods = lods && b.model.bodyLods != null && b.model.bodyLods.Length >= 2;
                foreach (var f in b.fish)
                {
                    if (f.hidden) continue;
                    var pos = new Vector3(-f.pos.x, f.pos.y, f.pos.z);
                    float k = f.size / Mathf.Max(0.1f, Vector3.Distance(eye, pos));
                    var l = L[!hasLods || k > Lod1 ? 0 : k > Lod2 ? 1 : 2];
                    int i = l.n++;
                    count++;
                    l.matrices[i] = Matrix4x4.TRS(pos, ToUnity(f.rotation), f.scaleXYZ);
                    l.wag[i] = new Vector4(f.wag.x, -f.wag.y, -f.wag.z, f.wag.w);
                    l.pect[i] = f.pect;
                    l.finA[i] = f.finA;
                    l.finB[i] = f.finB;
                    var t = f.rec.Gene.Tint;
                    l.tint[i] = new Vector4((float)t[0], (float)t[1], (float)t[2], 1);
                }
                if (count == 0) continue;
                if (b.fish[0].sp.glow)
                {
                    b.model.bodyMaterial.SetFloat(GlowId, 0.15f + night * 0.9f);
                    if (b.model.finMaterial) b.model.finMaterial.SetFloat(GlowId, 0.15f + night * 0.9f);
                }
                for (int li = 0; li < 3; li++)
                {
                    var l = L[li];
                    if (l.n == 0) continue;
                    l.props.SetVectorArray(WagId, l.wag);
                    l.props.SetFloatArray(PectId, l.pect);
                    l.props.SetVectorArray(TintId, l.tint);
                    l.props.SetVectorArray(FinAId, l.finA);
                    l.props.SetVectorArray(FinBId, l.finB);
                    var mesh = li == 0 ? b.model.body : b.model.bodyLods[li - 1];
                    // with simpler bodies at hand, the shadows are drawn separately with the simplest
                    var body = new RenderParams(b.model.bodyMaterial) { matProps = l.props, shadowCastingMode = hasLods ? ShadowCastingMode.Off : ShadowCastingMode.On, receiveShadows = true, worldBounds = TankBounds, camera = only };
                    Graphics.RenderMeshInstanced(body, mesh, 0, l.matrices, l.n);
                    if (hasLods)
                    {
                        var shadow = new RenderParams(b.model.bodyMaterial) { matProps = l.props, shadowCastingMode = ShadowCastingMode.ShadowsOnly, worldBounds = TankBounds, camera = only };
                        Graphics.RenderMeshInstanced(shadow, b.model.bodyLods[1], 0, l.matrices, l.n);
                    }
                    if (!b.model.fins) continue;   // a sculpted model: its fins are part of the body
                    var fins = new RenderParams(b.model.finMaterial) { matProps = l.props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = TankBounds, camera = only };
                    Graphics.RenderMeshInstanced(fins, b.model.fins, 0, l.matrices, l.n);
                }
            }
        }

        /// <summary>Simpler bodies for small fish (off: always the full model, e.g. for close-up tools).</summary>
        public static bool LodsOn = true;
    }

    /// <summary>Food flakes, pellets and worms, one instanced call per colour.</summary>
    public sealed class FoodRenderer
    {
        readonly Mesh mesh;
        readonly Material material;
        readonly Dictionary<Color, (List<Matrix4x4> m, MaterialPropertyBlock p)> groups = new Dictionary<Color, (List<Matrix4x4>, MaterialPropertyBlock)>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public FoodRenderer(Mesh mesh, Material material) { this.mesh = mesh; this.material = material; }

        public void Draw(List<FoodItem> food, Camera only = null)
        {
            if (!mesh || !material) return;
            foreach (var g in groups.Values) g.m.Clear();
            foreach (var f in food)
            {
                if (!groups.TryGetValue(f.color, out var g))
                {
                    g = (new List<Matrix4x4>(), new MaterialPropertyBlock());
                    g.p.SetColor(BaseColorId, f.color);
                    groups[f.color] = g;
                }
                float s = f.grounded ? f.r * Mathf.Max(0, 1 - Mathf.Max(0, f.groundT - 60) / 10) : f.r;
                g.m.Add(Matrix4x4.TRS(new Vector3(-f.pos.x, f.pos.y, f.pos.z), Aquarium.World.WebSpace.Rot(f.rot.x, f.rot.y, f.rot.z), new Vector3(s, s * f.stretch, s)));
            }
            foreach (var g in groups.Values)
            {
                if (g.m.Count == 0) continue;
                var rp = new RenderParams(material) { matProps = g.p, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, camera = only };
                Graphics.RenderMeshInstanced(rp, mesh, 0, g.m);
            }
        }
    }
}
