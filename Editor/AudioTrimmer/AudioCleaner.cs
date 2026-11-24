using System;
using System.Threading;
using System.Threading.Tasks;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Takes steady background sound (hiss, a fan, hum, room noise) out of a recording: it learns what the background
    /// sounds like from a bit where nothing else plays, then turns down every pitch that isn't louder than that.
    /// It can't separate music or voices: those change all the time, so they don't look like a background.
    /// </summary>
    public static class AudioCleaner
    {
        const int N = 2048, Hop = N / 4, Bins = N / 2 + 1;

        /// <summary>How loud the background is at each pitch, per channel.</summary>
        public class Background { public float[][] level; }

        public class Settings
        {
            public float strength = 0.5f;   // 0..1: how sure it has to be that something is louder than the background
            public float amountDb = 24f;    // how much the background is turned down
            public float rumbleHz;          // everything below this is cut (0 = off)
            public float gapsDb;            // how much the quiet gaps are turned down further (0 = off)
        }

        // ------------------------------------------------------------------ learning the background

        public static Background Learn(float[] samples, int channels, int from, int to)
        {
            int frames = samples.Length / channels;
            from = Math.Max(0, Math.Min(frames, from)); to = Math.Max(from, Math.Min(frames, to));
            var bg = new Background { level = new float[channels][] };
            var win = Window();
            for (int c = 0; c < channels; c++)
            {
                var sum = new double[Bins]; var sq = new double[Bins]; int n = 0;
                var re = new float[N]; var im = new float[N];
                // (a bit shorter than one window still counts: it's repeated to fill it)
                int len = Math.Max(1, to - from), steps = Math.Max(1, (len - N) / Hop + 1);
                for (int s = 0; s < steps; s++)
                {
                    for (int i = 0; i < N; i++) { int f = from + (s * Hop + i) % len; re[i] = f < frames ? samples[f * channels + c] * win[i] : 0; im[i] = 0; }
                    FFT(re, im, false);
                    for (int k = 0; k < Bins; k++) { double m = Math.Sqrt(re[k] * re[k] + im[k] * im[k]); sum[k] += m; sq[k] += m * m; }
                    n++;
                }
                var lv = new float[Bins];
                for (int k = 0; k < Bins; k++) { double mean = sum[k] / n, var = Math.Max(0, sq[k] / n - mean * mean); lv[k] = (float)(mean + Math.Sqrt(var)); }
                bg.level[c] = lv;
            }
            return bg;
        }

        /// <summary>The quietest stretch of the sound (frames), where most likely only the background plays. Digital
        /// silence doesn't count: there's nothing to learn from it.</summary>
        public static bool FindQuietest(float[] samples, int channels, int rate, float seconds, out int from, out int to)
        {
            int frames = samples.Length / channels, len = Math.Max(N, Math.Min(frames, (int)(seconds * rate)));
            from = 0; to = Math.Min(frames, len);
            if (frames < N / 2) return false;
            int block = Math.Max(64, rate / 100), nb = frames / block;
            var e = new double[nb];
            for (int b = 0; b < nb; b++) { double s = 0; for (int i = b * block * channels, end = (b + 1) * block * channels; i < end; i++) s += samples[i] * samples[i]; e[b] = s / (block * channels); }
            int span = Math.Max(1, Math.Min(nb, len / block));
            double best = double.MaxValue; int at = -1;
            for (int b = 0; b + span <= nb; b++)
            {
                double s = 0, low = double.MaxValue;
                for (int i = 0; i < span; i++) { s += e[b + i]; low = Math.Min(low, e[b + i]); }
                if (low < 1e-11) continue; // (nothing at all there)
                if (s < best) { best = s; at = b; }
            }
            if (at < 0) return false;
            from = at * block; to = Math.Min(frames, from + span * block);
            return true;
        }

        // ------------------------------------------------------------------ cleaning

        public static float[] Clean(float[] samples, int channels, int rate, Background bg, Settings s, CancellationToken stop)
        {
            int frames = samples.Length / channels;
            var outp = new float[samples.Length];
            Parallel.For(0, channels, c =>
            {
                var x = new float[frames];
                for (int f = 0; f < frames; f++) x[f] = samples[f * channels + c];
                if (s.rumbleHz > 1) HighPass(x, rate, s.rumbleHz);
                if (bg != null) x = CleanChannel(x, bg.level[Math.Min(c, bg.level.Length - 1)], s, stop);
                for (int f = 0; f < frames; f++) outp[f * channels + c] = x[f];
            });
            stop.ThrowIfCancellationRequested();
            if (s.gapsDb > 0.1f) QuietGaps(outp, channels, rate, bg, s.gapsDb);
            return outp;
        }

        static float[] CleanChannel(float[] x, float[] noise, Settings s, CancellationToken stop)
        {
            int n = x.Length; var win = Window();
            var y = new float[n]; var norm = new float[n];
            float floor = (float)Math.Pow(10, -s.amountDb / 20), over = 1f + s.strength * 3f;
            var re = new float[N]; var im = new float[N]; var gain = new float[Bins]; var prev = new float[Bins]; var soft = new float[Bins];
            for (int k = 0; k < Bins; k++) prev[k] = floor;
            for (int start = -N + Hop; start < n; start += Hop)
            {
                if ((start & 0xFFFF) == 0) stop.ThrowIfCancellationRequested();
                for (int i = 0; i < N; i++) { int f = start + i; re[i] = f >= 0 && f < n ? x[f] * win[i] : 0; im[i] = 0; }
                FFT(re, im, false);
                for (int k = 0; k < Bins; k++)
                {
                    // compared over a few neighbouring pitches, so single ones don't flicker on and off ("underwater" sounds)
                    float p = 0, q = 0;
                    for (int d = -2; d <= 2; d++) { int j = k + d; if (j < 0 || j >= Bins) continue; p += re[j] * re[j] + im[j] * im[j]; q += noise[j] * noise[j]; }
                    float ratio = q > 1e-20f ? p / (q * over * over) : 1e6f;
                    float g = ratio <= 1 ? floor : Math.Max(floor, (float)Math.Sqrt(1 - 1 / ratio));
                    // opens at once, closes gently
                    gain[k] = g > prev[k] ? g : Math.Max(g, prev[k] * 0.72f);
                    prev[k] = gain[k];
                }
                for (int k = 0; k < Bins; k++) soft[k] = (gain[Math.Max(0, k - 1)] + gain[k] * 2 + gain[Math.Min(Bins - 1, k + 1)]) * 0.25f;
                for (int k = 0; k < Bins; k++)
                {
                    re[k] *= soft[k]; im[k] *= soft[k];
                    if (k > 0 && k < N / 2) { re[N - k] = re[k]; im[N - k] = -im[k]; }
                }
                FFT(re, im, true);
                for (int i = 0; i < N; i++) { int f = start + i; if (f < 0 || f >= n) continue; y[f] += re[i] * win[i]; norm[f] += win[i] * win[i]; }
            }
            for (int f = 0; f < n; f++) y[f] = norm[f] > 1e-6f ? y[f] / norm[f] : 0;
            return y;
        }

        // the parts between the sounds, turned down further
        static void QuietGaps(float[] a, int channels, int rate, Background bg, float db)
        {
            int frames = a.Length / channels, block = Math.Max(32, rate / 200), nb = (frames + block - 1) / block;
            var env = new float[nb]; float peak = 0;
            for (int b = 0; b < nb; b++)
            {
                double s = 0; int end = Math.Min(frames, (b + 1) * block) * channels, n = 0;
                for (int i = b * block * channels; i < end; i++) { s += a[i] * a[i]; n++; }
                env[b] = n > 0 ? (float)Math.Sqrt(s / n) : 0; peak = Math.Max(peak, env[b]);
            }
            float open = peak * 0.02f, low = (float)Math.Pow(10, -db / 20), g = low; int hold = 0;
            for (int b = 0; b < nb; b++)
            {
                // (looks a little ahead, so the start of a sound isn't cut)
                bool loud = false; for (int d = 0; d <= 3 && b + d < nb; d++) if (env[b + d] > open) loud = true;
                if (loud) hold = 24;
                float want = hold > 0 ? 1 : low; if (hold > 0) hold--;
                float g1 = want > g ? Math.Min(want, g + 0.5f) : Math.Max(want, g - 0.06f);
                int f0 = b * block, f1 = Math.Min(frames, f0 + block);
                for (int f = f0; f < f1; f++) { float k = g + (g1 - g) * (f - f0) / block; for (int c = 0; c < channels; c++) a[f * channels + c] *= k; }
                g = g1;
            }
        }

        static void HighPass(float[] x, int rate, float hz)
        {
            // two second-order filters one after the other: a firm cut with no ringing
            double w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071);
            double b0 = (1 + cs) / 2, b1 = -(1 + cs), b2 = b0, a0 = 1 + al, a1 = -2 * cs, a2 = 1 - al;
            for (int pass = 0; pass < 2; pass++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                for (int i = 0; i < x.Length; i++) { double v = x[i], y = (b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0; x2 = x1; x1 = v; y2 = y1; y1 = y; x[i] = (float)y; }
            }
        }

        // ------------------------------------------------------------------ the maths

        static float[] s_Window;
        static float[] Window()
        {
            if (s_Window != null) return s_Window;
            var w = new float[N]; for (int i = 0; i < N; i++) w[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
            return s_Window = w;
        }

        internal static void FFT(float[] re, float[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1); float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                        float nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
            if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }
    }
}
