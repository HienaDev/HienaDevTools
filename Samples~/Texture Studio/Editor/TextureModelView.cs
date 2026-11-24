using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Shows the result on a 3D shape (or your own model), lit, so bumps and shine can be judged. Drag to turn it, the wheel to come closer.</summary>
    public class TextureModelView : IDisposable
    {
        public enum Shape { Ball, Cube, Wall, Floor, Cylinder, Model }
        public static readonly string[] ShapeNames = { "Ball", "Cube", "Wall", "Floor", "Pillar", "My model" };

        PreviewRenderUtility m_Pru; Material m_Mat, m_Own, m_OwnFrom, m_Use; Vector2 m_Turn = new Vector2(25, 18); float m_Near = 1f, m_Light = 40f;
        readonly Dictionary<Shape, Mesh> m_Meshes = new Dictionary<Shape, Mesh>();
        GameObject m_ModelFor; readonly List<(Mesh mesh, Matrix4x4 at)> m_Parts = new List<(Mesh, Matrix4x4)>(); Bounds m_ModelBounds;
        public bool HasModel => m_Parts.Count > 0;

        void Ensure()
        {
            if (m_Pru != null) return;
            m_Pru = new PreviewRenderUtility(); m_Pru.camera.fieldOfView = 28; m_Pru.camera.nearClipPlane = 0.05f; m_Pru.camera.farClipPlane = 60;
            m_Pru.camera.clearFlags = CameraClearFlags.SolidColor; m_Pru.camera.backgroundColor = new Color(0.09f, 0.095f, 0.115f, 1);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            m_Mat = new Material(sh ? sh : Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public Material Material { get { Ensure(); return m_Mat; } }

        public void Set(Texture albedo, Texture normal, Texture mask, float tiling, bool hard, SeeThrough see = SeeThrough.Solid)
        {
            Ensure(); TextureAlpha.Apply(m_Mat, see);
            bool urp = m_Mat.HasProperty("_BaseMap"); string main = urp ? "_BaseMap" : "_MainTex";
            m_Mat.SetTexture(main, albedo); m_Mat.SetTextureScale(main, Vector2.one * tiling);
            m_Mat.SetTexture("_BumpMap", normal); if (normal) m_Mat.EnableKeyword("_NORMALMAP"); else m_Mat.DisableKeyword("_NORMALMAP");
            string mk = urp ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP";
            m_Mat.SetTexture("_MetallicGlossMap", mask); m_Mat.SetTexture("_OcclusionMap", mask);
            if (mask) { m_Mat.EnableKeyword(mk); m_Mat.EnableKeyword("_OCCLUSIONMAP"); m_Mat.SetFloat("_Smoothness", 1); m_Mat.SetFloat("_GlossMapScale", 1); }
            else { m_Mat.DisableKeyword(mk); m_Mat.DisableKeyword("_OCCLUSIONMAP"); m_Mat.SetFloat("_Smoothness", 0.2f); }
            foreach (var t in new[] { albedo, normal, mask }) if (t) t.filterMode = hard ? FilterMode.Point : FilterMode.Bilinear;
            m_Use = m_Mat;
        }

        /// <summary>The glow on whatever is shown now (null: none), and how dark the room is (0..1).</summary>
        public void SetGlow(Texture map, float strength, float tiling, string slot) { TextureMaterials.Glow(m_Use, map, strength, slot); if (map && m_Use.HasProperty("_EmissionMap")) m_Use.SetTextureScale("_EmissionMap", Vector2.one * tiling); Dark = map ? 0.75f : 0f; }
        public float Dark;

        Material m_NeverMat;
        /// <summary>Shows it with the Never repeats material.</summary>
        public void SetNever(NeverRepeats n, Texture albedo, Texture normal, Texture mask, float tiling, SeeThrough see, Texture2DArray[] tiles = null, float rare = 0.3f)
        {
            if (!m_NeverMat) m_NeverMat = new Material(Shader.Find(NeverRepeats.ShaderName)) { hideFlags = HideFlags.HideAndDontSave };
            m_NeverMat.SetTexture("_BaseMap", albedo); m_NeverMat.SetTextureScale("_BaseMap", Vector2.one * tiling); m_NeverMat.SetTexture("_BumpMap", normal); m_NeverMat.SetTexture("_MetallicGlossMap", mask); n.To(m_NeverMat); m_NeverMat.SetFloat("_Smoothness", mask ? 1 : 0.2f); NeverRepeats.Tiles(m_NeverMat, tiles != null ? tiles[0] : null, tiles != null ? tiles[1] : null, tiles != null ? tiles[2] : null, rare); TextureAlpha.Apply(m_NeverMat, see); m_Use = m_NeverMat;
        }

        /// <summary>Shows it with a material of yours instead (a copy of it, yours isn't changed).</summary>
        public void SetOwn(Material template, TextureSlots slots, Texture albedo, Texture normal, Texture mask, float tiling)
        {
            if (m_OwnFrom != template || !m_Own) { if (m_Own) UnityEngine.Object.DestroyImmediate(m_Own); m_Own = new Material(template) { hideFlags = HideFlags.HideAndDontSave }; m_OwnFrom = template; }
            else { m_Own.CopyPropertiesFromMaterial(template); }
            TextureMaterials.Fill(m_Own, albedo, normal, mask, null, slots); if (slots.colour != "" && m_Own.HasProperty(slots.colour)) m_Own.SetTextureScale(slots.colour, Vector2.one * tiling);
            m_Use = m_Own;
        }

        public Texture Render(Rect r, Shape shape, GameObject model)
        {
            Ensure();
            var parts = new List<(Mesh mesh, Matrix4x4 at)>(); Bounds b;
            if (shape == Shape.Model)
            {
                if (model != m_ModelFor) { Collect(model); m_ModelFor = model; }
                if (m_Parts.Count == 0) return null;
                float k = 2f / Mathf.Max(0.0001f, m_ModelBounds.size.magnitude / 1.4f); var fit = Matrix4x4.Scale(Vector3.one * k) * Matrix4x4.Translate(-m_ModelBounds.center);
                foreach (var p in m_Parts) parts.Add((p.mesh, fit * p.at));
                b = new Bounds(Vector3.zero, m_ModelBounds.size * k);
            }
            else
            {
                var m = MeshOf(shape); var at = Matrix4x4.identity;
                // (built-in shapes come in different sizes: brought to the same one)
                float side = Mathf.Max(0.001f, Mathf.Max(m.bounds.size.x, Mathf.Max(m.bounds.size.y, m.bounds.size.z)));
                float want = shape == Shape.Wall ? 2.4f : shape == Shape.Floor ? 6f : shape == Shape.Cube ? 1.4f : 2f;
                at = shape == Shape.Floor ? Matrix4x4.TRS(new Vector3(0, -0.25f, 0), Quaternion.Euler(90, 0, 0), Vector3.one * (want / side)) : Matrix4x4.Scale(Vector3.one * (want / side));
                parts.Add((m, at)); b = new Bounds(Vector3.zero, Vector3.one * 2);
            }
            m_Pru.BeginPreview(r, GUIStyle.none);
            var cam = m_Pru.camera; float dist = Mathf.Max(3f, b.size.magnitude * 1.55f) / m_Near;
            var turn = Quaternion.Euler(Mathf.Clamp(m_Turn.y, -85, 85), m_Turn.x, 0);
            cam.transform.position = turn * new Vector3(0, 0, -dist); cam.transform.rotation = turn;
            m_Pru.lights[0].intensity = 1.35f * (1 - Dark); m_Pru.lights[0].color = Color.white; m_Pru.lights[0].transform.rotation = Quaternion.Euler(m_Light, m_Turn.x + 40f, 0);
            m_Pru.lights[1].intensity = 0.45f * (1 - Dark); m_Pru.lights[1].color = new Color(0.65f, 0.75f, 1f); m_Pru.lights[1].transform.rotation = Quaternion.Euler(340, m_Turn.x + 215f, 0);
            m_Pru.ambientColor = new Color(0.28f, 0.29f, 0.32f) * (1 - Dark * 0.8f);
            foreach (var p in parts) for (int s = 0; s < p.mesh.subMeshCount; s++) m_Pru.DrawMesh(p.mesh, p.at, m_Use ? m_Use : m_Mat, s);
            m_Pru.Render(true, true);
            return m_Pru.EndPreview();
        }

        /// <summary>A decal on a plain wall, ball, box or floor.</summary>
        public Texture RenderDecal(Rect r, Shape shape, Material decal, float size, float aspect, float turn, float depth, bool brush = false)
        {
            Ensure(); m_Mat.SetTexture("_BaseMap", null); m_Mat.color = new Color(0.62f, 0.6f, 0.57f); foreach (var k in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_METALLICGLOSSMAP", "_OCCLUSIONMAP", "_EMISSION" }) m_Mat.DisableKeyword(k); m_Mat.SetFloat("_Smoothness", 0.15f); TextureAlpha.Apply(m_Mat, SeeThrough.Solid);
            var m = MeshOf(shape); float side = Mathf.Max(m.bounds.size.x, Mathf.Max(m.bounds.size.y, m.bounds.size.z)); var at = shape == Shape.Floor ? Matrix4x4.TRS(new Vector3(0, -0.25f, 0), Quaternion.Euler(90, 0, 0), Vector3.one * (3f / side)) : Matrix4x4.Scale(Vector3.one * ((shape == Shape.Wall ? 2.6f : shape == Shape.Cube ? 1.5f : 2f) / side));
            string key = brush + "|" + shape + "|" + size + "|" + aspect + "|" + turn + "|" + depth; if (key != m_DecalKey || !m_DecalMesh) { m_DecalKey = key; if (m_DecalMesh) UnityEngine.Object.DestroyImmediate(m_DecalMesh); m_DecalMesh = DecalPlacer.On_(m, at, shape == Shape.Floor ? new Vector3(0, -0.25f, 0) : new Vector3(0, 0, shape == Shape.Wall ? 0 : shape == Shape.Cube ? -0.75f : -1f), shape == Shape.Floor ? Vector3.up : Vector3.back, size, aspect, turn, depth, out m_DecalAt); if (brush && m_DecalMesh) m_DecalMesh.SetUVs(0, m_DecalMesh.uv.Select(u => (u - Vector2.one * 0.5f) * 2f).ToArray()); }
            m_Pru.BeginPreview(r, GUIStyle.none); var cam = m_Pru.camera; var rot = Quaternion.Euler(Mathf.Clamp(m_Turn.y, -85, 85), m_Turn.x, 0); cam.transform.position = rot * new Vector3(0, 0, -5.2f / m_Near); cam.transform.rotation = rot; m_Pru.lights[0].intensity = 1.3f; m_Pru.lights[0].color = Color.white; m_Pru.lights[0].transform.rotation = Quaternion.Euler(m_Light, m_Turn.x + 40f, 0); m_Pru.lights[1].intensity = 0.45f; m_Pru.lights[1].color = new Color(0.65f, 0.75f, 1f); m_Pru.lights[1].transform.rotation = Quaternion.Euler(340, m_Turn.x + 215f, 0); m_Pru.ambientColor = new Color(0.28f, 0.29f, 0.32f);
            m_Pru.DrawMesh(m, at, m_Mat, 0); if (m_DecalMesh && decal) m_Pru.DrawMesh(m_DecalMesh, m_DecalAt, decal, 0); m_Pru.Render(true, true); return m_Pru.EndPreview();
        }
        Mesh m_DecalMesh; Matrix4x4 m_DecalAt; string m_DecalKey = "";

        /// <summary>Turning and zooming. Returns true when it needs drawing again.</summary>
        public bool Mouse(Rect r, int button = 0)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("TextureModelView".GetHashCode(), FocusType.Passive);
            if (e.type == EventType.MouseDown && e.button == button && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { if (e.shift) m_Light = Mathf.Clamp(m_Light + e.delta.y * 0.5f, -10, 89); else m_Turn += new Vector2(e.delta.x, e.delta.y) * 0.5f; e.Use(); return true; }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition)) { m_Near = Mathf.Clamp(m_Near * (e.delta.y > 0 ? 0.92f : 1.08f), 0.4f, 4f); e.Use(); return true; }
            return false;
        }

        /// <summary>What's under the mouse: where on the texture, and which way on the texture is to the right and up as seen
        /// (for one unit of the model).</summary>
        public bool Hit(Rect r, Vector2 mouse, Shape shape, GameObject model, out Vector2 uv, out Vector2 right, out Vector2 up)
        {
            uv = right = up = Vector2.zero; Ensure(); var parts = new List<(TextureMesh mesh, Matrix4x4 at)>(); float dist;
            if (shape == Shape.Model) { if (model != m_ModelFor) { Collect(model); m_ModelFor = model; } if (m_Parts.Count == 0) return false; float k = 2f / Mathf.Max(0.0001f, m_ModelBounds.size.magnitude / 1.4f); var fit = Matrix4x4.Scale(Vector3.one * k) * Matrix4x4.Translate(-m_ModelBounds.center); foreach (var p in m_Parts) parts.Add((TextureMesh.Of(p.mesh), fit * p.at)); dist = Mathf.Max(3f, (m_ModelBounds.size * k).magnitude * 1.55f) / m_Near; }
            else { var m = MeshOf(shape); float side = Mathf.Max(0.001f, Mathf.Max(m.bounds.size.x, Mathf.Max(m.bounds.size.y, m.bounds.size.z))), want = shape == Shape.Wall ? 2.4f : shape == Shape.Floor ? 6f : shape == Shape.Cube ? 1.4f : 2f; parts.Add((TextureMesh.Of(m), shape == Shape.Floor ? Matrix4x4.TRS(new Vector3(0, -0.25f, 0), Quaternion.Euler(90, 0, 0), Vector3.one * (want / side)) : Matrix4x4.Scale(Vector3.one * (want / side)))); dist = Mathf.Max(3f, (Vector3.one * 2).magnitude * 1.55f) / m_Near; }
            var turn = Quaternion.Euler(Mathf.Clamp(m_Turn.y, -85, 85), m_Turn.x, 0); float tan = Mathf.Tan(28f * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1f, r.height / r.width), asp = r.width / r.height; var v = new Vector2((mouse.x - r.x) / r.width * 2 - 1, 1 - (mouse.y - r.y) / r.height * 2); var ray = new Ray(turn * new Vector3(0, 0, -dist), turn * new Vector3(v.x * tan * asp, v.y * tan, 1).normalized); float best = float.MaxValue; bool hit = false;
            foreach (var p in parts)
            {
                if (p.mesh == null || !p.mesh.Hit(ray, p.at, ref best, out int tri, out var bc)) continue; hit = true; var M = p.mesh; int a = M.t[tri], b = M.t[tri + 1], c = M.t[tri + 2]; uv = M.uv[a] * bc.x + M.uv[b] * bc.y + M.uv[c] * bc.z;
                Vector3 pa = p.at.MultiplyPoint3x4(M.v[a]), e1 = p.at.MultiplyPoint3x4(M.v[b]) - pa, e2 = p.at.MultiplyPoint3x4(M.v[c]) - pa; Vector2 d1 = M.uv[b] - M.uv[a], d2 = M.uv[c] - M.uv[a]; var n = Vector3.Cross(e1, e2).normalized; var cr = turn * Vector3.right; var rt = (cr - n * Vector3.Dot(cr, n)).normalized; var cu = turn * Vector3.up; var ut = (cu - n * Vector3.Dot(cu, n)); ut = (ut - rt * Vector3.Dot(ut, rt)).normalized;
                // a step on the surface as a step on the texture
                Vector2 Step(Vector3 s) { float A = Vector3.Dot(e1, e1), B = Vector3.Dot(e1, e2), C = Vector3.Dot(e2, e2), D = Vector3.Dot(s, e1), E = Vector3.Dot(s, e2), det = A * C - B * B; if (Mathf.Abs(det) < 1e-12f) return Vector2.zero; return d1 * ((D * C - E * B) / det) + d2 * ((E * A - D * B) / det); }
                right = Step(rt); up = Step(ut);
            }
            return hit;
        }

        public void Face(Shape s) { m_Turn = s == Shape.Wall ? new Vector2(12, 4) : s == Shape.Floor ? new Vector2(20, 32) : new Vector2(25, 18); m_Near = 1; }

        void Collect(GameObject model)
        {
            m_Parts.Clear(); if (!model) return;
            bool first = true; var toRoot = model.transform.worldToLocalMatrix;
            void Add(Mesh mesh, Transform t)
            {
                if (!mesh) return; var at = toRoot * t.localToWorldMatrix; m_Parts.Add((mesh, at));
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = at.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)));
                    if (first) { m_ModelBounds = new Bounds(c, Vector3.zero); first = false; } else m_ModelBounds.Encapsulate(c);
                }
            }
            foreach (var f in model.GetComponentsInChildren<MeshFilter>(true)) Add(f.sharedMesh, f.transform);
            foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(s.sharedMesh, s.transform);
        }

        Mesh MeshOf(Shape s)
        {
            if (m_Meshes.TryGetValue(s, out var m) && m) return m;
            m = Resources.GetBuiltinResource<Mesh>(s == Shape.Ball ? "New-Sphere.fbx" : s == Shape.Cube ? "Cube.fbx" : s == Shape.Cylinder ? "New-Cylinder.fbx" : "Quad.fbx");
            return m_Meshes[s] = m;
        }

        public void Dispose() { m_Pru?.Cleanup(); m_Pru = null; if (m_Mat) UnityEngine.Object.DestroyImmediate(m_Mat); if (m_Own) UnityEngine.Object.DestroyImmediate(m_Own); if (m_NeverMat) UnityEngine.Object.DestroyImmediate(m_NeverMat); }
    }

    /// <summary>A small window that asks for a name. Returns null when cancelled.</summary>
    public class NamePrompt : EditorWindow
    {
        string m_Label, m_Value, m_Result; Func<string, string> m_Note; bool m_Focused; string m_Ok;

        public static string Ask(string title, string label, string value, Func<string, string> note = null, string ok = "Save")
        {
            var w = CreateInstance<NamePrompt>(); w.titleContent = new GUIContent(title); w.m_Label = label; w.m_Value = value; w.m_Note = note; w.m_Ok = ok;
            w.minSize = w.maxSize = new Vector2(430, 138);
            var main = EditorGUIUtility.GetMainWindowPosition(); w.position = new Rect(main.center.x - 215, main.center.y - 90, 430, 138);
            w.ShowModalUtility();
            return w.m_Result;
        }

        void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.215f) : new Color(0.83f, 0.84f, 0.87f));
            var e = Event.current; string name = TextureShelf.Clean(m_Value ?? ""); bool ok = name.Length > 0;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && ok) { m_Result = name; e.Use(); Close(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
            GUILayout.BeginArea(new Rect(16, 12, position.width - 32, position.height - 22));
            GUILayout.Label(m_Label, EditorStyles.boldLabel);
            GUI.SetNextControlName("name");
            m_Value = EditorGUILayout.TextField(m_Value, new GUIStyle(EditorStyles.textField) { fontSize = 15, fixedHeight = 26 }, GUILayout.Height(26));
            if (!m_Focused) { EditorGUI.FocusTextInControl("name"); m_Focused = true; }
            GUILayout.Label(m_Note != null && ok ? m_Note(name) : "", EditorStyles.wordWrappedMiniLabel, GUILayout.Height(28));
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(24))) Close();
            using (new EditorGUI.DisabledScope(!ok)) { var old = GUI.backgroundColor; GUI.backgroundColor = new Color(1f, 0.62f, 0.2f); if (GUILayout.Button(m_Ok, GUILayout.Width(100), GUILayout.Height(24))) { m_Result = name; Close(); } GUI.backgroundColor = old; }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
