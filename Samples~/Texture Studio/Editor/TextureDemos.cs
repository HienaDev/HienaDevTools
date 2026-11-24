using System;
using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The little before | after pictures on the tools. Each one is set up so the difference is plain to see
    /// (stronger than the tool starts with, on a part of your texture that shows it).</summary>
    public static class TextureDemos
    {
        const int S = 48;

        public static (Pixels before, Pixels after) Of(TextureStep tool, Pixels p)
        {
            var run = new TextureRun { scale = 1 };
            Pixels whole = p.Resized(S, S), part = Crop(p, 0.3f, 0.3f, 0.34f).Resized(S, S);
            switch (tool)
            {
                case EvenLightingStep _:
                {
                    // the same texture lit from one side
                    var lit = whole.Copy(); for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float k = Mathf.Lerp(1.55f, 0.35f, x / (float)S); var c = lit.c[y * S + x]; lit.c[y * S + x] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1); }
                    return (lit, TextureOps.EvenLighting(lit, 1f, 4));
                }
                case SeamlessStep _:
                {
                    // a piece that doesn't loop, shown 2 x 2
                    var piece = Crop(p, 0.12f, 0.2f, 0.55f).Resized(S, S); piece = Shade(piece);
                    return (Tile(piece, 2), Tile(TextureOps.MakeSeamless(piece, 0.35f, TextureOps.SeamStyle.Soft), 2));
                }
                case BreakRepeatsStep _:
                {
                    // the same stone again and again: the thing that gives a repeat away
                    var one = whole.Copy(); for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.5f, S * 0.5f)) / (S * 0.23f); if (d < 1f) one.c[y * S + x] = Color.Lerp(new Color(0.95f, 0.9f, 0.8f), new Color(0.55f, 0.5f, 0.45f), d * d); }
                    return (Tile(one, 3), Tile(whole, 3));
                }
                case VariationsStep _: { var v = new VariationsStep { grid = 2, made = 3, tint = 1f, rare = 0 }; return (Tile(whole, 2), v.Run(p.Resized(64, 64), run).Resized(S, S)); }
                case ShiftStep _: { var m = Mark(whole); return (m, TextureOpsMore.Shift(m, 0.5f, 0.5f)); }
                case PaintOutStep _:
                {
                    var big = p.Resized(96, 96); var st = new TextureHeal.Stroke { radius = 0.15f }; st.points.Add(new Vector2(0.5f, 0.5f)); var m = TextureHeal.Mask(new List<TextureHeal.Stroke> { new TextureHeal.Stroke { radius = 0.11f, points = st.points } }, 96, 96);
                    var stain = big.Copy(); for (int i = 0; i < m.Length; i++) if (m[i]) stain.c[i] = Color.Lerp(stain.c[i], new Color(0.75f, 0.12f, 0.08f), 0.85f);
                    return (stain.Resized(S, S), TextureHeal.Heal(stain, new List<TextureHeal.Stroke> { st }).Resized(S, S));
                }
                case StickerStep sk: return (whole, sk.Demo().Run(p.Resized(96, 96), run).Resized(S, S));
                case MixStep _:
                {
                    var green = TextureOps.Adjust(whole, new TextureOps.AdjustSettings { tint = new Color(0.35f, 0.75f, 0.2f), brightness = 0.05f, contrast = -0.2f });
                    return (whole, TextureMix.Mix(whole, green, TextureMix.Patches(S, S, 0.5f, 1.6f, 4), 0.1f, 0.3f));
                }
                case ColourStep _: return (whole, TextureOps.Adjust(whole, new TextureOps.AdjustSettings { saturation = 0.6f, hue = 0.45f, brightness = 0.04f }));
                case PaletteStep _: return (whole, TexturePalette.Apply(whole, TexturePalette.ReadyColours(1), TexturePalette.How.Soft, 1f, 0));
                case DirtStep _: return (whole, TextureOpsMore.Dirt(whole, 0.95f, 0.8f, 1.4f, new Color(0.2f, 0.14f, 0.08f), 3));
                case CrunchStep _: return (whole, TextureOpsMore.Nearest(TextureOpsMore.Crunch(TextureOpsMore.Crisp(whole, 24, 24), TextureOpsMore.Depth.Colours16, 1.4f, 0.5f, 0.15f, 1), S, S));
                case PixelArtStep _: return (whole, TextureOpsMore.PixelArt(whole, 12, 8, true));
                case SharpenStep _: return (part, TextureOps.Sharpen(part, 2.2f, 24f));
                case BlurStep _: return (part, TextureOpsMore.Blur(part, 70f));
                case ResizeStep _: return (part, TextureOpsMore.Nearest(TextureOpsMore.Crisp(part, 10, 10), S, S));
                case FlipTurnStep _: { var m = Mark(whole); return (m, TextureOpsMore.FlipTurn(m, false, false, 1)); }
            }
            return (whole, tool.Run(whole, run).Resized(S, S));
        }

        static Pixels Crop(Pixels p, float x, float y, float size)
        {
            int w = Mathf.Max(8, Mathf.RoundToInt(p.w * size)), h = Mathf.Max(8, Mathf.RoundToInt(p.h * size)), x0 = Mathf.RoundToInt(p.w * x), y0 = Mathf.RoundToInt(p.h * y); var r = new Pixels(w, h, p.srgb);
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) r.c[j * w + i] = p[x0 + i, y0 + j];
            return r;
        }
        static Pixels Tile(Pixels p, int n) { var s = p.Resized(Mathf.Max(4, S / n), Mathf.Max(4, S / n)); var r = new Pixels(S, S, p.srgb); for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) r.c[y * S + x] = s[x, y]; return r; }
        // darker towards one side, so the seam is plain
        static Pixels Shade(Pixels p) { var r = p.Copy(); for (int y = 0; y < p.h; y++) for (int x = 0; x < p.w; x++) { float k = Mathf.Lerp(1.25f, 0.6f, x / (float)p.w); var c = p.c[y * p.w + x]; r.c[y * p.w + x] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1); } return r; }
        // an arrow on it, so you can see which way round it is
        static Pixels Mark(Pixels p)
        {
            var r = p.Copy(); var o = new Color(1f, 0.62f, 0.2f);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { float u = (x + 0.5f) / S, v = (y + 0.5f) / S; bool stem = Mathf.Abs(u - 0.5f) < 0.07f && v > 0.2f && v < 0.6f, head = v >= 0.55f && v < 0.82f && Mathf.Abs(u - 0.5f) < (0.82f - v) * 0.8f; if (stem || head) r.c[y * S + x] = o; }
            return r;
        }

        /// <summary>Both next to each other, with a line between.</summary>
        public static Pixels Pair(Pixels a, Pixels b)
        {
            var r = new Pixels(S * 2 + 2, S, true);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) { r.c[y * r.w + x] = a.c[y * S + x]; r.c[y * r.w + S + 2 + x] = b.c[y * S + x]; }
            for (int y = 0; y < S; y++) { r.c[y * r.w + S] = Color.white; r.c[y * r.w + S + 1] = Color.white; }
            for (int i = 0; i < r.c.Length; i++) r.c[i].a = 1;
            return r;
        }
    }
}
