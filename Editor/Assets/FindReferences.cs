using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Right-click an asset > Find References: every scene, prefab, material and other asset that uses it, plus the
    /// objects in the open scenes that point at it. Nothing found = nothing in the project uses it (code that loads it by
    /// name, from Resources or Addressables isn't counted).
    /// </summary>
    public class FindReferences : EditorWindow
    {
        [MenuItem("Assets/Find References", false, 25)]
        static void FromMenu() { var w = GetWindow<FindReferences>(false, "References", true); w.minSize = new Vector2(320, 240); w.Search(Selection.activeObject); }
        [MenuItem("Assets/Find References", true)]
        static bool FromMenuOk() { var o = Selection.activeObject; var p = o ? AssetDatabase.GetAssetPath(o) : null; return !string.IsNullOrEmpty(p) && !AssetDatabase.IsValidFolder(p); }

        [MenuItem("Tools/Find References", false, 150)]
        static void Open() { var w = GetWindow<FindReferences>(false, "References", true); w.minSize = new Vector2(320, 240); }

        // the whole project's "what uses what", built once and kept until assets change
        static Dictionary<string, string[]> s_Deps;
        class Invalidate : AssetPostprocessor { static void OnPostprocessAllAssets(string[] a, string[] b, string[] c, string[] d) { s_Deps = null; } }

        [SerializeField] UnityEngine.Object m_Target;
        List<string> m_Assets = new List<string>();
        List<(GameObject go, string what)> m_InScene = new List<(GameObject, string)>();
        Vector2 m_Scroll; bool m_Searched;
        GUIStyle m_Head;

        void Search(UnityEngine.Object target)
        {
            m_Target = target; m_Assets.Clear(); m_InScene.Clear(); m_Searched = false;
            if (!target) return;
            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path)) return;
            if (s_Deps == null)
            {
                s_Deps = new Dictionary<string, string[]>();
                var all = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p)).ToArray();
                try
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (i % 200 == 0 && EditorUtility.DisplayCancelableProgressBar("Find References", "Reading what uses what (once)...", i / (float)all.Length)) { s_Deps = null; return; }
                        s_Deps[all[i]] = AssetDatabase.GetDependencies(all[i], false);
                    }
                }
                finally { EditorUtility.ClearProgressBar(); }
            }
            foreach (var kv in s_Deps) if (kv.Key != path && kv.Value.Contains(path)) m_Assets.Add(kv.Key);
            m_Assets.Sort((a, b) => Order(a).CompareTo(Order(b)) != 0 ? Order(a).CompareTo(Order(b)) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            // objects in the open scenes pointing at it (or at anything inside it, like a sprite in a texture)
            var inside = new HashSet<UnityEngine.Object>(AssetDatabase.LoadAllAssetsAtPath(path)) { target };
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s); if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                    {
                        if (!c) continue;
                        var so = new SerializedObject(c); var it = so.GetIterator();
                        while (it.Next(true))
                            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue && inside.Contains(it.objectReferenceValue))
                            { m_InScene.Add((c.gameObject, c.GetType().Name + " › " + it.displayName)); break; }
                    }
            }
            m_Searched = true;
            Repaint();
        }

        static int Order(string p) => p.EndsWith(".unity") ? 0 : p.EndsWith(".prefab") ? 1 : p.EndsWith(".mat") ? 2 : 3;

        void OnGUI()
        {
            if (m_Head == null) m_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            EditorGUILayout.BeginHorizontal();
            var t = EditorGUILayout.ObjectField(m_Target, typeof(UnityEngine.Object), false, GUILayout.Height(20));
            if (t != m_Target) Search(t);
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Refresh"), GUILayout.Width(28), GUILayout.Height(20))) { s_Deps = null; Search(m_Target); }
            EditorGUILayout.EndHorizontal();
            if (!m_Target) { EditorGUILayout.HelpBox("Drop an asset here, or right-click one in the Project window > Find References.", MessageType.None); return; }
            if (!m_Searched) return;

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Assets.Count == 0 && m_InScene.Count == 0)
            {
                var r = EditorGUILayout.GetControlRect(false, 46);
                EditorGUI.DrawRect(r, new Color(0.3f, 0.55f, 0.35f));
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 20), "Nothing uses it", m_Head);
                GUI.Label(new Rect(r.x + 10, r.y + 24, r.width - 20, 18), "(code that loads it by name, Resources or Addressables isn't counted)", EditorStyles.miniLabel);
            }
            if (m_InScene.Count > 0)
            {
                EditorGUILayout.LabelField("In the open scene (" + m_InScene.Count + ")", m_Head);
                foreach (var (go, what) in m_InScene)
                {
                    if (!go) continue;
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUILayout.Label(EditorGUIUtility.ObjectContent(go, typeof(GameObject)).image, GUILayout.Width(18), GUILayout.Height(18));
                    if (GUILayout.Button(go.name, EditorStyles.linkLabel)) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(what, EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(6);
            }
            if (m_Assets.Count > 0)
            {
                EditorGUILayout.LabelField("Used by " + m_Assets.Count + " asset" + (m_Assets.Count == 1 ? "" : "s"), m_Head);
                foreach (var p in m_Assets)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUILayout.Label(AssetDatabase.GetCachedIcon(p), GUILayout.Width(18), GUILayout.Height(18));
                    if (GUILayout.Button(new GUIContent(Path.GetFileName(p), p), EditorStyles.linkLabel)) { var o = AssetDatabase.LoadMainAssetAtPath(p); Selection.activeObject = o; EditorGUIUtility.PingObject(o); }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(Path.GetDirectoryName(p).Replace('\\', '/'), EditorStyles.miniLabel);
                    if (p.EndsWith(".unity") || p.EndsWith(".prefab"))
                        if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(44))) { string pp = p; EditorApplication.delayCall += () => OpenAsset(pp); }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static void OpenAsset(string p)
        {
            if (p.EndsWith(".unity")) { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(p); }
            else AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(p));
        }
    }
}
