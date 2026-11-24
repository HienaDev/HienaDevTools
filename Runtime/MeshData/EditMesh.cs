using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A mesh as lists, easy to add vertices and triangles to. Keeps every attribute (UVs, colours, bone
    /// weights, blend shapes) and copies or blends them when vertices are copied or made between two others.</summary>
    public class EditMesh
    {
        public string name;
        public List<Vector3> pos = new List<Vector3>();
        public List<Vector3> nrm; public List<Vector4> tan; public List<Color> col; public List<BoneWeight> bw;
        public List<Vector4>[] uv = new List<Vector4>[8]; public int[] uvDims = new int[8];
        public Matrix4x4[] bindposes;
        public List<List<int>> idx = new List<List<int>>(); public List<MeshTopology> topo = new List<MeshTopology>();
        public class Frame { public string shape; public float weight; public List<Vector3> dv = new List<Vector3>(), dn = new List<Vector3>(), dt = new List<Vector3>(); }
        public List<Frame> frames = new List<Frame>();

        public int Count => pos.Count;

        /// <summary>An empty mesh with the same kinds of data as <paramref name="like"/> (and the same submeshes, empty).</summary>
        public static EditMesh LikeOf(EditMesh like)
        {
            var e = new EditMesh { name = like.name, bindposes = like.bindposes, uvDims = (int[])like.uvDims.Clone() };
            if (like.nrm != null) e.nrm = new List<Vector3>(); if (like.tan != null) e.tan = new List<Vector4>(); if (like.col != null) e.col = new List<Color>(); if (like.bw != null) e.bw = new List<BoneWeight>();
            for (int c = 0; c < 8; c++) if (like.uv[c] != null) e.uv[c] = new List<Vector4>();
            foreach (var f in like.frames) e.frames.Add(new Frame { shape = f.shape, weight = f.weight });
            foreach (var t in like.topo) { e.topo.Add(t); e.idx.Add(new List<int>()); }
            return e;
        }

        public static EditMesh From(MeshSnapshot s)
        {
            var e = new EditMesh { name = s.name, bindposes = s.bindposes, uvDims = (int[])s.uvDims.Clone() };
            e.pos.AddRange(s.positions);
            if (s.normals != null) e.nrm = new List<Vector3>(s.normals);
            if (s.tangents != null) e.tan = new List<Vector4>(s.tangents);
            if (s.colors != null) e.col = new List<Color>(s.colors);
            if (s.boneWeights != null) e.bw = new List<BoneWeight>(s.boneWeights);
            for (int c = 0; c < 8; c++) if (s.uvs[c] != null) e.uv[c] = new List<Vector4>(s.uvs[c]);
            foreach (var sh in s.shapes) foreach (var f in sh.frames) e.frames.Add(new Frame { shape = sh.name, weight = f.weight, dv = new List<Vector3>(f.dv), dn = new List<Vector3>(f.dn ?? new Vector3[s.vertexCount]), dt = new List<Vector3>(f.dt ?? new Vector3[s.vertexCount]) });
            for (int i = 0; i < s.indices.Length; i++) { e.idx.Add(new List<int>(s.indices[i])); e.topo.Add(s.topologies[i]); }
            return e;
        }

        public MeshSnapshot ToSnapshot()
        {
            var s = new MeshSnapshot { name = name, vertexCount = pos.Count, positions = pos.ToArray(), bindposes = bindposes, uvDims = (int[])uvDims.Clone() };
            s.normals = nrm?.ToArray(); s.tangents = tan?.ToArray(); s.colors = col?.ToArray(); s.boneWeights = bw?.ToArray();
            for (int c = 0; c < 8; c++) s.uvs[c] = uv[c]?.ToArray();
            s.indices = idx.Select(l => l.ToArray()).ToArray(); s.topologies = topo.ToArray();
            var shapes = new List<MeshSnapshot.Shape>();
            foreach (var g in frames.GroupBy(f => f.shape)) shapes.Add(new MeshSnapshot.Shape { name = g.Key, frames = g.Select(f => new MeshSnapshot.Frame { weight = f.weight, dv = f.dv.ToArray(), dn = f.dn.ToArray(), dt = f.dt.ToArray() }).ToArray() });
            s.shapes = shapes.ToArray();
            return s;
        }

        /// <summary>Copies vertex i of <paramref name="src"/> to the end. Returns its new number.</summary>
        public int Copy(EditMesh src, int i)
        {
            pos.Add(src.pos[i]);
            nrm?.Add(src.nrm[i]); tan?.Add(src.tan[i]); col?.Add(src.col[i]); bw?.Add(src.bw[i]);
            for (int c = 0; c < 8; c++) uv[c]?.Add(src.uv[c][i]);
            for (int f = 0; f < frames.Count; f++) { frames[f].dv.Add(src.frames[f].dv[i]); frames[f].dn.Add(src.frames[f].dn[i]); frames[f].dt.Add(src.frames[f].dt[i]); }
            return pos.Count - 1;
        }

        /// <summary>Adds a vertex between a and b (t = 0 at a) of <paramref name="src"/>.</summary>
        public int Lerp(EditMesh src, int a, int b, float t)
        {
            pos.Add(Vector3.Lerp(src.pos[a], src.pos[b], t));
            nrm?.Add(Vector3.Lerp(src.nrm[a], src.nrm[b], t).normalized);
            if (tan != null) { var x = Vector4.Lerp(src.tan[a], src.tan[b], t); var d = new Vector3(x.x, x.y, x.z).normalized; tan.Add(new Vector4(d.x, d.y, d.z, src.tan[a].w)); }
            col?.Add(Color.Lerp(src.col[a], src.col[b], t)); bw?.Add(t < 0.5f ? src.bw[a] : src.bw[b]);
            for (int c = 0; c < 8; c++) uv[c]?.Add(Vector4.Lerp(src.uv[c][a], src.uv[c][b], t));
            for (int f = 0; f < frames.Count; f++) { var s = src.frames[f]; frames[f].dv.Add(Vector3.Lerp(s.dv[a], s.dv[b], t)); frames[f].dn.Add(Vector3.Lerp(s.dn[a], s.dn[b], t)); frames[f].dt.Add(Vector3.Lerp(s.dt[a], s.dt[b], t)); }
            return pos.Count - 1;
        }

        public Bounds Bounds() { if (pos.Count == 0) return new Bounds(); var b = new Bounds(pos[0], Vector3.zero); foreach (var p in pos) b.Encapsulate(p); return b; }

        /// <summary>Two vertices that look the same everywhere (so they can be one).</summary>
        public bool Same(int a, int b)
        {
            if (pos[a] != pos[b]) return false;
            if (nrm != null && Vector3.Dot(nrm[a], nrm[b]) < 0.9999f) return false;
            if (tan != null && (tan[a] - tan[b]).sqrMagnitude > 1e-8f) return false;
            if (col != null && col[a] != col[b]) return false;
            if (bw != null && !bw[a].Equals(bw[b])) return false;
            for (int c = 0; c < 8; c++) if (uv[c] != null && (uv[c][a] - uv[c][b]).sqrMagnitude > 1e-12f) return false;
            foreach (var f in frames) if (f.dv[a] != f.dv[b] || f.dn[a] != f.dn[b]) return false;
            return true;
        }
    }
}
