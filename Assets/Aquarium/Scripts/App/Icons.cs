// The interface's icons, drawn as vectors (Painter2D) from the same SVG paths as the web
// version's ui.js, in a 24x24 box; colour comes from the element's style (color).
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aquarium.App
{
    public sealed class Icon : VisualElement
    {
        /// <summary>One drawing step: a path (SVG d), a circle or a rounded rectangle, stroked or filled.</summary>
        public readonly struct Part
        {
            public readonly string d;
            public readonly Vector4 shape;   // circle: cx, cy, r, -1; rect: x, y, w, h (with radius)
            public readonly float radius, stroke;   // stroke 0: filled
            public readonly bool rect;

            public Part(string d, float stroke) { this.d = d; this.stroke = stroke; shape = default; radius = 0; rect = false; }
            public Part(float cx, float cy, float r, float stroke) { d = null; shape = new Vector4(cx, cy, r, -1); this.stroke = stroke; radius = 0; rect = false; }
            public Part(float x, float y, float w, float h, float rx, float stroke) { d = null; shape = new Vector4(x, y, w, h); radius = rx; this.stroke = stroke; rect = true; }
        }

        static Part P(string d, float stroke = 0) => new Part(d, stroke);
        static Part C(float cx, float cy, float r, float stroke = 0) => new Part(cx, cy, r, stroke);

        public static readonly Dictionary<string, Part[]> All = new Dictionary<string, Part[]>
        {
            ["full"] = new[] { P("M4 9V4h5M15 4h5v5M20 15v5h-5M9 20H4v-5", 1.9f) },
            ["unfull"] = new[] { P("M9 4v5H4M20 9h-5V4M15 20v-5h5M4 15h5v5", 1.9f) },
            ["feed"] = new[] { C(7, 8, 1.6f), C(12, 5.5f, 1.6f), C(16.5f, 9, 1.6f), C(10, 12, 1.6f), C(14, 15, 1.6f), C(9, 18.5f, 1.6f) },
            ["watch"] = new[] { P("M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z", 1.8f), C(12, 12, 3) },
            ["cine"] = new[] { new Part(3, 6, 13, 12, 2.5f, 1.8f), P("M16 10.5l5-3v9l-5-3z") },
            ["cam"] = new[] { P("M12 3l8 4.5v9L12 21l-8-4.5v-9z", 1.8f), P("M12 12l8-4.5M12 12v9M12 12L4 7.5", 1.4f) },
            ["fish"] = new[] { P("M3 12c3-5 9-6.5 13-3l4-3v12l-4-3c-4 3.5-10 2-13-3z", 1.8f), C(8, 11, 1.2f) },
            ["sun"] = new[] { C(12, 12, 4), P("M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1", 1.8f) },
            ["moon"] = new[] { P("M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z") },
            ["gear"] = new[] { C(12, 12, 3.2f, 1.8f), P("M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1", 1.8f) },
            // stand-ins for symbols the font does not have
            ["close"] = new[] { P("M6 6L18 18M18 6L6 18", 2.2f) },
            ["edit"] = new[] { P("M4 20l1-4.5L15.5 5l3.5 3.5L8.5 19z", 1.7f), P("M13.5 7l3.5 3.5", 1.7f) },
            ["star"] = new[] { P("M12 2L14.2 9.8L22 12L14.2 14.2L12 22L9.8 14.2L2 12L9.8 9.8z") },
            ["female"] = new[] { C(12, 9, 5, 2.2f), P("M12 14v8M8.5 18.5h7", 2.2f) },
            ["male"] = new[] { C(10, 14, 5, 2.2f), P("M13.5 10.5L20 4M15 4h5v5", 2.2f) },
        };

        string iconName;

        public Icon(string name)
        {
            AddToClassList("icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            Set(name);
        }

        public void Set(string n)
        {
            if (n == iconName) return;
            iconName = n;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            if (iconName == null || !All.TryGetValue(iconName, out var parts)) return;
            var r = contentRect;
            float k = Mathf.Min(r.width, r.height) / 24;
            var o = new Vector2(r.x + (r.width - 24 * k) / 2, r.y + (r.height - 24 * k) / 2);
            Vector2 T(Vector2 p) => o + p * k;
            var p2 = ctx.painter2D;
            var col = resolvedStyle.color;
            p2.fillColor = col;
            p2.strokeColor = col;
            p2.lineJoin = LineJoin.Round;
            p2.lineCap = LineCap.Round;
            foreach (var part in parts)
            {
                p2.lineWidth = part.stroke * k;
                p2.BeginPath();
                if (part.d != null) Path(p2, part.d, T);
                else if (part.rect)
                {
                    var s = part.shape;
                    float rx = part.radius;
                    p2.MoveTo(T(new Vector2(s.x + rx, s.y)));
                    p2.ArcTo(T(new Vector2(s.x + s.z, s.y)), T(new Vector2(s.x + s.z, s.y + s.w)), rx * k);
                    p2.ArcTo(T(new Vector2(s.x + s.z, s.y + s.w)), T(new Vector2(s.x, s.y + s.w)), rx * k);
                    p2.ArcTo(T(new Vector2(s.x, s.y + s.w)), T(new Vector2(s.x, s.y)), rx * k);
                    p2.ArcTo(T(new Vector2(s.x, s.y)), T(new Vector2(s.x + s.z, s.y)), rx * k);
                    p2.ClosePath();
                }
                else p2.Arc(T(new Vector2(part.shape.x, part.shape.y)), part.shape.z * k, Angle.Degrees(0), Angle.Degrees(360));
                if (part.stroke > 0) p2.Stroke(); else p2.Fill();
            }
        }

        /// <summary>Just enough of SVG path syntax for these icons: M L H V C S A Z, absolute and relative.</summary>
        static void Path(Painter2D p, string d, System.Func<Vector2, Vector2> T)
        {
            var nums = new List<float>();
            Vector2 cur = default, start = default, lastCtrl = default;
            char cmd = ' ', prev = ' ';
            int i = 0;
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsLetter(ch)) { cmd = ch; i++; nums.Clear(); ReadNums(d, ref i, nums); Run(); prev = cmd; continue; }
                i++;
            }

            void Run()
            {
                bool rel = char.IsLower(cmd);
                char c = char.ToUpperInvariant(cmd);
                Vector2 Pt(int j) => new Vector2(nums[j], nums[j + 1]) + (rel ? cur : Vector2.zero);
                switch (c)
                {
                    case 'Z': p.ClosePath(); cur = start; break;
                    case 'M':
                        for (int j = 0; j + 1 < nums.Count; j += 2)
                        {
                            var q = Pt(j);
                            if (j == 0) { p.MoveTo(T(q)); start = q; } else p.LineTo(T(q));
                            cur = q;
                        }
                        break;
                    case 'L':
                        for (int j = 0; j + 1 < nums.Count; j += 2) { cur = Pt(j); p.LineTo(T(cur)); }
                        break;
                    case 'H':
                        foreach (var v in nums) { cur = new Vector2(rel ? cur.x + v : v, cur.y); p.LineTo(T(cur)); }
                        break;
                    case 'V':
                        foreach (var v in nums) { cur = new Vector2(cur.x, rel ? cur.y + v : v); p.LineTo(T(cur)); }
                        break;
                    case 'C':
                        for (int j = 0; j + 5 < nums.Count; j += 6)
                        {
                            Vector2 a = Pt(j), b = Pt(j + 2), e = Pt(j + 4);
                            p.BezierCurveTo(T(a), T(b), T(e));
                            lastCtrl = b; cur = e;
                        }
                        break;
                    case 'S':
                        for (int j = 0; j + 3 < nums.Count; j += 4)
                        {
                            char pc = char.ToUpperInvariant(prev);
                            var a = pc == 'C' || pc == 'S' ? 2 * cur - lastCtrl : cur;
                            Vector2 b = Pt(j), e = Pt(j + 2);
                            p.BezierCurveTo(T(a), T(b), T(e));
                            lastCtrl = b; cur = e; prev = 'S';
                        }
                        break;
                    case 'A':
                        // only the circular arcs these icons use: approximated by a quadratic through the bulge
                        for (int j = 0; j + 6 < nums.Count; j += 7)
                        {
                            float rr = nums[j];
                            bool large = nums[j + 3] > 0.5f, sweep = nums[j + 4] > 0.5f;
                            var e = new Vector2(nums[j + 5], nums[j + 6]) + (rel ? cur : Vector2.zero);
                            ArcSvg(p, T, cur, e, rr, large, sweep);
                            cur = e;
                        }
                        break;
                }
            }
        }

        static void ArcSvg(Painter2D p, System.Func<Vector2, Vector2> T, Vector2 a, Vector2 b, float r, bool large, bool sweep)
        {
            // centre of the circle through a and b with radius r (SVG's endpoint parameterisation)
            var mid = (a + b) / 2;
            var half = (b - a) / 2;
            float h2 = Mathf.Max(0, r * r - half.sqrMagnitude), h = Mathf.Sqrt(h2);
            var n = new Vector2(-half.y, half.x).normalized;
            var c = mid + n * h * (large == sweep ? -1 : 1);
            float a0 = Mathf.Atan2(a.y - c.y, a.x - c.x), a1 = Mathf.Atan2(b.y - c.y, b.x - c.x);
            float da = a1 - a0;
            if (sweep && da < 0) da += Mathf.PI * 2;
            if (!sweep && da > 0) da -= Mathf.PI * 2;
            int steps = Mathf.CeilToInt(Mathf.Abs(da) / 0.2f);
            for (int s = 1; s <= steps; s++)
            {
                float t = a0 + da * s / steps;
                p.LineTo(T(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r));
            }
        }

        static void ReadNums(string d, ref int i, List<float> nums)
        {
            while (i < d.Length)
            {
                char ch = d[i];
                if (char.IsLetter(ch)) return;
                if (ch == ' ' || ch == ',') { i++; continue; }
                int s = i;
                if (ch == '-' || ch == '+') i++;
                bool dot = false;
                while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !dot)))
                {
                    if (d[i] == '.') dot = true;
                    i++;
                }
                if (i == s) { i++; continue; }
                nums.Add(float.Parse(d.Substring(s, i - s), CultureInfo.InvariantCulture));
            }
        }
    }
}
