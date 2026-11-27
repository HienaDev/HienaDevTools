using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>More Texture Studio changes: move, blur, pixel-art look, dirt and wear, flip/turn, and the extra maps
    /// (height, ambient occlusion, smoothness) used to make a whole material.</summary>
    public static class TextureOpsMore
    {
        // ---------------------------------------------------------------- shift (move the picture, wrapping)

        /// <summary>Moves the picture around (it wraps), e.g. to see the seam in the middle.</summary>
        public static Pixels Shift(Pixels p, float x, float y)
        {
            int dx = Mathf.RoundToInt(x * p.w), dy = Mathf.RoundToInt(y * p.h);
            var r = new Pixels(p.w, p.h, p.srgb);
            for (int j = 0; j < p.h; j++) for (int i = 0; i < p.w; i++) r.c[j * p.w + i] = p[i - dx, j - dy];
            return r;
        }

        // ---------------------------------------------------------------- blur

        /// <summary>Soft blur, wrapping round the edges (so a looping texture keeps looping). Radius in pixels of a 1024 picture.</summary>
        public static Pixels Blur(Pixels p, float radius)
        {
            // (in between two whole sizes it's a mix of both, so a small preview and the full size look the same)
            float real = Mathf.Clamp(radius * Mathf.Max(p.w, p.h) / 1024f, 0, 128); int lo = Mathf.FloorToInt(real); float mix = real - lo;
            if (real <= 0.01f) return p.Copy();
            var a = lo > 0 ? Box(p, lo) : p.c; if (mix < 0.02f) return new Pixels(p.w, p.h, p.srgb) { c = lo > 0 ? a : (Color[])a.Clone() };
            var b = Box(p, lo + 1); var o = new Color[a.Length]; for (int i = 0; i < o.Length; i++) o[i] = Color.LerpUnclamped(a[i], b[i], mix);
            return new Pixels(p.w, p.h, p.srgb) { c = o };
        }

        // twice both ways: looks round, not square
        static Color[] Box(Pixels p, int rad)
        {
            var a = BoxPass(p.c, p.w, p.h, rad, true); a = BoxPass(a, p.w, p.h, rad, false);
            a = BoxPass(a, p.w, p.h, rad, true); return BoxPass(a, p.w, p.h, rad, false);
        }

        static Color[] BoxPass(Color[] src, int w, int h, int rad, bool alongX)
        {
            var o = new Color[src.Length]; float k = 1f / (rad * 2 + 1);
            if (alongX)
                TextureOps.Rows(h, y =>
                {
                    int row = y * w; Color sum = Color.clear;
                    for (int t = -rad; t <= rad; t++) sum += src[row + Mod(t, w)];
                    for (int x = 0; x < w; x++)
                    {
                        o[row + x] = sum * k;
                        sum += src[row + Mod(x + rad + 1, w)] - src[row + Mod(x - rad, w)];
                    }
                });
            else
            {
                // (columns a block at a time: cores writing neighbouring pixels slow each other down)
                const int block = 32;
                TextureOps.Rows((w + block - 1) / block, b =>
                {
                    int x0 = b * block, x1 = Mathf.Min(w, x0 + block);
                    var sum = new Color[x1 - x0];
                    for (int t = -rad; t <= rad; t++) { int row = Mod(t, h) * w; for (int x = x0; x < x1; x++) sum[x - x0] += src[row + x]; }
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * w, add = Mod(y + rad + 1, h) * w, sub = Mod(y - rad, h) * w;
                        for (int x = x0; x < x1; x++) { o[row + x] = sum[x - x0] * k; sum[x - x0] += src[add + x] - src[sub + x]; }
                    }
                });
            }
            return o;
        }
        static int Mod(int v, int n) { v %= n; return v < 0 ? v + n : v; }

        // ---------------------------------------------------------------- pixel-art look

        /// <summary>Big square pixels and a limited set of colours (picked from the picture itself).</summary>
        public static Pixels PixelArt(Pixels p, int pixelsAcross, int colours, bool keepSize)
        {
            pixelsAcross = Mathf.Clamp(pixelsAcross, 4, 1024);
            float k = pixelsAcross / (float)p.w;
            int sw = Mathf.Max(1, pixelsAcross), sh = Mathf.Max(1, Mathf.RoundToInt(p.h * k));
            var small = p.Resized(sw, sh);
            if (colours > 0) small = Palette(small, colours);
            if (!keepSize) return small;
            var r = new Pixels(p.w, p.h, p.srgb);
            for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) r.c[y * p.w + x] = small.c[Mathf.Min(sh - 1, y * sh / p.h) * sw + Mathf.Min(sw - 1, x * sw / p.w)];
            return r;
        }

        /// <summary>Only <paramref name="n"/> colours: the ones the picture uses most (a few rounds of k-means).</summary>
        public static Pixels Palette(Pixels p, int n)
        {
            n = Mathf.Clamp(n, 2, 64);
            var rnd = new System.Random(7);
            var cent = new Color[n];
            for (int i = 0; i < n; i++) cent[i] = p.c[rnd.Next(p.c.Length)];
            var assign = new int[p.c.Length];
            int step = Mathf.Max(1, p.c.Length / 20000);
            for (int it = 0; it < 8; it++)
            {
                var sum = new Color[n]; var cnt = new int[n];
                for (int q = 0; q < p.c.Length; q += step) { int b = Nearest(p.c[q], cent); sum[b] += p.c[q]; cnt[b]++; }
                for (int i = 0; i < n; i++) cent[i] = cnt[i] > 0 ? sum[i] / cnt[i] : p.c[rnd.Next(p.c.Length)];
            }
            var r = new Pixels(p.w, p.h, p.srgb);
            for (int q = 0; q < p.c.Length; q++) { var c = cent[Nearest(p.c[q], cent)]; c.a = p.c[q].a; r.c[q] = c; }
            return r;
        }
        static int Nearest(Color c, Color[] cent)
        {
            int b = 0; float best = float.MaxValue;
            for (int i = 0; i < cent.Length; i++) { float dr = c.r - cent[i].r, dg = c.g - cent[i].g, db = c.b - cent[i].b; float d = dr * dr + dg * dg + db * db; if (d < best) { best = d; b = i; } }
            return b;
        }

        // ---------------------------------------------------------------- resize without smoothing

        /// <summary>Keeps one pixel out of every few instead of averaging them: harsh and crunchy, like old games.</summary>
        public static Pixels Nearest(Pixels p, int nw, int nh)
        {
            var r = new Pixels(nw, nh, p.srgb);
            for (int y = 0; y < nh; y++)
            {
                int sy = Mathf.Min(p.h - 1, (int)((y + 0.5f) * p.h / nh));
                for (int x = 0; x < nw; x++) r.c[y * nw + x] = p.c[sy * p.w + Mathf.Min(p.w - 1, (int)((x + 0.5f) * p.w / nw))];
            }
            return r;
        }

        /// <summary>Fewer pixels, averaged and then sharpened: thin lines (like mortar) survive, and it still looks hard.</summary>
        public static Pixels Crisp(Pixels p, int nw, int nh) => nw >= p.w && nh >= p.h ? Nearest(p, nw, nh) : TextureOps.Sharpen(p.Resized(nw, nh), 0.8f);

        // ---------------------------------------------------------------- crunch (an old console's look)

        public enum Depth { Full, Bits15, Colours256, Colours16 }

        /// <summary>The dotted pattern used to mix two colours (0..1).</summary>
        public static float Dots(int x, int y) => (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;

        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        /// <summary>
        /// The things that make old console textures look crunchy, other than big pixels: blocky compression (every 4x4
        /// block only gets two colours and the mixes between them), grain, and few colours with a dither pattern.
        /// Works at the size the picture has.
        /// </summary>
        public static Pixels Crunch(Pixels p, Depth depth, float dither, float blocks, float grain, int seed)
        {
            var r = p.Copy();
            if (blocks > 0.001f) r = Blocks(r, blocks);
            if (grain > 0.001f)
                TextureOps.Rows(r.h, y =>
                {
                    for (int x = 0; x < r.w; x++)
                    {
                        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 144269504)); h = unchecked((h ^ (h >> 13)) * 1274126177u); h ^= h >> 16;
                        float n = ((h & 0xFFFF) / 65535f - 0.5f) * grain * 0.5f;
                        var c = r.c[y * r.w + x]; r.c[y * r.w + x] = new Color(Mathf.Clamp01(c.r + n), Mathf.Clamp01(c.g + n), Mathf.Clamp01(c.b + n), c.a);
                    }
                });
            if (depth == Depth.Full) return r;

            Color[] pal = null; float step;
            if (depth == Depth.Bits15) step = 1f / 31f;
            else { int n = depth == Depth.Colours256 ? 256 : 16; pal = PaletteOf(r, n); step = n == 16 ? 0.14f : 0.05f; }
            TextureOps.Rows(r.h, y =>
            {
                for (int x = 0; x < r.w; x++)
                {
                    float b = ((Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f - 0.5f) * dither * step * (pal == null ? 2f : 1f);
                    var c = r.c[y * r.w + x];
                    var d = new Color(Mathf.Clamp01(c.r + b), Mathf.Clamp01(c.g + b), Mathf.Clamp01(c.b + b), c.a);
                    if (pal == null) d = new Color(Mathf.Round(d.r * 31) / 31f, Mathf.Round(d.g * 31) / 31f, Mathf.Round(d.b * 31) / 31f, c.a);
                    else { d = pal[Nearest(d, pal)]; d.a = c.a; }
                    r.c[y * r.w + x] = d;
                }
            });
            return r;
        }

        static Pixels Blocks(Pixels p, float amount)
        {
            var r = p.Copy();
            TextureOps.Rows((p.h + 3) / 4, by =>
            {
                for (int bx = 0; bx < (p.w + 3) / 4; bx++)
                {
                    // the block's darkest and brightest colours, stored with few bits like the real thing
                    Color lo = Color.white, hi = Color.black; float ll = 9, hl = -1;
                    for (int y = by * 4; y < Mathf.Min(p.h, by * 4 + 4); y++)
                        for (int x = bx * 4; x < Mathf.Min(p.w, bx * 4 + 4); x++)
                        { var c = p.c[y * p.w + x]; float l = Pixels.Lum(c); if (l < ll) { ll = l; lo = c; } if (l > hl) { hl = l; hi = c; } }
                    lo = new Color(Mathf.Round(lo.r * 31) / 31f, Mathf.Round(lo.g * 63) / 63f, Mathf.Round(lo.b * 31) / 31f);
                    hi = new Color(Mathf.Round(hi.r * 31) / 31f, Mathf.Round(hi.g * 63) / 63f, Mathf.Round(hi.b * 31) / 31f);
                    for (int y = by * 4; y < Mathf.Min(p.h, by * 4 + 4); y++)
                        for (int x = bx * 4; x < Mathf.Min(p.w, bx * 4 + 4); x++)
                        {
                            var c = p.c[y * p.w + x];
                            float t = hl > ll ? (Pixels.Lum(c) - ll) / (hl - ll) : 0;
                            var q = Color.Lerp(lo, hi, Mathf.Round(t * 3) / 3f);
                            var o = Color.Lerp(c, q, amount); o.a = c.a; r.c[y * p.w + x] = o;
                        }
                }
            });
            return r;
        }

        static Color[] PaletteOf(Pixels p, int n)
        {
            var rnd = new System.Random(7);
            var cent = new Color[n];
            for (int i = 0; i < n; i++) cent[i] = p.c[rnd.Next(p.c.Length)];
            int step = Mathf.Max(1, p.c.Length / 6000);
            for (int it = 0; it < 6; it++)
            {
                var sum = new Color[n]; var cnt = new int[n];
                for (int q = 0; q < p.c.Length; q += step) { int b = Nearest(p.c[q], cent); sum[b] += p.c[q]; cnt[b]++; }
                for (int i = 0; i < n; i++) cent[i] = cnt[i] > 0 ? sum[i] / cnt[i] : p.c[rnd.Next(p.c.Length)];
            }
            return cent;
        }

        // ---------------------------------------------------------------- dirt and wear

        /// <summary>Patchy dirt (a looping noise) plus darker cracks and dips, as if dirt collected in them.</summary>
        public static Pixels Dirt(Pixels p, float amount, float inCracks, float patchSize, Color dirt, int seed)
        {
            var noise = new TextureOps.TileNoise(seed * 31 + 11, Mathf.Clamp(Mathf.RoundToInt(6 / Mathf.Max(0.2f, patchSize)), 2, 40));
            var hgt = Height(p, 0.5f);
            var cav = Cavity(hgt, p.w, p.h);
            var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x;
                    float n = noise.Fbm((x + 0.5f) / p.w, (y + 0.5f) / p.h);
                    float patches = Mathf.SmoothStep(0, 1, (n - 0.45f) * 3f) * amount;
                    float cracks = Mathf.Clamp01(-cav[q] * 6f) * inCracks;
                    float t = Mathf.Clamp01(patches + cracks);
                    var c = Color.Lerp(p.c[q], p.c[q] * dirt, t); c.a = p.c[q].a;
                    r.c[q] = c;
                }
            });
            return r;
        }

        // ---------------------------------------------------------------- flip and turn

        public static Pixels FlipTurn(Pixels p, bool flipX, bool flipY, int quarterTurns)
        {
            var r = p;
            if (flipX || flipY)
            {
                r = new Pixels(p.w, p.h, p.srgb);
                for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) r.c[y * p.w + x] = p.c[(flipY ? p.h - 1 - y : y) * p.w + (flipX ? p.w - 1 - x : x)];
            }
            for (int t = 0; t < ((quarterTurns % 4) + 4) % 4; t++)
            {
                var o = new Pixels(r.h, r.w, r.srgb);
                for (int y = 0; y < r.h; y++) for (int x = 0; x < r.w; x++) o.c[x * o.w + (r.h - 1 - y)] = r.c[y * r.w + x];
                r = o;
            }
            return r == p ? p.Copy() : r;
        }

        // ---------------------------------------------------------------- maps for a material

        /// <summary>How high each pixel probably is (bright = high), stretched to use the full 0..1 range.</summary>
        public static float[] Height(Pixels p, float smooth)
        {
            var h = new float[p.c.Length];
            float lo = 1, hi = 0;
            for (int i = 0; i < h.Length; i++) { h[i] = Pixels.Lum(p.c[i]); lo = Mathf.Min(lo, h[i]); hi = Mathf.Max(hi, h[i]); }
            float k = hi > lo ? 1f / (hi - lo) : 1;
            for (int i = 0; i < h.Length; i++) h[i] = (h[i] - lo) * k;
            if (smooth > 0)
            {
                var px = new Pixels(p.w, p.h, false); for (int i = 0; i < h.Length; i++) px.c[i] = new Color(h[i], h[i], h[i], 1);
                px = Blur(px, smooth * 2f);
                for (int i = 0; i < h.Length; i++) h[i] = px.c[i].r;
            }
            return h;
        }

        /// <summary>Below its surroundings (negative) or above them (positive): cracks and dips are negative.</summary>
        public static float[] Cavity(float[] h, int w, int hh)
        {
            var px = new Pixels(w, hh, false); for (int i = 0; i < h.Length; i++) px.c[i] = new Color(h[i], h[i], h[i], 1);
            var wide = Blur(px, 12f);
            var o = new float[h.Length]; for (int i = 0; i < h.Length; i++) o[i] = h[i] - wide.c[i].r;
            return o;
        }

        public static Pixels HeightMap(Pixels p, float contrast, float smooth = 0.6f, bool invert = false)
        {
            var h = Height(p, smooth); if (invert) for (int i = 0; i < h.Length; i++) h[i] = 1 - h[i];
            var r = new Pixels(p.w, p.h, false);
            for (int i = 0; i < h.Length; i++) { float v = Mathf.Clamp01((h[i] - 0.5f) * contrast + 0.5f); r.c[i] = new Color(v, v, v, 1); }
            return r;
        }

        /// <summary>Metallic (R), ambient occlusion (G), unused (B), smoothness (A): what URP's Lit material reads
        /// from its Metallic map (R, A) and Occlusion map (G). AO: dips and cracks get darker. Smoothness: from how
        /// bright and how flat each spot is.</summary>
        /// <summary>How metal the surface is (the window sets it): all over, or only where the picture is close to a colour.</summary>
        public struct MetalSettings { public float all; public bool byColour; public Color colour; public float range; }
        public static MetalSettings Metal;
        public static float MetalAt(Color c) { var m = Metal; if (!m.byColour) return m.all; float d = Mathf.Sqrt((c.r - m.colour.r) * (c.r - m.colour.r) + (c.g - m.colour.g) * (c.g - m.colour.g) + (c.b - m.colour.b) * (c.b - m.colour.b)); return Mathf.Clamp01((m.range - d) / Mathf.Max(0.02f, m.range * 0.35f)) * Mathf.Max(0.05f, m.all); }
        /// <summary>Light that can't reach a spot because the bumps round it are higher (looked for in 8 directions).</summary>
        public static float[] Occlusion(float[] h, int w, int hh, float radius)
        {
            var o = new float[h.Length]; int steps = 5; float rr = Mathf.Max(2, radius); var dirs = Enumerable.Range(0, 8).Select(k => new Vector2(Mathf.Cos(k * Mathf.PI / 4), Mathf.Sin(k * Mathf.PI / 4))).ToArray();
            float H(int x, int y) { x %= w; if (x < 0) x += w; y %= hh; if (y < 0) y += hh; return h[y * w + x]; }
            TextureOps.Rows(hh, y => { for (int x = 0; x < w; x++) { float h0 = h[y * w + x], occ = 0; foreach (var d in dirs) { float best = 0; for (int s = 1; s <= steps; s++) { float dist = rr * s / steps; float dh = H(Mathf.RoundToInt(x + d.x * dist), Mathf.RoundToInt(y + d.y * dist)) - h0; best = Mathf.Max(best, dh / (dist / rr) * 0.5f); } occ += Mathf.Clamp01(best); } o[y * w + x] = 1 - occ / 8; } });
            return o;
        }
        /// <summary>Edges up (bright) and dips down (dark), grey where it's flat: rust, wear and dirt follow it.</summary>
        public static Pixels Curvature(Pixels p, float strength = 1f)
        {
            var h = Height(p, 0.2f); var cav = Cavity(h, p.w, p.h); var r = new Pixels(p.w, p.h, false);
            for (int i = 0; i < h.Length; i++) { float v = Mathf.Clamp01(0.5f + cav[i] * 4f * strength); r.c[i] = new Color(v, v, v, 1); }
            return r;
        }
        public static Pixels MaskMap(Pixels p, float aoStrength, float smoothness, float metallic)
        {
            var h = Height(p, 0.3f);
            var cav = Cavity(h, p.w, p.h); var occ = Occlusion(h, p.w, p.h, Mathf.Max(p.w, p.h) * 0.012f);
            var r = new Pixels(p.w, p.h, false);
            for (int i = 0; i < h.Length; i++)
            {
                float ao = Mathf.Clamp01(1 + Mathf.Min(0, cav[i]) * 4f * aoStrength) * Mathf.Lerp(1, occ[i], Mathf.Clamp01(aoStrength)); metallic = Mathf.Max(0, MetalAt(p.c[i]));
                float sm = Mathf.Clamp01(smoothness * (0.6f + 0.4f * h[i]) * ao);
                r.c[i] = new Color(metallic, ao, 0, sm);
            }
            return r;
        }
    }
}
