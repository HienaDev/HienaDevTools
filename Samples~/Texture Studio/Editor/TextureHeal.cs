using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Paints out spots: each painted area is filled with a piece from somewhere else in the same texture, the one whose
    /// surroundings match best, with its light and colour shifted to meet the edges. Nothing is made up.
    /// </summary>
    public static class TextureHeal
    {
        [Serializable] public class Stroke { public float radius; public bool erase; public List<Vector2> points = new List<Vector2>(); }

        static readonly Vector2Int[] Around = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        /// <summary>Which pixels are painted (strokes are in 0..1 of the picture; the radius in parts of its width).</summary>
        public static bool[] Mask(List<Stroke> strokes, int w, int h)
        {
            var m = new bool[w * h];
            foreach (var s in strokes)
                for (int i = 0; i < s.points.Count; i++)
                {
                    var a = s.points[Mathf.Max(0, i - 1)]; var b = s.points[i];
                    if (Mathf.Abs(a.x - b.x) > 0.5f || Mathf.Abs(a.y - b.y) > 0.5f) a = b;
                    float r = Mathf.Max(0.75f, s.radius * w); Vector2 pa = new Vector2(a.x * w, a.y * h), pb = new Vector2(b.x * w, b.y * h);
                    int x0 = Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) - r), x1 = Mathf.CeilToInt(Mathf.Max(pa.x, pb.x) + r);
                    int y0 = Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) - r), y1 = Mathf.CeilToInt(Mathf.Max(pa.y, pb.y) + r);
                    var d = pb - pa; float dd = d.sqrMagnitude;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var q = new Vector2(x + 0.5f, y + 0.5f); float t = dd > 0 ? Mathf.Clamp01(Vector2.Dot(q - pa, d) / dd) : 0;
                            if ((q - pa - d * t).sqrMagnitude <= r * r) m[Wrap(y, h) * w + Wrap(x, w)] = !s.erase;
                        }
                }
            return m;
        }

        public static Pixels Heal(Pixels p, List<Stroke> strokes)
        {
            if (strokes == null || strokes.Count == 0) return p;
            return HealMask(p, Mask(strokes, p.w, p.h));
        }

        /// <summary>Fills every marked area with a matching piece from elsewhere.</summary>
        public static Pixels HealMask(Pixels p, bool[] mask)
        {
            var r = p.Copy();
            // where to copy from is decided on a small version, so the preview and the saved texture agree
            float k = Mathf.Min(1f, 160f / Mathf.Max(p.w, p.h));
            var small = k < 1 ? p.Resized(Mathf.Max(8, Mathf.RoundToInt(p.w * k)), Mathf.Max(8, Mathf.RoundToInt(p.h * k))) : p;
            var smallMask = k < 1 ? Shrink(mask, p.w, p.h, small.w, small.h) : mask;
            var smallAreas = Areas(smallMask, small.w, small.h);
            foreach (var area in Areas(mask, p.w, p.h))
            {
                List<Vector2Int> sa = null; float near = float.MaxValue; var mid = Centre(area, p.w, p.h);
                foreach (var a in smallAreas) { var d = Delta(Centre(a, small.w, small.h), mid); if (d < near) { near = d; sa = a; } }
                var off = sa != null ? Find(small, smallMask, sa, false, Vector2Int.zero, 0) : Vector2Int.zero;
                var full = new Vector2Int(Mathf.RoundToInt(off.x / k), Mathf.RoundToInt(off.y / k));
                if (k < 1 && off != Vector2Int.zero) full = Find(p, mask, area, true, full, Mathf.CeilToInt(1.5f / k));
                if (full != Vector2Int.zero) Fill(p, r, mask, area, full);
            }
            return r;
        }

        // a small pixel counts as marked when any of it is
        static bool[] Shrink(bool[] m, int w, int h, int sw, int sh)
        {
            var s = new bool[sw * sh];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (m[y * w + x]) s[Mathf.Min(sh - 1, y * sh / h) * sw + Mathf.Min(sw - 1, x * sw / w)] = true;
            return s;
        }

        static Vector2 Centre(List<Vector2Int> a, int w, int h) { Vector2 s = Vector2.zero; foreach (var v in a) s += v; s /= a.Count; return new Vector2(Mathf.Repeat(s.x / w, 1), Mathf.Repeat(s.y / h, 1)); }
        static float Delta(Vector2 a, Vector2 b) { float x = Mathf.Abs(a.x - b.x), y = Mathf.Abs(a.y - b.y); x = Mathf.Min(x, 1 - x); y = Mathf.Min(y, 1 - y); return x * x + y * y; }
        static int Wrap(int v, int n) { v %= n; return v < 0 ? v + n : v; }

        // the separate painted areas (one that goes over the edge stays in one piece: its pixels can be outside 0..w)
        static List<List<Vector2Int>> Areas(bool[] m, int w, int h)
        {
            var seen = new bool[m.Length]; var list = new List<List<Vector2Int>>(); var todo = new Stack<Vector2Int>();
            for (int i = 0; i < m.Length; i++)
            {
                if (!m[i] || seen[i]) continue;
                var a = new List<Vector2Int>(); todo.Push(new Vector2Int(i % w, i / w)); seen[i] = true;
                while (todo.Count > 0)
                {
                    var v = todo.Pop(); a.Add(v);
                    foreach (var d in Around)
                    {
                        var n = v + d; int q = Wrap(n.y, h) * w + Wrap(n.x, w);
                        if (m[q] && !seen[q]) { seen[q] = true; todo.Push(n); }
                    }
                }
                list.Add(a);
            }
            return list;
        }

        // the pixels just outside an area
        static List<Vector2Int> Ring(bool[] m, int w, int h, List<Vector2Int> area, int width)
        {
            var ring = new List<Vector2Int>(); var seen = new HashSet<Vector2Int>(); var edge = area;
            for (int step = 0; step < width; step++)
            {
                var next = new List<Vector2Int>();
                foreach (var v in edge)
                    foreach (var d in Around) { var n = v + d; if (!m[Wrap(n.y, h) * w + Wrap(n.x, w)] && seen.Add(n)) next.Add(n); }
                ring.AddRange(next); edge = next;
            }
            return ring;
        }

        /// <summary>How far away the best piece is: its surroundings look most like this area's surroundings.</summary>
        static Vector2Int Find(Pixels p, bool[] mask, List<Vector2Int> area, bool refine, Vector2Int from, int reach)
        {
            int w = p.w, h = p.h;
            var ring = Ring(mask, w, h, area, Mathf.Max(3, w / 40)); if (ring.Count == 0) return from;
            int rs = Mathf.Max(1, ring.Count / 220), st = Mathf.Max(1, area.Count / 260);
            var offs = new List<Vector2Int>();
            if (refine) { for (int y = -reach; y <= reach; y++) for (int x = -reach; x <= reach; x++) offs.Add(from + new Vector2Int(x, y)); }
            else for (int y = 0; y < h; y += 2) for (int x = 0; x < w; x += 2) if (x != 0 || y != 0) offs.Add(new Vector2Int(x > w / 2 ? x - w : x, y > h / 2 ? y - h : y));
            var score = new float[offs.Count];
            TextureOps.Rows(offs.Count, o =>
            {
                var d = offs[o];
                // (a piece that's painted over itself can't be used)
                for (int i = 0; i < area.Count; i += st) if (mask[Wrap(area[i].y + d.y, h) * w + Wrap(area[i].x + d.x, w)]) { score[o] = float.MaxValue; return; }
                int n = 0; Vector3 sum = Vector3.zero; float sq = 0;
                for (int i = 0; i < ring.Count; i += rs)
                {
                    int q = Wrap(ring[i].y + d.y, h) * w + Wrap(ring[i].x + d.x, w); if (mask[q]) continue;
                    var a = p[ring[i].x, ring[i].y]; var b = p.c[q]; var v = new Vector3(a.r - b.r, a.g - b.g, a.b - b.b);
                    sum += v; sq += v.sqrMagnitude; n++;
                }
                if (n < 4) { score[o] = float.MaxValue; return; }
                // an overall difference in light is mostly fine (it gets corrected), differences in shape aren't
                var mean = sum / n; score[o] = sq / n - mean.sqrMagnitude * 0.65f;
            });
            int best = -1; for (int o = 0; o < offs.Count; o++) if (score[o] < float.MaxValue && (best < 0 || score[o] < score[best])) best = o;
            return best < 0 ? from : offs[best];
        }

        static void Fill(Pixels p, Pixels r, bool[] mask, List<Vector2Int> area, Vector2Int off)
        {
            int w = p.w, h = p.h;
            var edge = Ring(mask, w, h, area, 1); int st = Mathf.Max(1, edge.Count / 72);
            var at = new List<Vector2Int>(); var diff = new List<Vector3>();
            for (int i = 0; i < edge.Count; i += st)
            {
                var a = p[edge[i].x, edge[i].y]; var b = p[edge[i].x + off.x, edge[i].y + off.y];
                at.Add(edge[i]); diff.Add(Vector3.ClampMagnitude(new Vector3(a.r - b.r, a.g - b.g, a.b - b.b), 0.35f)); // (single odd pixels shouldn't smear in)
            }
            TextureOps.Rows(area.Count, i =>
            {
                var v = area[i]; var c = p[v.x + off.x, v.y + off.y]; float ws = 0; var d = Vector3.zero;
                for (int k = 0; k < at.Count; k++) { float wt = 1f / ((at[k] - v).sqrMagnitude + 0.5f); ws += wt; d += diff[k] * wt; }
                if (ws > 0) d /= ws;
                r.c[Wrap(v.y, h) * w + Wrap(v.x, w)] = new Color(Mathf.Clamp01(c.r + d.x), Mathf.Clamp01(c.g + d.y), Mathf.Clamp01(c.b + d.z), c.a);
            });
        }
    }

    [Serializable]
    public class PaintOutStep : TextureStep, IPaintable
    {
        public List<TextureHeal.Stroke> strokes = new List<TextureHeal.Stroke>();
        public float brush = 0.04f;
        public override string Title => "Paint out";
        public override string Blurb => "Brush over a spot to make it go away";
        public override string Explain => "Brush over anything you don't want (a stain, a leaf, a stone that gives the repeat away) on the left picture. It's filled with a matching piece from elsewhere in the same texture. Right-drag takes paint away again.";
        public override string Summary => strokes.Count == 0 ? "nothing painted yet" : strokes.Count + (strokes.Count == 1 ? " stroke" : " strokes");
        public override Color Tint => new Color(0.95f, 0.65f, 0.35f);
        public override bool Settings()
        {
            Slider(ref brush, "Brush size", "The mouse wheel over the picture changes it too", 0.005f, 0.2f);
            bool ch = false;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(strokes.Count == 0))
            {
                if (GUILayout.Button("Remove last stroke")) { strokes.RemoveAt(strokes.Count - 1); ch = true; }
                if (GUILayout.Button("Clear")) { strokes.Clear(); ch = true; }
            }
            EditorGUILayout.EndHorizontal();
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) => TextureHeal.Heal(p, strokes);
        public List<TextureHeal.Stroke> Strokes => strokes;
        public float Brush { get => brush; set => brush = value; }
        public bool Painting => true;
        public string PaintLabel => "BRUSH OVER WHAT SHOULD GO";
        public Color PaintColour => new Color(1f, 0.25f, 0.15f, 0.5f);
        public override TextureStep Demo() => null;
        public override TextureStep ForRecipe() => null; // (the spots are this texture's own)
    }
}
