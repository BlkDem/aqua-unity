// Prepares the fish models for instanced drawing (the web version's fishAssets.js + FishBatch):
// the body with cartoon-but-natural eyes (iris ball, big pupil, a glint) painted on a white
// texel block of the body texture, and the fins with the pectorals' pivots stored per vertex
// (uv2: paddle direction, pivot) so the shader can paddle them. Saved as assets in
// Assets/Aquarium/Art/Generated/Fish and listed in FishModels.asset.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquarium.Fish;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.EditorTools
{
    public static class FishModelBuilder
    {
        const string Dir = "Assets/Aquarium/Art/Generated/Fish";
        public const string SetPath = Dir + "/FishModels.asset";
        // the white block reserved in every body texture (glTF uv (0.997, 0.997), v flipped by glTFast)
        static readonly Vector2 EyeUV = new Vector2(0.997f, 0.003f);

        [MenuItem("Aquarium/Build Fish Models")]
        public static FishModelSet Build()
        {
            Directory.CreateDirectory(Dir);
            var set = AssetDatabase.LoadAssetAtPath<FishModelSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<FishModelSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            set.models.Clear();
            foreach (var key in FishSpecies.ModelKeys())
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Aquarium/Resources/Models/Fish/{key}.glb");
                if (!src) { Debug.LogWarning($"fish model {key} missing"); continue; }
                var sp = FishSpecies.All[key.Split('-')[0]];
                set.models.Add(BuildOne(key, sp, src.transform));
            }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"fish models built: {set.models.Count}");
            return set;
        }

        static Transform Node(Transform root, string name) => root.GetComponentsInChildren<Transform>().First(t => t.name == name);

        /// <summary>A node's mesh with its transform relative to the model root baked in.</summary>
        static Mesh Baked(Transform root, Transform node)
        {
            var src = node.GetComponent<MeshFilter>().sharedMesh;
            var m = root.worldToLocalMatrix * node.localToWorldMatrix;
            var mesh = new Mesh();
            mesh.vertices = src.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            mesh.normals = src.normals.Select(n => m.MultiplyVector(n).normalized).ToArray();
            if (src.uv.Length > 0) mesh.uv = src.uv;
            mesh.triangles = src.triangles;
            return mesh;
        }

        static Mesh Combine(List<(Mesh mesh, Color color, Vector4 pect, bool eyeUV)> parts, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var p = new List<Vector4>(); var t = new List<int>();
            foreach (var (mesh, color, pect, eye) in parts)
            {
                int b = v.Count;
                v.AddRange(mesh.vertices);
                n.AddRange(mesh.normals);
                var muv = mesh.uv;
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    uv.Add(eye || muv.Length == 0 ? EyeUV : muv[i]);
                    c.Add(color);
                    p.Add(pect);
                }
                t.AddRange(mesh.triangles.Select(i => i + b));
            }
            var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(c); m.SetUVs(2, p); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        static Mesh Sphere(float r, int ws, int hs, Vector3 at)
        {
            var m = MeshGen.Sphere(r, ws, hs);
            m.vertices = m.vertices.Select(x => x + at).ToArray();
            return m;
        }

        static FishModel BuildOne(string key, FishSpecies sp, Transform root)
        {
            var parts = new List<(Mesh, Color, Vector4, bool)> { (Baked(root, Node(root, "Body")), Color.white, Vector4.zero, false) };
            // natural eyes: coloured iris ball, big dark pupil, small specular glint
            ColorUtility.TryParseHtmlString(sp.iris, out var iris);
            foreach (var eyeName in new[] { "EyeL", "EyeR" })
            {
                var e = Baked(root, Node(root, eyeName));
                e.RecalculateBounds();
                var pos = e.bounds.center;
                float r = e.bounds.extents.magnitude / Mathf.Sqrt(3) * 0.85f, s = Mathf.Sign(pos.x);
                parts.Add((Sphere(r, 16, 12, pos), iris.linear, Vector4.zero, true));
                parts.Add((Sphere(r * 0.62f, 14, 10, pos + new Vector3(s * r * 0.48f, 0, r * 0.05f)), new Color(0.003f, 0.003f, 0.003f), Vector4.zero, true));
                parts.Add((Sphere(r * 0.13f, 6, 4, pos + new Vector3(s * r * 0.98f, r * 0.32f, r * 0.28f)), Color.white, Vector4.zero, true));
            }
            var body = Save(Combine(parts, $"{key}_body"));

            // fins: fixed fins + pectorals paddled around their pivots. The model is mirrored on the
            // way into Unity (glTFast), so the paddle direction is too: the left fin (web -1) is +1 here.
            var finParts = new List<(Mesh, Color, Vector4, bool)> { (Baked(root, Node(root, "Fins")), Color.white, Vector4.zero, false) };
            foreach (var (pectName, side) in new[] { ("PectL", 1f), ("PectR", -1f) })
            {
                var node = Node(root, pectName);
                var pivot = root.InverseTransformPoint(node.position);
                finParts.Add((Baked(root, node), Color.white, new Vector4(side, pivot.x, pivot.y, pivot.z), false));
            }
            var fins = Save(Combine(finParts, $"{key}_fins"));

            var srcBody = Node(root, "Body").GetComponent<MeshRenderer>().sharedMaterial;
            var srcFin = Node(root, "Fins").GetComponent<MeshRenderer>().sharedMaterial;
            var shader = Shader.Find("Aquarium/Fish");
            var bm = new Material(shader) { enableInstancing = true };
            bm.SetTexture("_BaseMap", srcBody.GetTexture("baseColorTexture"));
            var nrm = srcBody.GetTexture("normalTexture");
            if (nrm) { bm.SetTexture("_BumpMap", nrm); bm.EnableKeyword("_NORMALMAP"); }
            bm.SetFloat("_BumpScale", 0.7f);
            bm.SetFloat("_Roughness", 0.3f);
            bm.SetFloat("_Metallic", 0.05f);
            bm.SetFloat("_CausticStrength", 0.55f);
            if (sp.glow) bm.SetColor("_EmissionColor", WebHex("#8fe9ff") * 0.4f);
            var fm = new Material(shader) { enableInstancing = true };
            fm.SetTexture("_BaseMap", srcFin.GetTexture("baseColorTexture"));
            fm.SetFloat("_Fins", 1);
            fm.EnableKeyword("_FINS");
            fm.SetFloat("_Roughness", 0.35f);
            fm.SetFloat("_Metallic", 0);
            fm.SetFloat("_CausticStrength", 0.4f);
            fm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fm.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            fm.SetFloat("_ZWrite", 0);
            fm.SetFloat("_Cull", 0);
            fm.renderQueue = 3000;
            fm.SetOverrideTag("RenderType", "Transparent");
            if (sp.glow) fm.SetColor("_EmissionColor", WebHex("#8fb6ff") * 0.6f);
            return new FishModel { key = key, body = body, fins = fins, bodyMaterial = SaveMat(bm, $"{key}_body"), finMaterial = SaveMat(fm, $"{key}_fins") };
        }

        static Color WebHex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static Mesh Save(Mesh m)
        {
            string path = $"{Dir}/{m.name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); Object.DestroyImmediate(m); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material SaveMat(Material m, string name)
        {
            string path = $"{Dir}/{name}.mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old) { old.shader = m.shader; old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; old.renderQueue = m.renderQueue; old.enableInstancing = true; Object.DestroyImmediate(m); return old; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
