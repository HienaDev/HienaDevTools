using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The Layers page: a few sounds on top of each other, saved as one. Each is a block you slide along the time;
    /// its ends cut it, its top corners fade it.</summary>
    public partial class AudioTrimmer
    {
        [Serializable]
        public class Layer
        {
            public AudioClip clip; public string file = "", name = "";   // (file: a sound brought over from Edit, kept outside Assets)
            public float at, cutStart, cutEnd; public bool mute, solo, flip; public SoundFx fx = new SoundFx();
            public string Key => !string.IsNullOrEmpty(file) ? file : clip ? AssetDatabase.GetAssetPath(clip) : "";
        }
        class Sound { public float[] s; public int ch, rate; public float[] peaks; public float Length => s.Length / (float)Mathf.Max(1, ch) / Mathf.Max(1, rate); public long stamp; }
        const int PeakBlock = 128; [SerializeField] float layerSize;

        [SerializeField] List<Layer> layers = new List<Layer>(); [SerializeField] int layer = -1; [SerializeField] string mixName = ""; [SerializeField] bool mixLoop; [SerializeField] float mixGain;
        static readonly Dictionary<string, Sound> sounds = new Dictionary<string, Sound>();
        Vector2 layerScroll; bool mixOn; double mixRestartAt = -1; float mixCursor, lastMixHead; int layDrag = -1, layPart; float layOff, layView; readonly float[] layOld = new float[4];
        static readonly Color[] LayerTints = { new Color(1f, 0.72f, 0.38f), new Color(0.5f, 0.78f, 1f), new Color(0.6f, 0.9f, 0.55f), new Color(0.9f, 0.6f, 0.9f), new Color(0.95f, 0.85f, 0.45f), new Color(0.5f, 0.9f, 0.85f) };

        static Sound SoundOf(Layer l)
        {
            string key = l.Key; if (string.IsNullOrEmpty(key)) return null; long stamp = File.Exists(key) ? File.GetLastWriteTimeUtc(key).Ticks : 0; if (sounds.TryGetValue(key, out var s) && s.stamp == stamp) return s;
            float[] d; int ch, rt; if (string.IsNullOrEmpty(l.file) && l.clip ? !AudioIO.Read(l.clip, out d, out ch, out rt, out _) : !File.Exists(key) || !ReadWav(File.ReadAllBytes(key), out d, out ch, out rt)) { sounds[key] = new Sound { s = new float[0], ch = 1, rate = 44100, peaks = new float[0], stamp = stamp }; return sounds[key]; }
            int n = d.Length / ch; var pk = new float[(n + PeakBlock - 1) / PeakBlock]; for (int i = 0; i < n; i++) for (int c = 0; c < ch; c++) pk[i / PeakBlock] = Mathf.Max(pk[i / PeakBlock], Mathf.Abs(d[i * ch + c]));
            return sounds[key] = new Sound { s = d, ch = ch, rate = rt, peaks = pk, stamp = stamp };
        }
        static float SpeedOf(Layer l) => l.fx.Speed;
        static float LengthOf(Layer l) { var s = SoundOf(l); return s == null ? 0 : Mathf.Max(0.005f, s.Length - l.cutStart - l.cutEnd) / SpeedOf(l); }
        float MixLength => layers.Count == 0 ? 1f : Mathf.Max(0.05f, layers.Max(l => l.at + Mathf.Max(LengthOf(l), DoneLength(l))));

        // ---- a layer with everything done to it (worked out in the background, kept until something changes)
        class DoneSound { public string key; public float[] s; public int ch, rate; public float Length => s.Length / (float)ch / rate; }
        static readonly Dictionary<Layer, DoneSound> done = new Dictionary<Layer, DoneSound>(); Layer doneNow; System.Threading.Tasks.Task<DoneSound> doneTask;
        static string DoneKey(Layer l) { float g = l.fx.gainDb; l.fx.gainDb = 0; var k = l.Key + "|" + l.cutStart.ToString("0.0000") + "|" + l.cutEnd.ToString("0.0000") + l.flip + JsonUtility.ToJson(l.fx); l.fx.gainDb = g; return k; }
        static float DoneLength(Layer l) => done.TryGetValue(l, out var d) && d.s != null ? d.Length : 0;
        static DoneSound Do(string key, Sound s, float cs, float ce, bool flip, SoundFx fx)
        {
            int n = s.s.Length / s.ch, f0 = Mathf.Clamp(Mathf.RoundToInt(cs * s.rate), 0, Mathf.Max(0, n - 1)), f1 = Mathf.Clamp(n - Mathf.RoundToInt(ce * s.rate), f0 + 1, n); var a = new float[(f1 - f0) * s.ch]; Array.Copy(s.s, f0 * s.ch, a, 0, a.Length);
            if (flip) { int m = a.Length / s.ch; for (int i = 0; i < m / 2; i++) for (int c = 0; c < s.ch; c++) { float v = a[i * s.ch + c]; a[i * s.ch + c] = a[(m - 1 - i) * s.ch + c]; a[(m - 1 - i) * s.ch + c] = v; } }
            // (the fades are measured on the piece as it's heard)
            return Finish(key, a, s, fx);
        }
        static DoneSound Finish(string key, float[] a, Sound s, SoundFx fx) { fx.Shape(a, s.ch, s.rate); a = fx.Run(a, s.ch, s.rate, false); fx.Level(a, s.ch, s.rate); return new DoneSound { key = key, s = a, ch = s.ch, rate = s.rate }; }
        DoneSound Done(Layer l) { string k = DoneKey(l); if (done.TryGetValue(l, out var d) && d.key == k) return d; var s = SoundOf(l); if (s == null || s.s.Length == 0) return null; if (l.fx.matchTo) AudioDsp.Learn(l.fx.matchTo); return done[l] = Do(k, s, l.cutStart, l.cutEnd, l.flip, l.fx); }
        void LayerTick()
        {
            if (doneTask != null) { if (!doneTask.IsCompleted) return; if (doneTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && doneTask.Result != null) done[doneNow] = doneTask.Result; doneTask = null; doneNow = null; Repaint(); }
            if (page != Page.Layers || GUIUtility.hotControl != 0) return;
            foreach (var l in layers) { string k = DoneKey(l); if (done.TryGetValue(l, out var d) && d.key == k) continue; var s = SoundOf(l); if (s == null || s.s.Length == 0) continue; if (l.fx.matchTo) AudioDsp.Learn(l.fx.matchTo); var m = l.fx.matchTo; var fx = l.fx.Copy(); fx.matchTo = m; float cs = l.cutStart, ce = l.cutEnd; bool fl = l.flip; doneNow = l; var gone = done.Keys.Where(x => !layers.Contains(x)).ToList(); foreach (var g in gone) done.Remove(g); doneTask = System.Threading.Tasks.Task.Run(() => Do(k, s, cs, ce, fl, fx)); break; }
        }
        bool Heard(Layer l) => !l.mute && (l.solo || !layers.Any(x => x.solo));

        // ------------------------------------------------------------------ the sound

        /// <summary>All the layers together. Turned down when they'd crackle.</summary>
        float[] Mix(out int ch, out int rt, out float turnedDown)
        {
            ch = 1; rt = 44100; turnedDown = 0; var on = layers.Where(l => Heard(l) && SoundOf(l) != null && SoundOf(l).s.Length > 0).ToList(); if (on.Count > 0) { rt = on.Max(l => SoundOf(l).rate); ch = Mathf.Min(2, on.Max(l => SoundOf(l).ch)); }
            foreach (var l in on) Done(l); int frames = Mathf.CeilToInt(MixLength * rt); var o = new float[Mathf.Max(1, frames) * ch];
            foreach (var l in on)
            {
                var d = Done(l); if (d == null) continue; int n = d.s.Length / d.ch; double step = d.rate / (double)rt; float g = l.fx.levelMode == 0 ? Mathf.Pow(10f, l.fx.gainDb / 20f) : 1f; int f0 = Mathf.RoundToInt(l.at * rt), m = (int)(n / step);
                for (int f = 0; f < m; f++) { int at = f0 + f; if (at < 0 || at >= frames) continue; double p = f * step; int i0 = (int)p; if (i0 >= n) break; int i1 = Mathf.Min(n - 1, i0 + 1); float k = (float)(p - i0); for (int c = 0; c < ch; c++) { int sc = Mathf.Min(c, d.ch - 1); float a = d.s[i0 * d.ch + sc], b = d.s[i1 * d.ch + sc]; o[at * ch + c] += (a + (b - a) * k) * g; } }
            }
            float gg = Mathf.Pow(10f, mixGain / 20f); if (mixGain != 0) for (int i = 0; i < o.Length; i++) o[i] *= gg;
            float pk = AudioFx.PeakOf(o); if (pk > 0.99f) { turnedDown = 20 * Mathf.Log10(pk / 0.99f); float k = 0.99f / pk; for (int i = 0; i < o.Length; i++) o[i] *= k; }
            return o;
        }
        static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }

        void PlayMix(float from)
        {
            Stop(); if (layers.Count == 0) return; var d = Mix(out int ch, out int rt, out _); Make(d, ch, rt); source.loop = mixLoop; float len = d.Length / (float)ch / rt; if (from >= len - 0.02f) from = 0; source.time = Mathf.Clamp(from, 0, Mathf.Max(0, len - 0.005f)); source.Play(); mixOn = true; playing = true; previewDirty = true; restartAt = -1;
        }
        float MixHead => mixOn && source && source.isPlaying ? source.time : mixCursor;
        void MixChanged() { if (mixOn && source && source.isPlaying) mixRestartAt = EditorApplication.timeSinceStartup + 0.15; Repaint(); }
        void MixTick()
        {
            if (mixRestartAt > 0 && EditorApplication.timeSinceStartup > mixRestartAt && GUIUtility.hotControl == 0) { mixRestartAt = -1; if (mixOn && source && source.isPlaying) PlayMix(source.time); }
            if (mixOn && source && source.isPlaying && Mathf.Abs(MixHead - lastMixHead) > 0.01f) { lastMixHead = MixHead; Repaint(); }
            if (mixOn && (!source || !source.isPlaying)) { mixOn = false; mixCursor = 0; playing = false; Repaint(); }
        }

        // ------------------------------------------------------------------ the page

        void LayersPage(Rect r)
        {
            var e = Event.current; if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition)) { var l = Sounds(DragAndDrop.objectReferences); if (l.Count > 0) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var c in l) AddLayer(new Layer { clip = c, name = c.name }); } e.Use(); } }
            if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0) { if (e.keyCode == KeyCode.Space) { if (mixOn) { mixCursor = MixHead; Stop(); } else PlayMix(mixCursor); e.Use(); } else if (e.keyCode == KeyCode.Delete && layer >= 0 && layer < layers.Count) { layers.RemoveAt(layer); if (linked == layer) linked = -1; else if (linked > layer) linked--; layer = -1; MixChanged(); e.Use(); } }
            var top = new Rect(r.x + 6, r.y + 6, r.width - 12, 44); EditorGUI.DrawRect(top, Panel);
            GUILayout.BeginArea(new Rect(top.x + 10, top.y + 8, top.width - 20, 30)); GUILayout.BeginHorizontal();
            if (Big(mixOn ? "❚❚" : "▶", "Play them all together (Space)", 28, 46)) { if (mixOn) { mixCursor = MixHead; Stop(); } else PlayMix(mixCursor); }
            if (GUILayout.Button(new GUIContent("■", "Stop"), GUILayout.Height(28), GUILayout.Width(32))) { Stop(); mixOn = false; mixCursor = 0; }
            GUILayout.BeginVertical(); GUILayout.Space(3); GUILayout.BeginHorizontal(); if (Pill("Loop", mixLoop)) { mixLoop = !mixLoop; if (source) source.loop = mixLoop; } GUILayout.Space(10);
            if (Pill("+ Add sounds", false, "The sounds selected in the Project window, or choose one. You can also drop sounds here")) AddSelected();
            using (new EditorGUI.DisabledScope(!clip || samples == null)) if (Pill("+ The one in Edit", false, "The sound open in Edit, with everything you did to it. They stay linked: what you change there changes here")) AddEdited();
            GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.FlexibleSpace();
            GUILayout.Label($"{Fmt(MixHead)} / {Fmt(MixLength)}", sTime, GUILayout.Width(140), GUILayout.Height(28)); GUILayout.EndHorizontal(); GUILayout.EndArea();
            PickerLayers();

            float most = Mathf.Max(140, r.height - 220); if (layerSize <= 0) layerSize = Mathf.Clamp(r.height * 0.42f, 170, 360); float bottomH = Mathf.Clamp(layerSize, 130, most); var mid = new Rect(r.x + 6, top.yMax + 6, r.width - 12, r.height - 44 - bottomH - 24); layerSize = SplitBar(3, new Rect(r.x, mid.yMax - 1, r.width, 9), false, bottomH, -1, 130, most); EditorGUI.DrawRect(mid, new Color(0.07f, 0.075f, 0.09f));
            if (layers.Count == 0) GUI.Label(mid, "Drop two or more sounds here.\nThey play on top of each other; slide them to change when each one starts.", sEmpty);
            else Lanes(mid);

            var bot = new Rect(r.x + 6, mid.yMax + 7, r.width - 12, bottomH); float sw = 250; var det = new Rect(bot.x, bot.y, bot.width - sw - 6, bot.height); var sav = new Rect(det.xMax + 6, bot.y, sw, bot.height); EditorGUI.DrawRect(sav, Panel);
            if (layer >= 0 && layer < layers.Count)
            {
                var l = layers[layer]; EditorGUI.DrawRect(new Rect(det.x, det.y, det.width, 32), Panel); EditorGUI.DrawRect(new Rect(det.x, det.y, 4, 32), LayerTints[layer % LayerTints.Length]); GUILayout.BeginArea(new Rect(det.x + 14, det.y + 5, det.width - 24, 26)); GUILayout.BeginHorizontal(); EditorGUI.BeginChangeCheck();
                l.name = EditorGUILayout.TextField(l.name, GUILayout.Width(150)); EditorGUIUtility.labelWidth = 50; Slide("Starts", "Seconds", ref l.at, 0f, Mathf.Max(5f, MixLength + 2f)); EditorGUIUtility.labelWidth = 0; GUILayout.Space(6);
                if (Pill("Backwards", l.flip, "Plays it from the end to the start")) l.flip = !l.flip; if (Pill("Copy", false, "Another one just like it, after this one")) { var c = JsonUtility.FromJson<Layer>(JsonUtility.ToJson(l)); c.clip = l.clip; c.fx.matchTo = l.fx.matchTo; c.at = l.at + LengthOf(l); layers.Insert(layer + 1, c); if (linked > layer) linked++; layer++; GUI.changed = true; }
                var bg = GUI.backgroundColor; GUI.backgroundColor = Accent; if (GUILayout.Button(new GUIContent("Open in Edit", "Edits this layer with everything Edit has (cleaning, drawing the volume...). What you do there changes the layer. Double-clicking a layer does the same"), sPillWide)) { int i = layer; EditorApplication.delayCall += () => { EditLayer(i); Repaint(); }; } GUI.backgroundColor = bg; if (Pill("Remove", false, "Delete")) { layers.RemoveAt(layer); if (linked == layer) linked = -1; else if (linked > layer) linked--; layer = -1; GUI.changed = true; }
                if (EditorGUI.EndChangeCheck()) MixChanged(); GUILayout.EndHorizontal(); GUILayout.EndArea();
                if (layer >= 0) { EditorGUI.BeginChangeCheck(); WithFx(l.fx, LengthOf(l) * SpeedOf(l), () => Rack(new Rect(det.x, det.y + 38, det.width, det.height - 38), LayerTools, rackLayer, ref layerSetScroll)); if (EditorGUI.EndChangeCheck()) MixChanged(); }
            }
            else { EditorGUI.DrawRect(det, Panel); GUI.Label(new Rect(det.x + 14, det.y + 10, det.width - 28, 40), "Click a sound to change it. Drag it to change when it starts, its ends to cut it, the squares at its top corners to fade it. Double-click opens it in Edit.", sHint); }
            GUILayout.BeginArea(new Rect(sav.x + 12, sav.y + 8, sav.width - 24, sav.height - 12)); GUILayout.Label("SAVE THEM AS ONE SOUND", sHead); if (mixName.Length == 0 && layers.Count > 0) mixName = layers[0].name + "_mix"; mixName = EditorGUILayout.TextField(mixName, new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 22 }, GUILayout.Height(22)); EditorGUIUtility.labelWidth = 60; if (Slide("Overall", "dB", ref mixGain, -24f, 12f)) MixChanged(); EditorGUIUtility.labelWidth = 0;
            using (new EditorGUI.DisabledScope(layers.Count == 0)) if (Big("Save  " + (CleanName(mixName).Length > 0 ? CleanName(mixName) : "mix") + ".wav", "A new sound; the ones it's made of don't change", 30)) EditorApplication.delayCall += SaveMix; GUILayout.EndArea();
        }

        void Lanes(Rect r)
        {
            var e = Event.current; const float infoW = 210, rowH = 64, rulerH = 20; var lanes = new Rect(r.x + infoW, r.y, r.width - infoW - 16, r.height); if (layDrag < 0) layView = Mathf.Max(0.3f, MixLength * 1.12f); float k = lanes.width / layView;
            // the time
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, rulerH), new Color(0.1f, 0.105f, 0.125f)); float st = NiceStep(layView / Mathf.Max(1f, lanes.width / 80f)); for (float t = 0; t <= layView; t += st) { EditorGUI.DrawRect(new Rect(lanes.x + t * k, r.y + rulerH - 6, 1, 6), new Color(0.6f, 0.6f, 0.6f)); GUI.Label(new Rect(lanes.x + t * k + 2, r.y, 70, rulerH), Fmt(t), EditorStyles.miniLabel); }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && new Rect(lanes.x, r.y, lanes.width, rulerH).Contains(e.mousePosition)) { mixCursor = Mathf.Clamp((e.mousePosition.x - lanes.x) / k, 0, MixLength); if (mixOn) PlayMix(mixCursor); e.Use(); Repaint(); }
            var view = new Rect(r.x, r.y + rulerH, r.width, r.height - rulerH); layerScroll = GUI.BeginScrollView(view, layerScroll, new Rect(0, 0, r.width - 16, Mathf.Max(view.height - 2, layers.Count * rowH)), false, false);
            float lx = infoW; int id = GUIUtility.GetControlID("AudioLayers".GetHashCode(), FocusType.Passive); bool ch = false;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i]; var s = SoundOf(l); var row = new Rect(0, i * rowH, r.width - 16, rowH - 2); var tint = LayerTints[i % LayerTints.Length]; EditorGUI.DrawRect(row, i == layer ? new Color(1, 1, 1, 0.05f) : new Color(1, 1, 1, 0.015f));
                var info = new Rect(row.x, row.y, infoW - 6, row.height); EditorGUI.DrawRect(info, Panel); EditorGUI.DrawRect(new Rect(info.x, info.y, 4, info.height), tint); GUI.Label(new Rect(info.x + 10, info.y + 4, info.width - 76, 18), new GUIContent(l.name, l.Key), sTileTitle);
                var bg = GUI.backgroundColor; if (l.mute) GUI.backgroundColor = new Color(1f, 0.45f, 0.4f); if (GUI.Button(new Rect(info.xMax - 62, info.y + 4, 26, 18), new GUIContent("M", "Mute"), EditorStyles.miniButton)) { l.mute = !l.mute; ch = true; } GUI.backgroundColor = l.solo ? Accent : bg; if (GUI.Button(new Rect(info.xMax - 33, info.y + 4, 26, 18), new GUIContent("S", "Solo: only this one (and others on solo)"), EditorStyles.miniButton)) { l.solo = !l.solo; ch = true; } GUI.backgroundColor = bg;
                EditorGUI.BeginChangeCheck(); float g = GUI.HorizontalSlider(new Rect(info.x + 10, info.y + 32, info.width - 76, 16), l.fx.gainDb, -36f, 18f); if (EditorGUI.EndChangeCheck()) { l.fx.gainDb = Mathf.Abs(g) < 0.7f ? 0 : g; layer = i; ch = true; } GUI.Label(new Rect(info.xMax - 60, info.y + 30, 58, 18), l.fx.gainDb.ToString("+0.0;-0.0;0") + " dB", sTileSub);
                if (s == null || s.s.Length == 0) { GUI.Label(new Rect(lx + 10, row.y + 20, 300, 20), "This sound can't be read.", sHint); continue; }
                float len = LengthOf(l); var b = new Rect(lx + l.at * k, row.y + 4, Mathf.Max(6, len * k), row.height - 8); bool heard = Heard(l); float dim = heard ? 1f : 0.35f;
                if (e.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(b, new Color(tint.r, tint.g, tint.b, (i == layer ? 0.28f : 0.16f) * dim)); float sp = SpeedOf(l), gk = Mathf.Pow(10f, l.fx.gainDb / 20f); int n = Mathf.Max(1, (int)b.width);
                    for (int x = 0; x < n; x++)
                    {
                        float t = x / (float)n * len, src = l.cutStart + (l.flip ? len - t : t) * sp, t2 = src + len / n * sp; int p0 = Mathf.Clamp((int)(src * s.rate / PeakBlock), 0, s.peaks.Length - 1), p1 = Mathf.Clamp((int)(t2 * s.rate / PeakBlock), p0, s.peaks.Length - 1); float pk = 0; int hop = Mathf.Max(1, (p1 - p0) / 12); for (int q = p0; q <= p1; q += hop) pk = Mathf.Max(pk, s.peaks[q]);
                        float st2 = t * sp, full = len * sp; if (l.fx.fadeIn > 0 && st2 < l.fx.fadeIn) pk *= SoundFx.Shaped(st2 / l.fx.fadeIn, l.fx.fadeShape); if (l.fx.fadeOut > 0 && st2 > full - l.fx.fadeOut) pk *= SoundFx.Shaped((full - st2) / l.fx.fadeOut, l.fx.fadeShape); if (l.fx.volume.Count > 0) pk *= SoundFx.VolumeAt(l.fx.volume, st2); float h = Mathf.Max(1, Mathf.Min(1, pk * gk) * (b.height - 4)); EditorGUI.DrawRect(new Rect(b.x + x, b.center.y - h / 2, 1, h), new Color(tint.r, tint.g, tint.b, 0.9f * dim));
                    }
                    if (done.TryGetValue(l, out var dn) && dn.s != null && dn.Length > len + 0.02f)
                    {
                        int x1 = Mathf.Min((int)((dn.Length - len) * k), (int)(lanes.width - (b.xMax - lx))); int fr = dn.s.Length / dn.ch; for (int x = 0; x < x1; x += 2) { float tt = len + x / k; int q0 = Mathf.Clamp((int)(tt * dn.rate), 0, fr - 1), q1 = Mathf.Clamp(q0 + (int)(2 / k * dn.rate), q0 + 1, fr); float pk = 0; int hop = Mathf.Max(1, (q1 - q0) / 24); for (int q = q0; q < q1; q += hop) pk = Mathf.Max(pk, Mathf.Abs(dn.s[q * dn.ch])); float h = Mathf.Max(1, Mathf.Min(1, pk * gk) * (b.height - 4)); EditorGUI.DrawRect(new Rect(b.xMax + x, b.center.y - h / 2, 1, h), new Color(tint.r, tint.g, tint.b, 0.45f * dim)); }
                    }
                    EditorGUI.DrawRect(new Rect(b.x, b.y, 2, b.height), tint); EditorGUI.DrawRect(new Rect(b.xMax - 2, b.y, 2, b.height), tint);
                    if (i == layer) { EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 1), tint); EditorGUI.DrawRect(new Rect(b.x, b.yMax - 1, b.width, 1), tint); EditorGUI.DrawRect(FadeSquare(b, l.fx.fadeIn / sp * k), FadeTint); EditorGUI.DrawRect(FadeSquare(b, b.width - l.fx.fadeOut / sp * k), FadeTint); }
                    if (i == linked) GUI.Label(new Rect(b.x + 6, b.yMax - 18, 90, 16), "open in Edit", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.95f, 0.7f, 0.95f) } });
                }
                EditorGUIUtility.AddCursorRect(new Rect(b.x - 4, b.y, 8, b.height), MouseCursor.ResizeHorizontal); EditorGUIUtility.AddCursorRect(new Rect(b.xMax - 4, b.y, 8, b.height), MouseCursor.ResizeHorizontal); EditorGUIUtility.AddCursorRect(new Rect(b.x + 4, b.y, Mathf.Max(1, b.width - 8), b.height), MouseCursor.Pan);
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition) && e.mousePosition.x >= lx - 6)
                {
                    GUIUtility.keyboardControl = 0; bool sel = layer == i; layer = i; if (e.clickCount == 2) { int ii = i; EditorApplication.delayCall += () => { EditLayer(ii); Repaint(); }; e.Use(); break; } var grown = new Rect(b.x - 5, b.y, b.width + 10, b.height);
                    if (grown.Contains(e.mousePosition))
                    {
                        layDrag = i; layPart = sel && Grow(FadeSquare(b, l.fx.fadeIn / SpeedOf(l) * k)).Contains(e.mousePosition) ? 3 : sel && Grow(FadeSquare(b, b.width - l.fx.fadeOut / SpeedOf(l) * k)).Contains(e.mousePosition) ? 4 : Mathf.Abs(e.mousePosition.x - b.x) < 5 ? 1 : Mathf.Abs(e.mousePosition.x - b.xMax) < 5 ? 2 : 0; layOff = e.mousePosition.x; layOld[0] = l.at; layOld[1] = l.cutStart; layOld[2] = l.cutEnd; layOld[3] = layPart == 3 ? l.fx.fadeIn : l.fx.fadeOut; GUIUtility.hotControl = id;
                    }
                    e.Use(); Repaint();
                }
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && layDrag >= 0 && layDrag < layers.Count) { DragLayer(layers[layDrag], layPart, (e.mousePosition.x - layOff) / k, 6f / k); ch = true; e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; layDrag = -1; e.Use(); Repaint(); }
            GUI.EndScrollView(); float hx = lanes.x + MixHead * k; if (hx <= lanes.xMax) EditorGUI.DrawRect(new Rect(hx, r.y, 2, r.height), new Color(1f, 0.35f, 0.3f));
            if (ch) MixChanged();
        }
        static Rect FadeSquare(Rect b, float x) => new Rect(b.x + x - 5, b.y, 10, 10);
        static Rect Grow(Rect x) => new Rect(x.x - 3, x.y - 2, x.width + 6, x.height + 5);

        /// <summary>part: 0 move, 1 its start, 2 its end, 3 fade in, 4 fade out. Moving sticks to the starts and ends of the others.</summary>
        void DragLayer(Layer l, int part, float by, float stick)
        {
            var s = SoundOf(l); float sp = SpeedOf(l), len = LengthOf(l);
            if (part == 0)
            {
                float at = Mathf.Max(0, layOld[0] + by); foreach (var o in layers) if (o != l) foreach (var t in new[] { o.at, o.at + LengthOf(o) }) { if (Mathf.Abs(at - t) < stick) at = t; else if (Mathf.Abs(at + len - t) < stick) at = Mathf.Max(0, t - len); }
                l.at = at < stick ? 0 : at;
            }
            else if (part == 1) { float c = Mathf.Clamp(layOld[1] + by * sp, 0, s.Length - l.cutEnd - 0.01f); l.at = Mathf.Max(0, layOld[0] + (c - layOld[1]) / sp); l.cutStart = c; }
            else if (part == 2) l.cutEnd = Mathf.Clamp(layOld[2] - by * sp, 0, s.Length - l.cutStart - 0.01f);
            else if (part == 3) l.fx.fadeIn = Mathf.Clamp(layOld[3] + by * sp, 0, len * sp - l.fx.fadeOut);
            else l.fx.fadeOut = Mathf.Clamp(layOld[3] - by * sp, 0, len * sp - l.fx.fadeIn);
        }

        void AddLayer(Layer l) { layers.Add(l); layer = layers.Count - 1; MixChanged(); }
        const int LayerPicker = 812377;
        void AddSelected() { var l = Sounds(Selection.objects); if (l.Count > 0) foreach (var c in l) AddLayer(new Layer { clip = c, name = c.name }); else EditorGUIUtility.ShowObjectPicker<AudioClip>(null, false, "", LayerPicker); }
        void PickerLayers() { var e = Event.current; if (e.type == EventType.ExecuteCommand && e.commandName == "ObjectSelectorClosed" && EditorGUIUtility.GetObjectPickerControlID() == LayerPicker) { if (EditorGUIUtility.GetObjectPickerObject() is AudioClip c) AddLayer(new Layer { clip = c, name = c.name }); e.Use(); } }

        void AddEdited()
        {
            if (IsLinked) { SyncLinked(true); layer = linked; return; }
            var l = new Layer { clip = clip, name = CleanName(saveName).Length > 0 ? CleanName(saveName) : BaseName }; layers.Add(l); layer = linked = layers.Count - 1; SyncLinked(true); MixChanged();
        }

        void SaveMix()
        {
            var first = layers.FirstOrDefault(l => l.clip); var dir = first != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(first.clip)).Replace('\\', '/') : clip ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip)).Replace('\\', '/') : "Assets"; if (!dir.StartsWith("Assets")) dir = "Assets";
            var name = CleanName(mixName); if (name.Length == 0) name = "mix"; var target = dir + "/" + name + ".wav";
            if (File.Exists(target)) { int pick = EditorUtility.DisplayDialogComplex("Name taken", $"There's already a sound called {name}.wav there.", "Replace it", "Cancel", "Keep both"); if (pick == 1) return; if (pick == 2) target = AssetDatabase.GenerateUniqueAssetPath(target); }
            WriteMix(target);
        }
        void WriteMix(string target) { var d = Mix(out int ch, out int rt, out float down); File.WriteAllBytes(target, WriteWav(d, ch, rt)); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate); var c = AssetDatabase.LoadAssetAtPath<AudioClip>(target); if (c) EditorGUIUtility.PingObject(c); ShowNotification(new GUIContent("Saved " + Path.GetFileName(target) + (down > 0.3f ? $" (turned down {down:0.0} dB so it doesn't crackle)" : ""))); }
    }
}
