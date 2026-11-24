using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The right side: what's picked (every number of it), materials (Texture Studio's too), parts (and making your
    /// own) and the building's own settings.</summary>
    public partial class BuildingWindow
    {
        static readonly string[] Roles = { "Floors", "Walls", "Ceilings", "Stairs, trims" };
        List<Material> m_Studio; double m_StudioAt; List<BuildPart> m_Parts; [SerializeField] bool m_Making; [SerializeField] string m_NewName = "My part"; [SerializeField] string m_NewCat = "Props"; [SerializeField] BuildPart.Shape m_NewShape = BuildPart.Shape.Box; [SerializeField] GameObject m_NewModel; [SerializeField] Vector3 m_NewSize = new Vector3(1, 1, 1); [SerializeField] Material m_NewMat; [SerializeField] BuildPart.Collide m_NewCollide = BuildPart.Collide.Box; [SerializeField] BuildPart.Sits m_NewSits = BuildPart.Sits.Middle; [SerializeField] int m_NewSides;

        void RightSide(float w)
        {
            GUILayout.BeginHorizontal(); foreach (var (i, n) in new[] { (0, "Picked"), (4, "Rooms"), (1, "Materials"), (2, "Parts"), (3, "Building") }) if (Pill(n, m_Tab == i, "", GUILayout.Height(22))) m_Tab = i; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.Space(6);
            if (!m_B) { GUILayout.Label("No building yet.", m_Hint); return; }
            m_RightScroll = GUILayout.BeginScrollView(m_RightScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); EditorGUIUtility.labelWidth = 110;
            if (m_Tab != 4) m_HoverRoom = -1; if (m_Tab == 0) PickedTab(w - 16); else if (m_Tab == 1) MaterialsTab(w - 16); else if (m_Tab == 2) PartsTab(w - 16); else if (m_Tab == 4) RoomsTab(w - 16); else BuildingTab(w - 16);
            EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ what's picked

        void PickedTab(float w)
        {
            if (!Valid(m_Sel)) { GUILayout.Label("Nothing picked. Press Q (Select) and click a wall, a door, a window, stairs, a part or a floor: everything about it shows up here.", m_Hint); return; }
            var fl = m_B.FloorAt(m_Sel.level); var s = m_Sel; GUILayout.Label(Describe(s).ToUpper(), new GUIStyle(m_Head) { normal = { textColor = Accent } }); bool ch = false; float H = m_B.HeightOf(s.level);
            void V2(string l, ref Vector2 v) { var n = EditorGUILayout.Vector2Field(l, v); if (n != v) { v = n; ch = true; } }
            void F(string l, ref float v, float lo, float hi, string tip = "") { ch |= Num(ref v, l, lo, hi, tip); }
            void I(string l, ref int v, int lo, int hi) { int n = EditorGUILayout.IntSlider(l, v, lo, hi); if (n != v) { v = n; ch = true; } }
            void MatPick(string l, ref int v) { int def = l == "Front" || l == "Back" ? 1 : l == "Top" ? 0 : l == "Underneath" ? 2 : l == "Lid" ? 1 : l == "Frame material" ? m_B.trimMat : 3; if (MatField(l, l + s.kind + s.level + s.i + "_" + s.j, ref v, def)) ch = true; }
            Undo.RecordObject(m_B, "Building: change it");
            switch (s.kind)
            {
                case SelKind.Wall:
                {
                    var wl = fl.walls[s.i]; V2("From", ref wl.a); V2("To", ref wl.b); float len = wl.Length; float nl = EditorGUILayout.FloatField("Length", len); if (Mathf.Abs(nl - len) > 1e-4f && nl > 0.05f) { wl.b = wl.a + (wl.b - wl.a).normalized * nl; ch = true; }
                    bool full = wl.height <= 0; bool nf = EditorGUILayout.Toggle(new GUIContent("Full height", "Up to the floor above"), full); if (nf != full) { wl.height = nf ? -1 : H - m_B.slabThickness; ch = true; }
                    if (!full) { F("Height", ref wl.height, 0.05f, H, "From its bottom"); GUILayout.BeginHorizontal(); GUILayout.Label("", GUILayout.Width(106)); foreach (var (n, k) in new[] { ("3/4", 0.75f), ("1/2", 0.5f), ("1/4", 0.25f) }) if (GUILayout.Button(n, EditorStyles.miniButton)) { wl.height = (H - m_B.slabThickness) * k; ch = true; } GUILayout.EndHorizontal(); F("Starts at", ref wl.bottom, 0, H, "A gap under it, or a wall that hangs (a beam)"); }
                    float t = BuildingMaker.Thick(m_B, wl); if (Num(ref t, "Thickness", 0.02f, 1.5f)) { wl.thickness = t; ch = true; }
                    F("Curve", ref wl.bend, -6f, 6f, "Bows out to its front by this much in the middle (Shift and - = to change it by keys)");
                    MatPick("Front", ref wl.front); MatPick("Back", ref wl.back);
                    void Fin(string l, ref Building.Finish f) { EditorGUILayout.BeginHorizontal(); GUILayout.Label(l, GUILayout.Width(106)); foreach (var (n, k) in new[] { ("Skirting", Building.Finish.Skirting), ("Crown", Building.Finish.Crown), ("Wainscot", Building.Finish.Wainscot) }) { bool on = (f & k) != 0; if (Pill(n, on)) { f ^= k; ch = true; } } EditorGUILayout.EndHorizontal(); }
                    Fin("Front finish", ref wl.frontFinish); Fin("Back finish", ref wl.backFinish);
                    EditorGUILayout.BeginHorizontal(); GUILayout.Label(new GUIContent("Windows along it", "Spreads the same window evenly (like its first one)"), GUILayout.Width(106)); foreach (int n in new[] { 1, 2, 3, 4, 5, 6 }) if (GUILayout.Button(n.ToString(), EditorStyles.miniButton)) { Spread(wl, n); GUIUtility.ExitGUI(); } EditorGUILayout.EndHorizontal();
                    GUILayout.Label(wl.openings.Count + " doors and windows in it", m_Hint); for (int j = 0; j < wl.openings.Count; j++) if (GUILayout.Button(wl.openings[j].kind + " at " + M(wl.openings[j].at), EditorStyles.miniButton)) m_Sel = new Sel { kind = SelKind.Opening, level = s.level, i = s.i, j = j };
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("Split in two")) { var mid = (wl.a + wl.b) / 2; var b2 = new Building.Wall { a = mid, b = wl.b, height = wl.height, bottom = wl.bottom, thickness = wl.thickness, front = wl.front, back = wl.back }; float half = wl.Length / 2; foreach (var o in wl.openings.Where(o => o.at > half).ToList()) { wl.openings.Remove(o); o.at -= half; b2.openings.Add(o); } wl.b = mid; fl.walls.Add(b2); ch = true; } if (GUILayout.Button("Flip sides")) { var x = wl.a; wl.a = wl.b; wl.b = x; var m = wl.front; wl.front = wl.back; wl.back = m; float l2 = wl.Length; foreach (var o in wl.openings) o.at = l2 - o.at; ch = true; } GUILayout.EndHorizontal(); break;
                }
                case SelKind.Opening:
                {
                    var wl = fl.walls[s.i]; var o = wl.openings[s.j]; int k = (int)o.kind; int nk = GUILayout.Toolbar(k, new[] { "Door", "Window", "Arch", "Gap" }); if (nk != k) { o.kind = (Building.OpeningKind)nk; ch = true; }
                    float len = wl.Length; F("Along the wall", ref o.at, o.width / 2, Mathf.Max(o.width / 2, len - o.width / 2), "Where its middle is, from the wall's start"); GUILayout.Label("From the start: " + M(o.at - o.width / 2) + ", from the end: " + M(len - o.at - o.width / 2), m_Hint);
                    F("Width", ref o.width, 0.1f, len); if (o.kind != Building.OpeningKind.Gap) { if (o.kind == Building.OpeningKind.Window || o.kind == Building.OpeningKind.Arch) F("Bottom", ref o.bottom, 0, H); F("Top", ref o.top, o.bottom + 0.05f, H); }
                    if (o.kind == Building.OpeningKind.Arch) I("Faces round it", ref o.sides, 2, 24);
                    bool fr = EditorGUILayout.Toggle(new GUIContent("Frame", "A frame round it (and a sill under a window)"), o.frame); if (fr != o.frame) { o.frame = fr; ch = true; }
                    if (o.kind != Building.OpeningKind.Gap) { bool fi = EditorGUILayout.Toggle(new GUIContent(o.kind == Building.OpeningKind.Door ? "Door leaf" : "Glass", o.kind == Building.OpeningKind.Door ? "A door that swings open in the game when someone comes near" : "A pane of glass"), o.fill); if (fi != o.fill) { o.fill = fi; ch = true; } }
                    if (o.kind == Building.OpeningKind.Door && o.fill)
                    {
                        GUILayout.Label(new GUIContent("Swings", "In the game (and walk mode) the door swings open when someone comes near"), m_Hint); GUILayout.BeginHorizontal(); foreach (Building.DoorSwing sw in Enum.GetValues(typeof(Building.DoorSwing))) if (Pill(sw == Building.DoorSwing.BothWays ? "Away from you" : sw == Building.DoorSwing.ToTheFront ? "To the front" : "To the back", o.swing == sw)) { o.swing = sw; ch = true; } GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal(); if (Pill("Hinged on the other side", o.hingeOther)) { o.hingeOther = !o.hingeOther; ch = true; } if (Pill("Double door", o.doubleDoor, "Two doors, one hinged at each side")) { o.doubleDoor = !o.doubleDoor; ch = true; } GUILayout.EndHorizontal();
                    }
                    if (o.frame && o.kind != Building.OpeningKind.Gap) MatPick("Frame material", ref o.frameMat); if (o.kind == Building.OpeningKind.Door && o.fill && !o.model) MatPick("Door material", ref o.leafMat);
                    if (o.fill || o.kind == Building.OpeningKind.Gap) { var mo = (GameObject)EditorGUILayout.ObjectField(new GUIContent(o.kind == Building.OpeningKind.Door ? "Your door model" : "Your model in it", o.kind == Building.OpeningKind.Door ? "Swings open like the builder's door, stretched to the doorway when Fit is on" : "Fills the hole (a window, bars, a gate), stretched to it when Fit is on"), o.model, typeof(GameObject), false); if (mo != o.model) { o.model = mo; if (mo && o.kind == Building.OpeningKind.Gap) o.fill = true; ch = true; } if (o.model) { bool ft = EditorGUILayout.Toggle(new GUIContent("Fit", "Stretch it to the hole"), o.fit); if (ft != o.fit) { o.fit = ft; ch = true; } } }
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("Centre it")) { o.at = len / 2; ch = true; } if (GUILayout.Button("The wall")) m_Sel = new Sel { kind = SelKind.Wall, level = s.level, i = s.i }; GUILayout.EndHorizontal(); break;
                }
                case SelKind.Stair:
                {
                    var st = fl.stairs[s.i]; int k = (int)st.kind; int nk = GUILayout.SelectionGrid(k, new[] { "Straight", "Turn L", "Turn R", "U-turn", "Ramp", "Spiral", "Ladder" }, 4, EditorStyles.miniButton); if (nk != k) { st.kind = (Building.StairKind)nk; ch = true; }
                    V2("Where", ref st.at); F("Turned", ref st.turn, 0, 360); F("Width", ref st.width, 0.3f, 6f); F("Length", ref st.length, 0.3f, 20f); F("Goes up", ref st.rise, 0.05f, 2f, "1 is a whole floor"); GUILayout.BeginHorizontal(); GUILayout.Label("", GUILayout.Width(106)); foreach (var (n, r) in new[] { ("floor", 1f), ("1/2", 0.5f), ("1/4", 0.25f) }) if (GUILayout.Button(n, EditorStyles.miniButton)) { st.rise = r; ch = true; } GUILayout.EndHorizontal();
                    int steps = st.steps > 0 ? st.steps : BuildingMaker.StepCount(st, H); int ns = EditorGUILayout.IntSlider(new GUIContent("Steps", "How many (step height " + M(st.rise * H / Mathf.Max(1, steps)) + ")"), steps, 2, 60); if (ns != steps) { st.steps = ns; ch = true; } MatPick("Material", ref st.mat); MatPick("Step tops", ref st.topMat); F("Starts higher", ref st.lift, 0, H, "On a landing, a raised floor or other stairs");
                    GUILayout.Label("Underneath", m_Hint); GUILayout.BeginHorizontal(); foreach (Building.StairUnder k2 in Enum.GetValues(typeof(Building.StairUnder))) if (Pill(k2 == Building.StairUnder.Solid ? "Solid" : k2 == Building.StairUnder.Open ? "Open" : "Cupboard", st.under == k2, k2 == Building.StairUnder.Cupboard ? "A cupboard with a door under it (straight stairs along a wall; the door shows where it's tall enough)" : k2 == Building.StairUnder.Open ? "Treads on two stringers, nothing under them" : "")) { st.under = k2; ch = true; } GUILayout.EndHorizontal();
                    GUILayout.Space(4); GUILayout.Label("RAILINGS", m_Head); ch |= SidesRow(ref st.railSides); if (st.railSides != Building.RailSides.None) ch |= RailGUI(st.railing, true); break;
                }
                case SelKind.Piece:
                {
                    var p = fl.pieces[s.i]; V2("Where", ref p.at); F("Turned", ref p.turn, 0, 360); F("Lifted", ref p.lift, -2, 10); var sc = EditorGUILayout.Vector3Field("Scale", p.scale); if (sc != p.scale) { p.scale = sc; ch = true; } MatPick("Material", ref p.mat);
                    var part = BuildingMaker.Part(p.part); if (part && GUILayout.Button("Edit the part itself")) { m_Part = p.part; m_Tab = 2; } break;
                }
                case SelKind.Column: { var c = fl.columns[s.i]; V2("Where", ref c.at); F("Size", ref c.size, 0.05f, 3f); int sd = EditorGUILayout.IntSlider(new GUIContent("Round", "0 square"), c.sides, 0, 24); if (sd != c.sides) { c.sides = sd == 1 || sd == 2 ? 3 : sd; ch = true; } F("Starts higher", ref c.lift, 0, H); MatPick("Material", ref c.mat); break; }
                case SelKind.Beam: { var bm = fl.beams[s.i]; V2("From", ref bm.a); V2("To", ref bm.b); F("Width", ref bm.width, 0.05f, 2f); F("Depth", ref bm.depth, 0.05f, 2f); MatPick("Material", ref bm.mat); break; }
                case SelKind.Area:
                {
                    var a = fl.areas[s.i]; var nm = EditorGUILayout.TextField(new GUIContent("Room name", "Shown in the Rooms list and on the floor plan"), a.name); if (nm != a.name) { a.name = nm; ch = true; } V2("Corner", ref a.min); V2("Other corner", ref a.max); var z = a.max - a.min; var nz = EditorGUILayout.Vector2Field("Size", new Vector2(Mathf.Abs(z.x), Mathf.Abs(z.y))); if (nz != new Vector2(Mathf.Abs(z.x), Mathf.Abs(z.y))) { var lo = Vector2.Min(a.min, a.max); a.min = lo; a.max = lo + nz; ch = true; }
                    bool h = EditorGUILayout.Toggle(new GUIContent("Is a hole", "Takes the floor away there"), a.hole); if (h != a.hole) { a.hole = h; ch = true; } bool r = EditorGUILayout.Toggle(new GUIContent("Lid on top", "A ceiling at the top of this floor, where the floor above has none: tunnels, roofs"), a.roof); if (r != a.roof) { a.roof = r; ch = true; }
                    F("Raised by", ref a.lift, -3, 3, "Split levels, a stage, a sunken pit (- and = by keys)"); MatPick("Top", ref a.top); MatPick("Underneath", ref a.under); if (a.roof) MatPick("Lid", ref a.roofMat);
                    GUILayout.Label("Shape", m_Hint); GUILayout.BeginHorizontal(); var lo2 = Vector2.Min(a.min, a.max); var hi2 = Vector2.Max(a.min, a.max); if (Pill("Rectangle", !a.IsShape)) { a.shape.Clear(); a.min = lo2; a.max = hi2; ch = true; } if (Pill("Round", false, "Rounded (8 sides): change the sides on the Room tool")) { var before = a.Corners(); a.SetShape(BuildingMaker.Round(lo2, hi2, Mathf.Max(3, m_RoomSides))); ch = true; } if (Pill("Broken", false, "A rough, smashed outline (a new one each click): good for holes")) { a.SetShape(BuildingMaker.Broken(lo2, hi2, UnityEngine.Random.Range(1, 99999))); ch = true; } GUILayout.EndHorizontal();
                    GUILayout.Label("Drag a corner to move it, drag the small dot in an edge's middle to add a corner there, Ctrl-click a corner to take it away. Walls on its edges follow.", m_Hint);
                    if (!a.hole) { bool sb = EditorGUILayout.ToggleLeft(new GUIContent(" Solid underneath", "Filled down to the floor below: under a landing, a bridge or a gap"), a.solidBelow); if (sb != a.solidBelow) { a.solidBelow = sb; ch = true; } bool po = EditorGUILayout.ToggleLeft(new GUIContent(" Posts under it", "Down to the ground where the building below doesn't hold it: a balcony"), a.posts); if (po != a.posts) { a.posts = po; ch = true; } if (a.posts) F("Gap between posts", ref a.postGap, 0.5f, 6f); }
                    if (a.roof) { if (GUILayout.Button(new GUIContent("Take every tunnel ceiling on this floor off", "Every lid on this floor (Ctrl+Z puts them back)"))) { fl.areas.ForEach(q => q.roof = false); ch = true; } }
                    if (a.hole) { bool hr = EditorGUILayout.ToggleLeft(new GUIContent(" Railing round this hole", "When the floor has railings on"), a.rails); if (hr != a.rails) { a.rails = hr; ch = true; } }
                    GUILayout.Space(4); GUILayout.Label("RAILING ROUND ITS OPEN EDGES", m_Head); bool fr2 = EditorGUILayout.ToggleLeft(new GUIContent(" Railings on this floor", "Where you could fall: edges with no wall, holes, balconies (the same as the tickbox on the left)"), fl.rails); if (fr2 != fl.rails) { fl.rails = fr2; ch = true; } if (fl.rails) ch |= RailGUI(a.railing, true);
                    if (GUILayout.Button(new GUIContent("Save it as a room", "With its walls, doors, stairs and parts: stamp it again with the Stamp tool (O)"))) { SaveRoom(fl, a); ShowNotification(new GUIContent("Saved " + m_Room.name)); }
                    if (GUILayout.Button(new GUIContent("Walls all round it", "Adds walls along its edges where there are none"))) { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); foreach (var (p0, p1) in new[] { (lo, new Vector2(hi.x, lo.y)), (new Vector2(hi.x, lo.y), hi), (hi, new Vector2(lo.x, hi.y)), (new Vector2(lo.x, hi.y), lo) }) AddWallLine(fl, p0, p1); ch = true; } break;
                }
            }
            GUILayout.Space(8); GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Copy (Ctrl+D)"))) { Duplicate(); GUIUtility.ExitGUI(); } if (GUILayout.Button(new GUIContent("Delete (Del)"))) { DeleteSelected(); GUIUtility.ExitGUI(); } GUILayout.EndHorizontal();
            if (ch) { EditorUtility.SetDirty(m_B); Remake(); }
        }

        // ------------------------------------------------------------------ railings

        static readonly string[] SideNames = { "Auto", "Both", "Left", "Right", "None" };
        bool SidesRow(ref Building.RailSides v)
        {
            GUILayout.Label(new GUIContent("Sides (left and right as you walk up)", "Auto: both, except a side that runs along a wall"), m_Hint); GUILayout.BeginHorizontal(); bool ch = false;
            for (int i = 0; i < SideNames.Length; i++) if (Pill(SideNames[i], (int)v == i, i == 0 ? "Both, except a side that runs along a wall" : "")) { v = (Building.RailSides)i; ch = true; }
            GUILayout.EndHorizontal(); return ch;
        }
        static readonly string[] SupportNames = { "Posts", "Bars", "Panel", "Glass", "Nothing" };
        /// <summary>The look of a railing: what holds its handrail up and how many, its rails, and your own models.</summary>
        bool RailGUI(Building.RailStyle st, bool perOne)
        {
            bool ch = false;
            if (perOne)
            {
                bool own = EditorGUILayout.ToggleLeft(new GUIContent(" Its own look", "Off: it looks like all the others (Building tab, Railings)"), st.own);
                if (own != st.own) { if (own) { JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(m_B.railing), st); st.postModel = m_B.railing.postModel; st.handrailModel = m_B.railing.handrailModel; } st.own = own; ch = true; }
                if (!st.own) return ch;
            }
            var box = EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(6, 6, 4, 6) }); if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(box, new Color(1, 1, 1, 0.04f));
            void Fl(string l, ref float v, float lo, float hi, string tip = "") { ch |= Num(ref v, l, lo, hi, tip); }
            GUILayout.Label(new GUIContent("Held up by", "What holds the handrail up"), m_Hint); GUILayout.BeginHorizontal(); for (int i = 0; i < SupportNames.Length; i++) if (Pill(SupportNames[i], (int)st.support == i)) { var was = st.support; st.support = (Building.RailSupport)i; if (st.support == Building.RailSupport.Bars && st.spacing > 0.4f) st.spacing = 0.12f; if (st.support == Building.RailSupport.Posts && was == Building.RailSupport.Bars) st.spacing = 1f; ch = true; } GUILayout.EndHorizontal();
            if (st.support == Building.RailSupport.Posts || st.support == Building.RailSupport.Bars) { Fl("Gap between", ref st.spacing, 0.05f, 4f, "How far apart they are: fewer or more of them"); GUILayout.BeginHorizontal(); foreach (var (n, g) in new[] { ("Few", 1.5f), ("Some", 1f), ("Many", 0.5f), ("Bars", 0.12f) }) if (GUILayout.Button(n, EditorStyles.miniButton)) { st.spacing = g; ch = true; } GUILayout.EndHorizontal(); }
            if (st.support != Building.RailSupport.None) { Fl("Thickness", ref st.postSize, 0.01f, 0.3f, "Of its posts"); int r = EditorGUILayout.IntSlider(new GUIContent("Round", "0 square, 3 and up: that many faces"), st.round, 0, 16); if (r == 1 || r == 2) r = 3; if (r != st.round) { st.round = r; ch = true; } }
            Fl("Height", ref st.height, 0.3f, 2f);
            GUILayout.Label("Rails", m_Hint); GUILayout.BeginHorizontal(); if (Pill("Handrail", st.handrail)) { st.handrail = !st.handrail; ch = true; } if (Pill("Middle", st.midRail, "A second rail half way down (with posts)")) { st.midRail = !st.midRail; ch = true; } if (Pill("Bottom", st.bottomRail, "A rail just above the floor")) { st.bottomRail = !st.bottomRail; ch = true; } GUILayout.EndHorizontal();
            var pm = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Your post model", "Put where each post or bar goes, stretched to its size when Fit is on"), st.postModel, typeof(GameObject), false); if (pm != st.postModel) { st.postModel = pm; ch = true; }
            var hm = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Your handrail model", "Stretched along the handrail when Fit is on (its long side is its depth, z)"), st.handrailModel, typeof(GameObject), false); if (hm != st.handrailModel) { st.handrailModel = hm; ch = true; }
            if (st.postModel || st.handrailModel) { bool f = EditorGUILayout.Toggle(new GUIContent("Fit", "Stretch your models to the size of the pieces"), st.fit); if (f != st.fit) { st.fit = f; ch = true; } }
            var names = new[] { "(the trim's)" }.Concat(m_B.materials.Select((m, k) => (k + 1) + ": " + (m ? m.name : "empty"))).ToArray(); int nm = EditorGUILayout.Popup("Material", st.mat + 1, names) - 1; if (nm != st.mat) { st.mat = nm; ch = true; }
            EditorGUILayout.EndVertical(); return ch;
        }

        // ------------------------------------------------------------------ materials

        void MaterialsTab(float w)
        {
            GUILayout.Label("Click one to paint with it (Paint, key 0). The first four are what new floors, walls, ceilings and stairs get. Drop a material on one to swap it.", m_Hint);
            int cols = Mathf.Max(2, Mathf.FloorToInt(w / 90)); float cw = (w - (cols - 1) * 6) / cols; int count = Mathf.Max(4, m_B.materials.Count);
            for (int i = 0; i < count; i += cols)
            {
                var row = GUILayoutUtility.GetRect(w, cw + 30); for (int k = i; k < Mathf.Min(count, i + cols); k++)
                {
                    var r = new Rect(row.x + (k - i) * (cw + 6), row.y, cw, cw + 26); var m = k < m_B.materials.Count ? m_B.materials[k] : null; bool on = k == m_Mat; EditorGUI.DrawRect(r, on ? Accent : new Color(1, 1, 1, 0.05f));
                    var pic = new Rect(r.x + 4, r.y + 4, cw - 8, cw - 8); var prev = m ? AssetPreview.GetAssetPreview(m) : null; if (prev) GUI.DrawTexture(pic, prev, ScaleMode.ScaleAndCrop); else { EditorGUI.DrawRect(pic, new Color(0, 0, 0, 0.3f)); if (m) Repaint(); }
                    GUI.Label(new Rect(r.x + 2, r.yMax - 22, r.width - 4, 20), (k < Roles.Length ? Roles[k] + ": " : "") + (m ? m.name : "empty"), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, normal = { textColor = on ? Color.black : Color.white } });
                    var e = Event.current; if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition) && DragAndDrop.objectReferences.FirstOrDefault() is Material dm) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); int kk = k; Change("Set a material", () => { while (m_B.materials.Count <= kk) m_B.materials.Add(null); m_B.materials[kk] = dm; }); } e.Use(); }
                    if (GUI.Button(r, new GUIContent("", "Click: paint with it"), GUIStyle.none)) { m_Mat = k; SetTool(Tool.Paint); }
                }
            }
            var sel = m_Mat >= 0 && m_Mat < m_B.materials.Count ? m_B.materials[m_Mat] : null;
            GUILayout.BeginHorizontal(); GUILayout.Label("Slot " + (m_Mat + 1), GUILayout.Width(48)); var nm = (Material)EditorGUILayout.ObjectField(sel, typeof(Material), false); if (nm != sel) Change("Set a material", () => { while (m_B.materials.Count <= m_Mat) m_B.materials.Add(null); m_B.materials[m_Mat] = nm; }); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if (GUILayout.Button("+ Another slot")) Change("Add a material", () => { m_B.materials.Add(null); m_Mat = m_B.materials.Count - 1; }); using (new EditorGUI.DisabledScope(!sel)) if (GUILayout.Button(new GUIContent("Edit in Texture Studio", "Opens Texture Studio on how this was made (or on its texture). Save there and the building shows it"))) TextureStudioWindow.Edit(sel); GUILayout.EndHorizontal();
            if (m_Studio == null || EditorApplication.timeSinceStartup - m_StudioAt > 10) { m_StudioAt = EditorApplication.timeSinceStartup; m_Studio = StudioMaterials(); }
            GUILayout.Space(8); GUILayout.Label("FROM TEXTURE STUDIO (" + m_Studio.Count + "): click one to put it in slot " + (m_Mat + 1), m_Head); if (m_Studio.Count == 0) GUILayout.Label("Materials you save in Texture Studio (or its Patterns page) show up here.", m_Hint);
            int c2 = Mathf.Max(3, Mathf.FloorToInt(w / 66)); float s2 = (w - (c2 - 1) * 4) / c2;
            for (int i = 0; i < m_Studio.Count; i += c2) { var row = GUILayoutUtility.GetRect(w, s2 + 4); for (int k = i; k < Mathf.Min(m_Studio.Count, i + c2); k++) { var m = m_Studio[k]; var r = new Rect(row.x + (k - i) * (s2 + 4), row.y, s2, s2); var prev = AssetPreview.GetAssetPreview(m); if (prev) GUI.DrawTexture(r, prev, ScaleMode.ScaleAndCrop); else EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.3f)); if (GUI.Button(r, new GUIContent("", m.name), GUIStyle.none)) { var mm = m; Change("Set a material", () => { while (m_B.materials.Count <= m_Mat) m_B.materials.Add(null); m_B.materials[m_Mat] = mm; }); } } }
            GUILayout.Space(8); GUILayout.Label("ON ALL OF " + Building.LevelName(m_Level).ToUpper(), m_Head); var fl = m_B.FloorAt(m_Level);
            if (fl != null) { GUILayout.BeginHorizontal(); if (GUILayout.Button("Floors")) Change("Paint", () => fl.areas.ForEach(a => a.top = m_Mat)); if (GUILayout.Button("Walls")) Change("Paint", () => fl.walls.ForEach(q => { q.front = m_Mat; q.back = m_Mat; })); if (GUILayout.Button("Ceilings")) Change("Paint", () => fl.areas.ForEach(a => a.under = m_Mat)); GUILayout.EndHorizontal(); }
        }
        static List<Material> StudioMaterials()
        {
            var dirs = new HashSet<string>(AssetDatabase.FindAssets("t:TextureRecipe").Select(g => Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(g)).Replace('\\', '/'))); if (AssetDatabase.IsValidFolder("Assets/Textures")) dirs.Add("Assets/Textures");
            return dirs.Count == 0 ? new List<Material>() : AssetDatabase.FindAssets("t:Material", dirs.ToArray()).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m).Distinct().Take(60).ToList();
        }

        // ------------------------------------------------------------------ parts

        void PartsTab(float w)
        {
            if (m_Parts == null || Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup - m_StudioAt > 10) m_Parts = AssetDatabase.FindAssets("t:BuildPart").Select(g => AssetDatabase.LoadAssetAtPath<BuildPart>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p).OrderBy(p => p.category).ThenBy(p => p.name).ToList();
            GUILayout.Label("Click one, then click in the view to put it there (Parts, key 9). R turns it.", m_Hint); if (m_Parts.Count == 0) GUILayout.Label("No parts yet: make one below.", m_Hint);
            int cols = Mathf.Max(2, Mathf.FloorToInt(w / 90)); float cw = (w - (cols - 1) * 6) / cols; string lastCat = null;
            for (int i = 0; i < m_Parts.Count;)
            {
                var cat = m_Parts[i].category; if (cat != lastCat) { GUILayout.Label(cat.ToUpper(), m_Head); lastCat = cat; } var row = GUILayoutUtility.GetRect(w, cw + 26); int k = 0;
                while (i < m_Parts.Count && k < cols && m_Parts[i].category == cat)
                {
                    var p = m_Parts[i]; var id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)); var r = new Rect(row.x + k * (cw + 6), row.y, cw, cw + 22); bool on = m_Part == id; EditorGUI.DrawRect(r, on ? Accent : new Color(1, 1, 1, 0.05f)); var pic = new Rect(r.x + 4, r.y + 4, cw - 8, cw - 8);
                    var prev = p.model ? AssetPreview.GetAssetPreview(p.model) : null; if (prev) GUI.DrawTexture(pic, prev, ScaleMode.ScaleToFit); else GUI.Label(pic, Glyph(p), new GUIStyle(m_Big) { fontSize = 30, normal = { textColor = on ? Color.black : Color.white } });
                    GUI.Label(new Rect(r.x + 2, r.yMax - 20, r.width - 4, 18), p.name, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, normal = { textColor = on ? Color.black : Color.white } });
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { m_Part = id; SetTool(Tool.Props); m_Tab = 2; m_Making = false; }
                    i++; k++;
                }
            }
            GUILayout.Space(8); var picked = BuildingMaker.Part(m_Part);
            if (picked && !m_Making) { GUILayout.Label("THE ONE YOU PICKED", m_Head); EditorGUI.BeginChangeCheck(); var so = new SerializedObject(picked); so.Update(); var it = so.GetIterator(); it.NextVisible(true); while (it.NextVisible(false)) EditorGUILayout.PropertyField(it, true); if (so.ApplyModifiedProperties() || EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(picked); Remake(); } GUILayout.Space(6); }
            if (!m_Making) { if (GUILayout.Button("+ Make a new part", GUILayout.Height(28))) m_Making = true; return; }
            MakePart(w);
        }
        static string Glyph(BuildPart p) => !p ? "?" : p.shape == BuildPart.Shape.Box ? "■" : p.shape == BuildPart.Shape.Pillar ? "▮" : p.shape == BuildPart.Shape.Wedge ? "◢" : p.shape == BuildPart.Shape.Stairs ? "⩨" : p.shape == BuildPart.Shape.Arch ? "∩" : p.shape == BuildPart.Shape.Beam ? "━" : p.shape == BuildPart.Shape.Fence ? "≡" : "◆";

        void MakePart(float w)
        {
            var box = EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 8, 6, 8) }); if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(box, new Color(1, 1, 1, 0.04f));
            GUILayout.Label("A NEW PART", m_Head); m_NewName = EditorGUILayout.TextField("Name", m_NewName); m_NewCat = EditorGUILayout.TextField(new GUIContent("Group", "Props, Furniture, Doors... parts are sorted by it"), m_NewCat);
            GUILayout.Label("Made from", m_Head); GUILayout.BeginHorizontal(); int n = 0; foreach (BuildPart.Shape s in Enum.GetValues(typeof(BuildPart.Shape))) { if (n++ == 4) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } if (Pill(s == BuildPart.Shape.MyModel ? "My model" : s.ToString(), m_NewShape == s)) m_NewShape = s; } GUILayout.EndHorizontal();
            if (m_NewShape == BuildPart.Shape.MyModel) { var g = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Model", "A model or prefab"), m_NewModel, typeof(GameObject), false); if (g != m_NewModel) { m_NewModel = g; if (g) { m_NewName = g.name; var o = (GameObject)PrefabUtility.InstantiatePrefab(g); var bb = BuildingMaker.Bounds(o); m_NewSize = bb.size; DestroyImmediate(o); } } GUILayout.Label("Its size: " + M(m_NewSize.x) + " x " + M(m_NewSize.y) + " x " + M(m_NewSize.z), m_Hint); }
            else { m_NewSize = EditorGUILayout.Vector3Field(new GUIContent("Size", "Width, height, depth"), m_NewSize); if (m_NewShape == BuildPart.Shape.Pillar || m_NewShape == BuildPart.Shape.Arch) m_NewSides = EditorGUILayout.IntSlider(new GUIContent("Faces round it", "0 uses the building's setting"), m_NewSides, 0, 24); }
            m_NewMat = (Material)EditorGUILayout.ObjectField(new GUIContent("Material", "Empty: its own (a model) or the Stairs and trims slot (a shape)"), m_NewMat, typeof(Material), false);
            GUILayout.BeginHorizontal(); GUILayout.Label("Collider", GUILayout.Width(106)); foreach (BuildPart.Collide c in Enum.GetValues(typeof(BuildPart.Collide))) if (Pill(c == BuildPart.Collide.Box ? "Box" : c == BuildPart.Collide.Exact ? "Exact" : "None", m_NewCollide == c)) m_NewCollide = c; GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Snaps to", GUILayout.Width(106)); foreach (BuildPart.Sits c in Enum.GetValues(typeof(BuildPart.Sits))) if (Pill(c == BuildPart.Sits.Middle ? "Middle" : c == BuildPart.Sits.Edge ? "A wall" : "A corner", m_NewSits == c)) m_NewSits = c; GUILayout.EndHorizontal();
            GUILayout.Space(4); GUILayout.BeginHorizontal(); var old = GUI.backgroundColor; GUI.backgroundColor = Accent; using (new EditorGUI.DisabledScope(m_NewShape == BuildPart.Shape.MyModel && !m_NewModel)) if (GUILayout.Button("Save it", GUILayout.Height(26))) SavePart(); GUI.backgroundColor = old; if (GUILayout.Button("Cancel", GUILayout.Height(26), GUILayout.Width(70))) m_Making = false; GUILayout.EndHorizontal();
            GUILayout.Label("Saved in Assets/BuildParts. To change its texture, use Texture Studio on its material.", m_Hint); EditorGUILayout.EndVertical();
        }
        public BuildPart SavePart()
        {
            const string dir = "Assets/BuildParts"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "BuildParts"); var p = CreateInstance<BuildPart>(); p.shape = m_NewShape; p.model = m_NewModel; p.size = m_NewSize; p.material = m_NewMat; p.collide = m_NewCollide; p.sits = m_NewSits; p.sides = m_NewSides; p.category = string.IsNullOrWhiteSpace(m_NewCat) ? "Props" : m_NewCat.Trim();
            var name = string.Join("_", (m_NewName.Length > 0 ? m_NewName : "Part").Split(Path.GetInvalidFileNameChars())); var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + ".asset"); AssetDatabase.CreateAsset(p, path); AssetDatabase.SaveAssets(); m_Parts = null; m_Part = AssetDatabase.AssetPathToGUID(path); SetTool(Tool.Props); m_Making = false; ShowNotification(new GUIContent("Saved " + p.name + ": click in the view to put it")); return p;
        }

        // ------------------------------------------------------------------ the building's own settings

        bool Section(string title, string tip)
        {
            string key = "HienaDev.Building.section." + title; bool open = SessionState.GetBool(key, false); var r = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(r, r.Contains(Event.current.mousePosition) ? PanelHi : new Color(1, 1, 1, 0.07f));
            GUI.Label(new Rect(r.x + 6, r.y + 3, r.width - 12, 16), (open ? "\u25BE  " : "\u25B8  ") + title, new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = open ? Accent : Color.white } }); if (GUI.Button(r, new GUIContent("", tip), GUIStyle.none)) { open = !open; SessionState.SetBool(key, open); }
            GUILayout.Space(open ? 4 : 2); return open;
        }
        void BuildingTab(float w)
        {
            var b = m_B; Undo.RecordObject(b, "Building: settings"); EditorGUI.BeginChangeCheck();
            if (Section("Low-poly and PSX look", "How many faces round things get, the texture size and the old-console look")) { b.roundSides = EditorGUILayout.IntSlider(new GUIContent("Faces on round things", "The low-poly setting: arches, round columns, spiral stairs, round rooms. 3 is very low-poly, 16 is smooth"), b.roundSides, 2, 24); b.textureSize = EditorGUILayout.Slider(new GUIContent("Texture size", "Units one texture covers: they run on from wall to wall"), b.textureSize, 0.25f, 10f); b.psx = EditorGUILayout.ToggleLeft(new GUIContent(" PSX look", "Corners that jump to the old screen's few pixels, textures that slide on faces, hard pixels, few colours and light per corner. Your materials aren't changed: copies are made in Assets/Buildings/PSX"), b.psx);
            if (b.psx) { b.psxWobble = EditorGUILayout.Slider(new GUIContent("Wobble", "0 none, 0.5 like the old console's 320 pixels, 1 very jumpy"), b.psxWobble, 0, 1); b.psxSwim = EditorGUILayout.Slider(new GUIContent("Texture swim", "Textures that slide as you turn"), b.psxSwim, 0, 1); b.psxColours = EditorGUILayout.Slider(new GUIContent("Fewer colours", "With a dotted pattern between them"), b.psxColours, 0, 1); } GUILayout.Space(6); }
            if (Section("Sizes", "The grid, floor heights and thicknesses")) { b.cell = EditorGUILayout.Slider(new GUIContent("Square size", "The grid. Snap can be 1/2 or 1/4 of it"), b.cell, 0.25f, 6f); b.floorHeight = EditorGUILayout.Slider(new GUIContent("Floor to floor", "Each floor can have its own on the left"), b.floorHeight, 1.5f, 8f); b.wallThickness = EditorGUILayout.Slider("Wall thickness", b.wallThickness, 0.02f, 1f); b.slabThickness = EditorGUILayout.Slider("Floor thickness", b.slabThickness, 0.02f, 1f); GUILayout.Space(6); }
            if (Section("Doors, windows and trims", "Frames, skirting, crown, wainscot and doors that open")) { b.trimWidth = EditorGUILayout.Slider(new GUIContent("Trim width", "Frames round doors and windows"), b.trimWidth, 0, 0.3f); b.skirtingHeight = EditorGUILayout.Slider("Skirting height", b.skirtingHeight, 0.03f, 0.4f); b.crownHeight = EditorGUILayout.Slider("Crown height", b.crownHeight, 0.03f, 0.4f); b.wainscotHeight = EditorGUILayout.Slider("Wainscot height", b.wainscotHeight, 0.3f, 2f); b.trimMat = EditorGUILayout.IntSlider(new GUIContent("Trim material", "Its slot number - 1"), b.trimMat, 0, Mathf.Max(3, b.materials.Count - 1)); b.doorLeaves = EditorGUILayout.Toggle(new GUIContent("Doors that open", "New doors get a leaf that swings open in the game"), b.doorLeaves); GUILayout.Space(6); }
            if (Section("Ground and walking", "The foundation and the slopes on stairs")) { b.foundation = EditorGUILayout.Slider(new GUIContent("Foundation", "Walls and floor go this far down, further where the ground under them is lower"), b.foundation, 0, 5); b.smoothStairs = EditorGUILayout.Toggle(new GUIContent("Smooth stairs", "Characters walk up a hidden slope instead of bumping up each step"), b.smoothStairs); GUILayout.Space(6); }
            if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(b); Remake(); }
            if (Section("Railings (all of them)", "How every railing looks unless one has its own")) { GUILayout.Label("How every railing looks: stairs, balconies, floor edges. Pick one railing's stairs or floor to give just that one its own look.", m_Hint); Undo.RecordObject(b, "Building: railings"); if (RailGUI(b.railing, false)) { EditorUtility.SetDirty(b); Remake(); } GUILayout.Space(6); }
            if (Section("Your own pieces", "Swap pieces the builder makes for your models or materials")) {             GUILayout.Label("Swap a piece the builder makes for your own model (put in its place, stretched to fit when Fit is on), or just give it another material. Empty: the builder's own.", m_Hint);
            foreach (Building.PieceKind k in System.Enum.GetValues(typeof(Building.PieceKind)))
            {
                var sw = b.swaps.FirstOrDefault(q => q.kind == k); if ((k == Building.PieceKind.RailPost || k == Building.PieceKind.Handrail) && sw == null) continue; bool matOnly = k == Building.PieceKind.StepTop || k == Building.PieceKind.Glass || k == Building.PieceKind.Roof; string nm = k == Building.PieceKind.StepTop ? "Step tops" : k == Building.PieceKind.RailPost ? "Railing posts" : k == Building.PieceKind.Handrail ? "Handrails" : k == Building.PieceKind.DoorLeaf ? "Doors" : k == Building.PieceKind.Window ? "Windows (whole)" : k == Building.PieceKind.LadderRung ? "Ladder rungs" : k == Building.PieceKind.Roof ? "Roofs" : k + "s";
                EditorGUILayout.BeginHorizontal(); GUILayout.Label(nm, GUILayout.Width(96)); GameObject mo = sw?.model; Material ma = sw?.material; bool fit = sw == null || sw.fit;
                if (!matOnly) mo = (GameObject)EditorGUILayout.ObjectField(mo, typeof(GameObject), false, GUILayout.MinWidth(60)); ma = (Material)EditorGUILayout.ObjectField(ma, typeof(Material), false, GUILayout.MinWidth(60)); if (!matOnly && mo) fit = GUILayout.Toggle(fit, new GUIContent("Fit", "Stretch it to the piece's size"), GUILayout.Width(36)); EditorGUILayout.EndHorizontal();
                if (mo != sw?.model || ma != sw?.material || sw != null && fit != sw.fit) { var kk = k; var m2 = mo; var a2 = ma; var f2 = fit; Change("Your own piece", () => { b.swaps.RemoveAll(q => q.kind == kk); if (m2 || a2) b.swaps.Add(new Building.Swap { kind = kk, model = m2, material = a2, fit = f2 }); }); GUIUtility.ExitGUI(); }
            }
            GUILayout.Space(6); }
            if (Section("The person (walk mode and P)", "Their height and your player model, to compare sizes")) { m_ShowPerson = EditorGUILayout.Toggle(new GUIContent("Show a person", "P puts them where the mouse is"), m_ShowPerson); m_PersonHeight = EditorGUILayout.Slider(new GUIContent("Height", "Walk mode's eyes are just under this"), m_PersonHeight, 0.5f, 3f); var g = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Your player", "Shown when you press P, and its height is used for walking. Empty: an orange capsule"), m_Person, typeof(GameObject), false); if (g != m_Person) { m_Person = g; if (g) { var sz = LocalSize(g); if (sz.y > 0.3f) m_PersonHeight = sz.y; } } m_WalkSpeed = EditorGUILayout.Slider("Walk speed", m_WalkSpeed, 1f, 10f); GUILayout.Space(6); }
            if (Section("Styles", "Save this building's look, use it on another")) {             var styles = AssetDatabase.FindAssets("t:BuildingStyle").Select(g => AssetDatabase.LoadAssetAtPath<BuildingStyle>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x).ToList();
            foreach (var st in styles) { GUILayout.BeginHorizontal(); GUILayout.Label(st.name); if (GUILayout.Button("Use it", EditorStyles.miniButton, GUILayout.Width(60))) UseStyle(st); GUILayout.EndHorizontal(); }
            GUILayout.BeginHorizontal(); m_StyleName = EditorGUILayout.TextField(m_StyleName); if (GUILayout.Button("Save this look", GUILayout.Width(110))) { SaveStyle(m_StyleName); ShowNotification(new GUIContent("Saved " + m_StyleName)); } GUILayout.EndHorizontal();
            GUILayout.Space(6); }
            if (Section("For the game", "NavMesh, prefab, floor plans, finishing")) {
            if (GUILayout.Button(new GUIContent("Make a NavMesh", "Needs the AI Navigation package. Walks up stairs (their slopes) and through doorways"))) ShowNotification(new GUIContent(BakeNavMesh()));
            { float ar = AgentRadius(); if (ar > 0.32f) { GUILayout.Label("Your NavMesh agent is " + M(ar * 2) + " wide (Navigation, Agents): too wide for 1 unit doors and stairs.", m_Hint); if (GUILayout.Button(new GUIContent("Make the agent thinner (0.6 wide)", "Sets the first agent's radius to 0.3 in your project's Navigation settings. Bake again after"))) { SetAgentRadius(0.3f); ShowNotification(new GUIContent(BakeNavMesh())); } } }
            if (GUILayout.Button(new GUIContent("Save it as a prefab", "Meshes saved with lightmap UVs, in Assets/Buildings/Prefabs"))) { var p = Export(); EditorGUIUtility.PingObject(p); }
            GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Plan of " + Building.LevelName(m_Level), "A PNG in Assets/Buildings/Plans: shown, and copied to paste anywhere"))) ShowPlan(FloorPlan(m_Level)); if (GUILayout.Button(new GUIContent("Plan of every floor", "All floors side by side, with their names"))) ShowPlan(AllPlans()); GUILayout.EndHorizontal();
            GUILayout.Space(4); if (GUILayout.Button(new GUIContent("Finish it (take the plan off)", "The Building component does nothing while the game runs (no Update, a few kilobytes of plan), so it's fine to leave it. Finishing takes it off for good, keeping the meshes"))) Finish(); GUILayout.Space(6); }
        }
        [SerializeField] string m_StyleName = "My style";

        // ------------------------------------------------------------------ rooms

        void RoomsTab(float w)
        {
            int hoverWas = m_HoverRoom; if (Event.current.type == EventType.Repaint) m_HoverRoom = -1;
            var fl = m_B.FloorAt(m_Level); GUILayout.Label("ROOMS ON " + Building.LevelName(m_Level).ToUpper(), m_Head); GUILayout.Label("Each floor rectangle is a room. Name them, paint them, save them to stamp again, or save each as its own prefab with connectors at its doorways (for levels put together at random).", m_Hint);
            if (fl != null) for (int i = 0; i < fl.areas.Count; i++)
                {
                    var a = fl.areas[i]; if (a.hole) continue; var z = Vector2.Max(a.min, a.max) - Vector2.Min(a.min, a.max); bool sel = m_Sel.kind == SelKind.Area && m_Sel.i == i && m_Sel.level == m_Level; var box = EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(6, 6, 4, 6), margin = new RectOffset(0, 0, 2, 2) }); bool hov = box.Contains(Event.current.mousePosition); if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(box, sel ? new Color(Accent.r, Accent.g, Accent.b, 0.25f) : hov ? new Color(0.4f, 0.8f, 1f, 0.18f) : new Color(1, 1, 1, 0.04f)); if (hov) m_HoverRoom = i; }
                    GUILayout.BeginHorizontal(); var nm = EditorGUILayout.TextField(a.name.Length > 0 ? a.name : "", GUILayout.Width(120)); if (nm != a.name) { int k = i; Change("Name a room", () => fl.areas[k].name = nm); } GUILayout.Label(M(z.x) + " x " + M(z.y) + "  " + M(z.x * z.y) + " m\u00B2", m_Hint); if (GUILayout.Button("Pick", EditorStyles.miniButton, GUILayout.Width(36))) { m_Sel = new Sel { kind = SelKind.Area, level = m_Level, i = i }; m_Tool = Tool.Select; } GUILayout.EndHorizontal();
                    GUILayout.Label("Paint it with slot " + (m_Mat + 1) + " (" + MatName(m_Mat) + "):", m_Hint); GUILayout.BeginHorizontal(); int kk = i; if (GUILayout.Button("Floor", EditorStyles.miniButton)) PaintRoom(fl, fl.areas[kk], m_Mat, 0); if (GUILayout.Button("Walls", EditorStyles.miniButton)) PaintRoom(fl, fl.areas[kk], m_Mat, 1); if (GUILayout.Button("Ceiling", EditorStyles.miniButton)) PaintRoom(fl, fl.areas[kk], m_Mat, 2); GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal(); if (GUILayout.Button("Save to stamp", EditorStyles.miniButton)) { SaveRoom(fl, fl.areas[kk]); ShowNotification(new GUIContent("Saved: stamp it with O")); } if (GUILayout.Button("Save as a prefab", EditorStyles.miniButton)) { var p = Export(fl.areas[kk]); EditorGUIUtility.PingObject(p); } GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); if (GUILayout.Button("Skirting all round", EditorStyles.miniButton)) { var ar = fl.areas[kk]; var lo = Vector2.Min(ar.min, ar.max) - Vector2.one * 0.01f; var hi = Vector2.Max(ar.min, ar.max) + Vector2.one * 0.01f; var mid = (lo + hi) / 2; Change("Skirting", () => { foreach (var wl in fl.walls.Where(q => Inside(q.a, lo, hi) && Inside(q.b, lo, hi))) { var d = (wl.b - wl.a).normalized; var fr = Vector3.Cross(new Vector3(d.x, 0, d.y), Vector3.up); if (Vector2.Dot(mid - (wl.a + wl.b) / 2, new Vector2(fr.x, fr.z)) > 0) wl.frontFinish |= Building.Finish.Skirting; else wl.backFinish |= Building.Finish.Skirting; } }); } GUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                }
            GUILayout.Label("Point at a room here and it lights up blue in the view.", m_Hint);
            if (fl != null && GUILayout.Button(new GUIContent("Shuffle the rooms", "Puts this floor's rooms together another way, each against another with a doorway between. Ctrl+Z puts them back"))) { Shuffle(fl, UnityEngine.Random.Range(1, 999999)); GUIUtility.ExitGUI(); }
            GUILayout.Space(8); GUILayout.Label("SAVED ROOMS: click one, then click in the view (O)", m_Head); var rooms = AssetDatabase.FindAssets("t:BuildingRoom").Select(g => AssetDatabase.LoadAssetAtPath<BuildingRoom>(AssetDatabase.GUIDToAssetPath(g))).Where(r => r && !r.name.EndsWith(" meshes")).ToList(); if (rooms.Count == 0) GUILayout.Label("None yet.", m_Hint);
            foreach (var r in rooms) if (Pill(r.name + "  (" + M(r.size.x) + " x " + M(r.size.y) + ")", m_Room == r)) { m_Room = r; SetTool(Tool.Stamp); }
            if (Event.current.type == EventType.Repaint && m_HoverRoom != hoverWas) Repaint();
        }
    }
}
