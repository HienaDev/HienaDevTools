using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    public enum MatKind { Normal, NeverRepeats, Mine }
    public enum SeeThrough { Auto, Solid, CutOut, Smooth }

    /// <summary>The settings of the "HienaDev/Never Repeats Lit" material.</summary>
    [Serializable]
    public class NeverRepeats
    {
        public float patch = 1f, turn = 1f, macro = 0.25f, macroSize = 4f, secondAmount = 0.4f, secondSize = 3f, secondSoft = 0.25f; public Vector2Int snap; public bool sideways; public Color macroColor = new Color(0.75f, 0.7f, 0.62f); public Texture2D second;
        public const string ShaderName = "HienaDev/Never Repeats Lit";
        /// <summary>Tile variations for it to swap between (null: none).</summary>
        public static void Tiles(Material m, Texture2DArray colour, Texture2DArray normal, Texture2DArray mask, float rare) { if (colour) { m.SetTexture("_Tiles", colour); m.SetTexture("_TilesNormal", normal); m.SetTexture("_TilesMask", mask); m.SetFloat("_TileCount", colour.depth); m.SetFloat("_TileRare", rare); m.EnableKeyword("_TILES"); } else m.DisableKeyword("_TILES"); }
        public NeverRepeats Copy() => JsonUtility.FromJson<NeverRepeats>(JsonUtility.ToJson(this));

        public void To(Material m)
        {
            m.SetFloat("_PatchSize", patch); m.SetFloat("_Turn", snap == Vector2Int.zero && !sideways ? turn : 0); m.SetVector("_Snap", sideways ? new Vector4(snap.x, 1) : new Vector4(snap.x, snap.y)); // (with a foot, patches only slide sideways: the rows stay where they are) m.SetFloat("_Macro", macro); m.SetFloat("_MacroSize", macroSize); m.SetColor("_MacroColor", macroColor);
            m.SetTexture("_SecondMap", second); m.SetFloat("_SecondAmount", second ? secondAmount : 0); m.SetFloat("_SecondSize", secondSize); m.SetFloat("_SecondSoft", secondSoft);
        }
    }

    /// <summary>Several pictures in one texture, for shaders that pick between them.</summary>
    public static class TextureArrays
    {
        public static Texture2DArray Make(List<Pixels> l, bool srgb, bool compress)
        {
            int w = l[0].w, h = l[0].h; bool dxt = compress && w % 4 == 0 && h % 4 == 0; var f = dxt ? srgb ? TextureFormat.DXT1 : TextureFormat.BC7 : TextureFormat.RGBA32; Texture2DArray a = null;
            for (int i = 0; i < l.Count; i++) { var p = l[i].w == w && l[i].h == h ? l[i] : l[i].Resized(w, h); var t = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb); t.SetPixels(p.c); t.Apply(true); if (dxt) EditorUtility.CompressTexture(t, f, TextureCompressionQuality.Normal); if (a == null) a = new Texture2DArray(w, h, l.Count, t.format, t.mipmapCount, !srgb) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 }; Graphics.CopyTexture(t, 0, a, i); UnityEngine.Object.DestroyImmediate(t); }
            if (!compress) a.hideFlags = HideFlags.HideAndDontSave; return a;
        }
        public static Texture2DArray Save(List<Pixels> l, bool srgb, string path)
        {
            // (as an asset it needs its pixels on this side too, not only on the graphics card)
            int w = l[0].w, h = l[0].h; bool dxt = w % 4 == 0 && h % 4 == 0; Texture2DArray a = null; for (int i = 0; i < l.Count; i++) { var p = l[i].w == w && l[i].h == h ? l[i] : l[i].Resized(w, h); var t = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb); t.SetPixels(p.c); t.Apply(true); if (dxt) EditorUtility.CompressTexture(t, srgb ? TextureFormat.DXT1 : TextureFormat.BC7, TextureCompressionQuality.Normal); if (a == null) a = new Texture2DArray(w, h, l.Count, t.format, t.mipmapCount, !srgb) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 }; for (int m = 0; m < t.mipmapCount; m++) a.SetPixelData(t.GetPixelData<byte>(m), m, i); UnityEngine.Object.DestroyImmediate(t); }
            a.Apply(false); a.name = System.IO.Path.GetFileNameWithoutExtension(path); var old = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path); if (old) { EditorUtility.CopySerialized(a, old); UnityEngine.Object.DestroyImmediate(a); a = old; } else AssetDatabase.CreateAsset(a, path); AssetDatabase.SaveAssetIfDirty(a); return a;
        }
    }

    /// <summary>Pictures with see-through parts.</summary>
    public static class TextureAlpha
    {
        /// <summary>What a picture needs: solid, cut out (parts are there or not) or smooth (soft edges, glass, smoke).</summary>
        public static SeeThrough Guess(Pixels p)
        {
            int see = 0, half = 0; foreach (var c in p.c) { if (c.a < 0.98f) see++; if (c.a > 0.08f && c.a < 0.92f) half++; }
            if (see < p.c.Length * 0.002f) return SeeThrough.Solid;
            return half > p.c.Length * 0.12f ? SeeThrough.Smooth : SeeThrough.CutOut;
        }

        /// <summary>The colours hidden under see-through parts are replaced by the nearest visible ones. Without it whatever
        /// is hidden there (usually black or white) bleeds round the edges on a model.</summary>
        public static Pixels Bleed(Pixels p)
        {
            bool any = false; foreach (var c in p.c) if (c.a < 0.5f) { any = true; break; }
            if (!any) return p;
            // smaller and smaller copies that only count what's visible
            var levels = new List<(int w, int h, Color[] c)>(); var first = new Color[p.c.Length]; for (int i = 0; i < first.Length; i++) { var c = p.c[i]; first[i] = new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a); }
            levels.Add((p.w, p.h, first));
            while (levels[levels.Count - 1].w > 1 || levels[levels.Count - 1].h > 1)
            {
                var (w, h, c) = levels[levels.Count - 1]; int nw = Mathf.Max(1, (w + 1) / 2), nh = Mathf.Max(1, (h + 1) / 2); var n = new Color[nw * nh];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) n[y / 2 * nw + x / 2] += c[y * w + x];
                levels.Add((nw, nh, n));
            }
            Color[] up = null; int uw = 0, uh = 0;
            for (int l = levels.Count - 1; l >= 0; l--)
            {
                var (w, h, c) = levels[l]; var o = new Color[c.Length];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { var q = c[y * w + x]; var below = up != null ? up[Mathf.Min(uh - 1, y / 2) * uw + Mathf.Min(uw - 1, x / 2)] : Color.gray; o[y * w + x] = q.a > 0.004f ? new Color(q.r / q.a, q.g / q.a, q.b / q.a, 1) : below; }
                up = o; uw = w; uh = h;
            }
            var r = p.Copy(); for (int i = 0; i < r.c.Length; i++) if (r.c[i].a < 0.5f) { var k = Mathf.Clamp01(r.c[i].a * 2); var c = Color.Lerp(up[i], r.c[i], k); c.a = p.c[i].a; r.c[i] = c; }
            return r;
        }

        /// <summary>Sets a material up for it (URP Lit / Unlit, Standard, Never Repeats; other shaders are left alone).</summary>
        public static void Apply(Material m, SeeThrough s)
        {
            bool cut = s == SeeThrough.CutOut, smooth = s == SeeThrough.Smooth;
            void F(string n, float v) { if (m.HasProperty(n)) m.SetFloat(n, v); }
            void K(string k, bool on) { if (on) m.EnableKeyword(k); else m.DisableKeyword(k); }
            if (m.shader.name == NeverRepeats.ShaderName) { if (smooth) cut = true; F("_AlphaClip", cut ? 1 : 0); K("_ALPHATEST_ON", cut); F("_Cull", cut ? 0 : 2); m.renderQueue = cut ? 2450 : -1; return; }
            if (m.HasProperty("_Surface") && m.HasProperty("_AlphaClip"))
            {
                F("_Surface", smooth ? 1 : 0); F("_AlphaClip", cut ? 1 : 0); F("_Cutoff", 0.5f); F("_Blend", 0); F("_BlendModePreserveSpecular", 0);
                F("_SrcBlend", smooth ? 5 : 1); F("_DstBlend", smooth ? 10 : 0); F("_SrcBlendAlpha", 1); F("_DstBlendAlpha", smooth ? 10 : 0); F("_ZWrite", smooth ? 0 : 1); F("_Cull", cut || smooth ? 0 : 2); F("_AlphaToMask", cut ? 1 : 0);
                K("_ALPHATEST_ON", cut); K("_SURFACE_TYPE_TRANSPARENT", smooth); K("_ALPHAPREMULTIPLY_ON", false);
                m.SetOverrideTag("RenderType", smooth ? "Transparent" : cut ? "TransparentCutout" : "Opaque"); m.renderQueue = smooth ? 3000 : cut ? 2450 : -1; m.SetShaderPassEnabled("DepthOnly", !smooth);
                return;
            }
            if (m.HasProperty("_Mode") && m.HasProperty("_SrcBlend"))
            {
                F("_Mode", smooth ? 2 : cut ? 1 : 0); F("_SrcBlend", smooth ? 5 : 1); F("_DstBlend", smooth ? 10 : 0); F("_ZWrite", smooth ? 0 : 1); F("_Cutoff", 0.5f);
                K("_ALPHATEST_ON", cut); K("_ALPHABLEND_ON", smooth); K("_ALPHAPREMULTIPLY_ON", false); m.SetOverrideTag("RenderType", smooth ? "Transparent" : cut ? "TransparentCutout" : ""); m.renderQueue = smooth ? 3000 : cut ? 2450 : -1;
            }
        }

        /// <summary>The same on any shader: what it can do is found out. Returns what's wrong, in words ("" when fine).</summary>
        public static string ApplyAny(Material m, SeeThrough s)
        {
            if (s == SeeThrough.Solid || s == SeeThrough.Auto) return ""; bool known = m.shader.name == NeverRepeats.ShaderName || m.HasProperty("_Surface") && m.HasProperty("_AlphaClip") || m.HasProperty("_Mode") && m.HasProperty("_SrcBlend"); if (known) { Apply(m, s); return ""; }
            string tag = m.GetTag("RenderType", true, ""), queue = m.GetTag("Queue", true, ""); bool blends = tag.Contains("Transparent") && !tag.Contains("Cutout") || queue.StartsWith("Transparent") || m.renderQueue >= 2900; bool cuts = tag.Contains("Cutout") || queue.StartsWith("AlphaTest") || m.HasProperty("_Cutoff") || m.HasProperty("_AlphaClip");
            if (m.HasProperty("_AlphaClip") || m.HasProperty("_Cutoff")) { if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1); foreach (var k in new[] { "_ALPHATEST_ON", "_ALPHACLIP_ON", "_ALPHA_CLIP" }) m.EnableKeyword(k); if (!blends) m.renderQueue = 2450; }
            return blends || cuts ? "" : "That shader (" + m.shader.name + ") draws everything solid, so the empty parts of the picture can't be see-through with it. Pick a material whose shader can be transparent or cut out, or leave this empty.";
        }

        public static readonly string[] Names = { "Find out", "Solid", "Cut out", "Smooth" };

        /// <summary>A grey checkerboard, so see-through parts show as such.</summary>
        public static Texture2D Checks
        {
            get
            {
                if (s_Checks) return s_Checks; s_Checks = new Texture2D(16, 16) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { float v = (x / 8 + y / 8) % 2 == 0 ? 0.32f : 0.42f; s_Checks.SetPixel(x, y, new Color(v, v, v, 1)); }
                s_Checks.Apply(); return s_Checks;
            }
        }
        static Texture2D s_Checks;

        /// <summary>Takes a plain background out of a picture: everything like the colour in its corners (or a given one).</summary>
        public static Pixels CutColour(Pixels p, Color back, float tolerance, float soft)
        {
            var b = TexturePalette.Lab(back); var r = p.Copy(); float lo = tolerance * 0.5f, hi = lo + 0.02f + soft * 0.25f;
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int i = y * p.w + x; var c = p.c[i]; float d = (TexturePalette.Lab(c) - b).magnitude; c.a *= Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - lo) / (hi - lo))); r.c[i] = c; } });
            return Bleed(r);
        }
        public static Color Corner(Pixels p) { var l = new[] { p[1, 1], p[p.w - 2, 1], p[1, p.h - 2], p[p.w - 2, p.h - 2] }; Color best = l[0]; float near = float.MaxValue; foreach (var a in l) { float d = 0; foreach (var b in l) d += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b); if (d < near) { near = d; best = a; } } return best; }
    }

    /// <summary>How plainly a texture's repeat shows, and which spots give it away.</summary>
    public static class TextureRepeat
    {
        /// <summary>Makes every part of it about as bright, as colourful and as contrasty as the rest, so no part stands out.</summary>
        public static Pixels EvenOut(Pixels p, float amount, float radius = 55f)
        {
            if (amount <= 0.001f) return p;
            var mean = TextureOpsMore.Blur(p, radius); Color all = Color.clear; foreach (var c in p.c) all += c; all /= p.c.Length;
            var dev = new Pixels(p.w, p.h, false); float total = 0; for (int i = 0; i < p.c.Length; i++) { float d = Pixels.Lum(p.c[i]) - Pixels.Lum(mean.c[i]); d *= d; total += d; dev.c[i] = new Color(d, d, d, 1); }
            dev = TextureOpsMore.Blur(dev, radius); float std = Mathf.Sqrt(total / p.c.Length); var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int i = y * p.w + x; var c = p.c[i]; float k = Mathf.Clamp(std / Mathf.Max(0.004f, Mathf.Sqrt(dev.c[i].r)), 0.6f, 1.7f); var o = all + (c - mean.c[i]) * k; o = Color.LerpUnclamped(c, o, amount);
                    r.c[i] = new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b), c.a);
                }
            });
            return r;
        }

        /// <summary>0 (you can't tell) to 100 (you see it at once): how much is left of it when you squint.</summary>
        public static int Score(Pixels p)
        {
            var s = p.Resized(96, 96); var lab = new Pixels(96, 96, false); for (int i = 0; i < lab.c.Length; i++) { var v = TexturePalette.Lab(s.c[i]); lab.c[i] = new Color(v.x, v.y * 1.6f + 0.5f, v.z * 1.6f + 0.5f, 1); }
            var b = TextureOpsMore.Blur(lab, 34f); Color m = Color.clear; foreach (var c in b.c) m += c; m /= b.c.Length; float e = 0, top = 0;
            foreach (var c in b.c) { var d = c - m; float q = d.r * d.r + d.g * d.g + d.b * d.b; e += q; top = Mathf.Max(top, q); }
            return Mathf.Clamp(Mathf.RoundToInt((Mathf.Sqrt(e / b.c.Length) * 0.6f + Mathf.Sqrt(top) * 0.2f) * 1100f), 0, 100);
        }

        /// <summary>The spots you recognise, as an orange glow.</summary>
        public static Pixels Glow(Pixels p)
        {
            var m = TextureCalm.Spots(p, 0.45f, out int w, out int h); var r = new Pixels(w, h); for (int i = 0; i < m.Length; i++) r.c[i] = new Color(1f, 0.45f, 0.05f, m[i] ? 0.6f : 0f);
            var soft = TextureOpsMore.Blur(r, 12f); for (int i = 0; i < soft.c.Length; i++) soft.c[i] = new Color(1f, 0.45f, 0.05f, soft.c[i].a); return soft;
        }
    }
}
