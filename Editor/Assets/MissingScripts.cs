using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Missing Scripts: every object in the open scenes and every prefab that has a missing script (a component
    /// whose script was deleted or renamed: "The referenced script is missing") or a broken reference (a field that
    /// pointed at something that's gone). Click to go to it; "Remove missing scripts" cleans them (Ctrl+Z works for scene
    /// objects; prefabs are saved).
    /// </summary>
    public class MissingScripts : EditorWindow
    {
        [MenuItem("Tools/Missing Scripts", false, 153)]
        static void Open() { var w = GetWindow<MissingScripts>(false, "Missing Scripts", true); w.minSize = new Vector2(340, 260); w.Scan(); }

        class Hit { public string where, what, prefabPath; public GameObject go; public int missing; public List<string> broken = new List<string>(); }
        List<Hit> m_Hits = new List<Hit>(); Vector2 m_Scroll; bool m_Scanned; int m_Prefabs;
        GUIStyle m_Head;

        void Scan()
        {
            m_Hits.Clear(); m_Scanned = true;
            // the open scenes
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s); if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) Check(t.gameObject, scene.name, null);
            }
            // every prefab in Assets
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            m_Prefabs = prefabs.Count;
            try
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    if (i % 20 == 0 && EditorUtility.DisplayCancelableProgressBar("Missing Scripts", "Checking prefabs...", i / (float)prefabs.Count)) break;
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]); if (!root) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) Check(t.gameObject, System.IO.Path.GetFileNameWithoutExtension(prefabs[i]) + " (prefab)", prefabs[i]);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Repaint();
        }

        void Check(GameObject go, string where, string prefabPath)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            var broken = new List<string>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (!c) continue;
                var so = new SerializedObject(c); var it = so.GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        broken.Add(c.GetType().Name + " › " + it.displayName);
            }
            if (missing == 0 && broken.Count == 0) return;
            m_Hits.Add(new Hit { go = go, where = where, what = PathOf(go), prefabPath = prefabPath, missing = missing, broken = broken });
        }

        static string PathOf(GameObject go) { var p = go.name; for (var t = go.transform.parent; t != null; t = t.parent) p = t.name + " › " + p; return p; }

        void OnGUI()
        {
            if (m_Head == null) m_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(" Check again", EditorGUIUtility.IconContent("d_Refresh").image), GUILayout.Height(24))) Scan();
            int total = m_Hits.Sum(h => h.missing);
            GUI.enabled = total > 0;
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.93f, 0.45f, 0.4f);
            if (GUILayout.Button("Remove " + total + " missing script" + (total == 1 ? "" : "s"), GUILayout.Height(24))) EditorApplication.delayCall += RemoveAll;
            GUI.backgroundColor = ob; GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (!m_Scanned) return;
            if (m_Hits.Count == 0)
            {
                var r = EditorGUILayout.GetControlRect(false, 46);
                EditorGUI.DrawRect(r, new Color(0.3f, 0.55f, 0.35f));
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 20), "All clean", m_Head);
                GUI.Label(new Rect(r.x + 10, r.y + 24, r.width - 20, 18), "The open scenes and " + m_Prefabs + " prefabs have no missing scripts or broken references.", EditorStyles.miniLabel);
                return;
            }
            EditorGUILayout.LabelField(m_Hits.Count + " object" + (m_Hits.Count == 1 ? "" : "s") + " with problems   ·   checked the open scenes and " + m_Prefabs + " prefabs", EditorStyles.centeredGreyMiniLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var h in m_Hits)
            {
                if (!h.go) continue;
                var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(box.x, box.y, 4, box.height), h.missing > 0 ? new Color(0.93f, 0.4f, 0.35f) : new Color(0.97f, 0.7f, 0.3f));
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(6);
                if (GUILayout.Button(h.what, EditorStyles.linkLabel)) { var hh = h; EditorApplication.delayCall += () => Go(hh); }
                GUILayout.FlexibleSpace();
                GUILayout.Label(h.where, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                if (h.missing > 0) { EditorGUILayout.BeginHorizontal(); GUILayout.Space(10); GUILayout.Label(h.missing + " missing script" + (h.missing == 1 ? "" : "s"), EditorStyles.miniBoldLabel); EditorGUILayout.EndHorizontal(); }
                foreach (var b in h.broken.Take(5)) { EditorGUILayout.BeginHorizontal(); GUILayout.Space(10); GUILayout.Label("Broken reference: " + b, EditorStyles.miniLabel); EditorGUILayout.EndHorizontal(); }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        void Go(Hit h)
        {
            if (h.prefabPath == null) { Selection.activeGameObject = h.go; EditorGUIUtility.PingObject(h.go); return; }
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(h.prefabPath));
            var st = PrefabStageUtility.GetCurrentPrefabStage();
            if (st == null) return;
            var found = st.prefabContentsRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => PathOf(t.gameObject) == h.what);
            if (found) { Selection.activeGameObject = found.gameObject; EditorGUIUtility.PingObject(found.gameObject); }
        }

        void RemoveAll()
        {
            int total = m_Hits.Sum(h => h.missing);
            if (!EditorUtility.DisplayDialog("Missing Scripts", "Remove " + total + " missing script component(s)?\nScene objects: Ctrl+Z brings them back. Prefabs are saved straight away.", "Remove", "Cancel")) return;
            int removed = 0;
            foreach (var h in m_Hits.Where(h => h.missing > 0 && h.go))
            {
                if (h.prefabPath == null) { Undo.RegisterCompleteObjectUndo(h.go, "Remove missing scripts"); removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(h.go); EditorSceneManager.MarkSceneDirty(h.go.scene); }
            }
            foreach (var path in m_Hits.Where(h => h.missing > 0 && h.prefabPath != null).Select(h => h.prefabPath).Distinct())
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { foreach (var t in root.GetComponentsInChildren<Transform>(true)) removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[Missing Scripts] removed " + removed);
            Scan();
        }
    }
}
