// Cuts the fins out of a sculpted fish into a mesh of their own, to be drawn as thin see-through
// skin (Aquarium/Fish with _MEMBRANE). A sculpt comes as one closed surface whose fins are marked by
// type (uv3.x, tools/fin_map.py in the web version). Only the type is trusted here, and not fully:
// the head has no fins, behind the peduncle there is no body, a sculpt with a body outline of its
// own has no fins inside it, the body is the large connected unmarked parts, and unmarked scraps
// join the fin they touch.
//   - Only the thin part of a marked fin is cut out: the thick root, a narrow band along the body,
//     stays with the opaque body, and a thick spot inside a fin is no root. A triangle with any
//     point on a blade goes with the fin (a long one would stick out of the body as an opaque
//     spike); a third coordinate, skin, turns the fin from just like the body where they meet (0)
//     to thin skin (1), so the seam does not show.
//   - What is connected is one fin, whatever its marks say (they patch up a flowing tail): one type,
//     so it moves as a whole, and one fan of rays around a pivot near its root. From the fan come
//     the two coordinates the shader needs: u from the root to the rim along each ray, and the ray
//     number. u replaces the marks' own and also drives how the fin moves: the root stays put, the
//     rim is free.
//   - Each point moves towards the middle of its own thickness, which thins the fin without
//     squashing the lobes of a folded tail together, and its bumps are smoothed out of the normals.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.EditorTools
{
    public static class FinMembrane
    {
        const float ThinShare = 0.05f;      // a sheet is thinner than this share of the model's length
        const float RootBand = 0.06f;       // beyond this share of it from the body a fin is a fin, however thick
        const float BodyPart = 0.25f;       // an unmarked part this share of the largest is body (the flanks of a flat fish may not connect)
        const float HeadShare = 0.22f;      // this share of the way from the nose to the peduncle has no fins
        const float SkinWidth = 0.06f;      // over this share of it from the seam a fin turns to skin
        const float SkinU = 0.16f;          // a fin with no seam (a pectoral of its own): over this much of u
        const int MinVerts = 30;            // smaller patches are stray marks
        const int ScrapVerts = 120;         // a fin smaller than this and touching another is a part of it
        const float RaySpacing = 0.035f;    // between rays, in shares of the model's length
        const int MinRays = 7, MaxRays = 40;
        const int FanBins = 36;
        const float Flatten = 0.3f;         // what is left of a fin's thickness
        const float NormalPull = 0.9f;      // how far the normals turn to the fin's plane

        // The flanks of this sculpt are as thin as its fins, and its marks cover them: the body's outline
        // by hand, (z, y) in the model, from the nose over the back.
        static readonly Dictionary<string, Vector2[]> Outlines = new Dictionary<string, Vector2[]>
        {
            ["angelfish"] = new[]
            {
                new Vector2(0.306f, 0.095f), new Vector2(0.198f, 0.171f), new Vector2(0.108f, 0.238f), new Vector2(0.027f, 0.275f),
                new Vector2(-0.080f, 0.252f), new Vector2(-0.144f, 0.162f), new Vector2(-0.225f, 0.072f), new Vector2(-0.292f, 0.036f),
                new Vector2(-0.292f, -0.036f), new Vector2(-0.234f, -0.045f), new Vector2(-0.189f, -0.072f), new Vector2(-0.126f, -0.126f),
                new Vector2(-0.045f, -0.162f), new Vector2(0.036f, -0.158f), new Vector2(0.108f, -0.144f), new Vector2(0.171f, -0.108f),
                new Vector2(0.207f, -0.045f), new Vector2(0.234f, 0.009f), new Vector2(0.279f, 0.054f),
            },
        };

        /// <summary>Split a whole fish into the opaque body and the fin mesh (null if it has no fins to cut out).</summary>
        public static void Split(Mesh whole, string key, float fishLength, out Mesh body, out Mesh fins)
        {
            var v = whole.vertices; var n = whole.normals; var tris = whole.triangles;
            var pect = new List<Vector4>(); whole.GetUVs(2, pect);
            var fin = new List<Vector4>(); whole.GetUVs(3, fin);
            int count = v.Length;
            float length = whole.bounds.size.z;

            var weld = Weld(v, out int groups);
            var first = new int[groups];
            for (int i = count - 1; i >= 0; i--) first[weld[i]] = i;
            var near = new HashSet<int>[groups];
            for (int g = 0; g < groups; g++) near[g] = new HashSet<int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = weld[tris[t + e]], b = weld[tris[t + (e + 1) % 3]];
                    if (a != b) { near[a].Add(b); near[b].Add(a); }
                }

            // the type of each point; a pectoral of its own (the paddle direction in uv2) has a type of its own
            float peduncle = -0.47f * fishLength, nose = whole.bounds.max.z, headEnd = nose - HeadShare * (nose - peduncle);
            var type = new int[groups];
            for (int i = 0; i < count; i++)
            {
                int marked = Mathf.FloorToInt(fin[i].x + 1e-3f);
                type[weld[i]] = pect[i].x != 0 ? 6 : v[i].z > headEnd ? 0 : marked == 0 && v[i].z < peduncle ? 1 : marked;
            }

            if (Outlines.TryGetValue(key, out var outline))
                for (int g = 0; g < groups; g++)
                    if (Inside(outline, new Vector2(v[first[g]].z, v[first[g]].y))) type[g] = 0;

            var blade = ThinPoints(v, n, tris, weld, near, length * ThinShare, out var thickness);
            var fromBody = DistanceFromBody(type, near, v, first);
            AdoptScraps(fromBody, type, near);
            // a blade: marked, and thin or away from the body (a lump in a fin is not its root)
            for (int g = 0; g < groups; g++)
                blade[g] = type[g] != 0 && (blade[g] || fromBody[g] > length * RootBand);
            FillIslands(blade, type, fromBody, near);
            var comps = Components(blade, type, near);
            if (JoinScraps(comps, blade, type, near)) comps = Components(blade, type, near);

            // u and the ray number per point
            var u = new float[groups];
            var ray = new float[groups];
            var bladeNormal = new Vector3[groups];
            foreach (var comp in comps)
                Fan(comp, type[comp[0]], v, first, pect, blade, near, length, peduncle, u, ray, bladeNormal);
            var skin = Skin(comps, blade, u, v, first, near, length * SkinWidth);
            var flat = Thinned(comps, v, first, thickness, skin, near);

            // every vertex takes the values of its point; a marked point outside the blades is a rigid root
            var newV = (Vector3[])v.Clone();
            var newN = (Vector3[])n.Clone();
            var newFin = new List<Vector4>(count);
            for (int i = 0; i < count; i++)
            {
                int g = weld[i];
                newV[i] = flat[g];
                newFin.Add(new Vector4(type[g] + (blade[g] ? 0.98f * u[g] : 0), ray[g], skin[g], 0));
                if (!blade[g]) continue;
                var plane = bladeNormal[g];
                var target = Vector3.Dot(n[i], plane) >= 0 ? plane : -plane;
                newN[i] = Vector3.Lerp(n[i], target, NormalPull * Mathf.SmoothStep(0, 1, skin[g])).normalized;
            }

            var bodyTris = new List<int>(tris.Length);
            var finTris = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var centre = (v[a] + v[b] + v[c]) / 3;
                bool inside = outline != null && Inside(outline, new Vector2(centre.z, centre.y));
                var to = !inside && (blade[weld[a]] || blade[weld[b]] || blade[weld[c]]) ? finTris : bodyTris;
                to.Add(a); to.Add(b); to.Add(c);
            }
            Debug.Log($"FIN {key}: {comps.Count} fins [{string.Join(" ", comps.Select(x => $"{type[x[0]]}:{x.Count}"))}], {finTris.Count / 3} of {tris.Length / 3} triangles");

            body = Part(whole, $"{key}_body", bodyTris, newV, newN, pect, newFin);
            fins = finTris.Count > 0 ? Part(whole, $"{key}_fins", finTris, newV, newN, pect, newFin) : null;
        }

        /// <summary>Which vertices are the same point of the surface (a texture seam splits a vertex).</summary>
        static int[] Weld(Vector3[] v, out int groups)
        {
            var map = new Dictionary<Vector3Int, int>();
            var weld = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 1e5f);
                if (!map.TryGetValue(key, out int g)) map[key] = g = map.Count;
                weld[i] = g;
            }
            groups = map.Count;
            return weld;
        }

        static bool Inside(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y)
                    && p.x < polygon[i].x + (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y))
                    inside = !inside;
            return inside;
        }

        /// <summary>Points with another sheet of the surface close behind them, tidied by their neighbours.</summary>
        static bool[] ThinPoints(Vector3[] v, Vector3[] n, int[] tris, int[] weld, HashSet<int>[] near, float thin, out Vector3[] thickness)
        {
            var grid = new TriGrid(v, tris, thin);
            var flag = new bool[near.Length];
            thickness = new Vector3[near.Length];   // from the point to the sheet behind it
            for (int i = 0; i < v.Length; i++)
            {
                int g = weld[i];
                if (flag[g] || !grid.Hit(v[i], -n[i], thin, weld, g, out float d)) continue;
                flag[g] = true;
                thickness[g] = -n[i] * d;
            }
            // a rim's normal points along the sheet, a stray thin spot lies among thick ones
            for (int pass = 0; pass < 3; pass++)
            {
                var next = (bool[])flag.Clone();
                for (int g = 0; g < flag.Length; g++)
                {
                    if (near[g].Count == 0) continue;
                    float share = near[g].Count(x => flag[x]) / (float)near[g].Count;
                    if (!flag[g] && share >= 0.5f) next[g] = true;
                    else if (flag[g] && share < 0.25f) next[g] = false;
                }
                flag = next;
            }
            return flag;
        }

        /// <summary>Distance along the surface from the body: the large connected parts of what is not marked as a fin.</summary>
        static float[] DistanceFromBody(int[] type, HashSet<int>[] near, Vector3[] v, int[] first)
        {
            var seen = new bool[type.Length];
            var parts = new List<List<int>>();
            var stack = new Stack<int>();
            for (int s = 0; s < type.Length; s++)
            {
                if (seen[s] || type[s] != 0) continue;
                var part = new List<int>();
                seen[s] = true; stack.Push(s);
                while (stack.Count > 0)
                {
                    int g = stack.Pop();
                    part.Add(g);
                    foreach (int x in near[g])
                        if (!seen[x] && type[x] == 0) { seen[x] = true; stack.Push(x); }
                }
                parts.Add(part);
            }
            int largest = parts.Count > 0 ? parts.Max(x => x.Count) : 0;
            var dist = Enumerable.Repeat(float.MaxValue, type.Length).ToArray();
            var queue = new Queue<int>();
            foreach (var part in parts)
                if (part.Count >= BodyPart * largest)
                    foreach (int g in part) { dist[g] = 0; queue.Enqueue(g); }
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                foreach (int x in near[g])
                {
                    float d = dist[g] + Vector3.Distance(v[first[g]], v[first[x]]);
                    if (d < dist[x]) { dist[x] = d; queue.Enqueue(x); }
                }
            }
            return dist;
        }

        /// <summary>Unmarked scraps apart from the body take the type of the fin they touch (a scrap touching none is left alone).</summary>
        static void AdoptScraps(float[] fromBody, int[] type, HashSet<int>[] near)
        {
            var queue = new Queue<int>();
            for (int g = 0; g < type.Length; g++)
                if (type[g] != 0) queue.Enqueue(g);
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                foreach (int x in near[g])
                    if (type[x] == 0 && fromBody[x] > 0) { type[x] = type[g]; queue.Enqueue(x); }
            }
        }

        /// <summary>A thick spot inside a fin is no root: a marked point joins the blades unless it reaches the body through points like itself.</summary>
        static void FillIslands(bool[] blade, int[] type, float[] fromBody, HashSet<int>[] near)
        {
            var root = new bool[blade.Length];
            var stack = new Stack<int>();
            for (int g = 0; g < blade.Length; g++)
                if (fromBody[g] == 0) { root[g] = true; stack.Push(g); }
            while (stack.Count > 0)
                foreach (int x in near[stack.Pop()])
                    if (!root[x] && !blade[x] && type[x] != 0) { root[x] = true; stack.Push(x); }
            for (int g = 0; g < blade.Length; g++)
                if (type[g] != 0 && !blade[g] && !root[g]) blade[g] = true;
        }

        /// <summary>
        /// The connected fins, one list of points each; small patches lose their blade mark. The median
        /// fins connect whatever their marks, and the whole fin takes one type: the tail's if it has
        /// a good part of one, otherwise the commonest.
        /// </summary>
        static List<List<int>> Components(bool[] blade, int[] type, HashSet<int>[] near)
        {
            int Kind(int t) => t >= 1 && t <= 3 ? 1 : t;
            var seen = new bool[blade.Length];
            var result = new List<List<int>>();
            var stack = new Stack<int>();
            for (int s = 0; s < blade.Length; s++)
            {
                if (!blade[s] || seen[s]) continue;
                var comp = new List<int>();
                seen[s] = true; stack.Push(s);
                while (stack.Count > 0)
                {
                    int g = stack.Pop();
                    comp.Add(g);
                    foreach (int x in near[g])
                        if (blade[x] && !seen[x] && Kind(type[x]) == Kind(type[s])) { seen[x] = true; stack.Push(x); }
                }
                if (comp.Count < MinVerts)
                {
                    foreach (int g in comp) blade[g] = false;
                    continue;
                }
                var byType = comp.GroupBy(g => type[g]).OrderByDescending(x => x.Count()).ToList();
                int whole = byType.Any(x => x.Key == 1 && x.Count() >= 0.3f * comp.Count) ? 1 : byType[0].Key;
                foreach (int g in comp) type[g] = whole;
                result.Add(comp);
            }
            return result;
        }

        /// <summary>A small fin touching a larger one takes its type (patchy marks would give it a fan of its own). True if any did.</summary>
        static bool JoinScraps(List<List<int>> comps, bool[] blade, int[] type, HashSet<int>[] near)
        {
            var owner = new int[blade.Length];
            for (int c = 0; c < comps.Count; c++)
                foreach (int g in comps[c]) owner[g] = c;
            bool any = false;
            for (int c = 0; c < comps.Count; c++)
            {
                if (comps[c].Count >= ScrapVerts) continue;
                int best = -1;
                foreach (int g in comps[c])
                    foreach (int x in near[g])
                        if (blade[x] && owner[x] != c && comps[owner[x]].Count > comps[c].Count && (best < 0 || comps[owner[x]].Count > comps[best].Count)) best = owner[x];
                if (best < 0) continue;
                foreach (int g in comps[c]) type[g] = type[comps[best][0]];
                any = true;
            }
            return any;
        }

        /// <summary>The fin's plane and fan of rays: writes u, the ray number and the plane's normal per point.</summary>
        static void Fan(List<int> comp, int finType, Vector3[] v, int[] first, List<Vector4> pect, bool[] blade, HashSet<int>[] near, float length, float peduncle,
            float[] u, float[] ray, Vector3[] bladeNormal)
        {
            bool median = finType >= 1 && finType <= 3;
            Vector3 center = Vector3.zero;
            foreach (int g in comp) center += v[first[g]];
            center /= comp.Count;
            // the median fins stand in the fish's own plane, the paired ones in whatever plane they lie
            Vector3 e1, e2, normal;
            if (median) { e1 = Vector3.forward; e2 = Vector3.up; normal = Vector3.right; center = Vector3.zero; }
            else PlaneOf(comp, v, first, center, out e1, out e2, out normal);
            Vector2 To2(Vector3 p) => new Vector2(Vector3.Dot(p - center, e1), Vector3.Dot(p - center, e2));

            // the root: where the fin meets what stays opaque
            var rootPts = new List<Vector2>();
            foreach (int g in comp)
                if (near[g].Any(x => !blade[x])) rootPts.Add(To2(v[first[g]]));
            Vector2 root;
            if (finType == 6) root = To2(new Vector3(pect[first[comp[0]]].y, pect[first[comp[0]]].z, pect[first[comp[0]]].w));
            else if (rootPts.Count > 0) root = rootPts.Aggregate(Vector2.zero, (a, b) => a + b) / rootPts.Count;
            else root = comp.Select(g => To2(v[first[g]])).OrderBy(p => p.sqrMagnitude).First();

            Vector2 main = Vector2.zero; float reach = 0;
            foreach (int g in comp)
            {
                var d = To2(v[first[g]]) - root;
                reach = Mathf.Max(reach, d.magnitude);
                if (d.sqrMagnitude > 1e-10f) main += d.normalized;
            }
            main = main.sqrMagnitude > 1e-10f ? main.normalized : Vector2.up;

            // the rays come out of a point behind the root: the tail's from the peduncle, the dorsal's
            // and the anal's lean back from ahead of it
            Vector2 pivot = root - main * (0.15f * reach);
            if (finType == 1) pivot = new Vector2(peduncle + 0.1f * reach, 0);
            if ((finType == 2 || finType == 3) && rootPts.Count > 0)
            {
                float front = rootPts.Max(p => p.x), back = rootPts.Min(p => p.x), run = front - back;
                float side = Mathf.Sign(comp.Average(g => To2(v[first[g]]).y) - root.y + 1e-6f);
                pivot = new Vector2(front + 0.2f * run, root.y - side * Mathf.Max(0.7f * run, 0.15f * reach));
                main = (comp.Select(g => To2(v[first[g]]) - pivot).Aggregate(Vector2.zero, (a, b) => a + b.normalized)).normalized;
            }

            float Angle(Vector2 p) { var q = p - pivot; return Mathf.Atan2(main.x * q.y - main.y * q.x, Vector2.Dot(main, q)); }
            float lo = float.MaxValue, hi = float.MinValue, sumR = 0;
            foreach (int g in comp)
            {
                var p = To2(v[first[g]]);
                float a = Angle(p);
                lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a);
                sumR += (p - pivot).magnitude;
            }
            float span = Mathf.Max(hi - lo, 1e-4f);

            // each direction's nearest and farthest point: the root and the rim along that ray
            var inner = Enumerable.Repeat(float.MaxValue, FanBins).ToArray();
            var outer = Enumerable.Repeat(float.MinValue, FanBins).ToArray();
            int Bin(float a) => Mathf.Clamp((int)((a - lo) / span * FanBins), 0, FanBins - 1);
            foreach (int g in comp)
            {
                var p = To2(v[first[g]]);
                int b = Bin(Angle(p));
                float r = (p - pivot).magnitude;
                inner[b] = Mathf.Min(inner[b], r); outer[b] = Mathf.Max(outer[b], r);
            }
            Smooth(inner, float.MaxValue);
            Smooth(outer, float.MinValue);
            float At(float[] f, float a)
            {
                float x = Mathf.Clamp((a - lo) / span * FanBins - 0.5f, 0, FanBins - 1);
                int i = Mathf.Min((int)x, FanBins - 2);
                return Mathf.Lerp(f[i], f[i + 1], x - i);
            }

            int rays = Mathf.Clamp(Mathf.RoundToInt(span * (sumR / comp.Count) / (RaySpacing * length)), MinRays, MaxRays);
            float RayAt(float a)
            {
                float x = (Mathf.Clamp(a, lo, hi) - lo) / span * rays;
                return x + 0.12f * Mathf.Sin(x * 1.7f);   // not quite evenly spaced
            }
            foreach (int g in comp)
            {
                var p = To2(v[first[g]]);
                float a = Angle(p), r = (p - pivot).magnitude;
                float inR = At(inner, a), outR = Mathf.Max(At(outer, a), inR + 1e-4f);
                u[g] = Mathf.Clamp01((r - inR) / (outR - inR));
                ray[g] = RayAt(a);
                bladeNormal[g] = normal;
            }
            // the points of the root in the fin's triangles stay put, but the rays run on to them
            foreach (int g in comp)
                foreach (int x in near[g])
                    if (!blade[x]) ray[x] = RayAt(Angle(To2(v[first[x]])));
        }

        /// <summary>0 where a fin meets what stays opaque, 1 where it is all skin: by the distance along the surface, or by u if it meets nothing.</summary>
        static float[] Skin(List<List<int>> comps, bool[] blade, float[] u, Vector3[] v, int[] first, HashSet<int>[] near, float width)
        {
            var dist = Enumerable.Repeat(float.MaxValue, blade.Length).ToArray();
            var queue = new Queue<int>();
            for (int g = 0; g < blade.Length; g++)
                if (!blade[g]) { dist[g] = 0; queue.Enqueue(g); }
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                foreach (int x in near[g])
                {
                    if (!blade[x]) continue;
                    float d = dist[g] + Vector3.Distance(v[first[g]], v[first[x]]);
                    if (d < dist[x]) { dist[x] = d; queue.Enqueue(x); }
                }
            }
            var skin = new float[blade.Length];
            foreach (var comp in comps)
            {
                bool seam = comp.Any(g => dist[g] < float.MaxValue);
                foreach (int g in comp)
                    skin[g] = Mathf.Clamp01(seam ? dist[g] / width : u[g] / SkinU);
            }
            return skin;
        }

        /// <summary>Every point of a fin moved towards the middle of its thickness, the moves smoothed along the surface.</summary>
        static Vector3[] Thinned(List<List<int>> comps, Vector3[] v, int[] first, Vector3[] thickness, float[] skin, HashSet<int>[] near)
        {
            var result = new Vector3[first.Length];
            for (int g = 0; g < result.Length; g++) result[g] = v[first[g]];
            var move = new Vector3[first.Length];
            foreach (var comp in comps)
                foreach (int g in comp)
                    move[g] = thickness[g] * 0.5f * (1 - Flatten) * Mathf.SmoothStep(0, 1, skin[g]);
            for (int pass = 0; pass < 2; pass++)
            {
                var c = (Vector3[])move.Clone();
                foreach (var comp in comps)
                    foreach (int g in comp)
                    {
                        var sum = c[g];
                        foreach (int x in near[g]) sum += c[x];
                        move[g] = sum / (near[g].Count + 1);
                    }
            }
            foreach (var comp in comps)
                foreach (int g in comp) result[g] += move[g];
            return result;
        }

        static void Smooth(float[] f, float empty)
        {
            for (int fill = 0; fill < FanBins; fill++)
            {
                bool any = false;
                for (int i = 0; i < FanBins; i++)
                {
                    if (f[i] != empty) continue;
                    any = true;
                    if (i > 0 && f[i - 1] != empty) f[i] = f[i - 1];
                    else if (i < FanBins - 1 && f[i + 1] != empty) f[i] = f[i + 1];
                }
                if (!any) break;
            }
            for (int pass = 0; pass < 2; pass++)
            {
                var c = (float[])f.Clone();
                for (int i = 0; i < FanBins; i++)
                    f[i] = (c[Mathf.Max(0, i - 1)] + 2 * c[i] + c[Mathf.Min(FanBins - 1, i + 1)]) / 4;
            }
        }

        /// <summary>The two longest directions of the points and the shortest as the normal.</summary>
        static void PlaneOf(List<int> comp, Vector3[] v, int[] first, Vector3 center, out Vector3 e1, out Vector3 e2, out Vector3 normal)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (int g in comp)
            {
                var d = v[first[g]] - center;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            Vector3 Mul(Vector3 a) => new Vector3(xx * a.x + xy * a.y + xz * a.z, xy * a.x + yy * a.y + yz * a.z, xz * a.x + yz * a.y + zz * a.z);
            e1 = new Vector3(0.31f, 0.87f, 0.39f);
            for (int i = 0; i < 24; i++) e1 = Mul(e1).normalized;
            e2 = Vector3.Cross(e1, new Vector3(0.53f, 0.21f, -0.82f)).normalized;
            for (int i = 0; i < 24; i++)
            {
                var m = Mul(e2);
                e2 = (m - e1 * Vector3.Dot(m, e1)).normalized;
            }
            normal = Vector3.Cross(e1, e2).normalized;
        }

        /// <summary>A mesh of some of the triangles, with only the vertices they use.</summary>
        static Mesh Part(Mesh whole, string name, List<int> tris, Vector3[] v, Vector3[] n, List<Vector4> pect, List<Vector4> fin)
        {
            var uv = whole.uv; var color = whole.colors;
            var map = new Dictionary<int, int>();
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUv = new List<Vector2>(); var outC = new List<Color>();
            var outP = new List<Vector4>(); var outF = new List<Vector4>(); var outT = new List<int>(tris.Count);
            foreach (int i in tris)
            {
                if (!map.TryGetValue(i, out int k))
                {
                    map[i] = k = outV.Count;
                    outV.Add(v[i]); outN.Add(n[i]); outUv.Add(uv[i]); outC.Add(color[i]); outP.Add(pect[i]); outF.Add(fin[i]);
                }
                outT.Add(k);
            }
            var m = new Mesh { name = name, indexFormat = outV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(outV); m.SetNormals(outN); m.SetUVs(0, outUv); m.SetColors(outC); m.SetUVs(2, outP); m.SetUVs(3, outF); m.SetTriangles(outT, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The triangles in a grid of cells, for short rays.</summary>
        sealed class TriGrid
        {
            readonly Vector3[] v; readonly int[] tris; readonly float cell;
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();

            public TriGrid(Vector3[] v, int[] tris, float cell)
            {
                this.v = v; this.tris = tris; this.cell = cell;
                for (int t = 0; t < tris.Length; t += 3)
                {
                    var a = v[tris[t]]; var b = v[tris[t + 1]]; var c = v[tris[t + 2]];
                    var lo = Vector3Int.FloorToInt(Vector3.Min(a, Vector3.Min(b, c)) / cell);
                    var hi = Vector3Int.FloorToInt(Vector3.Max(a, Vector3.Max(b, c)) / cell);
                    for (int x = lo.x; x <= hi.x; x++)
                        for (int y = lo.y; y <= hi.y; y++)
                            for (int z = lo.z; z <= hi.z; z++)
                            {
                                var key = new Vector3Int(x, y, z);
                                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                                list.Add(t);
                            }
                }
            }

            /// <summary>Does the ray meet a triangle within reach, other than those around the point it starts from?</summary>
            public bool Hit(Vector3 from, Vector3 dir, float reach, int[] weld, int self, out float distance)
            {
                distance = float.MaxValue;
                float step = cell * 0.5f;
                for (float s = 0; s <= reach + step; s += step)
                {
                    if (!cells.TryGetValue(Vector3Int.FloorToInt((from + dir * s) / cell), out var list)) continue;
                    foreach (int t in list)
                    {
                        if (weld[tris[t]] == self || weld[tris[t + 1]] == self || weld[tris[t + 2]] == self) continue;
                        if (Ray(from, dir, v[tris[t]], v[tris[t + 1]], v[tris[t + 2]], out float d) && d > 1e-5f && d <= reach) distance = Mathf.Min(distance, d);
                    }
                    if (distance <= s + step) return true;
                }
                return distance <= reach;
            }

            static bool Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
            {
                t = 0;
                Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(d, ac);
                float det = Vector3.Dot(ab, p);
                if (Mathf.Abs(det) < 1e-12f) return false;
                float inv = 1 / det;
                Vector3 s = o - a;
                float x = Vector3.Dot(s, p) * inv;
                if (x < 0 || x > 1) return false;
                Vector3 q = Vector3.Cross(s, ab);
                float y = Vector3.Dot(d, q) * inv;
                if (y < 0 || x + y > 1) return false;
                t = Vector3.Dot(ac, q) * inv;
                return true;
            }
        }
    }
}
