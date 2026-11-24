using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A picture as plain colours (row by row, bottom row first, like Unity), ready to be changed.</summary>
    public class Pixels
    {
        public int w, h;
        public Color[] c;
        public bool srgb = true;

        public Pixels(int w, int h, bool srgb = true) { this.w = w; this.h = h; this.srgb = srgb; c = new Color[w * h]; }

        public Color this[int x, int y]
        {
            get { x %= w; if (x < 0) x += w; y %= h; if (y < 0) y += h; return c[y * w + x]; }
            set => c[y * w + x] = value;
        }

        /// <summary>Smooth sample between pixels, wrapping around (x, y in pixels).</summary>
        public Color Sample(float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            var a = Color.LerpUnclamped(this[x0, y0], this[x0 + 1, y0], fx);
            var b = Color.LerpUnclamped(this[x0, y0 + 1], this[x0 + 1, y0 + 1], fx);
            return Color.LerpUnclamped(a, b, fy);
        }

        public Pixels Resized(int nw, int nh)
        {
            if (nw == w && nh == h) return Copy();
            var r = new Pixels(nw, nh, srgb);
            // shrinking a lot: average first so nothing sparkles
            var src = this;
            while (src.w >= nw * 2 && src.h >= nh * 2) src = src.Half();
            float sx = src.w / (float)nw, sy = src.h / (float)nh;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    r.c[y * nw + x] = src.Sample((x + 0.5f) * sx, (y + 0.5f) * sy);
            return r;
        }

        Pixels Half()
        {
            var r = new Pixels(w / 2, h / 2, srgb);
            for (int y = 0; y < r.h; y++)
                for (int x = 0; x < r.w; x++)
                    r.c[y * r.w + x] = (this[2 * x, 2 * y] + this[2 * x + 1, 2 * y] + this[2 * x, 2 * y + 1] + this[2 * x + 1, 2 * y + 1]) * 0.25f;
            return r;
        }

        public Pixels Copy() { var r = new Pixels(w, h, srgb); System.Array.Copy(c, r.c, c.Length); return r; }

        /// <summary>A texture to show in the window (tiles when drawn with more than 1x1 coordinates).</summary>
        public Texture2D ToTexture()
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels(c); t.Apply(true);
            return t;
        }

        public static float Lum(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        // ---------------------------------------------------------------- reading and saving

        public static bool IsSRGB(Texture t)
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            return imp == null || (imp.sRGBTexture && imp.textureType != TextureImporterType.NormalMap);
        }

        /// <summary>The picture at full quality: straight from the file when it's a PNG/JPG (the imported one may be
        /// shrunk and compressed), else from the imported texture.</summary>
        public static Pixels Read(Texture2D t)
        {
            if (!t) return null;
            bool srgb = IsSRGB(t);
            var path = AssetDatabase.GetAssetPath(t);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && File.Exists(path))
            {
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, !srgb);
                if (tmp.LoadImage(File.ReadAllBytes(path)))
                {
                    var p = new Pixels(tmp.width, tmp.height, srgb) { c = tmp.GetPixels() };
                    Object.DestroyImmediate(tmp);
                    return p;
                }
                Object.DestroyImmediate(tmp);
            }
            // any other kind (PSD, TGA, EXR...): what Unity imported, drawn into a plain picture
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var read = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, !srgb);
            read.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); read.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = new Pixels(t.width, t.height, srgb) { c = read.GetPixels() };
            Object.DestroyImmediate(read);
            return px;
        }

        static readonly System.Collections.Generic.Dictionary<string, Pixels> s_Cache = new System.Collections.Generic.Dictionary<string, Pixels>();

        /// <summary>The same, remembered until the file changes (for textures that are used again and again).</summary>
        public static Pixels ReadCached(Texture2D t)
        {
            var path = AssetDatabase.GetAssetPath(t); var key = path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0);
            if (s_Cache.TryGetValue(key, out var p)) return p;
            if (s_Cache.Count > 12) s_Cache.Clear();
            return s_Cache[key] = Read(t);
        }

        /// <summary>Saves as a PNG at that place (replacing what's there, which keeps everything that uses it working),
        /// imported like <paramref name="like"/> the first time.</summary>
        public static Texture2D SaveAt(Pixels p, string path, Texture2D like, bool normalMap, bool hard, bool seeThrough = false)
        {
            bool isNew = !File.Exists(path);
            var t = new Texture2D(p.w, p.h, TextureFormat.RGBA32, false, !p.srgb);
            t.SetPixels(p.c); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp)
            {
                var from = like ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(like)) as TextureImporter : null;
                if (isNew && from)
                {
                    imp.textureType = from.textureType == TextureImporterType.NormalMap ? TextureImporterType.Default : from.textureType;
                    imp.maxTextureSize = from.maxTextureSize; imp.textureCompression = from.textureCompression; imp.crunchedCompression = from.crunchedCompression; imp.compressionQuality = from.compressionQuality;
                    imp.filterMode = from.filterMode; imp.mipmapEnabled = from.mipmapEnabled; imp.alphaIsTransparency = from.alphaIsTransparency;
                }
                imp.sRGBTexture = p.srgb && !normalMap;
                if (seeThrough) imp.alphaIsTransparency = true;
                if (!Mathf.IsPowerOfTwo(p.w) || !Mathf.IsPowerOfTwo(p.h)) imp.npotScale = TextureImporterNPOTScale.None; // (kept as big as it is, not squeezed to the nearest 512)
                if (normalMap) imp.textureType = TextureImporterType.NormalMap;
                if (isNew) imp.wrapMode = TextureWrapMode.Repeat;
                if (hard) { imp.filterMode = FilterMode.Point; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.crunchedCompression = false; imp.userData = "HienaHard"; }
                else if (imp.userData == "HienaHard") { imp.filterMode = FilterMode.Bilinear; imp.textureCompression = TextureImporterCompression.Compressed; imp.userData = ""; }
                if (Mathf.Max(p.w, p.h) > imp.maxTextureSize) imp.maxTextureSize = Mathf.Min(8192, Mathf.NextPowerOfTwo(Mathf.Max(p.w, p.h)));
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Saves as a new PNG next to <paramref name="like"/> (never over it) and imports it with the same
        /// settings. Returns the new texture.</summary>
        public static Texture2D SaveNextTo(Pixels p, Texture2D like, string suffix, bool normalMap = false, bool repeat = true, string fullName = null)
        {
            var src = like ? AssetDatabase.GetAssetPath(like) : "Assets/Texture.png";
            var dir = Path.GetDirectoryName(src).Replace('\\', '/');
            var name = (string.IsNullOrEmpty(fullName) ? Path.GetFileNameWithoutExtension(src) + suffix : fullName) + ".png";
            var path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name);
            var t = new Texture2D(p.w, p.h, TextureFormat.RGBA32, false, !p.srgb);
            t.SetPixels(p.c); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            var from = like ? AssetImporter.GetAtPath(src) as TextureImporter : null;
            if (imp)
            {
                if (from)
                {
                    imp.textureType = from.textureType == TextureImporterType.NormalMap ? TextureImporterType.Default : from.textureType;
                    imp.maxTextureSize = from.maxTextureSize;
                    imp.textureCompression = from.textureCompression; imp.crunchedCompression = from.crunchedCompression; imp.compressionQuality = from.compressionQuality;
                    imp.filterMode = from.filterMode; imp.mipmapEnabled = from.mipmapEnabled; imp.alphaIsTransparency = from.alphaIsTransparency;
                }
                imp.sRGBTexture = p.srgb && !normalMap;
                if (normalMap) imp.textureType = TextureImporterType.NormalMap;
                if (repeat) imp.wrapMode = TextureWrapMode.Repeat;
                if (Mathf.Max(p.w, p.h) > imp.maxTextureSize) imp.maxTextureSize = Mathf.Min(8192, Mathf.NextPowerOfTwo(Mathf.Max(p.w, p.h)));
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
