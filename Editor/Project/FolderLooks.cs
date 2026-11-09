using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Coloured folders and folder icons in the Project window. Right-click a folder > Folder Colour / Folder Icon. Saved in
    /// ProjectSettings/HienaDevTools.json, so the whole team (through git) sees the same colours. Common folder names
    /// (Scripts, Audio, Scenes, Prefabs...) get a matching icon by themselves (switch off in Project Settings).
    /// </summary>
    [InitializeOnLoad]
    static class FolderLooks
    {
        public static readonly (string name, Color color)[] Colours =
        {
            ("Red", new Color(0.93f, 0.33f, 0.31f)), ("Orange", new Color(0.97f, 0.6f, 0.25f)), ("Yellow", new Color(0.98f, 0.82f, 0.25f)),
            ("Green", new Color(0.4f, 0.78f, 0.38f)), ("Teal", new Color(0.25f, 0.75f, 0.72f)), ("Blue", new Color(0.33f, 0.6f, 0.95f)),
            ("Purple", new Color(0.65f, 0.45f, 0.92f)), ("Pink", new Color(0.95f, 0.5f, 0.75f)), ("Grey", new Color(0.55f, 0.55f, 0.58f)),
        };
        public static readonly (string name, string icon, string[] auto)[] Icons =
        {
            ("Scripts", "d_cs Script Icon", new[] { "scripts", "script", "code", "src" }),
            ("Scenes", "d_SceneAsset Icon", new[] { "scenes", "scene", "levels" }),
            ("Prefabs", "d_Prefab Icon", new[] { "prefabs", "prefab" }),
            ("Art", "d_Texture Icon", new[] { "art", "textures", "sprites", "images", "graphics" }),
            ("Models", "d_PrefabModel Icon", new[] { "models", "meshes", "3d" }),
            ("Materials", "d_Material Icon", new[] { "materials", "material", "mats" }),
            ("Shaders", "d_Shader Icon", new[] { "shaders", "shader" }),
            ("Audio", "d_AudioClip Icon", new[] { "audio", "sounds", "sound", "sfx", "music" }),
            ("Animations", "d_AnimationClip Icon", new[] { "animations", "animation", "anims" }),
            ("UI", "d_Canvas Icon", new[] { "ui", "gui", "hud", "menus" }),
            ("Fonts", "d_Font Icon", new[] { "fonts", "font" }),
            ("Video", "d_VideoClip Icon", new[] { "video", "videos" }),
            ("Settings", "d_Settings Icon", new[] { "settings", "config" }),
            ("Editor", "d_UnityEditor.InspectorWindow", new[] { "editor" }),
            ("Plugins", "d_Assembly Icon", new[] { "plugins" }),
            ("Physics", "d_PhysicMaterial Icon", new[] { "physics" }),
            ("Particles / VFX", "d_ParticleSystem Icon", new[] { "vfx", "particles", "effects", "fx" }),
            ("Star", "d_Favorite Icon", new string[0]),
        };

        static Texture2D s_Folder, s_FolderEmpty;
        static readonly Dictionary<string, Texture2D> s_Tex = new Dictionary<string, Texture2D>();

        static FolderLooks() { EditorApplication.projectWindowItemOnGUI += OnItem; }

        static Texture2D Tex(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!s_Tex.TryGetValue(name, out var t)) { t = EditorGUIUtility.FindTexture(name); if (!t && name.StartsWith("d_")) t = EditorGUIUtility.FindTexture(name.Substring(2)); s_Tex[name] = t; }
            return t;
        }

        static void OnItem(string guid, Rect r)
        {
            if (!DevToolsSettings.FolderLooks || Event.current.type != EventType.Repaint) return;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;
            var set = DevToolsSettings.Get.FolderFor(guid);
            Color color = set != null ? set.color : Color.clear;
            string icon = set != null && !string.IsNullOrEmpty(set.icon) ? set.icon : null;
            if (icon == null && DevToolsSettings.AutoFolderIcons) { string n = Path.GetFileName(path).ToLowerInvariant(); foreach (var i in Icons) if (i.auto.Contains(n)) { icon = i.icon; break; } }
            if (icon == "none") icon = null;
            if (color.a <= 0.01f && icon == null) return;

            // where the folder icon is: a 16 px square in lists, the top square in the grid
            Rect ir = r.height <= 20 ? new Rect(r.x, r.y, 16, 16) : new Rect(r.x, r.y, r.width, r.width);
            if (color.a > 0.01f)
            {
                if (!s_Folder) { s_Folder = Tex("d_Folder Icon") ?? Tex("Folder Icon"); s_FolderEmpty = Tex("d_FolderEmpty Icon") ?? Tex("FolderEmpty Icon"); }
                bool empty = !Directory.EnumerateFileSystemEntries(path).Any();
                var folder = empty && s_FolderEmpty ? s_FolderEmpty : s_Folder;
                if (folder)
                {
                    var old = GUI.color; GUI.color = color;
                    GUI.DrawTexture(ir, folder, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
            }
            var it = Tex(icon);
            if (it)
            {
                float s = ir.width * 0.62f;
                GUI.DrawTexture(new Rect(ir.xMax - s, ir.yMax - s, s, s), it, ScaleMode.ScaleToFit);
            }
        }

        // ---- right-click menus
        static IEnumerable<string> SelectedFolders() => Selection.assetGUIDs.Where(g => AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(g)));
        static bool AnyFolder() => SelectedFolders().Any();

        static void SetColour(Color c) { var s = DevToolsSettings.Get; foreach (var g in SelectedFolders()) s.FolderFor(g, true).color = c; Tidy(s); s.Save(); EditorApplication.RepaintProjectWindow(); }
        static void SetIcon(string icon) { var s = DevToolsSettings.Get; foreach (var g in SelectedFolders()) s.FolderFor(g, true).icon = icon; Tidy(s); s.Save(); EditorApplication.RepaintProjectWindow(); }
        static void Tidy(DevToolsSettings s) => s.folders.RemoveAll(f => f.color.a <= 0.01f && string.IsNullOrEmpty(f.icon));

        [MenuItem("Assets/Folder Colour/Red", false, 2000)] static void C0() => SetColour(Colours[0].color);
        [MenuItem("Assets/Folder Colour/Orange", false, 2001)] static void C1() => SetColour(Colours[1].color);
        [MenuItem("Assets/Folder Colour/Yellow", false, 2002)] static void C2() => SetColour(Colours[2].color);
        [MenuItem("Assets/Folder Colour/Green", false, 2003)] static void C3() => SetColour(Colours[3].color);
        [MenuItem("Assets/Folder Colour/Teal", false, 2004)] static void C4() => SetColour(Colours[4].color);
        [MenuItem("Assets/Folder Colour/Blue", false, 2005)] static void C5() => SetColour(Colours[5].color);
        [MenuItem("Assets/Folder Colour/Purple", false, 2006)] static void C6() => SetColour(Colours[6].color);
        [MenuItem("Assets/Folder Colour/Pink", false, 2007)] static void C7() => SetColour(Colours[7].color);
        [MenuItem("Assets/Folder Colour/Grey", false, 2008)] static void C8() => SetColour(Colours[8].color);
        [MenuItem("Assets/Folder Colour/No colour", false, 2050)] static void CNone() => SetColour(Color.clear);
        [MenuItem("Assets/Folder Colour/Red", true)] [MenuItem("Assets/Folder Colour/Orange", true)] [MenuItem("Assets/Folder Colour/Yellow", true)]
        [MenuItem("Assets/Folder Colour/Green", true)] [MenuItem("Assets/Folder Colour/Teal", true)] [MenuItem("Assets/Folder Colour/Blue", true)]
        [MenuItem("Assets/Folder Colour/Purple", true)] [MenuItem("Assets/Folder Colour/Pink", true)] [MenuItem("Assets/Folder Colour/Grey", true)]
        [MenuItem("Assets/Folder Colour/No colour", true)]
        static bool ColourOk() => AnyFolder();

        [MenuItem("Assets/Folder Icon/Scripts", false, 2100)] static void I0() => SetIcon(Icons[0].icon);
        [MenuItem("Assets/Folder Icon/Scenes", false, 2101)] static void I1() => SetIcon(Icons[1].icon);
        [MenuItem("Assets/Folder Icon/Prefabs", false, 2102)] static void I2() => SetIcon(Icons[2].icon);
        [MenuItem("Assets/Folder Icon/Art", false, 2103)] static void I3() => SetIcon(Icons[3].icon);
        [MenuItem("Assets/Folder Icon/Models", false, 2104)] static void I4() => SetIcon(Icons[4].icon);
        [MenuItem("Assets/Folder Icon/Materials", false, 2105)] static void I5() => SetIcon(Icons[5].icon);
        [MenuItem("Assets/Folder Icon/Shaders", false, 2106)] static void I6() => SetIcon(Icons[6].icon);
        [MenuItem("Assets/Folder Icon/Audio", false, 2107)] static void I7() => SetIcon(Icons[7].icon);
        [MenuItem("Assets/Folder Icon/Animations", false, 2108)] static void I8() => SetIcon(Icons[8].icon);
        [MenuItem("Assets/Folder Icon/UI", false, 2109)] static void I9() => SetIcon(Icons[9].icon);
        [MenuItem("Assets/Folder Icon/Fonts", false, 2110)] static void I10() => SetIcon(Icons[10].icon);
        [MenuItem("Assets/Folder Icon/Video", false, 2111)] static void I11() => SetIcon(Icons[11].icon);
        [MenuItem("Assets/Folder Icon/Settings", false, 2112)] static void I12() => SetIcon(Icons[12].icon);
        [MenuItem("Assets/Folder Icon/Editor", false, 2113)] static void I13() => SetIcon(Icons[13].icon);
        [MenuItem("Assets/Folder Icon/Plugins", false, 2114)] static void I14() => SetIcon(Icons[14].icon);
        [MenuItem("Assets/Folder Icon/Physics", false, 2115)] static void I15() => SetIcon(Icons[15].icon);
        [MenuItem("Assets/Folder Icon/Particles - VFX", false, 2116)] static void I16() => SetIcon(Icons[16].icon);
        [MenuItem("Assets/Folder Icon/Star", false, 2117)] static void I17() => SetIcon(Icons[17].icon);
        [MenuItem("Assets/Folder Icon/No icon", false, 2150)] static void INone() => SetIcon("none");
        [MenuItem("Assets/Folder Icon/Automatic (from the name)", false, 2151)] static void IAuto() => SetIcon("");
        [MenuItem("Assets/Folder Icon/Scripts", true)] [MenuItem("Assets/Folder Icon/Scenes", true)] [MenuItem("Assets/Folder Icon/Prefabs", true)]
        [MenuItem("Assets/Folder Icon/Art", true)] [MenuItem("Assets/Folder Icon/Models", true)] [MenuItem("Assets/Folder Icon/Materials", true)]
        [MenuItem("Assets/Folder Icon/Shaders", true)] [MenuItem("Assets/Folder Icon/Audio", true)] [MenuItem("Assets/Folder Icon/Animations", true)]
        [MenuItem("Assets/Folder Icon/UI", true)] [MenuItem("Assets/Folder Icon/Fonts", true)] [MenuItem("Assets/Folder Icon/Video", true)]
        [MenuItem("Assets/Folder Icon/Settings", true)] [MenuItem("Assets/Folder Icon/Editor", true)] [MenuItem("Assets/Folder Icon/Plugins", true)]
        [MenuItem("Assets/Folder Icon/Physics", true)] [MenuItem("Assets/Folder Icon/Particles - VFX", true)] [MenuItem("Assets/Folder Icon/Star", true)]
        [MenuItem("Assets/Folder Icon/No icon", true)] [MenuItem("Assets/Folder Icon/Automatic (from the name)", true)]
        static bool IconOk() => AnyFolder();
    }
}
