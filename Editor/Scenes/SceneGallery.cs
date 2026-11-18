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
    /// Every scene as a picture: click one to open it (Tools > Scene Gallery, or "Scene gallery..." at the top of the
    /// toolbar's scenes list). The pictures are taken when you save a scene (through its main camera, or the Scene view's),
    /// so the gallery never opens scenes and stays quick however big they are. Kept in Library/HienaDevTools/SceneThumbs.
    /// </summary>
    [InitializeOnLoad]
    public class SceneGallery : EditorWindow
    {
        const int W = 320, H = 180;
        static string Folder => Path.GetFullPath("Library/HienaDevTools/SceneThumbs");
        public static bool TakeOnSave { get => EditorPrefs.GetBool("HienaDev.SceneThumbs", true); set => EditorPrefs.SetBool("HienaDev.SceneThumbs", value); }

        static SceneGallery() { EditorSceneManager.sceneSaved += s => { if (TakeOnSave) Capture(s); }; }

        [MenuItem("Tools/Scene Gallery", false, 155)]
        public static void Open() { var w = GetWindow<SceneGallery>(false, "Scene Gallery", true); w.minSize = new Vector2(360, 300); }

        // ---- the picture of a scene: its main camera (how the game sees it), else the Scene view
        public static bool Capture(Scene s)
        {
            if (!s.IsValid() || string.IsNullOrEmpty(s.path) || !s.isLoaded) return false;
            Camera cam = null;
            try
            {
                var cams = s.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(false)).Where(c => c.enabled && c.targetTexture == null).ToList();
                cam = cams.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cams.FirstOrDefault();
                if (cam == null && SceneView.lastActiveSceneView != null) cam = SceneView.lastActiveSceneView.camera;
                if (cam == null) return false;
                var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var keep = cam.targetTexture;
                try { cam.targetTexture = rt; cam.Render(); }
                finally { cam.targetTexture = keep; } // (never leave a camera drawing into a texture)
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(Path.Combine(Folder, AssetDatabase.AssetPathToGUID(s.path) + ".png"), tex.EncodeToPNG());
                DestroyImmediate(tex);
                s_Thumbs.Remove(AssetDatabase.AssetPathToGUID(s.path));
                foreach (var g in Resources.FindObjectsOfTypeAll<SceneGallery>()) g.Repaint();
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Scene Gallery] couldn't take a picture of " + s.name + ": " + e.Message); return false; }
        }

        static readonly Dictionary<string, Texture2D> s_Thumbs = new Dictionary<string, Texture2D>();
        static Texture2D Thumb(string guid)
        {
            if (s_Thumbs.TryGetValue(guid, out var t) && t) return t;
            string f = Path.Combine(Folder, guid + ".png");
            if (!File.Exists(f)) return null;
            t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave }; t.LoadImage(File.ReadAllBytes(f));
            s_Thumbs[guid] = t; return t;
        }

        // ---- the window
        string m_Search = ""; Vector2 m_Scroll; GUIStyle m_Name, m_Small, m_Badge;
        void OnGUI()
        {
            if (m_Name == null)
            {
                m_Name = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                m_Small = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                m_Badge = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            }
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_Search = EditorGUILayout.TextField(m_Search, EditorStyles.toolbarSearchField);
            TakeOnSave = GUILayout.Toggle(TakeOnSave, new GUIContent("Picture on save", "Takes a new picture each time you save a scene"), EditorStyles.toolbarButton, GUILayout.Width(100));
            if (GUILayout.Button(new GUIContent("Update all...", "Opens each scene without a picture for a moment to take one"), EditorStyles.toolbarButton, GUILayout.Width(80))) EditorApplication.delayCall += TakeAll;
            EditorGUILayout.EndHorizontal();

            var inBuild = EditorBuildSettings.scenes.Select(s => s.path).ToList();
            var scenes = AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => m_Search == "" || Path.GetFileNameWithoutExtension(p).IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => inBuild.Contains(p) ? inBuild.IndexOf(p) : 1000).ThenBy(p => p).ToList();
            string open = SceneManager.GetActiveScene().path;
            float cw = 168, ch = 128;
            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 16) / (cw + 6)));
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (i % cols == 0) EditorGUILayout.BeginHorizontal();
                string p = scenes[i];
                var r = GUILayoutUtility.GetRect(cw, ch, GUILayout.Width(cw), GUILayout.Height(ch));
                var img = new Rect(r.x + 2, r.y + 2, r.width - 4, (r.width - 4) * H / W);
                bool hover = r.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(r, p == open ? new Color(0.25f, 0.45f, 0.7f) : hover ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.04f));
                var t = Thumb(AssetDatabase.AssetPathToGUID(p));
                if (t) GUI.DrawTexture(img, t, ScaleMode.ScaleAndCrop);
                else { EditorGUI.DrawRect(img, new Color(0.15f, 0.15f, 0.15f)); GUI.Label(img, "No picture yet\n(save the scene once)", m_Small); }
                int bi = inBuild.IndexOf(p);
                if (bi >= 0) { var b = new Rect(img.x + 3, img.y + 3, 22, 14); EditorGUI.DrawRect(b, new Color(0.35f, 0.7f, 0.45f)); GUI.Label(b, bi.ToString(), m_Badge); }
                GUI.Label(new Rect(r.x + 4, img.yMax + 2, r.width - 8, 16), Path.GetFileNameWithoutExtension(p), m_Name);
                GUI.Label(new Rect(r.x + 4, img.yMax + 16, r.width - 8, 12), Path.GetDirectoryName(p).Replace('\\', '/'), EditorStyles.miniLabel);
                if (GUI.Button(r, new GUIContent("", p + (bi >= 0 ? "\n(scene " + bi + " in the build)" : "")), GUIStyle.none)) { string pp = p; EditorApplication.delayCall += () => OpenScene(pp); }
                if (i % cols == cols - 1 || i == scenes.Count - 1) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); }
            }
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.MouseMove) Repaint();
        }
        void OnEnable() { wantsMouseMove = true; }

        static void OpenScene(string p)
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Scene Gallery", "Stop Play mode first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(p);
        }

        // every scene without a picture: opened beside the current one for a moment, pictured, closed
        static void TakeAll()
        {
            var missing = AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !File.Exists(Path.Combine(Folder, AssetDatabase.AssetPathToGUID(p) + ".png"))).ToList();
            if (missing.Count == 0) { EditorUtility.DisplayDialog("Scene Gallery", "Every scene has a picture.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Scene Gallery", missing.Count + " scene(s) have no picture yet. Open each for a moment to take one?\n(Big scenes take a while to open; nothing in them changes.)", "Take the pictures", "Cancel")) return;
            try
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Scene Gallery", Path.GetFileNameWithoutExtension(missing[i]), i / (float)missing.Count)) break;
                    var already = SceneManager.GetSceneByPath(missing[i]);
                    if (already.isLoaded) { Capture(already); continue; }
                    var s = EditorSceneManager.OpenScene(missing[i], OpenSceneMode.Additive);
                    Capture(s);
                    EditorSceneManager.CloseScene(s, true);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
