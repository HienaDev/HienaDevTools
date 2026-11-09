using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The note's text in a little card while the mouse is over its tag in the Hierarchy (Unity's own tooltips
    /// don't show there reliably). Doesn't take focus; goes away when the mouse leaves the tag.</summary>
    class NoteHover : EditorWindow
    {
        static NoteHover s_Open;
        DevNote m_Note;
        static GUIStyle s_Text, s_Head, s_Small;
        const float Width = 260;

        public static bool IsShowing(DevNote n) => s_Open && s_Open.m_Note == n;

        public static void Show(DevNote n, Rect tagScreen)
        {
            if (IsShowing(n)) return;
            Hide();
            Styles();
            float h = 30 + s_Text.CalcHeight(new GUIContent(string.IsNullOrWhiteSpace(n.text) ? "(empty)" : n.text), Width - 20) + (string.IsNullOrEmpty(n.forWho) && n.priority != DevNote.Priority.High ? 6 : 24);
            var w = CreateInstance<NoteHover>();
            w.m_Note = n;
            // below the tag, its right edge lined up with the tag's
            var pos = new Rect(tagScreen.xMax - Width, tagScreen.yMax + 4, Width, h);
            if (pos.x < 0) pos.x = tagScreen.x;
            // (Unity's own tooltip-style window when it has one: no focus taken; else a plain popup)
            var tip = typeof(EditorWindow).GetMethod("ShowTooltip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
            if (tip != null) tip.Invoke(w, null); else w.ShowPopup();
            w.position = pos;
            s_Open = w;
            EditorApplication.update -= Watch; EditorApplication.update += Watch;
        }

        public static void Hide()
        {
            if (s_Open) s_Open.Close();
            s_Open = null;
            EditorApplication.update -= Watch;
        }

        // the mouse left the Hierarchy (no more events there to say so): close
        static void Watch()
        {
            if (!s_Open) { EditorApplication.update -= Watch; return; }
            var over = EditorWindow.mouseOverWindow;
            if (over == null || (over != s_Open && over.GetType().Name != "SceneHierarchyWindow") || !s_Open.m_Note) Hide();
        }

        static void Styles()
        {
            if (s_Text != null) return;
            // dark text: it sits on the note's (light) colour
            s_Text = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 12, richText = false, normal = { textColor = new Color(0.08f, 0.08f, 0.08f) } };
            s_Head = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.black } };
            s_Small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = new Color(0.15f, 0.15f, 0.15f) } };
        }

        void OnGUI()
        {
            if (!m_Note) { Close(); return; }
            Styles();
            var n = m_Note;
            var colour = n.done ? new Color(0.45f, 0.75f, 0.45f) : n.color;
            // the whole card in the note's colour (pick it per note in the Inspector), a darker strip on top
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colour);
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 22), colour * new Color(0.82f, 0.82f, 0.82f, 1f));
            GUI.Label(new Rect(8, 3, position.width - 16, 18), (n.kind == DevNote.Kind.Note ? "NOTE" : n.done ? "DONE" : "TODO") + "   ·   " + n.gameObject.name, s_Head);
            string text = string.IsNullOrWhiteSpace(n.text) ? "(empty)" : n.text;
            var tr = new Rect(10, 28, position.width - 20, s_Text.CalcHeight(new GUIContent(text), position.width - 20));
            GUI.Label(tr, text, s_Text);
            if (n.done) DevNoteEditor.StrikeLines(tr, text, s_Text);
            string extra = (string.IsNullOrEmpty(n.forWho) ? "" : "For " + n.forWho) + (n.priority == DevNote.Priority.High ? (string.IsNullOrEmpty(n.forWho) ? "" : "   ·   ") + "High priority" : "");
            if (extra.Length > 0) GUI.Label(new Rect(10, tr.yMax + 4, position.width - 20, 16), extra, s_Small);
            // a thin border
            var b = new Color(0, 0, 0, 0.5f);
            EditorGUI.DrawRect(new Rect(0, position.height - 1, position.width, 1), b);
            EditorGUI.DrawRect(new Rect(0, 0, 1, position.height), b);
            EditorGUI.DrawRect(new Rect(position.width - 1, 0, 1, position.height), b);
        }
    }
}
