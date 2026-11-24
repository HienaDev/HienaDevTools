using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A step you paint on in the picture.</summary>
    public interface IPaintable
    {
        List<TextureHeal.Stroke> Strokes { get; }
        float Brush { get; set; }
        bool Painting { get; }
        string PaintLabel { get; }
        Color PaintColour { get; }
    }

    /// <summary>Hides a repeat without a bigger texture: finds the spots that stand out (they're what the eye recognises
    /// again and again) and fills them with ordinary pieces of the same texture.</summary>
    public static class TextureCalm
    {
        /// <summary>How much each spot stands out, on a small copy (0 = ordinary).</summary>
        public static float[] Standing(Pixels small)
        {
            int n = small.c.Length; var lab = new Pixels(small.w, small.h, false);
            for (int i = 0; i < n; i++) { var v = TexturePalette.Lab(small.c[i]); lab.c[i] = new Color(v.x, v.y * 1.6f + 0.5f, v.z * 1.6f + 0.5f, 1); }
            // medium-sized things compared with everything around them
            float s = 1024f / Mathf.Max(small.w, small.h);
            var fine = TextureOpsMore.Blur(lab, 3 * s); var wide = TextureOpsMore.Blur(lab, 22 * s); Color mean = Color.clear; foreach (var c in lab.c) mean += c; mean /= n;
            var o = new float[n];
            for (int i = 0; i < n; i++) { var a = fine.c[i] - wide.c[i]; var b = fine.c[i] - mean; o[i] = Mathf.Sqrt(a.r * a.r + a.g * a.g + a.b * a.b) * 0.5f + Mathf.Sqrt(b.r * b.r + b.g * b.g + b.b * b.b) * 0.5f; }
            return o;
        }

        public static bool[] Spots(Pixels p, float amount, out int w, out int h)
        {
            float k = Mathf.Min(1f, 128f / Mathf.Max(p.w, p.h)); w = Mathf.Max(8, Mathf.RoundToInt(p.w * k)); h = Mathf.Max(8, Mathf.RoundToInt(p.h * k));
            var f = Standing(p.Resized(w, h)); var sorted = (float[])f.Clone(); Array.Sort(sorted);
            float cut = sorted[Mathf.Clamp((int)(sorted.Length * (1f - 0.3f * amount)), 0, sorted.Length - 1)], mid = sorted[sorted.Length / 2];
            var m = new bool[f.Length];
            for (int i = 0; i < f.Length; i++) m[i] = f[i] > cut && f[i] > mid * 1.25f;
            // a little bigger, so the whole thing goes and not just its middle
            for (int pass = 0; pass < 2; pass++) { var g = (bool[])m.Clone(); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (m[y * w + x]) { g[y * w + (x + 1) % w] = true; g[y * w + (x + w - 1) % w] = true; g[((y + 1) % h) * w + x] = true; g[((y + h - 1) % h) * w + x] = true; } m = g; }
            return m;
        }

        public static Pixels Calm(Pixels p, float amount)
        {
            if (amount <= 0.001f) return p;
            // a few rounds of not too many spots: marking a lot at once would leave nothing ordinary to fill them with
            int rounds = Mathf.Clamp(Mathf.CeilToInt(amount * 4), 1, 4); for (int i = 0; i < rounds; i++) p = Round(p, Mathf.Min(0.4f, amount));
            return p;
        }

        static Pixels Round(Pixels p, float amount)
        {
            var small = Spots(p, amount, out int w, out int h); var full = new bool[p.c.Length];
            for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) full[y * p.w + x] = small[Mathf.Min(h - 1, y * h / p.h) * w + Mathf.Min(w - 1, x * w / p.w)];
            return TextureHeal.HealMask(p, full);
        }
    }

    /// <summary>Puts a second texture on parts of this one.</summary>
    public static class TextureMix
    {
        public static Pixels Mix(Pixels a, Pixels b, float[] where, float soft, float shapes)
        {
            var r = new Pixels(a.w, a.h, a.srgb); float hard = Mathf.Lerp(14f, 1.2f, Mathf.Clamp01(soft)), mean = 0, sq = 0;
            // (what's dark or light compared with the rest of this texture)
            foreach (var c in a.c) { float l = Pixels.Lum(c); mean += l; sq += l * l; }
            mean /= a.c.Length; float spread = Mathf.Max(0.02f, Mathf.Sqrt(Mathf.Max(0, sq / a.c.Length - mean * mean)));
            TextureOps.Rows(a.h, y =>
            {
                for (int x = 0; x < a.w; x++)
                {
                    int i = y * a.w + x; float m = where[i]; var ca = a.c[i]; var cb = b.c[i];
                    // the edge follows the shapes: the second one settles in the dark, low parts first
                    float k = m <= 0.002f ? 0 : m >= 0.998f ? 1 : Mathf.Clamp01((m - 0.5f + Mathf.Clamp((mean - Pixels.Lum(ca)) / (2f * spread), -1f, 1f) * shapes * 0.5f * Mathf.Min(1f, 5 * m * (1 - m))) * hard + 0.5f);
                    var c = Color.Lerp(ca, cb, k); c.a = ca.a; r.c[i] = c;
                }
            });
            return r;
        }

        /// <summary>A painted area with soft edges (0..1).</summary>
        public static float[] Painted(List<TextureHeal.Stroke> strokes, int w, int h, float soft)
        {
            var m = TextureHeal.Mask(strokes, w, h); var p = new Pixels(w, h, false); for (int i = 0; i < m.Length; i++) { float v = m[i] ? 1 : 0; p.c[i] = new Color(v, v, v, 1); }
            p = TextureOpsMore.Blur(p, 10f + soft * 70f); /* (a wide edge, so it has room to follow the shapes) */ var o = new float[m.Length]; for (int i = 0; i < o.Length; i++) o[i] = p.c[i].r; return o;
        }

        public static float[] Patches(int w, int h, float amount, float size, int seed)
        {
            var n = new TextureOps.TileNoise(seed * 31 + 5, Mathf.Clamp(Mathf.RoundToInt(5f / Mathf.Max(0.15f, size)), 2, 40)); var o = new float[w * h];
            TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) o[y * w + x] = Mathf.Clamp01((n.Fbm((x + 0.5f) / w, (y + 0.5f) / h) - (0.78f - amount * 0.56f)) * 5f + 0.5f); });
            return o;
        }

        /// <summary>Low parts (cracks) or high parts.</summary>
        public static float[] Parts(Pixels p, float amount, bool high)
        {
            var hgt = TextureOpsMore.Height(p, 3f); var o = new float[hgt.Length]; float cut = high ? 1 - amount : amount;
            for (int i = 0; i < o.Length; i++) o[i] = Mathf.Clamp01(((high ? hgt[i] - cut : cut - hgt[i])) * 6f + 0.5f);
            return o;
        }
    }

    [Serializable]
    public class MixStep : TextureStep, IPaintable
    {
        public enum Where { Painted, Patches, Cracks, HighParts }
        public Texture2D other; public Where where = Where.Painted; public List<TextureHeal.Stroke> strokes = new List<TextureHeal.Stroke>();
        public float brush = 0.1f, amount = 0.45f, size = 1f, soft = 0.35f, shapes = 0.6f, match = 0f; public int scale = 1, seed = 1;
        [NonSerialized] public Pixels read;
        public override string Title => "Mix with another";
        public override string Blurb => "Moss on stone, sand on tiles...";
        public override string Explain => where == Where.Painted ? "Pick a second texture, then brush over the left picture where it should show. Right-drag takes it away. Use textures that loop." : "Pick a second texture; it's put on this one by itself. Use textures that loop.";
        public override string Summary => (other ? other.name : "pick a texture") + ", " + (where == Where.Painted ? "painted" : where == Where.Patches ? "patches" : where == Where.Cracks ? "in the cracks" : "on the high parts");
        public override Color Tint => new Color(0.55f, 0.8f, 0.4f);
        public List<TextureHeal.Stroke> Strokes => strokes;
        public float Brush { get => brush; set => brush = value; }
        public bool Painting => where == Where.Painted && other;
        public string PaintLabel => "BRUSH WHERE THE OTHER TEXTURE SHOULD SHOW";
        public Color PaintColour => new Color(0.4f, 0.9f, 0.4f, 0.28f);

        public override bool Settings()
        {
            bool ch = false;
            var t = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Second texture", "Drop one here"), other, typeof(Texture2D), false, GUILayout.Height(64)); if (t != other) { other = t; ch = true; }
            EditorGUILayout.LabelField("Where it shows", EditorStyles.miniBoldLabel);
            int w = (int)where; if (Choice(ref w, "Painted", "Patches", "Cracks", "High")) { where = (Where)w; ch = true; }
            if (where == Where.Painted)
            {
                Slider(ref brush, "Brush size", "The mouse wheel over the picture changes it too", 0.01f, 0.4f);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(strokes.Count == 0)) { if (GUILayout.Button("Remove last stroke")) { strokes.RemoveAt(strokes.Count - 1); ch = true; } if (GUILayout.Button("Clear")) { strokes.Clear(); ch = true; } }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                ch |= Slider(ref amount, "How much", "", 0, 1);
                if (where == Where.Patches) { ch |= Slider(ref size, "Patch size", "", 0.2f, 3f); if (GUILayout.Button("Shuffle")) { seed = UnityEngine.Random.Range(1, 99999); ch = true; } }
            }
            ch |= Slider(ref soft, "Soft edge", "", 0, 1); ch |= Slider(ref shapes, "Follow shapes", "The edge follows this texture's dark and light parts, so it settles into gaps", 0, 1);
            ch |= Slider(ref match, "Match colours", "Gives the second texture this one's overall colours", 0, 1);
            ch |= IntSlider(ref scale, "Repeat it", "How many times it fits across", 1, 6);
            return ch;
        }
        public override void Prepare() { read = other ? Pixels.ReadCached(other) : null; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (read == null) return p;
            int tw = Mathf.Max(2, p.w / scale), th = Mathf.Max(2, p.h / scale); var t = read.Resized(tw, th); if (match > 0) t = TextureVariations.MatchColours(t, p, match);
            var b = new Pixels(p.w, p.h, p.srgb); TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) b.c[y * p.w + x] = scale == 1 ? t.c[Mathf.Min(th - 1, y) * tw + Mathf.Min(tw - 1, x)] : t.Sample((x + 0.5f) * tw * scale / p.w, (y + 0.5f) * th * scale / p.h); });
            var m = where == Where.Painted ? TextureMix.Painted(strokes, p.w, p.h, soft) : where == Where.Patches ? TextureMix.Patches(p.w, p.h, amount, size, seed) : TextureMix.Parts(p, amount, where == Where.HighParts);
            return TextureMix.Mix(p, b, m, soft, shapes);
        }
        public override TextureStep ForRecipe() { var c = (MixStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(MixStep)); c.other = null; c.strokes.Clear(); return c; }
        public override TextureStep Demo() => null;
    }
}
