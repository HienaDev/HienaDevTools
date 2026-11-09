using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace HienaDev.DevTools
{
    /// <summary>
    /// Buttons in Unity's top toolbar, next to Play: Play from the first scene (the game starts the way players start it,
    /// and you come back to your scene after), game speed, restart the scene, a frame-rate cap, a screenshot, clear saves,
    /// a scene list, the jam timer.
    /// Right-click the toolbar to show or hide each. (Before Unity 6.3: Tools > Play Bar, a small window instead.)
    /// </summary>
    [InitializeOnLoad]
    public static class PlayTools
    {
        public const string FromStartPath = "HienaDev/Play From First Scene", SpeedPath = "HienaDev/Game Speed", RestartPath = "HienaDev/Restart Scene", FpsPath = "HienaDev/Frame Rate Cap", ShotPath = "HienaDev/Screenshot", SavesPath = "HienaDev/Clear Saves", ScenesPath = "HienaDev/Scenes";
        static readonly string[] AllPaths = { FromStartPath, SpeedPath, RestartPath, FpsPath, ShotPath, SavesPath, ScenesPath, JamTimer.Path, SelectionHistory.BackPath, SelectionHistory.ForwardPath, SelectionHistory.RecentPath, Favourites.ToolbarPath };

        static PlayTools()
        {
            ApplyStartScene();
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && !Mathf.Approximately(Speed, 1f)) Time.timeScale = Speed;
                if (s == PlayModeStateChange.EnteredPlayMode) ApplyFps();
                if (s == PlayModeStateChange.ExitingPlayMode) UndoFps();
                Refresh();
            };
            EditorBuildSettings.sceneListChanged += ApplyStartScene;
#if UNITY_6000_3_OR_NEWER
            // (a couple of seconds after loading, when the toolbar exists; tried again for a while if it isn't built yet)
            double at = EditorApplication.timeSinceStartup + 2; int tries = 0;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.timeSinceStartup < at) return;
                at = EditorApplication.timeSinceStartup + 2;
                if (ShowOnce() || ++tries > 30) EditorApplication.update -= tick;
            };
            EditorApplication.update += tick;
#endif
        }

#if UNITY_6000_3_OR_NEWER
        // Unity adds new toolbar buttons hidden: the first time in each project, show ours (after that, your right-click
        // choices on the toolbar are kept)
        static bool ShowOnce()
        {
            string key = "HienaDev.ToolbarShownItems." + Application.dataPath;
            var shown = new HashSet<string>(EditorPrefs.GetString(key, "").Split('|').Where(s => s.Length > 0));
            var todo = AllPaths.Where(p => !shown.Contains(p)).ToList();
            if (todo.Count == 0) return true;
            try
            {
                var tryGet = typeof(MainToolbar).GetMethod("TryGetOverlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (tryGet == null) return true;
                bool missing = false;
                foreach (var p in todo)
                {
                    var args = new object[] { p, null };
                    if ((bool)tryGet.Invoke(null, args) && args[1] is UnityEditor.Overlays.Overlay ov) { ov.displayed = true; shown.Add(p); }
                    else missing = true;
                }
                EditorPrefs.SetString(key, string.Join("|", shown));
                return !missing; // (the toolbar may not be built yet right after Unity starts: tried again)
            }
            catch (Exception e) { Debug.LogWarning("[Dev Tools] couldn't show the toolbar buttons (right-click the toolbar to show them): " + e.Message); return true; }
        }
#endif

        public static void Refresh()
        {
#if UNITY_6000_3_OR_NEWER
            foreach (var p in new[] { FromStartPath, SpeedPath, RestartPath, FpsPath, ShotPath, SavesPath, ScenesPath }) MainToolbar.Refresh(p);
#endif
        }

        // ---- play from the first scene
        public static bool FromStart { get => EditorPrefs.GetBool("HienaDev.PlayFromStart." + Application.dataPath, false); set { EditorPrefs.SetBool("HienaDev.PlayFromStart." + Application.dataPath, value); ApplyStartScene(); Refresh(); } }
        public static string FirstScene => EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && File.Exists(s.path))?.path;
        static void ApplyStartScene()
        {
            string first = FirstScene;
            EditorSceneManager.playModeStartScene = FromStart && first != null ? AssetDatabase.LoadAssetAtPath<SceneAsset>(first) : null;
        }

        // ---- game speed (kept while you test; put back to 1 with the button)
        public static float Speed
        {
            get => EditorPrefs.GetFloat("HienaDev.GameSpeed", 1f);
            set { SetSpeed(value, true); }
        }
        public static void SetSpeed(float value, bool refresh)
        {
            value = Mathf.Clamp(value, 0.05f, 4f);
            // (snaps to 1x near the middle, so normal speed is easy to get back to)
            if (Mathf.Abs(value - 1f) < 0.06f) value = 1f;
            EditorPrefs.SetFloat("HienaDev.GameSpeed", value);
            if (EditorApplication.isPlaying) Time.timeScale = value;
            if (refresh) Refresh();
            else
            {
                // the slider's label catches up once the slider has been still for a moment
                s_RefreshAt = EditorApplication.timeSinceStartup + 0.6;
                if (!s_Waiting) { s_Waiting = true; EditorApplication.update += LateRefresh; }
            }
        }
        static double s_RefreshAt; static bool s_Waiting;
        static void LateRefresh()
        {
            if (EditorApplication.timeSinceStartup < s_RefreshAt) return;
            EditorApplication.update -= LateRefresh; s_Waiting = false; Refresh();
        }
        public static string SpeedText(float v) => v >= 0.995f ? v.ToString("0.#") + "x" : v.ToString("0.##") + "x";

        // ---- restart: the open scene again, without leaving Play mode
        public static void Restart()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.isPlaying = true; return; }
            var s = SceneManager.GetActiveScene();
            EditorSceneManager.LoadSceneInPlayMode(s.path, new LoadSceneParameters(LoadSceneMode.Single));
            if (!Mathf.Approximately(Speed, 1f)) Time.timeScale = Speed;
        }

        // ---- frame-rate cap while playing (feel the game on a slow computer): 0 = no cap
        public static int Fps { get => EditorPrefs.GetInt("HienaDev.FpsCap", 0); set { EditorPrefs.SetInt("HienaDev.FpsCap", value); if (EditorApplication.isPlaying) ApplyFps(); Refresh(); } }
        static int s_VSync = -1;
        static void ApplyFps()
        {
            if (Fps <= 0) { UndoFps(); return; }
            if (s_VSync < 0) s_VSync = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0; // (vsync would ignore the cap)
            Application.targetFrameRate = Fps;
        }
        static void UndoFps()
        {
            Application.targetFrameRate = -1;
            if (s_VSync >= 0) { QualitySettings.vSyncCount = s_VSync; s_VSync = -1; }
        }
        public static void FpsMenu(Rect r)
        {
            var m = new GenericMenu();
            foreach (var f in new[] { 0, 15, 30, 60 }) { int v = f; m.AddItem(new GUIContent(v == 0 ? "No cap" : v + " fps"), Fps == v, () => Fps = v); }
            m.DropDown(r);
        }

        // ---- clear saves: PlayerPrefs and the save folder, with a backup first if you like
        public static void ClearSaves()
        {
            string dir = Application.persistentDataPath;
            int files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0;
            int c = EditorUtility.DisplayDialogComplex("Clear saves",
                "Delete everything " + PlayerSettings.productName + " has saved on this computer, so the game starts like a first-time player?\n\n" +
                "- PlayerPrefs (settings, unlocks, scores...)\n- " + files + " file(s) in " + dir + "\n\nThis can't be undone (unless you keep a backup).",
                "Back up, then clear", "Cancel", "Clear without a backup");
            if (c == 1) return;
            string backup = null;
            if (c == 0) backup = BackupSaves(dir);
            PlayerPrefs.DeleteAll(); PlayerPrefs.Save();
            if (Directory.Exists(dir)) foreach (var e in Directory.GetFileSystemEntries(dir)) { try { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); } catch (Exception ex) { Debug.LogWarning("[Dev Tools] couldn't delete " + e + ": " + ex.Message); } }
            Debug.Log("[Dev Tools] saves cleared" + (backup != null ? " (backup: " + backup + ")" : ""));
            if (backup != null) EditorUtility.RevealInFinder(backup);
        }
        static string BackupSaves(string dir)
        {
            string root = Path.Combine(Path.GetFullPath("Build"), "Save Backups", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            Directory.CreateDirectory(root);
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var to = Path.Combine(root, "files", f.Substring(dir.Length).TrimStart('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(f, to, true);
                }
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                // the editor's PlayerPrefs live in the registry: export them (double-click the .reg to put them back)
                string key = "HKCU\\Software\\Unity\\UnityEditor\\" + PlayerSettings.companyName + "\\" + PlayerSettings.productName;
                try
                {
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("reg.exe", "export \"" + key + "\" \"" + Path.Combine(root, "PlayerPrefs.reg") + "\" /y") { CreateNoWindow = true, UseShellExecute = false });
                    p?.WaitForExit(10000);
                }
                catch (Exception e) { Debug.LogWarning("[Dev Tools] couldn't back up PlayerPrefs: " + e.Message); }
            }
            return root;
        }

        // ---- the scene list: the build's scenes first, then every other scene in Assets
        public static void ScenesMenu(Rect r)
        {
            var menu = new GenericMenu();
            var inBuild = EditorBuildSettings.scenes.Where(s => File.Exists(s.path)).Select(s => s.path).ToList();
            var all = AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            string open = SceneManager.GetActiveScene().path;
            foreach (var p in inBuild) menu.AddItem(new GUIContent("In the build/" + Path.GetFileNameWithoutExtension(p)), p == open, () => OpenScene(p));
            var others = all.Where(p => !inBuild.Contains(p)).ToList();
            if (inBuild.Count > 0 && others.Count > 0) menu.AddSeparator("");
            foreach (var p in others)
            {
                // grouped by folder, like the Project window
                string dir = Path.GetDirectoryName(p).Replace('\\', '/');
                string label = dir.StartsWith("Assets") ? dir.Substring(6).TrimStart('/') : dir;
                menu.AddItem(new GUIContent((label.Length > 0 ? label.Replace("/", " › ") + "/" : "") + Path.GetFileNameWithoutExtension(p)), p == open, () => OpenScene(p));
            }
            if (all.Count == 0) menu.AddDisabledItem(new GUIContent("No scenes in Assets"));
            menu.DropDown(r);
        }

        static void OpenScene(string path)
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Scenes", "Stop Play mode first.", "OK"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }

#if UNITY_6000_3_OR_NEWER
        static Texture2D Icon(string name) => EditorGUIUtility.IconContent(name).image as Texture2D;

        [MainToolbarElement(FromStartPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement FromStartToggle()
        {
            string first = FirstScene;
            var c = new MainToolbarContent(FromStart ? "From " + (first != null ? Path.GetFileNameWithoutExtension(first) : "scene 1") : "From here", Icon("d_SceneAsset Icon"),
                first == null ? "No scenes in the build list" : FromStart ? "Play starts from the first scene in the build (" + Path.GetFileNameWithoutExtension(first) + "), like the real game; you come back to your scene after. Click to play the open scene instead."
                                                                          : "Play starts in the open scene. Click to always start from the first scene in the build, like the real game.");
            return new MainToolbarToggle(c, FromStart, v => FromStart = v);
        }

        [MainToolbarElement(SpeedPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement SpeedSlider() =>
            new MainToolbarSlider(new MainToolbarContent(SpeedText(Speed), "Game speed: slow motion to look at effects, faster to skip boring parts. (Time.timeScale while playing)"), Speed, 0.05f, 3f, v => SetSpeed(v, false));

        [MainToolbarElement(RestartPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement RestartButton() =>
            new MainToolbarButton(new MainToolbarContent(Icon("d_Refresh"), "Restart the scene (without leaving Play mode)"), () => { Restart(); });

        [MainToolbarElement(FpsPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement FpsDropdown() =>
            new MainToolbarDropdown(new MainToolbarContent(Fps > 0 ? Fps + " fps" : "fps", "Frame-rate cap while playing (15 / 30 / 60), to feel the game on a slower computer"), FpsMenu);

        [MainToolbarElement(ShotPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement ShotButton() =>
            new MainToolbarButton(new MainToolbarContent(Icon("d_SceneViewCamera"), "Screenshot of the Game view (Ctrl+Shift+K), into the Screenshots folder"), () => GameViewScreenshot.TakeScreenshot());

        [MainToolbarElement(SavesPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement SavesButton() =>
            new MainToolbarButton(new MainToolbarContent(Icon("d_TreeEditor.Trash"), "Clear saves: PlayerPrefs and saved files, to test as a first-time player (asks first, can back up)"), ClearSaves);

        [MainToolbarElement(ScenesPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        static MainToolbarElement ScenesDropdown()
        {
            string open = SceneManager.GetActiveScene().name;
            return new MainToolbarDropdown(new MainToolbarContent(string.IsNullOrEmpty(open) ? "Scenes" : open, Icon("d_SceneAsset Icon"), "Open another scene"), ScenesMenu);
        }

        [InitializeOnLoadMethod]
        static void WatchScene() => EditorSceneManager.activeSceneChangedInEditMode += (a, b) => MainToolbar.Refresh(ScenesPath);
#else
        [MenuItem("Tools/Play Bar", false, 130)]
        static void OpenBar() { var w = EditorWindow.GetWindow<PlayBarWindow>(false, "Play Bar", true); w.minSize = new Vector2(300, 22); }
#endif
    }

#if !UNITY_6000_3_OR_NEWER
    /// <summary>Before Unity 6.3 (no toolbar API): the same buttons in a thin window.</summary>
    public class PlayBarWindow : EditorWindow
    {
        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            PlayTools.FromStart = GUILayout.Toggle(PlayTools.FromStart, new GUIContent("From scene 1", "Play starts from the first scene in the build"), EditorStyles.toolbarButton);
            GUILayout.Label(PlayTools.SpeedText(PlayTools.Speed), GUILayout.Width(36));
            float v = GUILayout.HorizontalSlider(PlayTools.Speed, 0.05f, 3f, GUILayout.Width(80)); if (!Mathf.Approximately(v, PlayTools.Speed)) PlayTools.Speed = v;
            if (GUILayout.Button("1x", EditorStyles.toolbarButton)) PlayTools.Speed = 1f;
            if (GUILayout.Button("Restart", EditorStyles.toolbarButton)) PlayTools.Restart();
            var fr = GUILayoutUtility.GetRect(new GUIContent("60 fps"), EditorStyles.toolbarDropDown);
            if (GUI.Button(fr, PlayTools.Fps > 0 ? PlayTools.Fps + " fps" : "fps", EditorStyles.toolbarDropDown)) PlayTools.FpsMenu(fr);
            if (GUILayout.Button("Shot", EditorStyles.toolbarButton)) GameViewScreenshot.TakeScreenshot();
            if (GUILayout.Button("Clear saves", EditorStyles.toolbarButton)) PlayTools.ClearSaves();
            var r = GUILayoutUtility.GetRect(new GUIContent("Scenes"), EditorStyles.toolbarDropDown);
            if (GUI.Button(r, "Scenes", EditorStyles.toolbarDropDown)) PlayTools.ScenesMenu(r);
            var jr = GUILayoutUtility.GetRect(new GUIContent(JamTimer.Label), EditorStyles.toolbarDropDown);
            if (GUI.Button(jr, JamTimer.Label, EditorStyles.toolbarDropDown)) JamTimer.Menu(jr);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        void OnInspectorUpdate() => Repaint();
    }
#endif
}
