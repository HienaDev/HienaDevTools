using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The view's helpers: cancelling a drag, seeing what you're about to put, move/turn/scale handles, a right-click
    /// menu, the floor buttons, the keys sheet, lighting, hiding the roof and outside walls, and lines kept inside the view.</summary>
    public partial class BuildingWindow
    {
        [SerializeField] bool m_HideRoof, m_HideOuter, m_AnyFloor, m_ShowKeys, m_PersonPanel; [SerializeField] int m_Light; [SerializeField] float m_LW = 280, m_RW = 300;
        static readonly string[] Lights = { "Day", "Evening", "Night", "Grey sky", "Studio" };

        // ------------------------------------------------------------------ lines and shapes stay inside the view

        bool ClipSeg(ref Vector2 a, ref Vector2 b)
        {
            if (m_V == null || m_V.scene) return true; var r = m_V.rect; float t0 = 0, t1 = 1; var d = b - a;
            foreach (var (p, q) in new[] { (-d.x, a.x - r.xMin), (d.x, r.xMax - a.x), (-d.y, a.y - r.yMin), (d.y, r.yMax - a.y) })
            {
                if (Mathf.Abs(p) < 1e-6f) { if (q < 0) return false; continue; } float k = q / p; if (p < 0) { if (k > t1) return false; if (k > t0) t0 = k; } else { if (k < t0) return false; if (k < t1) t1 = k; }
            }
            var a0 = a; a = a0 + d * t0; b = a0 + d * t1; return true;
        }
        List<Vector3> ClipPoly(List<Vector3> g)
        {
            if (m_V == null || m_V.scene) return g; var r = m_V.rect; var l = g;
            foreach (var (axis, val, keepBelow) in new[] { (0, r.xMin, false), (0, r.xMax, true), (1, r.yMin, false), (1, r.yMax, true) })
            {
                var o = new List<Vector3>(); for (int i = 0; i < l.Count; i++) { var a = l[i]; var b = l[(i + 1) % l.Count]; bool ia = keepBelow ? a[axis] <= val : a[axis] >= val, ib = keepBelow ? b[axis] <= val : b[axis] >= val; if (ia) o.Add(a); if (ia != ib) { float k = (val - a[axis]) / (b[axis] - a[axis]); o.Add(Vector3.Lerp(a, b, k)); } }
                l = o; if (l.Count == 0) break;
            }
            return l;
        }
        bool InView(Vector2 g) => m_V == null || m_V.scene || m_V.rect.Contains(g);

        // ------------------------------------------------------------------ Esc or a right click while dragging: stop, and put things back

        bool Busy => m_Drag != Grab.None || m_OpWall >= 0 || m_PolyPts.Count > 0;
        void CancelDrag()
        {
            bool changed = m_Drag == Grab.Move || m_Drag == Grab.Handle || m_Drag == Grab.Gizmo; m_Drag = Grab.None; m_OpWall = -1; m_Path.Clear(); m_PolyPts.Clear(); if (GUIUtility.hotControl != 0) GUIUtility.hotControl = 0;
            if (changed) { Undo.RevertAllDownToGroup(m_UndoGroup); if (!Valid(m_Sel)) m_Sel = default; Remake(); }
            ClearGhost(); m_Status = "cancelled"; Repaint(); SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ what you're about to put, shown as it will be

        readonly List<(Mesh mesh, Material[] mats)> m_Ghost = new List<(Mesh, Material[])>(); string m_GhostKey; bool m_GhostWanted;
        void SetGhost(string key, Func<Building.Floor> make)
        {
            m_GhostWanted = true; if (key == m_GhostKey) return; ClearGhost(); m_GhostKey = key; var part = make(); if (part == null) return;
            try { m_Ghost.AddRange(BuildingMaker.Ghost(m_B, part, m_B.FloorAt(m_Level))); } catch (Exception ex) { Debug.LogWarning("Building Builder: couldn't show the preview: " + ex.Message); }
            Repaint();
        }
        void ClearGhost() { foreach (var g in m_Ghost) if (g.mesh) DestroyImmediate(g.mesh); m_Ghost.Clear(); m_GhostKey = null; }
        Building.Floor Part() => new Building.Floor { level = m_Level, height = m_B.FloorAt(m_Level)?.height ?? 0 };
        void DrawGhost(PreviewRenderUtility pru) { if (!m_B) return; foreach (var (mesh, mats) in m_Ghost) for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++) if (mats[s]) pru.DrawMesh(mesh, Tr.localToWorldMatrix, mats[s], s); }
        void DrawGhostScene() { if (!m_B || Event.current.type != EventType.Repaint || !Camera.current) return; foreach (var (mesh, mats) in m_Ghost) for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++) if (mats[s]) Graphics.DrawMesh(mesh, Tr.localToWorldMatrix, mats[s], 0, Camera.current, s); }

        // ------------------------------------------------------------------ handles to move, turn and scale what's picked (Ctrl: no snapping)

        int m_Axis; Vector2 m_GizmoFrom; float m_GizmoDone;
        bool GizmoAt(Sel s, out Vector3 c, out bool canY, out bool canTurn, out bool canScale)
        {
            c = default; canY = canTurn = canScale = false; if (!Valid(s) || s.kind == SelKind.Opening) return false; var fl = m_B.FloorAt(s.level); float y0 = m_B.Y(s.level);
            switch (s.kind)
            {
                case SelKind.Wall: { var w = fl.walls[s.i]; c = L((w.a + w.b) / 2, y0 + 0.05f); canTurn = true; return true; }
                case SelKind.Area: { var a = fl.areas[s.i]; var cs = a.Corners(); c = L(cs.Aggregate(Vector2.zero, (q, v) => q + v) / cs.Count, y0 + a.lift + 0.05f); canY = true; return true; }
                case SelKind.Stair: { var st = fl.stairs[s.i]; c = L(st.at, y0 + st.lift + 0.05f); canY = canTurn = true; return true; }
                case SelKind.Piece: { var p = fl.pieces[s.i]; c = L(p.at, y0 + p.lift + 0.05f); canY = canTurn = canScale = true; return true; }
                case SelKind.Column: { var cl = fl.columns[s.i]; c = L(cl.at, y0 + cl.lift + 0.05f); canY = canScale = true; return true; }
                case SelKind.Beam: { var bm = fl.beams[s.i]; c = L((bm.a + bm.b) / 2, y0 + m_B.HeightOf(s.level) - m_B.slabThickness - bm.depth); return true; }
            }
            return false;
        }
        const float Ring = 1.4f;
        float GizmoLen(Vector3 c) { var w = Tr.TransformPoint(c); if (m_V.scene) return HandleUtility.GetHandleSize(w) * 1.1f; return Mathf.Max(0.3f, (m_V.cam.transform.position - w).magnitude * 0.11f); }
        static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] AxisCol = { new Color(1f, 0.35f, 0.3f), new Color(0.45f, 1f, 0.4f), new Color(0.35f, 0.6f, 1f) };
        /// <summary>Draws the handles, and starts or carries on a drag on one. True when it used the event.</summary>
        bool Gizmo(Event e, int id)
        {
            if (!GizmoAt(m_Sel, out var c, out bool canY, out bool canTurn, out bool canScale)) return false; float len = GizmoLen(c);
            if (!m_V.Gui(Tr.TransformPoint(c), out var g0)) return false;
            Vector2 G(Vector3 p) { m_V.Gui(Tr.TransformPoint(p), out var g); return g; }
            int Under(Vector2 m)
            {
                // the nearest handle within reach: the arrows, the scale square, the ring (it sits outside the arrows)
                int best = -1; float bd = 8;
                for (int k = 0; k < 3; k++) { if (k == 1 && !canY) continue; var ge = G(c + Axes[k] * len); float d = DistSeg(m, g0, ge); if (d < bd) { bd = d; best = k; } }
                if (canScale) { var gs = G(c + (Vector3.right + Vector3.forward).normalized * len * 0.55f); float d = (m - gs).magnitude - 2; if (d < bd) { bd = d; best = 4; } }
                if (canTurn) for (int k = 0; k < 32; k++) { var a = G(c + Quaternion.Euler(0, k * 360 / 32f, 0) * Vector3.forward * len * Ring); var b2 = G(c + Quaternion.Euler(0, (k + 1) * 360 / 32f, 0) * Vector3.forward * len * Ring); float d = DistSeg(m, a, b2); if (d < bd) { bd = d; best = 3; } }
                return best;
            }
            if (e.type == EventType.Repaint)
            {
                int hot = m_Drag == Grab.Gizmo ? m_Axis : Under(e.mousePosition);
                for (int k = 0; k < 3; k++) { if (k == 1 && !canY) continue; var col = hot == k ? Color.yellow : AxisCol[k]; Line(c, c + Axes[k] * len, col, hot == k ? 5 : 3.5f); Dot(c + Axes[k] * len, col, hot == k ? 7 : 5); }
                if (canTurn) { var ring = Enumerable.Range(0, 32).Select(k => c + Quaternion.Euler(0, k * 360 / 32f, 0) * Vector3.forward * len * Ring).ToArray(); var col = hot == 3 ? Color.yellow : new Color(1f, 0.85f, 0.3f, 0.8f); for (int k = 0; k < 32; k++) Line(ring[k], ring[(k + 1) % 32], col, hot == 3 ? 4 : 2); }
                if (canScale) { var sp = c + (Vector3.right + Vector3.forward).normalized * len * 0.55f; Dot(sp, hot == 4 ? Color.yellow : Color.white, hot == 4 ? 7 : 5); }
                Dot(c, Color.white, 3);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                int k = Under(e.mousePosition); if (k < 0) return false; m_Axis = k; m_Drag = Grab.Gizmo; m_GizmoFrom = e.mousePosition; m_GizmoDone = 0; Begin(k == 3 ? "Turn" : k == 4 ? "Scale" : "Move"); GUIUtility.hotControl = id; e.Use(); return true;
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && m_Drag == Grab.Gizmo)
            {
                bool free = e.control || e.command; var fl = m_B.FloorAt(m_Sel.level); var s = m_Sel;
                if (m_Axis <= 2)
                {
                    var ge = G(c + Axes[m_Axis] * len); var sd = ge - g0; if (sd.sqrMagnitude < 4) { e.Use(); return true; }
                    float total = Vector2.Dot(e.mousePosition - m_GizmoFrom, sd.normalized) * len / sd.magnitude; float step = m_Axis == 1 ? 0.05f : SnapStep; if (!free) total = Mathf.Round(total / step) * step; float d = total - m_GizmoDone;
                    if (Mathf.Abs(d) > 1e-5f) { m_GizmoDone = total; Step(() => { if (m_Axis == 1) Lift(s, d); else MoveBy(s, m_Axis == 0 ? new Vector2(d, 0) : new Vector2(0, d)); }); }
                }
                else if (m_Axis == 3)
                {
                    float total = Vector2.SignedAngle(m_GizmoFrom - g0, e.mousePosition - g0); if (!free) total = Mathf.Round(total / 15f) * 15f; float d = total - m_GizmoDone;
                    if (Mathf.Abs(d) > 1e-3f) { m_GizmoDone = total; Step(() => Spin(s, d)); }
                }
                else { float total = Mathf.Exp(((e.mousePosition.x - m_GizmoFrom.x) - (e.mousePosition.y - m_GizmoFrom.y)) / 150f); if (!free) total = Mathf.Max(0.1f, Mathf.Round(total * 10) / 10); float prev = m_GizmoDone <= 0 ? 1 : m_GizmoDone; if (Mathf.Abs(total - prev) > 1e-4f) { m_GizmoDone = total; float f = total / prev; Step(() => Grow(s, f)); } }
                e.Use(); return true;
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id && m_Drag == Grab.Gizmo) { GUIUtility.hotControl = 0; m_Drag = Grab.None; End(); e.Use(); return true; }
            return false;
        }
        void Lift(Sel s, float d)
        {
            var fl = m_B.FloorAt(s.level);
            switch (s.kind) { case SelKind.Stair: fl.stairs[s.i].lift = Mathf.Max(0, fl.stairs[s.i].lift + d); break; case SelKind.Piece: fl.pieces[s.i].lift += d; break; case SelKind.Column: fl.columns[s.i].lift = Mathf.Max(0, fl.columns[s.i].lift + d); break; case SelKind.Area: fl.areas[s.i].lift += d; break; }
        }
        void Spin(Sel s, float deg)
        {
            var fl = m_B.FloorAt(s.level);
            switch (s.kind)
            {
                case SelKind.Stair: fl.stairs[s.i].turn = Mathf.Repeat(fl.stairs[s.i].turn + deg, 360); break; case SelKind.Piece: fl.pieces[s.i].turn = Mathf.Repeat(fl.pieces[s.i].turn + deg, 360); break;
                case SelKind.Wall: { var w = fl.walls[s.i]; var c = (w.a + w.b) / 2; var q = Quaternion.Euler(0, 0, -deg); w.a = c + (Vector2)(q * (w.a - c)); w.b = c + (Vector2)(q * (w.b - c)); break; }
            }
        }
        void Grow(Sel s, float f)
        {
            var fl = m_B.FloorAt(s.level);
            switch (s.kind) { case SelKind.Piece: fl.pieces[s.i].scale *= f; break; case SelKind.Column: fl.columns[s.i].size = Mathf.Max(0.05f, fl.columns[s.i].size * f); break; }
        }

        // ------------------------------------------------------------------ a right click on something: quick changes

        void Context(Vector2 mouse)
        {
            var s = PickAt(mouse, out float along); var fl = m_B.FloorAt(m_Level); var menu = new GenericMenu(); if (s.kind != SelKind.None) { m_Sel = s; m_Tab = 0; }
            void Item(string path, bool on, Action a) => menu.AddItem(new GUIContent(path), on, () => Change(path.Replace("/", ": "), a));
            switch (s.kind)
            {
                case SelKind.Wall:
                {
                    var w = fl.walls[s.i]; float len = w.Length; float at = Mathf.Clamp(Mathf.Round(along / SnapStep) * SnapStep, 0.6f, Mathf.Max(0.6f, len - 0.6f));
                    Item("Put a door here", false, () => w.openings.Add(new Building.Opening { kind = Building.OpeningKind.Door, at = at, width = m_DoorW, top = m_DoorH, fill = m_DoorFill, frame = m_DoorFrame }));
                    Item("Put a window here", false, () => w.openings.Add(new Building.Opening { kind = Building.OpeningKind.Window, at = at, width = m_WinW, bottom = m_WinBottom, top = m_WinTop }));
                    Item("Put a doorway (no door) here", false, () => w.openings.Add(new Building.Opening { kind = Building.OpeningKind.Gap, at = at, width = 1 }));
                    menu.AddSeparator(""); Item(Mathf.Abs(w.bend) > 0.01f ? "Straighten it" : "Curve it", false, () => w.bend = Mathf.Abs(w.bend) > 0.01f ? 0 : Mathf.Min(1.5f, len * 0.2f)); Item("Curve it the other way", false, () => w.bend = -(Mathf.Abs(w.bend) > 0.01f ? w.bend : Mathf.Min(1.5f, len * 0.2f)));
                    Item("Skirting on both sides", (w.frontFinish & Building.Finish.Skirting) != 0, () => { w.frontFinish ^= Building.Finish.Skirting; w.backFinish = w.frontFinish; });
                    Item("Half height", w.height > 0, () => w.height = w.height > 0 ? -1 : (m_B.HeightOf(m_Level) - m_B.slabThickness) / 2);
                    Item("Split it here", false, () => SplitWall(fl, w, along)); break;
                }
                case SelKind.Opening:
                {
                    var o = fl.walls[s.i].openings[s.j]; foreach (Building.OpeningKind k in Enum.GetValues(typeof(Building.OpeningKind))) { var kk = k; Item("Kind/" + (k == Building.OpeningKind.Gap ? "Doorway (no door)" : k.ToString()), o.kind == k, () => o.kind = kk); }
                    if (o.kind == Building.OpeningKind.Door) { Item("Double door", o.doubleDoor, () => o.doubleDoor = !o.doubleDoor); Item("Hinged on the other side", o.hingeOther, () => o.hingeOther = !o.hingeOther); foreach (Building.DoorSwing w in Enum.GetValues(typeof(Building.DoorSwing))) { var ww = w; Item("Swings/" + SwingName(w), o.swing == w, () => o.swing = ww); } Item("With a door", o.fill, () => o.fill = !o.fill); }
                    if (o.kind == Building.OpeningKind.Window || o.kind == Building.OpeningKind.Arch) Item("With glass", o.fill, () => o.fill = !o.fill);
                    Item("With a frame", o.frame, () => o.frame = !o.frame); Item("Centre it on the wall", false, () => o.at = fl.walls[s.i].Length / 2); break;
                }
                case SelKind.Stair:
                {
                    var st = fl.stairs[s.i]; foreach (Building.StairKind k in Enum.GetValues(typeof(Building.StairKind))) { var kk = k; Item("Kind/" + k, st.kind == k, () => st.kind = kk); }
                    for (int i = 0; i < SideNames.Length; i++) { var v = (Building.RailSides)i; Item("Railings/" + SideNames[i], st.railSides == v, () => st.railSides = v); }
                    foreach (Building.StairUnder k in Enum.GetValues(typeof(Building.StairUnder))) { var kk = k; Item("Underneath/" + UnderName(k), st.under == k, () => st.under = kk); }
                    Item("Turn it 90°", false, () => st.turn = Mathf.Repeat(st.turn + 90, 360)); Item("Goes up a whole floor", Mathf.Approximately(st.rise, 1), () => st.rise = 1); Item("Goes up half a floor", Mathf.Approximately(st.rise, 0.5f), () => st.rise = 0.5f); break;
                }
                case SelKind.Area:
                {
                    var a = fl.areas[s.i]; Item("Is a hole", a.hole, () => a.hole = !a.hole);
                    if (a.hole) Item("Railing round it", a.rails, () => a.rails = !a.rails); else { Item("Ceiling (lid) on top", a.roof, () => a.roof = !a.roof); Item("Solid underneath (to the floor below)", a.solidBelow, () => a.solidBelow = !a.solidBelow); Item("Posts under it (a balcony)", a.posts, () => a.posts = !a.posts); Item("Raise it a quarter", false, () => a.lift += 0.25f); Item("Lower it a quarter", false, () => a.lift -= 0.25f); }
                    Item("Shape/Rectangle", !a.IsShape, () => { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); a.shape.Clear(); a.min = lo; a.max = hi; }); foreach (int n in new[] { 6, 8, 12 }) { int nn = n; Item("Shape/Round (" + n + " sides)", false, () => a.SetShape(BuildingMaker.Round(Vector2.Min(a.min, a.max), Vector2.Max(a.min, a.max), nn))); } Item("Shape/Broken (a new one each time)", false, () => a.SetShape(BuildingMaker.Broken(Vector2.Min(a.min, a.max), Vector2.Max(a.min, a.max), UnityEngine.Random.Range(1, 99999))));
                    if (fl.areas.Any(q => q.roof)) Item("Take every tunnel ceiling on this floor off", false, () => fl.areas.ForEach(q => q.roof = false)); break;
                }
                case SelKind.Piece: { var p = fl.pieces[s.i]; Item("Turn it 90°", false, () => p.turn = Mathf.Repeat(p.turn + 90, 360)); Item("Bigger", false, () => p.scale *= 1.1f); Item("Smaller", false, () => p.scale /= 1.1f); break; }
                case SelKind.Column: { var c = fl.columns[s.i]; Item("Round", c.sides > 0, () => c.sides = c.sides > 0 ? 0 : 8); break; }
            }
            if (s.kind != SelKind.None) { menu.AddSeparator(""); menu.AddItem(new GUIContent("Copy (Ctrl+D)"), false, Duplicate); menu.AddItem(new GUIContent("Delete (Del)"), false, DeleteSelected); }
            menu.AddSeparator(""); foreach (var f in m_B.floors.OrderByDescending(q => q.level)) { int lv = f.level; menu.AddItem(new GUIContent("Go to floor/" + (f.name.Length > 0 ? f.name : Building.LevelName(f.level))), lv == m_Level, () => { m_Level = lv; m_Sel = default; Floors(); Repaint(); }); }
            menu.AddItem(new GUIContent("View/Hide the roof"), m_HideRoof, () => { m_HideRoof = !m_HideRoof; Floors(); }); menu.AddItem(new GUIContent("View/Hide the outside walls"), m_HideOuter, () => { m_HideOuter = !m_HideOuter; Floors(); });
            menu.ShowAsContext();
        }
        static string SwingName(Building.DoorSwing w) => w == Building.DoorSwing.BothWays ? "Both ways (away from you)" : w == Building.DoorSwing.ToTheFront ? "To the front only" : "To the back only";
        static string UnderName(Building.StairUnder k) => k == Building.StairUnder.Solid ? "Solid" : k == Building.StairUnder.Open ? "Open (treads only)" : "A cupboard with a door";
        void SplitWall(Building.Floor fl, Building.Wall wl, float at)
        {
            float len = wl.Length; if (at <= 0.05f || at >= len - 0.05f) return; var d = (wl.b - wl.a) / len; var mid = wl.a + d * at; var b2 = new Building.Wall { a = mid, b = wl.b, height = wl.height, bottom = wl.bottom, thickness = wl.thickness, front = wl.front, back = wl.back, frontFinish = wl.frontFinish, backFinish = wl.backFinish };
            foreach (var o in wl.openings.Where(o => o.at > at).ToList()) { wl.openings.Remove(o); o.at -= at; b2.openings.Add(o); } wl.b = mid; fl.walls.Add(b2);
        }

        // ------------------------------------------------------------------ over the view: floors, keys, the person

        void Overlays(Rect area, Event e)
        {
            if (!m_B) return;
            // the floors, top to bottom, to jump between them
            var floors = m_B.floors.OrderByDescending(f => f.level).ToList(); float y = area.y + 34;
            y = area.y + 8; foreach (var f in floors) { var r = new Rect(area.x + 8, y, 120, 20); bool on = f.level == m_Level; EditorGUI.DrawRect(r, on ? new Color(Accent.r, Accent.g, Accent.b, 0.85f) : new Color(0, 0, 0, r.Contains(e.mousePosition) ? 0.7f : 0.45f)); GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 8, 16), f.name.Length > 0 ? f.name : Building.LevelName(f.level), new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = on ? Color.black : Color.white } }); if (GUI.Button(r, new GUIContent("", "Work on this floor (PageUp / PageDown)"), GUIStyle.none)) { m_Level = f.level; m_Sel = default; Floors(); SceneView.RepaintAll(); } y += 22; }
            if (m_PersonPanel) PersonPanel(new Rect(area.xMax - 268, area.y + 34, 260, 112));
            if (m_ShowKeys) KeysSheet(area);
        }
        void PersonPanel(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.12f, 0.95f)); GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 10)); EditorGUIUtility.labelWidth = 90;
            GUILayout.Label("THE PERSON (walk mode and P)", m_Head); m_PersonHeight = EditorGUILayout.Slider(new GUIContent("Height", "Their eyes are just under this"), m_PersonHeight, 0.5f, 3f);
            var g = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Your player", "Shown when you press P; its height is used for walking"), m_Person, typeof(GameObject), false); if (g != m_Person) { m_Person = g; if (g) { var bb = LocalSize(g); if (bb.y > 0.3f) m_PersonHeight = bb.y; } }
            m_WalkSpeed = EditorGUILayout.Slider(new GUIContent("Walk speed", "Units a second (Shift runs twice as fast)"), m_WalkSpeed, 1f, 10f);
            EditorGUIUtility.labelWidth = 0; GUILayout.EndArea();
        }
        static Vector3 LocalSize(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return Vector3.zero; var o = (GameObject)PrefabUtility.InstantiatePrefab(g); var bb = BuildingMaker.Bounds(o); DestroyImmediate(o); return bb.size; }
        static readonly (string group, string[] keys)[] KeyList =
        {
            ("Tools", new[] { "Q  select", "1  room", "2  floor", "3  wall", "4  door", "5  window", "6  stairs", "7  hole", "8  tunnel", "9  parts", "0  paint", "C  column", "B  beam", "L  lift shaft", "U  balcony", "O  stamp a room" }),
            ("Sizes (what's picked, or what you're about to put)", new[] { "Alt + Right / Left  bigger / smaller", "Alt + Shift + Right / Left  its other size", "Alt + Up / Down  higher / lower", "K  its kind", "R  turn (Shift: 15°)", "( [ ]  - =  also work on US keyboards )" }),
            ("Picked things", new[] { "Drag  move it", "Arrows  nudge (the way you're looking)", "Ctrl+D  copy", "Delete  remove", "Handles: arrows move, ring turns, square scales", "Ctrl while dragging  no snapping", "Esc or right-click  cancel a drag" }),
            ("View", new[] { "Right-drag  turn", "Middle-drag  move", "Wheel  zoom", "F  frame it", "T  from above", "H  hide the floors above", "J  hide the roof", "N  hide the outside walls", "M  measurements", "P  a person here", "Tab  walk", "G  snap size", "PageUp / PageDown  floors", "Right-click  quick menu", "F1  this sheet" }),
        };
        void KeysSheet(Rect area)
        {
            var r = new Rect(area.x + area.width / 2 - 330, area.y + 34, 660, Mathf.Min(area.height - 40, 470)); EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.1f, 0.95f)); var head = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Accent } }; var st = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } };
            float x = r.x + 12, y = r.y + 10; int col = 0;
            foreach (var (group, keys) in KeyList) { if (col == 2) { x = r.x + 12 + 330; y = r.y + 10; } GUI.Label(new Rect(x, y, 320, 18), group, head); y += 18; foreach (var k in keys) { GUI.Label(new Rect(x, y, 320, 15), k, st); y += 14; } y += 8; col++; }
            GUI.Label(new Rect(r.x + 12, r.yMax - 20, 400, 16), "F1 or the Keys button closes this.", new GUIStyle(st) { normal = { textColor = new Color(1, 1, 1, 0.5f) } });
        }

        // ------------------------------------------------------------------ light in the view

        void ApplyLight(PreviewRenderUtility pru, Camera cam)
        {
            var l0 = pru.lights[0]; var l1 = pru.lights[1]; l0.transform.rotation = Quaternion.Euler(50, m_Yaw + 40, 0); l1.transform.rotation = Quaternion.Euler(330, m_Yaw + 210, 0);
            switch (m_Light)
            {
                default: l0.intensity = 1.1f; l0.color = Color.white; l1.intensity = 0.45f; l1.color = Color.white; pru.ambientColor = new Color(0.36f, 0.37f, 0.4f); cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f); break;
                case 1: l0.intensity = 1.0f; l0.color = new Color(1f, 0.62f, 0.38f); l0.transform.rotation = Quaternion.Euler(12, m_Yaw + 60, 0); l1.intensity = 0.3f; l1.color = new Color(0.5f, 0.55f, 0.9f); pru.ambientColor = new Color(0.3f, 0.25f, 0.3f); cam.backgroundColor = new Color(0.35f, 0.22f, 0.2f); break;
                case 2: l0.intensity = 0.35f; l0.color = new Color(0.55f, 0.65f, 1f); l0.transform.rotation = Quaternion.Euler(60, m_Yaw - 30, 0); l1.intensity = 0.1f; l1.color = new Color(0.4f, 0.45f, 0.8f); pru.ambientColor = new Color(0.08f, 0.09f, 0.14f); cam.backgroundColor = new Color(0.03f, 0.04f, 0.07f); break;
                case 3: l0.intensity = 0.55f; l0.color = new Color(0.9f, 0.92f, 0.95f); l0.transform.rotation = Quaternion.Euler(80, m_Yaw, 0); l1.intensity = 0.35f; l1.color = Color.white; pru.ambientColor = new Color(0.5f, 0.52f, 0.55f); cam.backgroundColor = new Color(0.55f, 0.57f, 0.6f); break;
                case 4: l0.intensity = 0.9f; l0.color = Color.white; l0.transform.rotation = Quaternion.Euler(35, m_Yaw + 20, 0); l1.intensity = 0.7f; l1.color = new Color(0.9f, 0.95f, 1f); pru.ambientColor = new Color(0.45f, 0.45f, 0.47f); cam.backgroundColor = new Color(0.22f, 0.22f, 0.24f); break;
            }
        }
        /// <summary>Whether the view (and the Scene view) leaves this out: the roof, or the outside walls with their doors and glass.</summary>
        bool Hidden(Component c) => m_HideRoof && (c.name == "Roof" || c.name == "Roof glass") || m_HideOuter && (c.name == "Outer walls" || c.name == "Glass (outside)" || c.GetComponentInParent<BuildingDoor>() && c.GetComponentInParent<BuildingDoor>().name == "Door (outside)");

        // ------------------------------------------------------------------ the panels on the sides can be made wider

        int m_Resizing;
        void Splitters(Rect left, Rect right)
        {
            var e = Event.current; var a = new Rect(left.xMax, left.y, 6, left.height); var b = new Rect(right.x - 6, right.y, 6, right.height); EditorGUIUtility.AddCursorRect(a, MouseCursor.ResizeHorizontal); EditorGUIUtility.AddCursorRect(b, MouseCursor.ResizeHorizontal);
            if (e.type == EventType.MouseDown && e.button == 0) { if (a.Contains(e.mousePosition)) { m_Resizing = 1; e.Use(); } else if (b.Contains(e.mousePosition)) { m_Resizing = 2; e.Use(); } }
            if (e.type == EventType.MouseDrag && m_Resizing > 0) { if (m_Resizing == 1) m_LW = Mathf.Clamp(e.mousePosition.x - 6, 220, Mathf.Min(640, position.width - m_RW - 300)); else m_RW = Mathf.Clamp(position.width - e.mousePosition.x - 6, 240, Mathf.Min(700, position.width - m_LW - 300)); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && m_Resizing > 0) { m_Resizing = 0; e.Use(); }
        }
    }
}
