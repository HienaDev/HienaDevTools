using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Warns when something much bigger than it needs to be comes into the project, and fixes it in one click: huge
    /// textures (4K and up), uncompressed textures, long sounds unpacked whole into memory, uncompressed (PCM) sounds,
    /// models kept readable for no reason, very big videos. Tools > Asset Size Watcher lists them (and can check the
    /// whole project); new imports show a note in the Console.
    /// </summary>
    public class AssetSizeWatcher : EditorWindow
    {
        public class Issue { public string path, text; public long bytes; public Action fix; public string fixLabel; }

        public static bool Watching { get => EditorPrefs.GetBool("HienaDev.SizeWatcher", true); set => EditorPrefs.SetBool("HienaDev.SizeWatcher", value); }
        public static int BigTexture { get => EditorPrefs.GetInt("HienaDev.SizeWatcher.Tex", 4096); set => EditorPrefs.SetInt("HienaDev.SizeWatcher.Tex", value); }

        [MenuItem("Tools/Asset Size Watcher", false, 156)]
        public static void Open() { var w = GetWindow<AssetSizeWatcher>(false, "Asset Sizes", true); w.minSize = new Vector2(360, 280); }

        // ---- watching imports
        class Watch : AssetPostprocessor
        {
            static readonly List<string> s_Queue = new List<string>();
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (!Watching || imported.Length == 0 || imported.Length > 300 || EditorApplication.timeSinceStartup < 60) return; // (not the big import when a project opens)
                lock (s_Queue) { s_Queue.AddRange(imported.Where(p => p.StartsWith("Assets/"))); }
                EditorApplication.delayCall -= Flush; EditorApplication.delayCall += Flush;
            }
            static void Flush()
            {
                List<string> paths; lock (s_Queue) { paths = s_Queue.Distinct().ToList(); s_Queue.Clear(); }
                var found = paths.Select(Check).Where(i => i != null).ToList();
                if (found.Count == 0) return;
                Recent.RemoveAll(r => found.Any(f => f.path == r.path));
                Recent.InsertRange(0, found);
                Debug.LogWarning("[Asset Size Watcher] " + found.Count + " new asset(s) bigger than they need to be (Tools > Asset Size Watcher to fix in one click):\n" + string.Join("\n", found.Select(f => "• " + f.path + ": " + f.text)));
                foreach (var w in Resources.FindObjectsOfTypeAll<AssetSizeWatcher>()) w.Repaint();
            }
        }
        static readonly List<Issue> Recent = new List<Issue>();

        // ---- one asset: what's too big about it (null: fine)
        public static Issue Check(string path)
        {
            try
            {
                var imp = AssetImporter.GetAtPath(path);
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (imp is TextureImporter ti)
                {
                    ti.GetSourceTextureWidthAndHeight(out int w, out int h);
                    int size = Mathf.Min(Mathf.Max(w, h), ti.maxTextureSize);
                    if (size >= BigTexture)
                        return new Issue { path = path, bytes = bytes, text = "a " + size + " px texture: about " + Mb(Mem(size, size, ti)) + " in memory. 2048 is plenty for most things (1024 for small props, UI icons).", fixLabel = "Make it 2048", fix = () => { ti.maxTextureSize = 2048; ti.SaveAndReimport(); } };
                    if (ti.textureCompression == TextureImporterCompression.Uncompressed && size >= 1024 && ti.textureType != TextureImporterType.Cursor)
                        return new Issue { path = path, bytes = bytes, text = "not compressed: about " + Mb(Mem(size, size, ti)) + " in memory instead of " + Mb(Mem(size, size, ti) / 4) + ".", fixLabel = "Compress it", fix = () => { ti.textureCompression = TextureImporterCompression.Compressed; ti.SaveAndReimport(); } };
                    return null;
                }
                if (imp is AudioImporter ai)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    float len = clip ? clip.length : 0f;
                    var st = ai.defaultSampleSettings;
                    if (len > 20f && st.loadType == AudioClipLoadType.DecompressOnLoad)
                        return new Issue { path = path, bytes = bytes, text = "a " + len.ToString("0") + " s sound unpacked whole into memory (about " + Mb((long)(len * (clip ? clip.frequency * clip.channels : 88200) * 2)) + "). Music plays fine streamed.", fixLabel = "Stream it", fix = () => { var s = ai.defaultSampleSettings; s.loadType = AudioClipLoadType.Streaming; if (s.compressionFormat == AudioCompressionFormat.PCM) s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f; ai.defaultSampleSettings = s; ai.SaveAndReimport(); } };
                    if (st.compressionFormat == AudioCompressionFormat.PCM && bytes > 1024 * 1024)
                        return new Issue { path = path, bytes = bytes, text = "an uncompressed sound (" + Mb(bytes) + "). Compressed, it's about a tenth of that and sounds the same.", fixLabel = "Compress it", fix = () => { var s = ai.defaultSampleSettings; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f; ai.defaultSampleSettings = s; ai.SaveAndReimport(); } };
                    return null;
                }
                if (imp is ModelImporter mi)
                {
                    if (mi.isReadable && bytes > 5 * 1024 * 1024)
                        return new Issue { path = path, bytes = bytes, text = "a big model kept \"readable\": its meshes are stored twice in memory. Only needed if scripts read or change the mesh.", fixLabel = "Turn it off", fix = () => { mi.isReadable = false; mi.SaveAndReimport(); } };
                    return null;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((ext == ".mp4" || ext == ".mov" || ext == ".webm" || ext == ".avi") && bytes > 50L * 1024 * 1024)
                    return new Issue { path = path, bytes = bytes, text = "a " + Mb(bytes) + " video: it all goes into the download. Shorter or lower-quality (re-encode it outside Unity) if you can." };
            }
            catch { }
            return null;
        }

        // memory a texture takes: 4 bytes a pixel uncompressed, about 1 compressed; mipmaps add a third
        static long Mem(int w, int h, TextureImporter ti) => (long)(w * (long)h * (ti.textureCompression == TextureImporterCompression.Uncompressed ? 4f : 1f) * (ti.mipmapEnabled ? 1.33f : 1f));
        static string Mb(long b) => b >= 1024 * 1024 ? (b / 1048576f).ToString("0.#") + " MB" : (b / 1024f).ToString("0") + " KB";

        // ---- the window
        List<Issue> m_Scan; Vector2 m_Scroll; GUIStyle m_Row;
        void OnGUI()
        {
            if (m_Row == null) m_Row = new GUIStyle(EditorStyles.label) { wordWrap = true };
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Watching = GUILayout.Toggle(Watching, new GUIContent("Watch new imports", "Checks each asset you add or change"), EditorStyles.toolbarButton, GUILayout.Width(120));
            GUILayout.Label("Warn from", GUILayout.Width(62));
            BigTexture = EditorGUILayout.IntPopup(BigTexture, new[] { "2048 px", "4096 px", "8192 px" }, new[] { 2048, 4096, 8192 }, EditorStyles.toolbarPopup, GUILayout.Width(70));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Check the whole project", EditorStyles.toolbarButton)) EditorApplication.delayCall += ScanAll;
            EditorGUILayout.EndHorizontal();

            var list = m_Scan ?? Recent;
            list.RemoveAll(i => !File.Exists(i.path));
            if (list.Count == 0)
            {
                var r = EditorGUILayout.GetControlRect(false, 46);
                EditorGUI.DrawRect(r, new Color(0.3f, 0.55f, 0.35f));
                GUI.Label(new Rect(r.x + 10, r.y + 4, r.width - 20, 20), m_Scan != null ? "Nothing too big" : "Nothing new to look at", EditorStyles.boldLabel);
                GUI.Label(new Rect(r.x + 10, r.y + 24, r.width - 20, 18), m_Scan != null ? "Textures, sounds, models and videos all look sensible." : "New imports are checked as they come in. Or check the whole project.", EditorStyles.miniLabel);
                return;
            }
            int fixable = list.Count(i => i.fix != null);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField((m_Scan != null ? "In the whole project: " : "Recently imported: ") + list.Count + " to look at", EditorStyles.boldLabel);
            GUI.enabled = fixable > 0;
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
            if (GUILayout.Button("Fix all " + fixable, GUILayout.Width(90))) { var fixes = list.Where(i => i.fix != null).ToList(); EditorApplication.delayCall += () => FixAll(fixes, list); }
            GUI.backgroundColor = ob; GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            Issue done = null;
            foreach (var i in list)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(AssetDatabase.GetCachedIcon(i.path), GUILayout.Width(32), GUILayout.Height(32));
                EditorGUILayout.BeginVertical();
                if (GUILayout.Button(Path.GetFileName(i.path), EditorStyles.linkLabel)) { var o = AssetDatabase.LoadMainAssetAtPath(i.path); Selection.activeObject = o; EditorGUIUtility.PingObject(o); }
                GUILayout.Label("It's " + i.text, m_Row);
                EditorGUILayout.EndVertical();
                if (i.fix != null && GUILayout.Button(i.fixLabel, GUILayout.Width(100), GUILayout.Height(32))) done = i;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (done != null) { var d = done; EditorApplication.delayCall += () => { d.fix(); list.Remove(d); Repaint(); }; }
        }

        void FixAll(List<Issue> fixes, List<Issue> list)
        {
            AssetDatabase.StartAssetEditing();
            try { foreach (var i in fixes) { i.fix(); list.Remove(i); } }
            finally { AssetDatabase.StopAssetEditing(); }
            Repaint();
        }

        void ScanAll()
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D t:AudioClip t:Model t:VideoClip", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
            var found = new List<Issue>();
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Asset Size Watcher", "Checking " + paths.Count + " assets...", i / (float)paths.Count)) break;
                    var issue = Check(paths[i]); if (issue != null) found.Add(issue);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            m_Scan = found.OrderByDescending(i => i.bytes).ToList();
            Repaint();
        }
    }
}
