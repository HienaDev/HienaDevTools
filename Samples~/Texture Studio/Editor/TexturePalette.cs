using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Recolours a picture with a small set of colours, so textures from different places look like one game.</summary>
    public static class TexturePalette
    {
        public enum How { Soft, Hard, ColourOnly }

        // a colour space where distances look right to the eye
        public static Vector3 Lab(Color c)
        {
            float r = Lin(c.r), g = Lin(c.g), b = Lin(c.b);
            float l = Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b), m = Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b), s = Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
            return new Vector3(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s, 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s, 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }
        public static Color Rgb(Vector3 v)
        {
            float l = v.x + 0.3963377774f * v.y + 0.2158037573f * v.z, m = v.x - 0.1055613458f * v.y - 0.0638541728f * v.z, s = v.x - 0.0894841775f * v.y - 1.2914855480f * v.z;
            l = l * l * l; m = m * m * m; s = s * s * s;
            return new Color(Gam(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s), Gam(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s), Gam(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s));
        }
        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        static float Gam(float v) { v = Mathf.Clamp01(v); return v <= 0.0031308f ? v * 12.92f : 1.055f * Mathf.Pow(v, 1f / 2.4f) - 0.055f; }
        static float Cbrt(float v) => Mathf.Pow(Mathf.Max(0, v), 1f / 3f);

        /// <summary>The colours a set of pictures uses most, dark to bright.</summary>
        public static List<Color> Extract(IEnumerable<Pixels> pictures, int n)
        {
            var pts = new List<Vector3>();
            foreach (var p in pictures) { int step = Mathf.Max(1, p.c.Length / 5000); for (int i = 0; i < p.c.Length; i += step) if (p.c[i].a > 0.1f) pts.Add(Lab(p.c[i])); }
            if (pts.Count == 0) return new List<Color>();
            n = Mathf.Clamp(n, 2, 48);
            // the starting colours are spread out: each one far from the ones already picked
            var rnd = new System.Random(11); var cent = new List<Vector3> { pts[rnd.Next(pts.Count)] }; var dist = pts.Select(q => (q - cent[0]).sqrMagnitude).ToArray();
            while (cent.Count < n)
            {
                double sum = 0; foreach (var d in dist) sum += d; if (sum < 1e-9) break;
                double at = rnd.NextDouble() * sum; int pick = 0; for (; pick < dist.Length - 1 && (at -= dist[pick]) > 0; pick++) { }
                cent.Add(pts[pick]); for (int i = 0; i < pts.Count; i++) dist[i] = Mathf.Min(dist[i], (pts[i] - pts[pick]).sqrMagnitude);
            }
            for (int it = 0; it < 10; it++)
            {
                var s = new Vector3[cent.Count]; var c = new int[cent.Count];
                foreach (var q in pts) { int b = Near(q, cent); s[b] += q; c[b]++; }
                for (int i = 0; i < cent.Count; i++) if (c[i] > 0) cent[i] = s[i] / c[i];
            }
            return cent.OrderBy(v => v.x).Select(Rgb).ToList();
        }
        static int Near(Vector3 q, List<Vector3> cent) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < cent.Count; i++) { float d = (cent[i] - q).sqrMagnitude; if (d < bd) { bd = d; b = i; } } return b; }

        public static Pixels Apply(Pixels p, List<Color> colours, How how, float strength, float dither)
        {
            if (colours == null || colours.Count == 0 || strength <= 0) return p;
            var pal = colours.Select(Lab).ToArray(); int n = pal.Length; var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    var c = p.c[y * p.w + x]; var q = Lab(c);
                    if (dither > 0 && how == How.Hard) q.x += (TextureOpsMore.Dots(x, y) - 0.5f) * dither * 0.12f;
                    Vector3 o;
                    if (how == How.Hard)
                    {
                        int b = 0; float bd = float.MaxValue; for (int i = 0; i < n; i++) { float d = (pal[i] - q).sqrMagnitude; if (d < bd) { bd = d; b = i; } }
                        o = pal[b];
                    }
                    else
                    {
                        // mixes the closest colours, so smooth changes stay smooth
                        o = Vector3.zero; float ws = 0;
                        for (int i = 0; i < n; i++)
                        {
                            var d = pal[i] - q; float dd = how == How.ColourOnly ? d.y * d.y + d.z * d.z + d.x * d.x * 0.25f : d.sqrMagnitude;
                            float w = 1f / (dd * dd + 1e-6f); o += pal[i] * w; ws += w;
                        }
                        o /= ws;
                        if (how == How.ColourOnly) o.x = q.x;
                    }
                    var f = Color.Lerp(c, Rgb(o), strength); f.a = c.a; r.c[y * p.w + x] = f;
                }
            });
            return r;
        }

        static Color H(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
        public static readonly (string name, string[] hex)[] Ready =
        {
            ("Moss and stone", new[] { "1c2119", "38402b", "5c6340", "8a8a5c", "b5ad86", "6b6660", "97928a", "d6cfbd" }),
            ("Dusk", new[] { "1a1530", "3b2350", "6e2f62", "b1485f", "e37a54", "f5b56b", "fbe3a8", "4d4a7d" }),
            ("Frost", new[] { "0f1b2b", "223a55", "3f6484", "6f98b3", "a4c4d3", "dbe9ee", "8c8fa8", "f7fbfc" }),
            ("Rust and dust", new[] { "1e1512", "452a1f", "7a3f26", "b3623a", "d69a62", "eccd9c", "5b5148", "8f8478" }),
            ("Candy", new[] { "3a1e4a", "7a3b8f", "d45fa6", "ff9ab5", "ffd6c2", "fff3c9", "8fd9c8", "5aa4d8" }),
            ("Four greens", new[] { "10231a", "2f5238", "77944a", "c5d08a" }),
            ("Ink and paper", new[] { "15141a", "4a4640", "9a9080", "efe6d2" }),
        };
        public static List<Color> ReadyColours(int i) => Ready[i].hex.Select(H).ToList();
    }

    [Serializable]
    public class PaletteStep : TextureStep
    {
        public List<Color> colours = new List<Color>();
        public string paletteName = "";
        public TexturePalette.How how = TexturePalette.How.Soft;
        public float strength = 0.85f, dither, hue, rich, light; public int count = 8;
        public Texture2D from;
        [NonSerialized] int m_Picked = -1;
        static Dictionary<string, List<Color>> s_Mine; static List<string> s_MineNames;
        /// <summary>Re-reads the saved palettes next time (one was saved or deleted somewhere else).</summary>
        public static void Forget() { s_Mine = null; }
        public override string Title => "Palette";
        public override string Blurb => "Recolour with a shared set of colours";
        public override string Explain => "Uses the same few colours on every texture, so things from different places fit together. Click a palette, drop a texture to take its colours, or make one from a single colour.";
        public override string Summary => colours.Count == 0 ? "no colours yet" : (paletteName.Length > 0 ? paletteName + ", " : "") + colours.Count + " colours";
        public override Color Tint => new Color(0.5f, 0.85f, 0.65f);

        /// <summary>The colours as used: moved by the sliders.</summary>
        public List<Color> Used()
        {
            if (Mathf.Abs(hue) < 0.001f && Mathf.Abs(rich) < 0.001f && Mathf.Abs(light) < 0.001f) return colours;
            return colours.Select(c => { var v = TexturePalette.Lab(c); float a = hue * Mathf.PI * 2, s = 1 + rich; float y = v.y * s, z = v.z * s; return TexturePalette.Rgb(new Vector3(Mathf.Clamp01(v.x + light * 0.4f), y * Mathf.Cos(a) - z * Mathf.Sin(a), y * Mathf.Sin(a) + z * Mathf.Cos(a))); }).ToList();
        }

        void Set(List<Color> c, string name) { colours = new List<Color>(c); paletteName = name; m_Picked = -1; hue = rich = light = 0; }

        public override bool Settings()
        {
            bool ch = false; var e = Event.current;
            // ---- the colours: left-click picks one, right-click takes it out
            var used = Used();
            if (used.Count > 0)
            {
                int per = Mathf.Clamp(used.Count, 1, 12), rows = (used.Count + per - 1) / per; var r = GUILayoutUtility.GetRect(10, rows * 30, GUILayout.ExpandWidth(true)); float cw = r.width / per; int remove = -1;
                for (int k = 0; k < used.Count; k++)
                {
                    var b = new Rect(r.x + k % per * cw, r.y + k / per * 30, cw - 1, 29); EditorGUI.DrawRect(b, used[k]);
                    if (k == m_Picked) { EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 2), Color.white); EditorGUI.DrawRect(new Rect(b.x, b.yMax - 2, b.width, 2), Color.white); EditorGUI.DrawRect(new Rect(b.x, b.y, 2, b.height), Color.white); EditorGUI.DrawRect(new Rect(b.xMax - 2, b.y, 2, b.height), Color.white); }
                    if (b.Contains(e.mousePosition) && e.type == EventType.MouseDown) { if (e.button == 1) remove = k; else m_Picked = m_Picked == k ? -1 : k; e.Use(); }
                    if (b.Contains(e.mousePosition) && e.type == EventType.ContextClick) e.Use(); // (no copy / paste menu here)
                }
                if (remove >= 0) { colours.RemoveAt(remove); m_Picked = -1; paletteName = ""; ch = true; }
                EditorGUILayout.BeginHorizontal();
                if (m_Picked >= 0 && m_Picked < colours.Count)
                {
                    var c = EditorGUILayout.ColorField(GUIContent.none, colours[m_Picked], true, false, false); if (c != colours[m_Picked]) { colours[m_Picked] = c; ch = true; paletteName = ""; }
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58))) { colours.RemoveAt(m_Picked); m_Picked = -1; ch = true; paletteName = ""; }
                }
                else EditorGUILayout.LabelField("Click a colour to change it, right-click to remove it", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("+", "Another colour"), EditorStyles.miniButton, GUILayout.Width(24))) { colours.Add(m_Picked >= 0 && m_Picked < colours.Count ? colours[m_Picked] : Color.gray); m_Picked = colours.Count - 1; ch = true; paletteName = ""; }
                EditorGUILayout.EndHorizontal();
                ch |= Slider(ref hue, "Turn the hues", "Moves every colour round the colour wheel", -0.5f, 0.5f); ch |= Slider(ref rich, "Richer / greyer", "", -1f, 1f); ch |= Slider(ref light, "Lighter / darker", "", -1f, 1f);
            }
            // ---- from a texture
            EditorGUILayout.LabelField("From a texture", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            var tx = (Texture2D)EditorGUILayout.ObjectField(from, typeof(Texture2D), false, GUILayout.Width(52), GUILayout.Height(52)); bool take = tx != from && tx; from = tx;
            EditorGUILayout.BeginVertical();
            if (GUILayout.Button(new GUIContent("Use this texture's own", "The colours the texture you're editing uses most")) && Source != null) { var s = Source(); if (s != null) { from = null; Set(TexturePalette.Extract(new[] { s }, count), ""); ch = true; } }
            int n = EditorGUILayout.IntSlider(new GUIContent("Colours", "How many to take"), count, 2, 24); if (n != count) { count = n; if (from) take = true; }
            EditorGUILayout.EndVertical(); EditorGUILayout.EndHorizontal();
            if (take) { Set(TexturePalette.Extract(new[] { Pixels.ReadCached(from) }, count), from.name); ch = true; }
            // ---- made for you
            EditorGUILayout.LabelField("Made for you", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Surprise me", "A new palette that goes together"))) { Set(Random(count, UnityEngine.Random.Range(1, 99999)), ""); ch = true; }
            if (GUILayout.Button(new GUIContent("Shades of one", "From dark to light around the picked (or first) colour"))) { Set(Shades(m_Picked >= 0 && m_Picked < colours.Count ? colours[m_Picked] : colours.Count > 0 ? colours[colours.Count / 2] : new Color(0.5f, 0.45f, 0.3f), count), ""); ch = true; }
            if (GUILayout.Button(new GUIContent("Paste", "Colours copied as text from anywhere, like #1a2b3c #ffee00"))) { var c = Parse(EditorGUIUtility.systemCopyBuffer); if (c.Count > 0) { Set(c, ""); ch = true; } else EditorUtility.DisplayDialog("Palette", "Copy some colours first, written like #1a2b3c (any text with them in it is fine).", "OK"); }
            EditorGUILayout.EndHorizontal();
            // ---- click one
            if (s_Mine == null) { s_MineNames = TextureShelf.Palettes; s_Mine = s_MineNames.ToDictionary(x => x, TextureShelf.LoadPalette); }
            EditorGUILayout.LabelField("Click one to use it", EditorStyles.miniBoldLabel);
            foreach (var m in s_MineNames) if (Row(m, s_Mine[m], true, out bool del)) { if (del) { if (EditorUtility.DisplayDialog("Delete palette", "Delete the palette \"" + m + "\"? Textures already made with it don't change.", "Delete", "Cancel")) { TextureShelf.DeletePalette(m); s_Mine = null; return ch; } } else { Set(s_Mine[m], m); ch = true; } }
            for (int i = 0; i < TexturePalette.Ready.Length; i++) if (Row(TexturePalette.Ready[i].name, TexturePalette.ReadyColours(i), false, out _)) { Set(TexturePalette.ReadyColours(i), TexturePalette.Ready[i].name); ch = true; }
            using (new EditorGUI.DisabledScope(colours.Count == 0)) if (GUILayout.Button(new GUIContent("Save this palette...", "Keeps these colours under a name, for every texture in every project")))
                EditorApplication.delayCall += () => { var nm = NamePrompt.Ask("Save palette", "Name for this palette", paletteName.Length > 0 ? paletteName : "My palette", x => TextureShelf.HasPalette(x) ? "There's one with that name: it'll be replaced." : ""); if (nm != null) { TextureShelf.SavePalette(nm, Used()); paletteName = nm; s_Mine = null; Poke?.Invoke(); } };
            // ---- how
            EditorGUILayout.Space(4);
            int h = (int)how; if (Choice(ref h, "Soft", "Hard", "Colour only")) { how = (TexturePalette.How)h; ch = true; }
            EditorGUILayout.LabelField(how == TexturePalette.How.Soft ? "Mixes the closest colours, smooth changes stay smooth." : how == TexturePalette.How.Hard ? "Only exactly these colours." : "Keeps the texture's own light and dark, changes its colours.", EditorStyles.wordWrappedMiniLabel);
            ch |= Slider(ref strength, "Strength", "", 0, 1);
            if (how == TexturePalette.How.Hard) ch |= Slider(ref dither, "Dither", "A dotted pattern between two colours", 0, 2);
            return ch;
        }

        static bool Row(string name, List<Color> cs, bool canDelete, out bool delete)
        {
            delete = false; var r = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true)); var e = Event.current; var strip = new Rect(r.x + 104, r.y + 2, r.width - 104 - (canDelete ? 24 : 0), r.height - 4);
            if (r.Contains(e.mousePosition)) EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.08f));
            GUI.Label(new Rect(r.x + 2, r.y + 3, 100, 18), name, EditorStyles.miniLabel); float w = strip.width / Mathf.Max(1, cs.Count); for (int i = 0; i < cs.Count; i++) EditorGUI.DrawRect(new Rect(strip.x + i * w, strip.y, w, strip.height), cs[i]);
            if (canDelete && GUI.Button(new Rect(r.xMax - 22, r.y + 2, 20, 18), "\u2715", EditorStyles.miniButton)) { delete = true; return true; }
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static List<Color> Parse(string text)
        {
            var l = new List<Color>(); if (string.IsNullOrEmpty(text)) return l;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"(?<![0-9a-fA-F])[0-9a-fA-F]{6}(?![0-9a-fA-F])")) { if (ColorUtility.TryParseHtmlString("#" + m.Value, out var c) && l.Count < 48) l.Add(c); }
            return l;
        }
        /// <summary>Dark to light around one colour: the darks a bit cooler and richer, the lights a bit warmer.</summary>
        public static List<Color> Shades(Color c, int n)
        {
            var v = TexturePalette.Lab(c); float a = Mathf.Atan2(v.z, v.y), r = new Vector2(v.y, v.z).magnitude; var l = new List<Color>();
            for (int i = 0; i < n; i++) { float t = n > 1 ? i / (float)(n - 1) : 0.5f, k = r * (0.55f + 0.9f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.2f, 0.85f, t))); float q = a + (t - 0.5f) * 0.5f; l.Add(TexturePalette.Rgb(new Vector3(Mathf.Lerp(0.18f, 0.95f, t), k * Mathf.Cos(q), k * Mathf.Sin(q)))); }
            return l;
        }
        /// <summary>Two or three hues that go together, each from dark to light.</summary>
        public static List<Color> Random(int n, int seed)
        {
            var rnd = new System.Random(seed); float first = (float)rnd.NextDouble() * Mathf.PI * 2; float[] steps = rnd.Next(3) == 0 ? new[] { 0f, Mathf.PI } : rnd.Next(2) == 0 ? new[] { 0f, 0.6f, -0.6f } : new[] { 0f, 2.4f, -2.4f }; var l = new List<Color>(); float richness = 0.05f + (float)rnd.NextDouble() * 0.1f;
            for (int i = 0; i < n; i++) { float t = n > 1 ? i / (float)(n - 1) : 0.5f, q = first + steps[i % steps.Length] + (t - 0.5f) * 0.3f, k = richness * (0.6f + 0.7f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 0.9f, t))); l.Add(TexturePalette.Rgb(new Vector3(Mathf.Lerp(0.2f, 0.93f, t), k * Mathf.Cos(q), k * Mathf.Sin(q)))); }
            return l;
        }

        public override Pixels Run(Pixels p, TextureRun run) => TexturePalette.Apply(p, Used(), how, strength, dither);
        public override TextureStep ForRecipe() { var c = (PaletteStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(PaletteStep)); c.from = null; return c; }

        /// <summary>(the window fills these in: the texture being edited, and how to say something changed from a menu)</summary>
        public static Func<Pixels> Source; public static Action Poke;
    }

    /// <summary>One palette library for the whole Texture Studio: your saved palettes, the ready ones, gradients, retro
    /// palettes and the effects' own colours. Any colour list (palette step, gradient map, dithering, flipbooks) can pick
    /// from it and save into it.</summary>
    public static class PaletteLibrary
    {
        public static List<(string group, string name, List<Color> cols)> All()
        {
            var l = new List<(string, string, List<Color>)>();
            foreach (var n in TextureShelf.Palettes) { var c = TextureShelf.LoadPalette(n); if (c != null && c.Count > 0) l.Add(("My palettes", n, c)); }
            for (int i = 0; i < TexturePalette.Ready.Length; i++) l.Add(("Ready", TexturePalette.Ready[i].name, TexturePalette.ReadyColours(i)));
            foreach (var g in GradientMapStep.Ready) l.Add(("Gradients", g.name, g.c.Select(FxOps.Hex).ToList()));
            foreach (var d in DitherStep.Palettes) if (d.hex != null) l.Add(("Retro", d.name, d.hex.Select(FxOps.Hex).ToList()));
            for (int k = 0; k < (int)TextureMoving.Kind.Scroll; k++) l.Add(("Effects", TextureMoving.Names[k], TextureMoving.Ramp((TextureMoving.Kind)k).Item1.ToList()));
            return l;
        }
        /// <summary>Dark to bright, the way a gradient or a flame reads its colours.</summary>
        public static List<Color> DarkToBright(List<Color> c) => c.OrderBy(x => TexturePalette.Lab(x).x).ToList();
        /// <summary>At most n colours, spread evenly through the list.</summary>
        public static List<Color> AtMost(List<Color> c, int n) => c.Count <= n ? c : Enumerable.Range(0, n).Select(i => c[Mathf.RoundToInt(i * (c.Count - 1) / (float)(n - 1))]).ToList();
        public static void Menu(Action<List<Color>, string> pick)
        {
            var m = new GenericMenu(); var all = All(); if (!all.Any(p => p.group == "My palettes")) m.AddDisabledItem(new GUIContent("My palettes/(none saved yet)"));
            foreach (var p in all) { var q = p; m.AddItem(new GUIContent(q.group + "/" + q.name), false, () => { pick(q.cols, q.name); PaletteStep.Poke?.Invoke(); }); }
            m.ShowAsContext();
        }
        public static void SaveAs(List<Color> colours, string suggested)
        {
            var c = colours.ToList(); EditorApplication.delayCall += () => { var nm = NamePrompt.Ask("Save palette", "Name for this palette", string.IsNullOrEmpty(suggested) ? "My palette" : suggested, x => TextureShelf.HasPalette(x) ? "There's one with that name: it'll be replaced." : ""); if (nm != null) { TextureShelf.SavePalette(nm, c); PaletteStep.Forget(); } };
        }
        /// <summary>"Palette library" and "Save as a palette" buttons on one row.</summary>
        public static void Buttons(Action<List<Color>, string> pick, List<Color> current, string name = "")
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Palette library \u25be", "Your saved palettes and the ready ones: the same library as the Palette tool on the Edit page"), EditorStyles.miniButton)) Menu(pick);
            if (GUILayout.Button(new GUIContent("Save as a palette", "Keeps these colours in the library, for every tool and every project"), EditorStyles.miniButton)) SaveAs(current, name);
            GUILayout.EndHorizontal();
        }
    }
}
