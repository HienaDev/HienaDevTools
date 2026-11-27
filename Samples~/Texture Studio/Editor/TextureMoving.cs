using System;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Moving textures made from nothing (or from a texture): fire, smoke, caustics, ripples, lava, sparks, magic,
    /// electricity, or a texture scrolling, warping or pulsing. Every frame loops back into the first, and the tiled kinds
    /// also loop across their edges.</summary>
    public static class TextureMoving
    {
        public enum Kind { Fire, Smoke, Caustics, Ripples, Lava, Sparks, Magic, Electric, Scroll, Warp, Pulse }
        public static readonly string[] Names = { "Fire", "Smoke", "Caustics", "Ripples", "Lava", "Sparks", "Magic", "Electric", "Scroll it", "Warp it", "Pulse it" };
        public static readonly string[] About =
        {
            "A flame for torches and fires (see-through, add it on top)", "Rising smoke, soft and see-through", "Light dancing under water: for pool floors (loops across)", "Rings spreading on water (loops across)",
            "Glowing cracks in dark crust, slowly moving (loops across)", "Sparks flying out and falling", "A magic swirl or portal", "Crackling lightning bolts with forks (loops across)",
            "The texture you're editing, sliding along (loops)", "The texture you're editing, wobbling (loops)", "The texture you're editing, glowing brighter and darker",
        };
        /// <summary>Whether it's a shape in the middle (shown on a quad or particle) rather than a surface that tiles.</summary>
        public static bool Billboard(Kind k) => k == Kind.Fire || k == Kind.Smoke || k == Kind.Sparks || k == Kind.Magic;
        /// <summary>Whether it should be added on top (glowing) rather than drawn over.</summary>
        public static bool Additive(Kind k) => k == Kind.Fire || k == Kind.Sparks || k == Kind.Magic || k == Kind.Electric;

        [Serializable]
        public class Settings
        {
            public Kind kind; public int frames = 16, size = 128, seed = 1; public float scale = 0.5f, wild = 0.5f, speed = 1f, dirAngle = 90; public Color[] cols; public float[] at; public bool dither; public int levels = 5;
            /// <summary>Three extra knobs whose meaning depends on the kind (see <see cref="Knobs"/>).</summary>
            public float p1 = 0.5f, p2 = 0.5f, p3 = 0.5f;
            public void KnobDefaults() { var k = Knobs(kind); p1 = k.Length > 0 ? k[0].def : 0.5f; p2 = k.Length > 1 ? k[1].def : 0.5f; p3 = k.Length > 2 ? k[2].def : 0.5f; }
            public Settings Copy() { var c = (Settings)MemberwiseClone(); c.cols = (Color[])cols?.Clone(); c.at = (float[])at?.Clone(); return c; }
            public void Defaults() { var r = Ramp(kind); cols = r.Item1; at = r.Item2; }
        }
        public static (Color[], float[]) Ramp(Kind k)
        {
            string[] h; switch (k)
            {
                case Kind.Fire: h = new[] { "000000", "5e0b00", "e0400a", "ffae2b", "fff3b0" }; break; case Kind.Smoke: h = new[] { "2a2a2e", "5b5a5c", "9b9894", "d8d4cc" }; break;
                case Kind.Caustics: h = new[] { "0b3a4a", "1d7a8a", "8ee6e8", "ffffff" }; break; case Kind.Ripples: h = new[] { "123a52", "2f7aa0", "a9e2f5" }; break;
                case Kind.Lava: h = new[] { "120806", "3a0d05", "c42d06", "ff8a12", "ffe27a" }; break; case Kind.Sparks: h = new[] { "000000", "ff5a0a", "ffc24a", "ffffff" }; break;
                case Kind.Magic: h = new[] { "000000", "3d0a73", "a13cff", "5ef2ff", "ffffff" }; break; case Kind.Electric: h = new[] { "000000", "1b2a8a", "4fb7ff", "ffffff" }; break;
                default: h = new[] { "000000", "ffffff" }; break;
            }
            return (h.Select(FxOps.Hex).ToArray(), Enumerable.Range(0, h.Length).Select(i => i / (float)(h.Length - 1)).ToArray());
        }
        /// <summary>The extra knobs each kind has: its name, what it does, and where it starts.</summary>
        public static (string name, string tip, float def)[] Knobs(Kind k)
        {
            switch (k)
            {
                case Kind.Fire: return new[] { ("Height", "How tall the flame is", 0.5f), ("Width", "How wide the flame is", 0.5f), ("Flicker", "Small fast tongues on top of the big shapes", 0.5f) };
                case Kind.Smoke: return new[] { ("Spread", "How much wider it gets as it rises", 0.5f), ("Thickness", "Thin wisps or thick smoke", 0.5f), ("Height", "How high it goes before it fades", 0.5f) };
                case Kind.Caustics: return new[] { ("Line thickness", "Thin sharp lines (big bright holes) or thick soft ones", 0.4f), ("Second layer", "A finer layer of lines on top, moving the other way", 0.5f), ("Wavy lines", "Bends the lines so they look less like cells", 0.3f) };
                case Kind.Ripples: return new[] { ("Drops", "How many rings at once", 0.36f), ("Rings", "Few wide rings or many close ones", 0.5f), ("How far", "How far each ring spreads before it fades", 0.43f) };
                case Kind.Lava: return new[] { ("Crack width", "Thin glowing cracks or wide rivers", 0.5f), ("Crust size", "Small or big plates of crust", 0.5f), ("Glow", "How much the hot spots glow through the crust", 0.5f) };
                case Kind.Sparks: return new[] { ("How many", "", 0.5f), ("Spark size", "", 0.5f), ("Gravity", "How fast they fall back down", 0.5f) };
                case Kind.Magic: return new[] { ("Ring size", "How big the glowing ring is", 0.57f), ("Swirl", "How much it twists round", 0.5f), ("Middle glow", "How bright the middle is", 0.5f) };
                case Kind.Electric: return new[] { ("Bolts", "How many main bolts", 0.4f), ("Branches", "Forks coming off the bolts", 0.5f), ("Thickness", "How thick the bright core is", 0.3f) };
                case Kind.Warp: return new[] { ("Warp size", "Small ripples or big slow bends", 0.5f) };
                default: return new (string, string, float)[0];
            }
        }
        static float E(float p, float range) => Mathf.Pow(2, (p - 0.5f) * range); // 1 in the middle, smaller or bigger either side

        public static (int cols, int rows) Grid(int frames) { int c = Mathf.CeilToInt(Mathf.Sqrt(frames)); return (c, Mathf.CeilToInt(frames / (float)c)); }

        /// <summary>Frame i of n (t = i / n goes once round, so the last frame leads back into the first).</summary>
        public static Pixels Frame(Settings s, int i, int n, int size, Pixels source)
        {
            float t = i / (float)Mathf.Max(1, n), tau = Mathf.PI * 2; int per = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(12, 2, s.scale)), 1, 24), loops = Mathf.Max(1, Mathf.RoundToInt(s.speed)); var o = new Pixels(size, size, true);
            var n1 = new TextureOps.TileNoise(s.seed, per); var n2 = new TextureOps.TileNoise(s.seed + 17, per * 2); var n3 = new TextureOps.TileNoise(s.seed + 41, per * 4);
            float cx = Mathf.Cos(tau * t), cy = Mathf.Sin(tau * t); var rnd = new System.Random(s.seed * 7);
            if (s.kind == Kind.Electric) return Lightning(s, i, n, size);
            if (s.kind == Kind.Caustics) per = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(14, 1, s.scale)), 1, 24);
            var sparks = s.kind == Kind.Sparks ? Enumerable.Range(0, Mathf.Max(1, Mathf.RoundToInt((24 + s.wild * 40) * E(s.p1, 2.4f)))).Select(_ => (a: (float)rnd.NextDouble() * tau, sp: 0.35f + (float)rnd.NextDouble() * 0.45f, ph: (float)rnd.NextDouble(), sz: (0.014f + (float)rnd.NextDouble() * 0.02f) * E(s.p2, 2.4f))).ToArray() : null;
            // where every spark (and its short trail) is in this frame, worked out once rather than for every pixel
            var dots = new System.Collections.Generic.List<(Vector2 p, float r, float w)>();
            if (sparks != null) foreach (var sp in sparks) { float life = Mathf.Repeat(t * loops + sp.ph, 1); var dir = new Vector2(Mathf.Cos(sp.a), Mathf.Sin(sp.a) * 0.6f + 0.6f); for (int k = 0; k < 4; k++) { float l = Mathf.Max(0, life - k * 0.02f); var pp = new Vector2(0.5f, 0.3f) + dir * l * sp.sp + new Vector2(0, -0.9f * s.p3 * l * l * (0.5f + s.wild)); dots.Add((pp, Mathf.Max(1e-4f, sp.sz * (1 - k * 0.2f)), (1 - life) * (1 - k * 0.22f))); } }
            var drops = s.kind == Kind.Ripples ? Enumerable.Range(0, Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, 12, s.p1)), 1, 12)).Select(_ => (c: new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble()), ph: (float)rnd.NextDouble())).ToArray() : null;
            TextureOps.Rows(size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size, val = 0, alpha = 1; Color col = default; bool fromRamp = true;
                    switch (s.kind)
                    {
                        case Kind.Fire:
                        {
                            // a cone of flame, licked into tongues by noise rising through it (whole periods per loop, so it loops)
                            float rise = loops; float bend = (n2.Fbm(u, v - t * rise) - 0.5f) * 0.25f * v; float dx = Mathf.Abs((u + bend - 0.5f) * 2);
                            float nn = n1.Fbm(u * 1.0f, v - t * rise) * 0.55f + n3.Fbm(u, v - t * rise * 2) * 0.45f;
                            float fl = Mathf.Clamp01(s.p3) * 0.9f; nn = n1.Fbm(u * 1.0f, v - t * rise) * (1 - fl) + n3.Fbm(u, v - t * rise * 2) * fl; dx /= E(s.p2, 2.4f);
                            float body = 1 - v * 1.2f / E(s.p1, 2f) - dx * (1.7f + v * 1.4f); float flame = body + (nn - 0.45f) * (0.6f + s.wild * 1.6f) * (0.15f + v * 1.3f);
                            float top = Mathf.Clamp01((1 - v) * 6); flame *= top; val = Mathf.Clamp01(flame / 0.75f); val = val * val * (3 - 2 * val); alpha = Mathf.Clamp01(flame * 6); break; // (nothing reaches the frame's top, so frames don't bleed into each other)
                        }
                        case Kind.Smoke:
                        {
                            float dx = (u - 0.5f) * 2 / (0.35f + v * 0.9f * E(s.p1, 2f)); float nn = n1.Fbm(u + 0.1f * Mathf.Sin(v * 6), v - t * loops) * 0.7f + n2.Fbm(u, v - t * loops * 2) * 0.3f;
                            float body = (1 - dx * dx) * Mathf.Clamp01(1 - v * 1.05f / E(s.p3, 1.5f)) * Mathf.Clamp01(v * 6); float dens = Mathf.Clamp01(body * (0.4f + nn * 1.3f * (0.5f + s.wild)) * E(s.p2, 2f) - 0.15f); val = 0.3f + nn * 0.7f; alpha = Mathf.SmoothStep(0, 1, dens * 1.6f); break;
                        }
                        case Kind.Caustics:
                        {
                            float wv = s.p3 * 0.6f / per; float cu = u + (n1.Fbm(u + cx * 0.1f, v + cy * 0.1f) - 0.5f) * wv, cv = v + (n3.Fbm(u - cy * 0.1f, v + cx * 0.1f) - 0.5f) * wv; float thick = 2.2f / E(s.p1, 3f);
                            val = Mathf.Clamp01(Caustic(cu, cv, per, s.seed, t * loops, thick) * 0.75f + Caustic(cu + 0.37f, cv + 0.11f, per * 2, s.seed + 5, -t * loops, thick * 1.2f) * 0.9f * s.p2); val = Mathf.Pow(val, Mathf.Lerp(2.2f, 1f, s.wild)); break;
                        }
                        case Kind.Ripples:
                        {
                            float h = 0; foreach (var d in drops) { float dx = Mathf.Repeat(u - d.c.x + 0.5f, 1) - 0.5f, dy = Mathf.Repeat(v - d.c.y + 0.5f, 1) - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy); float ph = Mathf.Repeat(t * loops + d.ph, 1); float front = r - ph * Mathf.Lerp(0.15f, 0.95f, s.p3); h += Mathf.Sin(front * per * 12 * E(s.p2, 2.4f)) * Mathf.Exp(-front * front * 120) * (1 - ph); }
                            val = Mathf.Clamp01(0.5f + h * 0.5f * (0.5f + s.wild)); break;
                        }
                        case Kind.Lava:
                        {
                            var c = FxOps.Cells(u, v, Mathf.Clamp(Mathf.RoundToInt(per / E(s.p2, 2.4f)), 1, 24), s.seed); float crack = Mathf.Clamp01(1 - (c.d2 - c.d1) * Mathf.Lerp(10, 3, s.wild) / E(s.p1, 2.4f)); float heat = n2.Fbm(u + cx * 0.08f, v + cy * 0.08f) * 0.6f + n3.Fbm(u - cx * 0.05f, v - cy * 0.05f) * 0.4f;
                            val = Mathf.Clamp01(crack * (0.5f + heat * 0.8f) + Mathf.Pow(heat, 3) * s.p3); break;
                        }
                        case Kind.Sparks:
                        {
                            float best = 0; foreach (var dt in dots) { float dx = u - dt.p.x, dy = v - dt.p.y; if (dx > dt.r || dx < -dt.r || dy > dt.r || dy < -dt.r) continue; float d = Mathf.Sqrt(dx * dx + dy * dy); best = Mathf.Max(best, Mathf.Clamp01(1 - d / dt.r) * dt.w); }
                            val = best; alpha = Mathf.Clamp01(best * 2); break;
                        }
                        case Kind.Magic:
                        {
                            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2, a = Mathf.Atan2(dy, dx) / tau + 0.5f; float sw = n1.Fbm(Mathf.Repeat(a + r * 0.7f * s.p2 * (0.5f + s.wild) + t * loops, 1), Mathf.Repeat(r * 0.5f - t * loops, 1)); float rs = Mathf.Lerp(0.2f, 0.9f, s.p1);
                            float ring = Mathf.Clamp01(1 - Mathf.Abs(r - rs) * 3) + Mathf.Clamp01(1 - r / Mathf.Max(0.2f, rs) * 0.9f) * s.p3; val = Mathf.Clamp01(ring * (0.3f + sw * 1.2f)); alpha = Mathf.Clamp01(val * 1.5f) * Mathf.Clamp01((1 - r) * 4); break;
                        }
                        case Kind.Scroll: case Kind.Warp: case Kind.Pulse:
                        {
                            fromRamp = false; if (source == null) { col = Color.magenta; break; } float su = u, sv = v;
                            if (s.kind == Kind.Scroll) { float a = s.dirAngle * Mathf.Deg2Rad; su = u - Mathf.Round(Mathf.Cos(a) * loops) * t; sv = v - Mathf.Round(Mathf.Sin(a) * loops) * t; }
                            else if (s.kind == Kind.Warp) { var wn = s.p1 < 0.33f ? n2 : s.p1 > 0.66f ? new TextureOps.TileNoise(s.seed, Mathf.Max(1, per / 2)) : n1; float wa = s.wild * 0.15f * E(s.p1, 1.5f); su = u + (wn.Fbm(u + cx * 0.1f, v + cy * 0.1f) - 0.5f) * wa; sv = v + (wn.Fbm(u - cy * 0.1f + 0.31f, v + cx * 0.1f + 0.17f) - 0.5f) * wa; }
                            col = source.Sample(Mathf.Repeat(su, 1) * source.w, Mathf.Repeat(sv, 1) * source.h); if (s.kind == Kind.Pulse) { float k = 1 + Mathf.Sin(tau * t * loops) * 0.5f * s.wild; col = new Color(col.r * k, col.g * k, col.b * k, col.a); }
                            break;
                        }
                    }
                    if (fromRamp) { col = FxOps.Ramp(s.cols, s.at, Mathf.Clamp01(val)); col.a = Mathf.Clamp01(alpha); }
                    o.c[y * size + x] = col;
                }
            });
            if (s.dither) { var d = new DitherStep { method = 1, levels = Mathf.Max(2, s.levels), strength = 1 }; var keepA = o.c.Select(c => c.a).ToArray(); o = d.Run(o, TextureRun.Final); for (int i2 = 0; i2 < o.c.Length; i2++) o.c[i2].a = keepA[i2] > 0.5f ? 1 : 0; }
            return o;
        }
        /// <summary>Caustic lines: the edges between cells whose points circle round as time goes once round (so it loops).</summary>
        static float Caustic(float u, float v, int n, int seed, float t, float sharp)
        {
            float x = Mathf.Repeat(u, 1) * n, y = Mathf.Repeat(v, 1) * n; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float d1 = 9, d2 = 9;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j, wx = ((gx % n) + n) % n, wy = ((gy % n) + n) % n; float ph = FxOps.Rand(wx, wy, seed + 3) * Mathf.PI * 2, rr = 0.25f + FxOps.Rand(wx, wy, seed + 9) * 0.15f;
                    float px = gx + 0.5f + (FxOps.Rand(wx, wy, seed) - 0.5f) * 0.4f + Mathf.Cos(Mathf.PI * 2 * t + ph) * rr, py = gy + 0.5f + (FxOps.Rand(wx, wy, seed + 7) - 0.5f) * 0.4f + Mathf.Sin(Mathf.PI * 2 * t + ph) * rr;
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y)); if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
            return Mathf.Clamp01(1 - (d2 - d1) * sharp);
        }

        /// <summary>Lightning: jagged bolts (and forks) drawn as lines with a bright core and a soft glow. The big shape jumps every
        /// few frames, the small zig-zags every frame, like real arcs. Frame n is frame 0 again, and it wraps at every edge.</summary>
        static Pixels Lightning(Settings s, int i, int n, int size)
        {
            n = Mathf.Max(1, n); int fi = ((i % n) + n) % n, hold = Mathf.Max(1, 5 - Mathf.RoundToInt(s.speed)); var val = new float[size * size];
            int bolts = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, 6, s.p1)), 1, 6); float jag = Mathf.Lerp(0.25f, 1.1f, s.wild), px = size / 128f;
            float core = Mathf.Lerp(0.5f, 3f, s.p3) * px, glowR = Mathf.Lerp(2f, 12f, s.scale) * px;
            void Seg(Vector2 a, Vector2 b, float bright, float thin)
            {
                Vector2 A = a * size, B = b * size; float r = core * thin, G = glowR * thin, R = r + G * 2.2f; var ab = B - A; float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
                int x0 = Mathf.FloorToInt(Mathf.Min(A.x, B.x) - R), x1 = Mathf.CeilToInt(Mathf.Max(A.x, B.x) + R), y0 = Mathf.FloorToInt(Mathf.Min(A.y, B.y) - R), y1 = Mathf.CeilToInt(Mathf.Max(A.y, B.y) + R);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        var P = new Vector2(x + 0.5f, y + 0.5f); float h = Mathf.Clamp01(Vector2.Dot(P - A, ab) / len2); float d = (P - (A + ab * h)).magnitude;
                        float gd = d / Mathf.Max(0.3f, G); float v = Mathf.Clamp01(r + 0.7f - d) + 0.55f / (1 + gd * gd * 4) - 0.03f; v *= bright; if (v < 0.004f) continue;
                        int wx = ((x % size) + size) % size, wy = ((y % size) + size) % size; int q = wy * size + wx; val[q] = Mathf.Max(val[q], v); // the brightest line wins (no seams where segments meet)
                    }
            }
            Vector2[] Path(Vector2 a, Vector2 b, int depth, float amp, System.Random slow, System.Random fast)
            {
                var pts = new Vector2[(1 << depth) + 1]; pts[0] = a; pts[pts.Length - 1] = b; var nrm = new Vector2(-(b - a).y, (b - a).x).normalized; float len = (b - a).magnitude;
                int lv = 0;
                for (int step = pts.Length - 1; step > 1; step /= 2, lv++)
                    for (int k = 0; k + step < pts.Length; k += step)
                    {
                        var rr = lv < 2 ? slow : fast; float off = ((float)rr.NextDouble() - 0.5f) * amp * len / (1 << lv) * (lv < 2 ? 1f : 1.3f);
                        pts[k + step / 2] = (pts[k] + pts[k + step]) * 0.5f + nrm * off;
                    }
                return pts;
            }
            for (int b = 0; b < bolts; b++)
            {
                var fixedR = new System.Random(s.seed * 131 + b * 977); var slow = new System.Random(s.seed * 31 + b * 7717 + (fi / hold) * 92821); var fast = new System.Random(s.seed * 17 + b * 3371 + fi * 15485863);
                float y0 = (b + 0.5f) / bolts + ((float)fixedR.NextDouble() - 0.5f) * 0.5f / bolts; float bright = 0.65f + (float)fast.NextDouble() * 0.35f; if (fast.NextDouble() < 0.12) bright *= 0.35f; // now and then a bolt dims
                var pts = Path(new Vector2(0, y0), new Vector2(1, y0), 6, jag * 0.6f, slow, fast); for (int k = 0; k + 1 < pts.Length; k++) Seg(pts[k], pts[k + 1], bright, 1);
                int forks = Mathf.RoundToInt(s.p2 * 7 * (0.6f + (float)slow.NextDouble() * 0.8f));
                for (int f = 0; f < forks; f++)
                {
                    var from = pts[slow.Next(4, pts.Length - 4)]; float ang = (slow.NextDouble() < 0.5 ? 1 : -1) * Mathf.Lerp(20, 65, (float)slow.NextDouble()) * Mathf.Deg2Rad + (slow.NextDouble() < 0.5 ? 0 : Mathf.PI); float l = Mathf.Lerp(0.08f, 0.3f, (float)slow.NextDouble());
                    var to = from + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * l; var fp = Path(from, to, 4, jag * 0.7f, slow, fast); for (int k = 0; k + 1 < fp.Length; k++) Seg(fp[k], fp[k + 1], bright * Mathf.Lerp(0.85f, 0.4f, k / (float)fp.Length), 0.6f);
                }
            }
            var o = new Pixels(size, size, true); for (int q = 0; q < val.Length; q++) { var c = FxOps.Ramp(s.cols, s.at, Mathf.Clamp01(val[q])); c.a = Mathf.Clamp01(val[q] * 1.6f); o.c[q] = c; }
            if (s.dither) { var d = new DitherStep { method = 1, levels = Mathf.Max(2, s.levels), strength = 1 }; var keepA = o.c.Select(c => c.a).ToArray(); o = d.Run(o, TextureRun.Final); for (int i2 = 0; i2 < o.c.Length; i2++) o.c[i2].a = keepA[i2] > 0.5f ? 1 : 0; }
            return o;
        }
        /// <summary>All the frames in one sprite sheet (the first frame top-left, left to right, then down).</summary>
        public static Pixels Sheet(Settings s, int size, Pixels source, Action<float> progress = null)
        {
            var (cols, rows) = Grid(s.frames); var o = new Pixels(cols * size, rows * size, true);
            for (int i = 0; i < s.frames; i++) { progress?.Invoke(i / (float)s.frames); var f = Frame(s, i, s.frames, size, source); int ox = (i % cols) * size, oy = (rows - 1 - i / cols) * size; for (int y = 0; y < size; y++) Array.Copy(f.c, y * size, o.c, (oy + y) * o.w + ox, size); }
            return o;
        }

        // ---------------------------------------------------------------- flow maps

        /// <summary>A flow map you paint: which way (and how fast) things drift at each spot. Saved red-green, grey is still.</summary>
        [Serializable]
        public class Flow
        {
            public int res = 64; public Vector2[] d;
            public void Clear() { d = new Vector2[res * res]; }
            public void Ensure() { if (d == null || d.Length != res * res) Clear(); }
            public void Paint(Vector2 uv, Vector2 dir, float radius, float strength)
            {
                Ensure(); int r = Mathf.CeilToInt(radius * res);
                for (int j = -r; j <= r; j++) for (int i = -r; i <= r; i++)
                    {
                        int x = Mathf.FloorToInt(uv.x * res) + i, y = Mathf.FloorToInt(uv.y * res) + j; float dist = new Vector2(i, j).magnitude / Mathf.Max(1, r); if (dist > 1) continue; x = ((x % res) + res) % res; y = ((y % res) + res) % res;
                        int q = y * res + x; d[q] = Vector2.ClampMagnitude(Vector2.Lerp(d[q], dir, strength * (1 - dist * dist)), 1);
                    }
            }
            public void Fill(Func<Vector2, Vector2> f) { Ensure(); for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) d[y * res + x] = Vector2.ClampMagnitude(f(new Vector2((x + 0.5f) / res, (y + 0.5f) / res)), 1); }
            public void Smooth() { Ensure(); var n = new Vector2[d.Length]; for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) { Vector2 s = Vector2.zero; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) s += d[((y + j + res) % res) * res + (x + i + res) % res]; n[y * res + x] = s / 9; } d = n; }
            public Pixels ToPixels(int size)
            {
                Ensure(); var o = new Pixels(size, size, false);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { float fx = (x + 0.5f) / size * res - 0.5f, fy = (y + 0.5f) / size * res - 0.5f; int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy); float ax = fx - x0, ay = fy - y0; Vector2 D(int i, int j) => d[(((y0 + j) % res + res) % res) * res + ((x0 + i) % res + res) % res]; var v = Vector2.Lerp(Vector2.Lerp(D(0, 0), D(1, 0), ax), Vector2.Lerp(D(0, 1), D(1, 1), ax), ay); o.c[y * size + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, 0, 1); }
                return o;
            }
        }
    }
}
