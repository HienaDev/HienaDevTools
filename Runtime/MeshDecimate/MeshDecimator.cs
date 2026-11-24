using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace HienaDev.DevTools
{
    /// <summary>How the triangles are taken away.</summary>
    public enum DecimateMode
    {
        /// <summary>Fewer triangles: edges are collapsed where it changes the shape least (like Blender's Collapse).</summary>
        Collapse,
        /// <summary>Flat parts only: points on flat areas and straight edges are taken out, the shape does not change (like Blender's Planar).</summary>
        Planar,
    }

    [Serializable]
    public class DecimateSettings
    {
        public DecimateMode mode = DecimateMode.Collapse;
        /// <summary>Share of the triangles kept (Collapse).</summary>
        [Range(0.005f, 1f)] public float ratio = 0.5f;
        /// <summary>When above 0, the number of triangles to end with (Collapse), instead of the ratio.</summary>
        public int targetTriangles;
        /// <summary>Faces bent less than this (degrees) count as flat (Planar).</summary>
        [Range(0.1f, 45f)] public float planarAngle = 5f;
        /// <summary>Edges of holes and open sides stay where they are (they can still lose points along them).</summary>
        public bool keepBorders = true;
        /// <summary>Edges of holes keep every point.</summary>
        public bool lockBorders;
        /// <summary>Texture seams stay on their lines, so textures don't tear or slide.</summary>
        public bool keepSeams = true;
        /// <summary>Hard (flat shaded) edges stay sharp.</summary>
        public bool keepSharp = true;
        /// <summary>Points closer than this (in the mesh's units) are joined first. 0 joins only points in the very same place.</summary>
        public float joinDistance;

        public DecimateSettings Clone() => (DecimateSettings)MemberwiseClone();
        public bool SameAs(DecimateSettings o) => o != null && o.mode == mode && o.ratio == ratio && o.targetTriangles == targetTriangles && o.planarAngle == planarAngle && o.keepBorders == keepBorders && o.lockBorders == lockBorders && o.keepSeams == keepSeams && o.keepSharp == keepSharp && o.joinDistance == joinDistance;
    }

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

    /// <summary>Takes triangles out of a mesh, like Blender's Decimate modifier. Keeps texture coordinates, colours,
    /// normals, tangents, bone weights (skinned meshes), blend shapes and every material slot.</summary>
    public static class MeshDecimator
    {
        public class Result
        {
            public MeshSnapshot mesh;
            public int trianglesBefore, trianglesAfter, verticesBefore, verticesAfter;
            public double seconds;
        }

        /// <summary>Decimates a mesh in one call (main thread). Returns a new mesh.</summary>
        public static Mesh Decimate(Mesh source, DecimateSettings settings)
        {
            var r = Run(MeshSnapshot.Take(source), settings, null, CancellationToken.None);
            var m = r.mesh.ToMesh(); m.name = source.name; return m;
        }

        /// <summary>Decimates a copied mesh. Safe to call from any thread. progress gets 0..1.</summary>
        public static Result Run(MeshSnapshot src, DecimateSettings settings, Action<float> progress, CancellationToken cancel)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var w = new Work(src, settings.Clone(), progress, cancel);
            w.Go();
            if (cancel.IsCancellationRequested) return null;
            var r = new Result { mesh = w.Output(), trianglesBefore = src.TriangleCount, verticesBefore = src.vertexCount };
            r.trianglesAfter = r.mesh.TriangleCount; r.verticesAfter = r.mesh.vertexCount; r.seconds = sw.Elapsed.TotalSeconds;
            return r;
        }

        sealed class Work
        {
            readonly MeshSnapshot S; readonly DecimateSettings P; readonly Action<float> progress; readonly CancellationToken cancel;
            // packed float attributes per original vertex ("av")
            int stride, oN = -1, oT = -1, oC = -1; readonly int[] oUv = new int[8]; int oShape = -1;
            float[] attr; BoneWeight[] bw;
            int nAv, nPv;
            int[] dedupe;      // av -> the av it is the same as
            int[] owner;       // av -> pv (point)
            List<int>[] avsOf; // pv -> avs
            double[] px, py, pz; double[] Q; bool[] gone; int[] ver;
            List<int>[] vt;    // pv -> triangles
            int[] tv, ta, tsub; bool[] alive; float[] rn; int live;
            double scale, areaEps; float cosLimit;
            readonly List<int> sA = new List<int>(), sB = new List<int>(), sC = new List<int>();

            public Work(MeshSnapshot s, DecimateSettings p, Action<float> pr, CancellationToken c) { S = s; P = p; progress = pr; cancel = c; }

            // ---------------------------------------------------------------- setup
            void Pack()
            {
                nAv = S.vertexCount; int o = 0;
                if (S.normals != null) { oN = o; o += 3; }
                if (S.tangents != null) { oT = o; o += 4; }
                if (S.colors != null) { oC = o; o += 4; }
                for (int c = 0; c < 8; c++) { oUv[c] = -1; if (S.uvs[c] != null) { oUv[c] = o; o += 4; } }
                int frames = 0; foreach (var sh in S.shapes) frames += sh.frames.Length;
                if (frames > 0) { oShape = o; o += frames * 9; }
                stride = o; attr = new float[Math.Max(1, nAv * stride)];
                for (int i = 0; i < nAv; i++)
                {
                    int b = i * stride;
                    if (oN >= 0) { var n = S.normals[i]; attr[b + oN] = n.x; attr[b + oN + 1] = n.y; attr[b + oN + 2] = n.z; }
                    if (oT >= 0) { var t = S.tangents[i]; attr[b + oT] = t.x; attr[b + oT + 1] = t.y; attr[b + oT + 2] = t.z; attr[b + oT + 3] = t.w; }
                    if (oC >= 0) { var c = S.colors[i]; attr[b + oC] = c.r; attr[b + oC + 1] = c.g; attr[b + oC + 2] = c.b; attr[b + oC + 3] = c.a; }
                    for (int c = 0; c < 8; c++) if (oUv[c] >= 0) { var u = S.uvs[c][i]; attr[b + oUv[c]] = u.x; attr[b + oUv[c] + 1] = u.y; attr[b + oUv[c] + 2] = u.z; attr[b + oUv[c] + 3] = u.w; }
                    if (oShape >= 0)
                    {
                        int k = b + oShape;
                        foreach (var sh in S.shapes) foreach (var f in sh.frames) { var a = f.dv[i]; var n = f.dn[i]; var t = f.dt[i]; attr[k++] = a.x; attr[k++] = a.y; attr[k++] = a.z; attr[k++] = n.x; attr[k++] = n.y; attr[k++] = n.z; attr[k++] = t.x; attr[k++] = t.y; attr[k++] = t.z; }
                    }
                }
                bw = S.boneWeights != null ? (BoneWeight[])S.boneWeights.Clone() : null;
            }

            void Weld()
            {
                var pos = S.positions; owner = new int[nAv];
                var b = new Bounds(nAv > 0 ? pos[0] : Vector3.zero, Vector3.zero); for (int i = 0; i < nAv; i++) b.Encapsulate(pos[i]);
                scale = Math.Max(1e-6, b.size.magnitude); areaEps = (scale * 1e-7) * (scale * 1e-7);
                var reps = new List<int>(); // pv -> first av
                if (P.joinDistance <= 0)
                {
                    var d = new Dictionary<Vector3, int>(nAv);
                    for (int i = 0; i < nAv; i++) { if (!d.TryGetValue(pos[i], out int p)) { p = reps.Count; reps.Add(i); d[pos[i]] = p; } owner[i] = p; }
                }
                else
                {
                    double cell = P.joinDistance; float d2 = P.joinDistance * P.joinDistance;
                    var grid = new Dictionary<long, List<int>>();
                    long Key(long x, long y, long z) => (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
                    for (int i = 0; i < nAv; i++)
                    {
                        var v = pos[i]; long cx = (long)Math.Floor(v.x / cell), cy = (long)Math.Floor(v.y / cell), cz = (long)Math.Floor(v.z / cell);
                        int found = -1;
                        for (int x = -1; x <= 1 && found < 0; x++) for (int y = -1; y <= 1 && found < 0; y++) for (int z = -1; z <= 1 && found < 0; z++)
                                    if (grid.TryGetValue(Key(cx + x, cy + y, cz + z), out var l)) foreach (int p in l) if ((pos[reps[p]] - v).sqrMagnitude <= d2) { found = p; break; }
                        if (found < 0)
                        {
                            found = reps.Count; reps.Add(i); long k = Key(cx, cy, cz);
                            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(2); l.Add(found);
                        }
                        owner[i] = found;
                    }
                }
                nPv = reps.Count;
                px = new double[nPv]; py = new double[nPv]; pz = new double[nPv];
                for (int p = 0; p < nPv; p++) { var v = pos[reps[p]]; px[p] = v.x; py[p] = v.y; pz[p] = v.z; }
                // vertices in one place with the same look are the same vertex (so they don't count as a seam)
                avsOf = new List<int>[nPv]; dedupe = new int[nAv];
                for (int i = 0; i < nAv; i++)
                {
                    int p = owner[i]; var l = avsOf[p] ?? (avsOf[p] = new List<int>(2)); dedupe[i] = i;
                    int n = Math.Min(l.Count, 24);
                    for (int j = 0; j < n; j++) if (SameLook(l[j], i)) { dedupe[i] = l[j]; break; }
                    if (dedupe[i] == i) l.Add(i);
                }
                for (int p = 0; p < nPv; p++) if (avsOf[p] == null) avsOf[p] = new List<int>(1);
            }

            bool SameLook(int a, int b)
            {
                int ia = a * stride, ib = b * stride;
                for (int k = 0; k < stride; k++)
                {
                    float d = Math.Abs(attr[ia + k] - attr[ib + k]);
                    if (d > (oN >= 0 && k >= oN && k < oN + 3 ? 1e-3f : 1e-5f)) return false;
                }
                if (bw != null) { var x = bw[a]; var y = bw[b]; if (x.boneIndex0 != y.boneIndex0 || x.boneIndex1 != y.boneIndex1 || x.boneIndex2 != y.boneIndex2 || x.boneIndex3 != y.boneIndex3 || Math.Abs(x.weight0 - y.weight0) > 1e-4f || Math.Abs(x.weight1 - y.weight1) > 1e-4f || Math.Abs(x.weight2 - y.weight2) > 1e-4f) return false; }
                return true;
            }

            void Triangles()
            {
                int cap = 0; for (int s = 0; s < S.indices.Length; s++) if (S.topologies[s] == MeshTopology.Triangles) cap += S.indices[s].Length / 3;
                tv = new int[cap * 3]; ta = new int[cap * 3]; tsub = new int[cap]; alive = new bool[cap]; rn = new float[cap * 3];
                vt = new List<int>[nPv]; for (int p = 0; p < nPv; p++) vt[p] = new List<int>(6);
                int t = 0;
                for (int s = 0; s < S.indices.Length; s++)
                {
                    if (S.topologies[s] != MeshTopology.Triangles) continue;
                    var idx = S.indices[s];
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        int a0 = dedupe[idx[i]], a1 = dedupe[idx[i + 1]], a2 = dedupe[idx[i + 2]];
                        int p0 = owner[a0], p1 = owner[a1], p2 = owner[a2];
                        if (p0 == p1 || p1 == p2 || p0 == p2) continue;
                        tv[t * 3] = p0; tv[t * 3 + 1] = p1; tv[t * 3 + 2] = p2; ta[t * 3] = a0; ta[t * 3 + 1] = a1; ta[t * 3 + 2] = a2; tsub[t] = s; alive[t] = true;
                        Normal(p0, p1, p2, out double nx, out double ny, out double nz); double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (l > 0) { rn[t * 3] = (float)(nx / l); rn[t * 3 + 1] = (float)(ny / l); rn[t * 3 + 2] = (float)(nz / l); }
                        vt[p0].Add(t); vt[p1].Add(t); vt[p2].Add(t); t++;
                    }
                }
                live = t; Array.Resize(ref alive, t);
                cosLimit = (float)Math.Cos(P.planarAngle * Math.PI / 180.0);
            }

            void Normal(int a, int b, int c, out double nx, out double ny, out double nz) => Normal(px[a], py[a], pz[a], b, c, out nx, out ny, out nz);
            void Normal(double ax, double ay, double az, int b, int c, out double nx, out double ny, out double nz)
            {
                double ux = px[b] - ax, uy = py[b] - ay, uz = pz[b] - az, vx = px[c] - ax, vy = py[c] - ay, vz = pz[c] - az;
                nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
            }

            // corners of triangle t with point v replaced by (x,y,z)
            void NormalMoved(int t, int v, double x, double y, double z, out double nx, out double ny, out double nz)
            {
                double ax, ay, az, bx, by, bz, cx, cy, cz; int i0 = tv[t * 3], i1 = tv[t * 3 + 1], i2 = tv[t * 3 + 2];
                if (i0 == v) { ax = x; ay = y; az = z; } else { ax = px[i0]; ay = py[i0]; az = pz[i0]; }
                if (i1 == v) { bx = x; by = y; bz = z; } else { bx = px[i1]; by = py[i1]; bz = pz[i1]; }
                if (i2 == v) { cx = x; cy = y; cz = z; } else { cx = px[i2]; cy = py[i2]; cz = pz[i2]; }
                double ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
                nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
            }

            // ---------------------------------------------------------------- quadrics
            void AddPlane(int v, double nx, double ny, double nz, double d, double w)
            {
                int q = v * 10;
                Q[q] += w * nx * nx; Q[q + 1] += w * nx * ny; Q[q + 2] += w * nx * nz; Q[q + 3] += w * ny * ny; Q[q + 4] += w * ny * nz; Q[q + 5] += w * nz * nz;
                Q[q + 6] += w * nx * d; Q[q + 7] += w * ny * d; Q[q + 8] += w * nz * d; Q[q + 9] += w * d * d;
            }

            void Quadrics()
            {
                Q = new double[nPv * 10];
                for (int t = 0; t < alive.Length; t++)
                {
                    int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
                    Normal(a, b, c, out double nx, out double ny, out double nz); double l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l <= 0) continue;
                    double area = l * 0.5; nx /= l; ny /= l; nz /= l; double d = -(nx * px[a] + ny * py[a] + nz * pz[a]);
                    AddPlane(a, nx, ny, nz, d, area); AddPlane(b, nx, ny, nz, d, area); AddPlane(c, nx, ny, nz, d, area);
                }
                // edges that matter get planes standing up from them, so points stay on them
                for (int t = 0; t < alive.Length; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tv[t * 3 + k], b = tv[t * 3 + (k + 1) % 3];
                        int kind = EdgeKind(a, b, out _, out _);
                        double w = kind == 1 ? (P.keepBorders ? 20 : 1) : kind == 2 ? (P.keepSeams ? 20 : 0) : kind == 3 ? (P.keepSharp ? 20 : 0) : kind == 4 ? 20 : 0;
                        if (w <= 0) continue;
                        Normal(tv[t * 3], tv[t * 3 + 1], tv[t * 3 + 2], out double nx, out double ny, out double nz); double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl <= 0) continue; nx /= nl; ny /= nl; nz /= nl;
                        double ex = px[b] - px[a], ey = py[b] - py[a], ez = pz[b] - pz[a]; double el2 = ex * ex + ey * ey + ez * ez;
                        double mx = ey * nz - ez * ny, my = ez * nx - ex * nz, mz = ex * ny - ey * nx; double ml = Math.Sqrt(mx * mx + my * my + mz * mz); if (ml <= 0) continue; mx /= ml; my /= ml; mz /= ml;
                        double d = -(mx * px[a] + my * py[a] + mz * pz[a]);
                        AddPlane(a, mx, my, mz, d, w * el2); AddPlane(b, mx, my, mz, d, w * el2);
                    }
            }

            double QCost(int a, int b, double x, double y, double z)
            {
                int qa = a * 10, qb = b * 10;
                double a11 = Q[qa] + Q[qb], a12 = Q[qa + 1] + Q[qb + 1], a13 = Q[qa + 2] + Q[qb + 2], a22 = Q[qa + 3] + Q[qb + 3], a23 = Q[qa + 4] + Q[qb + 4], a33 = Q[qa + 5] + Q[qb + 5];
                double b1 = Q[qa + 6] + Q[qb + 6], b2 = Q[qa + 7] + Q[qb + 7], b3 = Q[qa + 8] + Q[qb + 8], c = Q[qa + 9] + Q[qb + 9];
                double r = x * (a11 * x + a12 * y + a13 * z) + y * (a12 * x + a22 * y + a23 * z) + z * (a13 * x + a23 * y + a33 * z) + 2 * (b1 * x + b2 * y + b3 * z) + c;
                return r < 0 ? 0 : r;
            }

            bool QSolve(int a, int b, out double x, out double y, out double z)
            {
                int qa = a * 10, qb = b * 10;
                double a11 = Q[qa] + Q[qb], a12 = Q[qa + 1] + Q[qb + 1], a13 = Q[qa + 2] + Q[qb + 2], a22 = Q[qa + 3] + Q[qb + 3], a23 = Q[qa + 4] + Q[qb + 4], a33 = Q[qa + 5] + Q[qb + 5];
                double b1 = -(Q[qa + 6] + Q[qb + 6]), b2 = -(Q[qa + 7] + Q[qb + 7]), b3 = -(Q[qa + 8] + Q[qb + 8]);
                double c11 = a22 * a33 - a23 * a23, c12 = a13 * a23 - a12 * a33, c13 = a12 * a23 - a13 * a22;
                double det = a11 * c11 + a12 * c12 + a13 * c13; double tr = (a11 + a22 + a33) / 3;
                x = y = z = 0;
                if (tr <= 0 || Math.Abs(det) < 1e-9 * tr * tr * tr) return false;
                double c22 = a11 * a33 - a13 * a13, c23 = a12 * a13 - a11 * a23, c33 = a11 * a22 - a12 * a12;
                x = (c11 * b1 + c12 * b2 + c13 * b3) / det; y = (c12 * b1 + c22 * b2 + c23 * b3) / det; z = (c13 * b1 + c23 * b2 + c33 * b3) / det;
                return !(double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z));
            }

            // ---------------------------------------------------------------- topology questions
            /// <summary>0 plain, 1 open edge, 2 texture seam, 3 hard edge, 4 more than two faces (never touched).</summary>
            int EdgeKind(int a, int b, out int t1, out int t2)
            {
                t1 = t2 = -1; int n = 0;
                foreach (int t in vt[a]) { if (!alive[t]) continue; if (tv[t * 3] != b && tv[t * 3 + 1] != b && tv[t * 3 + 2] != b) continue; if (n == 0) t1 = t; else if (n == 1) t2 = t; n++; }
                if (n == 1) return 1; if (n > 2) return 4; if (n == 0) return 0;
                int a1 = Corner(t1, a), b1 = Corner(t1, b), a2 = Corner(t2, a), b2 = Corner(t2, b);
                if (a1 == a2 && b1 == b2) return 0;
                if (UvDiffer(a1, a2) || UvDiffer(b1, b2)) return 2;
                if (NormalDiffer(a1, a2) || NormalDiffer(b1, b2)) return 3;
                return 2; // colours or something else changes there: treat it like a seam
            }
            int Corner(int t, int v) => tv[t * 3] == v ? ta[t * 3] : tv[t * 3 + 1] == v ? ta[t * 3 + 1] : ta[t * 3 + 2];
            bool UvDiffer(int x, int y)
            {
                if (x == y) return false;
                for (int c = 0; c < 8; c++) if (oUv[c] >= 0) for (int k = 0; k < 4; k++) if (Math.Abs(attr[x * stride + oUv[c] + k] - attr[y * stride + oUv[c] + k]) > 1e-5f) return true;
                return false;
            }
            bool NormalDiffer(int x, int y)
            {
                if (x == y || oN < 0) return false;
                float d = attr[x * stride + oN] * attr[y * stride + oN] + attr[x * stride + oN + 1] * attr[y * stride + oN + 1] + attr[x * stride + oN + 2] * attr[y * stride + oN + 2];
                return d < 0.999f;
            }
            bool Feature(int kind) => kind == 4 || (kind == 1 && (P.keepBorders || P.mode == DecimateMode.Planar)) || (kind == 2 && P.keepSeams) || (kind == 3 && P.keepSharp);

            void Neighbours(int v, List<int> into)
            {
                into.Clear();
                foreach (int t in vt[v])
                {
                    if (!alive[t]) continue;
                    for (int k = 0; k < 3; k++) { int o = tv[t * 3 + k]; if (o != v && !into.Contains(o)) into.Add(o); }
                }
            }

            /// <summary>0 free, 1 on one line of kept edges (may slide along it), 2 a corner (stays).</summary>
            int Class(int v, out int f1, out int f2)
            {
                f1 = f2 = -1; int n = 0; Neighbours(v, sC);
                foreach (int o in sC)
                {
                    int k = EdgeKind(v, o, out _, out _);
                    if (k == 4 || (k == 1 && P.lockBorders)) return 2;
                    if (!Feature(k)) continue;
                    if (n == 0) f1 = o; else if (n == 1) f2 = o; n++;
                }
                return n == 0 ? 0 : n == 2 ? 1 : 2;
            }

            // ---------------------------------------------------------------- one collapse
            const double Never = double.MaxValue;

            /// <summary>What collapsing the edge a-b costs, which end stays (keep) and where it goes (x,y,z).</summary>
            double Cost(int a, int b, out int keep, out int lose, out double x, out double y, out double z)
            {
                keep = a; lose = b; x = px[a]; y = py[a]; z = pz[a];
                int ca = Class(a, out _, out _), cb = Class(b, out int g1, out int g2);
                int kind = EdgeKind(a, b, out _, out _); bool fe = Feature(kind);
                if (kind == 4) return Never;
                if (P.mode == DecimateMode.Planar)
                {
                    // b goes into a (a stays where it is)
                    if (cb == 2) return Never;
                    if (cb == 1)
                    {
                        if (!fe) return Never;
                        int c = g1 == a ? g2 : g1; if (c < 0) return Never;
                        double ux = px[b] - px[a], uy = py[b] - py[a], uz = pz[b] - pz[a], vx = px[c] - px[b], vy = py[c] - py[b], vz = pz[c] - pz[b];
                        double lu = Math.Sqrt(ux * ux + uy * uy + uz * uz), lv = Math.Sqrt(vx * vx + vy * vy + vz * vz); if (lu <= 0 || lv <= 0) return Never;
                        if ((ux * vx + uy * vy + uz * vz) / (lu * lv) < cosLimit) return Never;
                    }
                    double worst = 0;
                    foreach (int t in vt[b])
                    {
                        if (!alive[t] || tv[t * 3] == a || tv[t * 3 + 1] == a || tv[t * 3 + 2] == a) continue;
                        NormalMoved(t, b, px[a], py[a], pz[a], out double nx, out double ny, out double nz); double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (l * l < areaEps) return Never;
                        double dot = (nx * rn[t * 3] + ny * rn[t * 3 + 1] + nz * rn[t * 3 + 2]) / l; if (dot < cosLimit) return Never;
                        worst = Math.Max(worst, 1 - dot);
                    }
                    double ex = px[b] - px[a], ey = py[b] - py[a], ez = pz[b] - pz[a];
                    return worst + 1e-3 * Math.Sqrt(ex * ex + ey * ey + ez * ez) / scale;
                }
                if (ca == 2 && cb == 2) return Never;
                if (!fe && ca > 0 && cb > 0) return Never;
                bool fixA = ca == 2 || (!fe && ca > 0), fixB = cb == 2 || (!fe && cb > 0);
                if (fixA) return QCost(a, b, px[a], py[a], pz[a]);
                if (fixB) { keep = b; lose = a; x = px[b]; y = py[b]; z = pz[b]; return QCost(a, b, x, y, z); }
                double best = QCost(a, b, px[a], py[a], pz[a]);
                double cB = QCost(a, b, px[b], py[b], pz[b]); if (cB < best) { best = cB; keep = b; lose = a; x = px[b]; y = py[b]; z = pz[b]; }
                double mx = (px[a] + px[b]) * 0.5, my = (py[a] + py[b]) * 0.5, mz = (pz[a] + pz[b]) * 0.5;
                double cM = QCost(a, b, mx, my, mz); if (cM < best) { best = cM; keep = a; lose = b; x = mx; y = my; z = mz; }
                if (QSolve(a, b, out double ox, out double oy, out double oz))
                {
                    double ex = px[b] - px[a], ey = py[b] - py[a], ez = pz[b] - pz[a]; double el2 = ex * ex + ey * ey + ez * ez;
                    double dx = ox - mx, dy = oy - my, dz = oz - mz;
                    if (dx * dx + dy * dy + dz * dz <= el2 * 1.0)
                    {
                        double cO = QCost(a, b, ox, oy, oz);
                        if (cO < best) { best = cO; x = ox; y = oy; z = oz; double t = (dx + mx - px[a]) * ex + (dy + my - py[a]) * ey + (dz + mz - pz[a]) * ez; keep = t <= el2 * 0.5 ? a : b; lose = keep == a ? b : a; }
                    }
                }
                return best;
            }

            bool Valid(int keep, int lose, double x, double y, double z)
            {
                // the two points may share only the faces between them (else the surface pinches)
                Neighbours(keep, sA); Neighbours(lose, sB); int common = 0; foreach (int o in sA) if (sB.Contains(o)) common++;
                int shared = 0; foreach (int t in vt[keep]) if (alive[t] && (tv[t * 3] == lose || tv[t * 3 + 1] == lose || tv[t * 3 + 2] == lose)) shared++;
                if (common > shared) return false;
                if (sA.Count + sB.Count - common - 2 < 2) return false; // would leave nothing
                // no face may flip over or squash flat
                for (int side = 0; side < 2; side++)
                {
                    int v = side == 0 ? keep : lose, other = side == 0 ? lose : keep;
                    foreach (int t in vt[v])
                    {
                        if (!alive[t] || tv[t * 3] == other || tv[t * 3 + 1] == other || tv[t * 3 + 2] == other) continue;
                        NormalMoved(t, v, x, y, z, out double nx, out double ny, out double nz); double l2 = nx * nx + ny * ny + nz * nz;
                        if (l2 < areaEps) return false; double l = Math.Sqrt(l2);
                        Normal(tv[t * 3], tv[t * 3 + 1], tv[t * 3 + 2], out double ox, out double oy, out double oz); double ol = Math.Sqrt(ox * ox + oy * oy + oz * oz);
                        if (ol > 0 && (nx * ox + ny * oy + nz * oz) / (l * ol) < 0.25) return false;
                        if (P.mode == DecimateMode.Planar && (nx * rn[t * 3] + ny * rn[t * 3 + 1] + nz * rn[t * 3 + 2]) / l < cosLimit) return false;
                        // keep faces from getting far worse than the ones they are made from
                        if (P.mode == DecimateMode.Collapse && (nx * rn[t * 3] + ny * rn[t * 3 + 1] + nz * rn[t * 3 + 2]) / l < -0.2) return false;
                    }
                }
                return true;
            }

            readonly List<int> mapFrom = new List<int>(), mapTo = new List<int>(), lerped = new List<int>();

            void Collapse(int keep, int lose, double x, double y, double z)
            {
                double ex = px[lose] - px[keep], ey = py[lose] - py[keep], ez = pz[lose] - pz[keep]; double el2 = ex * ex + ey * ey + ez * ez;
                float t = el2 > 0 ? (float)Math.Max(0, Math.Min(1, ((x - px[keep]) * ex + (y - py[keep]) * ey + (z - pz[keep]) * ez) / el2)) : 0;
                mapFrom.Clear(); mapTo.Clear(); lerped.Clear();
                foreach (int tr in vt[lose])
                {
                    if (!alive[tr]) continue;
                    if (tv[tr * 3] != keep && tv[tr * 3 + 1] != keep && tv[tr * 3 + 2] != keep) continue;
                    int ik = Corner(tr, keep), il = Corner(tr, lose);
                    if (!mapFrom.Contains(il))
                    {
                        mapFrom.Add(il); mapTo.Add(ik);
                        if (!lerped.Contains(ik)) { lerped.Add(ik); Blend(ik, il, t); }
                    }
                    alive[tr] = false; live--;
                }
                foreach (int tr in vt[lose])
                {
                    if (!alive[tr]) continue;
                    for (int k = 0; k < 3; k++)
                        if (tv[tr * 3 + k] == lose)
                        {
                            tv[tr * 3 + k] = keep; int m = mapFrom.IndexOf(ta[tr * 3 + k]); if (m >= 0) ta[tr * 3 + k] = mapTo[m];
                        }
                    vt[keep].Add(tr);
                }
                foreach (int av in avsOf[lose]) { owner[av] = keep; if (!mapFrom.Contains(av)) avsOf[keep].Add(av); }
                avsOf[lose].Clear(); vt[lose].Clear();
                px[keep] = x; py[keep] = y; pz[keep] = z;
                for (int k = 0; k < 10; k++) Q[keep * 10 + k] += Q[lose * 10 + k];
                gone[lose] = true; ver[keep]++; ver[lose]++;
                vt[keep].RemoveAll(tr => !alive[tr]);
            }

            void Blend(int into, int from, float t)
            {
                if (t <= 0) return;
                int a = into * stride, b = from * stride;
                for (int k = 0; k < stride; k++) attr[a + k] += (attr[b + k] - attr[a + k]) * t;
                if (oN >= 0) Unit(a + oN);
                if (oT >= 0) Unit(a + oT);
                if (bw != null && t > 0.5f) bw[into] = bw[from];
            }
            void Unit(int i) { float l = (float)Math.Sqrt(attr[i] * attr[i] + attr[i + 1] * attr[i + 1] + attr[i + 2] * attr[i + 2]); if (l > 1e-8f) { attr[i] /= l; attr[i + 1] /= l; attr[i + 2] /= l; } }

            // ---------------------------------------------------------------- the queue
            struct Entry { public double cost; public int a, b, va, vb; }
            Entry[] heap = new Entry[256]; int hn;
            void Push(Entry e)
            {
                if (hn == heap.Length) Array.Resize(ref heap, hn * 2);
                int i = hn++; while (i > 0) { int p = (i - 1) >> 1; if (heap[p].cost <= e.cost) break; heap[i] = heap[p]; i = p; }
                heap[i] = e;
            }
            Entry Pop()
            {
                var top = heap[0]; var last = heap[--hn]; int i = 0;
                while (true) { int c = i * 2 + 1; if (c >= hn) break; if (c + 1 < hn && heap[c + 1].cost < heap[c].cost) c++; if (heap[c].cost >= last.cost) break; heap[i] = heap[c]; i = c; }
                if (hn > 0) heap[i] = last; return top;
            }
            void PushEdge(int a, int b)
            {
                double c = Cost(a, b, out _, out _, out _, out _, out _);
                if (c < Never) Push(new Entry { cost = c, a = a, b = b, va = ver[a], vb = ver[b] });
                if (P.mode == DecimateMode.Planar) { c = Cost(b, a, out _, out _, out _, out _, out _); if (c < Never) Push(new Entry { cost = c, a = b, b = a, va = ver[b], vb = ver[a] }); }
            }

            public void Go()
            {
                Pack(); if (cancel.IsCancellationRequested) return;
                Weld(); if (cancel.IsCancellationRequested) return;
                Triangles(); Quadrics();
                gone = new bool[nPv]; ver = new int[nPv];
                int start = live;
                int target = P.mode == DecimateMode.Collapse ? Math.Max(1, P.targetTriangles > 0 ? Math.Min(P.targetTriangles, live) : (int)Math.Round(live * Mathf.Clamp01(P.ratio))) : 0;
                if (P.mode == DecimateMode.Collapse && target >= live) return;
                for (int round = 0; round < 6; round++)
                {
                    hn = 0; var seen = new HashSet<long>();
                    for (int t = 0; t < alive.Length; t++)
                    {
                        if (!alive[t]) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int a = tv[t * 3 + k], b = tv[t * 3 + (k + 1) % 3]; long key = a < b ? (long)a * nPv + b : (long)b * nPv + a;
                            if (seen.Add(key)) PushEdge(a, b);
                        }
                        if ((t & 4095) == 0 && cancel.IsCancellationRequested) return;
                    }
                    int done = 0, steps = 0;
                    while (hn > 0 && live > target)
                    {
                        var e = Pop();
                        if (gone[e.a] || gone[e.b] || ver[e.a] != e.va || ver[e.b] != e.vb) continue;
                        double c = Cost(e.a, e.b, out int keep, out int lose, out double x, out double y, out double z);
                        if (c >= Never) continue;
                        if (c > e.cost * 1.0001 + 1e-30) { e.cost = c; Push(e); continue; }
                        if (P.mode == DecimateMode.Planar) { keep = e.a; lose = e.b; x = px[keep]; y = py[keep]; z = pz[keep]; }
                        if (!Valid(keep, lose, x, y, z)) continue;
                        Collapse(keep, lose, x, y, z); done++;
                        Neighbours(keep, sA); var nb = sA.ToArray();
                        foreach (int o in nb) PushEdge(keep, o);
                        if ((++steps & 1023) == 0)
                        {
                            if (cancel.IsCancellationRequested) return;
                            progress?.Invoke(P.mode == DecimateMode.Collapse ? (float)(start - live) / Math.Max(1, start - target) : 1f - (float)hn / Math.Max(1, hn + steps));
                        }
                    }
                    if (live <= target || done == 0) break;
                }
                progress?.Invoke(1);
            }

            // ---------------------------------------------------------------- result
            public MeshSnapshot Output()
            {
                var o = new MeshSnapshot { name = S.name, bindposes = S.bindposes, uvDims = (int[])S.uvDims.Clone(), topologies = (MeshTopology[])S.topologies.Clone() };
                var map = new int[nAv]; for (int i = 0; i < nAv; i++) map[i] = -1;
                var order = new List<int>();
                int Use(int av) { if (map[av] < 0) { map[av] = order.Count; order.Add(av); } return map[av]; }
                var lists = new List<int>[S.indices.Length]; for (int s = 0; s < lists.Length; s++) lists[s] = new List<int>();
                if (alive != null)
                    for (int t = 0; t < alive.Length; t++) if (alive[t]) { var l = lists[tsub[t]]; l.Add(Use(ta[t * 3])); l.Add(Use(ta[t * 3 + 1])); l.Add(Use(ta[t * 3 + 2])); }
                o.indices = new int[S.indices.Length][];
                for (int s = 0; s < S.indices.Length; s++)
                {
                    if (S.topologies[s] == MeshTopology.Triangles) o.indices[s] = lists[s].ToArray();
                    else { var src = S.indices[s]; var r = new int[src.Length]; for (int i = 0; i < src.Length; i++) r[i] = Use(dedupe[src[i]]); o.indices[s] = r; }
                }
                int n = order.Count; o.vertexCount = n; o.positions = new Vector3[n];
                if (oN >= 0) o.normals = new Vector3[n]; if (oT >= 0) o.tangents = new Vector4[n]; if (oC >= 0) o.colors = new Color[n];
                for (int c = 0; c < 8; c++) if (oUv[c] >= 0) o.uvs[c] = new Vector4[n];
                if (bw != null) o.boneWeights = new BoneWeight[n];
                o.shapes = new MeshSnapshot.Shape[S.shapes.Length];
                for (int i = 0; i < S.shapes.Length; i++)
                {
                    var sh = S.shapes[i]; var ns = new MeshSnapshot.Shape { name = sh.name, frames = new MeshSnapshot.Frame[sh.frames.Length] };
                    for (int f = 0; f < sh.frames.Length; f++) ns.frames[f] = new MeshSnapshot.Frame { weight = sh.frames[f].weight, dv = new Vector3[n], dn = new Vector3[n], dt = new Vector3[n] };
                    o.shapes[i] = ns;
                }
                for (int i = 0; i < n; i++)
                {
                    int av = order[i], b = av * stride, p = owner[av];
                    o.positions[i] = new Vector3((float)px[p], (float)py[p], (float)pz[p]);
                    if (oN >= 0) o.normals[i] = new Vector3(attr[b + oN], attr[b + oN + 1], attr[b + oN + 2]);
                    if (oT >= 0) o.tangents[i] = new Vector4(attr[b + oT], attr[b + oT + 1], attr[b + oT + 2], attr[b + oT + 3] < 0 ? -1 : 1);
                    if (oC >= 0) o.colors[i] = new Color(attr[b + oC], attr[b + oC + 1], attr[b + oC + 2], attr[b + oC + 3]);
                    for (int c = 0; c < 8; c++) if (oUv[c] >= 0) o.uvs[c][i] = new Vector4(attr[b + oUv[c]], attr[b + oUv[c] + 1], attr[b + oUv[c] + 2], attr[b + oUv[c] + 3]);
                    if (bw != null) o.boneWeights[i] = bw[av];
                    if (oShape >= 0)
                    {
                        int k = b + oShape;
                        foreach (var sh in o.shapes) foreach (var f in sh.frames) { f.dv[i] = new Vector3(attr[k], attr[k + 1], attr[k + 2]); f.dn[i] = new Vector3(attr[k + 3], attr[k + 4], attr[k + 5]); f.dt[i] = new Vector3(attr[k + 6], attr[k + 7], attr[k + 8]); k += 9; }
                    }
                }
                return o;
            }
        }
    }
}
