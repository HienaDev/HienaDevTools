using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>What the Audio Trimmer does to a sound besides cutting it: tone, reverb and loudness. All on plain samples.</summary>
    public static class AudioFx
    {
        // ------------------------------------------------------------------ filters

        struct Biquad
        {
            public double b0, b1, b2, a1, a2;
            public void Run(float[] a, int channels)
            {
                for (int c = 0; c < channels; c++)
                {
                    double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                    for (int i = c; i < a.Length; i += channels) { double x = a[i], y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; a[i] = (float)y; }
                }
            }
            public static Biquad Of(double b0, double b1, double b2, double a0, double a1, double a2) => new Biquad { b0 = b0 / a0, b1 = b1 / a0, b2 = b2 / a0, a1 = a1 / a0, a2 = a2 / a0 };
        }

        static Biquad Shelf(bool high, double hz, double db, int rate, double q = 0.7071)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q), s = 2 * Math.Sqrt(A) * al, k = high ? -1 : 1;
            return Biquad.Of(A * (A + 1 - k * (A - 1) * cs + s), k * 2 * A * (A - 1 - k * (A + 1) * cs), A * (A + 1 - k * (A - 1) * cs - s), A + 1 + k * (A - 1) * cs + s, k * -2 * (A - 1 + k * (A + 1) * cs), A + 1 + k * (A - 1) * cs - s);
        }
        static Biquad Peak(double hz, double db, int rate, double q)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Biquad.Of(1 + al * A, -2 * cs, 1 - al * A, 1 + al / A, -2 * cs, 1 - al / A);
        }
        static Biquad HighPass(double hz, int rate, double q)
        {
            double w = 2 * Math.PI * hz / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Biquad.Of((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + al, -2 * cs, 1 - al);
        }

        static Biquad LowPass(double hz, int rate, double q)
        {
            double w = 2 * Math.PI * Math.Min(hz, rate * 0.45) / rate, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            return Biquad.Of((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + al, -2 * cs, 1 - al);
        }

        [Serializable] public struct ToneSettings
        {
            public float bass, mid, treble;      // dB
            public float muffle, thin, grit;     // 0..1
            public bool Any => Math.Abs(bass) > 0.05f || Math.Abs(mid) > 0.05f || Math.Abs(treble) > 0.05f || muffle > 0.005f || thin > 0.005f || grit > 0.005f;
        }

        /// <summary>The character of the sound: more or less of the deep, middle and bright parts (wide, so it's heard on
        /// any sound), muffled (no highs), thin (no lows) and grit (pushed until it crackles).</summary>
        public static void Tone(float[] a, int channels, int rate, ToneSettings s)
        {
            // (two gentle filters instead of one steep one: strong without ringing)
            if (Math.Abs(s.bass) > 0.05f) { Shelf(false, 180, s.bass / 2, rate).Run(a, channels); Shelf(false, 360, s.bass / 2, rate).Run(a, channels); }
            if (Math.Abs(s.mid) > 0.05f) Peak(1000, s.mid, rate, 0.55).Run(a, channels);
            if (Math.Abs(s.treble) > 0.05f) { Shelf(true, 2200, s.treble / 2, rate).Run(a, channels); Shelf(true, 4500, s.treble / 2, rate).Run(a, channels); }
            // muffle: 16 kHz down to 250 Hz; thin: 30 Hz up to 2.5 kHz
            if (s.muffle > 0.005f) { double hz = 16000 * Math.Pow(250.0 / 16000, s.muffle); LowPass(hz, rate, 0.54).Run(a, channels); LowPass(hz, rate, 1.31).Run(a, channels); }
            if (s.thin > 0.005f) { double hz = 30 * Math.Pow(2500.0 / 30, s.thin); HighPass(hz, rate, 0.54).Run(a, channels); HighPass(hz, rate, 1.31).Run(a, channels); }
            if (s.grit > 0.005f)
            {
                float was = PeakOf(a); if (was < 1e-5f) return; double drive = Math.Pow(10, s.grit * 34 / 20.0), norm = Math.Tanh(drive);
                for (int i = 0; i < a.Length; i++) a[i] = (float)(Math.Tanh(a[i] / was * drive) / norm * was);
                // (pushing it makes it a lot louder: most of that is taken back)
                float back = (float)Math.Pow(10, -s.grit * 9 / 20.0); for (int i = 0; i < a.Length; i++) a[i] *= back;
            }
        }

        // ------------------------------------------------------------------ loudness

        /// <summary>How loud it sounds (LUFS: what streaming and games use). Quieter than anything audible: -99.</summary>
        public static float Loudness(float[] samples, int channels, int rate)
        {
            int frames = samples.Length / Math.Max(1, channels); if (frames == 0) return -99;
            var a = (float[])samples.Clone();
            Shelf(true, 1681.97, 4.0, rate).Run(a, channels); HighPass(38.13, rate, 0.5).Run(a, channels);
            // blocks of 0.4 s that overlap; a shorter sound is one block
            int len = Math.Min(frames, (int)(0.4 * rate)), hop = Math.Max(1, len / 4), n = Math.Max(1, (frames - len) / hop + 1);
            var e = new double[n];
            for (int b = 0; b < n; b++) { double s = 0; for (int i = b * hop * channels, end = (b * hop + len) * channels; i < end; i++) s += (double)a[i] * a[i]; e[b] = s / len; }
            double Mean(double above) { double s = 0; int c = 0; foreach (var v in e) if (v > above) { s += v; c++; } return c > 0 ? s / c : 0; }
            double Power(double lufs) => Math.Pow(10, (lufs + 0.691) / 10);
            double m = Mean(Power(-70)); if (m <= 0) return -99;
            m = Mean(m * 0.1); // (the quiet parts don't count)
            return m <= 0 ? -99 : (float)(-0.691 + 10 * Math.Log10(m));
        }

        public static float PeakOf(float[] a) { float p = 0; foreach (var v in a) p = Math.Max(p, Math.Abs(v)); return p; }

        /// <summary>Makes it sound as loud as asked, but never past the limit (the loudest sample stays under 0.98).
        /// Returns how far short it fell (dB), 0 when it got there.</summary>
        public static float ToLoudness(float[] a, int channels, int rate, float target)
        {
            float now = Loudness(a, channels, rate); if (now < -98) return 0;
            float want = (float)Math.Pow(10, (target - now) / 20), peak = PeakOf(a), k = peak > 0 ? Math.Min(want, 0.98f / peak) : want;
            for (int i = 0; i < a.Length; i++) a[i] *= k;
            return k < want ? (float)(20 * Math.Log10(want / k)) : 0;
        }

        // ------------------------------------------------------------------ reverb

        static readonly int[] Combs = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }, Passes = { 556, 441, 341, 225 };

        /// <summary>
        /// A room around the sound. size 0..1 (closet to cathedral), dull 0..1 (bright tiles to thick carpet), amount 0..1.
        /// The result is longer than the sound: the echo rings out. For a loop the ring-out goes back onto the start.
        /// </summary>
        public static float[] Reverb(float[] a, int channels, int rate, float size, float dull, float amount, bool loop)
        {
            if (amount <= 0.001f || a.Length == 0) return a;
            int frames = a.Length / channels; float feedback = 0.7f + 0.28f * Mathf.Clamp01(size);
            // how long until it's inaudible (-60 dB)
            float tail = Mathf.Clamp((float)(-3.0 / Math.Log10(feedback) * 1356 / 44100.0), 0.2f, 7f);
            int total = frames + (int)(tail * rate), pre = (int)(rate * (0.004f + 0.03f * size));
            var wet = new float[total * channels]; double k = rate / 44100.0;
            for (int c = 0; c < channels; c++)
            {
                int spread = c * 23; var dry = new float[total]; for (int f = 0; f < frames && f + pre < total; f++) dry[f + pre] = a[f * channels + c] * 0.03f;
                var sum = new float[total]; float damp = 0.1f + 0.6f * Mathf.Clamp01(dull);
                foreach (var cb in Combs)
                {
                    var buf = new float[Math.Max(1, (int)((cb + spread) * k))]; int at = 0; float low = 0;
                    for (int f = 0; f < total; f++) { float o = buf[at]; low = o * (1 - damp) + low * damp; buf[at] = dry[f] + low * feedback; if (++at >= buf.Length) at = 0; sum[f] += o; }
                }
                foreach (var ap in Passes)
                {
                    var buf = new float[Math.Max(1, (int)((ap + spread) * k))]; int at = 0;
                    for (int f = 0; f < total; f++) { float o = buf[at], x = sum[f]; buf[at] = x + o * 0.5f; sum[f] = o - x; if (++at >= buf.Length) at = 0; }
                }
                for (int f = 0; f < total; f++) wet[f * channels + c] = sum[f];
            }
            float wetGain = amount * 3f, dryGain = 1f - amount * 0.45f;
            if (loop)
            {
                var lo = new float[a.Length];
                for (int i = 0; i < a.Length; i++) lo[i] = a[i] * dryGain;
                for (int f = 0; f < total; f++) { int g = f % frames; for (int c = 0; c < channels; c++) lo[g * channels + c] += wet[f * channels + c] * wetGain; }
                return Limit(lo);
            }
            var outp = new float[total * channels];
            for (int i = 0; i < outp.Length; i++) outp[i] = (i < a.Length ? a[i] * dryGain : 0) + wet[i] * wetGain;
            // the ring-out ends when it's gone, and fades the last bit
            float quiet = Math.Max(0.0004f, PeakOf(outp) * 0.0015f); int last = total - 1;
            while (last > frames) { bool loud = false; for (int c = 0; c < channels; c++) if (Math.Abs(outp[last * channels + c]) > quiet) loud = true; if (loud) break; last--; }
            int end = Math.Min(total, last + rate / 20), fade = Math.Min(rate / 20, Math.Max(1, end - frames));
            var cut = new float[end * channels]; Array.Copy(outp, cut, cut.Length);
            for (int f = Math.Max(frames, end - fade); f < end; f++) { float g = (end - f) / (float)fade; for (int c = 0; c < channels; c++) cut[f * channels + c] *= g; }
            return Limit(cut);
        }

        /// <summary>Turns it down if it went past the limit (so it can't crackle).</summary>
        public static float[] Limit(float[] a) { float p = PeakOf(a); if (p > 0.99f) { float k = 0.99f / p; for (int i = 0; i < a.Length; i++) a[i] *= k; } return a; }

        // ------------------------------------------------------------------ silence

        /// <summary>The part that isn't silence (frames), with a little kept around it.</summary>
        public static void Sounding(float[] a, int channels, int rate, float belowDb, float pad, out int from, out int to)
        {
            int frames = a.Length / channels, block = Math.Max(16, rate / 200), n = (frames + block - 1) / block; float peak = PeakOf(a), quiet = peak * (float)Math.Pow(10, belowDb / 20); int first = -1, last = -1;
            for (int b = 0; b < n; b++) { float m = 0; for (int i = b * block * channels, end = Math.Min(frames, (b + 1) * block) * channels; i < end; i++) m = Math.Max(m, Math.Abs(a[i])); if (m > quiet) { if (first < 0) first = b; last = b; } }
            from = 0; to = frames; if (first < 0) return;
            from = Math.Max(0, first * block - (int)(pad * rate)); to = Math.Min(frames, (last + 1) * block + (int)(pad * rate));
        }
    }

    /// <summary>Reading sounds at full quality and writing .wav files.</summary>
    public static class AudioIO
    {
        public static bool Read(AudioClip clip, out float[] samples, out int channels, out int rate, out string problem)
        {
            samples = null; channels = 1; rate = 44100; problem = null;
            string path = AssetDatabase.GetAssetPath(clip);
            try
            {
                if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && AudioTrimmer.ReadWav(File.ReadAllBytes(path), out samples, out channels, out rate)) return true;
                channels = clip.channels; rate = clip.frequency; samples = new float[clip.samples * clip.channels];
                if (clip.GetData(samples, 0)) return true;
                samples = null; problem = "Unity can't read this sound's samples.";
            }
            catch (Exception e) { samples = null; problem = e.Message; }
            problem = (problem ?? "") + " Set its Load Type to \"Decompress On Load\" and try again.";
            return false;
        }
    }
}
