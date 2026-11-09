using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The note in the Inspector: a coloured card, the text, who it's for, and one big Done button (done notes
    /// turn green and their text is crossed out, here, in the Hierarchy and in the Notes & TODO list).</summary>
    [CustomEditor(typeof(DevNote))]
    class DevNoteEditor : Editor
    {
        static GUIStyle s_Text, s_Head;
        string m_NewStep = "";

        public override void OnInspectorGUI()
        {
            var n = (DevNote)target;
            if (s_Text == null) { s_Text = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 }; s_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Color.black } }; }
            serializedObject.Update();
            var colour = n.done ? new Color(0.45f, 0.75f, 0.45f) : n.color;

            // header: the kind (Note / TODO) as two buttons on a coloured strip
            var head = EditorGUILayout.GetControlRect(false, 26);
            EditorGUI.DrawRect(head, colour);
            GUI.Label(new Rect(head.x + 8, head.y + 4, 200, 18), n.kind == DevNote.Kind.Todo ? (n.done ? "DONE" : "TODO") : "NOTE", s_Head);
            int k = GUI.Toolbar(new Rect(head.xMax - 124, head.y + 3, 120, 20), (int)n.kind, new[] { "Note", "TODO" });
            if (k != (int)n.kind) { Undo.RecordObject(n, "Note kind"); n.kind = (DevNote.Kind)k; Changed(n); }

            // the text (crossed out when done)
            EditorGUI.BeginChangeCheck();
            string t = EditorGUILayout.TextArea(n.text, s_Text, GUILayout.MinHeight(60));
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(n, "Note text"); n.text = t; Changed(n); }
            if (n.done && Event.current.type == EventType.Repaint) StrikeLines(GUILayoutUtility.GetLastRect(), n.text, s_Text);

            EditorGUI.BeginChangeCheck();
            string who = EditorGUILayout.TextField(new GUIContent("For", "Who it's for: Art, Code, Sound, a name..."), n.forWho);
            var pr = (DevNote.Priority)EditorGUILayout.EnumPopup("Priority", n.priority);
            var col = EditorGUILayout.ColorField("Colour", n.color);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(n, "Note"); n.forWho = who; n.priority = pr; n.color = col; Changed(n); }

            if (n.kind == DevNote.Kind.Todo)
            {
                EditorGUILayout.Space(4);
                var old = GUI.backgroundColor; GUI.backgroundColor = n.done ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.45f, 0.85f, 0.45f);
                if (GUILayout.Button(n.done ? "Done ✓   (click to undo)" : "Mark as done", GUILayout.Height(30))) { Undo.RecordObject(n, n.done ? "TODO not done" : "TODO done"); n.done = !n.done; Changed(n); }
                GUI.backgroundColor = old;
            }
            // ---- a checklist inside the note
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Steps" + (n.steps.Count > 0 ? "  (" + n.StepsDone + " of " + n.steps.Count + " done)" : ""), EditorStyles.boldLabel);
            int removeAt = -1;
            for (int i = 0; i < n.steps.Count; i++)
            {
                var st = n.steps[i];
                EditorGUILayout.BeginHorizontal();
                bool d = EditorGUILayout.Toggle(st.done, GUILayout.Width(16));
                string tx = EditorGUILayout.TextField(st.text);
                if (st.done && Event.current.type == EventType.Repaint) { var lr = GUILayoutUtility.GetLastRect(); EditorGUI.DrawRect(new Rect(lr.x + 3, lr.center.y, Mathf.Min(lr.width - 6, EditorStyles.label.CalcSize(new GUIContent(st.text)).x), 1.2f), new Color(0.6f, 0.6f, 0.6f)); }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) removeAt = i;
                EditorGUILayout.EndHorizontal();
                if (d != st.done || tx != st.text) { Undo.RecordObject(n, "Note step"); st.done = d; st.text = tx; Changed(n); }
            }
            if (removeAt >= 0) { Undo.RecordObject(n, "Remove step"); n.steps.RemoveAt(removeAt); Changed(n); GUIUtility.ExitGUI(); }
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("HienaDevNewStep");
            m_NewStep = EditorGUILayout.TextField(m_NewStep);
            if (string.IsNullOrEmpty(m_NewStep) && Event.current.type == EventType.Repaint) { var hr = GUILayoutUtility.GetLastRect(); hr.x += 4; GUI.Label(hr, "Add a step... (Enter)", EditorStyles.centeredGreyMiniLabel); }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "HienaDevNewStep";
            if ((GUILayout.Button("Add", EditorStyles.miniButton, GUILayout.Width(40)) || enter) && !string.IsNullOrWhiteSpace(m_NewStep))
            { Undo.RecordObject(n, "Add step"); n.steps.Add(new DevNote.Step { text = m_NewStep.Trim() }); m_NewStep = ""; Changed(n); if (enter) Event.current.Use(); GUI.FocusControl("HienaDevNewStep"); }
            EditorGUILayout.EndHorizontal();

            // ---- the Scene view it was written from
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(n.hasView ? "Look (go to its view)" : "Look at it", "Puts the Scene view where this note was written from (or frames the object)"))) SceneNotes.Look(n);
            if (SceneView.lastActiveSceneView != null && GUILayout.Button(new GUIContent("Remember this view", "Saves the Scene view's camera with the note, for Look"))) { Undo.RecordObject(n, "Note view"); SceneNotes.Remember(n, SceneView.lastActiveSceneView); }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Open the Notes & TODO list", EditorStyles.miniButton)) NotesWindow.Open();
        }

        static void Changed(DevNote n) { EditorUtility.SetDirty(n); EditorApplication.RepaintHierarchyWindow(); }

        /// <summary>A line through each line of a wrapped text drawn in r with style (done things).</summary>
        public static void StrikeLines(Rect r, string text, GUIStyle style)
        {
            var inner = style.padding.Remove(r);
            float lh = style.lineHeight;
            var size = style.CalcHeight(new GUIContent(text), r.width);
            int lines = Mathf.Max(1, Mathf.RoundToInt((size - style.padding.vertical) / lh));
            var c = new Color(0.1f, 0.1f, 0.1f, 0.75f); if (EditorGUIUtility.isProSkin) c = new Color(0.85f, 0.85f, 0.85f, 0.75f);
            for (int i = 0; i < lines; i++)
            {
                float w = i == lines - 1 ? Mathf.Min(inner.width, style.CalcSize(new GUIContent(LastLine(text, inner.width, style))).x - style.padding.horizontal) : inner.width;
                EditorGUI.DrawRect(new Rect(inner.x, inner.y + lh * i + lh * 0.55f, Mathf.Max(8, w), 1.2f), c);
            }
        }
        static string LastLine(string text, float width, GUIStyle style)
        {
            // roughly: the words that fit on the last wrapped line
            var words = (text ?? "").Split(' '); string line = "";
            foreach (var w in words) { string tryLine = line.Length == 0 ? w : line + " " + w; if (style.CalcSize(new GUIContent(tryLine)).x > width) line = w; else line = tryLine; }
            return line;
        }
    }
}
