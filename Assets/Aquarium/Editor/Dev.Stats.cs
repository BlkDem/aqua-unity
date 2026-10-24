// What the scene draws: renderers, sub-meshes and materials per group (for the draw-call budget).
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.EditorTools
{
    public static partial class Dev
    {
        public static void SceneStats()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            Debug.Log($"STATS renderers {all.Length}, sub-meshes {all.Sum(r => r.sharedMaterials.Length)}, casting shadows {all.Count(r => r.shadowCastingMode != ShadowCastingMode.Off)} ({all.Where(r => r.shadowCastingMode != ShadowCastingMode.Off).Sum(r => r.sharedMaterials.Length)} sub-meshes), materials {all.SelectMany(r => r.sharedMaterials).Distinct().Count()}");
            foreach (var g in all.GroupBy(r => r.transform.parent ? (r.transform.parent.parent && r.transform.parent.parent.parent ? r.transform.parent.parent.parent.name + "/" + r.transform.parent.parent.name : r.transform.parent.name) : "-").OrderByDescending(g => g.Sum(r => r.sharedMaterials.Length)).Take(25))
            {
                var sway = g.Count(r => r.sharedMaterials.Any(m => m && m.HasProperty("_Sway") && m.GetFloat("_Sway") > 0));
                Debug.Log($"STATS {g.Key}: renderers {g.Count()}, sub-meshes {g.Sum(r => r.sharedMaterials.Length)}, sway {sway}, verts {g.Sum(r => r.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0)}");
            }
        }
    }
}
namespace Aquarium.EditorTools
{
    public static partial class Dev
    {
        static long Tris(UnityEngine.Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return n; }

        public static void TriangleStats()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SceneBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var rs = UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsInactive.Exclude);
            var list = new System.Collections.Generic.List<(string name, long tris, bool shadow)>();
            foreach (var r in rs)
            {
                var f = r.GetComponent<UnityEngine.MeshFilter>();
                if (f && f.sharedMesh) list.Add((r.name, Tris(f.sharedMesh), r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off));
            }
            UnityEngine.Debug.Log($"TRIS scene {list.Sum(x => x.tris)}, of it casting shadows {list.Where(x => x.shadow).Sum(x => x.tris)}");
            foreach (var x in list.OrderByDescending(x => x.tris).Take(20)) UnityEngine.Debug.Log($"TRIS {x.name}: {x.tris}{(x.shadow ? " (shadow)" : "")}");
            var fish = UnityEditor.AssetDatabase.LoadAssetAtPath<Aquarium.Fish.FishModelSet>(FishModelBuilder.SetPath);
            foreach (var m in fish.models) UnityEngine.Debug.Log($"TRIS fish {m.key}: body {Tris(m.body)}, fins {Tris(m.fins)}");
            foreach (var p in UnityEditor.AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Aquarium/Art/Generated/Critters" }).Select(UnityEditor.AssetDatabase.GUIDToAssetPath))
                UnityEngine.Debug.Log($"TRIS critter {p}: {Tris(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(p))}");
            var plants = UnityEditor.AssetDatabase.LoadAssetAtPath<Aquarium.Fish.PlantModelSet>("Assets/Aquarium/Art/Generated/Plants/PlantModels.asset");
            foreach (var e in plants.plants) UnityEngine.Debug.Log($"TRIS plant {e.node}: {e.prefab.GetComponentsInChildren<UnityEngine.MeshFilter>().Sum(f => Tris(f.sharedMesh))}");
        }
    }
}
