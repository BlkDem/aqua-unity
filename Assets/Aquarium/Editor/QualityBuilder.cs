// The quality presets (the web version's QUALITY in main.js): a URP asset each, set up in code,
// and the project's quality levels pointing at them. Aquarium > Build Quality Presets; the scene
// builder runs it too.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium.EditorTools
{
    public static class QualityBuilder
    {
        const string Dir = "Assets/Settings/Quality";

        public sealed class Preset
        {
            public string name;
            public float renderScale;
            public int msaa, shadowMap, cascades, softQuality, textureLimit, anisotropic;
            public float shadowDistance;
            public bool softShadows;
        }

        // shadow distance: the tank is ~12 units wide and the camera usually 6..15 units away
        public static readonly Preset[] Presets =
        {
            new Preset { name = "Низкое", renderScale = 0.75f, msaa = 1, shadowMap = 1024, cascades = 1, softShadows = false, shadowDistance = 20, textureLimit = 1, anisotropic = 0 },
            new Preset { name = "Среднее", renderScale = 1, msaa = 2, shadowMap = 2048, cascades = 1, softShadows = true, softQuality = 1, shadowDistance = 22, textureLimit = 0, anisotropic = 1 },
            new Preset { name = "Высокое", renderScale = 1, msaa = 4, shadowMap = 2048, cascades = 2, softShadows = true, softQuality = 2, shadowDistance = 25, textureLimit = 0, anisotropic = 1 },
            new Preset { name = "Ультра", renderScale = 1, msaa = 4, shadowMap = 4096, cascades = 2, softShadows = true, softQuality = 3, shadowDistance = 25, textureLimit = 0, anisotropic = 2 },
        };

        [MenuItem("Aquarium/Build Quality Presets")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            // ambient occlusion (from the URP template) only on Ultra: the web version has none, and it
            // draws every opaque object once more for the normals
            var withAo = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
            string plainPath = $"{Dir}/Renderer_NoAO.asset";
            if (!AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(plainPath)) AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset", plainPath);
            var plain = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(plainPath);
            foreach (var f in plain.rendererFeatures)
                if (f && f.GetType().Name.Contains("AmbientOcclusion")) f.SetActive(false);
            EditorUtility.SetDirty(plain);
            var assets = new UniversalRenderPipelineAsset[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                var q = Presets[i];
                string path = $"{Dir}/Quality{i}_RPAsset.asset";
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!a)
                {
                    // start from the project's PC asset, so everything not set here stays as tuned
                    AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset", path);
                    a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                }
                var so = new SerializedObject(a);
                void Set(string prop, Action<SerializedProperty> f) { var p = so.FindProperty(prop); if (p != null) f(p); else Debug.LogWarning($"URP asset has no {prop}"); }
                Set("m_RenderScale", p => p.floatValue = q.renderScale);
                Set("m_MSAA", p => p.intValue = q.msaa);
                Set("m_SupportsHDR", p => p.boolValue = true);
                Set("m_MainLightShadowmapResolution", p => p.intValue = q.shadowMap);
                Set("m_ShadowCascadeCount", p => p.intValue = q.cascades);
                Set("m_ShadowDistance", p => p.floatValue = q.shadowDistance);
                Set("m_SoftShadowsSupported", p => p.boolValue = q.softShadows);
                Set("m_SoftShadowQuality", p => p.intValue = q.softQuality);
                // upscaling from a lower render scale: bilinear (FSR — which "auto" may pick — has no shader in browsers,
                // and without it the post-processing would stop)
                Set("m_UpscalingFilter", p => p.intValue = 1);
                Set("m_UseSRPBatcher", p => p.boolValue = true);
                // no shader reads the scene's depth or colour; asking for them costs a depth pre-pass
                // with MSAA (every opaque drawn twice). Depth of field gets its depth by itself.
                Set("m_RequireDepthTexture", p => p.boolValue = false);
                Set("m_RequireOpaqueTexture", p => p.boolValue = false);
                var list = so.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = i == Presets.Length - 1 ? withAo : plain;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                assets[i] = a;
            }

            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            levels.arraySize = Presets.Length;
            for (int i = 0; i < Presets.Length; i++)
            {
                var q = Presets[i];
                var l = levels.GetArrayElementAtIndex(i);
                l.FindPropertyRelative("name").stringValue = q.name;
                l.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                l.FindPropertyRelative("globalTextureMipmapLimit").intValue = q.textureLimit;
                l.FindPropertyRelative("anisotropicTextures").intValue = q.anisotropic;
                l.FindPropertyRelative("vSyncCount").intValue = 1;
                l.FindPropertyRelative("antiAliasing").intValue = 0;   // URP's own MSAA setting is used
                l.FindPropertyRelative("realtimeReflectionProbes").boolValue = false;
                l.FindPropertyRelative("lodBias").floatValue = i == 0 ? 0.7f : i == 1 ? 1 : 1.5f;
                var excluded = l.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.arraySize = 0;
            }
            // the default level per platform; the app picks one by the graphics card on first start
            var defaults = qs.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var e = defaults.GetArrayElementAtIndex(i);
                var first = e.FindPropertyRelative("first");
                var second = e.FindPropertyRelative("second");
                if (first == null || second == null) continue;
                second.intValue = first.stringValue == "Standalone" ? 2 : 1;
            }
            qs.FindProperty("m_CurrentQuality").intValue = 2;
            qs.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = assets[2];
            AssetDatabase.SaveAssets();
            Debug.Log("Quality presets built");
        }
    }
}
