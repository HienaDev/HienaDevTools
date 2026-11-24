using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Splits a song into what holds a note (melody, chords, pads: the calm part) and what hits (drums, clicks: the
    /// busy part). Both are exactly as long as the song, so a Music Layers player can play them together and fade the
    /// drums in when things get exciting.</summary>
    public static class AudioStems
    {
        /// <summary>Notes stay put in pitch and go on in time; hits are short and spread over all pitches. Each moment of the
        /// sound is given to whichever it looks more like (median filters across time and across pitch).</summary>
        public static void Split(float[] s, int ch, int rate, out float[] calm, out float[] hits, Action<float> progress, CancellationToken stop)
        {
            const int N = 2048, H = N / 4, K = N / 2 + 1, LT = 17, LF = 17; int n = s.Length / ch, frames = n / H + 4; var win = new float[N]; for (int i = 0; i < N; i++) win[i] = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / N);
            // what it looks like, from both sides mixed
            var mag = new float[frames][]; Parallel.For(0, frames, new ParallelOptions { CancellationToken = stop }, f => { var re = new float[N]; var im = new float[N]; int at = f * H - N / 2; for (int i = 0; i < N; i++) { int q = at + i; float v = 0; if (q >= 0 && q < n) for (int c = 0; c < ch; c++) v += s[q * ch + c]; re[i] = v / ch * win[i]; } AudioCleaner.FFT(re, im, false); var m = new float[K]; for (int k = 0; k < K; k++) m[k] = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]); mag[f] = m; });
            progress?.Invoke(0.25f);
            var mask = new float[frames][]; int done = 0;
            Parallel.For(0, frames, new ParallelOptions { CancellationToken = stop }, f =>
            {
                var a = new float[LT]; var b = new float[LF]; var m = new float[K];
                for (int k = 0; k < K; k++)
                {
                    for (int j = 0; j < LT; j++) { int ff = Mathf.Clamp(f + j - LT / 2, 0, frames - 1); a[j] = mag[ff][k]; } Array.Sort(a); float harm = a[LT / 2];
                    for (int j = 0; j < LF; j++) { int kk = Mathf.Clamp(k + j - LF / 2, 0, K - 1); b[j] = mag[f][kk]; } Array.Sort(b); float perc = b[LF / 2];
                    float h2 = harm * harm, p2 = perc * perc; m[k] = h2 + p2 > 1e-12f ? h2 / (h2 + p2) : 0.5f;
                }
                mask[f] = m; int d = Interlocked.Increment(ref done); if (d % 64 == 0) progress?.Invoke(0.25f + 0.5f * d / frames);
            });
            // each side, cut with the same mask
            var outC = new float[s.Length]; var outH = new float[s.Length]; var lockObj = new object();
            for (int c = 0; c < ch; c++)
            {
                stop.ThrowIfCancellationRequested(); var accC = new float[n + N]; var accH = new float[n + N]; int cc = c;
                Parallel.For(0, frames, new ParallelOptions { CancellationToken = stop }, f =>
                {
                    var re = new float[N]; var im = new float[N]; int at = f * H - N / 2; for (int i = 0; i < N; i++) { int q = at + i; re[i] = q >= 0 && q < n ? s[q * ch + cc] * win[i] : 0; } AudioCleaner.FFT(re, im, false);
                    var r2 = (float[])re.Clone(); var i2 = (float[])im.Clone(); var m = mask[f]; for (int k = 0; k < K; k++) { float g = m[k]; re[k] *= g; im[k] *= g; r2[k] *= 1 - g; i2[k] *= 1 - g; if (k > 0 && k < N / 2) { re[N - k] *= g; im[N - k] *= g; r2[N - k] *= 1 - g; i2[N - k] *= 1 - g; } }
                    AudioCleaner.FFT(re, im, true); AudioCleaner.FFT(r2, i2, true); lock (lockObj) for (int i = 0; i < N; i++) { int q = at + i; if (q < 0 || q >= n) continue; accC[q] += re[i] * win[i] / 1.5f; accH[q] += r2[i] * win[i] / 1.5f; }
                });
                for (int i = 0; i < n; i++) { outC[i * ch + c] = accC[i]; outH[i * ch + c] = accH[i]; } progress?.Invoke(0.75f + 0.25f * (c + 1) / ch);
            }
            calm = outC; hits = outH;
        }
    }

    public partial class AudioTrimmer
    {
        [SerializeField] AudioClip stemSong; float[] stemCalm, stemHits; int stemCh, stemRate; Task stemTask; CancellationTokenSource stemStop; float stemProgress; [SerializeField] float stemBusy = 1f; AudioClip stemFor; float[] stemPeaksC, stemPeaksH;

        void StemsPage(Rect r)
        {
            var top = new Rect(r.x + 6, r.y + 6, r.width - 12, 104); EditorGUI.DrawRect(top, Panel); GUILayout.BeginArea(new Rect(top.x + 12, top.y + 8, top.width - 24, top.height - 12));
            GUILayout.Label("Split a song in two: the calm part (melody, chords, pads) and the hits (drums). Play both together in the game, and bring the drums in when a fight starts.", sHint);
            GUILayout.BeginHorizontal(); GUILayout.Label("Song", GUILayout.Width(40)); var c = (AudioClip)EditorGUILayout.ObjectField(stemSong, typeof(AudioClip), false, GUILayout.Width(260)); if (c != stemSong) { stemSong = c; stemCalm = stemHits = null; } if (!stemSong && clip && GUILayout.Button("Use the one in Edit (" + clip.name + ")", GUILayout.Width(220))) stemSong = clip; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); bool busy = stemTask != null && !stemTask.IsCompleted;
            using (new EditorGUI.DisabledScope(!stemSong || busy)) if (Big("Split it", "Takes a while for a long song: it works in the background", 28, 120)) StemStart();
            if (busy) { GUILayout.Label("splitting... " + Mathf.RoundToInt(stemProgress * 100) + "%", sTime, GUILayout.Width(150)); if (GUILayout.Button("Stop", GUILayout.Width(60), GUILayout.Height(28))) stemStop.Cancel(); Repaint(); }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();
            var mid = new Rect(r.x + 6, top.yMax + 6, r.width - 12, r.height - top.height - 18 - 120); EditorGUI.DrawRect(mid, Panel);
            if (stemCalm == null) { GUI.Label(mid, busy ? "splitting..." : "Pick a song and press Split it.", sEmpty); return; }
            float lh = (mid.height - 30) / 2; StemLane(new Rect(mid.x + 10, mid.y + 10, mid.width - 20, lh - 6), "CALM: melody, chords, pads", stemPeaksC, new Color(0.5f, 0.78f, 1f)); StemLane(new Rect(mid.x + 10, mid.y + 16 + lh, mid.width - 20, lh - 6), "HITS: drums, clicks", stemPeaksH, new Color(1f, 0.62f, 0.3f));
            if (makeOn && source && source.isPlaying && source.clip) { float x = mid.x + 10 + (mid.width - 20) * source.time / source.clip.length; EditorGUI.DrawRect(new Rect(x, mid.y + 6, 2, mid.height - 12), new Color(1f, 0.35f, 0.3f)); Repaint(); }
            var bot = new Rect(r.x + 6, mid.yMax + 6, r.width - 12, 108); EditorGUI.DrawRect(bot, Panel); GUILayout.BeginArea(new Rect(bot.x + 12, bot.y + 8, bot.width - 24, bot.height - 12));
            GUILayout.BeginHorizontal(); if (Big(makeOn ? "■" : "▶", "Hear both, mixed with the slider", 28, 46)) { if (makeOn) Stop(); else StemPlay(-1); } if (GUILayout.Button("Only calm", GUILayout.Height(28), GUILayout.Width(90))) StemPlay(0); if (GUILayout.Button("Only hits", GUILayout.Height(28), GUILayout.Width(90))) StemPlay(1); GUILayout.Space(12); GUILayout.Label(new GUIContent("Drums", "How loud the hits are next to the calm part: the game moves this"), GUILayout.Width(46)); float nb = GUILayout.HorizontalSlider(stemBusy, 0, 1, GUILayout.Width(220)); if (!Mathf.Approximately(nb, stemBusy)) { stemBusy = nb; if (makeOn && stemMix) { float at = source.time; StemPlay(-1); source.time = at; } } GUILayout.Label(stemBusy < 0.05f ? "calm" : stemBusy > 0.95f ? "the whole song" : Mathf.RoundToInt(stemBusy * 100) + "%", sHint, GUILayout.Width(90)); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.Space(8); GUILayout.BeginHorizontal(); if (Big("Save both", "Two .wav files of the same length in Assets/Music", 28, 120)) EditorApplication.delayCall += () => StemSave("Assets/Music"); if (GUILayout.Button(new GUIContent("Save both and put a player in the scene", "A Music Layers object plays them in step; set its Intensity from your game (0 calm, 1 fight)"), GUILayout.Height(28))) EditorApplication.delayCall += () => { var l = StemSave("Assets/Music"); if (l.calm) { var go = new GameObject("Music " + stemSong.name); var m = go.AddComponent<MusicLayers>(); m.calm = l.calm; m.hits = l.hits; Undo.RegisterCreatedObjectUndo(go, "Music layers"); Selection.activeObject = go; } }; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            GUILayout.Label("In your game: GetComponent<MusicLayers>().Intensity = 1 when a fight starts, 0 when it's over. It fades by itself.", sHint); GUILayout.EndArea();
        }
        bool stemMix;
        void StemLane(Rect r, string name, float[] pk, Color tint)
        {
            EditorGUI.DrawRect(r, new Color(0.07f, 0.075f, 0.09f)); if (Event.current.type == EventType.Repaint && pk != null) for (int i = 0; i < pk.Length; i++) { float x = r.x + r.width * i / pk.Length, h = Mathf.Max(1, pk[i] * (r.height - 24)); EditorGUI.DrawRect(new Rect(x, r.y + 18 + (r.height - 18 - h) / 2, Mathf.Max(1, r.width / pk.Length), h), tint); }
            GUI.Label(new Rect(r.x + 6, r.y + 2, 400, 16), name, new GUIStyle(sHead) { normal = { textColor = tint } });
            var e = Event.current; if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { float t = (e.mousePosition.x - r.x) / r.width; if (!makeOn) StemPlay(-1); if (source && source.clip) source.time = t * source.clip.length; e.Use(); }
        }
        static float[] Peaks(float[] s, int ch, int bins) { var p = new float[bins]; int n = s.Length / ch; for (int f = 0; f < n; f++) { int b = Mathf.Min(bins - 1, (int)((long)f * bins / Math.Max(1, n))); float v = 0; for (int c = 0; c < ch; c++) v = Mathf.Max(v, Mathf.Abs(s[f * ch + c])); p[b] = Mathf.Max(p[b], v); } float m = Mathf.Max(1e-4f, p.Max()); for (int i = 0; i < bins; i++) p[i] /= m; return p; }
        void StemStart()
        {
            if (!AudioIO.Read(stemSong, out var s, out int ch, out int rt, out _)) { ShowNotification(new GUIContent("Can't read it: set its Load Type to Decompress On Load")); return; }
            stemStop = new CancellationTokenSource(); var stop = stemStop.Token; stemProgress = 0; stemCh = ch; stemRate = rt; stemFor = stemSong;
            stemTask = Task.Run(() => { AudioStems.Split(s, ch, rt, out var a, out var b, p => stemProgress = p, stop); var pa = Peaks(a, ch, 600); var pb = Peaks(b, ch, 600); EditorApplication.delayCall += () => { if (stemFor != stemSong) return; stemCalm = a; stemHits = b; stemPeaksC = pa; stemPeaksH = pb; Repaint(); }; });
        }
        void StemPlay(int which)
        {
            if (stemCalm == null) return; var o = new float[stemCalm.Length]; for (int i = 0; i < o.Length; i++) o[i] = which == 0 ? stemCalm[i] : which == 1 ? stemHits[i] : stemCalm[i] + stemHits[i] * stemBusy;
            Stop(); Make(o, stemCh, stemRate); source.loop = true; source.time = 0; source.Play(); makeOn = playing = true; previewDirty = true; restartAt = -1; stemMix = which < 0;
        }
        (AudioClip calm, AudioClip hits) StemSave(string folder)
        {
            if (stemCalm == null || !stemSong) return (null, null); if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder)); var name = CleanName(stemSong.name);
            AudioClip One(float[] d, string tag) { var t = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "_" + tag + ".wav"); File.WriteAllBytes(t, WriteWav(d, stemCh, stemRate)); AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate); var imp = AssetImporter.GetAtPath(t) as AudioImporter; if (imp) { var st = imp.defaultSampleSettings; st.loadType = AudioClipLoadType.Streaming; imp.defaultSampleSettings = st; imp.SaveAndReimport(); } return AssetDatabase.LoadAssetAtPath<AudioClip>(t); }
            var a = One(stemCalm, "calm"); var b = One(stemHits, "hits"); ShowNotification(new GUIContent("Saved " + name + "_calm and " + name + "_hits")); if (a) EditorGUIUtility.PingObject(a); return (a, b);
        }
    }
}
