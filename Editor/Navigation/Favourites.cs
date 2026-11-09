using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace HienaDev.DevTools
{
    /// <summary>
    /// Favourites: things you jump to all the time. Drag assets or scene objects onto the Favourites tab (Tools >
    /// Favourites) or right-click > Add to Favourites; click one to select it, double-click to open it (a scene opens, a
    /// prefab opens, a script opens in your code editor). Also in the toolbar's ★ list. Kept per person, per project
    /// (UserSettings/HienaDevFavourites.json).
    /// </summary>
    public class Favourites : EditorWindow
    {
        public const string ToolbarPath = "HienaDev/Favourites";
        [Serializable] public class Fav { public string id, name, scene; }
        [Serializable] class Data { public List<Fav> items = new List<Fav>(); }
        static Data s_Data;
        static string FilePath => Path.GetFullPath("UserSettings/HienaDevFavourites.json");
        static Data D { get { if (s_Data == null) { s_Data = new Data(); try { if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_Data); } catch { } } return s_Data; } }
        static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, JsonUtility.ToJson(D, true));
#if UNITY_6000_3_OR_NEWER
            MainToolbar.Refresh(ToolbarPath);
#endif
            foreach (var w in Resources.FindObjectsOfTypeAll<Favourites>()) w.Repaint();
        }

        [MenuItem("Tools/Favourites", false, 154)] public static void Open() { var w = GetWindow<Favourites>(false, "Favourites", true); w.minSize = new Vector2(200, 160); }
        [MenuItem("Assets/Add to Favourites", false, 27)] static void AddAssets() { foreach (var o in Selection.objects) Add(o); }
        [MenuItem("GameObject/Add to Favourites", false, 2)] static void AddObjects() { foreach (var o in Selection.gameObjects) Add(o); }

        public static void Add(UnityEngine.Object o)
        {
            if (!o) return;
            string id = GlobalObjectId.GetGlobalObjectIdSlow(o).ToString();
            if (D.items.Any(f => f.id == id)) return;
            var go = o as GameObject;
            D.items.Add(new Fav { id = id, name = o.name, scene = go && go.scene.IsValid() ? go.scene.path : null });
            Save();
        }

        static UnityEngine.Object Resolve(Fav f) => GlobalObjectId.TryParse(f.id, out var g) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(g) : null;

        public static void Select(Fav f, bool open)
        {
            var o = Resolve(f);
            if (!o && !string.IsNullOrEmpty(f.scene))
            {
                // an object in a scene that isn't open: open the scene first
                if (!EditorUtility.DisplayDialog("Favourites", f.name + " is in " + Path.GetFileNameWithoutExtension(f.scene) + ", which isn't open. Open it?", "Open", "Cancel")) return;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(f.scene);
                o = Resolve(f);
            }
            if (!o) { EditorUtility.DisplayDialog("Favourites", f.name + " can't be found any more (deleted?).", "OK"); return; }
            Selection.activeObject = o; EditorGUIUtility.PingObject(o);
            if (open && !(o is GameObject go && go.scene.IsValid())) AssetDatabase.OpenAsset(o);
        }

        // ---- the tab
        Vector2 m_Scroll; GUIStyle m_Row;
        void OnGUI()
        {
            if (m_Row == null) m_Row = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 0, 0) };
            var ev = Event.current;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            Fav remove = null; int moveFrom = -1, moveTo = -1;
            for (int i = 0; i < D.items.Count; i++)
            {
                var f = D.items[i];
                var o = Resolve(f);
                var r = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
                bool hover = r.Contains(ev.mousePosition);
                if (ev.type == EventType.Repaint) EditorGUI.DrawRect(r, hover ? new Color(1, 1, 1, 0.08f) : i % 2 == 0 ? new Color(1, 1, 1, 0.02f) : Color.clear);
                Texture icon = o ? AssetPreview.GetMiniThumbnail(o) : EditorGUIUtility.IconContent("d_console.warnicon.sml").image;
                if (icon) GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, 18, 18), icon, ScaleMode.ScaleToFit);
                string label = o ? o.name : f.name + (string.IsNullOrEmpty(f.scene) ? "  (missing)" : "  (in " + Path.GetFileNameWithoutExtension(f.scene) + ")");
                var old = GUI.color; if (!o) GUI.color = new Color(1, 1, 1, 0.55f);
                GUI.Label(new Rect(r.x + 26, r.y, r.width - 30, r.height), label, m_Row);
                GUI.color = old;
                if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
                {
                    if (ev.button == 0) { var ff = f; bool open = ev.clickCount == 2; EditorApplication.delayCall += () => Select(ff, open); ev.Use(); }
                    else if (ev.button == 1)
                    {
                        var m = new GenericMenu(); var ff = f; int ii = i;
                        m.AddItem(new GUIContent("Open"), false, () => Select(ff, true));
                        m.AddItem(new GUIContent("Move up"), false, () => { if (ii > 0) { D.items.RemoveAt(ii); D.items.Insert(ii - 1, ff); Save(); } });
                        m.AddItem(new GUIContent("Move down"), false, () => { if (ii < D.items.Count - 1) { D.items.RemoveAt(ii); D.items.Insert(ii + 1, ff); Save(); } });
                        m.AddSeparator(""); m.AddItem(new GUIContent("Remove"), false, () => { D.items.Remove(ff); Save(); });
                        m.ShowAsContext(); ev.Use();
                    }
                }
                // drag a favourite out (into the Scene, an Inspector field...)
                if (ev.type == EventType.MouseDrag && r.Contains(ev.mousePosition) && o)
                { DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new[] { o }; DragAndDrop.StartDrag(o.name); ev.Use(); }
            }
            if (D.items.Count == 0) EditorGUILayout.LabelField("Drag assets or objects here, or right-click > Add to Favourites", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(40));
            EditorGUILayout.EndScrollView();
            if (remove != null) { D.items.Remove(remove); Save(); }
            if (moveFrom >= 0 && moveTo >= 0) Save();
            // dropping things in
            var all = new Rect(0, 0, position.width, position.height);
            if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && all.Contains(ev.mousePosition) && DragAndDrop.objectReferences.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var o in DragAndDrop.objectReferences) Add(o); }
                ev.Use();
            }
            if (ev.type == EventType.MouseMove) Repaint();
        }
        void OnEnable() { wantsMouseMove = true; }

        public static void ToolbarMenu(Rect r)
        {
            var m = new GenericMenu();
            foreach (var f in D.items) { var ff = f; m.AddItem(new GUIContent(f.name.Replace("/", "∕")), false, () => Select(ff, false)); }
            if (D.items.Count == 0) m.AddDisabledItem(new GUIContent("No favourites yet (right-click something > Add to Favourites)"));
            m.AddSeparator("");
            if (Selection.activeObject) m.AddItem(new GUIContent("Add the selection"), false, () => { foreach (var o in Selection.objects) Add(o); });
            m.AddItem(new GUIContent("Open the Favourites tab"), false, Open);
            m.DropDown(r);
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        static MainToolbarElement Create() => new MainToolbarDropdown(new MainToolbarContent(EditorGUIUtility.IconContent("d_Favorite").image as Texture2D, "Favourites: jump to the things you use all the time"), ToolbarMenu);
#endif
    }
}
