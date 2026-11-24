using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HienaDev.DevTools
{
    /// <summary>Fast ray tests against a set of triangles (a grid of cells walked along the ray). Works on any thread.</summary>
    public class TriRays
    {
        readonly Vector3[] A, E1, E2; readonly Bounds box; readonly int nx, ny, nz; readonly Vector3 cell; readonly List<int>[] cells;

        public TriRays(IList<Vector3> a, IList<Vector3> b, IList<Vector3> c)
        {
            int n = a.Count; A = new Vector3[n]; E1 = new Vector3[n]; E2 = new Vector3[n];
            box = new Bounds(n > 0 ? a[0] : Vector3.zero, Vector3.zero);
            for (int i = 0; i < n; i++) { A[i] = a[i]; E1[i] = b[i] - a[i]; E2[i] = c[i] - a[i]; box.Encapsulate(a[i]); box.Encapsulate(b[i]); box.Encapsulate(c[i]); }
            box.Expand(box.size.magnitude * 1e-3f + 1e-4f);
            float res = Mathf.Clamp(Mathf.Pow(n, 1f / 3f) * 1.5f, 1, 96); float big = Mathf.Max(box.size.x, box.size.y, box.size.z);
            nx = Mathf.Clamp(Mathf.CeilToInt(res * box.size.x / big), 1, 128); ny = Mathf.Clamp(Mathf.CeilToInt(res * box.size.y / big), 1, 128); nz = Mathf.Clamp(Mathf.CeilToInt(res * box.size.z / big), 1, 128);
            cell = new Vector3(box.size.x / nx, box.size.y / ny, box.size.z / nz); cells = new List<int>[nx * ny * nz];
            for (int i = 0; i < n; i++)
            {
                var tb = new Bounds(a[i], Vector3.zero); tb.Encapsulate(b[i]); tb.Encapsulate(c[i]);
                var lo = Cell(tb.min); var hi = Cell(tb.max);
                for (int x = lo.x; x <= hi.x; x++) for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++) { int k = (z * ny + y) * nx + x; (cells[k] ?? (cells[k] = new List<int>(4))).Add(i); }
            }
        }
        Vector3Int Cell(Vector3 p) { var q = p - box.min; return new Vector3Int(Mathf.Clamp((int)(q.x / cell.x), 0, nx - 1), Mathf.Clamp((int)(q.y / cell.y), 0, ny - 1), Mathf.Clamp((int)(q.z / cell.z), 0, nz - 1)); }

        /// <summary>Does the ray hit anything closer than <paramref name="max"/> (skipping triangle <paramref name="skip"/>)?</summary>
        public bool Hit(Vector3 o, Vector3 d, float max, int skip = -1)
        {
            // where the ray enters the grid
            float t0 = 0, t1 = max;
            for (int k = 0; k < 3; k++)
            {
                if (Mathf.Abs(d[k]) < 1e-12f) { if (o[k] < box.min[k] || o[k] > box.max[k]) return false; continue; }
                float a = (box.min[k] - o[k]) / d[k], b = (box.max[k] - o[k]) / d[k]; if (a > b) { var t = a; a = b; b = t; }
                t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, b); if (t0 > t1) return false;
            }
            var p = o + d * t0; var c = Cell(p);
            int sx = d.x > 0 ? 1 : -1, sy = d.y > 0 ? 1 : -1, sz = d.z > 0 ? 1 : -1;
            Vector3 next = new Vector3(box.min.x + (c.x + (sx > 0 ? 1 : 0)) * cell.x, box.min.y + (c.y + (sy > 0 ? 1 : 0)) * cell.y, box.min.z + (c.z + (sz > 0 ? 1 : 0)) * cell.z);
            float tx = Mathf.Abs(d.x) > 1e-12f ? (next.x - o.x) / d.x : float.MaxValue, ty = Mathf.Abs(d.y) > 1e-12f ? (next.y - o.y) / d.y : float.MaxValue, tz = Mathf.Abs(d.z) > 1e-12f ? (next.z - o.z) / d.z : float.MaxValue;
            float dx = Mathf.Abs(d.x) > 1e-12f ? cell.x / Mathf.Abs(d.x) : float.MaxValue, dy = Mathf.Abs(d.y) > 1e-12f ? cell.y / Mathf.Abs(d.y) : float.MaxValue, dz = Mathf.Abs(d.z) > 1e-12f ? cell.z / Mathf.Abs(d.z) : float.MaxValue;
            for (int guard = 0; guard < nx + ny + nz + 3; guard++)
            {
                var l = cells[(c.z * ny + c.y) * nx + c.x];
                if (l != null) foreach (int i in l) if (i != skip && Tri(i, o, d, max)) return true;
                if (tx < ty && tx < tz) { if (tx > t1) return false; c.x += sx; tx += dx; if (c.x < 0 || c.x >= nx) return false; }
                else if (ty < tz) { if (ty > t1) return false; c.y += sy; ty += dy; if (c.y < 0 || c.y >= ny) return false; }
                else { if (tz > t1) return false; c.z += sz; tz += dz; if (c.z < 0 || c.z >= nz) return false; }
            }
            return false;
        }
        bool Tri(int i, Vector3 o, Vector3 d, float max)
        {
            var p = Vector3.Cross(d, E2[i]); float det = Vector3.Dot(E1[i], p); if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1 / det; var s = o - A[i]; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, E1[i]); float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return false;
            float t = Vector3.Dot(E2[i], q) * inv; return t > 1e-6f && t < max;
        }

        /// <summary>Evenly spread directions on a sphere.</summary>
        public static Vector3[] Directions(int n)
        {
            var r = new Vector3[n]; float g = Mathf.PI * (3 - Mathf.Sqrt(5));
            for (int i = 0; i < n; i++) { float y = 1 - (i + 0.5f) / n * 2, rad = Mathf.Sqrt(1 - y * y), th = g * i; r[i] = new Vector3(Mathf.Cos(th) * rad, y, Mathf.Sin(th) * rad); }
            return r;
        }
    }

    /// <summary>Mesh Kit jobs that need the scene: joining, splitting, colliders, lighting baked into vertex colours, export.</summary>
    public static class MeshKitActions
    {
        static Mesh MeshOf(Component c) => c is SkinnedMeshRenderer sk ? sk.sharedMesh : c is MeshFilter mf ? mf.sharedMesh : c.GetComponent<MeshFilter>()?.sharedMesh;

        // ---------------------------------------------------------------- hidden faces
        /// <summary>Faces nobody can see: from them, no direction gets out of the model (inside walls, covered faces).</summary>
        public static MeshSnapshot RemoveHidden(MeshSnapshot s, out int removed)
        {
            var part = MeshOps.Parts(s, out _, out var tris); var a = new List<Vector3>(); var b = new List<Vector3>(); var c = new List<Vector3>();
            foreach (var (sm, i) in tris) { var l = s.indices[sm]; a.Add(s.positions[l[i]]); b.Add(s.positions[l[i + 1]]); c.Add(s.positions[l[i + 2]]); }
            var rays = new TriRays(a, b, c); var dirs = TriRays.Directions(96); float far = EditMeshSize(s) * 4, nudge = EditMeshSize(s) * 1e-4f;
            var seen = new bool[tris.Count]; int gone = 0;
            System.Threading.Tasks.Parallel.For(0, tris.Count, t =>
            {
                var n = Vector3.Cross(b[t] - a[t], c[t] - a[t]); if (n.sqrMagnitude <= 0) { seen[t] = true; return; } n.Normalize();
                // a few spots on the face, each looking out over the face's front
                foreach (var w in new[] { new Vector3(1 / 3f, 1 / 3f, 1 / 3f), new Vector3(0.7f, 0.15f, 0.15f), new Vector3(0.15f, 0.7f, 0.15f), new Vector3(0.15f, 0.15f, 0.7f) })
                {
                    var p = a[t] * w.x + b[t] * w.y + c[t] * w.z + n * nudge;
                    foreach (var d in dirs) if (Vector3.Dot(d, n) > 0.02f && !rays.Hit(p, d, far, t)) { seen[t] = true; return; }
                }
            });
            foreach (bool v in seen) if (!v) gone++;
            removed = gone; if (gone == 0 || gone == tris.Count) { removed = 0; return s; }
            return MeshOps.KeepTriangles(s, t => seen[t]);
        }
        static float EditMeshSize(MeshSnapshot s) { var b = new Bounds(s.positions.Length > 0 ? s.positions[0] : Vector3.zero, Vector3.zero); foreach (var p in s.positions) b.Encapsulate(p); return Mathf.Max(1e-4f, b.size.magnitude); }

        // ---------------------------------------------------------------- join and split
        /// <summary>Joins every mesh under root into one object (one submesh per material). The old objects are hidden.</summary>
        public static GameObject Join(GameObject root, string folder)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(false).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>() && f.GetComponent<MeshRenderer>().enabled).ToList();
            if (filters.Count == 0) return null;
            var mats = new List<Material>(); var parts = new List<(MeshSnapshot, Matrix4x4, int[])>();
            foreach (var f in filters)
            {
                var r = f.GetComponent<MeshRenderer>(); var sm = r.sharedMaterials; var slot = new int[f.sharedMesh.subMeshCount];
                for (int i = 0; i < slot.Length; i++) { var m = i < sm.Length ? sm[i] : null; int k = mats.IndexOf(m); if (k < 0) { k = mats.Count; mats.Add(m); } slot[i] = k; }
                parts.Add((MeshSnapshot.Take(f.sharedMesh), root.transform.worldToLocalMatrix * f.transform.localToWorldMatrix, slot));
            }
            var joined = MeshOps.Join(parts, mats.Count, root.name + " joined");
            var mesh = MeshDecimateActions.Save(root.name + " joined", joined, folder, ""); mesh.RecalculateBounds();
            var go = new GameObject(root.name + " (joined)"); Undo.RegisterCreatedObjectUndo(go, "Join meshes");
            go.transform.SetParent(root.transform.parent, false); go.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation); go.transform.localScale = root.transform.localScale;
            go.transform.SetSiblingIndex(root.transform.GetSiblingIndex() + 1); go.layer = root.layer; go.isStatic = root.isStatic;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            Undo.RecordObject(root, "Join meshes"); root.SetActive(false);
            return go;
        }

        /// <summary>Splits an object's mesh into child objects: one per material, or one per loose piece.</summary>
        public static List<GameObject> Split(GameObject go, bool byPiece, string folder)
        {
            var made = new List<GameObject>(); var mf = go.GetComponent<MeshFilter>(); var r = go.GetComponent<MeshRenderer>();
            if (!mf || !mf.sharedMesh || !r) return made;
            var s = MeshSnapshot.Take(mf.sharedMesh); var mats = r.sharedMaterials;
            var part = MeshOps.Parts(s, out int count, out var tris);
            int groups = byPiece ? count : s.indices.Length;
            for (int g = 0; g < groups; g++)
            {
                int gg = g; var piece = MeshOps.KeepTriangles(s, t => byPiece ? part[t] == gg : tris[t].sm == gg);
                if (piece.TriangleCount == 0) continue;
                // the piece gets its own pivot in its middle (bottom for pieces, so they sit on things)
                var spot = MeshOps.PivotSpot(piece, byPiece ? 1 : 3); piece = MeshOps.Transform(piece, Matrix4x4.Translate(-spot));
                string label = byPiece ? "piece " + (g + 1) : (g < mats.Length && mats[g] ? mats[g].name : "part " + (g + 1));
                var mesh = MeshDecimateActions.Save(mf.sharedMesh.name + " " + label, piece, folder, "");
                var c = new GameObject(go.name + " " + label); Undo.RegisterCreatedObjectUndo(c, "Split mesh");
                c.transform.SetParent(go.transform, false); c.transform.localPosition = spot; c.layer = go.layer; c.isStatic = go.isStatic;
                c.AddComponent<MeshFilter>().sharedMesh = mesh; c.AddComponent<MeshRenderer>().sharedMaterials = byPiece ? mats : new[] { g < mats.Length ? mats[g] : null };
                made.Add(c);
            }
            if (made.Count > 0) { Undo.RecordObject(r, "Split mesh"); r.enabled = false; }
            return made;
        }

        // ---------------------------------------------------------------- colliders
        public enum ColliderKind { Box, Convex, LowPoly }

        /// <summary>Adds a collider to every mesh under root. Convex: a round-the-outside hull. Low poly: a mesh collider
        /// with <paramref name="share"/> of the triangles. Box: fits the mesh.</summary>
        public static int Colliders(GameObject root, ColliderKind kind, float share, bool replace, string folder)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue; var go = mf.gameObject;
                if (replace) foreach (var old in go.GetComponents<Collider>()) Undo.DestroyObjectImmediate(old);
                if (kind == ColliderKind.Box) { var b = Undo.AddComponent<BoxCollider>(go); b.center = mf.sharedMesh.bounds.center; b.size = mf.sharedMesh.bounds.size; n++; continue; }
                var set = new DecimateSettings { keepSeams = false, keepSharp = false, keepBorders = true, joinDistance = mf.sharedMesh.bounds.size.magnitude * 0.001f };
                if (kind == ColliderKind.Convex) set.targetTriangles = Mathf.Min(250, mf.sharedMesh.triangles.Length / 3); else set.ratio = share;
                var src = MeshSnapshot.Take(mf.sharedMesh); var r = MeshDecimator.Run(StripLooks(src), set, null, default);
                var mesh = MeshDecimateActions.Save(mf.sharedMesh, r.mesh, folder, kind == ColliderKind.Convex ? " hull" : " collider");
                var mc = Undo.AddComponent<MeshCollider>(go); mc.sharedMesh = mesh; mc.convex = kind == ColliderKind.Convex; n++;
            }
            return n;
        }
        /// <summary>Only the shape (a collider needs no UVs, normals or materials).</summary>
        static MeshSnapshot StripLooks(MeshSnapshot s)
        {
            var o = new MeshSnapshot { name = s.name, vertexCount = s.vertexCount, positions = s.positions };
            var all = new List<int>(); for (int i = 0; i < s.indices.Length; i++) if (s.topologies[i] == MeshTopology.Triangles) all.AddRange(s.indices[i]);
            o.indices = new[] { all.ToArray() }; o.topologies = new[] { MeshTopology.Triangles }; return o;
        }

        // ---------------------------------------------------------------- light baked into vertex colours
        [Serializable]
        public class BakeSettings
        {
            public float brightness = 1f;
            public bool shadows = true, occlusion = true;
            public float occlusionSize = 0.5f, occlusionStrength = 0.8f;
            public bool keepColours;
            public int rays = 48;
        }

        /// <summary>Works out the light at every vertex (scene lights, ambient light, shadows from other objects and
        /// ambient occlusion) and stores it as vertex colours, like the PS1 did. Each object gets its own mesh.</summary>
        public static int BakeLight(GameObject root, BakeSettings set, string folder)
        {
            var targets = root.GetComponentsInChildren<MeshFilter>(false).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()).ToList();
            if (targets.Count == 0) return 0;
            // everything in the scene that can throw a shadow
            var a = new List<Vector3>(); var b = new List<Vector3>(); var c = new List<Vector3>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (!mf.sharedMesh || !mr || !mr.enabled || mr.shadowCastingMode == ShadowCastingMode.Off) continue;
                if (a.Count > 400000) break;
                var s = MeshSnapshot.Take(mf.sharedMesh); var m = mf.transform.localToWorldMatrix;
                for (int sm = 0; sm < s.indices.Length; sm++) if (s.topologies[sm] == MeshTopology.Triangles) { var l = s.indices[sm]; for (int i = 0; i + 2 < l.Length; i += 3) { a.Add(m.MultiplyPoint3x4(s.positions[l[i]])); b.Add(m.MultiplyPoint3x4(s.positions[l[i + 1]])); c.Add(m.MultiplyPoint3x4(s.positions[l[i + 2]])); } }
            }
            var rays = new TriRays(a, b, c);
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled && l.intensity > 0 && l.type != LightType.Rectangle && l.type != LightType.Disc).ToArray();
            var probe = RenderSettings.ambientProbe; var dirs = TriRays.Directions(set.rays * 2);
            int done = 0;
            foreach (var mf in targets)
            {
                var s = MeshSnapshot.Take(mf.sharedMesh); var m = mf.transform.localToWorldMatrix; var nm = m.inverse.transpose; int n = s.vertexCount;
                var normals = s.normals; if (normals == null) { var tmp = s.ToMesh(); tmp.RecalculateNormals(); normals = tmp.normals; Object.DestroyImmediate(tmp); }
                var cols = new Color[n]; var size = mf.sharedMesh.bounds.size.magnitude * mf.transform.lossyScale.magnitude / 1.732f; float nudge = Mathf.Max(1e-4f, size * 1e-3f);
                var wp = new Vector3[n]; var wn = new Vector3[n]; for (int i = 0; i < n; i++) { wp[i] = m.MultiplyPoint3x4(s.positions[i]); wn[i] = nm.MultiplyVector(normals[i]).normalized; }
                var ld = lights.Select(l => (type: l.type, pos: l.transform.position, fwd: l.transform.forward, col: l.color.linear * l.intensity, range: l.range, spot: l.spotAngle, inner: l.innerSpotAngle, shadow: l.shadows != LightShadows.None)).ToArray();
                var sh = new Vector3[1]; var outc = new Color[1];
                for (int i = 0; i < n; i++)
                {
                    var p = wp[i]; var nn = wn[i]; var o = p + nn * nudge;
                    sh[0] = nn; probe.Evaluate(sh, outc); var col = outc[0]; if (col.maxColorComponent < 1e-4f) col = RenderSettings.ambientLight.linear;
                    if (set.occlusion)
                    {
                        int hits = 0, tot = 0; foreach (var d in dirs) { float k = Vector3.Dot(d, nn); if (k <= 0.05f) continue; tot++; if (rays.Hit(o, d, set.occlusionSize)) hits++; }
                        if (tot > 0) col *= 1 - set.occlusionStrength * hits / (float)tot;
                    }
                    foreach (var l in ld)
                    {
                        Vector3 L; float att = 1, dist = 1e6f;
                        if (l.type == LightType.Directional) L = -l.fwd;
                        else
                        {
                            var v = l.pos - p; dist = v.magnitude; if (dist > l.range || dist < 1e-6f) continue; L = v / dist;
                            float q = 1 - (dist * dist) / (l.range * l.range); att = q * q / (1 + dist * dist);
                            if (l.type == LightType.Spot)
                            {
                                float cosO = Mathf.Cos(l.spot * 0.5f * Mathf.Deg2Rad), cosI = Mathf.Cos(Mathf.Max(l.inner, 0.01f) * 0.5f * Mathf.Deg2Rad);
                                att *= Mathf.SmoothStep(0, 1, Mathf.InverseLerp(cosO, cosI, Vector3.Dot(-L, l.fwd)));
                            }
                        }
                        float ndl = Vector3.Dot(nn, L); if (ndl <= 0 || att <= 0) continue;
                        if (set.shadows && l.shadow && rays.Hit(o, L, dist - nudge * 2)) continue;
                        col += l.col * (ndl * att);
                    }
                    col *= set.brightness; col.a = 1;
                    if (set.keepColours && s.colors != null) col *= s.colors[i];
                    cols[i] = col;
                }
                s.colors = cols;
                var mesh = MeshDecimateActions.Save(mf.sharedMesh, s, folder, " lit " + mf.gameObject.name);
                Undo.RecordObject(mf, "Bake light"); mf.sharedMesh = mesh; done++;
            }
            return done;
        }

        public const string VertexLitShader = "HienaDev/Baked Vertex Light";

        /// <summary>Copies of the materials under root that show the baked vertex light (texture x vertex colour, no lighting).</summary>
        public static int UseVertexLitMaterials(GameObject root, string folder)
        {
            var sh = Shader.Find(VertexLitShader); if (!sh) return 0; folder = MeshDecimateActions.Folder(folder); int n = 0;
            var made = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m || m.shader == sh) continue;
                    if (!made.TryGetValue(m, out var v))
                    {
                        string path = folder + "/" + m.name + " vertex lit.mat"; v = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!v) { v = new Material(sh) { name = m.name + " vertex lit" }; AssetDatabase.CreateAsset(v, path); }
                        var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                        v.SetTexture("_BaseMap", tex); v.SetColor("_BaseColor", col);
                        if (m.HasProperty("_BaseMap")) { v.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap")); v.SetTextureOffset("_BaseMap", m.GetTextureOffset("_BaseMap")); }
                        EditorUtility.SetDirty(v); made[m] = v;
                    }
                    mats[i] = v; n++;
                }
                Undo.RecordObject(r, "Vertex lit materials"); r.sharedMaterials = mats;
            }
            return n;
        }

        // ---------------------------------------------------------------- export
        /// <summary>True when Unity's FBX Exporter package is in the project.</summary>
        public static bool CanFbx => FbxExporter != null;
        static Type FbxExporter => Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");

        public static bool ExportFbx(GameObject root, string path)
        {
            var t = FbxExporter; if (t == null) return false;
            var m = t.GetMethods().FirstOrDefault(x => x.Name == "ExportObject" && x.GetParameters().Length == 2 && x.GetParameters()[1].ParameterType == typeof(Object));
            return m != null && m.Invoke(null, new object[] { path, root }) != null;
        }

        /// <summary>Writes everything under root as an OBJ file (+ an MTL file with the colours and texture paths).</summary>
        public static void ExportObj(GameObject root, string path)
        {
            var ci = CultureInfo.InvariantCulture; var sb = new StringBuilder(); var mtl = new StringBuilder(); var mats = new List<Material>();
            string mtlName = Path.GetFileNameWithoutExtension(path) + ".mtl";
            sb.AppendLine("# Made with HienaDev Mesh Kit").AppendLine("mtllib " + mtlName);
            int baseV = 1; var inv = root.transform.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                var mesh = MeshOf(r); if (!mesh || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var s = MeshSnapshot.Take(mesh); var m = inv * r.transform.localToWorldMatrix; if (r is SkinnedMeshRenderer) m = inv * r.transform.localToWorldMatrix;
                var nm = m.inverse.transpose;
                sb.AppendLine("o " + r.name.Replace(' ', '_'));
                // Unity is left-handed: OBJ files are mirrored on X (Unity mirrors them back when importing)
                foreach (var p in s.positions) { var w = m.MultiplyPoint3x4(p); sb.Append("v ").Append((-w.x).ToString(ci)).Append(' ').Append(w.y.ToString(ci)).Append(' ').Append(w.z.ToString(ci)).AppendLine(); }
                bool hasUv = s.uvs[0] != null, hasN = s.normals != null;
                if (hasUv) foreach (var u in s.uvs[0]) sb.Append("vt ").Append(u.x.ToString(ci)).Append(' ').Append(u.y.ToString(ci)).AppendLine();
                if (hasN) foreach (var n in s.normals) { var w = nm.MultiplyVector(n).normalized; sb.Append("vn ").Append((-w.x).ToString(ci)).Append(' ').Append(w.y.ToString(ci)).Append(' ').Append(w.z.ToString(ci)).AppendLine(); }
                var rm = r.sharedMaterials;
                for (int sm = 0; sm < s.indices.Length; sm++)
                {
                    if (s.topologies[sm] != MeshTopology.Triangles) continue;
                    var mat = sm < rm.Length ? rm[sm] : null; if (!mats.Contains(mat)) mats.Add(mat);
                    sb.AppendLine("usemtl " + MatName(mat, mats.IndexOf(mat)));
                    var l = s.indices[sm];
                    string F(int i) { int v = l[i] + baseV; return hasUv && hasN ? v + "/" + v + "/" + v : hasUv ? v + "/" + v : hasN ? v + "//" + v : v.ToString(); }
                    for (int i = 0; i + 2 < l.Length; i += 3) sb.Append("f ").Append(F(i)).Append(' ').Append(F(i + 2)).Append(' ').Append(F(i + 1)).AppendLine();
                }
                baseV += s.vertexCount;
            }
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i]; mtl.AppendLine("newmtl " + MatName(m, i));
                var col = m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m && m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                mtl.AppendLine("Kd " + col.r.ToString(ci) + " " + col.g.ToString(ci) + " " + col.b.ToString(ci));
                var tex = m ? (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null) : null;
                if (tex) { var tp = AssetDatabase.GetAssetPath(tex); if (!string.IsNullOrEmpty(tp)) mtl.AppendLine("map_Kd " + Path.GetFullPath(tp).Replace('\\', '/')); }
                mtl.AppendLine();
            }
            File.WriteAllText(path, sb.ToString()); File.WriteAllText(Path.Combine(Path.GetDirectoryName(path) ?? "", mtlName), mtl.ToString());
        }
        static string MatName(Material m, int i) => (m ? m.name : "none").Replace(' ', '_') + "_" + i;
    }
}
