using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HienaDev.DevTools
{
    /// <summary>A plain copy of a mesh (arrays only), so it can be worked on away from Unity's main thread.</summary>
    public class MeshSnapshot
    {
        public string name;
        public int vertexCount;
        public Vector3[] positions, normals;
        public Vector4[] tangents;
        public Color[] colors;
        public Vector4[][] uvs = new Vector4[8][];
        public int[] uvDims = new int[8];
        public BoneWeight[] boneWeights;
        public Matrix4x4[] bindposes;
        public int[][] indices;
        public MeshTopology[] topologies;
        public Shape[] shapes = new Shape[0];

        public class Shape { public string name; public Frame[] frames; }
        public class Frame { public float weight; public Vector3[] dv, dn, dt; }

        public int TriangleCount { get { int n = 0; for (int i = 0; i < indices.Length; i++) if (topologies[i] == MeshTopology.Triangles) n += indices[i].Length / 3; return n; } }

        /// <summary>Copies a mesh. Call on the main thread.</summary>
        public static MeshSnapshot Take(Mesh m)
        {
            var s = new MeshSnapshot { name = m.name, vertexCount = m.vertexCount };
            s.positions = m.vertices;
            if (m.HasVertexAttribute(VertexAttribute.Normal)) s.normals = m.normals;
            if (m.HasVertexAttribute(VertexAttribute.Tangent)) s.tangents = m.tangents;
            if (m.HasVertexAttribute(VertexAttribute.Color)) s.colors = m.colors;
            var l = new List<Vector4>();
            for (int c = 0; c < 8; c++)
            {
                var at = VertexAttribute.TexCoord0 + c;
                if (!m.HasVertexAttribute(at)) continue;
                l.Clear(); m.GetUVs(c, l); s.uvs[c] = l.ToArray(); s.uvDims[c] = Mathf.Clamp(m.GetVertexAttributeDimension(at), 2, 4);
            }
            if (m.HasVertexAttribute(VertexAttribute.BlendWeight) || m.HasVertexAttribute(VertexAttribute.BlendIndices)) { var bw = m.boneWeights; if (bw != null && bw.Length == s.vertexCount) s.boneWeights = bw; }
            s.bindposes = m.bindposes;
            s.indices = new int[m.subMeshCount][]; s.topologies = new MeshTopology[m.subMeshCount];
            for (int i = 0; i < m.subMeshCount; i++) { s.topologies[i] = m.GetTopology(i); s.indices[i] = m.GetIndices(i); }
            s.shapes = new Shape[m.blendShapeCount];
            for (int i = 0; i < m.blendShapeCount; i++)
            {
                var sh = new Shape { name = m.GetBlendShapeName(i), frames = new Frame[m.GetBlendShapeFrameCount(i)] };
                for (int f = 0; f < sh.frames.Length; f++)
                {
                    var fr = new Frame { weight = m.GetBlendShapeFrameWeight(i, f), dv = new Vector3[s.vertexCount], dn = new Vector3[s.vertexCount], dt = new Vector3[s.vertexCount] };
                    m.GetBlendShapeFrameVertices(i, f, fr.dv, fr.dn, fr.dt); sh.frames[f] = fr;
                }
                s.shapes[i] = sh;
            }
            return s;
        }

        /// <summary>Makes a Unity mesh. Call on the main thread.</summary>
        public Mesh ToMesh(Mesh into = null)
        {
            var m = into ? into : new Mesh();
            m.Clear();
            m.name = name;
            m.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.vertices = positions;
            if (normals != null) m.normals = normals;
            if (tangents != null) m.tangents = tangents;
            if (colors != null) m.colors = colors;
            for (int c = 0; c < 8; c++)
            {
                var u = uvs[c]; if (u == null) continue;
                if (uvDims[c] == 2) { var a = new Vector2[u.Length]; for (int i = 0; i < u.Length; i++) a[i] = u[i]; m.SetUVs(c, a); }
                else if (uvDims[c] == 3) { var a = new Vector3[u.Length]; for (int i = 0; i < u.Length; i++) a[i] = u[i]; m.SetUVs(c, a); }
                else m.SetUVs(c, u);
            }
            if (boneWeights != null) { m.boneWeights = boneWeights; m.bindposes = bindposes; }
            else if (bindposes != null && bindposes.Length > 0) m.bindposes = bindposes;
            m.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) m.SetIndices(indices[i], topologies[i], i, false);
            foreach (var sh in shapes) foreach (var f in sh.frames) m.AddBlendShapeFrame(sh.name, f.weight, f.dv, f.dn, f.dt);
            m.RecalculateBounds();
            return m;
        }
    }
}
