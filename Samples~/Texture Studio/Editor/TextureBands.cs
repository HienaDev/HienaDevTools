using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Parts of a wall texture that stay as they are: a foot (skirting, plinth) at the bottom and, if wanted, a
    /// top. The steps work on what's between.</summary>
    [Serializable]
    public class Bands
    {
        public bool on, fixSeam = true, apart; public float foot = 0.2f, top, soft = 0.015f;
        public Bands Copy() => JsonUtility.FromJson<Bands>(JsonUtility.ToJson(this));
        public string Key => on ? foot.ToString("0.0000") + "|" + top.ToString("0.0000") + "|" + soft.ToString("0.000") + fixSeam : "";
    }

    public static class TextureBands
    {
        public static Pixels Rows(Pixels p, int y0, int y1) { var r = new Pixels(p.w, Mathf.Max(1, y1 - y0), p.srgb); Array.Copy(p.c, y0 * p.w, r.c, 0, r.c.Length); return r; }
        public static void Split(Pixels p, Bands b, out int y0, out int y1) { y0 = Mathf.Clamp(Mathf.RoundToInt(b.foot * p.h), 0, p.h - 8); y1 = Mathf.Clamp(p.h - Mathf.RoundToInt(b.top * p.h), y0 + 8, p.h); }

        /// <summary>Does this step leave the foot alone?</summary>
        public static bool Around(TextureStep s, Bands b) => b != null && b.on && s.SameSize && (s.foot < 0 ? s.FootByDefault : s.foot == 1);

        public static Pixels Run(TextureStep s, Pixels p, TextureRun run, Bands b)
        {
            if (!Around(s, b)) return s.Run(p, run);
            Split(p, b, out int y0, out int y1); var rest = Rows(p, y0, y1); Pixels q;
            if (s is SeamlessStep sm) q = TextureOps.MakeSeamless(rest, sm.blend, sm.style, sm.keepPattern, sm.alsoUp ? TextureOps.Ways.Both : TextureOps.Ways.Sideways);
            else if (s is BreakRepeatsStep br) q = br.Same(rest, run);
            else if (s is VariationsStep) return p; // (made when saving)
            else q = s.Run(rest, run);
            if (q.w != rest.w || q.h != rest.h) return s.Run(p, run);
            return Together(p, q, y0, y1, b, s is SeamlessStep);
        }

        public static Pixels Together(Pixels p, Pixels rest, int y0, int y1, Bands b, bool seam)
        {
            var r = p.Copy(); int soft = Mathf.Max(1, Mathf.RoundToInt(b.soft * p.h));
            TextureOps.Rows(rest.h, y =>
            {
                // a short blend where it meets the foot and the top
                float k = 1; if (y0 > 0) k = Mathf.Min(k, (y + 1f) / soft); if (y1 < p.h) k = Mathf.Min(k, (rest.h - y) / (float)soft); k = Mathf.Clamp01(k);
                for (int x = 0; x < p.w; x++) { int i = (y0 + y) * p.w + x; r.c[i] = Color.Lerp(p.c[i], rest.c[y * p.w + x], k); }
            });
            if (seam && b.fixSeam)
            {
                if (y0 > 2) { var f = SeamColours(Rows(p, 0, y0)); Array.Copy(f.c, 0, r.c, 0, f.c.Length); }
                if (y1 < p.h - 2) { var t = SeamColours(Rows(p, y1, p.h)); Array.Copy(t.c, 0, r.c, y1 * p.w, t.c.Length); }
            }
            return r;
        }

        /// <summary>Makes the left and right edges meet without changing what's drawn: only the difference in colour between
        /// them is spread out over the last stretch on both sides.</summary>
        public static Pixels SeamColours(Pixels p, float width = 0.18f)
        {
            var r = p.Copy(); var d = new Color[p.h]; for (int y = 0; y < p.h; y++) d[y] = (p[0, y] * 2 + p[1, y] + p[p.w - 1, y] * -2 + p[p.w - 2, y] * -1) / 3f;
            // (smoothed up and down a little, so single odd pixels don't draw streaks)
            int rad = Mathf.Max(1, p.h / 60); var s = new Color[p.h]; for (int y = 0; y < p.h; y++) { Color a = Color.clear; int n = 0; for (int k = -rad; k <= rad; k++) { int q = y + k; if (q < 0 || q >= p.h) continue; a += d[q]; n++; } s[y] = a / n; }
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    float t = (x + 0.5f) / p.w, kr = Mathf.Clamp01((t - (1 - width)) / width), kl = Mathf.Clamp01((width - t) / width); kr = kr * kr * (3 - 2 * kr); kl = kl * kl * (3 - 2 * kl); var c = p.c[y * p.w + x]; var o = c + s[y] * (0.5f * (kr - kl));
                    r.c[y * p.w + x] = new Color(Mathf.Clamp01(o.r), Mathf.Clamp01(o.g), Mathf.Clamp01(o.b), c.a);
                }
            });
            return r;
        }

        /// <summary>Where the foot ends: the strongest straight line across in the lower part (0 = none found).</summary>
        public static float Find(Pixels p)
        {
            var s = p.Resized(64, 256); var row = new Vector3[256]; for (int y = 0; y < 256; y++) { var a = Vector3.zero; for (int x = 0; x < 64; x++) a += TexturePalette.Lab(s.c[y * 64 + x]); row[y] = a / 64; }
            float best = 0; int at = 0; float all = 0; int many = 0; var ds = new float[256];
            for (int y = 8; y < 250; y++)
            {
                Vector3 up = Vector3.zero, down = Vector3.zero; for (int k = 0; k < 5; k++) { up += row[Mathf.Min(255, y + k)]; down += row[y - 1 - k < 0 ? 0 : y - 1 - k]; }
                float d = ((up - down) / 5).magnitude; all += d; many++; ds[y] = d; if (y < 120 && d > best) best = d;
            }
            // (a foot often has a few lines: it ends at the highest of the strong ones)
            for (int y = 119; y > 8; y--) if (ds[y] >= best * 0.45f && ds[y] >= ds[y - 1] && ds[y] >= ds[y + 1]) { at = y; break; }
            return best > all / many * 1.8f ? at / 256f : 0f;
        }
    }

    /// <summary>Makes parts of the texture give light. It doesn't change the picture: it makes a glow map for the material.</summary>
    [Serializable]
    public class GlowStep : TextureStep, IPaintable
    {
        public List<TextureHeal.Stroke> strokes = new List<TextureHeal.Stroke>(); public float brush = 0.05f, tolerance = 0.12f, soft = 0.3f, bright = 0.75f, strength = 2f, spread = 0.15f; public List<Color> colours = new List<Color>(); public bool byBright, own = true; public Color colour = new Color(0.3f, 0.9f, 1f); public float hot = 0.4f;
        [NonSerialized] public bool picking; public List<GlowPart> parts = new List<GlowPart>(); public int sel = -1;
        public GlowPart Sel => sel >= 0 && sel < parts.Count ? parts[sel] : null;
        public override string Title => "Glow";
        public override string Blurb => "Parts that give light: lamps, runes, lava, screens";
        public override string Explain => "Brush on the left picture where it should glow (right-drag takes it away), or let it find the parts by colour or brightness. The picture itself doesn't change: saving makes a glow map and sets the material up. It only shines in the game where Bloom is on. To make it pulse or flicker, put a Glow Mover on the object.";
        public override string Summary => (colours.Count > 0 ? colours.Count + (colours.Count == 1 ? " colour" : " colours") + (strokes.Count > 0 || byBright ? ", " : "") : "") + (byBright ? "bright parts" + (strokes.Count > 0 ? ", " : "") : "") + (strokes.Count > 0 ? "painted" : "") + (colours.Count == 0 && !byBright && strokes.Count == 0 ? parts.Count == 0 ? "nothing yet" : "" : parts.Count > 0 ? ", " : "") + string.Join(", ", parts.Select(q => q.Name.ToLowerInvariant()).Take(3));
        public override Color Tint => new Color(0.45f, 0.95f, 1f);
        public override bool FootByDefault => false;
        public List<TextureHeal.Stroke> Strokes => strokes;
        public float Brush { get => brush; set => brush = value; }
        public bool Painting => true;
        public string PaintLabel => Sel != null && Sel.Drag ? "DRAG: " + Sel.Name.ToUpper() : Sel != null && Sel.Click ? "CLICK: " + Sel.Name.ToUpper() : picking ? "CLICK A COLOUR THAT SHOULD GLOW" : "BRUSH WHERE IT SHOULD GLOW";
        public Color PaintColour => new Color(0.3f, 0.95f, 1f, 0.35f);
        public bool Any => strokes.Count > 0 || colours.Count > 0 || byBright || parts.Any(q => q.on);
        public override void Prepare() { Keys(); foreach (var q in parts) q.Prepare(); }

        public override bool Settings()
        {
            bool ch = false; var wr = GUILayoutUtility.GetRect(10, 1, GUILayout.ExpandWidth(true)); if (Event.current.type == EventType.Layout) s_W = s_WNext; else if (Event.current.type == EventType.Repaint && wr.width > 50 && s_WNext != wr.width) { s_WNext = wr.width; }
            EditorGUILayout.LabelField("Add something that glows", EditorStyles.miniBoldLabel); float x = 0; EditorGUILayout.BeginHorizontal();
            foreach (var a in GlowPart.All) { float w = EditorStyles.miniButton.CalcSize(new GUIContent(a.name)).x + 2; if (x + w > s_W && x > 0) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); EditorGUILayout.BeginHorizontal(); x = 0; } x += w + 2; if (GUILayout.Button(new GUIContent(a.name, a.tip), EditorStyles.miniButton, GUILayout.Width(w))) { var q = new GlowPart { kind = a.kind, seed = UnityEngine.Random.Range(1, 9999) }; if (a.kind == GlowPart.Kind.Line || a.kind == GlowPart.Kind.Box) q.thick = 0.15f; if (a.kind == GlowPart.Kind.Text) q.art.text.text = "EXIT"; parts.Add(q); sel = parts.Count - 1; ch = true; } }
            GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); int gone = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                var q = parts[i]; var row = GUILayoutUtility.GetRect(10, 24, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(new Rect(row.x, row.y + 1, row.width, 22), new Color(Tint.r, Tint.g, Tint.b, i == sel ? 0.35f : 0.12f)); bool on = GUI.Toggle(new Rect(row.x + 4, row.y + 4, 16, 16), q.on, GUIContent.none); if (on != q.on) { q.on = on; ch = true; } if (q.own) EditorGUI.DrawRect(new Rect(row.xMax - 50, row.y + 6, 14, 12), q.colour); GUI.Label(new Rect(row.x + 24, row.y + 4, row.width - 80, 18), new GUIContent(q.Name + (q.join == GlowPart.Join.TakeAway ? " (takes away)" : q.join == GlowPart.Join.OnlyInside ? " (only inside)" : ""), q.Tip)); if (GUI.Button(new Rect(row.xMax - 26, row.y + 3, 22, 18), "\u2715", EditorStyles.miniButton)) gone = i; else if (GUI.Button(new Rect(row.x + 22, row.y, row.width - 76, 24), GUIContent.none, GUIStyle.none)) { sel = sel == i ? -1 : i; GUIUtility.keyboardControl = 0; }
                if (i == sel) { if (q.Click || q.Drag) EditorGUILayout.LabelField(q.Drag ? "Drag on the left picture to place it." : "Click on the left picture to place it.", EditorStyles.miniLabel); ch |= q.Settings(s_W); GUILayout.Space(6); }
            }
            if (gone >= 0) { parts.RemoveAt(gone); sel = -1; ch = true; }
            EditorGUILayout.LabelField("Paint it", EditorStyles.miniBoldLabel); Slider(ref brush, "Brush size", "The mouse wheel over the picture changes it too", 0.005f, 0.3f);
            EditorGUILayout.BeginHorizontal(); using (new EditorGUI.DisabledScope(strokes.Count == 0)) { if (GUILayout.Button("Remove last stroke")) { strokes.RemoveAt(strokes.Count - 1); ch = true; } if (GUILayout.Button("Clear")) { strokes.Clear(); ch = true; } } EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Find it by colour", EditorStyles.miniBoldLabel); EditorGUILayout.BeginHorizontal(); int rem = -1;
            for (int i = 0; i < colours.Count; i++) { var r = GUILayoutUtility.GetRect(26, 22, GUILayout.Width(26)); EditorGUI.DrawRect(r, colours[i]); if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { rem = i; Event.current.Use(); } }
            var bg = GUI.backgroundColor; if (picking) GUI.backgroundColor = new Color(0.45f, 0.95f, 1f); if (GUILayout.Button(new GUIContent(picking ? "Click on the picture..." : "Pick from the picture", "Then click a colour on the left picture. Click a colour here to take it out"), GUILayout.Height(22))) picking = !picking; GUI.backgroundColor = bg; EditorGUILayout.EndHorizontal();
            if (rem >= 0) { colours.RemoveAt(rem); ch = true; }
            if (colours.Count > 0) ch |= Slider(ref tolerance, "How alike", "How different a colour may be and still glow", 0.01f, 0.5f);
            ch |= Toggle(ref byBright, "The bright parts", "Everything brighter than this glows"); if (byBright) ch |= Slider(ref bright, "Brighter than", "", 0.1f, 0.99f);
            ch |= Slider(ref soft, "Soft edge", "", 0f, 1f);
            EditorGUILayout.LabelField("Its light", EditorStyles.miniBoldLabel); int o = own ? 0 : 1; if (Choice(ref o, "The texture's colours", "One colour")) { own = o == 0; ch = true; }
            if (!own) { var c = EditorGUILayout.ColorField("Colour", colour); if (c != colour) { colour = c; ch = true; } }
            ch |= Slider(ref hot, "Hot middle", "The strongest parts go white, like real light does", 0f, 1f);
            ch |= Slider(ref strength, "Strength", "How much light it gives in the game (over 1 is what Bloom picks up)", 0.2f, 12f);
            return ch;
        }
        static float s_W = 290, s_WNext = 300f;
        public override Pixels Run(Pixels p, TextureRun run) => p;

        /// <summary>How much every spot glows, 0..1.</summary>
        [Serializable] class Key { public List<TextureHeal.Stroke> strokes; public List<Color> colours; public float tolerance, soft, bright; public bool byBright; }
        [NonSerialized] string m_Key; public void Keys() { m_Key = "base" + JsonUtility.ToJson(new Key { strokes = strokes, colours = colours, tolerance = tolerance, soft = soft, bright = bright, byBright = byBright }); }
        public float[] Mask(Pixels p) => GlowPart.Cached((m_Key ?? "nokey" + Guid.NewGuid()) + "|" + GlowPart.Id(p) + "|" + p.w + "x" + p.h, () => Made(p));
        float[] Made(Pixels p)
        {
            var m = new float[p.c.Length]; var labs = new List<Vector3>(); foreach (var c in colours) labs.Add(TexturePalette.Lab(c));
            TextureOps.Rows(p.h, y =>
            {
                for (int x = 0; x < p.w; x++)
                {
                    int i = y * p.w + x; float v = 0; var c = p.c[i]; float edge = 0.015f + soft * 0.1f;
                    if (labs.Count > 0) { var l = TexturePalette.Lab(c); foreach (var k in labs) { var d = l - k; d.x *= 0.6f; v = Mathf.Max(v, Mathf.SmoothStep(1, 0, Mathf.Clamp01((d.magnitude - tolerance * 0.6f) / edge + 0.5f))); } }
                    if (byBright) v = Mathf.Max(v, Mathf.SmoothStep(0, 1, Mathf.Clamp01((Pixels.Lum(c) - bright) / (0.02f + soft * 0.25f) + 0.5f)));
                    m[i] = v;
                }
            });
            // what's painted on top, in the order it was painted
            for (int i = 0; i < strokes.Count;)
            {
                int j = i; while (j < strokes.Count && strokes[j].erase == strokes[i].erase) j++;
                var some = new List<TextureHeal.Stroke>(); for (int k = i; k < j; k++) some.Add(new TextureHeal.Stroke { radius = strokes[k].radius, points = strokes[k].points });
                var on = TextureHeal.Mask(some, p.w, p.h); var q = new Pixels(p.w, p.h, false); for (int k = 0; k < on.Length; k++) { float v = on[k] ? 1 : 0; q.c[k] = new Color(v, v, v, 1); }
                q = TextureOpsMore.Blur(q, 1f + soft * 14f); bool er = strokes[i].erase; for (int k = 0; k < m.Length; k++) m[k] = er ? m[k] * (1 - q.c[k].r) : Mathf.Max(m[k], q.c[k].r); i = j;
            }
            return m;
        }

        public Pixels Map(Pixels p)
        {
            var m = Mask(p); var r = new Pixels(p.w, p.h, true); for (int i = 0; i < m.Length; i++) { var c = own ? Vivid(p.c[i]) : colour; r.c[i] = new Color(c.r * m[i], c.g * m[i], c.b * m[i], 1); }
            foreach (var q in parts)
            {
                if (!q.on) continue; var k = q.Mask(p);
                for (int i = 0; i < k.Length; i++) { var o = r.c[i]; if (q.join == GlowPart.Join.TakeAway) o = new Color(o.r * (1 - k[i]), o.g * (1 - k[i]), o.b * (1 - k[i]), 1); else if (q.join == GlowPart.Join.OnlyInside) o = new Color(o.r * k[i], o.g * k[i], o.b * k[i], 1); else { var c = q.own ? q.colour : own ? Vivid(p.c[i]) : colour; float v = k[i]; o = new Color(Mathf.Max(o.r, c.r * v), Mathf.Max(o.g, c.g * v), Mathf.Max(o.b, c.b * v), 1); } r.c[i] = o; }
            }
            // the strongest parts go towards white, the way a bright lamp or lava does
            if (hot > 0.01f) for (int i = 0; i < r.c.Length; i++) { var o = r.c[i]; float m0 = Mathf.Max(o.r, Mathf.Max(o.g, o.b)), k = hot * m0 * m0 * m0 * 0.85f; r.c[i] = new Color(o.r + (m0 - o.r) * k, o.g + (m0 - o.g) * k, o.b + (m0 - o.b) * k, 1); }
            return r;
        }
        // (its own colour, but as bright as that colour goes: a dull red would glow brown)
        static Color Vivid(Color c) { float k = Mathf.Max(0.05f, Mathf.Max(c.r, Mathf.Max(c.g, c.b))); return Color.Lerp(c, c / k, 0.7f); }

        /// <summary>What it looks like with the lights down.</summary>
        public Pixels Dark(Pixels p)
        {
            // like Bloom in the game: a tight glow, a wide one and a soft haze, then squeezed so nothing clips flat
            var g = Map(p); float u = p.w / 512f; var a = TextureOpsMore.Blur(g, 4f * u); var near = TextureOpsMore.Blur(g, 14f * u); var far = TextureOpsMore.Blur(g, 48f * u); var r = new Pixels(p.w, p.h, true); float s = Mathf.Min(4f, strength);
            for (int i = 0; i < r.c.Length; i++) { var o = p.c[i] * 0.16f + g.c[i] * Mathf.Min(1.4f, s) + a.c[i] * (0.35f * s) + near.c[i] * (0.4f * s) + far.c[i] * (0.3f * s); r.c[i] = new Color(Squeeze(o.r), Squeeze(o.g), Squeeze(o.b), 1); }
            return r;
        }
        static float Squeeze(float x) => x <= 0.6f ? x : 0.6f + 0.4f * (1 - Mathf.Exp(-(x - 0.6f) * 2.5f));
        public override TextureStep ForRecipe() { var c = (GlowStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(GlowStep)); c.strokes.Clear(); foreach (var q in c.parts) q.clicks.Clear(); return c; }
        public override TextureStep Demo() => null;
    }
}
