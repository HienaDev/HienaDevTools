using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Cut sounds without leaving Unity. Tools > Audio Trimmer (or right-click a sound > Trim Audio).
    /// Shows the waveform; drag on it to pick the part you want, play / pause / loop it, add fades, change the
    /// volume, then save it as a new .wav next to the original (or overwrite a .wav).
    /// Keys: Space play/pause, Home go to start, Enter play the selection.
    /// </summary>
    public class AudioTrimmer : EditorWindow
    {
        AudioClip clip;
        float[] samples; // interleaved
        int channels = 1, rate = 44100, frames;
        string loadError;

        // the part being kept, in seconds (saved with the window, so Ctrl+Z can undo changes)
        [SerializeField] float selStart, selEnd;
        [SerializeField] float fadeIn, fadeOut, gainDb;
        [SerializeField] float pitch;    // semitones (changes speed too, like a tape)
        [SerializeField] float loopFade; // seconds of crossfade for a seamless loop, 0 = off
        [SerializeField] List<Region> regions = new List<Region>();
        [SerializeField] int activeRegion = -1;
        // finding sounds
        float threshDb = -40f, minGap = 0.08f, minLen = 0.03f, pad = 0.01f;
        bool showEffects = true, showLoop, showFind, showRegions = true;
        Vector2 scroll;
        // the name the trimmed sound is saved as (without .wav)
        string saveName = "";
        bool loop = true, playSelectionOnly = true;
        [SerializeField] bool normalize;
        // the view (zoom), in seconds
        float viewStart, viewEnd;

        // playback
        GameObject player;
        AudioSource source;
        AudioClip previewClip;
        bool previewDirty = true, paused, playing;
        float playFrom; // seconds in the original where the preview starts
        float cursor;   // the playhead when stopped

        float[] colLo, colHi; // the peak of each pixel column on screen
        float waveFrom = -1f, waveTo = -1f;
        // peaks of every block of frames, so the waveform is quick to draw at any zoom
        const int SmallBlock = 64, BigBlock = 4096;
        float[] smallLo, smallHi, bigLo, bigHi;
        double restartAt = -1; // a change to the volume / fades waits a moment before rebuilding the preview
        float lastDrawnHead = -1f;
        Material lineMat;
        enum Drag { None, Start, End, Make, Seek, Pan }
        Drag drag;
        float dragAnchor;

        [MenuItem("Tools/Audio Trimmer")]
        public static void Open() => Open(Selection.activeObject as AudioClip);

        [MenuItem("Assets/Trim Audio", false, 2000)]
        static void OpenFromAsset() => Open(Selection.activeObject as AudioClip);

        [MenuItem("Assets/Trim Audio", true)]
        static bool CanOpenFromAsset() => Selection.activeObject is AudioClip;

        public static void Open(AudioClip c)
        {
            var w = GetWindow<AudioTrimmer>("Audio Trimmer");
            w.minSize = new Vector2(560, 360);
            if (c) w.Load(c);
        }

        void OnEnable()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Stop();
            if (player) DestroyImmediate(player);
            if (previewClip) DestroyImmediate(previewClip);
            if (lineMat) DestroyImmediate(lineMat);
        }

        // ------------------------------------------------------------------ loading

        void Load(AudioClip c)
        {
            Stop();
            clip = c;
            samples = null;
            loadError = null;
            previewDirty = true;
            if (!clip) return;
            string path = AssetDatabase.GetAssetPath(clip);
            try
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && ReadWav(File.ReadAllBytes(path), out samples, out channels, out rate)) { }
                else
                {
                    channels = clip.channels;
                    rate = clip.frequency;
                    samples = new float[clip.samples * clip.channels];
                    if (!clip.GetData(samples, 0)) { samples = null; loadError = "Unity can't read this sound's samples."; }
                }
            }
            catch (Exception e) { samples = null; loadError = e.Message; }
            if (samples == null)
            {
                loadError = (loadError ?? "") + " Set its Load Type to \"Decompress On Load\" (button below) and try again.";
                return;
            }
            frames = samples.Length / Mathf.Max(1, channels);
            selStart = 0f;
            selEnd = Length;
            viewStart = 0f;
            viewEnd = Length;
            cursor = 0f;
            fadeIn = fadeOut = 0f;
            gainDb = 0f;
            pitch = loopFade = 0f;
            regions.Clear();
            activeRegion = -1;
            Undo.ClearUndo(this);
            saveName = Path.GetFileNameWithoutExtension(path) + (Path.GetFileNameWithoutExtension(path).EndsWith("_trim") ? "" : "_trim");
            normalize = false;
            BuildPeaks();
            waveFrom = waveTo = -1f; // redraw
        }

        void BuildPeaks()
        {
            int ns = (frames + SmallBlock - 1) / SmallBlock;
            smallLo = new float[ns]; smallHi = new float[ns];
            for (int b = 0; b < ns; b++)
            {
                float lo = 0f, hi = 0f;
                int end = Mathf.Min(frames, (b + 1) * SmallBlock) * channels;
                for (int i = b * SmallBlock * channels; i < end; i++)
                {
                    float v = samples[i];
                    if (v < lo) lo = v; else if (v > hi) hi = v;
                }
                smallLo[b] = lo; smallHi[b] = hi;
            }
            int per = BigBlock / SmallBlock, nb = (ns + per - 1) / per;
            bigLo = new float[nb]; bigHi = new float[nb];
            for (int b = 0; b < nb; b++)
            {
                float lo = 0f, hi = 0f;
                int end = Mathf.Min(ns, (b + 1) * per);
                for (int i = b * per; i < end; i++) { if (smallLo[i] < lo) lo = smallLo[i]; if (smallHi[i] > hi) hi = smallHi[i]; }
                bigLo[b] = lo; bigHi[b] = hi;
            }
        }

        /// <summary>The lowest and highest sample in frames [a, b).</summary>
        void Peak(int a, int b, out float lo, out float hi)
        {
            lo = 0f; hi = 0f;
            if (b <= a) return;
            int n = b - a;
            if (n >= BigBlock * 2)
            {
                for (int i = a / BigBlock, e = Mathf.Min(bigLo.Length, (b + BigBlock - 1) / BigBlock); i < e; i++) { if (bigLo[i] < lo) lo = bigLo[i]; if (bigHi[i] > hi) hi = bigHi[i]; }
            }
            else if (n >= SmallBlock * 2)
            {
                for (int i = a / SmallBlock, e = Mathf.Min(smallLo.Length, (b + SmallBlock - 1) / SmallBlock); i < e; i++) { if (smallLo[i] < lo) lo = smallLo[i]; if (smallHi[i] > hi) hi = smallHi[i]; }
            }
            else
            {
                for (int i = a * channels, e = Mathf.Min(frames, b) * channels; i < e; i++)
                {
                    float v = samples[i];
                    if (v < lo) lo = v; else if (v > hi) hi = v;
                }
            }
        }

        float Length => frames / (float)Mathf.Max(1, rate);

        // ------------------------------------------------------------------ the window

        void OnGUI()
        {
            HandleKeys();
            // every change made in this window can be undone with Ctrl+Z (Unity groups a whole drag into one step)
            if (samples != null) Undo.RecordObject(this, "Audio Trimmer");
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var c = (AudioClip)EditorGUILayout.ObjectField("Sound", clip, typeof(AudioClip), false);
                if (c != clip) Load(c);
                if (GUILayout.Button("Use selected", GUILayout.Width(100)) && Selection.activeObject is AudioClip sel) Load(sel);
            }
            if (!clip) { EditorGUILayout.HelpBox("Pick a sound above, or right-click a sound in the Project window > Trim Audio.", MessageType.Info); return; }
            if (samples == null)
            {
                EditorGUILayout.HelpBox(loadError, MessageType.Warning);
                if (GUILayout.Button("Set Load Type to Decompress On Load")) MakeReadable();
                return;
            }

            // the waveform, the regions strip and the timeline
            var area = GUILayoutUtility.GetRect(100, 10000, 190, 320);
            var ruler = new Rect(area.x, area.y, area.width, 18);
            var strip = new Rect(area.x, area.y + 18, area.width, 16);
            var waveRect = new Rect(area.x, area.y + 34, area.width, area.height - 34);
            DrawRuler(ruler);
            DrawStrip(strip);
            DrawWave(waveRect);
            HandleMouse(waveRect, ruler, strip);

            // zoom
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("View", GUILayout.Width(40));
                float a = viewStart, b = viewEnd;
                EditorGUILayout.MinMaxSlider(ref a, ref b, 0f, Length);
                if (b - a > 0.01f) { viewStart = a; viewEnd = b; }
                if (GUILayout.Button("Zoom to selection", GUILayout.Width(130))) { viewStart = selStart; viewEnd = selEnd; }
                if (GUILayout.Button("All", GUILayout.Width(40))) { viewStart = 0f; viewEnd = Length; }
            }

            // transport
            using (new EditorGUILayout.HorizontalScope())
            {
                bool isPlaying = source && source.isPlaying;
                if (GUILayout.Button(isPlaying ? "❚❚ Pause" : (paused ? "▶ Resume" : "▶ Play"), GUILayout.Height(28), GUILayout.Width(110))) TogglePlay();
                if (GUILayout.Button("■ Stop", GUILayout.Height(28), GUILayout.Width(80))) { Stop(); cursor = playSelectionOnly ? selStart : 0f; }
                if (GUILayout.Button("⏮ Start", GUILayout.Height(28), GUILayout.Width(80))) { Stop(); cursor = selStart; }
                loop = GUILayout.Toggle(loop, " Loop", GUILayout.Width(60));
                bool only = GUILayout.Toggle(playSelectionOnly, " Play only the selection", GUILayout.Width(180));
                if (only != playSelectionOnly) { playSelectionOnly = only; Stop(); }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("↶ Undo", "Ctrl+Z"), GUILayout.Width(64))) Undo.PerformUndo();
                if (GUILayout.Button(new GUIContent("↷ Redo", "Ctrl+Y"), GUILayout.Width(64))) Undo.PerformRedo();
                if (GUILayout.Button(new GUIContent("Reset", "Back to the whole sound with no changes (can be undone)."), GUILayout.Width(52))) ResetAll();
                GUILayout.Label($"{Fmt(Playhead())} / {Fmt(Length)}", EditorStyles.boldLabel, GUILayout.Width(130));
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            // selection
            using (new EditorGUILayout.HorizontalScope())
            {
                selStart = Mathf.Clamp(EditorGUILayout.FloatField("Start (s)", selStart), 0f, Length);
                selEnd = Mathf.Clamp(EditorGUILayout.FloatField("End (s)", selEnd), 0f, Length);
                if (selEnd < selStart + 0.005f) selEnd = Mathf.Min(Length, selStart + 0.005f);
                GUILayout.Label($"length {Fmt(selEnd - selStart)}", GUILayout.Width(110));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Snap edges to zero", "Moves the start and end to the nearest point where the wave crosses zero, so there's no click."))) SnapToZero();
                if (GUILayout.Button(new GUIContent("Trim silence", "Cuts the quiet bits off both ends of the selection (uses the Quiet below level from Find sounds)."))) TrimSilence();
            }

            // effects
            showEffects = EditorGUILayout.Foldout(showEffects, "Volume, fades and pitch", true);
            if (showEffects)
            {
                EditorGUI.indentLevel++;
                float maxFade = Mathf.Max(0f, (selEnd - selStart) * 0.5f);
                using (new EditorGUI.DisabledScope(loopFade > 0f))
                {
                    fadeIn = EditorGUILayout.Slider("Fade in (s)", fadeIn, 0f, maxFade);
                    fadeOut = EditorGUILayout.Slider("Fade out (s)", fadeOut, 0f, maxFade);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    gainDb = EditorGUILayout.Slider("Volume (dB)", gainDb, -24f, 24f);
                    normalize = GUILayout.Toggle(normalize, " Normalize (as loud as it goes)", GUILayout.Width(220));
                }
                pitch = EditorGUILayout.Slider(new GUIContent("Pitch (semitones)", "Higher is faster and shorter, lower is slower and longer, like speeding up a tape. 12 = an octave."), pitch, -12f, 12f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    float speed = Speed;
                    float ns = EditorGUILayout.Slider(new GUIContent("Speed", "The same thing as Pitch, as a speed (2 = twice as fast and an octave higher)."), speed, 0.5f, 2f);
                    if (!Mathf.Approximately(ns, speed)) pitch = Mathf.Clamp(12f * Mathf.Log(ns, 2f), -12f, 12f);
                    if (GUILayout.Button("Random ±2", GUILayout.Width(80))) pitch = UnityEngine.Random.Range(-2f, 2f);
                }
                EditorGUI.indentLevel--;
            }

            // seamless loop
            showLoop = EditorGUILayout.Foldout(showLoop, "Make a seamless loop", true);
            if (showLoop)
            {
                EditorGUI.indentLevel++;
                float maxLoop = Mathf.Max(0f, Mathf.Min(4f, (selEnd - selStart) * 0.45f));
                loopFade = EditorGUILayout.Slider(new GUIContent("Crossfade (s)", "Blends the end of the selection into its start, so it loops without a jump. 0 = off. The saved sound is shorter by this much."), loopFade, 0f, maxLoop);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Find a good loop", "Nudges the end to where the sound best matches its start, and sets a crossfade."))) FindLoop();
                    if (GUILayout.Button("Listen to the loop")) { loop = true; playSelectionOnly = true; Stop(); previewDirty = true; Play(selStart); }
                }
                EditorGUILayout.LabelField(loopFade > 0f ? "Loop on: fades are off, and Save writes the looping version." : "Pick the part that loops, then Find a good loop (or set a crossfade yourself).", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }

            // finding sounds in a file of many
            showFind = EditorGUILayout.Foldout(showFind, "Find sounds (a file with many, like footsteps)", true);
            if (showFind)
            {
                EditorGUI.indentLevel++;
                threshDb = EditorGUILayout.Slider(new GUIContent("Quiet below (dB)", "Anything quieter than this, compared to the loudest part, counts as silence. Lower finds softer sounds."), threshDb, -70f, -6f);
                minGap = EditorGUILayout.Slider(new GUIContent("Silence between (s)", "How long a quiet gap has to be to split two sounds."), minGap, 0.01f, 1f);
                minLen = EditorGUILayout.Slider(new GUIContent("Shortest sound (s)", "Blips shorter than this are ignored."), minLen, 0.005f, 0.5f);
                pad = EditorGUILayout.Slider(new GUIContent("Padding (s)", "A little extra kept before and after each sound."), pad, 0f, 0.2f);
                if (GUILayout.Button("Find sounds", GUILayout.Height(24))) FindSounds();
                EditorGUI.indentLevel--;
            }
            bool changed = EditorGUI.EndChangeCheck();

            // regions
            showRegions = EditorGUILayout.Foldout(showRegions, $"Regions ({regions.Count})", true);
            if (showRegions)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Add selection as region", "Key A"))) AddRegion();
                    using (new EditorGUI.DisabledScope(regions.Count == 0))
                    {
                        if (GUILayout.Button("Save all regions", GUILayout.Width(130))) SaveRegions();
                        if (GUILayout.Button("Clear", GUILayout.Width(60))) { regions.Clear(); activeRegion = -1; }
                    }
                }
                int remove = -1;
                for (int i = 0; i < regions.Count; i++)
                {
                    var rg = regions[i];
                    using (new EditorGUILayout.HorizontalScope(i == activeRegion ? EditorStyles.helpBox : GUIStyle.none))
                    {
                        GUILayout.Label((i + 1).ToString(), GUILayout.Width(22));
                        rg.name = EditorGUILayout.TextField(rg.name);
                        GUILayout.Label($"{Fmt(rg.start)} – {Fmt(rg.end)}", EditorStyles.miniLabel, GUILayout.Width(130));
                        if (GUILayout.Button("Select", GUILayout.Width(52))) SelectRegion(i, false);
                        if (GUILayout.Button("▶", GUILayout.Width(26))) SelectRegion(i, true);
                        if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
                    }
                }
                if (remove >= 0) { regions.RemoveAt(remove); if (activeRegion == remove) activeRegion = -1; else if (activeRegion > remove) activeRegion--; }
            }
            EditorGUILayout.EndScrollView();

            if (changed)
            {
                previewDirty = true;
                if (source && source.isPlaying) restartAt = EditorApplication.timeSinceStartup + 0.15; // wait until the slider settles
            }
            // the picked region follows its edges
            if (activeRegion >= 0 && activeRegion < regions.Count) { regions[activeRegion].start = selStart; regions[activeRegion].end = selEnd; }

            // saving
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                saveName = EditorGUILayout.TextField(new GUIContent("Name", "What the trimmed sound is called (saved as a .wav next to the original). Rename changes the name of the sound that's open now."), saveName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(saveName) || CleanName(saveName) == Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip))))
                    if (GUILayout.Button("Rename this sound", GUILayout.Width(130))) Rename();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save as new .wav", GUILayout.Height(28))) Save(false);
                bool isWav = AssetDatabase.GetAssetPath(clip).EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
                using (new EditorGUI.DisabledScope(!isWav))
                    if (GUILayout.Button(isWav ? "Overwrite this .wav" : "Overwrite (only .wav files)", GUILayout.Height(28))
                        && EditorUtility.DisplayDialog("Overwrite?", $"Replace {Path.GetFileName(AssetDatabase.GetAssetPath(clip))} with the trimmed version? This can't be undone.", "Overwrite", "Cancel"))
                        Save(true);
            }
            EditorGUILayout.LabelField("Drag on the waveform to pick a part, drag its edges to change it. Click the ruler to move the playhead, click a region in the strip under it to pick it. Middle-drag or Alt-drag pans, the wheel zooms. Space: play/pause, A: add region, Ctrl+Z: undo.", EditorStyles.wordWrappedMiniLabel);
        }

        // ------------------------------------------------------------------ regions

        [Serializable]
        class Region { public float start, end; public string name; }

        string BaseName => clip ? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip)) : "sound";

        void AddRegion()
        {
            regions.Add(new Region { start = selStart, end = selEnd, name = $"{BaseName}_{regions.Count + 1:00}" });
            regions.Sort((a, b) => a.start.CompareTo(b.start));
            activeRegion = regions.FindIndex(r => Mathf.Approximately(r.start, selStart) && Mathf.Approximately(r.end, selEnd));
            previewDirty = true;
        }

        void SelectRegion(int i, bool play)
        {
            if (i < 0 || i >= regions.Count) return;
            activeRegion = i;
            selStart = regions[i].start;
            selEnd = regions[i].end;
            previewDirty = true;
            Stop();
            cursor = selStart;
            if (selStart < viewStart || selEnd > viewEnd)
            {
                float span = Mathf.Max(viewEnd - viewStart, (selEnd - selStart) * 1.5f);
                viewStart = Mathf.Clamp((selStart + selEnd) * 0.5f - span * 0.5f, 0f, Mathf.Max(0f, Length - span));
                viewEnd = Mathf.Min(Length, viewStart + span);
            }
            if (play) { playSelectionOnly = true; Play(selStart); }
        }

        void DrawStrip(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, new Color(0.13f, 0.13f, 0.15f));
            for (int i = 0; i < regions.Count; i++)
            {
                float x0 = Mathf.Max(r.x, X(regions[i].start, r)), x1 = Mathf.Min(r.xMax, X(regions[i].end, r));
                if (x1 < r.x || x0 > r.xMax) continue;
                var col = i == activeRegion ? new Color(1f, 0.75f, 0.3f) : (i % 2 == 0 ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.55f, 0.45f, 0.95f));
                EditorGUI.DrawRect(new Rect(x0, r.y + 2, Mathf.Max(2f, x1 - x0), r.height - 4), col);
                if (x1 - x0 > 14) GUI.Label(new Rect(x0 + 2, r.y, x1 - x0, r.height), (i + 1).ToString(), EditorStyles.miniBoldLabel);
            }
        }

        void SaveRegions()
        {
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip));
            var targets = new List<string>();
            for (int i = 0; i < regions.Count; i++)
            {
                string n = CleanName(regions[i].name);
                if (string.IsNullOrEmpty(n)) n = $"{BaseName}_{i + 1:00}";
                targets.Add(Path.Combine(dir, n + ".wav").Replace('\\', '/'));
            }
            bool keepBoth = false;
            if (targets.Any(File.Exists))
            {
                int pick = EditorUtility.DisplayDialogComplex("Names taken", "Some of these names already have a sound here.", "Replace them", "Cancel", "Keep both");
                if (pick == 1) return;
                keepBoth = pick == 2;
            }
            Stop();
            float s0 = selStart, e0 = selEnd;
            try
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    string target = keepBoth && File.Exists(targets[i]) ? AssetDatabase.GenerateUniqueAssetPath(targets[i]) : targets[i];
                    selStart = regions[i].start; selEnd = regions[i].end; // the fades are measured from these
                    File.WriteAllBytes(target, WriteWav(Render(regions[i].start, regions[i].end, true), channels, rate));
                    targets[i] = target;
                }
            }
            finally { selStart = s0; selEnd = e0; }
            foreach (var t in targets) AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate);
            var first = AssetDatabase.LoadAssetAtPath<AudioClip>(targets[0]);
            if (first) EditorGUIUtility.PingObject(first);
            Debug.Log($"[Audio Trimmer] Saved {targets.Count} sounds in {dir}: {string.Join(", ", targets.Select(Path.GetFileNameWithoutExtension))}.", first);
        }

        // ------------------------------------------------------------------ finding sounds and silence

        /// <summary>The loudest sample (any channel) in each block of about 5 ms, and how many frames a block is.</summary>
        float[] Envelope(out int block)
        {
            block = Mathf.Max(16, rate / 200);
            int n = (frames + block - 1) / block;
            var env = new float[n];
            for (int b = 0; b < n; b++)
            {
                float m = 0f;
                int end = Mathf.Min(frames, (b + 1) * block) * channels;
                for (int i = b * block * channels; i < end; i++) { float v = Mathf.Abs(samples[i]); if (v > m) m = v; }
                env[b] = m;
            }
            return env;
        }

        float QuietLevel(float[] env)
        {
            float peak = 0f;
            foreach (var v in env) peak = Mathf.Max(peak, v);
            return peak * Mathf.Pow(10f, threshDb / 20f);
        }

        /// <summary>Splits the whole file into its separate sounds (where it goes quiet in between) and makes a region of each.</summary>
        void FindSounds()
        {
            var env = Envelope(out int block);
            float quiet = QuietLevel(env);
            float bt = block / (float)rate;
            int gapBlocks = Mathf.Max(1, Mathf.RoundToInt(minGap / bt));
            var found = new List<Vector2>();
            int startB = -1, lastLoud = -1;
            for (int b = 0; b <= env.Length; b++)
            {
                bool loud = b < env.Length && env[b] > quiet;
                if (loud)
                {
                    if (startB < 0) startB = b;
                    lastLoud = b;
                }
                else if (startB >= 0 && (b - lastLoud >= gapBlocks || b == env.Length))
                {
                    found.Add(new Vector2(startB * bt, (lastLoud + 1) * bt));
                    startB = -1;
                }
            }
            found.RemoveAll(f => f.y - f.x < minLen);
            if (found.Count == 0) { EditorUtility.DisplayDialog("Find sounds", "Nothing louder than the Quiet below level. Try a lower level.", "OK"); return; }
            regions.Clear();
            for (int i = 0; i < found.Count; i++)
            {
                float s = Mathf.Max(0f, found[i].x - pad), e = Mathf.Min(Length, found[i].y + pad);
                // never overlap the neighbours' padding
                if (i > 0) s = Mathf.Max(s, (found[i - 1].y + found[i].x) * 0.5f);
                if (i < found.Count - 1) e = Mathf.Min(e, (found[i].y + found[i + 1].x) * 0.5f);
                regions.Add(new Region { start = SnapZero(s), end = SnapZero(e), name = $"{BaseName}_{i + 1:00}" });
            }
            showRegions = true;
            SelectRegion(0, false);
            viewStart = 0f; viewEnd = Length;
            Debug.Log($"[Audio Trimmer] Found {regions.Count} sounds. Rename them in the Regions list and press Save all regions.");
        }

        /// <summary>Cuts the quiet ends off the selection.</summary>
        void TrimSilence()
        {
            var env = Envelope(out int block);
            float quiet = QuietLevel(env);
            float bt = block / (float)rate;
            int b0 = Mathf.FloorToInt(selStart / bt), b1 = Mathf.Min(env.Length - 1, Mathf.CeilToInt(selEnd / bt) - 1);
            int first = -1, last = -1;
            for (int b = b0; b <= b1; b++) if (env[b] > quiet) { if (first < 0) first = b; last = b; }
            if (first < 0) return;
            selStart = SnapZero(Mathf.Max(selStart, first * bt - pad));
            selEnd = SnapZero(Mathf.Min(selEnd, (last + 1) * bt + pad));
            previewDirty = true;
        }

        void SnapToZero()
        {
            selStart = SnapZero(selStart);
            selEnd = SnapZero(selEnd);
            if (selEnd <= selStart) selEnd = Mathf.Min(Length, selStart + 0.005f);
            previewDirty = true;
        }

        /// <summary>The nearest time (within 10 ms) where the first channel crosses zero.</summary>
        float SnapZero(float t)
        {
            int f = Mathf.Clamp(Mathf.RoundToInt(t * rate), 1, frames - 1);
            int reach = rate / 100;
            for (int d = 0; d <= reach; d++)
            {
                foreach (int g in new[] { f - d, f + d })
                {
                    if (g < 1 || g >= frames) continue;
                    float a = samples[(g - 1) * channels], b = samples[g * channels];
                    if (a == 0f || (a < 0f) != (b < 0f)) return g / (float)rate;
                }
            }
            return t;
        }

        // ------------------------------------------------------------------ loops

        /// <summary>
        /// Looks near the end of the selection for the spot that sounds most like the start (so the seam is smooth),
        /// moves the end there and sets a crossfade.
        /// </summary>
        void FindLoop()
        {
            float len = selEnd - selStart;
            if (len < 0.1f) return;
            int s = Mathf.RoundToInt(selStart * rate);
            int win = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(0.05f, len * 0.1f) * rate), 64, 4096);
            int search = Mathf.RoundToInt(Mathf.Min(0.25f, len * 0.2f) * rate);
            int e0 = Mathf.RoundToInt(selEnd * rate);
            int best = e0;
            double bestScore = double.MaxValue;
            int step = Mathf.Max(1, search / 2000);
            for (int e = Mathf.Max(s + win * 2, e0 - search); e <= Mathf.Min(frames - win, e0 + search); e += step)
            {
                double score = 0;
                for (int i = 0; i < win; i += 2)
                {
                    float d = samples[(e + i) * channels] - samples[(s + i) * channels];
                    score += d * d;
                }
                if (score < bestScore) { bestScore = score; best = e; }
            }
            selEnd = best / (float)rate;
            loopFade = Mathf.Min(Mathf.Max(0.05f, (selEnd - selStart) * 0.15f), Mathf.Min(1.5f, (selEnd - selStart) * 0.45f));
            loop = true;
            previewDirty = true;
        }

        // ------------------------------------------------------------------ undo

        void OnUndoRedo()
        {
            if (samples == null) return;
            selStart = Mathf.Clamp(selStart, 0f, Length);
            selEnd = Mathf.Clamp(selEnd, selStart, Length);
            previewDirty = true;
            if (source && source.isPlaying) Restart();
            Repaint();
        }

        void ResetAll()
        {
            Stop();
            selStart = 0f; selEnd = Length;
            fadeIn = fadeOut = gainDb = pitch = loopFade = 0f;
            normalize = false;
            regions.Clear();
            activeRegion = -1;
            cursor = 0f;
            previewDirty = true;
        }

        float Speed => Mathf.Pow(2f, pitch / 12f);

        void DrawRuler(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, new Color(0.16f, 0.16f, 0.16f));
            float span = viewEnd - viewStart;
            float step = NiceStep(span / Mathf.Max(1f, r.width / 70f));
            for (float t = Mathf.Ceil(viewStart / step) * step; t <= viewEnd; t += step)
            {
                float x = X(t, r);
                EditorGUI.DrawRect(new Rect(x, r.yMax - 6, 1, 6), new Color(0.6f, 0.6f, 0.6f));
                GUI.Label(new Rect(x + 2, r.y, 70, r.height), Fmt(t), EditorStyles.miniLabel);
            }
        }

        static float NiceStep(float raw)
        {
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(1e-4f, raw))));
            foreach (var m in new[] { 1f, 2f, 5f, 10f }) if (p * m >= raw) return p * m;
            return p * 10f;
        }

        void DrawWave(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return; // only draw once per frame, never in the layout pass
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.12f));
            // selection
            float sx = X(selStart, r), ex = X(selEnd, r);
            EditorGUI.DrawRect(new Rect(sx, r.y, ex - sx, r.height), new Color(0.3f, 0.55f, 0.95f, 0.18f));
            DrawPeaks(r);
            // fades
            if (fadeIn > 0f) EditorGUI.DrawRect(new Rect(sx, r.y, X(selStart + fadeIn, r) - sx, 3), new Color(1f, 0.8f, 0.3f));
            if (fadeOut > 0f) { float fx = X(selEnd - fadeOut, r); EditorGUI.DrawRect(new Rect(fx, r.y, ex - fx, 3), new Color(1f, 0.8f, 0.3f)); }
            // handles
            EditorGUI.DrawRect(new Rect(sx - 1, r.y, 3, r.height), new Color(0.4f, 0.75f, 1f));
            EditorGUI.DrawRect(new Rect(ex - 1, r.y, 3, r.height), new Color(0.4f, 0.75f, 1f));
            EditorGUIUtility.AddCursorRect(new Rect(sx - 5, r.y, 10, r.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(ex - 5, r.y, 10, r.height), MouseCursor.ResizeHorizontal);
            // playhead
            float px = X(Playhead(), r);
            if (px >= r.x && px <= r.xMax) EditorGUI.DrawRect(new Rect(px, r.y - 18, 2, r.height + 18), new Color(1f, 0.35f, 0.3f));
        }

        /// <summary>
        /// The waveform as one vertical line per pixel column, straight to the GPU. Nothing is allocated and no
        /// texture is rebuilt, so zooming, panning and resizing the window stay smooth. The column peaks are only
        /// worked out again when the view or the width changes.
        /// </summary>
        void DrawPeaks(Rect r)
        {
            float ppp = EditorGUIUtility.pixelsPerPoint; // one line per real screen pixel, also on scaled screens
            int w = Mathf.Max(1, Mathf.FloorToInt(r.width * ppp));
            if (colLo == null || colLo.Length != w || waveFrom != viewStart || waveTo != viewEnd)
            {
                if (colLo == null || colLo.Length != w) { colLo = new float[w]; colHi = new float[w]; }
                waveFrom = viewStart; waveTo = viewEnd;
                int f0 = Mathf.Clamp(Mathf.FloorToInt(viewStart * rate), 0, frames - 1), f1 = Mathf.Clamp(Mathf.CeilToInt(viewEnd * rate), f0 + 1, frames);
                double per = (f1 - f0) / (double)w;
                for (int x = 0; x < w; x++)
                {
                    int a = f0 + (int)(x * per), b = Mathf.Max(a + 1, f0 + (int)((x + 1) * per));
                    Peak(a, b, out colLo[x], out colHi[x]);
                }
            }
            float mid = r.y + r.height * 0.5f, half = r.height * 0.5f - 1f;
            if (!lineMat)
            {
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                lineMat.SetInt("_ZWrite", 0);
                lineMat.SetInt("_Cull", 0);
            }
            lineMat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.27f, 0.27f, 0.31f));
            GL.Vertex3(r.x, mid, 0); GL.Vertex3(r.xMax, mid, 0);
            GL.Color(new Color(0.47f, 0.86f, 0.55f));
            for (int x = 0; x < w; x++)
            {
                float gx = r.x + (x + 0.5f) / ppp;
                float top = mid - colHi[x] * half, bottom = mid - colLo[x] * half;
                if (bottom - top < 1f) bottom = top + 1f;
                GL.Vertex3(gx, top, 0); GL.Vertex3(gx, bottom, 0);
            }
            GL.End();
            GL.PopMatrix();
        }

        float X(float t, Rect r) => r.x + (t - viewStart) / Mathf.Max(1e-5f, viewEnd - viewStart) * r.width;
        float T(float x, Rect r) => Mathf.Clamp(viewStart + (x - r.x) / Mathf.Max(1f, r.width) * (viewEnd - viewStart), 0f, Length);

        void HandleMouse(Rect waveRect, Rect ruler, Rect strip)
        {
            var e = Event.current;
            var all = new Rect(ruler.x, ruler.y, ruler.width, waveRect.yMax - ruler.y);
            switch (e.type)
            {
                case EventType.ScrollWheel when all.Contains(e.mousePosition):
                {
                    float at = T(e.mousePosition.x, waveRect);
                    float k = e.delta.y > 0 ? 1.25f : 0.8f;
                    viewStart = Mathf.Clamp(at - (at - viewStart) * k, 0f, Length);
                    viewEnd = Mathf.Clamp(at + (viewEnd - at) * k, 0f, Length);
                    if (viewEnd - viewStart < 0.01f) viewEnd = Mathf.Min(Length, viewStart + 0.01f);
                    e.Use();
                    break;
                }
                case EventType.MouseDown when all.Contains(e.mousePosition):
                {
                    GUIUtility.keyboardControl = 0; // so Space and A work again after typing in a box
                    float t = T(e.mousePosition.x, waveRect);
                    if (e.button == 2 || e.alt) { drag = Drag.Pan; dragAnchor = t; }
                    else if (strip.Contains(e.mousePosition))
                    {
                        int hit = regions.FindIndex(rg => t >= rg.start && t <= rg.end);
                        if (hit >= 0) SelectRegion(hit, e.clickCount > 1);
                        e.Use();
                        break;
                    }
                    else if (ruler.Contains(e.mousePosition)) { drag = Drag.Seek; Seek(t); }
                    else if (Mathf.Abs(e.mousePosition.x - X(selStart, waveRect)) < 6) drag = Drag.Start;
                    else if (Mathf.Abs(e.mousePosition.x - X(selEnd, waveRect)) < 6) drag = Drag.End;
                    else { drag = Drag.Make; dragAnchor = t; selStart = selEnd = t; activeRegion = -1; }
                    e.Use();
                    break;
                }
                case EventType.MouseDrag when drag != Drag.None:
                {
                    float t = T(e.mousePosition.x, waveRect);
                    switch (drag)
                    {
                        case Drag.Start: selStart = Mathf.Min(t, selEnd - 0.005f); break;
                        case Drag.End: selEnd = Mathf.Max(t, selStart + 0.005f); break;
                        case Drag.Make: selStart = Mathf.Min(dragAnchor, t); selEnd = Mathf.Max(dragAnchor, t); break;
                        case Drag.Seek: Seek(t); break;
                        case Drag.Pan:
                        {
                            float shift = dragAnchor - t, span = viewEnd - viewStart;
                            viewStart = Mathf.Clamp(viewStart + shift, 0f, Length - span);
                            viewEnd = viewStart + span;
                            break;
                        }
                    }
                    previewDirty = true;
                    e.Use();
                    break;
                }
                case EventType.MouseUp when drag != Drag.None:
                {
                    if (drag == Drag.Make && selEnd - selStart < 0.005f)
                    {
                        // just a click: keep everything selected and move the playhead there
                        float at = selStart;
                        selStart = 0f; selEnd = Length;
                        previewDirty = true;
                        Seek(at);
                    }
                    else if (drag != Drag.Seek && drag != Drag.Pan)
                    {
                        cursor = selStart;
                        if (source && source.isPlaying) { Stop(); Play(selStart); }
                    }
                    drag = Drag.None;
                    e.Use();
                    break;
                }
            }
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || samples == null) return;
            if (GUIUtility.keyboardControl != 0) return; // typing in a box
            if (e.keyCode == KeyCode.Space) { TogglePlay(); e.Use(); }
            else if (e.keyCode == KeyCode.A) { Undo.RecordObject(this, "Audio Trimmer"); AddRegion(); e.Use(); }
            else if (e.keyCode == KeyCode.Home) { Stop(); cursor = selStart; e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Stop(); playSelectionOnly = true; cursor = selStart; TogglePlay(); e.Use(); }
        }

        // ------------------------------------------------------------------ playback

        float Playhead()
        {
            if (source && (source.isPlaying || paused) && source.clip)
                return playFrom + (loopFade > 0f && playSelectionOnly ? loopFade : 0f) + source.time * Speed;
            return cursor;
        }

        void Seek(float t)
        {
            bool was = source && source.isPlaying;
            Stop();
            cursor = t;
            if (was) TogglePlay();
        }

        void TogglePlay()
        {
            if (samples == null) return;
            if (source && source.isPlaying) { source.Pause(); paused = true; cursor = Playhead(); return; }
            if (paused && source && !previewDirty) { source.UnPause(); paused = false; playing = true; return; }
            Play(cursor);
        }

        void Restart() { float at = Playhead(); Stop(); Play(at); }

        void Play(float at)
        {
            paused = false;
            float from = playSelectionOnly ? selStart : 0f, to = playSelectionOnly ? selEnd : Length;
            if (at < from || at >= to - 0.01f) at = from;
            if (previewDirty || !previewClip) BuildPreview(from, to);
            if (!player)
            {
                player = EditorUtility.CreateGameObjectWithHideFlags("Audio Trimmer Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
                source = player.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            source.clip = previewClip;
            source.loop = loop;
            playFrom = from;
            source.time = Mathf.Clamp((at - from - (loopFade > 0f && playSelectionOnly ? loopFade : 0f)) / Speed, 0f, Mathf.Max(0f, previewClip.length - 0.001f));
            source.Play();
            playing = true;
        }

        void Stop()
        {
            paused = false;
            playing = false;
            if (source) source.Stop();
        }

        void Tick()
        {
            if (restartAt > 0 && EditorApplication.timeSinceStartup >= restartAt && GUIUtility.hotControl == 0)
            {
                restartAt = -1;
                if (source && source.isPlaying) Restart();
            }
            if (source && source.isPlaying)
            {
                // the playhead only needs a redraw when it has moved a pixel or so
                float now = Playhead();
                if (Mathf.Abs(now - lastDrawnHead) * position.width / Mathf.Max(1e-4f, viewEnd - viewStart) >= 1f) { lastDrawnHead = now; Repaint(); }
            }
            if (playing && source && !source.isPlaying && !paused)
            {
                // reached the end
                playing = false;
                cursor = playSelectionOnly ? selStart : 0f;
                Repaint();
            }
        }

        void BuildPreview(float from, float to)
        {
            var data = Render(from, to, playSelectionOnly);
            if (previewClip) DestroyImmediate(previewClip);
            int n = data.Length / channels;
            previewClip = AudioClip.Create("Trim Preview", Mathf.Max(1, n), channels, rate, false);
            previewClip.hideFlags = HideFlags.HideAndDontSave;
            previewClip.SetData(data, 0);
            previewDirty = false;
        }

        /// <summary>The samples from <paramref name="from"/> to <paramref name="to"/>, with the volume and (optionally) the fades.</summary>
        float[] Render(float from, float to, bool withFades)
        {
            int f0 = Mathf.Clamp(Mathf.RoundToInt(from * rate), 0, frames), f1 = Mathf.Clamp(Mathf.RoundToInt(to * rate), f0 + 1, frames);
            int n = f1 - f0;
            var outp = new float[n * channels];
            Array.Copy(samples, f0 * channels, outp, 0, outp.Length);
            float gain = Mathf.Pow(10f, gainDb / 20f);
            if (normalize)
            {
                float peak = 0f;
                foreach (var v in outp) peak = Mathf.Max(peak, Mathf.Abs(v));
                if (peak > 1e-5f) gain *= 0.98f / peak;
            }
            bool looping = loopFade > 0f && withFades;
            if (looping) withFades = false;
            int fi = Mathf.RoundToInt(fadeIn * rate), fo = Mathf.RoundToInt(fadeOut * rate);
            for (int f = 0; f < n; f++)
            {
                float k = gain;
                if (withFades)
                {
                    if (fi > 0 && f < fi) k *= f / (float)fi;
                    if (fo > 0 && f > n - fo) k *= (n - f) / (float)fo;
                }
                for (int c = 0; c < channels; c++)
                {
                    int i = f * channels + c;
                    outp[i] = Mathf.Clamp(outp[i] * k, -1f, 1f);
                }
            }
            if (looping) outp = MakeLoop(outp, Mathf.RoundToInt(loopFade * rate));
            if (Mathf.Abs(pitch) > 0.001f) outp = Resample(outp, Speed);
            return outp;
        }

        /// <summary>
        /// Drops the first <paramref name="fade"/> frames and blends them into the end instead, so the last frame
        /// leads straight back into the first: a loop with no jump.
        /// </summary>
        float[] MakeLoop(float[] a, int fade)
        {
            int n = a.Length / channels;
            fade = Mathf.Clamp(fade, 0, n / 2);
            if (fade < 2) return a;
            int m = n - fade;
            var outp = new float[m * channels];
            for (int f = 0; f < m; f++)
            {
                int src = f + fade;
                int j = f - (m - fade); // how far into the blend
                for (int c = 0; c < channels; c++)
                {
                    float v = a[src * channels + c];
                    if (j >= 0)
                    {
                        float w = j / (float)fade;
                        // equal power, so the seam doesn't dip in volume
                        v = v * Mathf.Cos(w * Mathf.PI * 0.5f) + a[j * channels + c] * Mathf.Sin(w * Mathf.PI * 0.5f);
                    }
                    outp[f * channels + c] = Mathf.Clamp(v, -1f, 1f);
                }
            }
            return outp;
        }

        /// <summary>Plays the sound faster or slower (pitch and length change together).</summary>
        float[] Resample(float[] a, float speed)
        {
            int n = a.Length / channels;
            int m = Mathf.Max(1, Mathf.RoundToInt(n / speed));
            var outp = new float[m * channels];
            for (int f = 0; f < m; f++)
            {
                double pos = f * (double)speed;
                int i0 = Mathf.Min(n - 1, (int)pos), i1 = Mathf.Min(n - 1, i0 + 1);
                float t = (float)(pos - i0);
                for (int c = 0; c < channels; c++)
                    outp[f * channels + c] = Mathf.Lerp(a[i0 * channels + c], a[i1 * channels + c], t);
            }
            return outp;
        }

        // ------------------------------------------------------------------ saving

        void Save(bool overwrite)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            string target = path;
            if (!overwrite)
            {
                string dir = Path.GetDirectoryName(path);
                string name = CleanName(saveName);
                if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(path) + "_trim";
                target = Path.Combine(dir, name + ".wav").Replace('\\', '/');
                if (File.Exists(target))
                {
                    int pick = EditorUtility.DisplayDialogComplex("Name taken", $"There's already a sound called {name}.wav here.", "Replace it", "Cancel", "Keep both");
                    if (pick == 1) return;
                    if (pick == 2) target = AssetDatabase.GenerateUniqueAssetPath(target);
                }
            }
            var data = Render(selStart, selEnd, true);
            File.WriteAllBytes(target, WriteWav(data, channels, rate));
            Stop();
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<AudioClip>(target);
            Debug.Log($"[Audio Trimmer] Saved {target} ({Fmt(selEnd - selStart)}).", saved);
            if (saved) { EditorGUIUtility.PingObject(saved); Load(saved); saveName = Path.GetFileNameWithoutExtension(target); }
        }

        /// <summary>Gives the open sound the name in the Name box (keeps its references, it's the same asset).</summary>
        void Rename()
        {
            string path = AssetDatabase.GetAssetPath(clip);
            string name = CleanName(saveName);
            if (string.IsNullOrEmpty(name)) return;
            string error = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(error)) { EditorUtility.DisplayDialog("Can't rename", error, "OK"); return; }
            Debug.Log($"[Audio Trimmer] Renamed {Path.GetFileName(path)} to {name}{Path.GetExtension(path)}.", clip);
            EditorGUIUtility.PingObject(clip);
        }

        /// <summary>The name without characters a file name can't have.</summary>
        static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            name = name.Trim();
            if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        void MakeReadable()
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            if (!imp) return;
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
            Load(AssetDatabase.LoadAssetAtPath<AudioClip>(imp.assetPath));
        }

        static string Fmt(float t) => $"{Mathf.FloorToInt(t / 60f)}:{t % 60f:00.000}";

        // ------------------------------------------------------------------ wav files

        static bool ReadWav(byte[] b, out float[] data, out int channels, out int rate)
        {
            data = null; channels = 1; rate = 44100;
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return false;
            int pos = 12, format = 1, bits = 16;
            int dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(b, body);
                    channels = BitConverter.ToInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                    if (format == -2 && len >= 26) format = BitConverter.ToInt16(b, body + 24); // extensible
                }
                else if (id == "data") { dataPos = body; dataLen = Mathf.Min(len, b.Length - body); break; }
                pos = body + len + (len & 1);
            }
            if (dataPos < 0 || channels < 1) return false;
            int bytes = bits / 8;
            if (bytes < 1) return false;
            int count = dataLen / bytes;
            data = new float[count];
            for (int i = 0; i < count; i++)
            {
                int p = dataPos + i * bytes;
                if (format == 3 && bits == 32) data[i] = BitConverter.ToSingle(b, p);
                else if (bits == 8) data[i] = (b[p] - 128) / 128f;
                else if (bits == 16) data[i] = BitConverter.ToInt16(b, p) / 32768f;
                else if (bits == 24) data[i] = ((b[p] | (b[p + 1] << 8) | ((sbyte)b[p + 2] << 16))) / 8388608f;
                else if (bits == 32) data[i] = BitConverter.ToInt32(b, p) / 2147483648f;
                else return false;
            }
            return true;
        }

        static byte[] WriteWav(float[] data, int channels, int rate)
        {
            int dataBytes = data.Length * 2;
            using (var ms = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (var v in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
