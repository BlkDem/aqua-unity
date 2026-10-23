// Draws every fish of a model with one instanced call for the bodies and one for the fins (the
// web version's FishBatch). The fish live in the web version's coordinates: position (x, y, z)
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
        static readonly int WagId = Shader.PropertyToID("_Wag"), PectId = Shader.PropertyToID("_Pect"), TintId = Shader.PropertyToID("_Tint"), GlowId = Shader.PropertyToID("_Glow");

        sealed class Batch
        {
            public FishModel model;
            public readonly List<Fish> fish = new List<Fish>();
            public readonly Matrix4x4[] matrices = new Matrix4x4[Cap];
            public readonly Vector4[] wag = new Vector4[Cap], tint = new Vector4[Cap];
            public readonly float[] pect = new float[Cap];
            public readonly MaterialPropertyBlock props = new MaterialPropertyBlock();
        }

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
            foreach (var b in batches.Values)
            {
                int n = 0;
                foreach (var f in b.fish)
                {
                    if (f.hidden) continue;
                    b.matrices[n] = Matrix4x4.TRS(new Vector3(-f.pos.x, f.pos.y, f.pos.z), ToUnity(f.rotation), f.scaleXYZ);
                    b.wag[n] = new Vector4(f.wag.x, -f.wag.y, -f.wag.z, f.wag.w);
                    b.pect[n] = f.pect;
                    var t = f.rec.Gene.Tint;
                    b.tint[n] = new Vector4((float)t[0], (float)t[1], (float)t[2], 1);
                    n++;
                }
                if (n == 0) continue;
                b.props.SetVectorArray(WagId, b.wag);
                b.props.SetFloatArray(PectId, b.pect);
                b.props.SetVectorArray(TintId, b.tint);
                if (b.fish[0].sp.glow)
                {
                    b.model.bodyMaterial.SetFloat(GlowId, 0.15f + night * 0.9f);
                    b.model.finMaterial.SetFloat(GlowId, 0.15f + night * 0.9f);
                }
                var body = new RenderParams(b.model.bodyMaterial) { matProps = b.props, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = TankBounds, camera = only };
                Graphics.RenderMeshInstanced(body, b.model.body, 0, b.matrices, n);
                var fins = new RenderParams(b.model.finMaterial) { matProps = b.props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = TankBounds, camera = only };
                Graphics.RenderMeshInstanced(fins, b.model.fins, 0, b.matrices, n);
            }
        }
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
