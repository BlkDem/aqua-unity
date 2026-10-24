using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    /// <summary>Development helpers run from the command line (-executeMethod).</summary>
    public static partial class Dev
    {
        public static void DumpModels()
        {
            var sb = new StringBuilder();
            foreach (var path in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Aquarium/Models" }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                sb.AppendLine($"== {path}");
                Dump(go.transform, 1, sb);
            }
            foreach (var (file, name) in new[] { ("Decor/castle.glb", "Castle"), ("Plants/vallisneria.glb", null), ("Decor/castle.glb", "GlowWindow") })
            {
            var mat = AssetDatabase.LoadAllAssetsAtPath("Assets/Aquarium/Models/" + file).OfType<Material>().FirstOrDefault(m => name == null || m.name == name);
            if (mat)
            {
                sb.AppendLine($"material {mat.name} shader {mat.shader.name}");
                for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
                {
                    string n = mat.shader.GetPropertyName(i);
                    var t = mat.shader.GetPropertyType(i);
                    string v = t == UnityEngine.Rendering.ShaderPropertyType.Texture ? (mat.GetTexture(n) ? mat.GetTexture(n).name : "-") : t == UnityEngine.Rendering.ShaderPropertyType.Color ? mat.GetColor(n).ToString() : t == UnityEngine.Rendering.ShaderPropertyType.Vector ? mat.GetVector(n).ToString() : mat.GetFloat(n).ToString();
                    if (!n.StartsWith("_") || n.Contains("Cull") || n.Contains("Surface") || n.Contains("Alpha")) sb.AppendLine($"  {n} ({t}) = {v}");
                }
                sb.AppendLine("  keywords: " + string.Join(" ", mat.shaderKeywords) + " queue " + mat.renderQueue + " doubleSided " + mat.doubleSidedGI);
                foreach (var tn in mat.GetTexturePropertyNames())
                {
                    var tex = mat.GetTexture(tn) as Texture2D;
                    if (tex) sb.AppendLine($"  tex {tn}: {tex.name} {tex.width}x{tex.height} {tex.format} srgb={tex.isDataSRGB} path={AssetDatabase.GetAssetPath(tex)}");
                }
            }
            }
            File.WriteAllText("Logs/models.txt", sb.ToString());
        }

        static void Dump(Transform t, int depth, StringBuilder sb)
        {
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            string mesh = mf && mf.sharedMesh ? $" mesh={mf.sharedMesh.name} v={mf.sharedMesh.vertexCount} b={mf.sharedMesh.bounds.center:F2}/{mf.sharedMesh.bounds.size:F2}" : "";
            string mats = mr ? " mats=" + string.Join(",", mr.sharedMaterials.Select(m => m ? m.name : "null")) : "";
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name} p={t.localPosition:F3} r={t.localEulerAngles:F1} s={t.localScale:F2}{mesh}{mats}");
            if (depth < 4) foreach (Transform c in t) Dump(c, depth + 1, sb);
        }
    }
}
