using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Turns a Building's plan into meshes: one object per floor (everything solid in one mesh, with one collider),
    /// your own models as children, and the floor above opened where stairs come up.</summary>
    public static partial class BuildingMaker
    {
        // ------------------------------------------------------------------ meshes with one part per material

        public class Pack
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly Dictionary<int, List<int>> tris = new Dictionary<int, List<int>>(); readonly float tex; public Matrix4x4 m = Matrix4x4.identity;
            public Pack(float textureSize) { tex = Mathf.Max(0.01f, textureSize); }
            public float split;
            public bool Empty => v.Count == 0;
            /// <summary>A flat face (any number of corners, convex), textured from where it is so neighbours carry on the texture.</summary>
            public void Poly(IList<Vector3> pts, Vector3 normal, int mat)
            {
                if (pts.Count < 3) return;
                if (split > 0 && pts.Count == 4) { var wa = m.MultiplyPoint3x4(pts[0]); int nu = Mathf.Clamp(Mathf.CeilToInt((m.MultiplyPoint3x4(pts[1]) - wa).magnitude / split), 1, 32), nv = Mathf.Clamp(Mathf.CeilToInt((m.MultiplyPoint3x4(pts[3]) - wa).magnitude / split), 1, 32); if (nu * nv > 1) { float sp = split; split = 0; Vector3 Q(float u, float v) => Vector3.Lerp(Vector3.Lerp(pts[0], pts[1], u), Vector3.Lerp(pts[3], pts[2], u), v); for (int iu = 0; iu < nu; iu++) for (int k = 0; k < nv; k++) Poly(new[] { Q(iu / (float)nu, k / (float)nv), Q((iu + 1f) / nu, k / (float)nv), Q((iu + 1f) / nu, (k + 1f) / nv), Q(iu / (float)nu, (k + 1f) / nv) }, normal, mat); split = sp; return; } }
                var w = pts.Select(p => m.MultiplyPoint3x4(p)).ToList(); normal = m.MultiplyVector(normal).normalized; var c = Vector3.zero; for (int k = 0; k < w.Count; k++) { var a0 = w[k]; var a1 = w[(k + 1) % w.Count]; c.x += (a0.y - a1.y) * (a0.z + a1.z); c.y += (a0.z - a1.z) * (a0.x + a1.x); c.z += (a0.x - a1.x) * (a0.y + a1.y); } if (Vector3.Dot(c, normal) < 0) w.Reverse();
                int i = v.Count; foreach (var p in w) { v.Add(p); n.Add(normal); uv.Add(UV(p, normal)); } if (!tris.TryGetValue(mat, out var l)) tris[mat] = l = new List<int>(); for (int k = 1; k < w.Count - 1; k++) { l.Add(i); l.Add(i + k); l.Add(i + k + 1); }
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int mat) => Poly(new[] { a, b, c, d }, normal, mat);
            Vector2 UV(Vector3 p, Vector3 nr) { var a = new Vector3(Mathf.Abs(nr.x), Mathf.Abs(nr.y), Mathf.Abs(nr.z)); return (a.y >= a.x && a.y >= a.z ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z * Mathf.Sign(nr.x), p.y) : new Vector2(-p.x * Mathf.Sign(nr.z), p.y)) / tex; }
            /// <summary>A box; faces: 0 +x, 1 -x, 2 top, 3 bottom, 4 +z, 5 -z.</summary>
            public void Box(Vector3 lo, Vector3 hi, Func<int, int> mat, Func<int, bool> skip = null)
            {
                if (hi.x - lo.x < 1e-4f || hi.y - lo.y < 1e-4f || hi.z - lo.z < 1e-4f) return;
                Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z);
                void F(int f, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nr) { if (skip != null && skip(f)) return; Quad(a, b, c, d, nr, mat(f)); }
                F(0, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), Vector3.right); F(1, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.left);
                F(2, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up); F(3, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.down);
                F(4, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.forward); F(5, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), Vector3.back);
            }
            /// <summary>A flat outline (x, y) pushed through from z0 to z1: front (+z), back (-z) and the edges asked for.</summary>
            public void Prism(Vector2[] poly, float z0, float z1, int front, int back, int sides, Func<int, bool> edge)
            {
                Poly(poly.Select(p => new Vector3(p.x, p.y, z1)).ToList(), Vector3.forward, front); Poly(poly.Select(p => new Vector3(p.x, p.y, z0)).ToList(), Vector3.back, back);
                var mid = poly.Aggregate(Vector2.zero, (s, p) => s + p) / poly.Length;
                for (int i = 0; i < poly.Length; i++) { if (!edge(i)) continue; var a = poly[i]; var b = poly[(i + 1) % poly.Length]; var d = b - a; var nr = new Vector3(d.y, -d.x, 0).normalized; if (Vector2.Dot(new Vector2(nr.x, nr.y), (a + b) / 2 - mid) < 0) nr = -nr; Quad(new Vector3(a.x, a.y, z0), new Vector3(b.x, b.y, z0), new Vector3(b.x, b.y, z1), new Vector3(a.x, a.y, z1), nr, sides); }
            }
            public Mesh Make(string name, out int[] mats)
            {
                var mesh = new Mesh { name = name }; if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mats = tris.Keys.OrderBy(k => k).ToArray(); mesh.subMeshCount = mats.Length;
                for (int s = 0; s < mats.Length; s++) mesh.SetTriangles(tris[mats[s]], s); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
            }
        }

        // ------------------------------------------------------------------ the whole building

        public static void Rebuild(Building b)
        {
            if (!b) return;
            foreach (var old in b.GetComponentsInChildren<BuildingMade>(true).Where(m => m && m.transform.parent == b.transform).ToList()) { foreach (var f in old.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh && !AssetDatabase.Contains(f.sharedMesh)) UnityEngine.Object.DestroyImmediate(f.sharedMesh); UnityEngine.Object.DestroyImmediate(old.gameObject); }
            var mats = Materials(b);
            foreach (var fl in b.floors.ToList()) MakeFloor(b, fl, ref mats);
        }

        static Material s_Default; static Material Default() { if (s_Default) return s_Default; var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); s_Default = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "Building grey" }; return s_Default; }
        static Material[] Materials(Building b)
        {
            var l = new List<Material>(); for (int i = 0; i < Mathf.Max(4, b.materials.Count); i++) l.Add(i < b.materials.Count ? b.materials[i] : null);
            for (int i = 0; i < l.Count; i++) if (!l[i]) l[i] = Default(); if (b.psx) for (int i = 0; i < l.Count; i++) l[i] = PSX(l[i], b); return l.ToArray();
        }
        /// <summary>The same material with the old-console shader (made once in Assets/Buildings/PSX, kept up to date).</summary>
        public static Material PSX(Material m, Building b)
        {
            var sh = Shader.Find("HienaDev/PSX Lit"); if (!sh || !m) return m; if (m.shader == sh) return m; if (!AssetDatabase.Contains(m)) return m; const string dir = "Assets/Buildings/PSX"; if (!AssetDatabase.IsValidFolder("Assets/Buildings")) AssetDatabase.CreateFolder("Assets", "Buildings"); if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Buildings", "PSX");
            var path = dir + "/" + m.name + " PSX.mat"; var p = AssetDatabase.LoadAssetAtPath<Material>(path); bool make = !p; if (make) p = new Material(sh);
            var sl = TextureSlots.Guess(m.shader); var tex = sl.colour != "" && m.HasProperty(sl.colour) ? m.GetTexture(sl.colour) : m.mainTexture; p.SetTexture("_BaseMap", tex); if (sl.colour != "" && m.HasProperty(sl.colour)) { p.SetTextureScale("_BaseMap", m.GetTextureScale(sl.colour)); p.SetTextureOffset("_BaseMap", m.GetTextureOffset(sl.colour)); }
            p.SetColor("_BaseColor", m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white); p.SetFloat("_Wobble", b.psxWobble); p.SetFloat("_Swim", b.psxSwim); p.SetFloat("_Colours", b.psxColours);
            if (make) AssetDatabase.CreateAsset(p, path); else EditorUtility.SetDirty(p); return p;
        }
        public static BuildPart Part(string id) { if (string.IsNullOrEmpty(id) || id.StartsWith("builtin:")) return null; var path = AssetDatabase.GUIDToAssetPath(id); return path.Length > 0 ? AssetDatabase.LoadAssetAtPath<BuildPart>(path) : null; }
        public static Bounds Bounds(GameObject o) { var rs = o.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(o.transform.position, Vector3.one); var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); return bb; }

        // ------------------------------------------------------------------ floors: rectangles, laid on a fine grid

        public static float Fine(Building b) => Mathf.Max(0.05f, b.cell * 0.25f);
        /// <summary>Which fine squares a floor covers (and which of its rectangles each one comes from), holes and stair openings taken out.</summary>
        public static Dictionary<Vector2Int, int> Cover(Building b, Building.Floor fl, bool roofs = false)
        {
            var d = new Dictionary<Vector2Int, int>(); if (fl == null) return d; float q = Fine(b);
            IEnumerable<Vector2Int> Inside(Vector2 lo, Vector2 hi) { var a = Vector2.Min(lo, hi); var z = Vector2.Max(lo, hi); int x0 = Mathf.RoundToInt(a.x / q), x1 = Mathf.RoundToInt(z.x / q), y0 = Mathf.RoundToInt(a.y / q), y1 = Mathf.RoundToInt(z.y / q); for (int x = x0; x < x1; x++) for (int y = y0; y < y1; y++) yield return new Vector2Int(x, y); }
            for (int i = 0; i < fl.areas.Count; i++) { var ar = fl.areas[i]; if (roofs && !ar.roof && !ar.hole) continue; foreach (var c in Inside(ar.min, ar.max)) { if (ar.IsShape && !ar.Contains(new Vector2((c.x + 0.5f) * q, (c.y + 0.5f) * q))) continue; if (ar.hole) d.Remove(c); else d[c] = i; } }
            if (!roofs) foreach (var r in StairHoles(b, fl.level)) foreach (var c in Inside(r.min, r.max)) d.Remove(c);
            return d;
        }
        /// <summary>Where stairs from the floor below come up through this floor.</summary>
        public static List<Rect> StairHoles(Building b, int level) { var l = new List<Rect>(); var below = b.FloorAt(level - 1); if (below == null) return l; float H = b.HeightOf(level - 1); foreach (var s in below.stairs) if (s.lift + s.rise * H >= H * 0.98f) l.Add(Footprint(s)); return l; }

        static void FloorMesh(Building b, Building.Floor fl, Pack p, float y0, float H, float T)
        {
            float q = Fine(b); var cover = Cover(b, fl); var above = Cover(b, b.FloorAt(fl.level + 1)); int M(int i, int f) => i >= 0 ? i : f; float under = fl.level == 0 && b.foundation > 0 ? -b.foundation : y0 - T;
            // raised (or sunk) parts of the floor each get their own slab, from the bottom up to their top
            var clean = CleanShapes(b, fl); foreach (int i in clean) { var a = fl.areas[i]; float ya = a.solidBelow && fl.level != 0 ? b.Y(fl.level - 1) + 0.002f : Mathf.Min(under, y0 - T + a.lift); ShapeSlab(p, a.Corners(), ya, y0 + a.lift, M(a.top, 0), M(a.under, 2), M(a.top, 0)); }
            cover = cover.Where(kv => !clean.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value); float floorBelow = fl.level != 0 ? b.Y(fl.level - 1) + 0.002f : under;
            foreach (var g in cover.GroupBy(kv => (Mathf.Round(fl.areas[kv.Value].lift * 1000) / 1000, fl.areas[kv.Value].solidBelow))) { var part = g.ToDictionary(kv => kv.Key, kv => kv.Value); float lift = g.Key.Item1; if (g.Key.Item2) { Slab(p, part, q, floorBelow, y0 + lift, k => M(fl.areas[k].top, 0), k => M(fl.areas[k].under, 2), k => M(fl.areas[k].top, 0)); continue; } Slab(p, part, q, Mathf.Min(under, y0 - T + lift), y0 + lift, k => M(fl.areas[k].top, 0), k => M(fl.areas[k].under, 2), k => M(fl.areas[k].top, 0)); }
            // lids on top (tunnels, roofs) where the floor above has nothing
            var lids = Cover(b, fl, true).Where(kv => !above.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value); if (lids.Count > 0) Slab(p, lids, q, y0 + H - T, y0 + H, k => M(fl.areas[k].roofMat, 1), k => M(fl.areas[k].under, 2), k => M(fl.areas[k].roofMat, 1));
        }
        /// <summary>A slab over the covered squares, joined into as few rectangles as it can (per material).</summary>
        static void Slab(Pack p, Dictionary<Vector2Int, int> cover, float q, float ya, float yb, Func<int, int> topMat, Func<int, int> underMat, Func<int, int> sideMat)
        {
            foreach (var (mat, y, nr) in new[] { (topMat, yb, Vector3.up), (underMat, ya, Vector3.down) })
            {
                var left = new Dictionary<Vector2Int, int>(); foreach (var kv in cover) left[kv.Key] = mat(kv.Value);
                foreach (var c in left.Keys.OrderBy(k => k.y).ThenBy(k => k.x).ToList())
                {
                    if (!left.TryGetValue(c, out int m)) continue; int w = 1; while (left.TryGetValue(c + new Vector2Int(w, 0), out int m2) && m2 == m) w++;
                    int h = 1; while (true) { bool ok = true; for (int x = 0; x < w && ok; x++) ok = left.TryGetValue(c + new Vector2Int(x, h), out int m3) && m3 == m; if (!ok) break; h++; }
                    for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) left.Remove(c + new Vector2Int(x, z));
                    float x0 = c.x * q, x1 = (c.x + w) * q, z0 = c.y * q, z1 = (c.y + h) * q; p.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), nr, m);
                }
            }
            // the edges, only where the slab ends
            foreach (var kv in cover)
            {
                var c = kv.Key; int m = sideMat(kv.Value); float x0 = c.x * q, x1 = x0 + q, z0 = c.y * q, z1 = z0 + q;
                if (!cover.ContainsKey(c + Vector2Int.right)) p.Quad(new Vector3(x1, ya, z0), new Vector3(x1, yb, z0), new Vector3(x1, yb, z1), new Vector3(x1, ya, z1), Vector3.right, m);
                if (!cover.ContainsKey(c + Vector2Int.left)) p.Quad(new Vector3(x0, ya, z0), new Vector3(x0, ya, z1), new Vector3(x0, yb, z1), new Vector3(x0, yb, z0), Vector3.left, m);
                if (!cover.ContainsKey(c + Vector2Int.up)) p.Quad(new Vector3(x0, ya, z1), new Vector3(x1, ya, z1), new Vector3(x1, yb, z1), new Vector3(x0, yb, z1), Vector3.forward, m);
                if (!cover.ContainsKey(c + Vector2Int.down)) p.Quad(new Vector3(x0, ya, z0), new Vector3(x0, yb, z0), new Vector3(x1, yb, z0), new Vector3(x1, ya, z0), Vector3.back, m);
            }
        }

        // ------------------------------------------------------------------ walls, with their doors and windows

        public static float Thick(Building b, Building.Wall w) => w.thickness > 0 ? w.thickness : b.wallThickness;
        public static float Top(Building b, Building.Wall w, float H) => w.height > 0 ? Mathf.Min(w.bottom + w.height, H) : H - b.slabThickness;
        public class Extras { public Pack glass; public List<Leaf> leaves = new List<Leaf>(); public Func<int, int> M; }
        static void WallMesh(Building b, Building.Floor fl, Building.Wall w, Pack p, float y0, float H, Extras x, bool? meet0 = null, bool? meet1 = null)
        {
            float len = w.Length; if (len < 1e-3f) return; float t = Thick(b, w), top = Top(b, w, H), bot = w.bottom > 0 ? w.bottom : fl.level == 0 ? 0 : -b.slabThickness;
            if (fl.level == 0 && w.bottom <= 0 && b.foundation > 0) bot = GroundUnder(b, w.a, w.b);
            // a curved wall is made of short straight ones that join without seams
            if (Mathf.Abs(w.bend) > 0.01f && meet0 == null)
            {
                int n = Mathf.Clamp(b.roundSides * 2, 4, 48); var d0 = (w.b - w.a) / len; var nf = new Vector2(-d0.y, d0.x) * -1; Vector2 Pt(float s) => Vector2.Lerp(w.a, w.b, s) + nf * w.bend * 4 * s * (1 - s);
                bool e0 = fl.walls.Any(o => o != w && ((o.a - w.a).sqrMagnitude < 1e-4f || (o.b - w.a).sqrMagnitude < 1e-4f)), e1 = fl.walls.Any(o => o != w && ((o.a - w.b).sqrMagnitude < 1e-4f || (o.b - w.b).sqrMagnitude < 1e-4f));
                for (int i = 0; i < n; i++) { var seg = new Building.Wall { a = Pt(i / (float)n), b = Pt((i + 1f) / n), height = w.height, bottom = w.bottom, thickness = w.thickness, front = w.front, back = w.back, frontFinish = w.frontFinish, backFinish = w.backFinish }; WallMesh(b, fl, seg, p, y0, H, x, i == 0 ? e0 : true, i == n - 1 ? e1 : true); }
                return;
            }
            // where it meets another wall, it goes on by half a thickness (so corners close) and shows no end
            bool Meets(Vector2 e) => fl.walls.Any(o => o != w && ((o.a - e).sqrMagnitude < 1e-4f || (o.b - e).sqrMagnitude < 1e-4f || Dist(e, o) < 1e-3f));
            bool m0 = meet0 ?? Meets(w.a), m1 = meet1 ?? Meets(w.b); float e0x = m0 ? -t / 2 : 0, e1x = m1 ? len + t / 2 : len;
            var d = (w.b - w.a) / len; var right = new Vector3(d.x, 0, d.y); var fwd = Vector3.Cross(right, Vector3.up); var frame = Matrix4x4.TRS(new Vector3(w.a.x, y0, w.a.y), Quaternion.LookRotation(fwd, Vector3.up), Vector3.one); p.m = frame;
            int fr = x.M(w.front >= 0 ? w.front : 1), bk = x.M(w.back >= 0 ? w.back : 1), trim = x.M(b.trimMat); int FM(int f) => f == 4 ? fr : f == 5 ? bk : fr;
            void Seg(float x0, float x1, float ya, float yb) => p.Box(new Vector3(x0, ya, -t / 2), new Vector3(x1, yb, t / 2), FM, f => f == 1 && x0 <= e0x + 1e-4f && m0 || f == 0 && x1 >= e1x - 1e-4f && m1);
            var ops = w.openings.Where(o => o.width > 0.01f && meet0 == null).OrderBy(o => o.at).ToList(); float xx = e0x; var spans = new List<(float a, float c, float ob, float ot)>();
            foreach (var o in ops)
            {
                float a = Mathf.Clamp(o.at - o.width / 2, 0, len), c = Mathf.Clamp(o.at + o.width / 2, 0, len); if (c - a < 0.01f || a < xx - 1e-3f) continue; Seg(xx, a, bot, top); xx = c;
                float ob = o.kind == Building.OpeningKind.Door || o.kind == Building.OpeningKind.Gap ? Mathf.Max(bot, 0) : Mathf.Clamp(o.bottom, bot, top), ot = o.kind == Building.OpeningKind.Gap ? top : Mathf.Clamp(o.top, ob, top); spans.Add((a, c, ob, ot));
                if (ob > bot + 1e-3f) Seg(a, c, bot, ob);
                if (o.kind == Building.OpeningKind.Arch && ot > ob)
                {
                    // the top is a half circle (as wide as it is), in a few flat faces
                    float r = (c - a) / 2, cx = (a + c) / 2, spring = Mathf.Max(ob, ot - r); int n = Mathf.Clamp(o.sides > 0 ? o.sides : b.roundSides, 2, 32); var pts = new Vector2[n + 1]; for (int i = 0; i <= n; i++) { float an = Mathf.PI * (1 - i / (float)n); pts[i] = new Vector2(cx + Mathf.Cos(an) * r, spring + Mathf.Sin(an) * (ot - spring)); }
                    for (int i = 0; i < n; i++) p.Prism(new[] { pts[i], pts[i + 1], new Vector2(pts[i + 1].x, ot), new Vector2(pts[i].x, ot) }, -t / 2, t / 2, fr, bk, fr, k => k == 0);
                }
                if (ot < top - 1e-3f) Seg(a, c, ot, top);
                Dressing(b, o, p, x.glass, x.leaves, frame, a, c, ob, ot, t, bot, trim);
            }
            Seg(xx, e1x, bot, top);
            // skirting, crown moulding and wainscoting, on each side that has them, stopping at doors and windows
            foreach (var (sd, fin) in new[] { (1f, w.frontFinish), (-1f, w.backFinish) })
            {
                if (fin == Building.Finish.None) continue; float fy = Mathf.Max(0, bot);
                void Strip(float y0s, float y1s, float depth, int mat) { float z0 = sd > 0 ? t / 2 : -t / 2 - depth, z1 = sd > 0 ? t / 2 + depth : -t / 2; float s0 = e0x; foreach (var sp in spans.Where(q => q.ob < y1s && q.ot > y0s)) { if (sp.a > s0) p.Box(new Vector3(s0, y0s, z0), new Vector3(sp.a, y1s, z1), f => mat); s0 = Mathf.Max(s0, sp.c); } if (e1x > s0) p.Box(new Vector3(s0, y0s, z0), new Vector3(e1x, y1s, z1), f => mat); }
                if ((fin & Building.Finish.Wainscot) != 0) Strip(fy, fy + b.wainscotHeight, 0.012f, x.M(b.wainscotMat >= 0 ? b.wainscotMat : b.trimMat));
                if ((fin & Building.Finish.Skirting) != 0) Strip(fy, fy + b.skirtingHeight, 0.025f, trim);
                if ((fin & Building.Finish.Crown) != 0) Strip(top - b.crownHeight, top, 0.035f, trim);
            }
            p.m = Matrix4x4.identity;
        }
        public static float Dist(Vector2 pt, Building.Wall w) { var ab = w.b - w.a; float k = Mathf.Clamp01(Vector2.Dot(pt - w.a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude)); return (pt - (w.a + ab * k)).magnitude; }

        // ------------------------------------------------------------------ stairs

        /// <summary>The flights and the landing of a stair, in its own frame (x across, z forward), with the part of the rise each starts and ends at.</summary>
        public static List<(Rect r, float y0, float y1, Vector2 up)> Flights(Building.Stair s)
        {
            float W = Mathf.Max(0.2f, s.width), L = Mathf.Max(0.3f, s.length); var l = new List<(Rect, float, float, Vector2)>();
            switch (s.kind)
            {
                case Building.StairKind.TurnLeft: case Building.StairKind.TurnRight: { float sg = s.kind == Building.StairKind.TurnRight ? 1 : -1, L1 = Mathf.Max(0.3f, L / 2); l.Add((new Rect(-W / 2, 0, W, L1), 0, 0.5f, Vector2.up)); l.Add((new Rect(-W / 2, L1, W, W), 0.5f, 0.5f, Vector2.zero)); float x0 = sg > 0 ? W / 2 : -W / 2 - L1; l.Add((new Rect(x0, L1, L1, W), 0.5f, 1, new Vector2(sg, 0))); break; }
                case Building.StairKind.Spiral: { float R = Mathf.Max(0.5f, s.width); l.Add((new Rect(-R, -R, R * 2, R * 2), 0, 1, Vector2.up)); break; }
                case Building.StairKind.Ladder: { float w = Mathf.Clamp(s.width, 0.35f, 1f); l.Add((new Rect(-w / 2, -0.1f, w, 0.8f), 0, 1, Vector2.up)); break; }
                case Building.StairKind.UTurn: { float L1 = Mathf.Max(0.3f, L / 2); l.Add((new Rect(-W / 2, 0, W, L1), 0, 0.5f, Vector2.up)); l.Add((new Rect(-W / 2, L1, W * 2, W), 0.5f, 0.5f, Vector2.zero)); l.Add((new Rect(W / 2, 0, W, L1), 0.5f, 1, Vector2.down)); break; }
                default: l.Add((new Rect(-W / 2, 0, W, L), 0, 1, Vector2.up)); break;
            }
            return l;
        }
        public static Matrix4x4 StairFrame(Building.Stair s) => Matrix4x4.TRS(new Vector3(s.at.x, 0, s.at.y), Quaternion.Euler(0, s.turn, 0), Vector3.one);
        /// <summary>The ground it covers (in the building's units, a rectangle around it).</summary>
        public static Rect Footprint(Building.Stair s)
        {
            var m = StairFrame(s); var lo = new Vector2(float.MaxValue, float.MaxValue); var hi = -lo; foreach (var f in Flights(s)) foreach (var c in new[] { f.r.min, f.r.max, new Vector2(f.r.xMin, f.r.yMax), new Vector2(f.r.xMax, f.r.yMin) }) { var p = m.MultiplyPoint3x4(new Vector3(c.x, 0, c.y)); lo = Vector2.Min(lo, new Vector2(p.x, p.z)); hi = Vector2.Max(hi, new Vector2(p.x, p.z)); }
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }
        public static int StepCount(Building.Stair s, float H) => s.steps > 0 ? s.steps : Mathf.Max(3, Mathf.RoundToInt(Mathf.Clamp(s.rise, 0.05f, 4f) * H / 0.18f));
        static void StairMesh(Building b, Building.Floor fl, Building.Stair s, Pack p, Pack slope, float y0, float H, Func<int, int> M)
        {
            float R = Mathf.Clamp(s.rise, 0.05f, 4f) * H; int mat = M(s.mat >= 0 ? s.mat : 3); int total = StepCount(s, H); var frame = Matrix4x4.TRS(new Vector3(s.at.x, y0 + s.lift, s.at.y), Quaternion.Euler(0, s.turn, 0), Vector3.one);
            p.m = frame; if (slope != null) slope.m = frame;
            if (s.kind == Building.StairKind.Spiral) { Spiral(p, s, R, total, mat); p.m = Matrix4x4.identity; return; }
            if (s.kind == Building.StairKind.Ladder) { Ladder(p, s, R, mat); p.m = Matrix4x4.identity; return; }
            foreach (var f in Flights(s))
            {
                float ya = f.y0 * R, yb = f.y1 * R; if (f.up == Vector2.zero) { if (s.under != Building.StairUnder.Solid) { p.Box(new Vector3(f.r.xMin, ya - 0.15f, f.r.yMin), new Vector3(f.r.xMax, Mathf.Max(0.02f, ya), f.r.yMax), k => mat); foreach (var c in new[] { f.r.min, f.r.max, new Vector2(f.r.xMin, f.r.yMax), new Vector2(f.r.xMax, f.r.yMin) }) { var cc = Vector2.MoveTowards(c, f.r.center, 0.08f); p.Box(new Vector3(cc.x - 0.05f, 0, cc.y - 0.05f), new Vector3(cc.x + 0.05f, ya - 0.15f, cc.y + 0.05f), k => mat); } continue; } p.Box(new Vector3(f.r.xMin, 0, f.r.yMin), new Vector3(f.r.xMax, Mathf.Max(0.02f, ya), f.r.yMax), k => mat, k => k == 3); continue; }
                if (s.kind == Building.StairKind.Ramp) { Ramp(p, f.r, ya, yb, mat, f.up); continue; }
                int n = Mathf.Max(1, Mathf.RoundToInt(total * (f.y1 - f.y0))); bool alongX = f.up.x != 0;
                if (s.under != Building.StairUnder.Solid && b.SwapFor(Building.PieceKind.Step) == null) { int tmo = s.topMat >= 0 ? M(s.topMat) : mat; Swapped(Building.PieceKind.StepTop, p.m, Vector3.zero, Vector3.one, ref tmo); OpenFlight(p, f.r, f.up, ya, yb, n, mat, tmo); if (s.under == Building.StairUnder.Cupboard && s.kind == Building.StairKind.Straight) Cupboard(b, fl, s, p, f.r, f.up, ya, yb, mat, frame); continue; }
                for (int i = 0; i < n; i++)
                {
                    // solid steps, each from the floor up, so there are no gaps between them
                    float k0 = i / (float)n, k1 = (i + 1) / (float)n, top = ya + (yb - ya) * k1; Vector3 lo, hi;
                    if (!alongX) { float z0 = f.up.y > 0 ? f.r.yMin + f.r.height * k0 : f.r.yMax - f.r.height * k1, z1 = f.up.y > 0 ? f.r.yMin + f.r.height * k1 : f.r.yMax - f.r.height * k0; lo = new Vector3(f.r.xMin, 0, z0); hi = new Vector3(f.r.xMax, top, z1); }
                    else { float x0 = f.up.x > 0 ? f.r.xMin + f.r.width * k0 : f.r.xMax - f.r.width * k1, x1 = f.up.x > 0 ? f.r.xMin + f.r.width * k1 : f.r.xMax - f.r.width * k0; lo = new Vector3(x0, 0, f.r.yMin); hi = new Vector3(x1, top, f.r.yMax); }
                    int sm = mat, tm = s.topMat >= 0 ? M(s.topMat) : mat; Swapped(Building.PieceKind.StepTop, p.m, lo, hi, ref tm); int sm2 = sm; if (Swapped(Building.PieceKind.Step, p.m, new Vector3(lo.x, top - (yb - ya) / n, lo.z), hi, ref sm2, false)) { sm = sm2; continue; } sm = sm2; p.Box(lo, hi, k => k == 2 ? tm : sm, k => k == 3);
                }
            }
            StairExtras(b, fl, s, p, slope, R, mat); p.m = Matrix4x4.identity; if (slope != null) slope.m = Matrix4x4.identity;
        }
        /// <summary>A wedge over a rectangle, rising the way "up" points (in the stair's frame).</summary>
        static void Ramp(Pack p, Rect r, float ya, float yb, int mat, Vector2 up)
        {
            if (up == Vector2.up || up == Vector2.zero) { Ramp(p, r, ya, yb, mat); return; }
            // turned: build it rising along +z in a frame turned to face "up", then put it back
            var m0 = p.m; float ang = up == Vector2.down ? 180 : up == Vector2.right ? 90 : -90; var c = r.center; p.m = m0 * Matrix4x4.TRS(new Vector3(c.x, 0, c.y), Quaternion.Euler(0, ang, 0), Vector3.one); bool side = up.x != 0; float w = side ? r.height : r.width, l = side ? r.width : r.height; Ramp(p, new Rect(-w / 2, -l / 2, w, l), ya, yb, mat); p.m = m0;
        }
        static void Ramp(Pack p, Rect r, float ya, float yb, int mat)
        {
            Vector3 A = new Vector3(r.xMin, ya, r.yMin), B = new Vector3(r.xMax, ya, r.yMin), C = new Vector3(r.xMax, yb, r.yMax), D = new Vector3(r.xMin, yb, r.yMax); var nr = Vector3.Cross(D - A, B - A).normalized; if (nr.y < 0) nr = -nr; p.Quad(A, B, C, D, nr, mat);
            p.Quad(new Vector3(r.xMin, 0, r.yMax), new Vector3(r.xMax, 0, r.yMax), C, D, Vector3.forward, mat); p.Poly(new[] { new Vector3(r.xMin, 0, r.yMin), A, D, new Vector3(r.xMin, 0, r.yMax) }, Vector3.left, mat); p.Poly(new[] { new Vector3(r.xMax, 0, r.yMin), B, C, new Vector3(r.xMax, 0, r.yMax) }, Vector3.right, mat); if (ya > 0.01f) p.Quad(new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), B, A, Vector3.back, mat); p.Quad(new Vector3(r.xMin, 0, r.yMin), new Vector3(r.xMax, 0, r.yMin), new Vector3(r.xMax, 0, r.yMax), new Vector3(r.xMin, 0, r.yMax), Vector3.down, mat);
        }

        // ------------------------------------------------------------------ one floor

        static void MakeFloor(Building b, Building.Floor fl, ref Material[] mats)
        {
            float H = b.HeightOf(fl.level), T = b.slabThickness, y0 = b.Y(fl.level); float split = b.psx ? Mathf.Max(0.5f, b.cell) : 0;
            var solid = new Pack(b.textureSize) { split = split }; var decor = new Pack(b.textureSize) { split = split }; var stairs = b.smoothStairs ? new Pack(b.textureSize) { split = split } : solid; var slope = b.smoothStairs ? new Pack(b.textureSize) : null; var x = new Extras { glass = new Pack(b.textureSize) }; var outer = new Pack(b.textureSize) { split = split }; var rails = new Pack(b.textureSize); var roof = new Pack(b.textureSize) { split = split }; var outGlass = new Pack(b.textureSize); var outLeaves = new List<Leaf>();
            var mm0 = mats; x.M = k => Mathf.Clamp(k, 0, mm0.Length - 1); s_B = b; s_Mats = mats; s_Placed = new List<Placed>(); s_GlassPack = x.glass; s_RailPack = rails; s_Leaves = x.leaves; s_Level = fl.level;
            FloorMesh(b, fl, solid, y0, H, T);
            for (int wi = 0; wi < fl.walls.Count; wi++) { var w = fl.walls[wi]; s_Wall = wi; if (Outside(b, fl, w)) { var keep = (x.glass, x.leaves); x.glass = outGlass; x.leaves = outLeaves; s_GlassPack = outGlass; WallMesh(b, fl, w, outer, y0, H, x); (x.glass, x.leaves) = keep; s_GlassPack = x.glass; } else WallMesh(b, fl, w, solid, y0, H, x); } s_Wall = -1; foreach (var s in fl.stairs) StairMesh(b, fl, s, s.kind == Building.StairKind.Spiral || s.kind == Building.StairKind.Ladder ? solid : stairs, s.kind == Building.StairKind.Spiral || s.kind == Building.StairKind.Ladder ? null : slope, y0, H, x.M);
            Columns(b, fl, solid, y0, H, x.M); BalconyPosts(b, fl, solid, y0, x.M); EdgeRails(b, fl, decor, Cover(b, fl), y0, x.M); var roofGlass = new Pack(b.textureSize); s_GlassPack = roofGlass; Roof(b, fl, roof, y0, H, x.M); s_GlassPack = x.glass; s_RailPack = null; s_Leaves = null;
            var models = new List<(Building.Piece p, BuildPart part, int index)>();
            for (int i = 0; i < fl.pieces.Count; i++)
            {
                var p = fl.pieces[i]; var part = Part(p.part); if (!part) continue; if (part.shape == BuildPart.Shape.MyModel) { models.Add((p, part, i)); continue; }
                var pk = part.collide == BuildPart.Collide.None ? decor : solid; int mat = p.mat >= 0 ? p.mat : part.material ? Extra(b, part.material, ref mats) : 3; pk.m = Matrix4x4.TRS(new Vector3(p.at.x, y0 + p.lift, p.at.y), Quaternion.Euler(0, p.turn, 0), p.scale); Shape(pk, part, mat, b.roundSides); pk.m = Matrix4x4.identity;
            }
            var go = new GameObject(fl.name.Length > 0 ? fl.name : Building.LevelName(fl.level)); go.transform.SetParent(b.transform, false); go.layer = b.gameObject.layer; go.isStatic = b.gameObject.isStatic; go.AddComponent<BuildingMade>().level = fl.level;
            mats = s_Mats; var placed = s_Placed; s_Placed = null; var mm = mats; Material Mat(int k) => mm[Mathf.Clamp(k, 0, mm.Length - 1)];
            GameObject Part2(string name, Pack pk, bool render, bool collide) { if (pk == null || pk.Empty) return null; var o = new GameObject(name); o.transform.SetParent(go.transform, false); o.layer = go.layer; o.isStatic = go.isStatic; var mesh = pk.Make(name, out var ids); o.AddComponent<MeshFilter>().sharedMesh = mesh; if (render) o.AddComponent<MeshRenderer>().sharedMaterials = ids.Select(Mat).ToArray(); if (collide) o.AddComponent<MeshCollider>().sharedMesh = mesh; return o; }
            if (!solid.Empty) { var mesh = solid.Make(go.name, out var ids); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = ids.Select(Mat).ToArray(); go.AddComponent<MeshCollider>().sharedMesh = mesh; }
            Part2("Decoration (no collider)", decor, true, false); if (b.smoothStairs) { Part2("Stairs", stairs, true, false); Part2("Stair slopes (collider only)", slope, false, true); }
            Part2("Outer walls", outer, true, true); Part2("Railings", rails, true, true); Part2("Roof", roof, true, true);
            foreach (var (gname, gp) in new[] { ("Glass", x.glass), ("Glass (outside)", outGlass), ("Roof glass", roofGlass) }) { var gl = Part2(gname, gp, true, true); if (gl) { var gs = b.SwapFor(Building.PieceKind.Glass); gl.GetComponent<MeshRenderer>().sharedMaterial = gs != null && gs.material ? gs.material : Glass(); } }
            if (placed.Count > 0) { var holder = new GameObject("Your models"); holder.transform.SetParent(go.transform, false); holder.layer = go.layer; PlaceModels(holder, placed); }
            if (x.leaves.Count > 0) MakeLeaves(b, go, x.leaves, Mat(3), Mat, fl.level, false); if (outLeaves.Count > 0) MakeLeaves(b, go, outLeaves, Mat(3), Mat, fl.level, true);
            foreach (var (p, part, index) in models)
            {
                if (!part.model) continue; var o = PrefabUtility.InstantiatePrefab(part.model) as GameObject ?? UnityEngine.Object.Instantiate(part.model); o.transform.SetParent(go.transform, false); o.transform.localPosition = new Vector3(p.at.x, y0 + p.lift, p.at.y); o.transform.localRotation = Quaternion.Euler(0, p.turn, 0); o.transform.localScale = Vector3.Scale(o.transform.localScale, p.scale); var mk = o.AddComponent<BuildingMade>(); mk.piece = index; mk.level = fl.level;
                var pm = p.mat >= 0 ? Mat(p.mat) : part.material ? (b.psx ? PSX(part.material, b) : part.material) : null; if (pm) foreach (var r in o.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => pm).ToArray(); else if (b.psx) foreach (var r in o.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(q => PSX(q, b)).ToArray();
                if (part.collide == BuildPart.Collide.Box && !o.GetComponentInChildren<Collider>()) { var bc = o.AddComponent<BoxCollider>(); var bb = Bounds(o); bc.center = o.transform.InverseTransformPoint(bb.center); var sz = o.transform.InverseTransformVector(bb.size); bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z)); }
                else if (part.collide == BuildPart.Collide.Exact) foreach (var f in o.GetComponentsInChildren<MeshFilter>()) if (!f.GetComponent<Collider>()) f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }
        }
        static int Extra(Building b, Material m, ref Material[] mats) { int i = b.materials.IndexOf(m); if (i < 0) { b.materials.Add(m); i = b.materials.Count - 1; mats = Materials(b); } return i; }

        // ------------------------------------------------------------------ shapes for your own parts (low-poly, faceted)

        public static void Shape(Pack p, BuildPart part, int mat, int sides)
        {
            var s = part.size; float hx = s.x / 2, hz = s.z / 2; int n = Mathf.Clamp(part.sides > 0 ? part.sides : sides, 3, 32);
            switch (part.shape)
            {
                case BuildPart.Shape.Box: p.Box(new Vector3(-hx, 0, -hz), new Vector3(hx, s.y, hz), f => mat); break;
                case BuildPart.Shape.Beam: p.Box(new Vector3(-hx, s.y - Mathf.Min(s.y, 0.3f), -hz), new Vector3(hx, s.y, hz), f => mat); break;
                case BuildPart.Shape.Pillar: { var ring = Enumerable.Range(0, n).Select(i => { float a = Mathf.PI * 2 * (i + 0.5f) / n; return new Vector3(Mathf.Cos(a) * hx, 0, Mathf.Sin(a) * hz); }).ToArray(); for (int i = 0; i < n; i++) { var A = ring[i]; var B = ring[(i + 1) % n]; var nr = new Vector3((A.x + B.x) / 2, 0, (A.z + B.z) / 2).normalized; p.Quad(A, B, B + Vector3.up * s.y, A + Vector3.up * s.y, nr, mat); } p.Poly(ring.Select(r => r + Vector3.up * s.y).ToList(), Vector3.up, mat); p.Poly(ring.ToList(), Vector3.down, mat); break; }
                case BuildPart.Shape.Wedge: Ramp(p, new Rect(-hx, -hz, s.x, s.z), 0, s.y, mat); break;
                case BuildPart.Shape.Stairs: { int k = Mathf.Max(2, part.steps); for (int i = 0; i < k; i++) p.Box(new Vector3(-hx, 0, -hz + s.z * i / k), new Vector3(hx, s.y * (i + 1) / k, -hz + s.z * (i + 1) / k), f => mat, f => f == 3); break; }
                case BuildPart.Shape.Arch:
                {
                    float t = Mathf.Min(0.3f, hx * 0.3f), r = hx - t, spring = Mathf.Max(0, s.y - r - t); p.Box(new Vector3(-hx, 0, -hz), new Vector3(-r, s.y, hz), f => mat); p.Box(new Vector3(r, 0, -hz), new Vector3(hx, s.y, hz), f => mat); p.Box(new Vector3(-r, spring + r, -hz), new Vector3(r, s.y, hz), f => mat, f => f == 3);
                    var pts = Enumerable.Range(0, n + 1).Select(i => { float an = Mathf.PI * (1 - i / (float)n); return new Vector2(Mathf.Cos(an) * r, spring + Mathf.Sin(an) * r); }).ToArray();
                    for (int i = 0; i < n; i++) p.Prism(new[] { pts[i], pts[i + 1], new Vector2(pts[i + 1].x, spring + r), new Vector2(pts[i].x, spring + r) }, -hz, hz, mat, mat, mat, k => k == 0); break;
                }
                case BuildPart.Shape.Fence: { float t = 0.06f; int posts = Mathf.Max(2, Mathf.RoundToInt(s.x / 0.5f) + 1); for (int i = 0; i < posts; i++) { float x = -hx + s.x * i / (posts - 1); p.Box(new Vector3(x - t, 0, -t), new Vector3(x + t, s.y, t), f => mat); } p.Box(new Vector3(-hx, s.y * 0.85f, -t * 0.7f), new Vector3(hx, s.y, t * 0.7f), f => mat); p.Box(new Vector3(-hx, s.y * 0.4f, -t * 0.7f), new Vector3(hx, s.y * 0.5f, t * 0.7f), f => mat); break; }
            }
        }
    }
}
