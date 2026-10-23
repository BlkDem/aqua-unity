// Materials of the generated scene: the Blender models' glTF materials turned into Aquarium/Lit
// (with sway for Sway* parts, glow for Glow* parts, glass for Glass* parts — the same node-name
// roles as in the web version), and the tank's own materials. Saved as assets so the scene
// references stay stable between builds.
using System.Collections.Generic;
using System.IO;
using Aquarium.World;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static class Materials
    {
        public const string Dir = "Assets/Aquarium/Art/Generated/Materials";

        /// <summary>How strongly Sway* parts of each model bend in the current (web: decorAssets.js SWAY).</summary>
        public static readonly Dictionary<string, float> Sway = new Dictionary<string, float>
        {
            ["castle"] = 2.0f, ["ship"] = 0.25f, ["vallisneria"] = 0.55f, ["sword"] = 0.22f, ["crypt"] = 0.18f, ["anubias"] = 0.08f,
            ["limnophila"] = 0.45f, ["cabomba"] = 0.6f, ["ludwigia"] = 0.4f, ["pistia"] = 0, ["corals"] = 3.5f,
        };

        static Shader lit, glass;
        public static Shader Lit => lit ? lit : lit = Shader.Find("Aquarium/Lit");
        public static Shader GlassShader => glass ? glass : glass = Shader.Find("Aquarium/Glass");

        static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();

        public static void Reset() => made.Clear();

        /// <summary>Create (or overwrite) a material asset.</summary>
        public static Material Save(string name, Material m)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old)
            {
                old.shader = m.shader;
                old.CopyPropertiesFromMaterial(m);
                old.shaderKeywords = m.shaderKeywords;
                old.renderQueue = m.renderQueue;
                old.enableInstancing = m.enableInstancing;
                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(m);
                return old;
            }
            m.name = name;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static Material NewLit(string name, Color color, float rough = 0.8f, float metal = 0, Texture map = null,
            Color? emission = null, bool fog = true, float caustics = 1, Texture normal = null, float normalScale = 1)
        {
            var m = new Material(Lit);
            m.SetColor("_BaseColor", color);
            if (map) m.SetTexture("_BaseMap", map);
            m.SetFloat("_Roughness", rough);
            m.SetFloat("_Metallic", metal);
            m.SetColor("_EmissionColor", emission ?? Color.black);
            m.SetFloat("_UseFog", fog ? 1 : 0);
            m.SetFloat("_CausticStrength", caustics);
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            m.enableInstancing = true;
            return Save(name, m);
        }

        public static Material NewGlass(string name, Color tint, float reflect = 0.6f, float glint = 2)
        {
            var m = new Material(GlassShader);
            m.SetColor("_Tint", tint);
            m.SetFloat("_Reflect", reflect);
            m.SetFloat("_Glint", glint);
            return Save(name, m);
        }

        public static Material NewShader(string name, string shader)
        {
            var m = new Material(Shader.Find(shader)) { enableInstancing = true };
            return Save(name, m);
        }

        /// <summary>Which role a mesh plays in its model, from its own or an ancestor's name.</summary>
        public static string RoleOf(Transform t, Transform root)
        {
            for (var p = t; p != null; p = p.parent)
            {
                foreach (var r in new[] { "Glow", "Sway", "Glass" })
                    if (p.name.StartsWith(r)) return r;
                if (p == root) break;
            }
            return "Static";
        }

        /// <summary>The aquarium version of a model's glTF material (made once per model and material).</summary>
        public static Material FromGltf(Material src, string model, string role)
        {
            string key = $"{model}_{src.name}_{role}";
            if (made.TryGetValue(key, out var done)) return done;
            Material m;
            if (role == "Glass")
            {
                var c = src.GetColor("baseColorFactor");
                m = NewGlass(key, new Color(c.r, c.g, c.b, Mathf.Min(c.a, 0.75f) * 0.5f), 0.8f, 3);
            }
            else
            {
                m = new Material(Lit);
                m.SetColor("_BaseColor", src.GetColor("baseColorFactor"));
                var baseTex = src.GetTexture("baseColorTexture");
                if (baseTex) m.SetTexture("_BaseMap", baseTex);
                var orm = src.GetTexture("metallicRoughnessTexture");
                if (orm) m.SetTexture("_OrmMap", orm);
                m.SetFloat("_Roughness", src.GetFloat("roughnessFactor"));
                m.SetFloat("_Metallic", src.GetFloat("metallicFactor"));
                var n = src.GetTexture("normalTexture");
                if (n)
                {
                    m.SetTexture("_BumpMap", n);
                    m.SetFloat("_BumpScale", src.GetFloat("normalTexture_scale"));
                    m.EnableKeyword("_NORMALMAP");
                }
                var e = src.GetColor("emissiveFactor");
                var et = src.GetTexture("emissiveTexture");
                if (et) m.SetTexture("_EmissionMap", et);
                m.SetColor("_EmissionColor", role == "Glow" ? e * 0.15f : e);
                // textures with alpha (feathery leaves) are cutouts: no sorting, no blending
                bool cutout = src.GetFloat("_AlphaClip") > 0.5f || src.GetFloat("_Surface") > 0.5f;
                if (cutout)
                {
                    m.SetFloat("_AlphaClip", 1);
                    m.SetFloat("_Cutoff", src.GetFloat("alphaCutoff") > 0 ? src.GetFloat("alphaCutoff") : 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.renderQueue = 2450;
                }
                m.SetFloat("_Cull", 0);   // glTF double-sided, as in the web version
                m.SetFloat("_CausticStrength", role == "Glow" ? 0 : 1);
                m.SetFloat("_Sway", role == "Sway" ? (Sway.TryGetValue(model, out var s) ? s : 0.6f) : 0);
                m.enableInstancing = true;
                m = Save(key, m);
            }
            made[key] = m;
            return m;
        }

        /// <summary>The model's glTF emissive colour for a Glow material (DayCycle scales it by time of day).</summary>
        public static Color GltfEmission(Material converted) => converted.GetColor("_EmissionColor") / 0.15f;
    }
}
