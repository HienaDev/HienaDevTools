using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Build floor by floor, in this window's own 3D view (or in the Scene view): rooms, walls, doors and windows of
    /// any size anywhere along a wall, stairs of every kind, holes, tunnels and your own parts; click anything to change it.
    /// Tools > Building Builder.</summary>
    public partial class BuildingWindow : EditorWindow
    {
        public enum Tool { Select, Room, Floor, Wall, Door, Window, Stairs, Hole, Tunnel, Props, Paint, Column, Beam, Shaft, Balcony, Stamp }
        public static readonly (Tool tool, string key, string glyph, string name, string tip)[] Tools =
        {
            (Tool.Select, "Q", "↖", "Select", "Click anything to change it on the right. Drag it to move it, drag the round handles to stretch it. Delete removes, Ctrl+D copies, arrows nudge"),
            (Tool.Room, "1", "▣", "Room", "Drag a rectangle: a floor with walls all round"), (Tool.Floor, "2", "▢", "Floor", "Drag a rectangle of floor"), (Tool.Wall, "3", "┃", "Wall", "Drag a wall: it keeps to straight lines and 45° (Alt: any angle). Ctrl-drag takes walls away"),
            (Tool.Door, "4", "▯", "Door", "Click on a wall to put a door there (its size is below). Ctrl-click removes one"), (Tool.Window, "5", "▤", "Window", "Click on a wall to put a window there (its size is below). Ctrl-click removes one"),
            (Tool.Stairs, "6", "⩨", "Stairs", "Click where they start, R turns them. Straight, turning, U-turn or a ramp: pick below"), (Tool.Hole, "7", "◌", "Hole", "Drag a rectangle to cut a hole in the floor"),
            (Tool.Tunnel, "8", "∩", "Tunnel", "Drag a path: floor, walls and a ceiling all the way. Best on a basement floor"), (Tool.Props, "9", "◆", "Parts", "Pick one on the right, click to put it. R turns it"),
            (Tool.Paint, "0", "\u270E", "Paint", "Pick a material on the right, click a floor, wall side, ceiling, stairs or part. Shift: both sides of a wall. Ctrl: the whole room. Alt-click picks the material it has"),
            (Tool.Column, "C", "\u25AE", "Column", "Click to put a column (square or round, its size below). On a grid corner with the snap at 1"), (Tool.Beam, "B", "\u2501", "Beam", "Drag a beam under the ceiling"),
            (Tool.Shaft, "L", "\u25A9", "Lift shaft", "Drag a rectangle: a shaft with walls through the floors above (how many below)"), (Tool.Balcony, "U", "\u2293", "Balcony", "Drag a rectangle: floor with a railing round its open edges"),
            (Tool.Stamp, "O", "\u29C9", "Stamp a room", "Pick a saved room (Rooms, on the right), click to put it, R turns it"),
        };
        public static readonly Color Bg = new Color(0.13f, 0.135f, 0.15f), Panel = new Color(0.17f, 0.175f, 0.195f), PanelHi = new Color(0.22f, 0.235f, 0.28f), Accent = new Color(1f, 0.62f, 0.2f);
        static readonly float[] Snaps = { 1f, 0.5f, 0.25f, 0f };

        [SerializeField] Building m_B; [SerializeField] int m_Level; [SerializeField] Tool m_Tool = Tool.Room; [SerializeField] int m_Mat = 1; [SerializeField] string m_Part = ""; [SerializeField] float m_Turn; [SerializeField] int m_TunnelWide = 1; [SerializeField] bool m_HideAbove = true; [SerializeField] int m_Tab; [SerializeField] float m_Lift; [SerializeField] int m_Snap; [SerializeField] bool m_InScene;
        // what new doors, windows and stairs are like
        [SerializeField] Building.OpeningKind m_DoorKind = Building.OpeningKind.Door; [SerializeField] float m_DoorW = 1f, m_DoorH = 2.2f, m_WinW = 1.2f, m_WinBottom = 0.9f, m_WinTop = 2.1f; [SerializeField] bool m_WinArch; [SerializeField] bool m_DoorFill = true, m_DoorFrame = true, m_WinFill = true, m_WinFrame = true; [SerializeField] Building.RailSides m_StairRails;
        [SerializeField] Building.StairKind m_StairKind; [SerializeField] float m_StairW = 1f, m_StairL = 3f, m_StairRise = 1f;
        Vector2 m_LeftScroll, m_RightScroll; GUIStyle m_Head, m_Hint, m_Big, m_Small, m_Key;

        [MenuItem("Tools/Building Builder", false, 162)]
        public static void Open() { var w = GetWindow<BuildingWindow>("Building Builder"); w.minSize = new Vector2(900, 560); if (!w.m_B) w.m_B = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<Building>() : BbFind.Any<Building>(); }
        public static void Open(Building b) { Open(); var w = GetWindow<BuildingWindow>(); w.m_B = b; w.Frame(); w.Repaint(); }

        void OnEnable() { SceneView.duringSceneGui += Scene; Undo.undoRedoPerformed += AfterUndo; Selection.selectionChanged += Picked; EditorApplication.projectChanged += ProjectChanged; wantsMouseMove = true; }
        void OnDisable() { SceneView.duringSceneGui -= Scene; Undo.undoRedoPerformed -= AfterUndo; Selection.selectionChanged -= Picked; EditorApplication.projectChanged -= ProjectChanged; ShowAll(); ClearGhost(); m_Pru?.Cleanup(); m_Pru = null; }
        // (new materials from Texture Studio, new parts and rooms show up straight away)
        void ProjectChanged() { m_Studio = null; m_Parts = null; Repaint(); }
        void Picked() { var b = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<Building>() : null; if (b && b != m_B) { ShowAll(); m_B = b; m_Sel = default; Frame(); Repaint(); } }
        void AfterUndo() { if (m_B) BuildingMaker.Rebuild(m_B); if (!Valid(m_Sel)) m_Sel = default; Floors(); Repaint(); SceneView.RepaintAll(); }

        /// <summary>Changes the plan (one Undo step) and makes it again.</summary>
        public void Change(string what, Action a) { if (!m_B) return; Undo.RecordObject(m_B, "Building: " + what); a(); EditorUtility.SetDirty(m_B); Remake(); }
        public void Remake() { if (!m_B) return; BuildingMaker.Rebuild(m_B); Floors(); SceneView.RepaintAll(); Repaint(); }
        float SnapStep => !m_B ? 1 : m_Snap >= 3 ? 0.01f : m_B.cell * Snaps[Mathf.Clamp(m_Snap, 0, 2)];
        // (the floors above the one you work on are hidden in the Scene view only; nothing in the scene changes)
        void Floors()
        {
            if (!m_B) return; var v = SceneVisibilityManager.instance;
            foreach (var m in m_B.GetComponentsInChildren<BuildingMade>(true).Where(q => q.transform.parent == m_B.transform))
            {
                if (m_HideAbove && m.level > m_Level) { v.Hide(m.gameObject, true); continue; } v.Show(m.gameObject, true);
                foreach (Transform c in m.transform) if (Hidden(c)) v.Hide(c.gameObject, true);
            }
            SceneView.RepaintAll(); Repaint();
        }
        void ShowAll() { if (!m_B) return; var v = SceneVisibilityManager.instance; foreach (var m in m_B.GetComponentsInChildren<BuildingMade>(true)) if (m) v.Show(m.gameObject, true); }

        void Styles()
        {
            if (m_Head != null) return; m_Head = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1, 1, 1, 0.55f) } }; m_Hint = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = new Color(1, 1, 1, 0.55f) } };
            m_Big = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 18, normal = { textColor = Color.white } }; m_Small = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(1, 1, 1, 0.85f) } }; m_Key = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight, fontSize = 9, normal = { textColor = new Color(1, 1, 1, 0.5f) } };
        }
        bool Pill(string text, bool on, string tip = "", params GUILayoutOption[] o) { var c = GUI.backgroundColor; if (on) GUI.backgroundColor = Accent; bool r = GUILayout.Button(new GUIContent(text, tip), EditorStyles.miniButton, o); GUI.backgroundColor = c; return r; }
        static bool Num(ref float v, string label, float lo, float hi, string tip = "") { float n = EditorGUILayout.Slider(new GUIContent(label, tip), v, lo, hi); if (Mathf.Approximately(n, v)) return false; v = n; return true; }

        void OnGUI()
        {
            Styles(); var e = Event.current; EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), Bg);
            if (m_B && m_B.FloorAt(m_Level) == null && m_B.floors.Count > 0) m_Level = m_B.floors[0].level;
            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField && Keys(e)) { e.Use(); Repaint(); return; }
            if (e.type == EventType.KeyUp && m_Walk && WalkKey(e, false)) { e.Use(); return; }
            float lw = Mathf.Clamp(m_LW, 220, Mathf.Max(220, position.width - 560)), rw = Mathf.Clamp(m_RW, 240, Mathf.Max(240, position.width - lw - 320)); var left = new Rect(6, 6, lw, position.height - 12); var right = new Rect(position.width - rw - 6, 6, rw, position.height - 12); Splitters(left, right); var mid = new Rect(left.xMax + 6, 6, right.x - left.xMax - 12, position.height - 12);
            EditorGUI.DrawRect(left, Panel); EditorGUI.DrawRect(right, Panel);
            GUILayout.BeginArea(new Rect(left.x + 10, left.y + 8, left.width - 16, left.height - 12)); m_LeftScroll = GUILayout.BeginScrollView(m_LeftScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); LeftSide(left.width - 34); GUILayout.EndScrollView(); GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(right.x + 10, right.y + 8, right.width - 16, right.height - 12)); RightSide(right.width - 20); GUILayout.EndArea();
            Middle(mid);
        }

        void LeftSide(float w)
        {
            GUILayout.Label("Building Builder", new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, normal = { textColor = Color.white } }); GUILayout.Space(4);
            GUILayout.BeginHorizontal(); var b = (Building)EditorGUILayout.ObjectField(m_B, typeof(Building), true); if (b != m_B) { ShowAll(); m_B = b; m_Sel = default; Frame(); } if (GUILayout.Button(new GUIContent("New", "A new empty building"), GUILayout.Width(44))) NewBuilding(); GUILayout.EndHorizontal();
            if (!m_B) { GUILayout.Space(10); GUILayout.Label("Press New to start a building: it starts as one empty floor. Pick a tool and draw in the view on the right.", m_Hint); return; }
            GUILayout.Space(6); GUILayout.Label("FLOORS   (PageUp / PageDown)", m_Head);
            foreach (var f in m_B.floors.OrderByDescending(q => q.level))
            {
                var r = GUILayoutUtility.GetRect(w, 22); bool on = f.level == m_Level; EditorGUI.DrawRect(r, on ? new Color(Accent.r * 0.55f, Accent.g * 0.55f, Accent.b * 0.55f) : r.Contains(Event.current.mousePosition) ? PanelHi : new Color(1, 1, 1, 0.03f));
                GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 100, 18), f.name.Length > 0 ? f.name : Building.LevelName(f.level), new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white } }); GUI.Label(new Rect(r.xMax - 96, r.y + 3, 90, 16), m_B.HeightOf(f.level).ToString("0.##") + " tall", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1, 1, 1, 0.5f) } });
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { m_Level = f.level; m_Sel = default; Floors(); SceneView.RepaintAll(); }
            }
            var cf = m_B.FloorAt(m_Level); EditorGUIUtility.labelWidth = 100;
            if (cf != null) { float nh = m_B.HeightOf(m_Level); if (Num(ref nh, "Its height", 0.5f, 12f, "From its floor to the next one's. Taller for a hall, lower for an attic or a crawl space")) Change("Floor height", () => cf.height = nh); }
            EditorGUIUtility.labelWidth = 0;
            GUILayout.BeginHorizontal(); if (GUILayout.Button("+ Above", EditorStyles.miniButton)) { int top = m_B.floors.Count > 0 ? m_B.floors.Max(q => q.level) + 1 : 0; Change("Add a floor", () => m_B.FloorAt(top, true)); m_Level = top; Floors(); } if (GUILayout.Button("+ Basement", EditorStyles.miniButton)) { int bot = m_B.floors.Count > 0 ? m_B.floors.Min(q => q.level) - 1 : -1; Change("Add a basement", () => m_B.FloorAt(bot, true)); m_Level = bot; Floors(); } GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            if (cf != null && GUILayout.Button(new GUIContent("Copy up", "Makes the floor above the same: its floor, walls, doors and windows"), EditorStyles.miniButton)) Change("Copy a floor up", () => { var up = m_B.FloorAt(m_Level + 1, true); var c = JsonUtility.FromJson<Building.Floor>(JsonUtility.ToJson(cf)); up.areas = c.areas; up.walls = c.walls; foreach (var wl in up.walls) foreach (var o in wl.openings) if (o.kind == Building.OpeningKind.Door && m_Level == 0) { o.kind = Building.OpeningKind.Window; o.bottom = m_WinBottom; o.top = m_WinTop; } });
            if (cf != null && m_B.floors.Count > 1 && GUILayout.Button("Remove", EditorStyles.miniButton)) { Change("Remove a floor", () => m_B.floors.Remove(cf)); m_Level = m_B.floors[0].level; } GUILayout.EndHorizontal();
            bool hide = GUILayout.Toggle(m_HideAbove, new GUIContent(" Hide the floors above (H)", "So you can see in")); if (hide != m_HideAbove) { m_HideAbove = hide; Floors(); }
            if (cf != null)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label("Roof", GUILayout.Width(36)); var rk = (Building.RoofKind)EditorGUILayout.EnumPopup(cf.roof, GUILayout.MinWidth(60)); if (rk != cf.roof) Change("Roof", () => cf.roof = rk); GUILayout.EndHorizontal(); bool rails = GUILayout.Toggle(cf.rails, new GUIContent(" Railings where you could fall", "Open floor edges, holes, balconies")); if (rails != cf.rails) Change("Railings", () => cf.rails = rails);
                if (cf.roof >= Building.RoofKind.Gable)
                {
                    EditorGUIUtility.labelWidth = 100; float p = cf.roofPitch, o = cf.roofOverhang, aw = cf.atticWalls; bool g = cf.gutters, at = cf.attic, rw2 = cf.roofWindows;
                    if (Num(ref p, "Pitch", 5, 70) | Num(ref o, "Overhang", 0, 2) | g != (g = EditorGUILayout.Toggle("Gutters", g)) | at != (at = EditorGUILayout.Toggle(new GUIContent("Attic", "A floor inside the roof, on low walls: stairs from this floor going up a whole floor reach it"), at)) | (at && Num(ref aw, "Its low walls", 0, 2.5f)) | rw2 != (rw2 = EditorGUILayout.Toggle(new GUIContent("Gable windows", "A window in each gable end (always on with an attic)"), rw2)))
                        Change("Roof", () => { cf.roofPitch = p; cf.roofOverhang = o; cf.gutters = g; cf.attic = at; cf.atticWalls = aw; cf.roofWindows = rw2; });
                    EditorGUIUtility.labelWidth = 0;
                }
                else if (cf.roof != Building.RoofKind.None) { EditorGUIUtility.labelWidth = 100; float o = cf.roofOverhang; if (Num(ref o, "Overhang", 0, 2)) Change("Roof", () => cf.roofOverhang = o); EditorGUIUtility.labelWidth = 0; }
                if (GUILayout.Button(new GUIContent("Mirror this floor", "Flips it left to right"), EditorStyles.miniButton)) Mirror(cf);
            }
            GUILayout.Space(8); GUILayout.Label("TOOLS", m_Head); int cols = 4; float bw = (w - (cols - 1) * 3) / cols;
            for (int i = 0; i < Tools.Length; i += cols)
            {
                var row = GUILayoutUtility.GetRect(w, 50); for (int k = i; k < Mathf.Min(Tools.Length, i + cols); k++) { var t = Tools[k]; var r = new Rect(row.x + (k - i) * (bw + 3), row.y + 2, bw, 46); bool on = m_Tool == t.tool; EditorGUI.DrawRect(r, on ? Accent : r.Contains(Event.current.mousePosition) ? PanelHi : new Color(1, 1, 1, 0.05f)); GUI.Label(new Rect(r.x, r.y + 1, r.width, 26), t.glyph, new GUIStyle(m_Big) { normal = { textColor = on ? Color.black : Color.white } }); GUI.Label(new Rect(r.x + 1, r.y + 26, r.width - 2, 18), t.name, new GUIStyle(m_Small) { normal = { textColor = on ? Color.black : new Color(1, 1, 1, 0.85f) } }); GUI.Label(new Rect(r.x, r.y + 1, r.width - 3, 12), t.key, new GUIStyle(m_Key) { normal = { textColor = on ? new Color(0, 0, 0, 0.6f) : new Color(1, 1, 1, 0.45f) } }); if (GUI.Button(r, new GUIContent("", t.name + " (" + t.key + "): " + t.tip), GUIStyle.none)) SetTool(t.tool); }
            }
            GUILayout.Space(4); GUILayout.BeginHorizontal(); GUILayout.Label(new GUIContent("Snap (G)", "How finely things snap: a whole square, half or a quarter"), GUILayout.Width(60)); for (int i = 0; i < Snaps.Length; i++) if (Pill(i == 0 ? "1" : i == 1 ? "1/2" : i == 2 ? "1/4" : "Free", m_Snap == i, i == 3 ? "No snapping (or hold Ctrl while dragging)" : (m_B.cell * Snaps[i]).ToString("0.###") + " units")) m_Snap = i; GUILayout.EndHorizontal();
            GUILayout.Space(4); ToolOptions(w);
        }
        void SetTool(Tool t) { m_Tool = t; m_Drag = Grab.None; m_PolyPts.Clear(); ClearGhost(); if (t != Tool.Select) m_Sel = default; Repaint(); SceneView.RepaintAll(); }
        [SerializeField] bool m_TunnelLid = true;
        /// <summary>Up, down, left and right on the screen turned into the building's directions (the way the camera looks).</summary>
        Vector2 Facing(Vector2 d) { float yaw = m_Walk ? m_WYaw : m_Yaw; if (m_InScene && SceneView.lastActiveSceneView) yaw = SceneView.lastActiveSceneView.rotation.eulerAngles.y; if (m_B) yaw -= m_B.transform.eulerAngles.y; int q = Mathf.RoundToInt(yaw / 90f); var r = d; for (int i = 0; i < ((q % 4) + 4) % 4; i++) r = new Vector2(r.y, -r.x); return r; }

        void ToolOptions(float w)
        {
            var t = Tools.First(q => q.tool == m_Tool); GUILayout.Label(t.tip + ".", m_Hint); EditorGUIUtility.labelWidth = 96;
            switch (m_Tool)
            {
                case Tool.Room: case Tool.Floor: GUILayout.Label("Shape", m_Hint); GUILayout.BeginHorizontal(); foreach (var (n, k) in new[] { ("Rectangle", 0), ("Round", 1), ("Your points", 2) }) if (Pill(n, m_RoomShape == k, k == 2 ? "Click each corner; click the first one again (or press Enter) to finish" : "")) { m_RoomShape = k; m_PolyPts.Clear(); } GUILayout.EndHorizontal(); if (m_RoomShape == 1) { int s = EditorGUILayout.IntSlider(new GUIContent("Sides", "Fewer is more low-poly"), m_RoomSides, 3, 32); m_RoomSides = s; } break;
                case Tool.Wall: Num(ref m_WallBend, "Curve", -4f, 4f, "New walls bow out by this much in the middle (0: straight). Pick a wall and drag its middle handle to curve it later"); GUILayout.BeginHorizontal(); foreach (var (n, v) in new[] { ("Straight", 0f), ("Gentle", 0.5f), ("Round", 1.5f) }) if (Pill(n, Mathf.Approximately(m_WallBend, v))) m_WallBend = v; GUILayout.EndHorizontal(); break;
                case Tool.Door: GUILayout.BeginHorizontal(); foreach (var k in new[] { Building.OpeningKind.Door, Building.OpeningKind.Arch, Building.OpeningKind.Gap }) if (Pill(k == Building.OpeningKind.Gap ? "Open gap" : k.ToString(), m_DoorKind == k)) m_DoorKind = k; GUILayout.EndHorizontal(); Num(ref m_DoorW, "Width", 0.3f, 6f); if (m_DoorKind != Building.OpeningKind.Gap) Num(ref m_DoorH, "Height", 1f, 6f); GUILayout.BeginHorizontal(); GUILayout.Label("With", GUILayout.Width(92)); if (m_DoorKind == Building.OpeningKind.Door && Pill("A door", m_DoorFill, "A door that swings open (off: just the doorway)")) m_DoorFill = !m_DoorFill; if (m_DoorKind != Building.OpeningKind.Gap && Pill("A frame", m_DoorFrame, "A frame round it")) m_DoorFrame = !m_DoorFrame; GUILayout.EndHorizontal(); break;
                case Tool.Window: Num(ref m_WinW, "Width", 0.2f, 6f); Num(ref m_WinBottom, "From", 0f, 5f, "Height of its bottom"); Num(ref m_WinTop, "To", m_WinBottom + 0.1f, 6f, "Height of its top"); m_WinArch = EditorGUILayout.ToggleLeft("Round top", m_WinArch); GUILayout.BeginHorizontal(); GUILayout.Label("With", GUILayout.Width(92)); if (Pill("Glass", m_WinFill)) m_WinFill = !m_WinFill; if (Pill("A frame", m_WinFrame, "A frame round it and a sill")) m_WinFrame = !m_WinFrame; GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); foreach (var (n, a, b2, c) in new[] { ("Small", 0.6f, 1.4f, 2.0f), ("Normal", 1.2f, 0.9f, 2.1f), ("Tall", 1f, 0.3f, 2.4f), ("Strip", 2.4f, 1.6f, 2.1f) }) if (Pill(n, Mathf.Approximately(m_WinW, a) && Mathf.Approximately(m_WinBottom, b2))) { m_WinW = a; m_WinBottom = b2; m_WinTop = c; } GUILayout.EndHorizontal(); break;
                case Tool.Stairs: { var kinds = (Building.StairKind[])Enum.GetValues(typeof(Building.StairKind)); int sk = GUILayout.SelectionGrid((int)m_StairKind, kinds.Select(k => k == Building.StairKind.TurnLeft ? "Turn L" : k == Building.StairKind.TurnRight ? "Turn R" : k == Building.StairKind.UTurn ? "U-turn" : k.ToString()).ToArray(), 4, EditorStyles.miniButton); m_StairKind = kinds[sk]; } Num(ref m_StairW, "Width", 0.4f, 4f); Num(ref m_StairL, "Length", 0.5f, 12f, "How far it goes (turning ones: the first flight is half)"); GUILayout.BeginHorizontal(); GUILayout.Label("Goes up", GUILayout.Width(92)); foreach (var r in new[] { 1f, 0.5f, 0.25f }) if (Pill(r == 1 ? "a floor" : r == 0.5f ? "1/2" : "1/4", Mathf.Approximately(m_StairRise, r))) m_StairRise = r; GUILayout.EndHorizontal(); Num(ref m_StairRise, "Exactly", 0.05f, 2f, "Part of the floor's height"); { GUILayout.Label(new GUIContent("Railings (Auto: both sides, except along a wall)"), m_Hint); GUILayout.BeginHorizontal(); for (int i = 0; i < SideNames.Length; i++) if (Pill(SideNames[i], (int)m_StairRails == i)) m_StairRails = (Building.RailSides)i; GUILayout.EndHorizontal(); } TurnRow(); break;
                case Tool.Tunnel: GUILayout.BeginHorizontal(); GUILayout.Label("Wide", GUILayout.Width(40)); foreach (int k in new[] { 1, 2, 3 }) if (Pill(k + (k == 1 ? " square" : " squares"), m_TunnelWide == k)) m_TunnelWide = k; GUILayout.EndHorizontal(); m_TunnelLid = EditorGUILayout.ToggleLeft(new GUIContent("With a ceiling", "Off: open to the sky (a trench)"), m_TunnelLid); break;
                case Tool.Props: TurnRow(); Num(ref m_Lift, "Lifted", 0, 6, "Above the floor: shelves, things on tables"); GUILayout.Label("Part: " + PartName(m_Part), m_Hint); break;
                case Tool.Paint: GUILayout.Label("Painting with: " + MatName(m_Mat), m_Hint); break;
                case Tool.Column: Num(ref m_ColSize, "Size", 0.1f, 2f); { int s = m_ColSides; s = EditorGUILayout.IntSlider(new GUIContent("Round", "0 square, 3 and up: that many faces"), s, 0, 16); if (s == 1 || s == 2) s = 3; m_ColSides = s; } m_ColUp = EditorGUILayout.ToggleLeft("Through all the floors above too", m_ColUp); break;
                case Tool.Beam: Num(ref m_BeamW, "Width", 0.05f, 1f); Num(ref m_BeamD, "Depth", 0.05f, 1.5f); break;
                case Tool.Shaft: { int n = EditorGUILayout.IntSlider(new GUIContent("Floors up", "Through how many floors above"), m_ShaftUp, 1, 20); m_ShaftUp = n; } break;
                case Tool.Stamp: GUILayout.Label("Room: " + (m_Room ? m_Room.name : "(pick one on the right, Rooms)"), m_Hint); TurnRow(); break;
            }
            if (m_Tool == Tool.Props || m_Tool == Tool.Stairs || m_Tool == Tool.Column) m_OnTop = EditorGUILayout.ToggleLeft(new GUIContent("Sits on what's under it", "On stairs, a landing or a raised floor, instead of the floor"), m_OnTop);
            EditorGUIUtility.labelWidth = 0;
            GUILayout.Space(6); GUILayout.Label("THIS TOOL'S KEYS", m_Head); foreach (var k in ToolKeys()) GUILayout.Label(k, m_Hint); if (GUILayout.Button(new GUIContent("All the keys (F1)", "A sheet over the view with every key"), EditorStyles.miniButton)) m_ShowKeys = !m_ShowKeys;
            GUILayout.Space(6); GUILayout.Label("KEYS: Q select, 1-0 and C B L U O tools, R turn, G snap, H hide above, M measurements, P a person here, Tab walk, F frame, T top view, Delete, Ctrl+D copy, arrows nudge, Ctrl+Z / Ctrl+Y undo and redo, Esc.\nWhat's picked, or what you're about to put: [ ] its size, Shift+[ ] its other size, - = its height, K its kind (the strip at the top of the view says which).\nIn the view: right-drag turns, middle-drag (or Shift+right) moves, wheel zooms.", m_Hint);
        }
        void TurnRow() { GUILayout.Label("Turned (R)", m_Hint); GUILayout.BeginHorizontal(); foreach (int a in new[] { 0, 90, 180, 270 }) if (Pill(a + "°", Mathf.Approximately(Mathf.Repeat(m_Turn, 360), a))) m_Turn = a; GUILayout.EndHorizontal(); }
        string PartName(string id) => BuildingMaker.Part(id) ? BuildingMaker.Part(id).name : "(pick one on the right, Parts)";
        string MatName(int i) => !m_B ? "" : i >= 0 && i < m_B.materials.Count && m_B.materials[i] ? m_B.materials[i].name : "(empty slot " + (i + 1) + ")";

        /// <summary>The hotkeys, in this window and in the Scene view.</summary>
        bool Keys(Event e)
        {
            if (!m_B) return false; bool ctrl = e.control || e.command; if (m_Walk && WalkKey(e, true)) return true;
            if (ctrl && e.keyCode == KeyCode.Z) { if (e.shift) Undo.PerformRedo(); else Undo.PerformUndo(); return true; }
            if (ctrl && e.keyCode == KeyCode.Y) { Undo.PerformRedo(); return true; }
            if (ctrl && e.keyCode == KeyCode.D) { Duplicate(); return true; }
            if (e.keyCode == KeyCode.Escape && Busy) { CancelDrag(); return true; }
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && m_PolyPts.Count >= 3) { ShapedRoom(m_PolyPts, m_Tool == Tool.Room); m_PolyPts.Clear(); return true; }
            if (e.alt && !ctrl && (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow))
            {
                // Alt + arrows: the size keys on any keyboard (right and left: its size, Shift: its other size; up and down: its height)
                var k = e.keyCode == KeyCode.RightArrow ? KeyCode.RightBracket : e.keyCode == KeyCode.LeftArrow ? KeyCode.LeftBracket : e.keyCode == KeyCode.UpArrow ? KeyCode.Equals : KeyCode.Minus; var fake = new Event(e) { keyCode = k, alt = false };
                if (m_Tool == Tool.Select && Valid(m_Sel)) { Tune(fake); return true; } return ToolTune(fake);
            }
            if (e.keyCode == KeyCode.F1) { m_ShowKeys = !m_ShowKeys; return true; }
            if (ctrl || e.alt) return false;
            switch (e.keyCode)
            {
                case KeyCode.Q: SetTool(Tool.Select); return true; case KeyCode.C: SetTool(Tool.Column); return true; case KeyCode.B: SetTool(Tool.Beam); return true; case KeyCode.L: SetTool(Tool.Shaft); return true; case KeyCode.U: SetTool(Tool.Balcony); return true; case KeyCode.O: SetTool(Tool.Stamp); return true;
                case KeyCode.M: m_Measure = !m_Measure; SceneView.RepaintAll(); return true; case KeyCode.Tab: Walk(!m_Walk); return true; case KeyCode.P: PutPerson(); return true;
                case KeyCode.LeftBracket: case KeyCode.RightBracket: case KeyCode.Minus: case KeyCode.Equals: case KeyCode.K: if (m_Tool == Tool.Select && Valid(m_Sel)) { Tune(e); return true; } return ToolTune(e);
                case KeyCode.Alpha1: SetTool(Tool.Room); return true; case KeyCode.Alpha2: SetTool(Tool.Floor); return true; case KeyCode.Alpha3: SetTool(Tool.Wall); return true; case KeyCode.Alpha4: SetTool(Tool.Door); return true; case KeyCode.Alpha5: SetTool(Tool.Window); return true;
                case KeyCode.Alpha6: SetTool(Tool.Stairs); return true; case KeyCode.Alpha7: SetTool(Tool.Hole); return true; case KeyCode.Alpha8: SetTool(Tool.Tunnel); return true; case KeyCode.Alpha9: SetTool(Tool.Props); return true; case KeyCode.Alpha0: SetTool(Tool.Paint); return true;
                case KeyCode.R: if (m_Tool == Tool.Select && m_Sel.kind != SelKind.None) TurnSelected(e.shift ? 15 : 90); else m_Turn = Mathf.Repeat(m_Turn + (e.shift ? 15 : 90), 360); SceneView.RepaintAll(); return true;
                case KeyCode.G: m_Snap = (m_Snap + 1) % Snaps.Length; return true;
                case KeyCode.H: m_HideAbove = !m_HideAbove; Floors(); return true;
                case KeyCode.J: m_HideRoof = !m_HideRoof; Floors(); return true;
                case KeyCode.N: m_HideOuter = !m_HideOuter; Floors(); return true;
                case KeyCode.F: Frame(); return true;
                case KeyCode.T: m_Top = !m_Top; return true;
                case KeyCode.PageUp: case KeyCode.PageDown: { var ls = m_B.floors.Select(f => f.level).OrderBy(l => l).ToList(); int i = ls.IndexOf(m_Level) + (e.keyCode == KeyCode.PageUp ? 1 : -1); if (i >= 0 && i < ls.Count) { m_Level = ls[i]; m_Sel = default; Floors(); SceneView.RepaintAll(); } return true; }
                case KeyCode.Delete: case KeyCode.Backspace: if (m_Sel.kind != SelKind.None) { DeleteSelected(); return true; } return false;
                case KeyCode.Escape: if (m_ShowKeys) m_ShowKeys = false; else if (m_Sel.kind != SelKind.None) m_Sel = default; else SetTool(Tool.Select); return true;
                case KeyCode.LeftArrow: case KeyCode.RightArrow: case KeyCode.UpArrow: case KeyCode.DownArrow: if (m_Sel.kind == SelKind.None) return false; Nudge(Facing(e.keyCode == KeyCode.LeftArrow ? Vector2.left : e.keyCode == KeyCode.RightArrow ? Vector2.right : e.keyCode == KeyCode.UpArrow ? Vector2.up : Vector2.down)); return true;
            }
            return false;
        }

        void NewBuilding()
        {
            var go = new GameObject("Building"); var sv = SceneView.lastActiveSceneView; if (sv) { var p = sv.pivot; go.transform.position = new Vector3(Mathf.Round(p.x), 0, Mathf.Round(p.z)); }
            var b = go.AddComponent<Building>(); b.materials = DefaultMaterials(); b.FloorAt(0, true); Undo.RegisterCreatedObjectUndo(go, "New building"); ShowAll(); m_B = b; m_Level = 0; m_Tool = Tool.Room; m_Sel = default; Selection.activeGameObject = go; Remake(); Frame();
        }
        static List<Material> DefaultMaterials()
        {
            var l = new List<Material>(); var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); if (!AssetDatabase.IsValidFolder("Assets/Buildings")) AssetDatabase.CreateFolder("Assets", "Buildings");
            foreach (var (n, c) in new[] { ("Floor", new Color(0.55f, 0.47f, 0.38f)), ("Walls", new Color(0.82f, 0.8f, 0.74f)), ("Ceiling", new Color(0.92f, 0.91f, 0.88f)), ("Stairs and trims", new Color(0.42f, 0.3f, 0.22f)) })
            { var path = "Assets/Buildings/" + n + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) { m = new Material(sh); m.SetColor(m.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", c); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f); AssetDatabase.CreateAsset(m, path); } l.Add(m); }
            return l;
        }
    }

    [CustomEditor(typeof(Building))]
    class BuildingInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button("Open in the Building Builder", GUILayout.Height(30))) BuildingWindow.Open((Building)target);
            EditorGUI.BeginChangeCheck(); DrawPropertiesExcluding(serializedObject, "floors"); bool ch = serializedObject.ApplyModifiedProperties(); if (EditorGUI.EndChangeCheck() || ch) BuildingMaker.Rebuild((Building)target);
            EditorGUILayout.HelpBox(((Building)target).floors.Count + " floors. Draw it in the Building Builder; what it makes (its children) is made again after every change.", MessageType.None);
        }
    }

    /// <summary>Finds scene objects the same way on Unity 6.0 to 6.6+ (6.6 renamed the calls, the old ones only warn).</summary>
    static class BbFind
    {
        public static T[] All<T>(bool inactive = false) where T : UnityEngine.Object
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsByType<T>(inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
        }
        public static T Any<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>();
    }
}
