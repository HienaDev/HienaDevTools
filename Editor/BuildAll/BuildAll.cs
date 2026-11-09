using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Build All: the game's name, company and version (taken from Player Settings, editable here), which platforms,
    /// the main build options, then one Build button: a Windows build and a Web build, each zipped as
    /// "[Product] v[version] [Platform].zip" in Builds/, opened when done, plus a build report (sizes, biggest assets, files,
    /// warnings, times) shown here and saved next to the zips.
    /// </summary>
    public class BuildAllWindow : EditorWindow
    {
        [MenuItem("Tools/Build All (Windows + Web)", false, 100)]
        static void Open() { var w = GetWindow<BuildAllWindow>(false, "Build All", true); w.minSize = new Vector2(420, 420); }

        Vector2 m_Scroll;
        BuildAll.Report m_Report; string m_ReportPath; bool m_ShowAssets = true, m_ShowFiles, m_ShowWarnings, m_ShowScenes;
        GUIStyle m_Big, m_Box, m_Good, m_Bad;

        void Styles()
        {
            if (m_Big != null) return;
            m_Big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            m_Box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
            m_Good = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.35f, 0.8f, 0.4f) } };
            m_Bad = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.95f, 0.4f, 0.35f) } };
        }

        void OnGUI()
        {
            Styles();
            var s = BuildAll.Settings;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            // ---- the game
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("The game", m_Big);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.DelayedTextField(new GUIContent("Name", "Player Settings > Product Name: the window title, the .exe name, the web page title and the zip names.\nNote: a desktop build keeps its saves (PlayerPrefs) under this name, so renaming starts desktop saves fresh."), PlayerSettings.productName);
            string company = EditorGUILayout.DelayedTextField(new GUIContent("Company", "Player Settings > Company Name (desktop saves are kept under it too)"), PlayerSettings.companyName);
            EditorGUILayout.BeginHorizontal();
            string ver = EditorGUILayout.DelayedTextField(new GUIContent("Version", "Player Settings > Version"), PlayerSettings.bundleVersion);
            if (GUILayout.Button(new GUIContent("+1", "Add 1 to the last number now"), GUILayout.Width(32))) ver = BuildAll.Bump(ver);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrWhiteSpace(name)) PlayerSettings.productName = name.Trim();
                PlayerSettings.companyName = company.Trim();
                if (!string.IsNullOrWhiteSpace(ver)) PlayerSettings.bundleVersion = ver.Trim();
                AssetDatabase.SaveAssets();
            }
            s.bump = EditorGUILayout.ToggleLeft(new GUIContent("Bump the version on every build (" + PlayerSettings.bundleVersion + " → " + BuildAll.Bump(PlayerSettings.bundleVersion) + ")"), s.bump);
            int notes = Changelog.Next.entries.Count;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(notes == 0 ? "Changelog: no notes for the next version yet" : "Changelog: " + notes + " note" + (notes == 1 ? "" : "s") + " for the next version" + (s.bump ? " (saved as v" + BuildAll.Bump(PlayerSettings.bundleVersion) + " when you build)" : ""), EditorStyles.miniLabel);
            if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(44))) ChangelogWindow.Open();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Unity " + Application.unityVersion + "   ·   editor is on " + EditorUserBuildSettings.activeBuildTarget, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            // ---- what to build
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("What to build", m_Big);
            s.windows = EditorGUILayout.ToggleLeft("Windows (.exe)", s.windows);
            if (s.windows)
            {
                EditorGUI.indentLevel++;
                var backend = PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone);
                var nb = (ScriptingImplementation)EditorGUILayout.EnumPopup(new GUIContent("Code backend", "Mono: quick to build.  IL2CPP: slower to build, a bit faster to run, harder to decompile (needs the Windows IL2CPP module)."), backend);
                if (nb != backend) PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, nb);
                EditorGUI.indentLevel--;
            }
            s.web = EditorGUILayout.ToggleLeft("Web (itch.io and other sites)", s.web);
            if (s.web)
            {
                EditorGUI.indentLevel++;
                var comp = PlayerSettings.WebGL.compressionFormat;
                var nc = (WebGLCompressionFormat)EditorGUILayout.EnumPopup(new GUIContent("Compression", "Gzip + fallback works on itch.io with no setup. Brotli is smaller but some hosts can't serve it."), comp);
                if (nc != comp) PlayerSettings.WebGL.compressionFormat = nc;
                bool fb = EditorGUILayout.Toggle(new GUIContent("Decompression fallback", "On: works on hosts that don't send the right headers (itch.io). Keep it on."), PlayerSettings.WebGL.decompressionFallback);
                if (fb != PlayerSettings.WebGL.decompressionFallback) PlayerSettings.WebGL.decompressionFallback = fb;
                EditorGUILayout.LabelField("Page template", PlayerSettings.WebGL.template, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            s.dev = EditorGUILayout.ToggleLeft(new GUIContent("Development build (for profiling a build)", "Adds the profiler connection and error messages on screen. Bigger and slower: don't upload it."), s.dev);
            s.run = EditorGUILayout.ToggleLeft(new GUIContent("Open each build when it's done", "Starts the .exe; opens the web build in your browser from a small local server"), s.run);

            var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).ToArray();
            m_ShowScenes = EditorGUILayout.Foldout(m_ShowScenes, "Scenes in the build: " + scenes.Length + (scenes.Length > 0 ? " (starts in " + Path.GetFileNameWithoutExtension(scenes[0].path) + ")" : ""), true);
            if (m_ShowScenes) { foreach (var sc in scenes) EditorGUILayout.LabelField("   " + sc.path, EditorStyles.miniLabel); if (GUILayout.Button("Edit the scene list", GUILayout.Width(140))) EditorApplication.ExecuteMenuItem("File/Build Profiles"); }
            if (scenes.Length == 0) EditorGUILayout.HelpBox("No scenes are ticked in the build list.", MessageType.Error);
            EditorGUILayout.EndVertical();

            // ---- build
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.BeginHorizontal();
            string of = EditorGUILayout.DelayedTextField(new GUIContent("Output folder", "Where the builds, zips and reports go. A relative path starts at the project folder. \"Build\" is the one .gitignore skips."), s.outFolder);
            if (of != s.outFolder) { s.outFolder = string.IsNullOrWhiteSpace(of) ? "Build" : of.Trim(); BuildAll.SaveSettings(); }
            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string picked = EditorUtility.OpenFolderPanel("Where should the builds go?", BuildAll.OutDir, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string proj = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
                    picked = picked.Replace('\\', '/');
                    s.outFolder = picked.StartsWith(proj, StringComparison.OrdinalIgnoreCase) ? picked.Substring(proj.Length) : picked;
                    if (s.outFolder == "") s.outFolder = "Build";
                    BuildAll.SaveSettings(); GUI.FocusControl(null);
                }
            }
            if (GUILayout.Button("Reset", GUILayout.Width(50))) { s.outFolder = "Build"; BuildAll.SaveSettings(); GUI.FocusControl(null); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(BuildAll.OutDir, EditorStyles.wordWrappedMiniLabel);
            if (BuildAll.Running)
            {
                EditorGUILayout.HelpBox("Building: " + BuildAll.StatusText + "\nUnity reloads between the platforms; it carries on by itself.", MessageType.Info);
                if (GUILayout.Button("Stop after this step")) BuildAll.Cancel();
            }
            else
            {
                GUI.enabled = (s.windows || s.web) && scenes.Length > 0 && !EditorApplication.isPlayingOrWillChangePlaymode;
                if (GUILayout.Button(EditorApplication.isPlayingOrWillChangePlaymode ? "Stop Play mode to build" : "Build " + (s.windows && s.web ? "Windows + Web" : s.windows ? "Windows" : "Web"), GUILayout.Height(36))) { BuildAll.SaveSettings(); BuildAll.Start(); }
                GUI.enabled = true;
                if (GUILayout.Button("Open the output folder")) { Directory.CreateDirectory(BuildAll.OutDir); EditorUtility.RevealInFinder(BuildAll.OutDir + "/"); }
                if (WebServer.Running)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent("Web build being served at " + WebServer.Url, "A small hidden local server so the browser can run the web build. It stops by itself when Unity closes."), EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(WebServer.Url) && GUILayout.Button("Open", GUILayout.Width(50))) Application.OpenURL(WebServer.Url);
                    if (GUILayout.Button("Stop", GUILayout.Width(50))) WebServer.Stop();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();

            // ---- itch.io
            DrawItch(s);

            // ---- the last build's report
            DrawReport();

            EditorGUILayout.EndScrollView();
            if (GUI.changed) BuildAll.SaveSettings();
        }

        bool m_ShowChannels;
        void DrawItch(BuildAll.Prefs s)
        {
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("itch.io", m_Big);
            string butler = Itch.Butler;
            if (butler == null)
            {
                EditorGUILayout.HelpBox("butler (itch.io's upload tool) wasn't found. Install the itch.io app (it brings butler), or get butler from itch.io/docs/butler and put it on the PATH.", MessageType.Warning);
                EditorGUILayout.EndVertical(); return;
            }
            string page = EditorGUILayout.DelayedTextField(new GUIContent("Game page", "Your itch.io user and game, like hienadev/fish-creator (from https://hienadev.itch.io/fish-creator)"), s.itchPage);
            if (page != s.itchPage) { s.itchPage = Itch.CleanPage(page); BuildAll.SaveSettings(); }
            if (!string.IsNullOrEmpty(s.itchPage) && !Itch.ValidPage(s.itchPage)) EditorGUILayout.HelpBox("Write it as user/game, like hienadev/fish-creator.", MessageType.Error);
            m_ShowChannels = EditorGUILayout.Foldout(m_ShowChannels, "Upload names (channels): " + s.chanWindows + ", " + s.chanWeb, true);
            if (m_ShowChannels)
            {
                EditorGUI.indentLevel++;
                s.chanWindows = EditorGUILayout.TextField(new GUIContent("Windows", "Each channel is one upload on the page; the same name updates it"), s.chanWindows);
                s.chanWeb = EditorGUILayout.TextField(new GUIContent("Web", "Each channel is one upload on the page; the same name updates it"), s.chanWeb);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.BeginHorizontal();
            if (Itch.LoggedIn) EditorGUILayout.LabelField("butler is logged in", EditorStyles.miniLabel);
            else { EditorGUILayout.LabelField("butler isn't logged in yet", EditorStyles.boldLabel); if (GUILayout.Button("Log in (opens your browser)", GUILayout.Width(190))) Itch.Login(); }
            EditorGUILayout.EndHorizontal();
            bool ready = Itch.LoggedIn && Itch.ValidPage(s.itchPage);
            GUI.enabled = ready;
            s.publish = EditorGUILayout.ToggleLeft(new GUIContent("Publish to itch.io after building", "Uploads each build to its channel (only changed files), tagged with the version. Never a development build."), s.publish && ready);
            GUI.enabled = true;
            if (s.publish && s.dev) EditorGUILayout.HelpBox("Development builds are never published: untick Development build.", MessageType.Warning);
            if (Itch.Busy) EditorGUILayout.HelpBox("Publishing: " + Itch.Status, MessageType.Info);
            else
            {
                var last = BuildAll.LoadReport(BuildAll.LastReportPath);
                bool can = ready && last != null && last.platforms.Any(p => p.ok && !p.dev && !string.IsNullOrEmpty(p.folder) && Directory.Exists(p.folder));
                GUI.enabled = can && !BuildAll.Running;
                if (GUILayout.Button(last == null ? "Publish the last build" : "Publish the last build (v" + last.version + ") now")
                    && EditorUtility.DisplayDialog("Publish to itch.io", "Upload " + last.product + " v" + last.version + " (" + string.Join(", ", last.platforms.Where(p => p.ok && !p.dev).Select(p => p.platform)) + ") to " + s.itchPage + "?\nPlayers will get it straight away.", "Publish", "Cancel"))
                    Itch.Publish(last, s);
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(Itch.LastResult)) EditorGUILayout.HelpBox(Itch.LastResult, Itch.LastFailed ? MessageType.Error : MessageType.None);
            }
            if (Itch.ValidPage(s.itchPage) && GUILayout.Button("Open the page", GUILayout.Width(120))) Application.OpenURL("https://" + s.itchPage.Split('/')[0] + ".itch.io/" + s.itchPage.Split('/')[1]);
            EditorGUILayout.EndVertical();
        }

        void DrawReport()
        {
            string path = BuildAll.LastReportPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (m_Report == null || m_ReportPath != path || File.GetLastWriteTimeUtc(path) > m_ReportTime) { m_Report = BuildAll.LoadReport(path); m_ReportPath = path; m_ReportTime = File.GetLastWriteTimeUtc(path); }
            var r = m_Report; if (r == null) return;
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("Last build: " + r.product + " v" + r.version + "   (" + r.date + ")", m_Big);
            foreach (var p in r.platforms)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(p.platform + (p.dev ? " (development)" : ""), EditorStyles.boldLabel, GUILayout.Width(170));
                EditorGUILayout.LabelField(p.ok ? "OK" : "FAILED", p.ok ? m_Good : m_Bad, GUILayout.Width(60));
                EditorGUILayout.LabelField(p.minutes.ToString("0.0") + " min", GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
                if (!p.ok) { EditorGUILayout.HelpBox(p.errors + " errors. " + (p.firstError ?? "") + "\nSee the Console for all of them.", MessageType.Error); continue; }
                Line("Download (zip)", Mb(p.zipBytes), "what players download from itch.io");
                Line("Unpacked", Mb(p.folderBytes), "the build folder on disk");
                if (p.codeBytes > 0) Line("Code", Mb(p.codeBytes), p.platform == "Web" ? ".wasm + framework.js" : "game code and the engine's");
                Line("Warnings", p.warnings.ToString(), p.warnings > 0 ? "listed below" : "");
                // size by kind of asset, as bars
                if (p.byKind.Count > 0)
                {
                    long max = p.byKind.Max(k => k.bytes);
                    foreach (var k in p.byKind.Take(8)) Bar(k.name, k.bytes, max);
                }
                m_ShowAssets = EditorGUILayout.Foldout(m_ShowAssets, "Biggest assets in it", true);
                if (m_ShowAssets)
                    foreach (var a in p.biggest.Take(15))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(Mb(a.bytes), GUILayout.Width(70));
                        if (GUILayout.Button(a.path, EditorStyles.linkLabel)) { var o = AssetDatabase.LoadMainAssetAtPath(a.path); if (o) { EditorGUIUtility.PingObject(o); Selection.activeObject = o; } }
                        EditorGUILayout.EndHorizontal();
                    }
                m_ShowFiles = EditorGUILayout.Foldout(m_ShowFiles, "Files (" + p.files.Count + ")", true);
                if (m_ShowFiles) foreach (var f in p.files.Take(40)) EditorGUILayout.LabelField("   " + Mb(f.bytes).PadLeft(9) + "   " + f.path, EditorStyles.miniLabel);
                if (p.warningTexts.Count > 0)
                {
                    m_ShowWarnings = EditorGUILayout.Foldout(m_ShowWarnings, "Warnings (" + p.warnings + ")", true);
                    if (m_ShowWarnings) foreach (var w in p.warningTexts) EditorGUILayout.LabelField(w, EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open the report file")) EditorUtility.OpenWithDefaultApp(Path.ChangeExtension(path, ".txt"));
            if (GUILayout.Button("Copy it")) { EditorGUIUtility.systemCopyBuffer = File.ReadAllText(Path.ChangeExtension(path, ".txt")); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        DateTime m_ReportTime;

        static void Line(string label, string value, string note)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.Width(90));
            EditorGUILayout.LabelField(note, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        static void Bar(string label, long bytes, long max)
        {
            var r = EditorGUILayout.GetControlRect(false, 16);
            var l = new Rect(r.x, r.y, 110, r.height); var b = new Rect(r.x + 112, r.y + 2, Mathf.Max(2f, (r.width - 200) * bytes / Mathf.Max(1f, max)), r.height - 4);
            GUI.Label(l, label, EditorStyles.miniLabel);
            EditorGUI.DrawRect(b, new Color(0.3f, 0.6f, 0.95f, 0.85f));
            GUI.Label(new Rect(b.xMax + 6, r.y, 90, r.height), Mb(bytes), EditorStyles.miniLabel);
        }

        public static string Mb(long b) => b >= 1024 * 1024 ? (b / (1024f * 1024f)).ToString("0.0") + " MB" : (b / 1024f).ToString("0") + " KB";

        void OnInspectorUpdate() => Repaint();
    }

    [InitializeOnLoad]
    public static class BuildAll
    {
        [Serializable] public class Prefs { public bool bump, windows = true, web = true, run = true, dev, publish; public string outFolder = "Build", itchPage = "", chanWindows = "windows", chanWeb = "html5"; }

        // ---- the build report (kept in the state between reloads, saved as .json + .txt next to the zips)
        [Serializable] public class Sized { public string name, path; public long bytes; }
        [Serializable] public class PlatformReport
        {
            public string platform, folder; public bool ok, dev; public float minutes; public int errors, warnings; public string firstError;
            public long zipBytes, folderBytes, codeBytes, reportedBytes;
            public List<Sized> byKind = new List<Sized>(), biggest = new List<Sized>(), files = new List<Sized>();
            public List<string> warningTexts = new List<string>();
        }
        [Serializable] public class Report { public string product, company, version, date, unity; public List<PlatformReport> platforms = new List<PlatformReport>(); }

        [Serializable] class State
        {
            public List<string> steps = new List<string>(); // "Windows", "Web", "Back"
            public int at;
            public string startTarget, startGroup, version;
            public bool run, cancel, dev, publish;
            public string switchingTo; public int switchTries;
            public Report report = new Report();
        }

        static readonly string StatePath = Path.GetFullPath("Library/BuildAllState.json");
        const string PrefsKey = "BuildAll.Prefs";
        // the output folder: "Build" next to Assets by default (ignored by git); a relative path is from the project folder
        public static string OutDir => Path.GetFullPath(string.IsNullOrWhiteSpace(Settings.outFolder) ? "Build" : Settings.outFolder.Trim()).Replace('\\', '/');
        public static string LastReportPath { get => EditorPrefs.GetString("BuildAll.LastReport." + Application.productName, ""); set => EditorPrefs.SetString("BuildAll.LastReport." + Application.productName, value); }
        public static bool Running => File.Exists(StatePath);
        public static string StatusText { get { var st = Load(); return st == null ? "" : st.at < st.steps.Count ? (st.steps[st.at] == "Back" ? "switching back" : st.steps[st.at]) + " (step " + (st.at + 1) + " of " + st.steps.Count + ")" : "finishing"; } }

        static Prefs s_Prefs;
        public static Prefs Settings
        {
            get
            {
                if (s_Prefs == null) { s_Prefs = new Prefs(); var j = EditorPrefs.GetString(PrefsKey + "." + Application.dataPath, ""); if (j != "") JsonUtility.FromJsonOverwrite(j, s_Prefs); }
                return s_Prefs;
            }
        }
        public static void SaveSettings() => EditorPrefs.SetString(PrefsKey + "." + Application.dataPath, JsonUtility.ToJson(Settings));

        static BuildAll()
        {
            // after every reload (including the one a platform switch causes): carry on if a run is going
            if (Running) { Waker.Start(); EditorApplication.delayCall += Resume; }
        }

        public static void Start()
        {
            if (Running) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Build All", "Stop Play mode first.", "OK"); return; }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var p = Settings;
            if (p.bump)
            {
                PlayerSettings.bundleVersion = Bump(PlayerSettings.bundleVersion);
                // the changelog's notes so far become this version's
                if (Changelog.Release(PlayerSettings.bundleVersion)) Debug.Log("[Build All] changelog: notes saved as v" + PlayerSettings.bundleVersion);
            }
            AssetDatabase.SaveAssets();
            var st = new State
            {
                startTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                startGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                version = PlayerSettings.bundleVersion, run = p.run, dev = p.dev, publish = p.publish && !p.dev && Itch.ValidPage(p.itchPage),
            };
            st.report.product = PlayerSettings.productName; st.report.company = PlayerSettings.companyName; st.report.version = st.version;
            st.report.date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); st.report.unity = Application.unityVersion;
            // the platform the editor is on goes first (no switch needed for it)
            var order = new List<string>();
            bool onWeb = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL;
            if (onWeb) { if (p.web) order.Add("Web"); if (p.windows) order.Add("Windows"); }
            else { if (p.windows) order.Add("Windows"); if (p.web) order.Add("Web"); }
            st.steps = order; st.steps.Add("Back");
            Save(st);
            Debug.Log("[Build All] " + PlayerSettings.productName + " v" + st.version + ": " + string.Join(" -> ", st.steps));
            Waker.Start();
            EditorApplication.delayCall += Resume;
        }

        public static void Cancel() { var st = Load(); if (st == null) return; st.cancel = true; Save(st); }

        static bool s_Waiting;
        static void Resume()
        {
            if (s_Waiting) return;
            s_Waiting = true;
            EditorApplication.update += WaitThenStep;
        }

        // wait until Unity is done compiling / importing before doing the next step
        static void WaitThenStep()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= WaitThenStep;
            s_Waiting = false;
            try { Step(); }
            catch (Exception e) { Debug.LogException(e); Finish(Load(), "Build All stopped: " + e.Message, true); }
        }

        static void Step()
        {
            var st = Load();
            if (st == null) return;
            if (st.cancel && st.at < st.steps.Count && st.steps[st.at] != "Back") st.at = st.steps.IndexOf("Back");
            if (st.at >= st.steps.Count) { Finish(st, null, false); return; }
            string step = st.steps[st.at];

            BuildTarget target; BuildTargetGroup group;
            if (step == "Windows") { target = BuildTarget.StandaloneWindows64; group = BuildTargetGroup.Standalone; }
            else if (step == "Web") { target = BuildTarget.WebGL; group = BuildTargetGroup.WebGL; }
            else { target = (BuildTarget)Enum.Parse(typeof(BuildTarget), st.startTarget); group = (BuildTargetGroup)Enum.Parse(typeof(BuildTargetGroup), st.startGroup); }

            // the builds are done: report, publish and open them first, then switch back last (that switch recompiles,
            // which Unity may hold until you click back into it; nothing waits on it any more)
            if (step == "Back")
            {
                st.at++; Save(st);
                Finish(st, null, false);
                if (EditorUserBuildSettings.activeBuildTarget != target)
                {
                    Debug.Log("[Build All] switching the editor back to " + target + " (it finishes when Unity is in front)");
                    EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
                }
                return;
            }

            // 1) on the wrong platform: switch (Unity recompiles and reloads; the static constructor brings us back here)
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                if (st.switchingTo == step && st.switchTries >= 2) { Finish(st, "Couldn't switch the editor to " + step + " (is that platform's module installed?).", true); return; }
                st.switchTries = st.switchingTo == step ? st.switchTries + 1 : 1; st.switchingTo = step; Save(st);
                Debug.Log("[Build All] switching to " + step + "...");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target)) { Finish(st, "Couldn't switch the editor to " + step + ".", true); return; }
                EditorApplication.delayCall += Resume; // (if no reload happens, carry on anyway)
                return;
            }
            st.switchingTo = null; st.switchTries = 0;

            // 2) build
            string safe = Safe(PlayerSettings.productName);
            string folder = Path.Combine(OutDir, safe + " v" + st.version + " " + step);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Finish(st, "No scenes are ticked in the build list.", true); return; }
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                targetGroup = group,
                locationPathName = step == "Windows" ? Path.Combine(folder, safe + ".exe") : folder,
                options = st.dev ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
            };
            Debug.Log("[Build All] building " + step + " -> " + folder);
            var report = BuildPipeline.BuildPlayer(opts);
            var pr = Describe(report, step, folder, st.dev);
            pr.folder = folder;
            st.report.platforms.Add(pr);
            if (!pr.ok)
            {
                st.at = st.steps.IndexOf("Back"); Save(st);
                EditorApplication.delayCall += Resume; // still switch back
                return;
            }

            // 3) zip (without Unity's debug-symbol folders, which aren't meant to be shipped)
            string zip = Path.Combine(OutDir, safe + " v" + st.version + " " + step + ".zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(folder.Length + 1).Replace('\\', '/');
                    if (DontShip(rel)) continue;
                    z.CreateEntryFromFile(f, rel, System.IO.Compression.CompressionLevel.Optimal);
                }
            pr.zipBytes = new FileInfo(zip).Length;
            Debug.Log("[Build All] " + step + ": " + Path.GetFileName(zip) + " (" + BuildAllWindow.Mb(pr.zipBytes) + ")");

            st.at++; Save(st);
            EditorApplication.delayCall += Resume;
        }

        static bool DontShip(string rel) => rel.Contains("_BurstDebugInformation_DoNotShip") || rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame");

        // what the build report tells us, in a form that survives the reloads
        static PlatformReport Describe(BuildReport report, string step, string folder, bool dev)
        {
            var s = report.summary;
            var pr = new PlatformReport
            {
                platform = step, dev = dev, ok = s.result == BuildResult.Succeeded, minutes = (float)s.totalTime.TotalMinutes,
                errors = s.totalErrors, warnings = s.totalWarnings, reportedBytes = (long)s.totalSize,
            };
            foreach (var bs in report.steps)
                foreach (var m in bs.messages)
                {
                    if (m.type == LogType.Warning && pr.warningTexts.Count < 15) pr.warningTexts.Add("• " + m.content.Split('\n')[0]);
                    if ((m.type == LogType.Error || m.type == LogType.Exception) && pr.firstError == null) pr.firstError = m.content.Split('\n')[0];
                }
            if (!pr.ok) return pr;
            // files on disk
            foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(folder.Length + 1).Replace('\\', '/');
                if (DontShip(rel)) continue;
                long b = new FileInfo(f).Length;
                pr.folderBytes += b;
                pr.files.Add(new Sized { path = rel, bytes = b });
                string low = rel.ToLowerInvariant();
                if (low.EndsWith(".wasm") || low.EndsWith(".wasm.gz") || low.EndsWith(".wasm.br") || low.EndsWith(".wasm.unityweb") || low.Contains(".framework.js")
                    || low.EndsWith("gameassembly.dll") || low.EndsWith("unityplayer.dll") || (low.Contains("/managed/") && low.EndsWith(".dll"))) pr.codeBytes += b;
            }
            pr.files.Sort((a, b) => b.bytes.CompareTo(a.bytes));
            // assets packed into the build, by kind and the biggest ones
            var kinds = new Dictionary<string, long>(); var assets = new Dictionary<string, long>();
            foreach (var pa in report.packedAssets)
                foreach (var c in pa.contents)
                {
                    string kind = Kind(c.type, c.sourceAssetPath);
                    kinds[kind] = (kinds.TryGetValue(kind, out var k) ? k : 0) + (long)c.packedSize;
                    string path = string.IsNullOrEmpty(c.sourceAssetPath) ? "(built-in)" : c.sourceAssetPath;
                    assets[path] = (assets.TryGetValue(path, out var a) ? a : 0) + (long)c.packedSize;
                }
            pr.byKind = kinds.OrderByDescending(x => x.Value).Select(x => new Sized { name = x.Key, bytes = x.Value }).ToList();
            pr.biggest = assets.OrderByDescending(x => x.Value).Take(25).Select(x => new Sized { path = x.Key, bytes = x.Value }).ToList();
            return pr;
        }

        static string Kind(Type t, string path)
        {
            string n = t != null ? t.Name : "";
            if (n.Contains("Texture") || n == "Sprite" || n == "Cubemap") return "Textures";
            if (n == "Mesh") return "Meshes";
            if (n.Contains("Shader") || n == "ComputeShader") return "Shaders";
            if (n == "AudioClip") return "Audio";
            if (n == "Font" || n.Contains("FontAsset")) return "Fonts";
            if (n.Contains("Animation") || n.Contains("Animator") || n == "Avatar") return "Animation";
            if (n == "Material") return "Materials";
            if (n == "MonoScript" || n == "MonoBehaviour" || n == "ScriptableObject") return "Scripts & data";
            if (n == "GameObject" || n == "Transform" || n.Contains("Prefab")) return "Objects";
            if (n == "TextAsset") return "Text";
            if (n.Contains("Scene") || (path != null && path.EndsWith(".unity"))) return "Scenes";
            return "Other";
        }

        static void WriteReport(Report r)
        {
            Directory.CreateDirectory(OutDir);
            string basePath = Path.Combine(OutDir, Safe(r.product) + " v" + r.version + " report");
            File.WriteAllText(basePath + ".json", JsonUtility.ToJson(r, true));
            var t = new System.Text.StringBuilder();
            t.AppendLine(r.product + " v" + r.version + "  (" + r.company + ")  built " + r.date + " with Unity " + r.unity);
            foreach (var p in r.platforms)
            {
                t.AppendLine();
                t.AppendLine("== " + p.platform + (p.dev ? " (development)" : "") + ": " + (p.ok ? "OK" : "FAILED") + " in " + p.minutes.ToString("0.0") + " min, " + p.errors + " errors, " + p.warnings + " warnings");
                if (!p.ok) { if (p.firstError != null) t.AppendLine("First error: " + p.firstError); continue; }
                t.AppendLine("Download (zip): " + BuildAllWindow.Mb(p.zipBytes) + "   unpacked: " + BuildAllWindow.Mb(p.folderBytes) + "   code: " + BuildAllWindow.Mb(p.codeBytes));
                t.AppendLine("By kind: " + string.Join(", ", p.byKind.Select(k => k.name + " " + BuildAllWindow.Mb(k.bytes))));
                t.AppendLine("Biggest assets:"); foreach (var a in p.biggest.Take(15)) t.AppendLine("  " + BuildAllWindow.Mb(a.bytes).PadLeft(9) + "  " + a.path);
                t.AppendLine("Files:"); foreach (var f in p.files.Take(30)) t.AppendLine("  " + BuildAllWindow.Mb(f.bytes).PadLeft(9) + "  " + f.path);
                if (p.warningTexts.Count > 0) { t.AppendLine("Warnings:"); foreach (var w in p.warningTexts) t.AppendLine("  " + w); }
            }
            File.WriteAllText(basePath + ".txt", t.ToString());
            LastReportPath = basePath + ".json";
        }

        public static Report LoadReport(string path) { try { return JsonUtility.FromJson<Report>(File.ReadAllText(path)); } catch { return null; } }

        static void Finish(State st, string error, bool isError)
        {
            if (File.Exists(StatePath)) File.Delete(StatePath);
            string msg = error;
            if (st != null && st.report.platforms.Count > 0)
            {
                WriteReport(st.report);
                msg = (error != null ? error + "\n\n" : "") + st.report.product + " v" + st.report.version + (st.cancel ? " (stopped)" : "") + "\n" +
                      string.Join("\n", st.report.platforms.Select(p => p.platform + ": " + (p.ok ? "OK, zip " + BuildAllWindow.Mb(p.zipBytes) + ", " + p.minutes.ToString("0.0") + " min" : "FAILED (" + p.errors + " errors, see the Console)"))) +
                      "\n\nThe full report is in the Build All window.";
                isError |= st.report.platforms.Any(p => !p.ok);
            }
            if (msg == null) msg = "Nothing was built.";
            if (isError) Debug.LogError("[Build All] " + msg); else Debug.Log("[Build All] done.\n" + msg);
            foreach (var bw in Resources.FindObjectsOfTypeAll<BuildAllWindow>()) bw.Repaint(); // (repaints it if it is open; never opens it)
            // publish to itch.io (only when ticked, everything built fine, and not a development build)
            bool publish = st != null && st.publish && !st.cancel && st.report.platforms.Count > 0 && st.report.platforms.All(p => p.ok);
            if (publish) msg += "\n\nPublishing to itch.io now (" + Settings.itchPage + "): progress in the Build All window and the Console.";
            EditorUtility.DisplayDialog("Build All", msg, "OK");
            if (publish) Itch.Publish(st.report, Settings);
            // open the builds now that everything is done
            if (st != null && st.run && !st.cancel)
                foreach (var p in st.report.platforms.Where(p => p.ok && !string.IsNullOrEmpty(p.folder) && Directory.Exists(p.folder)))
                {
                    if (p.platform == "Web") { WebServer.Serve(p.folder); continue; }
                    var exe = Directory.GetFiles(p.folder, "*.exe").FirstOrDefault(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase));
                    if (exe != null) try { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = p.folder, UseShellExecute = true }); } catch (Exception e) { Debug.LogWarning("[Build All] couldn't start the .exe: " + e.Message); }
                }
        }

        static State Load() { try { return File.Exists(StatePath) ? JsonUtility.FromJson<State>(File.ReadAllText(StatePath)) : null; } catch { return null; } }
        static void Save(State st) => File.WriteAllText(StatePath, JsonUtility.ToJson(st, true));

        static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Trim(); }

        public static string Bump(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.1";
            var parts = v.Split('.');
            int last = parts.Length - 1;
            var digits = new string(parts[last].TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out int n)) return v + ".1";
            parts[last] = (n + 1).ToString();
            return string.Join(".", parts);
        }
    }

    /// <summary>
    /// While a build run is going, nudges Unity's window a few times a second (a harmless "nothing" message, no focus
    /// change). Unity otherwise sleeps when it's in the background (e.g. behind a game you started) and the run waits until
    /// you click back into it.
    /// </summary>
    static class Waker
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumProc fn, IntPtr l);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        delegate bool EnumProc(IntPtr hWnd, IntPtr l);
        static System.Threading.Thread s_Thread;
        static readonly List<IntPtr> s_Windows = new List<IntPtr>();

        public static void Start()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor || (s_Thread != null && s_Thread.IsAlive)) return;
            // Unity's windows belong to the main thread (this one)
            s_Windows.Clear();
            EnumThreadWindows(GetCurrentThreadId(), (h, l) => { s_Windows.Add(h); return true; }, IntPtr.Zero);
            var windows = s_Windows.ToArray();
            string state = Path.GetFullPath("Library/BuildAllState.json");
            s_Thread = new System.Threading.Thread(() =>
            {
                try
                {
                    while (File.Exists(state))
                    {
                        foreach (var h in windows) PostMessage(h, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
                        System.Threading.Thread.Sleep(200);
                    }
                }
                catch { }
            }) { IsBackground = true, Name = "Build All waker" };
            s_Thread.Start();
            AssemblyReloadEvents.beforeAssemblyReload += () => { try { s_Thread?.Abort(); } catch { } };
        }
    }

    /// <summary>
    /// Publishes builds to itch.io with butler (itch.io's own upload tool; the itch.io app ships a copy). Each platform goes
    /// to its channel ("windows", "html5"), tagged with the version; butler only uploads what changed. Runs in the background
    /// (outside Unity, so a script reload doesn't stop it); the log is Library/BuildAll/butler.log.
    /// </summary>
    public static class Itch
    {
        public static string Status = "", LastResult = "";
        public static bool Busy, LastFailed;
        static string s_Butler; static bool s_Looked;

        public static string Butler
        {
            get
            {
                if (s_Looked) return s_Butler;
                s_Looked = true;
                // the itch.io app's copy (newest version), else one on the PATH
                string broth = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "broth", "butler", "versions");
                if (Directory.Exists(broth))
                    s_Butler = Directory.GetDirectories(broth).OrderByDescending(d => d, StringComparer.Ordinal)
                        .Select(d => Path.Combine(d, "butler.exe")).FirstOrDefault(File.Exists);
                if (s_Butler == null)
                    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                    {
                        try { var f = Path.Combine(dir.Trim(), "butler.exe"); if (File.Exists(f)) { s_Butler = f; break; } } catch { }
                    }
                return s_Butler;
            }
        }

        public static bool LoggedIn =>
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "itch", "butler_creds")) ||
            File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "itch", "butler_creds"));

        public static string CleanPage(string s)
        {
            s = (s ?? "").Trim().TrimEnd('/');
            // https://user.itch.io/game -> user/game
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^(?:https?://)?([A-Za-z0-9_-]+)\.itch\.io/([A-Za-z0-9_-]+)");
            if (m.Success) return m.Groups[1].Value.ToLowerInvariant() + "/" + m.Groups[2].Value.ToLowerInvariant();
            return s;
        }
        public static bool ValidPage(string s) => !string.IsNullOrEmpty(s) && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$");

        // a console window: butler opens the browser, the person approves it there
        public static void Login()
        {
            if (Butler == null) return;
            try { Process.Start(new ProcessStartInfo(Butler, "login") { UseShellExecute = true }); }
            catch (Exception e) { Debug.LogWarning("[Build All] couldn't start butler login: " + e.Message); }
        }

        public static void Publish(BuildAll.Report r, BuildAll.Prefs p)
        {
            if (Busy || Butler == null || r == null) return;
            if (!LoggedIn) { LastResult = "butler isn't logged in: press Log in first."; LastFailed = true; return; }
            var jobs = new List<(string name, string args)>();
            foreach (var pl in r.platforms)
            {
                if (!pl.ok || pl.dev || string.IsNullOrEmpty(pl.folder) || !Directory.Exists(pl.folder)) continue;
                string chan = pl.platform == "Web" ? p.chanWeb : p.chanWindows;
                if (string.IsNullOrWhiteSpace(chan)) continue;
                jobs.Add((pl.platform, "push \"" + pl.folder + "\" " + p.itchPage + ":" + chan.Trim() + " --userversion \"" + r.version + "\"" +
                                       " --ignore \"*_DoNotShip*\" --ignore \"*_ButDontShipItWithYourGame*\""));
            }
            if (jobs.Count == 0) { LastResult = "Nothing to publish (no finished, non-development builds)."; LastFailed = true; return; }
            Busy = true; LastFailed = false; LastResult = ""; Status = "starting...";
            string log = Path.GetFullPath("Library/BuildAll/butler.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log, "");
            string butler = Butler, page = p.itchPage, version = r.version;
            var results = new List<string>(); bool failed = false;
            var thread = new System.Threading.Thread(() =>
            {
                foreach (var job in jobs)
                {
                    Status = job.name + ": uploading to " + page + "...";
                    try
                    {
                        var psi = new ProcessStartInfo(butler, job.args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                        using (var pr = Process.Start(psi))
                        {
                            pr.OutputDataReceived += (o, e) => { if (e.Data != null) { File.AppendAllText(log, e.Data + "\n"); if (e.Data.Trim().Length > 0) Status = job.name + ": " + e.Data.Trim(); } };
                            pr.ErrorDataReceived += (o, e) => { if (e.Data != null) { File.AppendAllText(log, e.Data + "\n"); if (e.Data.Trim().Length > 0) Status = job.name + ": " + e.Data.Trim(); } };
                            pr.BeginOutputReadLine(); pr.BeginErrorReadLine();
                            pr.WaitForExit();
                            if (pr.ExitCode == 0) results.Add(job.name + ": published as v" + version);
                            else { results.Add(job.name + ": FAILED (butler exit code " + pr.ExitCode + ", see Library/BuildAll/butler.log)"); failed = true; }
                        }
                    }
                    catch (Exception e) { results.Add(job.name + ": FAILED (" + e.Message + ")"); failed = true; }
                }
                Busy = false;
            }) { IsBackground = true };
            thread.Start();
            // report back on the main thread when it's done
            EditorApplication.CallbackFunction watch = null;
            watch = () =>
            {
                if (Busy) { RepaintWindow(); return; }
                EditorApplication.update -= watch;
                LastFailed = failed;
                LastResult = string.Join("\n", results) + (failed ? "" : "\nOn the page it can take a minute to show the new version.");
                if (failed) Debug.LogError("[Build All] itch.io: " + LastResult + "\n" + SafeTail(log)); else Debug.Log("[Build All] itch.io: " + LastResult);
                RepaintWindow();
            };
            EditorApplication.update += watch;
        }

        static string SafeTail(string log) { try { var l = File.ReadAllLines(log); return string.Join("\n", l.Skip(Math.Max(0, l.Length - 15))); } catch { return ""; } }
        static void RepaintWindow() { foreach (var w in Resources.FindObjectsOfTypeAll<BuildAllWindow>()) w.Repaint(); }
    }

    /// <summary>
    /// Opens a web build in the browser. Browsers won't run one from a file:// address, so this starts a tiny local web
    /// server (a PowerShell window, minimised, outside Unity so it survives Unity's reloads) on http://localhost:port.
    /// Starting a new one closes the previous one; close its window to stop it.
    /// </summary>
    public static class WebServer
    {
        const string Script = @"param([string]$root, [int]$port, [int]$unity)
$host.UI.RawUI.WindowTitle = 'Build All web server - http://localhost:' + $port + '  (close to stop)'
$root = [IO.Path]::GetFullPath($root)
$l = New-Object System.Net.HttpListener
$l.Prefixes.Add('http://localhost:' + $port + '/')
$l.Start()
Write-Host ('Serving ' + $root + ' on http://localhost:' + $port + '/  (close this window to stop)')
$types = @{ '.html'='text/html'; '.js'='application/javascript'; '.wasm'='application/wasm'; '.css'='text/css'; '.png'='image/png'; '.ico'='image/x-icon'; '.json'='application/json' }
while ($l.IsListening) {
  # (stops by itself when Unity closes)
  $ar = $l.BeginGetContext($null, $null)
  while (-not $ar.AsyncWaitHandle.WaitOne(1000)) { if ($unity -gt 0 -and -not (Get-Process -Id $unity -ErrorAction SilentlyContinue)) { $l.Stop(); exit } }
  $c = $l.EndGetContext($ar)
  try {
    $rel = [Uri]::UnescapeDataString($c.Request.Url.AbsolutePath).TrimStart('/')
    if ($rel -eq '') { $rel = 'index.html' }
    $p = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $rel))
    if (-not $p.StartsWith($root) -or -not [IO.File]::Exists($p)) { $c.Response.StatusCode = 404; $c.Response.Close(); continue }
    $n = $p
    if ($n.EndsWith('.gz')) { $c.Response.AddHeader('Content-Encoding', 'gzip'); $n = $n.Substring(0, $n.Length - 3) }
    elseif ($n.EndsWith('.br')) { $c.Response.AddHeader('Content-Encoding', 'br'); $n = $n.Substring(0, $n.Length - 3) }
    $ext = [IO.Path]::GetExtension($n).ToLower()
    if ($types.ContainsKey($ext)) { $c.Response.ContentType = $types[$ext] } else { $c.Response.ContentType = 'application/octet-stream' }
    $c.Response.AddHeader('Cache-Control', 'no-cache')
    $b = [IO.File]::ReadAllBytes($p)
    $c.Response.ContentLength64 = $b.Length
    $c.Response.OutputStream.Write($b, 0, $b.Length)
    $c.Response.Close()
  } catch { try { $c.Response.Abort() } catch {} }
}
";
        const string PidKey = "BuildAll.WebServerPid";

        public static string Url { get => SessionState.GetString("BuildAll.WebUrl", ""); private set => SessionState.SetString("BuildAll.WebUrl", value); }
        public static bool Running
        {
            get { int id = EditorPrefs.GetInt(PidKey, 0); if (id == 0) return false; try { var pr = Process.GetProcessById(id); return !pr.HasExited && pr.ProcessName.ToLower().Contains("powershell"); } catch { return false; } }
        }

        public static void Stop()
        {
            int old = EditorPrefs.GetInt(PidKey, 0);
            if (old != 0) { try { var pr = Process.GetProcessById(old); if (pr.ProcessName.ToLower().Contains("powershell")) pr.Kill(); } catch { } EditorPrefs.DeleteKey(PidKey); }
            Url = "";
        }

        public static void Serve(string folder)
        {
            Stop(); // (the server from the last run)

            int port = FreePort();
            string ps1 = Path.GetFullPath("Library/BuildAll/serve.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(ps1));
            File.WriteAllText(ps1, Script);
            try
            {
                var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\" -root \"" + folder + "\" -port " + port + " -unity " + Process.GetCurrentProcess().Id)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                if (p != null) EditorPrefs.SetInt(PidKey, p.Id);
            }
            catch (Exception e) { Debug.LogWarning("[Build All] couldn't start the local web server: " + e.Message); return; }
            string url = "http://localhost:" + port + "/";
            Url = url;
            Debug.Log("[Build All] web build at " + url + " (a small hidden server; it stops when Unity closes, or with Stop in the Build All window)");
            // give the server a moment to start listening
            double at = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.CallbackFunction open = null;
            open = () => { if (EditorApplication.timeSinceStartup < at) return; EditorApplication.update -= open; Application.OpenURL(url); };
            EditorApplication.update += open;
        }

        static int FreePort()
        {
            for (int port = 8642; port < 8700; port++)
            {
                try { var t = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port); t.Start(); t.Stop(); return port; }
                catch { }
            }
            return 8700;
        }
    }
}
