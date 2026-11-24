using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Everything that made a saved texture: the original, the steps and the save options. Kept next to the result, so
    /// it can be opened again and changed (right-click the material or a texture > Texture Studio > Reopen, or
    /// double-click this). Only used in the editor; it never ends up in a build.
    /// </summary>
    public class TextureRecipe : ScriptableObject
    {
        public Texture2D source;
        [SerializeReference] public List<TextureStep> steps = new List<TextureStep>();
        public bool normal, height, mask, material;
        public float bumpiness = 2f, shine = 0.4f, ao = 1f, heightContrast = 1.2f, heightSmooth = 0.6f;
        public bool heightInvert;
        public int importAs = -1; public Material template; public TextureSlots slots; public MatKind kind; public NeverRepeats never = new NeverRepeats(); public SeeThrough see; public Bands bands = new Bands();
        /// <summary>What was made from it (these may be replaced when it's saved again).</summary>
        public List<UnityEngine.Object> outputs = new List<UnityEngine.Object>();

        /// <summary>The recipe an asset was made with (or null).</summary>
        public static TextureRecipe Of(UnityEngine.Object o)
        {
            if (!o) return null;
            if (o is TextureRecipe r) return r;
            if (!(o is Texture2D) && !(o is Material)) return null;
            var path = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) return null;
            var dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            if (!System.IO.Directory.Exists(dir)) return null;
            foreach (var f in System.IO.Directory.GetFiles(dir, "*_Studio*.asset"))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<TextureRecipe>(f.Replace('\\', '/'));
                if (recipe && recipe.outputs.Contains(o)) return recipe;
            }
            return null;
        }
    }

    /// <summary>The step lists and palettes you name and keep. They live in your user folder, so every project has them.</summary>
    public static class TextureShelf
    {
        [Serializable] class StepList { public List<string> types = new List<string>(); public List<string> json = new List<string>(); }
        [Serializable] class ColourList { public List<Color> colours = new List<Color>(); }

        /// <summary>(tests point this somewhere else)</summary>
        public static string Root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HienaDev", "TextureStudio");
        static string Folder(string kind) => System.IO.Path.Combine(Root, kind);
        static string FileOf(string kind, string name) => System.IO.Path.Combine(Folder(kind), Clean(name) + ".json");
        public static string Clean(string name) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name.Trim(); }

        static List<string> Names(string kind) => System.IO.Directory.Exists(Folder(kind)) ? System.IO.Directory.GetFiles(Folder(kind), "*.json").Select(System.IO.Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList() : new List<string>();
        public static List<string> Recipes => Names("Recipes");
        public static List<string> Palettes => Names("Palettes");
        public static bool HasRecipe(string name) => System.IO.File.Exists(FileOf("Recipes", name));
        public static bool HasPalette(string name) => System.IO.File.Exists(FileOf("Palettes", name));

        public static void SaveRecipe(string name, IEnumerable<TextureStep> steps)
        {
            var l = new StepList();
            foreach (var s in steps)
            {
                var keep = s.ForRecipe(); // (things that only fit one texture, like painted spots, stay out)
                if (keep == null) continue;
                l.types.Add(keep.GetType().Name); l.json.Add(JsonUtility.ToJson(keep));
            }
            System.IO.Directory.CreateDirectory(Folder("Recipes"));
            System.IO.File.WriteAllText(FileOf("Recipes", name), JsonUtility.ToJson(l, true));
        }

        public static List<TextureStep> LoadRecipe(string name)
        {
            var made = new List<TextureStep>();
            try
            {
                var l = JsonUtility.FromJson<StepList>(System.IO.File.ReadAllText(FileOf("Recipes", name)));
                for (int i = 0; i < l.types.Count; i++)
                {
                    var type = typeof(TextureStep).Assembly.GetType("HienaDev.DevTools." + l.types[i]);
                    if (type != null && typeof(TextureStep).IsAssignableFrom(type)) made.Add((TextureStep)JsonUtility.FromJson(l.json[i], type));
                }
            }
            catch (Exception e) { Debug.LogWarning("[Texture Studio] Couldn't read the recipe " + name + ": " + e.Message); }
            return made;
        }
        public static void DeleteRecipe(string name) { if (HasRecipe(name)) System.IO.File.Delete(FileOf("Recipes", name)); }

        public static void SavePalette(string name, List<Color> colours)
        {
            System.IO.Directory.CreateDirectory(Folder("Palettes"));
            System.IO.File.WriteAllText(FileOf("Palettes", name), JsonUtility.ToJson(new ColourList { colours = colours }, true));
        }
        public static List<Color> LoadPalette(string name)
        {
            try { return JsonUtility.FromJson<ColourList>(System.IO.File.ReadAllText(FileOf("Palettes", name))).colours; }
            catch (Exception e) { Debug.LogWarning("[Texture Studio] Couldn't read the palette " + name + ": " + e.Message); return new List<Color>(); }
        }
        public static void DeletePalette(string name) { if (HasPalette(name)) System.IO.File.Delete(FileOf("Palettes", name)); }
    }
}
