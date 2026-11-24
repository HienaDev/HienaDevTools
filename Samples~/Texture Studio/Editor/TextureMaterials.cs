using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HienaDev.DevTools
{
    /// <summary>Which of a shader's textures gets which of the saved ones.</summary>
    [Serializable]
    public class TextureSlots
    {
        public string colour = "", normal = "", mask = "", occlusion = "", height = "", glow = "";

        public static List<string> TexturesOf(Shader s)
        {
            var l = new List<string>(); if (!s) return l;
            for (int i = 0; i < s.GetPropertyCount(); i++) if (s.GetPropertyType(i) == ShaderPropertyType.Texture && (s.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) == 0 && s.GetPropertyTextureDimension(i) == TextureDimension.Tex2D) l.Add(s.GetPropertyName(i));
            return l;
        }

        /// <summary>The best guess from the names the shader uses.</summary>
        public static TextureSlots Guess(Shader s)
        {
            var l = TexturesOf(s); var r = new TextureSlots();
            string Pick(params string[] names)
            {
                foreach (var n in names) { var f = l.FirstOrDefault(x => x.Equals(n, StringComparison.OrdinalIgnoreCase)); if (f != null) return f; }
                foreach (var n in names) { var f = l.FirstOrDefault(x => x.IndexOf(n.TrimStart('_'), StringComparison.OrdinalIgnoreCase) >= 0); if (f != null) return f; }
                return "";
            }
            r.normal = Pick("_BumpMap", "_NormalMap", "_Normal", "_BumpTex"); r.height = Pick("_ParallaxMap", "_HeightMap", "_Height"); r.mask = Pick("_MetallicGlossMap", "_MaskMap", "_MetallicMap", "_SmoothnessMap"); r.glow = Pick("_EmissionMap", "_EmissiveColorMap", "_EmissiveMap", "_EmissionTex", "_GlowMap", "_Emission"); r.occlusion = Pick("_OcclusionMap", "_AOMap", "_Occlusion");
            r.colour = Pick("_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_BaseTex", "_ColorMap", "_Diffuse", "_Texture");
            if (r.colour == "") r.colour = l.FirstOrDefault(x => x != r.normal && x != r.mask && x != r.height && x != r.occlusion && x != r.glow) ?? "";
            if (r.colour == r.normal || r.colour == r.mask) r.colour = l.FirstOrDefault() ?? "";
            return r;
        }
        public TextureSlots Copy() => new TextureSlots { colour = colour, normal = normal, mask = mask, occlusion = occlusion, height = height, glow = glow };
    }

    public static class TextureMaterials
    {
        /// <summary>Makes (or updates) the material. A template material gives its shader and all its settings; only the
        /// textures are put in. Without one it's a URP Lit material.</summary>
        public static Material Make(string path, Texture2D colour, Texture2D normal, Texture2D mask, Texture2D height, Material template, TextureSlots slots, NeverRepeats never = null, SeeThrough see = SeeThrough.Solid)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path); bool isNew = !mat;
            if (template)
            {
                if (isNew) mat = new Material(template); else if (mat.shader != template.shader) { mat.shader = template.shader; mat.CopyPropertiesFromMaterial(template); }
                mat.name = System.IO.Path.GetFileNameWithoutExtension(path);
                Fill(mat, colour, normal, mask, height, slots ?? TextureSlots.Guess(template.shader)); if (see != SeeThrough.Solid) { var w = TextureAlpha.ApplyAny(mat, see); if (w.Length > 0) Debug.LogWarning("[Texture Studio] " + w); }
            }
            else if (never != null)
            {
                var sh = Shader.Find(NeverRepeats.ShaderName); if (isNew) mat = new Material(sh); else if (mat.shader != sh) mat.shader = sh;
                mat.SetTexture("_BaseMap", colour); mat.SetTexture("_BumpMap", normal); mat.SetTexture("_MetallicGlossMap", mask); never.To(mat); TextureAlpha.Apply(mat, see);
            }
            else
            {
                var urp = Shader.Find("Universal Render Pipeline/Lit"); if (!isNew && mat.shader.name == NeverRepeats.ShaderName) mat.shader = urp ? urp : Shader.Find("Standard"); if (isNew) mat = new Material(urp ? urp : Shader.Find("Standard"));
                bool isUrp = mat.HasProperty("_BaseMap"); if (isUrp) mat.SetTexture("_BaseMap", colour);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", colour);
                if (normal && mat.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap", normal); if (isNew) mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
                if (mask && mat.HasProperty("_MetallicGlossMap"))
                {
                    // metallic in R, smoothness in A; occlusion in G of the same texture
                    mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword(isUrp ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP");
                    if (isNew) { mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_GlossMapScale", 1f); mat.SetFloat("_SmoothnessTextureChannel", 0); mat.SetFloat("_OcclusionStrength", 1f); }
                    mat.SetTexture("_OcclusionMap", mask); mat.EnableKeyword("_OCCLUSIONMAP");
                }
                if (height && mat.HasProperty("_ParallaxMap")) { mat.SetTexture("_ParallaxMap", height); if (isNew) mat.SetFloat("_Parallax", 0.02f); mat.EnableKeyword("_PARALLAXMAP"); }
                if (isNew || see != SeeThrough.Solid) TextureAlpha.Apply(mat, see);
            }
            if (isNew) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Switches a material's glow on with this map.</summary>
        public static void Glow(Material mat, Texture map, float strength, string slot = "_EmissionMap")
        {
            if (!map) { if (mat.HasProperty("_EmissionMap") && mat.IsKeywordEnabled("_EMISSION")) { mat.SetTexture("_EmissionMap", null); mat.DisableKeyword("_EMISSION"); } return; }
            if (!string.IsNullOrEmpty(slot) && mat.HasProperty(slot)) mat.SetTexture(slot, map);
            foreach (var c in new[] { "_EmissionColor", "_EmissiveColor" }) if (mat.HasProperty(c)) mat.SetColor(c, new Color(strength, strength, strength, 1));
            mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        public static void Fill(Material mat, Texture colour, Texture normal, Texture mask, Texture height, TextureSlots s)
        {
            void Put(string slot, Texture t) { if (t && !string.IsNullOrEmpty(slot) && mat.HasProperty(slot)) mat.SetTexture(slot, t); }
            Put(s.colour, colour); Put(s.normal, normal); Put(s.mask, mask); Put(s.occlusion, mask); Put(s.height, height);
            // shaders made like Unity's own switch these on with keywords
            foreach (var k in mat.shader.keywordSpace.keywordNames)
            {
                if (k == "_NORMALMAP" && normal && s.normal != "") mat.EnableKeyword(k);
                if ((k == "_METALLICSPECGLOSSMAP" || k == "_METALLICGLOSSMAP") && mask && s.mask != "") mat.EnableKeyword(k);
                if (k == "_OCCLUSIONMAP" && mask && s.occlusion != "") mat.EnableKeyword(k);
                if (k == "_PARALLAXMAP" && height && s.height != "") mat.EnableKeyword(k);
            }
        }

        /// <summary>Something made with Texture Studio in this project.</summary>
        public class Made { public string name, path; public TextureRecipe recipe; public Material material; public Texture2D texture; public bool HasSteps => recipe; }

        /// <summary>Everything made here: with its steps, and what was saved before steps were kept (a material whose
        /// textures are called like it, with _Normal and _Mask).</summary>
        public static List<Made> All()
        {
            var l = new List<Made>(); var known = new HashSet<UnityEngine.Object>();
            foreach (var g in AssetDatabase.FindAssets("t:TextureRecipe", new[] { "Assets" }))
            {
                var r = AssetDatabase.LoadAssetAtPath<TextureRecipe>(AssetDatabase.GUIDToAssetPath(g)); if (!r) continue;
                foreach (var o in r.outputs) if (o) known.Add(o);
                l.Add(new Made { name = NameOf(r), path = AssetDatabase.GetAssetPath(r), recipe = r, material = r.outputs.OfType<Material>().FirstOrDefault(m => m), texture = r.outputs.OfType<Texture2D>().FirstOrDefault(t => t) });
            }
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); if (!p.EndsWith(".mat")) continue; var stem = p.Substring(0, p.Length - 4);
                // (the files say it without loading every material in the project)
                if (!System.IO.File.Exists(stem + ".png") || !System.IO.File.Exists(stem + "_Normal.png") || !System.IO.File.Exists(stem + "_Mask.png")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m || known.Contains(m)) continue;
                l.Add(new Made { name = m.name, path = p, material = m, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(stem + ".png") });
            }
            return l.OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string NameOf(TextureRecipe r) => r.name.EndsWith("_Studio") ? r.name.Substring(0, r.name.Length - 7) : r.name;

        /// <summary>The texture something older was probably made from: the one called the same without the ending.</summary>
        public static Texture2D OriginalOf(Texture2D t)
        {
            if (!t) return null; var p = AssetDatabase.GetAssetPath(t); var dir = System.IO.Path.GetDirectoryName(p).Replace('\\', '/'); var n = System.IO.Path.GetFileNameWithoutExtension(p);
            foreach (var end in new[] { "_Edited", "_Seamless", "_Varied" })
            {
                int i = n.IndexOf(end, StringComparison.OrdinalIgnoreCase); if (i <= 0) continue;
                foreach (var d in new[] { dir, System.IO.Path.GetDirectoryName(dir).Replace('\\', '/') }) foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif" }) { var o = AssetDatabase.LoadAssetAtPath<Texture2D>(d + "/" + n.Substring(0, i) + ext); if (o && o != t) return o; }
            }
            return null;
        }
    }
}
