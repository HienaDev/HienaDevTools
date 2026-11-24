using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Variations of a looping texture that can be placed next to each other in any order: they all have exactly the
    /// same border, and something else inside. The inside comes from another texture, or from this one put together
    /// again from its own pieces in another way.
    /// </summary>
    public static class TextureVariations
    {
        public static Pixels Make(Pixels a, Pixels inside, float border)
        {
            var r = new Pixels(a.w, a.h, a.srgb); float bw = Mathf.Max(2, a.w * border), bh = Mathf.Max(2, a.h * border);
            var wobble = new TextureOps.TileNoise(431, 6);
            TextureOps.Rows(a.h, y =>
            {
                for (int x = 0; x < a.w; x++)
                {
                    float ex = Mathf.Min(x, a.w - 1 - x) / bw, ey = Mathf.Min(y, a.h - 1 - y) / bh, t = Mathf.Clamp01(Mathf.Min(ex, ey));
                    var ca = a.c[y * a.w + x]; var cb = inside.c[y * a.w + x];
                    // the join follows light and dark shapes and isn't a rectangle; the very edge is always the original's
                    float d = ((wobble.Fbm((x + 0.5f) / a.w, (y + 0.5f) / a.h) - 0.5f) * 2.2f + (Pixels.Lum(cb) - Pixels.Lum(ca)) * 0.4f) * 4 * t * (1 - t);
                    float k = t <= 0 ? 0 : Mathf.Clamp01((t - 0.5f + d) * 4f + 0.5f);
                    r.c[y * a.w + x] = Color.Lerp(ca, cb, k);
                }
            });
            return r;
        }

        /// <summary>This texture again, put together from its own pieces in another way.</summary>
        public static Pixels Rearranged(Pixels a, float pieces, bool turn, float tint, int seed, bool final)
        {
            var s = new TextureOps.BreakSettings { tiles = 1, maxSize = 16384, pieceSize = pieces, rotate = turn, keepPattern = !turn, brightnessVariation = 0, colourVariation = 0, seed = seed };
            // (full size either way; a preview compares fewer pieces)
            var r = TextureOps.BreakRepeats(a, s, final ? 0 : Mathf.Max(a.w, a.h));
            if (r.w != a.w || r.h != a.h) r = r.Resized(a.w, a.h);
            if (tint > 0) { var rnd = new System.Random(seed * 7 + 1); r = TextureOps.Adjust(r, new TextureOps.AdjustSettings { brightness = ((float)rnd.NextDouble() - 0.5f) * 0.16f * tint, hue = ((float)rnd.NextDouble() - 0.5f) * 0.05f * tint, saturation = ((float)rnd.NextDouble() - 0.5f) * 0.5f * tint }); }
            return r;
        }

        /// <summary>Another picture with this one's overall colours.</summary>
        public static Pixels MatchColours(Pixels p, Pixels to, float amount)
        {
            Stats(p, out var m0, out var d0); Stats(to, out var m1, out var d1);
            var r = new Pixels(p.w, p.h, p.srgb);
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    var c = p.c[y * p.w + x];
                    var o = new Color((c.r - m0.x) * d1.x / d0.x + m1.x, (c.g - m0.y) * d1.y / d0.y + m1.y, (c.b - m0.z) * d1.z / d0.z + m1.z);
                    o = Color.Lerp(c, new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b)), amount); o.a = c.a; r.c[y * p.w + x] = o;
                }
            });
            return r;
        }
        static void Stats(Pixels p, out Vector3 mean, out Vector3 dev)
        {
            double r = 0, g = 0, b = 0, rr = 0, gg = 0, bb = 0; int n = p.c.Length;
            foreach (var c in p.c) { r += c.r; g += c.g; b += c.b; rr += c.r * c.r; gg += c.g * c.g; bb += c.b * c.b; }
            mean = new Vector3((float)(r / n), (float)(g / n), (float)(b / n));
            dev = new Vector3(Mathf.Max(0.02f, Mathf.Sqrt(Mathf.Max(0, (float)(rr / n) - mean.x * mean.x))), Mathf.Max(0.02f, Mathf.Sqrt(Mathf.Max(0, (float)(gg / n) - mean.y * mean.y))), Mathf.Max(0.02f, Mathf.Sqrt(Mathf.Max(0, (float)(bb / n) - mean.z * mean.z))));
        }

        /// <summary>Which variation goes in each place: random, but not the same one twice in a row.</summary>
        public static int[] Layout(int grid, int kinds, float rare, int seed)
        {
            var rnd = new System.Random(seed * 13 + 5); var l = new int[grid * grid];
            for (int i = 0; i < l.Length; i++)
            {
                int x = i % grid, y = i / grid;
                for (int tries = 0; tries < 8; tries++)
                {
                    l[i] = kinds < 2 || rnd.NextDouble() < rare ? 0 : 1 + rnd.Next(kinds - 1);
                    bool same = l[i] > 0 && ((x > 0 && l[i - 1] == l[i]) || (y > 0 && l[i - grid] == l[i]) || (x == grid - 1 && l[i - x] == l[i]) || (y == grid - 1 && l[x] == l[i]));
                    if (!same) break;
                }
            }
            return l;
        }

        public static Pixels Bake(List<Pixels> tiles, int grid, float rare, int seed)
        {
            int w = tiles[0].w, h = tiles[0].h; var l = Layout(grid, tiles.Count, rare, seed); var r = new Pixels(w * grid, h * grid, tiles[0].srgb);
            TextureOps.Rows(grid * grid, i => { int gx = i % grid, gy = i / grid; var t = tiles[l[i]]; for (int y = 0; y < h; y++) Array.Copy(t.c, y * w, r.c, (gy * h + y) * r.w + gx * w, w); });
            return r;
        }
    }

    [Serializable]
    public class VariationsStep : TextureStep
    {
        public enum Save { OneBig, Separate, Both }
        public List<Texture2D> extras = new List<Texture2D>();
        public int made = 3, grid = 4, seed = 1, maxSize = 4096; public float border = 0.14f, pieces = 0.35f, tint = 0.08f, rare = 0.35f, match = 0.7f; public bool turn; public Save save = Save.Separate;
        [NonSerialized] List<Pixels> m_Read;
        public override bool FootByDefault => true;
        public override string Title => "Tile variations";
        public override string Blurb => "More tiles that fit next to this one";
        public override string Explain => "Makes more versions of a looping texture that fit next to each other in any order. They're put together again from this texture's own pieces, and you can add your own textures too. Use it last, on a texture that loops.";
        public override string Summary => (made + extras.Count(e => e)) + " more, " + (save == Save.Separate ? "same size" : "in a " + grid + " x " + grid);
        public override Color Tint => new Color(0.45f, 0.65f, 0.95f);
        public int Kinds => 1 + made + extras.Count(e => e);

        public override bool Settings()
        {
            bool ch = IntSlider(ref made, "Make from this", "How many to put together from this texture's own pieces", 0, 8);
            using (new EditorGUI.DisabledScope(made == 0))
            {
                ch |= Slider(ref pieces, "Piece size", "Bigger pieces keep big shapes whole", 0.2f, 0.7f);
                ch |= Slider(ref tint, "Colour change", "Each one a little lighter, darker or another tint", 0, 1);
                ch |= Toggle(ref turn, "Turn and flip pieces", "For things with no direction (rocks, gravel). Off keeps bricks and planks lined up");
            }
            EditorGUILayout.LabelField("Your own textures as variations", EditorStyles.miniBoldLabel);
            int remove = -1;
            for (int i = 0; i < extras.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var t = (Texture2D)EditorGUILayout.ObjectField(extras[i], typeof(Texture2D), false); if (t != extras[i]) { extras[i] = t; ch = true; }
                if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) { extras.RemoveAt(remove); ch = true; }
            if (GUILayout.Button("+ Add a texture")) extras.Add(null);
            using (new EditorGUI.DisabledScope(extras.Count == 0)) ch |= Slider(ref match, "Match colours", "Gives them this texture's overall colours", 0, 1);
            EditorGUILayout.Space(4);
            ch |= Slider(ref border, "Shared edge", "How much around the edge is the same on all of them (that's what makes them fit)", 0.05f, 0.35f);
            ch |= Slider(ref rare, "Original shows up", "How often the original is used between the variations", 0, 0.9f);
            ch |= IntSlider(ref grid, "Grid", "How many tiles across the big texture (and the preview)", 2, 8);
            if (GUILayout.Button("Shuffle")) { seed = UnityEngine.Random.Range(1, 99999); ch = true; }
            EditorGUILayout.LabelField("Save as", EditorStyles.miniBoldLabel);
            int s = (int)save; if (Choice(ref s, "One big", "Tiles, same size", "Both")) { save = (Save)s; ch = true; }
            return ch;
        }

        /// <summary>(textures can only be read by the window itself, not in the background)</summary>
        public override void Prepare() { m_Read = extras.Where(e => e).Select(e => Pixels.ReadCached(e)).ToList(); }

        /// <summary>The original and all its variations.</summary>
        public List<Pixels> Tiles(Pixels p, bool final)
        {
            var list = new List<Pixels> { p }; var own = new Pixels[Mathf.Max(0, made)];
            for (int i = 0; i < own.Length; i++) own[i] = TextureVariations.Make(p, TextureVariations.Rearranged(p, pieces, turn, tint, seed + i * 101 + 1, final), border);
            list.AddRange(own);
            foreach (var e in m_Read ?? new List<Pixels>())
            {
                var q = e.w == p.w && e.h == p.h ? e : e.Resized(p.w, p.h);
                if (match > 0) q = TextureVariations.MatchColours(q, p, match);
                list.Add(TextureVariations.Make(p, q, border));
            }
            return list;
        }

        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (run.final && save == Save.Separate) return p; // (the tiles are saved on their own, each as big as the original)
            int cap = run.final ? maxSize : Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(p.w, p.h) * 1.75f), 160, 900);
            float k = Mathf.Min(1f, cap / (float)(Mathf.Max(p.w, p.h) * grid));
            var q = k < 0.999f ? p.Resized(Mathf.Max(8, Mathf.FloorToInt(p.w * k)), Mathf.Max(8, Mathf.FloorToInt(p.h * k))) : p;
            return TextureVariations.Bake(Tiles(q, run.final), grid, rare, seed);
        }
        public override Vector2Int SizeAfter(Vector2Int z)
        {
            if (save == Save.Separate) return z;
            float k = Mathf.Min(1f, maxSize / (float)(Mathf.Max(z.x, z.y) * grid));
            return new Vector2Int(Mathf.Max(8, Mathf.FloorToInt(z.x * k)) * grid, Mathf.Max(8, Mathf.FloorToInt(z.y * k)) * grid);
        }
        public override int Across => grid;
        public override TextureStep ForRecipe() { var c = (VariationsStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(VariationsStep)); c.extras.Clear(); return c; }
    }
}
