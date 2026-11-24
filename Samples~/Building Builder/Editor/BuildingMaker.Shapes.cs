using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Rooms of any shape (their floors cut true, not in squares), posts under balconies, what goes under stairs
    /// (open treads, a cupboard with a door), and which walls face outside.</summary>
    public static partial class BuildingMaker
    {
        // ------------------------------------------------------------------ rooms of any shape

        /// <summary>Triangles filling a simple outline (any order round it), as indices into it.</summary>
        public static List<int> Triangulate(IList<Vector2> pts)
        {
            var tris = new List<int>(); int n = pts.Count; if (n < 3) return tris; var idx = Enumerable.Range(0, n).ToList();
            float s = 0; for (int i = 0; i < n; i++) { var a = pts[i]; var b = pts[(i + 1) % n]; s += a.x * b.y - b.x * a.y; } if (s < 0) idx.Reverse();
            bool In(Vector2 p, Vector2 a, Vector2 b, Vector2 c) { float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c); return d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f; }
            int guard = 0;
            while (idx.Count > 3 && guard++ < 4000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count]; var a = pts[i0]; var b = pts[i1]; var c = pts[i2];
                    if (Cross(b - a, c - b) <= 1e-7f) continue; bool empty = true; foreach (int k in idx) { if (k == i0 || k == i1 || k == i2) continue; if (In(pts[k], a, b, c)) { empty = false; break; } }
                    if (!empty) continue; tris.Add(i0); tris.Add(i1); tris.Add(i2); idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) { idx.RemoveAt(0); }
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>The rooms of a floor with their own outline that nothing cuts into (no hole, stair hole or later room over
        /// them): their floors are made from the true outline instead of squares.</summary>
        public static HashSet<int> CleanShapes(Building b, Building.Floor fl)
        {
            var l = new HashSet<int>(); if (fl == null) return l; var holes = StairHoles(b, fl.level);
            for (int i = 0; i < fl.areas.Count; i++)
            {
                var a = fl.areas[i]; if (!a.IsShape || a.hole) continue; var box = Rect.MinMaxRect(a.min.x, a.min.y, a.max.x, a.max.y); bool hit = holes.Any(r => r.Overlaps(box));
                for (int j = 0; j < fl.areas.Count && !hit; j++) { if (j == i) continue; var o = fl.areas[j]; if (!o.hole && j < i) continue; var ob = Rect.MinMaxRect(Mathf.Min(o.min.x, o.max.x), Mathf.Min(o.min.y, o.max.y), Mathf.Max(o.min.x, o.max.x), Mathf.Max(o.min.y, o.max.y)); if (ob.Overlaps(box)) hit = true; }
                if (!hit) l.Add(i);
            }
            return l;
        }
        /// <summary>A slab with this outline from ya to yb: its top, its underside and its edges.</summary>
        static void ShapeSlab(Pack p, List<Vector2> pts, float ya, float yb, int top, int under, int side)
        {
            var tris = Triangulate(pts);
            for (int k = 0; k < tris.Count; k += 3) { var a = pts[tris[k]]; var c = pts[tris[k + 1]]; var d = pts[tris[k + 2]]; p.Poly(new[] { new Vector3(a.x, yb, a.y), new Vector3(c.x, yb, c.y), new Vector3(d.x, yb, d.y) }, Vector3.up, top); p.Poly(new[] { new Vector3(a.x, ya, a.y), new Vector3(c.x, ya, c.y), new Vector3(d.x, ya, d.y) }, Vector3.down, under); }
            var mid = pts.Aggregate(Vector2.zero, (s, q) => s + q) / pts.Count; float sgn = 0; for (int i = 0; i < pts.Count; i++) { var q0 = pts[i]; var q1 = pts[(i + 1) % pts.Count]; sgn += q0.x * q1.y - q1.x * q0.y; }
            for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var c = pts[(i + 1) % pts.Count]; var d = c - a; var n = sgn > 0 ? new Vector3(d.y, 0, -d.x).normalized : new Vector3(-d.y, 0, d.x).normalized; p.Quad(new Vector3(a.x, ya, a.y), new Vector3(c.x, ya, c.y), new Vector3(c.x, yb, c.y), new Vector3(a.x, yb, a.y), n, side); }
        }
        /// <summary>A rough, broken outline inside a rectangle (for holes that look smashed through).</summary>
        public static List<Vector2> Broken(Vector2 lo, Vector2 hi, int seed)
        {
            var rnd = new System.Random(seed); var c = (lo + hi) / 2; var r = (hi - lo) / 2; int n = 9 + rnd.Next(5); var l = new List<Vector2>();
            for (int i = 0; i < n; i++) { float an = Mathf.PI * 2 * (i + (float)rnd.NextDouble() * 0.5f) / n; float k = 0.55f + (float)rnd.NextDouble() * 0.45f; if (i % 2 == 1) k *= 0.7f; l.Add(c + new Vector2(Mathf.Cos(an) * r.x * k, Mathf.Sin(an) * r.y * k)); }
            return l;
        }
        /// <summary>A round room's outline: so many corners round the rectangle's middle.</summary>
        public static List<Vector2> Round(Vector2 lo, Vector2 hi, int sides) { var c = (lo + hi) / 2; var r = (hi - lo) / 2; var l = new List<Vector2>(); for (int i = 0; i < sides; i++) { float an = Mathf.PI * 2 * (i + 0.5f) / sides; l.Add(c + new Vector2(Mathf.Cos(an) * r.x, Mathf.Sin(an) * r.y)); } return l; }

        // ------------------------------------------------------------------ what you're about to put, before you put it

        /// <summary>Meshes (with their materials) for a few new things on a floor, made the same way as the real ones: shown
        /// while you place them. The real floor is used to know where its walls are.</summary>
        public static List<(Mesh mesh, Material[] mats)> Ghost(Building b, Building.Floor part, Building.Floor real)
        {
            var l = new List<(Mesh, Material[])>(); if (!b || part == null) return l; var mats = Materials(b); s_B = b; s_Mats = mats; s_Placed = null; s_RailPack = null; s_GlassPack = null; s_Leaves = null; s_Level = part.level; s_Wall = -1;
            try
            {
                float H = b.HeightOf(part.level), T = b.slabThickness, y0 = b.Y(part.level); var pk = new Pack(b.textureSize); var x = new Extras { glass = new Pack(b.textureSize) }; int nm = mats.Length; x.M = k => Mathf.Clamp(k, 0, nm - 1);
                if (part.areas.Count > 0) FloorMesh(b, part, pk, y0, H, T);
                foreach (var w in part.walls) WallMesh(b, part, w, pk, y0, H, x);
                foreach (var s in part.stairs) StairMesh(b, real ?? part, s, pk, null, y0, H, x.M);
                Columns(b, part, pk, y0, H, x.M);
                if (!pk.Empty) { var mesh = pk.Make("Ghost", out var ids); mesh.hideFlags = HideFlags.HideAndDontSave; var mm = s_Mats; l.Add((mesh, ids.Select(i => mm[Mathf.Clamp(i, 0, mm.Length - 1)]).ToArray())); }
                if (!x.glass.Empty) { var gm = x.glass.Make("Ghost glass", out _); gm.hideFlags = HideFlags.HideAndDontSave; l.Add((gm, new[] { Glass() })); }
            }
            finally { s_Mats = null; s_B = null; }
            return l;
        }

        // ------------------------------------------------------------------ which walls face outside

        /// <summary>Whether a wall has the outside (no floor) on one of its sides.</summary>
        public static bool Outside(Building b, Building.Floor fl, Building.Wall w)
        {
            float len = w.Length; if (len < 1e-3f) return false; var d = (w.b - w.a) / len; var n = new Vector2(-d.y, d.x) * (Thick(b, w) / 2 + 0.2f); int open = 0;
            foreach (float k in new[] { 0.2f, 0.5f, 0.8f }) { var p = Vector2.Lerp(w.a, w.b, k); foreach (var q in new[] { p + n, p - n }) if (!fl.areas.Any(a => !a.hole && a.Contains(q))) { open++; break; } }
            return open >= 2;
        }

        // ------------------------------------------------------------------ posts under balconies

        static void BalconyPosts(Building b, Building.Floor fl, Pack p, float y0, Func<int, int> M)
        {
            if (fl.level <= 0) return; var below = Cover(b, b.FloorAt(fl.level - 1)); float q = Fine(b); int mat = M(3); float ground = b.Y(0);
            foreach (var a in fl.areas.Where(a => a.posts && !a.hole))
            {
                var c = a.Corners(); var mid = c.Aggregate(Vector2.zero, (s, v) => s + v) / c.Count; var pts = new List<Vector2>();
                for (int i = 0; i < c.Count; i++) { var e0 = c[i]; var e1 = c[(i + 1) % c.Count]; float len = (e1 - e0).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, a.postGap) - 1e-3f)); for (int k = 0; k < n; k++) pts.Add(Vector2.Lerp(e0, e1, k / (float)n)); }
                foreach (var pt in pts)
                {
                    var at = pt + (mid - pt).normalized * 0.15f; if (below.ContainsKey(new Vector2Int(Mathf.FloorToInt(at.x / q), Mathf.FloorToInt(at.y / q)))) continue; // (the building below holds it up there)
                    float top = y0 + a.lift - b.slabThickness, s = 0.1f; int pm = mat; if (Swapped(Building.PieceKind.Column, p.m * Matrix4x4.Translate(new Vector3(at.x, 0, at.y)), new Vector3(-s, ground, -s), new Vector3(s, top, s), ref pm, true)) continue;
                    p.Box(new Vector3(at.x - s, ground, at.y - s), new Vector3(at.x + s, top, at.y + s), f => pm);
                }
            }
        }

        // ------------------------------------------------------------------ under the stairs

        static List<Leaf> s_Leaves;
        /// <summary>A flight's steps as treads only, held by two stringers along its sides (nothing under it).</summary>
        static void OpenFlight(Pack p, Rect r, Vector2 up, float ya, float yb, int n, int sm, int tm)
        {
            bool alongX = up.x != 0; float th = 0.06f;
            for (int i = 0; i < n; i++)
            {
                float k0 = i / (float)n, k1 = (i + 1) / (float)n, top = ya + (yb - ya) * k1; Vector3 lo, hi;
                if (!alongX) { float z0 = up.y > 0 ? r.yMin + r.height * k0 : r.yMax - r.height * k1, z1 = up.y > 0 ? r.yMin + r.height * k1 : r.yMax - r.height * k0; lo = new Vector3(r.xMin + 0.06f, top - th, z0); hi = new Vector3(r.xMax - 0.06f, top, z1); }
                else { float x0 = up.x > 0 ? r.xMin + r.width * k0 : r.xMax - r.width * k1, x1 = up.x > 0 ? r.xMin + r.width * k1 : r.xMax - r.width * k0; lo = new Vector3(x0, top - th, r.yMin + 0.06f); hi = new Vector3(x1, top, r.yMax - 0.06f); }
                p.Box(lo, hi, k => k == 2 ? tm : sm);
            }
            // the two stringers, along the slope
            float rise = (yb - ya) / n; foreach (bool first in new[] { true, false })
            {
                Vector3 S, E; if (!alongX) { float x = first ? r.xMin + 0.03f : r.xMax - 0.03f; S = new Vector3(x, ya, up.y > 0 ? r.yMin : r.yMax); E = new Vector3(x, yb, up.y > 0 ? r.yMax : r.yMin); }
                else { float z = first ? r.yMin + 0.03f : r.yMax - 0.03f; S = new Vector3(up.x > 0 ? r.xMin : r.xMax, ya, z); E = new Vector3(up.x > 0 ? r.xMax : r.xMin, yb, z); }
                Bar(p, S + Vector3.down * 0.25f, E + Vector3.down * 0.25f, 0.06f, 0.25f + rise, sm);
            }
        }
        /// <summary>Closes the space under a straight flight on the side away from the wall, with a door where it's tall enough.</summary>
        static void Cupboard(Building b, Building.Floor fl, Building.Stair s, Pack p, Rect r, Vector2 up, float ya, float yb, int mat, Matrix4x4 frame)
        {
            if (up.x != 0) return; float L = r.height; bool wallLeft = AlongWall(b, fl, frame, new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMin, 0, r.yMax)), wallRight = AlongWall(b, fl, frame, new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax)); if (!wallLeft && !wallRight) return;
            float x = wallLeft ? r.xMax - 0.04f : r.xMin + 0.04f, t = 0.08f, sd = wallLeft ? 1 : -1; float H(float z) => ya + (yb - ya) * Mathf.Clamp01((up.y > 0 ? z - r.yMin : r.yMax - z) / L) - 0.08f; // (the underside of the treads)
            // a door where there's room for one: its far edge at the tall end
            float dw = Mathf.Min(0.8f, L * 0.4f), z1 = up.y > 0 ? r.yMax - 0.15f : r.yMin + 0.15f + dw, z0 = z1 - dw; bool door = H(up.y > 0 ? z0 : z1) >= 2.05f;
            void Panel(float za, float zb, float yLo, Func<float, float> yHi) { if (zb - za < 1e-3f) return; foreach (float side in new[] { 1f, -1f }) { float xx = x + side * t / 2; var poly = new[] { new Vector3(xx, yLo, za), new Vector3(xx, yLo, zb), new Vector3(xx, yHi(zb), zb), new Vector3(xx, yHi(za), za) }; if (yHi(za) <= yLo && yHi(zb) <= yLo) continue; p.Poly(poly, new Vector3(side * sd, 0, 0), mat); } }
            if (door) { Panel(r.yMin, z0, 0, H); Panel(z1, r.yMax, 0, H); Panel(z0, z1, 2.0f, H); s_Leaves?.Add(new Leaf { m = frame * Matrix4x4.TRS(new Vector3(x, 0, sd > 0 ? z0 : z1), Quaternion.Euler(0, -90 * sd, 0), Vector3.one), x0 = 0, x1 = dw, y0 = 0, y1 = 2.0f, t = t, swing = Building.DoorSwing.BothWays, mat = -1, wall = -1, opening = -1 }); }
            else Panel(r.yMin, r.yMax, 0, H);
            // the tall end closed too
            float zEnd = up.y > 0 ? r.yMax - 0.02f : r.yMin + 0.02f, hEnd = yb - 0.08f; foreach (float side in new[] { 1f, -1f }) p.Quad(new Vector3(r.xMin, 0, zEnd), new Vector3(r.xMax, 0, zEnd), new Vector3(r.xMax, hEnd, zEnd), new Vector3(r.xMin, hEnd, zEnd), new Vector3(0, 0, side), mat);
        }
    }
}
