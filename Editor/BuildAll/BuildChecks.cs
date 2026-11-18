using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HienaDev.DevTools
{
    /// <summary>
    /// The usual jam mistakes, checked before every Build All (and shown in its window, with Fix buttons): no scenes in the
    /// build, script errors, a development build still ticked, the same version as last time, default names, missing
    /// scripts in the build's scenes, web settings itch.io can't load, a web build over your size budget, no icon.
    /// </summary>
    public static class BuildChecks
    {
        public class Issue { public int level; public string text, fixLabel; public Action fix; public Action edit; } // level: 0 tip, 1 warning, 2 problem

        public static int WebBudgetMb { get => EditorPrefs.GetInt("HienaDev.WebBudgetMb." + Application.dataPath, 100); set => EditorPrefs.SetInt("HienaDev.WebBudgetMb." + Application.dataPath, Mathf.Max(1, value)); }

        public static List<Issue> Run(BuildAll.Prefs p)
        {
            var list = new List<Issue>();
            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled && File.Exists(x.path)).ToList();
            var active = SceneManager.GetActiveScene();

            if (scenes.Count == 0)
                list.Add(new Issue { level = 2, text = "No scenes are in the build list.", fixLabel = active.IsValid() && !string.IsNullOrEmpty(active.path) ? "Add " + active.name : null, fix = () => AddScene(active.path), edit = QuickInput.Scenes });
            else if (active.IsValid() && !string.IsNullOrEmpty(active.path) && !EditorBuildSettings.scenes.Any(x => x.path == active.path))
                list.Add(new Issue { level = 0, text = "The open scene (" + active.name + ") isn't in the build.", fixLabel = "Add it", fix = () => AddScene(active.path) });

            if (EditorUtility.scriptCompilationFailed) list.Add(new Issue { level = 2, text = "Scripts have errors: the build would fail. Fix them first (Console)." });
            int errors = ConsoleErrors();
            if (errors > 0 && !EditorUtility.scriptCompilationFailed) list.Add(new Issue { level = 1, text = errors + " error" + (errors == 1 ? "" : "s") + " in the Console. Worth a look before shipping.", fixLabel = "Open Console", fix = () => EditorApplication.ExecuteMenuItem("Window/General/Console") });

            if (p.dev) list.Add(new Issue { level = 1, text = "Development build is ticked: bigger, slower, shows a \"Development Build\" label. Don't upload it.", fixLabel = "Untick", fix = () => { p.dev = false; BuildAll.SaveSettings(); } });

            var last = BuildAll.LoadReport(BuildAll.LastReportPath);
            if (!p.bump && last != null && last.version == PlayerSettings.bundleVersion)
                list.Add(new Issue { level = 1, text = "Same version as the last build (v" + last.version + "): players and itch.io can't tell them apart.", fixLabel = "+1", fix = () => PlayerSettings.bundleVersion = BuildAll.Bump(PlayerSettings.bundleVersion),
                    edit = () => QuickInput.Text("Version", "The version players see, e.g. 1.0, 1.1, 0.2.3. Each build you upload should have a new one.", PlayerSettings.bundleVersion, v => PlayerSettings.bundleVersion = v) });

            string pn = PlayerSettings.productName;
            if (string.IsNullOrWhiteSpace(pn) || pn == "My project" || pn.StartsWith("New Unity Project")) list.Add(new Issue { level = 1, text = "The game is still called \"" + pn + "\" (window title, .exe and page title).", edit = () => QuickInput.Text("Game name", "The game's name: the window title, the .exe and the web page's title.", "", v => PlayerSettings.productName = v) });
            if (PlayerSettings.companyName == "DefaultCompany") list.Add(new Issue { level = 0, text = "Company is still \"DefaultCompany\" (only changes where desktop saves are kept).", edit = () => QuickInput.Text("Company", "Your name or studio name. Desktop builds keep their saves in a folder with this name (changing it later starts saves fresh).", "", v => PlayerSettings.companyName = v) });

            int missing = MissingScriptsInBuildScenes(scenes.Select(x => x.path));
            if (missing > 0) list.Add(new Issue { level = 1, text = missing + " missing script" + (missing == 1 ? "" : "s") + " in the build's scenes (they do nothing and log warnings).", fixLabel = "Show them", fix = () => EditorApplication.ExecuteMenuItem("Tools/Missing Scripts") });

            if (p.web)
            {
                if (PlayerSettings.WebGL.compressionFormat != WebGLCompressionFormat.Disabled && !PlayerSettings.WebGL.decompressionFallback)
                    list.Add(new Issue { level = 2, text = "Web: compressed without the decompression fallback: itch.io won't load it.", fixLabel = "Turn fallback on", fix = () => PlayerSettings.WebGL.decompressionFallback = true });
                if (PlayerSettings.WebGL.compressionFormat == WebGLCompressionFormat.Disabled)
                    list.Add(new Issue { level = 0, text = "Web: compression is off, so the download is a lot bigger.", fixLabel = "Use Gzip", fix = () => { PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip; PlayerSettings.WebGL.decompressionFallback = true; } });
                var web = last?.platforms.FirstOrDefault(x => x.platform == "Web" && x.ok);
                if (web != null && web.zipBytes > WebBudgetMb * 1024L * 1024L)
                    list.Add(new Issue { level = 1, text = "The last web build was " + (web.zipBytes / 1048576f).ToString("0") + " MB, more than the " + WebBudgetMb + " MB you set. Players wait for it all before the game starts; the build report lists the biggest things in it.",
                        edit = () => QuickInput.Text("Web download limit", "Warn me when the web download is bigger than this many MB. Under ~50 MB starts quickly; over ~200 MB many players leave before it loads.", WebBudgetMb.ToString(), v => { if (int.TryParse(v, out int mb)) WebBudgetMb = mb; }) });
            }

            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (icons == null || icons.Length == 0 || icons.All(t => t == null)) list.Add(new Issue { level = 0, text = "No game icon yet (the .exe and taskbar show Unity's).", edit = QuickInput.Icon });
            return list.OrderByDescending(i => i.level).ToList();
        }

        static void AddScene(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var l = EditorBuildSettings.scenes.ToList(); l.Add(new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = l.ToArray();
        }

        // the Console's error count (Unity keeps it internal)
        static int ConsoleErrors()
        {
            try
            {
                var t = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
                var m = t?.GetMethod("GetCountsByType", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (m == null) return 0;
                var args = new object[] { 0, 0, 0 }; m.Invoke(null, args);
                return (int)args[0];
            }
            catch { return 0; }
        }

        // scripts a scene file points at that no longer exist (read from the file: the scene isn't opened)
        static readonly Dictionary<string, (DateTime at, int n)> s_Missing = new Dictionary<string, (DateTime, int)>();
        static readonly Regex ScriptRef = new Regex(@"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", RegexOptions.Compiled);
        static int MissingScriptsInBuildScenes(IEnumerable<string> paths)
        {
            int total = 0;
            foreach (var p in paths)
            {
                var at = File.GetLastWriteTimeUtc(p);
                if (!s_Missing.TryGetValue(p, out var c) || c.at != at)
                {
                    int n = 0;
                    try { foreach (Match m in ScriptRef.Matches(File.ReadAllText(p))) if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value))) n++; } catch { }
                    c = (at, n); s_Missing[p] = c;
                }
                total += c.n;
            }
            return total;
        }

        /// <summary>Before building: problems stop it, warnings ask.</summary>
        public static bool OkToBuild(BuildAll.Prefs p)
        {
            var issues = Run(p);
            var bad = issues.Where(i => i.level == 2).ToList();
            if (bad.Count > 0) { EditorUtility.DisplayDialog("Build All: fix these first", string.Join("\n\n", bad.Select(i => "• " + i.text)) + "\n\n(The Build All window has Fix buttons.)", "OK"); return false; }
            var warn = issues.Where(i => i.level == 1).ToList();
            if (warn.Count == 0) return true;
            return EditorUtility.DisplayDialog("Build All: before you build", string.Join("\n\n", warn.Select(i => "• " + i.text)), "Build anyway", "Cancel");
        }

        // ---- in the Build All window
        static GUIStyle s_Row; static List<Issue> s_Cached; static double s_At;
        public static void Draw(BuildAll.Prefs p, GUIStyle box, GUIStyle big)
        {
            if (s_Row == null) s_Row = new GUIStyle(EditorStyles.label) { wordWrap = true };
            // (checked about once a second while the window is open, not on every repaint)
            if (s_Cached == null || EditorApplication.timeSinceStartup > s_At + 1.0 || Event.current.type == EventType.MouseUp) { s_Cached = Run(p); s_At = EditorApplication.timeSinceStartup; }
            var issues = s_Cached;
            EditorGUILayout.BeginVertical(box);
            int bad = issues.Count(i => i.level == 2), warn = issues.Count(i => i.level == 1);
            EditorGUILayout.LabelField(bad > 0 ? "Before you build: " + bad + " to fix" : warn > 0 ? "Before you build: " + warn + " to look at" : "Ready to build ✓", big);
            Action later = null;
            foreach (var i in issues)
            {
                EditorGUILayout.BeginHorizontal();
                var c = i.level == 2 ? new Color(0.93f, 0.35f, 0.32f) : i.level == 1 ? new Color(0.97f, 0.7f, 0.25f) : new Color(0.45f, 0.6f, 0.8f);
                var dot = GUILayoutUtility.GetRect(10, 16, GUILayout.Width(10)); EditorGUI.DrawRect(new Rect(dot.x, dot.y + 4, 8, 8), c);
                GUILayout.Label(i.text, s_Row);
                if (i.fix != null && i.fixLabel != null && GUILayout.Button(i.fixLabel, EditorStyles.miniButton, GUILayout.ExpandWidth(false))) { var f = i.fix; later = f; }
                if (i.edit != null && GUILayout.Button(new GUIContent("✎ Fill in", "Opens a small window to type or pick it"), EditorStyles.miniButton, GUILayout.ExpandWidth(false))) { var e = i.edit; later = e; }
                EditorGUILayout.EndHorizontal();
            }
            if (p.web)
            {
                EditorGUILayout.BeginHorizontal();
var tip = "Players on itch.io wait for the whole web download before the game starts. Under ~50 MB starts quickly; over ~200 MB many leave. The check warns when the last web build was bigger than this.";
                GUILayout.Label(new GUIContent("Warn me if the web download is over", tip), GUILayout.ExpandWidth(false));
                WebBudgetMb = EditorGUILayout.IntField(WebBudgetMb, GUILayout.Width(44)); GUILayout.Label(new GUIContent("MB", tip), GUILayout.Width(24));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            if (later != null) EditorApplication.delayCall += () => { later(); s_Cached = null; };
        }

        // ---- the player settings people change most, in one place
        static bool s_ShowQuick;
        public static void DrawQuickSettings(GUIStyle box, GUIStyle big)
        {
            EditorGUILayout.BeginVertical(box);
            s_ShowQuick = EditorGUILayout.Foldout(s_ShowQuick, "Quick settings (icon, window, cursor, web size)", true);
            if (s_ShowQuick)
            {
                EditorGUI.BeginChangeCheck();
                var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
                var icon = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Game icon", "The .exe, taskbar and window icon (a square picture, 256x256 or more)"), icons != null && icons.Length > 0 ? icons[0] : null, typeof(Texture2D), false, GUILayout.Height(54));
                if (EditorGUI.EndChangeCheck()) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, icon ? new[] { icon } : new Texture2D[0], IconKind.Any);

                EditorGUI.BeginChangeCheck();
                var cursor = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Mouse cursor", "Your own cursor picture (empty: the normal one). Its texture type should be Cursor."), PlayerSettings.defaultCursor, typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck()) PlayerSettings.defaultCursor = cursor;

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Windows", EditorStyles.boldLabel);
                var modes = new[] { FullScreenMode.FullScreenWindow, FullScreenMode.Windowed, FullScreenMode.MaximizedWindow, FullScreenMode.ExclusiveFullScreen };
                int mi = Array.IndexOf(modes, PlayerSettings.fullScreenMode); if (mi < 0) mi = 0;
                int nm = GUILayout.Toolbar(mi, new[] { "Full screen", "Window", "Maximized", "Exclusive" });
                if (nm != mi) PlayerSettings.fullScreenMode = modes[nm];
                if (PlayerSettings.fullScreenMode == FullScreenMode.Windowed)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Window size", GUILayout.Width(110));
                    PlayerSettings.defaultScreenWidth = Mathf.Max(320, EditorGUILayout.IntField(PlayerSettings.defaultScreenWidth, GUILayout.Width(55)));
                    GUILayout.Label("×", GUILayout.Width(12));
                    PlayerSettings.defaultScreenHeight = Mathf.Max(240, EditorGUILayout.IntField(PlayerSettings.defaultScreenHeight, GUILayout.Width(55)));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    PlayerSettings.resizableWindow = EditorGUILayout.ToggleLeft("Players can resize the window", PlayerSettings.resizableWindow);
                }
                PlayerSettings.runInBackground = EditorGUILayout.ToggleLeft(new GUIContent("Keep running when the window isn't in front", "Off: the game pauses when players click another window"), PlayerSettings.runInBackground);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Web", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Page size", "The game's size on the web page (set the same as the itch.io embed size)"), GUILayout.Width(110));
                PlayerSettings.defaultWebScreenWidth = Mathf.Max(200, EditorGUILayout.IntField(PlayerSettings.defaultWebScreenWidth, GUILayout.Width(55)));
                GUILayout.Label("×", GUILayout.Width(12));
                PlayerSettings.defaultWebScreenHeight = Mathf.Max(150, EditorGUILayout.IntField(PlayerSettings.defaultWebScreenHeight, GUILayout.Width(55)));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("All player settings...", EditorStyles.miniButton, GUILayout.Width(140))) SettingsService.OpenProjectSettings("Project/Player");
            }
            EditorGUILayout.EndVertical();
        }
    }
}
