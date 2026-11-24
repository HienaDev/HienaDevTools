using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public partial class AudioTrimmer : EditorWindow
    {
        [SerializeField] AudioClip clip;
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
        // the name the trimmed sound is saved as (without .wav)
        [SerializeField] string saveName = "";
        bool loop = true, playSelectionOnly = true;
        // the view (zoom), in seconds
        [SerializeField] float viewStart, viewEnd;

        // cleaning up: taking out the background sound
        [SerializeField] bool cleanOn;
        [SerializeField] float bgFrom = -1f, bgTo = -1f;                 // the bit it learned the background from (seconds)
        [SerializeField] float cleanStrength = 0.5f, cleanAmount = 24f, rumble, gaps;
        bool hearRemoved;
        float[] cleaned, removed; string cleanedKey = "", cleanJobKey;
        Task<float[]> cleanJob; CancellationTokenSource cleanStop; double cleanAt = -1;
        string openName = "";                                            // the name box at the top
        string nameAll = "";                                             // one name for every region

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
        enum Drag { None, Start, End, Make, Seek, Pan, FadeIn, FadeOut, Volume }
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
            w.minSize = new Vector2(860, 560);
            if (c) w.Load(c);
        }

        void OnEnable()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += OnUndoRedo;
            // (after a script change or a restart: the same sound again, with the same selection and settings)
            if (clip && samples == null && AudioIO.Read(clip, out samples, out channels, out rate, out loadError)) { frames = samples.Length / Mathf.Max(1, channels); openName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip)); nameAll = openName; selEnd = Mathf.Min(selEnd, Length); if (viewEnd <= viewStart) { viewStart = 0; viewEnd = Length; } previewDirty = true; BuildPeaks(); }
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= OnUndoRedo;
            StopCleaning(); loopStop?.Cancel(); if (micClip) { Microphone.End(micName); micClip = null; }
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
            StopCleaning();
            samples = cleaned = removed = null; cleanedKey = "";
            cleanOn = hearRemoved = false; bgFrom = bgTo = -1f; rumble = gaps = 0f;
            loadError = null;
            previewDirty = true;
            if (!clip) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!AudioIO.Read(clip, out samples, out channels, out rate, out loadError)) return;
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
            ClearFx(); linked = -1; loopSpots = null; levelNow = float.NaN; levelShort = 0;
            openName = Path.GetFileNameWithoutExtension(path); nameAll = openName;
            BuildPeaks();
            waveFrom = waveTo = -1f; // redraw
        }

        /// <summary>The sound as it is now: cleaned up (or only what was taken out), or just as it was recorded.</summary>
        float[] Src => cleanOn && cleaned != null ? (hearRemoved ? removed : cleaned) : samples;

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
                    float v = Src[i];
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
                    float v = Src[i];
                    if (v < lo) lo = v; else if (v > hi) hi = v;
                }
            }
        }

        float Length => frames / (float)Mathf.Max(1, rate);

        // ------------------------------------------------------------------ the window

        // ------------------------------------------------------------------ the name

        /// <summary>The sound's name, to type over: Enter renames the file (everything that uses it keeps working).</summary>
        void NameRow()
        {
            string now = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip));
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "AudioName";
            bool esc = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && GUI.GetNameOfFocusedControl() == "AudioName";
            if (enter) { e.Use(); RenameTo(openName); GUIUtility.keyboardControl = 0; }
            if (esc) { e.Use(); openName = now; GUIUtility.keyboardControl = 0; }
            {
                GUI.SetNextControlName("AudioName");
                openName = EditorGUILayout.TextField(openName, new GUIStyle(EditorStyles.textField) { fontSize = 15, fontStyle = FontStyle.Bold, fixedHeight = 26 }, GUILayout.Height(26));
                bool changed = CleanName(openName) != now && CleanName(openName).Length > 0;
                using (new EditorGUI.DisabledScope(!changed))
                    if (GUILayout.Button(new GUIContent("Rename", "Renames this sound's file. Everything that uses it keeps working. (Enter does the same, Escape puts the name back.)"), GUILayout.Height(26), GUILayout.Width(80))) { RenameTo(openName); GUIUtility.keyboardControl = 0; }
            }
        }

        // ------------------------------------------------------------------ cleaning up

        string CleanKey => cleanOn ? $"{bgFrom:0.000}|{bgTo:0.000}|{cleanStrength:0.00}|{cleanAmount:0.0}|{rumble:0}|{gaps:0.0}" : "";

        void LearnQuietest()
        {
            if (!AudioCleaner.FindQuietest(samples, channels, rate, 0.4f, out int f0, out int f1)) { ShowNotification(new GUIContent("No background found: the quiet parts are completely silent")); return; }
            bgFrom = f0 / (float)rate; bgTo = f1 / (float)rate; cleanOn = true;
        }

        void StopCleaning() { cleanStop?.Cancel(); cleanStop = null; cleanJob = null; cleanJobKey = null; }

        // the work happens in the background; when it's done the waveform and the preview follow
        void CleanTick()
        {
            if (samples == null) return;
            string key = CleanKey;
            if (cleanJob != null && cleanJob.IsCompleted)
            {
                if (cleanJob.Status == TaskStatus.RanToCompletion && cleanJobKey == key) SetCleaned(cleanJob.Result, key);
                else if (cleanJob.IsFaulted) { Debug.LogWarning("[Audio Trimmer] Couldn't clean the sound: " + cleanJob.Exception.GetBaseException().Message); cleanedKey = key; }
                cleanJob = null;
                Repaint();
            }
            if (key == cleanedKey) return;
            if (key.Length == 0) { StopCleaning(); cleanedKey = ""; SourceChanged(); if (source && source.isPlaying) Restart(); Repaint(); return; }
            if (cleanJob != null && cleanJobKey != key) { cleanStop.Cancel(); return; }
            if (cleanJob == null && GUIUtility.hotControl == 0 && EditorApplication.timeSinceStartup > cleanAt) { cleanJob = StartCleaning(key); Repaint(); }
        }

        Task<float[]> StartCleaning(string key)
        {
            cleanStop = new CancellationTokenSource(); cleanJobKey = key;
            var stop = cleanStop.Token; var src = samples; int ch = channels, r = rate, f0 = Mathf.RoundToInt(bgFrom * rate), f1 = Mathf.RoundToInt(bgTo * rate);
            bool learn = bgTo > bgFrom; var s = new AudioCleaner.Settings { strength = cleanStrength, amountDb = cleanAmount, rumbleHz = rumble, gapsDb = gaps };
            return Task.Run(() => AudioCleaner.Clean(src, ch, r, learn ? AudioCleaner.Learn(src, ch, f0, f1) : null, s, stop));
        }

        void SetCleaned(float[] a, string key)
        {
            cleaned = a; removed = new float[a.Length];
            for (int i = 0; i < a.Length; i++) removed[i] = samples[i] - a[i];
            cleanedKey = key;
            SourceChanged();
            if (source && source.isPlaying) Restart();
        }

        /// <summary>Before saving: makes sure what's saved is the cleaned sound with the settings as they are now.</summary>
        void FinishCleaning()
        {
            hearRemoved = false;
            string key = CleanKey;
            if (key.Length == 0 || key == cleanedKey) { SourceChanged(); return; }
            EditorUtility.DisplayProgressBar("Audio Trimmer", "Cleaning the sound...", 0.5f);
            try { StopCleaning(); SetCleaned(StartCleaning(key).GetAwaiter().GetResult(), key); }
            finally { cleanJob = null; EditorUtility.ClearProgressBar(); }
        }

        void SourceChanged()
        {
            if (samples == null) return;
            previewDirty = true;
            BuildPeaks();
            waveFrom = waveTo = -1f;
            Repaint();
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
            FinishCleaning();
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
                for (int i = b * block * channels; i < end; i++) { float v = Mathf.Abs(Src[i]); if (v > m) m = v; }
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
                    float a = Src[(g - 1) * channels], b = Src[g * channels];
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
                    float d = Src[(e + i) * channels] - Src[(s + i) * channels];
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
            cleanAt = EditorApplication.timeSinceStartup;
            SourceChanged();
            if (source && source.isPlaying) Restart();
            Repaint();
        }

        void ResetAll()
        {
            Stop();
            selStart = 0f; selEnd = Length;
            fadeIn = fadeOut = gainDb = pitch = loopFade = 0f;
            ClearFx();
            cleanOn = hearRemoved = false; rumble = gaps = 0f;
            SourceChanged();
            regions.Clear();
            activeRegion = -1;
            cursor = 0f;
            previewDirty = true;
        }

        float Speed => apart ? 1f / Mathf.Clamp(stretch, 0.1f, 10f) : Mathf.Pow(2f, pitch / 12f);

        void DrawRuler(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, new Color(0.1f, 0.105f, 0.125f));
            float span = viewEnd - viewStart;
            float step = NiceStep(span / Mathf.Max(1f, r.width / 70f));
            for (float t = Mathf.Ceil(viewStart / step) * step; t <= viewEnd; t += step)
            {
                float x = X(t, r);
                EditorGUI.DrawRect(new Rect(x, r.yMax - 6, 1, 6), new Color(0.6f, 0.6f, 0.6f));
                if (x + 58 < r.xMax) GUI.Label(new Rect(x + 2, r.y, 70, r.height), Fmt(t), EditorStyles.miniLabel);
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
            EditorGUI.DrawRect(r, new Color(0.07f, 0.075f, 0.09f));
            // selection
            float sx = X(selStart, r), ex = X(selEnd, r);
            EditorGUI.DrawRect(new Rect(sx, r.y, ex - sx, r.height), new Color(0.3f, 0.55f, 0.95f, 0.18f));
            DrawPeaks(r);
            // what won't be kept, dimmed
            if (sx > r.x) EditorGUI.DrawRect(new Rect(r.x, r.y, Mathf.Min(sx, r.xMax) - r.x, r.height), new Color(0.07f, 0.075f, 0.09f, 0.62f));
            if (ex < r.xMax) { float dx = Mathf.Max(ex, r.x); EditorGUI.DrawRect(new Rect(dx, r.y, r.xMax - dx, r.height), new Color(0.07f, 0.075f, 0.09f, 0.62f)); }
            DrawFades(r); DrawVolume(r);
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
            GL.Color(cleanOn && hearRemoved ? new Color(0.95f, 0.5f, 0.4f) : new Color(1f, 0.72f, 0.38f));
            for (int x = 0; x < w; x++)
            {
                float gx = r.x + (x + 0.5f) / ppp;
                float vt = viewStart + (x + 0.5f) / w * (viewEnd - viewStart), k = FadeAt(vt) * (volPts.Count > 0 ? VolAt(vt) : 1f); // (drawn as it'll sound)
                float top = Mathf.Max(r.y, mid - colHi[x] * half * k), bottom = Mathf.Min(r.yMax, mid - colLo[x] * half * k);
                if (bottom - top < 1f) bottom = top + 1f;
                GL.Vertex3(gx, top, 0); GL.Vertex3(gx, bottom, 0);
            }
            GL.End();
            GL.PopMatrix();
        }

        static readonly Color FadeTint = new Color(1f, 0.85f, 0.35f);
        Rect FadeInHandle(Rect r) => new Rect(X(selStart + fadeIn, r) - 6, r.y, 12, 12);
        Rect FadeOutHandle(Rect r) => new Rect(X(selEnd - fadeOut, r) - 6, r.y, 12, 12);

        /// <summary>The fades as curves over the wave, with a square at the end of each to drag.</summary>
        void DrawFades(Rect r)
        {
            if (LoopOn || selEnd - selStart < 0.01f) return;
            lineMat.SetPass(0); GL.PushMatrix();
            for (int side = 0; side < 2; side++)
            {
                float len = side == 0 ? fadeIn : fadeOut; if (len <= 0f) continue;
                float x0 = X(side == 0 ? selStart : selEnd, r), x1 = X(side == 0 ? selStart + len : selEnd - len, r); const int N = 48;
                // what's taken away, shaded
                GL.Begin(GL.QUADS); GL.Color(new Color(FadeTint.r, FadeTint.g, FadeTint.b, 0.13f));
                for (int i = 0; i < N; i++)
                {
                    float a = i / (float)N, b = (i + 1) / (float)N, xa = Mathf.Clamp(Mathf.Lerp(x0, x1, a), r.x, r.xMax), xb = Mathf.Clamp(Mathf.Lerp(x0, x1, b), r.x, r.xMax), ya = Mathf.Lerp(r.yMax, r.y, Shaped(a)), yb = Mathf.Lerp(r.yMax, r.y, Shaped(b));
                    GL.Vertex3(xa, r.y, 0); GL.Vertex3(xb, r.y, 0); GL.Vertex3(xb, yb, 0); GL.Vertex3(xa, ya, 0);
                }
                GL.End();
                GL.Begin(GL.QUADS); GL.Color(FadeTint);
                for (int i = 0; i < N; i++)
                {
                    float a = i / (float)N, b = (i + 1) / (float)N, xa = Mathf.Lerp(x0, x1, a), xb = Mathf.Lerp(x0, x1, b), ya = Mathf.Lerp(r.yMax, r.y, Shaped(a)), yb = Mathf.Lerp(r.yMax, r.y, Shaped(b));
                    if (xa < r.x - 1 || xb < r.x - 1 || xa > r.xMax + 1 || xb > r.xMax + 1) continue;
                    var d = new Vector2(xb - xa, yb - ya).normalized; float nx = -d.y * 1.1f, ny = d.x * 1.1f;
                    GL.Vertex3(xa - nx, ya - ny, 0); GL.Vertex3(xb - nx, yb - ny, 0); GL.Vertex3(xb + nx, yb + ny, 0); GL.Vertex3(xa + nx, ya + ny, 0);
                }
                GL.End();
            }
            GL.PopMatrix();
            foreach (var h in new[] { FadeInHandle(r), FadeOutHandle(r) })
            {
                if (h.center.x < r.x - 1 || h.center.x > r.xMax + 1) continue;
                EditorGUI.DrawRect(h, new Color(0, 0, 0, 0.6f)); EditorGUI.DrawRect(new Rect(h.x + 1.5f, h.y + 1.5f, h.width - 3, h.height - 3), FadeTint);
                EditorGUIUtility.AddCursorRect(new Rect(h.x - 2, h.y, h.width + 4, h.height + 3), MouseCursor.SlideArrow);
            }
        }

        float X(float t, Rect r) => r.x + (t - viewStart) / Mathf.Max(1e-5f, viewEnd - viewStart) * r.width;
        float T(float x, Rect r) => Mathf.Clamp(viewStart + (x - r.x) / Mathf.Max(1f, r.width) * (viewEnd - viewStart), 0f, Length);

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || samples == null) return;
            if (GUIUtility.keyboardControl != 0) return; // typing in a box
            if (e.keyCode == KeyCode.Space) { TogglePlay(); e.Use(); }
            else if (e.keyCode == KeyCode.A) { Undo.RecordObject(this, "Audio Trimmer"); AddRegion(); e.Use(); }
            else if (e.keyCode == KeyCode.H) { hand = hand == Hand.Select ? Hand.Move : Hand.Select; e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { Undo.RecordObject(this, "Audio Trimmer"); SelectAll(); e.Use(); }
            else if (e.keyCode == KeyCode.Home) { Stop(); cursor = selStart; e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Stop(); playSelectionOnly = true; cursor = selStart; TogglePlay(); e.Use(); }
        }

        // ------------------------------------------------------------------ playback

        float Playhead()
        {
            if (varTimes == null && source && (source.isPlaying || paused) && source.clip)
                return playFrom + (LoopOn && playSelectionOnly ? loopFade : 0f) + source.time * Speed;
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

        void Restart() { if (varTimes != null || mixOn || makeOn) return; float at = Playhead(); Stop(); Play(at); }

        void Play(float at)
        {
            paused = false; varTimes = null;
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
            source.time = Mathf.Clamp((at - from - (LoopOn && playSelectionOnly ? loopFade : 0f)) / Speed, 0f, Mathf.Max(0f, previewClip.length - 0.001f));
            source.Play();
            playing = true;
        }

        void Stop()
        {
            paused = false;
            playing = false;
            varTimes = null; mixOn = makeOn = false;
            if (source) source.Stop();
        }

        void Tick()
        {
            MicTick(); LoopTick(); LayerTick();
            if (page == Page.Layers) { MixTick(); return; }
            if (page == Page.Check) CheckTick();
            if (page == Page.Make || page == Page.Record || page == Page.Ambience || page == Page.Check || page == Page.Stems) { MakeTick(); return; }
            CleanTick();
            if (restartAt > 0 && EditorApplication.timeSinceStartup >= restartAt && GUIUtility.hotControl == 0)
            {
                restartAt = -1;
                if (source && source.isPlaying) Restart();
            }
            if (source && source.isPlaying && varTimes != null) { int now = VarNow; if (now != lastVar) { lastVar = now; Repaint(); } }
            else if (source && source.isPlaying)
            {
                // the playhead only needs a redraw when it has moved a pixel or so
                float now = Playhead();
                if (Mathf.Abs(now - lastDrawnHead) * position.width / Mathf.Max(1e-4f, viewEnd - viewStart) >= 1f) { lastDrawnHead = now; Repaint(); }
            }
            if (playing && source && !source.isPlaying && !paused)
            {
                // reached the end
                playing = false;
                cursor = playSelectionOnly || varTimes != null ? selStart : 0f; varTimes = null;
                Repaint();
            }
        }

        void BuildPreview(float from, float to)
        {
            var data = Render(from, to, playSelectionOnly);
            Make(data);
            previewDirty = false;
        }

        void Make(float[] data) => Make(data, channels, rate);
        void Make(float[] data, int channels, int rate)
        {
            if (previewClip) DestroyImmediate(previewClip);
            int n = data.Length / channels;
            previewClip = AudioClip.Create("Trim Preview", Mathf.Max(1, n), channels, rate, false);
            previewClip.hideFlags = HideFlags.HideAndDontSave;
            previewClip.SetData(data, 0);
            if (!player)
            {
                player = EditorUtility.CreateGameObjectWithHideFlags("Audio Trimmer Preview", HideFlags.HideAndDontSave, typeof(AudioSource));
                source = player.GetComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0f;
            }
            source.clip = previewClip;
        }
        float levelNow = float.NaN, levelShort;

        /// <summary>The samples from <paramref name="from"/> to <paramref name="to"/>, with everything done to them.</summary>
        float[] Render(float from, float to, bool withFades, Vary vary = default)
        {
            from = Mathf.Min(from + vary.skip, Mathf.Max(from, to - 0.01f));
            int f0 = Mathf.Clamp(Mathf.RoundToInt(from * rate), 0, frames), f1 = Mathf.Clamp(Mathf.RoundToInt(to * rate), f0 + 1, frames);
            int n = f1 - f0;
            var outp = new float[n * channels];
            Array.Copy(Src, f0 * channels, outp, 0, outp.Length);
            bool looping = LoopOn && withFades, all = withFades; // (the whole sound, played for orientation, keeps its length)
            var fx = FxNow(from); if (looping || !withFades) fx.fadeIn = fx.fadeOut = 0f;
            // (a start that's been skipped gets the tiniest fade, so it can't click)
            if (vary.skip > 0f) fx.fadeIn = Mathf.Max(fx.fadeIn, Mathf.Min(1 / 400f, n / 4f / rate));
            fx.Shape(outp, channels, rate); float gain = levelMode == 0 ? Mathf.Pow(10f, gainDb / 20f) : 1f; if (gain != 1f) for (int i = 0; i < outp.Length; i++) outp[i] *= gain;
            if (looping) outp = MakeLoop(outp, Mathf.RoundToInt(loopFade * rate));
            outp = fx.Run(outp, channels, rate, looping, vary.pitch, all);
            float roomBefore = vary.isOne ? -20f * Mathf.Log10(Mathf.Max(1e-5f, AudioFx.PeakOf(outp)) / 0.98f) : 0f;
            // a variation's tone: duller or brighter, the whole range leans (and as loud as before: volume has its own slider)
            if (Mathf.Abs(vary.tilt) > 0.05f)
            {
                float was = AudioFx.Loudness(outp, channels, rate); AudioFx.Tone(outp, channels, rate, new AudioFx.ToneSettings { bass = -vary.tilt * 0.6f, treble = vary.tilt });
                float now = AudioFx.Loudness(outp, channels, rate); if (was > -90 && now > -90) { float k = Mathf.Pow(10f, (was - now) / 20f); for (int i = 0; i < outp.Length; i++) outp[i] *= k; }
            }
            levelShort = fx.Level(outp, channels, rate);
            // (a loud sound has no room for louder variations: then they're all moved down together, so they keep their differences)
            if (vary.isOne) { float room = -20f * Mathf.Log10(Mathf.Max(1e-5f, AudioFx.PeakOf(outp)) / 0.98f), down = levelMode == 0 ? Mathf.Max(0f, varVolume + varTone * 0.4f - roomBefore) : Mathf.Max(0f, varVolume - room), k = Mathf.Pow(10f, (vary.gain - down) / 20f); for (int i = 0; i < outp.Length; i++) outp[i] *= k; }
            if (vary.isOne) AudioFx.Limit(outp);
            for (int i = 0; i < outp.Length; i++) outp[i] = Mathf.Clamp(outp[i], -1f, 1f);
            levelNow = AudioFx.Loudness(outp, channels, rate);
            return outp;
        }

        bool LoopOn => loopFade > 0.004f;
        AudioFx.ToneSettings ToneNow => new AudioFx.ToneSettings { bass = bass, mid = middle, treble = treble, muffle = muffle, thin = thin, grit = grit };
        /// <summary>How loud it is part of the way through a fade (0..1).</summary>
        float Shaped(float k) => SoundFx.Shaped(k, fadeShape);
        /// <summary>What the fades do at a time in the sound.</summary>
        float FadeAt(float time)
        {
            if (LoopOn || time < selStart || time > selEnd) return 1f;
            float k = 1f;
            if (fadeIn > 0f && time < selStart + fadeIn) k *= Shaped((time - selStart) / fadeIn);
            if (fadeOut > 0f && time > selEnd - fadeOut) k *= Shaped((selEnd - time) / fadeOut);
            return k;
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
            FinishCleaning();
            var data = Render(selStart, selEnd, true);
            File.WriteAllBytes(target, WriteWav(data, channels, rate));
            Stop();
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<AudioClip>(target);
            Debug.Log($"[Audio Trimmer] Saved {target} ({Fmt(selEnd - selStart)}).", saved);
            if (saved) { EditorGUIUtility.PingObject(saved); Load(saved); saveName = Path.GetFileNameWithoutExtension(target); }
        }

        /// <summary>Gives the open sound the name in the Name box (keeps its references, it's the same asset).</summary>
        void RenameTo(string wanted)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            string was = Path.GetFileNameWithoutExtension(path), name = CleanName(wanted);
            if (string.IsNullOrEmpty(name) || name == was) return;
            string error = AssetDatabase.RenameAsset(path, name);
            if (!string.IsNullOrEmpty(error)) { EditorUtility.DisplayDialog("Can't rename", error, "OK"); return; }
            Debug.Log($"[Audio Trimmer] Renamed {Path.GetFileName(path)} to {name}{Path.GetExtension(path)}.", clip);
            openName = name;
            // the names made from the old one follow
            if (saveName.StartsWith(was)) saveName = name + saveName.Substring(was.Length);
            if (nameAll == was) nameAll = name;
            foreach (var r in regions) if (r.name.StartsWith(was)) r.name = name + r.name.Substring(was.Length);
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

        internal static bool ReadWav(byte[] b, out float[] data, out int channels, out int rate)
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
