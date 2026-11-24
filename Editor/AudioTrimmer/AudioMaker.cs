using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Makes simple game sounds from nothing: whooshes, hits, steps, explosions, zaps, blips, pick-ups, risers, wind.
    /// Always stereo, 44.1 kHz. The same settings and number always give the same sound.</summary>
    public static class AudioMaker
    {
        public const int Rate = 44100;
        public class Knob { public string name, tip; public float lo, hi, def; public Knob(string n, float lo, float hi, float def, string tip = "") { name = n; this.lo = lo; this.hi = hi; this.def = def; this.tip = tip; } }
        public class Kind { public string name, about; public Color tint; public Knob[] knobs; public (string name, float[] v)[] ready; public Func<float[], System.Random, float[]> make; public bool loops; }

        // ------------------------------------------------------------------ pieces

        class Noise { readonly System.Random r; float b0, b1, b2; public Noise(System.Random r) { this.r = r; } public float White => (float)r.NextDouble() * 2 - 1; public float Pink { get { float w = White; b0 = 0.99765f * b0 + w * 0.0990460f; b1 = 0.963f * b1 + w * 0.2965164f; b2 = 0.57f * b2 + w * 1.0526913f; return (b0 + b1 + b2 + w * 0.1848f) * 0.22f; } } }
        // a filter whose frequency can move every sample
        struct Svf { float s1, s2; public float Low, Band, High; public void Run(float x, float hz, float q) { float g = Mathf.Tan(Mathf.PI * Mathf.Clamp(hz, 10f, 18000f) / Rate), k = 1f / Mathf.Max(0.5f, q), a1 = 1f / (1 + g * (g + k)), v3 = x - s2, v1 = a1 * s1 + g * a1 * v3, v2 = s2 + g * a1 * s1 + g * g * a1 * v3; s1 = 2 * v1 - s1; s2 = 2 * v2 - s2; Low = v2; Band = v1; High = x - k * v1 - v2; } } // (stays calm at any setting)
        // slow wandering between -1 and 1
        class Wander { readonly System.Random r; float a, b, t, sp; public Wander(System.Random r, float hz) { this.r = r; sp = hz / Rate; a = N(); b = N(); t = (float)r.NextDouble(); } float N() => (float)r.NextDouble() * 2 - 1; public float Next() { t += sp; if (t >= 1) { t -= 1; a = b; b = N(); } float k = t * t * (3 - 2 * t); return a + (b - a) * k; } }
        static float Env(float t, float attack, float decay) => t < attack ? t / Mathf.Max(1e-5f, attack) : Mathf.Exp(-(t - attack) / Mathf.Max(1e-4f, decay));
        static float Bell(float u, float peak, float sharp) { float k = u < peak ? u / Mathf.Max(0.01f, peak) : (1 - u) / Mathf.Max(0.01f, 1 - peak); return Mathf.Pow(Mathf.Clamp01(k), sharp); }
        static float Tanh(float x) => (float)Math.Tanh(x);
        static float[] New(float seconds) => new float[Mathf.Max(64, Mathf.RoundToInt(seconds * Rate)) * 2];
        static void Pan(float[] o, int f, float v, float pan) { float a = (pan + 1) * Mathf.PI / 4; o[f * 2] += v * Mathf.Cos(a) * 1.2f; o[f * 2 + 1] += v * Mathf.Sin(a) * 1.2f; }

        static float[] Finish(float[] o, float peak = 0.85f, float fadeMs = 4)
        {
            // no hum under everything, no clicks at the ends, always equally tall
            for (int c = 0; c < 2; c++) { float x1 = 0, y1 = 0; for (int i = c; i < o.Length; i += 2) { float x = o[i], y = x - x1 + 0.9972f * y1; x1 = x; y1 = y; o[i] = y; } }
            float p = AudioFx.PeakOf(o); if (p > 1e-6f) for (int i = 0; i < o.Length; i++) o[i] *= peak / p; int n = o.Length / 2, fd = Mathf.Min(n / 4, (int)(fadeMs * 0.001f * Rate)); for (int f = 0; f < fd; f++) { float k = f / (float)fd; for (int c = 0; c < 2; c++) { o[f * 2 + c] *= k; o[(n - 1 - f) * 2 + c] *= k; } }
            return o;
        }

        // ------------------------------------------------------------------ the sounds

        // 0 length 1 weight 2 speed 3 sharpness 4 air 5 tear 6 whistle 7 when it passes 8 left-right 9 twice
        static float[] Whoosh(float[] k, System.Random r)
        {
            float len = k[0], deep = k[1]; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); var nz2 = new Noise(r); Svf main = default, body = default, air = default, main2 = default; var flut = new Wander(r, 18f + 40f * k[5]); var flut2 = new Wander(r, 95f); var drift = new Wander(r, 3f); float lo = Mathf.Lerp(900f, 130f, deep), up = 0.4f + k[2] * 3.2f, q = 0.7f + k[3] * k[3] * 11f; float ph = 0, dir = r.NextDouble() < 0.5 ? -1 : 1; int hits = k[9] > 0.5f ? 2 : 1;
            for (int f = 0; f < n; f++)
            {
                float u = f / (float)n, b = 0, w = 0; for (int h = 0; h < hits; h++) { float uu = hits == 1 ? u : Mathf.Clamp01((u - h * 0.42f) / 0.58f), g = hits == 1 ? 1 : h == 0 ? 0.7f : 1; float bb = Bell(uu, k[7], 2.2f) * g; if (bb > b) { b = bb; w = Bell(uu, k[7], 1.25f); } }
                // faster things are higher, and highest as they pass (then they drop: they're moving away)
                float after = u > k[7] ? 0.82f : 1f, hz = lo * Mathf.Pow(2f, up * w + drift.Next() * 0.25f) * after; float x = nz.Pink * 2.2f; main.Run(x, hz, q); main2.Run(x, hz * 1.9f, q * 0.7f); body.Run(nz2.Pink * 2f, lo * 0.55f * (1 + w), 0.8f); air.Run(nz2.White, 5200f + 4500f * w, 0.8f);
                float tear = 1 + k[5] * 0.85f * (flut.Next() * 0.7f + flut2.Next() * 0.5f), v = (main.Band * (1.2f + k[3]) + main2.Band * 0.5f) * tear; v += body.Low * (0.3f + deep * 1.5f); v += air.High * k[4] * 0.5f * b * tear; ph += hz * 1.5f / Rate; v += Mathf.Sin(ph * 6.2832f) * k[6] * 0.35f * w * w * (0.7f + 0.3f * flut.Next());
                Pan(o, f, v * b, dir * k[8] * Mathf.Clamp((u - k[7]) * 2.2f, -1, 1));
            }
            return Finish(o, 0.85f, Mathf.Min(12, len * 60));
        }

        // 0 length 1 weight 2 hardness 3 ring 4 dirt 5 size of the crack
        static float[] Hit(float[] k, System.Random r)
        {
            float len = k[0]; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); Svf crack = default, dust = default, thud = default; float lo = Mathf.Lerp(150f, 42f, k[1]), ph = 0; var modes = Enumerable.Range(0, 5).Select(i => (hz: Mathf.Lerp(2400f, 260f, k[1]) * (0.55f + i * 0.62f) * (0.93f + 0.14f * (float)r.NextDouble()), p: (float)r.NextDouble() * 6.28f, a: 1f / (1 + i), d: (0.02f + 0.3f * k[3]) / (1 + i * 0.6f))).ToArray(); float hard = Mathf.Lerp(0.012f, 0.0008f, k[2]);
            for (int f = 0; f < n; f++)
            {
                float t = f / (float)Rate, hz = lo * (1 + 2.6f * Mathf.Exp(-t / 0.018f)); ph += hz / Rate; float th = Mathf.Sin(ph * 6.2832f) * Env(t, 0.001f + hard, 0.05f + 0.22f * k[1] * len); thud.Run(nz.Pink, 320f, 0.7f); th += thud.Low * 1.6f * Env(t, 0.002f, 0.05f) * (0.4f + k[1]); crack.Run(nz.White, Mathf.Lerp(900f, 5200f, k[2]), 1.1f); float cr = crack.Band * Env(t, hard, 0.006f + 0.03f * k[5]) * (0.3f + 1.6f * k[5]); float ring = 0; foreach (var m in modes) ring += Mathf.Sin(6.2832f * m.hz * t + m.p) * m.a * Mathf.Exp(-t / m.d); ring *= k[3] * 0.55f * Env(t, 0.0005f + hard, 10f); dust.Run(nz.White, 2600f, 0.6f); float dr = dust.Band * k[4] * 0.5f * Env(t, 0.01f, 0.16f * len) * (nz.White > 0.3f ? 1.4f : 0.5f);
                float v = Tanh((th * (0.6f + k[1]) + cr + ring + dr) * (1 + 1.6f * k[2])); Pan(o, f, v, 0);
            }
            return Finish(o, 0.92f, 1.5f);
        }

        // 0 surface (stone, wood, grass, gravel, snow, metal, water) 1 weight 2 heel and toe 3 crunch 4 brightness
        static float[] Step(float[] k, System.Random r)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt(k[0]), 0, 6); float len = s == 6 ? 0.5f : s == 3 || s == 4 ? 0.36f : 0.28f; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); Svf a = default, b = default, c = default, lo = default; float gap = 0.045f + 0.05f * k[2] + 0.015f * (float)r.NextDouble(), br = Mathf.Pow(2f, (k[4] - 0.5f) * 2f); float grain = 0; float ph = 0, drop = 400 + 500 * (float)r.NextDouble(); float[] ringHz = { 420 + 160 * (float)r.NextDouble(), 930 + 300 * (float)r.NextDouble(), 1800 + 500 * (float)r.NextDouble() };
            for (int f = 0; f < n; f++)
            {
                float t = f / (float)Rate, t2 = t - gap, e1 = Env(t, 0.002f, s == 2 || s == 4 ? 0.05f : 0.02f), e2 = t2 > 0 ? Env(t2, 0.003f, s == 2 || s == 4 ? 0.06f : 0.025f) * (0.35f + 0.65f * k[2]) : 0, e = e1 + e2; float thudE = Env(t, 0.003f, 0.028f) + (t2 > 0 ? Env(t2, 0.003f, 0.02f) * 0.5f * k[2] : 0); ph += (Mathf.Lerp(150f, 70f, k[1]) * (1 + Mathf.Exp(-t / 0.012f))) / Rate; float thud = Mathf.Sin(ph * 6.2832f) * thudE * (0.25f + 0.9f * k[1]); float v = 0;
                // crunch: lots of tiny separate bits
                if (r.NextDouble() < (s == 3 ? 0.012f : s == 4 ? 0.02f : 0.006f)) grain = 0.4f + 0.6f * (float)r.NextDouble(); grain *= s == 3 ? 0.985f : 0.97f; float w = nz.White;
                switch (s)
                {
                    case 0: a.Run(w, 1900f * br, 1.1f); b.Run(w, 4200f * br, 0.9f); v = a.Band * e * 0.9f + b.Band * Env(t, 0.0008f, 0.006f) * 0.8f + w * grain * e * k[3] * 0.35f; break;
                    case 1: a.Run(w, 520f * br, 3.2f); b.Run(w, 1250f * br, 2.4f); v = a.Band * e * 1.5f + b.Band * e * 0.5f + Mathf.Sin(6.28f * 210f * br * t) * Env(t, 0.001f, 0.04f) * 0.35f; break;
                    case 2: a.Run(w, 2600f * br, 0.6f); v = a.Band * e * 0.55f * (0.5f + grain) + w * grain * e * k[3] * 0.3f; thud *= 0.7f; break;
                    case 3: a.Run(w, 3100f * br, 0.8f); v = a.Band * grain * (Env(t, 0.01f, 0.07f) + e2) * (0.6f + 1.4f * k[3]); break;
                    case 4: a.Run(w, 1500f * br, 0.7f); b.Run(w, 5200f * br, 0.8f); v = (a.Band + b.Band * 0.5f) * grain * (Env(t, 0.03f, 0.08f) + e2) * (0.6f + 1.2f * k[3]); thud *= 0.5f; break;
                    case 5: a.Run(w, 2800f * br, 1.4f); v = a.Band * e * 0.6f; foreach (var hz in ringHz) v += Mathf.Sin(6.28f * hz * br * t) * Env(t, 0.0006f, 0.06f) * 0.3f; break;
                    case 6: a.Run(w, 900f * br * (1 + 2 * Mathf.Exp(-t / 0.05f)), 1.6f); b.Run(w, 3600f * br, 0.7f); v = a.Band * Env(t, 0.008f, 0.09f) * 1.2f + b.Band * Env(t, 0.004f, 0.05f) * 0.6f; for (int q = 0; q < 3; q++) { float tq = t - 0.06f - q * 0.07f; if (tq > 0) v += Mathf.Sin(6.28f * drop * (1 + q * 0.35f) * tq * (1 + tq * 6)) * Env(tq, 0.004f, 0.018f) * 0.2f * k[3]; } thud *= 0.35f; break;
                }
                lo.Run(v, 9000f, 0.7f); Pan(o, f, lo.Low + thud, 0);
            }
            return Finish(o, 0.75f, 2f);
        }

        // 0 length 1 size 2 sharpness 3 debris 4 rumble 5 distance
        static float[] Explosion(float[] k, System.Random r)
        {
            float len = k[0]; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); var nz2 = new Noise(r); Svf a = default, b = default, c = default, far = default; float ph = 0, bit = 0; var wob = new Wander(r, 22f); var pan = new Wander(r, 1.5f);
            for (int f = 0; f < n; f++)
            {
                float t = f / (float)Rate, u = t / len, hz = Mathf.Lerp(5200f, 900f, k[1]) * Mathf.Exp(-t / (0.12f * len + 0.04f)) + 60f; a.Run(nz.Pink * 2.4f, hz, 0.7f); float v = a.Low * 1.5f * Env(t, 0.004f, 0.28f * len); b.Run(nz.White, 3800f, 0.8f); v += b.Band * k[2] * 1.4f * Env(t, 0.0006f, 0.02f); ph += (Mathf.Lerp(90f, 34f, k[1]) * (1 + 1.6f * Mathf.Exp(-t / 0.05f))) / Rate; v += Mathf.Sin(ph * 6.2832f) * Env(t, 0.004f, 0.22f * len) * (0.5f + k[1]); c.Run(nz2.Pink * 3f, 110f, 0.8f); v += c.Low * k[4] * 1.6f * Env(t, 0.05f * len, 0.5f * len) * (0.7f + 0.5f * wob.Next());
                if (r.NextDouble() < 0.0016 * (1 - u)) bit = (float)r.NextDouble(); bit *= 0.992f; v += nz2.White * bit * k[3] * 0.5f * Mathf.Clamp01(t * 8); v = Tanh(v * 1.7f); if (k[5] > 0.01f) { far.Run(v, 14000f * Mathf.Pow(0.03f, k[5]), 0.6f); v = far.Low; } v *= 1 - u * u * u; Pan(o, f, v, pan.Next() * 0.25f * (1 - k[5]));
            }
            return Finish(o, 0.95f, 2f);
        }

        // 0 length 1 pitch 2 drop 3 wobble 4 buzz 5 noise
        static float[] Zap(float[] k, System.Random r)
        {
            float len = k[0]; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); float ph = 0, ph2 = 0; Svf s = default; float start = 110f * Mathf.Pow(2f, k[1] * 5f);
            for (int f = 0; f < n; f++) { float t = f / (float)Rate, u = t / len, hz = start * Mathf.Pow(2f, -k[2] * 5f * u) * (1 + 0.25f * k[3] * Mathf.Sin(6.2832f * (14 + 30 * k[3]) * t)); ph += hz / Rate; ph2 += hz * 1.007f / Rate; float sn = Mathf.Sin(ph * 6.2832f), sq = (ph % 1 < 0.5f ? 1 : -1) + (ph2 % 1 < 0.5f ? 0.8f : -0.8f), v = Mathf.Lerp(sn, sq * 0.6f, k[4]); s.Run(nz.White, hz * 2.5f, 2f); v += s.Band * k[5] * 0.9f; v *= Env(t, 0.003f, 0.35f * len) * (1 - u); Pan(o, f, Tanh(v * 1.3f), Mathf.Sin(t * 9) * 0.15f); }
            return Finish(o, 0.8f, 2f);
        }

        // 0 length 1 pitch 2 change 3 tone (soft to buzzy) 4 notes 5 click
        static float[] Blip(float[] k, System.Random r)
        {
            int notes = Mathf.Clamp(Mathf.RoundToInt(k[4]), 1, 4); float one = k[0], len = one * notes + 0.02f; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); float ph = 0, basehz = 180f * Mathf.Pow(2f, k[1] * 3.6f);
            for (int f = 0; f < n; f++) { float t = f / (float)Rate; int i = Mathf.Min(notes - 1, (int)(t / one)); float tt = t - i * one, u = tt / one, hz = basehz * Mathf.Pow(2f, i * Mathf.Sign(k[2] + 0.0001f) * 4f / 12f * (notes > 1 ? 1 : 0)) * Mathf.Pow(2f, k[2] * u); ph += hz / Rate; float p = ph % 1, sn = Mathf.Sin(p * 6.2832f), tr = 4 * Mathf.Abs(p - 0.5f) - 1, sq = p < 0.5f ? 1 : -1, v = k[3] < 0.5f ? Mathf.Lerp(sn, tr, k[3] * 2) : Mathf.Lerp(tr, sq * 0.6f, k[3] * 2 - 1); v *= Env(tt, 0.004f, one * 0.38f) * Mathf.Clamp01((one - tt) * 200); v += nz.White * k[5] * 0.6f * Env(tt, 0.0004f, 0.004f); Pan(o, f, v, 0); }
            return Finish(o, 0.7f, 2f);
        }

        // 0 length 1 pitch 2 sparkle 3 shape (0 up, 1 chord) 4 notes
        static float[] Pickup(float[] k, System.Random r)
        {
            int notes = Mathf.Clamp(Mathf.RoundToInt(k[4]), 1, 6); float len = k[0]; var o = New(len); int n = o.Length / 2; int[] scale = { 0, 4, 7, 12, 16, 19 }; float basehz = 330f * Mathf.Pow(2f, k[1] * 2f), gap = k[3] > 0.5f ? 0.012f : Mathf.Min(0.08f, len * 0.5f / notes); var sp = Enumerable.Range(0, 14).Select(i => (t: (float)r.NextDouble() * len * 0.6f, hz: 2600f + 5200f * (float)r.NextDouble(), p: (float)r.NextDouble() * 2 - 1)).ToArray();
            for (int f = 0; f < n; f++) { float t = f / (float)Rate, v = 0; for (int i = 0; i < notes; i++) { float tt = t - i * gap; if (tt < 0) continue; float hz = basehz * Mathf.Pow(2f, scale[i] / 12f), e = Env(tt, 0.003f, len * 0.28f); v += (Mathf.Sin(6.2832f * hz * tt) + 0.35f * Mathf.Sin(6.2832f * hz * 2.01f * tt) * Mathf.Exp(-tt * 9) + 0.18f * Mathf.Sin(6.2832f * hz * 3.98f * tt) * Mathf.Exp(-tt * 18)) * e / Mathf.Sqrt(notes); } v *= 1 - t / len; Pan(o, f, v, 0); foreach (var s in sp) { float tt = t - s.t; if (tt > 0 && tt < 0.25f) Pan(o, f, Mathf.Sin(6.2832f * s.hz * tt) * Env(tt, 0.002f, 0.045f) * k[2] * 0.14f, s.p); } }
            return Finish(o, 0.75f, 3f);
        }

        // 0 length 1 low end 2 tone 3 how far up 4 pulse
        static float[] Riser(float[] k, System.Random r)
        {
            float len = k[0]; var o = New(len); int n = o.Length / 2; var nz = new Noise(r); Svf s = default; float p1 = 0, p2 = 0, p3 = 0; float lo = Mathf.Lerp(400f, 70f, k[1]);
            for (int f = 0; f < n; f++) { float t = f / (float)Rate, u = t / len, up = Mathf.Pow(2f, k[3] * 5f * u * u), hz = lo * up; p1 += hz / Rate; p2 += hz * 1.005f / Rate; p3 += hz * 1.5f / Rate; s.Run(nz.Pink * 2, lo * 3 * up, 1.4f); float saw = ((p1 % 1) + (p2 % 1) + 0.6f * (p3 % 1) - 1.3f), v = s.Band * (1 - k[2] * 0.6f) + saw * k[2] * 0.4f; float pulse = 1 - k[4] * (0.5f + 0.5f * Mathf.Sin(6.2832f * (3 + 13 * u * u) * t * 0.6f)); v *= u * u * pulse; Pan(o, f, v, Mathf.Sin(t * (2 + 6 * u)) * 0.35f); }
            return Finish(o, 0.85f, 3f);
        }

        // 0 length 1 depth 2 gusts 3 howl 4 air   (loops)
        static float[] Wind(float[] k, System.Random r)
        {
            float len = k[0], over = 0.6f; var o = New(len + over); int n = o.Length / 2; var nz = new[] { new Noise(r), new Noise(r) }; var sv = new Svf[2]; var hw = new Svf[2]; var ar = new Svf[2]; var g = new Wander(r, 0.35f + 0.5f * k[2]); var g2 = new Wander(r, 1.7f); var h = new Wander(r, 0.6f);
            for (int f = 0; f < n; f++) { float gust = Mathf.Clamp01(0.55f + k[2] * (0.5f * g.Next() + 0.2f * g2.Next())), hh = h.Next(); for (int c = 0; c < 2; c++) { sv[c].Run(nz[c].Pink * 2, Mathf.Lerp(900f, 180f, k[1]) * (0.6f + gust), 0.8f); hw[c].Run(nz[c].Pink * 2, 420f * Mathf.Pow(2f, hh * 0.7f + gust * 0.8f) * (c == 0 ? 1 : 1.03f), 14f); ar[c].Run(nz[c].White, 5200f, 0.7f); o[f * 2 + c] = sv[c].Low * gust + hw[c].Band * k[3] * 0.5f * gust * gust + ar[c].High * k[4] * 0.08f * gust; } }
            // the end is blended into the start, so it loops
            int m = Mathf.RoundToInt(len * Rate), x = n - m; var l = new float[m * 2]; for (int f = 0; f < m; f++) for (int c = 0; c < 2; c++) { float v = o[f * 2 + c]; if (f < x) { float w = f / (float)x; v = v * Mathf.Sin(w * Mathf.PI / 2) + o[(m + f) * 2 + c] * Mathf.Cos(w * Mathf.PI / 2); } l[f * 2 + c] = v; }
            float p = AudioFx.PeakOf(l); if (p > 1e-6f) for (int i = 0; i < l.Length; i++) l[i] *= 0.6f / p; return l;
        }

        static Knob L(float lo, float hi, float def) => new Knob("Length", lo, hi, def, "Seconds");
        public static readonly Kind[] Kinds =
        {
            new Kind { name = "Whoosh", about = "Swings, dashes, things flying past", tint = new Color(0.5f, 0.85f, 1f), make = Whoosh, knobs = new[] { L(0.08f, 3f, 0.4f), new Knob("Weight", 0, 1, 0.4f, "A thin stick to a huge axe"), new Knob("Speed", 0, 1, 0.5f, "How far the pitch goes up as it passes"), new Knob("Sharpness", 0, 1, 0.35f, "A soft cloth to a thin blade"), new Knob("Air", 0, 1, 0.4f, "Hiss"), new Knob("Tear", 0, 1, 0.2f, "Flutter, like cloth or fire"), new Knob("Whistle", 0, 1, 0.05f, "A tone in it"), new Knob("When it passes", 0.1f, 0.9f, 0.42f, "Early is a sudden swing, late is something coming at you"), new Knob("Left to right", 0, 1, 0.4f), new Knob("Twice", 0, 1, 0, "Over half: two swings, one after the other") },
                ready = new[] { ("Sword", new[] { 0.32f, 0.3f, 0.7f, 0.6f, 0.5f, 0.1f, 0.14f, 0.45f, 0.5f, 0f }), ("Heavy axe", new[] { 0.6f, 0.85f, 0.5f, 0.2f, 0.25f, 0.25f, 0f, 0.5f, 0.4f, 0f }), ("Fist", new[] { 0.18f, 0.6f, 0.4f, 0.1f, 0.15f, 0.15f, 0f, 0.4f, 0.1f, 0f }), ("Arrow", new[] { 0.5f, 0.1f, 0.8f, 0.75f, 0.45f, 0.35f, 0.25f, 0.35f, 0.9f, 0f }), ("Cape", new[] { 0.5f, 0.55f, 0.25f, 0f, 0.3f, 0.9f, 0f, 0.4f, 0.3f, 0f }), ("Dash", new[] { 0.38f, 0.5f, 0.6f, 0.15f, 0.6f, 0.15f, 0f, 0.22f, 0.2f, 0f }), ("Fly past", new[] { 1.4f, 0.75f, 0.45f, 0.25f, 0.3f, 0.3f, 0.06f, 0.5f, 1f, 0f }), ("Magic", new[] { 0.9f, 0.35f, 0.6f, 0.5f, 0.6f, 0.45f, 0.55f, 0.6f, 0.6f, 0f }), ("Fireball", new[] { 0.8f, 0.75f, 0.4f, 0.05f, 0.35f, 1f, 0f, 0.35f, 0.4f, 0f }), ("Double swing", new[] { 0.6f, 0.35f, 0.65f, 0.45f, 0.4f, 0.1f, 0.08f, 0.42f, 0.7f, 1f }), ("Menu slide", new[] { 0.3f, 0.2f, 0.35f, 0.05f, 0.7f, 0f, 0f, 0.5f, 0.2f, 0f }) } },
            new Kind { name = "Hit", about = "Punches, knocks, things landing", tint = new Color(1f, 0.65f, 0.45f), make = Hit, knobs = new[] { L(0.08f, 1.5f, 0.35f), new Knob("Weight", 0, 1, 0.5f), new Knob("Hardness", 0, 1, 0.5f, "A pillow to a hammer"), new Knob("Ring", 0, 1, 0.15f, "Metal and glass ring"), new Knob("Dirt", 0, 1, 0.1f), new Knob("Crack", 0, 1, 0.4f, "The sharp bit at the start") },
                ready = new[] { ("Punch", new[] { 0.25f, 0.6f, 0.35f, 0f, 0.1f, 0.5f }), ("Wood", new[] { 0.3f, 0.35f, 0.75f, 0.2f, 0f, 0.4f }), ("Metal", new[] { 0.9f, 0.25f, 1f, 1f, 0f, 0.35f }), ("Stone", new[] { 0.45f, 0.6f, 0.9f, 0.05f, 0.6f, 0.7f }), ("Body fall", new[] { 0.5f, 0.95f, 0.1f, 0f, 0.35f, 0.15f }), ("Slam", new[] { 0.8f, 1f, 0.7f, 0.12f, 0.4f, 0.9f }), ("Tap", new[] { 0.1f, 0.1f, 0.8f, 0.08f, 0f, 0.5f }) } },
            new Kind { name = "Footstep", about = "One step, on anything", tint = new Color(0.7f, 0.85f, 0.5f), make = Step, knobs = new[] { new Knob("Surface", 0, 6, 0, "0 stone, 1 wood, 2 grass, 3 gravel, 4 snow, 5 metal, 6 water"), new Knob("Weight", 0, 1, 0.45f), new Knob("Heel and toe", 0, 1, 0.5f, "One thud, or heel then toe"), new Knob("Crunch", 0, 1, 0.5f), new Knob("Brightness", 0, 1, 0.5f) },
                ready = new[] { ("Stone", new[] { 0f, 0.4f, 0.6f, 0.3f, 0.5f }), ("Wood", new[] { 1f, 0.5f, 0.6f, 0f, 0.45f }), ("Grass", new[] { 2f, 0.4f, 0.4f, 0.5f, 0.5f }), ("Gravel", new[] { 3f, 0.4f, 0.5f, 0.7f, 0.5f }), ("Snow", new[] { 4f, 0.5f, 0.5f, 0.8f, 0.4f }), ("Metal", new[] { 5f, 0.35f, 0.5f, 0f, 0.5f }), ("Water", new[] { 6f, 0.3f, 0.5f, 0.6f, 0.5f }) } },
            new Kind { name = "Explosion", about = "From a pop to a building", tint = new Color(1f, 0.5f, 0.35f), make = Explosion, knobs = new[] { L(0.3f, 5f, 1.8f), new Knob("Size", 0, 1, 0.6f), new Knob("Sharpness", 0, 1, 0.5f), new Knob("Debris", 0, 1, 0.4f), new Knob("Rumble", 0, 1, 0.5f), new Knob("Distance", 0, 1, 0.1f) },
                ready = new[] { ("Grenade", new[] { 1.4f, 0.5f, 0.8f, 0.6f, 0.3f, 0f }), ("Big", new[] { 3f, 0.9f, 0.6f, 0.6f, 0.9f, 0f }), ("Far away", new[] { 3f, 0.9f, 0.1f, 0.1f, 1f, 0.85f }), ("Pop", new[] { 0.35f, 0.1f, 0.6f, 0.1f, 0f, 0f }), ("Cannon", new[] { 1.2f, 0.75f, 0.9f, 0.05f, 0.35f, 0.05f }) } },
            new Kind { name = "Zap", about = "Lasers, shots, power-downs", tint = new Color(0.8f, 0.6f, 1f), make = Zap, knobs = new[] { L(0.06f, 1.5f, 0.28f), new Knob("Pitch", 0, 1, 0.75f), new Knob("Drop", -0.6f, 1, 0.5f, "Below 0 it goes up"), new Knob("Wobble", 0, 1, 0), new Knob("Buzz", 0, 1, 0.5f), new Knob("Noise", 0, 1, 0.15f) },
                ready = new[] { ("Laser", new[] { 0.28f, 0.85f, 0.6f, 0f, 0.4f, 0.1f }), ("Blaster", new[] { 0.35f, 0.65f, 0.8f, 0f, 0.8f, 0.5f }), ("Power up", new[] { 0.6f, 0.4f, -0.45f, 0.25f, 0.4f, 0f }), ("Power down", new[] { 0.9f, 0.6f, 0.6f, 0.2f, 0.5f, 0f }), ("Alien", new[] { 0.7f, 0.6f, 0.25f, 0.9f, 0.2f, 0f }) } },
            new Kind { name = "Blip", about = "Buttons, text, jumps", tint = new Color(0.5f, 1f, 0.75f), make = Blip, knobs = new[] { L(0.02f, 0.5f, 0.08f), new Knob("Pitch", 0, 1, 0.5f), new Knob("Change", -2, 2, 0, "Up or down while it plays"), new Knob("Tone", 0, 1, 0.3f, "Soft to buzzy"), new Knob("Notes", 1, 4, 1), new Knob("Click", 0, 1, 0.1f) },
                ready = new[] { ("Button", new[] { 0.06f, 0.5f, 0f, 0.2f, 1f, 0.3f }), ("Hover", new[] { 0.03f, 0.65f, 0f, 0f, 1f, 0.1f }), ("Confirm", new[] { 0.08f, 0.5f, 0.1f, 0.3f, 2f, 0f }), ("Cancel", new[] { 0.09f, 0.4f, -0.1f, 0.4f, 2f, 0f }), ("Jump", new[] { 0.22f, 0.3f, 1.2f, 0.75f, 1f, 0f }), ("Talking", new[] { 0.045f, 0.45f, 0.2f, 0.9f, 1f, 0f }), ("Error", new[] { 0.14f, 0.15f, 0f, 0.95f, 2f, 0f }) } },
            new Kind { name = "Pick-up", about = "Coins, items, level up", tint = new Color(1f, 0.85f, 0.4f), make = Pickup, knobs = new[] { L(0.15f, 2f, 0.5f), new Knob("Pitch", 0, 1, 0.6f), new Knob("Sparkle", 0, 1, 0.4f), new Knob("Chord", 0, 1, 0, "Under half: one note after the other. Over: together"), new Knob("Notes", 1, 6, 2) },
                ready = new[] { ("Coin", new[] { 0.4f, 0.9f, 0.2f, 0f, 2f }), ("Item", new[] { 0.7f, 0.5f, 0.6f, 0f, 3f }), ("Level up", new[] { 1.4f, 0.4f, 0.8f, 0f, 6f }), ("Heal", new[] { 1.2f, 0.35f, 1f, 1f, 4f }), ("Chime", new[] { 1.6f, 0.7f, 0.3f, 1f, 2f }) } },
            new Kind { name = "Riser", about = "Building up to something", tint = new Color(0.95f, 0.6f, 0.85f), make = Riser, knobs = new[] { L(0.4f, 8f, 2f), new Knob("Low end", 0, 1, 0.5f), new Knob("Tone", 0, 1, 0.4f, "Noise to a synth"), new Knob("How far up", 0, 1, 0.6f), new Knob("Pulse", 0, 1, 0) },
                ready = new[] { ("Trailer", new[] { 3f, 0.8f, 0.5f, 0.7f, 0.2f }), ("Charge", new[] { 1.2f, 0.4f, 0.8f, 0.6f, 0f }), ("Alarm", new[] { 2f, 0.3f, 0.7f, 0.3f, 1f }), ("Air", new[] { 2f, 0.4f, 0f, 0.7f, 0f }) } },
            new Kind { name = "Wind", about = "Background, loops by itself", tint = new Color(0.7f, 0.8f, 0.9f), make = Wind, loops = true, knobs = new[] { L(2f, 20f, 8f), new Knob("Depth", 0, 1, 0.5f), new Knob("Gusts", 0, 1, 0.5f), new Knob("Howl", 0, 1, 0.2f), new Knob("Air", 0, 1, 0.4f) },
                ready = new[] { ("Breeze", new[] { 8f, 0.2f, 0.3f, 0f, 0.7f }), ("Storm", new[] { 10f, 0.8f, 0.9f, 0.4f, 0.6f }), ("Cave", new[] { 10f, 0.9f, 0.4f, 0.8f, 0.05f }), ("Mountain", new[] { 10f, 0.5f, 0.7f, 0.6f, 0.4f }) } },
        };

        public static float[] Make(int kind, float[] values, int seed) { var k = Kinds[kind]; var v = new float[k.knobs.Length]; for (int i = 0; i < v.Length; i++) v[i] = i < values.Length ? Mathf.Clamp(values[i], k.knobs[i].lo, k.knobs[i].hi) : k.knobs[i].def;
            // another take is also played a little differently (number 1 is exactly as set)
            if (seed != 1) { var j = new System.Random(seed * 31 + 5); for (int i = 0; i < v.Length; i++) { var kn = k.knobs[i]; float r = (float)j.NextDouble() * 2 - 1; if (kn.name == "Surface" || kn.name == "Notes" || kn.name == "Twice" || kn.name == "Chord") continue; v[i] = kn.name == "Length" ? Mathf.Clamp(v[i] * (1 + r * 0.06f), kn.lo, kn.hi) : Mathf.Clamp(v[i] + r * 0.035f * (kn.hi - kn.lo), kn.lo, kn.hi); } }
            return k.make(v, new System.Random(seed * 7919 + kind)); }
    }
}
