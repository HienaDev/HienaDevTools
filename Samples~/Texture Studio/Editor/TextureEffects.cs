using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Shared bits for the effect steps: sizes in real pixels, tiled noise and cells, drawing lines that wrap.</summary>
    public static class FxOps
    {
        /// <summary>A size in the saved texture's pixels, as pixels of the picture being worked on (previews are smaller).</summary>
        public static float Px(float realPixels, TextureRun run) => Mathf.Max(0.5f, realPixels * (run.scale > 0 ? run.scale : 1));
        public static float Lum(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        public static uint Hash(int x, int y, int seed) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504); h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); } }
        public static float Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777215f;

        /// <summary>Distances to the nearest and second-nearest of a wrapped grid of random points (n cells across), and which cell is nearest.</summary>
        public static (float d1, float d2, int cell) Cells(float u, float v, int n, int seed, float jitter = 1f)
        {
            float x = u * n, y = v * n; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float d1 = 9, d2 = 9; int best = 0;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j, wx = ((gx % n) + n) % n, wy = ((gy % n) + n) % n;
                    float px = gx + 0.5f + (Rand(wx, wy, seed) - 0.5f) * jitter, py = gy + 0.5f + (Rand(wx, wy, seed + 7) - 0.5f) * jitter; float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < d1) { d2 = d1; d1 = d; best = wy * n + wx; } else if (d < d2) d2 = d;
                }
            return (d1, d2, best);
        }
        /// <summary>A stroke of a given width from a to b (in pixels), added into a mask that wraps at the edges.</summary>
        public static void Stroke(float[] m, int w, int h, Vector2 a, Vector2 b, float width, float strength = 1f)
        {
            float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, width * 0.5f))); float r = Mathf.Max(0.5f, width / 2); int ri = Mathf.CeilToInt(r + 1);
            for (int k = 0; k <= n; k++)
            {
                var c = Vector2.Lerp(a, b, k / (float)n);
                for (int dy = -ri; dy <= ri; dy++) for (int dx = -ri; dx <= ri; dx++)
                    {
                        int x = Mathf.FloorToInt(c.x) + dx, y = Mathf.FloorToInt(c.y) + dy; float d = new Vector2(x + 0.5f - c.x, y + 0.5f - c.y).magnitude; float v = Mathf.Clamp01(r - d + 0.5f) * strength; if (v <= 0) continue;
                        x %= w; if (x < 0) x += w; y %= h; if (y < 0) y += h; int q = y * w + x; if (m[q] < v) m[q] = v;
                    }
            }
        }
        public static Color Over(Color a, Color b, float t) { var c = Color.Lerp(a, b, Mathf.Clamp01(t)); c.a = a.a; return c; }
        /// <summary>Normal of a height field (tiled), z up.</summary>
        public static Vector3 Normal(float[] h, int w, int hh, int x, int y, float k)
        {
            float H(int i, int j) { i %= w; if (i < 0) i += w; j %= hh; if (j < 0) j += hh; return h[j * w + i]; }
            return new Vector3((H(x - 1, y) - H(x + 1, y)) * k, (H(x, y - 1) - H(x, y + 1)) * k, 1).normalized;
        }
        public static Color Hex(string s) { ColorUtility.TryParseHtmlString("#" + s, out var c); return c; }
        /// <summary>A colour from stops (positions 0..1 in order).</summary>
        public static Color Ramp(Color[] cols, float[] at, float t)
        {
            if (cols == null || cols.Length == 0) return Color.white; if (cols.Length == 1 || t <= at[0]) return cols[0]; for (int i = 1; i < cols.Length; i++) if (t <= at[i]) return Color.Lerp(cols[i - 1], cols[i], Mathf.InverseLerp(at[i - 1], at[i], t)); return cols[cols.Length - 1];
        }
        public static bool Colour(ref Color c, string label, string tip = "") { var n = EditorGUILayout.ColorField(new GUIContent(label, tip), c); if (n == c) return false; c = n; return true; }
    }

    // ==================================================================== retro and stylised

    [Serializable]
    public class DitherStep : TextureStep
    {
        public int method; public int palette; public int levels = 4; public float strength = 1, size = 1; public bool keepColourCount; public string[] mine; public string mineName = "";
        static readonly string[] Methods = { "Bayer 2", "Bayer 4", "Bayer 8", "Blue noise", "Floyd-Steinberg", "Atkinson" };
        public static readonly (string name, string[] hex)[] Palettes =
        {
            ("Colours per channel", null), ("1-bit", new[] { "000000", "ffffff" }), ("Game Boy", new[] { "0f380f", "306230", "8bac0f", "9bbc0f" }), ("CGA", new[] { "000000", "55ffff", "ff55ff", "ffffff" }),
            ("PICO-8", new[] { "000000", "1d2b53", "7e2553", "008751", "ab5236", "5f574f", "c2c3c7", "fff1e8", "ff004d", "ffa300", "ffec27", "00e436", "29adff", "83769c", "ff77a8", "ffccaa" }),
            ("Grey 4", new[] { "000000", "555555", "aaaaaa", "ffffff" }), ("Sepia", new[] { "1e1408", "5a3c1e", "a07850", "e6d2aa" }),
        };
        public override string Title => "Dithering";
        public override string Blurb => "Few colours with dotted patterns";
        public override string Explain => "Cuts the picture down to a few colours and fakes the ones in between with a pattern of dots: ordered (Bayer), blue noise, or spread errors (Floyd-Steinberg, Atkinson for an old Mac look). Pick a palette or how many shades of each colour.";
        public override string Summary => Methods[Mathf.Clamp(method, 0, Methods.Length - 1)] + ", " + (palette == 0 ? levels + " per channel" : palette >= Palettes.Length ? mineName : Palettes[palette].name);
        string[] Hex => palette >= Palettes.Length ? (mine != null && mine.Length > 1 ? mine : Palettes[1].hex) : Palettes[palette].hex;
        public override Color Tint => new Color(0.75f, 0.6f, 0.95f);
        public override bool HardPixels => true;
        public override bool Settings()
        {
            bool ch = false; EditorGUILayout.LabelField("How", EditorStyles.miniBoldLabel); int m = method; m = GUILayout.SelectionGrid(m, Methods, 3); if (m != method) { method = m; ch = true; }
            var names = Palettes.Select(q => q.name).ToList(); if (mine != null && mine.Length > 1) names.Add("From the library: " + mineName);
            int pl = EditorGUILayout.Popup("Palette", Mathf.Min(palette, names.Count - 1), names.ToArray()); if (pl != palette) { palette = pl; ch = true; }
            PaletteLibrary.Buttons((l, n) => { mine = PaletteLibrary.AtMost(l, 32).Select(c => ColorUtility.ToHtmlStringRGB(c)).ToArray(); mineName = n; palette = Palettes.Length; }, Hex == null ? new List<Color>() : Hex.Select(FxOps.Hex).ToList(), palette >= Palettes.Length ? mineName : palette > 0 ? Palettes[palette].name : "");
            if (palette == 0) ch |= IntSlider(ref levels, "Shades", "How many shades of red, green and blue", 2, 16);
            ch |= Slider(ref strength, "Pattern", "How strong the dotted pattern is (0: plain bands)", 0, 2);
            ch |= Slider(ref size, "Dot size", "How big each dot is, in pixels", 1, 8);
            return ch;
        }
        static readonly int[] B2 = { 0, 2, 3, 1 }, B4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        static float Bayer(int x, int y, int n)
        {
            if (n == 2) return (B2[(y & 1) * 2 + (x & 1)] + 0.5f) / 4f; if (n == 4) return (B4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
            int v = 0; int xx = x & 7, yy = y & 7; for (int bit = 0; bit < 3; bit++) { int a = (xx >> (2 - bit)) & 1, b = (yy >> (2 - bit)) & 1; v = (v << 2) | ((a ^ b) << 1) | b; } return (v + 0.5f) / 64f;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            float cell = FxOps.Px(size, run); int cw = Mathf.Max(1, Mathf.RoundToInt(p.w / cell)), chh = Mathf.Max(1, Mathf.RoundToInt(p.h / cell));
            var src = cw < p.w ? p.Resized(cw, chh) : p.Copy(); var pal = palette > 0 ? Hex.Select(FxOps.Hex).ToArray() : null; int lv = Mathf.Max(2, levels);
            bool mono = pal != null && pal.Length > 1 && Enumerable.Range(1, pal.Length - 1).All(i => FxOps.Lum(pal[i]) >= FxOps.Lum(pal[i - 1]) - 1e-3f) && palette != 4 && palette != 3;
            if (mono) { float lo = 1, hi = 0; foreach (var c in src.c) { float l = FxOps.Lum(c); lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); } float k = hi > lo + 0.02f ? 1 / (hi - lo) : 1; for (int i = 0; i < src.c.Length; i++) { float l = (FxOps.Lum(src.c[i]) - lo) * k; src.c[i] = new Color(l, l, l, src.c[i].a); } pal = pal.Select(q => q).ToArray(); }
            var palL = mono ? pal.Select(FxOps.Lum).ToArray() : null; float pl0 = mono ? palL[0] : 0, pl1 = mono ? palL[palL.Length - 1] : 1;
            Color Near(Color c) { if (mono) { float l = pl0 + Mathf.Clamp01(c.r) * (pl1 - pl0); int bi = 0; float bd2 = 9; for (int i = 0; i < pal.Length; i++) { float d = Mathf.Abs(palL[i] - l); if (d < bd2) { bd2 = d; bi = i; } } var r2 = pal[bi]; r2.a = c.a; return r2; } if (pal == null) { float k = lv - 1; return new Color(Mathf.Round(Mathf.Clamp01(c.r) * k) / k, Mathf.Round(Mathf.Clamp01(c.g) * k) / k, Mathf.Round(Mathf.Clamp01(c.b) * k) / k, c.a); } Color best = pal[0]; float bd = 99; foreach (var q in pal) { float d = (q.r - c.r) * (q.r - c.r) * 0.3f + (q.g - c.g) * (q.g - c.g) * 0.59f + (q.b - c.b) * (q.b - c.b) * 0.11f; if (d < bd) { bd = d; best = q; } } best.a = c.a; return best; }
            float spread = pal == null ? 1f / (lv - 1) : 1f / Mathf.Max(1, pal.Length - 1);
            var o = new Pixels(src.w, src.h, src.srgb);
            if (method >= 4)
            {
                // errors pushed on to the pixels not done yet
                var e = src.c.Select(c => c).ToArray(); bool atk = method == 5;
                for (int y = src.h - 1; y >= 0; y--) for (int x = 0; x < src.w; x++)
                    {
                        int q = y * src.w + x; var old = e[q]; var nw = Near(old); o.c[q] = nw; var err = (old - (mono ? new Color((FxOps.Lum(nw) - pl0) / Mathf.Max(0.01f, pl1 - pl0), 0, 0, 0) : nw)) * strength; err.a = 0; if (mono) err = new Color(err.r, err.r, err.r, 0);
                        void Add(int dx, int dy, float k) { int xx = x + dx, yy = y - dy; if (xx < 0 || xx >= src.w || yy < 0) return; e[yy * src.w + xx] += err * k; }
                        if (atk) { Add(1, 0, 1 / 8f); Add(2, 0, 1 / 8f); Add(-1, 1, 1 / 8f); Add(0, 1, 1 / 8f); Add(1, 1, 1 / 8f); Add(0, 2, 1 / 8f); } else { Add(1, 0, 7 / 16f); Add(-1, 1, 3 / 16f); Add(0, 1, 5 / 16f); Add(1, 1, 1 / 16f); }
                    }
            }
            else
            {
                int n = method == 0 ? 2 : method == 1 ? 4 : 8;
                TextureOps.Rows(src.h, y => { for (int x = 0; x < src.w; x++) { int q = y * src.w + x; float t = method == 3 ? Mathf.Repeat(x * 0.7548777f + y * 0.5698403f + FxOps.Rand(x, y, 3) * 0.35f, 1f) : Bayer(x, y, n); var c = src.c[q]; float d = (t - 0.5f) * spread * strength; o.c[q] = Near(new Color(c.r + d, c.g + d, c.b + d, c.a)); } });
            }
            if (o.w == p.w) return o; var r = new Pixels(p.w, p.h, p.srgb); for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) r.c[y * p.w + x] = o.c[Mathf.Min(o.h - 1, y * o.h / p.h) * o.w + Mathf.Min(o.w - 1, x * o.w / p.w)]; return r;
        }
    }

    [Serializable]
    public class HalftoneStep : TextureStep
    {
        public int shape, mode; public float cell = 8, angle = 45, soft = 0.3f; public Color ink = new Color(0.08f, 0.08f, 0.12f), paper = new Color(0.96f, 0.94f, 0.88f);
        public override string Title => "Halftone";
        public override string Blurb => "Comic-print dots or lines";
        public override string Explain => "Prints the picture as dots, lines or crosses that grow where it's dark, like a newspaper or comic. One ink on paper, the four printing inks (CMYK), or dots in the picture's own colours.";
        public override string Summary => new[] { "dots", "lines", "crosses" }[shape] + ", " + new[] { "one ink", "CMYK", "its colours" }[mode];
        public override Color Tint => new Color(0.95f, 0.75f, 0.45f);
        public override bool Settings()
        {
            bool ch = Choice(ref shape, "Dots", "Lines", "Crosses"); ch |= Choice(ref mode, "One ink", "CMYK", "Its colours");
            ch |= Slider(ref cell, "Size", "How far apart the dots are, in pixels", 2, 40); ch |= Slider(ref angle, "Angle", "Which way the rows run", 0, 90); ch |= Slider(ref soft, "Soft edges", "", 0, 1);
            if (mode == 0) { ch |= FxOps.Colour(ref ink, "Ink"); ch |= FxOps.Colour(ref paper, "Paper"); }
            return ch;
        }
        float Cover(float x, float y, float dark, float ang, float s)
        {
            float a = ang * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a); float u = (x * ca + y * sa) / s, v = (-x * sa + y * ca) / s; float fu = u - Mathf.Floor(u) - 0.5f, fv = v - Mathf.Floor(v) - 0.5f; float d, edge;
            if (shape == 0) { d = Mathf.Sqrt(fu * fu + fv * fv); edge = Mathf.Sqrt(Mathf.Clamp01(dark) / Mathf.PI); }
            else if (shape == 1) { d = Mathf.Abs(fv); edge = Mathf.Clamp01(dark) * 0.5f; }
            else { d = Mathf.Min(Mathf.Abs(fu), Mathf.Abs(fv)); edge = (1 - Mathf.Sqrt(1 - Mathf.Clamp01(dark))) * 0.5f; }
            float w = Mathf.Max(0.02f, soft * 0.25f) + 0.5f / s; return Mathf.Clamp01((edge - d) / w + 0.5f);
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            float s = FxOps.Px(cell, run); var o = new Pixels(p.w, p.h, p.srgb); float lo = 1, hi = 0; foreach (var c0 in p.c) { float l = FxOps.Lum(c0); lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); } float span = Mathf.Max(0.05f, hi - lo); float Dark(Color c0) => 1 - Mathf.Clamp01((FxOps.Lum(c0) - lo) / span);
            var avg = mode == 2 ? TextureOpsMore.Blur(p, cell * 0.6f * 1024f / Mathf.Max(p.w, p.h) * (run.scale > 0 ? run.scale : 1)) : null;
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; var c = p.c[q];
                    if (mode == 1) { float k = 1 - Mathf.Max(c.r, Mathf.Max(c.g, c.b)), cc = (1 - c.r - k) / Mathf.Max(1e-3f, 1 - k), mm = (1 - c.g - k) / Mathf.Max(1e-3f, 1 - k), yy = (1 - c.b - k) / Mathf.Max(1e-3f, 1 - k); float C = Cover(x, y, cc, angle + 15, s), Mm = Cover(x, y, mm, angle + 75, s), Y = Cover(x, y, yy, angle, s), K = Cover(x, y, k, angle + 45, s); o.c[q] = new Color((1 - C) * (1 - K), (1 - Mm) * (1 - K), (1 - Y) * (1 - K), c.a); }
                    else if (mode == 2) { var a = avg.c[q]; float t = Cover(x, y, Dark(a) * 0.85f + 0.08f, angle, s); var col = Color.Lerp(paper, a * 1.1f, t); col.a = c.a; o.c[q] = col; }
                    else { float t = Cover(x, y, Dark(c) * 0.9f + 0.05f, angle, s); var col = Color.Lerp(paper, ink, t); col.a = c.a; o.c[q] = col; }
                }
            });
            return o;
        }
    }

    [Serializable]
    public class ScanlinesStep : TextureStep
    {
        public float every = 3, dark = 0.45f, mask = 0.35f, glow = 0.3f; public int maskKind = 1;
        public override string Title => "Scanlines (CRT)";
        public override string Blurb => "Old TV lines and phosphor dots";
        public override string Explain => "Makes it look like an old TV or arcade screen: dark lines across, the red-green-blue stripes of the screen, and a soft glow round bright parts.";
        public override string Summary => "a line every " + every.ToString("0.#") + " px";
        public override Color Tint => new Color(0.45f, 0.85f, 0.65f);
        public override bool Settings()
        {
            bool ch = Slider(ref every, "Line every", "Pixels from one line to the next", 2, 16); ch |= Slider(ref dark, "Line darkness", "", 0, 1);
            EditorGUILayout.LabelField("Screen dots", EditorStyles.miniBoldLabel); ch |= Choice(ref maskKind, "None", "Stripes", "Slots", "Dots"); ch |= Slider(ref mask, "How strong", "", 0, 1); ch |= Slider(ref glow, "Glow", "Bright parts bleed light round them", 0, 1);
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            float e = FxOps.Px(every, run); var bl = glow > 0 ? TextureOpsMore.Blur(p, 6f) : null; var o = new Pixels(p.w, p.h, p.srgb); float px = FxOps.Px(1, run);
            TextureOps.Rows(p.h, y =>
            {
                float line = 1 - dark * (0.5f + 0.5f * Mathf.Cos((y + 0.5f) / e * Mathf.PI * 2));
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; var c = p.c[q]; if (bl != null) { var b = bl.c[q]; c += new Color(b.r * b.r, b.g * b.g, b.b * b.b, 0) * glow * 0.6f; }
                    var m = Color.white; if (maskKind > 0) { int col = Mathf.FloorToInt(x / px) % 3, row = Mathf.FloorToInt(y / (px * 3)); if (maskKind == 2 && (row & 1) == 1) col = (Mathf.FloorToInt(x / px) + 1) % 3; var t = col == 0 ? new Color(1, 0.3f, 0.3f) : col == 1 ? new Color(0.3f, 1, 0.3f) : new Color(0.3f, 0.3f, 1); if (maskKind == 3 && Mathf.FloorToInt(y / px) % 3 == 2) t *= 0.4f; m = Color.Lerp(Color.white, t * 1.6f, mask); }
                    o.c[q] = new Color(c.r * m.r * line, c.g * m.g * line, c.b * m.b * line, p.c[q].a);
                }
            });
            return o;
        }
    }

    [Serializable]
    public class VhsStep : TextureStep
    {
        public float shift = 3, wobble = 0.3f, noise = 0.15f, bleed = 0.5f, lines = 0.3f; public int seed = 1;
        public override string Title => "VHS and colour fringes";
        public override string Blurb => "Tape wobble, colour fringes, noise";
        public override string Explain => "An old video tape: red and blue slide apart (chromatic aberration), lines wobble sideways, colour smears to the right, and there's grain and tracking lines.";
        public override string Summary => "fringe " + shift.ToString("0.#") + " px";
        public override Color Tint => new Color(0.95f, 0.45f, 0.7f);
        public override bool Settings()
        {
            bool ch = Slider(ref shift, "Colour fringes", "How far red and blue slide apart, in pixels", 0, 20); ch |= Slider(ref wobble, "Wobble", "Lines slide sideways", 0, 1); ch |= Slider(ref bleed, "Colour smear", "Colour runs to the right like on tape", 0, 1);
            ch |= Slider(ref noise, "Grain", "", 0, 1); ch |= Slider(ref lines, "Tracking lines", "Bright bands across", 0, 1); ch |= IntSlider(ref seed, "Seed", "Another random wobble", 1, 99); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            float sh = FxOps.Px(shift, run), sm = FxOps.Px(bleed * 12, run); var noiseRow = new TextureOps.TileNoise(seed * 13 + 5, 6); var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                float v = (y + 0.5f) / p.h, off = (noiseRow.Fbm(0.13f, v * 3) - 0.5f) * wobble * 0.04f * p.w; float band = Mathf.Clamp01((noiseRow.Fbm(0.71f, v) - 0.72f) * 8) * lines;
                for (int x = 0; x < p.w; x++)
                {
                    float xx = x + 0.5f + off; var cr = p.Sample(xx + sh, y + 0.5f); var cg = p.Sample(xx, y + 0.5f); var cb = p.Sample(xx - sh, y + 0.5f); var c = new Color(cr.r, cg.g, cb.b, p.c[y * p.w + x].a);
                    if (sm > 0.5f) { var s = Color.black; for (int k = 0; k < 4; k++) s += p.Sample(xx - sm * k / 4f, y + 0.5f); s /= 4; float l = FxOps.Lum(c); var chroma = s - new Color(FxOps.Lum(s), FxOps.Lum(s), FxOps.Lum(s), 0); c = Color.Lerp(c, new Color(l, l, l, c.a) + chroma, bleed * 0.8f); c.a = p.c[y * p.w + x].a; }
                    float g = (FxOps.Rand(x, y, seed) - 0.5f) * noise * 0.5f; o.c[y * p.w + x] = new Color(c.r + g + band, c.g + g + band, c.b + g + band, c.a);
                }
            });
            return o;
        }
    }

    [Serializable]
    public class InkStep : TextureStep
    {
        public float width = 2, amount = 0.6f; public int bands; public Color ink = new Color(0.08f, 0.06f, 0.06f); public bool fromHeight = true;
        public override string Title => "Ink outlines";
        public override string Blurb => "Toon lines round shapes";
        public override string Explain => "Draws dark lines where the colour or the bumps change, like a comic or a cartoon. Can also cut the shading into a few flat bands (cel shading).";
        public override string Summary => width.ToString("0.#") + " px lines" + (bands > 0 ? ", " + bands + " bands" : "");
        public override Color Tint => new Color(0.55f, 0.55f, 0.6f);
        public override bool Settings()
        {
            bool ch = Slider(ref width, "Line width", "Pixels", 0.5f, 8); ch |= Slider(ref amount, "How many lines", "Lower: only the strongest edges", 0, 1); ch |= FxOps.Colour(ref ink, "Ink");
            ch |= Toggle(ref fromHeight, "Lines where the bumps change too", "Not only where the colour changes"); ch |= IntSlider(ref bands, "Flat bands", "0 keeps the shading; 2 to 6 cuts it into flat cel bands", 0, 6); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var sm = TextureOpsMore.Blur(p, 1.5f); var h = fromHeight ? TextureOpsMore.Height(p, 0.6f) : null; var edge = new float[p.c.Length]; float thr = Mathf.Lerp(0.5f, 0.04f, amount);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { var a = sm[x - 1, y]; var b = sm[x + 1, y]; var c = sm[x, y - 1]; var d = sm[x, y + 1]; float g = (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(c.r - d.r) + Mathf.Abs(c.g - d.g) + Mathf.Abs(c.b - d.b)) / 3; if (h != null) { float H(int i, int j) { i = ((i % p.w) + p.w) % p.w; j = ((j % p.h) + p.h) % p.h; return h[j * p.w + i]; } g = Mathf.Max(g, (Mathf.Abs(H(x - 1, y) - H(x + 1, y)) + Mathf.Abs(H(x, y - 1) - H(x, y + 1))) * 1.5f); } edge[y * p.w + x] = g > thr ? 1 : 0; } });
            var line = new float[edge.Length]; int r = Mathf.Max(0, Mathf.RoundToInt(FxOps.Px(width, run) / 2 - 0.5f));
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { float v = 0; for (int dy = -r; dy <= r && v < 1; dy++) for (int dx = -r; dx <= r; dx++) { if (dx * dx + dy * dy > r * r + r) continue; int i = ((x + dx) % p.w + p.w) % p.w, j = ((y + dy) % p.h + p.h) % p.h; if (edge[j * p.w + i] > 0) { v = 1; break; } } line[y * p.w + x] = v; } });
            var o = new Pixels(p.w, p.h, p.srgb);
            for (int i = 0; i < p.c.Length; i++) { var c = p.c[i]; if (bands > 0) { float l = FxOps.Lum(c), lq = (Mathf.Floor(l * bands) + 0.5f) / bands; c = c * (lq / Mathf.Max(0.02f, l)); c.a = p.c[i].a; } o.c[i] = FxOps.Over(c, ink, line[i]); }
            return o;
        }
    }

    [Serializable]
    public class PaintStep : TextureStep
    {
        public int style; public float size = 4, amount = 1; public Color pencil = new Color(0.15f, 0.15f, 0.2f), paperCol = new Color(0.97f, 0.95f, 0.9f);
        public override string Title => "Painterly";
        public override string Blurb => "Oil paint, watercolour, pencil";
        public override string Explain => "Makes it look hand-made: oil paint (flat strokes that keep the edges sharp), watercolour (soft, bleeding colours with dark rims and paper grain) or pencil hatching (lines that get denser where it's dark).";
        public override string Summary => new[] { "oil paint", "watercolour", "pencil" }[style];
        public override Color Tint => new Color(0.85f, 0.6f, 0.5f);
        public override bool Settings()
        {
            bool ch = Choice(ref style, "Oil paint", "Watercolour", "Pencil"); ch |= Slider(ref size, "Brush size", "Pixels", 1, 12); ch |= Slider(ref amount, "Amount", "", 0, 1);
            if (style == 2) { ch |= FxOps.Colour(ref pencil, "Pencil"); ch |= FxOps.Colour(ref paperCol, "Paper"); }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(FxOps.Px(size, run)), 1, 12); var o = new Pixels(p.w, p.h, p.srgb);
            if (style == 0)
            {
                // Kuwahara: each pixel takes the calmest of its four corners
                TextureOps.Rows(p.h, y =>
                {
                    for (int x = 0; x < p.w; x++)
                    {
                        Color best = p.c[y * p.w + x]; float bv = float.MaxValue;
                        for (int k = 0; k < 4; k++)
                        {
                            int sx = k % 2 == 0 ? -r : 0, sy = k < 2 ? -r : 0; Color s = Color.black; float ss = 0; int n = 0;
                            for (int j = 0; j <= r; j++) for (int i = 0; i <= r; i++) { var c = p[x + sx + i, y + sy + j]; s += c; float l = FxOps.Lum(c); ss += l * l; n++; }
                            s /= n; float m = FxOps.Lum(s), v = ss / n - m * m; if (v < bv) { bv = v; best = s; }
                        }
                        best.a = p.c[y * p.w + x].a; o.c[y * p.w + x] = Color.Lerp(p.c[y * p.w + x], best, amount);
                    }
                });
                return o;
            }
            if (style == 1)
            {
                var soft = TextureOpsMore.Blur(p, size * 1.5f); var grain = new TextureOps.TileNoise(77, 48); var flow = new TextureOps.TileNoise(19, 5);
                TextureOps.Rows(p.h, y =>
                {
                    for (int x = 0; x < p.w; x++)
                    {
                        int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; var c = soft.Sample(x + 0.5f + (flow.Fbm(u, v) - 0.5f) * r * 3, y + 0.5f + (flow.Fbm(v, u) - 0.5f) * r * 3);
                        float rim = Mathf.Clamp01((FxOps.Lum(soft.c[q]) - FxOps.Lum(c)) * 6); float g = grain.Fbm(u, v); var w = Color.Lerp(c, c * 0.75f, rim) * (0.92f + g * 0.12f); w = Color.Lerp(w, Color.white, 0.08f); w.a = p.c[q].a; o.c[q] = Color.Lerp(p.c[q], w, amount);
                    }
                });
                return o;
            }
            float sp = FxOps.Px(size * 1.5f, run);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; float d = 1 - FxOps.Lum(p.c[q]); float ink = 0; var angs = new[] { 45f, -45f, 0f, 90f };
                    for (int k = 0; k < 4; k++) { if (d < 0.2f + k * 0.2f) break; float a = angs[k] * Mathf.Deg2Rad; float t = (x * Mathf.Cos(a) + y * Mathf.Sin(a)) / sp; float f = Mathf.Abs(t - Mathf.Round(t)); ink = Mathf.Max(ink, Mathf.Clamp01((0.18f - f) * 6) * (0.7f + FxOps.Rand(x / 3, y / 3, k) * 0.3f)); }
                    var c = Color.Lerp(paperCol, pencil, ink); c.a = p.c[q].a; o.c[q] = Color.Lerp(p.c[q], c, amount);
                }
            });
            return o;
        }
    }

    [Serializable]
    public class MosaicStep : TextureStep
    {
        public int shape; public float size = 24, gap = 0.12f, bevel = 0.4f; public Color lead = new Color(0.12f, 0.11f, 0.1f); public int seed = 1;
        public override string Title => "Mosaic";
        public override string Blurb => "Tiles, hexagons, stained glass";
        public override string Explain => "Rebuilds the picture from little flat pieces: square tiles, hexagons, or stained glass (random shapes with dark lead between them). Each piece takes the colour under it.";
        public override string Summary => new[] { "squares", "hexagons", "stained glass" }[shape] + ", " + size.ToString("0") + " px";
        public override Color Tint => new Color(0.5f, 0.7f, 0.95f);
        public override bool Settings()
        {
            bool ch = Choice(ref shape, "Squares", "Hexagons", "Stained glass"); ch |= Slider(ref size, "Piece size", "Pixels", 4, 128); ch |= Slider(ref gap, "Gap", "", 0, 0.4f); ch |= Slider(ref bevel, "Bevel", "Light on the pieces' edges", 0, 1); ch |= FxOps.Colour(ref lead, shape == 2 ? "Lead" : "Grout");
            if (shape == 2) ch |= IntSlider(ref seed, "Seed", "Other shapes", 1, 99); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            float s = FxOps.Px(size, run); int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(p.w, p.h) / s)); var o = new Pixels(p.w, p.h, p.srgb);
            // each piece's colour: the picture averaged over it
            var blur = TextureOpsMore.Blur(p, size * 0.5f * 1024f / Mathf.Max(p.w, p.h) * (run.scale > 0 ? run.scale : 1));
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; float edgeDist; Vector2 centre;
                    if (shape == 0) { float gx = u * n, gy = v * n; centre = new Vector2((Mathf.Floor(gx) + 0.5f) / n, (Mathf.Floor(gy) + 0.5f) / n); edgeDist = Mathf.Min(Mathf.Min(gx - Mathf.Floor(gx), 1 - (gx - Mathf.Floor(gx))), Mathf.Min(gy - Mathf.Floor(gy), 1 - (gy - Mathf.Floor(gy)))); }
                    else if (shape == 1) { float sq = 0.8660254f; int rows = Mathf.Max(1, Mathf.RoundToInt(n / sq)); float gx = u * n, gy = v * rows; int ry = Mathf.FloorToInt(gy); float off = (ry & 1) * 0.5f; int rx = Mathf.FloorToInt(gx - off); Vector2 best = default; float bd = 9, b2 = 9; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { int yy = ry + j; float o2 = (yy & 1) * 0.5f; var c = new Vector2(rx + i + 0.5f + o2, yy + 0.5f); float d = new Vector2((gx - c.x), (gy - c.y) * sq).magnitude; if (d < bd) { b2 = bd; bd = d; best = c; } else if (d < b2) b2 = d; } centre = new Vector2(best.x / n, best.y / rows); edgeDist = (b2 - bd) * 0.6f; }
                    else { var c = FxOps.Cells(u, v, n, seed); edgeDist = (c.d2 - c.d1) * 0.5f; int cx = c.cell % n, cy = c.cell / n; centre = new Vector2((cx + 0.5f) / n, (cy + 0.5f) / n); }
                    var col = blur.Sample(centre.x * p.w, centre.y * p.h); if (shape == 2) col = Color.Lerp(col, col * 1.3f, 0.3f);
                    float bev = Mathf.Clamp01(edgeDist * 6) ; col *= Mathf.Lerp(1, 0.75f + 0.25f * bev, bevel); col.a = p.c[y * p.w + x].a;
                    float g = Mathf.Clamp01((gap * 0.5f - edgeDist) * n * 2 + 0.5f); if (gap <= 0) g = 0; o.c[y * p.w + x] = FxOps.Over(col, lead, g);
                }
            });
            return o;
        }
    }

    // ==================================================================== colour

    [Serializable]
    public class GradientMapStep : TextureStep
    {
        public Color[] cols = { FxOps.Hex("1a0a2e"), FxOps.Hex("b8336a"), FxOps.Hex("ffd166") }; public float[] at = { 0, 0.5f, 1 }; public float mix = 1;
        public static readonly (string name, string[] c)[] Ready = { ("Sunset", new[] { "1a0a2e", "b8336a", "ffd166" }), ("Fire", new[] { "000000", "7a0d00", "ff6a00", "ffe36e", "ffffff" }), ("Ice", new[] { "02101f", "1d5c8a", "8fd3ff", "ffffff" }), ("Sepia", new[] { "1e1408", "7a5a35", "f0dcb4" }), ("Toxic", new[] { "06140a", "2e7d1a", "b6ff2e" }), ("Night vision", new[] { "000a00", "0f6b1e", "9bff9b" }), ("Gold", new[] { "2b1a05", "8a5d14", "f2c14e", "fff4c2" }), ("Rainbow", new[] { "ff0040", "ffaa00", "40ff40", "00aaff", "aa40ff" }) };
        public override string Title => "Gradient map";
        public override string Blurb => "Recolour by brightness";
        public override string Explain => "Gives every brightness its own colour from a gradient: darks get the first colour, brights the last. Great for stylised textures, fire, ice, toxic slime.";
        public override string Summary => cols.Length + " colours";
        public override Color Tint => new Color(0.95f, 0.5f, 0.45f);
        public override bool Settings()
        {
            bool ch = false; GUILayout.BeginHorizontal(); for (int i = 0; i < Ready.Length; i++) { if (i == 4) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } if (GUILayout.Button(Ready[i].name, EditorStyles.miniButton)) { cols = Ready[i].c.Select(FxOps.Hex).ToArray(); at = Enumerable.Range(0, cols.Length).Select(k => k / (float)(cols.Length - 1)).ToArray(); ch = true; } } GUILayout.EndHorizontal();
            PaletteLibrary.Buttons((l, n) => { var c = PaletteLibrary.AtMost(PaletteLibrary.DarkToBright(l), 6); cols = c.ToArray(); at = Enumerable.Range(0, cols.Length).Select(k => k / (float)Mathf.Max(1, cols.Length - 1)).ToArray(); }, cols.ToList());
            var r = GUILayoutUtility.GetRect(10, 16, GUILayout.ExpandWidth(true)); if (Event.current.type == EventType.Repaint) for (int x = 0; x < (int)r.width; x++) EditorGUI.DrawRect(new Rect(r.x + x, r.y, 1, r.height), FxOps.Ramp(cols, at, x / r.width));
            for (int i = 0; i < cols.Length; i++) { GUILayout.BeginHorizontal(); var c = EditorGUILayout.ColorField(GUIContent.none, cols[i], GUILayout.Width(60)); if (c != cols[i]) { cols[i] = c; ch = true; } float a = EditorGUILayout.Slider(at[i], 0, 1); if (!Mathf.Approximately(a, at[i])) { at[i] = Mathf.Clamp(a, i > 0 ? at[i - 1] : 0, i < at.Length - 1 ? at[i + 1] : 1); ch = true; } if (cols.Length > 2 && GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(20))) { var l = cols.ToList(); l.RemoveAt(i); cols = l.ToArray(); var la = at.ToList(); la.RemoveAt(i); at = la.ToArray(); ch = true; GUILayout.EndHorizontal(); break; } GUILayout.EndHorizontal(); }
            if (cols.Length < 6 && GUILayout.Button("+ A colour", EditorStyles.miniButton)) { var l = cols.ToList(); l.Add(Color.white); cols = l.ToArray(); var la = at.ToList(); la.Add(1); at = la.ToArray(); ch = true; }
            ch |= Slider(ref mix, "Amount", "", 0, 1); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) { var o = new Pixels(p.w, p.h, p.srgb); TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; var c = FxOps.Ramp(cols, at, Mathf.Clamp01(FxOps.Lum(p.c[q]))); c.a = p.c[q].a; o.c[q] = Color.Lerp(p.c[q], c, mix); } }); return o; }
    }

    [Serializable]
    public class LevelsStep : TextureStep
    {
        public float black, white = 1, gamma = 1, warmth, tint, vignette, vignetteSoft = 0.5f;
        public override string Title => "Levels and warmth";
        public override string Blurb => "Black and white points, warmth, vignette";
        public override string Explain => "Sets how dark the darks and how bright the brights are (levels), the middle brightness (gamma), makes it warmer or cooler, greener or pinker, and darkens the corners (vignette).";
        public override string Summary => (warmth != 0 ? (warmth > 0 ? "warmer" : "cooler") + ", " : "") + (vignette > 0 ? "vignette" : "levels");
        public override Color Tint => new Color(0.9f, 0.8f, 0.5f);
        public override bool Settings()
        {
            bool ch = Slider(ref black, "Black point", "Darker than this becomes black", 0, 0.9f); ch |= Slider(ref white, "White point", "Brighter than this becomes white", 0.1f, 1); ch |= Slider(ref gamma, "Middle", "Above 1: brighter middle tones", 0.2f, 3);
            ch |= Slider(ref warmth, "Warmth", "Orange or blue", -1, 1); ch |= Slider(ref tint, "Tint", "Green or pink", -1, 1); ch |= Slider(ref vignette, "Vignette", "Darker corners (it won't loop any more)", 0, 1); if (vignette > 0) ch |= Slider(ref vignetteSoft, "Softness", "", 0, 1); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var o = new Pixels(p.w, p.h, p.srgb); float span = Mathf.Max(0.01f, white - black); var wc = new Color(1 + warmth * 0.12f - tint * 0.03f, 1 + tint * 0.08f, 1 - warmth * 0.12f - tint * 0.03f);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; var c = p.c[q]; float L(float v) => Mathf.Pow(Mathf.Clamp01((v - black) / span), 1f / gamma); var n = new Color(L(c.r) * wc.r, L(c.g) * wc.g, L(c.b) * wc.b, c.a); if (vignette > 0) { float dx = (x + 0.5f) / p.w - 0.5f, dy = (y + 0.5f) / p.h - 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy) * 1.41f; float k = 1 - vignette * Mathf.SmoothStep(1 - vignetteSoft, 1.1f, d); n.r *= k; n.g *= k; n.b *= k; } o.c[q] = n; } });
            return o;
        }
    }

    [Serializable]
    public class DuotoneStep : TextureStep
    {
        public Color shadows = FxOps.Hex("1b1f4a"), mids = FxOps.Hex("d1495b"), highs = FxOps.Hex("ffe9c7"); public bool three = true; public float mix = 1;
        public override string Title => "Duotone";
        public override string Blurb => "Two or three inks";
        public override string Explain => "Prints the picture with only two (or three) colours: one for the shadows, one for the brights and, if you want, one for the middle.";
        public override string Summary => three ? "three colours" : "two colours";
        public override Color Tint => new Color(0.7f, 0.5f, 0.85f);
        public override bool Settings() { bool ch = Toggle(ref three, "Three colours", "A colour for the middle tones too"); ch |= FxOps.Colour(ref shadows, "Shadows"); if (three) ch |= FxOps.Colour(ref mids, "Middle"); ch |= FxOps.Colour(ref highs, "Brights"); ch |= Slider(ref mix, "Amount", "", 0, 1); return ch; }
        public override Pixels Run(Pixels p, TextureRun run) { var cs = three ? new[] { shadows, mids, highs } : new[] { shadows, highs }; var at = three ? new[] { 0f, 0.5f, 1f } : new[] { 0f, 1f }; var o = new Pixels(p.w, p.h, p.srgb); for (int i = 0; i < p.c.Length; i++) { var c = FxOps.Ramp(cs, at, FxOps.Lum(p.c[i])); c.a = p.c[i].a; o.c[i] = Color.Lerp(p.c[i], c, mix); } return o; }
    }

    // ==================================================================== shape

    [Serializable]
    public class WarpStep : TextureStep
    {
        public int kind; public float strength = 0.4f, size = 0.5f; public int seed = 1;
        public override string Title => "Warp";
        public override string Blurb => "Wavy, swirl, ripples, marble";
        public override string Explain => "Bends the picture: wavy (pushed around by smooth noise, it still loops), a swirl round the middle, ripples like a drop in water, or marble veins (stretched and folded).";
        public override string Summary => new[] { "wavy", "swirl", "ripples", "marble" }[kind];
        public override Color Tint => new Color(0.45f, 0.75f, 0.95f);
        public override bool Settings() { bool ch = Choice(ref kind, "Wavy", "Swirl", "Ripples", "Marble"); ch |= Slider(ref strength, "Strength", "", 0, 1); ch |= Slider(ref size, kind == 1 ? "Reach" : "Size", "", 0.05f, 1); if (kind == 0 || kind == 3) ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var o = new Pixels(p.w, p.h, p.srgb); var nz = new TextureOps.TileNoise(seed * 7 + 1, Mathf.Clamp(Mathf.RoundToInt(2 + (1 - size) * 10), 2, 14)); var nz2 = new TextureOps.TileNoise(seed * 7 + 99, Mathf.Clamp(Mathf.RoundToInt(2 + (1 - size) * 10), 2, 14));
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; float su = u, sv = v;
                    if (kind == 0) { su += (nz.Fbm(u, v) - 0.5f) * strength * 0.3f; sv += (nz2.Fbm(u, v) - 0.5f) * strength * 0.3f; }
                    else if (kind == 3) { float n = nz.Fbm(u, v); float t = Mathf.Sin((u + v * 0.3f + n * strength * 3f) * Mathf.PI * 4) * 0.5f + 0.5f; su = u + (t - 0.5f) * strength * 0.15f + (n - 0.5f) * strength * 0.2f; sv = v + (nz2.Fbm(u, v) - 0.5f) * strength * 0.1f; }
                    else { float dx = u - 0.5f, dy = v - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy); if (kind == 1) { float a = strength * 6 * Mathf.Pow(Mathf.Clamp01(1 - d / Mathf.Max(0.05f, size * 0.75f)), 2); float ca = Mathf.Cos(a), sa = Mathf.Sin(a); su = 0.5f + dx * ca - dy * sa; sv = 0.5f + dx * sa + dy * ca; } else { float k = Mathf.Sin(d / Mathf.Max(0.01f, size * 0.15f) * Mathf.PI * 2) * strength * 0.02f * Mathf.Clamp01(1.2f - d * 1.5f); su = u + (d > 0 ? dx / d : 0) * k; sv = v + (d > 0 ? dy / d : 0) * k; } }
                    o.c[y * p.w + x] = p.Sample(su * p.w, sv * p.h);
                }
            });
            return o;
        }
    }

    [Serializable]
    public class MirrorStep : TextureStep
    {
        public int kind = 3, slices = 6; public float turn;
        public override string Title => "Mirror and kaleidoscope";
        public override string Blurb => "Symmetric patterns";
        public override string Explain => "Mirrors the picture: left onto right, top onto bottom, all four quarters, or a kaleidoscope of slices round the middle.";
        public override string Summary => kind == 3 ? slices + " slices" : new[] { "left-right", "top-bottom", "four ways" }[kind];
        public override Color Tint => new Color(0.75f, 0.45f, 0.9f);
        public override bool Settings() { bool ch = Choice(ref kind, "Left-right", "Top-bottom", "Four ways", "Kaleidoscope"); if (kind == 3) { ch |= IntSlider(ref slices, "Slices", "", 2, 16); ch |= Slider(ref turn, "Turn", "", 0, 360); } return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h;
                    if (kind == 0 || kind == 2) u = u > 0.5f ? 1 - u : u; if (kind == 1 || kind == 2) v = v > 0.5f ? 1 - v : v;
                    if (kind == 3) { float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx) - turn * Mathf.Deg2Rad; float seg = Mathf.PI * 2 / slices; a = Mathf.Repeat(a, seg); if (a > seg / 2) a = seg - a; a += turn * Mathf.Deg2Rad; u = 0.5f + Mathf.Cos(a) * r; v = 0.5f + Mathf.Sin(a) * r; }
                    o.c[y * p.w + x] = p.Sample(u * p.w, v * p.h);
                }
            });
            return o;
        }
    }

    [Serializable]
    public class PolarStep : TextureStep
    {
        public bool toCircle = true; public int repeat = 1; public float inner;
        public override string Title => "Polar (strip to circle)";
        public override string Blurb => "Wrap a strip into a circle";
        public override string Explain => "Wraps the picture round in a circle (the bottom goes to the middle, the top to the rim): rugs, round tiles, plates, portals. Or the other way, a circle into a strip.";
        public override string Summary => toCircle ? "into a circle" : "into a strip";
        public override Color Tint => new Color(0.95f, 0.7f, 0.4f);
        public override bool Settings() { bool ch = Toggle(ref toCircle, "Strip into a circle", "Off: a circle into a strip"); ch |= IntSlider(ref repeat, "Times round", "How many times the picture goes round", 1, 12); if (toCircle) ch |= Slider(ref inner, "Hole in the middle", "", 0, 0.8f); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; float su, sv;
                    if (toCircle) { float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2; su = Mathf.Repeat((Mathf.Atan2(dy, dx) / (Mathf.PI * 2) + 0.5f) * repeat, 1); sv = Mathf.Clamp01((r - inner) / Mathf.Max(0.05f, 1 - inner)); if (r > 1) { o.c[y * p.w + x] = p.Sample(su * p.w, (p.h - 0.5f)); continue; } }
                    else { float a = (u / repeat - 0.5f) * Mathf.PI * 2, r = v * 0.5f; su = 0.5f + Mathf.Cos(a) * r; sv = 0.5f + Mathf.Sin(a) * r; }
                    o.c[y * p.w + x] = p.Sample(su * p.w, Mathf.Clamp(sv * p.h, 0.5f, p.h - 0.5f));
                }
            });
            return o;
        }
    }
}
