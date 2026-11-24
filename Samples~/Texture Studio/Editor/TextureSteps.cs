using System;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>How a step is being run: for real (saving), or on a smaller copy to look at.</summary>
    public struct TextureRun
    {
        public bool final;
        /// <summary>How big this picture is compared to the real one (0.25 = a quarter of the pixels across).</summary>
        public float scale;
        public static readonly TextureRun Final = new TextureRun { final = true, scale = 1 };
    }

    /// <summary>One step in a Texture Studio session. Steps run top to bottom.</summary>
    [Serializable]
    public abstract class TextureStep
    {
        public bool on = true;
        /// <summary>With a foot kept (see Bands): -1 as this kind of step usually does, 0 changes the foot too, 1 leaves it alone.</summary>
        public int foot = -1;
        public virtual bool FootByDefault => false;
        /// <summary>The result is as big as what went in, and nothing has moved.</summary>
        public virtual bool SameSize => true;
        public abstract string Title { get; }
        /// <summary>One short line under the tool's tile: what it's for.</summary>
        public abstract string Blurb { get; }
        /// <summary>A sentence or two on what it does and when to use it (shown above its settings).</summary>
        public virtual string Explain => Blurb + ".";
        /// <summary>What it's set to, in a few words (shown on the step's block).</summary>
        public virtual string Summary => "";
        public abstract Color Tint { get; }
        /// <summary>Draws the settings; returns true when something changed.</summary>
        public abstract bool Settings();
        public abstract Pixels Run(Pixels p, TextureRun run);
        /// <summary>Called in the window before a copy of the step runs in the background: for things only the window may read.</summary>
        public virtual void Prepare() { }
        /// <summary>How many of the original's tiles the result is across.</summary>
        public virtual int Across => 1;
        /// <summary>The step as it goes in a saved recipe (null: it only makes sense for this one texture).</summary>
        public virtual TextureStep ForRecipe() => this;
        /// <summary>The step as it's shown when you point at the tool (null: nothing to show before it's set up).</summary>
        public virtual TextureStep Demo() => this;
        /// <summary>The saved texture should show hard pixels (no smoothing between them).</summary>
        public virtual bool HardPixels => false;
        public virtual Vector2Int SizeAfter(Vector2Int s) => s;

        protected static bool Slider(ref float v, string label, string tip, float min, float max)
        {
            float n = EditorGUILayout.Slider(new GUIContent(label, tip), v, min, max);
            if (Mathf.Approximately(n, v)) return false; v = n; return true;
        }
        protected static bool IntSlider(ref int v, string label, string tip, int min, int max)
        {
            int n = EditorGUILayout.IntSlider(new GUIContent(label, tip), v, min, max);
            if (n == v) return false; v = n; return true;
        }
        protected static bool Toggle(ref bool v, string label, string tip)
        {
            bool n = EditorGUILayout.ToggleLeft(new GUIContent(label, tip), v);
            if (n == v) return false; v = n; return true;
        }
        protected static bool Choice(ref int v, params string[] names)
        {
            int n = GUILayout.Toolbar(v, names, GUILayout.Height(24));
            if (n == v) return false; v = n; return true;
        }
        protected static string Pct(float v) => Mathf.RoundToInt(v * 100) + "%";
    }

    // ==================================================================== make it tile

    [Serializable]
    public class EvenLightingStep : TextureStep
    {
        public float strength = 0.6f;
        public override string Title => "Even lighting";
        public override string Blurb => "Takes out big bright and dark areas";
        public override bool FootByDefault => true;
        public override string Explain => "Photos are often lit from one side. When they repeat, that shows as a checkerboard of light and dark. This flattens the big light changes and keeps the detail. Use it first.";
        public override string Summary => Pct(strength);
        public override Color Tint => new Color(0.95f, 0.75f, 0.3f);
        public override bool Settings() => Slider(ref strength, "Strength", "How much of the uneven light to take out (a photo lit from one side shows as a checkerboard when it repeats)", 0, 1);
        public override Pixels Run(Pixels p, TextureRun run) => TextureOps.EvenLighting(p, strength);
    }

    [Serializable]
    public class SeamlessStep : TextureStep
    {
        public TextureOps.SeamStyle style = TextureOps.SeamStyle.Natural;
        public float blend = 0.25f;
        public bool keepPattern = true, alsoUp, evenLight; public TextureOps.Ways ways; public static string found = "";
        public static bool FootOn; // (with a foot kept it always goes sideways)
        public override bool FootByDefault => true;
        public override string Title => "Make it loop";
        public override string Blurb => "Repeats with no visible seam";
        public override string Explain => "Rebuilds the edges from the texture's own middle so the right edge flows into the left and the top into the bottom. Turn on Edges in the viewer to check.";
        public override string Summary => style + (style == TextureOps.SeamStyle.Mirror ? "" : ", " + Pct(blend) + " blend");
        public override Color Tint => new Color(0.35f, 0.78f, 0.5f);
        public override bool Settings()
        {
            int s = (int)style;
            bool ch = Choice(ref s, "Natural", "Soft", "Mirror", "Lines");
            if (s != (int)style && s == (int)TextureOps.SeamStyle.Lines) evenLight = true; style = (TextureOps.SeamStyle)s;
            EditorGUILayout.LabelField(style == TextureOps.SeamStyle.Natural ? "The join follows light and dark shapes, so stones and planks stay whole." : style == TextureOps.SeamStyle.Soft ? "A smooth fade: clouds, grass, noise." : style == TextureOps.SeamStyle.Mirror ? "Flips the picture: always perfect, but the symmetry shows." : "For stripes, slats, planks, fabric: makes the lines exactly straight and cuts at a whole number of them, so nothing bends at the join." + (found.Length > 0 ? "\nFound: " + found + "." : ""), EditorStyles.wordWrappedMiniLabel);
            ch |= Toggle(ref evenLight, "Even out the light first", "A photo is often lighter on one side: that shows as a stripe every time it repeats. This takes the big light and dark areas out first");
            int wy = (int)ways; if (!FootOn && Choice(ref wy, "Both ways", "Sideways", "Up and down")) { ways = (TextureOps.Ways)wy; ch = true; }
            using (new EditorGUI.DisabledScope(style == TextureOps.SeamStyle.Mirror))
            {
                ch |= Slider(ref blend, "Blend width", "How far in from the edges the blend reaches", 0.05f, 0.5f);
                if (style != TextureOps.SeamStyle.Lines) ch |= Toggle(ref keepPattern, "Keep bricks and tiles lined up", "If there's a repeating pattern (bricks, tiles, planks), it blends by whole bricks so nothing smears. Found automatically");
            }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (evenLight) p = TextureOps.EvenLighting(p, 1f, 4);
            if (style == TextureOps.SeamStyle.Lines) { var r = TextureLoopLines.Loop(p, blend, ways, out var f); found = f; return r; }
            return TextureOps.MakeSeamless(p, blend, style, keepPattern, ways);
        }
    }

    [Serializable]
    public class BreakRepeatsStep : TextureStep
    {
        public TextureOps.BreakSettings s = new TextureOps.BreakSettings();
        public override string Title => "Hide the repeat";
        public override string Blurb => "Same size or bigger, nothing catches the eye";
        public override string Explain => s.tiles <= 1 ? "For a texture that already loops: keeps its size and takes out the spots that stand out (they're what you recognise again and again), filling them with ordinary pieces of it. It still repeats, but nothing catches the eye. For a surface that never repeats, also pick Never repeats under A material." : "For a texture that already loops: makes a bigger one out of real pieces of it, joined where they match best, so the same spot doesn't keep showing up. It still loops.";
        public float calm = 0.5f, even = 0.4f; public bool mixPieces;
        public override bool FootByDefault => true;
        public override string Summary => s.tiles <= 1 ? "same size, " + Pct(calm) : s.tiles + "x bigger, pieces " + Pct(s.pieceSize);
        public override int Across => Mathf.Max(1, s.tiles);
        public override Color Tint => new Color(0.72f, 0.5f, 0.95f);
        public override bool Settings()
        {
            bool ch = false;
            int t = s.tiles - 1;
            EditorGUILayout.LabelField("Size", EditorStyles.miniBoldLabel);
            if (Choice(ref t, "Same size", "2 x", "3 x", "4 x")) { s.tiles = t + 1; ch = true; }
            if (s.tiles <= 1)
            {
                ch |= Slider(ref calm, "Take out", "How many of the spots that stand out are replaced", 0f, 1f);
                ch |= Slider(ref even, "Even out", "Makes every part as bright, colourful and contrasty as the rest, so no part stands out", 0f, 1f);
                ch |= Toggle(ref mixPieces, "Also mix the pieces", "Puts the whole texture together again from its own pieces");
                if (mixPieces) { ch |= Slider(ref s.pieceSize, "Piece size", "", 0.2f, 0.7f); ch |= Toggle(ref s.keepPattern, "Keep bricks and tiles lined up", ""); if (GUILayout.Button("Shuffle the pieces", GUILayout.Height(22))) { s.seed = UnityEngine.Random.Range(1, 99999); ch = true; } }
                return ch;
            }
            int nm = EditorGUILayout.IntPopup(new GUIContent("Largest size", "The saved texture is never bigger than this"), s.maxSize, new[] { new GUIContent("1024"), new GUIContent("2048"), new GUIContent("4096") }, new[] { 1024, 2048, 4096 });
            if (nm != s.maxSize) { s.maxSize = nm; ch = true; }
            ch |= Slider(ref s.pieceSize, "Piece size", "Bigger pieces keep big shapes whole; smaller pieces mix more", 0.2f, 0.7f);
            ch |= Toggle(ref s.keepPattern, "Keep bricks and tiles lined up", "Pieces only move by whole bricks, so rows stay straight. Found automatically");
            using (new EditorGUI.DisabledScope(s.keepPattern))
                ch |= Toggle(ref s.rotate, "Turn and flip pieces", "More variety, for things with no direction (rocks, gravel, moss)");
            ch |= Slider(ref s.brightnessVariation, "Light and dark areas", "Large gentle light and dark areas", 0, 0.3f);
            ch |= Slider(ref s.colourVariation, "Colour areas", "Large gentle colour shifts", 0, 0.1f);
            if (GUILayout.Button(new GUIContent("Shuffle the pieces", "Another random mix"), GUILayout.Height(22))) { s.seed = UnityEngine.Random.Range(1, 99999); ch = true; }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            return s.tiles > 1 ? Bigger(p, run) : Same(p, run);
        }
        public Pixels Same(Pixels p, TextureRun run)
        {
            var r = TextureRepeat.EvenOut(TextureCalm.Calm(p, calm), even);
            return mixPieces ? TextureVariations.Rearranged(r, s.pieceSize, s.rotate && !s.keepPattern, 0, s.seed, run.final) : r;
        }
        Pixels Bigger(Pixels p, TextureRun run) => TextureOps.BreakRepeats(p, s, run.final ? 0 : Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(p.w, p.h) * 1.75f), 128, 900));
        public override Vector2Int SizeAfter(Vector2Int z)
        {
            if (s.tiles <= 1) return z;
            int w = z.x * s.tiles, h = z.y * s.tiles; float k = Mathf.Min(1f, s.maxSize / (float)Mathf.Max(w, h));
            return new Vector2Int(Mathf.FloorToInt(w * k), Mathf.FloorToInt(h * k));
        }
    }

    [Serializable]
    public class ShiftStep : TextureStep
    {
        public float x = 0.5f, y = 0.5f;
        public override bool SameSize => false;
        public override string Title => "Move";
        public override string Blurb => "Slides it round to check the seam";
        public override string Explain => "Moves the picture (it wraps round), so the edges end up in the middle where you can see them. Handy to check or fix a seam.";
        public override string Summary => Pct(x) + ", " + Pct(y);
        public override Color Tint => new Color(0.5f, 0.7f, 0.75f);
        public override bool Settings()
        {
            bool ch = Slider(ref x, "Across", "", 0, 1);
            ch |= Slider(ref y, "Up", "", 0, 1);
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) => TextureOpsMore.Shift(p, x, y);
    }

    // ==================================================================== look

    [Serializable]
    public class ColourStep : TextureStep
    {
        public TextureOps.AdjustSettings a = new TextureOps.AdjustSettings();
        public override string Title => "Colour";
        public override string Blurb => "Brightness, contrast, hue, tint";
        public override TextureStep Demo() => new ColourStep { a = new TextureOps.AdjustSettings { saturation = 0.5f, hue = 0.12f, contrast = 0.15f } };
        public override string Explain => "Changes the colours: brighter, more contrast, more or less colourful, another hue, or tinted.";
        public override string Summary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (Mathf.Abs(a.brightness) > 0.01f) parts.Add("brightness"); if (Mathf.Abs(a.contrast) > 0.01f) parts.Add("contrast");
                if (Mathf.Abs(a.saturation) > 0.01f) parts.Add("saturation"); if (Mathf.Abs(a.hue) > 0.01f) parts.Add("hue");
                if (a.tint != Color.white) parts.Add("tint"); if (a.invert) parts.Add("inverted");
                return parts.Count == 0 ? "no change yet" : string.Join(", ", parts);
            }
        }
        public override Color Tint => new Color(0.95f, 0.45f, 0.45f);
        public override bool Settings()
        {
            bool ch = false;
            ch |= Slider(ref a.brightness, "Brightness", "", -0.5f, 0.5f);
            ch |= Slider(ref a.contrast, "Contrast", "", -0.9f, 1.5f);
            ch |= Slider(ref a.saturation, "Saturation", "How colourful", -1f, 1.5f);
            ch |= Slider(ref a.hue, "Hue", "Turns every colour round the colour wheel", -0.5f, 0.5f);
            ch |= Slider(ref a.gamma, "Mid tones", "Lighter or darker middle tones, keeping black and white", 0.3f, 3f);
            var t = EditorGUILayout.ColorField("Tint", a.tint); if (t != a.tint) { a.tint = t; ch = true; }
            ch |= Toggle(ref a.invert, "Invert", "Negative");
            if (GUILayout.Button("Reset", GUILayout.Width(70))) { a = new TextureOps.AdjustSettings(); ch = true; }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) => TextureOps.Adjust(p, a);
    }

    [Serializable]
    public class DirtStep : TextureStep
    {
        public float amount = 0.35f, cracks = 0.5f, patch = 1f; public Color dirt = new Color(0.45f, 0.38f, 0.3f); public int seed = 3;
        public override string Title => "Dirt and wear";
        public override string Blurb => "Grime in patches and in the cracks";
        public override string Explain => "Adds dirt in patches across the texture, and more of it in cracks and gaps, the way grime collects. It still loops.";
        public override string Summary => Pct(amount) + " patches, " + Pct(cracks) + " in cracks";
        public override Color Tint => new Color(0.65f, 0.55f, 0.4f);
        public override bool Settings()
        {
            bool ch = Slider(ref amount, "Patches", "Dirty patches across the texture", 0, 1);
            ch |= Slider(ref cracks, "In the cracks", "Dirt collected in dips, cracks and gaps", 0, 1);
            ch |= Slider(ref patch, "Patch size", "", 0.3f, 3f);
            var d = EditorGUILayout.ColorField(new GUIContent("Dirt colour", "Multiplied in: brown for mud, grey for soot, green for moss"), dirt); if (d != dirt) { dirt = d; ch = true; }
            if (GUILayout.Button("Shuffle", GUILayout.Width(80))) { seed = UnityEngine.Random.Range(1, 9999); ch = true; }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) => TextureOpsMore.Dirt(p, amount, cracks, patch, dirt, seed);
    }

    [Serializable]
    public class SharpenStep : TextureStep
    {
        public float amount = 0.8f, detail = 6f;
        public override string Title => "Sharpen";
        public override string Blurb => "Crisper detail";
        public override string Explain => "Makes details crisper. Detail size decides which ones: fine grain, or whole shapes.";
        public override string Summary => Pct(amount) + ", detail " + detail.ToString("0");
        public override Color Tint => new Color(0.55f, 0.75f, 0.95f);
        public override bool Settings() { bool ch = Slider(ref amount, "Amount", "", 0, 4); ch |= Slider(ref detail, "Detail size", "Small sharpens fine grain (only seen up close), big makes whole shapes stand out", 1f, 40f); return ch; }
        public override Pixels Run(Pixels p, TextureRun run) => TextureOps.Sharpen(p, amount, detail);
    }

    [Serializable]
    public class BlurStep : TextureStep
    {
        public float radius = 4f;
        public override string Title => "Blur";
        public override string Blurb => "Softer, less noisy";
        public override string Explain => "Softens the picture. A little takes out noise; a lot makes a soft background.";
        public override string Summary => radius.ToString("0") + " px";
        public override Color Tint => new Color(0.6f, 0.65f, 0.9f);
        public override bool Settings() => Slider(ref radius, "Amount", "In pixels of a 1024 texture", 0.5f, 40f);
        public override Pixels Run(Pixels p, TextureRun run) => TextureOpsMore.Blur(p, radius);
    }

    // ==================================================================== size and shape

    [Serializable]
    public class ResizeStep : TextureStep
    {
        public enum How { Crunchy, Harsh, Smooth }
        public int size = 256; public How how = How.Crunchy;
        static readonly int[] Sizes = { 2048, 1024, 512, 256, 128, 64, 32 };
        public override string Title => "Resize";
        public override string Blurb => "Fewer pixels: smaller, or crunchier";
        public override TextureStep Demo() => new ResizeStep { size = 64 };
        public override string Explain => "Changes how many pixels it has. Crunchy and Harsh keep hard pixels (the saved texture isn't smoothed either); Harsh skips pixels, so thin lines break up. Smooth just makes it smaller.";
        public override string Summary => size + " px, " + how.ToString().ToLower();
        public override Color Tint => new Color(0.7f, 0.7f, 0.72f);
        public override bool HardPixels => how != How.Smooth;
        public override bool Settings()
        {
            int h = (int)how;
            bool ch = Choice(ref h, "Crunchy", "Harsh", "Smooth"); how = (How)h;
            int i = Array.IndexOf(Sizes, size); if (i < 0) i = 3;
            EditorGUILayout.LabelField("Longest side, in pixels", EditorStyles.miniBoldLabel);
            if (Choice(ref i, Array.ConvertAll(Sizes, s => s.ToString()))) { size = Sizes[i]; ch = true; }
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            // (in the preview the picture keeps its size, so you see the pixels it'll really have)
            float k = size / (Mathf.Max(p.w, p.h) / Mathf.Max(0.0001f, run.scale));
            int real = Mathf.RoundToInt(size * 1f);
            int nw, nh;
            if (p.w >= p.h) { nw = real; nh = Mathf.Max(1, Mathf.RoundToInt(real * p.h / (float)p.w)); }
            else { nh = real; nw = Mathf.Max(1, Mathf.RoundToInt(real * p.w / (float)p.h)); }
            if (run.final) return Down(p, nw, nh);
            if (k >= 1 || (nw >= p.w && nh >= p.h)) return p; // (more pixels than the preview shows: nothing to see)
            var small = Down(p, nw, nh);
            return how != How.Smooth ? TextureOpsMore.Nearest(small, p.w, p.h) : small.Resized(p.w, p.h);
        }
        Pixels Down(Pixels p, int nw, int nh) => how == How.Smooth ? p.Resized(nw, nh) : how == How.Harsh ? TextureOpsMore.Nearest(p, nw, nh) : TextureOpsMore.Crisp(p, nw, nh);
        public override Vector2Int SizeAfter(Vector2Int z) { float k = size / (float)Mathf.Max(z.x, z.y); return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(z.x * k)), Mathf.Max(1, Mathf.RoundToInt(z.y * k))); }
    }

    [Serializable]
    public class CrunchStep : TextureStep
    {
        public int size = 128; public TextureOpsMore.Depth depth = TextureOpsMore.Depth.Bits15;
        public float dither = 1f, blocks = 0.35f, grain = 0.1f; public bool keepSize; public int seed = 1;
        static readonly int[] Sizes = { 0, 512, 256, 128, 64, 32 };
        public override string Title => "Crunch (old console)";
        public override string Blurb => "PS1 look: few colours, dither, blocks";
        public override string Explain => "Makes it look like it came off an old console: few pixels, few colours with a dither pattern, blocky compression and grain. Each one can be turned down to nothing.";
        public override string Summary => (size > 0 ? size + " px, " : "") + (depth == TextureOpsMore.Depth.Full ? "all colours" : depth == TextureOpsMore.Depth.Bits15 ? "15-bit" : depth == TextureOpsMore.Depth.Colours256 ? "256 colours" : "16 colours");
        public override Color Tint => new Color(0.9f, 0.55f, 0.75f);
        public override bool HardPixels => true;
        public override bool Settings()
        {
            bool ch = false;
            EditorGUILayout.LabelField("Pixels across", EditorStyles.miniBoldLabel);
            int i = Mathf.Max(0, Array.IndexOf(Sizes, size));
            if (Choice(ref i, "keep", "512", "256", "128", "64", "32")) { size = Sizes[i]; ch = true; }
            EditorGUILayout.LabelField("Colours", EditorStyles.miniBoldLabel);
            int d = (int)depth;
            if (Choice(ref d, "all", "15-bit", "256", "16")) { depth = (TextureOpsMore.Depth)d; ch = true; }
            using (new EditorGUI.DisabledScope(depth == TextureOpsMore.Depth.Full)) ch |= Slider(ref dither, "Dither", "The dotted pattern that fakes the colours in between", 0, 2);
            ch |= Slider(ref blocks, "Blocky", "Old texture compression: every 4 x 4 block gets two colours and the mixes between them", 0, 1);
            ch |= Slider(ref grain, "Grain", "Noise", 0, 1);
            ch |= Toggle(ref keepSize, "Keep the texture's size", "Saves it at its own size with big hard pixels, instead of really small");
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int side = Mathf.Max(p.w, p.h); int want = size > 0 ? Mathf.Min(size, side) : side;
            float k = want / (float)side;
            var small = want == side ? p : TextureOpsMore.Crisp(p, Mathf.Max(1, Mathf.RoundToInt(p.w * k)), Mathf.Max(1, Mathf.RoundToInt(p.h * k)));
            small = TextureOpsMore.Crunch(small, depth, dither, blocks, grain, seed);
            return (keepSize || !run.final) && small.w != p.w ? TextureOpsMore.Nearest(small, p.w, p.h) : small;
        }
        public override Vector2Int SizeAfter(Vector2Int z)
        {
            if (keepSize || size <= 0 || Mathf.Max(z.x, z.y) <= size) return z;
            float k = size / (float)Mathf.Max(z.x, z.y); return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(z.x * k)), Mathf.Max(1, Mathf.RoundToInt(z.y * k)));
        }
    }

    [Serializable]
    public class FlipTurnStep : TextureStep
    {
        public bool flipX, flipY; public int turns;
        public override string Title => "Flip and turn";
        public override string Blurb => "Mirror or rotate it";
        public override TextureStep Demo() => new FlipTurnStep { turns = 1 };
        public override string Explain => "Mirrors it or turns it by 90°, 180° or 270°.";
        public override string Summary => (flipX ? "flipped across, " : "") + (flipY ? "flipped up, " : "") + (turns * 90) + "°";
        public override Color Tint => new Color(0.8f, 0.65f, 0.5f);
        public override bool Settings()
        {
            bool ch = Toggle(ref flipX, "Flip left to right", "");
            ch |= Toggle(ref flipY, "Flip upside down", "");
            EditorGUILayout.LabelField("Turn", EditorStyles.miniBoldLabel);
            ch |= Choice(ref turns, "0°", "90°", "180°", "270°");
            return ch;
        }
        public override Pixels Run(Pixels p, TextureRun run) => TextureOpsMore.FlipTurn(p, flipX, flipY, turns);
        public override Vector2Int SizeAfter(Vector2Int z) => turns % 2 == 1 ? new Vector2Int(z.y, z.x) : z;
    }
}
