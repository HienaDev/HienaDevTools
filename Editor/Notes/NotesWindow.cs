using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Notes & TODO: every note and TODO in the project in one list. Notes pinned to objects (saved in their scene
    /// or prefab, so git shares them) and project TODOs not tied to an object (saved in ProjectSettings/HienaDevTodo.json).
    /// Filter by who it's for, tick things done, Go jumps to the object (opening its scene or prefab).
    /// </summary>
    public class NotesWindow : EditorWindow
    {
        [MenuItem("Tools/Notes & TODO", false, 140)]
        public static void Open()
        {
            // a tab beside the Inspector (where it stays, like any tab you dock)
            var inspector = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            var w = inspector != null ? GetWindow<NotesWindow>("Notes & TODO", true, inspector) : GetWindow<NotesWindow>(false, "Notes & TODO", true);
            w.minSize = new Vector2(300, 260);
        }

        [MenuItem("GameObject/Add Note or TODO", false, 0)]
        static void AddNote()
        {
            foreach (var go in Selection.gameObjects) if (!go.GetComponent<DevNote>()) Undo.AddComponent<DevNote>(go);
            EditorApplication.RepaintHierarchyWindow();
        }
        [MenuItem("GameObject/Add Note or TODO", true)] static bool AddNoteOk() => Selection.gameObjects.Length > 0;

        // ---- project TODOs (not tied to an object), shared through git
        [Serializable] public class Item { public string text = "", forWho = ""; public int priority = 1; public bool done; public string link = ""; public Color color = new Color(0.55f, 0.75f, 0.95f); }
        [Serializable] class TodoFile { public List<Item> items = new List<Item>(); }
        static string TodoPath => Path.GetFullPath("ProjectSettings/HienaDevTodo.json");
        TodoFile m_Todo; DateTime m_TodoAt;
        TodoFile Todo
        {
            get
            {
                var t = File.Exists(TodoPath) ? File.GetLastWriteTimeUtc(TodoPath) : DateTime.MinValue;
                if (m_Todo == null || t != m_TodoAt) { m_Todo = new TodoFile(); m_TodoAt = t; try { if (File.Exists(TodoPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(TodoPath), m_Todo); } catch { } }
                return m_Todo;
            }
        }
        void SaveTodo() { File.WriteAllText(TodoPath, JsonUtility.ToJson(m_Todo, true)); m_TodoAt = File.GetLastWriteTimeUtc(TodoPath); }

        // ---- one row of the list
        class Row
        {
            public string text, forWho, where, file; public int priority; public bool done, isTodo; public Color color;
            public DevNote live; public Item item; public long fileId; public string objectName;
        }

        string m_New = ""; static GUIStyle s_Hint; GUIStyle m_Where; System.Action m_Pending;
        Vector2 m_Scroll; bool m_HideDone; string m_For = ""; string m_Search = "";
        List<Row> m_Closed = new List<Row>(); double m_ScannedAt = -100;
        GUIStyle m_Text, m_Chip;

        void OnEnable() { EditorSceneManager.sceneSaved += _ => m_ScannedAt = -100; }
        void OnFocus() { if (EditorApplication.timeSinceStartup - m_ScannedAt > 5) m_ScannedAt = -100; }

        void OnGUI()
        {
            if (m_Text == null) { m_Text = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false, fontSize = 12 }; m_Chip = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } }; }
            if (EditorApplication.timeSinceStartup - m_ScannedAt > 30) Scan();
            var rows = Rows();

            // ---- top bar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var old = GUI.backgroundColor; GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
            if (GUILayout.Button(new GUIContent("+ In the scene", "A new note in the middle of the Scene view (Ctrl+Alt+N); it remembers the view"), EditorStyles.toolbarButton, GUILayout.Width(92))) EditorApplication.delayCall += SceneNotes.AddHere;
            GUI.backgroundColor = old;
            var whos = new List<string> { "Everyone" }; whos.AddRange(rows.Select(r => r.forWho).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s));
            int sel = Mathf.Max(0, whos.FindIndex(s => string.Equals(s, m_For, StringComparison.OrdinalIgnoreCase)));
            sel = EditorGUILayout.Popup(sel, whos.Select(s => s == "Everyone" ? "For: everyone" : "For: " + s).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(120));
            m_For = sel == 0 ? "" : whos[sel];
            m_HideDone = GUILayout.Toggle(m_HideDone, "Hide done", EditorStyles.toolbarButton, GUILayout.Width(70));
            SceneNotes.Show = GUILayout.Toggle(SceneNotes.Show, new GUIContent(EditorGUIUtility.IconContent(SceneNotes.Show ? "d_scenevis_visible_hover" : "d_scenevis_hidden_hover").image, "Show the notes as cards in the Scene view"), EditorStyles.toolbarButton, GUILayout.Width(28));
            m_Search = EditorGUILayout.TextField(m_Search, EditorStyles.toolbarSearchField);
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Refresh"), EditorStyles.toolbarButton, GUILayout.Width(26))) Scan();
            EditorGUILayout.EndHorizontal();

            var shown = rows.Where(r => (!m_HideDone || !r.done)
                                     && (m_For == "" || string.Equals((r.forWho ?? "").Trim(), m_For, StringComparison.OrdinalIgnoreCase))
                                     && (m_Search == "" || (r.text ?? "").IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0 || (r.where ?? "").IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0))
                            .OrderBy(r => r.done).ThenByDescending(r => r.priority).ToList();
            int open = rows.Count(r => r.isTodo && !r.done);
            EditorGUILayout.LabelField(open + " to do" + (rows.Count(r => r.done) > 0 ? ", " + rows.Count(r => r.done) + " done" : "") + "   ·   right-click an object > Add Note or TODO", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("HienaDevQuickTodo");
            m_New = EditorGUILayout.TextField(m_New, GUILayout.Height(22));
            if (string.IsNullOrEmpty(m_New) && Event.current.type == EventType.Repaint)
            {
                var hint = GUILayoutUtility.GetLastRect(); hint.x += 4;
                if (s_Hint == null) s_Hint = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }, alignment = TextAnchor.MiddleLeft };
                GUI.Label(hint, "Add a task... (Enter)", s_Hint);
            }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "HienaDevQuickTodo";
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
            if ((GUILayout.Button("Add", GUILayout.Width(50), GUILayout.Height(22)) || enter) && !string.IsNullOrWhiteSpace(m_New))
            {
                Todo.items.Insert(0, new Item { text = m_New.Trim(), forWho = m_For });
                SaveTodo(); m_New = "";
                if (enter) Event.current.Use();
                GUI.FocusControl("HienaDevQuickTodo");
            }
            GUI.backgroundColor = ob;
            EditorGUILayout.EndHorizontal();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var r in shown) if (!DrawRow(r)) break;
            if (shown.Count == 0) EditorGUILayout.LabelField(rows.Count == 0 ? "No notes yet." : "Nothing here with these filters.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            // (changes made by a row run after the whole list is drawn: changing it half-way broke Unity's layout)
            if (m_Pending != null) { var act = m_Pending; m_Pending = null; act(); Repaint(); }
        }

        // false when the list changed (stop drawing)
        bool DrawRow(Row r)
        {
            var box = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            // a coloured bar on the left: the note's colour (red edge: high priority)
            if (Event.current.type == EventType.Repaint)
            {
                var baseBg = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.86f, 0.86f, 0.86f);
                var tint = r.done ? new Color(0.45f, 0.75f, 0.45f) : r.color;
                EditorGUI.DrawRect(box, Color.Lerp(baseBg, tint, r.done ? 0.18f : 0.35f));
                EditorGUI.DrawRect(new Rect(box.x, box.y, 5, box.height), r.done ? tint : r.priority == 2 ? new Color(0.95f, 0.3f, 0.25f) : tint);
            }
            GUILayout.Space(4);
            if (r.isTodo)
            {
                bool d = EditorGUILayout.Toggle(r.done, GUILayout.Width(16));
                if (d != r.done) { var rr = r; m_Pending = () => SetDone(rr, d); }
            }
            else GUILayout.Label(EditorGUIUtility.IconContent("d_console.infoicon.sml"), GUILayout.Width(16));
            EditorGUILayout.BeginVertical();
            if (r.item != null)
            {
                string t = EditorGUILayout.TextArea(r.item.text, m_Text);
                if (r.done && Event.current.type == EventType.Repaint) DevNoteEditor.StrikeLines(GUILayoutUtility.GetLastRect(), r.item.text, m_Text);
                if (t != r.item.text) { r.item.text = t; SaveTodo(); }
            }
            else
            {
                var old = GUI.color; if (r.done) GUI.color = new Color(1, 1, 1, 0.55f);
                GUILayout.Label(string.IsNullOrWhiteSpace(r.text) ? "(empty note)" : r.text, m_Text);
                if (r.done && Event.current.type == EventType.Repaint) DevNoteEditor.StrikeLines(GUILayoutUtility.GetLastRect(), r.text, m_Text);
                GUI.color = old;
            }
            EditorGUILayout.BeginHorizontal();
            if (r.item != null)
            {
                string fw = EditorGUILayout.TextField(r.item.forWho, EditorStyles.miniTextField, GUILayout.Width(80));
                if (fw != r.item.forWho) { r.item.forWho = fw; SaveTodo(); }
                var nc = EditorGUILayout.ColorField(GUIContent.none, r.item.color, false, false, false, GUILayout.Width(28));
                if (nc != r.item.color) { r.item.color = nc; SaveTodo(); }
                int p = EditorGUILayout.Popup(r.item.priority, new[] { "Low", "Normal", "High" }, EditorStyles.miniPullDown, GUILayout.Width(60));
                if (p != r.item.priority) { r.item.priority = p; SaveTodo(); }
                var linked = string.IsNullOrEmpty(r.item.link) ? null : AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(r.item.link));
                var nl = EditorGUILayout.ObjectField(linked, typeof(UnityEngine.Object), false, GUILayout.MinWidth(60));
                if (nl != linked) { r.item.link = nl ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(nl)) : ""; SaveTodo(); }
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_TreeEditor.Trash"), EditorStyles.miniButton, GUILayout.Width(24))) { var it = r.item; m_Pending = () => { Todo.items.Remove(it); SaveTodo(); }; }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(r.forWho)) Chip("for " + r.forWho, new Color(0.3f, 0.45f, 0.7f));
                if (r.priority == 2 && !r.done) Chip("high", new Color(0.75f, 0.3f, 0.25f));
                GUILayout.Label(new GUIContent(r.where, r.where), m_Where ?? (m_Where = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip }), GUILayout.MinWidth(0));
                GUILayout.FlexibleSpace();
                if (r.live && r.live.steps.Count > 0) Chip(r.live.StepsDone + "/" + r.live.steps.Count + " steps", new Color(0.35f, 0.55f, 0.4f));
                if (r.live && GUILayout.Button(new GUIContent("Look", "Show it in the Scene view"), EditorStyles.miniButton, GUILayout.Width(40))) { var lr = r.live; m_Pending = () => EditorApplication.delayCall += () => SceneNotes.Look(lr); }
                if (GUILayout.Button("Go", EditorStyles.miniButton, GUILayout.Width(36))) { var gr = r; m_Pending = () => EditorApplication.delayCall += () => Go(gr); }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            return true;
        }

        void Chip(string text, Color c)
        {
            var size = m_Chip.CalcSize(new GUIContent(text));
            var rr = GUILayoutUtility.GetRect(size.x + 8, 16, GUILayout.Width(size.x + 8));
            EditorGUI.DrawRect(rr, c); GUI.Label(rr, text, m_Chip);
        }

        void SetDone(Row r, bool done)
        {
            if (r.item != null) { r.item.done = done; SaveTodo(); return; }
            if (r.live) { Undo.RecordObject(r.live, done ? "TODO done" : "TODO not done"); r.live.done = done; EditorUtility.SetDirty(r.live); EditorApplication.RepaintHierarchyWindow(); return; }
            // a note in a closed scene or prefab: go there first
            if (EditorUtility.DisplayDialog("Notes & TODO", "This TODO is in " + Path.GetFileName(r.file) + ", which isn't open. Open it to tick it?", "Open it", "Cancel")) Go(r);
        }

        // ---- every row: live notes in the open scenes / prefab stage, notes found in other scene and prefab files, project TODOs
        List<Row> Rows()
        {
            var rows = new List<Row>();
            var openFiles = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i); if (!s.isLoaded) continue;
                openFiles.Add(s.path);
                foreach (var root in s.GetRootGameObjects())
                    foreach (var n in root.GetComponentsInChildren<DevNote>(true)) rows.Add(Live(n, s.name));
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                openFiles.Add(stage.assetPath);
                foreach (var n in stage.prefabContentsRoot.GetComponentsInChildren<DevNote>(true)) rows.Add(Live(n, Path.GetFileNameWithoutExtension(stage.assetPath) + " (prefab)"));
            }
            rows.AddRange(m_Closed.Where(r => !openFiles.Contains(r.file)));
            foreach (var it in Todo.items) rows.Add(new Row { item = it, text = it.text, forWho = it.forWho, priority = it.priority, done = it.done, isTodo = true, color = it.color.a > 0.01f ? it.color : new Color(0.55f, 0.75f, 0.95f), where = "Project" });
            return rows;
        }

        static Row Live(DevNote n, string where) => new Row
        {
            live = n, text = n.text, forWho = n.forWho, priority = (int)n.priority, done = n.done, isTodo = n.kind == DevNote.Kind.Todo,
            color = n.color, where = where + " › " + n.gameObject.name,
        };

        void Go(Row r)
        {
            if (r.live) { Selection.activeObject = r.live.gameObject; EditorGUIUtility.PingObject(r.live.gameObject); return; }
            if (string.IsNullOrEmpty(r.file)) return;
            if (r.file.EndsWith(".prefab"))
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(r.file));
                var st = PrefabStageUtility.GetCurrentPrefabStage();
                var n = st ? st.prefabContentsRoot.GetComponentsInChildren<DevNote>(true).FirstOrDefault(x => x.text == r.text) : null;
                if (n) { Selection.activeObject = n.gameObject; EditorGUIUtility.PingObject(n.gameObject); }
                return;
            }
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Notes & TODO", "Stop Play mode to open another scene.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(r.file);
            foreach (var root in scene.GetRootGameObjects())
            {
                var n = root.GetComponentsInChildren<DevNote>(true).FirstOrDefault(x => x.text == r.text);
                if (n) { Selection.activeObject = n.gameObject; EditorGUIUtility.PingObject(n.gameObject); break; }
            }
            m_ScannedAt = -100;
        }

        // ---- notes in scenes and prefabs that aren't open: read from the files (they're text)
        void Scan()
        {
            m_ScannedAt = EditorApplication.timeSinceStartup;
            m_Closed.Clear();
            var script = AssetDatabase.FindAssets("DevNote t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<MonoScript>).FirstOrDefault(m => m && m.GetClass() == typeof(DevNote));
            if (!script) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            var files = AssetDatabase.FindAssets("t:SceneAsset t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct();
            foreach (var f in files)
            {
                string txt;
                try { txt = File.ReadAllText(f); } catch { continue; }
                if (!txt.Contains(guid)) continue;
                var docs = txt.Split(new[] { "\n--- " }, StringSplitOptions.None);
                var names = new Dictionary<string, string>();
                foreach (var d in docs)
                {
                    var m = Regex.Match(d, @"^!u!1 &(\d+)");
                    if (m.Success) { var nm = Regex.Match(d, @"\n\s*m_Name: (.*)"); if (nm.Success) names[m.Groups[1].Value] = nm.Groups[1].Value.Trim(); }
                }
                foreach (var d in docs)
                {
                    if (!d.Contains("guid: " + guid)) continue;
                    var go = Regex.Match(d, @"m_GameObject: \{fileID: (\d+)\}");
                    string goName = go.Success && names.TryGetValue(go.Groups[1].Value, out var gn) ? gn : "?";
                    var row = new Row
                    {
                        file = f, text = Field(d, "text"), forWho = Field(d, "forWho"), isTodo = Field(d, "kind") != "0",
                        priority = int.TryParse(Field(d, "priority"), out var p) ? p : 1, done = Field(d, "done") == "1",
                        where = Path.GetFileNameWithoutExtension(f) + (f.EndsWith(".prefab") ? " (prefab)" : "") + " › " + goName, color = new Color(1f, 0.82f, 0.25f),
                    };
                    var col = Regex.Match(d, @"\n\s*color: \{r: ([\d.eE-]+), g: ([\d.eE-]+), b: ([\d.eE-]+)");
                    if (col.Success && float.TryParse(col.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cr)
                        && float.TryParse(col.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cg)
                        && float.TryParse(col.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cb)) row.color = new Color(cr, cg, cb);
                    m_Closed.Add(row);
                }
            }
        }

        // one field of a YAML block: plain, 'single' or "double" quoted (first line of multi-line texts)
        static string Field(string doc, string name)
        {
            var m = Regex.Match(doc, @"\n\s*" + name + @": ?(.*)");
            if (!m.Success) return "";
            string v = m.Groups[1].Value.TrimEnd('\r');
            if (v.StartsWith("'")) return v.Trim('\'').Replace("''", "'");
            if (v.StartsWith("\"")) return Regex.Unescape(v.Trim('"'));
            return v.Trim();
        }

        void OnInspectorUpdate() => Repaint();
    }
}
