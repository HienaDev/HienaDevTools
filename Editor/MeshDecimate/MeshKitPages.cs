using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HienaDev.DevTools
{
    /// <summary>The Mesh Kit's other pages: Fix, Shape, UVs, Objects and PSX. Every change saves a new mesh (or updates
    /// the one the kit saved before) and Ctrl+Z puts the old one back.</summary>
    public partial class MeshDecimateWindow
    {
        public enum Page { Decimate, Fix, Shape, UVs, Objects, PSX }
        [SerializeField] public Page page;
        [SerializeField] bool onlyShown;
        [SerializeField] int pivotAt;
        [SerializeField] float height = 1.8f, smoothAngle = 40f, weldDist = 0.001f, smallShare = 0.02f, maxEdge = 1f, boxTile = 1f;
        [SerializeField] Vector2 uvScale = Vector2.one; [SerializeField] int snapSize = 128, budget = 500, symAxis; [SerializeField] bool symPlus = true;
        [SerializeField] MeshKitActions.ColliderKind colKind = MeshKitActions.ColliderKind.Convex; [SerializeField] float colShare = 0.2f; [SerializeField] bool colReplace = true;
        [SerializeField] MeshKitActions.BakeSettings bake = new MeshKitActions.BakeSettings();
        Vector2 pageScroll;

        void PagePills()
        {
            if (Pill("Decimate", page == Page.Decimate, "Fewer triangles, LODs")) page = Page.Decimate;
            if (Pill("Fix", page == Page.Fix, "Pivot, size, turning, normals and cleaning up")) page = Page.Fix;
            if (Pill("Shape", page == Page.Shape, "Mirror, make both halves the same, cut big triangles")) page = Page.Shape;
            if (Pill("UVs", page == Page.UVs, "Box mapping, scaling and snapping UVs to pixels")) page = Page.UVs;
            if (Pill("Objects", page == Page.Objects, "Join, split, colliders and export (works on objects in the scene)")) page = Page.Objects;
            if (Pill("PSX", page == Page.PSX, "Triangle budget and light baked into vertex colours, like the PS1")) page = Page.PSX;
        }

        void Head(string t) { GUILayout.Space(4); GUILayout.Label(t, sHead); }
        bool Btn(string t, string tip, float w = 0) => w > 0 ? GUILayout.Button(new GUIContent(t, tip), sPill, GUILayout.Width(w)) : GUILayout.Button(new GUIContent(t, tip), sPill);

        void PageRight(Rect r)
        {
            EditorGUI.DrawRect(r, Bg);
            GUILayout.BeginArea(r); pageScroll = GUILayout.BeginScrollView(pageScroll);
            GUI.enabled = items.Count > 0;
            if (page != Page.Objects && page != Page.PSX)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28)); GUILayout.Space(8); GUILayout.Label("Change", sHint, GUILayout.Width(44));
                if (Pill("All on the list", !onlyShown)) onlyShown = false; if (Pill("Only the shown one", onlyShown)) onlyShown = true; GUILayout.EndHorizontal();
            }
            if (page == Page.Fix) FixPage(); else if (page == Page.Shape) ShapePage(); else if (page == Page.UVs) UvPage(); else if (page == Page.Objects) ObjectsPage(); else PsxPage();
            GUI.enabled = true;
            GUILayout.Space(4);
            GUILayout.BeginVertical(sCard); GUILayout.Label("SAVE INTO", sHead); folder = EditorGUILayout.TextField(folder); GUILayout.Label("Ctrl+Z undoes any change.", sHint); GUILayout.EndVertical();
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- pages
        void FixPage()
        {
            GUILayout.BeginVertical(sCard);
            Head("PIVOT");
            GUILayout.BeginHorizontal(); if (Pill("Bottom", pivotAt == 0)) pivotAt = 0; if (Pill("Middle", pivotAt == 1)) pivotAt = 1; if (Pill("Corner", pivotAt == 2)) pivotAt = 2; GUILayout.EndHorizontal();
            if (Btn("Move the pivot there", "The model stays where it is in the scene; only its pivot moves")) later.Add(() => Pivot(pivotAt));
            Head("SIZE");
            GUILayout.BeginHorizontal(); GUILayout.Label("Make it", sHint, GUILayout.Width(44)); height = Mathf.Max(0.001f, EditorGUILayout.FloatField(height, GUILayout.Width(56))); GUILayout.Label("m tall", sHint, GUILayout.Width(40));
            if (Btn("Do it", "Scales the mesh so it is this tall in the scene")) later.Add(() => SetHeight(height)); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); foreach (var k in new[] { 0.01f, 0.1f, 10f, 100f }) if (Btn("x" + k, "Scale the mesh by " + k + " (fixes centimetre / metre mix-ups)")) { float kk = k; later.Add(() => Op("Size x" + kk, s => MeshOps.Transform(s, Matrix4x4.Scale(Vector3.one * kk)))); } GUILayout.EndHorizontal();
            Head("TURN");
            GUILayout.BeginHorizontal();
            if (Btn("Stand it up", "For models that come in lying on their back (Blender's -90° X)")) later.Add(() => Op("Stand up", s => MeshOps.Transform(s, Matrix4x4.Rotate(Quaternion.Euler(-90, 0, 0)))));
            if (Btn("Face the other way", "Turns it 180° around Y")) later.Add(() => Op("Turn around", s => MeshOps.Transform(s, Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)))));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var (n, e) in new[] { ("X +90", new Vector3(90, 0, 0)), ("Y +90", new Vector3(0, 90, 0)), ("Z +90", new Vector3(0, 0, 90)) }) if (Btn(n, "Turns the mesh 90°")) { var ee = e; later.Add(() => Op("Turn", s => MeshOps.Transform(s, Matrix4x4.Rotate(Quaternion.Euler(ee))))); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("NORMALS");
            GUILayout.BeginHorizontal();
            if (Btn("Flip inside out", "For faces you see from the wrong side")) later.Add(() => Op("Flip faces", MeshOps.FlipFaces));
            if (Btn("Both sides", "Faces show from the front and the back (leaves, cloth, cards)")) later.Add(() => Op("Double sided", MeshOps.DoubleSided));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Flat", "Every face its own flat shade (faceted, low-poly look)")) later.Add(() => Op("Flat shading", s => MeshOps.Normals(s, 0), true));
            if (Btn("Smooth", "Everything smooth")) later.Add(() => Op("Smooth shading", s => MeshOps.Normals(s, 180), true));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Smooth below", sHint, GUILayout.Width(76)); smoothAngle = Mathf.Round(GUILayout.HorizontalSlider(smoothAngle, 0, 180)); GUILayout.Label(smoothAngle + "°", sHint, GUILayout.Width(30)); GUILayout.EndHorizontal();
            if (Btn("Smooth by angle", "Edges sharper than the angle stay hard, the rest is smooth (Blender's Auto Smooth)")) later.Add(() => Op("Smooth by angle", s => MeshOps.Normals(s, smoothAngle), true));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("CLEAN UP");
            GUILayout.BeginHorizontal(); GUILayout.Label("Join points closer than", sHint, GUILayout.Width(124)); weldDist = Mathf.Max(0, EditorGUILayout.FloatField(weldDist, GUILayout.Width(56))); GUILayout.EndHorizontal();
            if (Btn("Join split points", "Glues points that sit (almost) in the same place, and removes squashed triangles and unused points")) later.Add(() => Op("Join points", s => MeshOps.Weld(s, weldDist)));
            GUILayout.BeginHorizontal(); GUILayout.Label("Pieces smaller than", sHint, GUILayout.Width(110)); smallShare = Mathf.Round(GUILayout.HorizontalSlider(smallShare * 100, 0.5f, 30) * 2) / 200f; GUILayout.Label(MeshDecimateActions.Pct(smallShare), sHint, GUILayout.Width(36)); GUILayout.EndHorizontal();
            if (Btn("Remove small loose pieces", "Takes away floating bits smaller than this share of the whole model")) later.Add(() => { int n = 0; Op("Remove small pieces", s => { var r = MeshOps.RemoveSmallParts(s, smallShare, out int k); n += k; return r; }); Say("Removed " + n + " small piece" + (n == 1 ? "" : "s") + "."); });
            if (Btn("Remove hidden faces", "Faces nobody can ever see: inside walls, under other parts, closed off inside the model")) later.Add(() => { int n = 0; Op("Remove hidden faces", s => { var r = MeshKitActions.RemoveHidden(s, out int k); n += k; return r; }); Say("Removed " + Num(n) + " hidden triangle" + (n == 1 ? "" : "s") + "."); });
            GUILayout.EndVertical();
        }

        void ShapePage()
        {
            GUILayout.BeginVertical(sCard);
            Head("MIRROR");
            GUILayout.BeginHorizontal(); foreach (var (n, a) in new[] { ("Left-right (X)", 0), ("Up-down (Y)", 1), ("Front-back (Z)", 2) }) if (Btn(n, "Flips the mesh")) { int aa = a; later.Add(() => Op("Mirror", s => MeshOps.Mirror(s, aa))); } GUILayout.EndHorizontal();
            Head("MAKE BOTH HALVES THE SAME");
            GUILayout.BeginHorizontal(); foreach (var (n, a) in new[] { ("X", 0), ("Y", 1), ("Z", 2) }) if (Pill(n, symAxis == a, "", 34)) symAxis = a; GUILayout.Space(8); if (Pill("keep +", symPlus)) symPlus = true; if (Pill("keep -", !symPlus)) symPlus = false; GUILayout.EndHorizontal();
            if (Btn("Symmetrize", "Keeps one half and copies it over to the other side (the middle is the mesh's 0)")) later.Add(() => Op("Symmetrize", s => MeshOps.Symmetrize(s, symAxis, symPlus), true));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("CUT UP BIG TRIANGLES");
            GUILayout.Label("Big faces warp textures on the PS1 look and light badly with vertex lighting. This cuts every edge longer than:", sHint);
            GUILayout.BeginHorizontal(); maxEdge = Mathf.Max(0.01f, EditorGUILayout.FloatField(maxEdge, GUILayout.Width(56))); GUILayout.Label("units", sHint, GUILayout.Width(36));
            foreach (var k in new[] { 0.25f, 0.5f, 1f, 2f }) if (Pill(k.ToString(), Mathf.Approximately(maxEdge, k), "", 36)) maxEdge = k; GUILayout.EndHorizontal();
            if (Btn("Cut them", "No cracks: an edge is cut on both of its faces")) later.Add(() => Op("Cut big triangles", s => MeshOps.SplitBig(s, maxEdge), true));
            GUILayout.EndVertical();
        }

        void UvPage()
        {
            GUILayout.BeginVertical(sCard);
            Head("BOX MAPPING");
            GUILayout.Label("New UVs from the sides each face looks at, so a texture repeats the same size everywhere (blockouts, walls, rocks).", sHint);
            GUILayout.BeginHorizontal(); GUILayout.Label("One repeat every", sHint, GUILayout.Width(96)); boxTile = Mathf.Max(0.001f, EditorGUILayout.FloatField(boxTile, GUILayout.Width(50))); GUILayout.Label("units", sHint); GUILayout.EndHorizontal();
            if (Btn("Box map it", "Replaces the first UV set")) later.Add(() => Op("Box mapping", s => MeshOps.BoxUV(s, boxTile), true));
            Head("SCALE");
            GUILayout.BeginHorizontal(); uvScale = EditorGUILayout.Vector2Field(GUIContent.none, uvScale); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Scale UVs", "Multiplies the UVs (2 = the texture repeats twice as often)")) later.Add(() => Op("Scale UVs", s => MeshOps.ScaleUV(s, uvScale, Vector2.zero)));
            if (Btn("Flip V", "Turns the texture upside down")) later.Add(() => Op("Flip V", s => MeshOps.ScaleUV(s, new Vector2(1, -1), new Vector2(0, 1))));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("SNAP TO PIXELS");
            GUILayout.Label("Puts UV points on pixel corners of a texture this big, like the PS1 (crisp pixels, no half-pixel smears).", sHint);
            GUILayout.BeginHorizontal(); foreach (var k in new[] { 32, 64, 128, 256, 512 }) if (Pill(k.ToString(), snapSize == k, "", 42)) snapSize = k; GUILayout.EndHorizontal();
            if (Btn("From the texture", "Uses the size of the shown model's first texture")) { var t = FirstTexture(); if (t) snapSize = Mathf.Max(t.width, t.height); else Say("The shown model has no texture."); }
            if (Btn("Snap them", "Moves each UV point to the nearest pixel corner")) later.Add(() => Op("Snap UVs", s => MeshOps.SnapUV(s, snapSize, snapSize)));
            GUILayout.EndVertical();
        }
        Texture FirstTexture()
        {
            var it = cur >= 0 && cur < items.Count ? items[cur] : null; if (it == null || !it.look) return null;
            foreach (var m in it.look.sharedMaterials) if (m) { var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null; if (t) return t; }
            return null;
        }

        void ObjectsPage()
        {
            GUILayout.BeginVertical(sCard);
            GUILayout.Label("These work on the objects in the scene on the list.", sHint);
            Head("JOIN AND SPLIT");
            if (Btn("Join into one object", "Every mesh under each object becomes one mesh (one slot per material). The old objects are hidden, not deleted.")) later.Add(() => Scene("Join meshes", g => MeshKitActions.Join(g, folder) ? 1 : 0, "joined"));
            GUILayout.BeginHorizontal();
            if (Btn("Split by material", "One child object per material")) later.Add(() => Scene("Split by material", g => SplitAll(g, false), "made"));
            if (Btn("Split loose pieces", "One child object per separate piece, each with its own pivot")) later.Add(() => Scene("Split pieces", g => SplitAll(g, true), "made"));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("COLLIDERS");
            GUILayout.BeginHorizontal();
            if (Pill("Box", colKind == MeshKitActions.ColliderKind.Box, "A box around it: the cheapest")) colKind = MeshKitActions.ColliderKind.Box;
            if (Pill("Wrapped", colKind == MeshKitActions.ColliderKind.Convex, "A convex hull, like shrink-wrap: works with Rigidbodies")) colKind = MeshKitActions.ColliderKind.Convex;
            if (Pill("Low poly", colKind == MeshKitActions.ColliderKind.LowPoly, "The real shape with fewer triangles: for static things")) colKind = MeshKitActions.ColliderKind.LowPoly;
            GUILayout.EndHorizontal();
            if (colKind == MeshKitActions.ColliderKind.LowPoly) { GUILayout.BeginHorizontal(); GUILayout.Label("Keep", sHint, GUILayout.Width(32)); colShare = Mathf.Round(GUILayout.HorizontalSlider(colShare * 100, 1, 100)) / 100f; GUILayout.Label(MeshDecimateActions.Pct(colShare), sHint, GUILayout.Width(36)); GUILayout.EndHorizontal(); }
            Toggle(ref colReplace, "Replace old colliders", "Takes away the colliders already there");
            if (Btn("Add colliders", "To every mesh under each object")) later.Add(() => Scene("Colliders", g => MeshKitActions.Colliders(g, colKind, colShare, colReplace, folder), "collider"));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("EXPORT");
            GUILayout.BeginHorizontal();
            if (Btn("OBJ...", "Writes the object (and everything under it) as an OBJ file with its materials")) later.Add(() => Export(false));
            GUI.enabled = items.Count > 0 && MeshKitActions.CanFbx;
            if (Btn("FBX...", MeshKitActions.CanFbx ? "Writes an FBX file (with Unity's FBX Exporter)" : "Add Unity's FBX Exporter package to export FBX")) later.Add(() => Export(true));
            GUI.enabled = items.Count > 0;
            GUILayout.EndHorizontal();
            if (!MeshKitActions.CanFbx) GUILayout.Label("FBX needs Unity's FBX Exporter package (Package Manager).", sHint);
            GUILayout.EndVertical();
        }
        int SplitAll(GameObject g, bool pieces) { int n = 0; foreach (var mf in g.GetComponentsInChildren<MeshFilter>(false).ToArray()) n += MeshKitActions.Split(mf.gameObject, pieces, folder).Count; return n; }

        void PsxPage()
        {
            GUILayout.BeginVertical(sCard);
            Head("TRIANGLE BUDGET");
            GUILayout.BeginHorizontal(); GUILayout.Label("Each mesh", sHint, GUILayout.Width(60)); budget = Mathf.Max(4, EditorGUILayout.IntField(budget, GUILayout.Width(60))); GUILayout.Label("triangles", sHint); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); foreach (var (n, b) in new[] { ("prop", 300), ("big prop", 800), ("character", 1500), ("room", 3000) }) if (Pill(n, budget == b, b + " triangles", 0)) budget = b; GUILayout.EndHorizontal();
            GUILayout.Space(4);
            int over = 0;
            foreach (var it in items)
            {
                if (!it.mesh) continue; int t = MeshTris(it.mesh); if (t > budget) over++;
                var row = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
                float f = Mathf.Clamp01(t / (float)(budget * 2)); var col = t <= budget ? new Color(0.35f, 0.8f, 0.45f) : t <= budget * 1.5f ? Accent : new Color(0.9f, 0.35f, 0.3f);
                EditorGUI.DrawRect(new Rect(row.x, row.y + 13, row.width, 3), Ink(0.1f)); EditorGUI.DrawRect(new Rect(row.x, row.y + 13, row.width * f, 3), col); EditorGUI.DrawRect(new Rect(row.x + row.width * 0.5f, row.y + 11, 1, 7), Ink(0.5f));
                GUI.Label(new Rect(row.x, row.y - 2, row.width - 70, 16), it.mesh.name, sRowSub); GUI.Label(new Rect(row.xMax - 70, row.y - 2, 70, 16), Num(t), new GUIStyle(sRowSub) { alignment = TextAnchor.UpperRight });
            }
            GUILayout.Space(4);
            GUILayout.Label(items.Count == 0 ? "" : over == 0 ? "Everything fits." : over + " over the budget.", sHint);
            GUI.enabled = over > 0;
            if (Big("Bring them under the budget", "Decimates every mesh above the budget down to it (the Keep settings of the Decimate page apply)", 28)) later.Add(() =>
            {
                var s = settings.Clone(); s.mode = DecimateMode.Collapse; s.targetTriangles = budget;
                Op("Under budget", sn => sn.TriangleCount <= budget ? null : MeshDecimator.Run(sn, s, null, default)?.mesh, false, false);
            });
            GUI.enabled = items.Count > 0;
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Head("BAKE LIGHT INTO VERTEX COLOURS");
            GUILayout.Label("Like the PS1: the scene's lights, ambient light, shadows and dark corners are stored in the mesh, then an unlit material shows them. Cheap, and works great with the PSX look.", sHint);
            GUILayout.BeginHorizontal(); Toggle(ref bake.shadows, "Shadows", "Other objects block the lights"); Toggle(ref bake.occlusion, "Dark corners", "Ambient occlusion"); GUILayout.EndHorizontal();
            if (bake.occlusion) { GUILayout.BeginHorizontal(); GUILayout.Label("Corner size", sHint, GUILayout.Width(70)); bake.occlusionSize = EditorGUILayout.FloatField(bake.occlusionSize, GUILayout.Width(44)); GUILayout.Label("strength", sHint, GUILayout.Width(50)); bake.occlusionStrength = GUILayout.HorizontalSlider(bake.occlusionStrength, 0, 1); GUILayout.EndHorizontal(); }
            GUILayout.BeginHorizontal(); GUILayout.Label("Brightness", sHint, GUILayout.Width(70)); bake.brightness = GUILayout.HorizontalSlider(bake.brightness, 0.2f, 3f); GUILayout.Label(bake.brightness.ToString("0.0"), sHint, GUILayout.Width(26)); GUILayout.EndHorizontal();
            Toggle(ref bake.keepColours, "Keep the old colours too", "Multiplies with the vertex colours already there");
            GUILayout.Label("Tip: cut up big triangles first (Shape page) so light and shadow have points to land on.", sHint);
            if (Big("Bake", "Each object gets its own lit mesh", 28)) later.Add(() => Scene("Bake light", g => MeshKitActions.BakeLight(g, bake, folder), "lit mesh"));
            if (Btn("Use materials that show it", "Copies of the materials with the 'HienaDev/Baked Vertex Light' shader (texture x baked light, optional PS1 texture warp)")) later.Add(() => Scene("Vertex lit materials", g => MeshKitActions.UseVertexLitMaterials(g, folder), "material slot"));
            GUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- doing it
        List<Mesh> Targets() => (onlyShown && Current ? new List<Mesh> { Current } : items.Where(i => i.mesh).Select(i => i.mesh).Distinct().ToList());

        /// <summary>Runs a change on the meshes (all on the list, or the shown one), saves them and swaps them in.
        /// An op that gives null leaves that mesh alone.</summary>
        public int Op(string what, Func<MeshSnapshot, MeshSnapshot> op, bool tangents = false, bool all = true)
        {
            var list = all ? Targets() : items.Where(i => i.mesh).Select(i => i.mesh).Distinct().ToList();
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName(what); int group = Undo.GetCurrentGroup();
            var map = new Dictionary<Mesh, Mesh>();
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i]; var sn = Snap(m); if (sn == null) continue;
                    EditorUtility.DisplayProgressBar("Mesh Kit", what + ": " + m.name, i / (float)Mathf.Max(1, list.Count));
                    var r = op(sn); if (r == null) continue;
                    var made = Keep(m, r); if (tangents && made.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) made.RecalculateTangents();
                    Forget(made); map[m] = made;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            SwapEverywhere(map);
            Undo.CollapseUndoOperations(group);
            Rebuild(); if (status == "" || EditorApplication.timeSinceStartup > statusUntil) Say(what + ": " + map.Count + " mesh" + (map.Count == 1 ? "" : "es") + " changed.");
            return map.Count;
        }

        /// <summary>Saves a changed mesh as a new asset ("name edit", "name edit 1"...). Never over the old one, so
        /// Ctrl+Z only has to swap meshes back (undoing mesh data itself can freeze Unity).</summary>
        Mesh Keep(Mesh source, MeshSnapshot r)
        {
            string name = System.Text.RegularExpressions.Regex.Replace(source.name, @" edit( \d+)?$", "");
            string path = AssetDatabase.GenerateUniqueAssetPath(MeshDecimateActions.Folder(folder) + "/" + name + " edit.asset");
            return MeshDecimateActions.Save(System.IO.Path.GetFileNameWithoutExtension(path), r, folder, "");
        }
        void Forget(Mesh m) { snaps.Remove(m); if (done.TryGetValue(m, out var d)) { if (d.shown) DestroyImmediate(d.shown); done.Remove(m); } if (wireOf.TryGetValue(m, out var w)) { if (w) DestroyImmediate(w); wireOf.Remove(m); } }

        void SwapEverywhere(Dictionary<Mesh, Mesh> map)
        {
            if (map.Count == 0) return;
            for (int i = 0; i < sources.Count; i++)
            {
                if (!(sources[i] is GameObject go)) continue;
                if (MeshDecimateActions.IsSceneObject(go)) MeshDecimateActions.Swap(go, map);
                else sources[i] = MeshDecimateActions.SaveCopy(go, folder, " edit", c => MeshDecimateActions.Swap(c, map)); // a prefab or model: a changed copy (later changes go on the copy)
            }
            for (int i = 0; i < sources.Count; i++) if (sources[i] is Mesh m && map.TryGetValue(m, out var n)) sources[i] = n;
        }

        void Pivot(int where)
        {
            var spots = new Dictionary<Mesh, Vector3>();
            foreach (var m in Targets()) { var sn = Snap(m); if (sn != null) spots[m] = MeshOps.PivotSpot(sn, where); }
            // where every object using the mesh is, before
            var moves = new List<(Transform t, Vector3 spot, Vector3[] kids)>();
            foreach (var src in sources) if (src is GameObject go && MeshDecimateActions.IsSceneObject(go))
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && spots.TryGetValue(mf.sharedMesh, out var sp)) moves.Add((mf.transform, sp, Enumerable.Range(0, mf.transform.childCount).Select(k => mf.transform.GetChild(k).position).ToArray()));
            Op("Move pivot", s => { var sp = MeshOps.PivotSpot(s, where); return MeshOps.Transform(s, Matrix4x4.Translate(-sp)); });
            foreach (var (t, sp, kids) in moves)
            {
                Undo.RecordObject(t, "Move pivot"); t.position += t.TransformVector(sp);
                for (int k = 0; k < t.childCount && k < kids.Length; k++) { Undo.RecordObject(t.GetChild(k), "Move pivot"); t.GetChild(k).position = kids[k]; }
            }
        }

        void SetHeight(float h)
        {
            var sizes = new Dictionary<Mesh, float>();
            foreach (var src in sources) if (src is GameObject go) foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && !sizes.ContainsKey(mf.sharedMesh)) sizes[mf.sharedMesh] = Mathf.Abs(mf.transform.lossyScale.y);
            var meshes = Targets(); var k = new Dictionary<string, float>();
            Op("Set height", s =>
            {
                var b = EditMesh.From(s).Bounds(); var m = meshes.FirstOrDefault(x => x.name == s.name); float sc = m && sizes.TryGetValue(m, out var ls) && ls > 0 ? ls : 1;
                float f = b.size.y > 1e-6f ? h / (b.size.y * sc) : 1; return MeshOps.Transform(s, Matrix4x4.Scale(Vector3.one * f));
            });
        }

        /// <summary>Runs a scene job on every scene object on the list.</summary>
        void Scene(string what, Func<GameObject, int> job, string word)
        {
            var gos = sources.OfType<GameObject>().Where(MeshDecimateActions.IsSceneObject).ToList();
            if (gos.Count == 0) { Say("This works on objects in the scene: put some on the list."); return; }
            Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName(what); int group = Undo.GetCurrentGroup(); int n = 0;
            try { for (int i = 0; i < gos.Count; i++) { EditorUtility.DisplayProgressBar("Mesh Kit", what + ": " + gos[i].name, i / (float)gos.Count); n += job(gos[i]); } }
            finally { EditorUtility.ClearProgressBar(); }
            Undo.CollapseUndoOperations(group); AssetDatabase.SaveAssets();
            foreach (var m in items.Select(x => x.mesh).ToList()) if (m) Forget(m);
            Rebuild(); Say(what + ": " + n + " " + word + (n == 1 ? "" : "s") + ".");
        }

        void Export(bool fbx)
        {
            var go = sources.OfType<GameObject>().FirstOrDefault(MeshDecimateActions.IsSceneObject) ?? sources.OfType<GameObject>().FirstOrDefault();
            if (!go) { Say("Put an object on the list first."); return; }
            var path = EditorUtility.SaveFilePanel("Export " + go.name, "", go.name, fbx ? "fbx" : "obj"); if (string.IsNullOrEmpty(path)) return;
            if (fbx) Say(MeshKitActions.ExportFbx(go, path) ? "Saved " + path : "The FBX Exporter could not save it.");
            else { MeshKitActions.ExportObj(go, path); Say("Saved " + path); }
            if (path.Replace('\\', '/').Contains(Application.dataPath)) AssetDatabase.Refresh();
        }
    }
}
