using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Notes in the Scene view: every note shows as a small sticky card where its object is (hover: the whole text; click:
    /// select it). "+ Note in the scene" (Notes & TODO tab, or Ctrl+Alt+N in the Scene view) puts a new note in the middle
    /// of the view and remembers the camera, so "Look" brings that exact view back. Notes made this way are editor-only
    /// objects: builds leave them out. On/off: the eye button in the Notes & TODO tab.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneNotes
    {
        public static bool Show { get => EditorPrefs.GetBool("HienaDev.SceneNotes", true); set { EditorPrefs.SetBool("HienaDev.SceneNotes", value); SceneView.RepaintAll(); } }
        static GUIStyle s_Text, s_Head;

        static SceneNotes() { SceneView.duringSceneGui += Draw; }

        [MenuItem("GameObject/Dev Tools/Note in the Scene View", false, 10)]
        [UnityEditor.ShortcutManagement.Shortcut("HienaDev/New Scene Note", typeof(SceneView), KeyCode.N, UnityEditor.ShortcutManagement.ShortcutModifiers.Control | UnityEditor.ShortcutManagement.ShortcutModifiers.Alt)]
        public static void AddHere()
        {
            var sv = SceneView.lastActiveSceneView;
            var go = new GameObject("Note") { tag = "EditorOnly" };
            if (sv != null) go.transform.position = sv.pivot;
            var stage = StageUtility.GetCurrentStage();
            if (stage is PrefabStage ps) go.transform.SetParent(ps.prefabContentsRoot.transform, true);
            else if (Selection.activeTransform == null) SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var n = go.AddComponent<DevNote>();
            n.kind = DevNote.Kind.Todo;
            if (sv != null) Remember(n, sv);
            Undo.RegisterCreatedObjectUndo(go, "New scene note");
            Selection.activeGameObject = go;
            EditorApplication.RepaintHierarchyWindow();
        }

        public static void Remember(DevNote n, SceneView sv)
        {
            n.hasView = true; n.viewPivot = sv.pivot; n.viewRotation = sv.rotation; n.viewSize = sv.size;
            EditorUtility.SetDirty(n);
        }

        /// <summary>The Scene view goes back to where the note was written from (or frames its object).</summary>
        public static void Look(DevNote n)
        {
            var sv = SceneView.lastActiveSceneView ?? (SceneView.sceneViews.Count > 0 ? (SceneView)SceneView.sceneViews[0] : null);
            if (sv == null) return;
            Selection.activeGameObject = n.gameObject;
            if (n.hasView) sv.LookAt(n.viewPivot, n.viewRotation, n.viewSize);
            else sv.FrameSelected();
            sv.Focus();
        }

        static void Draw(SceneView sv)
        {
            if (!Show || Event.current == null) return;
            if (s_Text == null)
            {
                s_Text = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11, normal = { textColor = new Color(0.08f, 0.08f, 0.08f) } };
                s_Head = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.05f, 0.05f, 0.05f) }, clipping = TextClipping.Clip };
            }
            var stage = StageUtility.GetCurrentStage();
            DevNote[] notes = stage is PrefabStage ps ? ps.prefabContentsRoot.GetComponentsInChildren<DevNote>(true) : Object.FindObjectsByType<DevNote>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cam = sv.camera;
            Handles.BeginGUI();
            DevNote hovered = null; Rect hoverRect = default;
            foreach (var n in notes)
            {
                if (!n || !n.gameObject.scene.IsValid() && !(stage is PrefabStage)) continue;
                var vp = cam.WorldToViewportPoint(n.transform.position);
                if (vp.z <= 0 || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) continue;
                Vector2 p = HandleUtility.WorldToGUIPoint(n.transform.position);
                var colour = n.done ? new Color(0.55f, 0.82f, 0.55f) : n.color;
                string head = (n.kind == DevNote.Kind.Note ? "NOTE" : n.done ? "DONE" : "TODO") + (n.steps.Count > 0 ? "  " + n.StepsDone + "/" + n.steps.Count : "") + (string.IsNullOrEmpty(n.forWho) ? "" : "  · " + n.forWho);
                string body = string.IsNullOrWhiteSpace(n.text) ? "(empty)" : n.text;
                // a small card above the point, with a pin line down to it
                var card = new Rect(p.x - 70, p.y - 62, 140, 50);
                Handles.color = colour; Handles.DrawLine(new Vector3(p.x, p.y - 12), new Vector3(p.x, p.y));
                EditorGUI.DrawRect(new Rect(p.x - 3, p.y - 3, 6, 6), colour);
                EditorGUI.DrawRect(card, colour);
                EditorGUI.DrawRect(new Rect(card.x, card.y, card.width, 15), colour * new Color(0.82f, 0.82f, 0.82f, 1));
                if (n.priority == DevNote.Priority.High && !n.done) EditorGUI.DrawRect(new Rect(card.x, card.y, 4, card.height), new Color(0.9f, 0.25f, 0.2f));
                GUI.Label(new Rect(card.x + 5, card.y, card.width - 8, 15), head, s_Head);
                var tr = new Rect(card.x + 5, card.y + 15, card.width - 8, card.height - 16);
                GUI.Label(tr, body.Length > 70 ? body.Substring(0, 70) + "…" : body, s_Text);
                if (n.done) DevNoteEditor.StrikeLines(tr, body.Length > 70 ? body.Substring(0, 70) + "…" : body, s_Text);
                if (card.Contains(Event.current.mousePosition)) { hovered = n; hoverRect = card; }
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && card.Contains(Event.current.mousePosition))
                { Selection.activeGameObject = n.gameObject; EditorGUIUtility.PingObject(n.gameObject); Event.current.Use(); }
            }
            // the whole note while the mouse is over its card
            if (hovered)
            {
                string body = string.IsNullOrWhiteSpace(hovered.text) ? "(empty)" : hovered.text;
                float h = s_Text.CalcHeight(new GUIContent(body), 236) + 24 + hovered.steps.Count * 15;
                var big = new Rect(hoverRect.x - 50, hoverRect.yMax + 18, 240, h);
                EditorGUI.DrawRect(big, hovered.done ? new Color(0.55f, 0.82f, 0.55f) : hovered.color);
                GUI.Label(new Rect(big.x + 6, big.y + 3, big.width - 10, 16), hovered.gameObject.name, s_Head);
                var br = new Rect(big.x + 6, big.y + 19, big.width - 10, s_Text.CalcHeight(new GUIContent(body), 236));
                GUI.Label(br, body, s_Text);
                float y = br.yMax + 2;
                foreach (var s in hovered.steps) { GUI.Label(new Rect(big.x + 8, y, big.width - 12, 15), (s.done ? "[x] " : "[  ] ") + s.text, s_Text); y += 15; }
            }
            Handles.EndGUI();
            if (Event.current.type == EventType.MouseMove) sv.Repaint();
        }
    }
}
