using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace HienaDev.DevTools
{
    /// <summary>Texture Studio's "Shrink": import settings for many textures at once, from a few plain presets, with an undo.</summary>
    public static class TextureShrink
    {
        public enum Preset { Small, Balanced, BestLook, PixelArt }

        public static readonly string[] PresetNames = { "Small (web, jams)", "Balanced", "Best look", "Pixel art" };
        public static readonly string[] PresetHelp =
        {
            "Smallest download and memory: at most 1024 pixels, compressed, crunched. Great for itch.io web builds.",
            "Good look for less: at most 2048 pixels, normal compression.",
            "Keeps the detail: at most 4096 pixels, high quality compression.",
            "Crisp pixels: no blur (Point filter), no compression, no mipmaps, size kept.",
        };

        public class Row
        {
            public string path; public Texture2D tex; public TextureImporter imp;
            public long memNow; public long memAfter; public int sourceSide; public bool normal, sprite;
        }

        public static List<Row> Rows(IEnumerable<Texture2D> textures, Preset preset)
        {
            var list = new List<Row>();
            foreach (var t in textures)
            {
                if (!t) continue;
                var path = AssetDatabase.GetAssetPath(t);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!imp) continue;
                imp.GetSourceTextureWidthAndHeight(out int sw, out int sh);
                var row = new Row { path = path, tex = t, imp = imp, memNow = Profiler.GetRuntimeMemorySizeLong(t), sourceSide = Mathf.Max(sw, sh),
                    normal = imp.textureType == TextureImporterType.NormalMap, sprite = imp.textureType == TextureImporterType.Sprite };
                row.memAfter = Estimate(row, preset, sw, sh);
                if (preset != Preset.PixelArt && row.memNow > 0) row.memAfter = System.Math.Min(row.memAfter, row.memNow); // (these presets never make it bigger)
                list.Add(row);
            }
            return list.OrderByDescending(r => r.memNow).ToList();
        }

        static int MaxFor(Preset p, int side) => p == Preset.Small ? 1024 : p == Preset.Balanced ? 2048 : p == Preset.BestLook ? 4096 : Mathf.Max(32, Mathf.NextPowerOfTwo(side));

        /// <summary>About how much memory it'll take after (the GPU size; the download is usually smaller still).</summary>
        static long Estimate(Row r, Preset p, int sw, int sh)
        {
            int max = Mathf.Min(MaxFor(p, r.sourceSide), 8192);
            float k = Mathf.Min(1f, max / (float)Mathf.Max(1, Mathf.Max(sw, sh)));
            double px = (double)Mathf.Max(1, sw * k) * Mathf.Max(1, sh * k);
            bool mips = p != Preset.PixelArt && !r.sprite;
            bool alpha = r.imp.DoesSourceTextureHaveAlpha() && !r.normal;
            double bpp = p == Preset.PixelArt ? 4 : (alpha || r.normal || p == Preset.BestLook ? 1 : 0.5);
            return (long)(px * bpp * (mips ? 1.333 : 1));
        }

        // ---- undo: the settings each texture had before the last Apply
        [System.Serializable] class Saved
        {
            public string path; public int maxSize, compression, quality, filter; public bool crunch, mips;
            public bool webOn; public int webMax, webQuality; public bool webCrunch;
        }
        [System.Serializable] class SavedList { public List<Saved> items = new List<Saved>(); }
        const string UndoKey = "HienaDev.TextureShrink.Undo";
        public static bool CanUndo => SessionState.GetString(UndoKey, "").Length > 0;

        public static void Apply(List<Row> rows, Preset p)
        {
            var saved = new SavedList();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    EditorUtility.DisplayProgressBar("Texture Studio", "Shrinking " + r.tex.name, i / (float)rows.Count);
                    var imp = r.imp;
                    var web0 = imp.GetPlatformTextureSettings("WebGL");
                    saved.items.Add(new Saved { path = r.path, maxSize = imp.maxTextureSize, compression = (int)imp.textureCompression, quality = imp.compressionQuality, filter = (int)imp.filterMode,
                        crunch = imp.crunchedCompression, mips = imp.mipmapEnabled, webOn = web0.overridden, webMax = web0.maxTextureSize, webQuality = web0.compressionQuality, webCrunch = web0.crunchedCompression });
                    Set(imp, p, r.sourceSide, r.sprite);
                    imp.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }
            SessionState.SetString(UndoKey, JsonUtility.ToJson(saved));
        }


        /// <summary>One texture's import settings for a preset (doesn't reimport).</summary>
        public static void Set(TextureImporter imp, Preset p, int sourceSide, bool sprite)
        {
                    if (p == Preset.PixelArt)
                    {
                        imp.filterMode = FilterMode.Point; imp.textureCompression = TextureImporterCompression.Uncompressed;
                        imp.mipmapEnabled = false; imp.crunchedCompression = false;
                        imp.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(sourceSide), 32, 8192);
                    }
                    else
                    {
                        imp.maxTextureSize = MaxFor(p, sourceSide);
                        imp.textureCompression = p == Preset.BestLook ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
                        imp.crunchedCompression = p == Preset.Small;
                        imp.compressionQuality = p == Preset.Small ? 50 : 100;
                        if (sprite) imp.mipmapEnabled = false; // (UI and 2D sprites don't need them)
                        if (imp.filterMode == FilterMode.Point && p != Preset.PixelArt) { } // (left as it was)
                    }
                    // web builds take the same size (a WebGL override would win over these, so it follows along)
                    var web = imp.GetPlatformTextureSettings("WebGL");
                    if (web.overridden) { web.maxTextureSize = imp.maxTextureSize; web.crunchedCompression = imp.crunchedCompression; web.compressionQuality = imp.compressionQuality; imp.SetPlatformTextureSettings(web); }
        }

        public static int Undo()
        {
            var list = JsonUtility.FromJson<SavedList>(SessionState.GetString(UndoKey, ""));
            if (list == null) return 0;
            int n = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var s in list.items)
                {
                    var imp = AssetImporter.GetAtPath(s.path) as TextureImporter; if (!imp) continue;
                    imp.maxTextureSize = s.maxSize; imp.textureCompression = (TextureImporterCompression)s.compression; imp.compressionQuality = s.quality;
                    imp.filterMode = (FilterMode)s.filter; imp.crunchedCompression = s.crunch; imp.mipmapEnabled = s.mips;
                    if (s.webOn) { var w = imp.GetPlatformTextureSettings("WebGL"); w.maxTextureSize = s.webMax; w.compressionQuality = s.webQuality; w.crunchedCompression = s.webCrunch; imp.SetPlatformTextureSettings(w); }
                    imp.SaveAndReimport(); n++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            SessionState.EraseString(UndoKey);
            return n;
        }

        public static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024) return (bytes / (1024f * 1024f)).ToString(bytes >= 10L * 1024 * 1024 ? "0" : "0.0") + " MB";
            return Mathf.Max(1, bytes / 1024) + " KB";
        }
    }
}
