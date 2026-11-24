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
    /// <summary>The tools you use are cards, open and side by side; the ones you don't are small buttons above them. Edit and
    /// Layers use the same cards.</summary>
    public partial class AudioTrimmer
    {
        [SerializeField] bool apart; [SerializeField] float stretch = 1f, echo, echoTime = 0.28f, echoAgain = 0.4f, echoDull = 0.4f, matchTone = 0.8f, matchLoud = 1f; [SerializeField] AudioClip matchTo; [SerializeField] List<Vector2> volPts = new List<Vector2>(); // (volume points: seconds in the file, times)
        [SerializeField] List<Tool> rack = new List<Tool> { Tool.Trim, Tool.Fades, Tool.Level }, rackLayer = new List<Tool> { Tool.Level, Tool.Fades, Tool.Tone };
        static readonly Tool[] EditTools = { Tool.Trim, Tool.Fades, Tool.Level, Tool.Draw, Tool.Tone, Tool.Pitch, Tool.Voice, Tool.Echo, Tool.Reverb, Tool.Match, Tool.Clean, Tool.Loop, Tool.Vary, Tool.Find }, LayerTools = { Tool.Level, Tool.Fades, Tool.Tone, Tool.Pitch, Tool.Voice, Tool.Echo, Tool.Reverb, Tool.Match };
        readonly Dictionary<int, float> cardH = new Dictionary<int, float>(); readonly List<List<Tool>> cardCols = new List<List<Tool>>(); Vector2 layerSetScroll; double flashAt; Tool flash; bool inLayer;

        // ------------------------------------------------------------------ the settings as one thing

        SoundFx FxNow(float zero)
        {
            if (matchTo) AudioDsp.Learn(matchTo);
            return new SoundFx { gainDb = gainDb, fadeIn = fadeIn, fadeOut = fadeOut, fadeShape = fadeShape, levelMode = levelMode, loudness = loudness, pitch = pitch, stretch = stretch, apart = apart, bass = bass, mid = middle, treble = treble, muffle = muffle, thin = thin, grit = grit, echo = echo, echoTime = echoTime, echoAgain = echoAgain, echoDull = echoDull, room = revAmount, roomSize = revSize, roomDull = revDull, volume = volPts.Select(v => new Vector2(v.x - zero, v.y)).ToList(), matchTo = matchTo, matchTone = matchTone, matchLoud = matchLoud, voice = voice.Copy() };
        }
        void SetFx(SoundFx f, float zero)
        {
            gainDb = f.gainDb; fadeIn = f.fadeIn; fadeOut = f.fadeOut; fadeShape = f.fadeShape; levelMode = f.levelMode; loudness = f.loudness; pitch = f.pitch; stretch = f.stretch; apart = f.apart; bass = f.bass; middle = f.mid; treble = f.treble; muffle = f.muffle; thin = f.thin; grit = f.grit; echo = f.echo; echoTime = f.echoTime; echoAgain = f.echoAgain; echoDull = f.echoDull; revAmount = f.room; revSize = f.roomSize; revDull = f.roomDull; volPts = f.volume.Select(v => new Vector2(v.x + zero, v.y)).ToList(); matchTo = f.matchTo; matchTone = f.matchTone; matchLoud = f.matchLoud; voice = f.voice != null ? f.voice.Copy() : new VoiceFx();
        }
        void ClearFx() => SetFx(new SoundFx(), 0);
        float VolAt(float t) => SoundFx.VolumeAt(volPts, t);

        /// <summary>Runs some settings' GUI on another sound's settings (a layer's).</summary>
        void WithFx(SoundFx f, float length, Action gui)
        {
            var keep = FxNow(0); float s = selStart, e = selEnd, lf = loopFade, ln = levelNow; bool il = inLayer; SetFx(f, 0); selStart = 0; selEnd = length; loopFade = 0; levelNow = float.NaN; inLayer = true;
            try { gui(); } finally { var now = FxNow(0); JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(now), f); SetFx(keep, 0); selStart = s; selEnd = e; loopFade = lf; levelNow = ln; inLayer = il; }
        }

        // ------------------------------------------------------------------ the buttons and the cards

        void Rack(Rect r, Tool[] tools, List<Tool> list, ref Vector2 scroll)
        {
            foreach (var t in tools) if (IsOn(t) && !list.Contains(t)) list.Add(t); // (what's in use always has its card)
            sPill = sPillNarrow; GUILayout.BeginArea(r); float x = 0; GUILayout.BeginHorizontal();
            foreach (var t in tools)
            {
                if (t == Tool.Trim) continue; var info = Info[t]; bool has = list.Contains(t); float w = sPillWide.CalcSize(new GUIContent(info.title)).x + 12; if (x + w > r.width && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w + 4;
                var b = GUILayoutUtility.GetRect(w, 24, GUILayout.Width(w)); b.height = 22; bool hover = b.Contains(Event.current.mousePosition); EditorGUI.DrawRect(b, has ? new Color(info.tint.r, info.tint.g, info.tint.b, 0.2f) : hover ? PanelHi : Panel); EditorGUI.DrawRect(new Rect(b.x, b.y, 3, b.height), has ? info.tint : new Color(info.tint.r, info.tint.g, info.tint.b, 0.5f)); GUI.Label(new Rect(b.x + 8, b.y, b.width - 8, b.height), (has ? "" : "+ ") + info.title, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink(has ? 0.9f : 0.6f) } });
                if (GUI.Button(b, new GUIContent("", has ? "It's below" : info.help), GUIStyle.none)) { if (has) { flash = t; flashAt = EditorApplication.timeSinceStartup; } else { list.Add(t); flash = t; flashAt = EditorApplication.timeSinceStartup; } GUIUtility.keyboardControl = 0; Repaint(); GUIUtility.ExitGUI(); }
            }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.Space(4);
            int n = Mathf.Clamp(Mathf.FloorToInt((r.width - 15) / 280f), 1, 4); float cw = (r.width - 15 - 6 * (n - 1)) / n;
            // each card goes to the shortest column, in order
            if (Event.current.type == EventType.Layout) { cardCols.Clear(); var hs = new float[n]; for (int c = 0; c < n; c++) cardCols.Add(new List<Tool>()); foreach (var t in tools.Where(list.Contains)) { int best = 0; for (int c = 1; c < n; c++) if (hs[c] < hs[best] - 20) best = c; cardCols[best].Add(t); hs[best] += cardH.TryGetValue((int)t, out var h) ? h : 110; } }
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); GUILayout.BeginHorizontal();
            for (int c = 0; c < cardCols.Count; c++) { GUILayout.BeginVertical(GUILayout.Width(cw)); foreach (var t in cardCols[c]) Card(t, cw, list); GUILayout.EndVertical(); if (c < cardCols.Count - 1) GUILayout.Space(6); }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea(); sPill = sPillWide;
        }

        void Card(Tool t, float width, List<Tool> list, Action close = null)
        {
            var info = Info[t]; bool on = IsOn(t); float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = Mathf.Clamp(width * 0.3f, 84, 120);
            var box = EditorGUILayout.BeginVertical(new GUIStyle(sCard) { padding = new RectOffset(12, 10, 6, 9), margin = new RectOffset(0, 0, 0, 6) }, GUILayout.Width(width));
            if (Event.current.type == EventType.Repaint) { cardH[(int)t] = box.height + 6; float fl = flash == t ? Mathf.Clamp01(1 - (float)(EditorApplication.timeSinceStartup - flashAt) / 0.7f) : 0; if (fl > 0) { EditorGUI.DrawRect(box, new Color(info.tint.r, info.tint.g, info.tint.b, fl * 0.3f)); Repaint(); } EditorGUI.DrawRect(new Rect(box.x, box.y, 3, box.height), on || t == Tool.Vary || t == Tool.Find ? info.tint : new Color(info.tint.r, info.tint.g, info.tint.b, 0.4f)); }
            GUILayout.BeginHorizontal(GUILayout.Height(20)); GUILayout.Label(new GUIContent(info.title, info.help), sTileTitle, GUILayout.ExpandWidth(false));
            if (on) { var dot = GUILayoutUtility.GetRect(8, 20, GUILayout.Width(8)); EditorGUI.DrawRect(new Rect(dot.x, dot.y + 6, 8, 8), info.tint); }
            GUILayout.FlexibleSpace(); if (on && GUILayout.Button(new GUIContent("reset", "Puts this one back"), EditorStyles.miniButton, GUILayout.Width(42))) ResetTool(t);
            if (close != null && GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(22))) { close(); GUIUtility.ExitGUI(); } if (list != null && t != Tool.Trim && GUILayout.Button(new GUIContent("✕", "Takes it away (and puts it back to nothing)"), EditorStyles.miniButton, GUILayout.Width(20))) { ResetTool(t); list.Remove(t); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            switch (t)
            {
                case Tool.Trim: TrimGUI(); break; case Tool.Fades: FadesGUI(); break; case Tool.Loop: LoopGUI(); break; case Tool.Find: FindGUI(); break; case Tool.Level: LevelGUI(); break; case Tool.Pitch: PitchGUI(); break; case Tool.Tone: ToneGUI(width); break; case Tool.Clean: CleanGUI(); break; case Tool.Reverb: RoomGUI(width); break; case Tool.Vary: VaryGUI(width); break; case Tool.Echo: EchoGUI(width); break; case Tool.Match: MatchGUI(); break; case Tool.Draw: DrawGUI(); break; case Tool.Voice: VoiceGUI(width); break;
            }
            EditorGUILayout.EndVertical(); EditorGUIUtility.labelWidth = lw;
        }

        // ------------------------------------------------------------------ pitch and length, echo, match, drawn volume

        void PitchGUI()
        {
            GUILayout.BeginHorizontal(); if (Pill("Like a tape", !apart, "Higher is also faster")) apart = false; if (Pill("On their own", apart, "The pitch and the length are changed separately")) apart = true; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            if (apart) { Slide("Pitch", "Semitones, 12 = an octave. The length stays", ref pitch, -24f, 24f); Slide("Length", "Times as long. The pitch stays", ref stretch, 0.25f, 4f); GUILayout.BeginHorizontal(); foreach (var p in new[] { ("Half", 0.5f), ("Normal", 1f), ("Double", 2f) }) if (Pill(p.Item1, Mathf.Approximately(stretch, p.Item2), "Length x " + p.Item2)) stretch = p.Item2; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); return; }
            Slide("Pitch", "Semitones, 12 = an octave", ref pitch, -24f, 24f); float sp = Speed; if (Slide("Speed", "The same thing as a speed", ref sp, 0.25f, 4f)) pitch = Mathf.Clamp(12f * Mathf.Log(sp, 2f), -24f, 24f);
            GUILayout.BeginHorizontal(); if (Pill("Octave down", false)) pitch = -12; if (Pill("Lower", false)) pitch = -4; if (Pill("Higher", false)) pitch = 4; if (Pill("Octave up", false)) pitch = 12; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
        }

        void EchoGUI(float width)
        {
            float x = 0; GUILayout.BeginHorizontal();
            foreach (var p in new[] { ("Slap", 0.45f, 0.09f, 0.1f, 0.3f), ("Street", 0.35f, 0.22f, 0.35f, 0.5f), ("Canyon", 0.5f, 0.55f, 0.5f, 0.55f), ("Dub", 0.6f, 0.38f, 0.72f, 0.7f), ("Robot", 0.7f, 0.012f, 0.75f, 0.1f) }) { float w = sPill.CalcSize(new GUIContent(p.Item1)).x + 4; if (x + w > width - 26 && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w; if (Pill(p.Item1, Mathf.Approximately(echo, p.Item2) && Mathf.Approximately(echoTime, p.Item3))) { echo = p.Item2; echoTime = p.Item3; echoAgain = p.Item4; echoDull = p.Item5; } }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); Slide("Amount", "", ref echo, 0f, 1f); Slide("Time", "Seconds between the repeats", ref echoTime, 0.005f, 1.5f); Slide("Repeats", "How much of it comes back each time", ref echoAgain, 0f, 0.9f); Slide("Softness", "Each repeat is duller", ref echoDull, 0f, 1f);
        }

        void MatchGUI()
        {
            var m = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Sound like", "A sound that already fits your game"), matchTo, typeof(AudioClip), false); if (m != matchTo) { matchTo = m; if (m) AudioDsp.Learn(m); GUI.changed = true; }
            if (matchTo && AudioDsp.Known(matchTo) == null) GUILayout.Label("That one can't be read (set its Load Type to Decompress On Load).", sHint);
            using (new EditorGUI.DisabledScope(!matchTo)) { Slide("Tone", "How deep and bright it is becomes like that one", ref matchTone, 0f, 1f); using (new EditorGUI.DisabledScope(levelMode != 0)) Slide("Loudness", "It becomes as loud as that one (when Volume is By hand)", ref matchLoud, 0f, 1f); }
        }

        void DrawGUI()
        {
            GUILayout.BeginHorizontal(); if (Pill(hand == Hand.Volume ? "Drawing: drag on the wave" : "Draw on the wave", hand == Hand.Volume, "Drag over the wave to draw how loud it is. Drag a point to move it, right-click takes it away. The middle is normal, the top is twice as loud, the bottom is silent")) { hand = hand == Hand.Volume ? Hand.Select : Hand.Volume; } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); float a = selStart, b = selEnd, d = b - a; void Put(params (float t, float g)[] p) { volPts = p.Select(q => new Vector2(a + d * q.t, q.g)).ToList(); GUI.changed = true; }
            if (Pill("Swell", false)) Put((0, 0.1f), (0.5f, 1.3f), (1, 0.1f)); if (Pill("Dip", false, "Makes room in the middle, for a voice")) Put((0, 1), (0.2f, 1), (0.3f, 0.3f), (0.7f, 0.3f), (0.8f, 1), (1, 1)); if (Pill("Late punch", false)) Put((0, 0.35f), (0.45f, 0.4f), (0.5f, 1.6f), (1, 0.8f)); if (Pill("Pulse", false)) Put(Enumerable.Range(0, 17).Select(i => (i / 16f, i % 2 == 0 ? 1f : 0.2f)).ToArray()); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.Label(volPts.Count == 0 ? "Nothing drawn yet." : volPts.Count + " points", sHint);
        }

        // ---- drawing on the wave
        int volDrag = -1; float volLastX; bool volFree;
        static float VolY(float g, Rect r) => r.yMax - Mathf.Clamp(g, 0, 2) / 2f * r.height;
        static float VolG(float y, Rect r) { float g = Mathf.Clamp((r.yMax - y) / r.height * 2f, 0f, 2f); return Mathf.Abs(g - 1) < 0.04f ? 1 : g; }

        bool VolumeMouse(Rect wave, Event e)
        {
            if (hand != Hand.Volume) return false;
            switch (e.type)
            {
                case EventType.MouseDown when wave.Contains(e.mousePosition) && e.button < 2 && !e.alt:
                {
                    GUIUtility.keyboardControl = 0; int hit = volPts.FindIndex(v => Mathf.Abs(X(v.x, wave) - e.mousePosition.x) < 8 && Mathf.Abs(VolY(v.y, wave) - e.mousePosition.y) < 10); if (e.button == 1) { if (hit >= 0) { volPts.RemoveAt(hit); VolChanged(); } e.Use(); return true; }
                    drag = Drag.Volume; volFree = hit < 0; volLastX = e.mousePosition.x; if (hit >= 0) volDrag = hit; else { float t = T(e.mousePosition.x, wave); int i = volPts.FindIndex(v => v.x > t); if (i < 0) i = volPts.Count; volPts.Insert(i, new Vector2(t, VolG(e.mousePosition.y, wave))); volDrag = i; } VolChanged(); e.Use(); return true;
                }
                case EventType.MouseDrag when drag == Drag.Volume:
                {
                    float t = T(e.mousePosition.x, wave), g = VolG(e.mousePosition.y, wave);
                    if (!volFree) { float lo = volDrag > 0 ? volPts[volDrag - 1].x + 0.001f : 0, hi = volDrag < volPts.Count - 1 ? volPts[volDrag + 1].x - 0.001f : Length; volPts[volDrag] = new Vector2(Mathf.Clamp(t, lo, hi), g); }
                    else if (Mathf.Abs(e.mousePosition.x - volLastX) >= 5) { float t0 = T(volLastX, wave), lo = Mathf.Min(t0, t), hi = Mathf.Max(t0, t); bool right = t > t0; volPts.RemoveAll(v => right ? v.x > lo && v.x <= hi : v.x >= lo && v.x < hi); int i = volPts.FindIndex(v => v.x > t); if (i < 0) i = volPts.Count; volPts.Insert(i, new Vector2(t, g)); volLastX = e.mousePosition.x; }
                    VolChanged(); e.Use(); return true;
                }
                case EventType.MouseUp when drag == Drag.Volume: { drag = Drag.None; if (volFree) volPts = Simpler(volPts, 0.03f); volDrag = -1; VolChanged(); e.Use(); return true; }
                case EventType.ContextClick when wave.Contains(e.mousePosition): e.Use(); return true;
            }
            return false;
        }
        void VolChanged() { previewDirty = true; GUI.changed = true; if (!rack.Contains(Tool.Draw)) rack.Add(Tool.Draw); Repaint(); }

        // (a hand-drawn line has hundreds of points: the ones that don't bend it are left out)
        static List<Vector2> Simpler(List<Vector2> p, float eps)
        {
            if (p.Count < 3) return p; var keep = new bool[p.Count]; keep[0] = keep[p.Count - 1] = true; var st = new Stack<(int a, int b)>(); st.Push((0, p.Count - 1));
            while (st.Count > 0) { var (a, b) = st.Pop(); float worst = eps; int at = -1; for (int i = a + 1; i < b; i++) { float y = Mathf.Lerp(p[a].y, p[b].y, Mathf.InverseLerp(p[a].x, p[b].x, p[i].x)), d = Mathf.Abs(y - p[i].y); if (d > worst) { worst = d; at = i; } } if (at > 0) { keep[at] = true; st.Push((a, at)); st.Push((at, b)); } }
            return p.Where((v, i) => keep[i]).ToList();
        }

        void DrawVolume(Rect r)
        {
            if (volPts.Count == 0 && hand != Hand.Volume) return; var col = new Color(0.45f, 1f, 0.6f); bool live = hand == Hand.Volume; if (live) EditorGUI.DrawRect(new Rect(r.x, VolY(1, r), r.width, 1), new Color(col.r, col.g, col.b, 0.25f));
            Handles.BeginGUI(); Handles.color = new Color(col.r, col.g, col.b, live ? 1 : 0.5f); var l = new List<Vector3> { new Vector3(r.x, VolY(VolAt(viewStart), r)) }; foreach (var v in volPts) if (v.x > viewStart && v.x < viewEnd) l.Add(new Vector3(X(v.x, r), VolY(v.y, r))); l.Add(new Vector3(r.xMax, VolY(VolAt(viewEnd), r))); if (volPts.Count > 0) Handles.DrawAAPolyLine(2.5f, l.ToArray()); Handles.EndGUI();
            if (live) foreach (var v in volPts) { float x = X(v.x, r); if (x < r.x || x > r.xMax) continue; EditorGUI.DrawRect(new Rect(x - 4, VolY(v.y, r) - 4, 8, 8), col); }
        }

        // ------------------------------------------------------------------ loops in music

        Task<List<AudioDsp.LoopSpot>> loopTask; CancellationTokenSource loopStop; List<AudioDsp.LoopSpot> loopSpots; float loopMin = 8f; float[] loopFor; int loopUsed = -1;

        void LoopGUI()
        {
            Slide("Blend", "Seconds of the end mixed into the start", ref loopFade, 0f, Mathf.Max(0.005f, Mathf.Min(4f, (selEnd - selStart) * 0.45f)));
            GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Short sounds: find a loop", "For engines, fire, rain: nudges the end to where the sound best matches its start, and sets a blend"))) FindLoop(); if (GUILayout.Button(new GUIContent("Hear the join", "The last 3 seconds, then the first 3"))) HearJoin(); GUILayout.EndHorizontal();
            GUILayout.Space(4); GUILayout.BeginHorizontal(); GUILayout.Label("Music", sTileTitle, GUILayout.Width(48)); if (loopTask == null) { if (GUILayout.Button(new GUIContent("Find loops in the music", "Looks for places that sound the same again, so it can jump back without being noticed"))) FindMusicLoops(); } else { GUILayout.Label("listening...", sHint); if (GUILayout.Button("Stop", GUILayout.Width(50))) loopStop.Cancel(); } GUILayout.EndHorizontal(); Slide("At least", "Seconds. Shorter loops aren't looked at", ref loopMin, 1f, Mathf.Max(4f, Length * 0.8f));
            if (loopSpots == null || loopFor != Src) return; if (loopSpots.Count == 0) { GUILayout.Label("No good place found. Try a shorter loop.", sHint); return; }
            for (int i = 0; i < loopSpots.Count; i++)
            {
                var s = loopSpots[i]; var row = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(new Rect(row.x, row.y + 1, row.width, 20), Ink(loopUsed == i ? 0.12f : 0.04f)); EditorGUI.DrawRect(new Rect(row.x, row.y + 1, row.width * s.score, 20), new Color(0.45f, 1f, 0.6f, 0.14f)); GUI.Label(new Rect(row.x + 34, row.y + 2, row.width - 40, 18), $"{Fmt(s.start)} to {Fmt(s.end)}   ({s.end - s.start:0.0} s)   {Mathf.RoundToInt(s.score * 100)}%", EditorStyles.miniLabel);
                if (GUI.Button(new Rect(row.x + 2, row.y + 2, 26, 18), new GUIContent("▶", "Use it and hear the join"), EditorStyles.miniButton)) { UseLoop(i); HearJoin(); } if (GUI.Button(new Rect(row.x + 30, row.y, row.width - 30, 22), new GUIContent("", "Use this one"), GUIStyle.none)) UseLoop(i);
            }
        }
        void FindMusicLoops() { loopStop = new CancellationTokenSource(); var st = loopStop.Token; var src = Src; int ch = channels, rt = rate; float min = loopMin; loopSpots = null; loopUsed = -1; loopFor = src; loopTask = Task.Run(() => AudioDsp.FindLoops(src, ch, rt, min, st)); }
        void UseLoop(int i) { var s = loopSpots[i]; loopUsed = i; float f = Mathf.Min(0.06f, (s.end - s.start) * 0.2f); f = Mathf.Min(f, Mathf.Max(0.006f, Length - s.end)); selStart = s.start; selEnd = Mathf.Min(Length, s.end + f); loopFade = f; loop = true; activeRegion = -1; previewDirty = true; GUI.changed = true; Stop(); cursor = selStart; }
        void HearJoin() { if (!LoopOn) { loop = true; playSelectionOnly = true; } var d = Render(selStart, selEnd, true); int n = d.Length / channels, k = Mathf.Min(n / 2, rate * 3); var j = new float[k * 2 * channels]; Array.Copy(d, (n - k) * channels, j, 0, k * channels); Array.Copy(d, 0, j, k * channels, k * channels); Stop(); Make(j); cursor = selStart; restartAt = -1; varTimes = new[] { 0f }; varFirst = -9; lastVar = -3; source.loop = false; source.time = 0; source.Play(); playing = true; previewDirty = true; }
        void LoopTick() { if (loopTask == null || !loopTask.IsCompleted) return; if (loopTask.Status == TaskStatus.RanToCompletion) loopSpots = loopTask.Result; else if (loopTask.IsFaulted) Debug.LogWarning("[Audio Trimmer] Couldn't look for loops: " + loopTask.Exception.GetBaseException().Message); loopTask = null; Repaint(); }

        // ------------------------------------------------------------------ linked to a layer

        [SerializeField] int linked = -1;
        bool IsLinked => linked >= 0 && linked < layers.Count && layers[linked].clip == clip && clip;

        void LinkBar(Rect r)
        {
            var l = layers[linked]; EditorGUI.DrawRect(r, new Color(0.9f, 0.6f, 0.9f, 0.16f)); EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), new Color(0.9f, 0.6f, 0.9f)); GUI.Label(new Rect(r.x + 12, r.y, r.width - 260, r.height), "You're editing the layer  " + l.name + ".  What you change here changes it there.", new GUIStyle(sTileTitle) { alignment = TextAnchor.MiddleLeft });
            if (GUI.Button(new Rect(r.xMax - 226, r.y + 3, 120, r.height - 6), "← Back to Layers")) { SyncLinked(true); Stop(); page = Page.Layers; layer = linked; GUIUtility.ExitGUI(); } if (GUI.Button(new Rect(r.xMax - 100, r.y + 3, 92, r.height - 6), new GUIContent("Let go", "Edit goes on by itself, the layer stays as it is now"))) { SyncLinked(true); linked = -1; GUIUtility.ExitGUI(); }
        }

        void SyncLinked(bool clean)
        {
            if (!IsLinked) return; var l = layers[linked]; l.cutStart = selStart; l.cutEnd = Mathf.Max(0, Length - selEnd); l.fx = FxNow(selStart);
            if (clean) { if (cleanOn) { FinishCleaning(); l.file = Keep(cleaned, channels, rate); } else l.file = ""; }
        }
        static string Keep(float[] d, int ch, int rt) { var dir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "HienaDevTools", "Layers"); Directory.CreateDirectory(dir); var f = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wav").Replace('\\', '/'); File.WriteAllBytes(f, WriteWav(d, ch, rt)); return f; }

        /// <summary>Opens a layer in Edit, linked: everything done there is done to the layer.</summary>
        void EditLayer(int i)
        {
            var l = layers[i]; if (!l.clip)
            {
                if (string.IsNullOrEmpty(l.file) || !File.Exists(l.file)) return; if (!EditorUtility.DisplayDialog("Edit this layer", l.name + " isn't a file in your project yet. Save it in Assets/Sounds to edit it?", "Save and edit", "Cancel")) return;
                if (!AssetDatabase.IsValidFolder("Assets/Sounds")) AssetDatabase.CreateFolder("Assets", "Sounds"); var target = AssetDatabase.GenerateUniqueAssetPath("Assets/Sounds/" + (CleanName(l.name).Length > 0 ? CleanName(l.name) : "sound") + ".wav"); File.Copy(l.file, target); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate); l.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(target); l.file = "";
            }
            var fx = l.fx.Copy(); fx.matchTo = l.fx.matchTo; float cs = l.cutStart, ce = l.cutEnd; Stop(); Load(l.clip); if (samples == null) { page = Page.Edit; return; } selStart = Mathf.Clamp(cs, 0, Length - 0.005f); selEnd = Mathf.Clamp(Length - ce, selStart + 0.005f, Length); SetFx(fx, selStart); linked = i; page = Page.Edit; previewDirty = true; viewStart = 0; viewEnd = Length;
        }
    }
}
