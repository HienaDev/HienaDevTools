using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Mesh fixes that would otherwise need Blender. Each takes a mesh copy and gives a new one back, so they
    /// can run on any thread and on models imported without Read/Write.</summary>
    public static class MeshOps
    {
        // ---------------------------------------------------------------- moving the whole mesh
        /// <summary>Moves, turns or sizes the mesh itself (normals, tangents, blend shapes and bind poses follow).</summary>
        public static MeshSnapshot Transform(MeshSnapshot s, Matrix4x4 m)
        {
            var e = EditMesh.From(s); var nm = m.inverse.transpose; bool flip = m.determinant < 0;
            for (int i = 0; i < e.Count; i++)
            {
                e.pos[i] = m.MultiplyPoint3x4(e.pos[i]);
                if (e.nrm != null) e.nrm[i] = nm.MultiplyVector(e.nrm[i]).normalized;
                if (e.tan != null) { var t = e.tan[i]; var d = m.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized; e.tan[i] = new Vector4(d.x, d.y, d.z, flip ? -t.w : t.w); }
            }
            foreach (var f in e.frames) for (int i = 0; i < e.Count; i++) { f.dv[i] = m.MultiplyVector(f.dv[i]); f.dn[i] = nm.MultiplyVector(f.dn[i]); f.dt[i] = m.MultiplyVector(f.dt[i]); }
            if (e.bindposes != null && e.bindposes.Length > 0) { var inv = m.inverse; e.bindposes = e.bindposes.Select(b => b * inv).ToArray(); }
            if (flip) Reverse(e);
            return e.ToSnapshot();
        }
        static void Reverse(EditMesh e) { for (int s = 0; s < e.idx.Count; s++) if (e.topo[s] == MeshTopology.Triangles) { var l = e.idx[s]; for (int i = 0; i + 2 < l.Count; i += 3) { int t = l[i + 1]; l[i + 1] = l[i + 2]; l[i + 2] = t; } } }

        /// <summary>Where a pivot spot is in the mesh: 0 bottom centre, 1 centre, 2 the lowest corner, 3 the origin.</summary>
        /// <summary>A spot in the mesh's box: (0,0,0) is the left-bottom-back corner, (1,1,1) the right-top-front one.</summary>
        public static Vector3 PivotSpot(MeshSnapshot s, Vector3 share) { var b = EditMesh.From(s).Bounds(); return b.min + Vector3.Scale(b.size, share); }
        public static Vector3 PivotSpot(MeshSnapshot s, int where)
        {
            var e = EditMesh.From(s); var b = e.Bounds();
            return where == 0 ? new Vector3(b.center.x, b.min.y, b.center.z) : where == 1 ? b.center : where == 2 ? b.min : Vector3.zero;
        }

        // ---------------------------------------------------------------- normals
        public static MeshSnapshot FlipFaces(MeshSnapshot s)
        {
            var e = EditMesh.From(s); Reverse(e);
            if (e.nrm != null) for (int i = 0; i < e.Count; i++) e.nrm[i] = -e.nrm[i];
            foreach (var f in e.frames) for (int i = 0; i < e.Count; i++) f.dn[i] = -f.dn[i];
            return e.ToSnapshot();
        }

        /// <summary>New normals: faces meeting at less than <paramref name="angle"/> degrees are smooth, sharper ones get a hard edge.
        /// 0 is fully flat (faceted), 180 fully smooth.</summary>
        public static MeshSnapshot Normals(MeshSnapshot s, float angle)
        {
            var e = EditMesh.From(s); if (e.nrm == null) e.nrm = Enumerable.Repeat(Vector3.up, e.Count).ToList(); var o = EditMesh.LikeOf(e);
            float cos = Mathf.Cos(Mathf.Clamp(angle, 0, 180) * Mathf.Deg2Rad) - 1e-5f;
            // faces by the point they touch
            var pid = new Dictionary<Vector3, int>(); var faces = new List<List<int>>(); var fn = new List<Vector3>(); var fu = new List<Vector3>();
            var tris = new List<(int s, int i)>();
            for (int sm = 0; sm < e.idx.Count; sm++) if (e.topo[sm] == MeshTopology.Triangles) for (int i = 0; i + 2 < e.idx[sm].Count; i += 3) tris.Add((sm, i));
            int P(Vector3 p) { if (!pid.TryGetValue(p, out int k)) { k = faces.Count; pid[p] = k; faces.Add(new List<int>()); } return k; }
            for (int t = 0; t < tris.Count; t++)
            {
                var l = e.idx[tris[t].s]; int i = tris[t].i; var a = e.pos[l[i]]; var n = Vector3.Cross(e.pos[l[i + 1]] - a, e.pos[l[i + 2]] - a);
                fn.Add(n); fu.Add(n.normalized); for (int k = 0; k < 3; k++) faces[P(e.pos[l[i + k]])].Add(t);
            }
            var made = new Dictionary<(int, Vector3Int), int>();
            for (int t = 0; t < tris.Count; t++)
            {
                var l = e.idx[tris[t].s]; int i = tris[t].i;
                for (int k = 0; k < 3; k++)
                {
                    int v = l[i + k]; var n = Vector3.zero;
                    foreach (int f in faces[pid[e.pos[v]]]) if (Vector3.Dot(fu[f], fu[t]) >= cos) n += fn[f];
                    n = n.sqrMagnitude > 0 ? n.normalized : fu[t];
                    var key = (v, Vector3Int.RoundToInt(n * 2000));
                    if (!made.TryGetValue(key, out int nv)) { nv = o.Copy(e, v); o.nrm[nv] = n; made[key] = nv; }
                    o.idx[tris[t].s].Add(nv);
                }
            }
            for (int sm = 0; sm < e.idx.Count; sm++) if (e.topo[sm] != MeshTopology.Triangles) foreach (int v in e.idx[sm]) o.idx[sm].Add(o.Copy(e, v));
            return o.ToSnapshot();
        }

        /// <summary>Both sides of every face show (a flipped copy of each face is added).</summary>
        public static MeshSnapshot DoubleSided(MeshSnapshot s)
        {
            var e = EditMesh.From(s); int n = e.Count; var map = new int[n];
            for (int i = 0; i < n; i++) { map[i] = e.Copy(e, i); if (e.nrm != null) e.nrm[map[i]] = -e.nrm[i]; }
            for (int sm = 0; sm < e.idx.Count; sm++)
            {
                if (e.topo[sm] != MeshTopology.Triangles) continue; var l = e.idx[sm]; int c = l.Count;
                for (int i = 0; i + 2 < c; i += 3) { l.Add(map[l[i]]); l.Add(map[l[i + 2]]); l.Add(map[l[i + 1]]); }
            }
            return e.ToSnapshot();
        }

        // ---------------------------------------------------------------- cleaning
        /// <summary>Points closer than <paramref name="dist"/> move together, then vertices that are now the same become one,
        /// and squashed triangles and unused vertices go.</summary>
        public static MeshSnapshot Weld(MeshSnapshot s, float dist)
        {
            var e = EditMesh.From(s);
            if (dist > 0)
            {
                var grid = new Dictionary<Vector3Int, List<int>>(); var rep = new List<int>(); float d2 = dist * dist;
                for (int i = 0; i < e.Count; i++)
                {
                    var p = e.pos[i]; var c = Vector3Int.FloorToInt(p / dist); int found = -1;
                    for (int x = -1; x <= 1 && found < 0; x++) for (int y = -1; y <= 1 && found < 0; y++) for (int z = -1; z <= 1 && found < 0; z++)
                                if (grid.TryGetValue(c + new Vector3Int(x, y, z), out var l)) foreach (int r in l) if ((e.pos[r] - p).sqrMagnitude <= d2) { found = r; break; }
                    if (found < 0) { if (!grid.TryGetValue(c, out var l)) grid[c] = l = new List<int>(); l.Add(i); }
                    else e.pos[i] = e.pos[found];
                }
            }
            return Clean(e);
        }

        /// <summary>Same vertices joined, flat triangles and unused vertices taken out.</summary>
        public static MeshSnapshot Clean(MeshSnapshot s) => Clean(EditMesh.From(s));
        static MeshSnapshot Clean(EditMesh e)
        {
            var b = e.Bounds(); float eps = b.size.sqrMagnitude * 1e-14f;
            var byPos = new Dictionary<Vector3, List<int>>(); var same = new int[e.Count];
            for (int i = 0; i < e.Count; i++)
            {
                same[i] = i; if (!byPos.TryGetValue(e.pos[i], out var l)) byPos[e.pos[i]] = l = new List<int>();
                foreach (int j in l) if (e.Same(i, j)) { same[i] = j; break; }
                if (same[i] == i) l.Add(i);
            }
            var o = EditMesh.LikeOf(e); var map = new Dictionary<int, int>();
            int Use(int v) { v = same[v]; if (!map.TryGetValue(v, out int n)) { n = o.Copy(e, v); map[v] = n; } return n; }
            for (int sm = 0; sm < e.idx.Count; sm++)
            {
                var l = e.idx[sm];
                if (e.topo[sm] != MeshTopology.Triangles) { foreach (int v in l) o.idx[sm].Add(Use(v)); continue; }
                for (int i = 0; i + 2 < l.Count; i += 3)
                {
                    int a = same[l[i]], c = same[l[i + 1]], d = same[l[i + 2]];
                    if (e.pos[a] == e.pos[c] || e.pos[c] == e.pos[d] || e.pos[a] == e.pos[d]) continue;
                    if (Vector3.Cross(e.pos[c] - e.pos[a], e.pos[d] - e.pos[a]).sqrMagnitude <= eps) continue;
                    o.idx[sm].Add(Use(a)); o.idx[sm].Add(Use(c)); o.idx[sm].Add(Use(d));
                }
            }
            return o.ToSnapshot();
        }

        /// <summary>Which separate piece each triangle belongs to (pieces touch through shared points).</summary>
        public static int[] Parts(MeshSnapshot s, out int count, out List<(int sm, int i)> tris)
        {
            var e = s; tris = new List<(int, int)>();
            for (int sm = 0; sm < e.indices.Length; sm++) if (e.topologies[sm] == MeshTopology.Triangles) for (int i = 0; i + 2 < e.indices[sm].Length; i += 3) tris.Add((sm, i));
            var pid = new Dictionary<Vector3, int>(); var up = new List<int>();
            int Find(int x) { while (up[x] != x) { up[x] = up[up[x]]; x = up[x]; } return x; }
            int P(Vector3 p) { if (!pid.TryGetValue(p, out int k)) { k = up.Count; pid[p] = k; up.Add(k); } return k; }
            foreach (var (sm, i) in tris) { var l = e.indices[sm]; int a = Find(P(e.positions[l[i]])), b = Find(P(e.positions[l[i + 1]])), c = Find(P(e.positions[l[i + 2]])); up[b] = a; up[Find(c)] = a; }
            var ids = new Dictionary<int, int>(); var part = new int[tris.Count];
            for (int t = 0; t < tris.Count; t++) { var (sm, i) = tris[t]; int r = Find(pid[e.positions[e.indices[sm][i]]]); if (!ids.TryGetValue(r, out int id)) ids[r] = id = ids.Count; part[t] = id; }
            count = ids.Count; return part;
        }

        /// <summary>Keeps only the triangles <paramref name="keep"/> says yes to (number in the list of all triangles).</summary>
        public static MeshSnapshot KeepTriangles(MeshSnapshot s, Func<int, bool> keep)
        {
            var e = EditMesh.From(s); var o = EditMesh.LikeOf(e); var map = new Dictionary<int, int>();
            int Use(int v) { if (!map.TryGetValue(v, out int n)) { n = o.Copy(e, v); map[v] = n; } return n; }
            int t = 0;
            for (int sm = 0; sm < e.idx.Count; sm++)
            {
                var l = e.idx[sm];
                if (e.topo[sm] != MeshTopology.Triangles) { foreach (int v in l) o.idx[sm].Add(Use(v)); continue; }
                for (int i = 0; i + 2 < l.Count; i += 3, t++) if (keep(t)) { o.idx[sm].Add(Use(l[i])); o.idx[sm].Add(Use(l[i + 1])); o.idx[sm].Add(Use(l[i + 2])); }
            }
            return o.ToSnapshot();
        }

        /// <summary>Takes away loose pieces smaller than <paramref name="share"/> of the whole model's size.</summary>
        public static MeshSnapshot RemoveSmallParts(MeshSnapshot s, float share, out int removed)
        {
            var part = Parts(s, out int count, out var tris); var size = new Bounds[count]; var has = new bool[count];
            for (int t = 0; t < tris.Count; t++)
            {
                var (sm, i) = tris[t]; int p = part[t];
                for (int k = 0; k < 3; k++) { var v = s.positions[s.indices[sm][i + k]]; if (!has[p]) { size[p] = new Bounds(v, Vector3.zero); has[p] = true; } else size[p].Encapsulate(v); }
            }
            float whole = EditMesh.From(s).Bounds().size.magnitude; var drop = new bool[count]; removed = 0;
            for (int p = 0; p < count; p++) if (size[p].size.magnitude < whole * share) { drop[p] = true; removed++; }
            if (removed == count) { removed = 0; return s; } // never take everything
            return KeepTriangles(s, t => !drop[part[t]]);
        }

        // ---------------------------------------------------------------- mirror
        public static MeshSnapshot Mirror(MeshSnapshot s, int axis)
        {
            var sc = Vector3.one; sc[axis] = -1; return Transform(s, Matrix4x4.Scale(sc));
        }

        /// <summary>Makes both halves the same: the half on the + side (or the - side) is kept and copied over.</summary>
        public static MeshSnapshot Symmetrize(MeshSnapshot s, int axis, bool keepPlus)
        {
            var e = EditMesh.From(s); float sz = e.Bounds().size.magnitude, eps = sz * 1e-4f; float sign = keepPlus ? 1 : -1;
            // keep the faces on the chosen side; points on the middle line go exactly onto it
            var half = KeepTriangles(s, t => true); var h = EditMesh.From(half);
            var keep = new List<bool>(); int ti = 0;
            for (int sm = 0; sm < h.idx.Count; sm++) if (h.topo[sm] == MeshTopology.Triangles) for (int i = 0; i + 2 < h.idx[sm].Count; i += 3, ti++) { float c = (h.pos[h.idx[sm][i]][axis] + h.pos[h.idx[sm][i + 1]][axis] + h.pos[h.idx[sm][i + 2]][axis]) / 3f; keep.Add(c * sign >= -eps); }
            var kept = EditMesh.From(KeepTriangles(half, t => keep[t]));
            for (int i = 0; i < kept.Count; i++) if (Mathf.Abs(kept.pos[i][axis]) < eps) { var p = kept.pos[i]; p[axis] = 0; kept.pos[i] = p; }
            var mirrored = EditMesh.From(Mirror(kept.ToSnapshot(), axis));
            int n = kept.Count; for (int i = 0; i < mirrored.Count; i++) kept.Copy(mirrored, i);
            for (int sm = 0; sm < kept.idx.Count; sm++) foreach (int v in mirrored.idx[sm]) kept.idx[sm].Add(v + n);
            return Clean(kept); // joins the middle line
        }

        // ---------------------------------------------------------------- UVs
        /// <summary>Box projection: each face takes its texture from the side it faces most. <paramref name="tile"/> is
        /// the size (in the mesh's units) one repeat of the texture covers.</summary>
        public static MeshSnapshot BoxUV(MeshSnapshot s, float tile, int channel = 0)
        {
            var e = EditMesh.From(s); if (e.uv[channel] == null) { e.uv[channel] = Enumerable.Repeat(Vector4.zero, e.Count).ToList(); e.uvDims[channel] = 2; }
            var o = EditMesh.LikeOf(e);
            var made = new Dictionary<(int, int), int>(); tile = Mathf.Max(1e-5f, tile);
            for (int sm = 0; sm < e.idx.Count; sm++)
            {
                var l = e.idx[sm];
                if (e.topo[sm] != MeshTopology.Triangles) { foreach (int v in l) o.idx[sm].Add(o.Copy(e, v)); continue; }
                for (int i = 0; i + 2 < l.Count; i += 3)
                {
                    var n = Vector3.Cross(e.pos[l[i + 1]] - e.pos[l[i]], e.pos[l[i + 2]] - e.pos[l[i]]); var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                    int ax = a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2; int side = ax * 2 + (n[ax] < 0 ? 1 : 0);
                    for (int k = 0; k < 3; k++)
                    {
                        int v = l[i + k];
                        if (!made.TryGetValue((v, side), out int nv))
                        {
                            nv = o.Copy(e, v); var p = e.pos[v] / tile;
                            Vector2 u = ax == 0 ? new Vector2(n.x > 0 ? -p.z : p.z, p.y) : ax == 1 ? new Vector2(p.x, n.y > 0 ? p.z : -p.z) : new Vector2(n.z > 0 ? p.x : -p.x, p.y);
                            o.uv[channel][nv] = u; made[(v, side)] = nv;
                        }
                        o.idx[sm].Add(nv);
                    }
                }
            }
            return o.ToSnapshot();
        }

        public static MeshSnapshot ScaleUV(MeshSnapshot s, Vector2 scale, Vector2 move, int channel = 0)
        {
            var e = EditMesh.From(s); if (e.uv[channel] == null) return s;
            for (int i = 0; i < e.Count; i++) { var u = e.uv[channel][i]; e.uv[channel][i] = new Vector4(u.x * scale.x + move.x, u.y * scale.y + move.y, u.z, u.w); }
            return e.ToSnapshot();
        }

        /// <summary>Puts UV points on the corners of a texture's pixels (like the PS1, which only had whole texels).</summary>
        public static MeshSnapshot SnapUV(MeshSnapshot s, int width, int height, int channel = 0)
        {
            var e = EditMesh.From(s); if (e.uv[channel] == null) return s;
            for (int i = 0; i < e.Count; i++) { var u = e.uv[channel][i]; e.uv[channel][i] = new Vector4(Mathf.Round(u.x * width) / width, Mathf.Round(u.y * height) / height, u.z, u.w); }
            return e.ToSnapshot();
        }

        // ---------------------------------------------------------------- more triangles
        /// <summary>Cuts triangles with edges longer than <paramref name="maxEdge"/> into smaller ones (no cracks: a long
        /// edge is cut in the middle on both of its faces). Stops at <paramref name="maxTriangles"/>.</summary>
        public static MeshSnapshot SplitBig(MeshSnapshot s, float maxEdge, int maxTriangles = 500000)
        {
            var e = EditMesh.From(s); float m2 = maxEdge * maxEdge;
            for (int pass = 0; pass < 12; pass++)
            {
                int total = e.idx.Where((l, i) => e.topo[i] == MeshTopology.Triangles).Sum(l => l.Count / 3);
                var mid = new Dictionary<Vector3Int, int>(); bool any = false;
                // the middle of an edge, shared by both faces (found by position so it works across seams too)
                int Mid(int a, int b)
                {
                    var key = Vector3Int.RoundToInt((e.pos[a] + e.pos[b]) * 50000f);
                    // a seam has different vertices in one place: give each side its own (same position)
                    long ka = Math.Min(a, b), kb = Math.Max(a, b);
                    var k2 = new Vector3Int(key.x ^ (int)(ka * 73856093), key.y ^ (int)(kb * 19349663), key.z);
                    if (!mid.TryGetValue(k2, out int v)) { v = e.Lerp(e, a, b, 0.5f); mid[k2] = v; }
                    return v;
                }
                bool Long(int a, int b) => (e.pos[a] - e.pos[b]).sqrMagnitude > m2;
                for (int sm = 0; sm < e.idx.Count; sm++)
                {
                    if (e.topo[sm] != MeshTopology.Triangles) continue;
                    var l = e.idx[sm]; var n = new List<int>(l.Count);
                    for (int i = 0; i + 2 < l.Count; i += 3)
                    {
                        int a = l[i], b = l[i + 1], c = l[i + 2];
                        bool ab = Long(a, b), bc = Long(b, c), ca = Long(c, a); int k = (ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0);
                        if (k == 0 || total > maxTriangles) { n.Add(a); n.Add(b); n.Add(c); continue; }
                        any = true;
                        if (k == 3) { int x = Mid(a, b), y = Mid(b, c), z = Mid(c, a); n.AddRange(new[] { a, x, z, x, b, y, z, y, c, x, y, z }); total += 3; continue; }
                        // turn the triangle so the cut edges come first
                        for (int r = 0; r < 3 && !(k == 1 ? Long(a, b) : Long(a, b) && Long(b, c)); r++) { int t = a; a = b; b = c; c = t; }
                        if (k == 1) { int x = Mid(a, b); n.AddRange(new[] { a, x, c, x, b, c }); total += 1; }
                        else { int x = Mid(a, b), y = Mid(b, c); n.AddRange(new[] { x, b, y, a, x, c, x, y, c }); total += 2; }
                    }
                    e.idx[sm] = n;
                }
                if (!any) break;
            }
            return e.ToSnapshot();
        }

        // ---------------------------------------------------------------- joining
        /// <summary>Puts many meshes into one. Each part gives a matrix into the new mesh's space and which submesh
        /// (slot) each of its submeshes goes to.</summary>
        public static MeshSnapshot Join(IList<(MeshSnapshot mesh, Matrix4x4 m, int[] slotOf)> parts, int slots, string name)
        {
            var o = new EditMesh { name = name }; o.nrm = new List<Vector3>(); o.tan = parts.Any(p => p.mesh.tangents != null) ? new List<Vector4>() : null; o.col = parts.Any(p => p.mesh.colors != null) ? new List<Color>() : null;
            for (int c = 0; c < 8; c++) if (parts.Any(p => p.mesh.uvs[c] != null)) { o.uv[c] = new List<Vector4>(); o.uvDims[c] = parts.Max(p => p.mesh.uvDims[c]); }
            for (int i = 0; i < slots; i++) { o.idx.Add(new List<int>()); o.topo.Add(MeshTopology.Triangles); }
            foreach (var (mesh, m, slotOf) in parts)
            {
                var e = EditMesh.From(Transform(mesh, m)); int start = o.Count;
                for (int i = 0; i < e.Count; i++)
                {
                    o.pos.Add(e.pos[i]); o.nrm.Add(e.nrm != null ? e.nrm[i] : Vector3.up); o.tan?.Add(e.tan != null ? e.tan[i] : new Vector4(1, 0, 0, 1)); o.col?.Add(e.col != null ? e.col[i] : Color.white);
                    for (int c = 0; c < 8; c++) o.uv[c]?.Add(e.uv[c] != null ? e.uv[c][i] : Vector4.zero);
                }
                for (int sm = 0; sm < e.idx.Count; sm++) if (e.topo[sm] == MeshTopology.Triangles && sm < slotOf.Length && slotOf[sm] >= 0) foreach (int v in e.idx[sm]) o.idx[slotOf[sm]].Add(v + start);
            }
            return o.ToSnapshot();
        }

        public static int Triangles(MeshSnapshot s) => s.TriangleCount;
    }
}
