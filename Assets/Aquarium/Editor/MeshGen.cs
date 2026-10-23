// Simple procedural meshes for the tank (the web version builds the same shapes with three.js).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aquarium.EditorTools
{
    public static class MeshGen
    {
        /// <summary>A box centred on the origin.</summary>
        public static Mesh Box(Vector3 size, string name = "Box")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            Vector3 h = size * 0.5f;
            void Face(Vector3 normal, Vector3 u, Vector3 w)
            {
                int b = v.Count;
                Vector3 c = Vector3.Scale(normal, h);
                Vector3 du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                v.Add(c - du - dw); v.Add(c + du - dw); v.Add(c + du + dw); v.Add(c - du + dw);
                for (int i = 0; i < 4; i++) n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A box with rounded edges (a smoothed copy of a subdivided box, like RoundedBoxGeometry).</summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 4, string name = "RoundedBox")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            Vector3 inner = size * 0.5f - Vector3.one * radius;
            int res = seg * 2 + 2;
            // each face is a grid of points on a cube, pushed onto the rounded box
            void Face(Vector3 normal, Vector3 u, Vector3 w)
            {
                int b = v.Count;
                for (int j = 0; j <= res; j++)
                for (int i = 0; i <= res; i++)
                {
                    Vector3 p = normal + u * (i / (float)res * 2 - 1) + w * (j / (float)res * 2 - 1);   // on the unit cube
                    Vector3 core = Vector3.Scale(new Vector3(Mathf.Clamp(p.x, -1, 1), Mathf.Clamp(p.y, -1, 1), Mathf.Clamp(p.z, -1, 1)), inner);
                    Vector3 surf = Vector3.Scale(p, size * 0.5f);
                    Vector3 dir = surf - core;
                    // clamp the point onto the inner box, then push out by the radius along the offset
                    Vector3 c2 = new Vector3(Mathf.Clamp(surf.x, -inner.x, inner.x), Mathf.Clamp(surf.y, -inner.y, inner.y), Mathf.Clamp(surf.z, -inner.z, inner.z));
                    dir = surf - c2;
                    if (dir.sqrMagnitude < 1e-12f) dir = normal;
                    dir.Normalize();
                    v.Add(c2 + dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2(i / (float)res, j / (float)res));
                }
                for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int a = b + j * (res + 1) + i;
                    t.AddRange(new[] { a, a + res + 1, a + 1, a + 1, a + res + 1, a + res + 2 });
                }
            }
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Cylinder(float r0, float r1, float h, int seg, string name = "Cylinder")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r1 + Vector3.up * h / 2); v.Add(d * r0 - Vector3.up * h / 2);
                n.Add(d); n.Add(d);
                uv.Add(new Vector2(i / (float)seg, 1)); uv.Add(new Vector2(i / (float)seg, 0));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            foreach (var (y, r, up) in new[] { (h / 2, r1, 1f), (-h / 2, r0, -1f) })
            {
                int c = v.Count;
                v.Add(new Vector3(0, y, 0)); n.Add(Vector3.up * up); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2;
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); n.Add(Vector3.up * up); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < seg; i++)
                    if (up > 0) t.AddRange(new[] { c, c + i + 2, c + i + 1 }); else t.AddRange(new[] { c, c + i + 1, c + i + 2 });
            }
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Sphere(float r, int seg = 16, int rings = 12, string name = "Sphere")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int j = 0; j <= rings; j++)
            for (int i = 0; i <= seg; i++)
            {
                float th = j / (float)rings * Mathf.PI, ph = i / (float)seg * Mathf.PI * 2;
                var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(d * r); n.Add(d); uv.Add(new Vector2(i / (float)seg, 1 - j / (float)rings));
            }
            for (int j = 0; j < rings; j++)
            for (int i = 0; i < seg; i++)
            {
                int a = j * (seg + 1) + i, b = a + seg + 1;
                t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A horizontal grid over [-w/2, w/2] x [-d/2, d/2] with heights from a function of (x, z), uv 0..1.</summary>
        public static Mesh Grid(float w, float d, int nx, int nz, Func<float, float, float> height, float uvRepeatX = 1, float uvRepeatZ = 1, string name = "Grid")
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            var v = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[v.Length];
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = -w / 2 + w * i / nx, z = -d / 2 + d * j / nz;
                v[j * (nx + 1) + i] = new Vector3(x, height(x, z), z);
                uv[j * (nx + 1) + i] = new Vector2(i / (float)nx * uvRepeatX, j / (float)nz * uvRepeatZ);
            }
            var t = new int[nx * nz * 6];
            int k = 0;
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + nx + 1;
                t[k++] = a; t[k++] = b; t[k++] = a + 1;
                t[k++] = a + 1; t[k++] = b; t[k++] = b + 1;
            }
            m.vertices = v; m.uv = uv; m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A vertical strip along x at z = 0 from y = bottom(x) to top(x).</summary>
        public static Mesh Strip(float w, int nx, Func<float, float> top, float bottom, string name = "Strip")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= nx; i++)
            {
                float x = -w / 2 + w * i / nx;
                v.Add(new Vector3(x, bottom, 0)); v.Add(new Vector3(x, top(x), 0));
                uv.Add(new Vector2(i / (float)nx, 0)); uv.Add(new Vector2(i / (float)nx, 1));
            }
            for (int i = 0; i < nx; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Quad(float w, float h, string name = "Quad")
        {
            var m = new Mesh { name = name };
            m.vertices = new[] { new Vector3(-w / 2, -h / 2), new Vector3(w / 2, -h / 2), new Vector3(w / 2, h / 2), new Vector3(-w / 2, h / 2) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static Mesh Disc(float r, int seg, string name = "Disc")
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < seg; i++) t.AddRange(new[] { 0, i + 2, i + 1 });
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
