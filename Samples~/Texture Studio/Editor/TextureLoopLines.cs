using System;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Make it loop for stripes, slats, planks and fabric: the lines are made exactly straight, the picture is cut
    /// at a whole number of them, and only then joined, so nothing bends or doubles up at the join.</summary>
    public static class TextureLoopLines
    {
        public static Pixels Loop(Pixels p, float blend, TextureOps.Ways ways, out string found)
        {
            bool upright = RunUpright(p); var q = upright ? p : Turn(p);
            // (in the turned picture the lines always run up and down)
            bool along = ways != (upright ? TextureOps.Ways.Sideways : TextureOps.Ways.UpDown), across = ways != (upright ? TextureOps.Ways.UpDown : TextureOps.Ways.Sideways);
            q = Straighten(q, out float slant);
            float period = Period(Profile(q, 0, q.h)); int lines = 0;
            if (across && period > 2) { lines = Mathf.Max(1, Mathf.FloorToInt(q.w / period)); q = Crop(q, lines * period); }
            if (along) q = Join(q, Mathf.Clamp(blend, 0.03f, 0.5f), false, 0);
            if (across) q = Join(q, Mathf.Clamp(blend * 0.6f, 0.02f, 0.3f), true, period > 2 ? q.w / (float)lines : 0);
            found = (upright ? "lines up and down" : "lines across") + (lines > 0 ? ", " + lines + " of them" : "") + (Mathf.Abs(slant) > 0.4f ? ", straightened by " + slant.ToString("0") + " px" : "");
            return upright ? q : Turn(q);
        }

        static float Lum(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;

        /// <summary>True when the lines run up and down (the picture changes most going sideways).</summary>
        public static bool RunUpright(Pixels p)
        {
            double sx = 0, sy = 0; int step = Mathf.Max(1, p.w / 256);
            for (int y = 1; y < p.h - 1; y += step) for (int x = 1; x < p.w - 1; x += step) { float c = Lum(p.c[y * p.w + x]); sx += Mathf.Abs(Lum(p.c[y * p.w + x + 1]) - c); sy += Mathf.Abs(Lum(p.c[(y + 1) * p.w + x]) - c); }
            return sx >= sy;
        }

        static Pixels Turn(Pixels p) { var r = new Pixels(p.h, p.w, p.srgb); for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) r.c[x * p.h + y] = p.c[y * p.w + x]; return r; }

        static float[] Profile(Pixels p, int y0, int y1) { var a = new float[p.w]; for (int y = y0; y < y1; y++) for (int x = 0; x < p.w; x++) a[x] += Lum(p.c[y * p.w + x]); float m = 0; for (int x = 0; x < p.w; x++) m += a[x]; m /= p.w; for (int x = 0; x < p.w; x++) a[x] -= m; return a; }

        /// <summary>Leans every row back so the lines stand exactly straight. Followed band by band up the picture, each step
        /// much smaller than the gap between two lines, so it can't jump to the wrong one.</summary>
        static Pixels Straighten(Pixels p, out float slant)
        {
            const int N = 12; int band = Mathf.Max(2, p.h / N); float per0 = Period(Profile(p, 0, band)); int most = Mathf.Max(1, Mathf.Min(p.w / 10, per0 > 2 ? Mathf.FloorToInt(per0 * 0.45f) : p.w / 10)); float total = 0; var prev = Profile(p, 0, band);
            for (int k = 1; k < N; k++)
            {
                var cur = Profile(p, k * band, Mathf.Min(p.h, (k + 1) * band)); float best = float.MinValue; int at = 0; var sc = new float[most * 2 + 1];
                for (int s = -most; s <= most; s++) { double sum = 0; for (int x = 0; x < p.w; x++) sum += prev[x] * cur[((x + s) % p.w + p.w) % p.w]; sc[s + most] = (float)sum; if (sum > best) { best = (float)sum; at = s; } }
                float f = at; int q = at + most; if (q > 0 && q < sc.Length - 1) { float l = sc[q - 1], m = sc[q], r = sc[q + 1], d = l - 2 * m + r; if (Mathf.Abs(d) > 1e-6f) f += 0.5f * (l - r) / d; } total += f; prev = cur;
            }
            slant = total; if (Mathf.Abs(total) < 0.3f) return p; float per = total / (band * (N - 1f)); var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y => { float off = per * (y - p.h / 2f); for (int x = 0; x < p.w; x++) o.c[y * p.w + x] = p.Sample(x + 0.5f + off, y + 0.5f); });
            return o;
        }

        /// <summary>How far apart the lines are (pixels), 0 when there's no clear repeat.</summary>
        public static float Period(Pixels p) => Period(Profile(p, 0, p.h));
        static float Period(float[] a)
        {
            int n = a.Length; double z = 1e-9; for (int x = 0; x < n; x++) z += a[x] * a[x]; var ac = new float[n / 2]; for (int lag = 1; lag < ac.Length; lag++) { double s = 0; for (int x = 0; x < n; x++) s += a[x] * a[(x + lag) % n]; ac[lag] = (float)(s / z); }
            // the first clear peak after the first dip
            int k = 1; while (k < ac.Length - 1 && ac[k] > 0) k++; int bestK = 0; float best = 0.15f; for (int i = k; i < ac.Length - 1; i++) if (ac[i] > ac[i - 1] && ac[i] >= ac[i + 1] && ac[i] > best) { best = ac[i]; bestK = i; if (ac[i] > 0.35f) break; }
            if (bestK == 0) return 0; float l = ac[bestK - 1], m = ac[bestK], r = ac[bestK + 1], d = l - 2 * m + r; return bestK + (Mathf.Abs(d) > 1e-6f ? 0.5f * (l - r) / d : 0);
        }

        /// <summary>Keeps a whole number of lines from the middle and stretches it back to the full width.</summary>
        static Pixels Crop(Pixels p, float keep)
        {
            float x0 = (p.w - keep) / 2f, k = keep / p.w; var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) r.c[y * p.w + x] = p.Sample(x0 + (x + 0.5f) * k, y + 0.5f); }); return r;
        }

        /// <summary>A soft join with a copy moved by half (by whole lines when going across, so they sit on each other).</summary>
        static Pixels Join(Pixels p, float blend, bool sideways, float period)
        {
            int n = sideways ? p.w : p.h; float shift = n / 2f; if (period > 0) shift = Mathf.Max(1, Mathf.Round(shift / period)) * period; float bw = Mathf.Max(2, n * blend); var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int i = sideways ? x : y; float t = Mathf.Clamp01(Mathf.Min(i + 0.5f, n - 0.5f - i) / bw); t = t * t * (3 - 2 * t); var a = p.c[y * p.w + x]; var b = sideways ? p.Sample(x + 0.5f + shift, y + 0.5f) : p.Sample(x + 0.5f, y + 0.5f + shift); r.c[y * p.w + x] = Color.Lerp(b, a, t); } });
            return r;
        }
    }
}
