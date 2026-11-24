using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>What turns a voice into something else. They go on top of each other.</summary>
    [Serializable]
    public class VoiceFx
    {
        public float robot, robotHz = 100f, whisper, ring, ringHz = 60f, low, lowSemi = -12f, high, highSemi = 12f, chorus, crush, wobble, wobbleHz = 5f, pulse, pulseHz = 8f, stutter, ghost;
        public bool Any => robot > 0.005f || whisper > 0.005f || ring > 0.005f || low > 0.005f || high > 0.005f || chorus > 0.005f || crush > 0.005f || wobble > 0.005f || pulse > 0.005f || stutter > 0.005f || ghost > 0.005f;
        public VoiceFx Copy() => (VoiceFx)MemberwiseClone();
    }

    public static class AudioVoice
    {
        /// <summary>A whole character: the voice itself, and the pitch, tone, echo and room that go with it.</summary>
        public class Preset { public string name, group; public VoiceFx v = new VoiceFx(); public float pitch, echo, echoTime = 0.28f, echoAgain = 0.4f, room, roomSize = 0.4f; public AudioFx.ToneSettings tone; }
        static Preset P(string group, string name, Action<Preset> set) { var p = new Preset { name = name, group = group }; set(p); return p; }
        public static readonly Preset[] All =
        {
            P("Creatures", "Monster", p => { p.pitch = -6; p.v.low = 0.6f; p.v.chorus = 0.2f; p.tone.bass = 7; p.tone.grit = 0.35f; p.room = 0.12f; }),
            P("Creatures", "Demon", p => { p.pitch = -7; p.v.low = 0.8f; p.v.lowSemi = -7; p.v.high = 0.25f; p.v.highSemi = 5; p.v.ring = 0.15f; p.v.ringHz = 34; p.tone.bass = 6; p.tone.grit = 0.45f; p.room = 0.3f; p.roomSize = 0.8f; }),
            P("Creatures", "Giant", p => { p.pitch = -9; p.tone.bass = 9; p.tone.treble = -4; p.room = 0.2f; p.roomSize = 0.7f; }),
            P("Creatures", "Zombie", p => { p.pitch = -4; p.v.wobble = 0.35f; p.v.wobbleHz = 3.1f; p.v.whisper = 0.25f; p.tone.grit = 0.5f; p.tone.treble = -6; }),
            P("Creatures", "Goblin", p => { p.pitch = 5; p.v.ring = 0.18f; p.v.ringHz = 90; p.tone.mid = 6; p.tone.grit = 0.3f; }),
            P("Creatures", "Insects", p => { p.pitch = 4; p.v.ring = 0.6f; p.v.ringHz = 210; p.v.chorus = 0.7f; p.v.pulse = 0.4f; p.v.pulseHz = 27; p.tone.thin = 0.4f; }),
            P("Creatures", "Dragon", p => { p.pitch = -10; p.v.low = 0.5f; p.v.lowSemi = -5; p.v.chorus = 0.35f; p.tone.bass = 10; p.tone.grit = 0.55f; p.room = 0.3f; p.roomSize = 0.85f; }),
            P("Machines", "Robot", p => { p.v.robot = 0.9f; p.v.robotHz = 110; p.tone.mid = 3; }),
            P("Machines", "Deep robot", p => { p.v.robot = 0.9f; p.v.robotHz = 62; p.v.low = 0.3f; p.tone.bass = 5; p.tone.grit = 0.15f; }),
            P("Machines", "Android", p => { p.v.robot = 0.45f; p.v.robotHz = 150; p.v.chorus = 0.4f; p.tone.treble = 4; p.room = 0.08f; p.roomSize = 0.1f; }),
            P("Machines", "Metal head", p => { p.v.ring = 0.75f; p.v.ringHz = 48; p.tone.mid = 4; p.tone.grit = 0.2f; }),
            P("Machines", "Broken machine", p => { p.v.robot = 0.6f; p.v.robotHz = 85; p.v.stutter = 0.6f; p.v.crush = 0.4f; p.tone.grit = 0.3f; }),
            P("Machines", "Game console", p => { p.v.crush = 0.75f; p.tone.mid = 4; }),
            P("Machines", "Helmet", p => { p.tone.muffle = 0.42f; p.tone.mid = 7; p.v.chorus = 0.12f; p.room = 0.18f; p.roomSize = 0.02f; }),
            P("Far and near", "Radio", p => { p.tone.mid = 8; p.tone.muffle = 0.4f; p.tone.thin = 0.6f; p.tone.grit = 0.35f; }),
            P("Far and near", "Walkie-talkie", p => { p.tone.mid = 10; p.tone.muffle = 0.48f; p.tone.thin = 0.68f; p.tone.grit = 0.7f; p.v.crush = 0.25f; }),
            P("Far and near", "Phone", p => { p.tone.mid = 5; p.tone.muffle = 0.36f; p.tone.thin = 0.62f; }),
            P("Far and near", "Megaphone", p => { p.tone.mid = 11; p.tone.thin = 0.62f; p.tone.grit = 0.6f; p.echo = 0.18f; p.echoTime = 0.11f; p.echoAgain = 0.25f; }),
            P("Far and near", "Stadium speaker", p => { p.tone.thin = 0.45f; p.tone.mid = 5; p.echo = 0.4f; p.echoTime = 0.33f; p.echoAgain = 0.4f; p.room = 0.4f; p.roomSize = 0.9f; }),
            P("Far and near", "Next room", p => { p.tone.muffle = 0.7f; p.tone.bass = 3; p.room = 0.15f; p.roomSize = 0.2f; }),
            P("Far and near", "Underwater", p => { p.tone.muffle = 0.86f; p.tone.bass = 5; p.v.wobble = 0.3f; p.v.wobbleHz = 2.2f; p.v.chorus = 0.3f; }),
            P("Far and near", "Old record", p => { p.tone.thin = 0.5f; p.tone.muffle = 0.42f; p.tone.grit = 0.3f; p.v.wobble = 0.12f; p.v.wobbleHz = 1.3f; p.v.crush = 0.12f; }),
            P("Spirits", "Ghost", p => { p.v.ghost = 0.75f; p.v.chorus = 0.35f; p.v.whisper = 0.25f; p.tone.thin = 0.35f; p.room = 0.55f; p.roomSize = 0.9f; }),
            P("Spirits", "Whisper", p => { p.v.whisper = 0.95f; p.tone.thin = 0.3f; p.tone.treble = 5; }),
            P("Spirits", "Possessed", p => { p.v.low = 0.75f; p.v.lowSemi = -12; p.v.high = 0.4f; p.v.highSemi = 3; p.v.ghost = 0.4f; p.tone.bass = 4; p.room = 0.2f; p.roomSize = 0.5f; }),
            P("Spirits", "Choir", p => { p.v.chorus = 0.9f; p.v.low = 0.4f; p.v.lowSemi = -12; p.v.high = 0.35f; p.v.highSemi = 7; p.room = 0.5f; p.roomSize = 0.92f; }),
            P("Spirits", "Cave god", p => { p.pitch = -5; p.v.low = 0.4f; p.echo = 0.5f; p.echoTime = 0.5f; p.echoAgain = 0.5f; p.room = 0.6f; p.roomSize = 0.97f; p.tone.bass = 6; }),
            P("Spirits", "Dream", p => { p.v.chorus = 0.6f; p.v.ghost = 0.45f; p.tone.muffle = 0.3f; p.echo = 0.35f; p.echoTime = 0.42f; p.echoAgain = 0.55f; p.room = 0.45f; p.roomSize = 0.85f; }),
            P("Funny", "Helium", p => { p.pitch = 8; p.tone.treble = 3; }),
            P("Funny", "Tiny", p => { p.pitch = 12; p.tone.thin = 0.4f; }),
            P("Funny", "Alien", p => { p.v.ring = 0.5f; p.v.ringHz = 130; p.v.wobble = 0.5f; p.v.wobbleHz = 7; p.pitch = 3; }),
            P("Funny", "Drunk", p => { p.pitch = -2; p.v.wobble = 0.55f; p.v.wobbleHz = 0.9f; }),
            P("Funny", "Glitch", p => { p.v.stutter = 0.9f; p.v.crush = 0.5f; p.v.pulse = 0.3f; p.v.pulseHz = 16; }),
            P("Funny", "Fan", p => { p.v.pulse = 0.85f; p.v.pulseHz = 15; p.v.wobble = 0.1f; p.v.wobbleHz = 15; }),
        };

        public static float[] Run(float[] a, int ch, int rate, VoiceFx v)
        {
            if (!v.Any || a.Length == 0) return a;
            if (v.low > 0.005f || v.high > 0.005f)
            {
                var o = (float[])a.Clone(); void Add(float semi, float amt) { float sp = Mathf.Pow(2f, semi / 12f); var s = AudioDsp.Resample(AudioDsp.Stretch(a, ch, rate, sp), ch, sp); int n = Mathf.Min(s.Length, o.Length); for (int i = 0; i < n; i++) o[i] += s[i] * amt; }
                if (v.low > 0.005f) Add(v.lowSemi, v.low); if (v.high > 0.005f) Add(v.highSemi, v.high * 0.8f); float k = 1f / (1 + v.low * 0.6f + v.high * 0.5f); for (int i = 0; i < o.Length; i++) o[i] *= k; a = o;
            }
            if (v.robot > 0.005f) a = Mix(a, Spectral(a, ch, rate, Mathf.Clamp(v.robotHz, 40, 400), false), v.robot);
            if (v.whisper > 0.005f) a = Mix(a, Spectral(a, ch, rate, 0, true), v.whisper);
            if (v.chorus > 0.005f) a = Chorus(a, ch, rate, v.chorus);
            if (v.wobble > 0.005f) a = Wobble(a, ch, rate, v.wobble, v.wobbleHz);
            a = (float[])a.Clone(); int fr = a.Length / ch;
            if (v.ring > 0.005f) for (int f = 0; f < fr; f++) { float m = Mathf.Lerp(1, Mathf.Sin(2 * Mathf.PI * v.ringHz * f / rate) * 1.3f, v.ring); for (int c = 0; c < ch; c++) a[f * ch + c] *= m; }
            if (v.pulse > 0.005f) for (int f = 0; f < fr; f++) { float m = 1 - v.pulse * (0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * v.pulseHz * f / rate)); for (int c = 0; c < ch; c++) a[f * ch + c] *= m; }
            if (v.stutter > 0.005f) Stutter(a, ch, rate, v.stutter);
            if (v.crush > 0.005f) { int hold = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1, rate / 3200f, v.crush))); float steps = Mathf.Pow(2, Mathf.Lerp(15, 3.2f, Mathf.Sqrt(v.crush))); for (int c = 0; c < ch; c++) { float h = 0; for (int f = 0; f < fr; f++) { if (f % hold == 0) h = Mathf.Round(a[f * ch + c] * steps) / steps; a[f * ch + c] = h; } } }
            if (v.ghost > 0.005f)
            {
                // what comes before the sound: its own echo, played backwards
                int pre = Mathf.RoundToInt(rate * 0.45f), n = fr + pre; var rev = new float[n * ch]; for (int f = 0; f < fr; f++) for (int c = 0; c < ch; c++) rev[f * ch + c] = a[(fr - 1 - f) * ch + c]; var wet = AudioFx.Reverb(rev, ch, rate, 0.75f, 0.35f, 1f, false); var o = new float[n * ch]; int wn = wet.Length / ch; for (int f = 0; f < n; f++) { int q = n - 1 - f; for (int c = 0; c < ch; c++) { float w = q < wn ? wet[q * ch + c] : 0; o[f * ch + c] = w * v.ghost * 0.9f; } } for (int f = 0; f < fr; f++) for (int c = 0; c < ch; c++) o[(f + pre) * ch + c] += a[f * ch + c] * (1 - v.ghost * 0.35f); a = o;
            }
            return AudioFx.Limit(a);
        }

        static float[] Mix(float[] a, float[] b, float k) { var o = new float[a.Length]; float ra = 1e-9f, rb = 1e-9f; for (int i = 0; i < a.Length; i++) { ra += a[i] * a[i]; rb += b[i] * b[i]; } float g = Mathf.Clamp(Mathf.Sqrt(ra / rb), 0.2f, 6f); for (int i = 0; i < a.Length; i++) o[i] = a[i] * (1 - k) + b[i] * g * k; return o; }

        // every little piece rebuilt: all waves starting together (a robot on one note) or anyhow (a whisper)
        static float[] Spectral(float[] a, int ch, int rate, float hz, bool random)
        {
            const int N = 1024; int n = a.Length / ch, hop = random ? N / 4 : Mathf.Clamp(Mathf.RoundToInt(rate / hz), 64, N); var o = new float[a.Length]; var w = new float[N]; for (int i = 0; i < N; i++) w[i] = 0.5f - 0.5f * Mathf.Cos(2 * Mathf.PI * i / N); var re = new float[N]; var im = new float[N]; var rnd = new System.Random(5);
            for (int c = 0; c < ch; c++) for (int at = -N / 2; at < n; at += hop)
                {
                    for (int i = 0; i < N; i++) { int f = at + i; re[i] = f >= 0 && f < n ? a[f * ch + c] * w[i] : 0; im[i] = 0; } AudioCleaner.FFT(re, im, false);
                    for (int k = 0; k <= N / 2; k++) { float m = Mathf.Sqrt(re[k] * re[k] + im[k] * im[k]); if (random) { float ph = (float)rnd.NextDouble() * 6.2832f; re[k] = m * Mathf.Cos(ph); im[k] = m * Mathf.Sin(ph); } else { float s = (k & 1) == 0 ? 1 : -1; re[k] = m * s; im[k] = 0; } if (k > 0 && k < N / 2) { re[N - k] = re[k]; im[N - k] = -im[k]; } }
                    AudioCleaner.FFT(re, im, true); for (int i = 0; i < N; i++) { int f = at + i; if (f >= 0 && f < n) o[f * ch + c] += re[i] * w[i]; }
                }
            return o;
        }

        static float[] Chorus(float[] a, int ch, int rate, float amount)
        {
            int n = a.Length / ch; var o = new float[a.Length]; float[] hz = { 0.31f, 0.47f, 0.73f, 0.59f }, ms = { 13, 19, 27, 34 };
            for (int f = 0; f < n; f++) for (int c = 0; c < ch; c++) { float x = a[f * ch + c], s = 0; for (int k = 0; k < 4; k++) { float d = (ms[k] + 5f * Mathf.Sin(2 * Mathf.PI * hz[k] * f / rate + k * 1.7f + c * 0.9f)) * 0.001f * rate, p = f - d; int i0 = Mathf.FloorToInt(p); if (i0 >= 0 && i0 + 1 < n) s += Mathf.Lerp(a[i0 * ch + c], a[(i0 + 1) * ch + c], p - i0); } o[f * ch + c] = (x * (1 - amount * 0.35f) + s * amount * 0.42f); }
            return o;
        }
        static float[] Wobble(float[] a, int ch, int rate, float amount, float hz)
        {
            int n = a.Length / ch; var o = new float[a.Length]; float depth = amount * 0.35f * rate / (2 * Mathf.PI * Mathf.Max(0.3f, hz)) * 0.35f, mid = depth + 2; for (int f = 0; f < n; f++) { float p = f - mid + depth * Mathf.Sin(2 * Mathf.PI * hz * f / rate); int i0 = Mathf.FloorToInt(p); if (i0 < 0 || i0 + 1 >= n) continue; for (int c = 0; c < ch; c++) o[f * ch + c] = Mathf.Lerp(a[i0 * ch + c], a[(i0 + 1) * ch + c], p - i0); }
            return o;
        }
        static void Stutter(float[] a, int ch, int rate, float amount)
        {
            int n = a.Length / ch, slice = rate / 14; var rnd = new System.Random(11); int f = slice * 2; while (f < n - slice * 4) { if (rnd.NextDouble() < amount * 0.35f) { int len = slice / (1 + rnd.Next(3)), times = 2 + rnd.Next(4); for (int t = 1; t < times; t++) for (int i = 0; i < len; i++) { int to = f + t * len + i; if (to >= n) break; float k = Mathf.Clamp01(Mathf.Min(i, len - i) / 60f); for (int c = 0; c < ch; c++) a[to * ch + c] = a[(f + i) * ch + c] * k; } f += len * times; } else f += slice; }
        }
    }
}
