using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HienaDev.DevTools
{
    /// <summary>What the Mesh Decimate window does with its results: saving meshes, swapping them in, making LODs
    /// (on scene objects, or on copies of prefabs and models).</summary>
    public static class MeshDecimateActions
    {
        /// <summary>Every mesh under a source (a scene object, a prefab/model asset or a mesh asset), with a renderer that shows it.</summary>
        public static void Collect(Object src, List<(Mesh mesh, Renderer look)> into)
        {
            void Add(Mesh m, Renderer r) { if (!m) return; int i = into.FindIndex(x => x.mesh == m); if (i < 0) into.Add((m, r)); else if (!into[i].look && r) into[i] = (m, r); }
            if (src is Mesh mesh) { Add(mesh, null); return; }
            var go = src as GameObject ?? (src as Component)?.gameObject; if (!go) return;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) Add(mf.sharedMesh, mf.GetComponent<Renderer>());
            foreach (var sk in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(sk.sharedMesh, sk);
        }

        public static bool IsSceneObject(Object o) { var go = o as GameObject ?? (o as Component)?.gameObject; return go && !EditorUtility.IsPersistent(go); }

        /// <summary>Runs many decimations at once on other threads, with a progress bar. null where cancelled.</summary>
        public static MeshDecimator.Result[] RunAll(IList<(MeshSnapshot snap, DecimateSettings s)> jobs, string title)
        {
            var res = new MeshDecimator.Result[jobs.Count]; var prog = new float[jobs.Count]; var cts = new CancellationTokenSource();
            var tasks = new Task[jobs.Count];
            for (int i = 0; i < jobs.Count; i++) { int k = i; tasks[i] = Task.Run(() => res[k] = MeshDecimator.Run(jobs[k].snap, jobs[k].s, p => prog[k] = p, cts.Token)); }
            try
            {
                while (!tasks.All(t => t.IsCompleted))
                {
                    float p = jobs.Count == 0 ? 1 : prog.Sum() / jobs.Count;
                    if (EditorUtility.DisplayCancelableProgressBar(title, "Taking triangles out... " + tasks.Count(t => t.IsCompleted) + " of " + jobs.Count + " meshes done", p)) { cts.Cancel(); break; }
                    Thread.Sleep(30);
                }
                Task.WaitAll(tasks.Where(t => !t.IsFaulted).ToArray(), 2000);
                foreach (var t in tasks) if (t.IsFaulted) Debug.LogException(t.Exception);
            }
            finally { EditorUtility.ClearProgressBar(); }
            return cts.IsCancellationRequested ? null : res;
        }

        public static string Folder(string folder)
        {
            folder = string.IsNullOrWhiteSpace(folder) ? "Assets/Decimated" : folder.Replace('\\', '/').TrimEnd('/');
            if (!folder.StartsWith("Assets")) folder = "Assets/" + folder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parts = folder.Split('/'); string at = parts[0];
                for (int i = 1; i < parts.Length; i++) { string next = at + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]); at = next; }
            }
            return folder;
        }

        static string Safe(string n) { foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_'); return string.IsNullOrWhiteSpace(n) ? "Mesh" : n.Trim(); }

        /// <summary>Saves a result as a mesh asset. A mesh saved before at the same place is updated in place, so
        /// whatever already uses it gets the new one.</summary>
        public static Mesh Save(Mesh source, MeshSnapshot result, string folder, string suffix) => Save(source.name, result, folder, suffix);
        public static Mesh Save(string name, MeshSnapshot result, string folder, string suffix)
        {
            folder = Folder(folder);
            string path = folder + "/" + Safe(name) + suffix + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { result.ToMesh(old); old.name = Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(old); return old; }
            var m = result.ToMesh(); m.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(m, path); return m;
        }

        /// <summary>Points every mesh filter, skinned renderer and mesh collider under root at the new meshes (Undo works).</summary>
        public static int Swap(GameObject root, Dictionary<Mesh, Mesh> map)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && map.TryGetValue(mf.sharedMesh, out var m)) { Undo.RecordObject(mf, "Decimate"); mf.sharedMesh = m; n++; }
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sk.sharedMesh && map.TryGetValue(sk.sharedMesh, out var m)) { Undo.RecordObject(sk, "Decimate"); sk.sharedMesh = m; n++; }
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (mc.sharedMesh && map.TryGetValue(mc.sharedMesh, out var m)) { Undo.RecordObject(mc, "Decimate"); mc.sharedMesh = m; }
            return n;
        }

        public const string LodTag = " LOD";

        /// <summary>Adds an LOD Group to root: the meshes as they are for LOD 0, then one copy of each renderer per level
        /// with the decimated meshes (lods[level][mesh]). Copies made by an earlier run are replaced.</summary>
        public static LODGroup MakeLods(GameObject root, List<Dictionary<Mesh, Mesh>> lods)
        {
            var renderers = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains(LodTag) && r.transform.parent && r.name.StartsWith(r.transform.parent.name + LodTag)) continue; // one made earlier
                var m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (m && lods.Count > 0 && lods[0].ContainsKey(m)) renderers.Add(r);
            }
            // take away the copies from before
            foreach (var r in renderers) for (int i = r.transform.childCount - 1; i >= 0; i--) { var c = r.transform.GetChild(i); if (c.name.StartsWith(r.name + LodTag)) Undo.DestroyObjectImmediate(c.gameObject); }
            var levels = new LOD[lods.Count + 1];
            levels[0] = new LOD(0, renderers.ToArray());
            for (int l = 0; l < lods.Count; l++)
            {
                var made = new List<Renderer>();
                foreach (var r in renderers)
                {
                    var go = new GameObject(r.name + LodTag + (l + 1)); Undo.RegisterCreatedObjectUndo(go, "Make LODs");
                    go.transform.SetParent(r.transform, false); go.layer = r.gameObject.layer; go.isStatic = r.gameObject.isStatic;
                    if (r is SkinnedMeshRenderer sk)
                    {
                        var c = go.AddComponent<SkinnedMeshRenderer>(); c.sharedMesh = lods[l][sk.sharedMesh]; c.bones = sk.bones; c.rootBone = sk.rootBone; c.quality = sk.quality; c.updateWhenOffscreen = sk.updateWhenOffscreen; c.localBounds = sk.localBounds;
                        c.sharedMaterials = sk.sharedMaterials; c.shadowCastingMode = sk.shadowCastingMode; c.receiveShadows = sk.receiveShadows;
                        for (int b = 0; b < sk.sharedMesh.blendShapeCount && b < c.sharedMesh.blendShapeCount; b++) c.SetBlendShapeWeight(b, sk.GetBlendShapeWeight(b));
                        made.Add(c);
                    }
                    else
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = lods[l][r.GetComponent<MeshFilter>().sharedMesh];
                        var c = go.AddComponent<MeshRenderer>(); c.sharedMaterials = r.sharedMaterials; c.shadowCastingMode = r.shadowCastingMode; c.receiveShadows = r.receiveShadows; c.lightProbeUsage = r.lightProbeUsage; c.reflectionProbeUsage = r.reflectionProbeUsage;
                        made.Add(c);
                    }
                }
                levels[l + 1] = new LOD(0, made.ToArray());
            }
            // how big on screen each level stops at
            for (int i = 0; i < levels.Length; i++) levels[i].screenRelativeTransitionHeight = i == levels.Length - 1 ? 0.01f : 0.5f * Mathf.Pow(0.5f, i);
            var g = root.GetComponent<LODGroup>(); if (!g) g = Undo.AddComponent<LODGroup>(root); else Undo.RecordObject(g, "Make LODs");
            g.SetLODs(levels); g.RecalculateBounds();
            return g;
        }

        /// <summary>For prefabs and models in the Project: makes a copy, changes it and saves it as a new prefab next to the meshes.</summary>
        public static GameObject SaveCopy(GameObject asset, string folder, string suffix, Action<GameObject> change)
        {
            folder = Folder(folder);
            var inst = (GameObject)Object.Instantiate(asset); inst.name = asset.name;
            try
            {
                change(inst);
                string path = folder + "/" + Safe(asset.name) + suffix + ".prefab";
                return PrefabUtility.SaveAsPrefabAsset(inst, path);
            }
            finally { Object.DestroyImmediate(inst); }
        }

        public static string Pct(float r) => (r * 100 >= 10 || Mathf.Approximately(r * 100, Mathf.Round(r * 100)) ? (r * 100).ToString("0") : (r * 100).ToString("0.#")) + "%";

        /// <summary>The name added to saved meshes for these settings.</summary>
        public static string Suffix(DecimateSettings s) => s.mode == DecimateMode.Planar ? " flat " + s.planarAngle.ToString("0.#") : s.targetTriangles > 0 ? " " + s.targetTriangles + " tris" : " " + Pct(s.ratio);
    }
}
