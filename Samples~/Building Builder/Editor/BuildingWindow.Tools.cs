using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The tools, the same in the window's view and in the Scene view: what's under the mouse, what a click or a
    /// drag would make (drawn over the picture), and picking things to change them.</summary>
    public partial class BuildingWindow
    {
        public enum SelKind { None, Wall, Opening, Stair, Piece, Area, Column, Beam }
        public struct Sel { public SelKind kind; public int level, i, j; public static bool operator ==(Sel a, Sel b) => a.kind == b.kind && a.level == b.level && a.i == b.i && a.j == b.j; public static bool operator !=(Sel a, Sel b) => !(a == b); public override bool Equals(object o) => o is Sel s && s == this; public override int GetHashCode() => (int)kind ^ i * 31 ^ j * 977 ^ level * 7919; }
        enum Grab { None, Rect, Wall, Path, Move, Handle, Gizmo, Stair }
        [NonSerialized] Sel m_Sel; Sel m_Hover; Grab m_Drag; Vector2 m_From, m_To, m_Last; int m_Handle, m_UndoGroup; bool m_Erase; readonly List<Vector2Int> m_Path = new List<Vector2Int>();
        static readonly Color Add = new Color(1f, 0.62f, 0.2f), Take = new Color(1f, 0.3f, 0.25f), Pick = new Color(0.4f, 0.8f, 1f);

        bool Valid(Sel s)
        {
            if (!m_B || s.kind == SelKind.None) return false; var f = m_B.FloorAt(s.level); if (f == null) return false;
            switch (s.kind) { case SelKind.Wall: return s.i < f.walls.Count; case SelKind.Opening: return s.i < f.walls.Count && s.j < f.walls[s.i].openings.Count; case SelKind.Stair: return s.i < f.stairs.Count; case SelKind.Piece: return s.i < f.pieces.Count; case SelKind.Area: return s.i < f.areas.Count; case SelKind.Column: return s.i < f.columns.Count; case SelKind.Beam: return s.i < f.beams.Count; }
            return false;
        }

        // ------------------------------------------------------------------ drawing over the picture (in the building's own units)

        View m_V; Transform Tr => m_B.transform;
        Vector3 L(Vector2 u, float y) => new Vector3(u.x, y, u.y);
        void Line(Vector3 a, Vector3 b, Color c, float w = 2f) { if (!m_V.Gui(Tr.TransformPoint(a), out var ga) || !m_V.Gui(Tr.TransformPoint(b), out var gb)) return; if (!ClipSeg(ref ga, ref gb)) return; Handles.color = c; Handles.DrawAAPolyLine(w, ga, gb); }
        void Loop(IList<Vector3> pts, Color c, float w = 2f, float fill = 0)
        {
            var g = new List<Vector3>(); foreach (var p in pts) { if (!m_V.Gui(Tr.TransformPoint(p), out var q)) return; g.Add(q); }
            if (fill > 0) { var cp = ClipPoly(g); if (cp.Count >= 3) { Handles.color = new Color(c.r, c.g, c.b, fill); if (IsConvex(cp)) Handles.DrawAAConvexPolygon(cp.ToArray()); else { var tris = BuildingMaker.Triangulate(cp.Select(v => new Vector2(v.x, v.y)).ToList()); for (int k = 0; k + 2 < tris.Count; k += 3) Handles.DrawAAConvexPolygon(cp[tris[k]], cp[tris[k + 1]], cp[tris[k + 2]]); } } }
            Handles.color = c; for (int k = 0; k < g.Count; k++) { Vector2 a = g[k], b = g[(k + 1) % g.Count]; if (ClipSeg(ref a, ref b)) Handles.DrawAAPolyLine(w, a, b); }
        }
        static bool IsConvex(List<Vector3> g) { int sign = 0; for (int k = 0; k < g.Count; k++) { var a = g[k]; var b = g[(k + 1) % g.Count]; var c = g[(k + 2) % g.Count]; float z = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x); if (Mathf.Abs(z) < 1e-4f) continue; int s = z > 0 ? 1 : -1; if (sign == 0) sign = s; else if (s != sign) return false; } return true; }
        void RectAt(Vector2 a, Vector2 b, float y, Color c, float fill = 0.2f) { var lo = Vector2.Min(a, b); var hi = Vector2.Max(a, b); Loop(new[] { L(lo, y), L(new Vector2(hi.x, lo.y), y), L(hi, y), L(new Vector2(lo.x, hi.y), y) }, c, 2.5f, fill); }
        void Dot(Vector3 p, Color c, float r = 6) { if (!m_V.Gui(Tr.TransformPoint(p), out var g) || !InView(g)) return; Handles.color = Color.black; Handles.DrawSolidDisc(g, Vector3.forward, r + 1.5f); Handles.color = c; Handles.DrawSolidDisc(g, Vector3.forward, r); }
        void Text(Vector3 p, string s) { if (!m_V.Gui(Tr.TransformPoint(p), out var g) || !InView(g)) return; var st = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter }; var sz = st.CalcSize(new GUIContent(s)); var r = new Rect(g.x - sz.x / 2 - 4, g.y - 26, sz.x + 8, 18); EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.6f)); GUI.Label(r, s, st); }
        bool Near(Vector3 p, Vector2 mouse, float px = 10) => m_V.Gui(Tr.TransformPoint(p), out var g) && (g - mouse).magnitude < px;

        // ------------------------------------------------------------------ where the mouse is

        Ray LocalRay(Vector2 mouse) { var r = m_V.Ray(mouse); return new Ray(Tr.InverseTransformPoint(r.origin), Tr.InverseTransformDirection(r.direction)); }
        bool OnFloor(Vector2 mouse, float y, out Vector2 u) { var r = LocalRay(mouse); u = default; if (Mathf.Abs(r.direction.y) < 1e-5f) return false; float t = (y - r.origin.y) / r.direction.y; if (t <= 0) return false; var p = r.origin + r.direction * t; u = new Vector2(p.x, p.z); return true; }
        bool m_Magnet;
        /// <summary>To the grid, or to a wall end or room corner close by (on this floor or the one below), or in line with one.</summary>
        Vector2 Snap(Vector2 u)
        {
            float s = SnapStep; var g = new Vector2(Mathf.Round(u.x / s) * s, Mathf.Round(u.y / s) * s); m_Magnet = false; if (!m_B) return g;
            float r = Mathf.Clamp(s * 0.3f, 0.08f, 0.3f), best = r; var pts = new List<Vector2>();
            foreach (var f in new[] { m_B.FloorAt(m_Level), m_B.FloorAt(m_Level - 1) }) if (f != null) { foreach (var w in f.walls) { pts.Add(w.a); pts.Add(w.b); } foreach (var a in f.areas) { pts.Add(a.min); pts.Add(a.max); pts.Add(new Vector2(a.min.x, a.max.y)); pts.Add(new Vector2(a.max.x, a.min.y)); } }
            foreach (var p in pts) { float d = (p - u).magnitude; if (d < best && (p - g).sqrMagnitude > 1e-6f) { best = d; g = p; m_Magnet = true; } }
            if (m_Magnet) return g;
            foreach (var p in pts) { if (Mathf.Abs(p.x - u.x) < r * 0.6f && Mathf.Abs(p.x - g.x) > 1e-4f) g.x = p.x; if (Mathf.Abs(p.y - u.y) < r * 0.6f && Mathf.Abs(p.y - g.y) > 1e-4f) g.y = p.y; }
            return g;
        }
        static string M(float v) => v.ToString("0.##");

        /// <summary>The nearest thing on this floor under the mouse; along = how far along a wall, for doors and windows.</summary>
        Sel PickAt(Vector2 mouse, out float along, bool wallsOnly = false)
        {
            along = 0; var best = default(Sel); float bd = float.MaxValue; var fl = m_B.FloorAt(m_Level); if (fl == null) return best; var r = LocalRay(mouse); float y0 = m_B.Y(m_Level), H = m_B.HeightOf(m_Level);
            for (int i = 0; i < fl.walls.Count; i++)
            {
                var w = fl.walls[i]; float len = w.Length; if (len < 1e-3f) continue; var d = (w.b - w.a) / len; var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up); var A = L(w.a, y0); float t = BuildingMaker.Thick(m_B, w);
                foreach (float side in new[] { t / 2, -t / 2 })
                {
                    float den = Vector3.Dot(r.direction, fw); if (Mathf.Abs(den) < 1e-6f) continue; float k = Vector3.Dot(A + fw * side - r.origin, fw) / den; if (k <= 0 || k >= bd) continue; var p = r.origin + r.direction * k; float x = Vector3.Dot(p - A, rt), yy = p.y - y0; float top = BuildingMaker.Top(m_B, w, H), bot = w.bottom;
                    if (x < -t || x > len + t || yy < bot - 0.05f || yy > top + 0.05f) continue; bd = k; along = x; best = new Sel { kind = SelKind.Wall, level = m_Level, i = i };
                    for (int j = 0; j < w.openings.Count; j++) { var o = w.openings[j]; float ob = o.kind == Building.OpeningKind.Door || o.kind == Building.OpeningKind.Gap ? 0 : o.bottom, ot = o.kind == Building.OpeningKind.Gap ? top : o.top; if (Mathf.Abs(x - o.at) <= o.width / 2 && yy >= ob - 0.05f && yy <= ot + 0.05f) best = new Sel { kind = SelKind.Opening, level = m_Level, i = i, j = j }; }
                }
            }
            if (wallsOnly) return best;
            for (int i = 0; i < fl.stairs.Count; i++) { var s = fl.stairs[i]; var fp = BuildingMaker.Footprint(s); var bb = new Bounds(new Vector3(fp.center.x, y0 + s.rise * H / 2, fp.center.y), new Vector3(fp.width, Mathf.Max(0.1f, s.rise * H), fp.height)); if (bb.IntersectRay(r, out float k) && k < bd) { bd = k; best = new Sel { kind = SelKind.Stair, level = m_Level, i = i }; } }
            for (int i = 0; i < fl.pieces.Count; i++) { var p = fl.pieces[i]; var part = BuildingMaker.Part(p.part); var sz = part ? Vector3.Scale(part.size, p.scale) : Vector3.one; var bb = new Bounds(new Vector3(p.at.x, y0 + p.lift + sz.y / 2, p.at.y), new Vector3(Mathf.Max(sz.x, sz.z), sz.y, Mathf.Max(sz.x, sz.z))); if (bb.IntersectRay(r, out float k) && k < bd) { bd = k; best = new Sel { kind = SelKind.Piece, level = m_Level, i = i }; } }
            float top0 = y0 + H - m_B.slabThickness;
            for (int i = 0; i < fl.columns.Count; i++) { var c = fl.columns[i]; var bb = new Bounds(new Vector3(c.at.x, (y0 + c.lift + top0) / 2, c.at.y), new Vector3(c.size, top0 - y0 - c.lift, c.size)); if (bb.IntersectRay(r, out float k) && k < bd) { bd = k; best = new Sel { kind = SelKind.Column, level = m_Level, i = i }; } }
            for (int i = 0; i < fl.beams.Count; i++) { var bm = fl.beams[i]; var mid = (bm.a + bm.b) / 2; var bb = new Bounds(new Vector3(mid.x, top0 - bm.depth / 2, mid.y), new Vector3(Mathf.Abs(bm.b.x - bm.a.x) + bm.width, bm.depth, Mathf.Abs(bm.b.y - bm.a.y) + bm.width)); if (bb.IntersectRay(r, out float k) && k < bd) { bd = k; best = new Sel { kind = SelKind.Beam, level = m_Level, i = i }; } }
            if (best.kind == SelKind.None && OnFloor(mouse, y0, out var u)) for (int i = fl.areas.Count - 1; i >= 0; i--) { var a = fl.areas[i]; var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); if (u.x >= lo.x && u.x <= hi.x && u.y >= lo.y && u.y <= hi.y) { best = new Sel { kind = SelKind.Area, level = m_Level, i = i }; break; } }
            return best;
        }

        // ------------------------------------------------------------------ outlines of things

        void Outline(Sel s, Color c, float w = 3f)
        {
            if (!Valid(s)) return; var fl = m_B.FloorAt(s.level); float y0 = m_B.Y(s.level), H = m_B.HeightOf(s.level);
            switch (s.kind)
            {
                case SelKind.Wall: case SelKind.Opening:
                {
                    var wl = fl.walls[s.i]; float len = wl.Length; var d = (wl.b - wl.a) / Mathf.Max(1e-5f, len); var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up) * BuildingMaker.Thick(m_B, wl) / 2 * 1.05f; var A = L(wl.a, y0); float top = BuildingMaker.Top(m_B, wl, H), bot = wl.bottom;
                    if (s.kind == SelKind.Wall) { foreach (var f in new[] { fw, -fw }) Loop(new[] { A + f + Vector3.up * bot, A + f + rt * len + Vector3.up * bot, A + f + rt * len + Vector3.up * top, A + f + Vector3.up * top }, c, w); }
                    else { var o = wl.openings[s.j]; float ob = o.kind == Building.OpeningKind.Door || o.kind == Building.OpeningKind.Gap ? 0 : o.bottom, ot = o.kind == Building.OpeningKind.Gap ? top : o.top; foreach (var f in new[] { fw, -fw }) Loop(new[] { A + f + rt * (o.at - o.width / 2) + Vector3.up * ob, A + f + rt * (o.at + o.width / 2) + Vector3.up * ob, A + f + rt * (o.at + o.width / 2) + Vector3.up * ot, A + f + rt * (o.at - o.width / 2) + Vector3.up * ot }, c, w, 0.12f); }
                    break;
                }
                case SelKind.Stair: { var s2 = fl.stairs[s.i]; var m = BuildingMaker.StairFrame(s2); float R = s2.rise * H; foreach (var f in BuildingMaker.Flights(s2)) { var pts = new[] { f.r.min, new Vector2(f.r.xMax, f.r.yMin), f.r.max, new Vector2(f.r.xMin, f.r.yMax) }.Select(q => m.MultiplyPoint3x4(new Vector3(q.x, 0, q.y))).Select(q => new Vector3(q.x, y0 + 0.02f + f.y1 * R, q.z)).ToArray(); Loop(pts, c, w, 0.15f); } break; }
                case SelKind.Piece: { var p = fl.pieces[s.i]; var part = BuildingMaker.Part(p.part); var sz = part ? Vector3.Scale(part.size, p.scale) : Vector3.one; var m = Matrix4x4.TRS(new Vector3(p.at.x, y0 + p.lift, p.at.y), Quaternion.Euler(0, p.turn, 0), Vector3.one); var c4 = new[] { new Vector3(-sz.x / 2, 0, -sz.z / 2), new Vector3(sz.x / 2, 0, -sz.z / 2), new Vector3(sz.x / 2, 0, sz.z / 2), new Vector3(-sz.x / 2, 0, sz.z / 2) }; Loop(c4.Select(q => m.MultiplyPoint3x4(q)).ToArray(), c, w, 0.12f); Loop(c4.Select(q => m.MultiplyPoint3x4(q + Vector3.up * sz.y)).ToArray(), c, w); break; }
                case SelKind.Area: { var a = fl.areas[s.i]; RectAt(a.min, a.max, y0 + 0.03f + a.lift, a.hole ? Take : c, 0.12f); break; }
                case SelKind.Column: { var cl = fl.columns[s.i]; float h2 = cl.size / 2, top = y0 + H - m_B.slabThickness; foreach (var y in new[] { y0 + cl.lift, top }) Loop(new[] { new Vector3(cl.at.x - h2, y, cl.at.y - h2), new Vector3(cl.at.x + h2, y, cl.at.y - h2), new Vector3(cl.at.x + h2, y, cl.at.y + h2), new Vector3(cl.at.x - h2, y, cl.at.y + h2) }, c, w, 0.12f); break; }
                case SelKind.Beam: { var bm = fl.beams[s.i]; float top = y0 + H - m_B.slabThickness; Line(L(bm.a, top - bm.depth), L(bm.b, top - bm.depth), c, w + 2); Line(L(bm.a, top), L(bm.b, top), c, w); break; }
            }
        }
        /// <summary>The round handles of what's picked: wall ends, rectangle corners, the sides of a door or window.</summary>
        List<(Vector3 p, int id)> Handles_(Sel s)
        {
            var l = new List<(Vector3, int)>(); if (!Valid(s)) return l; var fl = m_B.FloorAt(s.level); float y0 = m_B.Y(s.level);
            if (s.kind == SelKind.Wall) { var w = fl.walls[s.i]; l.Add((L(w.a, y0 + 0.05f), 0)); l.Add((L(w.b, y0 + 0.05f), 1)); var d = (w.b - w.a).normalized; var nf = new Vector2(d.y, -d.x); l.Add((L((w.a + w.b) / 2 + nf * w.bend, y0 + 0.05f), 5)); }
            if (s.kind == SelKind.Area) { var a = fl.areas[s.i]; var cs = a.Corners(); float y = y0 + a.lift + 0.05f; for (int i = 0; i < cs.Count; i++) l.Add((L(cs[i], y), i)); for (int i = 0; i < cs.Count; i++) l.Add((L((cs[i] + cs[(i + 1) % cs.Count]) / 2, y), 100 + i)); }
            if (s.kind == SelKind.Opening) { var w = fl.walls[s.i]; var o = w.openings[s.j]; var d = (w.b - w.a).normalized; bool low = o.kind == Building.OpeningKind.Door || o.kind == Building.OpeningKind.Gap; float ob = low ? 0 : o.bottom, mid = (ob + o.top) / 2; l.Add((L(w.a + d * (o.at - o.width / 2), y0 + mid), 0)); l.Add((L(w.a + d * (o.at + o.width / 2), y0 + mid), 1)); if (o.kind != Building.OpeningKind.Gap) l.Add((L(w.a + d * o.at, y0 + o.top), 2)); if (!low) l.Add((L(w.a + d * o.at, y0 + ob), 3)); }
            return l;
        }

        // ------------------------------------------------------------------ the tools

        void Work(View v, Event e)
        {
            m_V = v; if (!m_B) return; int id = GUIUtility.GetControlID("BuildingWork".GetHashCode(), FocusType.Keyboard); bool over = v.scene || v.rect.Contains(e.mousePosition); var fl = m_B.FloorAt(m_Level); float y0 = m_B.Y(m_Level), H = m_B.HeightOf(m_Level);
            bool erase = e.control || e.command; bool have = OnFloor(e.mousePosition, y0, out var u); var su = Snap(u);
            // while you use the panels on the sides, the view stays as it was (nothing follows the mouse there)
            bool busy = m_Drag != Grab.None || GUIUtility.hotControl != 0 || m_OpWall >= 0;
            if (!over && !busy) { if (e.type == EventType.Repaint) { Grid(m_LastSu, y0, false); Below(y0); if (Valid(m_Sel)) { Outline(m_Sel, Pick, 3.5f); foreach (var h in Handles_(m_Sel)) Dot(h.p, Color.white); } } return; }
            m_LastSu = su;
            if (e.type == EventType.Repaint) { Grid(su, y0); Below(y0); m_GhostWanted = false; if (m_HoverRoom >= 0 && fl != null && m_HoverRoom < fl.areas.Count) AreaLoop(fl.areas[m_HoverRoom], y0 + fl.areas[m_HoverRoom].lift + 0.04f, Pick, 0.3f); }
            if (e.type == EventType.MouseDown && e.button == 0 && over && !e.alt) GUIUtility.keyboardControl = id;
            switch (m_Tool)
            {
                case Tool.Select: SelectTool(e, id, over, su, y0); break;
                case Tool.Room: case Tool.Floor: case Tool.Hole: if (m_RoomShape == 2 && m_Tool != Tool.Hole) PolyTool(e, over, have, su, y0); else RectTool(e, id, over, have, su, y0, erase || m_Tool == Tool.Hole); break;
                case Tool.Wall: WallTool(e, id, over, have, u, y0, H, erase); break;
                case Tool.Door: case Tool.Window: OpeningTool(e, id, over, y0, H, erase); break;
                case Tool.Stairs: StairTool(e, id, over, have, su, u, y0, H, erase); break;
                case Tool.Tunnel: TunnelTool(e, id, over, have, u, y0, erase); break;
                case Tool.Props: PropTool(e, over, have, u, y0, erase); break;
                case Tool.Paint: PaintTool(e, over); break;
                case Tool.Column: ColumnTool(e, over, have, su, u, y0); break;
                case Tool.Beam: BeamTool(e, id, over, have, su, y0, H); break;
                case Tool.Shaft: case Tool.Balcony: RectTool(e, id, over, have, su, y0, erase); break;
                case Tool.Stamp: StampTool(e, over, have, su, y0); break;
            }
            if (m_Measure && e.type == EventType.Repaint) Measure(y0, H);
            if (e.type == EventType.Repaint) { if (m_Tool == Tool.Select && Valid(m_Sel)) TuneHint(); else ToolHint(); }
            if (Valid(m_Sel) && e.type == EventType.Repaint) { Outline(m_Sel, Pick, 3.5f); if (m_Tool == Tool.Select) foreach (var h in Handles_(m_Sel)) { bool hot = Near(h.p, e.mousePosition) || m_Drag == Grab.Handle && m_Handle == h.id; Dot(h.p, hot ? Accent : h.id >= 100 ? new Color(1, 1, 1, 0.6f) : Color.white, hot ? 8 : h.id >= 100 ? 4 : 6); } }
            if (e.type == EventType.Repaint && !m_GhostWanted && m_GhostKey != null) ClearGhost();
        }

        Vector2 m_LastSu;
        void Grid(Vector2 at, float y0, bool dot = true)
        {
            float c = m_B.cell, s = SnapStep; var fl = m_B.FloorAt(m_Level); var lo = at - Vector2.one * 8 * c; var hi = at + Vector2.one * 8 * c;
            if (fl != null) foreach (var a in fl.areas) { lo = Vector2.Min(lo, Vector2.Min(a.min, a.max) - Vector2.one * c); hi = Vector2.Max(hi, Vector2.Max(a.min, a.max) + Vector2.one * c); }
            lo = new Vector2(Mathf.Floor(lo.x / c) * c, Mathf.Floor(lo.y / c) * c); hi = new Vector2(Mathf.Ceil(hi.x / c) * c, Mathf.Ceil(hi.y / c) * c); float y = y0 + 0.01f;
            for (float x = lo.x; x <= hi.x + 1e-3f; x += c) Line(new Vector3(x, y, lo.y), new Vector3(x, y, hi.y), new Color(1, 1, 1, 0.13f), 1f);
            for (float z = lo.y; z <= hi.y + 1e-3f; z += c) Line(new Vector3(lo.x, y, z), new Vector3(hi.x, y, z), new Color(1, 1, 1, 0.13f), 1f);
            if (s < c - 1e-4f) { var a = at - Vector2.one * 2 * c; for (float x = Mathf.Floor(a.x / s) * s; x <= at.x + 2 * c; x += s) Line(new Vector3(x, y, at.y - 2 * c), new Vector3(x, y, at.y + 2 * c), new Color(1, 1, 1, 0.06f), 1f); for (float z = Mathf.Floor(a.y / s) * s; z <= at.y + 2 * c; z += s) Line(new Vector3(at.x - 2 * c, y, z), new Vector3(at.x + 2 * c, y, z), new Color(1, 1, 1, 0.06f), 1f); }
            if (dot) Dot(new Vector3(at.x, y, at.y), new Color(1, 1, 1, 0.8f), 3);
            if (dot && m_Magnet) { var g = new Vector3(at.x, y, at.y); Loop(Enumerable.Range(0, 12).Select(i => g + Quaternion.Euler(0, i * 30, 0) * Vector3.forward * 0.18f).ToArray(), Pick, 2f); }
        }
        /// <summary>The walls and floors of the floor below, faintly, so what you draw lines up with them.</summary>
        void Below(float y0)
        {
            var fb = m_B.FloorAt(m_Level - 1); if (fb == null) return; var c = new Color(0.4f, 0.8f, 1f, 0.45f);
            foreach (var w in fb.walls) Line(L(w.a, y0 + 0.02f), L(w.b, y0 + 0.02f), c, 2f);
            foreach (var a in fb.areas.Where(q => !q.hole)) RectAt(a.min, a.max, y0 + 0.015f, new Color(c.r, c.g, c.b, 0.18f), 0);
        }

        void Begin(string what) { Undo.IncrementCurrentGroup(); m_UndoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Building: " + what); }
        void Step(Action a) { Undo.RecordObject(m_B, Undo.GetCurrentGroupName()); a(); EditorUtility.SetDirty(m_B); Remake(); }
        void End() { Undo.CollapseUndoOperations(m_UndoGroup); }

        // ---- select, move, stretch

        void SelectTool(Event e, int id, bool over, Vector2 su, float y0)
        {
            if (e.type != EventType.MouseDown && Gizmo(e, id)) return;
            if (e.type == EventType.MouseMove || e.type == EventType.Repaint) { m_Hover = over && m_Drag == Grab.None ? PickAt(e.mousePosition, out _) : default; if (e.type == EventType.Repaint && m_Hover != m_Sel) Outline(m_Hover, new Color(1, 1, 1, 0.5f), 2f); m_Status = Valid(m_Hover) ? Describe(m_Hover) : ""; }
            if (e.type == EventType.MouseDown && e.button == 0 && over && !e.alt)
            {
                foreach (var h in Handles_(m_Sel)) if (Near(h.p, e.mousePosition))
                {
                    if ((e.control || e.command) && m_Sel.kind == SelKind.Area && h.id < 100) { var fa = m_B.FloorAt(m_Sel.level); var ar = fa.areas[m_Sel.i]; int hid = h.id; if (ar.Corners().Count > 3) Change("Take a corner away", () => { if (!ar.IsShape) ar.SetShape(ar.Corners()); RemoveCorner(fa, ar, hid); }); e.Use(); return; }
                    m_Drag = Grab.Handle; m_Handle = h.id; Begin("Stretch"); GUIUtility.hotControl = id; e.Use(); return;
                }
                if (Gizmo(e, id)) return;
                if (m_AnyFloor && Hit(m_V.Ray(e.mousePosition), out var anyHit)) { var made = anyHit.collider.GetComponentInParent<BuildingMade>(); if (made && made.level != m_Level && m_B.FloorAt(made.level) != null) { m_Level = made.level; y0 = m_B.Y(m_Level); if (OnFloor(e.mousePosition, y0, out var u2)) su = Snap(u2); Floors(); } }
                var s = PickAt(e.mousePosition, out float along); m_Sel = s; if (s.kind != SelKind.None) { m_Drag = Grab.Move; m_Last = s.kind == SelKind.Opening ? new Vector2(along, 0) : su; Begin("Move"); GUIUtility.hotControl = id; m_Tab = 0; } Repaint(); e.Use();
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && Valid(m_Sel))
            {
                var fl = m_B.FloorAt(m_Sel.level);
                if (m_Drag == Grab.Handle) Step(() =>
                {
                    if (m_Sel.kind == SelKind.Wall) { var w = fl.walls[m_Sel.i]; if (m_Handle == 0) w.a = su; else if (m_Handle == 1) w.b = su; else if (OnFloor(e.mousePosition, m_B.Y(m_Sel.level), out var uw)) { var d = (w.b - w.a).normalized; var nf = new Vector2(d.y, -d.x); w.bend = Mathf.Round(Vector2.Dot(uw - (w.a + w.b) / 2, nf) / 0.05f) * 0.05f; } }
                    if (m_Sel.kind == SelKind.Area)
                    {
                        var a = fl.areas[m_Sel.i]; var before = a.Corners();
                        if (m_Handle >= 100) { AddCorner(fl, a, m_Handle - 100); m_Handle = m_Handle - 100 + 1; before = a.Corners(); }
                        if (a.IsShape) { var cs = a.Corners(); if (m_Handle < cs.Count) { cs[m_Handle] = su; a.SetShape(cs); } }
                        else { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); if (m_Handle == 0) lo = su; else if (m_Handle == 2) hi = su; else if (m_Handle == 1) { hi.x = su.x; lo.y = su.y; } else { lo.x = su.x; hi.y = su.y; } a.min = Vector2.Min(lo, hi); a.max = Vector2.Max(lo, hi); }
                        WallsFollow(fl, before, a.Corners());
                    }
                    if (m_Sel.kind == SelKind.Opening)
                    {
                        var w = fl.walls[m_Sel.i]; var o = w.openings[m_Sel.j]; float Hs = m_B.HeightOf(m_Sel.level);
                        if (m_Handle >= 2) { float y = Mathf.Round(UpOn(w, e.mousePosition) / 0.05f) * 0.05f; if (m_Handle == 2) o.top = Mathf.Clamp(y, (o.kind == Building.OpeningKind.Window || o.kind == Building.OpeningKind.Arch ? o.bottom : 0) + 0.2f, Hs); else o.bottom = Mathf.Clamp(y, 0, o.top - 0.2f); }
                        else { float x = Along(w, e.mousePosition), sx = Mathf.Round(x / SnapStep) * SnapStep; float l0 = o.at - o.width / 2, r0 = o.at + o.width / 2; if (m_Handle == 0) l0 = Mathf.Clamp(sx, 0, r0 - 0.1f); else r0 = Mathf.Clamp(sx, l0 + 0.1f, w.Length); o.at = (l0 + r0) / 2; o.width = r0 - l0; }
                    }
                });
                else if (m_Drag == Grab.Move)
                {
                    if (m_Sel.kind == SelKind.Opening) { var w = fl.walls[m_Sel.i]; float x = Along(w, e.mousePosition), d = Mathf.Round((x - m_Last.x) / SnapStep) * SnapStep; if (Mathf.Abs(d) > 1e-4f) { m_Last.x += d; Step(() => w.openings[m_Sel.j].at = Mathf.Clamp(w.openings[m_Sel.j].at + d, 0, w.Length)); } }
                    else { var d = su - m_Last; if (d.sqrMagnitude > 1e-6f) { m_Last = su; Step(() => MoveBy(m_Sel, d)); } }
                }
                e.Use();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_Drag = Grab.None; End(); e.Use(); }
        }

        // ---- a room from the corners you click

        void PolyTool(Event e, bool over, bool have, Vector2 su, float y0)
        {
            if (e.type == EventType.Repaint && (over || m_PolyPts.Count > 0))
            {
                var pts = m_PolyPts.Select(p => L(p, y0 + 0.03f)).ToList(); for (int k = 0; k + 1 < pts.Count; k++) Line(pts[k], pts[k + 1], Add, 4); if (pts.Count > 0 && have) Line(pts[pts.Count - 1], L(su, y0 + 0.03f), new Color(Add.r, Add.g, Add.b, 0.6f), 2.5f); if (pts.Count >= 2 && have) Line(L(su, y0 + 0.03f), pts[0], new Color(Add.r, Add.g, Add.b, 0.3f), 1.5f);
                foreach (var p in pts) Dot(p, Add, 5); if (pts.Count >= 3) Dot(pts[0], Color.white, 8); if (have) Dot(L(su, y0 + 0.03f), Add, 4);
                if (m_PolyPts.Count >= 3 && have) { var cs = new List<Vector2>(m_PolyPts) { su }; float s = 0; for (int k = 0; k < cs.Count; k++) { var a = cs[k]; var b = cs[(k + 1) % cs.Count]; s += a.x * b.y - b.x * a.y; } Text(L(su, y0 + 0.3f), M(Mathf.Abs(s) / 2) + " m²"); }
                m_Status = m_PolyPts.Count == 0 ? "click the first corner" : m_PolyPts.Count < 3 ? "click the next corner (Esc cancels)" : "click the next corner, or the first one again (or Enter) to finish";
            }
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt)
            {
                if (m_PolyPts.Count >= 3 && (su - m_PolyPts[0]).magnitude < Mathf.Max(0.05f, SnapStep * 0.3f)) { ShapedRoom(m_PolyPts, m_Tool == Tool.Room); m_PolyPts.Clear(); }
                else if (m_PolyPts.Count == 0 || (su - m_PolyPts[m_PolyPts.Count - 1]).sqrMagnitude > 1e-6f) m_PolyPts.Add(su);
                e.Use();
            }
        }
        /// <summary>How high up a wall (from its floor) the mouse points.</summary>
        float UpOn(Building.Wall w, Vector2 mouse) { var r = LocalRay(mouse); float y0 = m_B.Y(m_Sel.kind != SelKind.None ? m_Sel.level : m_Level); float len = w.Length; var d = (w.b - w.a) / Mathf.Max(1e-5f, len); var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up); var A = L(w.a, y0); float den = Vector3.Dot(r.direction, fw); if (Mathf.Abs(den) < 1e-6f) return 0; float k = Vector3.Dot(A - r.origin, fw) / den; return (r.origin + r.direction * k).y - y0; }
        float Along(Building.Wall w, Vector2 mouse) { var r = LocalRay(mouse); float y0 = m_B.Y(m_Level); float len = w.Length; var d = (w.b - w.a) / Mathf.Max(1e-5f, len); var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up); var A = L(w.a, y0); float den = Vector3.Dot(r.direction, fw); if (Mathf.Abs(den) < 1e-6f) return 0; float k = Vector3.Dot(A - r.origin, fw) / den; return Vector3.Dot(r.origin + r.direction * k - A, rt); }
        void MoveBy(Sel s, Vector2 d)
        {
            var fl = m_B.FloorAt(s.level);
            switch (s.kind) { case SelKind.Column: fl.columns[s.i].at += d; break; case SelKind.Beam: fl.beams[s.i].a += d; fl.beams[s.i].b += d; break; case SelKind.Wall: fl.walls[s.i].a += d; fl.walls[s.i].b += d; break; case SelKind.Stair: fl.stairs[s.i].at += d; break; case SelKind.Piece: fl.pieces[s.i].at += d; break; case SelKind.Area: fl.areas[s.i].Move(p => p + d); break; case SelKind.Opening: { var w = fl.walls[s.i]; var dir = (w.b - w.a).normalized; w.openings[s.j].at = Mathf.Clamp(w.openings[s.j].at + Vector2.Dot(d, dir), 0, w.Length); break; } }
        }
        string Describe(Sel s)
        {
            var fl = m_B.FloorAt(s.level);
            switch (s.kind) { case SelKind.Column: return "column"; case SelKind.Beam: return "beam, " + M((fl.beams[s.i].b - fl.beams[s.i].a).magnitude) + " long"; case SelKind.Wall: { var w = fl.walls[s.i]; return (Mathf.Abs(w.bend) > 0.01f ? "curved wall, " : "wall, ") + M(w.Length) + " long"; } case SelKind.Opening: { var o = fl.walls[s.i].openings[s.j]; return o.kind.ToString().ToLower() + ", " + M(o.width) + " wide"; } case SelKind.Stair: return "stairs (" + fl.stairs[s.i].kind + ")"; case SelKind.Piece: return PartName(fl.pieces[s.i].part); case SelKind.Area: { var a = fl.areas[s.i]; var z = a.max - a.min; return (a.hole ? "hole, " : "floor, ") + M(Mathf.Abs(z.x)) + " x " + M(Mathf.Abs(z.y)); } }
            return "";
        }
        void Nudge(Vector2 d) { if (!Valid(m_Sel)) return; Change("Nudge", () => MoveBy(m_Sel, d * SnapStep)); }
        void TurnSelected(float deg)
        {
            if (!Valid(m_Sel)) return; var fl = m_B.FloorAt(m_Sel.level);
            Change("Turn", () => { switch (m_Sel.kind) { case SelKind.Stair: fl.stairs[m_Sel.i].turn = Mathf.Repeat(fl.stairs[m_Sel.i].turn + deg, 360); break; case SelKind.Piece: fl.pieces[m_Sel.i].turn = Mathf.Repeat(fl.pieces[m_Sel.i].turn + deg, 360); break; case SelKind.Wall: { var w = fl.walls[m_Sel.i]; var c = (w.a + w.b) / 2; var q = Quaternion.Euler(0, 0, -deg); w.a = c + (Vector2)(q * (w.a - c)); w.b = c + (Vector2)(q * (w.b - c)); w.a = Snap(w.a); w.b = Snap(w.b); break; } } });
        }
        void DeleteSelected()
        {
            if (!Valid(m_Sel)) return; var fl = m_B.FloorAt(m_Sel.level); var s = m_Sel;
            Change("Delete", () => { switch (s.kind) { case SelKind.Column: fl.columns.RemoveAt(s.i); break; case SelKind.Beam: fl.beams.RemoveAt(s.i); break; case SelKind.Wall: fl.walls.RemoveAt(s.i); break; case SelKind.Opening: fl.walls[s.i].openings.RemoveAt(s.j); break; case SelKind.Stair: fl.stairs.RemoveAt(s.i); break; case SelKind.Piece: fl.pieces.RemoveAt(s.i); break; case SelKind.Area: fl.areas.RemoveAt(s.i); break; } }); m_Sel = default;
        }
        void Duplicate()
        {
            if (!Valid(m_Sel)) return; var fl = m_B.FloorAt(m_Sel.level); var s = m_Sel; var off = Vector2.one * m_B.cell;
            T Copy<T>(T x) => JsonUtility.FromJson<T>(JsonUtility.ToJson(x));
            Change("Copy", () => { switch (s.kind) { case SelKind.Column: { var c = Copy(fl.columns[s.i]); c.at += off; fl.columns.Add(c); m_Sel.i = fl.columns.Count - 1; break; } case SelKind.Beam: { var c = Copy(fl.beams[s.i]); c.a += off; c.b += off; fl.beams.Add(c); m_Sel.i = fl.beams.Count - 1; break; } case SelKind.Wall: { var c = Copy(fl.walls[s.i]); c.a += off; c.b += off; fl.walls.Add(c); m_Sel.i = fl.walls.Count - 1; break; } case SelKind.Opening: { var w = fl.walls[s.i]; var c = Copy(w.openings[s.j]); c.at = Mathf.Min(w.Length - c.width / 2, c.at + c.width + 0.3f); w.openings.Add(c); m_Sel.j = w.openings.Count - 1; break; } case SelKind.Stair: { var c = Copy(fl.stairs[s.i]); c.at += off; fl.stairs.Add(c); m_Sel.i = fl.stairs.Count - 1; break; } case SelKind.Piece: { var c = Copy(fl.pieces[s.i]); c.at += off; fl.pieces.Add(c); m_Sel.i = fl.pieces.Count - 1; break; } case SelKind.Area: { var c = Copy(fl.areas[s.i]); c.min += off; c.max += off; fl.areas.Add(c); m_Sel.i = fl.areas.Count - 1; break; } } });
        }

        // ---- rooms, floors, holes

        void RectTool(Event e, int id, bool over, bool have, Vector2 su, float y0, bool erase)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt) { m_Drag = Grab.Rect; m_From = m_To = su; m_Erase = erase; GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { m_To = su; e.Use(); }
            if (e.type == EventType.Repaint && (over || m_Drag == Grab.Rect))
            {
                var col = (m_Drag == Grab.Rect ? m_Erase : erase) ? Take : Add; if (m_Drag == Grab.Rect) { RectAt(m_From, m_To, y0 + 0.02f, col, 0.25f); var z = m_To - m_From; Text(L((m_From + m_To) / 2, y0), M(Mathf.Abs(z.x)) + " x " + M(Mathf.Abs(z.y))); if (m_Tool == Tool.Room && !m_Erase) { float h = m_B.HeightOf(m_Level) - m_B.slabThickness; var lo = Vector2.Min(m_From, m_To); var hi = Vector2.Max(m_From, m_To); foreach (var c in new[] { lo, new Vector2(hi.x, lo.y), hi, new Vector2(lo.x, hi.y) }) Line(L(c, y0), L(c, y0 + h), col, 2); RectAt(lo, hi, y0 + h, col, 0); } }
                else if (have) Dot(L(su, y0 + 0.02f), col, 5);
                if (m_Drag == Grab.Rect && !m_Erase && (m_Tool == Tool.Room || m_Tool == Tool.Floor || m_Tool == Tool.Balcony))
                {
                    var lo = Vector2.Min(m_From, m_To); var hi = Vector2.Max(m_From, m_To); bool round = m_RoomShape == 1 && m_Tool != Tool.Balcony;
                    if (hi.x - lo.x > 1e-3f && hi.y - lo.y > 1e-3f) SetGhost("rect" + m_Tool + lo + hi + round + m_RoomSides, () => { var p = Part(); var a = new Building.Area(); if (round) a.SetShape(BuildingMaker.Round(lo, hi, Mathf.Max(3, m_RoomSides))); else { a.min = lo; a.max = hi; } p.areas.Add(a); if (m_Tool == Tool.Room) { var cs = a.Corners(); for (int k = 0; k < cs.Count; k++) AddWallLine(p, cs[k], cs[(k + 1) % cs.Count]); } return p; });
                }
                m_Status = m_Drag == Grab.Rect ? "let go to make it (Esc or right-click: don't)" : "drag from corner to corner";
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0; m_Drag = Grab.None; var lo = Vector2.Min(m_From, m_To); var hi = Vector2.Max(m_From, m_To); bool er = m_Erase; e.Use(); if (hi.x - lo.x < 1e-3f || hi.y - lo.y < 1e-3f) return;
                if (m_Tool == Tool.Shaft) { int up = m_ShaftUp; Change("Add a lift shaft", () => { for (int k = 0; k <= up; k++) { var f = m_B.FloorAt(m_Level + k, true); if (k > 0) f.areas.Add(new Building.Area { min = lo, max = hi, hole = true, name = "Shaft" }); else f.areas.Add(new Building.Area { min = lo, max = hi, name = "Shaft" }); foreach (var (a, c2) in new[] { (lo, new Vector2(hi.x, lo.y)), (new Vector2(hi.x, lo.y), hi), (hi, new Vector2(lo.x, hi.y)), (new Vector2(lo.x, hi.y), lo) }) AddWallLine(f, a, c2); } }); return; }
                if (m_Tool == Tool.Balcony) { Change("Add a balcony", () => { var f = m_B.FloorAt(m_Level, true); f.areas.Add(new Building.Area { min = lo, max = hi, name = "Balcony" }); f.rails = true; }); return; }
                if ((m_Tool == Tool.Room || m_Tool == Tool.Floor) && m_RoomShape == 1 && !er) { ShapedRoom(BuildingMaker.Round(lo, hi, Mathf.Max(3, m_RoomSides)), m_Tool == Tool.Room); return; }
                if (m_Tool == Tool.Room && !er) Change("Add a room", () => { var f = m_B.FloorAt(m_Level, true); f.areas.Add(new Building.Area { min = lo, max = hi }); foreach (var (a, b) in new[] { (lo, new Vector2(hi.x, lo.y)), (new Vector2(hi.x, lo.y), hi), (hi, new Vector2(lo.x, hi.y)), (new Vector2(lo.x, hi.y), lo) }) AddWallLine(f, a, b); });
                else if (m_Tool == Tool.Room) Change("Remove a room", () => { var f = m_B.FloorAt(m_Level, true); f.areas.Add(new Building.Area { min = lo, max = hi, hole = true }); foreach (var (a, b) in new[] { (lo, new Vector2(hi.x, lo.y)), (new Vector2(hi.x, lo.y), hi), (hi, new Vector2(lo.x, hi.y)), (new Vector2(lo.x, hi.y), lo) }) RemoveWallLine(f, a, b); f.walls.RemoveAll(w => Inside(w.a, lo, hi) && Inside(w.b, lo, hi)); });
                else Change(er ? "Cut a hole" : "Add floor", () => m_B.FloorAt(m_Level, true).areas.Add(new Building.Area { min = lo, max = hi, hole = er }));
            }
        }
        static bool Inside(Vector2 p, Vector2 lo, Vector2 hi) => p.x >= lo.x - 1e-4f && p.x <= hi.x + 1e-4f && p.y >= lo.y - 1e-4f && p.y <= hi.y + 1e-4f;

        /// <summary>Adds a wall along a line, only where there isn't one already (so rooms side by side share a wall).</summary>
        public static void AddWallLine(Building.Floor f, Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude; if (len < 1e-3f) return; var d = (b - a) / len; var free = new List<(float, float)> { (0, len) };
            foreach (var w in f.walls) { if (!OnLine(w, a, d)) continue; float t0 = Vector2.Dot(w.a - a, d), t1 = Vector2.Dot(w.b - a, d); if (t0 > t1) { var x = t0; t0 = t1; t1 = x; } free = free.SelectMany(s => Cut(s, t0, t1)).ToList(); }
            foreach (var (s0, s1) in free) if (s1 - s0 > 1e-3f) f.walls.Add(new Building.Wall { a = a + d * s0, b = a + d * s1 });
        }
        /// <summary>Takes walls away along a line (a wall that only partly lies on it is cut, keeping its doors and windows).</summary>
        public static void RemoveWallLine(Building.Floor f, Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude; if (len < 1e-3f) return; var d = (b - a) / len;
            foreach (var w in f.walls.ToList())
            {
                if (!OnLine(w, a, d)) continue; float wl = w.Length; var wd = (w.b - w.a) / wl; float t0 = Vector2.Dot(a - w.a, wd), t1 = Vector2.Dot(b - w.a, wd); if (t0 > t1) { var x = t0; t0 = t1; t1 = x; } if (t1 <= 1e-4f || t0 >= wl - 1e-4f) continue;
                f.walls.Remove(w); foreach (var (s0, s1) in Cut((0, wl), t0, t1)) { if (s1 - s0 < 1e-3f) continue; var n = new Building.Wall { a = w.a + wd * s0, b = w.a + wd * s1, height = w.height, bottom = w.bottom, thickness = w.thickness, front = w.front, back = w.back }; foreach (var o in w.openings) if (o.at >= s0 && o.at <= s1) { var c = JsonUtility.FromJson<Building.Opening>(JsonUtility.ToJson(o)); c.at -= s0; n.openings.Add(c); } f.walls.Add(n); }
            }
        }
        static bool OnLine(Building.Wall w, Vector2 a, Vector2 d) { var wd = w.b - w.a; if (wd.sqrMagnitude < 1e-8f) return false; wd.Normalize(); if (Mathf.Abs(Mathf.Abs(Vector2.Dot(wd, d)) - 1) > 1e-3f) return false; var n = new Vector2(-d.y, d.x); return Mathf.Abs(Vector2.Dot(w.a - a, n)) < 1e-3f; }
        static IEnumerable<(float, float)> Cut((float a, float b) s, float c0, float c1) { if (c1 <= s.a || c0 >= s.b) { yield return s; yield break; } if (c0 > s.a) yield return (s.a, c0); if (c1 < s.b) yield return (c1, s.b); }

        // ---- walls

        void WallTool(Event e, int id, bool over, bool have, Vector2 u, float y0, float H, bool erase)
        {
            Vector2 End(Vector2 p) { var s = Snap(p); if (e.alt) return s; var d = s - m_From; if (d.sqrMagnitude < 1e-6f) return s; float ang = Mathf.Round(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4)) * (Mathf.PI / 4); var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)); float len = Vector2.Dot(d, dir); bool diag = Mathf.Abs(dir.x) > 0.1f && Mathf.Abs(dir.y) > 0.1f; if (!diag) len = Mathf.Round(len / SnapStep) * SnapStep; else len = Mathf.Round(len / (SnapStep * 1.41421356f)) * SnapStep * 1.41421356f; var r = m_From + dir * len; return diag ? r : Snap(r); }
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt) { m_Drag = Grab.Wall; m_From = m_To = Snap(u); m_Erase = erase; GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { m_To = End(u); e.Use(); }
            if (e.type == EventType.Repaint && (over || m_Drag == Grab.Wall))
            {
                var col = (m_Drag == Grab.Wall ? m_Erase : erase) ? Take : Add; float h = H - m_B.slabThickness;
                if (m_Drag == Grab.Wall && Mathf.Abs(m_WallBend) > 0.01f && !m_Erase) { var d = (m_To - m_From).normalized; var nf = new Vector2(d.y, -d.x); Vector2 Pt(float s) => Vector2.Lerp(m_From, m_To, s) + nf * m_WallBend * 4 * s * (1 - s); for (int k = 0; k < 16; k++) { Line(L(Pt(k / 16f), y0), L(Pt((k + 1) / 16f), y0), col, 5); Line(L(Pt(k / 16f), y0 + h), L(Pt((k + 1) / 16f), y0 + h), col, 2); } }
                if (m_Drag == Grab.Wall) { var A = L(m_From, y0); var B = L(m_To, y0); Line(A, B, col, 5); Line(A + Vector3.up * h, B + Vector3.up * h, col, 2); Line(A, A + Vector3.up * h, col, 2); Line(B, B + Vector3.up * h, col, 2); Text((A + B) / 2 + Vector3.up * h, M((m_To - m_From).magnitude)); m_Status = "let go to make it (Alt: any angle)"; }
                else if (have) { Dot(L(Snap(u), y0), col, 5); m_Status = erase ? "drag along walls to take them away" : "drag to draw a wall"; }
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0; m_Drag = Grab.None; var a = m_From; var b = m_To; bool er = m_Erase; e.Use(); if ((b - a).sqrMagnitude < 1e-6f) return;
                float bend = m_WallBend; Change(er ? "Remove walls" : "Add a wall", () => { var f = m_B.FloorAt(m_Level, true); if (er) RemoveWallLine(f, a, b); else if (Mathf.Abs(bend) > 0.01f) { RemoveWallLine(f, a, b); f.walls.Add(new Building.Wall { a = a, b = b, bend = bend }); } else AddWallLine(f, a, b); });
            }
        }

        // ---- doors and windows

        int m_OpWall = -1; float m_OpFrom;
        void OpeningTool(Event e, int id, bool over, float y0, float H, bool erase)
        {
            var fl = m_B.FloorAt(m_Level); bool dragging = GUIUtility.hotControl == id && fl != null && m_OpWall >= 0 && m_OpWall < fl.walls.Count; if (!dragging && GUIUtility.hotControl != id) m_OpWall = -1;
            float along = 0; var s = dragging ? new Sel { kind = SelKind.Wall, level = m_Level, i = m_OpWall } : over ? PickAt(e.mousePosition, out along, true) : default;
            if (s.kind == SelKind.None) { if (e.type == EventType.Repaint) m_Status = "point at a wall"; return; }
            var w = fl.walls[s.i]; bool door = m_Tool == Tool.Door; float width = door ? m_DoorW : m_WinW, len = w.Length; if (dragging) along = Along(w, e.mousePosition);
            float at = Mathf.Clamp(Mathf.Round(along / SnapStep) * SnapStep, Mathf.Min(width / 2, len / 2), Mathf.Max(len - width / 2, len / 2)); bool sized = false;
            if (dragging) { float a0 = Mathf.Clamp(m_OpFrom, 0, len), a1 = Mathf.Clamp(Mathf.Round(along / SnapStep) * SnapStep, 0, len); if (Mathf.Abs(a1 - a0) >= 0.2f) { width = Mathf.Abs(a1 - a0); at = (a0 + a1) / 2; sized = true; } else at = Mathf.Clamp(a0, Mathf.Min(width / 2, len / 2), Mathf.Max(len - width / 2, len / 2)); }
            int under = w.openings.FindIndex(o => Mathf.Abs(o.at - along) <= o.width / 2);
            bool blocked = !erase && w.openings.Any(q => Mathf.Abs(q.at - at) < (q.width + width) / 2 - 1e-3f);
            int pair = door && m_DoorKind == Building.OpeningKind.Door && !erase ? w.openings.FindIndex(q => q.kind == Building.OpeningKind.Door && !q.doubleDoor && Mathf.Abs(Mathf.Abs(q.at - at) - (q.width + width) / 2) < 0.06f && Mathf.Abs(q.width - width) < 0.3f) : -1;
            if (e.type == EventType.Repaint)
            {
                if (erase) { if (under >= 0) Outline(new Sel { kind = SelKind.Opening, level = m_Level, i = s.i, j = under }, Take); m_Status = "click a door or window to take it away"; }
                else { var top = BuildingMaker.Top(m_B, w, H); float ob = door ? 0 : m_WinBottom, ot = door ? (m_DoorKind == Building.OpeningKind.Gap ? top : m_DoorH) : m_WinTop; var d = (w.b - w.a) / len; var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up) * BuildingMaker.Thick(m_B, w) * 0.55f; var A = L(w.a, y0); var fillCol = blocked ? Take : door ? new Color(0.55f, 0.4f, 0.3f) : new Color(0.55f, 0.8f, 1f); foreach (var f in new[] { fw, -fw }) { var quad = new[] { A + f + rt * (at - width / 2) + Vector3.up * ob, A + f + rt * (at + width / 2) + Vector3.up * ob, A + f + rt * (at + width / 2) + Vector3.up * ot, A + f + rt * (at - width / 2) + Vector3.up * ot }; Loop(quad, fillCol, 1, 0.45f); Loop(quad, blocked ? Take : Color.white, 4); }
                    if (dragging) { Dot(A + rt * Mathf.Clamp(m_OpFrom, 0, len) + Vector3.up * ob, Color.white, 6); Dot(A + rt * Mathf.Clamp(Mathf.Round(along / SnapStep) * SnapStep, 0, len) + Vector3.up * ob, Accent, 6); }
                    Text(A + rt * at + Vector3.up * ot, blocked ? "too close to another one" : pair >= 0 ? "makes a double door" : M(width) + " x " + M(ot - ob)); m_Status = blocked ? "there's already one there" : dragging ? (sized ? "let go to make it " + M(width) + " wide" : "drag along the wall to make it wider or narrower") : "click to put it here, or drag along the wall for its width"; }
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && over)
            {
                if (erase) { if (under >= 0) Change("Remove an opening", () => w.openings.RemoveAt(under)); }
                else { m_OpWall = s.i; m_OpFrom = Mathf.Round(along / SnapStep) * SnapStep; GUIUtility.hotControl = id; }
                e.Use();
            }
            if (e.type == EventType.MouseDrag && dragging) e.Use();
            if (e.type == EventType.MouseUp && dragging)
            {
                GUIUtility.hotControl = 0; m_OpWall = -1; e.Use(); if (sized) { if (door) m_DoorW = width; else m_WinW = width; }
                if (pair >= 0 && !blocked) { var q = w.openings[pair]; float l0 = Mathf.Min(q.at - q.width / 2, at - width / 2), r0 = Mathf.Max(q.at + q.width / 2, at + width / 2); Change("Make a double door", () => { q.at = (l0 + r0) / 2; q.width = r0 - l0; q.doubleDoor = true; }); return; }
                var o = door ? new Building.Opening { kind = m_DoorKind, at = at, width = width, bottom = 0, top = m_DoorH, fill = m_DoorFill, frame = m_DoorFrame } : new Building.Opening { kind = m_WinArch ? Building.OpeningKind.Arch : Building.OpeningKind.Window, at = at, width = width, bottom = m_WinBottom, top = m_WinTop, fill = m_WinFill, frame = m_WinFrame }; if (!blocked) Change(door ? "Add a door" : "Add a window", () => w.openings.Add(o));
            }
        }

        // ---- stairs

        void StairTool(Event e, int id, bool over, bool have, Vector2 su, Vector2 u, float y0, float H, bool erase)
        {
            var fl = m_B.FloorAt(m_Level); bool dragging = m_Drag == Grab.Stair && GUIUtility.hotControl == id;
            var s = new Building.Stair { kind = m_StairKind, at = dragging ? m_From : su, turn = m_Turn, width = m_StairW, length = m_StairL, rise = m_StairRise, railSides = m_StairRails, lift = dragging ? m_Lift0 : over && have ? Surface(su) : 0 };
            if (dragging) { var d = su - m_From; if (d.magnitude >= 0.5f) { float ang = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; s.turn = Mathf.Repeat(e.control || e.command ? Mathf.Round(ang / 15) * 15 : Mathf.Round(ang / 90) * 90, 360); var dir = Quaternion.Euler(0, s.turn, 0) * Vector3.forward; s.length = Mathf.Max(0.5f, Mathf.Round(Vector2.Dot(d, new Vector2(dir.x, dir.z)) / SnapStep) * SnapStep); } }
            y0 += s.lift; int under = -1; if (fl != null && have) under = fl.stairs.FindIndex(q => BuildingMaker.Footprint(q).Contains(su));
            if (e.type == EventType.Repaint && (over && have || dragging))
            {
                if (erase) { if (under >= 0) Outline(new Sel { kind = SelKind.Stair, level = m_Level, i = under }, Take); m_Status = "click stairs to take them away"; }
                else
                {
                    var m = BuildingMaker.StairFrame(s); float R = s.rise * H; foreach (var f in BuildingMaker.Flights(s)) { var pts = new[] { f.r.min, new Vector2(f.r.xMax, f.r.yMin), f.r.max, new Vector2(f.r.xMin, f.r.yMax) }.Select(q => m.MultiplyPoint3x4(new Vector3(q.x, 0, q.y))).ToArray(); Loop(pts.Select(q => new Vector3(q.x, y0 + 0.03f, q.z)).ToArray(), Add, 2f); }
                    var start = m.MultiplyPoint3x4(Vector3.zero); var fwd = m.MultiplyVector(Vector3.forward); Line(new Vector3(start.x, y0 + 0.05f, start.z), new Vector3(start.x, y0 + 0.05f, start.z) + fwd * Mathf.Min(1.2f, s.length * 0.5f), Color.white, 4); Text(new Vector3(start.x, y0 + 0.4f, start.z), dragging ? M(s.length) + " long" : "drag for its length and way, or click");
                    var keep = s; SetGhost(JsonUtility.ToJson(keep), () => { var p = Part(); p.stairs.Add(JsonUtility.FromJson<Building.Stair>(JsonUtility.ToJson(keep))); return p; });
                    m_Status = dragging ? "let go to put them (Esc or right-click: don't)" : "click to put stairs here, or drag from where they start for their length and way (" + (s.rise >= 0.99f ? "they open the floor above" : "part of a floor up") + ")";
                }
            }
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt)
            {
                if (erase) { if (under >= 0) Change("Remove stairs", () => fl.stairs.RemoveAt(under)); }
                else { m_Drag = Grab.Stair; m_From = su; m_Lift0 = Surface(su); GUIUtility.hotControl = id; }
                e.Use();
            }
            if (e.type == EventType.MouseDrag && dragging) e.Use();
            if (e.type == EventType.MouseUp && dragging)
            {
                GUIUtility.hotControl = 0; m_Drag = Grab.None; e.Use(); if (!Mathf.Approximately(s.length, m_StairL) && (su - m_From).magnitude >= 0.5f) { m_StairL = s.length; m_Turn = s.turn; }
                Change("Add stairs", () => { m_B.FloorAt(m_Level, true).stairs.Add(s); if (s.lift + s.rise * H >= H * 0.98f) m_B.FloorAt(m_Level + 1, true); }); ClearGhost();
            }
        }
        float m_Lift0;

        // ---- tunnels

        void TunnelTool(Event e, int id, bool over, bool have, Vector2 u, float y0, bool erase)
        {
            float c = m_B.cell; var cell = new Vector2Int(Mathf.FloorToInt(u.x / c), Mathf.FloorToInt(u.y / c));
            IEnumerable<Vector2Int> Brush(Vector2Int q) { for (int x = 0; x < m_TunnelWide; x++) for (int z = 0; z < m_TunnelWide; z++) yield return q + new Vector2Int(x, z); }
            void Visit(Vector2Int q) { if (m_Path.Count == 0) { m_Path.Add(q); return; } var last = m_Path[m_Path.Count - 1]; while (last != q) { var d = q - last; last += Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int(Math.Sign(d.x), 0) : new Vector2Int(0, Math.Sign(d.y)); if (!m_Path.Contains(last)) m_Path.Add(last); } }
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt) { m_Drag = Grab.Path; m_Erase = erase; m_Path.Clear(); Visit(cell); GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && have) { Visit(cell); e.Use(); }
            if (e.type == EventType.Repaint && (over || m_Drag == Grab.Path)) { var col = (m_Drag == Grab.Path ? m_Erase : erase) ? Take : Add; foreach (var q in (m_Drag == Grab.Path ? m_Path : (have ? new List<Vector2Int> { cell } : new List<Vector2Int>())).SelectMany(Brush).Distinct()) RectAt((Vector2)q * c, ((Vector2)q + Vector2.one) * c, y0 + 0.02f, col, 0.25f); m_Status = "drag a path"; }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0; m_Drag = Grab.None; var cells = new HashSet<Vector2Int>(m_Path.SelectMany(Brush)); bool er = m_Erase; m_Path.Clear(); e.Use(); if (cells.Count == 0) return;
                Change(er ? "Fill a tunnel" : "Dig a tunnel", () =>
                {
                    var f = m_B.FloorAt(m_Level, true); var cover = BuildingMaker.Cover(m_B, f); float q = BuildingMaker.Fine(m_B); bool Has(Vector2Int k) => cells.Contains(k) || cover.ContainsKey(new Vector2Int(Mathf.RoundToInt((k.x + 0.5f) * c / q - 0.5f), Mathf.RoundToInt((k.y + 0.5f) * c / q - 0.5f)));
                    foreach (var row in cells.GroupBy(k => k.y)) { var xs = row.Select(k => k.x).OrderBy(x => x).ToList(); int s0 = xs[0], prev = xs[0]; foreach (var x in xs.Skip(1).Concat(new[] { int.MaxValue })) { if (x == prev + 1) { prev = x; continue; } f.areas.Add(new Building.Area { min = new Vector2(s0 * c, row.Key * c), max = new Vector2((prev + 1) * c, (row.Key + 1) * c), roof = !er && m_TunnelLid, hole = er }); s0 = prev = x; } }
                    if (er) { foreach (var k in cells) foreach (var (a, b) in Edges(k, c)) RemoveWallLine(f, a, b); return; }
                    foreach (var k in cells) foreach (var (n, a, b) in new[] { (k + Vector2Int.right, new Vector2(k.x + 1, k.y) * c, new Vector2(k.x + 1, k.y + 1) * c), (k + Vector2Int.left, new Vector2(k.x, k.y) * c, new Vector2(k.x, k.y + 1) * c), (k + Vector2Int.up, new Vector2(k.x, k.y + 1) * c, new Vector2(k.x + 1, k.y + 1) * c), (k + Vector2Int.down, new Vector2(k.x, k.y) * c, new Vector2(k.x + 1, k.y) * c) })
                    { if (cells.Contains(n)) RemoveWallLine(f, a, b); else if (!Has(n)) AddWallLine(f, a, b); }
                    Join(f);
                });
            }
        }
        static IEnumerable<(Vector2, Vector2)> Edges(Vector2Int k, float c) { yield return (new Vector2(k.x, k.y) * c, new Vector2(k.x + 1, k.y) * c); yield return (new Vector2(k.x + 1, k.y) * c, new Vector2(k.x + 1, k.y + 1) * c); yield return (new Vector2(k.x, k.y + 1) * c, new Vector2(k.x + 1, k.y + 1) * c); yield return (new Vector2(k.x, k.y) * c, new Vector2(k.x, k.y + 1) * c); }
        /// <summary>Walls on the same line that touch become one (only plain ones, so doors and windows stay where they are).</summary>
        public static void Join(Building.Floor f)
        {
            bool again = true; while (again)
            {
                again = false; for (int i = 0; i < f.walls.Count && !again; i++) for (int j = i + 1; j < f.walls.Count && !again; j++)
                    {
                        var a = f.walls[i]; var b = f.walls[j]; if (a.openings.Count > 0 || b.openings.Count > 0 || a.height != b.height || a.front != b.front || a.back != b.back) continue; var d = (a.b - a.a).normalized; if (!OnLine(b, a.a, d)) continue;
                        var pts = new[] { a.a, a.b, b.a, b.b }.OrderBy(p => Vector2.Dot(p - a.a, d)).ToArray(); float span = Vector2.Dot(pts[3] - pts[0], d); if (span > a.Length + b.Length + 1e-3f) continue; a.a = pts[0]; a.b = pts[3]; f.walls.RemoveAt(j); again = true;
                    }
            }
        }

        // ---- parts

        /// <summary>Whether a part would stand where people walk through a door (a door's width, one unit out on both sides).</summary>
        bool Blocks(Building.Floor fl, Vector2 at, Vector3 size, float turn)
        {
            if (fl == null) return false; float rx = Mathf.Max(size.x, size.z) / 2;
            foreach (var w in fl.walls) { var d = (w.b - w.a).normalized; var n = new Vector2(-d.y, d.x); foreach (var o in w.openings.Where(o => o.kind != Building.OpeningKind.Window)) { var p = w.a + d * o.at; var rel = at - p; float along = Vector2.Dot(rel, d), across = Vector2.Dot(rel, n); if (Mathf.Abs(along) < o.width / 2 + rx && Mathf.Abs(across) < 1f + rx) return true; } }
            return false;
        }
        Vector2 PartSnap(BuildPart part, Vector2 u, out float turn)
        {
            turn = m_Turn; if (part && part.sits == BuildPart.Sits.Corner) return Snap(u);
            if (part && part.sits == BuildPart.Sits.Edge) { var fl = m_B.FloorAt(m_Level); var w = fl?.walls.OrderBy(q => BuildingMaker.Dist(u, q)).FirstOrDefault(); if (w != null && BuildingMaker.Dist(u, w) < Mathf.Max(2.5f, part.size.z * 2f)) { var d = (w.b - w.a).normalized; var n = new Vector2(-d.y, d.x); float along = Vector2.Dot(u - w.a, d), sgn = Mathf.Sign(Vector2.Dot(u - w.a, n)); along = Mathf.Clamp(Mathf.Round(along / SnapStep) * SnapStep, 0, w.Length); var face = n * sgn; turn = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg; return w.a + d * along + face * (BuildingMaker.Thick(m_B, w) / 2 + part.size.z / 2 + 0.01f); } }
            if (m_Snap > 0) return Snap(u); float c = m_B.cell; Vector2 size = part ? new Vector2(part.size.x, part.size.z) / c : Vector2.one; int Sq(float v) => Mathf.Max(1, Mathf.CeilToInt(v - 0.05f)); bool side = Mathf.Abs(Mathf.Repeat(turn, 180) - 90) < 45; bool ew = Sq(side ? size.y : size.x) % 2 == 0, ed = Sq(side ? size.x : size.y) % 2 == 0;
            return new Vector2(ew ? Mathf.Round(u.x / c) * c : (Mathf.Floor(u.x / c) + 0.5f) * c, ed ? Mathf.Round(u.y / c) * c : (Mathf.Floor(u.y / c) + 0.5f) * c);
        }
        void PropTool(Event e, bool over, bool have, Vector2 u, float y0, bool erase)
        {
            var fl = m_B.FloorAt(m_Level); var part = BuildingMaker.Part(m_Part); if (!over || !have) return;
            if (erase) { var s = PickAt(e.mousePosition, out _); bool ok = s.kind == SelKind.Piece; if (e.type == EventType.Repaint) { if (ok) Outline(s, Take); m_Status = "click a part to take it away"; } if (e.type == EventType.MouseDown && e.button == 0 && ok) { Change("Remove a part", () => fl.pieces.RemoveAt(s.i)); e.Use(); } return; }
            if (!part) { if (e.type == EventType.Repaint) m_Status = "pick a part on the right (Parts)"; return; }
            var at = PartSnap(part, u, out float turn); float H = m_B.HeightOf(m_Level); float lift = part.sits == BuildPart.Sits.Ceiling ? H - m_B.slabThickness - part.size.y : m_Lift + Surface(at); bool blocks = Blocks(fl, at, part.size, turn);
            if (e.type == EventType.Repaint) { var sz = part.size; var m = Matrix4x4.TRS(new Vector3(at.x, y0 + lift, at.y), Quaternion.Euler(0, turn, 0), Vector3.one); if (blocks) Text(new Vector3(at.x, y0 + lift + sz.y + 0.3f, at.y), "in front of a doorway"); var c4 = new[] { new Vector3(-sz.x / 2, 0, -sz.z / 2), new Vector3(sz.x / 2, 0, -sz.z / 2), new Vector3(sz.x / 2, 0, sz.z / 2), new Vector3(-sz.x / 2, 0, sz.z / 2) }; var pc = blocks ? Take : Add; Loop(c4.Select(q => m.MultiplyPoint3x4(q)).ToArray(), pc, 2.5f, 0.2f); Loop(c4.Select(q => m.MultiplyPoint3x4(q + Vector3.up * sz.y)).ToArray(), pc, 1.5f); for (int i = 0; i < 4; i++) Line(m.MultiplyPoint3x4(c4[i]), m.MultiplyPoint3x4(c4[i] + Vector3.up * sz.y), pc, 1.5f); Line(m.MultiplyPoint3x4(Vector3.zero), m.MultiplyPoint3x4(Vector3.forward * Mathf.Max(0.4f, sz.z * 0.7f)), Color.white, 3); m_Status = "click to put " + part.name + " (R turns it)"; }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { string pid = m_Part; Change("Add " + part.name, () => m_B.FloorAt(m_Level, true).pieces.Add(new Building.Piece { part = pid, at = at, turn = turn, lift = lift })); e.Use(); }
        }

        // ---- paint

        void PaintTool(Event e, bool over)
        {
            if (!over) return; var ray = m_V.Ray(e.mousePosition); bool hit = Hit(ray, out var h); var tg = hit ? PaintTarget(h, e) : null;
            if (e.type == EventType.Repaint) { if (tg != null) { tg.draw(); m_Status = (e.alt ? "pick the material of the " : "paint the ") + tg.what + (e.alt ? "" : " with " + MatName(m_Mat)); } else m_Status = hit ? "nothing to paint there" : "point at the building"; return; }
            if (e.type != EventType.MouseDown || e.button != 0 || tg == null) return; e.Use();
            if (e.alt) { int v = tg.get(); m_Mat = v >= 0 ? v : tg.def; Repaint(); return; }
            int mat = m_Mat; Change("Paint the " + tg.what, () => tg.set(mat));
        }
        static string What(Vector3 n) => n.y > 0.7f ? "floor" : n.y < -0.7f ? "ceiling" : "wall side (Shift: both sides)";
        bool Hit(Ray ray, out RaycastHit best)
        {
            best = default; float d = float.MaxValue; bool any = false; foreach (var c in m_B.GetComponentsInChildren<Collider>()) { if (!c.enabled || !c.gameObject.activeInHierarchy) continue; var made = c.GetComponentInParent<BuildingMade>(); if (made && m_HideAbove && made.level > m_Level) continue; if (c.Raycast(ray, out var h, 5000) && h.distance < d) { d = h.distance; best = h; any = true; } }
            return any;
        }
    }
}
