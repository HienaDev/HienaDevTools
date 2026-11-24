using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Columns, beams, stamped rooms, building on top of stairs, tuning with keys, measurements, a person for scale,
    /// walking round it, and saving it all (styles, rooms, prefabs, a floor plan, a NavMesh).</summary>
    public partial class BuildingWindow
    {
        [SerializeField] float m_ColSize = 0.3f, m_BeamW = 0.2f, m_BeamD = 0.3f; [SerializeField] int m_ColSides, m_ShaftUp = 2; [SerializeField] bool m_ColUp, m_OnTop = true, m_Measure; [SerializeField] BuildingRoom m_Room;
        [SerializeField] GameObject m_Person; [SerializeField] float m_PersonHeight = 1.8f; [SerializeField] Vector2 m_PersonAt = new Vector2(-1, -1); [SerializeField] bool m_ShowPerson = true; [SerializeField] int m_PersonLevel;

        // ------------------------------------------------------------------ what's under the mouse: stairs, landings, raised floors

        /// <summary>How high above this floor the top of whatever is under that spot is (stairs, a landing, a raised floor), or 0.</summary>
        float Surface(Vector2 u)
        {
            if (!m_OnTop) return 0; float y0 = m_B.Y(m_Level), H = m_B.HeightOf(m_Level); var from = Tr.TransformPoint(new Vector3(u.x, y0 + H - m_B.slabThickness - 0.05f, u.y)); var ray = new Ray(from, -Tr.up); float best = 0;
            foreach (var c in m_B.GetComponentsInChildren<Collider>()) { var made = c.GetComponentInParent<BuildingMade>(); if (made && made.level != m_Level) continue; if (c.Raycast(ray, out var h, H)) { float lift = Tr.InverseTransformPoint(h.point).y - y0; if (lift > best + 0.02f) best = lift; } }
            return best;
        }

        void ColumnTool(Event e, bool over, bool have, Vector2 su, Vector2 u, float y0)
        {
            if (!over || !have) return; var at = m_Snap == 0 ? new Vector2(Mathf.Round(u.x / m_B.cell) * m_B.cell, Mathf.Round(u.y / m_B.cell) * m_B.cell) : su; float lift = Surface(at);
            if (e.type == EventType.Repaint) { var cl = new Building.Column { at = at, size = m_ColSize, sides = m_ColSides, lift = lift }; SetGhost("col" + JsonUtility.ToJson(cl), () => { var p = Part(); p.columns.Add(cl); return p; }); m_Status = "click to put a column" + (m_ColUp ? " on every floor above too" : ""); }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { var c = new Building.Column { at = at, size = m_ColSize, sides = m_ColSides, lift = lift }; bool up = m_ColUp; Change("Add a column", () => { m_B.FloorAt(m_Level, true).columns.Add(c); if (up) foreach (var f in m_B.floors.Where(f => f.level > m_Level)) f.columns.Add(new Building.Column { at = c.at, size = c.size, sides = c.sides }); }); e.Use(); }
        }
        void BeamTool(Event e, int id, bool over, bool have, Vector2 su, float y0, float H)
        {
            float top = y0 + H - m_B.slabThickness;
            if (e.type == EventType.MouseDown && e.button == 0 && over && have && !e.alt) { m_Drag = Grab.Wall; m_From = m_To = su; GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { m_To = su; e.Use(); }
            if (e.type == EventType.Repaint && m_Drag == Grab.Wall && (m_To - m_From).sqrMagnitude > 1e-4f) { var bm = new Building.Beam { a = m_From, b = m_To, width = m_BeamW, depth = m_BeamD }; SetGhost("beam" + JsonUtility.ToJson(bm), () => { var p = Part(); p.beams.Add(bm); return p; }); }
            if (e.type == EventType.Repaint && (over || m_Drag == Grab.Wall)) { if (m_Drag == Grab.Wall) { Line(L(m_From, top - m_BeamD), L(m_To, top - m_BeamD), Add, 6); Line(L(m_From, y0), L(m_To, y0), new Color(Add.r, Add.g, Add.b, 0.4f), 2); Text(L((m_From + m_To) / 2, top), M((m_To - m_From).magnitude)); } else if (have) Dot(L(su, top - m_BeamD), Add, 5); m_Status = "drag a beam under the ceiling"; }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_Drag = Grab.None; var a = m_From; var b = m_To; e.Use(); if ((b - a).sqrMagnitude < 1e-4f) return; Change("Add a beam", () => m_B.FloorAt(m_Level, true).beams.Add(new Building.Beam { a = a, b = b, width = m_BeamW, depth = m_BeamD })); }
        }

        // ------------------------------------------------------------------ rooms you saved

        static Vector2 Turn(Vector2 v, int quarter) { for (int i = 0; i < ((quarter % 4) + 4) % 4; i++) v = new Vector2(v.y, -v.x); return v; }
        void StampTool(Event e, bool over, bool have, Vector2 su, float y0)
        {
            if (!over || !have) return; if (!m_Room) { if (e.type == EventType.Repaint) m_Status = "pick a saved room on the right (Rooms)"; return; } int q = Mathf.RoundToInt(m_Turn / 90f);
            Vector2 P(Vector2 v) => su + Turn(v, q);
            if (e.type == EventType.Repaint) { foreach (var a in m_Room.areas) RectAt(P(a.min), P(a.max), y0 + 0.03f, a.hole ? Take : Add, 0.2f); float h = m_B.HeightOf(m_Level) - m_B.slabThickness; foreach (var w in m_Room.walls) { Line(L(P(w.a), y0), L(P(w.b), y0), Add, 4); Line(L(P(w.a), y0 + h), L(P(w.b), y0 + h), Add, 1.5f); } m_Status = "click to put " + m_Room.name + " (R turns it)"; }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { var room = m_Room; Change("Stamp " + room.name, () => Stamp(m_B.FloorAt(m_Level, true), room, su, q)); e.Use(); }
        }
        static T Copy<T>(T x) => JsonUtility.FromJson<T>(JsonUtility.ToJson(x));
        public static void Stamp(Building.Floor f, BuildingRoom r, Vector2 at, int q)
        {
            Vector2 P(Vector2 v) => at + Turn(v, q);
            foreach (var a in r.areas) { var c = Copy(a); c.Move(P); f.areas.Add(c); }
            foreach (var w in r.walls) { var c = Copy(w); c.a = P(w.a); c.b = P(w.b); if (c.openings.Count == 0 && Mathf.Abs(c.bend) < 0.01f) AddWallLine(f, c.a, c.b); else { RemoveWallLine(f, c.a, c.b); f.walls.Add(c); } }
            foreach (var s in r.stairs) { var c = Copy(s); c.at = P(s.at); c.turn = Mathf.Repeat(s.turn + q * 90, 360); f.stairs.Add(c); }
            foreach (var p in r.pieces) { var c = Copy(p); c.at = P(p.at); c.turn = Mathf.Repeat(p.turn + q * 90, 360); f.pieces.Add(c); }
            foreach (var cl in r.columns) { var c = Copy(cl); c.at = P(cl.at); f.columns.Add(c); }
            Join(f);
        }
        /// <summary>A room (a floor rectangle) and everything on it or on its edges, kept as an asset to stamp again.</summary>
        public BuildingRoom SaveRoom(Building.Floor f, Building.Area a, string folder = "Assets/Buildings/Rooms")
        {
            var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); bool In(Vector2 p) => Inside(p, lo, hi);
            var r = CreateInstance<BuildingRoom>(); r.size = hi - lo; r.areas.Add(Copy(a)); r.areas[0].Move(p => p - lo);
            foreach (var w in f.walls.Where(w => In(w.a) && In(w.b))) { var c = Copy(w); c.a -= lo; c.b -= lo; r.walls.Add(c); }
            foreach (var s in f.stairs.Where(s => In(s.at))) { var c = Copy(s); c.at -= lo; r.stairs.Add(c); } foreach (var p in f.pieces.Where(p => In(p.at))) { var c = Copy(p); c.at -= lo; r.pieces.Add(c); } foreach (var cl in f.columns.Where(c => In(c.at))) { var c = Copy(cl); c.at -= lo; r.columns.Add(c); }
            Folder(folder); var name = (a.name.Length > 0 ? a.name : "Room"); var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Clean(name) + ".asset"); AssetDatabase.CreateAsset(r, path); AssetDatabase.SaveAssets(); m_Room = r; return r;
        }
        static void Folder(string path) { var parts = path.Split('/'); string cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var n = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(n)) AssetDatabase.CreateFolder(cur, parts[i]); cur = n; } }
        static string Clean(string s) { var c = string.Join("_", s.Split(Path.GetInvalidFileNameChars())).Trim(); return c.Length > 0 ? c : "Building"; }

        // ------------------------------------------------------------------ painting stairs, columns, beams and whole rooms

        bool PaintThings(Vector3 l, Vector3 n, Vector2 u, bool pick, int mat)
        {
            var fl = m_B.FloorAt(m_B.LevelAt(l.y - 0.02f)); if (fl == null) return false; float y0 = m_B.Y(fl.level), H = m_B.HeightOf(fl.level); var e = Event.current;
            foreach (var s in fl.stairs) { var r = BuildingMaker.Footprint(s); r.xMin -= 0.06f; r.yMin -= 0.06f; r.xMax += 0.06f; r.yMax += 0.06f; if (r.Contains(u) && l.y <= y0 + s.lift + s.rise * H + 1.0f && l.y >= y0 + s.lift - 0.02f) { if (pick) { m_Mat = s.mat; Repaint(); } else Change("Paint", () => s.mat = mat); return true; } }
            foreach (var c in fl.columns) if ((c.at - u).magnitude < c.size * 0.75f + 0.03f) { if (pick) { m_Mat = c.mat; Repaint(); } else Change("Paint", () => c.mat = mat); return true; }
            float top = y0 + H - m_B.slabThickness; foreach (var bm in fl.beams) if (l.y >= top - bm.depth - 0.02f && DistSeg(u, bm.a, bm.b) < bm.width) { if (pick) { m_Mat = bm.mat; Repaint(); } else Change("Paint", () => bm.mat = mat); return true; }
            // Ctrl: the whole room it's in (a floor: its floor; a wall: every wall side facing into it; a ceiling: its ceiling)
            if ((e.control || e.command) && !pick)
            {
                int what = n.y > 0.7f ? 0 : n.y < -0.7f ? 2 : 1; var lf = what == 2 ? m_B.FloorAt(m_B.LevelNear(l.y + m_B.slabThickness)) : fl; if (lf == null) return false; var pt = what == 1 ? u + new Vector2(n.x, n.z) * 0.3f : u;
                var room = lf.areas.LastOrDefault(a => !a.hole && Inside(pt, Vector2.Min(a.min, a.max), Vector2.Max(a.min, a.max))); if (room != null) { PaintRoom(lf, room, mat, what); return true; }
            }
            return false;
        }
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude)); return (p - (a + ab * k)).magnitude; }
        /// <summary>Paints a room: 0 its floor, 1 the wall sides that face into it, 2 its ceiling.</summary>
        public void PaintRoom(Building.Floor fl, Building.Area room, int mat, int what)
        {
            var lo = Vector2.Min(room.min, room.max); var hi = Vector2.Max(room.min, room.max); var mid = (lo + hi) / 2;
            Change("Paint a room", () =>
            {
                if (what == 0) room.top = mat; else if (what == 2) room.under = mat;
                else foreach (var w in fl.walls) { if (!Inside(w.a, lo - Vector2.one * 0.01f, hi + Vector2.one * 0.01f) || !Inside(w.b, lo - Vector2.one * 0.01f, hi + Vector2.one * 0.01f)) continue; var d = (w.b - w.a).normalized; var front = Vector3.Cross(new Vector3(d.x, 0, d.y), Vector3.up); bool faces = Vector2.Dot(mid - (w.a + w.b) / 2, new Vector2(front.x, front.z)) > 0; if (faces) w.front = mat; else w.back = mat; }
            });
        }

        // ------------------------------------------------------------------ tuning what's picked with keys

        void Tune(Event e)
        {
            var fl = m_B.FloorAt(m_Sel.level); var s = m_Sel; float st = SnapStep, H = m_B.HeightOf(s.level); int dir = e.keyCode == KeyCode.RightBracket || e.keyCode == KeyCode.Equals ? 1 : -1; bool size = e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket, high = e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.Equals, kind = e.keyCode == KeyCode.K;
            Change("Tune", () =>
            {
                switch (s.kind)
                {
                    case SelKind.Wall: { var w = fl.walls[s.i]; if (kind) { w.frontFinish = w.frontFinish == Building.Finish.None ? Building.Finish.Skirting : w.frontFinish == Building.Finish.Skirting ? Building.Finish.Skirting | Building.Finish.Crown : w.frontFinish == (Building.Finish.Skirting | Building.Finish.Crown) ? Building.Finish.Skirting | Building.Finish.Crown | Building.Finish.Wainscot : Building.Finish.None; w.backFinish = w.frontFinish; } else if (size && e.shift) w.thickness = Mathf.Max(0.02f, BuildingMaker.Thick(m_B, w) + dir * 0.05f); else if (size) { float h = w.height > 0 ? w.height : H - m_B.slabThickness; w.height = Mathf.Clamp(h + dir * st, st, H); } else if (high && e.shift) w.bend += dir * st; else if (high) w.bottom = Mathf.Max(0, w.bottom + dir * 0.1f); break; }
                    case SelKind.Opening: { var o = fl.walls[s.i].openings[s.j]; if (kind) o.kind = (Building.OpeningKind)(((int)o.kind + 1) % 4); else if (size && e.shift) o.top = Mathf.Clamp(o.top + dir * 0.1f, o.bottom + 0.1f, H); else if (size) o.width = Mathf.Max(0.1f, o.width + dir * st); else if (high) o.bottom = Mathf.Clamp(o.bottom + dir * 0.1f, 0, o.top - 0.1f); break; }
                    case SelKind.Stair: { var t = fl.stairs[s.i]; if (kind) t.kind = (Building.StairKind)(((int)t.kind + 1) % 7); else if (size && e.shift) t.width = Mathf.Max(0.3f, t.width + dir * st / 2); else if (size) t.length = Mathf.Max(0.5f, t.length + dir * st); else if (high && e.shift) t.lift = Mathf.Max(0, t.lift + dir * 0.1f); else if (high) t.rise = Mathf.Clamp(t.rise + dir * 0.25f, 0.25f, 2f); break; }
                    case SelKind.Piece: { var p = fl.pieces[s.i]; if (size) p.scale *= dir > 0 ? 1.1f : 1 / 1.1f; else if (high) p.lift += dir * 0.1f; break; }
                    case SelKind.Area: { var a = fl.areas[s.i]; var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); if (kind) a.hole = !a.hole; else if (size) { lo -= Vector2.one * dir * st; hi += Vector2.one * dir * st; if (hi.x - lo.x > 0.05f && hi.y - lo.y > 0.05f) { a.min = lo; a.max = hi; } } else if (high) a.lift += dir * (e.shift ? 0.05f : 0.25f); break; }
                    case SelKind.Column: { var c = fl.columns[s.i]; if (kind) c.sides = c.sides == 0 ? 6 : c.sides >= 16 ? 0 : c.sides + 2; else if (size) c.size = Mathf.Max(0.05f, c.size + dir * 0.05f); else if (high) c.lift = Mathf.Max(0, c.lift + dir * 0.1f); break; }
                    case SelKind.Beam: { var bm = fl.beams[s.i]; if (size && e.shift) bm.width = Mathf.Max(0.05f, bm.width + dir * 0.05f); else if (size) bm.depth = Mathf.Max(0.05f, bm.depth + dir * 0.05f); break; }
                }
            });
        }
        /// <summary>The same keys before you put something: they change what the tool will make.</summary>
        bool ToolTune(Event e)
        {
            int dir = e.keyCode == KeyCode.RightBracket || e.keyCode == KeyCode.Equals ? 1 : -1; bool size = e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket, high = e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.Equals, kind = e.keyCode == KeyCode.K, sh = e.shift; float st = SnapStep;
            switch (m_Tool)
            {
                case Tool.Door: if (kind) m_DoorKind = m_DoorKind == Building.OpeningKind.Door ? Building.OpeningKind.Arch : m_DoorKind == Building.OpeningKind.Arch ? Building.OpeningKind.Gap : Building.OpeningKind.Door; else if (size && sh) m_DoorH = Mathf.Clamp(m_DoorH + dir * 0.1f, 1f, 6f); else if (size) m_DoorW = Mathf.Clamp(m_DoorW + dir * st / 2, 0.3f, 6f); else if (high) m_DoorFill = !m_DoorFill; else return false; break;
                case Tool.Window: if (kind) m_WinArch = !m_WinArch; else if (size && sh) m_WinTop = Mathf.Clamp(m_WinTop + dir * 0.1f, m_WinBottom + 0.1f, 6f); else if (size) m_WinW = Mathf.Clamp(m_WinW + dir * st / 2, 0.2f, 6f); else if (high) { float h = m_WinTop - m_WinBottom; m_WinBottom = Mathf.Clamp(m_WinBottom + dir * 0.1f, 0, 5f); m_WinTop = m_WinBottom + h; } else return false; break;
                case Tool.Stairs: if (kind) m_StairKind = (Building.StairKind)(((int)m_StairKind + 1) % 7); else if (size && sh) m_StairW = Mathf.Clamp(m_StairW + dir * st / 2, 0.4f, 4f); else if (size) m_StairL = Mathf.Clamp(m_StairL + dir * st, 0.5f, 12f); else if (high && sh) m_StairRails = (Building.RailSides)(((int)m_StairRails + 1) % 5); else if (high) m_StairRise = Mathf.Clamp(m_StairRise + dir * 0.25f, 0.25f, 2f); else return false; break;
                case Tool.Column: if (kind) m_ColSides = m_ColSides == 0 ? 6 : m_ColSides >= 16 ? 0 : m_ColSides + 2; else if (size) m_ColSize = Mathf.Clamp(m_ColSize + dir * 0.05f, 0.1f, 2f); else return false; break;
                case Tool.Beam: if (size && sh) m_BeamW = Mathf.Clamp(m_BeamW + dir * 0.05f, 0.05f, 1f); else if (size) m_BeamD = Mathf.Clamp(m_BeamD + dir * 0.05f, 0.05f, 1.5f); else return false; break;
                case Tool.Props: if (high) m_Lift = Mathf.Clamp(m_Lift + dir * 0.1f, 0, 6); else return false; break;
                case Tool.Tunnel: if (size) m_TunnelWide = Mathf.Clamp(m_TunnelWide + dir, 1, 3); else return false; break;
                case Tool.Shaft: if (size) m_ShaftUp = Mathf.Clamp(m_ShaftUp + dir, 1, 20); else return false; break;
                default: return false;
            }
            Repaint(); SceneView.RepaintAll(); return true;
        }
        /// <summary>A strip at the top of the view: the tool you're on, what it will make, and its keys.</summary>
        /// <summary>The keys of the tool you're on, one per line.</summary>
        string[] ToolKeys() { var (_, keys) = ToolText(); return keys.Split(new[] { "   " }, StringSplitOptions.RemoveEmptyEntries); }
        void ToolHint()
        {
            var (what, keys) = ToolText();
            var st = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } }; var r = new Rect(m_V.rect.x + 136, m_V.rect.y + 8, Mathf.Min(m_V.rect.width - 144, st.CalcSize(new GUIContent(what + ":   " + keys)).x + 14), 20); if (m_V.scene) { r.x = m_V.rect.x + 8; r.y += 30; }
            EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.55f)); GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 8, 18), new GUIContent(what + ":   " + keys), st);
        }
        (string what, string keys) ToolText()
        {
            string what = "", keys = "";
            switch (m_Tool)
            {
                case Tool.Select: what = "Select"; keys = "click something to change it   drag moves   arrows, ring and square: move, turn, scale (Ctrl: no snap)   right-click: quick menu   Delete   Ctrl+D copy   arrows nudge"; break;
                case Tool.Room: case Tool.Floor: case Tool.Hole: case Tool.Balcony: what = Tools.First(q => q.tool == m_Tool).name + (m_Tool != Tool.Hole && m_Tool != Tool.Balcony && m_RoomShape == 1 ? " (round)" : m_Tool != Tool.Hole && m_Tool != Tool.Balcony && m_RoomShape == 2 ? " (your corners)" : ""); keys = (m_RoomShape == 2 && m_Tool != Tool.Hole && m_Tool != Tool.Balcony ? "click each corner   click the first again or Enter to finish" : "drag from corner to corner") + (m_Tool == Tool.Hole ? "" : "   Ctrl+drag takes away"); break;
                case Tool.Shaft: what = "Lift shaft, " + m_ShaftUp + " floors up"; keys = "drag a rectangle   [ ] floors up"; break;
                case Tool.Wall: what = Mathf.Abs(m_WallBend) > 0.01f ? "Curved wall (" + M(m_WallBend) + ")" : "Wall"; keys = "drag   Alt any angle   Ctrl+drag takes away   curve it on the left, or pick a wall and drag its middle dot"; break;
                case Tool.Door: what = (m_DoorKind == Building.OpeningKind.Gap ? "Open gap" : m_DoorKind.ToString()) + " " + M(m_DoorW) + " x " + M(m_DoorH) + (m_DoorKind == Building.OpeningKind.Door ? m_DoorFill ? ", with a door" : ", no door" : ""); keys = "click a wall, or drag along it for the width   [ ] width   Shift+[ ] height   - = door or not   K door/arch/gap"; break;
                case Tool.Window: what = (m_WinArch ? "Round window " : "Window ") + M(m_WinW) + " wide, " + M(m_WinBottom) + " to " + M(m_WinTop); keys = "click a wall, or drag along it   [ ] width   Shift+[ ] top   - = up and down   K round top"; break;
                case Tool.Stairs: what = m_StairKind + " stairs " + M(m_StairW) + " x " + M(m_StairL) + ", up " + M(m_StairRise) + " floor, railings " + SideNames[(int)m_StairRails].ToLower(); keys = "click, or drag for its length and way   R turn   [ ] length   Shift+[ ] width   - = how far up   Shift+- railings   K kind"; break;
                case Tool.Column: what = "Column " + M(m_ColSize) + (m_ColSides > 0 ? ", " + m_ColSides + " sides" : ", square"); keys = "click   [ ] size   K round"; break;
                case Tool.Beam: what = "Beam " + M(m_BeamW) + " x " + M(m_BeamD); keys = "drag under the ceiling   [ ] depth   Shift+[ ] width"; break;
                case Tool.Props: what = PartName(m_Part) + (m_Lift > 0 ? ", lifted " + M(m_Lift) : ""); keys = "click   R turn   - = lift   Ctrl+click takes away"; break;
                case Tool.Tunnel: what = "Tunnel " + m_TunnelWide + " wide"; keys = "drag a path   [ ] wider   Ctrl+drag takes away"; break;
                case Tool.Paint: what = "Paint with " + MatName(m_Mat); keys = "click   Shift both sides of a wall   Ctrl the whole room   Alt picks the material"; break;
                case Tool.Stamp: what = m_Room ? m_Room.name : "Stamp a room"; keys = "click   R turn"; break;
            }
            keys = keys.Replace("Shift+[ ]", "Alt+Shift+Left/Right").Replace("[ ]", "Alt+Left/Right").Replace("Shift+- =", "Alt+Shift+Up/Down").Replace("Shift+-", "Alt+Shift+Up/Down").Replace("- =", "Alt+Up/Down");
            return (what, keys + "   Esc or right-click cancels a drag   F1 all keys");
        }
        void TuneHint()
        {
            string h = m_Sel.kind == SelKind.Wall ? "Alt+Left/Right height   Alt+Shift+Left/Right thickness   Alt+Up/Down starts at   Alt+Shift+Up/Down curve (or drag its middle dot)   K skirting/crown/wainscot" : m_Sel.kind == SelKind.Opening ? "Alt+Left/Right width   Alt+Shift+Left/Right top   Alt+Up/Down bottom   K door/window/arch/gap   drag its dots" : m_Sel.kind == SelKind.Stair ? "Alt+Left/Right length   Alt+Shift+Left/Right width   Alt+Up/Down how far up   Alt+Shift+Up/Down start higher   K kind" : m_Sel.kind == SelKind.Piece ? "Alt+Left/Right scale   Alt+Up/Down lift" : m_Sel.kind == SelKind.Area ? "Alt+Left/Right grow   Alt+Up/Down raise or sink   K hole   drag corners, add one at an edge's middle dot, Ctrl-click one to take it away" : m_Sel.kind == SelKind.Column ? "Alt+Left/Right size   Alt+Up/Down lift   K round" : "Alt+Left/Right depth   Alt+Shift+Left/Right width";
            var r = new Rect(m_V.rect.x + 136, m_V.rect.y + 8, Mathf.Min(m_V.rect.width - 144, 980), 20); if (m_V.scene) { r.x = m_V.rect.x + 8; r.y += 30; } var sty = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } }; var txt = Describe(m_Sel) + ":   " + h + "   R turn   Del"; r.width = Mathf.Min(r.width, sty.CalcSize(new GUIContent(txt)).x + 14); EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.55f)); GUI.Label(new Rect(r.x + 6, r.y + 2, r.width, 18), txt, sty);
        }

        // ------------------------------------------------------------------ measurements

        void Measure(float y0, float H)
        {
            var fl = m_B.FloorAt(m_Level); if (fl == null) return; float h = H - m_B.slabThickness;
            foreach (var w in fl.walls) { var mid = (w.a + w.b) / 2; Line(L(w.a, y0 + h + 0.1f), L(w.b, y0 + h + 0.1f), Color.white, 1.5f); Text(L(mid, y0 + h + 0.15f), M(w.Length)); }
            foreach (var a in fl.areas.Where(a => !a.hole)) { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); var z = hi - lo; var cs = a.Corners(); Text(L(cs.Aggregate(Vector2.zero, (s, v) => s + v) / cs.Count, y0 + a.lift + 0.05f), (a.name.Length > 0 ? a.name + ": " : "") + (a.IsShape ? "" : M(z.x) + " x " + M(z.y) + " = ") + M(a.AreaSize()) + " m²"); }
            foreach (var w in fl.walls) foreach (var o in w.openings) { var d = (w.b - w.a).normalized; Text(L(w.a + d * o.at, y0 + o.top + 0.1f), M(o.width) + " x " + M(o.top - (o.kind == Building.OpeningKind.Door ? 0 : o.bottom))); }
        }

        /// <summary>The floor you're on, drawn from above as a picture: walls black, doors as gaps with their swing, stairs hatched, rooms named.</summary>
        public Texture2D FloorPlan(int level, string folder = "Assets/Buildings/Plans")
        {
            var fl = m_B.FloorAt(level); if (fl == null) return null; var pts = fl.areas.SelectMany(a => new[] { a.min, a.max }).Concat(fl.walls.SelectMany(w => new[] { w.a, w.b })).ToList(); if (pts.Count == 0) return null;
            var lo = pts.Aggregate(Vector2.Min) - Vector2.one; var hi = pts.Aggregate(Vector2.Max) + Vector2.one; const int ppu = 48; int W = Mathf.Clamp(Mathf.CeilToInt((hi.x - lo.x) * ppu), 64, 4096), Hh = Mathf.Clamp(Mathf.CeilToInt((hi.y - lo.y) * ppu), 64, 4096); var px = new Pixels(W, Hh); for (int i = 0; i < px.c.Length; i++) px.c[i] = Color.white;
            Vector2 G(Vector2 u) => (u - lo) * ppu;
            void Disc(Vector2 c, float r, Color col) { for (int y = Mathf.Max(0, (int)(c.y - r)); y <= Mathf.Min(Hh - 1, (int)(c.y + r)); y++) for (int x = Mathf.Max(0, (int)(c.x - r)); x <= Mathf.Min(W - 1, (int)(c.x + r)); x++) if ((new Vector2(x, y) - c).sqrMagnitude <= r * r) px.c[y * W + x] = col; }
            void Seg(Vector2 a, Vector2 b, float thick, Color col) { var ga = G(a); var gb = G(b); int n = Mathf.CeilToInt((gb - ga).magnitude) + 1; for (int i = 0; i <= n; i++) Disc(Vector2.Lerp(ga, gb, i / (float)n), thick, col); }
            foreach (var a in fl.areas) { var a0 = G(Vector2.Min(a.min, a.max)); var a1 = G(Vector2.Max(a.min, a.max)); for (int y = Mathf.Max(0, (int)a0.y); y < Mathf.Min(Hh, (int)a1.y); y++) for (int x = Mathf.Max(0, (int)a0.x); x < Mathf.Min(W, (int)a1.x); x++) if (!a.IsShape || a.Contains(lo + new Vector2(x + 0.5f, y + 0.5f) / ppu)) px.c[y * W + x] = a.hole ? Color.white : Mathf.Abs(a.lift) > 0.01f ? new Color(0.85f, 0.88f, 0.92f) : new Color(0.93f, 0.92f, 0.9f); }
            foreach (var s in fl.stairs) { var m = BuildingMaker.StairFrame(s); foreach (var f in BuildingMaker.Flights(s)) { int n = Mathf.Max(2, Mathf.RoundToInt((f.up.x != 0 ? f.r.width : f.r.height) / 0.28f)); for (int i = 0; i <= n; i++) { float k = i / (float)n; Vector2 A, B; if (f.up.x == 0) { A = new Vector2(f.r.xMin, f.r.yMin + f.r.height * k); B = new Vector2(f.r.xMax, A.y); } else { A = new Vector2(f.r.xMin + f.r.width * k, f.r.yMin); B = new Vector2(A.x, f.r.yMax); } var wa = m.MultiplyPoint3x4(new Vector3(A.x, 0, A.y)); var wb = m.MultiplyPoint3x4(new Vector3(B.x, 0, B.y)); Seg(new Vector2(wa.x, wa.z), new Vector2(wb.x, wb.z), 0.8f, new Color(0.4f, 0.4f, 0.45f)); } } }
            foreach (var c in fl.columns) Disc(G(c.at), c.size * ppu / 2, Color.black);
            foreach (var w in fl.walls)
            {
                float t = BuildingMaker.Thick(m_B, w) * ppu / 2 + 0.6f, len = w.Length; var d = (w.b - w.a) / Mathf.Max(1e-4f, len); float s0 = 0;
                foreach (var o in w.openings.OrderBy(o => o.at)) { float a = o.at - o.width / 2, c = o.at + o.width / 2; if (a > s0) Seg(w.a + d * s0, w.a + d * a, t, Color.black); s0 = c; if (o.kind == Building.OpeningKind.Window) Seg(w.a + d * a, w.a + d * c, 1f, new Color(0.3f, 0.5f, 0.9f)); if (o.kind == Building.OpeningKind.Door) { var hinge = w.a + d * a; var nrm = new Vector2(-d.y, d.x) * (o.swing == Building.DoorSwing.ToTheBack ? 1 : -1); for (int k = 0; k <= 16; k++) { float an = Mathf.PI / 2 * k / 16; Disc(G(hinge + (d * Mathf.Cos(an) + nrm * Mathf.Sin(an)) * o.width), 0.7f, new Color(0.5f, 0.5f, 0.5f)); } Seg(hinge, hinge + nrm * o.width, 0.8f, new Color(0.3f, 0.3f, 0.3f)); } }
                if (len > s0) Seg(w.a + d * s0, w.b, t, Color.black);
            }
            foreach (var a in fl.areas.Where(a => !a.hole)) { var z = Vector2.Max(a.min, a.max) - Vector2.Min(a.min, a.max); var txt = new TextArt { text = (a.name.Length > 0 ? a.name + "\n" : "") + M(z.x * z.y) + " m²" }.Draw(); if (txt != null) { var c = (Vector2.Min(a.min, a.max) + Vector2.Max(a.min, a.max)) / 2; TextureStamp.Put(px, txt, true, new Color(0.15f, 0.15f, 0.2f), new Vector2((c.x - lo.x) / (hi.x - lo.x), (c.y - lo.y) / (hi.y - lo.y)), Mathf.Min(0.25f, z.x * ppu * 0.6f / W), 0, false, 1, TextureStamp.How.Paint, 0, 0, 0.5f, 0.2f); } }
            Folder(folder); var path = folder + "/" + Clean(m_B.name) + " " + Clean(Building.LevelName(level)) + ".png"; return Pixels.SaveAt(px, path, null, false, false);
        }

        // ------------------------------------------------------------------ styles

        public BuildingStyle SaveStyle(string name)
        {
            var s = CreateInstance<BuildingStyle>(); var b = m_B; s.materials = new List<Material>(b.materials); s.floorHeight = b.floorHeight; s.wallThickness = b.wallThickness; s.slabThickness = b.slabThickness; s.textureSize = b.textureSize; s.roundSides = b.roundSides; s.doorLeaves = b.doorLeaves; s.psx = b.psx; s.psxWobble = b.psxWobble; var top = b.floors.OrderBy(f => f.level).LastOrDefault(); if (top != null) { s.roof = top.roof; s.roofPitch = top.roofPitch; s.roofOverhang = top.roofOverhang; } s.finish = b.floors.SelectMany(f => f.walls).Select(w => w.frontFinish).FirstOrDefault();
            Folder("Assets/Buildings/Styles"); var path = AssetDatabase.GenerateUniqueAssetPath("Assets/Buildings/Styles/" + Clean(name) + ".asset"); AssetDatabase.CreateAsset(s, path); AssetDatabase.SaveAssets(); return s;
        }
        public void UseStyle(BuildingStyle s)
        {
            Change("Use the style " + s.name, () => { var b = m_B; b.materials = new List<Material>(s.materials); b.floorHeight = s.floorHeight; b.wallThickness = s.wallThickness; b.slabThickness = s.slabThickness; b.textureSize = s.textureSize; b.roundSides = s.roundSides; b.doorLeaves = s.doorLeaves; b.psx = s.psx; b.psxWobble = s.psxWobble; var top = b.floors.OrderBy(f => f.level).LastOrDefault(); if (top != null) { top.roof = s.roof; top.roofPitch = s.roofPitch; top.roofOverhang = s.roofOverhang; } foreach (var w in b.floors.SelectMany(f => f.walls)) { w.frontFinish = s.finish; w.backFinish = s.finish; } });
        }

        // ------------------------------------------------------------------ facade helpers

        public void Spread(Building.Wall w, int count)
        {
            var like = w.openings.FirstOrDefault() ?? new Building.Opening { kind = Building.OpeningKind.Window, width = m_WinW, bottom = m_WinBottom, top = m_WinTop }; float len = w.Length;
            Change("Spread windows", () => { w.openings.Clear(); for (int i = 0; i < count; i++) { var c = Copy(like); c.at = len * (i + 0.5f) / count; w.openings.Add(c); } });
        }
        /// <summary>Flips this floor left to right (about its middle).</summary>
        public void Mirror(Building.Floor f)
        {
            var pts = f.areas.SelectMany(a => new[] { a.min, a.max }).Concat(f.walls.SelectMany(w => new[] { w.a, w.b })).ToList(); if (pts.Count == 0) return; float cx = (pts.Min(p => p.x) + pts.Max(p => p.x)) / 2; Vector2 F(Vector2 v) => new Vector2(2 * cx - v.x, v.y);
            Change("Mirror the floor", () =>
            {
                foreach (var a in f.areas) { a.Move(F); if (a.IsShape) a.shape.Reverse(); }
                foreach (var w in f.walls) { var na = F(w.b); var nb = F(w.a); float len = w.Length; w.a = na; w.b = nb; foreach (var o in w.openings) o.at = len - o.at; }
                foreach (var s in f.stairs) { s.at = F(s.at); s.turn = Mathf.Repeat(-s.turn, 360); if (s.kind == Building.StairKind.TurnLeft) s.kind = Building.StairKind.TurnRight; else if (s.kind == Building.StairKind.TurnRight) s.kind = Building.StairKind.TurnLeft; }
                foreach (var p in f.pieces) { p.at = F(p.at); p.turn = Mathf.Repeat(-p.turn, 360); } foreach (var c in f.columns) c.at = F(c.at); foreach (var bm in f.beams) { bm.a = F(bm.a); bm.b = F(bm.b); }
            });
        }

        // ------------------------------------------------------------------ for the game: a NavMesh, a prefab, rooms as prefabs

        public string BakeNavMesh()
        {
            var t = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation"); if (t == null) return "Add the AI Navigation package (Package Manager) first: it makes the NavMesh.";
            var s = m_B.GetComponent(t) ?? Undo.AddComponent(m_B.gameObject, t);
            var geo = t.GetProperty("useGeometry"); if (geo != null) geo.SetValue(s, Enum.ToObject(geo.PropertyType, 1)); // (physics colliders: the smooth slopes over stairs, not the steps' look)
            var mod = Type.GetType("Unity.AI.Navigation.NavMeshModifier, Unity.AI.Navigation"); if (mod != null) foreach (var d in m_B.GetComponentsInChildren<BuildingDoor>()) { var mm = d.GetComponent(mod) ?? d.gameObject.AddComponent(mod); mod.GetProperty("ignoreFromBuild")?.SetValue(mm, true); }
            if (!m_B.smoothStairs) { Undo.RecordObject(m_B, "Smooth stairs"); m_B.smoothStairs = true; Remake(); }
            t.GetMethod("BuildNavMesh")?.Invoke(s, null); float r = AgentRadius(), narrow = Narrowest();
            return "NavMesh made on " + m_B.name + (narrow < r * 2 + 0.1f ? ". Your agent is " + M(r * 2) + " wide, so it can't get through doorways or up stairs " + M(narrow) + " wide: make it thinner (the button under Make a NavMesh)" : " (up the stairs and through doorways).");
        }
        /// <summary>The project's first NavMesh agent's radius (Navigation, Agents).</summary>
        public static float AgentRadius() => UnityEngine.AI.NavMesh.GetSettingsCount() > 0 ? UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentRadius : 0.5f;
        /// <summary>Makes the project's first NavMesh agent thinner (Navigation, Agents), so it fits through doors and up stairs.</summary>
        public static void SetAgentRadius(float r)
        {
            var o = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault(); if (!o) return; var so = new SerializedObject(o); var p = so.FindProperty("m_Settings.Array.data[0].agentRadius"); if (p == null) return; p.floatValue = r; so.ApplyModifiedProperties();
        }
        /// <summary>The narrowest doorway or stair in the building.</summary>
        float Narrowest() { float n = 99; foreach (var f in m_B.floors) { foreach (var w in f.walls) foreach (var o in w.openings) if (o.kind != Building.OpeningKind.Window) n = Mathf.Min(n, o.width); foreach (var s in f.stairs) if (s.kind != Building.StairKind.Ladder && s.kind != Building.StairKind.Spiral) n = Mathf.Min(n, s.width); } return n; }
        /// <summary>The building (or one room of it) as a prefab: meshes saved as assets with lightmap UVs, doors that open,
        /// and a connector at each doorway on the outside, for putting rooms together in a level generator.</summary>
        public GameObject Export(Building.Area onlyRoom = null, string folder = "Assets/Buildings/Prefabs")
        {
            var src = m_B; GameObject root; Building b;
            if (onlyRoom == null) { root = (GameObject)UnityEngine.Object.Instantiate(src.gameObject); root.name = src.name; b = root.GetComponent<Building>(); /* (the copy shares the original's meshes: drop its children without touching them, then make its own) */ foreach (var m in root.GetComponentsInChildren<BuildingMade>(true).Where(q => q && q.transform.parent == root.transform).ToList()) DestroyImmediate(m.gameObject); }
            else
            {
                var fl = src.FloorAt(m_Level); var r = SaveRoomTemp(fl, onlyRoom); root = new GameObject(onlyRoom.name.Length > 0 ? onlyRoom.name : "Room"); b = root.AddComponent<Building>(); EditorUtility.CopySerialized(src, b); b.floors = new List<Building.Floor>(); var f2 = b.FloorAt(0, true); f2.height = src.HeightOf(m_Level); Stamp(f2, r, Vector2.zero, 0); f2.roof = fl.roof; f2.roofPitch = fl.roofPitch; f2.roofOverhang = fl.roofOverhang; f2.areas.ForEach(a => a.roof |= fl.roof == Building.RoofKind.None); DestroyImmediate(r);
                BuildingMaker.Rebuild(b);
                // doorways on the room's edge become connectors, facing out
                var lo = Vector2.zero; var hi = r.size; foreach (var w in f2.walls) { bool edge = (Mathf.Abs(w.a.x - w.b.x) < 1e-3f && (Mathf.Abs(w.a.x - lo.x) < 1e-3f || Mathf.Abs(w.a.x - hi.x) < 1e-3f)) || (Mathf.Abs(w.a.y - w.b.y) < 1e-3f && (Mathf.Abs(w.a.y - lo.y) < 1e-3f || Mathf.Abs(w.a.y - hi.y) < 1e-3f)); if (!edge) continue; var d = (w.b - w.a).normalized; foreach (var o in w.openings.Where(o => o.kind != Building.OpeningKind.Window)) { var p = w.a + d * o.at; var n = new Vector2(-d.y, d.x); if (Vector2.Dot(p + n * 0.1f - (lo + hi) / 2, n) < Vector2.Dot(p - (lo + hi) / 2, n)) n = -n; var c = new GameObject("Connector " + o.kind); c.transform.SetParent(root.transform, false); c.transform.localPosition = new Vector3(p.x, 0, p.y); c.transform.localRotation = Quaternion.LookRotation(new Vector3(n.x, 0, n.y)); var rc = c.AddComponent<RoomConnector>(); rc.width = o.width; rc.height = o.top; rc.kind = o.kind.ToString().ToLower(); } }
            }
            BuildingMaker.Rebuild(b); Folder(folder); var name = Clean(root.name); var meshPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + " meshes.asset"); var holder = CreateInstance<BuildingRoom>(); holder.name = name + " meshes"; AssetDatabase.CreateAsset(holder, meshPath);
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true)) { var mesh = f.sharedMesh; if (!mesh || AssetDatabase.Contains(mesh)) continue; Unwrapping.GenerateSecondaryUVSet(mesh); AssetDatabase.AddObjectToAsset(mesh, meshPath); }
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".prefab"); var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); DestroyImmediate(root); AssetDatabase.SaveAssets(); return prefab;
        }
        BuildingRoom SaveRoomTemp(Building.Floor f, Building.Area a) { var lo = Vector2.Min(a.min, a.max); var hi = Vector2.Max(a.min, a.max); bool In(Vector2 p) => Inside(p, lo, hi); var r = CreateInstance<BuildingRoom>(); r.size = hi - lo; var c0 = Copy(a); c0.Move(p => p - lo); r.areas.Add(c0); foreach (var w in f.walls.Where(w => In(w.a) && In(w.b))) { var c = Copy(w); c.a -= lo; c.b -= lo; r.walls.Add(c); } foreach (var s in f.stairs.Where(s => In(s.at))) { var c = Copy(s); c.at -= lo; r.stairs.Add(c); } foreach (var p in f.pieces.Where(p => In(p.at))) { var c = Copy(p); c.at -= lo; r.pieces.Add(c); } foreach (var cl in f.columns.Where(c => In(c.at))) { var c = Copy(cl); c.at -= lo; r.columns.Add(c); } return r; }

        // ------------------------------------------------------------------ a person for scale

        void PutPerson() { if (!m_B || m_V == null) return; if (OnFloor(Event.current.mousePosition, m_B.Y(m_Level), out var u)) { m_PersonAt = u; m_PersonLevel = m_Level; m_ShowPerson = true; Repaint(); SceneView.RepaintAll(); } }
        static Mesh s_Capsule; static Material s_PersonMat;
        void DrawPerson(PreviewRenderUtility pru)
        {
            if (!m_ShowPerson || m_PersonAt.x < -0.5f && m_PersonAt.y < -0.5f) return; var at = Tr.TransformPoint(new Vector3(m_PersonAt.x, m_B.Y(m_PersonLevel) + Surface0(m_PersonAt, m_PersonLevel), m_PersonAt.y));
            if (m_Person) { foreach (var r in m_Person.GetComponentsInChildren<MeshRenderer>()) { var f = r.GetComponent<MeshFilter>(); if (!f || !f.sharedMesh) continue; var m = Matrix4x4.TRS(at, Tr.rotation, Vector3.one) * m_Person.transform.worldToLocalMatrix * r.transform.localToWorldMatrix; for (int s = 0; s < f.sharedMesh.subMeshCount && s < r.sharedMaterials.Length; s++) pru.DrawMesh(f.sharedMesh, m, r.sharedMaterials[s], s); } return; }
            if (!s_Capsule) s_Capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx"); if (!s_PersonMat) { s_PersonMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave }; s_PersonMat.SetColor(s_PersonMat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(1f, 0.55f, 0.2f)); }
            pru.DrawMesh(s_Capsule, Matrix4x4.TRS(at + Vector3.up * m_PersonHeight / 2, Tr.rotation, new Vector3(0.45f, m_PersonHeight / 2, 0.45f)), s_PersonMat, 0);
        }
        float Surface0(Vector2 u, int level) { int keep = m_Level; m_Level = level; float s = Surface(u); m_Level = keep; return s; }
    }
}
