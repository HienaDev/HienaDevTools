using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Puts a decal in the scene: cuts out the piece of the surfaces under it and lays the picture on that. It
    /// follows curves and bumps (it stops at sharp corners), and works with any shader and pipeline (no renderer feature needed).</summary>
    public static class DecalPlacer
    {
        public static Material Material; public static float Size = 1f, RandomSize = 0.15f, Turn, RandomTurn = 1f, Depth = 0.35f, Aspect = 1f;
        public static bool On { get; private set; }
        public static event Action Changed;

        static GameObject s_Ghost; static float s_NextSize = 1, s_NextTurn; static Vector2 s_GhostAt; static string s_GhostKey = "";
        static int s_NextCell; public static int ForceCell = -1;
        static void Roll() { s_NextSize = 1 + UnityEngine.Random.Range(-0.7f, 0.7f) * RandomSize; s_NextTurn = UnityEngine.Random.Range(-180f, 180f) * RandomTurn; var sh = SheetOf(Material); int last = s_NextCell; s_NextCell = sh.count > 1 ? UnityEngine.Random.Range(0, sh.count) : 0; if (sh.count > 1 && s_NextCell == last) s_NextCell = (s_NextCell + 1) % sh.count; }
        /// <summary>A decal picture that holds several (a sheet): how many across, down, and in all. Written on the material's import settings.</summary>
        public static (int cols, int rows, int count) SheetOf(Material m)
        {
            if (!m) return (1, 1, 1); var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m)); var d = imp ? imp.userData : ""; if (d == null || !d.StartsWith("HienaSheet:")) return (1, 1, 1);
            var p = d.Substring(11).Split(','); return p.Length == 3 && int.TryParse(p[0], out int c) && int.TryParse(p[1], out int r) && int.TryParse(p[2], out int n) && c > 0 && r > 0 ? (c, r, Mathf.Clamp(n, 1, c * r)) : (1, 1, 1);
        }
        public static void SetSheet(Material m, int cols, int rows, int count) { var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(m)); if (!imp) return; imp.userData = cols * rows > 1 ? "HienaSheet:" + cols + "," + rows + "," + count : ""; imp.SaveAndReimport(); }
        /// <summary>Points the picture at one cell of the sheet.</summary>
        static void ToCell(Mesh mesh, Material m, int cell)
        {
            var sh = SheetOf(m); if (sh.count <= 1 || !mesh) return; cell = Mathf.Clamp(cell, 0, sh.count - 1); int cx = cell % sh.cols, cy = cell / sh.cols; const float pad = 0.01f; var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            for (int i = 0; i < uv.Count; i++) { var u = uv[i]; u.x = Mathf.Clamp(u.x, pad, 1 - pad); u.y = Mathf.Clamp(u.y, pad, 1 - pad); uv[i] = new Vector2((cx + u.x) / sh.cols, (sh.rows - 1 - cy + u.y) / sh.rows); } mesh.SetUVs(0, uv);
        }
        static void DropGhost() { if (s_Ghost) { var f = s_Ghost.GetComponent<MeshFilter>(); if (f.sharedMesh) UnityEngine.Object.DestroyImmediate(f.sharedMesh); UnityEngine.Object.DestroyImmediate(s_Ghost); } s_Ghost = null; s_GhostKey = ""; }

        public static void Set(bool on)
        {
            if (on == On) return; On = on; s_Read.Clear(); Roll();
            if (on) { SceneView.duringSceneGui += Gui; var v = SceneView.lastActiveSceneView; if (v) { v.Focus(); v.Repaint(); } } else { SceneView.duringSceneGui -= Gui; DropGhost(); }
            Changed?.Invoke(); SceneView.RepaintAll();
        }

        static Rect s_Panel;
        static void Gui(SceneView view)
        {
            var e = Event.current; if (!Material) { Set(false); return; }
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            // the settings, right there in the scene
            s_Panel = new Rect(10, view.position.height - 236, 268, 200); bool onPanel = s_Panel.Contains(e.mousePosition);
            Handles.BeginGUI(); GUI.Label(new Rect(56, 8, 700, 24), "Click to place  " + Material.name + "     Shift + wheel: size     Ctrl + wheel: turn     [ ]: size     , .: turn     Esc: stop", new GUIStyle(EditorStyles.helpBox) { fontSize = 12 }); Panel(); Handles.EndGUI();
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Set(false); e.Use(); return; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket)) { Size = Mathf.Clamp(Size * (e.keyCode == KeyCode.RightBracket ? 1.1f : 0.9f), 0.02f, 50f); e.Use(); Changed?.Invoke(); view.Repaint(); }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Comma || e.keyCode == KeyCode.Period)) { Turn += e.keyCode == KeyCode.Period ? 15 : -15; e.Use(); Changed?.Invoke(); view.Repaint(); }
            if (e.type == EventType.ScrollWheel && (e.shift || e.control) && !onPanel) { float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x; if (e.shift) Size = Mathf.Clamp(Size * (d > 0 ? 0.92f : 1.08f), 0.02f, 50f); else Turn += d > 0 ? 8 : -8; e.Use(); Changed?.Invoke(); view.Repaint(); return; }
            if (e.type == EventType.MouseMove) view.Repaint(); if (onPanel) { if (s_Ghost && e.type == EventType.Layout) s_Ghost.SetActive(false); return; }
            bool hit = Hit(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var at, out var normal, e.type);
            if (!hit) { if (e.type == EventType.Layout && s_Ghost) s_Ghost.SetActive(false); return; }
            // what a click would put there, for real: rebuilt as soon as anything changes, not only when the mouse moves
            if (e.type == EventType.MouseMove || e.type == EventType.Layout || e.type == EventType.Repaint) { string key = at.ToString("0.000") + normal.ToString("0.00") + Size + Turn + s_NextTurn + s_NextSize + Aspect + Depth + Wrap + s_NextCell + Material.name + Material.GetHashCode(); if (key != s_GhostKey) { s_GhostKey = key; if (e.type == EventType.Repaint) EditorApplication.delayCall += () => { if (On && s_GhostKey == key) { Ghost(at, normal); view.Repaint(); } }; else Ghost(at, normal); } }
            if (e.type == EventType.Repaint)
            {
                var rot = Facing(normal, Turn + s_NextTurn); float w = Size * s_NextSize, h = w * Aspect; Vector3 r = rot * Vector3.right * w / 2, u = rot * Vector3.up * h / 2, lift = normal * w * 0.01f; var pts = new[] { at - r - u + lift, at + r - u + lift, at + r + u + lift, at - r + u + lift, at - r - u + lift };
                Handles.color = new Color(0, 0, 0, 0.5f); Handles.DrawAAPolyLine(5f, pts); Handles.color = new Color(1f, 0.62f, 0.2f, 1f); Handles.DrawAAPolyLine(2.5f, pts); Handles.DrawAAPolyLine(2.5f, at + u + lift, at + u * 1.35f + lift); Handles.ConeHandleCap(0, at + u * 1.35f + lift, Quaternion.LookRotation(u, normal), HandleUtility.GetHandleSize(at) * 0.08f, EventType.Repaint);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) { if (s_Ghost) s_Ghost.SetActive(false); Place(at, normal, Size * s_NextSize, Turn + s_NextTurn); Roll(); s_GhostKey = ""; e.Use(); }
        }

        static void Panel()
        {
            var r = s_Panel; EditorGUI.DrawRect(r, new Color(0.13f, 0.135f, 0.15f, 0.94f)); EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2), new Color(1f, 0.62f, 0.2f)); GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 10)); float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 84; EditorGUI.BeginChangeCheck();
            GUILayout.Label("PLACING " + Material.name.ToUpper(), new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, 0.62f, 0.2f) } });
            Size = EditorGUILayout.Slider("Size", Size, 0.05f, 10f); Turn = EditorGUILayout.Slider("Turn", Mathf.Repeat(Turn + 180, 360) - 180, -180, 180); Aspect = EditorGUILayout.Slider(new GUIContent("Stretch", "Taller or wider"), Aspect, 0.2f, 5f); Depth = EditorGUILayout.Slider(new GUIContent("Reach", "How far over bumps and into gaps it goes"), Depth, 0.05f, 1.5f);
            RandomSize = EditorGUILayout.Slider("Random size", RandomSize, 0, 1); RandomTurn = EditorGUILayout.Slider("Random turn", RandomTurn, 0, 1); Wrap = EditorGUILayout.ToggleLeft(new GUIContent("Bend round corners", "Goes round the corner of a box or a wall instead of stopping"), Wrap);
            GUILayout.BeginHorizontal(); if (GUILayout.Button("New random")) { Roll(); s_GhostKey = ""; } if (GUILayout.Button("Stop (Esc)")) { Set(false); } GUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck()) { s_GhostKey = ""; Changed?.Invoke(); } EditorGUIUtility.labelWidth = lw; GUILayout.EndArea();
        }

        static void Ghost(Vector3 at, Vector3 normal)
        {
            if (!s_Ghost) { s_Ghost = new GameObject("Decal ghost") { hideFlags = HideFlags.HideAndDontSave }; s_Ghost.AddComponent<MeshFilter>(); var r = s_Ghost.AddComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; s_Ghost.AddComponent<DecalMark>(); }
            var f = s_Ghost.GetComponent<MeshFilter>(); if (f.sharedMesh) UnityEngine.Object.DestroyImmediate(f.sharedMesh); var rot = Facing(normal, Turn + s_NextTurn); float size = Size * s_NextSize; f.sharedMesh = Build(at, rot, size, size * Aspect, Mathf.Max(0.02f, size * Depth)); ToCell(f.sharedMesh, Material, s_NextCell); s_Ghost.GetComponent<MeshRenderer>().sharedMaterial = Material; s_Ghost.transform.SetPositionAndRotation(at, rot); s_Ghost.SetActive(f.sharedMesh);
        }

        // (looking for the surface is only worth it when something can come of it)
        static Vector2 s_LastMouse; static bool s_LastHit; static Vector3 s_LastAt, s_LastNormal; static Vector3 s_LastCam;
        static bool Hit(Ray ray, out Vector3 at, out Vector3 normal, EventType type)
        {
            var m = Event.current.mousePosition; var cam = ray.origin; if (type != EventType.Layout && (m != s_LastMouse || cam != s_LastCam)) { s_LastMouse = m; s_LastCam = cam; s_LastHit = Hit(ray, out s_LastAt, out s_LastNormal); }
            at = s_LastAt; normal = s_LastNormal; return s_LastHit;
        }

        public static void Fresh() { s_Read.Clear(); s_LastMouse = new Vector2(-1, -1); }
        public static bool Under(Ray ray, EventType type, out Vector3 at, out Vector3 normal) => Hit(ray, out at, out normal, type);
        public struct Piece { public Transform owner; public List<Vector3> v, n; public List<Vector2> uv; public List<int> t; }
        /// <summary>What's under a round brush, for every object there, in that object's own space. uv is -1..1 across the brush.</summary>
        public static List<Piece> Pieces(Vector3 at, Vector3 normal, float size, float depth, Bounds box)
        {
            var l = new List<Piece>(); var rot = Facing(normal, 0); foreach (var s in Surfaces(b => b.Intersects(box), box).ToList())
            {
                var mesh = Build(at, rot, size, size, depth, new[] { s }); if (!mesh) continue; var owner = s.owner; if (s.skin) { float near = float.MaxValue; foreach (var bone in s.skin.bones) if (bone && (bone.position - at).sqrMagnitude < near) { near = (bone.position - at).sqrMagnitude; owner = bone; } } var m = owner.worldToLocalMatrix * Matrix4x4.TRS(at, rot, Vector3.one); var nm = m.inverse.transpose; var t = mesh.triangles; if (m.determinant < 0) Array.Reverse(t);
                l.Add(new Piece { owner = owner, v = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToList(), n = mesh.normals.Select(v => nm.MultiplyVector(v).normalized).ToList(), uv = mesh.uv.Select(u => (u - Vector2.one * 0.5f) * 2f).ToList(), t = t.ToList() }); UnityEngine.Object.DestroyImmediate(mesh);
            }
            return l;
        }

        struct Surface { public Vector3[] v, n; public int[] t; public Matrix4x4 m; public Bounds b; public Transform owner; public SkinnedMeshRenderer skin; }

        static readonly Dictionary<Mesh, (Vector3[] v, Vector3[] n, int[] t)> s_Read = new Dictionary<Mesh, (Vector3[], Vector3[], int[])>();
        static bool Known(Mesh m, out Vector3[] v, out Vector3[] n, out int[] t) { if (s_Read.TryGetValue(m, out var k)) { v = k.v; n = k.n; t = k.t; return t != null; } bool ok = Read(m, out v, out n, out t); if (s_Read.Count > 300) s_Read.Clear(); s_Read[m] = (v, n, ok ? t : null); return ok; }

        static IEnumerable<Surface> Surfaces(Func<Bounds, bool> wanted, Bounds? box = null)
        {
            foreach (var r in TsFind.All<MeshRenderer>())
            {
                if (!r.enabled || r.GetComponent<DecalMark>() || (r.hideFlags & HideFlags.HideInHierarchy) != 0 || !wanted(r.bounds)) continue; var f = r.GetComponent<MeshFilter>(); if (!f || !f.sharedMesh) continue;
                if (!Known(f.sharedMesh, out var v, out var n, out var t)) continue;
                yield return new Surface { v = v, n = n, t = t, m = f.transform.localToWorldMatrix, b = r.bounds, owner = f.transform };
            }
            foreach (var r in TsFind.All<SkinnedMeshRenderer>())
            {
                if (!r.enabled || !r.sharedMesh || !wanted(r.bounds)) continue; var baked = new Mesh(); r.BakeMesh(baked, true); bool ok = Read(baked, out var v, out var n, out var t); UnityEngine.Object.DestroyImmediate(baked); if (ok) yield return new Surface { v = v, n = n, t = t, m = r.transform.localToWorldMatrix, b = r.bounds, owner = r.transform, skin = r };
            }
#if HIENADEV_TERRAIN
            if (box != null) foreach (var tr in Terrain.activeTerrains)
                {
                    var b = box.Value; if (!tr.terrainData || !wanted(new Bounds(tr.transform.position + tr.terrainData.bounds.center, tr.terrainData.bounds.size))) continue; const int N = 24; var v = new Vector3[(N + 1) * (N + 1)]; var n = new Vector3[v.Length]; var t = new List<int>(); var size = tr.terrainData.size;
                    for (int y = 0; y <= N; y++) for (int x = 0; x <= N; x++) { var w = new Vector3(Mathf.Lerp(b.min.x, b.max.x, x / (float)N), 0, Mathf.Lerp(b.min.z, b.max.z, y / (float)N)); w.y = tr.SampleHeight(w) + tr.transform.position.y; v[y * (N + 1) + x] = w; n[y * (N + 1) + x] = tr.terrainData.GetInterpolatedNormal((w.x - tr.transform.position.x) / size.x, (w.z - tr.transform.position.z) / size.z); if (x < N && y < N) { int i = y * (N + 1) + x; t.AddRange(new[] { i, i + N + 1, i + 1, i + 1, i + N + 1, i + N + 2 }); } }
                    yield return new Surface { v = v, n = n, t = t.ToArray(), m = Matrix4x4.identity, b = b, owner = tr.transform };
                }
#endif
        }

        // (also for meshes that aren't readable: the editor may look at those this way)
        static bool Read(Mesh mesh, out Vector3[] v, out Vector3[] n, out int[] t)
        {
            v = n = null; t = null;
            try
            {
                using (var arr = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var d = arr[0]; using (var nv = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { d.GetVertices(nv); v = nv.ToArray(); }
                    using (var nn = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { if (d.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) d.GetNormals(nn); n = nn.ToArray(); }
                    var l = new List<int>(); for (int s = 0; s < d.subMeshCount; s++) { var sm = d.GetSubMesh(s); if (sm.topology != MeshTopology.Triangles) continue; using (var ni = new NativeArray<int>(sm.indexCount, Allocator.Temp)) { d.GetIndices(ni, s); l.AddRange(ni.ToArray()); } }
                    t = l.ToArray(); return t.Length > 0;
                }
            }
            catch { return false; }
        }

        /// <summary>The nearest surface along a ray (any visible mesh: no collider needed).</summary>
        public static bool Hit(Ray ray, out Vector3 at, out Vector3 normal)
        {
            at = normal = Vector3.zero; float best = float.MaxValue;
            foreach (var s in Surfaces(b => b.IntersectRay(ray)))
            {
                var inv = s.m.inverse; var o = inv.MultiplyPoint3x4(ray.origin); var d = inv.MultiplyVector(ray.direction);
                for (int i = 0; i < s.t.Length; i += 3)
                {
                    Vector3 a = s.v[s.t[i]], e1 = s.v[s.t[i + 1]] - a, e2 = s.v[s.t[i + 2]] - a, p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < 1e-12f) continue;
                    float f = 1 / det; var q0 = o - a; float u = f * Vector3.Dot(q0, p); if (u < 0 || u > 1) continue; var q = Vector3.Cross(q0, e1); float v = f * Vector3.Dot(d, q); if (v < 0 || u + v > 1) continue; float k = f * Vector3.Dot(e2, q); if (k <= 0 || k >= best) continue;
                    best = k; at = ray.origin + ray.direction * k; normal = s.m.inverse.transpose.MultiplyVector(Vector3.Cross(e1, e2)).normalized; if (Vector3.Dot(normal, ray.direction) > 0) normal = -normal;
                }
            }
#if HIENADEV_TERRAIN
            foreach (var tr in Terrain.activeTerrains) { var col = tr.GetComponent<TerrainCollider>(); if (col && col.Raycast(ray, out var th, best < float.MaxValue ? best : 10000f) && th.distance < best) { best = th.distance; at = th.point; normal = th.normal; } }
#endif
            return best < float.MaxValue;
        }

        /// <summary>Makes the decal there (can be undone). Each thing it lands on gets its own piece, as a child of it, so
        /// the decal moves, turns and scales with what it's stuck on.</summary>
        public static GameObject Place(Vector3 at, Vector3 normal, float size, float turn, string group = null)
        {
            group = group ?? Guid.NewGuid().ToString("N"); var rot = Facing(normal, turn); int cell = ForceCell >= 0 ? ForceCell : s_NextCell; float depth = Mathf.Max(0.02f, size * Depth); var box = new Bounds(at, Vector3.one * new Vector3(size, size * Aspect, depth * 2).magnitude); GameObject first = null; int undoGroup = Undo.GetCurrentGroup();
            foreach (var s in Surfaces(b => b.Intersects(box), box).ToList())
            {
                var mesh = Build(at, rot, size, size * Aspect, depth, new[] { s }); if (!mesh) continue; ToCell(mesh, Material, cell); var owner = s.owner;
                if (s.skin) { float near = float.MaxValue; foreach (var bone in s.skin.bones) if (bone && (bone.position - at).sqrMagnitude < near) { near = (bone.position - at).sqrMagnitude; owner = bone; } } // (a character: the nearest bone carries it)
                // the mesh is kept in its parent's own space: nothing to line up, whatever the parent's scale is
                var m = owner.worldToLocalMatrix * Matrix4x4.TRS(at, rot, Vector3.one); var nm = m.inverse.transpose; mesh.vertices = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray(); mesh.normals = mesh.normals.Select(v => nm.MultiplyVector(v).normalized).ToArray(); if (m.determinant < 0) mesh.triangles = mesh.triangles.Reverse().ToArray(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                var go = new GameObject("Decal " + Material.name); go.transform.SetParent(owner, false); Undo.RegisterCreatedObjectUndo(go, "Place decal"); go.layer = owner.gameObject.layer; go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; var dm = go.AddComponent<DecalMark>(); dm.group = group; dm.at = owner.InverseTransformPoint(at); dm.normal = owner.InverseTransformDirection(normal); dm.up = owner.InverseTransformDirection(rot * Vector3.up); dm.size = size; dm.turn = turn; dm.aspect = Aspect; dm.depth = Depth; dm.wrap = Wrap; dm.cell = cell; if (!first) first = go;
            }
            Undo.CollapseUndoOperations(undoGroup); return first;
        }

        /// <summary>The same on one mesh only (for the preview).</summary>
        public static Mesh On_(Mesh mesh, Matrix4x4 m, Vector3 at, Vector3 normal, float size, float aspect, float turn, float depth, out Matrix4x4 place)
        {
            var rot = Facing(normal, turn); place = Matrix4x4.TRS(at, rot, Vector3.one); if (!Known(mesh, out var v, out var n, out var t)) return null; return Build(at, rot, size, size * aspect, Mathf.Max(0.02f, size * depth), new[] { new Surface { v = v, n = n, t = t, m = m } });
        }
        public static Quaternion Facing(Vector3 normal, float turn) => Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.95f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0, 0, turn);

        public static float LiftScale = 1f; public static bool Wrap;
        /// <summary>Where a point lands on the picture when the surface is folded out flat around the line it shares with the
        /// surface under the middle (so a decal goes round a corner instead of smearing down its side).</summary>
        static Vector2 Unfold(Vector3 p, Vector3 face)
        {
            var n = face; var main = Vector3.back; // (both point out of the surface, towards the viewer) float c = Vector3.Dot(n, main); if (c > 0.985f) return new Vector2(p.x, p.y);
            var axis = Vector3.Cross(n, main); if (axis.sqrMagnitude < 1e-8f) return new Vector2(p.x, p.y); axis.Normalize();
            // a point on the line where this surface's plane meets the middle one (z = 0)
            float dn = Vector3.Dot(n, p); var dir = Vector3.Cross(axis, Vector3.forward); float k = Vector3.Dot(n, dir); var hinge = Mathf.Abs(k) > 1e-5f ? dir * (dn / k) : Vector3.zero;
            var r = Quaternion.FromToRotation(n, main) * (p - hinge) + hinge; return new Vector2(r.x, r.y);
        }

        static Mesh Build(Vector3 at, Quaternion rot, float w, float h, float depth, IEnumerable<Surface> only = null)
        {
            var toLocal = Matrix4x4.TRS(at, rot, Vector3.one).inverse; var ext = new Vector3(w / 2, h / 2, depth); var box = new Bounds(at, Vector3.one * (new Vector3(w, h, depth * 2).magnitude)); var vs = new List<Vector3>(); var ns = new List<Vector3>(); var uv = new List<Vector2>(); var ts = new List<int>(); float lift = Mathf.Max(0.0015f, w * 0.0025f) * LiftScale;
            var poly = new List<(Vector3 p, Vector3 n)>(); var next = new List<(Vector3 p, Vector3 n)>();
            foreach (var s in only ?? Surfaces(b => b.Intersects(box), box))
            {
                var m = toLocal * s.m; var nm = m.inverse.transpose; bool flip = s.m.determinant < 0;
                for (int i = 0; i < s.t.Length; i += 3)
                {
                    int ia = s.t[i], ib = s.t[i + 1], ic = s.t[i + 2]; if (flip) { int x = ib; ib = ic; ic = x; } Vector3 a = m.MultiplyPoint3x4(s.v[ia]), b = m.MultiplyPoint3x4(s.v[ib]), c = m.MultiplyPoint3x4(s.v[ic]);
                    if (Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > ext.x || Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < -ext.x || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > ext.y || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < -ext.y || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > ext.z || Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < -ext.z) continue;
                    var face = Vector3.Cross(b - a, c - a).normalized; if (face.z > (Wrap ? 0.25f : -0.15f)) continue; // (only what faces it: the picture looks along +z)
                    Vector3 N(int k) { var q = nm.MultiplyVector(s.n[k]); return q.sqrMagnitude > 1e-8f ? q.normalized : face; }
                    poly.Clear(); poly.Add((a, N(ia))); poly.Add((b, N(ib))); poly.Add((c, N(ic)));
                    for (int side = 0; side < 6 && poly.Count > 0; side++)
                    {
                        int ax = side / 2; float sign = side % 2 == 0 ? 1 : -1; next.Clear(); float D(Vector3 p) { if (Wrap && ax < 2) { var f = Unfold(p, face); return ext[ax] - (ax == 0 ? f.x : f.y) * sign; } return (Wrap && ax == 2 ? Mathf.Max(ext.x, ext.y) : ext[ax]) - p[ax] * sign; }
                        for (int k = 0; k < poly.Count; k++)
                        {
                            var p = poly[k]; var q = poly[(k + 1) % poly.Count]; float dp = D(p.p), dq = D(q.p); if (dp >= 0) next.Add(p);
                            if ((dp >= 0) != (dq >= 0)) { float f = dp / (dp - dq); next.Add((Vector3.Lerp(p.p, q.p, f), Vector3.Lerp(p.n, q.n, f).normalized)); }
                        }
                        var sw = poly; poly = next; next = sw;
                    }
                    if (poly.Count < 3) continue; int first = vs.Count;
                    foreach (var p in poly) { vs.Add(p.p); ns.Add(p.n); uv.Add(Wrap ? Unfold(p.p, face) : new Vector2(p.p.x / w + 0.5f, p.p.y / h + 0.5f)); }
                    for (int k = 2; k < poly.Count; k++) { ts.Add(first); ts.Add(first + k - 1); ts.Add(first + k); }
                }
            }
            if (ts.Count == 0) return null;
            // where two sides meet (a corner) both are lifted the same way, so no gap opens between them
            var dirs = new Dictionary<Vector3Int, Vector3>(); float qz = 1f / Mathf.Max(1e-5f, lift * 0.25f); Vector3Int K(Vector3 v) => new Vector3Int(Mathf.RoundToInt(v.x * qz), Mathf.RoundToInt(v.y * qz), Mathf.RoundToInt(v.z * qz));
            for (int i = 0; i < vs.Count; i++) { var k = K(vs[i]); dirs.TryGetValue(k, out var d); dirs[k] = d + ns[i]; }
            for (int i = 0; i < vs.Count; i++) { var d = dirs[K(vs[i])].normalized; if (d == Vector3.zero) d = ns[i]; vs[i] += d * (lift / Mathf.Max(0.4f, Vector3.Dot(d, ns[i]))); }
            if (Wrap) { for (int i = 0; i < uv.Count; i++) uv[i] = new Vector2(uv[i].x / w + 0.5f, uv[i].y / h + 0.5f); }
            var mesh = new Mesh { name = "Decal" }; if (vs.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; mesh.SetVertices(vs); mesh.SetNormals(ns); mesh.SetUVs(0, uv); mesh.SetTriangles(ts, 0); mesh.RecalculateTangents(); mesh.RecalculateBounds(); return mesh;
        }
    }

    /// <summary>The Decals page: make one (your own picture or one that's drawn by itself), then click it onto things in the scene.</summary>
    public partial class TextureStudioWindow
    {
        [SerializeField] StickerArt m_DecalArt = new StickerArt(); [SerializeField] int m_DecalSeed = 3, m_DecalSize = 512; [SerializeField] SeeThrough m_DecalSee = SeeThrough.CutOut; [SerializeField] bool m_DecalLit = true; [SerializeField] Material m_DecalLike; [SerializeField] string m_DecalName = ""; [SerializeField] Material m_DecalMat; [SerializeField] float m_DecalShine = 0.2f; [SerializeField] int m_DecalCount = 1, m_DecalCols = 1, m_DecalRows = 1;
        string m_DecalWarn = ""; Vector2 m_DecalScroll; [SerializeField] int m_DecalView; [SerializeField] TextureModelView.Shape m_DecalShape = TextureModelView.Shape.Wall; TextureModelView m_DecalModel; Material m_DecalTemp; Texture2D m_DecalTempTex; string m_DecalTempKey = ""; List<Material> m_DecalList; Vector2 m_DecalListScroll;

        void DecalsPage(Rect body)
        {
            const float gap = 6, listH = 126; var left = new Rect(body.x + gap, body.y + gap, 360, body.height - gap * 2); var right = new Rect(left.xMax + gap, left.y, body.xMax - left.xMax - gap * 2, left.height - listH - gap); var list = new Rect(right.x, right.yMax + gap, right.width, listH); EditorGUI.DrawRect(left, Panel);
            GUILayout.BeginArea(new Rect(left.x + 12, left.y + 10, left.width - 24, left.height - 16)); m_DecalScroll = GUILayout.BeginScrollView(m_DecalScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); EditorGUIUtility.labelWidth = 110;
            GUILayout.Label("Make a decal", m_Title); GUILayout.Label("Something you stick onto walls and floors in the scene: blood, posters, bullet holes. The picture on the right is what you get.", m_Hint); GUILayout.Space(6);
            GUILayout.Label("1. THE PICTURE", m_Head); m_DecalArt.Settings(left.width - 40);
            if (m_DecalArt.kind != StickerArt.Kind.Mine && GUILayout.Button("Draw another one")) m_DecalSeed = UnityEngine.Random.Range(1, 999999);
            if (m_DecalArt.kind != StickerArt.Kind.Mine) { GUILayout.BeginHorizontal(); GUILayout.Label(new GUIContent("How many", "A sheet: several different ones in one picture, and each click puts down a random one"), GUILayout.Width(106)); foreach (int n in new[] { 1, 4, 6, 9 }) if (Pill(n == 1 ? "Just one" : n + " in a sheet", m_DecalCount == n)) m_DecalCount = n; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
            else { GUILayout.BeginHorizontal(); GUILayout.Label(new GUIContent("Your picture holds", "If your picture is already a sheet of several decals in a grid, say how many across and down: each click then picks one"), GUILayout.Width(118)); m_DecalCols = Mathf.Clamp(EditorGUILayout.IntField(m_DecalCols, GUILayout.Width(30)), 1, 8); GUILayout.Label("across", GUILayout.Width(42)); m_DecalRows = Mathf.Clamp(EditorGUILayout.IntField(m_DecalRows, GUILayout.Width(30)), 1, 8); GUILayout.Label("down", GUILayout.Width(36)); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
            GUILayout.Space(8); GUILayout.Label("2. HOW IT LOOKS", m_Head);
            GUILayout.BeginHorizontal(); if (Pill("Cut out", m_DecalSee == SeeThrough.CutOut, "Hard edges. Works with every fog and shadow, and is the cheapest")) m_DecalSee = SeeThrough.CutOut; if (Pill("Smooth", m_DecalSee == SeeThrough.Smooth, "Soft edges, half see-through parts. Fog made from depth (like your PSX fog) can't see these unless the shader is made for it: use Shader from")) m_DecalSee = SeeThrough.Smooth; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            m_DecalLike = (Material)EditorGUILayout.ObjectField(new GUIContent("Shader from", "Leave it empty for a normal URP material. Or drop one of your materials: the decal then uses that material's shader and settings (how it's lit, how fog works on it), with this picture on it. Your material isn't changed"), m_DecalLike, typeof(Material), false); if (m_DecalLike) GUILayout.Label(m_DecalWarn.Length > 0 ? m_DecalWarn : "Uses " + m_DecalLike.shader.name + ", with this picture.", m_DecalWarn.Length > 0 ? new GUIStyle(m_Hint) { normal = { textColor = new Color(1f, 0.6f, 0.4f) } } : m_Hint);
            if (!m_DecalLike) { m_DecalLit = EditorGUILayout.ToggleLeft("Lit by lights", m_DecalLit); if (m_DecalLit) m_DecalShine = EditorGUILayout.Slider(new GUIContent("Wet", "How shiny it is: turn the preview to see the light on it"), m_DecalShine, 0f, 1f); }
            m_DecalSize = EditorGUILayout.IntPopup(new GUIContent("Pixels"), m_DecalSize, new[] { new GUIContent("128"), new GUIContent("256"), new GUIContent("512"), new GUIContent("1024") }, new[] { 128, 256, 512, 1024 });
            GUILayout.Space(8); GUILayout.Label("3. SAVE IT", m_Head); if (m_DecalName.Length == 0) m_DecalName = m_DecalArt.kind == StickerArt.Kind.Mine ? (m_DecalArt.picture ? m_DecalArt.picture.name + " decal" : "My decal") : m_DecalArt.Name + " decal"; m_DecalName = EditorGUILayout.TextField("Name", m_DecalName);
            bool none = m_DecalArt.kind == StickerArt.Kind.Mine && !m_DecalArt.picture; var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            using (new EditorGUI.DisabledScope(none)) { if (GUILayout.Button(new GUIContent("Save and place it", "Saves the picture and its material in Assets/Decals, then you click where it goes in the Scene view"), GUILayout.Height(30))) EditorApplication.delayCall += () => AskDecal(true); GUI.backgroundColor = old; if (GUILayout.Button(new GUIContent("Only save", "Saves the picture and its material in Assets/Decals"))) EditorApplication.delayCall += () => AskDecal(false); }
            GUI.backgroundColor = old; GUILayout.Space(8); GUILayout.Label("4. PLACING" + (m_DecalMat ? ": " + m_DecalMat.name.ToUpper() : ""), m_Head);
            if (!m_DecalMat) GUILayout.Label("Save one, or click one of your decals at the bottom right.", m_Hint);
            DecalPlacer.Material = m_DecalMat; using (new EditorGUI.DisabledScope(!m_DecalMat))
            {
                DecalPlacer.Size = EditorGUILayout.Slider(new GUIContent("Size", "Units. Shift + wheel in the scene"), DecalPlacer.Size, 0.05f, 10f); DecalPlacer.RandomSize = EditorGUILayout.Slider("Random size", DecalPlacer.RandomSize, 0f, 1f); DecalPlacer.Turn = EditorGUILayout.Slider(new GUIContent("Turn", "Ctrl + wheel in the scene"), Mathf.Repeat(DecalPlacer.Turn + 180, 360) - 180, -180f, 180f); DecalPlacer.RandomTurn = EditorGUILayout.Slider(new GUIContent("Random turn", "0 for posters and signs"), DecalPlacer.RandomTurn, 0f, 1f); DecalPlacer.Depth = EditorGUILayout.Slider(new GUIContent("Reach", "How far over bumps and into gaps it reaches"), DecalPlacer.Depth, 0.05f, 1.5f); DecalPlacer.Aspect = EditorGUILayout.Slider(new GUIContent("Stretch", "Taller or wider"), DecalPlacer.Aspect, 0.2f, 5f); DecalPlacer.Wrap = EditorGUILayout.ToggleLeft(new GUIContent("Bend round corners", "Goes round the corner of a box or a wall instead of stopping"), DecalPlacer.Wrap); if (GUI.changed) SceneView.RepaintAll();
                GUI.backgroundColor = DecalPlacer.On ? Accent : old; if (GUILayout.Button(DecalPlacer.On ? "Stop placing (Esc)" : "Place it: click in the Scene view", GUILayout.Height(28))) { if (!DecalPlacer.On && !SceneView.lastActiveSceneView) ShowNotification(new GUIContent("Open the Scene view first (Window > General > Scene)")); else DecalPlacer.Set(!DecalPlacer.On); } GUI.backgroundColor = old;
            }
            GUILayout.Label("In the Scene view the decal follows the mouse over your models, and these settings are there too. Each click leaves one there as a child of what it's on, so it moves with it.\n\nTo change one later: stop placing, click it in the Scene view, then drag its dot to slide it, its ring to turn it and its square to size it.", m_Hint);
            EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView(); GUILayout.EndArea();

            // ---- what you get
            EditorGUI.DrawRect(right, new Color(0.07f, 0.075f, 0.09f)); EditorGUI.DrawRect(new Rect(right.x, right.y, right.width, 34), Panel); GUILayout.BeginArea(new Rect(right.x + 8, right.y + 6, right.width - 16, 26)); GUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++) { var sh = new[] { TextureModelView.Shape.Wall, TextureModelView.Shape.Ball, TextureModelView.Shape.Cube, TextureModelView.Shape.Floor }[i]; if (Pill(new[] { "On a wall", "On a ball", "On a box", "On the floor" }[i], m_DecalView == 0 && m_DecalShape == sh)) { m_DecalView = 0; m_DecalShape = sh; m_DecalModel?.Face(sh); } } GUILayout.Space(8); if (Pill("Just the picture", m_DecalView == 1)) m_DecalView = 1; GUILayout.FlexibleSpace();
            if (DecalPlacer.On) GUILayout.Label("PLACING: click in the Scene view", new GUIStyle(m_Head) { normal = { textColor = Accent } }); GUILayout.EndHorizontal(); GUILayout.EndArea();
            var area = new Rect(right.x + 6, right.y + 40, right.width - 12, right.height - 46); var tex = m_DecalArt.Picture(m_DecalSeed, 384);
            if (!tex) GUI.Label(area, "Drop a picture on the \"Yours\" square.", m_Empty);
            else if (m_DecalView == 1) { var pic = Fit(area, tex.width / (float)tex.height, 20); GUI.DrawTextureWithTexCoords(pic, TextureAlpha.Checks, new Rect(0, 0, pic.width / 32, pic.height / 32)); GUI.DrawTexture(pic, tex, ScaleMode.StretchToFill, true); }
            else
            {
                if (m_DecalModel == null) { m_DecalModel = new TextureModelView(); m_DecalModel.Face(m_DecalShape); } if (m_DecalModel.Mouse(area)) Repaint(); DecalTemp();
                if (Event.current.type == EventType.Repaint && area.width > 20 && area.height > 20) { var im = m_DecalModel.RenderDecal(area, m_DecalShape, m_DecalTemp, 1.3f, m_DecalTempTex ? m_DecalTempTex.height / (float)m_DecalTempTex.width : 1, 0, 0.5f); if (im) GUI.DrawTexture(area, im, ScaleMode.StretchToFill, false); }
                GUI.Label(new Rect(area.x + 8, area.yMax - 20, area.width, 18), "Drag to turn, wheel to come closer, Shift-drag for the light", new GUIStyle(m_Hint) { normal = { textColor = new Color(1, 1, 1, 0.45f) } });
            }

            // ---- the ones you've made
            EditorGUI.DrawRect(list, Panel); if (m_DecalList == null) m_DecalList = AssetDatabase.IsValidFolder("Assets/Decals") ? AssetDatabase.FindAssets("t:Material", new[] { "Assets/Decals" }).Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).Where(m => m).ToList() : new List<Material>(); m_DecalList.RemoveAll(m => !m);
            GUI.Label(new Rect(list.x + 10, list.y + 5, list.width, 16), "YOUR DECALS (" + m_DecalList.Count + ")" + (m_DecalList.Count > 0 ? ": CLICK ONE TO PLACE IT" : ""), m_Head); if (m_DecalList.Count == 0) { GUI.Label(new Rect(list.x + 10, list.y + 30, list.width - 20, 30), "The ones you save show up here.", m_Hint); return; }
            var inner = new Rect(0, 0, Mathf.Max(list.width - 20, m_DecalList.Count * 92), 84); m_DecalListScroll = GUI.BeginScrollView(new Rect(list.x + 8, list.y + 24, list.width - 14, listH - 28), m_DecalListScroll, inner, false, false);
            for (int i = 0; i < m_DecalList.Count; i++) { var m = m_DecalList[i]; var c = new Rect(i * 92, 0, 86, 82); bool on = m == m_DecalMat; EditorGUI.DrawRect(c, on ? new Color(Accent.r, Accent.g, Accent.b, 0.45f) : Ink(c.Contains(Event.current.mousePosition) ? 0.12f : 0.05f)); var mt = TexOf(m); if (!mt) mt = AssetPreview.GetMiniThumbnail(m); var pr = new Rect(c.x + 14, c.y + 3, 58, 58); GUI.DrawTextureWithTexCoords(pr, TextureAlpha.Checks, new Rect(0, 0, 2, 2)); if (mt) GUI.DrawTexture(pr, mt, ScaleMode.ScaleToFit, true); GUI.Label(new Rect(c.x + 2, c.yMax - 19, c.width - 4, 16), m.name, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip }); if (GUI.Button(c, new GUIContent("", m.name), GUIStyle.none)) { m_DecalMat = m; DecalPlacer.Material = m; var ta = TexOf(m); DecalPlacer.Aspect = ta ? ta.height / (float)ta.width : 1; if (SceneView.lastActiveSceneView) DecalPlacer.Set(true); } }
            GUI.EndScrollView();
        }

        /// <summary>The material as it'll be saved, for the preview.</summary>
        void DecalTemp()
        {
            m_DecalArt.Prepare(); string key = JsonUtility.ToJson(m_DecalArt) + m_DecalSeed + m_DecalSee + m_DecalLit + m_DecalShine.ToString("0.00") + (m_DecalLike ? m_DecalLike.name + m_DecalLike.shader.name : ""); if (key == m_DecalTempKey && m_DecalTemp) return; m_DecalTempKey = key; var art = m_DecalArt.Get(m_DecalSeed, 256); if (art == null) return; var p = art.Copy(); if (m_DecalArt.kind != StickerArt.Kind.Mine) for (int i = 0; i < p.c.Length; i++) p.c[i] = StickerArt.Tinted(p.c[i], m_DecalArt.colour);
            Put(ref m_DecalTempTex, TextureAlpha.Bleed(p)); m_DecalTempTex.wrapMode = TextureWrapMode.Clamp; if (m_DecalTemp) DestroyImmediate(m_DecalTemp); m_DecalTemp = new Material(m_DecalLike ? m_DecalLike.shader : DecalShader()) { hideFlags = HideFlags.HideAndDontSave }; DecalFill(m_DecalTemp, m_DecalTempTex);
        }
        Shader DecalShader() { var sh = Shader.Find(m_DecalLit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit"); return sh ? sh : Shader.Find(m_DecalLit ? "Standard" : m_DecalSee == SeeThrough.Smooth ? "Unlit/Transparent" : "Unlit/Transparent Cutout"); }
        void DecalFill(Material mat, Texture2D tex)
        {
            m_DecalWarn = "";
            if (m_DecalLike)
            {
                mat.shader = m_DecalLike.shader; mat.CopyPropertiesFromMaterial(m_DecalLike); var sl = TextureSlots.Guess(mat.shader); TextureMaterials.Fill(mat, tex, null, null, null, sl); if (sl.colour != "") { mat.SetTextureScale(sl.colour, Vector2.one); mat.SetTextureOffset(sl.colour, Vector2.zero); }
                // (its maps belong to another picture, and its colour would tint this one)
                foreach (var o in new[] { sl.normal, sl.mask, sl.occlusion, sl.height, sl.glow }) if (o != "") mat.SetTexture(o, null); foreach (var c in new[] { "_BaseColor", "_Color" }) if (mat.HasProperty(c)) mat.SetColor(c, Color.white);
                m_DecalWarn = TextureAlpha.ApplyAny(mat, m_DecalSee); return;
            }
            mat.shader = DecalShader(); if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex); if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", m_DecalShine); if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", m_DecalShine); TextureAlpha.Apply(mat, m_DecalSee); if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2);
        }

        static Texture TexOf(Material m) { var s = TextureSlots.Guess(m.shader).colour; return s != "" ? m.GetTexture(s) : null; }

        void AskDecal(bool place)
        {
            var name = TextureShelf.Clean(m_DecalName); if (name.Length == 0) return; const string folder = "Assets/Decals"; bool both = false;
            if (System.IO.File.Exists(folder + "/" + name + ".png") || System.IO.File.Exists(folder + "/" + name + ".mat")) { int pick = EditorUtility.DisplayDialogComplex("Name taken", "There's already a decal called " + name + ". Replacing it changes every one already in your scenes.", "Replace it", "Cancel", "Keep both"); if (pick == 1) return; both = pick == 2; }
            if (SaveDecal(folder, name, both) && place) { if (SceneView.lastActiveSceneView) DecalPlacer.Set(true); else ShowNotification(new GUIContent("Saved. Open the Scene view to place it")); }
        }

        /// <summary>Several drawn the same way (each a little different), side by side in one picture.</summary>
        Pixels Sheet(int count, int cols, int rows)
        {
            int cell = m_DecalSize; var r = new Pixels(cell * cols, cell * rows); for (int i = 0; i < r.c.Length; i++) r.c[i] = new Color(0, 0, 0, 0);
            for (int k = 0; k < count; k++)
            {
                var a = m_DecalArt.Get(m_DecalSeed + k * 7919, cell); if (a == null) continue; float s = cell * 0.96f / Mathf.Max(a.w, a.h); var q = a.Resized(Mathf.Max(4, Mathf.RoundToInt(a.w * s)), Mathf.Max(4, Mathf.RoundToInt(a.h * s))); int ox = (k % cols) * cell + (cell - q.w) / 2, oy = (rows - 1 - k / cols) * cell + (cell - q.h) / 2;
                for (int y = 0; y < q.h; y++) for (int x = 0; x < q.w; x++) r.c[(oy + y) * r.w + ox + x] = StickerArt.Tinted(q.c[y * q.w + x], m_DecalArt.colour);
            }
            return r;
        }

        Material SaveDecal(string folder, string name, bool both)
        {
            if (both) { var b = name; for (int n = 2; System.IO.File.Exists(folder + "/" + name + ".png") || System.IO.File.Exists(folder + "/" + name + ".mat"); n++) name = b + " " + n; }
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
            m_DecalArt.Prepare(); var art = m_DecalArt.Get(m_DecalSeed, m_DecalSize); if (art == null) return null; float k = m_DecalSize / (float)Mathf.Max(art.w, art.h); var p = art.Resized(Mathf.Max(8, Mathf.RoundToInt(art.w * k)), Mathf.Max(8, Mathf.RoundToInt(art.h * k))); if (m_DecalArt.kind != StickerArt.Kind.Mine) for (int i = 0; i < p.c.Length; i++) p.c[i] = StickerArt.Tinted(p.c[i], m_DecalArt.colour);
            int cols = 1, rows = 1, count = 1; if (m_DecalArt.kind != StickerArt.Kind.Mine && m_DecalCount > 1) { count = m_DecalCount; cols = count == 4 ? 2 : 3; rows = Mathf.CeilToInt(count / (float)cols); p = Sheet(count, cols, rows); } else if (m_DecalArt.kind == StickerArt.Kind.Mine) { cols = m_DecalCols; rows = m_DecalRows; count = cols * rows; }
            p = TextureAlpha.Bleed(p); var tex = Pixels.SaveAt(p, folder + "/" + name + ".png", null, false, false); var imp = AssetImporter.GetAtPath(folder + "/" + name + ".png") as TextureImporter; if (imp) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
            var path = folder + "/" + name + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(path); bool isNew = !mat;
            if (isNew) mat = new Material(m_DecalLike ? m_DecalLike.shader : DecalShader()); DecalFill(mat, tex); DecalPlacer.Aspect = p.h / (float)p.w; m_DecalList = null;
            mat.name = name; if (isNew) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); DecalPlacer.SetSheet(mat, cols, rows, count); if (count > 1) DecalPlacer.Aspect = (p.h / (float)rows) / (p.w / (float)cols); m_DecalMat = mat; DecalPlacer.Material = mat; EditorGUIUtility.PingObject(mat); ShowNotification(new GUIContent("Saved " + name)); Repaint(); return mat;
        }
    }
}
