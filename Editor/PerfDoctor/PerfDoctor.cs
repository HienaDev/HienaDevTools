using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Perf Doctor: a plain-language profiler. Press Record while the game runs (Play mode, or a development build
    /// picked in the Unity Profiler's target menu); it records a few seconds with Unity's own profiler, then lists what took
    /// the time: a verdict (fps, CPU or GPU bound), the biggest costs with what they are and what to try, the spikes
    /// (hitches) and what caused them, memory garbage per frame, and the draw-call numbers. Click a row to open that
    /// frame in Unity's Profiler, or the script behind it.
    /// </summary>
    public class PerfDoctorWindow : EditorWindow
    {
        [MenuItem("Tools/Perf Doctor (easy profiler)", false, 101)]
        static void Open() { var w = GetWindow<PerfDoctorWindow>(false, "Perf Doctor", true); w.minSize = new Vector2(460, 420); }

        // ---- recording
        float m_Seconds = 5f; int m_TargetFps = 60;
        bool m_Recording, m_WaitPlay; double m_RecordUntil, m_WarmUntil;
        readonly List<float> m_Gpu = new List<float>(), m_CpuMain = new List<float>();
        readonly List<int> m_Batches = new List<int>(), m_SetPass = new List<int>(), m_Tris = new List<int>(), m_Shadow = new List<int>();
        readonly FrameTiming[] m_Timing = new FrameTiming[1];
        bool m_HadTimingStats, m_PlayerTarget;

        Result m_Result; Vector2 m_Scroll;
        GUIStyle m_H, m_Big, m_Box, m_Wrap, m_Row;
        int m_Tab;

        void OnEnable() { EditorApplication.update += Tick; }
        void OnDisable() { EditorApplication.update -= Tick; if (m_Recording) StopRecording(false); }

        void Styles()
        {
            if (m_H != null) return;
            m_H = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            m_Big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 20 };
            m_Box = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
            m_Wrap = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            m_Row = new GUIStyle(EditorStyles.label) { richText = true };
        }

        void OnGUI()
        {
            Styles();
            // ---- top bar
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Record", GUILayout.Width(48));
            m_Seconds = EditorGUILayout.Slider(m_Seconds, 2f, 20f, GUILayout.Width(170));
            EditorGUILayout.LabelField("s", GUILayout.Width(12));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Aim for", GUILayout.Width(48));
            m_TargetFps = EditorGUILayout.IntPopup(m_TargetFps, new[] { "30 fps", "60 fps", "120 fps", "144 fps" }, new[] { 30, 60, 120, 144 }, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            if (m_Recording)
            {
                double left = (EditorApplication.timeSinceStartup < m_WarmUntil ? m_WarmUntil - EditorApplication.timeSinceStartup : 0) + Math.Max(0, m_RecordUntil - EditorApplication.timeSinceStartup);
                var r = EditorGUILayout.GetControlRect(false, 30);
                EditorGUI.ProgressBar(r, 1f - (float)(left / (m_Seconds + 1)), EditorApplication.timeSinceStartup < m_WarmUntil ? "Getting ready..." : "Recording... play the game normally (" + left.ToString("0") + " s)");
                if (GUILayout.Button("Stop now and look")) StopRecording(true);
            }
            else if (m_WaitPlay) { EditorGUILayout.HelpBox("Starting Play mode, then recording...", MessageType.Info); if (GUILayout.Button("Cancel")) m_WaitPlay = false; }
            else
            {
                EditorGUILayout.BeginHorizontal();
                bool playing = EditorApplication.isPlaying;
                string label = playing ? "Record " + m_Seconds.ToString("0") + " s of the game" : "Play and record " + m_Seconds.ToString("0") + " s";
                if (GUILayout.Button(label, GUILayout.Height(34)))
                {
                    if (playing) StartRecording();
                    else { m_WaitPlay = true; EditorApplication.isPlaying = true; }
                }
                if (!playing && GUILayout.Button(new GUIContent("Record a\nconnected build", "A development build picked in the Profiler's target menu"), GUILayout.Height(34), GUILayout.Width(120))) { m_PlayerTarget = true; StartRecording(); }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(playing ? "Do what you want to check (open the slow screen, move around) while it records." :
                    "Tip: a build runs faster than the editor. To check a build: tick Development build in Build All, start it, pick it in Window > Analysis > Profiler's target menu, then Record here.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();

            if (m_Result == null) { EditorGUILayout.HelpBox("Record a few seconds of the game and Perf Doctor tells you what's taking the time, in plain words.", MessageType.None); return; }

            m_Tab = GUILayout.Toolbar(m_Tab, new[] { "Summary", "Biggest costs", "Hitches", "Garbage", "Copy report" });
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            switch (m_Tab)
            {
                case 0: DrawSummary(); break;
                case 1: DrawCosts(m_Result.costs, "Time per frame, by what it is (self time: the time spent in that thing itself, not in what it calls).", false); break;
                case 2: DrawSpikes(); break;
                case 3: DrawCosts(m_Result.garbage, "Memory thrown away every frame. Garbage piles up and the cleaner (GC.Collect) then stops the game for a moment: a hitch.", true); break;
                case 4: EditorGUILayout.TextArea(m_Result.text, GUILayout.ExpandHeight(true)); if (GUILayout.Button("Copy to clipboard")) EditorGUIUtility.systemCopyBuffer = m_Result.text; break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ views

        void DrawSummary()
        {
            var r = m_Result;
            float budget = 1000f / m_TargetFps;
            EditorGUILayout.BeginVertical(m_Box);
            Color c = r.avgMs <= budget * 1.05f ? new Color(0.35f, 0.8f, 0.4f) : r.avgMs <= budget * 1.6f ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.95f, 0.4f, 0.35f);
            var old = GUI.color; GUI.color = c;
            EditorGUILayout.LabelField(r.verdict, m_Big, GUILayout.Height(28));
            GUI.color = old;
            EditorGUILayout.LabelField(r.verdictDetail, m_Wrap);
            EditorGUILayout.EndVertical();

            // where a frame's time goes, as one bar
            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("Where a frame's time goes (" + r.avgMs.ToString("0.0") + " ms on average)", m_H);
            var rect = EditorGUILayout.GetControlRect(false, 22);
            float total = Mathf.Max(0.001f, r.groups.Sum(g => g.ms)), x = rect.x;
            foreach (var g in r.groups)
            {
                float w = rect.width * g.ms / total;
                EditorGUI.DrawRect(new Rect(x, rect.y, Mathf.Max(0, w - 1), rect.height), Group.Colour(g.name));
                if (w > 50) GUI.Label(new Rect(x + 3, rect.y + 3, w - 4, rect.height), g.name, EditorStyles.whiteMiniLabel);
                x += w;
            }
            foreach (var g in r.groups.Where(g => g.ms >= 0.05f))
            {
                EditorGUILayout.BeginHorizontal();
                var sw = EditorGUILayout.GetControlRect(false, 14, GUILayout.Width(14)); EditorGUI.DrawRect(new Rect(sw.x, sw.y + 2, 10, 10), Group.Colour(g.name));
                EditorGUILayout.LabelField(g.name, GUILayout.Width(150));
                EditorGUILayout.LabelField(g.ms.ToString("0.00") + " ms", EditorStyles.boldLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField(Group.Explain(g.name), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("What to look at first", m_H);
            if (r.findings.Count == 0) EditorGUILayout.LabelField("Nothing stands out. 👍", m_Wrap);
            foreach (var f in r.findings)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField((f.bad ? "<color=#ff7a66>●</color> " : "<color=#ffcc55>●</color> ") + "<b>" + f.title + "</b>", m_Row);
                EditorGUILayout.LabelField(f.detail, m_Wrap);
                if (f.frame >= 0 || f.marker != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (f.frame >= 0 && GUILayout.Button("Show that frame in Unity's Profiler", GUILayout.Width(230))) ShowFrame(f.frame, f.marker);
                    if (f.marker != null && ScriptFor(f.marker) != null && GUILayout.Button("Open the script", GUILayout.Width(120))) AssetDatabase.OpenAsset(ScriptFor(f.marker));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(m_Box);
            EditorGUILayout.LabelField("Numbers", m_H);
            Num("Frames recorded", r.frames.ToString(), "");
            Num("Average / worst frame", r.avgMs.ToString("0.0") + " / " + r.worstMs.ToString("0.0") + " ms", (1000f / Mathf.Max(0.01f, r.avgMs)).ToString("0") + " fps on average");
            Num("Game code + engine", r.gameMs.ToString("0.0") + " ms", "the part a build also pays");
            if (r.editorMs > 0.05f) Num("Editor overhead", r.editorMs.ToString("0.0") + " ms", "only in the editor, not in builds");
            Num("GPU", r.gpuMs > 0 ? r.gpuMs.ToString("0.0") + " ms" : "not measured", r.gpuMs > 0 ? "time the graphics card needs per frame" : "(turn on Player Settings > Frame Timing Stats)");
            Num("Batches / SetPass", r.batches + " / " + r.setPass, "draw work sent to the GPU each frame");
            Num("Triangles", (r.tris / 1000f).ToString("0") + "k", "");
            Num("Shadow casters", r.shadowCasters.ToString(), "");
            Num("Garbage per frame", Kb(r.gcPerFrame), "memory thrown away (should be near 0)");
            Num("Memory in use", Mb(r.memUsed), "textures " + Mb(r.memTex) + ", meshes " + Mb(r.memMesh));
            EditorGUILayout.EndVertical();
        }

        void DrawCosts(List<Cost> list, string intro, bool gc)
        {
            EditorGUILayout.LabelField(intro, EditorStyles.wordWrappedMiniLabel);
            float max = list.Count > 0 ? (gc ? list[0].gcPerFrame : list[0].msPerFrame) : 1f;
            foreach (var c in list.Take(40))
            {
                float v = gc ? c.gcPerFrame : c.msPerFrame; if (v <= 0) continue;
                EditorGUILayout.BeginVertical(m_Box);
                EditorGUILayout.BeginHorizontal();
                var rr = EditorGUILayout.GetControlRect(false, 16, GUILayout.Width(110));
                EditorGUI.DrawRect(new Rect(rr.x, rr.y + 3, Mathf.Max(2, rr.width * v / Mathf.Max(1e-5f, max)), 10), Group.Colour(c.group));
                EditorGUILayout.LabelField(gc ? Kb(v) : v.ToString("0.00") + " ms", EditorStyles.boldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("<b>" + c.name + "</b>  <color=#999999>" + c.group + (c.calls > 1.5f ? " · " + c.calls.ToString("0") + " calls" : "") + "</color>", m_Row);
                if (c.worstFrame >= 0 && GUILayout.Button("Profiler", GUILayout.Width(62))) ShowFrame(c.worstFrame, c.name);
                var script = ScriptFor(c.name);
                if (script != null && GUILayout.Button("Script", GUILayout.Width(52))) AssetDatabase.OpenAsset(script);
                EditorGUILayout.EndHorizontal();
                string what = Group.What(c.name);
                if (what != null) EditorGUILayout.LabelField(what, EditorStyles.wordWrappedMiniLabel);
                if (!string.IsNullOrEmpty(c.path)) EditorGUILayout.LabelField("in: " + c.path, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        void DrawSpikes()
        {
            var r = m_Result;
            EditorGUILayout.LabelField("A hitch is a frame where the game's own work took much longer than usual (the editor's own pauses are left out): the game stutters. " +
                (r.spikes.Count == 0 ? "None found." : r.spikes.Count + " found; the worst first, with what took the time in it."), EditorStyles.wordWrappedMiniLabel);
            foreach (var s in r.spikes.Take(20))
            {
                EditorGUILayout.BeginVertical(m_Box);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(s.ms.ToString("0.0") + " ms", m_H, GUILayout.Width(80));
                EditorGUILayout.LabelField("at " + s.time.ToString("0.0") + " s into the recording", EditorStyles.miniLabel);
                if (GUILayout.Button("Show in Unity's Profiler", GUILayout.Width(160))) ShowFrame(s.frame, s.top.Count > 0 ? s.top[0].name : null);
                EditorGUILayout.EndHorizontal();
                foreach (var t in s.top) EditorGUILayout.LabelField("   " + t.msPerFrame.ToString("0.0").PadLeft(6) + " ms   " + t.name + "  <color=#999999>" + t.group + "</color>", m_Row);
                EditorGUILayout.EndVertical();
            }
        }

        static void Num(string a, string b, string c)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(a, GUILayout.Width(150));
            EditorGUILayout.LabelField(b, EditorStyles.boldLabel, GUILayout.Width(120));
            EditorGUILayout.LabelField(c, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        static string Kb(float b) => b >= 1024 * 1024 ? (b / (1024f * 1024f)).ToString("0.0") + " MB" : b >= 1024 ? (b / 1024f).ToString("0.0") + " KB" : b.ToString("0") + " B";
        static string Mb(long b) => (b / (1024f * 1024f)).ToString("0") + " MB";

        // Opens Unity's Profiler at that frame (and searches for the item, where the window lets us)
        static void ShowFrame(int frame, string marker)
        {
            var w = GetWindow<ProfilerWindow>();
            w.selectedFrameIndex = frame;
            w.Show(); w.Focus();
        }

        // "FishPond.DrawFish" / "FishCreatorApp.Update()" / "PlayerController.Update() [Invoke]" -> the script with that class name
        static readonly Dictionary<string, MonoScript> s_Scripts = new Dictionary<string, MonoScript>();
        static MonoScript ScriptFor(string marker)
        {
            if (string.IsNullOrEmpty(marker)) return null;
            if (s_Scripts.TryGetValue(marker, out var cached)) return cached;
            MonoScript found = null;
            var parts = marker.Replace("()", "").Split(new[] { '.', ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts.Take(3))
            {
                if (p.Length < 3 || !char.IsUpper(p[0])) continue;
                foreach (var guid in AssetDatabase.FindAssets(p + " t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/com.hienadev")) continue;
                    string file = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (file == p || file.StartsWith(p + ".")) { found = AssetDatabase.LoadAssetAtPath<MonoScript>(path); break; }
                }
                if (found) break;
            }
            s_Scripts[marker] = found;
            return found;
        }

        // ------------------------------------------------------------------ recording

        void Tick()
        {
            if (m_WaitPlay && EditorApplication.isPlaying) { m_WaitPlay = false; m_PlayerTarget = false; StartRecording(); }
            if (!m_Recording) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < m_WarmUntil) { Repaint(); return; }
            if (m_RecordUntil == 0) { m_RecordUntil = now + m_Seconds; ProfilerDriver.ClearAllFrames(); }
            // per-frame numbers the profiler's CPU view doesn't give
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, m_Timing) > 0) { if (m_Timing[0].gpuFrameTime > 0) m_Gpu.Add((float)m_Timing[0].gpuFrameTime); m_CpuMain.Add((float)m_Timing[0].cpuMainThreadFrameTime); }
                m_Batches.Add(UnityStats.batches); m_SetPass.Add(UnityStats.setPassCalls); m_Tris.Add(UnityStats.triangles); m_Shadow.Add(UnityStats.shadowCasters);
            }
            if (now >= m_RecordUntil || (!m_PlayerTarget && !EditorApplication.isPlaying)) StopRecording(true);
            Repaint();
        }

        void StartRecording()
        {
            m_Result = null;
            m_Gpu.Clear(); m_CpuMain.Clear(); m_Batches.Clear(); m_SetPass.Clear(); m_Tris.Clear(); m_Shadow.Clear();
            m_HadTimingStats = PlayerSettings.enableFrameTimingStats;
            if (!m_HadTimingStats) PlayerSettings.enableFrameTimingStats = true; // (GPU time; put back afterwards)
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            m_Recording = true; m_RecordUntil = 0; m_WarmUntil = EditorApplication.timeSinceStartup + 1.0; // skip the first second (Play mode start-up)
        }

        void StopRecording(bool analyse)
        {
            m_Recording = false; m_PlayerTarget = false;
            ProfilerDriver.enabled = false;
            if (!m_HadTimingStats) PlayerSettings.enableFrameTimingStats = false;
            if (!analyse) return;
            try { m_Result = Analyse(); m_Tab = 0; }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("Perf Doctor", "Couldn't read the recording: " + e.Message, "OK"); }
            finally { EditorUtility.ClearProgressBar(); }
            Repaint();
        }

        // ------------------------------------------------------------------ analysis

        class Cost { public string name, group, path; public float msPerFrame, gcPerFrame, calls; public int worstFrame = -1; public float worstMs; }
        class GroupCost { public string name; public float ms; }
        class Spike { public int frame; public float ms, time; public List<Cost> top = new List<Cost>(); }
        class Finding { public string title, detail, marker; public bool bad; public int frame = -1; }
        class Result
        {
            public int frames; public float avgMs, medianMs, worstMs, gameMs, editorMs, gpuMs, gcPerFrame;
            public int batches, setPass, tris, shadowCasters; public long memUsed, memTex, memMesh;
            public string verdict, verdictDetail, text;
            public List<Cost> costs = new List<Cost>(), garbage = new List<Cost>();
            public List<GroupCost> groups = new List<GroupCost>();
            public List<Spike> spikes = new List<Spike>();
            public List<Finding> findings = new List<Finding>();
        }

        class Acc { public double self, gc, calls; public string path; public int worstFrame = -1; public float worstSelf; }

        // "Assembly-CSharp.dll!FishCreatorApp::FishCreatorApp.Update() [Invoke]" -> "FishCreatorApp.Update()"; "Inl_Forward+ Setup" -> "Forward+ Setup"
        static readonly Dictionary<string, string> s_Pretty = new Dictionary<string, string>();
        static string Pretty(string raw)
        {
            if (s_Pretty.TryGetValue(raw, out var p)) return p;
            string s = raw;
            int bang = s.IndexOf('!'); if (bang >= 0 && s.IndexOf(".dll", StringComparison.Ordinal) >= 0 && bang < s.IndexOf('(') + 1000) s = s.Substring(bang + 1);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[A-Za-z0-9_\.]+::", "");
            s = s.Replace(" [Invoke]", "");
            if (s.StartsWith("Inl_")) s = s.Substring(4);
            s_Pretty[raw] = s;
            return s;
        }

        Result Analyse()
        {
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first) throw new Exception("no frames were recorded (is the game running?)");
            if (last - first > 1200) first = last - 1200;
            var acc = new Dictionary<string, Acc>();
            var frameMs = new List<(int frame, float ms, float game)>();
            var perFrameTop = new Dictionary<int, List<(string name, float self)>>();
            double editor = 0, game = 0;
            var children = new List<int>();
            int n = 0, count = last - first + 1;
            for (int f = first; f <= last; f++)
            {
                if ((f - first) % 20 == 0) EditorUtility.DisplayProgressBar("Perf Doctor", "Reading the recording...", (f - first) / (float)count);
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    n++;
                    float frameGame = 0;
                    var top = new List<(string, float)>();
                    int root = view.GetRootItemID();
                    view.GetItemChildren(root, children);
                    var rootKids = children.ToList();
                    foreach (int id in rootKids)
                    {
                        string nm = Pretty(view.GetItemName(id));
                        float total = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
                        if (nm == "EditorLoop") { editor += total; continue; } // the editor's own work: not in a build
                        frameGame += total;
                        Walk(view, id, nm, 0, f, acc, top);
                    }
                    game += frameGame;
                    frameMs.Add((f, view.frameTimeMs, frameGame));
                    perFrameTop[f] = top;
                }
            }
            EditorUtility.ClearProgressBar();
            if (n == 0) throw new Exception("the recorded frames couldn't be read");

            var r = new Result { frames = n };
            var times = frameMs.Select(x => x.ms).OrderBy(x => x).ToList();
            r.avgMs = times.Average(); r.medianMs = times[times.Count / 2]; r.worstMs = times[times.Count - 1];
            r.gameMs = (float)(game / n); r.editorMs = (float)(editor / n);
            r.gpuMs = m_Gpu.Count > 0 ? m_Gpu.OrderBy(x => x).ElementAt(m_Gpu.Count / 2) : 0;
            r.batches = Median(m_Batches); r.setPass = Median(m_SetPass); r.tris = Median(m_Tris); r.shadowCasters = Median(m_Shadow);
            r.memUsed = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            r.memTex = (long)Texture.currentTextureMemory;
            long meshMem = 0; foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) meshMem += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m); r.memMesh = meshMem;

            foreach (var kv in acc)
            {
                var c = new Cost { name = kv.Key, group = Group.Of(kv.Key), path = kv.Value.path, msPerFrame = (float)(kv.Value.self / n), gcPerFrame = (float)(kv.Value.gc / n), calls = (float)(kv.Value.calls / n), worstFrame = kv.Value.worstFrame, worstMs = kv.Value.worstSelf };
                r.costs.Add(c);
            }
            r.garbage = r.costs.Where(c => c.gcPerFrame > 0).OrderByDescending(c => c.gcPerFrame).ToList();
            r.gcPerFrame = r.garbage.Sum(c => c.gcPerFrame);
            r.groups = r.costs.GroupBy(c => c.group).Select(g => new GroupCost { name = g.Key, ms = g.Sum(c => c.msPerFrame) }).Where(g => g.ms > 0.001f && g.name != "Profiler").OrderByDescending(g => g.ms).ToList();
            if (r.editorMs > 0.05f) r.groups.Add(new GroupCost { name = "Editor (not in builds)", ms = r.editorMs });
            r.costs = r.costs.Where(c => c.group != "Waiting" && c.group != "Profiler").OrderByDescending(c => c.msPerFrame).ToList();

            // hitches: frames well above the usual one
            var gameTimes = frameMs.Select(x => x.game).OrderBy(x => x).ToList();
            float gameMedian = gameTimes[gameTimes.Count / 2];
            float spikeAt = Mathf.Max(gameMedian * 2.5f, gameMedian + 8f);
            var t0 = frameMs.Count > 0 ? frameMs[0].frame : 0;
            float clock = 0; var timeAt = new Dictionary<int, float>();
            foreach (var fm in frameMs) { timeAt[fm.frame] = clock / 1000f; clock += fm.ms; }
            foreach (var fm in frameMs.Where(x => x.game > spikeAt).OrderByDescending(x => x.game))
            {
                var s = new Spike { frame = fm.frame, ms = fm.game, time = timeAt[fm.frame] };
                if (perFrameTop.TryGetValue(fm.frame, out var top))
                    foreach (var t in top.Where(t => Group.Of(t.name) != "Waiting" && Group.Of(t.name) != "Profiler").OrderByDescending(t => t.self).Take(4))
                        s.top.Add(new Cost { name = t.name, group = Group.Of(t.name), msPerFrame = t.self });
                r.spikes.Add(s);
            }

            Diagnose(r);
            r.text = Text(r);
            return r;
        }

        // adds each item's own (self) time under its name; remembers where it sits and its worst frame
        void Walk(HierarchyFrameDataView view, int id, string name, int depth, int frame, Dictionary<string, Acc> acc, List<(string, float)> top, string path = "")
        {
            float self = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            float calls = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
            if (!acc.TryGetValue(name, out var a)) { a = new Acc { path = path }; acc[name] = a; }
            a.self += self; a.calls += calls;
            if (self > a.worstSelf) { a.worstSelf = self; a.worstFrame = frame; }
            if (self > 0.05f) top.Add((name, self));
            var kids = new List<int>();
            view.GetItemChildren(id, kids);
            if (kids.Count == 0 || depth > 40) return;
            string childPath = depth < 5 ? (path.Length > 0 ? path + " › " : "") + name : path;
            foreach (int k in kids)
            {
                string kn = view.GetItemName(k);
                // garbage belongs to the code that made it: the parent of the GC.Alloc sample
                if (kn == "GC.Alloc") a.gc += view.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnGcMemory);
                Walk(view, k, Pretty(kn), depth + 1, frame, acc, top, childPath);
            }
        }

        static int Median(List<int> l) => l.Count == 0 ? 0 : l.OrderBy(x => x).ElementAt(l.Count / 2);

        void Diagnose(Result r)
        {
            float budget = 1000f / m_TargetFps;
            float fps = 1000f / Mathf.Max(0.01f, r.avgMs);
            bool gpuBound = r.gpuMs > 0 && r.gpuMs > r.gameMs * 0.9f && r.gpuMs > budget * 0.8f;
            r.verdict = fps.ToString("0") + " fps" + (r.avgMs <= budget * 1.05f ? " - smooth" : r.avgMs <= budget * 1.6f ? " - a bit slow" : " - slow");
            r.verdictDetail = "A frame takes " + r.avgMs.ToString("0.0") + " ms on average; " + m_TargetFps + " fps needs " + budget.ToString("0.0") + " ms. " +
                (r.gpuMs > 0 ? (gpuBound ? "The <b>graphics card</b> is the limit (it needs " + r.gpuMs.ToString("0.0") + " ms a frame): fewer pixels, cheaper shaders, fewer shadows or effects help most." :
                                           "The <b>CPU</b> (code and engine work, " + r.gameMs.ToString("0.0") + " ms) is the bigger cost; the graphics card needs " + r.gpuMs.ToString("0.0") + " ms.") : "") +
                (r.editorMs > 1f ? " The editor itself adds " + r.editorMs.ToString("0.0") + " ms that a build won't have." : "") +
                (r.spikes.Count > 0 ? " " + r.spikes.Count + " hitches (stutters) in the recording." : " No hitches.");

            // the biggest real costs
            foreach (var c in r.costs.Take(6))
            {
                if (c.msPerFrame < Mathf.Max(0.3f, budget * 0.06f)) break;
                r.findings.Add(new Finding
                {
                    bad = c.msPerFrame > budget * 0.25f,
                    title = c.name + ": " + c.msPerFrame.ToString("0.0") + " ms a frame (" + (100f * c.msPerFrame / Mathf.Max(0.01f, r.avgMs)).ToString("0") + "% of the frame)",
                    detail = (Group.What(c.name) ?? Group.Explain(c.group)) + (string.IsNullOrEmpty(c.path) ? "" : "\n<color=#999999>in " + c.path + "</color>"),
                    marker = c.name, frame = c.worstFrame,
                });
            }
            if (r.spikes.Count > 0)
            {
                var worst = r.spikes[0];
                var causes = r.spikes.SelectMany(s => s.top.Take(1)).GroupBy(c => c.name).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key + (g.Count() > 1 ? " (" + g.Count() + "x)" : ""));
                r.findings.Add(new Finding
                {
                    bad = worst.ms > 50, frame = worst.frame, marker = worst.top.Count > 0 ? worst.top[0].name : null,
                    title = r.spikes.Count + " hitch" + (r.spikes.Count > 1 ? "es" : "") + ", the worst " + worst.ms.ToString("0") + " ms",
                    detail = "Mostly caused by: " + string.Join(", ", causes) + ". " + (worst.top.Count > 0 ? Group.What(worst.top[0].name) ?? Group.Explain(worst.top[0].group) : ""),
                });
            }
            if (r.gcPerFrame > 1024)
            {
                var g = r.garbage[0];
                r.findings.Add(new Finding
                {
                    bad = r.gcPerFrame > 20 * 1024, marker = g.name, frame = g.worstFrame,
                    title = "Garbage: " + Kb(r.gcPerFrame) + " thrown away every frame",
                    detail = "Most from " + g.name + " (" + Kb(g.gcPerFrame) + "). Creating lists, strings, arrays or 'new' objects every frame fills memory; the clean-up then freezes the game for a moment. Reuse them instead (keep a list and Clear() it).",
                });
            }
            if (r.batches > 1500 || r.setPass > 250)
                r.findings.Add(new Finding { bad = r.batches > 3000, title = r.batches + " batches, " + r.setPass + " SetPass calls a frame", detail = "Lots of separate draws. Fewer different materials, GPU instancing, static batching, or merging small meshes helps." });
            if (r.tris > 3000000)
                r.findings.Add(new Finding { bad = r.tris > 8000000, title = (r.tris / 1e6f).ToString("0.0") + " million triangles a frame", detail = "Very detailed meshes (or many of them). Use LODs or lighter meshes for things far away." });
            if (r.editorMs > r.gameMs && r.editorMs > 4f)
                r.findings.Add(new Finding { bad = false, title = "Most of the time is the editor itself (" + r.editorMs.ToString("0.0") + " ms)", detail = "The Inspector, Scene view and other open windows redraw every frame. Close or hide them while testing, or profile a development build to see the real numbers." });
            r.findings = r.findings.OrderByDescending(f => f.bad).ToList();
        }

        string Text(Result r)
        {
            var t = new StringBuilder();
            t.AppendLine("Perf Doctor report  (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ", " + Application.productName + ", aim " + m_TargetFps + " fps)");
            t.AppendLine(r.verdict.Replace("<b>", "").Replace("</b>", ""));
            t.AppendLine(System.Text.RegularExpressions.Regex.Replace(r.verdictDetail, "<.*?>", ""));
            t.AppendLine();
            t.AppendLine("Frames " + r.frames + ", avg " + r.avgMs.ToString("0.0") + " ms, median " + r.medianMs.ToString("0.0") + ", worst " + r.worstMs.ToString("0.0") + ", game " + r.gameMs.ToString("0.0") + ", editor " + r.editorMs.ToString("0.0") + ", GPU " + (r.gpuMs > 0 ? r.gpuMs.ToString("0.0") : "n/a"));
            t.AppendLine("Batches " + r.batches + ", SetPass " + r.setPass + ", tris " + r.tris + ", shadow casters " + r.shadowCasters + ", garbage/frame " + Kb(r.gcPerFrame) + ", memory " + Mb(r.memUsed));
            t.AppendLine();
            t.AppendLine("By group:"); foreach (var g in r.groups) t.AppendLine("  " + g.ms.ToString("0.00").PadLeft(7) + " ms  " + g.name);
            t.AppendLine("Findings:"); foreach (var f in r.findings) t.AppendLine("  - " + f.title + ": " + System.Text.RegularExpressions.Regex.Replace(f.detail, "<.*?>", "").Replace("\n", " "));
            t.AppendLine("Top costs (self ms/frame):"); foreach (var c in r.costs.Take(25)) t.AppendLine("  " + c.msPerFrame.ToString("0.000").PadLeft(8) + "  " + c.name + "  [" + c.group + "]  " + c.path);
            t.AppendLine("Garbage:"); foreach (var c in r.garbage.Take(10)) t.AppendLine("  " + Kb(c.gcPerFrame).PadLeft(9) + "  " + c.name);
            t.AppendLine("Hitches:"); foreach (var s in r.spikes.Take(10)) t.AppendLine("  " + s.ms.ToString("0.0").PadLeft(7) + " ms at " + s.time.ToString("0.0") + " s: " + string.Join(", ", s.top.Select(x => x.name + " " + x.msPerFrame.ToString("0.0"))));
            return t.ToString();
        }
    }

    /// <summary>What a profiler item is, in words, and which group it belongs to.</summary>
    static class Group
    {
        static readonly (string match, string group, string what)[] Known =
        {
            ("WaitForTargetFPS", "Waiting", "Idle time: the game is faster than the frame-rate cap (vsync / targetFrameRate) and waits. Not a problem."),
            ("Gfx.WaitForPresent", "Waiting", "The CPU waits for the graphics card to finish the last frame: the GPU is the limit."),
            ("Gfx.WaitForGfxCommandsFromMainThread", "Waiting", "The render thread waits for the main thread: the main thread is the limit."),
            ("Gfx.WaitFor", "Waiting", "Waiting for the graphics card."),
            ("Semaphore.WaitForSignal", "Waiting", "A thread waiting for another one to finish."),
            ("WaitForJobGroupID", "Waiting", "Waiting for worker threads (jobs) to finish."),
            ("Idle", "Waiting", "Nothing to do."),
            ("Profiler.", "Profiler", "The profiler's own cost while recording."),
            ("GC.Collect", "Garbage collection", "The memory cleaner running: it freezes the game while it frees the garbage made earlier. Make less garbage (see the Garbage tab)."),
            ("GC.Alloc", "Garbage collection", "Making new memory (garbage later)."),
            ("Shader.CreateGPUProgram", "Loading", "A shader being prepared the first time it's drawn: a one-off hitch. Warm shaders up at the start (ShaderVariantCollection.WarmUp) to avoid it."),
            ("Shader.Parse", "Loading", "Loading a shader the first time it's used: a one-off hitch."),
            ("CreateGPUProgram", "Loading", "A shader being prepared the first time it's drawn: a one-off hitch."),
            ("Loading.", "Loading", "Loading assets from disk."),
            ("Instantiate", "Scripts", "Creating copies of objects (Instantiate). Pooling (reusing objects) avoids it."),
            ("Destroy", "Scripts", "Destroying objects."),
            ("Mesh.", "Rendering", "Uploading or changing a mesh (procedural meshes rebuilt every frame are costly)."),
            ("Texture2D.Apply", "Rendering", "Uploading a texture to the graphics card: costly if done every frame."),
            ("ReadPixels", "Rendering", "Reading pixels back from the graphics card: it stalls until the GPU catches up."),
            ("Shadows.", "Rendering", "Drawing shadow maps. Shorter shadow distance, fewer cascades or fewer shadow-casting lights help."),
            ("RenderShadowMap", "Rendering", "Drawing shadow maps. Shorter shadow distance, fewer cascades or fewer shadow-casting lights help."),
            ("MainLightShadow", "Rendering", "Drawing the sun's shadow map."),
            ("Culling", "Rendering", "Working out what's visible to each camera."),
            ("DrawOpaqueObjects", "Rendering", "Drawing the solid objects."),
            ("DrawTransparentObjects", "Rendering", "Drawing the see-through objects (water, glass, particles)."),
            ("RenderLoop", "Rendering", "Drawing objects."),
            ("Camera.Render", "Rendering", "A camera drawing the scene (each extra camera costs about as much again)."),
            ("Inl_", "Rendering", "URP drawing a part of the frame."),
            ("UniversalRenderPipeline", "Rendering", "URP preparing and drawing the frame."),
            ("RenderGraph", "Rendering", "URP preparing and drawing the frame."),
            ("PostProcess", "Rendering", "Post-processing effects (bloom, colour grading...)."),
            ("UberPost", "Rendering", "Post-processing effects."),
            ("Gfx.", "Rendering", "Sending work to the graphics card."),
            ("Batch", "Rendering", "Grouping draws together."),
            ("SRPBatcher", "Rendering", "URP's batching of draws."),
            ("Graphics.", "Rendering", "Drawing meshes from code."),
            ("Render", "Rendering", "Drawing."),
            ("SSAO", "Rendering", "Ambient occlusion (soft contact shadows), a screen effect on the renderer. Turning it off or lowering its quality saves GPU time."),
            ("Bloom", "Rendering", "The bloom glow effect."),
            ("Mono.JIT", "Loading", "Code being compiled the first time it runs: a one-off hitch (builds with IL2CPP don't have it)."),
            ("Physics", "Physics", "The physics simulation. Fewer rigidbodies/colliders, simpler colliders, or a larger fixed timestep help."),
            ("FixedUpdate", "Physics", "Fixed-step updates (physics rate). Many fixed steps in one frame mean the game fell behind."),
            ("Rigidbody", "Physics", "Rigidbody work."),
            ("UIElements", "UI", "The UI Toolkit interface: layout and drawing. Big or often-changing UI costs more."),
            ("UIR.", "UI", "Drawing the UI Toolkit interface."),
            ("Layout", "UI", "Laying out UI elements (sizes and positions)."),
            ("Canvas", "UI", "uGUI canvases being rebuilt or drawn: split often-changing UI onto its own canvas."),
            ("UGUI", "UI", "uGUI interface work."),
            ("TextMeshPro", "UI", "Text layout."),
            ("Animator", "Animation", "Animators. Fewer animated objects, culling off-screen animators, or simpler controllers help."),
            ("Animation", "Animation", "Animation."),
            ("Director", "Animation", "Timeline / Playables."),
            ("Particle", "Effects", "Particle systems."),
            ("VFX", "Effects", "Visual effects."),
            ("Audio", "Audio", "Sound."),
            ("BehaviourUpdate", "Scripts", "Your scripts' Update() calls."),
            ("LateBehaviourUpdate", "Scripts", "Your scripts' LateUpdate() calls."),
            ("Update()", "Scripts", "A script's Update(): the code in it runs every frame."),
            ("LateUpdate()", "Scripts", "A script's LateUpdate(): the code in it runs every frame."),
            ("Coroutine", "Scripts", "Coroutines."),
            ("Input", "Input", "Reading the keyboard, mouse and controllers."),
            ("PlayerLoop", "Engine", "The engine's frame loop itself."),
        };

        public static string Of(string name)
        {
            foreach (var k in Known) if (name.Contains(k.match)) return k.group;
            // a named marker from game code ("FishPond.DrawFish", "MyScript.Update()"): scripts
            if (name.Contains(".") && char.IsUpper(name[0])) return "Scripts";
            return "Engine";
        }

        public static string What(string name)
        {
            foreach (var k in Known) if (name.Contains(k.match)) return k.what;
            if (name.Contains(".") && char.IsUpper(name[0])) return "Your code (" + name.Split('.')[0] + "): the time is spent in that script's own work.";
            return null;
        }

        public static string Explain(string group)
        {
            switch (group)
            {
                case "Scripts": return "Your game code (Update, coroutines, your own markers).";
                case "Rendering": return "Preparing and drawing the frame on the CPU side.";
                case "Physics": return "Collisions and rigidbodies.";
                case "UI": return "Menus and on-screen interface.";
                case "Animation": return "Animators and timelines.";
                case "Garbage collection": return "Freeing memory that was thrown away.";
                case "Loading": return "Loading or preparing assets and shaders (one-off hitches).";
                case "Waiting": return "Not work: waiting for vsync, the GPU or another thread.";
                case "Effects": return "Particles and visual effects.";
                case "Audio": return "Sound.";
                case "Input": return "Reading input devices.";
                case "Profiler": return "The profiler's own cost.";
                case "Editor (not in builds)": return "The Unity editor's windows; a build doesn't pay this.";
                default: return "Engine work.";
            }
        }

        public static Color Colour(string group)
        {
            switch (group)
            {
                case "Scripts": return new Color(0.35f, 0.65f, 0.95f);
                case "Rendering": return new Color(0.4f, 0.8f, 0.45f);
                case "Physics": return new Color(0.95f, 0.6f, 0.25f);
                case "UI": return new Color(0.8f, 0.5f, 0.95f);
                case "Animation": return new Color(0.95f, 0.85f, 0.35f);
                case "Garbage collection": return new Color(0.95f, 0.4f, 0.35f);
                case "Loading": return new Color(0.9f, 0.45f, 0.6f);
                case "Waiting": return new Color(0.45f, 0.45f, 0.48f);
                case "Editor (not in builds)": return new Color(0.3f, 0.3f, 0.33f);
                default: return new Color(0.55f, 0.7f, 0.75f);
            }
        }
    }
}
