using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Ten ways to lay the same tools out. The Layout dropdown at the top picks one; it's remembered.</summary>
    public partial class AudioTrimmer
    {
        public static readonly (string name, string about)[] Layouts =
        {
            ("Everything, by category", "All settings at once in columns, no boxes"), ("Knobs, by category", "All settings at once as dials"), ("Mixer", "All settings at once as upright strips, like a mixing desk"), ("Side panel", "All settings in one tall list on the left, the wave gets the rest"), ("Both sides", "Cutting on the left, sound on the right, the wave in the middle"), ("All cards", "All settings at once as cards"), ("Cards I use", "Only the cards you add"), ("Tabs", "One category at a time"), ("List", "Pick a tool on the left, its settings on the right"), ("Folding rows", "One line each, click to open"),
        };
        int m_Layout = -1; int LayoutNow { get { if (m_Layout < 0) m_Layout = Mathf.Clamp(EditorPrefs.GetInt("HienaDev.AudioTrimmer.Layout", 0), 0, Layouts.Length - 1); return m_Layout; } set { m_Layout = value; EditorPrefs.SetInt("HienaDev.AudioTrimmer.Layout", value); } }
        static readonly (string name, Color tint, Tool[] tools)[] Categories =
        {
            ("CUT", new Color(0.4f, 0.75f, 1f), new[] { Tool.Trim, Tool.Fades, Tool.Loop, Tool.Find }), ("VOLUME", new Color(0.95f, 0.75f, 0.3f), new[] { Tool.Level, Tool.Draw, Tool.Match }), ("SOUND", new Color(0.55f, 0.85f, 1f), new[] { Tool.Tone, Tool.Pitch }), ("VOICE", new Color(0.8f, 0.6f, 1f), new[] { Tool.Voice }), ("SPACE", new Color(0.6f, 0.9f, 0.9f), new[] { Tool.Echo, Tool.Reverb }), ("CLEAN AND MULTIPLY", new Color(0.95f, 0.55f, 0.75f), new[] { Tool.Clean, Tool.Vary }),
        };
        [SerializeField] int tabNow, listNow; [SerializeField] float sideSize = 400, sideSize2 = 330; Vector2 layScroll, layScroll2;

        void LayoutMenu()
        {
            string cap = position.width < 1150 ? "Layout ▾" : "Layout: " + Layouts[LayoutNow].name + " ▾"; var r = GUILayoutUtility.GetRect(new GUIContent(cap), GUI.skin.button, GUILayout.Height(26)); if (!GUI.Button(r, new GUIContent(cap, "Ten ways to lay these tools out. Pick the one that feels best"))) return; var m = new GenericMenu(); for (int i = 0; i < Layouts.Length; i++) { int q = i; m.AddItem(new GUIContent(Layouts[i].name + "   (" + Layouts[i].about + ")"), i == LayoutNow, () => { LayoutNow = q; layScroll = layScroll2 = Vector2.zero; Repaint(); }); } m.DropDown(r);
        }

        /// <summary>The Edit page in the picked layout.</summary>
        void EditLayout(Rect body)
        {
            int L = LayoutNow; float W = body.width; EditorGUI.BeginChangeCheck();
            if (L == 3 || L == 4)
            {
                bool both = L == 4; if (both && sideSize > W * 0.4f) sideSize = W * 0.28f; float lw = Mathf.Clamp(sideSize, 250, Mathf.Max(250, W - (both ? 620 : 380))), rw2 = both ? Mathf.Clamp(sideSize2, 250, Mathf.Max(250, W - lw - 340)) : 0; sideSize = SplitBar(5, new Rect(body.x + 6 + lw, body.y, 7, body.height), true, lw, 1, 250, Mathf.Max(250, W - (both ? 620 : 380))); if (both) sideSize2 = SplitBar(6, new Rect(body.xMax - 13 - rw2, body.y, 7, body.height), true, rw2, -1, 250, Mathf.Max(250, W - lw - 340));
                var left = new Rect(body.x + 6, body.y + 6, lw, body.height - 12); var mid = new Rect(left.xMax + 7, left.y, W - lw - 19 - (both ? rw2 + 7 : 0), left.height); if (IsLinked) { LinkBar(new Rect(mid.x, mid.y, mid.width, 28)); mid.yMin += 34; } float ph = Mathf.Clamp(mid.height * 0.42f, 190, 330); WaveArea(new Rect(mid.x, mid.y, mid.width, mid.height - ph - 6));
                var lt = both ? new[] { 0, 1, 5 } : new[] { 0, 1, 2, 3, 4, 5 }; DenseColumn(left, lt, ref layScroll); if (both) DenseColumn(new Rect(body.xMax - 6 - rw2, left.y, rw2, left.height), new[] { 2, 3, 4 }, ref layScroll2); After(); RightColumn(new Rect(mid.x, mid.yMax - ph, mid.width, ph)); return;
            }
            float most = Mathf.Max(170, body.height - 150); if (waveSize <= 0) waveSize = Mathf.Clamp(body.height * 0.44f, 200, 460); float waveH = Mathf.Clamp(waveSize, 170, most); waveSize = SplitBar(1, new Rect(body.x, body.y + waveH + 4, body.width, 10), false, waveH, 1, 170, most); WaveArea(new Rect(body.x + 6, body.y + 6, body.width - 12, waveH));
            var low = new Rect(body.x + 6, body.y + waveH + 12, body.width - 12, body.height - waveH - 18); if (rightSize <= 0) rightSize = Mathf.Clamp(W * 0.2f, 220, 300); float rw = Mathf.Clamp(rightSize, 200, Mathf.Max(200, W - 320)); rightSize = SplitBar(2, new Rect(low.xMax - rw - 7, low.y, 8, low.height), true, rw, -1, 200, Mathf.Max(200, W - 320)); var cr = new Rect(low.x, low.y, low.width - rw - 6, low.height); if (IsLinked) { LinkBar(new Rect(cr.x, cr.y, cr.width, 28)); cr.yMin += 34; }
            switch (L)
            {
                case 1: Knobs(cr); break; case 2: Mixer(cr); break; case 5: AllCards(cr); break; case 6: Rack(cr, EditTools, rack, ref setScroll); break; case 7: Tabs(cr); break; case 8: ListPanel(cr); break; case 9: Folding(cr); break; default: Dense(cr); break;
            }
            After(); RightColumn(new Rect(low.xMax - rw, low.y, rw, low.height));
        }
        void After()
        {
            if (EditorGUI.EndChangeCheck()) { previewDirty = true; if (varTimes == null && source && source.isPlaying) restartAt = EditorApplication.timeSinceStartup + 0.15; } // (hearing the variations is its own sound; a slider waits until it rests)
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint) SyncLinked(false);
        }

        // ------------------------------------------------------------------ the settings as plain numbers

        class Par { public string name, tip; public Func<float> get; public Action<float> set; public Func<float> lo, hi; public float def; public bool whole; }
        Dictionary<Tool, Par[]> m_Pars;
        static Par N(string n, Func<float> g, Action<float> s, float lo, float hi, float def, string tip = "", bool whole = false) => new Par { name = n, get = g, set = s, lo = () => lo, hi = () => hi, def = def, tip = tip, whole = whole };
        Par[] Pars(Tool t)
        {
            if (m_Pars == null)
            {
                Par D(string n, Func<float> g, Action<float> s, Func<float> hi, string tip = "") => new Par { name = n, get = g, set = s, lo = () => 0, hi = hi, def = 0, tip = tip };
                m_Pars = new Dictionary<Tool, Par[]>
                {
                    { Tool.Trim, new[] { D("Start", () => selStart, v => selStart = Mathf.Clamp(v, 0, Mathf.Max(0, selEnd - 0.005f)), () => Length, "Seconds"), new Par { name = "End", get = () => selEnd, set = v => selEnd = Mathf.Clamp(v, selStart + 0.005f, Length), lo = () => 0, hi = () => Length, def = -1, tip = "Seconds" } } },
                    { Tool.Fades, new[] { D("Fade in", () => fadeIn, v => { fadeIn = v; loopFade = 0; }, () => Mathf.Max(0.001f, selEnd - selStart - fadeOut), "Seconds"), D("Fade out", () => fadeOut, v => { fadeOut = v; loopFade = 0; }, () => Mathf.Max(0.001f, selEnd - selStart - fadeIn), "Seconds") } },
                    { Tool.Loop, new[] { D("Blend", () => loopFade, v => loopFade = v, () => Mathf.Max(0.005f, Mathf.Min(4f, (selEnd - selStart) * 0.45f)), "Seconds of the end mixed into the start") } },
                    { Tool.Find, new[] { N("Quiet below", () => threshDb, v => threshDb = v, -70, -6, -40, "dB"), N("Gap needed", () => minGap, v => minGap = v, 0.01f, 1, 0.08f, "Seconds"), N("Shortest", () => minLen, v => minLen = v, 0.005f, 0.5f, 0.03f, "Seconds"), N("Padding", () => pad, v => pad = v, 0, 0.2f, 0.01f, "Seconds") } },
                    { Tool.Level, new[] { N("Volume", () => gainDb, v => { gainDb = v; levelMode = 0; }, -24, 24, 0, "dB, by hand"), N("Loudness", () => loudness, v => { loudness = v; levelMode = 2; }, -40, -8, -18, "LUFS: moving it switches to A loudness") } },
                    { Tool.Draw, new Par[0] }, { Tool.Match, new[] { N("Tone", () => matchTone, v => matchTone = v, 0, 1, 0.8f), N("Loudness", () => matchLoud, v => matchLoud = v, 0, 1, 1) } },
                    { Tool.Tone, new[] { N("Bass", () => bass, v => bass = v, -24, 24, 0, "dB"), N("Middle", () => middle, v => middle = v, -24, 24, 0, "dB"), N("Treble", () => treble, v => treble = v, -24, 24, 0, "dB"), N("Muffled", () => muffle, v => muffle = v, 0, 1, 0), N("Thin", () => thin, v => thin = v, 0, 1, 0), N("Grit", () => grit, v => grit = v, 0, 1, 0) } },
                    { Tool.Pitch, new[] { N("Pitch", () => pitch, v => pitch = v, -24, 24, 0, "Semitones"), N("Length", () => apart ? stretch : 1f / Speed, v => { if (apart) stretch = v; else pitch = Mathf.Clamp(-12f * Mathf.Log(v, 2f), -24f, 24f); }, 0.25f, 4, 1, "Times as long") } },
                    { Tool.Echo, new[] { N("Amount", () => echo, v => echo = v, 0, 1, 0), N("Time", () => echoTime, v => echoTime = v, 0.005f, 1.5f, 0.28f, "Seconds"), N("Repeats", () => echoAgain, v => echoAgain = v, 0, 0.9f, 0.4f), N("Softness", () => echoDull, v => echoDull = v, 0, 1, 0.4f) } },
                    { Tool.Reverb, new[] { N("Amount", () => revAmount, v => revAmount = v, 0, 1, 0), N("Size", () => revSize, v => revSize = v, 0, 1, 0.4f), N("Softness", () => revDull, v => revDull = v, 0, 1, 0.4f) } },
                    { Tool.Clean, new[] { N("Strength", () => cleanStrength, v => cleanStrength = v, 0, 1, 0.5f), N("Turn down", () => cleanAmount, v => cleanAmount = v, 3, 48, 24, "dB"), N("Rumble", () => rumble, v => { rumble = v; if (v > 0) cleanOn = true; }, 0, 300, 0, "Hz, 0 = off"), N("Quiet gaps", () => gaps, v => { gaps = v; if (v > 0) cleanOn = true; }, 0, 60, 0, "dB, 0 = off") } },
                    { Tool.Vary, new[] { N("How many", () => varCount, v => varCount = Mathf.RoundToInt(v), 2, 16, 5, "", true), N("Pitch ±", () => varPitch, v => varPitch = v, 0, 12, 3, "Semitones"), N("Volume ±", () => varVolume, v => varVolume = v, 0, 12, 3, "dB"), N("Tone ±", () => varTone, v => varTone = v, 0, 24, 8, "dB"), N("Late start", () => varStart, v => varStart = v, 0, 60, 15, "ms") } },
                    { Tool.Voice, new[] { N("Robot", () => voice.robot, v => voice.robot = v, 0, 1, 0), N("Its note", () => voice.robotHz, v => voice.robotHz = v, 40, 400, 100, "Hz"), N("Whisper", () => voice.whisper, v => voice.whisper = v, 0, 1, 0), N("Lower voice", () => voice.low, v => voice.low = v, 0, 1, 0, "A second voice below"), N("How low", () => voice.lowSemi, v => voice.lowSemi = v, -24, -1, -12, "Semitones"), N("Higher voice", () => voice.high, v => voice.high = v, 0, 1, 0, "A second voice above"), N("How high", () => voice.highSemi, v => voice.highSemi = v, 1, 24, 12, "Semitones"), N("Many", () => voice.chorus, v => voice.chorus = v, 0, 1, 0, "Several of it at once"), N("Metal", () => voice.ring, v => voice.ring = v, 0, 1, 0), N("Its buzz", () => voice.ringHz, v => voice.ringHz = v, 5, 400, 60, "Hz"), N("Wobble", () => voice.wobble, v => voice.wobble = v, 0, 1, 0), N("Wobble speed", () => voice.wobbleHz, v => voice.wobbleHz = v, 0.3f, 20, 5, "Hz"), N("Pulse", () => voice.pulse, v => voice.pulse = v, 0, 1, 0), N("Pulse speed", () => voice.pulseHz, v => voice.pulseHz = v, 1, 40, 8, "Hz"), N("Old console", () => voice.crush, v => voice.crush = v, 0, 1, 0), N("Stutter", () => voice.stutter, v => voice.stutter = v, 0, 1, 0), N("Ghost", () => voice.ghost, v => voice.ghost = v, 0, 1, 0, "Its echo comes before it") } },
                };
            }
            return m_Pars[t];
        }
        static string Num(Par p, float v) => p.whole ? Mathf.RoundToInt(v).ToString() : Mathf.Abs(v) >= 100 ? v.ToString("0") : Mathf.Abs(v) >= 10 ? v.ToString("0.0") : v.ToString("0.00");

        // ready-made ones and the buttons that aren't numbers, small
        void Extras(Tool t, float width)
        {
            var mini = EditorStyles.miniButton; float x = 0; bool open = false;
            bool B(string s, bool on = false, string tip = "") { float w = mini.CalcSize(new GUIContent(s)).x + 2; if (!open) { GUILayout.BeginHorizontal(); open = true; x = 0; } if (x + w > width && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w + 2; var c = GUI.backgroundColor; if (on) GUI.backgroundColor = Accent; bool r = GUILayout.Button(new GUIContent(s, tip), mini, GUILayout.Width(w)); GUI.backgroundColor = c; return r; }
            void End() { if (open) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); open = false; } }
            float len = selEnd - selStart;
            switch (t)
            {
                case Tool.Trim: if (B("Cut the silence off")) TrimSilence(); if (B("No clicks", false, "Both ends where the wave crosses zero")) SnapToZero(); if (B("All of it")) SelectAll(); break;
                case Tool.Fades: if (B("Tiny")) { fadeIn = fadeOut = Mathf.Min(0.005f, len * 0.4f); loopFade = 0; } if (B("Soft")) { fadeIn = Mathf.Min(0.05f, len * 0.2f); fadeOut = Mathf.Min(0.3f, len * 0.4f); loopFade = 0; } if (B("Long")) { fadeIn = len * 0.25f; fadeOut = len * 0.4f; loopFade = 0; } if (B("Straight", fadeShape == 0)) fadeShape = 0; if (B("Smooth", fadeShape == 1)) fadeShape = 1; if (B("Slow", fadeShape == 2)) fadeShape = 2; break;
                case Tool.Loop: if (B("Find a loop", false, "For short sounds")) FindLoop(); if (B(loopTask != null ? "listening..." : "Loops in music", false, "Looks for places that sound the same again")) { if (loopTask == null) FindMusicLoops(); else loopStop.Cancel(); } if (B("Hear the join")) HearJoin(); End(); if (loopSpots != null && loopFor == Src) for (int i = 0; i < Mathf.Min(5, loopSpots.Count); i++) { var s = loopSpots[i]; if (GUILayout.Button($"{Fmt(s.start)} to {Fmt(s.end)}   {Mathf.RoundToInt(s.score * 100)}%", mini)) { UseLoop(i); HearJoin(); } } break;
                case Tool.Find: if (B("Find the sounds")) FindSounds(); break;
                case Tool.Level: if (B("By hand", levelMode == 0)) levelMode = 0; if (B("Full", levelMode == 1, "As loud as it goes")) levelMode = 1; if (B("A loudness", levelMode == 2)) levelMode = 2; if (!float.IsNaN(levelNow)) { if (!open) { GUILayout.BeginHorizontal(); open = true; } GUILayout.Label($"{levelNow:0.0} LUFS", sHint, GUILayout.Width(64)); } break;
                case Tool.Draw: if (B(hand == Hand.Volume ? "Drawing: drag on the wave" : "Draw on the wave", hand == Hand.Volume)) hand = hand == Hand.Volume ? Hand.Select : Hand.Volume; void Put(params (float t, float g)[] p) { volPts = p.Select(q => new Vector2(selStart + len * q.t, q.g)).ToList(); GUI.changed = true; } if (B("Swell")) Put((0, 0.1f), (0.5f, 1.3f), (1, 0.1f)); if (B("Dip")) Put((0, 1), (0.2f, 1), (0.3f, 0.3f), (0.7f, 0.3f), (0.8f, 1), (1, 1)); if (B("Late punch")) Put((0, 0.35f), (0.45f, 0.4f), (0.5f, 1.6f), (1, 0.8f)); if (B("Pulse")) Put(Enumerable.Range(0, 17).Select(i => (i / 16f, i % 2 == 0 ? 1f : 0.2f)).ToArray()); if (volPts.Count > 0 && B("Clear")) { volPts.Clear(); GUI.changed = true; } break;
                case Tool.Match: { var m = (AudioClip)EditorGUILayout.ObjectField(matchTo, typeof(AudioClip), false, GUILayout.Width(width)); if (m != matchTo) { matchTo = m; if (m) AudioDsp.Learn(m); GUI.changed = true; } break; }
                case Tool.Tone: foreach (var p in Tones) if (B(p.name, Same(p.s))) { bass = p.s.bass; middle = p.s.mid; treble = p.s.treble; muffle = p.s.muffle; thin = p.s.thin; grit = p.s.grit; } break;
                case Tool.Pitch: if (B("Like a tape", !apart, "Higher is also faster")) apart = false; if (B("On their own", apart, "Pitch and length separately")) apart = true; break;
                case Tool.Echo: foreach (var p in new[] { ("Slap", 0.45f, 0.09f, 0.1f, 0.3f), ("Street", 0.35f, 0.22f, 0.35f, 0.5f), ("Canyon", 0.5f, 0.55f, 0.5f, 0.55f), ("Dub", 0.6f, 0.38f, 0.72f, 0.7f), ("Robot", 0.7f, 0.012f, 0.75f, 0.1f) }) if (B(p.Item1, Mathf.Approximately(echo, p.Item2) && Mathf.Approximately(echoTime, p.Item3))) { echo = p.Item2; echoTime = p.Item3; echoAgain = p.Item4; echoDull = p.Item5; } break;
                case Tool.Reverb: foreach (var p in new[] { ("Small", 0.22f, 0.12f, 0.6f), ("Bathroom", 0.35f, 0.3f, 0.05f), ("Hall", 0.35f, 0.65f, 0.45f), ("Cave", 0.5f, 0.88f, 0.3f), ("Cathedral", 0.55f, 0.97f, 0.5f) }) if (B(p.Item1, revAmount > 0 && Mathf.Approximately(revAmount, p.Item2))) { revAmount = p.Item2; revSize = p.Item3; revDull = p.Item4; } break;
                case Tool.Clean: if (B("Find it for me", false, "Takes the quietest bit as the background")) LearnQuietest(); if (B("It's the selection", false, "Select a bit with only background first")) { bgFrom = selStart; bgTo = selEnd; cleanOn = true; SelectAll(); } if (bgTo > bgFrom || rumble > 0 || gaps > 0) { if (B("On", cleanOn)) cleanOn = !cleanOn; if (B("Hear what's taken out", hearRemoved)) { hearRemoved = !hearRemoved; SourceChanged(); } } if (cleanJob != null) { End(); GUILayout.Label("cleaning...", sHint); } break;
                case Tool.Voice: { string g = null; foreach (var p in AudioVoice.All) { if (p.group != g) { End(); g = p.group; GUILayout.Label(g, sHint); } if (B(p.name, VoiceIs(p))) VoiceUse(p); } if (voice.Any || VoiceNow != null) { End(); if (B("Back to normal")) ResetTool(Tool.Voice); } break; }
                case Tool.Vary: foreach (var p in new[] { ("Subtle", 1.2f, 1.5f, 3f, 6f), ("Clear", 3f, 3f, 8f, 15f), ("Wild", 6f, 6f, 16f, 30f) }) if (B(p.Item1, Mathf.Approximately(varPitch, p.Item2) && Mathf.Approximately(varTone, p.Item4))) { varPitch = p.Item2; varVolume = p.Item3; varTone = p.Item4; varStart = p.Item5; } if (B("Shuffle")) varSeed = UnityEngine.Random.Range(1, 99999); bool all = varTimes != null && varTimes.Length > 1; if (B(all ? "■ Stop" : "▶ Hear all", all)) { if (all) Stop(); else PlayVariations(-1, varCount); } int now = VarNow; for (int i = -1; i < varCount; i++) if (B(i < 0 ? "orig" : (i + 1).ToString(), varTimes != null && now == i)) PlayVariations(i, i + 1); if (B("Save all " + varCount)) SaveVariations(); break;
            }
            End();
        }

        // ------------------------------------------------------------------ everything, by category

        void Row(Par p, float width, float labelW = 78)
        {
            float lo = p.lo(), hi = p.hi(), v = p.get(); var r = GUILayoutUtility.GetRect(width, 18, GUILayout.Width(width)); bool off = !Mathf.Approximately(v, p.def < 0 && p.name == "End" ? Length : p.def); GUI.Label(new Rect(r.x, r.y, labelW, 18), new GUIContent(p.name, p.tip), new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Ink(off ? 0.95f : 0.6f) }, clipping = TextClipping.Clip }); float n = GUI.HorizontalSlider(new Rect(r.x + labelW, r.y + 1, Mathf.Max(20, width - labelW - 46), 16), Mathf.Clamp(v, lo, hi), lo, hi); if (!Mathf.Approximately(n, Mathf.Clamp(v, lo, hi))) { p.set(p.whole ? Mathf.Round(n) : n); GUI.changed = true; }
            var fr = new Rect(r.xMax - 42, r.y, 42, 16); EditorGUI.BeginChangeCheck(); float f = EditorGUI.DelayedFloatField(fr, float.Parse(Num(p, v), System.Globalization.CultureInfo.CurrentCulture), new GUIStyle(EditorStyles.miniTextField) { alignment = TextAnchor.MiddleRight }); if (EditorGUI.EndChangeCheck()) p.set(Mathf.Clamp(f, lo, hi)); var e = Event.current; if (e.type == EventType.MouseDown && e.clickCount == 2 && new Rect(r.x, r.y, labelW, 18).Contains(e.mousePosition)) { p.set(p.name == "End" ? Length : p.def); GUI.changed = true; e.Use(); }
        }
        void DenseTool(Tool t, float width)
        {
            var info = Info[t]; bool on = IsOn(t); var h = GUILayoutUtility.GetRect(width, 20, GUILayout.Width(width)); EditorGUI.DrawRect(new Rect(h.x, h.y + 6, 8, 8), on ? info.tint : new Color(info.tint.r, info.tint.g, info.tint.b, 0.3f)); GUI.Label(new Rect(h.x + 14, h.y + 1, width - 60, 18), new GUIContent(info.title, info.help), new GUIStyle(sTileTitle) { fontSize = 11, normal = { textColor = Ink(on ? 0.95f : 0.75f) } }); if (on && GUI.Button(new Rect(h.xMax - 40, h.y + 2, 40, 16), "reset", EditorStyles.miniButton)) ResetTool(t); if (t == Tool.Voice) Extras(t, width); foreach (var p in Pars(t)) Row(p, width); if (t != Tool.Voice) Extras(t, width); GUILayout.Space(7);
        }
        void DenseColumn(Rect r, int[] cats, ref Vector2 scroll)
        {
            EditorGUI.DrawRect(r, Panel); GUILayout.BeginArea(new Rect(r.x + 10, r.y + 6, r.width - 14, r.height - 10)); scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); float w = r.width - 34;
            foreach (int c in cats) { Cat(c, w); foreach (var t in Categories[c].tools) DenseTool(t, w); GUILayout.Space(6); } GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        void Cat(int c, float w) { var h = GUILayoutUtility.GetRect(w, 20, GUILayout.Width(w)); EditorGUI.DrawRect(new Rect(h.x, h.yMax - 3, w, 1), new Color(Categories[c].tint.r, Categories[c].tint.g, Categories[c].tint.b, 0.6f)); GUI.Label(h, Categories[c].name, new GUIStyle(sHead) { normal = { textColor = Categories[c].tint } }); }
        void Dense(Rect r)
        {
            // as many columns as fit; every category stays in one piece, in order
            int n = Mathf.Clamp(Mathf.FloorToInt(r.width / 235f), 1, 6); int[][] sets = n >= 6 ? new[] { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 5 } } : n == 5 ? new[] { new[] { 0 }, new[] { 1, 4 }, new[] { 2 }, new[] { 3 }, new[] { 5 } } : n == 4 ? new[] { new[] { 0 }, new[] { 1, 2 }, new[] { 3 }, new[] { 4, 5 } } : n == 3 ? new[] { new[] { 0, 1 }, new[] { 2, 4, 5 }, new[] { 3 } } : n == 2 ? new[] { new[] { 0, 1, 5 }, new[] { 2, 4, 3 } } : new[] { new[] { 0, 1, 2, 3, 4, 5 } };
            EditorGUI.DrawRect(r, Panel); GUILayout.BeginArea(new Rect(r.x + 8, r.y + 4, r.width - 10, r.height - 6)); layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); float w = (r.width - 28 - 14 * (n - 1)) / n; GUILayout.BeginHorizontal(); for (int c = 0; c < sets.Length; c++) { GUILayout.BeginVertical(GUILayout.Width(w)); foreach (int k in sets[c]) { Cat(k, w); foreach (var t in Categories[k].tools) DenseTool(t, w); } GUILayout.EndVertical(); if (c < sets.Length - 1) GUILayout.Space(14); } GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ dials

        void Knob(Par p, Rect r, Color tint)
        {
            int id = GUIUtility.GetControlID("AudioKnob".GetHashCode(), FocusType.Passive, r); var e = Event.current; float lo = p.lo(), hi = p.hi(), v = Mathf.Clamp(p.get(), lo, hi), k = Mathf.InverseLerp(lo, hi, v), k0 = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, p.name == "End" ? Length : p.def)); var c = new Vector2(r.center.x, r.y + 22); EditorGUIUtility.AddCursorRect(r, MouseCursor.SlideArrow);
            if (e.type == EventType.Repaint)
            {
                Handles.BeginGUI(); var from = new Vector3(Mathf.Sin(-135 * Mathf.Deg2Rad), -Mathf.Cos(-135 * Mathf.Deg2Rad)); Handles.color = Ink(0.14f); Handles.DrawSolidArc(c, Vector3.forward, from, 270, 18); bool off = !Mathf.Approximately(v, p.name == "End" ? Length : p.def); float a0 = -135 + 270 * k0, a1 = -135 + 270 * k; var f = new Vector3(Mathf.Sin(a0 * Mathf.Deg2Rad), -Mathf.Cos(a0 * Mathf.Deg2Rad)); Handles.color = new Color(tint.r, tint.g, tint.b, off ? 1 : 0.4f); Handles.DrawSolidArc(c, Vector3.forward, f, a1 - a0, 18); Handles.color = Panel; Handles.DrawSolidDisc(c, Vector3.forward, 13); Handles.color = r.Contains(e.mousePosition) || GUIUtility.hotControl == id ? Color.white : tint; var d = new Vector3(Mathf.Sin(a1 * Mathf.Deg2Rad), -Mathf.Cos(a1 * Mathf.Deg2Rad)); Handles.DrawAAPolyLine(3f, (Vector3)c + d * 5, (Vector3)c + d * 16); Handles.EndGUI();
                GUI.Label(new Rect(r.x - 6, r.y + 40, r.width + 12, 14), new GUIContent(p.name), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, normal = { textColor = Ink(0.75f) }, clipping = TextClipping.Clip }); GUI.Label(new Rect(r.x - 6, r.y + 51, r.width + 12, 13), Num(p, p.get()), new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, normal = { textColor = Ink(off ? 0.95f : 0.45f) } });
            }
            GUI.Label(r, new GUIContent("", p.name + (p.tip.Length > 0 ? ": " + p.tip : "") + ". Drag up and down, double-click puts it back")); if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { if (e.clickCount == 2) { p.set(p.name == "End" ? Length : p.def); GUI.changed = true; } else GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { float nv = v + (e.delta.x - e.delta.y) / 150f * (hi - lo) * (e.shift ? 0.2f : 1); p.set(Mathf.Clamp(p.whole ? nv : nv, lo, hi)); GUI.changed = true; e.Use(); Repaint(); } if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); } if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition) && e.control) { p.set(Mathf.Clamp(v - e.delta.y * (hi - lo) * 0.01f, lo, hi)); GUI.changed = true; e.Use(); }
        }
        void Knobs(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); GUILayout.BeginArea(new Rect(r.x + 8, r.y + 4, r.width - 10, r.height - 6)); layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); float full = r.width - 28; int n = full > 900 ? 2 : 1; float w = (full - 20 * (n - 1)) / n; int[][] sets = n == 2 ? new[] { new[] { 0, 1, 5 }, new[] { 2, 4, 3 } } : new[] { new[] { 0, 1, 2, 4, 3, 5 } }; GUILayout.BeginHorizontal();
            for (int col = 0; col < sets.Length; col++)
            {
                GUILayout.BeginVertical(GUILayout.Width(w)); foreach (int c in sets[col]) { Cat(c, w); foreach (var t in Categories[c].tools) { var info = Info[t]; var ps = Pars(t); bool on = IsOn(t); GUILayout.BeginHorizontal(); GUILayout.BeginVertical(GUILayout.Width(104)); GUILayout.Label(new GUIContent(info.title, info.help), new GUIStyle(sTileTitle) { fontSize = 11, wordWrap = true, normal = { textColor = on ? info.tint : Ink(0.75f) } }, GUILayout.Width(100)); if (on && GUILayout.Button("reset", EditorStyles.miniButton, GUILayout.Width(42))) ResetTool(t); GUILayout.EndVertical(); GUILayout.BeginVertical(GUILayout.Width(w - 110)); int per = Mathf.Max(3, Mathf.FloorToInt((w - 112) / 56f)); for (int i = 0; i < ps.Length; i += per) { var row = GUILayoutUtility.GetRect(w - 112, 66, GUILayout.Width(w - 112)); for (int k = i; k < Mathf.Min(ps.Length, i + per); k++) Knob(ps[k], new Rect(row.x + (k - i) * 56, row.y + 1, 48, 62), info.tint); } Extras(t, w - 116); GUILayout.EndVertical(); GUILayout.EndHorizontal(); GUILayout.Space(4); } } GUILayout.EndVertical(); if (col < sets.Length - 1) GUILayout.Space(20);
            }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ mixing desk

        void Mixer(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); GUILayout.BeginArea(new Rect(r.x + 4, r.y + 2, r.width - 8, r.height - 4)); layScroll = GUILayout.BeginScrollView(layScroll, GUI.skin.horizontalScrollbar, GUIStyle.none); GUILayout.BeginHorizontal(); float h = Mathf.Max(150, r.height - 26);
            foreach (var c in Categories) foreach (var t in c.tools)
                {
                    var info = Info[t]; var ps = Pars(t); float w = Mathf.Max(150, ps.Length * 44 + 10); bool on = IsOn(t); GUILayout.BeginVertical(GUILayout.Width(w)); var hd = GUILayoutUtility.GetRect(w, 22, GUILayout.Width(w)); EditorGUI.DrawRect(hd, new Color(info.tint.r, info.tint.g, info.tint.b, on ? 0.45f : 0.16f)); GUI.Label(new Rect(hd.x + 6, hd.y + 2, w - 48, 18), new GUIContent(info.title, info.help), new GUIStyle(sTileTitle) { fontSize = 10 }); if (on && GUI.Button(new Rect(hd.xMax - 40, hd.y + 3, 38, 16), "reset", EditorStyles.miniButton)) ResetTool(t);
                    float slH = Mathf.Clamp(h * (t == Tool.Voice ? 0.42f : 0.5f), 64, 220); if (ps.Length > 0) { var row = GUILayoutUtility.GetRect(w, slH + 30, GUILayout.Width(w)); for (int i = 0; i < ps.Length; i++) { var p = ps[i]; float lo = p.lo(), hi = p.hi(), v = Mathf.Clamp(p.get(), lo, hi); var s = new Rect(row.x + 12 + i * 44, row.y + 4, 26, slH); float n = GUI.VerticalSlider(new Rect(s.x + 8, s.y, 14, s.height), v, hi, lo); if (!Mathf.Approximately(n, v)) { p.set(p.whole ? Mathf.Round(n) : n); GUI.changed = true; } GUI.Label(new Rect(s.x - 8, s.yMax, 44, 12), new GUIContent(p.name, p.name + ": " + p.tip), new GUIStyle(EditorStyles.miniLabel) { fontSize = 8, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, normal = { textColor = Ink(0.8f) } }); GUI.Label(new Rect(s.x - 8, s.yMax + 11, 44, 12), Num(p, p.get()), new GUIStyle(EditorStyles.miniLabel) { fontSize = 8, alignment = TextAnchor.MiddleCenter, normal = { textColor = info.tint } }); } }
                    Extras(t, w - 6); GUILayout.EndVertical(); GUILayout.Space(5);
                }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ the others

        void AllCards(Rect r)
        {
            sPill = sPillNarrow; GUILayout.BeginArea(r); int n = Mathf.Clamp(Mathf.FloorToInt((r.width - 15) / 280f), 1, 5); float cw = (r.width - 15 - 6 * (n - 1)) / n; if (Event.current.type == EventType.Layout) { cardCols.Clear(); var hs = new float[n]; for (int c = 0; c < n; c++) cardCols.Add(new List<Tool>()); foreach (var t in EditTools) { int best = 0; for (int c = 1; c < n; c++) if (hs[c] < hs[best] - 20) best = c; cardCols[best].Add(t); hs[best] += cardH.TryGetValue((int)t, out var h) ? h : 110; } }
            layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); GUILayout.BeginHorizontal(); for (int c = 0; c < cardCols.Count; c++) { GUILayout.BeginVertical(GUILayout.Width(cw)); foreach (var t in cardCols[c]) Card(t, cw, null); GUILayout.EndVertical(); if (c < cardCols.Count - 1) GUILayout.Space(6); } GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea(); sPill = sPillWide;
        }
        void Tabs(Rect r)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 34), Panel); float x = r.x + 6; for (int i = 0; i < Categories.Length; i++) { var c = Categories[i]; float w = sTileTitle.CalcSize(new GUIContent(c.name)).x + 34; var b = new Rect(x, r.y + 4, w, 26); bool on = i == tabNow, used = c.tools.Any(IsOn); EditorGUI.DrawRect(b, on ? new Color(c.tint.r * 0.5f, c.tint.g * 0.5f, c.tint.b * 0.5f, 0.8f) : b.Contains(Event.current.mousePosition) ? PanelHi : Ink(0.04f)); if (used) EditorGUI.DrawRect(new Rect(b.xMax - 13, b.center.y - 3, 6, 6), c.tint); GUI.Label(b, c.name, new GUIStyle(sTileTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 11 }); if (GUI.Button(b, GUIContent.none, GUIStyle.none) && !on) { tabNow = i; GUIUtility.ExitGUI(); } x += w + 4; }
            var area = new Rect(r.x, r.y + 38, r.width, r.height - 38); sPill = sPillNarrow; GUILayout.BeginArea(area); layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); var tools = Categories[Mathf.Clamp(tabNow, 0, Categories.Length - 1)].tools; int n = Mathf.Clamp(Mathf.FloorToInt((r.width - 15) / 300f), 1, Mathf.Max(1, tools.Length)); float cw = Mathf.Min(520, (r.width - 15 - 6 * (n - 1)) / n); GUILayout.BeginHorizontal(); for (int c = 0; c < n; c++) { GUILayout.BeginVertical(GUILayout.Width(cw)); for (int i = c; i < tools.Length; i += n) Card(tools[i], cw, null); GUILayout.EndVertical(); GUILayout.Space(6); } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea(); sPill = sPillWide;
        }
        void ListPanel(Rect r)
        {
            const float lw = 196; var left = new Rect(r.x, r.y, lw, r.height); EditorGUI.DrawRect(left, Panel); GUILayout.BeginArea(new Rect(left.x, left.y + 4, left.width, left.height - 6)); layScroll2 = GUILayout.BeginScrollView(layScroll2, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            foreach (var t in EditTools) { var info = Info[t]; var b = GUILayoutUtility.GetRect(10, 28, GUILayout.ExpandWidth(true)); b = new Rect(b.x + 6, b.y + 1, b.width - 10, 26); bool sel = listNow == (int)t; EditorGUI.DrawRect(b, sel ? new Color(info.tint.r * 0.5f, info.tint.g * 0.5f, info.tint.b * 0.5f, 0.8f) : b.Contains(Event.current.mousePosition) ? PanelHi : Ink(0.035f)); EditorGUI.DrawRect(new Rect(b.x, b.y, 3, b.height), info.tint); GUI.Label(new Rect(b.x + 10, b.y + 4, b.width - 24, 18), info.title, sTileTitle); if (IsOn(t)) EditorGUI.DrawRect(new Rect(b.xMax - 13, b.center.y - 4, 8, 8), info.tint); if (GUI.Button(b, GUIContent.none, GUIStyle.none) && !sel) { listNow = (int)t; GUIUtility.ExitGUI(); } } GUILayout.EndScrollView(); GUILayout.EndArea();
            var right = new Rect(r.x + lw + 6, r.y, r.width - lw - 6, r.height); var tool = EditTools.Contains((Tool)listNow) ? (Tool)listNow : Tool.Trim; sPill = sPillNarrow; GUILayout.BeginArea(right); layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); Card(tool, right.width - 16, null); GUILayout.Label(Info[tool].help, sHint); GUILayout.EndScrollView(); GUILayout.EndArea(); sPill = sPillWide;
        }
        [SerializeField] int foldOpen = 1 << (int)Tool.Trim | 1 << (int)Tool.Level;
        void Folding(Rect r)
        {
            sPill = sPillNarrow; GUILayout.BeginArea(r); layScroll = GUILayout.BeginScrollView(layScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); int n = r.width > 860 ? 3 : r.width > 560 ? 2 : 1; float cw = (r.width - 15 - 6 * (n - 1)) / n; int[][] sets = n == 3 ? new[] { new[] { 0, 1 }, new[] { 2, 4 }, new[] { 3, 5 } } : n == 2 ? new[] { new[] { 0, 1, 5 }, new[] { 2, 4, 3 } } : new[] { new[] { 0, 1, 2, 3, 4, 5 } }; GUILayout.BeginHorizontal();
            for (int c = 0; c < sets.Length; c++) { GUILayout.BeginVertical(GUILayout.Width(cw)); foreach (int k in sets[c]) { Cat(k, cw); foreach (var t in Categories[k].tools) { if ((foldOpen & 1 << (int)t) != 0) Card(t, cw, null, () => { foldOpen &= ~(1 << (int)t); }); else { var info = Info[t]; var b = GUILayoutUtility.GetRect(cw, 30, GUILayout.Width(cw)); b.height -= 3; bool on = IsOn(t); EditorGUI.DrawRect(b, b.Contains(Event.current.mousePosition) ? PanelHi : Panel); EditorGUI.DrawRect(new Rect(b.x, b.y, 3, b.height), on ? info.tint : new Color(info.tint.r, info.tint.g, info.tint.b, 0.35f)); GUI.Label(new Rect(b.x + 12, b.y, b.width * 0.55f, b.height), info.title, new GUIStyle(sTileTitle) { alignment = TextAnchor.MiddleLeft }); GUI.Label(new Rect(b.x + b.width * 0.45f, b.y, b.width * 0.55f - 28, b.height), Now(t), new GUIStyle(sTileSub) { alignment = TextAnchor.MiddleRight, normal = { textColor = on ? info.tint : Ink(0.45f) } }); GUI.Label(new Rect(b.xMax - 22, b.y, 18, b.height), "▾", new GUIStyle(sTileSub) { alignment = TextAnchor.MiddleCenter }); if (GUI.Button(b, new GUIContent("", info.help), GUIStyle.none)) { foldOpen |= 1 << (int)t; GUIUtility.ExitGUI(); } } } } GUILayout.EndVertical(); if (c < sets.Length - 1) GUILayout.Space(6); }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView(); GUILayout.EndArea(); sPill = sPillWide;
        }
        string Now(Tool t)
        {
            switch (t)
            {
                case Tool.Trim: return Fmt(selEnd - selStart) + (IsOn(t) ? " kept" : ", all of it"); case Tool.Fades: return IsOn(t) ? fadeIn.ToString("0.00") + " in, " + fadeOut.ToString("0.00") + " out" : "none"; case Tool.Loop: return LoopOn ? loopFade.ToString("0.00") + " s blend" : "off"; case Tool.Find: return regions.Count > 0 ? regions.Count + " pieces" : ""; case Tool.Level: return levelMode == 2 ? loudness.ToString("0") + " LUFS" : levelMode == 1 ? "full" : gainDb.ToString("+0.0;-0.0;0") + " dB"; case Tool.Pitch: return Mathf.Abs(pitch) < 0.05f ? "normal" : pitch.ToString("+0.0;-0.0"); case Tool.Tone: { if (!HasTone) return "normal"; foreach (var p in Tones) if (Same(p.s)) return p.name; return "changed"; } case Tool.Reverb: return revAmount > 0.001f ? Mathf.RoundToInt(revAmount * 100) + "%" : "off"; case Tool.Echo: return echo > 0.001f ? Mathf.RoundToInt(echo * 100) + "%" : "off"; case Tool.Clean: return cleanOn ? "on" : "off"; case Tool.Vary: return varCount + " versions"; case Tool.Draw: return volPts.Count > 0 ? volPts.Count + " points" : "off"; case Tool.Match: return matchTo ? matchTo.name : "off"; case Tool.Voice: return VoiceNow != null ? VoiceNow.name : voice.Any ? "changed" : "normal";
            }
            return "";
        }

        // ------------------------------------------------------------------ voices

        [SerializeField] VoiceFx voice = new VoiceFx();
        static bool SameVoice(VoiceFx a, VoiceFx b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        bool VoiceIs(AudioVoice.Preset p) => SameVoice(p.v, voice) && Same(p.tone) && Mathf.Approximately(p.pitch, pitch) && Mathf.Approximately(p.room, revAmount) && Mathf.Approximately(p.echo, echo);
        AudioVoice.Preset VoiceNow => AudioVoice.All.FirstOrDefault(VoiceIs);
        void VoiceUse(AudioVoice.Preset p) { voice = p.v.Copy(); apart = true; stretch = 1; pitch = p.pitch; bass = p.tone.bass; middle = p.tone.mid; treble = p.tone.treble; muffle = p.tone.muffle; thin = p.tone.thin; grit = p.tone.grit; echo = p.echo; echoTime = p.echoTime; echoAgain = p.echoAgain; revAmount = p.room; revSize = p.roomSize; GUI.changed = true; previewDirty = true; }
        void VoiceGUI(float width)
        {
            float x = 0; string g = null; bool open = false; void End() { if (open) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); open = false; } }
            foreach (var p in AudioVoice.All) { if (p.group != g) { End(); g = p.group; GUILayout.Label(g, sHint); GUILayout.BeginHorizontal(); open = true; x = 0; } float w = sPill.CalcSize(new GUIContent(p.name)).x + 3; if (x + w > width - 28 && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w; if (Pill(p.name, VoiceIs(p))) VoiceUse(p); } End(); GUILayout.Space(3); voiceMore = EditorGUILayout.Foldout(voiceMore, "Change it yourself", true); if (voiceMore) foreach (var p in Pars(Tool.Voice)) Row(p, width - 26, 88);
        }
        [SerializeField] bool voiceMore;
    }
}
