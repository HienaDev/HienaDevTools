using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// The Hierarchy, easier to read: each object shows its main component's icon instead of the plain cube (a camera
    /// shows a camera, a light a bulb, your script its icon; an icon you picked for the object wins; prefabs keep theirs),
    /// and objects with a note or TODO show a little coloured tag on the right (hover: the text; click: go to it).
    /// On/off: Edit > Project Settings > HienaDev Dev Tools.
    /// </summary>
    [InitializeOnLoad]
    static class HierarchyLooks
    {
        static readonly HashSet<System.Type> Skip = new HashSet<System.Type> { typeof(Transform), typeof(RectTransform), typeof(CanvasRenderer), typeof(DevNote) };
        static GUIStyle s_Tag;

        static HierarchyLooks() { EditorApplication.hierarchyWindowItemOnGUI += OnItem; }

        static void OnItem(int id, Rect r)
        {
            var go = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (!go) return;
            if (DevToolsSettings.HierarchyIcons) DrawIcon(id, go, r);
            if (DevToolsSettings.HierarchyNotes) DrawNote(go, r);
        }

        // ---- the icon on the left
        static void DrawIcon(int id, GameObject go, Rect r)
        {
            if (EditorGUIUtility.GetIconForObject(go) != null) return; // (an icon picked for this object: Unity draws it)
            if (PrefabUtility.IsAnyPrefabInstanceRoot(go)) return;    // (prefabs keep the blue cube: it says "prefab")
            Component main = null;
            foreach (var c in go.GetComponents<Component>()) { if (c == null || Skip.Contains(c.GetType())) continue; main = c; break; }
            if (main == null) return;
            var tex = EditorGUIUtility.ObjectContent(main, main.GetType()).image;
            if (!tex) return;
            var ir = new Rect(r.x, r.y, 16, 16);
            EditorGUI.DrawRect(ir, RowColour(id, r));
            var old = GUI.color; if (!go.activeInHierarchy) GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.DrawTexture(ir, tex, ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        // the Hierarchy's own background under the icon (selected, hovered or plain)
        static Color RowColour(int id, Rect r)
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (Selection.Contains(id))
            {
                bool focused = EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "SceneHierarchyWindow";
                return focused ? (pro ? new Color(0.172f, 0.365f, 0.529f) : new Color(0.227f, 0.447f, 0.69f)) : (pro ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.68f, 0.68f, 0.68f));
            }
            var full = new Rect(0, r.y, r.xMax + 400, r.height);
            if (full.Contains(Event.current.mousePosition)) return pro ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.7f, 0.7f, 0.7f);
            return pro ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.784f, 0.784f, 0.784f);
        }

        // ---- the note tag on the right
        static void DrawNote(GameObject go, Rect r)
        {
            var n = go.GetComponent<DevNote>();
            if (!n) return;
            if (s_Tag == null) s_Tag = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black }, hover = { textColor = Color.black }, padding = new RectOffset(0, 0, 0, 0) };
            string label = n.kind == DevNote.Kind.Note ? "NOTE" : n.done ? "DONE" : "TODO";
            float w = 38;
            var tr = new Rect(r.xMax - w - 2, r.y + 2, w, r.height - 4);
            var c = n.done ? new Color(0.45f, 0.75f, 0.45f) : n.color;
            EditorGUI.DrawRect(tr, c);
            if (n.priority == DevNote.Priority.High && !n.done) EditorGUI.DrawRect(new Rect(tr.x - 4, tr.y, 2, tr.height), new Color(0.95f, 0.3f, 0.25f));
            string tip = (n.kind == DevNote.Kind.Todo ? (n.done ? "Done: " : "To do: ") : "Note: ") + n.text + (string.IsNullOrEmpty(n.forWho) ? "" : "\nFor: " + n.forWho) + (n.priority == DevNote.Priority.High ? "\nHigh priority" : "");
            if (GUI.Button(tr, new GUIContent(label), s_Tag)) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
            // hover: the note's text in a little card (the Hierarchy repaints as the mouse moves over it)
            var ev = Event.current;
            if (ev.type == EventType.Repaint || ev.type == EventType.MouseMove)
            {
                if (tr.Contains(ev.mousePosition)) NoteHover.Show(n, new Rect(GUIUtility.GUIToScreenPoint(tr.position), tr.size));
                else if (NoteHover.IsShowing(n)) NoteHover.Hide();
            }
            if (n.kind == DevNote.Kind.Todo)
            {
                // a tick box just left of the tag: click to mark it done (or not)
                var cr = new Rect(tr.x - (n.priority == DevNote.Priority.High && !n.done ? 22 : 18), r.y + 1, 16, 16);
                bool d = GUI.Toggle(cr, n.done, new GUIContent("", n.done ? "Done: click to undo" : "Click when it's done"));
                if (d != n.done) { Undo.RecordObject(n, d ? "TODO done" : "TODO not done"); n.done = d; EditorUtility.SetDirty(n); }
                // done: the object's name crossed out
                if (n.done && Event.current.type == EventType.Repaint)
                {
                    float nameW = EditorStyles.label.CalcSize(new GUIContent(go.name)).x;
                    EditorGUI.DrawRect(new Rect(r.x + 18, r.y + r.height * 0.5f, Mathf.Min(nameW, cr.x - r.x - 22), 1.2f), EditorGUIUtility.isProSkin ? new Color(0.85f, 0.85f, 0.85f, 0.8f) : new Color(0.1f, 0.1f, 0.1f, 0.8f));
                }
            }
        }
    }

    /// <summary>Notes stay out of builds.</summary>
    class StripDevNotes : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            if (report == null) return; // (entering Play mode: keep them)
            foreach (var root in scene.GetRootGameObjects())
                foreach (var n in root.GetComponentsInChildren<DevNote>(true)) Object.DestroyImmediate(n);
        }
    }
}
