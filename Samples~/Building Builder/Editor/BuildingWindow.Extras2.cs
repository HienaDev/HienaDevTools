using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Painting anything (and seeing what will be painted), picking materials by their pictures, rooms of any shape
    /// (walls that follow their corners), shuffling rooms, the floor plan window, and finishing a building.</summary>
    public partial class BuildingWindow
    {
        [SerializeField] float m_WalkSpeed = 3f; readonly List<Vector2> m_PolyPts = new List<Vector2>(); [SerializeField] int m_RoomShape; [SerializeField] int m_RoomSides = 8; [SerializeField] float m_WallBend; int m_HoverRoom = -1;

        // ------------------------------------------------------------------ painting: what's under the mouse, lit up, and how to paint it

        class Target { public string what; public Func<int> get; public Action<int> set; public Action draw; public int def; }
        Target PaintTarget(RaycastHit h, Event e)
        {
            var made = h.collider.GetComponentInParent<BuildingMade>(); var l = Tr.InverseTransformPoint(h.point); var n = Tr.InverseTransformDirection(h.normal); var u = new Vector2(l.x - n.x * 0.01f, l.z - n.z * 0.01f); bool ctrl = e.control || e.command;
            if (made && made.piece >= 0) { var f0 = m_B.FloorAt(made.level); if (f0 == null || made.piece >= f0.pieces.Count) return null; var p = f0.pieces[made.piece]; var sel = new Sel { kind = SelKind.Piece, level = made.level, i = made.piece }; return new Target { what = "part", get = () => p.mat, set = m => p.mat = m, draw = () => Outline(sel, Add, 3), def = 3 }; }
            if (made && made.opening >= 0 && made.wall >= 0) { var f0 = m_B.FloorAt(made.level); if (f0 == null || made.wall >= f0.walls.Count || made.opening >= f0.walls[made.wall].openings.Count) return null; var o = f0.walls[made.wall].openings[made.opening]; var sel = new Sel { kind = SelKind.Opening, level = made.level, i = made.wall, j = made.opening }; return new Target { what = "door", get = () => o.leafMat, set = m => o.leafMat = m, draw = () => Outline(sel, Add, 3), def = 3 }; }
            var hitName = h.collider.name; var fl = m_B.FloorAt(m_B.LevelAt(l.y - 0.02f)) ?? m_B.FloorAt(m_B.LevelAt(l.y + 0.02f));
            if (made && hitName == "Roof") { var rf = m_B.FloorAt(made.level); if (rf == null) return null; return new Target { what = "roof", get = () => rf.roofMat, set = m => rf.roofMat = m, draw = () => { }, def = 3 }; }
            if (hitName == "Railings") { var rs = m_B.railing; var st = fl?.stairs.FirstOrDefault(s => { var r = BuildingMaker.Footprint(s); r.xMin -= 0.2f; r.yMin -= 0.2f; r.xMax += 0.2f; r.yMax += 0.2f; return r.Contains(u); }); if (st != null && st.railing.own) rs = st.railing; var target = rs; return new Target { what = st != null && st.railing.own ? "railing of these stairs" : "railings", get = () => target.mat, set = m => target.mat = m, draw = () => { }, def = m_B.trimMat }; }
            float y0 = fl != null ? m_B.Y(fl.level) : 0, H = fl != null ? m_B.HeightOf(fl.level) : 1, top = y0 + H - m_B.slabThickness;
            if (fl != null) for (int i = 0; i < fl.stairs.Count; i++) { var s = fl.stairs[i]; var r = BuildingMaker.Footprint(s); r.xMin -= 0.06f; r.yMin -= 0.06f; r.xMax += 0.06f; r.yMax += 0.06f; if (r.Contains(u) && l.y <= y0 + s.lift + s.rise * H + 1.0f && l.y >= y0 + s.lift - 0.02f) { var sel = new Sel { kind = SelKind.Stair, level = fl.level, i = i }; bool tread = n.y > 0.7f && e.shift; return tread ? new Target { what = "step tops of these stairs", get = () => s.topMat, set = m => s.topMat = m, draw = () => Outline(sel, Add, 3), def = s.mat >= 0 ? s.mat : 3 } : new Target { what = "stairs (Shift: just the step tops)", get = () => s.mat, set = m => s.mat = m, draw = () => Outline(sel, Add, 3), def = 3 }; } }
            if (fl != null) for (int i = 0; i < fl.columns.Count; i++) { var c = fl.columns[i]; if ((c.at - u).magnitude < c.size * 0.75f + 0.03f) { var sel = new Sel { kind = SelKind.Column, level = fl.level, i = i }; return new Target { what = "column", get = () => c.mat, set = m => c.mat = m, draw = () => Outline(sel, Add, 3), def = 3 }; } }
            if (fl != null) for (int i = 0; i < fl.beams.Count; i++) { var bm = fl.beams[i]; if (l.y >= top - bm.depth - 0.02f && DistSeg(u, bm.a, bm.b) < bm.width) { var sel = new Sel { kind = SelKind.Beam, level = fl.level, i = i }; return new Target { what = "beam", get = () => bm.mat, set = m => bm.mat = m, draw = () => Outline(sel, Add, 3), def = 3 }; } }
            // Ctrl: the whole room it's in
            if (ctrl)
            {
                int what = n.y > 0.7f ? 0 : n.y < -0.7f ? 2 : 1; var lf = what == 2 ? m_B.FloorAt(m_B.LevelNear(l.y + m_B.slabThickness)) : what == 0 ? m_B.FloorAt(m_B.LevelNear(l.y)) : fl; if (lf == null) return null; var pt = what == 1 ? u + new Vector2(n.x, n.z) * 0.3f : u;
                var room = lf.areas.LastOrDefault(a => !a.hole && a.Contains(pt)); if (room == null) return null; float ry = m_B.Y(lf.level) + room.lift + (what == 2 ? m_B.HeightOf(lf.level) - m_B.slabThickness : 0.03f);
                return new Target { what = (what == 0 ? "floor" : what == 1 ? "walls" : "ceiling") + " of the whole room", get = () => what == 0 ? room.top : what == 2 ? room.under : -1, set = m => PaintRoomNow(lf, room, m, what), draw = () => AreaLoop(room, ry, Add, 0.25f), def = what };
            }
            if (Mathf.Abs(n.y) > 0.7f)
            {
                bool up = n.y > 0; int level = up ? m_B.LevelNear(l.y) : m_B.LevelNear(l.y + m_B.slabThickness); var f = m_B.FloorAt(level);
                if (f != null && up) { var raised = f.areas.LastOrDefault(a => !a.hole && Mathf.Abs(a.lift) > 0.01f && a.Contains(u) && Mathf.Abs(l.y - (m_B.Y(level) + a.lift)) < 0.05f); if (raised != null) { float ry = m_B.Y(level) + raised.lift + 0.03f; return new Target { what = "raised floor", get = () => raised.top, set = m => raised.top = m, draw = () => AreaLoop(raised, ry, Add, 0.25f), def = 0 }; } }
                var below = m_B.FloorAt(m_B.LevelAt(l.y - 0.01f)); var stb = below?.stairs.FirstOrDefault(s => BuildingMaker.Footprint(s).Contains(u)); if (stb != null && (f == null || Mathf.Abs(l.y - m_B.Y(level)) > 0.05f)) return new Target { what = "stairs", get = () => stb.mat, set = m => stb.mat = m, draw = () => { }, def = 3 };
                Building.Area At(Building.Floor q, bool roofs) => q?.areas.LastOrDefault(a => !a.hole && (!roofs || a.roof) && a.Contains(u));
                var ar = At(f, false); bool lid = false; if (ar == null || Mathf.Abs(l.y - (up ? m_B.Y(level) + ar.lift : m_B.Y(level) - m_B.slabThickness)) > 0.05f) { var lf = m_B.FloorAt(level - 1); var la = At(lf, true); if (la != null) { ar = la; lid = true; } }
                if (ar == null) return null; var area = ar; float ay = l.y + (up ? 0.03f : -0.03f);
                if (lid && up) return new Target { what = "lid", get = () => area.roofMat, set = m => area.roofMat = m, draw = () => AreaLoop(area, ay, Add, 0.25f), def = 1 };
                return up ? new Target { what = "floor (Ctrl: the whole room)", get = () => area.top, set = m => area.top = m, draw = () => AreaLoop(area, ay, Add, 0.25f), def = 0 } : new Target { what = "ceiling", get = () => area.under, set = m => area.under = m, draw = () => AreaLoop(area, ay, Add, 0.25f), def = 2 };
            }
            var f2 = m_B.FloorAt(m_B.LevelAt(l.y + 0.001f)); if (f2 == null) return null; var best = f2.walls.OrderBy(w => BuildingMaker.Dist(u, w)).FirstOrDefault(); if (best == null || BuildingMaker.Dist(u, best) > BuildingMaker.Thick(m_B, best) + Mathf.Abs(best.bend) + 0.2f) return null;
            float wy0 = m_B.Y(f2.level), len = best.Length; var dd = (best.b - best.a) / Mathf.Max(1e-4f, len); float along = Vector2.Dot(u - best.a, dd), yy = l.y - wy0;
            // the frame round a door or window
            float tw = m_B.trimWidth + 0.03f; for (int j = 0; j < best.openings.Count; j++)
            {
                var o = best.openings[j]; if (!o.frame || o.kind == Building.OpeningKind.Gap) continue; float a = o.at - o.width / 2, c = o.at + o.width / 2, ob = o.kind == Building.OpeningKind.Door ? 0 : o.bottom, ot = o.top;
                bool near = along > a - tw && along < c + tw && yy > ob - tw - 0.06f && yy < ot + tw; bool inHole = along > a + 0.01f && along < c - 0.01f && yy > ob + 0.01f && yy < ot - 0.01f;
                if (near && !inHole) { var sel = new Sel { kind = SelKind.Opening, level = f2.level, i = f2.walls.IndexOf(best), j = j }; var op = o; return new Target { what = "frame of the " + o.kind.ToString().ToLower(), get = () => op.frameMat, set = m => op.frameMat = m, draw = () => Outline(sel, Add, 3), def = m_B.trimMat }; }
            }
            var front = Vector3.Cross(new Vector3(dd.x, 0, dd.y), Vector3.up); bool isFront = Vector3.Dot(n, front) >= 0; bool both = e.shift; var wall = best; var wsel = new Sel { kind = SelKind.Wall, level = f2.level, i = f2.walls.IndexOf(best) };
            return new Target { what = both ? "both sides of this wall" : "this side of the wall (Shift: both)", get = () => isFront ? wall.front : wall.back, set = m => { if (isFront || both) wall.front = m; if (!isFront || both) wall.back = m; }, draw = () => WallSide(wsel, both ? 0 : isFront ? 1 : -1), def = 1 };
        }
        void PaintRoomNow(Building.Floor fl, Building.Area room, int mat, int what)
        {
            var cs = room.Corners(); var mid = cs.Aggregate(Vector2.zero, (s, v) => s + v) / cs.Count;
            if (what == 0) room.top = mat; else if (what == 2) room.under = mat;
            else foreach (var w in fl.walls) { if (!OnEdgeOf(room, w)) continue; var d = (w.b - w.a).normalized; var front = Vector3.Cross(new Vector3(d.x, 0, d.y), Vector3.up); bool faces = Vector2.Dot(mid - (w.a + w.b) / 2, new Vector2(front.x, front.z)) > 0; if (faces) w.front = mat; else w.back = mat; }
        }
        /// <summary>Whether a wall runs along one of a room's edges.</summary>
        static bool OnEdgeOf(Building.Area a, Building.Wall w) { var cs = a.Corners(); for (int i = 0; i < cs.Count; i++) { var e0 = cs[i]; var e1 = cs[(i + 1) % cs.Count]; if (DistSeg(w.a, e0, e1) < 0.02f && DistSeg(w.b, e0, e1) < 0.02f) return true; } return false; }
        void AreaLoop(Building.Area a, float y, Color c, float fill) { var cs = a.Corners(); Loop(cs.Select(v => L(v, y)).ToArray(), c, 3, fill); }
        void WallSide(Sel s, int side)
        {
            if (!Valid(s)) return; var fl = m_B.FloorAt(s.level); var wl = fl.walls[s.i]; float y0 = m_B.Y(s.level), H = m_B.HeightOf(s.level), len = wl.Length; var d = (wl.b - wl.a) / Mathf.Max(1e-5f, len); var rt = new Vector3(d.x, 0, d.y); var fw = Vector3.Cross(rt, Vector3.up) * BuildingMaker.Thick(m_B, wl) / 2 * 1.08f; var A = L(wl.a, y0); float top = BuildingMaker.Top(m_B, wl, H), bot = Mathf.Max(0, wl.bottom);
            foreach (var f in side == 0 ? new[] { fw, -fw } : new[] { side > 0 ? fw : -fw }) Loop(new[] { A + f + Vector3.up * bot, A + f + rt * len + Vector3.up * bot, A + f + rt * len + Vector3.up * top, A + f + Vector3.up * top }, Add, 3, 0.22f);
        }

        // ------------------------------------------------------------------ picking a material by its picture

        string m_MatKey; int m_MatVal;
        /// <summary>A material slot shown as its picture and name; clicking opens a picker with every material's picture.</summary>
        bool MatField(string label, string key, ref int v, int def)
        {
            bool ch = false; if (m_MatKey == key) { v = m_MatVal; m_MatKey = null; ch = true; }
            var row = EditorGUILayout.GetControlRect(false, 34); var lr = new Rect(row.x, row.y + 8, EditorGUIUtility.labelWidth, 18); GUI.Label(lr, label);
            var box = new Rect(lr.xMax, row.y, row.width - EditorGUIUtility.labelWidth, 34); EditorGUI.DrawRect(box, box.Contains(Event.current.mousePosition) ? PanelHi : new Color(1, 1, 1, 0.05f));
            var m = v >= 0 && v < m_B.materials.Count ? m_B.materials[v] : def >= 0 && def < m_B.materials.Count ? m_B.materials[def] : null; var pic = new Rect(box.x + 2, box.y + 2, 30, 30); var prev = m ? AssetPreview.GetAssetPreview(m) : null; if (prev) GUI.DrawTexture(pic, prev, ScaleMode.ScaleAndCrop); else { EditorGUI.DrawRect(pic, new Color(0, 0, 0, 0.3f)); if (m) Repaint(); }
            GUI.Label(new Rect(pic.xMax + 6, box.y + 8, box.width - 40, 18), (v < 0 ? "(its usual) " : "") + (m ? m.name : "none"), EditorStyles.miniLabel);
            if (GUI.Button(box, new GUIContent("", "Click to pick a material"), GUIStyle.none)) { var k = key; PopupWindow.Show(box, new MatPopup(this, m_B, v, def, i => { m_MatKey = k; m_MatVal = i; Repaint(); })); }
            return ch;
        }
        class MatPopup : PopupWindowContent
        {
            readonly BuildingWindow w; readonly Building b; readonly int cur, def; readonly Action<int> pick; static bool s_All; string m_Find = ""; Vector2 m_Scroll; List<Material> m_Project;
            public MatPopup(BuildingWindow w, Building b, int cur, int def, Action<int> pick) { this.w = w; this.b = b; this.cur = cur; this.def = def; this.pick = pick; }
            public override Vector2 GetWindowSize() => new Vector2(330, 400);
            public override void OnGUI(Rect rect)
            {
                GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Its usual", "Back to what it gets by default"), EditorStyles.miniButton, GUILayout.Width(70))) { pick(-1); editorWindow.Close(); } s_All = GUILayout.Toggle(s_All, new GUIContent(" Every material in the project", "Not only the building's own")); GUILayout.EndHorizontal();
                if (s_All) { m_Find = EditorGUILayout.TextField(m_Find, EditorStyles.toolbarSearchField); if (m_Project == null) m_Project = AssetDatabase.FindAssets("t:Material").Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m && !AssetDatabase.GetAssetPath(m).StartsWith("Packages/") && !AssetDatabase.GetAssetPath(m).Contains("/PSX/")).OrderBy(m => m.name).ToList(); }
                var list = s_All ? m_Project.Where(m => m_Find.Length == 0 || m.name.IndexOf(m_Find, StringComparison.OrdinalIgnoreCase) >= 0).Take(120).ToList() : b.materials;
                m_Scroll = GUILayout.BeginScrollView(m_Scroll); int cols = 4; float cw = (rect.width - 30) / cols;
                for (int i = 0; i < list.Count; i += cols)
                {
                    var row = GUILayoutUtility.GetRect(rect.width - 24, cw + 16);
                    for (int k = i; k < Mathf.Min(list.Count, i + cols); k++)
                    {
                        var m = list[k]; var r = new Rect(row.x + (k - i) * cw, row.y, cw - 4, cw + 14); bool on = !s_All && (k == cur || cur < 0 && k == def); EditorGUI.DrawRect(r, on ? Accent : r.Contains(Event.current.mousePosition) ? PanelHi : new Color(1, 1, 1, 0.05f));
                        var prev = m ? AssetPreview.GetAssetPreview(m) : null; var pic = new Rect(r.x + 3, r.y + 3, r.width - 6, r.width - 6); if (prev) GUI.DrawTexture(pic, prev, ScaleMode.ScaleAndCrop); else { EditorGUI.DrawRect(pic, new Color(0, 0, 0, 0.3f)); editorWindow.Repaint(); }
                        GUI.Label(new Rect(r.x, r.yMax - 14, r.width, 14), (s_All ? "" : (k + 1) + ": ") + (m ? m.name : "empty"), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip });
                        if (GUI.Button(r, new GUIContent("", m ? m.name : ""), GUIStyle.none)) { int idx = k; if (s_All) { Undo.RecordObject(b, "Building: add a material"); idx = b.materials.IndexOf(m); if (idx < 0) { b.materials.Add(m); idx = b.materials.Count - 1; } } pick(idx); editorWindow.Close(); GUIUtility.ExitGUI(); }
                    }
                }
                GUILayout.EndScrollView();
            }
        }

        // ------------------------------------------------------------------ rooms of any shape: their walls follow their corners

        /// <summary>After a room's corners move, the walls along its edges move with them.</summary>
        static void WallsFollow(Building.Floor fl, List<Vector2> before, List<Vector2> after)
        {
            if (before.Count != after.Count) return;
            Vector2 Map(Vector2 p) { for (int i = 0; i < before.Count; i++) { var a = before[i]; var b = before[(i + 1) % before.Count]; var ab = b - a; float l2 = ab.sqrMagnitude; if (l2 < 1e-8f) continue; float k = Vector2.Dot(p - a, ab) / l2; if (k < -1e-3f || k > 1 + 1e-3f) continue; if ((a + ab * k - p).sqrMagnitude > 1e-5f) continue; return Vector2.Lerp(after[i], after[(i + 1) % after.Count], k); } return p; }
            foreach (var w in fl.walls) { var na = Map(w.a); var nb = Map(w.b); if (na == w.a && nb == w.b) continue; float l0 = w.Length; w.a = na; w.b = nb; float l1 = w.Length; if (l0 > 1e-4f) foreach (var o in w.openings) o.at *= l1 / l0; }
        }
        /// <summary>A new corner in the middle of a room's edge (its walls split there, so they can bend).</summary>
        static void AddCorner(Building.Floor fl, Building.Area a, int edge)
        {
            var cs = a.Corners(); var e0 = cs[edge]; var e1 = cs[(edge + 1) % cs.Count]; var mid = (e0 + e1) / 2; cs.Insert(edge + 1, mid); a.SetShape(cs);
            foreach (var w in fl.walls.ToList()) { if (DistSeg(mid, w.a, w.b) > 1e-3f || (mid - w.a).magnitude < 1e-3f || (mid - w.b).magnitude < 1e-3f) continue; float at = (mid - w.a).magnitude; var d = (w.b - w.a).normalized; var b2 = new Building.Wall { a = mid, b = w.b, height = w.height, bottom = w.bottom, thickness = w.thickness, front = w.front, back = w.back, frontFinish = w.frontFinish, backFinish = w.backFinish }; foreach (var o in w.openings.Where(o => o.at > at).ToList()) { w.openings.Remove(o); o.at -= at; b2.openings.Add(o); } w.b = mid; fl.walls.Add(b2); }
        }
        static void RemoveCorner(Building.Floor fl, Building.Area a, int i)
        {
            var cs = a.Corners(); if (cs.Count <= 3) return; var p = cs[i]; cs.RemoveAt(i); a.SetShape(cs);
            // the two walls meeting there become one
            var ends = fl.walls.Where(w => (w.a - p).magnitude < 1e-3f || (w.b - p).magnitude < 1e-3f).ToList(); if (ends.Count == 2 && ends.All(w => w.openings.Count == 0)) { var o0 = (ends[0].a - p).magnitude < 1e-3f ? ends[0].b : ends[0].a; var o1 = (ends[1].a - p).magnitude < 1e-3f ? ends[1].b : ends[1].a; ends[0].a = o0; ends[0].b = o1; fl.walls.Remove(ends[1]); }
        }
        /// <summary>A room from an outline: its floor and a wall along each edge.</summary>
        void ShapedRoom(List<Vector2> pts, bool walls)
        {
            if (pts.Count < 3) return; var copy = new List<Vector2>(pts);
            Change("Add a room", () => { var f = m_B.FloorAt(m_Level, true); var a = new Building.Area(); a.SetShape(copy); f.areas.Add(a); if (walls) for (int i = 0; i < copy.Count; i++) AddWallLine(f, copy[i], copy[(i + 1) % copy.Count]); });
        }

        // ------------------------------------------------------------------ shuffle: the rooms put together another way

        /// <summary>Takes the rectangle rooms of a floor (with what's in them) and puts them back together at random, each one
        /// against another with a doorway between them. Ctrl+Z puts them back.</summary>
        public void Shuffle(Building.Floor fl, int seed)
        {
            var rooms = fl.areas.Where(a => !a.hole && !a.IsShape && Mathf.Abs(a.lift) < 0.01f).ToList(); if (rooms.Count < 2) { ShowNotification(new GUIContent("Needs two rooms or more")); return; }
            Change("Shuffle the rooms", () =>
            {
                var rnd = new System.Random(seed); var saved = rooms.Select(a => SaveRoomTemp(fl, a)).ToList(); var start = Vector2.Min(rooms[0].min, rooms[0].max);
                foreach (var a in rooms) { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); bool In(Vector2 p) => Inside(p, lo, hi); fl.walls.RemoveAll(w => In(w.a) && In(w.b)); fl.stairs.RemoveAll(s => In(s.at)); fl.pieces.RemoveAll(p => In(p.at)); fl.columns.RemoveAll(c => In(c.at)); fl.areas.Remove(a); }
                var placed = new List<Rect>(); var order = saved.OrderBy(_ => rnd.Next()).ToList();
                foreach (var r in order)
                {
                    int q = rnd.Next(4); var cs = new[] { Vector2.zero, new Vector2(r.size.x, 0), r.size, new Vector2(0, r.size.y) }.Select(v => Turn(v, q)).ToArray(); var mn = cs.Aggregate(Vector2.Min); var sz = cs.Aggregate(Vector2.Max) - mn; Vector2 at = start; bool ok = placed.Count == 0;
                    for (int tries = 0; tries < 80 && !ok; tries++)
                    {
                        var other = placed[rnd.Next(placed.Count)]; int side = rnd.Next(4); float c = m_B.cell; Vector2 lo;
                        if (side < 2) { float y = Mathf.Round(((float)rnd.NextDouble() * (other.height + sz.y - 2 * c) + other.yMin - sz.y + c) / c) * c; lo = new Vector2(side == 0 ? other.xMax : other.xMin - sz.x, y); }
                        else { float x = Mathf.Round(((float)rnd.NextDouble() * (other.width + sz.x - 2 * c) + other.xMin - sz.x + c) / c) * c; lo = new Vector2(x, side == 2 ? other.yMax : other.yMin - sz.y); }
                        var box = new Rect(lo, sz); if (placed.Any(p => p.Overlaps(new Rect(box.x + 0.01f, box.y + 0.01f, box.width - 0.02f, box.height - 0.02f)))) continue; at = lo; ok = true;
                    }
                    if (!ok) { var right = placed.Max(p => p.xMax); at = new Vector2(right, start.y); }
                    var bx = new Rect(at, sz); Stamp(fl, r, at - mn, q);
                    // a doorway to a room it touches
                    foreach (var p in placed) { var ov = Touch(bx, p); if (ov == null) continue; var (a0, a1) = ov.Value; if ((a1 - a0).magnitude < 1.2f) continue; var mid = (a0 + a1) / 2; var w = fl.walls.FirstOrDefault(x => BuildingMaker.Dist(mid, x) < 1e-3f); if (w == null) continue; float along = Vector2.Dot(mid - w.a, (w.b - w.a).normalized); if (w.openings.Any(o => Mathf.Abs(o.at - along) < o.width / 2 + 0.6f)) break; w.openings.Add(new Building.Opening { kind = Building.OpeningKind.Gap, at = along, width = 1 }); break; }
                    placed.Add(bx);
                }
                foreach (var r in saved) DestroyImmediate(r); Join(fl);
            });
        }
        /// <summary>The shared edge of two rectangles side by side, if they touch along one.</summary>
        static (Vector2, Vector2)? Touch(Rect a, Rect b)
        {
            float e = 1e-3f;
            if (Mathf.Abs(a.xMax - b.xMin) < e || Mathf.Abs(a.xMin - b.xMax) < e) { float x = Mathf.Abs(a.xMax - b.xMin) < e ? a.xMax : a.xMin; float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax); if (y1 - y0 > e) return (new Vector2(x, y0), new Vector2(x, y1)); }
            if (Mathf.Abs(a.yMax - b.yMin) < e || Mathf.Abs(a.yMin - b.yMax) < e) { float y = Mathf.Abs(a.yMax - b.yMin) < e ? a.yMax : a.yMin; float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax); if (x1 - x0 > e) return (new Vector2(x0, y), new Vector2(x1, y)); }
            return null;
        }

        // ------------------------------------------------------------------ the floor plan: shown, copied, all floors in one

        /// <summary>Every floor's plan side by side, each with its name over it.</summary>
        public Texture2D AllPlans(string folder = "Assets/Buildings/Plans")
        {
            var plans = m_B.floors.OrderBy(f => f.level).Select(f => (f, t: FloorPlan(f.level, folder))).Where(q => q.t).ToList(); if (plans.Count == 0) return null;
            int pad = 24, title = 46, W = plans.Sum(q => q.t.width) + pad * (plans.Count + 1), Hh = plans.Max(q => q.t.height) + title + pad; var px = new Pixels(W, Hh); for (int i = 0; i < px.c.Length; i++) px.c[i] = Color.white; int x = pad;
            foreach (var (f, t) in plans)
            {
                var src = Pixels.Read(t); for (int y = 0; y < src.h; y++) for (int k = 0; k < src.w; k++) px.c[(y + pad) * W + x + k] = src.c[y * src.w + k];
                var txt = new TextArt { text = f.name.Length > 0 ? f.name : Building.LevelName(f.level) }.Draw(); if (txt != null) TextureStamp.Put(px, txt, true, new Color(0.15f, 0.15f, 0.2f), new Vector2((x + src.w / 2f) / W, 1 - (title / 2f) / Hh), Mathf.Min(0.3f, src.w * 0.5f / W), 0, false, 1, TextureStamp.How.Paint, 0, 0, 0.5f, 0.2f);
                x += src.w + pad;
            }
            Folder(folder); var path = folder + "/" + Clean(m_B.name) + " all floors.png"; return Pixels.SaveAt(px, path, null, false, false);
        }
        /// <summary>Puts a picture on the clipboard (Windows and macOS), to paste anywhere.</summary>
        public static bool CopyPicture(string assetPath)
        {
            var full = Path.GetFullPath(assetPath);
            try
            {
                if (Application.platform == RuntimePlatform.WindowsEditor) { var ps = "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; [System.Windows.Forms.Clipboard]::SetImage([System.Drawing.Image]::FromFile('" + full.Replace("'", "''") + "'))"; var pi = new System.Diagnostics.ProcessStartInfo("powershell", "-NoProfile -STA -Command \"" + ps + "\"") { CreateNoWindow = true, UseShellExecute = false }; System.Diagnostics.Process.Start(pi); return true; }
                if (Application.platform == RuntimePlatform.OSXEditor) { var pi = new System.Diagnostics.ProcessStartInfo("osascript", "-e 'set the clipboard to (read (POSIX file \"" + full + "\") as «class PNGf»)'") { CreateNoWindow = true, UseShellExecute = false }; System.Diagnostics.Process.Start(pi); return true; }
            }
            catch (Exception e) { Debug.LogWarning("Building Builder: couldn't copy the picture: " + e.Message); }
            return false;
        }
        void ShowPlan(Texture2D t) { if (!t) return; PlanWindow.Show(t); if (CopyPicture(AssetDatabase.GetAssetPath(t))) ShowNotification(new GUIContent("Copied: paste it anywhere")); }

        // ------------------------------------------------------------------ finishing: the plan taken off, the meshes kept

        /// <summary>Removes the Building (and the builder's markers) from it, keeping what it made. Ctrl+Z brings them back.</summary>
        public void Finish()
        {
            if (!m_B) return; if (!EditorUtility.DisplayDialog("Finish the building", "This takes the Building Builder's plan off " + m_B.name + ". What it made stays (meshes, colliders, doors), but you can't change it with the builder any more (Ctrl+Z brings the plan back).\n\nTip: save it as a prefab first if you might want to change it later.", "Finish it", "Cancel")) return;
            var b = m_B; Undo.SetCurrentGroupName("Finish the building"); foreach (var m in b.GetComponentsInChildren<BuildingMade>(true)) Undo.DestroyObjectImmediate(m); Undo.DestroyObjectImmediate(b); m_B = null; Repaint();
        }
    }

    /// <summary>A floor plan picture, shown big, with a button to copy it again.</summary>
    class PlanWindow : EditorWindow
    {
        Texture2D m_T;
        public static void Show(Texture2D t) { var w = GetWindow<PlanWindow>(true, "Floor plan"); w.m_T = t; w.minSize = new Vector2(300, 200); w.Show(); }
        void OnGUI()
        {
            if (!m_T) { GUILayout.Label("No plan."); return; } GUILayout.BeginHorizontal(); GUILayout.Label(AssetDatabase.GetAssetPath(m_T), EditorStyles.miniLabel); GUILayout.FlexibleSpace(); if (GUILayout.Button("Copy again", EditorStyles.miniButton)) BuildingWindow.CopyPicture(AssetDatabase.GetAssetPath(m_T)); if (GUILayout.Button("Show in the project", EditorStyles.miniButton)) EditorGUIUtility.PingObject(m_T); GUILayout.EndHorizontal();
            var r = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)); EditorGUI.DrawRect(r, Color.white); GUI.DrawTexture(r, m_T, ScaleMode.ScaleToFit);
        }
    }
}
