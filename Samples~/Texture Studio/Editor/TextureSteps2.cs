using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A wall, floor or poster photographed at an angle becomes flat, as if seen straight from the front.</summary>
    [Serializable]
    public class StraightenStep : TextureStep
    {
        public Vector2[] corners = { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f) }; // bottom left, bottom right, top right, top left
        public int shape; public float shapeRatio = 1f, side = 1f;
        public float bend;
        public override string Title => "Straighten";
        public override string Blurb => "Flattens a photo taken at an angle";
        public override string Explain => "Drag the four orange corners onto the corners of something that's really a rectangle: a wall, a door, a tile, a poster. It becomes flat and fills the picture. Use it first.";
        public override string Summary => "four corners" + (shape == 0 ? "" : shape == 1 ? ", square" : ", " + shapeRatio.ToString("0.00") + " wide");
        public override Color Tint => new Color(0.55f, 0.8f, 0.95f);
        public override bool SameSize => false;
        public override bool Settings()
        {
            bool ch = false; EditorGUILayout.LabelField("Shape of the result", EditorStyles.miniBoldLabel); if (Choice(ref shape, "Work it out", "Square", "Choose")) ch = true; if (shape == 2) ch |= Slider(ref shapeRatio, "Wide to tall", "2 = twice as wide as it's tall", 0.2f, 5f);
            ch |= Slider(ref bend, "Lens bend", "Straightens lines that bulge because of a wide camera lens. Below 0: lines that bend inwards", -0.4f, 0.4f); ch |= Slider(ref side, "Size", "Parts of the original's size", 0.25f, 2f);
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("Whole picture")) { corners = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; ch = true; } if (GUILayout.Button("Start again")) { corners = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.1f, 0.9f) }; bend = 0; ch = true; } EditorGUILayout.EndHorizontal();
            return ch;
        }

        float Ratio(Vector2Int z)
        {
            if (shape == 1) return 1; if (shape == 2) return shapeRatio; Vector2 P(int i) => new Vector2(corners[i].x * z.x, corners[i].y * z.y);
            // how wide and tall it really is can be worked out from how much it's squashed (if the photo isn't cropped)
            float cx = z.x / 2f, cy = z.y / 2f; Vector3 m1 = P(3), m2 = P(2), m3 = P(0), m4 = P(1); m1 -= new Vector3(cx, cy); m2 -= new Vector3(cx, cy); m3 -= new Vector3(cx, cy); m4 -= new Vector3(cx, cy); m1.z = m2.z = m3.z = m4.z = 1;
            float k2 = Vector3.Dot(Vector3.Cross(m1, m4), m3) / Vector3.Dot(Vector3.Cross(m2, m4), m3), k3 = Vector3.Dot(Vector3.Cross(m1, m4), m2) / Vector3.Dot(Vector3.Cross(m3, m4), m2); Vector3 n2 = k2 * m2 - m1, n3 = k3 * m3 - m1; float simple = ((P(1) - P(0)).magnitude + (P(2) - P(3)).magnitude) / Mathf.Max(1e-3f, (P(3) - P(0)).magnitude + (P(2) - P(1)).magnitude);
            if (Mathf.Abs(n2.z) < 1e-4f || Mathf.Abs(n3.z) < 1e-4f) return simple; float f2 = -(n2.x * n3.x + n2.y * n3.y) / (n2.z * n3.z); if (f2 <= 0 || float.IsNaN(f2) || f2 > 400f * z.x * z.x) return simple; float w = Mathf.Sqrt((n2.x * n2.x + n2.y * n2.y) / f2 + n2.z * n2.z), h = Mathf.Sqrt((n3.x * n3.x + n3.y * n3.y) / f2 + n3.z * n3.z), r = w / Mathf.Max(1e-5f, h); return float.IsNaN(r) || r < simple * 0.4f || r > simple * 2.5f ? simple : r;
        }
        public override Vector2Int SizeAfter(Vector2Int z) { float r = Mathf.Clamp(Ratio(z), 0.1f, 10f), area = Mathf.Sqrt(Mathf.Max(64, z.x * (float)z.y)) * side; int w = Mathf.Clamp(Mathf.RoundToInt(area * Mathf.Sqrt(r)), 16, 4096), h = Mathf.Clamp(Mathf.RoundToInt(area / Mathf.Sqrt(r)), 16, 4096); if (r > 0.97f && r < 1.03f) h = w; return new Vector2Int(w, h); }

        public override Pixels Run(Pixels p, TextureRun run)
        {
            var z = SizeAfter(new Vector2Int(p.w, p.h)); var o = new Pixels(z.x, z.y, p.srgb); var c = corners;
            // from the flat rectangle to the photo
            float dx1 = c[1].x - c[2].x, dx2 = c[3].x - c[2].x, dx3 = c[0].x - c[1].x + c[2].x - c[3].x, dy1 = c[1].y - c[2].y, dy2 = c[3].y - c[2].y, dy3 = c[0].y - c[1].y + c[2].y - c[3].y, det = dx1 * dy2 - dx2 * dy1, g = Mathf.Abs(det) < 1e-9f ? 0 : (dx3 * dy2 - dx2 * dy3) / det, h = Mathf.Abs(det) < 1e-9f ? 0 : (dx1 * dy3 - dx3 * dy1) / det, a = c[1].x - c[0].x + g * c[1].x, b = c[3].x - c[0].x + h * c[3].x, d = c[1].y - c[0].y + g * c[1].y, e = c[3].y - c[0].y + h * c[3].y;
            // (a far part of the photo is shrunk a little first, so it doesn't sparkle)
            TextureOps.Rows(z.y, y =>
            {
                for (int x = 0; x < z.x; x++)
                {
                    Color sum = Color.clear; for (int s = 0; s < 4; s++)
                    {
                        float u = (x + 0.25f + 0.5f * (s & 1)) / z.x, v = (y + 0.25f + 0.5f * (s >> 1)) / z.y, w = g * u + h * v + 1, sx = (a * u + b * v + c[0].x) / w, sy = (d * u + e * v + c[0].y) / w;
                        if (Mathf.Abs(bend) > 0.001f) { float ox = sx - 0.5f, oy = sy - 0.5f, r2 = (ox * ox + oy * oy) * 2f, k = 1 + bend * (r2 - 0.5f); sx = 0.5f + ox * k; sy = 0.5f + oy * k; }
                        float px = Mathf.Clamp(sx * p.w, 0.5f, p.w - 0.5f), py = Mathf.Clamp(sy * p.h, 0.5f, p.h - 0.5f); sum += p.Sample(px, py);
                    }
                    o.c[y * z.x + x] = sum * 0.25f;
                }
            });
            return o;
        }
        public override TextureStep Demo() => null;
        public override TextureStep ForRecipe() => null;
    }

    /// <summary>Turns one colour into another and leaves the rest.</summary>
    [Serializable]
    public class RecolourStep : TextureStep
    {
        [Serializable] public class Pair { public Color from = new Color(0.7f, 0.2f, 0.15f), to = new Color(0.2f, 0.35f, 0.75f); public float range = 0.35f, soft = 0.4f; }
        public List<Pair> pairs = new List<Pair> { new Pair() }; public int sel; public bool keepLight = true;
        [NonSerialized] public bool picking;
        public override string Title => "Recolour one colour";
        public override string Blurb => "The red parts become blue, the rest stays";
        public override string Explain => "Press Pick, click the colour on the left picture, then choose what it becomes. Its light and dark stay, so it keeps looking real.";
        public override string Summary => pairs.Count + (pairs.Count == 1 ? " colour" : " colours");
        public override Color Tint => new Color(0.85f, 0.55f, 0.85f);
        public override bool Settings()
        {
            bool ch = false; int rem = -1;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i]; EditorGUILayout.BeginHorizontal(); var f = EditorGUILayout.ColorField(GUIContent.none, p.from, true, false, false, GUILayout.Width(54)); var bg = GUI.backgroundColor; if (picking && sel == i) GUI.backgroundColor = new Color(0.85f, 0.55f, 0.85f); if (GUILayout.Button(new GUIContent(picking && sel == i ? "click it..." : "Pick", "Then click the colour on the left picture"), GUILayout.Width(62))) { picking = !(picking && sel == i); sel = i; } GUI.backgroundColor = bg; GUILayout.Label("→", GUILayout.Width(16)); var t = EditorGUILayout.ColorField(GUIContent.none, p.to, true, false, false, GUILayout.Width(54)); GUILayout.FlexibleSpace(); if (pairs.Count > 1 && GUILayout.Button("✕", GUILayout.Width(22))) rem = i; EditorGUILayout.EndHorizontal(); if (f != p.from || t != p.to) { p.from = f; p.to = t; ch = true; }
                ch |= Slider(ref p.range, "How alike", "How different a colour may be and still change", 0.03f, 1f); ch |= Slider(ref p.soft, "Soft edge", "", 0f, 1f);
            }
            if (rem >= 0) { pairs.RemoveAt(rem); sel = 0; ch = true; } if (GUILayout.Button("+ Another colour")) { pairs.Add(new Pair()); sel = pairs.Count - 1; ch = true; } ch |= Toggle(ref keepLight, "Keep its light and dark", "Off: becomes as light or dark as the new colour"); return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            var from = pairs.Select(q => TexturePalette.Lab(q.from)).ToArray(); var to = pairs.Select(q => TexturePalette.Lab(q.to)).ToArray(); var r = new Pixels(p.w, p.h, p.srgb); var prs = pairs.ToArray();
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int i = y * p.w + x; var c = p.c[i]; var l = TexturePalette.Lab(c);
                    for (int k = 0; k < prs.Length; k++)
                    {
                        // the kind of colour counts most, how light it is hardly
                        var f = from[k]; float cf = new Vector2(f.y, f.z).magnitude, cl = new Vector2(l.y, l.z).magnitude, grey = Mathf.Clamp01(1 - cf / 0.04f), dh = new Vector2(l.y - f.y * Mathf.Clamp(l.x / Mathf.Max(0.1f, f.x), 0.3f, 1.5f), l.z - f.z * Mathf.Clamp(l.x / Mathf.Max(0.1f, f.x), 0.3f, 1.5f)).magnitude, dl = Mathf.Abs(l.x - f.x), d = dh + dl * Mathf.Lerp(0.07f, 0.6f, grey), lim = 0.02f + prs[k].range * 0.2f, m = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - lim * (1 - prs[k].soft * 0.7f)) / (lim * (0.08f + prs[k].soft * 0.9f)))); if (m <= 0.001f) continue;
                        float sat = cf > 0.01f ? Mathf.Clamp(cl / cf, 0.2f, 2f) : 1f, lo = keepLight ? l.x + (to[k].x - f.x) * 0.35f : l.x + (to[k].x - f.x); var n = TexturePalette.Rgb(new Vector3(Mathf.Clamp01(lo), to[k].y * sat, to[k].z * sat)); n.a = c.a; c = Color.Lerp(c, n, m); l = TexturePalette.Lab(c);
                    }
                    r.c[i] = c;
                }
            });
            return r;
        }
        public override TextureStep Demo() => null;
    }

    /// <summary>Takes the shadows (and bright patches) of a photo out.</summary>
    [Serializable]
    public class ShadowsStep : TextureStep
    {
        public float amount = 0.8f, size = 0.3f, hard = 0.6f, blue = 0.5f, bright = 0.3f;
        public override string Title => "Remove shadows";
        public override string Blurb => "Takes a photo's shadows out";
        public override string Explain => "Lifts areas that are darker than what's around them but have the same kind of colour. It works best on one material (a wall, the ground) with clear shadows. Dark things that aren't shadows get lighter too, so use as little as you need.";
        public override string Summary => Pct(amount);
        public override Color Tint => new Color(0.95f, 0.9f, 0.6f);
        public override bool FootByDefault => true;
        public override bool Settings() { bool ch = Slider(ref amount, "Shadows", "", 0, 1); ch |= Slider(ref bright, "Bright patches", "Brings spots of sunlight down too", 0, 1); ch |= Slider(ref size, "Shadow size", "Smaller finds small shadows, but flattens real differences too", 0.05f, 1f); ch |= Slider(ref hard, "Sharp edges", "Shadows with a hard edge", 0, 1); ch |= Slider(ref blue, "Fix their tint", "Shadows outside are bluish", 0, 1); return ch; }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int n = p.c.Length; var L = new Pixels(p.w, p.h, false); for (int i = 0; i < n; i++) { float v = Mathf.Log(0.02f + Pixels.Lum(p.c[i])); L.c[i] = new Color(v, v * v, 0, 1); }
            // how light each area is, without blurring over the shadow's edge (a guided filter)
            float rad = 12f + size * 120f, eps = Mathf.Lerp(0.6f, 0.04f, hard); var mean = TextureOpsMore.Blur(L, rad); var ab = new Pixels(p.w, p.h, false); for (int i = 0; i < n; i++) { float m = mean.c[i].r, var = Mathf.Max(0, mean.c[i].g - m * m), a = var / (var + eps); ab.c[i] = new Color(a, m * (1 - a), 0, 1); } ab = TextureOpsMore.Blur(ab, rad * 0.5f); var bas = new float[n]; double avg = 0; for (int i = 0; i < n; i++) { bas[i] = ab.c[i].r * L.c[i].r + ab.c[i].g; avg += bas[i]; }
            avg /= n; var sorted = (float[])bas.Clone(); Array.Sort(sorted); float lit = sorted[(int)(n * 0.7f)], top = sorted[(int)(n * 0.92f)];
            // the usual colour of what's lit
            Vector2 litC = Vector2.zero; int litN = 0; for (int i = 0; i < n; i += 3) if (bas[i] > lit - 0.1f) { var v = TexturePalette.Lab(p.c[i]); litC += new Vector2(v.y, v.z) / Mathf.Max(0.1f, v.x); litN++; } if (litN > 0) litC /= litN; var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int i = y * p.w + x; var c = p.c[i]; float d = lit - bas[i], up = d > 0 ? d * amount : Mathf.Max(d, lit - top) * bright, k = Mathf.Exp(Mathf.Min(up, 2.2f)); var o = new Color(c.r * k, c.g * k, c.b * k);
                    if (d > 0.1f && blue > 0.01f) { var v = TexturePalette.Lab(new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b))); float s = blue * amount * Mathf.Clamp01((d - 0.1f) * 2f) * 0.6f; var tc = litC * v.x; v.y = Mathf.Lerp(v.y, v.y * 0.4f + tc.x * 0.6f, s); v.z = Mathf.Lerp(v.z, v.z * 0.4f + tc.y * 0.6f, s); o = TexturePalette.Rgb(v); }
                    r.c[i] = new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b), c.a);
                }
            });
            return r;
        }
    }

    /// <summary>Gives it the colours, contrast and roughness of another texture, so both look like they're from the same game.</summary>
    [Serializable]
    public class LikeStep : TextureStep
    {
        public Texture2D other; public float colours = 0.85f, contrast = 0.7f, detail = 0.5f; public bool pixels;
        [NonSerialized] Pixels m_Read;
        public override string Title => "Match another";
        public override string Blurb => "Looks like it's from the same game as another one";
        public override string Explain => "Pick a texture that already looks right in your game. This one gets its colours, how strong its light and dark are, and how fine or soft its detail is. What's drawn on it stays.";
        public override string Summary => other ? other.name : "pick a texture";
        public override Color Tint => new Color(1f, 0.75f, 0.6f);
        public override bool Settings() { bool ch = false; var t = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Look like", "Drop one here"), other, typeof(Texture2D), false, GUILayout.Height(64)); if (t != other) { other = t; ch = true; } ch |= Slider(ref colours, "Colours", "Its range of colours", 0, 1); ch |= Slider(ref contrast, "Light and dark", "How strong the differences are", 0, 1); ch |= Slider(ref detail, "Detail", "As crisp or as soft", 0, 1); ch |= Toggle(ref pixels, "As many pixels", "Also as big as that one, with hard pixels when it's small"); return ch; }
        public override void Prepare() { m_Read = other ? Pixels.ReadCached(other) : null; }
        public override bool HardPixels => pixels && other && Mathf.Max(other.width, other.height) <= 256;
        public override bool SameSize => !pixels;
        public override Vector2Int SizeAfter(Vector2Int z) { if (!pixels || !other) return z; float k = Mathf.Max(other.width, other.height) / (float)Mathf.Max(z.x, z.y); return k >= 1 ? z : new Vector2Int(Mathf.Max(4, Mathf.RoundToInt(z.x * k)), Mathf.Max(4, Mathf.RoundToInt(z.y * k))); }
        static float[] Levels(float[] v) { var s = (float[])v.Clone(); Array.Sort(s); var q = new float[65]; for (int i = 0; i <= 64; i++) q[i] = s[Mathf.Min(s.Length - 1, i * (s.Length - 1) / 64)]; return q; }
        static float Through(float[] a, float[] b, float v) { int lo = 0, hi = 64; while (hi - lo > 1) { int m = (lo + hi) / 2; if (a[m] <= v) lo = m; else hi = m; } float t = a[hi] - a[lo] < 1e-6f ? 0.5f : Mathf.Clamp01((v - a[lo]) / (a[hi] - a[lo])); return Mathf.Lerp(b[lo], b[hi], t); }
        // how strong what's smaller than a given size is
        static float Fine(Pixels p, float rad) { var b = TextureOpsMore.Blur(p, rad); double s = 0; for (int i = 0; i < p.c.Length; i++) { float d = Pixels.Lum(p.c[i]) - Pixels.Lum(b.c[i]); s += d * d; } return Mathf.Sqrt((float)(s / p.c.Length)); }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (m_Read == null) return p; var o = p; float k = Mathf.Min(1f, 256f / Mathf.Max(m_Read.w, m_Read.h)); var q = m_Read.Resized(Mathf.Max(8, Mathf.RoundToInt(m_Read.w * k)), Mathf.Max(8, Mathf.RoundToInt(m_Read.h * k))); float kp = Mathf.Min(1f, 256f / Mathf.Max(p.w, p.h)); var small = p.Resized(Mathf.Max(8, Mathf.RoundToInt(p.w * kp)), Mathf.Max(8, Mathf.RoundToInt(p.h * kp)));
            var A = small.c.Select(TexturePalette.Lab).ToArray(); var B = q.c.Select(TexturePalette.Lab).ToArray(); var la = Levels(A.Select(v => v.x).ToArray()); var lb = Levels(B.Select(v => v.x).ToArray()); var ya = Levels(A.Select(v => v.y).ToArray()); var yb = Levels(B.Select(v => v.y).ToArray()); var za = Levels(A.Select(v => v.z).ToArray()); var zb = Levels(B.Select(v => v.z).ToArray()); float ma = A.Average(v => v.x), mb = B.Average(v => v.x); var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y => { for (int x = 0; x < p.w; x++) { int i = y * p.w + x; var c = p.c[i]; var v = TexturePalette.Lab(c); float lx = Mathf.Lerp(v.x + (mb - ma) * colours, Through(la, lb, v.x), contrast); var n = TexturePalette.Rgb(new Vector3(lx, Mathf.Lerp(v.y, Through(ya, yb, v.y), colours), Mathf.Lerp(v.z, Through(za, zb, v.z), colours))); n.a = c.a; r.c[i] = n; } });
            if (detail > 0.01f) { float want = Fine(q, 14f), have = Fine(r.Resized(small.w, small.h), 14f); float ratio = Mathf.Clamp(want / Mathf.Max(0.003f, have), 0.25f, 3f); ratio = Mathf.Lerp(1, ratio, detail); r = ratio > 1 ? TextureOps.Sharpen(r, Mathf.Min(2.5f, ratio - 1), 10f) : TextureOps.Sharpen(r, ratio - 1 + 0.0001f, 10f, true); }
            var z = SizeAfter(new Vector2Int(p.w, p.h)); if (pixels && other) { if (!run.final) { float s = Mathf.Max(other.width, other.height) / (Mathf.Max(p.w, p.h) / Mathf.Max(0.01f, run.scale)); z = new Vector2Int(Mathf.Max(4, Mathf.RoundToInt(p.w / run.scale * Mathf.Min(1, s))), Mathf.Max(4, Mathf.RoundToInt(p.h / run.scale * Mathf.Min(1, s)))); if (z.x < p.w) return TextureOpsMore.Nearest(TextureOpsMore.Crisp(r, z.x, z.y), p.w, p.h); return r; } if (z.x < p.w) return TextureOpsMore.Crisp(r, z.x, z.y); }
            return r;
        }
        public override TextureStep ForRecipe() { var c = (LikeStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(LikeStep)); c.other = null; return c; }
        public override TextureStep Demo() => null;
    }

    /// <summary>Paint rubbed off where a model would really get knocked: along its edges. And dirt in its corners.</summary>
    [Serializable]
    public class EdgeWearStep : TextureStep, IPaintable
    {
        public enum From { Model, Texture, Sides }
        public From from = From.Model; public bool top = true, bottom = true, left = true, right = true, onlyPainted; public List<TextureHeal.Stroke> strokes = new List<TextureHeal.Stroke>(); public float brushSize = 0.06f;
        public List<TextureHeal.Stroke> Strokes => strokes; public float Brush { get => brushSize; set => brushSize = value; } public bool Painting => onlyPainted; public string PaintLabel => "BRUSH WHERE IT MAY WEAR (CTRL RUBS OUT)"; public Color PaintColour => new Color(0.75f, 0.75f, 0.8f, 0.35f);
        public GameObject model; public float width = 0.03f, wear = 0.7f, rough = 0.6f, angle = 25f, dirt = 0.4f, dirtWidth = 0.05f; public Color bare = new Color(0.62f, 0.62f, 0.64f), dark = new Color(0.12f, 0.09f, 0.07f); public Texture2D under; public int seed = 1;
        [NonSerialized] Pixels m_Under; [NonSerialized] float[] m_Out, m_In; [NonSerialized] int m_Size;
        static readonly Dictionary<string, (float[] o, float[] i)> s_Done = new Dictionary<string, (float[], float[])>();
        public override string Title => "Edge wear";
        public override string Blurb => "Worn edges and dirty corners, from your model's shape";
        public override string Explain => "Pick your model: it's worn along its real edges, and dirt gathers in its inward corners. It's made for that model, so see it under 3D > My model. (Empty: a plain box.) It works where each part of the model has its own place on the texture.";
        public override string Summary => (from == From.Model ? model ? model.name : "a box" : from == From.Texture ? "the texture's own edges" : "its sides") + ", " + Pct(wear) + (onlyPainted ? ", where painted" : "");
        public override Color Tint => new Color(0.75f, 0.75f, 0.8f);
        public override bool Settings()
        {
            bool ch = false; int f = (int)from; if (Choice(ref f, "My model's edges", "The texture's edges", "Its sides")) { from = (From)f; ch = true; }
            EditorGUILayout.LabelField(from == From.Model ? "Worn along the real edges of your model (for a texture made for one model)." : from == From.Texture ? "Worn where the texture's own shapes end: the edges of bricks, planks, tiles. Good for a texture that loops." : "Worn along the picture's own sides: trims, planks, the edge of a step.", EditorStyles.wordWrappedMiniLabel);
            if (from == From.Model) { var g = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Your model", "A model or prefab"), model, typeof(GameObject), false); if (g != model) { model = g; ch = true; } }
            else { EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(new GUIContent("Which edges", from == From.Texture ? "The top edge of each brick, the bottom..." : "Which sides of the picture")); bool T(bool v, string l) { var c = GUI.backgroundColor; if (v) GUI.backgroundColor = new Color(0.8f, 0.8f, 0.9f); bool n = GUILayout.Toggle(v, l, EditorStyles.miniButton); GUI.backgroundColor = c; if (n != v) ch = true; return n; } top = T(top, "Top"); bottom = T(bottom, "Bottom"); left = T(left, "Left"); right = T(right, "Right"); EditorGUILayout.EndHorizontal(); }
            ch |= Toggle(ref onlyPainted, "Only where I paint", "Brush on the picture where it may wear; Ctrl rubs out"); if (onlyPainted) { Slider(ref brushSize, "Brush size", "", 0.005f, 0.3f); EditorGUILayout.BeginHorizontal(); using (new EditorGUI.DisabledScope(strokes.Count == 0)) { if (GUILayout.Button("Remove last stroke")) { strokes.RemoveAt(strokes.Count - 1); ch = true; } if (GUILayout.Button("Clear")) { strokes.Clear(); ch = true; } } EditorGUILayout.EndHorizontal(); } ch |= Slider(ref wear, "Wear", "", 0, 1); ch |= Slider(ref width, "Width", "How far in from the edge", 0.003f, 0.15f); ch |= Slider(ref rough, "Chipped", "Smooth rubbing to big chips", 0, 1); ch |= Slider(ref angle, "Edge angle", "How sharp an edge has to be (degrees)", 5f, 80f);
            var u = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("What's under it", "Empty: the colour below"), under, typeof(Texture2D), false); if (u != under) { under = u; ch = true; } if (!under) { var b = EditorGUILayout.ColorField("Bare colour", bare); if (b != bare) { bare = b; ch = true; } }
            ch |= Slider(ref dirt, "Corner dirt", "In the inward corners", 0, 1); if (dirt > 0) { ch |= Slider(ref dirtWidth, "Dirt width", "", 0.005f, 0.2f); var d = EditorGUILayout.ColorField("Dirt colour", dark); if (d != dark) { dark = d; ch = true; } } if (GUILayout.Button("Other chips")) { seed++; ch = true; } return ch;
        }
        public override void Prepare()
        {
            m_Under = under ? Pixels.ReadCached(under) : null; if (from != From.Model) { m_Out = m_In = null; return; } float reach = Mathf.Max(width, dirtWidth) * 1.6f; m_Size = 512; var mesh = model ? null : Resources.GetBuiltinResource<Mesh>("Cube.fbx"); string key = (model ? AssetDatabase.GetAssetPath(model) + model.name : "box") + "|" + angle.ToString("0") + "|" + reach.ToString("0.000"); (float[] o, float[] i) d; lock (s_Done) if (s_Done.TryGetValue(key, out d)) { m_Out = d.o; m_In = d.i; return; }
            var m = model ? TextureMesh.Of(model) : TextureMesh.Of(mesh); if (m == null) { m_Out = m_In = null; return; } d = TextureMesh.Edges(m, m_Size, angle, reach); lock (s_Done) { if (s_Done.Count > 12) s_Done.Clear(); s_Done[key] = d; } m_Out = d.o; m_In = d.i;
        }
        float At(float[] a, float u, float v) { float x = u * m_Size - 0.5f, y = v * m_Size - 0.5f; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0; float G(int i, int j) { i = ((i % m_Size) + m_Size) % m_Size; j = ((j % m_Size) + m_Size) % m_Size; return a[j * m_Size + i]; } return Mathf.Lerp(Mathf.Lerp(G(x0, y0), G(x0 + 1, y0), fx), Mathf.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy); }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (from == From.Model && m_Out == null) return p; float[] near = null, gap = null, paint = null; if (onlyPainted && strokes.Count(s => !s.erase) == 0) return p; if (from == From.Texture) TexEdges(p, out near, out gap); if (onlyPainted) paint = Painted(p.w, p.h); var n1 = new TextureOps.TileNoise(seed * 7 + 1, 9); var n2 = new TextureOps.TileNoise(seed * 7 + 2, 34); var n3 = new TextureOps.TileNoise(seed * 7 + 3, 5); var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int i = y * p.w + x; float u = (x + 0.5f) / p.w, v = (y + 0.5f) / p.h; var c = p.c[i]; float noise = n1.Fbm(u, v) * 0.55f + n2.Fbm(u, v) * 0.45f - 0.5f;
                    float lim = paint != null ? paint[i] : 1;
                    if (dirt > 0.001f && (from == From.Model || gap != null)) { float dn = (from == From.Model ? 1 - Mathf.Clamp01(At(m_In, u, v) / dirtWidth) : gap[i]) * lim, k = Mathf.Clamp01(dn * dn * 1.3f + (n3.Fbm(u, v) - 0.5f) * dn) * dirt; c = Color.Lerp(c, new Color(dark.r, dark.g, dark.b, c.a), k * 0.85f); }
                    float e = (from == From.Model ? 1 - Mathf.Clamp01(At(m_Out, u, v) / width) : from == From.Texture ? near[i] : 1 - Mathf.Clamp01(Mathf.Min(Mathf.Min(bottom ? v : 9, top ? 1 - v : 9), Mathf.Min(left ? u : 9, right ? 1 - u : 9)) / width)) * lim, m = Mathf.Clamp01((e * 1.25f - (1.05f - wear * 0.85f) + noise * rough * 1.5f * Mathf.Min(1, e * 4) - (0.5f - Pixels.Lum(c)) * 0.2f) * Mathf.Lerp(3f, 12f, rough) + 0.5f) * (e > 0 ? 1 : 0);
                    if (m > 0.001f) { var b = m_Under != null ? m_Under.Sample(u * m_Under.w, v * m_Under.h) : bare * (0.82f + 0.36f * n2.Fbm(u, v)); b.a = c.a; float rim = Mathf.Clamp01(1 - Mathf.Abs(m - 0.5f) * 2.4f); c = Color.Lerp(c, b, m); c = Color.Lerp(c, new Color(c.r * 0.6f, c.g * 0.58f, c.b * 0.56f, c.a), rim * 0.6f * rough); }
                    r.c[i] = c;
                }
            });
            return r;
        }
        public override TextureStep ForRecipe() { var c = (EdgeWearStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(EdgeWearStep)); c.model = null; c.strokes.Clear(); return c; }

        float[] Painted(int w, int h)
        {
            var m = new float[w * h]; for (int i = 0; i < strokes.Count;) { int j = i; while (j < strokes.Count && strokes[j].erase == strokes[i].erase) j++; var some = strokes.GetRange(i, j - i).Select(s => new TextureHeal.Stroke { radius = s.radius, points = s.points }).ToList(); var on = TextureHeal.Mask(some, w, h); var q = new Pixels(w, h, false); for (int k = 0; k < on.Length; k++) { float v = on[k] ? 1 : 0; q.c[k] = new Color(v, v, v, 1); } q = TextureOpsMore.Blur(q, 1.5f + w / 200f); bool er = strokes[i].erase; for (int k = 0; k < m.Length; k++) m[k] = er ? m[k] * (1 - q.c[k].r) : Mathf.Max(m[k], q.c[k].r); i = j; }
            return m;
        }

        /// <summary>How near each spot of a raised part is to a gap (only on the edges asked for), and where the gaps are.</summary>
        void TexEdges(Pixels p, out float[] near, out float[] gap)
        {
            int w = p.w, h = p.h, n = w * h; var a = TextureOpsMore.Blur(p, Mathf.Max(1f, w / 300f)); var b = TextureOpsMore.Blur(p, w / 22f); var d = new float[n]; double s = 0; for (int i = 0; i < n; i++) { d[i] = Pixels.Lum(a.c[i]) - Pixels.Lum(b.c[i]); s += d[i] * d[i]; } float sd = (float)Math.Sqrt(s / n) + 1e-4f;
            // (the gaps are whichever of light or dark covers less: mortar is thin whether it is lighter or darker than the bricks)
            int lo = 0, hi = 0; for (int i = 0; i < n; i++) { if (d[i] < -0.6f * sd) lo++; else if (d[i] > 0.6f * sd) hi++; } float sg = hi < lo ? -1 : 1;
            var gp = new float[n]; var g = new Pixels(w, h, false); for (int i = 0; i < n; i++) { gp[i] = Mathf.Clamp01((-sg * d[i] / sd - 0.35f) * 1.4f); g.c[i] = new Color(gp[i], 0, 0, 1); }
            var bl = TextureOpsMore.Blur(g, Mathf.Max(1.2f, width * w * 0.5f)); var nr = new float[n]; bool all = top && bottom && left && right; bool tp = top, bt = bottom, lf = left, rt = right;
            TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) { int i = y * w + x; float v = bl.c[i].r, k = Mathf.Clamp01((v - 0.05f) * 2.4f) * (1 - gp[i]); if (!all && k > 0) { float gx = bl[x + 1, y].r - bl[x - 1, y].r, gy = bl[x, y + 1].r - bl[x, y - 1].r, len = Mathf.Sqrt(gx * gx + gy * gy) + 1e-6f; gx /= len; gy /= len; float dir = (tp ? Mathf.Max(0, gy) : 0) + (bt ? Mathf.Max(0, -gy) : 0) + (rt ? Mathf.Max(0, gx) : 0) + (lf ? Mathf.Max(0, -gx) : 0); k *= Mathf.Clamp01(dir * 1.6f - 0.35f); } nr[i] = k; } });
            near = nr; gap = gp;
        }
    }
}
