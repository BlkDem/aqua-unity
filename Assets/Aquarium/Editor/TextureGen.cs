// Procedural textures of the tank (the web version paints the same ones on a canvas): sand
// grains and pebbles, a wavy normal map for sand and water, wood grain for the cabinet.
// Saved as PNG assets; a fixed seed keeps every build identical.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static class TextureGen
    {
        public const string Dir = "Assets/Aquarium/Art/Generated/Textures";

        static Color Hsl(float h, float s, float l)
        {
            h = ((h % 360) + 360) % 360 / 360f; s /= 100f; l /= 100f;
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float Ch(float t)
            {
                t = (t % 1 + 1) % 1;
                if (t < 1 / 6f) return p + (q - p) * 6 * t;
                if (t < 0.5f) return q;
                if (t < 2 / 3f) return p + (q - p) * (2 / 3f - t) * 6;
                return p;
            }
            return new Color(Ch(h + 1 / 3f), Ch(h), Ch(h - 1 / 3f));
        }

        sealed class Canvas
        {
            public readonly int W, H;
            public readonly Color[] Px;
            public Canvas(int w, int h, Color fill) { W = w; H = h; Px = new Color[w * h]; for (int i = 0; i < Px.Length; i++) Px[i] = fill; }

            public void Ellipse(float cx, float cy, float rx, float ry, float rot, Color c, float alpha = 1)
            {
                float r = Mathf.Max(rx, ry);
                int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r), y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
                float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float u = (dx * cs + dy * sn) / rx, v = (-dx * sn + dy * cs) / ry;
                    float d = u * u + v * v;
                    if (d > 1) continue;
                    // a little antialiasing at the rim
                    float a = alpha * Mathf.Clamp01((1 - d) * Mathf.Min(rx, ry) * 1.5f);
                    int i = (H - 1 - y) * W + x;   // canvas y runs down, texture rows up
                    Px[i] = Color.Lerp(Px[i], c, a);
                }
            }

            public Texture2D Save(string name, bool linear)
            {
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false, linear);
                t.SetPixels(Px);
                t.Apply();
                Directory.CreateDirectory(Dir);
                string path = $"{Dir}/{name}.png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        static void Settings(string path, bool normal, bool srgb)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        public static Texture2D Sand()
        {
            var rnd = new System.Random(7);
            float R() => (float)rnd.NextDouble();
            var c = new Canvas(1024, 1024, new Color32(0xb5, 0x9a, 0x6e, 255));
            for (int i = 0; i < 90000; i++)
            {
                float x = R() * 1024, y = R() * 1024, r = R() * 1.8f + 0.4f;
                Color col = Hsl(30 + R() * 20, 20 + R() * 35, 38 + R() * 34);
                if (R() < 0.04f) col = Hsl(R() * 360, 15, 30 + R() * 30);
                c.Ellipse(x, y, r, r, 0, col);
            }
            for (int i = 0; i < 260; i++)
            {
                float x = R() * 1024, y = R() * 1024, r = R() * 5 + 2;
                c.Ellipse(x, y, r, r * (0.6f + R() * 0.4f), R() * 3, Hsl(20 + R() * 30, 10 + R() * 25, 35 + R() * 40));
                c.Ellipse(x - r * 0.3f, y - r * 0.3f, r * 0.35f, r * 0.35f, 0, Color.white, 0.25f);
            }
            var tex = c.Save("Sand", false);
            Settings(AssetDatabase.GetAssetPath(tex), false, true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        }

        /// <summary>A seamless wavy normal map (the web version's noiseNormalTexture).</summary>
        public static Texture2D NoiseNormal(string name, int size, float freq)
        {
            float Hgt(float x, float y)
            {
                float u = x / size * Mathf.PI * 2, v = y / size * Mathf.PI * 2, s = 0;
                for (int k = 1; k <= 4; k++)
                    s += Mathf.Sin(u * freq * k * 0.5f + Mathf.Cos(v * k + k)) * Mathf.Cos(v * freq * k * 0.4f + Mathf.Sin(u * k * 1.3f)) / k;
                return s;
            }
            var c = new Canvas(size, size, Color.black);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Hgt(x + 1, y) - Hgt(x - 1, y), dy = Hgt(x, y + 1) - Hgt(x, y - 1);
                var n = new Vector3(-dx * 2, -dy * 2, 1).normalized;
                c.Px[(size - 1 - y) * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
            var tex = c.Save(name, true);
            Settings(AssetDatabase.GetAssetPath(tex), true, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        }

        public static Texture2D Wood()
        {
            var rnd = new System.Random(11);
            float R() => (float)rnd.NextDouble();
            var c = new Canvas(512, 128, new Color32(0x3a, 0x2a, 0x20, 255));
            for (int i = 0; i < 160; i++)
            {
                var col = new Color((90 + R() * 40) / 255f, (60 + R() * 25) / 255f, (40 + R() * 20) / 255f);
                float a = 0.15f + R() * 0.25f, w = R() * 2 + 0.5f, y = R() * 128;
                for (float x = 0; x <= 512; x += 1.5f)
                    c.Ellipse(x, y + Mathf.Sin(x * 0.02f + i) * 3, w / 2, w / 2, 0, col, a * 0.5f);
            }
            var tex = c.Save("Wood", false);
            Settings(AssetDatabase.GetAssetPath(tex), false, true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(tex));
        }
    }
}
