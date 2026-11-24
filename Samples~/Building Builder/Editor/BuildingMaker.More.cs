using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The extras: roofs, finishes, frames and glass, door leaves, columns, beams, railings, spiral stairs, ladders,
    /// curved walls, foundations and smooth slopes over stairs.</summary>
    public static partial class BuildingMaker
    {
        // ------------------------------------------------------------------ your own models in place of the builder's pieces

        public struct Placed { public GameObject model; public Matrix4x4 m; public Vector3 size; public bool fit, collide; }
        static Building s_B; static List<Placed> s_Placed; static Func<int, int> s_M; static Material[] s_Mats;
        /// <summary>A piece about to be made: if you gave it a model it's put there instead (true), if you gave it a material that's used.</summary>
        static bool Swapped(Building.PieceKind k, Matrix4x4 m, Vector3 lo, Vector3 hi, ref int mat, bool collide = false)
        {
            var s = s_B ? s_B.SwapFor(k) : null; if (s == null) return false;
            if (s.model) { var c = (lo + hi) / 2; s_Placed?.Add(new Placed { model = s.model, m = m * Matrix4x4.Translate(new Vector3(c.x, lo.y, c.z)), size = hi - lo, fit = s.fit, collide = collide }); return true; }
            if (s.material && s_Mats != null) mat = MatIndex(s.material); return false;
        }
        static int MatIndex(Material m) { int i = s_B.materials.IndexOf(m); if (i < 0) { s_B.materials.Add(m); i = s_B.materials.Count - 1; var l = s_Mats.ToList(); l.Add(s_B.psx ? PSX(m, s_B) : m); s_Mats = l.ToArray(); } return i; }
        /// <summary>Puts your models where they go, each stretched so its own size fills the piece (or kept as it is).</summary>
        static void PlaceModels(GameObject parent, List<Placed> l)
        {
            var sizes = new Dictionary<GameObject, Bounds>();
            foreach (var p in l)
            {
                if (!sizes.TryGetValue(p.model, out var bb)) { bb = LocalBounds(p.model); sizes[p.model] = bb; }
                var o = PrefabUtility.InstantiatePrefab(p.model) as GameObject ?? UnityEngine.Object.Instantiate(p.model); o.transform.SetParent(parent.transform, false); o.layer = parent.layer;
                var sc = p.fit ? new Vector3(p.size.x / Mathf.Max(1e-4f, bb.size.x), p.size.y / Mathf.Max(1e-4f, bb.size.y), p.size.z / Mathf.Max(1e-4f, bb.size.z)) : Vector3.one; var rot = p.m.rotation; var bottom = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                o.transform.localRotation = rot; o.transform.localScale = Vector3.Scale(o.transform.localScale, sc); o.transform.localPosition = (Vector3)p.m.GetColumn(3) - rot * Vector3.Scale(bottom, sc);
                if (p.collide && !o.GetComponentInChildren<Collider>()) { var bc = o.AddComponent<BoxCollider>(); bc.center = bb.center; bc.size = bb.size; }
            }
        }
        static Bounds LocalBounds(GameObject g)
        {
            bool any = false; var bb = new Bounds(); var inv = g.transform.worldToLocalMatrix;
            foreach (var f in g.GetComponentsInChildren<MeshFilter>(true)) { if (!f.sharedMesh) continue; var m = inv * f.transform.localToWorldMatrix; var mb = f.sharedMesh.bounds; foreach (var c in new[] { new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1) }) { var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, c)); if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p); } }
            foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.sharedMesh) { var p = inv.MultiplyPoint3x4(r.transform.position); if (!any) { bb = new Bounds(p, r.sharedMesh.bounds.size); any = true; } else bb.Encapsulate(new Bounds(p, r.sharedMesh.bounds.size)); }
            return any ? bb : new Bounds(new Vector3(0, 0.5f, 0), Vector3.one);
        }

        // ------------------------------------------------------------------ a bar between two points (railings, beams)

        public static void Bar(Pack p, Vector3 from, Vector3 to, float w, float h, int mat)
        {
            var d = to - from; var flat = new Vector3(d.x, 0, d.z); if (flat.sqrMagnitude < 1e-8f) { p.Box(from + new Vector3(-w / 2, 0, -w / 2), to + new Vector3(w / 2, h, w / 2), f => mat); return; }
            var ac = Vector3.Cross(Vector3.up, flat.normalized) * w / 2; var up = Vector3.up * h; Vector3 A0 = from - ac, A1 = from + ac, B0 = to - ac, B1 = to + ac;
            p.Quad(A0, B0, B0 + up, A0 + up, -ac, mat); p.Quad(A1, B1, B1 + up, A1 + up, ac, mat); p.Quad(A0 + up, B0 + up, B1 + up, A1 + up, Vector3.Cross(B0 - A0, ac).normalized * (Vector3.Cross(B0 - A0, ac).y < 0 ? -1 : 1), mat);
            p.Quad(A0, B0, B1, A1, Vector3.down, mat); p.Quad(A0, A1, A1 + up, A0 + up, -flat, mat); p.Quad(B0, B1, B1 + up, B0 + up, flat, mat);
        }
        static Pack s_GlassPack, s_RailPack; static int s_Wall = -1, s_Level;
        /// <summary>A railing from a to b (feet level), in the look given (the building's when none): posts, bars, a panel
        /// or glass under a handrail, with your own models for its posts and handrail.</summary>
        public static void Rail(Pack p, Vector3 a, Vector3 b, int mat, Building.RailStyle st = null)
        {
            if (s_RailPack != null && p != s_RailPack) { var rp = s_RailPack; var keep = rp.m; rp.m = p.m; try { Rail(rp, a, b, mat, st); } finally { rp.m = keep; } return; }
            st = st ?? (s_B ? s_B.railing : null) ?? new Building.RailStyle(); if (st.mat >= 0 && s_Mats != null) mat = Mathf.Clamp(st.mat, 0, s_Mats.Length - 1);
            float h = Mathf.Max(0.2f, st.height), ps = Mathf.Clamp(st.postSize, 0.01f, 0.5f), top = st.handrail ? h - 0.05f : h; var flat = new Vector3(b.x - a.x, 0, b.z - a.z); float len = flat.magnitude; var face = Quaternion.LookRotation(len > 1e-4f ? flat / len : Vector3.forward);
            var S = st.support; bool bars = S == Building.RailSupport.Bars; float low = st.bottomRail ? 0.1f : 0.04f;
            if (S != Building.RailSupport.None)
            {
                int n = S == Building.RailSupport.Posts || bars ? Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.05f, st.spacing) - 1e-3f)) : 1;
                for (int i = 0; i <= n; i++) { var q = Vector3.Lerp(a, b, i / (float)n); bool end = i == 0 || i == n; if (bars && !end) Post(p, q + Vector3.up * low, top - low, ps * 0.6f, st, face, mat); else Post(p, q, top, ps, st, face, mat); }
            }
            var up = Vector3.up; float py0 = st.bottomRail ? 0.1f : 0.08f, py1 = top - 0.06f;
            if (S == Building.RailSupport.Panel && py1 > py0) Bar(p, a + up * py0, b + up * py0, Mathf.Max(0.015f, ps * 0.4f), py1 - py0, mat);
            if (S == Building.RailSupport.Glass && py1 > py0 && s_GlassPack != null) { var g = s_GlassPack; var m0 = g.m; g.m = p.m; var nr = Vector3.Cross(Vector3.up, flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward); g.Quad(a + up * py0, b + up * py0, b + up * py1, a + up * py1, nr, 0); g.m = m0; }
            var dv = b - a; var along = Quaternion.LookRotation(dv.sqrMagnitude > 1e-8f ? dv.normalized : Vector3.forward, Vector3.up);
            if (st.handrail)
            {
                var at = p.m * Matrix4x4.TRS(a + up * top, along, Vector3.one); int hm = mat;
                if (st.handrailModel) s_Placed?.Add(new Placed { model = st.handrailModel, m = at * Matrix4x4.Translate(new Vector3(0, 0, dv.magnitude / 2)), size = new Vector3(0.06f, 0.05f, dv.magnitude), fit = st.fit });
                else if (!Swapped(Building.PieceKind.Handrail, at, new Vector3(-0.03f, 0, 0), new Vector3(0.03f, 0.05f, dv.magnitude), ref hm)) Bar(p, a + up * top, b + up * top, 0.06f, 0.05f, hm);
            }
            if (st.midRail && (S == Building.RailSupport.Posts || S == Building.RailSupport.None)) Bar(p, a + up * (h * 0.5f), b + up * (h * 0.5f), 0.03f, 0.03f, mat);
            if (st.bottomRail) Bar(p, a + up * 0.05f, b + up * 0.05f, 0.04f, 0.04f, mat);
        }
        /// <summary>One post or bar standing at q: your model, the building's swap, or a square or round one.</summary>
        static void Post(Pack p, Vector3 q, float h, float size, Building.RailStyle st, Quaternion face, int mat)
        {
            if (h <= 0.01f) return; var lo = new Vector3(-size / 2, 0, -size / 2); var hi = new Vector3(size / 2, h, size / 2); var at = p.m * Matrix4x4.TRS(q, face, Vector3.one);
            if (st.postModel) { s_Placed?.Add(new Placed { model = st.postModel, m = at, size = hi - lo, fit = st.fit }); return; }
            int pm = mat; if (Swapped(Building.PieceKind.RailPost, at, lo, hi, ref pm)) return;
            if (st.round < 3) { p.Box(q + lo, q + hi, f => pm, f => f == 3); return; }
            int n = Mathf.Min(st.round, 16); var ring = Enumerable.Range(0, n).Select(i => { float an = Mathf.PI * 2 * (i + 0.5f) / n; return q + new Vector3(Mathf.Cos(an), 0, Mathf.Sin(an)) * size / 2; }).ToArray();
            for (int i = 0; i < n; i++) { var A = ring[i]; var B = ring[(i + 1) % n]; var nr = ((A + B) / 2 - q).normalized; p.Quad(A, B, B + Vector3.up * h, A + Vector3.up * h, nr, pm); } p.Poly(ring.Select(r => r + Vector3.up * h).ToList(), Vector3.up, pm);
        }

        // ------------------------------------------------------------------ roofs

        /// <summary>The rooms a roof covers, as few rectangles as it can: rooms side by side with the same span become one,
        /// so joined rooms get one roof (an L or a T gets one roof per wing, meeting in valleys).</summary>
        public static List<Rect> RoofRects(Building.Floor fl)
        {
            var l = fl.areas.Where(a => !a.hole && !a.IsShape && !a.posts && a.name != "Balcony").Select(a => Rect.MinMaxRect(Mathf.Min(a.min.x, a.max.x), Mathf.Min(a.min.y, a.max.y), Mathf.Max(a.min.x, a.max.x), Mathf.Max(a.min.y, a.max.y))).Where(r => r.width > 1e-3f && r.height > 1e-3f).ToList();
            bool Same(float a, float b) => Mathf.Abs(a - b) < 1e-3f; bool In(Rect a, Rect b) => a.xMin >= b.xMin - 1e-3f && a.xMax <= b.xMax + 1e-3f && a.yMin >= b.yMin - 1e-3f && a.yMax <= b.yMax + 1e-3f;
            for (bool again = true; again;)
            {
                again = false;
                for (int x = 0; x < l.Count && !again; x++) for (int y = 0; y < l.Count && !again; y++)
                {
                    if (x == y) continue; var a = l[x]; var c = l[y];
                    if (In(a, c)) { l.RemoveAt(x); again = true; break; }
                    bool rowJoin = Same(a.yMin, c.yMin) && Same(a.yMax, c.yMax) && a.xMax >= c.xMin - 1e-3f && a.xMin <= c.xMax + 1e-3f, colJoin = Same(a.xMin, c.xMin) && Same(a.xMax, c.xMax) && a.yMax >= c.yMin - 1e-3f && a.yMin <= c.yMax + 1e-3f;
                    if (rowJoin || colJoin) { l[x] = Rect.MinMaxRect(Mathf.Min(a.xMin, c.xMin), Mathf.Min(a.yMin, c.yMin), Mathf.Max(a.xMax, c.xMax), Mathf.Max(a.yMax, c.yMax)); l.RemoveAt(y); again = true; }
                }
            }
            return l;
        }

        /// <summary>A roof over a room with its own outline: flat, or rising from every edge to a point over its middle (a
        /// round room gets a cone).</summary>
        static void ShapeRoof(Building b, Building.Floor fl, Building.Area a, Pack p, float baseY, Func<int, int> M)
        {
            var cs = a.Corners(); if (cs.Count < 3) return; float t = b.wallThickness, ov = Mathf.Max(0, fl.roofOverhang), e = t / 2 + ov; int rm = M(fl.roofMat >= 0 ? fl.roofMat : 3), wm = M(1), cm = M(2);
            var mid = cs.Aggregate(Vector2.zero, (s, v) => s + v) / cs.Count; float sgn = 0; for (int i = 0; i < cs.Count; i++) { var q0 = cs[i]; var q1 = cs[(i + 1) % cs.Count]; sgn += q0.x * q1.y - q1.x * q0.y; }
            // the outline pushed out by the overhang (each corner along the two edges' outward sides)
            Vector2 Out(Vector2 d) => sgn > 0 ? new Vector2(d.y, -d.x) : new Vector2(-d.y, d.x);
            var ex = new List<Vector2>(); for (int i = 0; i < cs.Count; i++) { var pv = cs[(i + cs.Count - 1) % cs.Count]; var c = cs[i]; var nx = cs[(i + 1) % cs.Count]; var n0 = Out((c - pv).normalized); var n1 = Out((nx - c).normalized); var bis = (n0 + n1).normalized; float k = Mathf.Max(0.3f, Vector2.Dot(bis, n0)); ex.Add(c + bis * e / k); }
            var tris = Triangulate(cs); foreach (int k in Enumerable.Range(0, tris.Count / 3)) { var q0 = cs[tris[k * 3]]; var q1 = cs[tris[k * 3 + 1]]; var q2 = cs[tris[k * 3 + 2]]; p.Poly(new[] { new Vector3(q0.x, baseY, q0.y), new Vector3(q1.x, baseY, q1.y), new Vector3(q2.x, baseY, q2.y) }, Vector3.down, cm); }
            if (fl.roof == Building.RoofKind.Flat || fl.roof == Building.RoofKind.Parapet)
            {
                ShapeSlab(p, ex, baseY, baseY + 0.15f, rm, cm, rm);
                if (fl.roof == Building.RoofKind.Parapet) for (int i = 0; i < cs.Count; i++) { var q0 = cs[i]; var q1 = cs[(i + 1) % cs.Count]; Bar(p, new Vector3(q0.x, baseY + 0.15f, q0.y), new Vector3(q1.x, baseY + 0.15f, q1.y), t, 0.6f, wm); }
                return;
            }
            float slope = Mathf.Tan(Mathf.Clamp(fl.roofPitch, 2, 75) * Mathf.Deg2Rad), rin = float.MaxValue; for (int i = 0; i < cs.Count; i++) rin = Mathf.Min(rin, DistSeg2(mid, cs[i], cs[(i + 1) % cs.Count]));
            float eave = baseY - ov * slope; var apex = new Vector3(mid.x, baseY + (rin + t / 2) * slope, mid.y);
            for (int i = 0; i < ex.Count; i++) { var q0 = ex[i]; var q1 = ex[(i + 1) % ex.Count]; var A = new Vector3(q0.x, eave, q0.y); var B = new Vector3(q1.x, eave, q1.y); var n = Vector3.Cross(B - A, apex - A).normalized; if (n.y < 0) n = -n; p.Poly(new[] { A, B, apex }, n, rm); p.Poly(new[] { A, B, apex }, -n, cm); p.Quad(A + Vector3.down * 0.12f, B + Vector3.down * 0.12f, B, A, new Vector3(n.x, 0, n.z), rm); }
        }

        /// <summary>One roof over all of a floor's rooms: each wing gets its own (flat, gable, hip or shed) and where they meet
        /// the higher one wins, so they join in valleys; gable ends, a parapet and gutters go round the outside.</summary>
        static void Roof(Building b, Building.Floor fl, Pack p, float y0, float H, Func<int, int> M)
        {
            if (fl.roof == Building.RoofKind.None) return; float baseY = y0 + H - b.slabThickness, t = b.wallThickness, ov = Mathf.Max(0, fl.roofOverhang), e = t / 2 + ov; int rm = M(fl.roofMat >= 0 ? fl.roofMat : 3), wm = M(1), cm = M(2); { var rs = b.SwapFor(Building.PieceKind.Roof); if (rs != null && rs.material && fl.roofMat < 0) rm = MatIndex(rs.material); }
            foreach (var sa in fl.areas.Where(a => !a.hole && a.IsShape && !a.posts)) ShapeRoof(b, fl, sa, p, baseY, M);
            var inner = RoofRects(fl); if (inner.Count == 0) return; var outer = inner.Select(r => Rect.MinMaxRect(r.xMin - e, r.yMin - e, r.xMax + e, r.yMax + e)).ToList();
            var kind = fl.roof; bool flat = kind == Building.RoofKind.Flat || kind == Building.RoofKind.Parapet, attic, gableEnds; float th = 0.15f, slope = Mathf.Tan(Mathf.Clamp(fl.roofPitch, 2, 75) * Mathf.Deg2Rad), s = kind == Building.RoofKind.Shed ? slope * 0.5f : slope; attic = fl.attic && !flat; gableEnds = kind == Building.RoofKind.Gable || kind == Building.RoofKind.Shed; float rb = attic ? y0 + H + Mathf.Max(0, fl.atticWalls) : baseY, eave = flat ? baseY + th : rb - ov * s;
            var big = outer.OrderByDescending(r => r.width * r.height).First(); bool shedX = big.width >= big.height; float shed0 = shedX ? outer.Min(r => r.yMin) : outer.Min(r => r.xMin);
            float F(int k, float x, float z)
            {
                var r = outer[k]; if (flat) return baseY + th; bool alongX = r.width >= r.height; float d;
                if (kind == Building.RoofKind.Shed) d = shedX ? z - shed0 : x - shed0;
                else if (kind == Building.RoofKind.Hip) d = Mathf.Min(Mathf.Min(x - r.xMin, r.xMax - x), Mathf.Min(z - r.yMin, r.yMax - z));
                else d = alongX ? Mathf.Min(z - r.yMin, r.yMax - z) : Mathf.Min(x - r.xMin, r.xMax - x);
                return rb + (d - ov) * s;
            }
            // a grid through every edge, ridge and hip corner, so each face is flat
            var xs = new List<float>(); var zs = new List<float>();
            foreach (var r in outer) { float half = Mathf.Min(r.width, r.height) / 2; xs.AddRange(new[] { r.xMin, r.xMax, r.center.x, r.xMin + half, r.xMax - half }); zs.AddRange(new[] { r.yMin, r.yMax, r.center.y, r.yMin + half, r.yMax - half }); }
            foreach (var r in inner) { xs.Add(r.xMin); xs.Add(r.xMax); zs.Add(r.yMin); zs.Add(r.yMax); }
            var wins = new List<(bool ax, float line, int sd, float u0, float u1, float w0, float w1)>();
            if ((fl.roofWindows || attic) && kind == Building.RoofKind.Gable)
                for (int k = 0; k < outer.Count; k++)
                {
                    var r = outer[k]; bool alongX = r.width >= r.height; float uc = alongX ? r.center.y : r.center.x, w0 = (attic ? rb : baseY) + 0.45f, w1 = w0 + 0.8f, half = 0.4f;
                    foreach (int sd in new[] { -1, 1 }) { float line = alongX ? (sd < 0 ? r.xMin : r.xMax) : (sd < 0 ? r.yMin : r.yMax); float top = Mathf.Min(F(k, alongX ? line : uc - half, alongX ? uc - half : line), F(k, alongX ? line : uc + half, alongX ? uc + half : line)); if (top < w1 + 0.15f) continue; wins.Add((!alongX, line, sd, uc - half, uc + half, w0, w1)); if (alongX) { zs.Add(uc - half); zs.Add(uc + half); } else { xs.Add(uc - half); xs.Add(uc + half); } }
                }
            var atticHoles = attic ? StairHoles(b, fl.level + 1) : new List<Rect>(); foreach (var r in atticHoles) { xs.Add(r.xMin); xs.Add(r.xMax); zs.Add(r.yMin); zs.Add(r.yMax); }
            float step = Mathf.Max(0.25f, b.cell * 0.5f); { float lo = xs.Min(), hi = xs.Max(); for (float x = Mathf.Ceil(lo / step) * step; x < hi; x += step) xs.Add(x); lo = zs.Min(); hi = zs.Max(); for (float z = Mathf.Ceil(lo / step) * step; z < hi; z += step) zs.Add(z); }
            List<float> Clean(List<float> v) { var o = new List<float>(); foreach (var x in v.OrderBy(q => q)) if (o.Count == 0 || x - o[o.Count - 1] > 1e-3f) o.Add(x); return o; }
            xs = Clean(xs); zs = Clean(zs); int nx = xs.Count - 1, nz = zs.Count - 1; if (nx < 1 || nz < 1) return;
            // which wings cover each square, and how high the roof is at its corners
            var sets = new List<int>[nx, nz]; var hts = new float[nx, nz, 4];
            for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
                {
                    float cx = (xs[i] + xs[i + 1]) / 2, cz = (zs[k] + zs[k + 1]) / 2; var l = new List<int>(); for (int r = 0; r < outer.Count; r++) if (outer[r].Contains(new Vector2(cx, cz))) l.Add(r); if (l.Count == 0) continue; sets[i, k] = l;
                    var cs = new[] { (xs[i], zs[k]), (xs[i + 1], zs[k]), (xs[i + 1], zs[k + 1]), (xs[i], zs[k + 1]) }; for (int c = 0; c < 4; c++) hts[i, k, c] = l.Max(r => F(r, cs[c].Item1, cs[c].Item2));
                }
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
            void Up(Vector3 a, Vector3 c, Vector3 d, int mat, bool under) { var n = Vector3.Cross(c - a, d - a).normalized; if (n.y < 0) n = -n; p.Poly(new[] { a, c, d }, n, mat); if (under) p.Poly(new[] { a, c, d }, -n, cm); }
            var edges = new Dictionary<(bool alongX, float line, int side), List<(float a, float b, bool eaves)>>();
            for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
                {
                    if (sets[i, k] == null) continue; float x0 = xs[i], x1 = xs[i + 1], z0 = zs[k], z1 = zs[k + 1]; var A = V(x0, hts[i, k, 0], z0); var B = V(x1, hts[i, k, 1], z0); var C = V(x1, hts[i, k, 2], z1); var D = V(x0, hts[i, k, 3], z1);
                    bool inside = inner.Any(r => r.Contains(new Vector2((x0 + x1) / 2, (z0 + z1) / 2)));
                    if (flat) { p.Quad(A, B, C, D, Vector3.up, rm); p.Quad(V(x0, baseY, z0), V(x1, baseY, z0), V(x1, baseY, z1), V(x0, baseY, z1), Vector3.down, cm); }
                    else
                    {
                        // the square's two triangles, split along the crease (the higher middle for a ridge)
                        bool under = !inside || attic; if ((A.y + C.y) >= (B.y + D.y)) { Up(A, B, C, rm, under); Up(A, C, D, rm, under); } else { Up(A, B, D, rm, under); Up(B, C, D, rm, under); }
                        if (inside) p.Quad(V(x0, baseY, z0), V(x1, baseY, z0), V(x1, baseY, z1), V(x0, baseY, z1), Vector3.down, cm);
                        if (inside && attic && !atticHoles.Any(r => r.Contains(new Vector2((x0 + x1) / 2, (z0 + z1) / 2)))) p.Quad(V(x0, y0 + H, z0), V(x1, y0 + H, z0), V(x1, y0 + H, z1), V(x0, y0 + H, z1), Vector3.up, M(0));
                    }
                    // its four sides: the outside (gable ends, the roof's edge) or a step up to a higher wing
                    foreach (var (di, dk, c0, c1, ax, line, sd, ea, eb) in new[] { (1, 0, 1, 2, false, x1, 1, z0, z1), (-1, 0, 0, 3, false, x0, -1, z0, z1), (0, 1, 3, 2, true, z1, 1, x0, x1), (0, -1, 0, 1, true, z0, -1, x0, x1) })
                    {
                        int ni = i + di, nk = k + dk; bool outside = ni < 0 || nk < 0 || ni >= nx || nk >= nz || sets[ni, nk] == null; float h0 = hts[i, k, c0], h1 = hts[i, k, c1];
                        Vector3 P0(float y) => ax ? V(ea, y, line) : V(line, y, ea); Vector3 P1(float y) => ax ? V(eb, y, line) : V(line, y, eb); var nrm = ax ? new Vector3(0, 0, sd) : new Vector3(sd, 0, 0);
                        if (outside)
                        {
                            float lo = flat ? baseY : eave; if (!edges.TryGetValue((ax, line, sd), out var el)) edges[(ax, line, sd)] = el = new List<(float, float, bool)>(); el.Add((ea, eb, !flat && h0 < eave + 1e-3f && h1 < eave + 1e-3f));
                            if (flat) { if (h0 > lo + 1e-3f || h1 > lo + 1e-3f) p.Poly(new[] { P0(lo), P1(lo), P1(h1), P0(h0) }, nrm, rm); }
                            else
                            {
                                float inLine = line - sd * ov, wb = attic ? rb : baseY; var um = (ea + eb) / 2; var probe = ax ? new Vector2(um, inLine - sd * 0.02f) : new Vector2(inLine - sd * 0.02f, um);
                                bool onWall = inner.Any(r => Rect.MinMaxRect(r.xMin - t / 2 - 0.02f, r.yMin - t / 2 - 0.02f, r.xMax + t / 2 + 0.02f, r.yMax + t / 2 + 0.02f).Contains(probe));
                                Vector3 Q0(float y) => ax ? V(ea, y, inLine) : V(inLine, y, ea); Vector3 Q1(float y) => ax ? V(eb, y, inLine) : V(inLine, y, eb);
                                void Wall(float ya, float y0b, float y1b) { if (y0b <= ya + 1e-3f && y1b <= ya + 1e-3f) return; var poly = new[] { Q0(ya), Q1(ya), Q1(Mathf.Max(ya, y1b)), Q0(Mathf.Max(ya, y0b)) }; p.Poly(poly, nrm, wm); p.Poly(poly, -nrm, wm); }
                                if (onWall)
                                {
                                    var win = wins.FirstOrDefault(q => q.ax == ax && Mathf.Abs(q.line - line) < 1e-3f && q.sd == sd && um > q.u0 && um < q.u1);
                                    if (win.u1 > win.u0) { Wall(wb, Mathf.Min(h0, win.w0), Mathf.Min(h1, win.w0)); if (h0 > win.w1 || h1 > win.w1) { var poly = new[] { Q0(win.w1), Q1(win.w1), Q1(Mathf.Max(win.w1, h1)), Q0(Mathf.Max(win.w1, h0)) }; p.Poly(poly, nrm, wm); p.Poly(poly, -nrm, wm); } }
                                    else Wall(wb, h0, h1);
                                }
                            }
                            if (!flat) { var fa = new[] { P0(h0 - 0.12f), P1(h1 - 0.12f), P1(h1), P0(h0) }; p.Poly(fa, nrm, rm); }
                        }
                        else if (di > 0 || dk > 0)
                        {
                            int o0 = di > 0 ? 0 : 0, o1 = di > 0 ? 3 : 1; float g0 = hts[ni, nk, o0], g1 = hts[ni, nk, o1];
                            if (Mathf.Abs(g0 - h0) > 1e-3f || Mathf.Abs(g1 - h1) > 1e-3f) { var poly = new[] { P0(Mathf.Min(h0, g0)), P1(Mathf.Min(h1, g1)), P1(Mathf.Max(h1, g1)), P0(Mathf.Max(h0, g0)) }; p.Poly(poly, nrm, wm); p.Poly(poly, -nrm, wm); }
                        }
                    }
                }
            // the attic's low walls, along the house's walls
            if (attic && rb > baseY + 0.01f)
            {
                var knee = new Dictionary<(bool ax, float line), List<(float, float)>>();
                for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++)
                    {
                        bool In(int a, int c) => a >= 0 && c >= 0 && a < nx && c < nz && inner.Any(r => r.Contains(new Vector2((xs[a] + xs[a + 1]) / 2, (zs[c] + zs[c + 1]) / 2))); if (!In(i, k)) continue;
                        if (!In(i + 1, k)) Add((false, xs[i + 1]), zs[k], zs[k + 1]); if (!In(i - 1, k)) Add((false, xs[i]), zs[k], zs[k + 1]); if (!In(i, k + 1)) Add((true, zs[k + 1]), xs[i], xs[i + 1]); if (!In(i, k - 1)) Add((true, zs[k]), xs[i], xs[i + 1]);
                    }
                void Add((bool, float) key, float a, float c) { if (!knee.TryGetValue(key, out var l)) knee[key] = l = new List<(float, float)>(); l.Add((a, c)); }
                foreach (var kv in knee) { float? s0 = null, s1 = null; foreach (var (a, c) in kv.Value.OrderBy(q => q.Item1).Concat(new[] { (float.MaxValue, float.MaxValue) })) { if (s1 != null && a <= s1.Value + 1e-3f) { s1 = Mathf.Max(s1.Value, c); continue; } if (s0 != null) { var A = kv.Key.ax ? V(s0.Value - t / 2, baseY, kv.Key.line) : V(kv.Key.line, baseY, s0.Value - t / 2); var B = kv.Key.ax ? V(s1.Value + t / 2, baseY, kv.Key.line) : V(kv.Key.line, baseY, s1.Value + t / 2); Bar(p, A, B, t, rb - baseY, wm); } s0 = a; s1 = c; } }
            }
            // the windows in the gable ends: a frame and glass
            foreach (var w in wins)
            {
                float inLine = w.line - w.sd * ov, tw = Mathf.Max(0.04f, b.trimWidth); int trim = M(b.trimMat); Vector3 Pt(float u, float y, float off) => w.ax ? V(u, y, inLine + off) : V(inLine + off, y, u);
                foreach (var (ua, ub, ya2, yb2) in new[] { (w.u0 - tw, w.u1 + tw, w.w0 - tw, w.w0), (w.u0 - tw, w.u1 + tw, w.w1, w.w1 + tw), (w.u0 - tw, w.u0, w.w0, w.w1), (w.u1, w.u1 + tw, w.w0, w.w1) }) { var lo2 = Pt(ua, ya2, -0.06f); var hi2 = Pt(ub, yb2, 0.06f); p.Box(Vector3.Min(lo2, hi2), Vector3.Max(lo2, hi2), f => trim); }
                if (s_GlassPack != null) s_GlassPack.Quad(Pt(w.u0, w.w0, 0), Pt(w.u1, w.w0, 0), Pt(w.u1, w.w1, 0), Pt(w.u0, w.w1, 0), w.ax ? Vector3.forward : Vector3.right, 0);
            }
            // round the outside: a parapet on a flat roof, gutters under the eaves
            foreach (var kv in edges)
            {
                var runs = kv.Value.OrderBy(q => q.a).ToList(); var (ax, line, sd) = kv.Key;
                foreach (bool eavesOnly in new[] { false, true })
                {
                    if (eavesOnly ? !fl.gutters || flat : kind != Building.RoofKind.Parapet) continue; float? s0 = null, s1 = null;
                    void Flush() { if (s0 == null) return; float off = -sd * (eavesOnly ? -0.06f : t / 2); Vector3 Q(float u, float y) => ax ? new Vector3(u, y, line + off) : new Vector3(line + off, y, u);
                        if (eavesOnly) { var g0 = Q(s0.Value, eave - 0.1f); var g1 = Q(s1.Value, eave - 0.1f); int gm = M(3); if (!Swapped(Building.PieceKind.Gutter, Matrix4x4.TRS(g0, Quaternion.LookRotation((g1 - g0).normalized), Vector3.one), new Vector3(-0.06f, 0, 0), new Vector3(0.06f, 0.1f, (g1 - g0).magnitude), ref gm)) Bar(p, g0, g1, 0.12f, 0.1f, gm); }
                        else Bar(p, Q(s0.Value, baseY + th), Q(s1.Value, baseY + th), t, 0.6f, wm);
                        s0 = s1 = null; }
                    foreach (var r in runs) { if (eavesOnly && !r.eaves) { Flush(); continue; } if (s1 != null && r.a <= s1.Value + 1e-3f) s1 = Mathf.Max(s1.Value, r.b); else { Flush(); s0 = r.a; s1 = r.b; } }
                    Flush();
                }
            }
        }

        // ------------------------------------------------------------------ columns and beams

        static void Columns(Building b, Building.Floor fl, Pack p, float y0, float H, Func<int, int> M)
        {
            float top = y0 + H - b.slabThickness;
            foreach (var c in fl.columns)
            {
                int mat = M(c.mat >= 0 ? c.mat : 3); float s = c.size / 2, ya = y0 + c.lift; if (Swapped(Building.PieceKind.Column, p.m * Matrix4x4.Translate(new Vector3(c.at.x, 0, c.at.y)), new Vector3(-s, ya, -s), new Vector3(s, top, s), ref mat, true)) continue; if (c.sides < 3) { p.Box(new Vector3(c.at.x - s, ya, c.at.y - s), new Vector3(c.at.x + s, top, c.at.y + s), f => mat); continue; }
                int n = c.sides; var ring = Enumerable.Range(0, n).Select(i => { float a = Mathf.PI * 2 * (i + 0.5f) / n; return new Vector3(c.at.x + Mathf.Cos(a) * s, 0, c.at.y + Mathf.Sin(a) * s); }).ToArray();
                for (int i = 0; i < n; i++) { var A = ring[i]; var B = ring[(i + 1) % n]; var nr = new Vector3((A.x + B.x) / 2 - c.at.x, 0, (A.z + B.z) / 2 - c.at.y).normalized; p.Quad(A + Vector3.up * ya, B + Vector3.up * ya, B + Vector3.up * top, A + Vector3.up * top, nr, mat); }
                p.Poly(ring.Select(v => v + Vector3.up * top).ToList(), Vector3.up, mat); p.Poly(ring.Select(v => v + Vector3.up * ya).ToList(), Vector3.down, mat);
            }
            foreach (var bm in fl.beams) { int mat = M(bm.mat >= 0 ? bm.mat : 3); var d = new Vector3(bm.b.x - bm.a.x, 0, bm.b.y - bm.a.y); if (d.sqrMagnitude > 1e-6f && Swapped(Building.PieceKind.Beam, Matrix4x4.TRS(new Vector3(bm.a.x, top - bm.depth, bm.a.y), Quaternion.LookRotation(d.normalized), Vector3.one), new Vector3(-bm.width / 2, 0, 0), new Vector3(bm.width / 2, bm.depth, d.magnitude), ref mat, true)) continue; Bar(p, new Vector3(bm.a.x, top - bm.depth, bm.a.y), new Vector3(bm.b.x, top - bm.depth, bm.b.y), bm.width, bm.depth, mat); }
        }

        // ------------------------------------------------------------------ railings on floor edges

        /// <summary>Where stairs coming up from the floor below arrive (no railing across there).</summary>
        public static List<(Vector2 a, Vector2 b)> Arrivals(Building b, int level)
        {
            var l = new List<(Vector2, Vector2)>(); var below = b.FloorAt(level - 1); if (below == null) return l; float H = b.HeightOf(level - 1);
            foreach (var s in below.stairs)
            {
                if (s.lift + s.rise * H < H * 0.98f) continue; var m = StairFrame(s); Vector2 W(Vector2 v) { var w = m.MultiplyPoint3x4(new Vector3(v.x, 0, v.y)); return new Vector2(w.x, w.z); }
                if (s.kind == Building.StairKind.Spiral) { var r = Footprint(s); l.Add((r.min, new Vector2(r.xMax, r.yMin))); l.Add((new Vector2(r.xMax, r.yMin), r.max)); l.Add((r.max, new Vector2(r.xMin, r.yMax))); l.Add((new Vector2(r.xMin, r.yMax), r.min)); continue; }
                var f = Flights(s).Last(); var q = f.r; if (s.kind == Building.StairKind.Ladder) { l.Add((W(new Vector2(q.xMin, q.yMin)), W(new Vector2(q.xMax, q.yMin)))); continue; }
                if (f.up.y > 0) l.Add((W(new Vector2(q.xMin, q.yMax)), W(q.max))); else if (f.up.y < 0) l.Add((W(q.min), W(new Vector2(q.xMax, q.yMin)))); else if (f.up.x > 0) l.Add((W(new Vector2(q.xMax, q.yMin)), W(q.max))); else l.Add((W(q.min), W(new Vector2(q.xMin, q.yMax))));
            }
            return l;
        }
        static void EdgeRails(Building b, Building.Floor fl, Pack p, Dictionary<Vector2Int, int> cover, float y0, Func<int, int> M)
        {
            if (!fl.rails) return; float q = Fine(b); var arrive = Arrivals(b, fl.level); var clean = CleanShapes(b, fl);
            float Lift(int ai) => ai >= 0 && ai < fl.areas.Count ? fl.areas[ai].lift : 0;
            bool Skip(Vector2 mid, Vector2 beyond, bool open)
            {
                if (fl.walls.Any(w => Dist(mid, w) < Thick(b, w))) return true; if (arrive.Any(s => DistSeg2(mid, s.a, s.b) < 0.3f)) return true;
                if (!open) return false; var hole = fl.areas.LastOrDefault(a => a.hole && a.Contains(beyond)); if (hole != null) return !hole.rails; return fl.level == 0 && !StairHoles(b, fl.level).Any(r => r.Contains(beyond)); // (the ground floor's outside edges need none)
            }
            // the edges where the floor stops (or drops from a raised part), unless a wall stands there; joined into straight runs
            var runs = new Dictionary<(bool z, int line, int side, int area), List<int>>();
            foreach (var c in cover.Keys)
            {
                if (clean.Contains(cover[c])) continue; float here = Lift(cover[c]);
                foreach (var (dn, z, line, at, side) in new[] { (Vector2Int.right, true, c.x + 1, c.y, 1), (Vector2Int.left, true, c.x, c.y, -1), (Vector2Int.up, false, c.y + 1, c.x, 1), (Vector2Int.down, false, c.y, c.x, -1) })
                {
                    var n = c + dn; bool open = !cover.TryGetValue(n, out int na); if (!open && here - Lift(na) < 0.3f) continue; var mid = z ? new Vector2(line * q, (at + 0.5f) * q) : new Vector2((at + 0.5f) * q, line * q);
                    if (Skip(mid, mid + (Vector2)(Vector2Int)dn * q * 0.5f, open)) continue;
                    var key = (z, line, side, cover[c]); if (!runs.TryGetValue(key, out var l)) runs[key] = l = new List<int>(); l.Add(at);
                }
            }
            int mat = M(b.trimMat); float inset = 0.05f;
            foreach (var kv in runs) { var l = kv.Value.OrderBy(v => v).ToList(); int s0 = l[0], prev = l[0]; float y = y0 + Lift(kv.Key.area); foreach (var v in l.Skip(1).Concat(new[] { int.MaxValue })) { if (v == prev + 1) { prev = v; continue; } float off = -kv.Key.side * inset; Vector3 A, B; if (kv.Key.z) { A = new Vector3(kv.Key.line * q + off, y, s0 * q); B = new Vector3(kv.Key.line * q + off, y, (prev + 1) * q); } else { A = new Vector3(s0 * q, y, kv.Key.line * q + off); B = new Vector3((prev + 1) * q, y, kv.Key.line * q + off); } Rail(p, A, B, mat, b.RailFor(kv.Key.area >= 0 && kv.Key.area < fl.areas.Count ? fl.areas[kv.Key.area].railing : null)); s0 = prev = v; } }
            // rooms with their own outline: along its true edges
            foreach (int ai in clean)
            {
                var a = fl.areas[ai]; var cs = a.Corners(); var mid0 = cs.Aggregate(Vector2.zero, (s, v) => s + v) / cs.Count;
                for (int k = 0; k < cs.Count; k++) { var e0 = cs[k]; var e1 = cs[(k + 1) % cs.Count]; var mid = (e0 + e1) / 2; var dir = (e1 - e0).normalized; var nrm = new Vector2(-dir.y, dir.x); if (Vector2.Dot(nrm, mid - mid0) < 0) nrm = -nrm; var beyond = mid + nrm * q; if (fl.areas.Any(o => o != a && !o.hole && o.Contains(beyond))) continue; if (Skip(mid, beyond, true)) continue; var ins = -nrm * inset; Rail(p, new Vector3(e0.x + ins.x, y0 + a.lift, e0.y + ins.y), new Vector3(e1.x + ins.x, y0 + a.lift, e1.y + ins.y), mat, b.RailFor(a.railing)); }
            }
        }
        static float DistSeg2(Vector2 p, Vector2 a, Vector2 c) { var ab = c - a; float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude)); return (p - (a + ab * k)).magnitude; }
        static bool Inside(Vector2 u, Building.Area a) => a.Contains(u);

        // ------------------------------------------------------------------ spiral stairs, ladders, railings on stairs, a slope to walk on

        static void Spiral(Pack p, Building.Stair s, float R, int steps, int mat)
        {
            var rs = s_B ? s_B.RailFor(s.railing) : new Building.RailStyle(); float rh = Mathf.Max(0.2f, rs.height); int rmat = rs.mat >= 0 && s_Mats != null ? Mathf.Clamp(rs.mat, 0, s_Mats.Length - 1) : mat;
            float rad = Mathf.Max(0.5f, s.width), pole = 0.08f, th = 0.07f; p.Box(new Vector3(-pole, 0, -pole), new Vector3(pole, R + 0.9f, pole), f => mat);
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.PI * 2 * i / steps, a1 = Mathf.PI * 2 * (i + 1) / steps + 0.02f, y = R * (i + 1) / steps; Vector3 V(float a, float r, float yy) => new Vector3(Mathf.Sin(a) * r, yy, Mathf.Cos(a) * r);
                var top = new[] { V(a0, pole, y), V(a0, rad, y), V(a1, rad, y), V(a1, pole, y) }; p.Poly(top, Vector3.up, mat); p.Poly(top.Select(v => v - Vector3.up * th).ToArray(), Vector3.down, mat);
                p.Quad(V(a0, pole, y - th), V(a0, rad, y - th), V(a0, rad, y), V(a0, pole, y), -new Vector3(Mathf.Cos(a0), 0, -Mathf.Sin(a0)), mat); p.Quad(V(a1, pole, y - th), V(a1, rad, y - th), V(a1, rad, y), V(a1, pole, y), new Vector3(Mathf.Cos(a1), 0, -Mathf.Sin(a1)), mat);
                p.Quad(V(a0, rad, y - th), V(a1, rad, y - th), V(a1, rad, y), V(a0, rad, y), V((a0 + a1) / 2, 1, 0), mat);
                if (s.railSides != Building.RailSides.None) { var r0 = V(a0, rad - 0.04f, y); var r1 = V(a1, rad - 0.04f, R * (i + 2) / steps); if (rs.support != Building.RailSupport.None) Post(p, r0, rh, Mathf.Max(0.02f, rs.postSize * 0.8f), rs, Quaternion.Euler(0, a0 * Mathf.Rad2Deg, 0), rmat); if (rs.handrail) Bar(p, r0 + Vector3.up * (rh - 0.05f), r1 + Vector3.up * (rh - 0.05f), 0.05f, 0.05f, rmat); }
            }
        }
        static void Ladder(Pack p, Building.Stair s, float R, int mat)
        {
            float w = Mathf.Clamp(s.width, 0.35f, 1f) / 2, top = R + 0.9f; p.Box(new Vector3(-w - 0.03f, 0, -0.03f), new Vector3(-w + 0.03f, top, 0.03f), f => mat); p.Box(new Vector3(w - 0.03f, 0, -0.03f), new Vector3(w + 0.03f, top, 0.03f), f => mat);
            for (float y = 0.3f; y < R + 0.05f; y += 0.3f) { int rm = mat; if (!Swapped(Building.PieceKind.LadderRung, p.m, new Vector3(-w, y - 0.02f, -0.02f), new Vector3(w, y + 0.02f, 0.02f), ref rm)) p.Box(new Vector3(-w, y - 0.02f, -0.02f), new Vector3(w, y + 0.02f, 0.02f), f => rm); }
        }
        /// <summary>Railings on both sides of each flight, and the slope a character walks on (for smooth stairs).</summary>
        static void StairExtras(Building b, Building.Floor fl, Building.Stair s, Pack visual, Pack slope, float R, int mat)
        {
            var style = b.RailFor(s.railing); var sides = s.railSides; bool railed = sides != Building.RailSides.None && (s.kind != Building.StairKind.Ramp || sides != Building.RailSides.Auto);
            foreach (var f in Flights(s))
            {
                float ya = f.y0 * R, yb = f.y1 * R; var r = f.r;
                if (f.up == Vector2.zero) { if (slope != null) slope.Box(new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, Mathf.Max(0.02f, ya), r.yMax), k => 0, k => k == 3); continue; }
                Vector3 S0, S1, E0, E1; // the slope's corners at its start and end
                if (f.up.x == 0) { float zs = f.up.y > 0 ? r.yMin : r.yMax, ze = f.up.y > 0 ? r.yMax : r.yMin; S0 = new Vector3(r.xMin, ya, zs); S1 = new Vector3(r.xMax, ya, zs); E0 = new Vector3(r.xMin, yb, ze); E1 = new Vector3(r.xMax, yb, ze); }
                else { float xs = f.up.x > 0 ? r.xMin : r.xMax, xe = f.up.x > 0 ? r.xMax : r.xMin; S0 = new Vector3(xs, ya, r.yMin); S1 = new Vector3(xs, ya, r.yMax); E0 = new Vector3(xe, yb, r.yMin); E1 = new Vector3(xe, yb, r.yMax); }
                if (slope != null) Ramp(slope, f.r, ya, yb, 0, f.up); // (a closed wedge, so it's solid from the sides too)
                if (railed) foreach (var (a, c) in new[] { (S0, E0), (S1, E1) })
                    {
                        // which side this is for someone walking up: left or right
                        var outward = a == S0 ? S0 - S1 : S1 - S0; bool left = Vector2.Dot(new Vector2(outward.x, outward.z), new Vector2(-f.up.y, f.up.x)) > 0;
                        if (sides == Building.RailSides.Left && !left || sides == Building.RailSides.Right && left) continue;
                        if (sides == Building.RailSides.Auto && AlongWall(b, fl, visual.m, a, c)) continue;
                        var inA = a + (S1 - S0).normalized * (a == S0 ? 0.05f : -0.05f); var inC = c + (S1 - S0).normalized * (a == S0 ? 0.05f : -0.05f); Rail(visual, inA, inC, mat, style);
                    }
            }
        }

        /// <summary>Whether a stair's side (a to c, in the stair's frame) runs along a wall on its floor.</summary>
        public static bool AlongWall(Building b, Building.Floor fl, Matrix4x4 m, Vector3 a, Vector3 c)
        {
            if (fl == null) return false; int near = 0;
            foreach (float k in new[] { 0.15f, 0.5f, 0.85f }) { var w = m.MultiplyPoint3x4(Vector3.Lerp(a, c, k)); var u = new Vector2(w.x, w.z); if (fl.walls.Any(q => Dist(u, q) < Thick(b, q) / 2 + 0.25f)) near++; }
            return near >= 2;
        }

        // ------------------------------------------------------------------ doors and windows: frames, sills, glass, leaves

        public struct Leaf { public Matrix4x4 m; public float x0, x1, y0, y1, t; public Building.DoorSwing swing; public bool hingeOther, two; public GameObject model; public bool fit; public int mat, wall, opening; }
        static void Dressing(Building b, Building.Opening o, Pack p, Pack glass, List<Leaf> leaves, Matrix4x4 frame, float a, float c, float ob, float ot, float t, float bot, int trim)
        {
            float tw = b.trimWidth, d = 0.03f; bool arch = o.kind == Building.OpeningKind.Arch; int dummy = 0; if (o.frameMat >= 0 && s_Mats != null) trim = Mathf.Clamp(o.frameMat, 0, s_Mats.Length - 1);
            bool own = o.model && o.fill && o.kind != Building.OpeningKind.Door; if (own) s_Placed?.Add(new Placed { model = o.model, m = frame * Matrix4x4.Translate(new Vector3((a + c) / 2, ob, 0)), size = new Vector3(c - a, ot - ob, t), fit = o.fit, collide = true });
            if (!own && (o.kind == Building.OpeningKind.Window || arch && ob > bot + 0.05f) && o.fill && Swapped(Building.PieceKind.Window, frame, new Vector3(a, ob, -t / 2), new Vector3(c, ot, t / 2), ref dummy, true)) return;
            if (o.frame && tw > 0 && o.kind != Building.OpeningKind.Gap)
                foreach (float sd in new[] { 1f, -1f })
                {
                    float z0 = sd > 0 ? t / 2 : -t / 2 - d, z1 = sd > 0 ? t / 2 + d : -t / 2; float post = arch ? Arc(b, o, a, c, ob, ot).spring : ot + tw; p.Box(new Vector3(a - tw, ob, z0), new Vector3(a, post, z1), f => trim); p.Box(new Vector3(c, ob, z0), new Vector3(c + tw, post, z1), f => trim);
                    if (!arch) p.Box(new Vector3(a, ot, z0), new Vector3(c, ot + tw, z1), f => trim);
                    else { var (pts, cx, spring, rx, ry) = Arc(b, o, a, c, ob, ot); for (int k = 0; k + 1 < pts.Length; k++) { Vector2 O(Vector2 v) => new Vector2(cx + (v.x - cx) * (rx + tw) / rx, spring + (v.y - spring) * (ry + tw) / Mathf.Max(1e-4f, ry)); p.Prism(new[] { pts[k], pts[k + 1], O(pts[k + 1]), O(pts[k]) }, z0, z1, trim, trim, trim, e => true); } }
                }
            if (o.kind == Building.OpeningKind.Window || arch && ob > bot + 0.05f) { if (o.frame) p.Box(new Vector3(a - tw, ob - 0.05f, -t / 2 - 0.07f), new Vector3(c + tw, ob, t / 2 + 0.07f), f => trim); if (o.fill && !own) { var m = glass.m; glass.m = frame; if (arch) { var arc = Arc(b, o, a, c, ob, ot); var gp = new List<Vector3> { new Vector3(a, ob, 0), new Vector3(c, ob, 0) }; gp.AddRange(arc.pts.Reverse().Select(v => new Vector3(v.x, v.y, 0))); glass.Poly(gp, Vector3.forward, 0); } else { glass.Quad(new Vector3(a, ob, 0), new Vector3(c, ob, 0), new Vector3(c, ot, 0), new Vector3(a, ot, 0), Vector3.forward, 0); } glass.m = m; } }
            if (o.kind == Building.OpeningKind.Door && o.fill && b.doorLeaves) leaves.Add(new Leaf { m = frame, x0 = a, x1 = c, y0 = ob, y1 = ot, t = t, swing = o.swing, hingeOther = o.hingeOther, two = o.doubleDoor, model = o.model, fit = o.fit, mat = o.leafMat, wall = s_Wall, opening = s_Wall >= 0 && s_B ? s_B.FloorAt(s_Level)?.walls[s_Wall].openings.IndexOf(o) ?? -1 : -1 });
        }
        /// <summary>The curve over an arch: its points (left to right), its middle, where it springs from and its two radii.</summary>
        public static (Vector2[] pts, float cx, float spring, float rx, float ry) Arc(Building b, Building.Opening o, float a, float c, float ob, float ot)
        {
            float r = (c - a) / 2, cx = (a + c) / 2, spring = Mathf.Max(ob, ot - r), ry = ot - spring; int n = Mathf.Clamp(o.sides > 0 ? o.sides : b.roundSides, 2, 32); var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) { float an = Mathf.PI * (1 - i / (float)n); pts[i] = new Vector2(cx + Mathf.Cos(an) * r, spring + Mathf.Sin(an) * ry); }
            return (pts, cx, spring, r, ry);
        }
        static Material s_Glass; public static Material Glass()
        {
            if (s_Glass) return s_Glass; const string path = "Assets/Buildings/Glass.mat"; s_Glass = AssetDatabase.LoadAssetAtPath<Material>(path); if (s_Glass) return s_Glass; if (!AssetDatabase.IsValidFolder("Assets/Buildings")) AssetDatabase.CreateFolder("Assets", "Buildings");
            var sh = Shader.Find("Universal Render Pipeline/Lit"); s_Glass = new Material(sh ? sh : Shader.Find("Standard")); s_Glass.name = "Glass";
            if (sh) { s_Glass.SetFloat("_Surface", 1); s_Glass.SetFloat("_Blend", 0); s_Glass.SetFloat("_ZWrite", 0); s_Glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); s_Glass.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); s_Glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); s_Glass.SetFloat("_Cull", 0); s_Glass.SetColor("_BaseColor", new Color(0.7f, 0.82f, 0.9f, 0.28f)); s_Glass.SetFloat("_Smoothness", 0.92f); }
            else { s_Glass.SetFloat("_Mode", 3); s_Glass.SetColor("_Color", new Color(0.7f, 0.82f, 0.9f, 0.28f)); s_Glass.EnableKeyword("_ALPHABLEND_ON"); }
            s_Glass.renderQueue = 3000; AssetDatabase.CreateAsset(s_Glass, path); return s_Glass;
        }
        static void MakeLeaves(Building b, GameObject parent, List<Leaf> leaves, Material mat, Func<int, Material> Mat, int level, bool outside)
        {
            var sw = b.SwapFor(Building.PieceKind.DoorLeaf); if (sw != null && sw.material) mat = b.psx ? PSX(sw.material, b) : sw.material;
            foreach (var l in leaves)
            {
                // one leaf, or two meeting in the middle; a leaf hinged on the far side is turned round, so "away from you" still works
                var parts = l.two ? new[] { (l.x0, (l.x0 + l.x1) / 2, false), ((l.x0 + l.x1) / 2, l.x1, true) } : new[] { (l.x0, l.x1, l.hingeOther) };
                foreach (var (x0, x1, flip) in parts)
                {
                    float w = x1 - x0 - (l.two ? 0.015f : 0.02f), h = l.y1 - l.y0 - 0.02f, th = 0.05f; var hinge = l.m.MultiplyPoint3x4(new Vector3(flip ? x1 - 0.01f : x0 + 0.01f, l.y0 + 0.01f, 0)); var rot = l.m.rotation * (flip ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
                    var go = new GameObject(outside ? "Door (outside)" : "Door"); go.transform.SetParent(parent.transform, false); go.transform.localPosition = hinge; go.transform.localRotation = rot; go.layer = parent.layer;
                    var model = l.model ? l.model : sw?.model; bool fit = l.model ? l.fit : sw == null || sw.fit;
                    if (model) PlaceModels(go, new List<Placed> { new Placed { model = model, m = Matrix4x4.Translate(new Vector3(w / 2, 0, 0)), size = new Vector3(w, h, th), fit = fit, collide = true } });
                    else { var lm = l.mat >= 0 ? Mat(l.mat) : mat; var pk = new Pack(b.textureSize); pk.Box(new Vector3(0, 0, -th / 2), new Vector3(w, h, th / 2), f => 0); var mesh = pk.Make("Door", out _); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = lm; var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(w / 2, h / 2, 0); bc.size = new Vector3(w, h, th); }
                    // (turning it by + takes it to its own back: so a turned-round leaf goes the other way)
                    var dr = go.AddComponent<BuildingDoor>(); dr.bothWays = l.swing == Building.DoorSwing.BothWays; dr.angle = (l.swing == Building.DoorSwing.ToTheFront ? -95 : 95) * (flip ? -1 : 1);
                    var mk = go.AddComponent<BuildingMade>(); mk.level = level; mk.wall = l.wall; mk.opening = l.opening;
                }
            }
        }

        // ------------------------------------------------------------------ the ground under it

        /// <summary>How far down a wall on the ground floor goes: the foundation, or lower where the ground (anything that isn't the building) is lower.</summary>
        static float GroundUnder(Building b, Vector2 a, Vector2 c)
        {
            if (b.foundation <= 0) return 0; float low = -b.foundation;
            for (int i = 0; i <= 4; i++)
            {
                var u = Vector2.Lerp(a, c, i / 4f); var w = b.transform.TransformPoint(new Vector3(u.x, 1, u.y)); foreach (var h in Physics.RaycastAll(w, Vector3.down, 60).OrderBy(h => h.distance)) { if (h.collider.transform.IsChildOf(b.transform)) continue; low = Mathf.Min(low, b.transform.InverseTransformPoint(h.point).y - 0.15f); break; }
            }
            return Mathf.Max(low, -30);
        }
    }
}
