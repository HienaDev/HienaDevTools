using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Everything done to a sound besides cutting it. Edit and every layer in Layers have one of these.</summary>
    [Serializable]
    public class SoundFx
    {
        public float gainDb, fadeIn, fadeOut; public int fadeShape = 1;
        public int levelMode; public float loudness = -18f;
        public float pitch, stretch = 1f; public bool apart;               // apart: pitch and length on their own
        public float bass, mid, treble, muffle, thin, grit;
        public float echo, echoTime = 0.28f, echoAgain = 0.4f, echoDull = 0.4f;
        public float room, roomSize = 0.4f, roomDull = 0.4f;
        public List<Vector2> volume = new List<Vector2>();                  // seconds from its start, times (1 = as it is)
        public VoiceFx voice = new VoiceFx(); public AudioClip matchTo; public float matchTone = 0.8f, matchLoud = 1f;

        public SoundFx Copy() => JsonUtility.FromJson<SoundFx>(JsonUtility.ToJson(this));
        public AudioFx.ToneSettings Tone => new AudioFx.ToneSettings { bass = bass, mid = mid, treble = treble, muffle = muffle, thin = thin, grit = grit };
        /// <summary>How much faster it plays (2 = half as long).</summary>
        public float Speed => apart ? 1f / Mathf.Clamp(stretch, 0.1f, 10f) : Mathf.Pow(2f, pitch / 12f);
        public static float Shaped(float k, int shape) => shape == 0 ? k : shape == 1 ? k * k * (3 - 2 * k) : k * k * k;
        public static float VolumeAt(List<Vector2> v, float t)
        {
            if (v == null || v.Count == 0) return 1f; if (t <= v[0].x) return v[0].y; for (int i = 1; i < v.Count; i++) if (t <= v[i].x) return Mathf.Lerp(v[i - 1].y, v[i].y, Mathf.InverseLerp(v[i - 1].x, v[i].x, t)); return v[v.Count - 1].y;
        }

        /// <summary>Fades and the drawn volume, on the cut-out piece itself.</summary>
        public void Shape(float[] a, int ch, int rate)
        {
            int n = a.Length / ch, fi = Mathf.RoundToInt(fadeIn * rate), fo = Mathf.RoundToInt(fadeOut * rate); bool vol = volume != null && volume.Count > 0; if (fi <= 0 && fo <= 0 && !vol) return;
            for (int f = 0; f < n; f++)
            {
                float k = 1; if (fi > 0 && f < fi) k *= Shaped(f / (float)fi, fadeShape); if (fo > 0 && f > n - fo) k *= Shaped((n - f) / (float)fo, fadeShape); if (vol) k *= VolumeAt(volume, f / (float)rate); if (k != 1) for (int c = 0; c < ch; c++) a[f * ch + c] *= k;
            }
        }

        /// <summary>Pitch, length, tone, match, echo, room, in that order. loop: the ring-outs go round to the start.</summary>
        public float[] Run(float[] a, int ch, int rate, bool loop, float morePitch = 0f, bool tails = true)
        {
            if (apart)
            {
                if (Mathf.Abs(pitch + morePitch) > 0.01f) { float sp = Mathf.Pow(2f, (pitch + morePitch) / 12f); a = AudioDsp.Resample(AudioDsp.Stretch(a, ch, rate, sp * stretch), ch, sp); }
                else if (Mathf.Abs(stretch - 1) > 0.002f) a = AudioDsp.Stretch(a, ch, rate, stretch);
            }
            else if (Mathf.Abs(pitch + morePitch) > 0.005f) a = AudioDsp.Resample(a, ch, Mathf.Pow(2f, (pitch + morePitch) / 12f));
            if (voice != null && voice.Any) a = AudioVoice.Run(a, ch, rate, voice);
            var t = Tone; if (t.Any) { AudioFx.Tone(a, ch, rate, t); if (levelMode == 0) AudioFx.Limit(a); }
            if (matchTo && (matchTone > 0.01f || matchLoud > 0.01f)) { var r = AudioDsp.Known(matchTo); if (r != null) a = AudioDsp.Match(a, ch, rate, r, matchTone, levelMode == 0 ? matchLoud : 0f); }
            if (tails && echo > 0.001f) a = AudioDsp.Echo(a, ch, rate, echoTime, echoAgain, echo, echoDull, loop);
            if (tails && room > 0.001f) a = AudioFx.Reverb(a, ch, rate, roomSize, roomDull, room, loop);
            return a;
        }

        /// <summary>The volume at the end. Returns how far short of the asked loudness it fell.</summary>
        public float Level(float[] a, int ch, int rate)
        {
            if (levelMode == 1) { float pk = AudioFx.PeakOf(a); if (pk > 1e-5f) for (int i = 0; i < a.Length; i++) a[i] *= 0.98f / pk; return 0; }
            return levelMode == 2 ? AudioFx.ToLoudness(a, ch, rate, loudness) : 0;
        }
    }

    public static class AudioDsp
    {
        // ------------------------------------------------------------------ length and pitch

        public static float[] Resample(float[] a, int ch, float speed)
        {
            int n = a.Length / ch, m = Mathf.Max(1, Mathf.RoundToInt(n / speed)); var o = new float[m * ch];
            for (int f = 0; f < m; f++) { double p = f * (double)speed; int i0 = Mathf.Min(n - 1, (int)p), im = Mathf.Max(0, i0 - 1), i1 = Mathf.Min(n - 1, i0 + 1), i2 = Mathf.Min(n - 1, i0 + 2); float t = (float)(p - i0); for (int c = 0; c < ch; c++) { float y0 = a[im * ch + c], y1 = a[i0 * ch + c], y2 = a[i1 * ch + c], y3 = a[i2 * ch + c]; o[f * ch + c] = y1 + 0.5f * t * (y2 - y0 + t * (2 * y0 - 5 * y1 + 4 * y2 - y3 + t * (3 * (y1 - y2) + y3 - y0))); } }
            return o;
        }

        /// <summary>Longer (2 = twice as long) or shorter with the same pitch: little overlapping pieces are laid out again,
        /// each shifted a hair so its wave continues the one before.</summary>
        public static float[] Stretch(float[] a, int ch, int rate, float times)
        {
            int n = a.Length / ch; if (Mathf.Abs(times - 1) < 0.002f || n < rate / 20) return (float[])a.Clone();
            int win = (int)(rate * 0.056f) & ~1, hop = win / 2, reach = (int)(rate * 0.013f), m = Mathf.Max(1, Mathf.RoundToInt(n * times)); var mono = new float[n]; for (int f = 0; f < n; f++) { float s = 0; for (int c = 0; c < ch; c++) s += a[f * ch + c]; mono[f] = s / ch; }
            var o = new float[(m + win) * ch]; var sum = new float[m + win]; var w = new float[win]; for (int i = 0; i < win; i++) w[i] = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * (i + 0.5f) / win);
            int prev = 0; bool first = true;
            for (int at = 0; at < m; at += hop)
            {
                int want = Mathf.RoundToInt(at / times), use = want;
                if (!first)
                {
                    // what would come naturally after the piece before: look near the wanted place for the same shape
                    int next = prev + hop; float best = float.MinValue; int step = Mathf.Max(1, rate / 12000), from = Mathf.Max(0, want - reach), to = Mathf.Min(n - win - 1, want + reach);
                    for (int q = from; q <= to; q += 2) { double xy = 0, yy = 1e-9; for (int i = 0; i < hop; i += step * 2) { int p1 = next + i; if (p1 >= n) break; float x = mono[p1], y = mono[q + i]; xy += x * y; yy += y * y; } float sc = (float)(xy / Math.Sqrt(yy)); if (sc > best) { best = sc; use = q; } }
                }
                use = Mathf.Clamp(use, 0, Mathf.Max(0, n - 1)); first = false; prev = use;
                for (int i = 0; i < win; i++) { int f = use + i; if (at + i >= m + win) break; float k = w[i]; sum[at + i] += k; if (f < n) for (int c = 0; c < ch; c++) o[(at + i) * ch + c] += a[f * ch + c] * k; }
            }
            var r = new float[m * ch]; for (int f = 0; f < m; f++) { float k = sum[f] > 1e-3f ? 1 / sum[f] : 0; for (int c = 0; c < ch; c++) r[f * ch + c] = o[f * ch + c] * k; }
            return r;
        }

        // ------------------------------------------------------------------ echo

        /// <summary>Repeats after <paramref name="time"/> seconds, each quieter (and duller) than the one before.</summary>
        public static float[] Echo(float[] a, int ch, int rate, float time, float again, float amount, float dull, bool loop)
        {
            int n = a.Length / ch, d = Mathf.Max(1, Mathf.RoundToInt(time * rate)); again = Mathf.Clamp(again, 0f, 0.92f); int times = again < 0.02f ? 1 : Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(0.004f) / Mathf.Log(again)), 1, 60); float tail = Mathf.Min(12f, time * times); int total = loop ? n : n + Mathf.RoundToInt(tail * rate); var o = new float[total * ch]; float lp = 1 - 0.85f * Mathf.Clamp01(dull);
            for (int c = 0; c < ch; c++)
            {
                var line = new float[d]; float low = 0; int p = 0, rounds = loop ? 3 : 1; // (in a loop it goes round until what rings on from the end is there at the start)
                for (int r = 0; r < rounds; r++)
                    for (int f = 0; f < total; f++) { float x = f < n ? a[f * ch + c] : 0, e = line[p]; low += (e - low) * lp; line[p] = x + low * again; if (++p >= d) p = 0; if (r == rounds - 1) o[f * ch + c] = x + e * amount; }
            }
            if (!loop) { float quiet = Math.Max(0.0003f, AudioFx.PeakOf(o) * 0.0015f); int last = total - 1; while (last > n) { bool loud = false; for (int c = 0; c < ch; c++) if (Math.Abs(o[last * ch + c]) > quiet) loud = true; if (loud) break; last--; } int end = Math.Min(total, last + rate / 50), fade = Math.Max(1, rate / 50); if (end < total) Array.Resize(ref o, end * ch); for (int f = Math.Max(n, end - fade); f < end; f++) for (int c = 0; c < ch; c++) o[f * ch + c] *= (end - f) / (float)fade; }
            return AudioFx.Limit(o);
        }

        // ------------------------------------------------------------------ matching another sound

        public class Character { public float[] bands; public float loud; }
        const int Bands = 30, N = 4096; static float BandHz(int b) => 40f * Mathf.Pow(400f, b / (float)(Bands - 1)); // 40 Hz to 16 kHz
        static readonly Dictionary<string, Character> s_Known = new Dictionary<string, Character>();
        static string KeyOf(AudioClip c) => c ? c.name + "|" + c.length.ToString("0.000") + "|" + c.frequency + "|" + c.channels : "";
        /// <summary>(reading a clip only works in the window: call this there first)</summary>
        public static void Learn(AudioClip c) { if (!c) return; string k = KeyOf(c); lock (s_Known) if (s_Known.ContainsKey(k)) return; if (!AudioIO.Read(c, out var s, out int ch, out int rt, out _)) return; var r = CharacterOf(s, ch, rt); lock (s_Known) s_Known[k] = r; }
        public static Character Known(AudioClip c) { string k = KeyOf(c); lock (s_Known) return s_Known.TryGetValue(k, out var r) ? r : null; }

        /// <summary>How loud each part from deep to bright is (dB, on average 0), and how loud it all is.</summary>
        public static Character CharacterOf(float[] a, int ch, int rate)
        {
            int n = a.Length / ch; var pw = new double[Bands]; double cnt = 0; var re = new float[N]; var im = new float[N]; var mono = new float[n]; double rms = 0; for (int f = 0; f < n; f++) { float s = 0; for (int c = 0; c < ch; c++) s += a[f * ch + c]; mono[f] = s / ch; rms += mono[f] * mono[f]; }
            rms /= Math.Max(1, n); int hop = Math.Max(N / 2, n / 400);
            for (int at = 0; at == 0 || at + N <= n; at += hop)
            {
                double e = 0; for (int i = 0; i < N; i++) { float x = at + i < n ? mono[at + i] : 0; re[i] = x * (0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / N)); im[i] = 0; e += x * x; } if (e / N < rms * 0.05) continue; // (the quiet parts don't count)
                AudioCleaner.FFT(re, im, false); cnt++;
                for (int b = 0; b < Bands; b++) { float lo = BandHz(b) / 1.12f, hi = BandHz(b) * 1.12f; int k0 = Mathf.Max(1, Mathf.FloorToInt(lo * N / rate)), k1 = Mathf.Min(N / 2 - 1, Mathf.Max(k0, Mathf.CeilToInt(hi * N / rate))); double s = 0; for (int k = k0; k <= k1; k++) s += re[k] * re[k] + im[k] * im[k]; pw[b] += s / (k1 - k0 + 1); }
            }
            var db = new float[Bands]; for (int b = 0; b < Bands; b++) db[b] = (float)(10 * Math.Log10(pw[b] / Math.Max(1, cnt) + 1e-12)); float top = db.Max(), mean = db.Where(v => v > top - 50).Average(); for (int b = 0; b < Bands; b++) db[b] = Mathf.Max(db[b], top - 60) - mean; return new Character { bands = db, loud = AudioFx.Loudness(a, ch, rate) };
        }

        public static float[] Match(float[] a, int ch, int rate, Character to, float tone, float loud)
        {
            var me = CharacterOf(a, ch, rate); var o = (float[])a.Clone();
            if (tone > 0.01f)
            {
                var g = new float[Bands]; for (int b = 0; b < Bands; b++) g[b] = Mathf.Clamp(to.bands[b] - me.bands[b], -24f, 24f) * tone; for (int pass = 0; pass < 2; pass++) { var q = (float[])g.Clone(); for (int b = 0; b < Bands; b++) g[b] = (q[Mathf.Max(0, b - 1)] + q[b] * 2 + q[Mathf.Min(Bands - 1, b + 1)]) / 4; }
                var gain = new float[N / 2 + 1]; for (int k = 0; k <= N / 2; k++) { float hz = Mathf.Max(20, k * rate / (float)N), p = Mathf.Clamp(Mathf.Log(hz / 40f, 400f) * (Bands - 1), 0, Bands - 1.001f); int i = (int)p; gain[k] = Mathf.Pow(10f, Mathf.Lerp(g[i], g[i + 1], p - i) / 20f); }
                o = Filtered(a, ch, gain);
            }
            if (loud > 0.01f) { float now = AudioFx.Loudness(o, ch, rate); if (now > -90 && to.loud > -90) { float k = Mathf.Pow(10f, (to.loud - now) * loud / 20f); for (int i = 0; i < o.Length; i++) o[i] *= k; } }
            return AudioFx.Limit(o);
        }

        // every frequency times its own gain
        static float[] Filtered(float[] a, int ch, float[] gain)
        {
            int n = a.Length / ch, hop = N / 4; var o = new float[a.Length]; var w = new float[N]; for (int i = 0; i < N; i++) w[i] = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / N); var re = new float[N]; var im = new float[N];
            for (int c = 0; c < ch; c++)
                for (int at = -N + hop; at < n; at += hop)
                {
                    for (int i = 0; i < N; i++) { int f = at + i; re[i] = f >= 0 && f < n ? a[f * ch + c] * w[i] : 0; im[i] = 0; } AudioCleaner.FFT(re, im, false); for (int k = 0; k <= N / 2; k++) { re[k] *= gain[k]; im[k] *= gain[k]; if (k > 0 && k < N / 2) { re[N - k] *= gain[k]; im[N - k] *= gain[k]; } } AudioCleaner.FFT(re, im, true);
                    for (int i = 0; i < N; i++) { int f = at + i; if (f >= 0 && f < n) o[f * ch + c] += re[i] * w[i] / 1.5f; }
                }
            return o;
        }

        // ------------------------------------------------------------------ loops in music

        public class LoopSpot { public float start, end, score; }

        /// <summary>Places where the music sounds the same again, so it can jump back without being noticed. Best first.</summary>
        public static List<LoopSpot> FindLoops(float[] a, int ch, int rate, float shortest, CancellationToken stop)
        {
            const int D = 24; int n = a.Length / ch, win = 4096, hop = rate / 20, fr = (n - win) / hop; var found = new List<LoopSpot>(); if (fr < 40) return found; var mono = new float[n]; for (int f = 0; f < n; f++) { float s = 0; for (int c = 0; c < ch; c++) s += a[f * ch + c]; mono[f] = s / ch; }
            // what it sounds like 20 times a second: how loud each of 24 parts from deep to bright is
            var feat = new float[fr * D]; Parallel.For(0, fr, () => (new float[win], new float[win]), (t, st, b) =>
            {
                var re = b.Item1; var im = b.Item2; for (int i = 0; i < win; i++) { re[i] = mono[t * hop + i] * (0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / win)); im[i] = 0; } AudioCleaner.FFT(re, im, false); float mean = 0;
                for (int d = 0; d < D; d++) { float lo = 60f * Mathf.Pow(170f, d / (float)D), hi = 60f * Mathf.Pow(170f, (d + 1f) / D); int k0 = Mathf.Max(1, (int)(lo * win / rate)), k1 = Mathf.Max(k0 + 1, (int)(hi * win / rate)); double s = 0; for (int k = k0; k < k1 && k < win / 2; k++) s += re[k] * re[k] + im[k] * im[k]; float v = Mathf.Log10((float)(s / (k1 - k0)) + 1e-4f); feat[t * D + d] = v; mean += v; }
                return b;
            }, b => { });
            float mu = feat.Average(), sd = Mathf.Sqrt(feat.Select(v => (v - mu) * (v - mu)).Average()) + 1e-4f; int half = 24, min = Mathf.Max(half, Mathf.RoundToInt(shortest * 20)); var bestAt = new int[fr]; var bestD = new float[fr];
            Parallel.For(min, fr - half * 2, L =>
            {
                bestD[L] = float.MaxValue; if (stop.IsCancellationRequested) return; int m = fr - L; var d = new float[m]; for (int t = 0; t < m; t++) { float s = 0; int i = t * D, j = (t + L) * D; for (int k = 0; k < D; k++) { float q = feat[i + k] - feat[j + k]; s += q * q; } d[t] = s; }
                double run = 0; int w = half * 2; for (int t = 0; t < m; t++) { run += d[t]; if (t >= w) run -= d[t - w]; if (t >= w - 1) { float v = (float)(run / w); if (v < bestD[L]) { bestD[L] = v; bestAt[L] = t - half + 1; } } }
            });
            if (stop.IsCancellationRequested) return found;
            // (longer loops are better: they're heard less often)
            var order = Enumerable.Range(min, Mathf.Max(0, fr - half * 2 - min)).Where(L => bestD[L] < float.MaxValue).OrderBy(L => bestD[L] * (1.25f - 0.25f * L / fr)).ToList(); var used = new List<int>();
            foreach (int L in order)
            {
                if (used.Any(u => Mathf.Abs(u - L) < 8)) continue; used.Add(L); int s0 = Mathf.Max(0, bestAt[L] * hop + win / 2), e0 = Mathf.Min(n - 1, s0 + L * hop);
                // to the sample: slide the end a little until the waves lie on each other
                int cmp = rate / 12, reach = hop; double top = double.MinValue; int best = e0; if (s0 + cmp >= n) continue;
                for (int pass = 0; pass < 2; pass++) { int step = pass == 0 ? 6 : 1, r = pass == 0 ? reach : 8, c = best; for (int q = c - r; q <= c + r; q += step) { if (q < 0 || q + cmp >= n) continue; double xy = 0, xx = 1e-9, yy = 1e-9; for (int i = 0; i < cmp; i += 2) { double x = mono[s0 + i], y = mono[q + i]; xy += x * y; xx += x * x; yy += y * y; } double v = xy / Math.Sqrt(xx * yy); if (v > top) { top = v; best = q; } } }
                float alike = Mathf.Sqrt(bestD[L] / D) / sd; found.Add(new LoopSpot { start = s0 / (float)rate, end = best / (float)rate, score = Mathf.Clamp01(1f - alike * 1.1f) * 0.75f + Mathf.Clamp01((float)top) * 0.25f }); if (found.Count >= 8) break;
            }
            return found.OrderByDescending(f => f.score).ToList();
        }
    }
}
