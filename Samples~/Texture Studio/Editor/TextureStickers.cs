using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A picture to put on something: one of yours, or one that's drawn by itself (a new one every time).</summary>
    [Serializable]
    public class StickerArt
    {
        public enum Kind { Mine, Splat, Drips, Stain, Scratches, Cracks, BulletHole, Text }
        public TextArt text = new TextArt();
        public enum Back { Keep, TakeOut }
        public Kind kind = Kind.Splat; public Texture2D picture; public Back back = Back.Keep; public bool pickColour; public Color backColour = Color.white; public float tolerance = 0.15f, soft = 0.2f;
        public Color colour = new Color(0.42f, 0.03f, 0.03f);

        public static readonly (Kind kind, string name, Color colour)[] Drawn =
        {
            (Kind.Splat, "Splat", new Color(0.42f, 0.03f, 0.03f)), (Kind.Drips, "Drips", new Color(0.36f, 0.02f, 0.02f)), (Kind.Stain, "Stain", new Color(0.25f, 0.17f, 0.09f)),
            (Kind.Scratches, "Scratches", new Color(0.85f, 0.83f, 0.8f)), (Kind.Cracks, "Cracks", new Color(0.06f, 0.05f, 0.05f)), (Kind.BulletHole, "Bullet hole", new Color(0.12f, 0.11f, 0.1f)),
        };
        public string Name => kind == Kind.Text ? text.text.Split('\n')[0] : kind == Kind.Mine ? picture ? picture.name : "your picture" : Drawn.First(d => d.kind == kind).name;
        public StickerArt Copy() => JsonUtility.FromJson<StickerArt>(JsonUtility.ToJson(this));

        // (reading a texture can only happen in the window; what was read is shared with the background)
        static readonly Dictionary<string, Pixels> s_Ready = new Dictionary<string, Pixels>(); static readonly Dictionary<string, Texture2D> s_Pics = new Dictionary<string, Texture2D>();
        bool Fixed => kind == Kind.Mine || kind == Kind.Text;
        string Key(int seed) => kind == Kind.Text ? "t" + text.Key : kind == Kind.Mine ? "m" + (picture ? AssetDatabase.GetAssetPath(picture) : "") + back + pickColour + ColorUtility.ToHtmlStringRGB(backColour) + tolerance.ToString("0.000") + soft.ToString("0.000") : kind + "|" + seed;
        [NonSerialized] string m_Key;

        public void Prepare()
        {
            if (!Fixed) return; m_Key = Key(0); lock (s_Ready) if (s_Ready.ContainsKey(m_Key)) return;
            if (kind == Kind.Text) { var d = text.Draw(); if (d != null) lock (s_Ready) s_Ready[m_Key] = d; return; }
            if (!picture) return;
            var p = Pixels.ReadCached(picture); float k = Mathf.Min(1f, 768f / Mathf.Max(p.w, p.h)); p = TextureAlpha.Bleed(p); if (k < 1) p = p.Resized(Mathf.RoundToInt(p.w * k), Mathf.RoundToInt(p.h * k));
            if (back == Back.TakeOut) p = TextureAlpha.CutColour(p, pickColour ? backColour : TextureAlpha.Corner(p), tolerance, soft);
            lock (s_Ready) { if (s_Ready.Count > 400) s_Ready.Clear(); s_Ready[m_Key] = p; }
        }

        /// <summary>The picture with its see-through parts (null when there's none yet). Drawn ones are white to grey: the colour goes on later.</summary>
        public Pixels Get(int seed, int side = 192)
        {
            string key = Fixed ? m_Key : Key(seed) + "|" + side; if (key == null) return null;
            lock (s_Ready) if (s_Ready.TryGetValue(key, out var p)) return p;
            if (Fixed) return null;
            var d = Draw(kind, seed, side); lock (s_Ready) { if (s_Ready.Count > 400) s_Ready.Clear(); s_Ready[key] = d; }
            return d;
        }

        /// <summary>For the window: the picture in its colour.</summary>
        public Texture2D Picture(int seed, int side = 192)
        {
            Prepare(); string key = Key(Fixed ? 0 : seed) + "|" + side + "#" + ColorUtility.ToHtmlStringRGB(colour); if (s_Pics.TryGetValue(key, out var t) && t) return t;
            var p = Get(seed, side); if (p == null) return null; var q = p.Copy(); if (kind != Kind.Mine) for (int i = 0; i < q.c.Length; i++) q.c[i] = Tinted(q.c[i], colour);
            if (s_Pics.Count > 200) { foreach (var o in s_Pics.Values) if (o) UnityEngine.Object.DestroyImmediate(o); s_Pics.Clear(); }
            t = q.ToTexture(); t.wrapMode = TextureWrapMode.Clamp; return s_Pics[key] = t;
        }
        public static Color Tinted(Color shade, Color c) { float v = shade.r; var o = v < 0.5f ? Color.Lerp(Color.black, c, v * 2) : Color.Lerp(c, Color.white, (v - 0.5f) * 0.7f); o.a = shade.a; return o; }

        // ------------------------------------------------------------------ drawn by itself

        static Pixels Draw(Kind kind, int seed, int n)
        {
            var rnd = new System.Random(seed * 7919 + (int)kind); float R() => (float)rnd.NextDouble(); var a = new float[n * n]; var shade = new float[n * n]; for (int i = 0; i < shade.Length; i++) shade[i] = 0.5f;
            var noise = new TextureOps.TileNoise(seed + 11, 5); var fine = new TextureOps.TileNoise(seed + 77, 17);
            float N(float u, float v) => noise.Fbm(u, v); float F(float u, float v) => fine.Fbm(u, v);
            void Blob(float cx, float cy, float rad, float wobble)
            {
                float ph = R() * 6.28f, p2 = R() * 6.28f, p3 = R() * 6.28f; int x0 = Mathf.Max(0, (int)((cx - rad * 1.6f) * n)), x1 = Mathf.Min(n - 1, (int)((cx + rad * 1.6f) * n)), y0 = Mathf.Max(0, (int)((cy - rad * 1.6f) * n)), y1 = Mathf.Min(n - 1, (int)((cy + rad * 1.6f) * n));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) / n - cx, dy = (y + 0.5f) / n - cy, d = Mathf.Sqrt(dx * dx + dy * dy), an = Mathf.Atan2(dy, dx);
                        float r = rad * (1 + wobble * (0.5f * Mathf.Sin(an * 3 + ph) + 0.35f * Mathf.Sin(an * 5 + p2) + 0.3f * Mathf.Sin(an * 9 + p3))), k = Mathf.Clamp01((r - d) * n * 0.45f + 0.5f); a[y * n + x] = Mathf.Max(a[y * n + x], k);
                    }
            }
            void Line(Vector2 p, Vector2 q, float wide0, float wide1, float strength)
            {
                float m = Mathf.Max(wide0, wide1) + 0.02f; int x0 = Mathf.Max(0, (int)((Mathf.Min(p.x, q.x) - m) * n)), x1 = Mathf.Min(n - 1, (int)((Mathf.Max(p.x, q.x) + m) * n)), y0 = Mathf.Max(0, (int)((Mathf.Min(p.y, q.y) - m) * n)), y1 = Mathf.Min(n - 1, (int)((Mathf.Max(p.y, q.y) + m) * n)); var dir = q - p; float dd = Mathf.Max(1e-6f, dir.sqrMagnitude);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { var at = new Vector2((x + 0.5f) / n, (y + 0.5f) / n); float t = Mathf.Clamp01(Vector2.Dot(at - p, dir) / dd), w = Mathf.Lerp(wide0, wide1, t), d = (at - p - dir * t).magnitude, k = Mathf.Clamp01((w - d) * n * 0.6f + 0.5f) * strength; if (k > a[y * n + x]) a[y * n + x] = k; }
            }
            switch (kind)
            {
                case Kind.Splat:
                {
                    float size = 0.13f + R() * 0.07f; Blob(0.5f, 0.5f, size, 0.35f); int rays = 9 + rnd.Next(9);
                    for (int i = 0; i < rays; i++)
                    {
                        float an = R() * 6.283f, len = size * (1.1f + R() * R() * 2.2f); var dir = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); var mid = new Vector2(0.5f, 0.5f);
                        if (len < size * 2.1f) Line(mid, mid + dir * len, size * 0.35f, 0.006f + R() * 0.012f, 1); // a streak that hangs on
                        Blob(0.5f + dir.x * len, 0.5f + dir.y * len, 0.008f + R() * R() * 0.035f, 0.3f);
                    }
                    for (int i = 0; i < 26; i++) { float an = R() * 6.283f, d = size + R() * 0.3f; float px = 0.5f + Mathf.Cos(an) * d, py = 0.5f + Mathf.Sin(an) * d; if (px > 0.03f && px < 0.97f && py > 0.03f && py < 0.97f) Blob(px, py, 0.003f + R() * R() * 0.013f, 0.2f); }
                    break;
                }
                case Kind.Drips:
                {
                    int drips = 5 + rnd.Next(4); Blob(0.5f, 0.82f, 0.11f, 0.3f); Blob(0.36f, 0.84f, 0.07f, 0.3f); Blob(0.64f, 0.83f, 0.08f, 0.3f);
                    for (int i = 0; i < drips; i++) { float x = 0.26f + 0.48f * (i + R() * 0.7f) / drips, len = 0.15f + R() * 0.55f, w = 0.008f + R() * 0.014f; var p = new Vector2(x, 0.82f); for (int s = 0; s < 8; s++) { var q = new Vector2(p.x + (R() - 0.5f) * 0.012f, p.y - len / 8); Line(p, q, w, w * 0.96f, 1); p = q; } Blob(p.x, p.y, w * 1.5f, 0.1f); }
                    break;
                }
                case Kind.Stain:
                    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n, d = new Vector2(u - 0.5f, v - 0.5f).magnitude, e = 0.36f + (N(u, v) - 0.5f) * 0.28f - d; float inside = Mathf.Clamp01(e * 9), rim = Mathf.Exp(-Mathf.Pow((e - 0.02f) / 0.022f, 2));
                            a[y * n + x] = Mathf.Clamp01(inside * (0.28f + 0.3f * F(u, v)) + rim * 0.6f); shade[y * n + x] = 0.5f - rim * 0.2f;
                        }
                    break;
                case Kind.Scratches:
                {
                    int many = 5 + rnd.Next(7); float main = R() * 3.14f;
                    for (int i = 0; i < many; i++) { float an = main + (R() - 0.5f) * 0.7f, len = 0.15f + R() * 0.55f; var c = new Vector2(0.5f + (R() - 0.5f) * 0.4f, 0.5f + (R() - 0.5f) * 0.4f); var dir = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); var p = c - dir * len / 2; float w = 0.002f + R() * 0.005f; int parts = 10; for (int s = 0; s < parts; s++) { var q = p + dir * len / parts + new Vector2(-dir.y, dir.x) * (R() - 0.5f) * 0.006f; float t = s / (float)parts, t2 = (s + 1f) / parts; if (R() > 0.1f) Line(p, q, w * Mathf.Sin(t * 3.1f) + 0.001f, w * Mathf.Sin(t2 * 3.1f) + 0.001f, 0.5f + R() * 0.5f); p = q; } }
                    break;
                }
                case Kind.Cracks:
                {
                    void Crack(Vector2 p, float an, float len, float w, int deep)
                    {
                        float done = 0; while (done < len && w > 0.0012f) { float st = 0.025f + R() * 0.03f; an += (R() - 0.5f) * 0.9f; var q = p + new Vector2(Mathf.Cos(an), Mathf.Sin(an)) * st; Line(p, q, w, w * 0.93f, 1); p = q; done += st; w *= 0.93f; if (deep > 0 && R() < 0.22f) Crack(p, an + (R() < 0.5f ? 1 : -1) * (0.5f + R() * 0.7f), (len - done) * 0.7f, w * 0.75f, deep - 1); }
                    }
                    int arms = 3 + rnd.Next(3); float off = R() * 6.28f; for (int i = 0; i < arms; i++) Crack(new Vector2(0.5f, 0.5f), off + i * 6.283f / arms + (R() - 0.5f) * 0.6f, 0.46f, 0.011f, 2);
                    break;
                }
                case Kind.BulletHole:
                {
                    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                        {
                            float u = (x + 0.5f) / n, v = (y + 0.5f) / n, d = new Vector2(u - 0.5f, v - 0.5f).magnitude + (N(u, v) - 0.5f) * 0.09f + (F(u, v) - 0.5f) * 0.05f; float hole = Mathf.Clamp01((0.11f - d) * 40), chip = Mathf.Clamp01((0.24f - d) * 22), dust = Mathf.Clamp01((0.42f - d) * 3f) * 0.35f;
                            a[y * n + x] = Mathf.Max(hole, Mathf.Max(chip * 0.9f, dust * F(u, v) * 1.4f)); shade[y * n + x] = hole > 0.5f ? 0.02f : chip > 0.3f ? 0.72f + (F(u, v) - 0.5f) * 0.5f - Mathf.Clamp01((d - 0.11f) * 4) * 0.1f : 0.35f;
                        }
                    for (int i = 0; i < 5; i++) { float an = R() * 6.283f; var dir = new Vector2(Mathf.Cos(an), Mathf.Sin(an)); Line(new Vector2(0.5f, 0.5f) + dir * 0.2f, new Vector2(0.5f, 0.5f) + dir * (0.28f + R() * 0.16f), 0.005f, 0.001f, 0.9f); }
                    break;
                }
            }
            var r = new Pixels(n, n);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float u = (x + 0.5f) / n, v = (y + 0.5f) / n, sh = shade[i];
                    if (kind == Kind.Splat || kind == Kind.Drips)
                    {
                        // wet: lighter along the top edge, darker in the middle
                        int st = Mathf.Max(1, n / 96); float e = a[Mathf.Min(n - 1, y + st) * n + x] - a[Mathf.Max(0, y - st) * n + x]; sh = Mathf.Clamp(0.46f + (F(u, v) - 0.5f) * 0.25f - e * 0.25f, 0.1f, 0.9f);
                    }
                    r.c[i] = new Color(sh, sh, sh, a[i]);
                }
            return r;
        }

        // ------------------------------------------------------------------ settings

        static Rect Cell(float w, float h) => GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));

        /// <summary>Picks what it is. Returns true when something changed.</summary>
        public bool Settings(float width)
        {
            bool ch = false; float cw = Mathf.Floor((width - 4) / 4); int k = 0; var e = Event.current;
            GUILayout.BeginHorizontal();
            foreach (var d in Drawn)
            {
                if (k == 4) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                var r = Cell(cw - 2, 58); var art = new StickerArt { kind = d.kind, colour = kind == d.kind ? colour : d.colour }; bool on = kind == d.kind; EditorGUI.DrawRect(r, on ? new Color(1f, 0.62f, 0.2f, 0.55f) : new Color(1, 1, 1, r.Contains(e.mousePosition) ? 0.12f : 0.05f)); var pic = art.Picture(3); var inner = new Rect(r.center.x - 19, r.y + 3, 38, 38); EditorGUI.DrawRect(inner, new Color(0.55f, 0.55f, 0.55f)); if (pic) GUI.DrawTexture(inner, pic);
                GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), d.name, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { kind = d.kind; colour = d.colour; ch = true; } k++;
            }
            {
                var r = Cell(cw - 2, 58); bool on = kind == Kind.Text; EditorGUI.DrawRect(r, on ? new Color(1f, 0.62f, 0.2f, 0.55f) : new Color(1, 1, 1, r.Contains(e.mousePosition) ? 0.12f : 0.05f)); GUI.Label(new Rect(r.x, r.y + 6, r.width, 32), "Aa", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 22 }); GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), "Text", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter }); if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !on) { kind = Kind.Text; colour = new Color(0.85f, 0.1f, 0.08f); ch = true; }
            }
            {
                var r = Cell(cw - 2, 58); bool on = kind == Kind.Mine; EditorGUI.DrawRect(r, on ? new Color(1f, 0.62f, 0.2f, 0.55f) : new Color(1, 1, 1, 0.05f)); var t = (Texture2D)EditorGUI.ObjectField(new Rect(r.center.x - 19, r.y + 3, 38, 38), picture, typeof(Texture2D), false); GUI.Label(new Rect(r.x, r.yMax - 17, r.width, 16), "Yours", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
                if (t != picture) { picture = t; kind = Kind.Mine; ch = true; } else if (picture && GUI.Button(new Rect(r.x, r.yMax - 17, r.width, 16), GUIContent.none, GUIStyle.none)) { kind = Kind.Mine; ch = true; }
            }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            if (kind == Kind.Text) ch |= text.Settings();
            if (kind != Kind.Mine) { var c = EditorGUILayout.ColorField("Colour", colour); if (c != colour) { colour = c; ch = true; } }
            else
            {
                bool tk = EditorGUILayout.ToggleLeft(new GUIContent("Take its background out", "For pictures with a plain background: everything like the colour in its corners becomes see-through"), back == Back.TakeOut); if (tk != (back == Back.TakeOut)) { back = tk ? Back.TakeOut : Back.Keep; ch = true; }
                if (back == Back.TakeOut)
                {
                    bool pc = EditorGUILayout.ToggleLeft(new GUIContent("Choose the background colour", "Off: the colour in the picture's corners"), pickColour); if (pc != pickColour) { pickColour = pc; ch = true; }
                    if (pickColour) { var c = EditorGUILayout.ColorField("Background", backColour); if (c != backColour) { backColour = c; ch = true; } }
                    float a = EditorGUILayout.Slider("How much", tolerance, 0.01f, 0.8f), b = EditorGUILayout.Slider("Soft edge", soft, 0f, 1f); if (a != tolerance || b != soft) { tolerance = a; soft = b; ch = true; }
                }
            }
            return ch;
        }
    }

    public static class TextureStamp
    {
        public enum How { Paint, SoakIn, Light, Stain }
        public static readonly string[] HowNames = { "Paint", "Soak in", "Light", "Stain" };

        /// <summary>Puts a picture on another one (wrapping round its edges). size in parts of the width.</summary>
        public static void Put(Pixels p, Pixels art, bool tint, Color colour, Vector2 at, float size, float turn, bool flip, float strength, How how, float shapes, float shading, float mean, float spread)
        {
            float k = size * p.w, hh = k * art.h / art.w, cs = Mathf.Cos(turn * Mathf.Deg2Rad), sn = Mathf.Sin(turn * Mathf.Deg2Rad); Put(p, art, tint, colour, at, new Vector2(cs, sn) * k, new Vector2(-sn, cs) * hh, flip, strength, how, shapes, shading, mean, spread);
        }

        /// <summary>The same with its whole width and height as arrows in pixels (it may be slanted or squeezed).</summary>
        public static void Put(Pixels p, Pixels art, bool tint, Color colour, Vector2 at, Vector2 ax, Vector2 ay, bool flip, float strength, How how, float shapes, float shading, float mean, float spread)
        {
            float det = ax.x * ay.y - ax.y * ay.x; if (Mathf.Abs(det) < 0.25f) return; float k = ax.magnitude, cx = at.x * p.w, cy = at.y * p.h, rx = (Mathf.Abs(ax.x) + Mathf.Abs(ay.x)) / 2 + 1, ry = (Mathf.Abs(ax.y) + Mathf.Abs(ay.y)) / 2 + 1;
            if (art.w > k * 1.6f) { float s = Mathf.Max(2f / art.w, k / art.w); art = art.Resized(Mathf.Max(2, Mathf.RoundToInt(art.w * s)), Mathf.Max(2, Mathf.RoundToInt(art.h * s))); } // (smaller first, so nothing sparkles)
            int y0 = Mathf.FloorToInt(cy - ry), y1 = Mathf.CeilToInt(cy + ry), x0 = Mathf.FloorToInt(cx - rx), x1 = Mathf.CeilToInt(cx + rx); if (y1 - y0 >= p.h) y1 = y0 + p.h - 1; if (x1 - x0 >= p.w) x1 = x0 + p.w - 1;
            TextureOps.Rows(y1 - y0 + 1, row =>
            {
                int y = y0 + row, wy = ((y % p.h) + p.h) % p.h;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, lx = (dx * ay.y - dy * ay.x) / det, ly = (dy * ax.x - dx * ax.y) / det; if (lx < -0.5f || lx > 0.5f || ly < -0.5f || ly > 0.5f) continue; if (flip) lx = -lx;
                    var c = Clamped(art, (lx + 0.5f) * art.w, (ly + 0.5f) * art.h); if (c.a <= 0.003f) continue;
                    int wx = ((x % p.w) + p.w) % p.w, i = wy * p.w + wx; var b = p.c[i]; float l = Pixels.Lum(b), low = Mathf.Clamp((mean - l) / (2f * spread), -1f, 1f); float a = c.a * strength;
                    if (tint) c = StickerArt.Tinted(c, colour); else { c.r *= colour.r; c.g *= colour.g; c.b *= colour.b; }
                    // follows the surface: thinner on what's raised, thicker in the gaps; and the surface's own light and dark shows through
                    a = Mathf.Clamp01(a * (1 + low * shapes * 1.2f)); float sh = Mathf.Lerp(1f, Mathf.Clamp(l / Mathf.Max(0.02f, mean), 0.35f, 1.6f), shading);
                    Color o;
                    if (how == How.Paint) o = new Color(c.r * sh, c.g * sh, c.b * sh);
                    else if (how == How.SoakIn) o = new Color(b.r * c.r, b.g * c.g, b.b * c.b);
                    else if (how == How.Light) o = new Color(1 - (1 - b.r) * (1 - c.r), 1 - (1 - b.g) * (1 - c.g), 1 - (1 - b.b) * (1 - c.b));
                    else { var lab = TexturePalette.Lab(b); var cl = TexturePalette.Lab(c); o = TexturePalette.Rgb(new Vector3(lab.x * 0.92f, cl.y, cl.z)); }
                    p.c[i] = new Color(Mathf.Clamp01(Mathf.Lerp(b.r, o.r, a)), Mathf.Clamp01(Mathf.Lerp(b.g, o.g, a)), Mathf.Clamp01(Mathf.Lerp(b.b, o.b, a)), b.a);
                }
            });
        }

        static Color Clamped(Pixels p, float x, float y)
        {
            x -= 0.5f; y -= 0.5f; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0; Color G(int i, int j) => i < 0 || j < 0 || i >= p.w || j >= p.h ? new Color(p.c[Mathf.Clamp(j, 0, p.h - 1) * p.w + Mathf.Clamp(i, 0, p.w - 1)].r, p.c[Mathf.Clamp(j, 0, p.h - 1) * p.w + Mathf.Clamp(i, 0, p.w - 1)].g, p.c[Mathf.Clamp(j, 0, p.h - 1) * p.w + Mathf.Clamp(i, 0, p.w - 1)].b, 0) : p.c[j * p.w + i];
            return Color.LerpUnclamped(Color.LerpUnclamped(G(x0, y0), G(x0 + 1, y0), fx), Color.LerpUnclamped(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy);
        }

        public static void Levels(Pixels p, out float mean, out float spread) { mean = 0; float sq = 0; foreach (var c in p.c) { float l = Pixels.Lum(c); mean += l; sq += l * l; } mean /= p.c.Length; spread = Mathf.Max(0.02f, Mathf.Sqrt(Mathf.Max(0, sq / p.c.Length - mean * mean))); }
    }

    /// <summary>Puts other pictures on the texture like stickers: blood, posters, cracks, stains.</summary>
    [Serializable]
    public class StickerStep : TextureStep
    {
        [Serializable] public class Stamp { public StickerArt art; public Vector2 at; public float size, turn, strength = 1; public bool flip; public int seed; public Vector2 ax, ay; /* put on a model: its width and height as arrows over the texture (0..1), so it looks right on the surface */ public bool OnModel => ax != Vector2.zero; }
        public List<Stamp> stamps = new List<Stamp>(); public StickerArt brush = new StickerArt(); [NonSerialized] public int pick = -1; public static bool EditMode;
        public Stamp Picked => pick >= 0 && pick < stamps.Count ? stamps[pick] : null;
        public float size = 0.3f, turn, strength = 1f, randomSize = 0.2f, randomTurn = 1f, shapes = 0.35f, shading = 0.4f; public TextureStamp.How how; public int scatter = 12;
        public override string Title => "Stickers";
        public override string Blurb => "Put blood, cracks, posters or your own pictures on it";
        public override string Explain => "Pick a sticker, then click on the picture to put it there. Click one to pick it: drag it to move it, drag a corner to size it, the round handle to turn it, and Delete (or the cross) takes it off. Shift + drag paints a trail. They go over the edges, so it still loops. In the 3D view you can click them straight onto the model.";
        public override string Summary => stamps.Count == 0 ? "click on the picture to put one" : stamps.Count + (stamps.Count == 1 ? " sticker" : " stickers");
        public override Color Tint => new Color(0.95f, 0.4f, 0.45f);

        public Stamp New(Vector2 at, int seed)
        {
            var rnd = new System.Random(seed); float R() => (float)rnd.NextDouble() * 2 - 1;
            return new Stamp { art = brush.Copy(), at = at, size = size * (1 + R() * randomSize * 0.7f), turn = turn + R() * 180f * randomTurn, strength = strength, flip = randomTurn > 0.3f && R() > 0, seed = seed };
        }

        public override bool Settings()
        {
            bool ch = false; var wr = GUILayoutUtility.GetRect(10, 1, GUILayout.ExpandWidth(true)); if (Event.current.type == EventType.Layout) LastWidth = s_Next; else if (Event.current.type == EventType.Repaint && wr.width > 50) s_Next = wr.width;
            int md = EditMode ? 1 : 0; if (Choice(ref md, "+ Place new ones", "Pick and change")) { EditMode = md == 1; if (!EditMode) pick = -1; }
            EditorGUILayout.LabelField(EditMode ? "Click a sticker on the picture to pick it, then drag it, its corners or its round handle. Nothing new is put down in this mode." : "Every click puts a new one down, even right next to another. The wheel sizes the next one, Shift + wheel turns it.", EditorStyles.wordWrappedMiniLabel);
            var pk = EditMode ? Picked : null;
            if (pk != null)
            {
                // the one picked on the picture
                var box = EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(8, 6, 6, 8), margin = new RectOffset(0, 0, 0, 8) }); if (Event.current.type == EventType.Repaint) { EditorGUI.DrawRect(box, new Color(Tint.r, Tint.g, Tint.b, 0.16f)); EditorGUI.DrawRect(new Rect(box.x, box.y, 3, box.height), Tint); }
                EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField("The one you picked", EditorStyles.miniBoldLabel); GUILayout.FlexibleSpace(); if (GUILayout.Button("Done", EditorStyles.miniButton, GUILayout.Width(44))) pick = -1; EditorGUILayout.EndHorizontal();
                if (pk.OnModel) { float cur = pk.ax.magnitude, n = EditorGUILayout.Slider("Size", cur, 0.01f, 2f); if (!Mathf.Approximately(n, cur) && cur > 1e-5f) { pk.ax *= n / cur; pk.ay *= n / cur; ch = true; } float ang = Mathf.Atan2(pk.ax.y, pk.ax.x) * Mathf.Rad2Deg, na = EditorGUILayout.Slider("Turn", ang, -180, 180); if (!Mathf.Approximately(na, ang)) { var q = Quaternion.Euler(0, 0, na - ang); pk.ax = q * pk.ax; pk.ay = q * pk.ay; ch = true; } }
                else { ch |= Slider(ref pk.size, "Size", "Or drag a corner", 0.01f, 2f); ch |= Slider(ref pk.turn, "Turn", "Or drag the round handle", -180f, 180f); }
                ch |= Slider(ref pk.strength, "Strength", "", 0.05f, 1f); ch |= Toggle(ref pk.flip, "Flipped", "");
                if (pk.art.kind != StickerArt.Kind.Mine) { var c = EditorGUILayout.ColorField("Colour", pk.art.colour); if (c != pk.art.colour) { pk.art.colour = c; ch = true; } }
                EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("Another shape")) { pk.seed = UnityEngine.Random.Range(1, 999999); ch = true; } if (GUILayout.Button("Copy it")) { var c = JsonUtility.FromJson<Stamp>(JsonUtility.ToJson(pk)); c.at += new Vector2(0.06f, -0.06f); c.at = new Vector2(Mathf.Repeat(c.at.x, 1), Mathf.Repeat(c.at.y, 1)); stamps.Add(c); pick = stamps.Count - 1; ch = true; } if (GUILayout.Button("Take it off")) { stamps.RemoveAt(pick); pick = -1; ch = true; } EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            brush.Settings(LastWidth);
            GUILayout.Space(4); EditorGUILayout.LabelField("New ones", EditorStyles.miniBoldLabel);
            Slider(ref size, "Size", "How big new ones are (the wheel over the picture too)", 0.02f, 1f); Slider(ref turn, "Turn", "", -180f, 180f); Slider(ref randomSize, "Random size", "", 0f, 1f); Slider(ref randomTurn, "Random turn", "0 = always the same way up (for posters), 1 = any way", 0f, 1f); Slider(ref strength, "Strength", "", 0.05f, 1f);
            EditorGUILayout.BeginHorizontal(); scatter = EditorGUILayout.IntSlider(scatter, 1, 80); if (GUILayout.Button(new GUIContent("Scatter", "Puts that many in random places"), GUILayout.Width(70))) { for (int i = 0; i < scatter; i++) stamps.Add(New(new Vector2(UnityEngine.Random.value, UnityEngine.Random.value), UnityEngine.Random.Range(1, 999999))); ch = true; } EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal(); using (new EditorGUI.DisabledScope(stamps.Count == 0)) { if (GUILayout.Button("Remove last")) { stamps.RemoveAt(stamps.Count - 1); ch = true; } if (GUILayout.Button("Remove all")) { stamps.Clear(); ch = true; } if (GUILayout.Button(new GUIContent("Draw again", "New shapes for the ones that draw themselves"))) { foreach (var s in stamps) s.seed = UnityEngine.Random.Range(1, 999999); ch = true; } } EditorGUILayout.EndHorizontal();
            GUILayout.Space(4); EditorGUILayout.LabelField("All of them", EditorStyles.miniBoldLabel);
            int h = (int)how; if (Choice(ref h, TextureStamp.HowNames)) { how = (TextureStamp.How)h; ch = true; }
            ch |= Slider(ref shapes, "Follow shapes", "Thinner on what's raised, thicker in the gaps", 0f, 1f); ch |= Slider(ref shading, "Keep the light", "The surface's own light and dark shows through", 0f, 1f);
            return ch;
        }
        static float LastWidth = 290, s_Next = 290;

        public override void Prepare() { foreach (var s in stamps) s.art.Prepare(); }
        public override Pixels Run(Pixels p, TextureRun run)
        {
            if (stamps.Count == 0) return p; var r = p.Copy(); TextureStamp.Levels(p, out float mean, out float spread);
            foreach (var s in stamps) { var a = s.art.Get(s.seed, s.size * p.w > 230 ? 384 : 192); if (a != null) TextureStamp.Put(r, a, s.art.kind != StickerArt.Kind.Mine, s.art.kind == StickerArt.Kind.Mine ? Color.white : s.art.colour, s.at, s.OnModel ? Vector2.Scale(s.ax, new Vector2(p.w, p.h)) : new Vector2(Mathf.Cos(s.turn * Mathf.Deg2Rad), Mathf.Sin(s.turn * Mathf.Deg2Rad)) * s.size * p.w, s.OnModel ? Vector2.Scale(s.ay, new Vector2(p.w, p.h)) : new Vector2(-Mathf.Sin(s.turn * Mathf.Deg2Rad), Mathf.Cos(s.turn * Mathf.Deg2Rad)) * s.size * p.w * a.h / a.w, s.flip, s.strength, how, shapes, shading, mean, spread); }
            return r;
        }
        public override TextureStep Demo() { var d = new StickerStep { size = 0.4f }; for (int i = 0; i < 4; i++) d.stamps.Add(d.New(new Vector2(0.2f + 0.55f * (i % 2) + 0.1f * (i / 2), 0.25f + 0.5f * (i / 2)), i + 5)); return d; }
        public override TextureStep ForRecipe() { var c = (StickerStep)JsonUtility.FromJson(JsonUtility.ToJson(this), typeof(StickerStep)); c.stamps.Clear(); c.brush.picture = null; if (c.brush.kind == StickerArt.Kind.Mine) c.brush.kind = StickerArt.Kind.Splat; return c; }
    }
}
