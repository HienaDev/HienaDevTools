using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HienaDev.DevTools
{
    /// <summary>A model's triangles as plain numbers (also when the mesh isn't marked readable).</summary>
    public class TextureMesh
    {
        public Vector3[] v, n; public Vector2[] uv; public int[] t; public Bounds bounds;
        static readonly Dictionary<Mesh, TextureMesh> s_Known = new Dictionary<Mesh, TextureMesh>();

        public static TextureMesh Of(Mesh mesh)
        {
            if (!mesh) return null; if (s_Known.TryGetValue(mesh, out var k)) return k; TextureMesh r = null;
            try
            {
                using (var arr = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var d = arr[0]; r = new TextureMesh { bounds = mesh.bounds }; using (var a = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { d.GetVertices(a); r.v = a.ToArray(); } using (var a = new NativeArray<Vector3>(d.vertexCount, Allocator.Temp)) { if (d.HasVertexAttribute(VertexAttribute.Normal)) d.GetNormals(a); r.n = a.ToArray(); } using (var a = new NativeArray<Vector2>(d.vertexCount, Allocator.Temp)) { if (d.HasVertexAttribute(VertexAttribute.TexCoord0)) d.GetUVs(0, a); r.uv = a.ToArray(); }
                    var l = new List<int>(); for (int s = 0; s < d.subMeshCount; s++) { var sm = d.GetSubMesh(s); if (sm.topology != MeshTopology.Triangles) continue; using (var a = new NativeArray<int>(sm.indexCount, Allocator.Temp)) { d.GetIndices(a, s); l.AddRange(a.ToArray()); } } r.t = l.ToArray();
                }
            }
            catch { r = null; }
            if (s_Known.Count > 60) s_Known.Clear(); return s_Known[mesh] = r;
        }

        /// <summary>All the meshes of a model as one, in the model's own space.</summary>
        public static TextureMesh Of(GameObject go)
        {
            if (!go) return null; var r = new TextureMesh(); var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var root = go.transform.worldToLocalMatrix;
            void Add(Mesh mesh, Transform at) { var m = Of(mesh); if (m == null) return; var x = root * at.localToWorldMatrix; int b = v.Count; foreach (var p in m.v) v.Add(x.MultiplyPoint3x4(p)); foreach (var p in m.n) n.Add(x.MultiplyVector(p).normalized); uv.AddRange(m.uv); foreach (var i in m.t) t.Add(b + i); }
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true)) Add(f.sharedMesh, f.transform); foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(s.sharedMesh, s.transform); if (t.Count == 0) return null;
            r.v = v.ToArray(); r.n = n.ToArray(); r.uv = uv.ToArray(); r.t = t.ToArray(); var bd = new Bounds(v[0], Vector3.zero); foreach (var p in v) bd.Encapsulate(p); r.bounds = bd; return r;
        }

        public bool Hit(Ray ray, Matrix4x4 at, ref float best, out int tri, out Vector3 bary)
        {
            tri = -1; bary = Vector3.zero; var inv = at.inverse; var o = inv.MultiplyPoint3x4(ray.origin); var d = inv.MultiplyVector(ray.direction);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a, p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < 1e-14f) continue; float f = 1 / det; var q0 = o - a; float u = f * Vector3.Dot(q0, p); if (u < 0 || u > 1) continue; var q = Vector3.Cross(q0, e1); float w = f * Vector3.Dot(d, q); if (w < 0 || u + w > 1) continue; float k = f * Vector3.Dot(e2, q); if (k <= 0 || k >= best) continue; best = k; tri = i; bary = new Vector3(1 - u - w, u, w);
            }
            return tri >= 0;
        }

        // ------------------------------------------------------------------ where the corners are

        /// <summary>For every spot of the texture: how far it is from the model's nearest outward edge (and inward corner), in
        /// parts of the model's size. 1 = far from any, or no part of the model uses that spot.</summary>
        public static (float[] outward, float[] inward) Edges(TextureMesh m, int size, float angle, float reach)
        {
            float unit = Mathf.Max(1e-5f, m.bounds.size.magnitude); var outw = new float[size * size]; var inw = new float[size * size]; for (int i = 0; i < outw.Length; i++) outw[i] = inw[i] = 1f; var used = new bool[size * size];
            // corners in the same place are one corner (a hard edge has its own for each side)
            var same = new Dictionary<Vector3Int, int>(); var id = new int[m.v.Length]; float g = unit * 2e-5f; for (int i = 0; i < id.Length; i++) { var k = new Vector3Int(Mathf.RoundToInt(m.v[i].x / g), Mathf.RoundToInt(m.v[i].y / g), Mathf.RoundToInt(m.v[i].z / g)); if (!same.TryGetValue(k, out int q)) same[k] = q = same.Count; id[i] = q; }
            var edges = new Dictionary<(int, int), List<int>>(); for (int i = 0; i < m.t.Length; i += 3) for (int e = 0; e < 3; e++) { int a = id[m.t[i + e]], b = id[m.t[i + (e + 1) % 3]]; var k = a < b ? (a, b) : (b, a); if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<int>(2); l.Add(i); }
            Vector3 Face(int i) => Vector3.Cross(m.v[m.t[i + 1]] - m.v[m.t[i]], m.v[m.t[i + 2]] - m.v[m.t[i]]).normalized; Vector3 Mid(int i) => (m.v[m.t[i]] + m.v[m.t[i + 1]] + m.v[m.t[i + 2]]) / 3;
            var segs = new List<(Vector3 a, Vector3 b, bool outw)>(); float cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            foreach (var kv in edges) { if (kv.Value.Count < 2) continue; int f0 = kv.Value[0], f1 = kv.Value[1]; Vector3 n0 = Face(f0), n1 = Face(f1); if (Vector3.Dot(n0, n1) > cos) continue; int va = -1, vb = -1; for (int c = 0; c < 3; c++) { if (id[m.t[f0 + c]] == kv.Key.Item1) va = m.t[f0 + c]; if (id[m.t[f0 + c]] == kv.Key.Item2) vb = m.t[f0 + c]; } if (va < 0 || vb < 0) continue; segs.Add((m.v[va], m.v[vb], Vector3.Dot(n0, Mid(f1) - Mid(f0)) < 0)); }
            if (segs.Count == 0) return (outw, inw);
            // looked up by place, so a big model doesn't compare everything with everything
            float cell = reach * unit; var grid = new Dictionary<Vector3Int, List<int>>(); Vector3Int C(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
            for (int s = 0; s < segs.Count; s++) { int steps = Mathf.Clamp(Mathf.CeilToInt((segs[s].b - segs[s].a).magnitude / cell * 1.5f), 1, 400); Vector3Int last = new Vector3Int(int.MinValue, 0, 0); for (int q = 0; q <= steps; q++) { var c = C(Vector3.Lerp(segs[s].a, segs[s].b, q / (float)steps)); if (c == last) continue; last = c; if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>(); if (!l.Contains(s)) l.Add(s); } }
            Parallel.For(0, m.t.Length / 3, ti =>
            {
                int i = ti * 3; Vector2 u0 = m.uv[m.t[i]] * size, u1 = m.uv[m.t[i + 1]] * size, u2 = m.uv[m.t[i + 2]] * size; float area = (u1.x - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (u1.y - u0.y); if (Mathf.Abs(area) < 1e-6f) return; int x0 = Mathf.FloorToInt(Mathf.Min(u0.x, Mathf.Min(u1.x, u2.x))) - 1, x1 = Mathf.CeilToInt(Mathf.Max(u0.x, Mathf.Max(u1.x, u2.x))) + 1, y0 = Mathf.FloorToInt(Mathf.Min(u0.y, Mathf.Min(u1.y, u2.y))) - 1, y1 = Mathf.CeilToInt(Mathf.Max(u0.y, Mathf.Max(u1.y, u2.y))) + 1; if (x1 - x0 > size * 8 || y1 - y0 > size * 8) return;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f, b = ((px - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (py - u0.y)) / area, c = ((u1.x - u0.x) * (py - u0.y) - (px - u0.x) * (u1.y - u0.y)) / area, a = 1 - b - c; float slack = -1.2f / Mathf.Sqrt(Mathf.Abs(area)); if (a < slack || b < slack || c < slack) continue; var p = m.v[m.t[i]] * a + m.v[m.t[i + 1]] * b + m.v[m.t[i + 2]] * c; int w = ((y % size) + size) % size * size + ((x % size) + size) % size; float o = 1, n = 1; var cc = C(p);
                        for (int gz = -1; gz <= 1; gz++) for (int gy = -1; gy <= 1; gy++) for (int gx = -1; gx <= 1; gx++) if (grid.TryGetValue(new Vector3Int(cc.x + gx, cc.y + gy, cc.z + gz), out var l)) foreach (int s in l) { var sg = segs[s]; var d = sg.b - sg.a; float t = Mathf.Clamp01(Vector3.Dot(p - sg.a, d) / Mathf.Max(1e-12f, d.sqrMagnitude)), dist = (p - sg.a - d * t).magnitude / unit; if (sg.outw) o = Mathf.Min(o, dist); else n = Mathf.Min(n, dist); }
                        lock (used) { if (!used[w]) { used[w] = true; outw[w] = o; inw[w] = n; } else { outw[w] = Mathf.Min(outw[w], o); inw[w] = Mathf.Min(inw[w], n); } }
                    }
            });
            return (outw, inw);
        }
    }
}
