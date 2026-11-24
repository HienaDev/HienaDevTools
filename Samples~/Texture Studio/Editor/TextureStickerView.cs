using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Stickers on the flat picture: click to put one, click one to pick it, then a frame lets you move it (drag
    /// inside), size it (drag a corner) and turn it (drag the round handle). Delete or the cross takes it off.</summary>
    public partial class TextureStudioWindow
    {
        enum Grip { None, Move, Size, Turn, Trail }
        int m_NewOne = -1;
        bool ModePill(Rect r, string text, bool on) { EditorGUI.DrawRect(r, on ? Accent : r.Contains(Event.current.mousePosition) ? PanelHi : Ink(0.08f)); GUI.Label(r, text, new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = on ? Color.black : Color.white } }); return GUI.Button(r, GUIContent.none, GUIStyle.none) && !on; }
        Grip m_Grip; Vector2 m_GripFrom, m_GripTile; float m_GripSize, m_GripTurn, m_GripAngle; Vector2 m_GripAx, m_GripAy;

        void StickerView(Rect area, StickerStep st)
        {
            int before = m_Steps.Take(m_Selected).Count(s => s.on); if (before >= m_Between.Count) return; var pout = m_Between[Mathf.Min(before + 1, m_Between.Count - 1)]; if (pout != m_PaintOutFor) { Put(ref m_PaintOut, pout); m_PaintOutFor = pout; }
            float aspect = pout.w / (float)pout.h; var pic = Fit(new Rect(area.x, area.y + 18, area.width, area.height - 18), aspect, 10); EditorGUI.DrawRect(new Rect(pic.x - 2, pic.y - 2, pic.width + 4, pic.height + 4), st.Tint); float rep = m_Repeat; Pic(pic, m_PaintOut, rep); if (m_Edges) Edges(pic, m_Repeat);
            bool edit = StickerStep.EditMode; if (!edit) st.pick = -1;
            // the two modes, right above the picture (Tab swaps them)
            var mb = new Rect(pic.x, pic.y - 22, 112, 19); if (ModePill(mb, "+ Place new", !edit)) { StickerStep.EditMode = false; st.pick = -1; } if (ModePill(new Rect(mb.xMax + 4, mb.y, 112, 19), "Pick and change", edit)) StickerStep.EditMode = true;
            GUI.Label(new Rect(mb.xMax + 124, pic.y - 20, pic.width - 240, 18), edit ? st.Picked != null ? "DRAG: MOVE   CORNERS: SIZE   ROUND HANDLE: TURN   DELETE: TAKE OFF" : "CLICK A STICKER TO PICK IT     TAB: BACK TO PLACING" : "CLICK: PUT ONE   WHEEL: SIZE   SHIFT + WHEEL: TURN   SHIFT + DRAG: A TRAIL   TAB: PICK", new GUIStyle(m_Head) { normal = { textColor = st.Tint }, clipping = TextClipping.Clip });
            var e = Event.current; int id = GUIUtility.GetControlID("TextureStickers".GetHashCode(), FocusType.Keyboard); bool over = pic.Contains(e.mousePosition); float unit = pic.width / rep;
            Vector2 At(Vector2 m) => new Vector2((m.x - pic.x) / unit, (pic.yMax - m.y) / unit * aspect);   // (not wrapped: tile numbers count)
            Vector2 Wrap(Vector2 v) => new Vector2(Mathf.Repeat(v.x, 1), Mathf.Repeat(v.y, 1));
            Vector2 Off(Vector2 a, Vector2 b) { var d = a - b; d.x -= Mathf.Round(d.x); d.y -= Mathf.Round(d.y); return d; }
            // a sticker on the screen: its middle (in the copy nearest the mouse) and its whole width and height as arrows
            void Shape(StickerStep.Stamp s, Vector2 near, out Vector2 c, out Vector2 X, out Vector2 Y)
            {
                var u = s.at + new Vector2(Mathf.Round(near.x - s.at.x), Mathf.Round(near.y - s.at.y)); c = new Vector2(pic.x + u.x * unit, pic.yMax - u.y * unit / aspect);
                if (s.OnModel) { X = new Vector2(s.ax.x * unit, -s.ax.y * unit / aspect); Y = new Vector2(s.ay.x * unit, -s.ay.y * unit / aspect); return; }
                var t = s.art.Picture(s.seed); float asp = t ? t.height / (float)t.width : 1, cs = Mathf.Cos(s.turn * Mathf.Deg2Rad), sn = Mathf.Sin(s.turn * Mathf.Deg2Rad); X = new Vector2(cs, -sn) * s.size * unit; Y = new Vector2(-sn, -cs) * s.size * unit * asp;
            }
            bool Inside(Vector2 m, Vector2 c, Vector2 X, Vector2 Y) { var d = m - c; float det = X.x * Y.y - X.y * Y.x; if (Mathf.Abs(det) < 1e-3f) return false; float lx = (d.x * Y.y - d.y * Y.x) / det, ly = (d.y * X.x - d.x * X.y) / det; return Mathf.Abs(lx) <= 0.5f && Mathf.Abs(ly) <= 0.5f; }
            int Under(Vector2 m) { var p = At(m); for (int i = st.stamps.Count - 1; i >= 0; i--) { Shape(st.stamps[i], p, out var c, out var X, out var Y); if (Inside(m, c, X, Y) || (m - c).magnitude < 6) return i; } return -1; }
            Vector2 Knob(Vector2 c, Vector2 Y) => c + Y / 2 + Y.normalized * 20; Vector2 Cross(Vector2 c, Vector2 X, Vector2 Y) => c + X / 2 + Y / 2 + (X.normalized + Y.normalized) * 11;
            if (st.pick >= st.stamps.Count) st.pick = -1; var pk = edit ? st.Picked : null;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab && (over || GUIUtility.keyboardControl == id)) { StickerStep.EditMode = !edit; st.pick = -1; e.Use(); Repaint(); return; }

            if (e.type == EventType.Repaint)
            {
                if (pk != null)
                {
                    Shape(pk, m_GripTile, out var c, out var X, out var Y); var pts = new Vector3[] { c - X / 2 - Y / 2, c + X / 2 - Y / 2, c + X / 2 + Y / 2, c - X / 2 + Y / 2, c - X / 2 - Y / 2 };
                    Handles.BeginGUI(); Handles.color = new Color(0, 0, 0, 0.6f); Handles.DrawAAPolyLine(5f, pts); Handles.color = Color.white; Handles.DrawAAPolyLine(2f, pts); var k = Knob(c, Y); Handles.DrawAAPolyLine(2f, c + Y / 2, k); Handles.color = Accent; Handles.DrawSolidDisc(k, Vector3.forward, 7); Handles.color = Color.white; Handles.DrawWireDisc(k, Vector3.forward, 7);
                    for (int i = 0; i < 4; i++) { var q = pts[i]; EditorGUI.DrawRect(new Rect(q.x - 5, q.y - 5, 10, 10), Color.black); EditorGUI.DrawRect(new Rect(q.x - 4, q.y - 4, 8, 8), Color.white); }
                    var x = Cross(c, X, Y); Handles.color = new Color(0.85f, 0.25f, 0.25f); Handles.DrawSolidDisc(x, Vector3.forward, 8); Handles.EndGUI(); GUI.Label(new Rect(x.x - 8, x.y - 9, 16, 16), "✕", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 10, normal = { textColor = Color.white } });
                    EditorGUIUtility.AddCursorRect(new Rect(k.x - 9, k.y - 9, 18, 18), MouseCursor.RotateArrow); for (int i = 0; i < 4; i++) EditorGUIUtility.AddCursorRect(new Rect(pts[i].x - 7, pts[i].y - 7, 14, 14), MouseCursor.ScaleArrow);
                }
                if (over && GUIUtility.hotControl == 0 && !edit)
                {
                    // what a click would put there
                    var n = st.New(Vector2.zero, m_StickerSeed); var t = n.art.Picture(n.seed); if (t) { float w = n.size * unit, h = w * t.height / t.width; var m = GUI.matrix; var cl = GUI.color; GUI.color = new Color(1, 1, 1, 0.45f * n.strength); GUIUtility.RotateAroundPivot(-n.turn, e.mousePosition); GUI.DrawTextureWithTexCoords(new Rect(e.mousePosition.x - w / 2, e.mousePosition.y - h / 2, w, h), t, n.flip ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1), true); GUI.color = cl; GUI.matrix = m; }
                }
            }
            bool Near(StickerStep.Stamp s) { Shape(s, m_GripTile, out var c, out var X, out var Y); var m = e.mousePosition; return (m - Knob(c, Y)).magnitude < 10 || (m - Cross(c, X, Y)).magnitude < 10 || Inside(m, c, X * 1.15f, Y * 1.15f); }

            if (e.type == EventType.MouseDown && e.button == 0 && over)
            {
                GUIUtility.keyboardControl = id; m_GripFrom = e.mousePosition; var pm = At(e.mousePosition);
                if (edit && pk != null)
                {
                    Shape(pk, m_GripTile, out var c, out var X, out var Y); var m = e.mousePosition; var corners = new[] { c - X / 2 - Y / 2, c + X / 2 - Y / 2, c + X / 2 + Y / 2, c - X / 2 + Y / 2 };
                    if ((m - Cross(c, X, Y)).magnitude < 10) { st.stamps.RemoveAt(st.pick); st.pick = -1; Changed(); e.Use(); return; }
                    m_GripSize = pk.size; m_GripTurn = pk.turn; m_GripAx = pk.ax; m_GripAy = pk.ay; m_GripAngle = Mathf.Atan2(m.y - c.y, m.x - c.x) * Mathf.Rad2Deg;
                    if ((m - Knob(c, Y)).magnitude < 11) { m_Grip = Grip.Turn; GUIUtility.hotControl = id; e.Use(); return; }
                    if (corners.Any(q => (m - q).magnitude < 9)) { m_Grip = Grip.Size; GUIUtility.hotControl = id; e.Use(); return; }
                }
                if (edit)
                {
                    int u = Under(e.mousePosition); st.pick = u; if (u < 0) { e.Use(); Repaint(); return; }
                    m_GripTile = pm; m_StickerOff = Off(st.stamps[u].at, Wrap(pm)); m_Grip = Grip.Move; m_NewOne = -1;
                }
                else if (e.shift) { st.stamps.Add(st.New(Wrap(pm), m_StickerSeed)); m_StickerSeed = Random.Range(1, 999999); m_Grip = Grip.Trail; Changed(); }
                else { st.stamps.Add(st.New(Wrap(pm), m_StickerSeed)); m_StickerSeed = Random.Range(1, 999999); m_NewOne = st.stamps.Count - 1; m_GripTile = pm; m_StickerOff = Vector2.zero; m_Grip = Grip.Move; Changed(); }
                GUIUtility.hotControl = id; e.Use();
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                var pm = At(e.mousePosition); var s = edit ? st.Picked : m_NewOne >= 0 && m_NewOne < st.stamps.Count ? st.stamps[m_NewOne] : null;
                if (m_Grip == Grip.Trail) { if (st.stamps.Count > 0 && Off(Wrap(pm), st.stamps[st.stamps.Count - 1].at).magnitude > st.size * 0.6f) { st.stamps.Add(st.New(Wrap(pm), Random.Range(1, 999999))); Changed(); } }
                else if (s != null && m_Grip == Grip.Move) { s.at = Wrap(pm + m_StickerOff); m_GripTile = pm; Changed(); }
                else if (s != null)
                {
                    Shape(s, m_GripTile, out var c, out _, out _); var m = e.mousePosition;
                    if (m_Grip == Grip.Size) { float k = Mathf.Clamp((m - c).magnitude / Mathf.Max(4f, (m_GripFrom - c).magnitude), 0.05f, 20f); if (s.OnModel) { s.ax = m_GripAx * k; s.ay = m_GripAy * k; } else s.size = Mathf.Clamp(m_GripSize * k, 0.01f, 2f); }
                    else { float d = Mathf.DeltaAngle(m_GripAngle, Mathf.Atan2(m.y - c.y, m.x - c.x) * Mathf.Rad2Deg); if (e.shift) d = Mathf.Round(d / 15f) * 15f; if (s.OnModel) { var A = new Vector2(m_GripAx.x * aspect, m_GripAx.y); var B = new Vector2(m_GripAy.x * aspect, m_GripAy.y); var q = Quaternion.Euler(0, 0, -d); A = q * A; B = q * B; s.ax = new Vector2(A.x / aspect, A.y); s.ay = new Vector2(B.x / aspect, B.y); } else s.turn = Mathf.Repeat(m_GripTurn - d + 180, 360) - 180; }
                    Changed();
                }
                e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_Grip = Grip.None; m_NewOne = -1; e.Use(); }
            if (e.type == EventType.MouseDown && e.button == 1 && over) { int u = Under(e.mousePosition); if (u >= 0) { st.stamps.RemoveAt(u); st.pick = -1; Changed(); } e.Use(); }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && pk != null && GUIUtility.keyboardControl == id) { st.stamps.Remove(pk); st.pick = -1; Changed(); e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && pk != null) { st.pick = -1; e.Use(); Repaint(); }
            if (e.type == EventType.ScrollWheel && over)
            {
                var s = !edit ? null : pk ?? (Under(e.mousePosition) is int u && u >= 0 ? st.stamps[u] : null); float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;
                if (e.shift) { if (s != null) { if (s.OnModel) { var q = Quaternion.Euler(0, 0, d > 0 ? 8 : -8); s.ax = q * s.ax; s.ay = q * s.ay; } else s.turn += d > 0 ? 8 : -8; } else st.turn = Mathf.Repeat(st.turn + (d > 0 ? 8 : -8) + 180, 360) - 180; }
                else { float k = d > 0 ? 0.92f : 1.08f; if (s != null) { if (s.OnModel) { s.ax *= k; s.ay *= k; } else s.size = Mathf.Clamp(s.size * k, 0.01f, 2f); } else st.size = Mathf.Clamp(st.size * k, 0.02f, 1f); }
                if (s != null) Changed(); e.Use(); Repaint();
            }
            if (e.type == EventType.ContextClick && over) e.Use();
        }
    }
}
