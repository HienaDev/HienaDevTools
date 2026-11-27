using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    // ==================================================================== ageing: rust, growth, cracks, scratches, drips, stains

    [Serializable]
    public class RustStep : TextureStep
    {
        public float amount = 0.5f, edges = 0.6f, streaks = 0.5f, patch = 0.5f; public Color rust = FxOps.Hex("8a3f17"), dark = FxOps.Hex("3d1e0e"); public int seed = 1;
        public override string Title => "Rust";
        public override string Blurb => "Grows from edges and cracks";
        public override string Explain => "Rust that starts on edges and in cracks, spreads in patches, and runs down in orange streaks.";
        public override string Summary => Pct(amount);
        public override Color Tint => new Color(0.85f, 0.45f, 0.2f);
        public override bool Settings() { bool ch = Slider(ref amount, "Amount", "", 0, 1); ch |= Slider(ref edges, "On edges and cracks", "", 0, 1); ch |= Slider(ref patch, "Patch size", "", 0.1f, 1); ch |= Slider(ref streaks, "Streaks down", "", 0, 1); ch |= FxOps.Colour(ref rust, "Rust"); ch |= FxOps.Colour(ref dark, "Dark rust"); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var h = TextureOpsMore.Height(p, 0.4f); var cav = TextureOpsMore.Cavity(h, p.w, p.h); var nz = new TextureOps.TileNoise(seed * 5 + 3, Mathf.Clamp(Mathf.RoundToInt(3 + (1 - patch) * 12), 2, 16)); var fine = new TextureOps.TileNoise(seed * 5 + 11, 40);
            var m = new float[p.c.Length];
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; float n = nz.Fbm(u, v) + (fine.Fbm(u, v) - 0.5f) * 0.4f; float e = Mathf.Abs(cav[q]) * 8 * edges; m[q] = Mathf.Clamp01((n + e - 1.05f + amount) * 4); } });
            // streaks: rust carried down from where there is some
            if (streaks > 0) { int len = Mathf.RoundToInt(p.h * 0.25f * streaks); var s = new float[m.Length]; TextureOps.Rows(p.w, x => { float carry = 0; for (int k = 0; k < p.h + len; k++) { int y = ((p.h - 1 - k) % p.h + p.h) % p.h; int q = y * p.w + x; carry = Mathf.Max(carry * (1 - 1f / Mathf.Max(1, len)), m[q] * (0.6f + FxOps.Rand(x, 0, seed) * 0.4f)); if (k >= len) s[q] = Mathf.Max(s[q], carry * 0.7f); else s[q] = Mathf.Max(s[q], carry * 0.7f); } }); for (int i = 0; i < m.Length; i++) m[i] = Mathf.Max(m[i], s[i] * streaks); }
            var o = new Pixels(p.w, p.h, p.srgb); TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; var col = Color.Lerp(rust, dark, fine.Fbm(v, u)) * (0.85f + FxOps.Lum(p.c[q]) * 0.3f); o.c[q] = FxOps.Over(p.c[q], col, m[q]); } });
            return o;
        }
    }

    [Serializable]
    public class GrowthStep : TextureStep
    {
        public int kind; public float amount = 0.5f, soft = 0.5f, patchy = 0.5f; public Color colour = FxOps.Hex("4a5a24"); public int seed = 1;
        static readonly string[] Kinds = { "Moss", "Grime", "Dust", "Snow" };
        static readonly string[] Cols = { "4a5a24", "2e2a20", "a89f8c", "f4f6fa" };
        public override string Title => "Moss, grime, dust, snow";
        public override string Blurb => "Grows in the low parts or settles on top";
        public override string Explain => "Moss and grime collect in the low parts and cracks; dust and snow settle on the upper side of every bump (the top of the picture is up).";
        public override string Summary => Kinds[kind].ToLower() + ", " + Pct(amount);
        public override Color Tint => new Color(0.5f, 0.75f, 0.4f);
        public override bool Settings() { int k = kind; bool ch = Choice(ref k, Kinds); if (ch) { kind = k; colour = FxOps.Hex(Cols[k]); } ch |= Slider(ref amount, "Amount", "", 0, 1); ch |= Slider(ref patchy, "Patchy", "", 0, 1); ch |= Slider(ref soft, "Soft edges", "", 0, 1); ch |= FxOps.Colour(ref colour, "Colour"); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var h = TextureOpsMore.Height(p, 0.6f); var cav = TextureOpsMore.Cavity(h, p.w, p.h); var nz = new TextureOps.TileNoise(seed * 3 + kind, 6); var fine = new TextureOps.TileNoise(seed * 3 + 50, 48); float k = Mathf.Max(p.w, p.h) / 64f; var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; float where;
                    if (kind <= 1) where = -cav[q] * 10 + (1 - h[q]) * 0.6f; else { var n = FxOps.Normal(h, p.w, p.h, x, y, k); where = n.y * 1.5f + h[q] * 0.4f; }
                    float patch = (nz.Fbm(u, v) - 0.5f) * patchy * 1.5f + (fine.Fbm(u, v) - 0.5f) * 0.3f; float t = (where + patch + amount * 1.6f - 0.9f) / Mathf.Max(0.05f, soft * 0.6f + 0.05f);
                    var col = colour * (0.85f + fine.Fbm(v, u) * 0.3f); if (kind == 1) col = Color.Lerp(p.c[q], p.c[q] * colour * 1.6f, 0.7f); o.c[q] = FxOps.Over(p.c[q], col, Mathf.Clamp01(t) * (kind == 1 ? 0.85f : 1));
                }
            });
            return o;
        }
    }

    [Serializable]
    public class CracksStep : TextureStep
    {
        public int count = 8; public float length = 0.4f, branches = 0.5f, width = 1.5f, depth = 0.6f; public Color colour = FxOps.Hex("1c1a17"); public int seed = 1;
        public override string Title => "Cracks";
        public override string Blurb => "Thin branching cracks";
        public override string Explain => "Draws random cracks that wander and branch, darker in the middle, with a lighter chipped rim. They loop across the edges.";
        public override string Summary => count + " cracks";
        public override Color Tint => new Color(0.6f, 0.55f, 0.5f);
        public override bool Settings() { bool ch = IntSlider(ref count, "How many", "", 1, 40); ch |= Slider(ref length, "Length", "", 0.05f, 1); ch |= Slider(ref branches, "Branches", "", 0, 1); ch |= Slider(ref width, "Width", "Pixels", 0.5f, 6); ch |= Slider(ref depth, "Depth", "How dark", 0, 1); ch |= FxOps.Colour(ref colour, "Colour"); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var m = new float[p.c.Length]; var rnd = new System.Random(seed * 101); float side = Mathf.Max(p.w, p.h), wd = FxOps.Px(width, run);
            void Crack(Vector2 at, float ang, float len, float wid, int depthLeft)
            {
                float step = side * 0.012f; int n = Mathf.Max(2, Mathf.RoundToInt(len * side / step));
                for (int i = 0; i < n; i++) { ang += ((float)rnd.NextDouble() - 0.5f) * 0.9f; var nx = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * step; FxOps.Stroke(m, p.w, p.h, at, nx, wid * (1 - 0.6f * i / n)); at = nx; if (depthLeft > 0 && rnd.NextDouble() < branches * 0.08f) Crack(at, ang + (rnd.NextDouble() < 0.5 ? 1 : -1) * (0.5f + (float)rnd.NextDouble()), len * 0.45f * (1 - i / (float)n), wid * 0.6f, depthLeft - 1); }
            }
            for (int c = 0; c < count; c++) Crack(new Vector2((float)rnd.NextDouble() * p.w, (float)rnd.NextDouble() * p.h), (float)(rnd.NextDouble() * Math.PI * 2), length * (0.5f + (float)rnd.NextDouble() * 0.5f), wd, 2);
            var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; float rim = 0; if (m[q] < 0.5f) { for (int k = 0; k < 4; k++) { int dx = k == 0 ? 2 : k == 1 ? -2 : 0, dy = k == 2 ? 2 : k == 3 ? -2 : 0; int i2 = ((x + dx) % p.w + p.w) % p.w, j2 = ((y + dy) % p.h + p.h) % p.h; rim = Mathf.Max(rim, m[j2 * p.w + i2]); } } var c = FxOps.Over(p.c[q], colour, m[q] * depth); c = FxOps.Over(c, c * 1.25f, rim * 0.35f * depth); o.c[q] = c; } });
            return o;
        }
    }

    [Serializable]
    public class ScratchesStep : TextureStep
    {
        public int count = 60, dir; public float length = 0.15f, angle = 20, bright = 0.5f, width = 1f; public bool light = true; public int seed = 1;
        public override string Title => "Scratches";
        public override string Blurb => "Fine random lines";
        public override string Explain => "Fine scratches: any way, mostly one way (brushed metal, a worn floor) or criss-crossed. Light ones show bare material; dark ones are grooves.";
        public override string Summary => count + (light ? " light" : " dark") + " scratches";
        public override Color Tint => new Color(0.75f, 0.78f, 0.85f);
        public override bool Settings() { bool ch = IntSlider(ref count, "How many", "", 1, 400); ch |= Slider(ref length, "Length", "", 0.01f, 0.6f); ch |= Choice(ref dir, "Any way", "One way", "Criss-cross"); if (dir > 0) ch |= Slider(ref angle, "Angle", "", 0, 180); ch |= Slider(ref width, "Width", "Pixels", 0.5f, 4); ch |= Toggle(ref light, "Light scratches", "Off: dark grooves"); ch |= Slider(ref bright, "Strength", "", 0, 1); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var m = new float[p.c.Length]; var rnd = new System.Random(seed * 17); float side = Mathf.Max(p.w, p.h), wd = FxOps.Px(width, run);
            for (int i = 0; i < count; i++)
            {
                float a = dir == 0 ? (float)rnd.NextDouble() * Mathf.PI : (angle + (dir == 2 && rnd.NextDouble() < 0.5 ? 90 : 0) + ((float)rnd.NextDouble() - 0.5f) * 12) * Mathf.Deg2Rad; float len = length * side * (0.3f + (float)rnd.NextDouble() * 0.7f); var s = new Vector2((float)rnd.NextDouble() * p.w, (float)rnd.NextDouble() * p.h); var bend = ((float)rnd.NextDouble() - 0.5f) * 0.3f; var cur = s; int seg = 6;
                for (int k = 0; k < seg; k++) { var nx = cur + new Vector2(Mathf.Cos(a + bend * k / seg), Mathf.Sin(a + bend * k / seg)) * len / seg; FxOps.Stroke(m, p.w, p.h, cur, nx, wd, 0.4f + (float)rnd.NextDouble() * 0.6f); cur = nx; }
            }
            var o = new Pixels(p.w, p.h, p.srgb); for (int i = 0; i < m.Length; i++) { var c = p.c[i]; var t = light ? Color.Lerp(c, Color.white, 0.55f) : c * 0.45f; o.c[i] = FxOps.Over(c, t, m[i] * bright); }
            return o;
        }
    }

    [Serializable]
    public class DripsStep : TextureStep
    {
        public int from, count = 14; public float length = 0.4f, width = 6, strength = 0.6f; public Color colour = FxOps.Hex("3a2c1c"); public int seed = 1;
        static readonly (string n, string c)[] Ready = { ("Dirt", "3a2c1c"), ("Rust", "8a3f17"), ("Water", "1c2630"), ("Blood", "5a0a0a"), ("Paint", "e8e2d4") };
        public override string Title => "Drips and leaks";
        public override string Blurb => "Streaks running down";
        public override string Explain => "Streaks that run down from the top edge, from random spots, or from the dark parts (cracks, holes): water stains, rust, dirt, blood or paint.";
        public override string Summary => count + " drips";
        public override Color Tint => new Color(0.5f, 0.55f, 0.65f);
        public override bool Settings() { bool ch = Choice(ref from, "Top edge", "Random spots", "Dark parts"); ch |= IntSlider(ref count, "How many", "", 1, 80); ch |= Slider(ref length, "Length", "", 0.05f, 1); ch |= Slider(ref width, "Width", "Pixels", 1, 30); ch |= Slider(ref strength, "Strength", "", 0, 1); GUILayout.BeginHorizontal(); foreach (var r in Ready) if (GUILayout.Button(r.n, EditorStyles.miniButton)) { colour = FxOps.Hex(r.c); ch = true; } GUILayout.EndHorizontal(); ch |= FxOps.Colour(ref colour, "Colour"); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var m = new float[p.c.Length]; var rnd = new System.Random(seed * 29); float wd = FxOps.Px(width, run); float[] h = from == 2 ? TextureOpsMore.Height(p, 0.5f) : null;
            for (int i = 0; i < count; i++)
            {
                float x = (float)rnd.NextDouble() * p.w, y = from == 0 ? p.h - 1 : (float)rnd.NextDouble() * p.h;
                if (from == 2) { float bestH = 9; for (int k = 0; k < 12; k++) { int tx = rnd.Next(p.w), ty = rnd.Next(p.h); if (h[ty * p.w + tx] < bestH) { bestH = h[ty * p.w + tx]; x = tx; y = ty; } } }
                float len = length * p.h * (0.4f + (float)rnd.NextDouble() * 0.6f); float w0 = wd * (0.5f + (float)rnd.NextDouble()); int n = Mathf.Max(4, Mathf.RoundToInt(len / Mathf.Max(1, w0 * 0.5f)));
                var cur = new Vector2(x, y); for (int k = 0; k < n; k++) { var nx = cur + new Vector2(((float)rnd.NextDouble() - 0.5f) * w0 * 0.3f, -len / n); float t = k / (float)n; FxOps.Stroke(m, p.w, p.h, cur, nx, w0 * (1 - t * 0.7f), (1 - t) * (0.6f + 0.4f * (float)rnd.NextDouble())); cur = nx; }
            }
            var o = new Pixels(p.w, p.h, p.srgb); for (int i = 0; i < m.Length; i++) o.c[i] = FxOps.Over(p.c[i], p.c[i] * colour * 1.8f + colour * 0.2f, m[i] * strength); return o;
        }
    }

    [Serializable]
    public class StainsStep : TextureStep
    {
        public int kind; public int count = 6; public float size = 0.15f, strength = 0.6f; public int seed = 1;
        public override string Title => "Burns, rings and wet";
        public override string Blurb => "Scorch marks, water rings, wet patches";
        public override string Explain => "Burn and scorch marks (black centre, brown edge), water rings (a darker ring where the water dried), or wet patches (darker and a bit shinier looking).";
        public override string Summary => new[] { "burns", "rings", "wet" }[kind] + ", " + count;
        public override Color Tint => new Color(0.55f, 0.45f, 0.4f);
        public override bool Settings() { bool ch = Choice(ref kind, "Burns", "Water rings", "Wet patches"); ch |= IntSlider(ref count, "How many", "", 1, 30); ch |= Slider(ref size, "Size", "", 0.02f, 0.6f); ch |= Slider(ref strength, "Strength", "", 0, 1); ch |= IntSlider(ref seed, "Seed", "", 1, 99); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var rnd = new System.Random(seed * 41); var spots = Enumerable.Range(0, count).Select(_ => (c: new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble()), r: size * (0.5f + (float)rnd.NextDouble() * 0.7f))).ToArray(); var nz = new TextureOps.TileNoise(seed * 9 + 2, 12); var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; var c = p.c[q]; float wob = (nz.Fbm(u, v) - 0.5f) * 0.6f;
                    foreach (var s in spots)
                    {
                        float dx = Mathf.Repeat(u - s.c.x + 0.5f, 1) - 0.5f, dy = Mathf.Repeat(v - s.c.y + 0.5f, 1) - 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(0.01f, s.r) + wob * 0.5f; if (d > 1.3f) continue;
                        if (kind == 0) { float core = Mathf.Clamp01(1 - d); var burn = Color.Lerp(c * FxOps.Hex("5a3a1e") * 1.6f, FxOps.Hex("100c08"), Mathf.Clamp01(core * 1.5f)); c = FxOps.Over(c, burn, Mathf.Clamp01((1.15f - d) * 2) * strength); }
                        else if (kind == 1) { float ring = Mathf.Clamp01(1 - Mathf.Abs(d - 0.9f) * 12) + Mathf.Clamp01(1 - d) * 0.15f; c = FxOps.Over(c, c * 0.62f, ring * strength); }
                        else { float wet = Mathf.Clamp01((1 - d) * 3); c = FxOps.Over(c, new Color(c.r * c.r * 0.9f, c.g * c.g * 0.9f, c.b * c.b, c.a) * 1.05f, wet * strength); }
                    }
                    o.c[q] = c;
                }
            });
            return o;
        }
    }

    // ==================================================================== new starting points

    [Serializable]
    public class NoiseStep : TextureStep
    {
        public int kind; public float scale = 0.4f, detail = 0.6f, contrast = 0.5f, mix = 1; public Color a = FxOps.Hex("1e1e24"), b = FxOps.Hex("d8d4cc"); public int seed = 1;
        static readonly string[] Kinds = { "Clouds", "Cells", "Cell edges", "Ridges" };
        public override string Title => "Noise";
        public override string Blurb => "Clouds, cells, ridges (loops)";
        public override string Explain => "Makes a new picture from noise, or lays it over the one you have: soft clouds (Perlin), cells (Worley), the lines between cells (cracked earth, scales) or sharp ridges (mountains, lightning). It always loops.";
        public override string Summary => Kinds[kind].ToLower();
        public override Color Tint => new Color(0.6f, 0.6f, 0.7f);
        public override TextureStep Demo() => new NoiseStep { kind = 1 };
        public override bool Settings() { bool ch = Choice(ref kind, Kinds); ch |= Slider(ref scale, "Size", "Bigger shapes to the right", 0, 1); ch |= Slider(ref detail, "Detail", "", 0, 1); ch |= Slider(ref contrast, "Contrast", "", 0, 1); ch |= FxOps.Colour(ref a, "Dark"); ch |= FxOps.Colour(ref b, "Bright"); ch |= Slider(ref mix, "Over the picture", "1: only the noise; less: laid over what you have", 0, 1); ch |= IntSlider(ref seed, "Seed", "", 1, 999); return ch; }
        [NonSerialized] TextureOps.TileNoise[] m_N;
        public float Value(int kind, float u, float v, float scale, float detail, int seed)
        {
            int per = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(24, 2, scale)), 1, 64);
            if (kind == 0) { float s = 0, amp = 1, tot = 0; for (int o = 0; o < 1 + Mathf.RoundToInt(detail * 5); o++) { s += m_N[o].Fbm(u, v) * amp; tot += amp; amp *= 0.5f; } return s / tot; }
            if (kind == 3) { float s = 0, amp = 1, tot = 0; for (int o = 0; o < 1 + Mathf.RoundToInt(detail * 4); o++) { float n = m_N[o].Fbm(u, v); s += (1 - Mathf.Abs(n * 2 - 1)) * amp; tot += amp; amp *= 0.5f; } return Mathf.Pow(s / tot, 2.5f); }
            var c = FxOps.Cells(u, v, per, seed); float f = kind == 1 ? Mathf.Clamp01(c.d1 / 0.8f) : Mathf.Clamp01((c.d2 - c.d1) * 3); if (detail > 0) { var c2 = FxOps.Cells(u, v, per * 3, seed + 5); f = Mathf.Lerp(f, f * Mathf.Clamp01(kind == 1 ? c2.d1 / 0.8f : (c2.d2 - c2.d1) * 3 + 0.4f), detail * 0.5f); } return kind == 2 ? 1 - f : f;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int per0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(24, 2, scale)), 1, 64); m_N = Enumerable.Range(0, 6).Select(o2 => new TextureOps.TileNoise(seed + o2 * (kind == 3 ? 17 : 31), per0 << o2)).ToArray();
            var o = new Pixels(p.w, p.h, p.srgb); float k = Mathf.Lerp(0.6f, 3f, contrast);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int q = y * p.w + x; float t = Value(kind, (x + 0.5f) / p.w, (y + 0.5f) / p.h, scale, detail, seed); t = Mathf.Clamp01((t - 0.5f) * k + 0.5f); var c = Color.Lerp(a, b, t); c.a = 1; o.c[q] = mix >= 1 ? c : Color.Lerp(p.c[q], new Color(p.c[q].r * c.r * 2, p.c[q].g * c.g * 2, p.c[q].b * c.b * 2, p.c[q].a), mix); } });
            return o;
        }
    }

    [Serializable]
    public class SurfaceStep : TextureStep
    {
        public int kind; public float scale = 0.5f, rough = 0.5f, mix = 1; public Color a = FxOps.Hex("6b4423"), b = FxOps.Hex("c08a52"); public int seed = 1;
        static readonly string[] Kinds = { "Wood", "Marble", "Rough stone" };
        static readonly string[][] Cols = { new[] { "6b4423", "c08a52" }, new[] { "e9e6e1", "5c5f66" }, new[] { "56534d", "9b968c" } };
        public override string Title => "Wood, marble, stone";
        public override string Blurb => "A new surface from scratch";
        public override string Explain => "Makes wood grain (rings and lines along it), marble (soft veins) or rough stone (lumpy, speckled). It loops, and can be laid over the picture you have.";
        public override string Summary => Kinds[kind].ToLower();
        public override Color Tint => new Color(0.75f, 0.6f, 0.45f);
        public override TextureStep Demo() => new SurfaceStep { kind = 1, a = FxOps.Hex(Cols[1][0]), b = FxOps.Hex(Cols[1][1]) };
        public override bool Settings() { int k = kind; bool ch = Choice(ref k, Kinds); if (ch) { kind = k; a = FxOps.Hex(Cols[k][0]); b = FxOps.Hex(Cols[k][1]); } ch |= Slider(ref scale, "Size", "", 0, 1); ch |= Slider(ref rough, kind == 0 ? "Wavy grain" : kind == 1 ? "Vein detail" : "Rough", "", 0, 1); ch |= FxOps.Colour(ref a, kind == 1 ? "Stone" : "Dark"); ch |= FxOps.Colour(ref b, kind == 1 ? "Veins" : "Light"); ch |= Slider(ref mix, "Over the picture", "", 0, 1); ch |= IntSlider(ref seed, "Seed", "", 1, 999); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int per = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(10, 2, scale)), 1, 32); var n1 = new TextureOps.TileNoise(seed, per); var n2 = new TextureOps.TileNoise(seed + 9, per * 4); var n3 = new TextureOps.TileNoise(seed + 21, per * 16); var o = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int q = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; Color c;
                    if (kind == 0) { float w = n1.Fbm(u, v) * 3 * (0.3f + rough) + u * per * 2; float ring = Mathf.Abs(Mathf.Sin(w * Mathf.PI)); ring = Mathf.Pow(ring, 0.6f); float grain = n3.Fbm(u * 0.3f, v * 4); c = Color.Lerp(a, b, Mathf.Clamp01(ring * 0.8f + grain * 0.3f - 0.05f)); }
                    else if (kind == 1) { float t = n1.Fbm(u, v) * 2 + n2.Fbm(u, v) * rough; float vein = 1 - Mathf.Abs(Mathf.Sin((u + v + t) * Mathf.PI * per * 0.5f)); vein = Mathf.Pow(vein, 8 - rough * 5); float cloud = n2.Fbm(v, u) * 0.15f; c = Color.Lerp(a * (0.95f + cloud), b, Mathf.Clamp01(vein)); }
                    else { float lump = n1.Fbm(u, v) * 0.6f + n2.Fbm(u, v) * 0.3f + n3.Fbm(u, v) * 0.25f * (0.5f + rough); float spk = FxOps.Rand(x, y, seed) > 0.985f - rough * 0.01f ? 0.25f : 0; c = Color.Lerp(a, b, Mathf.Clamp01(lump - 0.25f + spk)); }
                    c.a = 1; o.c[q] = mix >= 1 ? c : Color.Lerp(p.c[q], new Color(p.c[q].r * c.r * 2, p.c[q].g * c.g * 2, p.c[q].b * c.b * 2, p.c[q].a), mix);
                }
            });
            return o;
        }
    }
}
