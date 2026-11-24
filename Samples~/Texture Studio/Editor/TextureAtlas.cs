using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HienaDev.DevTools
{
    /// <summary>Many materials into one: their colour, normal, shine and glow textures are packed side by side into one
    /// set of textures with one material, and the meshes' UVs are moved onto their part of it. Textures that repeat on
    /// a model are baked repeated into their part, so nothing changes on screen.</summary>
    public static class TextureAtlas
    {
        [Serializable]
        public class Options
        {
            public string name = "Atlas", folder = "Assets/Atlases";
            public int size = 1024; public bool shrinkToFit = true; public int padding = 4;
            public Material shaderFrom;
            public bool putOnObjects = true;
            public int maxRepeats = 8;
        }

        public class Entry
        {
            public Material mat; public Texture2D colour, normal, metal, occlusion, glow;
            public Vector2 uvMin = Vector2.zero, uvMax = Vector2.one; // the part of the (repeating) texture the meshes use
            public Vector2Int tiles = Vector2Int.one, want; public RectInt cell, inner;
            public bool clamped;
            public Vector4 st = new Vector4(1, 1, 0, 0);
        }

        public class Plan { public List<Entry> entries = new List<Entry>(); public int size; public float scale = 1; public bool hard, normal, mask, glow; public string warning = ""; }

        static Texture2D Tex(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n) && m.GetTexture(n) is Texture2D t) return t; return null; }
        static Color Col(Material m, params string[] names) { foreach (var n in names) if (m.HasProperty(n)) return m.GetColor(n); return Color.white; }
        static float Num(Material m, string n, float d) => m.HasProperty(n) ? m.GetFloat(n) : d;
        static Vector4 ST(Material m) { foreach (var n in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(n)) { var s = m.GetTextureScale(n); var o = m.GetTextureOffset(n); return new Vector4(s.x, s.y, o.x, o.y); } return new Vector4(1, 1, 0, 0); }

        /// <summary>Where each material goes in the atlas. The renderers tell how far their UVs go (for repeating textures).</summary>
        public static Plan Lay(IList<Material> mats, IList<Renderer> users, Options o)
        {
            var p = new Plan();
            foreach (var m in mats.Where(x => x).Distinct())
            {
                var e = new Entry { mat = m, colour = Tex(m, "_BaseMap", "_MainTex", "_BaseColorMap"), normal = Tex(m, "_BumpMap", "_NormalMap"), metal = Tex(m, "_MetallicGlossMap", "_MaskMap"), occlusion = Tex(m, "_OcclusionMap"), glow = m.IsKeywordEnabled("_EMISSION") ? Tex(m, "_EmissionMap", "_EmissiveColorMap") : null, st = ST(m) };
                bool first = true;
                foreach (var r in users)
                {
                    if (!r) continue; var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh; if (!mesh) continue;
                    var rm = r.sharedMaterials; var snap = MeshSnapshot.Take(mesh); if (snap.uvs[0] == null) continue;
                    for (int s = 0; s < snap.indices.Length && s < rm.Length; s++)
                    {
                        if (rm[s] != m || snap.topologies[s] != MeshTopology.Triangles) continue;
                        foreach (int v in snap.indices[s])
                        {
                            var u = snap.uvs[0][v]; var w = new Vector2(u.x * e.st.x + e.st.z, u.y * e.st.y + e.st.w);
                            if (first) { e.uvMin = e.uvMax = w; first = false; } else { e.uvMin = Vector2.Min(e.uvMin, w); e.uvMax = Vector2.Max(e.uvMax, w); }
                        }
                    }
                }
                if (first) { e.uvMin = Vector2.zero; e.uvMax = Vector2.one; }
                // whole repeats only, so the baked part lines up with the texture
                var lo = new Vector2(Mathf.Floor(e.uvMin.x + 1e-4f), Mathf.Floor(e.uvMin.y + 1e-4f)); var hi = new Vector2(Mathf.Ceil(e.uvMax.x - 1e-4f), Mathf.Ceil(e.uvMax.y - 1e-4f));
                e.tiles = new Vector2Int(Mathf.Max(1, (int)(hi.x - lo.x)), Mathf.Max(1, (int)(hi.y - lo.y)));
                if (e.tiles.x > o.maxRepeats || e.tiles.y > o.maxRepeats) { e.clamped = true; e.tiles = Vector2Int.Min(e.tiles, new Vector2Int(o.maxRepeats, o.maxRepeats)); }
                e.uvMin = lo; e.uvMax = lo + (Vector2)e.tiles;
                int tw = e.colour ? e.colour.width : 64, th = e.colour ? e.colour.height : 64;
                e.want = new Vector2Int(tw * e.tiles.x, th * e.tiles.y);
                p.entries.Add(e);
            }
            p.hard = p.entries.Count > 0 && p.entries.Where(e => e.colour).All(e => e.colour.filterMode == FilterMode.Point) && p.entries.Any(e => e.colour);
            p.normal = p.entries.Any(e => e.normal); p.glow = p.entries.Any(e => e.glow);
            p.mask = p.entries.Any(e => e.metal || e.occlusion) || p.entries.Select(e => (Num(e.mat, "_Metallic", 0), Num(e.mat, "_Smoothness", Num(e.mat, "_Glossiness", 0.5f)))).Distinct().Count() > 1;
            if (p.entries.Any(e => e.clamped)) p.warning = "Some textures repeat more than " + o.maxRepeats + " times on their models: those parts repeat only " + o.maxRepeats + " times in the atlas.";
            // pack, as big as fits (never bigger than the textures were)
            int size = o.size;
            if (!Pack(p.entries, size, o.padding, 1f)) { float lo2 = 0.001f, hi2 = 1f; for (int i = 0; i < 24; i++) { float mid = (lo2 + hi2) / 2; if (Pack(p.entries, size, o.padding, mid)) lo2 = mid; else hi2 = mid; } Pack(p.entries, size, o.padding, lo2); p.scale = lo2; }
            else if (o.shrinkToFit) { while (size > 64 && Pack(p.entries, size / 2, o.padding, 1f)) size /= 2; Pack(p.entries, size, o.padding, 1f); }
            p.size = size;
            if (p.scale < 0.999f && p.warning == "") p.warning = "Textures are shrunk to " + Mathf.RoundToInt(p.scale * 100) + "% to fit. A bigger atlas keeps more detail.";
            return p;
        }

        /// <summary>Shelf packing: tallest first, rows left to right. False if it doesn't fit.</summary>
        static bool Pack(List<Entry> es, int size, int pad, float scale)
        {
            int x = 0, y = 0, row = 0;
            foreach (var e in es.OrderByDescending(e => e.want.y * scale).ThenByDescending(e => e.want.x))
            {
                int w = Mathf.Max(4, Mathf.RoundToInt(e.want.x * scale)) + pad * 2, h = Mathf.Max(4, Mathf.RoundToInt(e.want.y * scale)) + pad * 2;
                if (w > size || h > size) return false;
                if (x + w > size) { x = 0; y += row; row = 0; }
                if (y + h > size) return false;
                e.cell = new RectInt(x, y, w, h); e.inner = new RectInt(x + pad, y + pad, w - pad * 2, h - pad * 2);
                x += w; row = Mathf.Max(row, h);
            }
            return true;
        }

        // ---------------------------------------------------------------- baking the textures
        enum Map { Colour, Normal, Mask, Glow }

        static Pixels ReadAny(Texture2D t, bool normal)
        {
            var p = Pixels.Read(t); if (p == null || !normal) return p;
            // normal maps read back from the GPU come packed (x in alpha, y in green): unpack them
            int packed = 0; for (int i = 0; i < p.c.Length; i += 97) if (p.c[i].r > 0.95f && p.c[i].b > 0.95f) packed++;
            if (packed > p.c.Length / 97 * 0.9f) for (int i = 0; i < p.c.Length; i++) { float x = p.c[i].a * 2 - 1, y = p.c[i].g * 2 - 1, z = Mathf.Sqrt(Mathf.Max(0, 1 - x * x - y * y)); p.c[i] = new Color(x * 0.5f + 0.5f, y * 0.5f + 0.5f, z * 0.5f + 0.5f, 1); }
            return p;
        }

        static Pixels Bake(Plan plan, Map map)
        {
            bool srgb = map == Map.Colour || map == Map.Glow;
            var outp = new Pixels(plan.size, plan.size, srgb);
            var bg = map == Map.Normal ? new Color(0.5f, 0.5f, 1, 1) : map == Map.Mask ? new Color(0, 1, 0, 0.5f) : new Color(0, 0, 0, map == Map.Colour ? 0 : 1);
            for (int i = 0; i < outp.c.Length; i++) outp.c[i] = bg;
            foreach (var e in plan.entries)
            {
                var m = e.mat; Pixels a = null, b = null; Color tint = Color.white; Color flat = bg;
                if (map == Map.Colour) { a = e.colour ? ReadAny(e.colour, false) : null; tint = Col(m, "_BaseColor", "_Color"); flat = tint; }
                else if (map == Map.Normal) { a = e.normal ? ReadAny(e.normal, true) : null; }
                else if (map == Map.Glow)
                {
                    bool on = m.IsKeywordEnabled("_EMISSION"); tint = on ? Col(m, "_EmissionColor", "_EmissiveColor") : Color.black; tint.a = 1;
                    a = on && e.glow ? ReadAny(e.glow, false) : null; flat = on ? tint : Color.black; flat.a = 1;
                }
                else { a = e.metal ? ReadAny(e.metal, false) : null; b = e.occlusion ? ReadAny(e.occlusion, false) : null; }
                // shrink the source to the size one repeat gets, so nothing sparkles (pixel art is picked from, not shrunk)
                int rw = Mathf.Max(1, Mathf.RoundToInt(e.inner.width / (float)e.tiles.x)), rh = Mathf.Max(1, Mathf.RoundToInt(e.inner.height / (float)e.tiles.y));
                if (!plan.hard && a != null && (a.w > rw * 1.5f || a.h > rh * 1.5f)) a = a.Resized(rw, rh);
                if (!plan.hard && b != null && (b.w > rw * 1.5f || b.h > rh * 1.5f)) b = b.Resized(rw, rh);
                float metal = Num(m, "_Metallic", 0), smooth = Num(m, "_Smoothness", Num(m, "_Glossiness", 0.5f)), occ = Num(m, "_OcclusionStrength", 1);
                for (int y = e.cell.yMin; y < e.cell.yMax; y++)
                    for (int x = e.cell.xMin; x < e.cell.xMax; x++)
                    {
                        // padding repeats the edge pixels of the part
                        float lx = Mathf.Clamp01((x + 0.5f - e.inner.x) / e.inner.width), ly = Mathf.Clamp01((y + 0.5f - e.inner.y) / e.inner.height);
                        float u = lx * e.tiles.x, v = ly * e.tiles.y; Color c;
                        Color S(Pixels px) => plan.hard ? px[Mathf.FloorToInt(Mathf.Min(u * px.w, e.tiles.x * px.w - 0.001f)), Mathf.FloorToInt(Mathf.Min(v * px.h, e.tiles.y * px.h - 0.001f))] : px.Sample(u * px.w, v * px.h);
                        if (map == Map.Mask)
                        {
                            float mt = metal, sm = smooth, ao = 1;
                            if (a != null) { var s = S(a); mt = s.r; sm = s.a * smooth; }
                            if (b != null) ao = Mathf.Lerp(1, S(b).g, occ);
                            c = new Color(mt, ao, 0, sm);
                        }
                        else if (a != null) { c = S(a) * tint; if (map == Map.Glow) c.a = 1; }
                        else c = flat;
                        outp.c[y * plan.size + x] = c;
                    }
            }
            return outp;
        }

        public class Made { public Material material; public Texture2D colour, normal, mask, glow; public int meshes, objects; public Plan plan; }

        /// <summary>Makes the atlas textures and material, and (if asked) moves the objects' UVs onto it and gives them the one material.</summary>
        public static Made Make(IList<Material> mats, IList<Renderer> users, Options o)
        {
            var plan = Lay(mats, users, o); if (plan.entries.Count == 0) return null;
            string folder = MakeFolder(o.folder.TrimEnd('/') + "/" + Safe(o.name));
            var made = new Made { plan = plan };
            var like = plan.entries.Select(e => e.colour).FirstOrDefault(t => t);
            try
            {
                EditorUtility.DisplayProgressBar("Atlas", "Colour", 0.1f); made.colour = Pixels.SaveAt(Bake(plan, Map.Colour), folder + "/" + Safe(o.name) + "_Colour.png", like, false, plan.hard, true);
                if (plan.normal) { EditorUtility.DisplayProgressBar("Atlas", "Normals", 0.35f); made.normal = Pixels.SaveAt(Bake(plan, Map.Normal), folder + "/" + Safe(o.name) + "_Normal.png", like, true, plan.hard); }
                if (plan.mask) { EditorUtility.DisplayProgressBar("Atlas", "Shine", 0.55f); made.mask = Pixels.SaveAt(Bake(plan, Map.Mask), folder + "/" + Safe(o.name) + "_Mask.png", like, false, plan.hard); }
                if (plan.glow) { EditorUtility.DisplayProgressBar("Atlas", "Glow", 0.7f); made.glow = Pixels.SaveAt(Bake(plan, Map.Glow), folder + "/" + Safe(o.name) + "_Glow.png", like, false, plan.hard); }
                foreach (var t in new[] { made.mask }) if (t) { var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)); if (imp.sRGBTexture) { imp.sRGBTexture = false; imp.SaveAndReimport(); } }
                foreach (var t in new[] { made.colour, made.normal, made.mask, made.glow }) if (t) { var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)); imp.wrapMode = TextureWrapMode.Clamp; if (imp.mipmapEnabled && o.padding < 4) imp.mipmapEnabled = false; imp.SaveAndReimport(); }
                // the material: the shader and settings of the first material (or the one picked), with the atlas textures
                var template = o.shaderFrom ? o.shaderFrom : plan.entries[0].mat;
                var slots = TextureSlots.Guess(template.shader);
                made.material = TextureMaterials.Make(folder + "/" + Safe(o.name) + ".mat", made.colour, made.normal, made.mask, null, template, slots);
                var mat = made.material;
                foreach (var n in new[] { "_BaseColor", "_Color" }) if (mat.HasProperty(n)) mat.SetColor(n, Color.white);
                foreach (var n in new[] { slots.colour, slots.normal, slots.mask, "_BaseMap", "_MainTex" }) if (!string.IsNullOrEmpty(n) && mat.HasProperty(n)) { mat.SetTextureScale(n, Vector2.one); mat.SetTextureOffset(n, Vector2.zero); }
                if (!made.normal && !string.IsNullOrEmpty(slots.normal) && mat.HasProperty(slots.normal)) { mat.SetTexture(slots.normal, null); mat.DisableKeyword("_NORMALMAP"); }
                if (made.mask) { if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1); if (mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", 1); if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1); if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 1); }
                else foreach (var n in new[] { slots.mask, slots.occlusion }) if (!string.IsNullOrEmpty(n) && mat.HasProperty(n)) mat.SetTexture(n, null);
                if (made.glow) TextureMaterials.Glow(mat, made.glow, 1); else TextureMaterials.Glow(mat, null, 0);
                EditorUtility.SetDirty(mat);
                if (o.putOnObjects) Remap(plan, users, mat, folder, made);
                AssetDatabase.SaveAssets();
            }
            finally { EditorUtility.ClearProgressBar(); }
            return made;
        }

        static string MakeFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/'); if (!folder.StartsWith("Assets")) folder = "Assets/" + folder;
            var parts = folder.Split('/'); string at = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = at + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]); at = next; }
            return folder;
        }

        /// <summary>Saves a mesh asset; one saved before at the same place is updated, so what uses it keeps working.</summary>
        static Mesh SaveMesh(string name, MeshSnapshot s, string folder)
        {
            string path = folder + "/" + name + ".asset"; var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { s.ToMesh(old); old.name = name; EditorUtility.SetDirty(old); return old; }
            var m = s.ToMesh(); m.name = name; AssetDatabase.CreateAsset(m, path); return m;
        }

        static string Safe(string n) { foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return string.IsNullOrWhiteSpace(n) ? "Atlas" : n.Trim(); }

        /// <summary>New meshes with their UVs on the atlas; the submeshes that used atlas materials become one, with the atlas material.</summary>
        static void Remap(Plan plan, IList<Renderer> users, Material atlas, string folder, Made made)
        {
            var byMat = plan.entries.ToDictionary(e => e.mat); var cache = new Dictionary<string, (Mesh mesh, Material[] mats)>();
            Undo.SetCurrentGroupName("Atlas");
            foreach (var r in users)
            {
                if (!r) continue; var mf = r.GetComponent<MeshFilter>(); var sk = r as SkinnedMeshRenderer; var mesh = sk ? sk.sharedMesh : mf ? mf.sharedMesh : null; if (!mesh) continue;
                var rm = r.sharedMaterials; if (!rm.Any(m => m && byMat.ContainsKey(m))) continue;
                string key = mesh.GetHashCode() + "|" + string.Join(",", rm.Select(m => m ? m.GetHashCode() : 0));
                if (!cache.TryGetValue(key, out var res))
                {
                    var e = EditMesh.From(MeshSnapshot.Take(mesh)); if (e.uv[0] == null) continue;
                    var o = EditMesh.LikeOf(e); o.idx.Clear(); o.topo.Clear();
                    var keptMats = new List<Material>(); var atlasIdx = new List<int>();
                    var copies = new Dictionary<(int, int), int>(); // (vertex, which part of the atlas) -> new vertex
                    for (int s = 0; s < e.idx.Count; s++)
                    {
                        var m = s < rm.Length ? rm[s] : null;
                        if (m && byMat.TryGetValue(m, out var en) && e.topo[s] == MeshTopology.Triangles)
                        {
                            int part = plan.entries.IndexOf(en);
                            foreach (int v in e.idx[s])
                            {
                                if (!copies.TryGetValue((v, part), out int nv))
                                {
                                    nv = o.Copy(e, v); var u = e.uv[0][v]; var w = new Vector2(u.x * en.st.x + en.st.z, u.y * en.st.y + en.st.w);
                                    var l = new Vector2((w.x - en.uvMin.x) / en.tiles.x, (w.y - en.uvMin.y) / en.tiles.y);
                                    if (en.clamped) l = new Vector2(Mathf.Repeat(l.x, 1.0001f), Mathf.Repeat(l.y, 1.0001f));
                                    o.uv[0][nv] = new Vector4((en.inner.x + Mathf.Clamp01(l.x) * en.inner.width) / plan.size, (en.inner.y + Mathf.Clamp01(l.y) * en.inner.height) / plan.size, u.z, u.w);
                                    copies[(v, part)] = nv;
                                }
                                atlasIdx.Add(nv);
                            }
                        }
                        else
                        {
                            var l = new List<int>(); foreach (int v in e.idx[s]) { if (!copies.TryGetValue((v, -1), out int nv)) { nv = o.Copy(e, v); copies[(v, -1)] = nv; } l.Add(nv); }
                            o.idx.Add(l); o.topo.Add(e.topo[s]); keptMats.Add(m);
                        }
                    }
                    o.idx.Insert(0, atlasIdx); o.topo.Insert(0, MeshTopology.Triangles); keptMats.Insert(0, atlas);
                    // one mesh asset per mesh + materials (a shared mesh with other materials gets other UVs)
                    string mats = string.Join("+", rm.Where(m => m && byMat.ContainsKey(m)).Select(m => m.name).Distinct()); if (mats.Length > 40) mats = mats.Substring(0, 40);
                    var saved = SaveMesh(Safe(mesh.name + " atlas " + mats), o.ToSnapshot(), folder);
                    res = (saved, keptMats.ToArray()); cache[key] = res; made.meshes++;
                }
                if (sk) { Undo.RecordObject(sk, "Atlas"); sk.sharedMesh = res.mesh; } else { Undo.RecordObject(mf, "Atlas"); mf.sharedMesh = res.mesh; }
                Undo.RecordObject(r, "Atlas"); r.sharedMaterials = res.mats; made.objects++;
            }
        }
    }
}
