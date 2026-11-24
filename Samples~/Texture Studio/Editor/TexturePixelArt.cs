using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Pixel art from any texture: a few big pixels, a small set of colours (its own, or a ready-made set), an
    /// optional checker dither, lone pixels cleaned away and dark outlines where shapes meet.</summary>
    [Serializable]
    public class PixelArtStep : TextureStep
    {
        public int across = 64, colours = 16, palette = -1; public float dither = 0.25f, outline = 0.5f; public bool clean = true, keepSize = true;
        static readonly int[] Sizes = { 16, 32, 64, 128, 256 };
        public override string Title => "Pixel art";
        public override string Blurb => "Big pixels, few colours, clean edges";
        public override string Explain => "Shrinks it to a few pixels, then picks a small set of colours (the texture's own, or a ready-made set) and puts every pixel on one of them. Lone pixels are cleaned away and shapes get a dark outline, the way pixel artists draw. It's saved with hard pixels.";
        public override string Summary => across + " pixels across, " + (palette < 0 ? colours == 0 ? "all colours" : colours + " colours" : TexturePalette.Ready[palette].name);
        public override Color Tint => new Color(0.55f, 0.85f, 0.45f);
        public override bool HardPixels => true;
        public override bool Settings()
        {
            bool ch = false; EditorGUILayout.LabelField("Pixels across", EditorStyles.miniBoldLabel); int i = Array.IndexOf(Sizes, across); if (i < 0) i = 2; if (Choice(ref i, "16", "32", "64", "128", "256")) { across = Sizes[i]; ch = true; }
            EditorGUILayout.LabelField("Colours", EditorStyles.miniBoldLabel); var names = new[] { "Its own colours" }.Concat(TexturePalette.Ready.Select(r => r.name)).ToArray(); int p = EditorGUILayout.Popup(palette + 1, names) - 1; if (p != palette) { palette = p; ch = true; }
            if (palette < 0) { int n = EditorGUILayout.IntSlider(new GUIContent("How many", "0 keeps every colour"), colours, 0, 32); if (n != colours) { colours = n; ch = true; } }
            ch |= Slider(ref dither, "Dither", "A checker pattern between two colours, for soft changes", 0, 1); ch |= Slider(ref outline, "Outlines", "Darker pixels where light and dark shapes meet", 0, 1); ch |= Toggle(ref clean, "Clean lone pixels", "A pixel unlike all four around it takes their colour"); ch |= Toggle(ref keepSize, "Keep the texture's size", "Saves it at its own size with big hard pixels, instead of really small");
            return ch;
        }
        static readonly float[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        public override Pixels Run(Pixels p, TextureRun run)
        {
            int side = Mathf.Max(p.w, p.h), want = Mathf.Min(across, side); float k = want / (float)side; int w = Mathf.Max(1, Mathf.RoundToInt(p.w * k)), h = Mathf.Max(1, Mathf.RoundToInt(p.h * k));
            var small = w == p.w ? p.Copy() : p.Resized(w, h);
            if (palette < 0 && colours == 0) return (keepSize || !run.final) && w != p.w ? TextureOpsMore.Nearest(small, p.w, p.h) : small;
            var pal = (palette >= 0 && palette < TexturePalette.Ready.Length ? TexturePalette.ReadyColours(palette) : TexturePalette.Extract(new[] { small }, colours)).OrderBy(Pixels.Lum).ToList(); if (pal.Count == 0) return p; var labs = pal.Select(TexturePalette.Lab).ToArray();
            // how far apart neighbouring colours are, so the dither nudges by about one step
            float spread = 0; for (int a = 1; a < labs.Length; a++) spread += (labs[a] - labs[a - 1]).magnitude; spread = labs.Length > 1 ? spread / (labs.Length - 1) : 0.1f;
            var idx = new int[w * h];
            TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) { var l = TexturePalette.Lab(small.c[y * w + x]); l.x += (Bayer[(y & 3) * 4 + (x & 3)] / 16f - 0.47f) * spread * dither; int best = 0; float bd = float.MaxValue; for (int q = 0; q < labs.Length; q++) { float d = (labs[q] - l).sqrMagnitude; if (d < bd) { bd = d; best = q; } } idx[y * w + x] = best; } });
            if (clean) { var c2 = (int[])idx.Clone(); for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int I(int xx, int yy) => idx[((yy + h) % h) * w + (xx + w) % w]; int a = I(x - 1, y), b = I(x + 1, y), c = I(x, y - 1), d = I(x, y + 1), me = idx[y * w + x]; if (a == b && b == c && c == d && a != me) c2[y * w + x] = a; } idx = c2; }
            if (outline > 0.01f)
            {
                // where a darker shape meets a lighter one, its edge pixel goes a step (or all the way) darker
                var o = (int[])idx.Clone(); float jump = Mathf.Lerp(0.25f, 0.08f, outline); int down = outline > 0.66f ? 99 : 1;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int me = idx[y * w + x]; bool edge = false; foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nb = idx[((y + dy + h) % h) * w + (x + dx + w) % w]; if (labs[nb].x - labs[me].x > jump) { edge = true; break; } } if (edge) o[y * w + x] = Mathf.Max(0, me - down); }
                idx = o;
            }
            var r = new Pixels(w, h, p.srgb); for (int i = 0; i < idx.Length; i++) { var c = pal[idx[i]]; c.a = small.c[i].a; r.c[i] = c; }
            return (keepSize || !run.final) && w != p.w ? TextureOpsMore.Nearest(r, p.w, p.h) : r;
        }
        public override Vector2Int SizeAfter(Vector2Int z) { if (keepSize || Mathf.Max(z.x, z.y) <= across) return z; float k = across / (float)Mathf.Max(z.x, z.y); return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(z.x * k)), Mathf.Max(1, Mathf.RoundToInt(z.y * k))); }
    }

    /// <summary>Click an object in the Scene view to take what it's made of (its texture, normal map and colour).</summary>
    public static class ScenePick
    {
        static Action<Material> s_Done; public static bool On => s_Done != null;
        public static void Start(Action<Material> done) { Stop(); s_Done = done; SceneView.duringSceneGui += Gui; var v = SceneView.lastActiveSceneView; if (v) { v.Focus(); v.Repaint(); } }
        public static void Stop() { if (s_Done != null) SceneView.duringSceneGui -= Gui; s_Done = null; }
        static void Gui(SceneView view)
        {
            var e = Event.current; HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive)); Handles.BeginGUI(); GUI.Label(new Rect(56, 8, 520, 24), "Click an object to take its texture     Esc: never mind", new GUIStyle(EditorStyles.helpBox) { fontSize = 12 }); Handles.EndGUI(); EditorGUIUtility.AddCursorRect(new Rect(0, 0, view.position.width, view.position.height), MouseCursor.ArrowPlus);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Stop(); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { var go = HandleUtility.PickGameObject(e.mousePosition, false); var r = go ? go.GetComponent<Renderer>() : null; var m = r ? r.sharedMaterial : null; var d = s_Done; Stop(); e.Use(); if (m) d?.Invoke(m); }
        }
        /// <summary>The texture, normal map and colour of a material, whatever its shader calls them.</summary>
        public static (Texture2D tex, Texture2D normal, Color colour, Vector2 scale) Of(Material m)
        {
            var sl = TextureSlots.Guess(m.shader); Texture2D T(params string[] names) { foreach (var n in names) if (n != "" && m.HasProperty(n) && m.GetTexture(n) is Texture2D t) return t; return null; }
            var tex = T(sl.colour, "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo") ?? m.mainTexture as Texture2D; var nrm = T(sl.normal, "_BumpMap", "_NormalMap"); var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white; var sc = sl.colour != "" && m.HasProperty(sl.colour) ? m.GetTextureScale(sl.colour) : Vector2.one; return (tex, nrm, col, sc);
        }
    }
}
