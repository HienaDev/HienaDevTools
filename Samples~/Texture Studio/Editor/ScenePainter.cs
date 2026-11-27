using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Paints a texture (moss, dirt, snow, blood...) onto one spot of the models in the scene. What's painted is a
    /// thin extra surface, a child of the object, so it moves with it; the object's material and texture aren't changed.</summary>
    public static class ScenePainter
    {
        public static Material Material; public static float Radius = 0.6f, Scatter; public static bool Erase;
        public static bool On { get; private set; } public static event Action Changed;
        static Vector3 s_LastDab; static bool s_Painting; static readonly HashSet<PaintedLayer> s_Touched = new HashSet<PaintedLayer>();

        public static void Set(bool on)
        {
            if (on == On) return; On = on; if (on) { DecalPlacer.Set(false); DecalPlacer.Fresh(); SceneView.duringSceneGui += Gui; var v = SceneView.lastActiveSceneView; if (v) { v.Focus(); v.Repaint(); } } else SceneView.duringSceneGui -= Gui; Changed?.Invoke(); SceneView.RepaintAll();
        }

        static void Gui(SceneView view)
        {
            var e = Event.current; if (!Material) { Set(false); return; } int id = GUIUtility.GetControlID("ScenePainter".GetHashCode(), FocusType.Passive); HandleUtility.AddDefaultControl(id); bool hit = DecalPlacer.Under(HandleUtility.GUIPointToWorldRay(e.mousePosition), e.type, out var at, out var normal); bool erase = Erase || e.control || e.command;
            Handles.BeginGUI(); GUI.Label(new Rect(56, 8, 640, 24), hit || e.type == EventType.Layout ? "Drag to paint  " + Material.name + "     Ctrl: rub out     Shift + wheel: brush size     Esc: stop" : "Nothing to paint on here: point at a model or a terrain     Esc: stop", new GUIStyle(EditorStyles.helpBox) { fontSize = 12 }); Handles.EndGUI();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Set(false); e.Use(); return; }
            if (e.type == EventType.ScrollWheel && e.shift) { float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x; Radius = Mathf.Clamp(Radius * (d > 0 ? 0.92f : 1.08f), 0.03f, 20f); e.Use(); Changed?.Invoke(); view.Repaint(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint(); if (hit && e.type == EventType.Repaint) { Handles.color = erase ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(0.5f, 1f, 0.5f, 0.95f); Handles.DrawWireDisc(at, normal, Radius); Handles.DrawWireDisc(at, normal, Radius * 0.6f); }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { GUIUtility.hotControl = id; s_Painting = true; s_Touched.Clear(); Undo.IncrementCurrentGroup(); if (hit) Stroke(at, normal, erase); e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && s_Painting) { if (hit && (at - s_LastDab).magnitude > Radius * 0.35f) Stroke(at, normal, erase); e.Use(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; s_Painting = false; Undo.SetCurrentGroupName(erase ? "Rub paint out" : "Paint"); e.Use(); }
        }
        static void Stroke(Vector3 at, Vector3 normal, bool erase) { s_LastDab = at; if (erase) Rub(at, Radius); else Dab(at, normal, Radius); }

        // ------------------------------------------------------------------ putting paint on

        public static int Dab(Vector3 at, Vector3 normal, float radius)
        {
            int made = 0; var box = new Bounds(at, Vector3.one * radius * 2); int under = 0;
            foreach (var l in TsFind.All<PaintedLayer>()) foreach (var d in l.dabs) if ((l.transform.TransformPoint(d.at) - at).magnitude < (radius + d.radius) * 0.95f) under = Mathf.Max(under, d.level + 1);
            DecalPlacer.LiftScale = 1 + Mathf.Min(under, 24) * 0.45f; bool wrap = DecalPlacer.Wrap; DecalPlacer.Wrap = false; List<DecalPlacer.Piece> pieces; try { pieces = DecalPlacer.Pieces(at, normal, radius * 2, radius * 0.75f, box); } finally { DecalPlacer.LiftScale = 1; DecalPlacer.Wrap = wrap; }
            foreach (var p in pieces)
            {
                var layer = p.owner.GetComponentsInChildren<PaintedLayer>(true).FirstOrDefault(l => l.transform.parent == p.owner && l.GetComponent<MeshRenderer>().sharedMaterial == Material && l.GetComponent<MeshFilter>().sharedMesh && l.GetComponent<MeshFilter>().sharedMesh.vertexCount < 200000);
                if (!layer)
                {
                    var go = new GameObject("Painted " + Material.name); go.transform.SetParent(p.owner, false); go.layer = p.owner.gameObject.layer; go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "Painted", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; go.AddComponent<DecalMark>(); layer = go.AddComponent<PaintedLayer>(); Undo.RegisterCreatedObjectUndo(go, "Paint"); s_Touched.Add(layer);
                }
                var f = layer.GetComponent<MeshFilter>(); Keep(layer, f); var mesh = f.sharedMesh; var v = new List<Vector3>(); mesh.GetVertices(v); var n = new List<Vector3>(); mesh.GetNormals(n); var uv = new List<Vector2>(); mesh.GetUVs(0, uv); var t = new List<int>(); if (mesh.vertexCount > 0) mesh.GetTriangles(t, 0);
                layer.dabs.Add(new PaintedLayer.Dab { at = p.owner.InverseTransformPoint(at), radius = radius, level = under, firstVertex = v.Count, vertices = p.v.Count, firstIndex = t.Count, indices = p.t.Count }); int b = v.Count; v.AddRange(p.v); n.AddRange(p.n); uv.AddRange(p.uv); foreach (var i in p.t) t.Add(b + i); mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(layer); made += p.t.Count / 3;
            }
            return made;
        }

        // (once per stroke the layer is remembered for Undo: its mesh is swapped for a copy, so the old one is still there)
        static void Keep(PaintedLayer l, MeshFilter f) { if (!s_Touched.Add(l)) return; Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { l, f }, "Paint"); var c = UnityEngine.Object.Instantiate(f.sharedMesh); c.name = "Painted"; f.sharedMesh = c; }

        /// <summary>Takes away the dabs whose middle is under the brush.</summary>
        public static int Rub(Vector3 at, float radius)
        {
            int gone = 0; foreach (var l in TsFind.All<PaintedLayer>())
            {
                var f = l.GetComponent<MeshFilter>(); var r = l.GetComponent<MeshRenderer>(); if (!f.sharedMesh || Material && r.sharedMaterial != Material || !r.bounds.Intersects(new Bounds(at, Vector3.one * radius * 2))) continue; var keep = l.dabs.Where(d => (l.transform.TransformPoint(d.at) - at).magnitude > radius * 0.9f).ToList(); if (keep.Count == l.dabs.Count) continue; Keep(l, f); gone += l.dabs.Count - keep.Count;
                var mesh = f.sharedMesh; var v = new List<Vector3>(); mesh.GetVertices(v); var n = new List<Vector3>(); mesh.GetNormals(n); var uv = new List<Vector2>(); mesh.GetUVs(0, uv); var t = new List<int>(); if (mesh.vertexCount > 0) mesh.GetTriangles(t, 0); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>(); var nd = new List<PaintedLayer.Dab>();
                foreach (var d in keep) { int b = nv.Count; var q = d; nv.AddRange(v.GetRange(d.firstVertex, d.vertices)); nn.AddRange(n.GetRange(d.firstVertex, d.vertices)); nu.AddRange(uv.GetRange(d.firstVertex, d.vertices)); q.firstIndex = nt.Count; for (int i = 0; i < d.indices; i++) nt.Add(t[d.firstIndex + i] - d.firstVertex + b); q.firstVertex = b; nd.Add(q); }
                l.dabs = nd; mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); mesh.SetTriangles(nt, 0); mesh.RecalculateBounds(); EditorUtility.SetDirty(l); if (nd.Count == 0 && !s_Painting) Undo.DestroyObjectImmediate(l.gameObject);
            }
            return gone;
        }
    }

    /// <summary>The Paint page.</summary>
    public partial class TextureStudioWindow
    {
        [SerializeField] Texture2D m_PaintTex, m_PaintNormal; [SerializeField] Material m_PaintMat; [SerializeField] string m_PaintName = ""; [SerializeField] float m_PaintTile = 1.5f, m_PaintEdge = 0.6f, m_PaintDetail = 0.3f, m_PaintFollow = 0.5f, m_PaintSoft = 0.25f, m_PaintTop, m_PaintShine = 0.15f; [SerializeField] Color m_PaintColour = Color.white; List<Material> m_PaintList; Vector2 m_PaintScroll, m_PaintListScroll; Material m_PaintTemp; TextureModelView m_PaintModel; Mesh m_PaintDemo;

        void PaintFill(Material m) { m.SetTexture("_BaseMap", m_PaintTex); m.SetTexture("_BumpMap", m_PaintNormal); m.SetColor("_BaseColor", m_PaintColour); m.SetFloat("_Tile", m_PaintTile); m.SetFloat("_Edge", m_PaintEdge); m.SetFloat("_EdgeSize", m_PaintDetail); m.SetFloat("_Follow", m_PaintFollow); m.SetFloat("_Dither", m_PaintSoft); m.SetFloat("_TopOnly", m_PaintTop); m.SetFloat("_Smoothness", m_PaintShine); }
        void PaintLoad(Material m) { m_PaintMat = m; m_PaintTex = m.GetTexture("_BaseMap") as Texture2D; m_PaintNormal = m.GetTexture("_BumpMap") as Texture2D; m_PaintColour = m.GetColor("_BaseColor"); m_PaintTile = m.GetFloat("_Tile"); m_PaintEdge = m.GetFloat("_Edge"); m_PaintDetail = m.GetFloat("_EdgeSize"); m_PaintFollow = m.GetFloat("_Follow"); m_PaintSoft = m.GetFloat("_Dither"); m_PaintTop = m.GetFloat("_TopOnly"); m_PaintShine = m.GetFloat("_Smoothness"); m_PaintName = m.name; ScenePainter.Material = m; }
        const string PaintShader = "HienaDev/Painted Layer";

        void PaintPage(Rect body)
        {
            const float gap = 6, listH = 126; var left = new Rect(body.x + gap, body.y + gap, 360, body.height - gap * 2); var right = new Rect(left.xMax + gap, left.y, body.xMax - left.xMax - gap * 2, left.height - listH - gap); var list = new Rect(right.x, right.yMax + gap, right.width, listH); EditorGUI.DrawRect(left, Panel); EditorGUI.BeginChangeCheck();
            GUILayout.BeginArea(new Rect(left.x + 12, left.y + 10, left.width - 24, left.height - 16)); m_PaintScroll = GUILayout.BeginScrollView(m_PaintScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); EditorGUIUtility.labelWidth = 116; GUILayout.Label("Paint the scene", m_Title); GUILayout.Label("Brush moss, dirt, snow or rust onto one spot of a wall or a floor. The wall's own material doesn't change, and the paint moves with it.", m_Hint); GUILayout.Space(6);
            GUILayout.Label("1. THE PAINT", m_Head); var t = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Texture", "One that loops"), m_PaintTex, typeof(Texture2D), false, GUILayout.Height(64)); if (t != m_PaintTex) { m_PaintTex = t; if (t) { var p = AssetDatabase.GetAssetPath(t); var n = AssetDatabase.LoadAssetAtPath<Texture2D>(System.IO.Path.ChangeExtension(p, null) + "_Normal.png"); if (n) m_PaintNormal = n; m_PaintName = t.name + " paint"; m_PaintMat = null; } }
            if (GUILayout.Button(new GUIContent(ScenePick.On ? "Click an object in the Scene view... (Esc)" : "Take it from something in the scene", "Click an object in the Scene view: its texture, normal map and colour become the paint"))) { if (ScenePick.On) ScenePick.Stop(); else if (!SceneView.lastActiveSceneView) ShowNotification(new GUIContent("Open the Scene view first")); else { ScenePainter.Set(false); ScenePick.Start(m => { var o = ScenePick.Of(m); if (!o.tex) { ShowNotification(new GUIContent(m.name + " has no texture")); return; } m_PaintTex = o.tex; if (o.normal) m_PaintNormal = o.normal; m_PaintColour = o.colour; m_PaintName = m.name + " paint"; m_PaintMat = null; Repaint(); }); } }
            m_PaintNormal = (Texture2D)EditorGUILayout.ObjectField("Normal map", m_PaintNormal, typeof(Texture2D), false, GUILayout.Height(18)); m_PaintColour = EditorGUILayout.ColorField("Colour", m_PaintColour); m_PaintTile = EditorGUILayout.Slider(new GUIContent("Texture size", "Units one tile covers"), m_PaintTile, 0.1f, 10f); m_PaintShine = EditorGUILayout.Slider("Shine", m_PaintShine, 0f, 1f);
            var blocky = new[] { m_PaintTex, m_PaintNormal }.Where(x => x && x.filterMode == FilterMode.Point).ToList(); if (blocky.Count > 0) { EditorGUILayout.HelpBox((blocky.Count == 2 ? "These textures are" : "\"" + blocky[0].name + "\" is") + " set to look pixelated (Filter Mode: Point, from a Crunch step), so the paint and its shine look blocky.", MessageType.Info); if (GUILayout.Button("Make " + (blocky.Count == 2 ? "them" : "it") + " smooth")) foreach (var x in blocky) { var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(x)) as TextureImporter; if (imp) { imp.filterMode = FilterMode.Bilinear; if (imp.userData == "HienaHard") imp.userData = ""; imp.SaveAndReimport(); } } }
            GUILayout.Space(6); GUILayout.Label("2. ITS EDGE", m_Head); m_PaintEdge = EditorGUILayout.Slider("Uneven", m_PaintEdge, 0f, 1f); m_PaintDetail = EditorGUILayout.Slider(new GUIContent("Detail size", "Big or small bites out of the edge"), m_PaintDetail, 0.02f, 2f); m_PaintFollow = EditorGUILayout.Slider(new GUIContent("Follow its shapes", "Its light parts stay longest"), m_PaintFollow, 0f, 1f); m_PaintSoft = EditorGUILayout.Slider(new GUIContent("Soft", "Dotted, so it still works with every fog"), m_PaintSoft, 0f, 1f); m_PaintTop = EditorGUILayout.Slider(new GUIContent("Only on top", "Only where the surface points up: snow, dust"), m_PaintTop, 0f, 1f);
            bool ch = EditorGUI.EndChangeCheck(); if (ch && m_PaintMat) { Undo.RecordObject(m_PaintMat, "Texture Studio: paint"); PaintFill(m_PaintMat); EditorUtility.SetDirty(m_PaintMat); AssetDatabase.SaveAssetIfDirty(m_PaintMat); }
            GUILayout.Space(6); GUILayout.Label("3. PAINT", m_Head); if (m_PaintMat) GUILayout.Label("Changes above also change what you've painted with \"" + m_PaintMat.name + "\".", m_Hint); m_PaintName = EditorGUILayout.TextField("Name", m_PaintName); var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            using (new EditorGUI.DisabledScope(!m_PaintTex)) { if (!m_PaintMat) { if (GUILayout.Button(new GUIContent("Save and start painting", "In Assets/Paint"), GUILayout.Height(30))) EditorApplication.delayCall += () => { SavePaint("Assets/Paint"); if (SceneView.lastActiveSceneView) ScenePainter.Set(true); Repaint(); }; } else { GUI.backgroundColor = ScenePainter.On ? Accent : old; if (GUILayout.Button(ScenePainter.On ? "Stop painting (Esc)" : "Paint in the Scene view", GUILayout.Height(30))) { ScenePainter.Material = m_PaintMat; if (!SceneView.lastActiveSceneView) ShowNotification(new GUIContent("Open the Scene view first")); else ScenePainter.Set(!ScenePainter.On); } GUI.backgroundColor = old; if (GUILayout.Button("Make it a new paint")) { m_PaintMat = null; ScenePainter.Set(false); } } } GUI.backgroundColor = old;
            ScenePainter.Radius = EditorGUILayout.Slider(new GUIContent("Brush size", "Shift + wheel in the scene"), ScenePainter.Radius, 0.03f, 10f); ScenePainter.Erase = EditorGUILayout.ToggleLeft(new GUIContent("Rub out", "Or hold Ctrl while painting"), ScenePainter.Erase); GUILayout.Label("Drag over your models in the Scene view. What you paint is a child of each object, so it moves along. Undo works on whole strokes.", m_Hint); EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView(); GUILayout.EndArea();
            EditorGUI.DrawRect(right, new Color(0.07f, 0.075f, 0.09f)); if (!m_PaintTex) GUI.Label(right, "Pick a texture on the left.", m_Empty);
            else if (Shader.Find(PaintShader)) { if (!m_PaintTemp) m_PaintTemp = new Material(Shader.Find(PaintShader)) { hideFlags = HideFlags.HideAndDontSave }; PaintFill(m_PaintTemp); if (m_PaintModel == null) { m_PaintModel = new TextureModelView(); m_PaintModel.Face(TextureModelView.Shape.Wall); } if (m_PaintModel.Mouse(right)) Repaint(); if (Event.current.type == EventType.Repaint && right.width > 20 && right.height > 20) { var im = m_PaintModel.RenderDecal(right, TextureModelView.Shape.Wall, m_PaintTemp, 1.9f, 1, 0, 0.5f, true); if (im) GUI.DrawTexture(right, im, ScaleMode.StretchToFill, false); } }
            EditorGUI.DrawRect(list, Panel); if (m_PaintList == null) m_PaintList = AssetDatabase.IsValidFolder("Assets/Paint") ? AssetDatabase.FindAssets("t:Material", new[] { "Assets/Paint" }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m && m.shader.name == PaintShader).ToList() : new List<Material>(); m_PaintList.RemoveAll(m => !m); GUI.Label(new Rect(list.x + 10, list.y + 5, list.width, 16), "YOUR PAINTS (" + m_PaintList.Count + ")" + (m_PaintList.Count > 0 ? ": CLICK ONE TO USE IT" : ""), m_Head); if (m_PaintList.Count == 0) { GUI.Label(new Rect(list.x + 10, list.y + 30, list.width - 20, 30), "The ones you save show up here.", m_Hint); return; }
            m_PaintListScroll = GUI.BeginScrollView(new Rect(list.x + 8, list.y + 24, list.width - 14, listH - 28), m_PaintListScroll, new Rect(0, 0, Mathf.Max(list.width - 20, m_PaintList.Count * 92), 84), false, false); for (int i = 0; i < m_PaintList.Count; i++) { var m = m_PaintList[i]; var c = new Rect(i * 92, 0, 86, 82); EditorGUI.DrawRect(c, m == m_PaintMat ? new Color(Accent.r, Accent.g, Accent.b, 0.45f) : Ink(0.05f)); var mt = m.GetTexture("_BaseMap"); if (mt) GUI.DrawTexture(new Rect(c.x + 14, c.y + 3, 58, 58), mt, ScaleMode.ScaleAndCrop); GUI.Label(new Rect(c.x + 2, c.yMax - 19, c.width - 4, 16), m.name, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip }); if (GUI.Button(c, new GUIContent("", m.name), GUIStyle.none)) { PaintLoad(m); if (SceneView.lastActiveSceneView) ScenePainter.Set(true); } } GUI.EndScrollView();
        }

        Material SavePaint(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder)); var name = TextureShelf.Clean(m_PaintName); if (name.Length == 0) name = "Paint"; var m = new Material(Shader.Find(PaintShader)); PaintFill(m); AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".mat")); AssetDatabase.SaveAssetIfDirty(m); m_PaintMat = m; m_PaintName = m.name; ScenePainter.Material = m; m_PaintList = null; return m;
        }
    }

    /// <summary>Finds scene objects the same way on Unity 6.0 to 6.6+ (6.6 renamed the calls, the old ones only warn).</summary>
    static class TsFind
    {
        public static T[] All<T>(bool inactive = false) where T : UnityEngine.Object
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsByType<T>(inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
        }
        public static T Any<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>();
    }
}
