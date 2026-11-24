using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The picture changes Texture Studio makes. All work on plain colours, so they run on any texture.</summary>
    public static class TextureOps
    {
        /// <summary>Does every row of a picture, spread over all the processor's cores.</summary>
        public static void Rows(int h, System.Action<int> row) => System.Threading.Tasks.Parallel.For(0, h, row);

        // ---------------------------------------------------------------- even out lighting

        /// <summary>Takes out big, slow changes in brightness (a photo lit from one side, a dark corner), which are what
        /// make a tiling texture look like a checkerboard. Small detail is kept.</summary>
        public static Pixels EvenLighting(Pixels p, float strength, int cells = 6)
        {
            if (strength <= 0) return p.Copy();
            // the average colour in a coarse grid, read back smoothly (wrapping): that's the "lighting"
            int gx = Mathf.Max(2, cells), gy = Mathf.Max(2, Mathf.RoundToInt(cells * p.h / (float)p.w));
            var grid = new Color[gx * gy]; var n = new int[gx * gy];
            Color mean = Color.clear;
            for (int y = 0; y < p.h; y++)
                for (int x = 0; x < p.w; x++)
                {
                    int i = (y * gy / p.h) * gx + (x * gx / p.w);
                    grid[i] += p.c[y * p.w + x]; n[i]++; mean += p.c[y * p.w + x];
                }
            for (int i = 0; i < grid.Length; i++) grid[i] /= Mathf.Max(1, n[i]);
            mean /= p.c.Length;
            var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float u = (x + 0.5f) / p.w * gx - 0.5f, v = (y + 0.5f) / p.h * gy - 0.5f;
                    int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v); float fx = u - x0, fy = v - y0;
                    Color G(int a, int b) { a %= gx; if (a < 0) a += gx; b %= gy; if (b < 0) b += gy; return grid[b * gx + a]; }
                    var low = Color.Lerp(Color.Lerp(G(x0, y0), G(x0 + 1, y0), fx), Color.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy);
                    var c = p.c[y * p.w + x];
                    var even = c - low + mean; even.a = c.a;
                    r.c[y * p.w + x] = Clamp(Color.Lerp(c, even, strength));
                }
            });
            return r;
        }

        // ---------------------------------------------------------------- the pattern's own repeat (bricks, tiles, planks)

        /// <summary>How far the picture repeats itself along x and along y (e.g. one brick, two brick rows), found by
        /// comparing it with shifted copies of itself. 0 = no clear repeat.</summary>
        public static Vector2Int FindRepeat(Pixels p)
        {
            var rnd = new System.Random(5);
            int n = 3000; var xs = new int[n]; var ys = new int[n];
            for (int i = 0; i < n; i++) { xs[i] = rnd.Next(p.w); ys[i] = rnd.Next(p.h); }
            var lum = new float[p.c.Length]; for (int i = 0; i < lum.Length; i++) lum[i] = Pixels.Lum(p.c[i]);
            int Best(bool alongX)
            {
                int len = alongX ? p.w : p.h; var score = new float[len];
                float worst = 0;
                System.Threading.Tasks.Parallel.For(4, Mathf.Max(4, len - 4), s =>
                {
                    float d = 0;
                    for (int i = 0; i < n; i++)
                    {
                        int x = xs[i], y = ys[i]; int x2 = alongX ? (x + s) % p.w : x, y2 = alongX ? y : (y + s) % p.h;
                        d += Mathf.Abs(lum[y * p.w + x] - lum[y2 * p.w + x2]);
                    }
                    score[s] = d / n;
                });
                for (int s = 4; s < len - 4; s++) worst = Mathf.Max(worst, score[s]);
                float min = float.MaxValue; for (int s = 4; s < len - 4; s++) min = Mathf.Min(min, score[s]);
                if (worst <= 0 || min > worst * 0.75f) return 0; // (nothing repeats clearly: stones, grass)
                // (tiny shifts always look alike, as neighbouring pixels do: start looking once it has stopped looking alike)
                int from = 5; while (from < len - 5 && score[from] < min + (worst - min) * 0.6f) from++;
                // the first shift that's nearly as good as the best one: the basic repeat, not a multiple of it
                for (int s = from; s < len - len / 4; s++)
                    if (score[s] <= min + (worst - min) * 0.12f && score[s] <= score[s - 1] && score[s] <= score[s + 1]) return s;
                return 0;
            }
            return new Vector2Int(Best(true), Best(false));
        }

        static int Snap(float want, int period, int len)
        {
            if (period <= 0) return Mathf.RoundToInt(want);
            return Mathf.RoundToInt(want / period) * period % len;
        }

        // ---------------------------------------------------------------- make seamless

        public enum SeamStyle { Natural, Soft, Mirror, Lines }

        /// <summary>Makes the picture loop: the edges are replaced by the picture's own middle, blended in.
        /// Natural follows the light and dark shapes (stones stay whole), Soft fades, Mirror flips the picture.</summary>
        public enum Ways { Both, Sideways, UpDown }
        public static Pixels MakeSeamless(Pixels p, float blend, SeamStyle style, bool keepPattern = false, Ways ways = Ways.Both)
        {
            var r = new Pixels(p.w, p.h, p.srgb);
            if (style == SeamStyle.Mirror)
            {
                for (int y = 0; y < p.h; y++)
                    for (int x = 0; x < p.w; x++)
                    {
                        float u = x < p.w / 2 ? x * 2 : (p.w - 1 - x) * 2, v = y < p.h / 2 ? y * 2 : (p.h - 1 - y) * 2;
                        r.c[y * p.w + x] = p.Sample(ways == Ways.UpDown ? x + 0.5f : u + 1f, ways == Ways.Sideways ? y + 0.5f : v + 1f);
                    }
                return r;
            }
            blend = Mathf.Clamp(blend, 0.02f, 0.5f);
            float bw = p.w * blend, bh = p.h * blend;
            int hw = p.w / 2, hh = p.h / 2;
            if (keepPattern)
            {
                // moved by whole bricks/tiles, so the copy lines up with the picture where they blend
                var rep = FindRepeat(p);
                hw = Snap(p.w / 2f, rep.x, p.w); hh = Snap(p.h / 2f, rep.y, p.h);
                if (hw < p.w / 5 || hw > p.w * 4 / 5) hw = p.w / 2;
                if (hh < p.h / 5 || hh > p.h * 4 / 5) hh = p.h / 2;
            }
            if (ways == Ways.Sideways) hh = 0; if (ways == Ways.UpDown) hw = 0;
            var wobble = new TileNoise(977, 5); // (bends the edge of the blend so it isn't a visible rectangle)
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    var a = p.c[y * p.w + x];             // the picture
                    var b = p[x + hw, y + hh];            // the picture moved by half: its edges are the middle, so they loop
                    float ex = Mathf.Min(x + 0.5f, p.w - 0.5f - x) / bw, ey = Mathf.Min(y + 0.5f, p.h - 0.5f - y) / bh;
                    if (ways == Ways.Sideways) ey = 1; if (ways == Ways.UpDown) ex = 1;
                    float t = Mathf.Clamp01(Mathf.Min(ex, ey));   // 0 at the edge, 1 inside
                    t = Mathf.Clamp01(t + (wobble.Fbm((x + 0.5f) / p.w, (y + 0.5f) / p.h) - 0.5f) * 1.6f * t * (1 - t) * 4);
                    float k;
                    if (style == SeamStyle.Natural)
                    {
                        // where the picture is brighter (raised) it wins sooner: the join follows the shapes
                        // (fades out towards the very edge and the inside, so the loop itself is exact)
                        float d = (Pixels.Lum(a) - Pixels.Lum(b)) * 0.45f * 4 * t * (1 - t);
                        k = Mathf.Clamp01((t - 0.5f + d) * 3.2f + 0.5f);
                    }
                    else k = t * t * (3 - 2 * t);
                    r.c[y * p.w + x] = Color.Lerp(b, a, k);
                }
            });
            return r;
        }

        // ---------------------------------------------------------------- break repeats (stitched pieces)

        [System.Serializable]
        public class BreakSettings
        {
            public int tiles = 3;            // the new texture covers this many of the old one across (so it repeats this much less)
            public int maxSize = 2048;
            public float pieceSize = 0.4f;   // the size of each stitched piece, as a part of the original
            public bool rotate = false;      // pieces can be turned and flipped (only for things with no direction)
            public bool keepPattern = true;  // pieces stay lined up with the pattern (bricks, tiles, planks), if it has one
            public float brightnessVariation = 0.06f;
            public float colourVariation = 0.0f;
            public int seed = 1;
        }

        /// <summary>
        /// A bigger texture with no visible repeat, made the way a person would patch it: real pieces of the original,
        /// each one picked because its edges match what's already there, and stitched along the line where the two
        /// differ least (so the joins follow cracks and edges instead of fading). It still loops. Works best on a
        /// texture that already loops.
        /// </summary>
        public static Pixels BreakRepeats(Pixels p, BreakSettings s, int previewSide = 0)
        {
            int tiles = Mathf.Clamp(s.tiles, 1, 8);
            int cap = previewSide > 0 ? previewSide : Mathf.Max(64, s.maxSize);
            float shrink = Mathf.Min(1f, cap / (float)(Mathf.Max(p.w, p.h) * tiles));
            var src = shrink < 0.999f ? p.Resized(Mathf.Max(16, Mathf.FloorToInt(p.w * shrink)), Mathf.Max(16, Mathf.FloorToInt(p.h * shrink))) : p;
            int W = src.w * tiles, H = src.h * tiles;

            int B = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(src.w, src.h) * Mathf.Clamp(s.pieceSize, 0.15f, 0.9f)), 12, Mathf.Min(src.w, src.h));
            int O = Mathf.Max(4, B / 5);
            int nx = Mathf.Max(2, Mathf.RoundToInt(W / (float)(B - O))), ny = Mathf.Max(2, Mathf.RoundToInt(H / (float)(B - O)));
            var xs = new int[nx + 1]; var ys = new int[ny + 1];
            for (int i = 0; i <= nx; i++) xs[i] = Mathf.RoundToInt(i * W / (float)nx);
            for (int j = 0; j <= ny; j++) ys[j] = Mathf.RoundToInt(j * H / (float)ny);

            var outC = new Color[W * H]; var filled = new bool[W * H];
            var rnd = new System.Random(s.seed);
            var rep = s.keepPattern ? FindRepeat(src) : Vector2Int.zero;
            int K = 22; // (the same for the preview and the real thing, so they pick the same pieces)
            var cx = new int[K]; var cy = new int[K]; var cr = new int[K]; var cf = new bool[K]; var ce = new float[K];

            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int ox = xs[i], oy = ys[j];
                    int pw = xs[i + 1] - xs[i] + O, ph = ys[j + 1] - ys[j] + O;
                    // ---- a few random pieces; keep one whose edges match best (not always the very best: variety)
                    float min = float.MaxValue;
                    for (int k = 0; k < K; k++)
                    {
                        int sx = rnd.Next(src.w), sy = rnd.Next(src.h);
                        if (rep.x > 0) sx = Wrap(ox + Snap(sx - ox, rep.x, src.w), src.w);
                        if (rep.y > 0) sy = Wrap(oy + Snap(sy - oy, rep.y, src.h), src.h);
                        cx[k] = sx; cy[k] = sy;
                        bool turn = s.rotate && !s.keepPattern;
                        cr[k] = turn ? rnd.Next(4) : 0; cf[k] = turn && rnd.Next(2) == 1;
                    }
                    if (i > 0 || j > 0)
                        System.Threading.Tasks.Parallel.For(0, K, k =>
                        {
                            float e = 0; int n = 0;
                            for (int ly = 0; ly < ph; ly += 2)
                                for (int lx = 0; lx < pw; lx += 2)
                                {
                                    int gx = (ox + lx) % W, gy = (oy + ly) % H, g = gy * W + gx;
                                    if (!filled[g]) continue;
                                    e += Dist2(outC[g], Piece(src, cx[k], cy[k], cr[k], cf[k], lx, ly)); n++;
                                }
                            ce[k] = n > 0 ? e / n : 0;
                        });
                    for (int k = 0; k < K; k++) if (ce[k] < min) min = ce[k];
                    int pick = 0; int seen = 0;
                    for (int k = 0; k < K; k++) if (ce[k] <= min * 1.2f + 1e-6f) { seen++; if (rnd.Next(seen) == 0) pick = k; }
                    int psx = cx[pick], psy = cy[pick], prot = cr[pick]; bool pflip = cf[pick];

                    // ---- where the new piece shows: everything, except across a cut through each overlap with what's there
                    var m = new float[pw * ph]; for (int q = 0; q < m.Length; q++) m[q] = 1;
                    var piece = new Color[pw * ph]; var had = new bool[pw * ph];
                    for (int ly = 0; ly < ph; ly++)
                        for (int lx = 0; lx < pw; lx++)
                        {
                            piece[ly * pw + lx] = Piece(src, psx, psy, prot, pflip, lx, ly);
                            had[ly * pw + lx] = filled[((oy + ly) % H) * W + (ox + lx) % W];
                        }
                    float Err(int lx, int ly)
                    {
                        int q = ly * pw + lx; if (!had[q]) return 0;
                        return Dist2(outC[((oy + ly) % H) * W + (ox + lx) % W], piece[q]);
                    }
                    if (i > 0) CutAcross(m, pw, ph, 0, O, true, false, Err);                   // left edge: old stays on the left
                    if (i == nx - 1) CutAcross(m, pw, ph, pw - O, O, true, true, Err);         // last in the row meets the first (loops)
                    if (j > 0) CutAcross(m, pw, ph, 0, O, false, false, Err);                  // bottom edge
                    if (j == ny - 1) CutAcross(m, pw, ph, ph - O, O, false, true, Err);        // last row meets the first
                    // soften the stitch by a pixel or two
                    m = Soften(m, pw, ph);
                    for (int ly = 0; ly < ph; ly++)
                        for (int lx = 0; lx < pw; lx++)
                        {
                            int q = ly * pw + lx, g = ((oy + ly) % H) * W + (ox + lx) % W;
                            outC[g] = had[q] ? Color.Lerp(outC[g], piece[q], m[q]) : piece[q];
                            filled[g] = true;
                        }
                }

            var r = new Pixels(W, H, p.srgb) { c = outC };
            // gentle, large light/colour changes so the eye has nothing regular to lock on to
            if (s.brightnessVariation > 0 || s.colourVariation > 0)
            {
                var macro = new TileNoise(s.seed * 17 + 3, Mathf.Max(2, tiles)); var macroC = new TileNoise(s.seed * 13 + 5, Mathf.Max(2, tiles));
                TextureOps.Rows(H, y =>
            {
                    for (int x = 0; x < W; x++)
                    {
                        float u = (x + 0.5f) / W, v = (y + 0.5f) / H; var c = r.c[y * W + x];
                        if (s.brightnessVariation > 0) { float k = 1 + (macro.Fbm(u, v) - 0.5f) * 2 * s.brightnessVariation; c.r *= k; c.g *= k; c.b *= k; }
                        if (s.colourVariation > 0) c = HueShift(c, (macroC.Fbm(u, v) - 0.5f) * 2 * s.colourVariation);
                        r.c[y * W + x] = Clamp(c);
                    }
            });
            }
            return r;
        }

        static int Wrap(int v, int n) { v %= n; return v < 0 ? v + n : v; }
        static float Dist2(Color a, Color b) { float r = a.r - b.r, g = a.g - b.g, bl = a.b - b.b; return r * r + g * g + bl * bl; }

        // a pixel of a piece of the (looping) source, turned/flipped as asked
        static Color Piece(Pixels src, int sx, int sy, int rot, bool flip, int lx, int ly)
        {
            int a = flip ? -lx : lx, b = ly;
            switch (rot) { case 1: { int t = a; a = -b; b = t; break; } case 2: a = -a; b = -b; break; case 3: { int t = a; a = b; b = -t; break; } }
            return src[sx + a, sy + b];
        }

        /// <summary>The cheapest path through an overlap strip (where old and new differ least). Pixels on the old
        /// side of it keep the old picture (m = 0).</summary>
        static void CutAcross(float[] m, int pw, int ph, int start, int width, bool vertical, bool oldOnFarSide, System.Func<int, int, float> err)
        {
            int len = vertical ? ph : pw;
            width = Mathf.Min(width, vertical ? pw : ph);
            var cost = new float[len * width]; var from = new int[len * width];
            for (int t = 0; t < len; t++)
                for (int k = 0; k < width; k++)
                {
                    float e = vertical ? err(start + k, t) : err(t, start + k);
                    if (t == 0) { cost[k] = e; continue; }
                    float best = float.MaxValue; int bk = k;
                    for (int d = -1; d <= 1; d++) { int kk = k + d; if (kk < 0 || kk >= width) continue; float c = cost[(t - 1) * width + kk]; if (c < best) { best = c; bk = kk; } }
                    cost[t * width + k] = e + best; from[t * width + k] = bk;
                }
            int end = 0; float bestEnd = float.MaxValue;
            for (int k = 0; k < width; k++) if (cost[(len - 1) * width + k] < bestEnd) { bestEnd = cost[(len - 1) * width + k]; end = k; }
            for (int t = len - 1; t >= 0; t--)
            {
                for (int k = 0; k < width; k++)
                {
                    bool old = oldOnFarSide ? k > end : k < end;
                    if (!old) continue;
                    int lx = vertical ? start + k : t, ly = vertical ? t : start + k;
                    m[ly * pw + lx] = 0;
                }
                if (t > 0) end = from[t * width + end];
            }
        }

        static float[] Soften(float[] m, int w, int h)
        {
            var o = new float[m.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; int n = 0;
                    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { int xx = x + i, yy = y + j; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue; s += m[yy * w + xx]; n++; }
                    o[y * w + x] = s / n;
                }
            return o;
        }

        // ---------------------------------------------------------------- sharpen

        /// <summary>Crisper detail (an unsharp mask): handy after resizing or blending.</summary>
        public static Pixels Sharpen(Pixels p, float amount, float radius, bool soften = false)
        {
            if (amount <= 0 && !soften) return p.Copy();
            var soft = TextureOpsMore.Blur(p, radius); var r = new Pixels(p.w, p.h, p.srgb);
            Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int i = y * p.w + x; var c = p.c[i]; var o = c + (c - soft.c[i]) * amount; o.a = c.a; r.c[i] = Clamp(o); } });
            return r;
        }

        public static Pixels Sharpen(Pixels p, float amount)
        {
            if (amount <= 0) return p.Copy();
            var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    var c = p.c[y * p.w + x];
                    var blur = (p[x - 1, y] + p[x + 1, y] + p[x, y - 1] + p[x, y + 1]) * 0.25f;
                    var o = c + (c - blur) * amount; o.a = c.a;
                    r.c[y * p.w + x] = Clamp(o);
                }
            });
            return r;
        }

        /// <summary>Smooth blotchy noise that repeats exactly every 1 unit (u, v in 0..1).</summary>
        public class TileNoise
        {
            readonly int seed, period;
            public TileNoise(int seed, int period) { this.seed = seed; this.period = Mathf.Max(1, period); }
            float Hash(int x, int y, int p)
            {
                x %= p; if (x < 0) x += p; y %= p; if (y < 0) y += p;
                unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144269504 + p * 9737333); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; }
            }
            float Value(float u, float v, int p)
            {
                float x = u * p, y = v * p; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, p), Hash(x0 + 1, y0, p), fx), Mathf.Lerp(Hash(x0, y0 + 1, p), Hash(x0 + 1, y0 + 1, p), fx), fy);
            }
            public float Fbm(float u, float v) => Value(u, v, period) * 0.62f + Value(u, v, period * 2) * 0.26f + Value(u, v, period * 4) * 0.12f;
        }

        // ---------------------------------------------------------------- normal map

        /// <summary>A normal map from a picture: bright = raised, dark = sunk.</summary>
        public static Pixels NormalFromHeight(Pixels p, float strength, float smooth, bool invert, bool directX)
        {
            var hgt = new float[p.w * p.h];
            for (int i = 0; i < hgt.Length; i++) hgt[i] = invert ? 1 - Pixels.Lum(p.c[i]) : Pixels.Lum(p.c[i]);
            int passes = Mathf.RoundToInt(Mathf.Clamp(smooth, 0, 1) * 4);
            for (int k = 0; k < passes; k++) hgt = Blur(hgt, p.w, p.h);
            float H(int x, int y) { x %= p.w; if (x < 0) x += p.w; y %= p.h; if (y < 0) y += p.h; return hgt[y * p.w + x]; }
            var r = new Pixels(p.w, p.h, false);
            float k2 = strength * Mathf.Max(p.w, p.h) / 256f;
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float dx = (H(x + 1, y - 1) + 2 * H(x + 1, y) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x - 1, y) + H(x - 1, y + 1));
                    float dy = (H(x - 1, y + 1) + 2 * H(x, y + 1) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2 * H(x, y - 1) + H(x + 1, y - 1));
                    var n = new Vector3(-dx * k2, -dy * k2, 1).normalized;
                    if (directX) n.y = -n.y;
                    r.c[y * p.w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
                }
            });
            return r;
        }

        static float[] Blur(float[] a, int w, int h)
        {
            var b = new float[a.Length];
            TextureOps.Rows(h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { int xx = (x + i + w) % w, yy = (y + j + h) % h; s += a[yy * w + xx]; }
                    b[y * w + x] = s / 9f;
                }
            });
            return b;
        }

        // ---------------------------------------------------------------- colour

        [System.Serializable]
        public class AdjustSettings
        {
            public float brightness, contrast, saturation, hue, gamma = 1;
            public Color tint = Color.white;
            public bool invert;
        }

        public static Pixels Adjust(Pixels p, AdjustSettings s)
        {
            var r = new Pixels(p.w, p.h, p.srgb);
            Rows(p.h, row =>
            {
                for (int i = row * p.w, end = i + p.w; i < end; i++)
                {
                var c = p.c[i]; float a = c.a;
                if (s.invert) c = new Color(1 - c.r, 1 - c.g, 1 - c.b);
                if (Mathf.Abs(s.gamma - 1) > 0.001f) { float g = 1f / Mathf.Max(0.05f, s.gamma); c = new Color(Mathf.Pow(Mathf.Max(0, c.r), g), Mathf.Pow(Mathf.Max(0, c.g), g), Mathf.Pow(Mathf.Max(0, c.b), g)); }
                if (s.hue != 0) c = HueShift(c, s.hue);
                if (s.saturation != 0) { float l = Pixels.Lum(c); c = Color.LerpUnclamped(new Color(l, l, l), c, 1 + s.saturation); }
                if (s.contrast != 0) { float k = 1 + s.contrast; c = new Color((c.r - 0.5f) * k + 0.5f, (c.g - 0.5f) * k + 0.5f, (c.b - 0.5f) * k + 0.5f); }
                c = new Color(c.r + s.brightness, c.g + s.brightness, c.b + s.brightness);
                c *= s.tint;
                c.a = a;
                r.c[i] = Clamp(c);
                }
            });
            return r;
        }

        static Color HueShift(Color c, float amount)
        {
            Color.RGBToHSV(c, out var h, out var sat, out var v);
            h = Mathf.Repeat(h + amount, 1f);
            var o = Color.HSVToRGB(h, sat, v, true); o.a = c.a; return o;
        }

        static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));

        // ---------------------------------------------------------------- channel packing

        public enum From { Red, Green, Blue, Alpha, Brightness }

        public class Slot
        {
            public string label;
            public Texture2D tex;
            public From from = From.Brightness;
            public bool invert;
            public float value = 1; // used when there's no texture
            public Slot(string label, float value, From from = From.Brightness) { this.label = label; this.value = value; this.from = from; }
        }

        /// <summary>Four greyscale pictures into one texture's red, green, blue and alpha (a mask map).</summary>
        public static Pixels Pack(Slot[] slots, out string problem)
        {
            problem = null;
            int w = 0, h = 0;
            var read = new Pixels[4];
            for (int i = 0; i < 4; i++)
                if (slots[i].tex) { read[i] = Pixels.Read(slots[i].tex); if (read[i].w * read[i].h > w * h) { w = read[i].w; h = read[i].h; } }
            if (w == 0) { problem = "Pick at least one texture."; return null; }
            for (int i = 0; i < 4; i++) if (read[i] != null && (read[i].w != w || read[i].h != h)) read[i] = read[i].Resized(w, h);
            var r = new Pixels(w, h, false);
            for (int k = 0; k < r.c.Length; k++)
            {
                var o = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    float v;
                    if (read[i] == null) v = slots[i].value;
                    else
                    {
                        var c = read[i].c[k];
                        v = slots[i].from == From.Red ? c.r : slots[i].from == From.Green ? c.g : slots[i].from == From.Blue ? c.b : slots[i].from == From.Alpha ? c.a : Pixels.Lum(c);
                        if (slots[i].invert) v = 1 - v;
                    }
                    o[i] = v;
                }
                r.c[k] = new Color(o[0], o[1], o[2], o[3]);
            }
            return r;
        }
    }
}
