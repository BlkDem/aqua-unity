// Compressed copies of the models' textures. glTFast keeps a .glb's images inside the model as
// uncompressed RGBA (a 2048 texture is 21 MB with mipmaps); here each one is written out once as
// an image next to the others (Art/Textures/<folder>/<model>/: JPEG, or PNG where there is
// transparency; the web version's sources were lossy WebP already) and imported the Unity way —
// BC7 on computers, mipmaps, normal maps as normal maps — and the materials use those.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public enum TexKind { Color, Linear, Normal }

    public static class TextureStore
    {
        const string Models = "Assets/Aquarium/Models/";
        const string Out = "Assets/Aquarium/Art/Textures";
        static readonly Dictionary<Texture, Texture2D> done = new Dictionary<Texture, Texture2D>();

        public static void Reset() => done.Clear();

        /// <summary>The compressed copy of a model's texture (other textures are returned as they are).</summary>
        public static Texture Get(Texture src, TexKind kind, int maxSize = 0)
        {
            if (!src) return null;
            if (done.TryGetValue(src, out var hit)) return hit;
            string path = AssetDatabase.GetAssetPath(src);
            if (!path.StartsWith(Models) || !path.EndsWith(".glb")) return src;
            string dir = $"{Out}/{path.Substring(Models.Length, path.Length - Models.Length - 4)}";
            string stem = $"{dir}/{Safe(src.name)}", file = File.Exists(stem + ".png") ? stem + ".png" : stem + ".jpg";
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(dir);
                var pixels = Read(src, kind != TexKind.Color);
                bool alpha = kind == TexKind.Color && System.Array.Exists(pixels.GetPixels32(), c => c.a < 250);
                file = stem + (alpha ? ".png" : ".jpg");
                File.WriteAllBytes(file, alpha ? pixels.EncodeToPNG() : pixels.EncodeToJPG(95));
                Object.DestroyImmediate(pixels);
                AssetDatabase.ImportAsset(file);
            }
            if (maxSize == 0) maxSize = SizeFor(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(file);
            bool dirty = false;
            void Set<T>(T now, T want, System.Action<T> set) { if (!EqualityComparer<T>.Default.Equals(now, want)) { set(want); dirty = true; } }
            Set(imp.textureType, kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => imp.textureType = v);
            Set(imp.sRGBTexture, kind == TexKind.Color, v => imp.sRGBTexture = v);
            Set(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
            Set(imp.maxTextureSize, maxSize, v => imp.maxTextureSize = v);
            Set(imp.textureCompression, TextureImporterCompression.CompressedHQ, v => imp.textureCompression = v);
            Set(imp.alphaSource, TextureImporterAlphaSource.FromInput, v => imp.alphaSource = v);
            Set(imp.isReadable, false, v => imp.isReadable = v);
            Set(imp.anisoLevel, 4, v => imp.anisoLevel = v);
            if (dirty) imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(file);
            done[src] = tex;
            return tex;
        }

        /// <summary>
        /// How sharp a model's textures need to be: on screen a piece of decor is rarely more than a
        /// few hundred pixels across, a critter a few dozen; the castle and the ship are the big ones.
        /// </summary>
        static int SizeFor(string glb)
        {
            if (glb.Contains("/Critters/")) return 512;
            if (glb.EndsWith("/castle.glb") || glb.EndsWith("/ship.glb")) return 2048;
            return 1024;
        }

        static string Safe(string n)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        /// <summary>The pixels of a (possibly unreadable) texture, through the GPU.</summary>
        static Texture2D Read(Texture src, bool linear)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, linear);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }
    }
}
