// A mouth that opens and gill covers that lift, for a fish that comes as one closed surface with its
// lips and gill covers only modelled on it. Lines measured on each model (Snapshot.HeadLook):
//   - The surface is cut along the line between the lips, and the cut stays closed by a strip of
//     dark inside of the mouth which has no area until the jaws part. The lower jaw turns around the
//     corner of the mouth, the upper one a little the other way, each fading out into the head.
//   - The rear half of the gill cover lifts, fading out towards its middle and just past its edge.
//     No cut here: seen from the side the opening is a slit the shader draws along the same ellipse.
// Per vertex in uv4: (jaw weight, gill weight, 1 inside the mouth); AquariumFishPasses.hlsl moves them
// by the fish's _Face. The simpler meshes do without: this little does not show on them.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.EditorTools
{
    public static class FishFace
    {
        public sealed class Face
        {
            public Vector2[] lips;      // (z, y) in the model: the corner of the mouth, along the lips, on out of the head
            public Vector2 gill, radii; // the gill cover as an ellipse, (z, y) in the model
            public float gape = 0.45f;  // how far the jaw turns, in radians, when the mouth is wide open
            public float slit = 1;      // how much the slit behind the gill cover shows (on a pale body a shadow stands out)
        }

        const float UpperShare = 0.25f;     // the upper jaw's part of the movement
        const float JawFrom = 0.3f, JawTo = 1f;     // the lower jaw fades out over this depth below the lips (shares of the mouth's length)
        const float UpperTo = 0.5f;         // the upper jaw over this height
        const float BehindTo = 0.6f;        // both of them over this much behind the corner
        const float GillFrom = 0.35f, GillPast = 0.18f;     // the gill cover fades out from its edge to this share of its radius, and over this much outside

        static readonly Dictionary<string, Face> Faces = new Dictionary<string, Face>
        {
            ["goldfish"] = new Face { gill = V(0.221f, 0.079f), radii = V(0.072f, 0.0995f), lips = new[] { V(0.4108f, 0.0794f), V(0.4137f, 0.0853f), V(0.4206f, 0.1059f), V(0.4241f, 0.1069f), V(0.44f, 0.11f) } },
            ["betta"] = new Face { gill = V(0.1092f, -0.0101f), radii = V(0.0353f, 0.048f), lips = new[] { V(0.1864f, -0.0175f), V(0.1908f, -0.0078f), V(0.2015f, -0.0002f), V(0.213f, 0.001f) } },
            ["clown"] = new Face { gape = 0.55f, gill = V(0.1886f, 0.0166f), radii = V(0.0469f, 0.0607f), lips = new[] { V(0.2147f, -0.0048f), V(0.2232f, 0.0072f), V(0.238f, 0.012f) } },
            ["angelfish"] = new Face { gill = V(0.1835f, 0.0372f), radii = V(0.08f, 0.067f), lips = new[] { V(0.2819f, 0.0818f), V(0.2904f, 0.0918f), V(0.2976f, 0.0997f), V(0.312f, 0.106f) } },
            ["discus"] = new Face { gill = V(0.2028f, -0.018f), radii = V(0.081f, 0.0864f), lips = new[] { V(0.271f, -0.0504f), V(0.2828f, -0.0442f), V(0.2938f, -0.0389f), V(0.31f, -0.034f) } },
            ["cichlid"] = new Face { gape = 0.35f, gill = V(0.1647f, -0.0139f), radii = V(0.0678f, 0.0766f), lips = new[] { V(0.2265f, -0.0498f), V(0.2372f, -0.0418f), V(0.2532f, -0.0217f), V(0.2693f, -0.007f), V(0.282f, -0.0023f), V(0.2947f, -0.001f) } },
            ["barb"] = new Face { gill = V(0.1262f, -0.0228f), radii = V(0.0456f, 0.0388f), lips = new[] { V(0.1635f, -0.0412f), V(0.1692f, -0.0346f), V(0.1758f, -0.0303f), V(0.1863f, -0.0281f), V(0.192f, -0.0268f) } },
            ["cory"] = new Face { gill = V(0.1344f, -0.0076f), radii = V(0.034f, 0.0302f), lips = new[] { V(0.1966f, -0.0436f), V(0.2044f, -0.0422f), V(0.2092f, -0.0412f), V(0.22f, -0.04f) } },
            ["guppy"] = new Face { gape = 0.55f, gill = V(0.1087f, 0.0065f), radii = V(0.0367f, 0.0324f), lips = new[] { V(0.1685f, 0.0238f), V(0.1751f, 0.0284f), V(0.1797f, 0.03f), V(0.1838f, 0.0304f), V(0.19f, 0.031f) } },
            ["neon"] = new Face { gill = V(0.0966f, -0.0082f), radii = V(0.0398f, 0.0377f), lips = new[] { V(0.1473f, -0.0204f), V(0.1571f, -0.0082f), V(0.1653f, -0.0008f), V(0.172f, 0f), V(0.18f, 0.0005f) } },
            ["rainbow"] = new Face { gape = 0.5f, gill = V(0.1565f, -0.0058f), radii = V(0.0432f, 0.0403f), lips = new[] { V(0.2223f, -0.0085f), V(0.23f, -0.0035f), V(0.2383f, 0f), V(0.2455f, 0.0003f), V(0.255f, 0.0005f) } },
            ["moonfish"] = new Face { slit = 0.3f, gill = V(0.165f, -0.015f), radii = V(0.055f, 0.075f), lips = new[] { V(0.23f, -0.037f), V(0.242f, -0.031f), V(0.256f, -0.025f), V(0.27f, -0.02f) } },
        };

        static Vector2 V(float z, float y) => new Vector2(z, y);

        /// <summary>The face of a model (variants share their species' sculpt), null if it has none.</summary>
        public static Face Get(string key) => Faces.TryGetValue(key.Split('-')[0], out var f) ? f : null;

        public static void SetUp(Material body, Face f)
        {
            body.SetVector("_Jaw", new Vector4(f.lips[0].x, f.lips[0].y, f.gape, f.slit));
            body.SetVector("_Gill", new Vector4(f.gill.x, f.gill.y, f.radii.x, f.radii.y));
        }

        /// <summary>The nearest point of the lips: signed distance (above positive) and, when it lies before the corner, how far.</summary>
        static void Measure(Face f, Vector3 p, out float s, out float behind)
        {
            var q = new Vector2(p.z, p.y);
            float best = float.MaxValue;
            s = 0; behind = 0;
            for (int i = 0; i + 1 < f.lips.Length; i++)
            {
                Vector2 a = f.lips[i], d = f.lips[i + 1] - a;
                float t = Vector2.Dot(q - a, d) / d.sqrMagnitude;
                float along = Mathf.Clamp01(t);
                float dist = (q - (a + d * along)).sqrMagnitude;
                if (dist >= best) continue;
                best = dist;
                s = (d.x * (q.y - a.y) - d.y * (q.x - a.x)) / d.magnitude;
                behind = i == 0 && t < 0 ? -t * d.magnitude : 0;
            }
        }

        static float JawWeight(float s, float behind, float mouth)
        {
            float fade = 1 - Mathf.SmoothStep(0, 1, behind / (BehindTo * mouth));
            if (s > 0) return -UpperShare * (1 - Mathf.SmoothStep(0, 1, s / (UpperTo * mouth))) * fade;
            return (1 - Mathf.SmoothStep(0, 1, (-s - JawFrom * mouth) / ((JawTo - JawFrom) * mouth))) * fade;
        }

        static float GillWeight(Face f, Vector3 p, Vector3 n)
        {
            float z = (p.z - f.gill.x) / f.radii.x, y = (p.y - f.gill.y) / f.radii.y;
            float r = Mathf.Sqrt(z * z + y * y);
            float edge = r < 1 ? Mathf.SmoothStep(0, 1, (r - GillFrom) / (1 - GillFrom)) : 1 - Mathf.SmoothStep(0, 1, (r - 1) / GillPast);
            float rear = Mathf.SmoothStep(0, 1, (-z + 0.15f) / 0.6f);   // the rear half: it hangs from the front
            return edge * rear * Mathf.Clamp01(Mathf.Abs(n.x) * 1.5f);   // not over the back and the belly
        }

        /// <summary>A copy of the body with its face cut in (the original is for the simpler meshes).</summary>
        public static Mesh Cut(Mesh whole, Face f)
        {
            var v = new List<Vector3>(whole.vertices); var n = new List<Vector3>(whole.normals); var uv = new List<Vector2>(whole.uv); var color = new List<Color>(whole.colors);
            var pect = new List<Vector4>(); whole.GetUVs(2, pect);
            var fin = new List<Vector4>(); whole.GetUVs(3, fin);
            var tris = whole.triangles;
            float mouth = 0;
            for (int i = 0; i + 1 < f.lips.Length; i++) mouth += (f.lips[i + 1] - f.lips[i]).magnitude;

            // which side of the lips a vertex is on goes with the vertex, as the faces need it for the weights
            int plain = v.Count;
            var side = new float[plain];
            var behindCorner = new float[plain];
            for (int i = 0; i < plain; i++) { Measure(f, v[i], out side[i], out behindCorner[i]); }

            var jaw = new List<float>(plain);
            var inside = new List<float>(plain);
            for (int i = 0; i < plain; i++) { jaw.Add(JawWeight(side[i], behindCorner[i], mouth)); inside.Add(0); }

            int Add(int a, int b, float t, float weight, float isInside)
            {
                v.Add(Vector3.Lerp(v[a], v[b], t));
                n.Add(Vector3.Lerp(n[a], n[b], t).normalized);
                uv.Add(Vector2.Lerp(uv[a], uv[b], t));
                color.Add(Color.Lerp(color[a], color[b], t));
                pect.Add(Vector4.Lerp(pect[a], pect[b], t));
                fin.Add(Vector4.Lerp(fin[a], fin[b], t));
                jaw.Add(weight);
                inside.Add(isInside);
                return v.Count - 1;
            }

            // a point on the lips, once per edge cut: above and below, on the skin and on the strip
            var cuts = new Dictionary<(int, int), (int up, int down, int stripUp, int stripDown)>();
            (int up, int down, int stripUp, int stripDown) CutEdge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cuts.TryGetValue(key, out var c)) return c;
                float t = side[key.Item1] / (side[key.Item1] - side[key.Item2]);
                c = (Add(key.Item1, key.Item2, t, -UpperShare, 0), Add(key.Item1, key.Item2, t, 1, 0), Add(key.Item1, key.Item2, t, -UpperShare, 1), Add(key.Item1, key.Item2, t, 1, 1));
                cuts[key] = c;
                return c;
            }

            // past the corner the lips are not cut
            bool Cuts(int a, int b)
            {
                if (side[a] > 0 == side[b] > 0) return false;
                Measure(f, Vector3.Lerp(v[a], v[b], side[a] / (side[a] - side[b])), out _, out float past);
                return past <= 0;
            }

            var result = new List<int>(tris.Length + 256);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                bool ua = side[a] > 0, ub = side[b] > 0, uc = side[c] > 0;
                // turn the triangle so that a is alone on its side
                if (ua == ub) { int x = a; a = c; c = b; b = x; }
                else if (ua == uc) { int x = a; a = b; b = c; c = x; }
                bool cutB = Cuts(a, b), cutC = Cuts(a, c);
                if (!cutB && !cutC) { result.AddRange(new[] { a, b, c }); continue; }
                bool alone = side[a] > 0;
                var ab = cutB ? CutEdge(a, b) : default;
                var ac = cutC ? CutEdge(a, c) : default;
                int abA = alone ? ab.up : ab.down, acA = alone ? ac.up : ac.down, abB = alone ? ab.down : ab.up, acB = alone ? ac.down : ac.up;
                int sabA = alone ? ab.stripUp : ab.stripDown, sacA = alone ? ac.stripUp : ac.stripDown, sabB = alone ? ab.stripDown : ab.stripUp, sacB = alone ? ac.stripDown : ac.stripUp;
                if (cutB && cutC) result.AddRange(new[] { a, abA, acA, abB, b, c, abB, c, acB, /* the strip */ sacA, sabA, sabB, sacA, sabB, sacB });
                // the cut ends in this triangle: the strip comes to a point at its far corner
                else if (cutB) result.AddRange(new[] { a, abA, c, abB, b, c, Add(c, c, 0, jaw[c], 1), sabA, sabB });
                else result.AddRange(new[] { a, b, acA, acB, b, c, sacA, Add(b, b, 0, jaw[b], 1), sacB });
            }

            var face = new List<Vector4>(v.Count);
            for (int i = 0; i < v.Count; i++) face.Add(new Vector4(jaw[i], GillWeight(f, v[i], n[i]), inside[i], 0));

            var m = new Mesh { name = whole.name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(color); m.SetUVs(2, pect); m.SetUVs(3, fin); m.SetUVs(4, face); m.SetTriangles(result, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            Debug.Log($"FACE {whole.name}: {cuts.Count} edges cut, {tris.Length / 3} -> {result.Count / 3} triangles");
            return m;
        }
    }
}
