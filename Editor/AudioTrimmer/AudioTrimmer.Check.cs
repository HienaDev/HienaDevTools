using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The Check page (what's wrong with the project's sounds) and the Ambience page (a loop with sounds scattered over it).</summary>
    public partial class AudioTrimmer
    {
        // ================================================================ check

        public enum Wrong { TooLoud, TooQuiet, Crackles, SilentStart, SilentEnd, SameBothSides, OffCentre, BigInMemory, Unreadable, Silent }
        public class Checked { public AudioClip clip; public string path; public float lufs, peak, length, startGap, endGap; public List<(Wrong what, string say)> wrong = new List<(Wrong, string)>(); public bool tick; }
        List<Checked> checkedAll; List<string> checkTodo; int checkTotal; Vector2 checkScroll; [SerializeField] DefaultAsset checkFolder; int checkFilter = -1; int checkOpen = -1;
        static string Say(Wrong w) => w == Wrong.TooLoud ? "Too loud" : w == Wrong.TooQuiet ? "Too quiet" : w == Wrong.Crackles ? "Crackles" : w == Wrong.SilentStart ? "Starts late" : w == Wrong.SilentEnd ? "Silence at the end" : w == Wrong.SameBothSides ? "Could be mono" : w == Wrong.OffCentre ? "Off centre" : w == Wrong.BigInMemory ? "Big in memory" : w == Wrong.Silent ? "Silent" : "Can't be read";

        public static Checked CheckSound(float[] s, int ch, int rate)
        {
            var c = new Checked(); int n = s.Length / Mathf.Max(1, ch); c.length = n / (float)rate; c.peak = AudioFx.PeakOf(s); if (n == 0 || c.peak < 0.0005f) { c.lufs = -99; c.wrong.Add((Wrong.Silent, "there's nothing in it")); return c; } c.lufs = AudioFx.Loudness(s, ch, rate);
            int flat = 0, runs = 0; double dc = 0; for (int i = 0; i < s.Length; i++) { dc += s[i]; if (Mathf.Abs(s[i]) > 0.995f) { flat++; if (flat == 4) runs++; } else flat = 0; } if (runs > 2) c.wrong.Add((Wrong.Crackles, runs + " places where the top is cut off")); dc /= s.Length; if (Math.Abs(dc) > 0.02) c.wrong.Add((Wrong.OffCentre, "its middle is " + (dc * 100).ToString("0") + "% off, which clicks at the start and end"));
            AudioFx.Sounding(s, ch, rate, -50, 0, out int a, out int b); c.startGap = a / (float)rate; c.endGap = (n - b) / (float)rate; if (c.startGap > 0.03f && c.length < 12) c.wrong.Add((Wrong.SilentStart, (c.startGap * 1000).ToString("0") + " ms of nothing first, so it feels late")); if (c.endGap > 0.5f && c.length < 60) c.wrong.Add((Wrong.SilentEnd, c.endGap.ToString("0.0") + " s of nothing"));
            if (ch == 2) { double diff = 0, sum = 1e-9; for (int i = 0; i + 1 < s.Length; i += 2) { diff += Math.Abs(s[i] - s[i + 1]); sum += Math.Abs(s[i]) + Math.Abs(s[i + 1]); } if (diff / sum < 0.004) c.wrong.Add((Wrong.SameBothSides, "both sides are the same: mono halves its size")); }
            return c;
        }

        /// <summary>Compares the loudness of each with the others of its kind (short sounds, music and ambience).</summary>
        public static void Compare(List<Checked> l)
        {
            foreach (var group in l.Where(c => c.lufs > -90).GroupBy(c => c.length < 3 ? 0 : c.length < 20 ? 1 : 2)) { var s = group.Select(c => c.lufs).OrderBy(v => v).ToList(); if (s.Count < 4) continue; float mid = s[s.Count / 2]; string kind = group.Key == 0 ? "short sounds" : group.Key == 1 ? "middle-long sounds" : "long ones"; foreach (var c in group) { float d = c.lufs - mid; if (d > 8) c.wrong.Insert(0, (Wrong.TooLoud, d.ToString("0") + " dB over most " + kind)); else if (d < -11) c.wrong.Insert(0, (Wrong.TooQuiet, (-d).ToString("0") + " dB under most " + kind)); } }
        }

        void CheckStart()
        {
            var where = checkFolder ? AssetDatabase.GetAssetPath(checkFolder) : "Assets"; checkTodo = AssetDatabase.FindAssets("t:AudioClip", new[] { where }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList(); checkTotal = checkTodo.Count; checkedAll = new List<Checked>(); checkFilter = -1;
        }
        void CheckTick()
        {
            if (checkTodo == null || checkTodo.Count == 0) return; double t0 = EditorApplication.timeSinceStartup;
            while (checkTodo.Count > 0 && EditorApplication.timeSinceStartup - t0 < 0.03)
            {
                var p = checkTodo[0]; checkTodo.RemoveAt(0); var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p); if (!clip) continue; Checked c; if (!AudioIO.Read(clip, out var s, out int ch, out int rt, out _)) { c = new Checked { lufs = -99, length = clip.length }; c.wrong.Add((Wrong.Unreadable, "its Load Type keeps it from being looked at")); } else c = CheckSound(s, ch, rt); c.clip = clip; c.path = p;
                var imp = AssetImporter.GetAtPath(p) as AudioImporter; if (imp) { if (imp.forceToMono) c.wrong.RemoveAll(w => w.what == Wrong.SameBothSides); var d = imp.defaultSampleSettings; float mb = clip.samples * clip.channels * 2f / 1048576f; if (clip.length > 15 && d.loadType == AudioClipLoadType.DecompressOnLoad && mb > 4) c.wrong.Add((Wrong.BigInMemory, mb.ToString("0") + " MB while loaded: long ones are better streamed")); }
                checkedAll.Add(c);
            }
            if (checkTodo.Count == 0) { Compare(checkedAll); checkedAll = checkedAll.OrderByDescending(c => c.wrong.Count > 0).ThenBy(c => c.wrong.Count > 0 ? (int)c.wrong[0].what : 99).ThenBy(c => c.path).ToList(); }
            Repaint();
        }

        void CheckPage(Rect r)
        {
            var top = new Rect(r.x + 6, r.y + 6, r.width - 12, 64); EditorGUI.DrawRect(top, Panel); GUILayout.BeginArea(new Rect(top.x + 12, top.y + 8, top.width - 24, top.height - 12)); GUILayout.BeginHorizontal(); if (Big("Check the sounds", "Looks at every sound: too loud or quiet next to the others, crackling, silence, and so on. Nothing is changed", 26, 140)) CheckStart(); GUILayout.Label("in", GUILayout.Width(16)); checkFolder = (DefaultAsset)EditorGUILayout.ObjectField(checkFolder, typeof(DefaultAsset), false, GUILayout.Width(180)); GUILayout.Label(checkFolder ? "" : "(empty: the whole project)", sHint, GUILayout.Width(150)); GUILayout.FlexibleSpace();
            if (checkTodo != null && checkTodo.Count > 0) { GUILayout.Label((checkTotal - checkTodo.Count) + " of " + checkTotal, sTime, GUILayout.Width(90)); if (GUILayout.Button("Stop", GUILayout.Width(50))) { checkTodo.Clear(); Compare(checkedAll); } } else if (checkedAll != null) GUILayout.Label(checkedAll.Count(c => c.wrong.Count == 0) + " fine, " + checkedAll.Count(c => c.wrong.Count > 0) + " to look at", sTime, GUILayout.Width(170)); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if (checkedAll != null) { if (Pill("All", checkFilter == -1)) checkFilter = -1; foreach (Wrong w in Enum.GetValues(typeof(Wrong))) { int n = checkedAll.Count(c => c.wrong.Any(q => q.what == w)); if (n > 0 && Pill(Say(w) + " (" + n + ")", checkFilter == (int)w)) checkFilter = (int)w; } } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();
            var bot = new Rect(r.x + 6, r.yMax - 46, r.width - 12, 40); var mid = new Rect(r.x + 6, top.yMax + 6, r.width - 12, bot.y - top.yMax - 12); EditorGUI.DrawRect(mid, Panel); if (checkedAll == null) { GUI.Label(mid, "Press Check the sounds. It only looks: nothing is changed until you ask.", sEmpty); return; }
            var list = checkedAll.Where(c => c.clip && (checkFilter < 0 ? c.wrong.Count > 0 : c.wrong.Any(q => (int)q.what == checkFilter))).ToList(); if (list.Count == 0) { GUI.Label(mid, checkTodo != null && checkTodo.Count > 0 ? "listening..." : "Nothing to fix here.", sEmpty); }
            const float h = 30; checkScroll = GUI.BeginScrollView(mid, checkScroll, new Rect(0, 0, mid.width - 16, list.Count * h)); int first = Mathf.Max(0, (int)(checkScroll.y / h)), last = Mathf.Min(list.Count, first + (int)(mid.height / h) + 2);
            for (int i = first; i < last; i++) { var c = list[i]; var row = new Rect(0, i * h, mid.width - 16, h - 2); EditorGUI.DrawRect(row, Ink(i % 2 == 0 ? 0.03f : 0.015f)); c.tick = GUI.Toggle(new Rect(8, row.y + 6, 18, 18), c.tick, GUIContent.none); if (GUI.Button(new Rect(28, row.y + 4, 26, 20), "▶", EditorStyles.miniButton)) { Stop(); if (AudioIO.Read(c.clip, out var s, out int ch, out int rt, out _)) { Make(s, ch, rt); source.loop = false; source.time = 0; source.Play(); makeOn = playing = true; previewDirty = true; } } if (GUI.Button(new Rect(60, row.y + 5, 210, 18), new GUIContent(c.clip.name, c.path), sTileTitle)) EditorGUIUtility.PingObject(c.clip); GUI.Label(new Rect(274, row.y + 6, 120, 18), c.lufs > -90 ? $"{c.lufs:0.0} LUFS  {c.length:0.0} s" : $"{c.length:0.0} s", sTileSub); GUI.Label(new Rect(398, row.y + 6, row.width - 398 - 70, 18), new GUIContent(string.Join("     ", c.wrong.Select(w => Say(w.what) + ": " + w.say))), new GUIStyle(sTileSub) { normal = { textColor = new Color(1f, 0.75f, 0.45f) } }); if (GUI.Button(new Rect(row.xMax - 62, row.y + 4, 56, 20), "Edit", EditorStyles.miniButton)) { Stop(); Load(c.clip); page = Page.Edit; GUI.EndScrollView(); GUIUtility.ExitGUI(); } }
            GUI.EndScrollView(); EditorGUI.DrawRect(bot, Panel); GUILayout.BeginArea(new Rect(bot.x + 10, bot.y + 7, bot.width - 20, 30)); GUILayout.BeginHorizontal(); if (GUILayout.Button("Tick all of these", GUILayout.Width(110), GUILayout.Height(26))) foreach (var c in list) c.tick = true; if (GUILayout.Button("None", GUILayout.Width(50), GUILayout.Height(26))) foreach (var c in checkedAll) c.tick = false; var tk = checkedAll.Where(c => c.tick && c.clip).ToList(); GUILayout.Label(tk.Count + " ticked", sHint, GUILayout.Width(60)); GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(tk.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Make them mono and stream long ones", "Only import settings: the files don't change. Mono for those where both sides are the same, streaming for long ones"), GUILayout.Height(26))) EditorApplication.delayCall += () => { int n = FixImport(tk); ShowNotification(new GUIContent("Changed " + n + " import settings")); CheckStart(); };
                if (Big("Fix them in Batch...", "Opens Batch with these sounds and what they need ticked (silence cut, equally loud). You still press the button there", 26)) { batch.Clear(); batch.AddRange(tk.Select(c => c.clip)); bTrim = tk.Any(c => c.wrong.Any(w => w.what == Wrong.SilentStart || w.what == Wrong.SilentEnd)); bLoud = tk.Any(c => c.wrong.Any(w => w.what == Wrong.TooLoud || w.what == Wrong.TooQuiet)); bMono = false; bClean = bTone = bReverb = bMatch = false; bFade = true; page = Page.Batch; GUIUtility.ExitGUI(); }
            }
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }
        static int FixImport(List<Checked> l) { int n = 0; foreach (var c in l) { var imp = AssetImporter.GetAtPath(c.path) as AudioImporter; if (!imp) continue; bool ch = false; if (c.wrong.Any(w => w.what == Wrong.SameBothSides)) { imp.forceToMono = true; ch = true; } if (c.wrong.Any(w => w.what == Wrong.BigInMemory)) { var d = imp.defaultSampleSettings; d.loadType = AudioClipLoadType.Streaming; imp.defaultSampleSettings = d; ch = true; } if (ch) { imp.SaveAndReimport(); n++; } } return n; }

        // ================================================================ ambience

        [SerializeField] AudioClip ambLoop; [SerializeField] float ambLoopVol = 0.6f, ambLength = 60f; [SerializeField] List<AmbiencePlayer.Scatter> amb = new List<AmbiencePlayer.Scatter>(); [SerializeField] int ambSeed = 1; [SerializeField] string ambName = "ambience"; Vector2 ambScroll; float[] ambMade; string ambKey = ""; List<(int layer, float at, float len, float vol)> ambHits = new List<(int, float, float, float)>();
        string AmbKey => ambSeed + "|" + ambLength + "|" + ambLoopVol + (ambLoop ? ambLoop.name : "") + string.Join(";", amb.Select(a => JsonUtility.ToJson(a))) + string.Join(";", ambMoves.Select(m => m.layer + "," + m.n + "," + m.at + m.off));
        [Serializable] public class AmbMove { public int layer, n; public float at = -1; public bool off; }
        [SerializeField] List<AmbMove> ambMoves = new List<AmbMove>(); int ambSelLayer = -1, ambSelN = -1; float ambGrab; Dictionary<AudioClip, float[]> ambPeaks = new Dictionary<AudioClip, float[]>();
        Color AmbTint(int i) => i >= 0 && i < amb.Count && amb[i].colour.a > 0.01f ? amb[i].colour : LayerTints[Mathf.Abs(i) % LayerTints.Length];
        AmbMove MoveOf(int layer, int n, bool make) { var m = ambMoves.FirstOrDefault(q => q.layer == layer && q.n == n); if (m == null && make) { m = new AmbMove { layer = layer, n = n }; ambMoves.Add(m); } return m; }

        /// <summary>Where every scattered sound lands.</summary>
        /// <summary>Where every scattered sound lands (n counts them in each group; moved and muted ones as you left them).</summary>
        static List<(int layer, int clip, float at, float vol, float semi, float pan, int n, bool off)> Plan(List<AmbiencePlayer.Scatter> l, float length, int seed, List<AmbMove> moves = null)
        {
            var o = new List<(int, int, float, float, float, float, int, bool)>(); for (int i = 0; i < l.Count; i++) { var s = l[i]; if (!s.on || s.clips.Count(c => c) == 0) continue; var r = new System.Random(seed * 131 + i * 17); float R(float a, float b) => Mathf.Lerp(a, b, (float)r.NextDouble()); float t = R(0, Mathf.Max(0.3f, s.every.y) * 0.7f); int last = -1, n = 0; while (t < length) { int c = r.Next(s.clips.Count); if (c == last && s.clips.Count > 1) c = (c + 1) % s.clips.Count; last = c; float at = t; bool off = false; var mv = moves?.FirstOrDefault(q => q.layer == i && q.n == n); if (mv != null) { if (mv.at >= 0) at = Mathf.Min(mv.at, length - 0.01f); off = mv.off; } o.Add((i, c, at, R(s.volume.x, s.volume.y) * (1 - s.far * 0.6f), R(-s.pitch, s.pitch), R(-s.wide, s.wide), n, off)); n++; t += Mathf.Max(0.15f, R(Mathf.Min(s.every.x, s.every.y), Mathf.Max(s.every.x, s.every.y))); } }
            return o;
        }

        /// <summary>A stereo piece of that length that loops: what rings over the end comes in at the start.</summary>
        float[] AmbBuild(out int rate)
        {
            rate = 44100; int n = Mathf.RoundToInt(ambLength * rate); var o = new float[n * 2]; ambHits.Clear();
            if (ambLoop && AudioIO.Read(ambLoop, out var ls, out int lch, out int lrt, out _))
            {
                // the loop, as often as it fits, stretched a hair so it ends exactly with the piece
                int ln = ls.Length / lch; float step = lrt / (float)rate; int times = Mathf.Max(1, Mathf.RoundToInt(n * step / ln)); double st = ln * (double)times / n; for (int f = 0; f < n; f++) { double p = f * st % ln; int i0 = (int)p, i1 = (i0 + 1) % ln; float k = (float)(p - i0); for (int c = 0; c < 2; c++) { int sc = Mathf.Min(c, lch - 1); o[f * 2 + c] += Mathf.Lerp(ls[i0 * lch + sc], ls[i1 * lch + sc], k) * ambLoopVol; } }
            }
            var read = new Dictionary<AudioClip, (float[] s, int ch, int rt)>(); foreach (var h in Plan(amb, ambLength, ambSeed, ambMoves))
            {
                if (h.off) continue;
                var sc = amb[h.layer]; var clip = sc.clips[h.clip]; if (!clip) continue; if (!read.TryGetValue(clip, out var d)) { if (!AudioIO.Read(clip, out var s, out int ch, out int rt, out _)) continue; read[clip] = d = (s, ch, rt); }
                var a = d.s; if (sc.far > 0.02f) { a = (float[])d.s.Clone(); AudioFx.Tone(a, d.ch, d.rt, new AudioFx.ToneSettings { muffle = sc.far * 0.62f }); } int m = a.Length / d.ch; double st = d.rt / (double)rate * Math.Pow(2, h.semi / 12); int len = (int)(m / st), f0 = Mathf.RoundToInt(h.at * rate); float al = (h.pan + 1) * Mathf.PI / 4, gl = Mathf.Cos(al) * 1.41f * h.vol, gr = Mathf.Sin(al) * 1.41f * h.vol; ambHits.Add((h.layer, h.at, len / (float)rate, h.vol));
                for (int f = 0; f < len; f++) { double p = f * st; int i0 = (int)p; if (i0 + 1 >= m) break; float k = (float)(p - i0), v0 = Mathf.Lerp(a[i0 * d.ch], a[(i0 + 1) * d.ch], k), v1 = d.ch > 1 ? Mathf.Lerp(a[i0 * d.ch + 1], a[(i0 + 1) * d.ch + 1], k) : v0; int q = (f0 + f) % n; o[q * 2] += v0 * gl; o[q * 2 + 1] += v1 * gr; }
            }
            return AudioFx.Limit(o);
        }

        double ambEdit; string ambSeen = "";
        void AmbiencePage(Rect r)
        {
            // playing and something changed: once the slider rests, it's made again and goes on from the same moment
            if (Event.current.type == EventType.Repaint) { var k = AmbKey; if (k != ambSeen) { ambSeen = k; ambEdit = EditorApplication.timeSinceStartup; } if (makeOn && source && source.isPlaying && k != ambKey) { if (EditorApplication.timeSinceStartup - ambEdit > 0.3) { float at = source.time; AmbPlay(); if (source.clip) source.time = Mathf.Min(at, source.clip.length - 0.01f); } else Repaint(); } }
            var e = Event.current; float lw = Mathf.Min(470, r.width * 0.46f); var left = new Rect(r.x + 6, r.y + 6, lw, r.height - 12); var right = new Rect(left.xMax + 6, left.y, r.xMax - left.xMax - 12, left.height); EditorGUI.DrawRect(left, Panel); EditorGUI.DrawRect(right, Panel);
            GUILayout.BeginArea(new Rect(left.x + 12, left.y + 8, left.width - 20, left.height - 12)); ambScroll = GUILayout.BeginScrollView(ambScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); EditorGUIUtility.labelWidth = 110; GUILayout.Label("1. THE BACKGROUND", sHead); ambLoop = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Loop", "Wind, rain, a room. Make > Wind makes one"), ambLoop, typeof(AudioClip), false); ambLoopVol = EditorGUILayout.Slider("Volume", ambLoopVol, 0, 1); GUILayout.Space(8);
            GUILayout.BeginHorizontal(); GUILayout.Label("2. SOUNDS SCATTERED OVER IT", sHead); GUILayout.FlexibleSpace(); if (GUILayout.Button("+ Add some", EditorStyles.miniButton, GUILayout.Width(80))) amb.Add(new AmbiencePlayer.Scatter { name = "Sounds " + (amb.Count + 1) }); GUILayout.EndHorizontal(); if (amb.Count == 0) GUILayout.Label("Birds, creaks, drips, distant thunder: add a group, and drop its sounds in.", sHint); int gone = -1;
            for (int i = 0; i < amb.Count; i++)
            {
                var a = amb[i]; var box = EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 6, 5, 8), margin = new RectOffset(0, 0, 4, 2) }); if (e.type == EventType.Repaint) { EditorGUI.DrawRect(box, Ink(0.04f)); EditorGUI.DrawRect(new Rect(box.x, box.y, 4, box.height), AmbTint(i)); } GUILayout.BeginHorizontal(); a.on = EditorGUILayout.Toggle(a.on, GUILayout.Width(16)); var tc = EditorGUILayout.ColorField(GUIContent.none, AmbTint(i), false, false, false, GUILayout.Width(26)); if (tc != AmbTint(i)) a.colour = new Color(tc.r, tc.g, tc.b, 1); a.name = EditorGUILayout.TextField(a.name); if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) gone = i; GUILayout.EndHorizontal();
                int rem = -1; for (int k = 0; k < a.clips.Count; k++) { GUILayout.BeginHorizontal(); a.clips[k] = (AudioClip)EditorGUILayout.ObjectField(a.clips[k], typeof(AudioClip), false); if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) rem = k; GUILayout.EndHorizontal(); } if (rem >= 0) a.clips.RemoveAt(rem); var add = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("+ Add a sound", "Pick one with the circle, like the loop above"), null, typeof(AudioClip), false); if (add) a.clips.Add(add); var dz = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true)); GUI.Box(dz, "or drop several here at once", EditorStyles.helpBox); if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && dz.Contains(e.mousePosition)) { var l = Sounds(DragAndDrop.objectReferences); if (l.Count > 0) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); a.clips.AddRange(l); } e.Use(); } }
                MinMax("Every (seconds)", ref a.every, 0.2f, 60); MinMax("Volume", ref a.volume, 0, 1); a.pitch = EditorGUILayout.Slider("Pitch ±", a.pitch, 0, 12); a.wide = EditorGUILayout.Slider("Left and right", a.wide, 0, 1); a.far = EditorGUILayout.Slider("Far away", a.far, 0, 1); EditorGUILayout.EndVertical();
            }
            if (gone >= 0) amb.RemoveAt(gone); EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView(); GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(right.x + 12, right.y + 8, right.width - 24, 40)); GUILayout.BeginHorizontal(); if (Big(makeOn ? "■" : "▶", "Hear it", 28, 46)) { if (makeOn) Stop(); else AmbPlay(); } if (GUILayout.Button(new GUIContent("Another order", "Shuffles where they land (what you moved goes back)"), GUILayout.Height(28), GUILayout.Width(100))) { ambSeed = UnityEngine.Random.Range(1, 99999); ambMoves.Clear(); ambSelLayer = -1; if (makeOn) AmbPlay(); } GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();
            var lanes = new Rect(right.x + 12, right.y + 50, right.width - 24, Mathf.Max(90, right.height - 50 - 170 - 34)); AmbTimeline(lanes); AmbInfo(new Rect(lanes.x, lanes.yMax + 4, lanes.width, 28));
            GUILayout.BeginArea(new Rect(right.x + 12, lanes.yMax + 38, right.width - 24, 156)); GUILayout.Label("3. KEEP IT", sHead); EditorGUIUtility.labelWidth = 90; ambName = EditorGUILayout.TextField("Name", ambName); ambLength = EditorGUILayout.Slider(new GUIContent("Length", "Seconds, for the saved sound"), ambLength, 5, 180); EditorGUIUtility.labelWidth = 0; if (Big("Put a player in the scene", "Never the same twice: the game picks the sounds as it plays. Adds an object with an Ambience Player", 28)) EditorApplication.delayCall += () => { var go = AmbPlayer(); Selection.activeObject = go; EditorGUIUtility.PingObject(go); }; if (GUILayout.Button(new GUIContent("Save it as one loop", "A .wav in Assets/Sounds that loops"), GUILayout.Height(24))) EditorApplication.delayCall += () => AmbSave("Assets/Sounds"); GUILayout.Label("The player needs no saved loop. The saved loop is for places that can't run it.", sHint); GUILayout.EndArea();
        }
        float[] PeaksOf(AudioClip c) { if (!c) return null; if (ambPeaks.TryGetValue(c, out var p)) return p; p = null; if (AudioIO.Read(c, out var s, out int ch, out _, out _)) { int n = 64, fr = s.Length / ch; p = new float[n]; for (int f = 0; f < fr; f++) { int b = Mathf.Min(n - 1, f * n / Math.Max(1, fr)); p[b] = Mathf.Max(p[b], Mathf.Abs(s[f * ch])); } float m = Mathf.Max(1e-4f, p.Max()); for (int k = 0; k < n; k++) p[k] /= m; } return ambPeaks[c] = p; }

        /// <summary>Like the Edit page: a ruler, a lane for the background and one per group, every sound as a little wave in
        /// its group's colour. Click one to hear and pick it, drag it to move it, click the ruler to jump there.</summary>
        void AmbTimeline(Rect r)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("AmbTimeline".GetHashCode(), FocusType.Passive); EditorGUI.DrawRect(r, new Color(0.07f, 0.075f, 0.09f)); var ruler = new Rect(r.x, r.y, r.width, 20); var body = new Rect(r.x, ruler.yMax, r.width, r.height - 20); float lh = body.height / Mathf.Max(1, amb.Count + 1), len = Mathf.Max(1, ambLength);
            float X(float t) => r.x + t / len * r.width; float T(float x) => Mathf.Clamp((x - r.x) / r.width * len, 0, len);
            var plan = Plan(amb, ambLength, ambSeed, ambMoves); Rect Box((int layer, int clip, float at, float vol, float semi, float pan, int n, bool off) h) { var c = amb[h.layer].clips[h.clip]; float w = Mathf.Max(5, (c ? c.length * Mathf.Pow(2, -h.semi / 12f) : 0.3f) / len * r.width), hh = Mathf.Max(8, lh * 0.78f * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(h.vol))); return new Rect(X(h.at), body.y + lh * (h.layer + 1.5f) - hh / 2, w, hh); }
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(ruler, new Color(0.1f, 0.105f, 0.125f)); float step = len > 90 ? 10 : len > 40 ? 5 : len > 15 ? 2 : 1; for (float s = 0; s <= len; s += step) { float x = X(s); EditorGUI.DrawRect(new Rect(x, ruler.yMax - 6, 1, 6), Ink(0.5f)); EditorGUI.DrawRect(new Rect(x, body.y, 1, body.height), Ink(0.035f)); if (x + 30 < r.xMax) GUI.Label(new Rect(x + 2, ruler.y + 1, 40, 16), s.ToString("0") + "s", EditorStyles.miniLabel); }
                // the background
                var bg = new Rect(r.x, body.y, r.width, lh); EditorGUI.DrawRect(new Rect(bg.x, bg.yMax - 1, bg.width, 1), Ink(0.06f)); var lp = PeaksOf(ambLoop); if (lp != null) { float cl = ambLoop.length; for (float s0 = 0; s0 < len; s0 += cl) for (int k = 0; k < lp.Length; k++) { float x0 = X(s0 + cl * k / lp.Length), x1 = X(s0 + cl * (k + 1) / lp.Length); if (x0 > r.xMax) break; float hh = Mathf.Max(1, lp[k] * lh * 0.7f * ambLoopVol); EditorGUI.DrawRect(new Rect(x0, bg.center.y - hh / 2, Mathf.Max(1, Mathf.Min(x1, r.xMax) - x0), hh), new Color(0.5f, 0.6f, 0.7f, 0.55f)); } }
                GUI.Label(new Rect(bg.x + 6, bg.y + 2, 300, 16), ambLoop ? "Background: " + ambLoop.name : "No background loop", new GUIStyle(sHint) { normal = { textColor = Ink(0.7f) } });
                for (int i = 0; i < amb.Count; i++) { var ln = new Rect(r.x, body.y + lh * (i + 1), r.width, lh); var tint = AmbTint(i); EditorGUI.DrawRect(ln, new Color(tint.r, tint.g, tint.b, i % 2 == 0 ? 0.05f : 0.03f)); EditorGUI.DrawRect(new Rect(ln.x, ln.y, 4, ln.height), tint); GUI.Label(new Rect(ln.x + 8, ln.y + 2, 300, 16), amb[i].name + (amb[i].on ? "" : "  (off)"), new GUIStyle(sHint) { normal = { textColor = tint } }); }
                foreach (var h in plan)
                {
                    var c = amb[h.layer].clips[h.clip]; var b = Box(h); if (b.x > r.xMax) continue; b.width = Mathf.Min(b.width, r.xMax - b.x); var tint = AmbTint(h.layer); bool sel = h.layer == ambSelLayer && h.n == ambSelN;
                    if (h.off) { var q = new Color(tint.r, tint.g, tint.b, 0.5f); EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 1), q); EditorGUI.DrawRect(new Rect(b.x, b.yMax - 1, b.width, 1), q); EditorGUI.DrawRect(new Rect(b.x, b.y, 1, b.height), q); continue; }
                    EditorGUI.DrawRect(b, new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 0.95f)); var pk = PeaksOf(c); if (pk != null) for (int k = 0; k < pk.Length; k++) { float x0 = b.x + b.width * k / pk.Length; if (x0 >= b.xMax) break; float hh = Mathf.Max(1, pk[k] * (b.height - 2)); EditorGUI.DrawRect(new Rect(x0, b.center.y - hh / 2, Mathf.Max(1, b.width / pk.Length), hh), tint); }
                    if (sel) { EditorGUI.DrawRect(new Rect(b.x - 1, b.y - 2, b.width + 2, 2), Color.white); EditorGUI.DrawRect(new Rect(b.x - 1, b.yMax, b.width + 2, 2), Color.white); } if (b.width > 60 && c) GUI.Label(new Rect(b.x + 3, b.y, b.width - 4, 14), c.name, new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, normal = { textColor = Color.white }, clipping = TextClipping.Clip });
                }
                if (makeOn && source && source.clip && Mathf.Abs(source.clip.length - len) < 0.5f) { float px = X(source.time); EditorGUI.DrawRect(new Rect(px, r.y, 2, r.height), new Color(1f, 0.35f, 0.3f)); Repaint(); }
            }
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                int hit = -1; for (int k = plan.Count - 1; k >= 0; k--) if (Box(plan[k]).Contains(e.mousePosition)) { hit = k; break; }
                if (hit >= 0 && !ruler.Contains(e.mousePosition)) { var h = plan[hit]; ambSelLayer = h.layer; ambSelN = h.n; ambGrab = T(e.mousePosition.x) - h.at; GUIUtility.hotControl = id; if (!h.off) AmbHear(amb[h.layer].clips[h.clip]); }
                else { float at = T(e.mousePosition.x); if (!(makeOn && source && source.clip && Mathf.Abs(source.clip.length - len) < 0.5f)) AmbPlay(); if (source && source.clip) source.time = Mathf.Min(at, source.clip.length - 0.01f); }
                e.Use(); Repaint();
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && ambSelLayer >= 0) { MoveOf(ambSelLayer, ambSelN, true).at = Mathf.Clamp(T(e.mousePosition.x) - ambGrab, 0, len - 0.05f); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && ambSelLayer >= 0) { MoveOf(ambSelLayer, ambSelN, true).off = true; e.Use(); Repaint(); }
        }
        void AmbInfo(Rect r)
        {
            GUILayout.BeginArea(r); GUILayout.BeginHorizontal(); var h = ambSelLayer >= 0 && ambSelLayer < amb.Count ? Plan(amb, ambLength, ambSeed, ambMoves).FirstOrDefault(q => q.layer == ambSelLayer && q.n == ambSelN) : default; bool any = ambSelLayer >= 0 && ambSelLayer < amb.Count && h.layer == ambSelLayer && h.n == ambSelN && amb[h.layer].clips.Count > h.clip;
            if (!any) GUILayout.Label("Click a sound to pick it and hear it, drag it to move it. Click the ruler or an empty spot to jump there. Moves and mutes go into the saved loop.", sHint);
            else
            {
                var c = amb[h.layer].clips[h.clip]; var tint = AmbTint(h.layer); GUILayout.Label(new GUIContent(amb[h.layer].name + ":  " + (c ? c.name : "?") + "  at " + h.at.ToString("0.0") + " s"), new GUIStyle(sTileTitle) { normal = { textColor = tint } }, GUILayout.ExpandWidth(false)); GUILayout.Space(8);
                if (GUILayout.Button("\u25B6 Hear it", EditorStyles.miniButton, GUILayout.Width(64))) AmbHear(c); var mv = MoveOf(h.layer, h.n, false); if (GUILayout.Button(h.off ? "Unmute" : "Mute it", EditorStyles.miniButton, GUILayout.Width(60))) MoveOf(h.layer, h.n, true).off = !h.off; using (new EditorGUI.DisabledScope(mv == null)) if (GUILayout.Button("Put it back", EditorStyles.miniButton, GUILayout.Width(70))) ambMoves.Remove(mv); if (GUILayout.Button("Done", EditorStyles.miniButton, GUILayout.Width(44))) ambSelLayer = -1;
            }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();
        }
        void AmbHear(AudioClip c) { if (!c || !AudioIO.Read(c, out var s, out int ch, out int rt, out _)) return; Stop(); Make(s, ch, rt); source.loop = false; source.time = 0; source.Play(); makeOn = playing = true; previewDirty = true; restartAt = -1; }

        static void MinMax(string l, ref Vector2 v, float lo, float hi) { EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(l); float a = v.x, b = v.y; EditorGUILayout.MinMaxSlider(ref a, ref b, lo, hi); EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(" "); a = EditorGUILayout.FloatField((float)Math.Round(a, 2)); b = EditorGUILayout.FloatField((float)Math.Round(b, 2)); EditorGUILayout.EndHorizontal(); v = new Vector2(Mathf.Clamp(Mathf.Min(a, b), lo, hi), Mathf.Clamp(Mathf.Max(a, b), lo, hi)); }
        void AmbPlay() { var k = AmbKey; if (k != ambKey || ambMade == null) { ambMade = AmbBuild(out _); ambKey = k; } Stop(); Make(ambMade, 2, 44100); source.loop = true; source.time = 0; source.Play(); makeOn = playing = true; previewDirty = true; restartAt = -1; }
        GameObject AmbPlayer() { var go = new GameObject("Ambience " + ambName); var p = go.AddComponent<AmbiencePlayer>(); p.loop = ambLoop; p.loopVolume = ambLoopVol; p.scatter = amb.Select(a => new AmbiencePlayer.Scatter { name = a.name, on = a.on, clips = a.clips.Where(c => c).ToList(), every = a.every, volume = a.volume, pitch = a.pitch, wide = a.wide, far = a.far }).ToList(); Undo.RegisterCreatedObjectUndo(go, "Ambience player"); return go; }
        AudioClip AmbSave(string folder) { if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder)); var d = AmbBuild(out int rt); var t = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + (CleanName(ambName).Length > 0 ? CleanName(ambName) : "ambience") + ".wav"); File.WriteAllBytes(t, WriteWav(d, 2, rt)); AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate); var c = AssetDatabase.LoadAssetAtPath<AudioClip>(t); if (c) EditorGUIUtility.PingObject(c); ShowNotification(new GUIContent("Saved " + Path.GetFileName(t))); return c; }
    }
}
