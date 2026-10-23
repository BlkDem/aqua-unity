// Turns the critter models (tools/blender/make_critters.py) into prefabs with aquarium materials.
using System.IO;
using Aquarium.Fish;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static class CritterModelBuilder
    {
        const string Dir = "Assets/Aquarium/Art/Generated/Critters";
        const string SetPath = Dir + "/CritterModels.asset";

        [MenuItem("Aquarium/Build Critter Models")]
        public static CritterModelSet Build()
        {
            Directory.CreateDirectory(Dir);
            var set = AssetDatabase.LoadAssetAtPath<CritterModelSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<CritterModelSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            set.crab = Prefab("crab", 0.9f);
            set.shrimp = Prefab("shrimp", 1.0f);
            set.snailMystery = Prefab("snail-mystery", 0.8f);
            set.snailNerite = Prefab("snail-nerite", 0.8f);
            set.snailRamshorn = Prefab("snail-ramshorn", 0.8f);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        static GameObject Prefab(string name, float caustics)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Aquarium/Resources/Models/Critters/{name}.glb");
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
            string path = $"{Dir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
