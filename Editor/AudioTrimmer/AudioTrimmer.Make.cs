using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The Make page (sounds from nothing) and the Record page (from the microphone).</summary>
    public partial class AudioTrimmer
    {
        // ================================================================ make

        [SerializeField] int makeKind, makeSeed = 1; [SerializeField] List<float> makeVals = new List<float>(); [SerializeField] string makeName = ""; [SerializeField] bool makeAuto = true; [SerializeField] int makeCount = 5;
        float[] made; string madeKey = ""; float[] madePeaks; double makeAt = -1; bool makeOn; Vector2 makeScroll; string makeLast = "";

        AudioMaker.Kind MakeK => AudioMaker.Kinds[Mathf.Clamp(makeKind, 0, AudioMaker.Kinds.Length - 1)];
        void MakeValues(float[] v) { makeVals = v.ToList(); }
        void MakeCheck() { var k = MakeK; if (makeVals.Count != k.knobs.Length) makeVals = k.knobs.Select(x => x.def).ToList(); }
        void MakeNow() { MakeCheck(); string key = makeKind + "|" + makeSeed + "|" + string.Join(",", makeVals.Select(v => v.ToString("0.0000"))); if (key == madeKey && made != null) return; madeKey = key; made = AudioMaker.Make(makeKind, makeVals.ToArray(), makeSeed); int n = made.Length / 2, w = 900; madePeaks = new float[w]; for (int f = 0; f < n; f++) { int x = Mathf.Min(w - 1, (int)((long)f * w / n)); madePeaks[x] = Mathf.Max(madePeaks[x], Mathf.Max(Mathf.Abs(made[f * 2]), Mathf.Abs(made[f * 2 + 1]))); } }
        void PlayMade() { MakeNow(); Stop(); Make(made, 2, AudioMaker.Rate); source.loop = MakeK.loops; source.time = 0; source.Play(); makeOn = true; playing = true; previewDirty = true; restartAt = -1; }
        void MakeChanged() { if (makeAuto) makeAt = EditorApplication.timeSinceStartup + 0.05; makeName = makeName == makeLast ? "" : makeName; Repaint(); }
        void MakeTick() { if (makeAt > 0 && EditorApplication.timeSinceStartup > makeAt && GUIUtility.hotControl == 0) { makeAt = -1; PlayMade(); } if (makeOn && source && source.isPlaying) Repaint(); if (makeOn && (!source || !source.isPlaying)) { makeOn = false; playing = false; Repaint(); } }
        string MakeReady() { var k = MakeK; foreach (var r in k.ready) if (r.v.Length == makeVals.Count && r.v.Select((v, i) => Mathf.Abs(v - makeVals[i]) < 0.0001f).All(b => b)) return r.name; return ""; }

        void MakePage(Rect r)
        {
            MakeCheck(); MakeNow(); var e = Event.current; if (e.type == EventType.KeyDown && GUIUtility.keyboardControl == 0) { if (e.keyCode == KeyCode.Space) { if (makeOn) Stop(); else PlayMade(); e.Use(); } else if (e.keyCode == KeyCode.R) { makeSeed = UnityEngine.Random.Range(1, 999999); MakeChanged(); e.Use(); } }
            const float lw = 190; var left = new Rect(r.x + 6, r.y + 6, lw, r.height - 12); EditorGUI.DrawRect(left, Panel); GUILayout.BeginArea(new Rect(left.x, left.y + 8, left.width, left.height - 10)); GUILayout.Label("WHAT KIND", new GUIStyle(sHead) { padding = new RectOffset(12, 0, 0, 0) });
            for (int i = 0; i < AudioMaker.Kinds.Length; i++) { var k = AudioMaker.Kinds[i]; var b = GUILayoutUtility.GetRect(10, 44, GUILayout.ExpandWidth(true)); b = new Rect(b.x + 8, b.y + 2, b.width - 16, 40); bool on = i == makeKind; EditorGUI.DrawRect(b, on ? new Color(k.tint.r * 0.5f, k.tint.g * 0.5f, k.tint.b * 0.5f, 0.75f) : b.Contains(e.mousePosition) ? PanelHi : Ink(0.035f)); EditorGUI.DrawRect(new Rect(b.x, b.y, 3, b.height), k.tint); GUI.Label(new Rect(b.x + 12, b.y + 3, b.width - 14, 18), k.name, on ? new GUIStyle(sTileTitle) { normal = { textColor = Color.white } } : sTileTitle); GUI.Label(new Rect(b.x + 12, b.y + 20, b.width - 14, 16), k.about, sTileSub); if (GUI.Button(b, GUIContent.none, GUIStyle.none) && !on) { makeKind = i; MakeValues(k.ready[0].v); MakeChanged(); GUIUtility.ExitGUI(); } }
            GUILayout.EndArea();

            var kind = MakeK; var right = new Rect(left.xMax + 6, left.y, r.xMax - left.xMax - 12, left.height); var wave = new Rect(right.x, right.y, right.width, Mathf.Clamp(right.height * 0.3f, 110, 240)); EditorGUI.DrawRect(wave, new Color(0.07f, 0.075f, 0.09f));
            if (e.type == EventType.Repaint && madePeaks != null) { int w = (int)wave.width; for (int x = 0; x < w; x++) { float h = Mathf.Max(1, madePeaks[Mathf.Min(madePeaks.Length - 1, x * madePeaks.Length / w)] * (wave.height - 16)); EditorGUI.DrawRect(new Rect(wave.x + x, wave.center.y - h / 2, 1, h), kind.tint); } if (makeOn && source && source.isPlaying && source.clip) EditorGUI.DrawRect(new Rect(wave.x + wave.width * source.time / source.clip.length, wave.y, 2, wave.height), new Color(1f, 0.35f, 0.3f)); }
            GUI.Label(new Rect(wave.x + 10, wave.y + 6, 300, 18), $"{made.Length / 2f / AudioMaker.Rate:0.00} s", sHint); if (GUI.Button(wave, GUIContent.none, GUIStyle.none)) { if (makeOn) Stop(); else PlayMade(); }
            var bar = new Rect(right.x, wave.yMax + 6, right.width, 40); EditorGUI.DrawRect(bar, Panel); GUILayout.BeginArea(new Rect(bar.x + 10, bar.y + 6, bar.width - 20, 30)); GUILayout.BeginHorizontal();
            if (Big(makeOn ? "■" : "▶", "Play (Space)", 28, 46)) { if (makeOn) Stop(); else PlayMade(); }
            if (GUILayout.Button(new GUIContent("Another one", "The same settings, a new take (R)"), GUILayout.Height(28), GUILayout.Width(96))) { makeSeed = UnityEngine.Random.Range(1, 999999); MakeChanged(); }
            if (GUILayout.Button(new GUIContent("A bit different", "Nudges every slider"), GUILayout.Height(28), GUILayout.Width(100))) { for (int i = 0; i < makeVals.Count; i++) { var kn = kind.knobs[i]; if (kn.name == "Surface" || kn.name == "Twice" || kn.name == "Notes") continue; makeVals[i] = Mathf.Clamp(makeVals[i] + UnityEngine.Random.Range(-0.14f, 0.14f) * (kn.hi - kn.lo) * (kn.name == "Length" ? 0.35f : 1), kn.lo, kn.hi); } makeSeed = UnityEngine.Random.Range(1, 999999); MakeChanged(); }
            if (GUILayout.Button(new GUIContent("Surprise me", "Every slider somewhere new"), GUILayout.Height(28), GUILayout.Width(92))) { for (int i = 0; i < makeVals.Count; i++) { var kn = kind.knobs[i]; float u = UnityEngine.Random.value; makeVals[i] = kn.name == "Length" ? Mathf.Lerp(kn.lo, Mathf.Lerp(kn.def, kn.hi, 0.35f), u * u) : Mathf.Lerp(kn.lo, kn.hi, u); } makeSeed = UnityEngine.Random.Range(1, 999999); MakeChanged(); }
            GUILayout.BeginVertical(); GUILayout.Space(3); if (Pill("Play as I change it", makeAuto)) makeAuto = !makeAuto; GUILayout.EndVertical(); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();

            float sw = 270; var set = new Rect(right.x, bar.yMax + 6, right.width - sw - 6, right.yMax - bar.yMax - 6); var sav = new Rect(set.xMax + 6, set.y, sw, set.height); EditorGUI.DrawRect(set, Panel); EditorGUI.DrawRect(new Rect(set.x, set.y, 3, set.height), kind.tint); EditorGUI.DrawRect(sav, Panel);
            GUILayout.BeginArea(new Rect(set.x + 14, set.y + 10, set.width - 26, set.height - 14)); makeScroll = GUILayout.BeginScrollView(makeScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); GUILayout.Label("START FROM", sHead); sPill = sPillNarrow; float px = 0; GUILayout.BeginHorizontal(); string now = MakeReady();
            foreach (var p in kind.ready) { float w = sPill.CalcSize(new GUIContent(p.name)).x + 5; if (px + w > set.width - 60 && px > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); px = 0; } px += w; if (Pill(p.name, p.name == now)) { MakeValues(p.v); MakeChanged(); } }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); sPill = sPillWide; GUILayout.Space(8); GUILayout.Label("CHANGE IT", sHead); EditorGUIUtility.labelWidth = 130;
            for (int i = 0; i < kind.knobs.Length; i++) { var kn = kind.knobs[i]; float v = makeVals[i]; bool whole = kn.name == "Surface" || kn.name == "Notes"; if (kn.name == "Surface") { string[] names = { "Stone", "Wood", "Grass", "Gravel", "Snow", "Metal", "Water" }; GUILayout.BeginHorizontal(); GUILayout.Label("Surface", GUILayout.Width(126)); sPill = sPillNarrow; for (int s = 0; s < names.Length; s++) if (Pill(names[s], Mathf.RoundToInt(v) == s)) { makeVals[i] = s; MakeChanged(); } sPill = sPillWide; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); } else if (kn.name == "Twice") { bool t = EditorGUILayout.ToggleLeft(new GUIContent("Two swings"), v > 0.5f); if (t != v > 0.5f) { makeVals[i] = t ? 1 : 0; MakeChanged(); } } else if (Slide(kn.name, kn.tip, ref v, kn.lo, kn.hi)) { makeVals[i] = whole ? Mathf.Round(v) : v; MakeChanged(); } }
            EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView(); GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(sav.x + 12, sav.y + 10, sav.width - 24, sav.height - 14)); GUILayout.Label("KEEP IT", sHead); if (makeName.Length == 0) makeName = makeLast = (now.Length > 0 ? now : kind.name).ToLowerInvariant().Replace(' ', '_'); makeName = EditorGUILayout.TextField(makeName, new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 22 }, GUILayout.Height(22));
            if (Big("Save  " + MakeName + ".wav", "In Assets/Sounds", 30)) EditorApplication.delayCall += () => SaveMade(1);
            GUILayout.BeginHorizontal(); makeCount = EditorGUILayout.IntSlider(makeCount, 2, 12); if (GUILayout.Button(new GUIContent("Save that many", "Different takes: name_01, name_02..."), GUILayout.Width(104))) EditorApplication.delayCall += () => SaveMade(makeCount); GUILayout.EndHorizontal(); GUILayout.Space(8);
            if (GUILayout.Button(new GUIContent("Save and open in Edit", "To cut it, add a room or an echo, and so on"), GUILayout.Height(24))) EditorApplication.delayCall += () => { var c = SaveMade(1); if (c) { Stop(); Load(c); page = Page.Edit; Repaint(); } };
            if (GUILayout.Button(new GUIContent("Add to Layers", "To mix it with other sounds (it isn't saved in the project until you want to)"), GUILayout.Height(24))) { layers.Add(new Layer { file = Keep(made, 2, AudioMaker.Rate), name = MakeName }); layer = layers.Count - 1; ShowNotification(new GUIContent("Added to Layers")); }
            GUILayout.EndArea();
        }
        string MakeName { get { var n = CleanName(makeName); return n.Length > 0 ? n : "sound"; } }

        AudioClip SaveMade(int many, string folder = "Assets/Sounds")
        {
            MakeNow(); if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder)); AudioClip first = null;
            for (int i = 0; i < many; i++) { var d = i == 0 && many == 1 ? made : AudioMaker.Make(makeKind, makeVals.ToArray(), makeSeed + i * 101); var t = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MakeName + (many > 1 ? $"_{i + 1:00}" : "") + ".wav"); File.WriteAllBytes(t, WriteWav(d, 2, AudioMaker.Rate)); AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate); if (!first) first = AssetDatabase.LoadAssetAtPath<AudioClip>(t); }
            if (first) EditorGUIUtility.PingObject(first); ShowNotification(new GUIContent(many > 1 ? "Saved " + many + " sounds" : "Saved " + (first ? first.name : ""))); return first;
        }

        // ================================================================ record

        [SerializeField] string micName = "", recName = "recording"; AudioClip micClip; double micFrom; float micLevel, micPeak; float[] rec; int recRate = 44100; float[] recPeaks; readonly List<float> micTrail = new List<float>(); string micProblem = "";
        const int MicMax = 600;
        bool Recording => micClip;

        void RecordPage(Rect r)
        {
            var box = new Rect(r.center.x - Mathf.Min(420, r.width / 2 - 10), r.y + 20, Mathf.Min(840, r.width - 20), Mathf.Min(430, r.height - 40)); EditorGUI.DrawRect(box, Panel); var devs = Microphone.devices; GUILayout.BeginArea(new Rect(box.x + 20, box.y + 16, box.width - 40, box.height - 30)); GUILayout.Label("Record from the microphone", sBig); GUILayout.Space(4);
            if (devs.Length == 0) { GUILayout.Label("No microphone found. Plug one in, and check that Windows lets apps use it (Settings > Privacy > Microphone).", sHint); GUILayout.EndArea(); return; }
            int d = Mathf.Max(0, Array.IndexOf(devs, micName)); using (new EditorGUI.DisabledScope(Recording)) { int nd = EditorGUILayout.Popup("Microphone", d, devs.Select(x => x.Replace('/', ' ')).ToArray()); micName = devs[nd]; }
            GUILayout.Space(8); var wave = GUILayoutUtility.GetRect(10, 130, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(wave, new Color(0.07f, 0.075f, 0.09f));
            if (Event.current.type == EventType.Repaint)
            {
                int w = (int)wave.width; if (Recording) { for (int x = 0; x < w && x < micTrail.Count; x++) { float h = Mathf.Max(1, micTrail[micTrail.Count - 1 - x] * (wave.height - 8)); EditorGUI.DrawRect(new Rect(wave.xMax - 1 - x, wave.center.y - h / 2, 1, h), new Color(1f, 0.45f, 0.4f)); } }
                else if (recPeaks != null) for (int x = 0; x < w; x++) { float h = Mathf.Max(1, recPeaks[Mathf.Min(recPeaks.Length - 1, x * recPeaks.Length / w)] * (wave.height - 8)); EditorGUI.DrawRect(new Rect(wave.x + x, wave.center.y - h / 2, 1, h), new Color(1f, 0.72f, 0.38f)); }
            }
            if (!Recording && rec == null) GUI.Label(wave, "Nothing recorded yet.", sEmpty);
            var lev = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(lev, Ink(0.08f)); if (Recording) { EditorGUI.DrawRect(new Rect(lev.x, lev.y, lev.width * Mathf.Clamp01(micLevel), lev.height), micPeak > 0.98f ? new Color(1f, 0.35f, 0.3f) : micPeak > 0.7f ? new Color(1f, 0.8f, 0.3f) : new Color(0.45f, 0.85f, 0.5f)); } GUILayout.Space(8);
            GUILayout.BeginHorizontal(); var old = GUI.backgroundColor; GUI.backgroundColor = new Color(1f, 0.35f, 0.3f); if (GUILayout.Button(Recording ? "■  Stop" : "●  Record", new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 14 }, GUILayout.Height(36), GUILayout.Width(150))) { if (Recording) MicStop(); else MicStart(); } GUI.backgroundColor = old;
            using (new EditorGUI.DisabledScope(Recording || rec == null)) if (GUILayout.Button("▶  Hear it", GUILayout.Height(36), GUILayout.Width(100))) { Stop(); Make(rec, 1, recRate); source.loop = false; source.time = 0; source.Play(); makeOn = true; playing = true; previewDirty = true; }
            GUILayout.Label(Recording ? Fmt((float)(EditorApplication.timeSinceStartup - micFrom)) + (micPeak > 0.98f ? "   too loud: move back or turn the microphone down" : "") : rec != null ? Fmt(rec.Length / (float)recRate) : "", new GUIStyle(sTileTitle) { alignment = TextAnchor.MiddleLeft }, GUILayout.Height(36)); GUILayout.EndHorizontal(); if (micProblem.Length > 0) GUILayout.Label(micProblem, sHint); GUILayout.Space(10);
            using (new EditorGUI.DisabledScope(Recording || rec == null)) { GUILayout.BeginHorizontal(); recName = EditorGUILayout.TextField(recName, new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 22 }, GUILayout.Height(22)); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); if (Big("Save and open in Edit", "In Assets/Sounds, with the silence at both ends cut off when you press Trim > Cut the silence off", 30)) EditorApplication.delayCall += () => { var c = SaveRec(); if (c) { Load(c); page = Page.Edit; Repaint(); } }; if (GUILayout.Button("Only save", GUILayout.Height(30))) EditorApplication.delayCall += () => SaveRec(); if (GUILayout.Button("Add to Layers", GUILayout.Height(30))) { layers.Add(new Layer { file = Keep(rec, 1, recRate), name = CleanName(recName).Length > 0 ? CleanName(recName) : "recording" }); layer = layers.Count - 1; ShowNotification(new GUIContent("Added to Layers")); } GUILayout.EndHorizontal(); }
            GUILayout.Label("It only listens between Record and Stop. Nothing is kept until you save it.", sHint); GUILayout.EndArea();
        }

        void MicStart()
        {
            Stop(); micProblem = ""; try { Microphone.GetDeviceCaps(micName, out int lo, out int hi); recRate = hi == 0 ? 44100 : Mathf.Clamp(44100, lo, hi); micClip = Microphone.Start(micName, false, MicMax, recRate); } catch (Exception x) { micClip = null; micProblem = x.Message; }
            if (!micClip) { micProblem = "The microphone couldn't be started. Check that Windows lets apps use it. " + micProblem; return; } micFrom = EditorApplication.timeSinceStartup; micTrail.Clear(); micPeak = micLevel = 0; rec = null; recPeaks = null;
        }
        void MicStop()
        {
            if (!micClip) return; int n = Microphone.GetPosition(micName); if (!Microphone.IsRecording(micName) && n == 0) n = micClip.samples; Microphone.End(micName); var c = micClip; micClip = null; if (n > 64) { var d = new float[n * c.channels]; c.GetData(d, 0); Recorded(d, c.channels, c.frequency); } else micProblem = "Nothing came in from that microphone."; DestroyImmediate(c); Repaint();
        }
        void Recorded(float[] d, int ch, int rt)
        {
            int n = d.Length / ch; rec = new float[n]; for (int f = 0; f < n; f++) { float s = 0; for (int c = 0; c < ch; c++) s += d[f * ch + c]; rec[f] = s / ch; } recRate = rt; recPeaks = new float[900]; for (int f = 0; f < n; f++) { int x = (int)((long)f * 900 / n); recPeaks[x] = Mathf.Max(recPeaks[x], Mathf.Abs(rec[f])); }
        }
        void MicTick()
        {
            if (!micClip) return; int at = Microphone.GetPosition(micName); int w = micClip.frequency / 30; if (at > w) { var d = new float[w * micClip.channels]; micClip.GetData(d, at - w); float p = 0; foreach (var v in d) p = Mathf.Max(p, Mathf.Abs(v)); micPeak = Mathf.Max(p, micPeak * 0.97f); micLevel = Mathf.Max(p, micLevel * 0.85f); micTrail.Add(p); if (micTrail.Count > 4000) micTrail.RemoveRange(0, 1000); }
            if (!Microphone.IsRecording(micName)) MicStop(); Repaint();
        }
        AudioClip SaveRec(string folder = "Assets/Sounds")
        {
            if (rec == null) return null; if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder)); var t = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (CleanName(recName).Length > 0 ? CleanName(recName) : "recording") + ".wav"); File.WriteAllBytes(t, WriteWav(rec, 1, recRate)); AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate); var c = AssetDatabase.LoadAssetAtPath<AudioClip>(t); if (c) EditorGUIUtility.PingObject(c); ShowNotification(new GUIContent("Saved " + Path.GetFileName(t))); return c;
        }
    }
}
