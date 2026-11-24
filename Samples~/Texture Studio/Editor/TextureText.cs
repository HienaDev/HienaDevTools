using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Words as a picture: any font on the computer or in the project, with an outline, worn away, or sprayed on.</summary>
    [System.Serializable]
    public class TextArt
    {
        public enum Look { Clean, Worn, Spray, Stencil }
        public string text = "DANGER", osFont = ""; public Font font; public FontStyle style = FontStyle.Bold; public Look look; public float outline, spacing, wear = 0.4f; public int seed = 1;
        static string[] s_Fonts;
        public static string[] Fonts => s_Fonts ?? (s_Fonts = Font.GetOSInstalledFontNames().Distinct().OrderBy(f => f).ToArray());
        public string Key => text + "|" + osFont + "|" + (font ? font.name : "") + style + look + outline.ToString("0.00") + spacing.ToString("0.00") + wear.ToString("0.00") + seed;

        public bool Settings()
        {
            bool ch = false; EditorGUI.BeginChangeCheck();
            text = EditorGUILayout.TextArea(text, new GUIStyle(EditorStyles.textArea) { fontSize = 14 }, GUILayout.MinHeight(36));
            font = (Font)EditorGUILayout.ObjectField(new GUIContent("Font", "A font in your project, or pick one of the computer's below"), font, typeof(Font), false);
            if (!font) { var r = EditorGUILayout.GetControlRect(); r = EditorGUI.PrefixLabel(r, new GUIContent("On this PC")); if (GUI.Button(r, osFont.Length > 0 ? osFont : "Arial", EditorStyles.popup)) { var m = new GenericMenu(); foreach (var f in Fonts) { var ff = f; m.AddItem(new GUIContent(f.Substring(0, 1).ToUpperInvariant() + "/" + f), f == osFont, () => { osFont = ff; PaletteStep.Poke?.Invoke(); }); } m.DropDown(r); } }
            style = (FontStyle)EditorGUILayout.Popup("Style", (int)style, new[] { "Normal", "Bold", "Italic", "Bold and italic" }); look = (Look)EditorGUILayout.Popup(new GUIContent("Look"), (int)look, new[] { "Clean", "Worn paint", "Sprayed (drips)", "Stencil" });
            if (look != Look.Clean) wear = EditorGUILayout.Slider("How much", wear, 0f, 1f); outline = EditorGUILayout.Slider(new GUIContent("Black outline"), outline, 0f, 1f); spacing = EditorGUILayout.Slider(new GUIContent("Letter gap"), spacing, -0.3f, 1f);
            ch = EditorGUI.EndChangeCheck(); if (look != Look.Clean && GUILayout.Button("Other scratches", EditorStyles.miniButton)) { seed++; ch = true; }
            return ch;
        }

        /// <summary>Only in the window (fonts can't be read in the background).</summary>
        public Pixels Draw()
        {
            var f = font ? font : Font.CreateDynamicFontFromOSFont(osFont.Length > 0 ? osFont : "Arial", 96); if (!f) return null; const int S = 96; var t = string.IsNullOrEmpty(text) ? " " : text.Replace("\r", ""); f.RequestCharactersInTexture(t, f.dynamic ? S : 0, f.dynamic ? style : FontStyle.Normal); var mt = f.material ? f.material.mainTexture : null; if (!mt) return null;
            var rt = RenderTexture.GetTemporary(mt.width, mt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); var prev = RenderTexture.active; RenderTexture.active = rt; GL.Clear(true, true, Color.clear); Graphics.Blit(mt, rt); RenderTexture.active = rt; var read = new Texture2D(mt.width, mt.height, TextureFormat.RGBA32, false, true); read.ReadPixels(new Rect(0, 0, mt.width, mt.height), 0, 0); read.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); var ap = read.GetPixels(); int aw = mt.width, ah = mt.height; Object.DestroyImmediate(read);
            float Alpha(float u, float v) { float x = u * aw - 0.5f, y = v * ah - 0.5f; int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, aw - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, ah - 1), x1 = Mathf.Min(aw - 1, x0 + 1), y1 = Mathf.Min(ah - 1, y0 + 1); float fx = Mathf.Clamp01(x - x0), fy = Mathf.Clamp01(y - y0); return Mathf.Lerp(Mathf.Lerp(ap[y0 * aw + x0].a, ap[y0 * aw + x1].a, fx), Mathf.Lerp(ap[y1 * aw + x0].a, ap[y1 * aw + x1].a, fx), fy); }
            // lay the letters out, every line in the middle
            float px = f.dynamic ? S : Mathf.Max(8, f.fontSize), line = Mathf.Max(px * 1.12f, f.lineHeight > 0 && !f.dynamic ? f.lineHeight : px * 1.12f); var lines = t.Split('\n'); var widths = lines.Select(l => l.Sum(c => f.GetCharacterInfo(c, out var ci, f.dynamic ? S : 0, f.dynamic ? style : FontStyle.Normal) ? ci.advance + spacing * px * 0.4f : px * 0.4f)).ToArray(); int pad = Mathf.RoundToInt(px * (0.18f + (look == Look.Spray ? 0.5f : 0))), w = Mathf.Clamp(Mathf.CeilToInt(widths.Max()) + pad * 2, 16, 2048), h = Mathf.Clamp(Mathf.CeilToInt(line * lines.Length) + pad * 2, 16, 2048); var a = new float[w * h];
            for (int li = 0; li < lines.Length; li++)
            {
                float x = (w - widths[li]) / 2, bas = h - pad - line * (li + 1) + line * 0.24f;
                foreach (var c in lines[li])
                {
                    if (!f.GetCharacterInfo(c, out var ci, f.dynamic ? S : 0, f.dynamic ? style : FontStyle.Normal)) { x += px * 0.4f; continue; }
                    int gw = ci.glyphWidth, gh = ci.glyphHeight; for (int y = 0; y < gh; y++) for (int q = 0; q < gw; q++) { float s = (q + 0.5f) / gw, r = (y + 0.5f) / gh; var uv = Vector2.Lerp(Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, s), Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, s), r); int ox = Mathf.RoundToInt(x + ci.minX) + q, oy = Mathf.RoundToInt(bas + ci.minY) + y; if (ox < 0 || oy < 0 || ox >= w || oy >= h) continue; int i = oy * w + ox; a[i] = Mathf.Max(a[i], Alpha(uv.x, uv.y)); }
                    x += ci.advance + spacing * px * 0.4f;
                }
            }
            var fine = new TextureOps.TileNoise(seed + 3, 40); var big = new TextureOps.TileNoise(seed + 31, 7); var rnd = new System.Random(seed); var fill = a;
            if (look == Look.Stencil) { fill = (float[])a.Clone(); int bar = Mathf.RoundToInt(px * 0.07f); for (int li = 0; li < lines.Length; li++) { int cx = w / 2; float mid = h - pad - line * (li + 0.58f); for (int y = 0; y < h; y++) if (Mathf.Abs(y - mid) < bar * (0.5f + wear)) for (int x = 0; x < w; x++) fill[y * w + x] = 0; } }
            if (look == Look.Worn || look == Look.Stencil) { if (fill == a) fill = (float[])a.Clone(); float k = look == Look.Stencil ? wear * 0.5f : wear; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float u = x / 512f, v = y / 512f, n = fine.Fbm(u, v) * 0.6f + big.Fbm(u, v) * 0.4f; fill[y * w + x] *= Mathf.Clamp01((n - (0.22f + k * 0.4f)) * 9 + 0.5f); } }
            if (look == Look.Spray)
            {
                var p = new Pixels(w, h, false); for (int i = 0; i < a.Length; i++) p.c[i] = new Color(a[i], 0, 0, 1); p = TextureOpsMore.Blur(p, 1024f / Mathf.Max(w, h) * px * (0.01f + wear * 0.03f)); fill = p.c.Select((c, i) => { float n = (float)rnd.NextDouble(); return Mathf.Clamp01(Mathf.Max(a[i] * 0.9f, Mathf.Clamp01(c.r * 1.5f) > n * 0.9f ? c.r * 1.2f : c.r * 0.5f)); }).ToArray();
                int drips = Mathf.RoundToInt(wear * widths.Max() / px * 3); for (int d = 0; d < drips; d++) { int x = rnd.Next(pad, Mathf.Max(pad + 1, w - pad)), y = h - 1; int low = -1; for (int yy = 0; yy < h; yy++) if (a[yy * w + x] > 0.6f) { low = yy; break; } if (low < 0) continue; int len = Mathf.RoundToInt(px * (0.1f + 0.5f * (float)rnd.NextDouble())), th = Mathf.Max(1, Mathf.RoundToInt(px * 0.015f)); for (int q = 0; q < len && low - q >= 0; q++) for (int xx = -th; xx <= th; xx++) { int i = (low - q) * w + Mathf.Clamp(x + xx, 0, w - 1); fill[i] = Mathf.Max(fill[i], 0.85f); } }
            }
            var o = new Pixels(w, h); float[] ring = null; if (outline > 0.01f) { int rad = Mathf.Max(1, Mathf.RoundToInt(px * 0.07f * outline)); ring = new float[w * h]; var off = new List<Vector2Int>(); for (int y = -rad; y <= rad; y++) for (int x = -rad; x <= rad; x++) if (x * x + y * y <= rad * rad) off.Add(new Vector2Int(x, y)); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (a[y * w + x] > 0.3f) foreach (var q in off) { int xx = x + q.x, yy = y + q.y; if (xx >= 0 && yy >= 0 && xx < w && yy < h) { int i = yy * w + xx; ring[i] = Mathf.Max(ring[i], a[y * w + x]); } } }
            for (int i = 0; i < o.c.Length; i++) { float al = fill[i], sh = 0.5f; if (ring != null) { float edge = ring[i]; sh = edge > 0.01f ? Mathf.Lerp(0f, 0.5f, al) : 0.5f; al = Mathf.Max(al, edge * (look == Look.Clean ? 1 : 0.85f)); } o.c[i] = new Color(sh, sh, sh, al); }
            return o;
        }
    }
}
