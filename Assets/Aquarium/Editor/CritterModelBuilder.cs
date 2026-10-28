// Turns the critter models (tools/blender/make_critters.py) into prefabs with aquarium materials.
// The rigid parts (body, leg segments, whiskers...) are stitched into one skinned mesh, each
// part a bone: the critter code still moves the parts' transforms, but a crab is drawn with one
// draw call instead of thirty-five.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquarium.Fish;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static class CritterModelBuilder
    {
        const string Dir = "Assets/Aquarium/Art/Generated/Critters";
        const string SetPath = Dir + "/CritterModels.asset";
        const string SkinDir = "Assets/Aquarium/Art/Textures/Critters/shrimp/skins";

        [MenuItem("Aquarium/Build Critter Models")]
        public static CritterModelSet Build()
        {
            Directory.CreateDirectory(Dir);
            var set = AssetDatabase.LoadAssetAtPath<CritterModelSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<CritterModelSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            set.crab = Prefab("crab", 0.9f);
            set.shrimp = Prefab("shrimp", 1.0f);
            // recoloured patterns for the sculpted shrimp (the web version's tools/import_critter.py --skins)
            var skins = Directory.Exists(SkinDir) ? Directory.GetFiles(SkinDir, "shrimp_*.jpg").OrderBy(f => f).ToArray() : new string[0];
            set.shrimpSkinKeys = skins.Select(f => Path.GetFileNameWithoutExtension(f).Substring("shrimp_".Length)).ToArray();
            set.shrimpSkins = skins.Select(f => AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'))).ToArray();
            set.snailMystery = Prefab("snail-mystery", 0.8f);
            set.snailNerite = Prefab("snail-nerite", 0.8f);
            set.snailRamshorn = Prefab("snail-ramshorn", 0.8f);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        static GameObject Prefab(string name, float caustics)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Aquarium/Models/Critters/{name}.glb");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = Materials.FromGltf(mats[i], name, "Static");
                    m.SetFloat("_CausticStrength", caustics);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            Skin(go, name, name.StartsWith("snail") ? new[] { "Shell" } : new string[0]);
            string path = $"{Dir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// Merge every part's mesh into one skinned mesh (one bone per part). Parts named in
        /// `separate` get sub-meshes of their own, so they can be tinted alone (the last slots).
        /// </summary>
        static void Skin(GameObject go, string name, string[] separate)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()).ToList();
            var skinT = new GameObject("Skin").transform;
            skinT.SetParent(go.transform, false);
            var bones = filters.Select(f => f.transform).ToArray();
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var uvs = new List<Vector2>(); var weights = new List<BoneWeight>();
            var groups = new List<(Material mat, bool own, List<int> tris)>();
            for (int b = 0; b < filters.Count; b++)
            {
                var f = filters[b];
                var m = f.sharedMesh;
                var mats = f.GetComponent<MeshRenderer>().sharedMaterials;
                bool own = separate.Contains(f.name);
                var M = skinT.worldToLocalMatrix * f.transform.localToWorldMatrix;
                int baseV = verts.Count;
                var v = m.vertices; var n = m.normals; var t = m.tangents; var uv = m.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    verts.Add(M.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length > 0 ? M.MultiplyVector(n[i]).normalized : Vector3.up);
                    if (t.Length > 0) { var tt = M.MultiplyVector(t[i]).normalized; tangents.Add(new Vector4(tt.x, tt.y, tt.z, t[i].w)); } else tangents.Add(new Vector4(1, 0, 0, 1));
                    uvs.Add(uv.Length > 0 ? uv[i] : Vector2.zero);
                    weights.Add(new BoneWeight { boneIndex0 = b, weight0 = 1 });
                }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var mat = mats[Mathf.Min(s, mats.Length - 1)];
                    int gi = groups.FindIndex(g => g.mat == mat && g.own == own);
                    if (gi < 0) { groups.Add((mat, own, new List<int>())); gi = groups.Count - 1; }
                    groups[gi].tris.AddRange(m.GetTriangles(s).Select(i => i + baseV));
                }
            }
            groups = groups.OrderBy(g => g.own).ToList();
            var mesh = new Mesh { name = $"{name}_skin", indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetTangents(tangents); mesh.SetUVs(0, uvs);
            mesh.subMeshCount = groups.Count;
            for (int s = 0; s < groups.Count; s++) mesh.SetTriangles(groups[s].tris, s);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * skinT.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            string meshPath = $"{Dir}/{name}_skin.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var smr = skinT.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = go.transform;
            smr.sharedMaterials = groups.Select(g => g.mat).ToArray();
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            var bounds = mesh.bounds;
            bounds.Expand(bounds.size.magnitude * 0.5f);   // legs and claws reach out while walking
            smr.localBounds = bounds;
            foreach (var f in filters)
            {
                Object.DestroyImmediate(f.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(f);
            }
        }
    }
}
