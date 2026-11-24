using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>One more thing that glows: cracks, outlines, a pattern, a shape, words, a picture...</summary>
    [Serializable]
    public class GlowPart
    {
        public enum Kind { Cracks, Outlines, Veins, Circuit, Stripes, Stars, Hexes, Runes, Ring, Line, Box, Fade, Wand, Text, Picture, Embers }
        public enum Join { Add, TakeAway, OnlyInside }
        public enum Where { Everywhere, Gaps, Raised }
        public Kind kind; public Join join; public Where where; public bool on = true, own; public Color colour = new Color(1f, 0.55f, 0.15f);
        public float amount = 0.5f, size = 0.5f, thick = 0.3f, soft = 0.3f, turn, patchy; public int seed = 1; public Vector2 at = new Vector2(0.5f, 0.5f), to = new Vector2(0.75f, 0.5f); public List<Vector2> clicks = new List<Vector2>(); public StickerArt art = new StickerArt { kind = StickerArt.Kind.Text, colour = Color.white };
        public static readonly (Kind kind, string name, string tip)[] All =
        {
            (Kind.Cracks, "Cracks", "The dark gaps glow, like lava between stones"), (Kind.Outlines, "Outlines", "Along the edges of what's drawn on it"), (Kind.Veins, "Veins", "Branching lines, like magic or infection"), (Kind.Circuit, "Circuit", "Straight lines with corners and dots, like a circuit board"), (Kind.Stripes, "Stripes", "Neon lines or scan lines"), (Kind.Stars, "Stars", "Dots of different sizes"), (Kind.Hexes, "Hexes", "A honeycomb of lines"), (Kind.Runes, "Runes", "Made-up symbols in rows"), (Kind.Embers, "Embers", "Hot patches that die out"),
            (Kind.Ring, "Ring", "A circle: click on the picture for the middle"), (Kind.Line, "Line", "A straight tube: drag on the picture"), (Kind.Box, "Box", "A frame: drag on the picture"), (Kind.Fade, "Fade", "From one side to the other"), (Kind.Wand, "Magic wand", "Click an area: everything joined to it with that colour glows"), (Kind.Text, "Text", "Words in any font"), (Kind.Picture, "Picture", "The shape of a picture of yours"),
        };
        public string Name => All.First(a => a.kind == kind).name; public string Tip => All.First(a => a.kind == kind).tip;
        public bool Click => kind == Kind.Ring || kind == Kind.Text || kind == Kind.Picture || kind == Kind.Wand; public bool Drag => kind == Kind.Line || kind == Kind.Box;

        static bool S(ref float v, string l, string tip, float a, float b) { float n = EditorGUILayout.Slider(new GUIContent(l, tip), v, a, b); if (Mathf.Approximately(n, v)) return false; v = n; return true; }
        public bool Settings(float width)
        {
            bool ch = false; int j = EditorGUILayout.Popup(new GUIContent("How"), (int)join, new[] { "Adds to the rest", "Takes away", "Only inside the rest" }); if (j != (int)join) { join = (Join)j; ch = true; }
            int wh = EditorGUILayout.Popup(new GUIContent("Where", "Follow the texture: only in its gaps and cracks, or only on what sticks out"), (int)where, new[] { "Everywhere", "Only in the gaps", "Only on raised parts" }); if (wh != (int)where) { where = (Where)wh; ch = true; }
            switch (kind)
            {
                case Kind.Cracks: ch |= S(ref amount, "How deep", "", 0, 1); ch |= S(ref thick, "Width", "", 0.02f, 1); break;
                case Kind.Outlines: ch |= S(ref amount, "How many", "Only the strongest edges, or all", 0, 1); ch |= S(ref thick, "Width", "", 0.02f, 1); break;
                case Kind.Veins: case Kind.Circuit: case Kind.Hexes: case Kind.Runes: case Kind.Stars: case Kind.Embers: ch |= S(ref size, "Size", "", 0.05f, 1); ch |= S(ref thick, "Width", "", 0.02f, 1); ch |= S(ref amount, "How many", "", 0, 1); break;
                case Kind.Stripes: ch |= S(ref size, "How far apart", "", 0.02f, 1); ch |= S(ref thick, "Width", "", 0.02f, 1); ch |= S(ref turn, "Turn", "", 0, 90); break;
                case Kind.Ring: ch |= S(ref size, "Size", "", 0.02f, 1); ch |= S(ref thick, "Width", "1 fills it", 0.02f, 1); break;
                case Kind.Line: case Kind.Box: ch |= S(ref thick, "Width", "", 0.02f, 1); break;
                case Kind.Fade: ch |= S(ref amount, "How far", "", 0.02f, 1); ch |= S(ref turn, "Direction", "", 0, 360); break;
                case Kind.Wand: ch |= S(ref amount, "How alike", "", 0.02f, 1); if (clicks.Count > 0 && GUILayout.Button("Take the last area off")) { clicks.RemoveAt(clicks.Count - 1); ch = true; } break;
                case Kind.Text: case Kind.Picture: if (kind == Kind.Text) { art.kind = StickerArt.Kind.Text; ch |= art.text.Settings(); } else { art.kind = StickerArt.Kind.Mine; var t = (Texture2D)EditorGUILayout.ObjectField("Picture", art.picture, typeof(Texture2D), false); if (t != art.picture) { art.picture = t; ch = true; } bool bk = EditorGUILayout.ToggleLeft("Take its background out", art.back == StickerArt.Back.TakeOut); if (bk != (art.back == StickerArt.Back.TakeOut)) { art.back = bk ? StickerArt.Back.TakeOut : StickerArt.Back.Keep; ch = true; } } ch |= S(ref size, "Size", "", 0.02f, 1); ch |= S(ref turn, "Turn", "", -180, 180); break;
            }
            ch |= S(ref soft, "Soft edge", "", 0, 1); ch |= S(ref patchy, "Uneven", "Stronger here, weaker there", 0, 1);
            bool o = EditorGUILayout.ToggleLeft("Its own colour", own); if (o != own) { own = o; ch = true; } if (own) { var c = EditorGUILayout.ColorField("Colour", colour); if (c != colour) { colour = c; ch = true; } }
            if (kind != Kind.Ring && kind != Kind.Line && kind != Kind.Box && kind != Kind.Fade && kind != Kind.Wand && kind != Kind.Text && kind != Kind.Picture && kind != Kind.Cracks && kind != Kind.Outlines && kind != Kind.Stripes && GUILayout.Button("Another one")) { seed = UnityEngine.Random.Range(1, 99999); ch = true; }
            return ch;
        }

        // ------------------------------------------------------------------ where it glows (0..1)

        static float H(int x, int y, int s) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + s * 144269504); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; } }
        static int W(int v, int n) { v %= n; return v < 0 ? v + n : v; }
        public void Prepare() { m_Key = JsonUtility.ToJson(this); if (kind == Kind.Text) art.kind = StickerArt.Kind.Text; else if (kind == Kind.Picture) art.kind = StickerArt.Kind.Mine; else return; art.Prepare(); }

        // (a part that didn't change isn't worked out again: moving one slider only redoes that one)
        static readonly Dictionary<string, float[]> s_Cache = new Dictionary<string, float[]>(); static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Pixels, object> s_Ids = new System.Runtime.CompilerServices.ConditionalWeakTable<Pixels, object>(); static int s_Next;
        public static string Id(Pixels p) { lock (s_Ids) { if (!s_Ids.TryGetValue(p, out var o)) { o = ++s_Next; s_Ids.Add(p, o); } return o.ToString(); } }
        public static float[] Cached(string key, Func<float[]> make) { lock (s_Cache) if (s_Cache.TryGetValue(key, out var m)) return m; var r = make(); lock (s_Cache) { if (s_Cache.Count > 48) s_Cache.Clear(); s_Cache[key] = r; } return r; }
        [NonSerialized] string m_Key; public float[] Mask(Pixels p) => Cached((m_Key ?? (m_Key = JsonUtility.ToJson(this))) + "|" + Id(p) + "|" + p.w + "x" + p.h, () => { var m = Made(p); if (where != Where.Everywhere) Follow(p, m, where == Where.Gaps); return m; });

        /// <summary>Keeps it to the gaps (or the raised parts) of the picture under it.</summary>
        public static void Follow(Pixels p, float[] m, bool gaps)
        {
            var a = TextureOpsMore.Blur(p, Mathf.Max(1f, p.w / 400f)); var b = TextureOpsMore.Blur(p, p.w / 30f); var d = new float[m.Length]; double s = 0; for (int i = 0; i < m.Length; i++) { d[i] = Pixels.Lum(a.c[i]) - Pixels.Lum(b.c[i]); s += d[i] * d[i]; } float sd = (float)Math.Sqrt(s / m.Length) + 1e-4f;
            for (int i = 0; i < m.Length; i++) { float k = Mathf.Clamp01(0.5f + (gaps ? -d[i] : d[i]) / sd * 0.9f); m[i] *= k * k * (3 - 2 * k); }
        }

        float[] Made(Pixels p)
        {
            int w = p.w, h = p.h; var m = new float[w * h]; float blur = soft * 10f;
            void Each(Func<float, float, float> f) => TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) m[y * w + x] = Mathf.Clamp01(f((x + 0.5f) / w, (y + 0.5f) / h)); });
            switch (kind)
            {
                case Kind.Cracks: { var a = TextureOpsMore.Blur(p, 2f); var b = TextureOpsMore.Blur(p, 10f + thick * 60f); float cut = Mathf.Lerp(0.16f, 0.01f, amount); TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) { int i = y * w + x; m[i] = Mathf.Clamp01((Pixels.Lum(b.c[i]) - Pixels.Lum(a.c[i]) - cut) * (14f - soft * 9f) + 0.3f); } }); break; }
                case Kind.Outlines: { var a = TextureOpsMore.Blur(p, 1.5f + thick * 10f); var e = new float[w * h]; TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) { var dx = a[x + 1, y] - a[x - 1, y]; var dy = a[x, y + 1] - a[x, y - 1]; e[y * w + x] = Mathf.Sqrt(dx.r * dx.r + dx.g * dx.g + dx.b * dx.b + dy.r * dy.r + dy.g * dy.g + dy.b * dy.b); } }); var s = (float[])e.Clone(); Array.Sort(s); float cut = s[Mathf.Clamp((int)(s.Length * (1 - 0.02f - amount * 0.35f)), 0, s.Length - 1)], top = Mathf.Max(cut + 1e-4f, s[(int)(s.Length * 0.995f)]); for (int i = 0; i < m.Length; i++) m[i] = Mathf.Clamp01((e[i] - cut) / (top - cut) * 3f); break; }
                case Kind.Veins:
                {
                    int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(16, 3, size)), 2, 24); var wob = new TextureOps.TileNoise(seed + 5, n); var wob2 = new TextureOps.TileNoise(seed + 9, n); var keep = new TextureOps.TileNoise(seed + 13, Mathf.Max(2, n / 2)); float th = 0.012f + thick * 0.16f;
                    Each((u, v) => { float px = u * n + (wob.Fbm(u, v) - 0.5f) * 1.5f, py = v * n + (wob2.Fbm(u, v) - 0.5f) * 1.5f; int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py); float d1 = 9, d2 = 9; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { float qx = cx + i + H(W(cx + i, n), W(cy + j, n), seed), qy = cy + j + H(W(cx + i, n), W(cy + j, n), seed + 77), d = Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy)); if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d; } float k = Mathf.Clamp01((keep.Fbm(u, v) - (0.78f - amount * 0.62f)) * 5f); return Mathf.Clamp01(1 - (d2 - d1) / (th * (0.4f + k))) * k * 1.4f; }); break;
                }
                case Kind.Circuit:
                {
                    int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(28, 5, size)), 3, 40); float th = 0.03f + thick * 0.16f;
                    // every cell joins some of its neighbours
                    bool R(int x, int y) => H(W(x, n), W(y, n), seed) < 0.18f + amount * 0.5f; bool U(int x, int y) => H(W(x, n), W(y, n), seed + 31) < 0.18f + amount * 0.5f;
                    Each((u, v) => { float px = u * n, py = v * n; int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py); float fx = px - cx - 0.5f, fy = py - cy - 0.5f, d = 9; bool r = R(cx, cy), l = R(cx - 1, cy), up = U(cx, cy), dn = U(cx, cy - 1); if (r && fx >= 0) d = Mathf.Min(d, Mathf.Abs(fy)); if (l && fx <= 0) d = Mathf.Min(d, Mathf.Abs(fy)); if (up && fy >= 0) d = Mathf.Min(d, Mathf.Abs(fx)); if (dn && fy <= 0) d = Mathf.Min(d, Mathf.Abs(fx)); int joins = (r ? 1 : 0) + (l ? 1 : 0) + (up ? 1 : 0) + (dn ? 1 : 0); float rr = Mathf.Sqrt(fx * fx + fy * fy), a = Mathf.Clamp01(1.5f - d / th); if (joins == 1) a = Mathf.Max(a, Mathf.Clamp01((0.24f - Mathf.Abs(rr - 0.16f) * 2.4f) * 10)); return a; }); break;
                }
                case Kind.Stripes: { int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(64, 1, Mathf.Sqrt(size))), 1, 128); float rad = turn * Mathf.Deg2Rad; int nx = Mathf.RoundToInt(n * Mathf.Sin(rad)), ny = Mathf.RoundToInt(n * Mathf.Cos(rad)); if (nx == 0 && ny == 0) ny = 1; float th = 0.02f + thick * 0.46f; Each((u, v) => { float t = u * nx + v * ny; t -= Mathf.Floor(t); return (th - Mathf.Abs(t - 0.5f)) * (18 - soft * 14) + 0.5f; }); break; }
                case Kind.Stars: { int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(48, 6, size)), 3, 64); Each((u, v) => { float px = u * n, py = v * n; int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py); float a = 0; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { int x = W(cx + i, n), y = W(cy + j, n); if (H(x, y, seed) > amount * 0.9f) continue; float qx = cx + i + H(x, y, seed + 3), qy = cy + j + H(x, y, seed + 7), big = 0.05f + thick * 0.4f * Mathf.Pow(H(x, y, seed + 11), 2.5f), d = Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy)); a = Mathf.Max(a, Mathf.Clamp01(1.6f - d / big)); } return a; }); break; }
                case Kind.Hexes: { int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(20, 3, size)), 2, 30), ny = Mathf.Max(2, Mathf.RoundToInt(n / 1.732f) * 2); float th = 0.02f + thick * 0.2f; Each((u, v) => { float px = u * n, py = v * ny; float best = 9, second = 9; int bx = 0, by = 0; int cy = Mathf.FloorToInt(py); for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { int ry = cy + j; float off = (ry & 1) == 0 ? 0.25f : 0.75f, qx = Mathf.Floor(px) + i + off, qy = ry + 0.5f, dx = px - qx, dy = (py - qy) * 0.866f, d = Mathf.Sqrt(dx * dx + dy * dy); if (d < best) { second = best; best = d; bx = Mathf.FloorToInt(px) + i; by = ry; } else if (d < second) second = d; } float line = Mathf.Clamp01(1.4f - (second - best) / th), fill = H(W(bx, n), W(by, ny), seed) < amount * 0.5f - 0.1f ? 0.55f : 0; return Mathf.Max(line, fill); }); break; }
                case Kind.Runes:
                {
                    int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(16, 2, size)), 2, 24); float th = 0.02f + thick * 0.09f; Vector2[] g = { new Vector2(0.25f, 0.2f), new Vector2(0.75f, 0.2f), new Vector2(0.25f, 0.5f), new Vector2(0.75f, 0.5f), new Vector2(0.25f, 0.8f), new Vector2(0.75f, 0.8f), new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.65f) };
                    Each((u, v) => { float px = u * n, py = v * n; int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py); int x = W(cx, n), y = W(cy, n); if (H(x, y, seed) > 0.15f + amount) return 0; var q = new Vector2(px - cx, py - cy); float d = 9; int parts = 3 + (int)(H(x, y, seed + 2) * 3); int last = (int)(H(x, y, seed + 4) * 7.99f); for (int k = 0; k < parts; k++) { int nx = (int)(H(x * 7 + k, y, seed + 9) * 7.99f); if (nx == last) nx = (nx + 3) % 8; var a = g[last]; var b = g[nx]; var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude); d = Mathf.Min(d, (q - a - ab * t).magnitude); last = nx; } return 1.5f - d / th; }); break;
                }
                case Kind.Embers: { int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(20, 3, size)), 2, 30); var a = new TextureOps.TileNoise(seed, n); var b = new TextureOps.TileNoise(seed + 41, n * 3); Each((u, v) => { float k = a.Fbm(u, v) - (0.72f - amount * 0.45f); return k <= 0 ? 0 : Mathf.Clamp01(k * (3 + 6 * (1 - thick))) * Mathf.Clamp01((b.Fbm(u, v) - 0.3f) * 3f); }); break; }
                case Kind.Ring: Each((u, v) => { float dx = u - at.x, dy = (v - at.y) * h / w; dx -= Mathf.Round(dx); float d = Mathf.Sqrt(dx * dx + dy * dy), r = size * 0.5f, t = Mathf.Max(0.004f, thick * r); return (t / 2 - Mathf.Abs(d - (r - t / 2))) * w / (1.5f + soft * 12) + 0.5f; }); break;
                case Kind.Line: Each((u, v) => { var q = new Vector2(u, v) - at; var ab = to - at; float t = Mathf.Clamp01(Vector2.Dot(q, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude)); return (0.004f + thick * 0.06f - (q - ab * t).magnitude) * w / (1.5f + soft * 12) + 0.5f; }); break;
                case Kind.Box: Each((u, v) => { float x0 = Mathf.Min(at.x, to.x), x1 = Mathf.Max(at.x, to.x), y0 = Mathf.Min(at.y, to.y), y1 = Mathf.Max(at.y, to.y), dx = Mathf.Max(x0 - u, u - x1), dy = Mathf.Max(y0 - v, v - y1), outd = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0); float t = thick >= 0.99f ? 9 : 0.004f + thick * 0.06f; return (thick >= 0.99f ? -outd : t / 2 - Mathf.Abs(outd + t / 2)) * w / (1.5f + soft * 12) + 0.5f; }); break;
                case Kind.Fade: { float rad = turn * Mathf.Deg2Rad, dx = Mathf.Sin(rad), dy = Mathf.Cos(rad), span = Mathf.Abs(dx) + Mathf.Abs(dy); Each((u, v) => { float t = ((u - 0.5f) * dx + (v - 0.5f) * dy) / span + 0.5f; return Mathf.Pow(Mathf.Clamp01(1 - t / Mathf.Max(0.02f, amount)), 1 + (1 - soft) * 1.5f); }); break; }
                case Kind.Wand: foreach (var c in clicks) Flood(p, c, m); break;
                case Kind.Text: case Kind.Picture: { var a = art.Get(0); if (a != null) { var q = new Pixels(w, h); for (int i = 0; i < q.c.Length; i++) q.c[i] = Color.black; var white = new Pixels(a.w, a.h); for (int i = 0; i < a.c.Length; i++) white.c[i] = new Color(1, 1, 1, a.c[i].a); TextureStamp.Put(q, white, false, Color.white, at, size * 1.2f, turn, false, 1, TextureStamp.How.Paint, 0, 0, 0.5f, 0.2f); for (int i = 0; i < m.Length; i++) m[i] = q.c[i].r; } break; }
            }
            if (blur > 0.3f && kind != Kind.Ring && kind != Kind.Line && kind != Kind.Box && kind != Kind.Fade && kind != Kind.Cracks) { var q = new Pixels(w, h, false); for (int i = 0; i < m.Length; i++) q.c[i] = new Color(m[i], 0, 0, 1); q = TextureOpsMore.Blur(q, blur); for (int i = 0; i < m.Length; i++) m[i] = Mathf.Max(m[i] * (1 - soft * 0.3f), q.c[i].r); }
            if (patchy > 0.01f) { var n = new TextureOps.TileNoise(seed + 501, 5); TextureOps.Rows(h, y => { for (int x = 0; x < w; x++) m[y * w + x] *= Mathf.Lerp(1, Mathf.Clamp01((n.Fbm((x + 0.5f) / w, (y + 0.5f) / h) - 0.28f) * 2.4f), patchy); }); }
            return m;
        }

        void Flood(Pixels p, Vector2 c, float[] m)
        {
            int w = p.w, h = p.h, x0 = Mathf.Clamp((int)(c.x * w), 0, w - 1), y0 = Mathf.Clamp((int)(c.y * h), 0, h - 1); var b = TextureOpsMore.Blur(p, 2.5f); var start = TexturePalette.Lab(b.c[y0 * w + x0]); float lim = 0.02f + amount * 0.22f; var seen = new bool[w * h]; var todo = new Stack<int>(); todo.Push(y0 * w + x0); seen[y0 * w + x0] = true;
            while (todo.Count > 0) { int i = todo.Pop(); m[i] = 1; int x = i % w, y = i / w; void Try(int nx, int ny) { nx = W(nx, w); ny = W(ny, h); int j = ny * w + nx; if (seen[j]) return; seen[j] = true; if ((TexturePalette.Lab(b.c[j]) - start).magnitude < lim) todo.Push(j); } Try(x + 1, y); Try(x - 1, y); Try(x, y + 1); Try(x, y - 1); }
        }
    }
}
