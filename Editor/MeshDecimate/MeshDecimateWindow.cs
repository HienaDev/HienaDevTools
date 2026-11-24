using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HienaDev.DevTools
{
    /// <summary>Mesh Decimate: fewer triangles for any model, like Blender's Decimate modifier. Pick models, see the
    /// result live (before / after / both, with wires), then swap it in, save the mesh or make LODs.</summary>
    public partial class MeshDecimateWindow : EditorWindow
    {
        [MenuItem("Tools/Mesh Kit", false, 160)]
        public static void Open() { var w = GetWindow<MeshDecimateWindow>(false, "Mesh Kit", true); w.minSize = new Vector2(900, 560); }

        [MenuItem("GameObject/Mesh Kit...", false, 21)]
        static void FromObjects() { Open(); GetWindow<MeshDecimateWindow>().UseSelection(); }
        [MenuItem("GameObject/Mesh Kit...", true)] static bool FromObjectsOk() => Selection.gameObjects.Length > 0;
        [MenuItem("Assets/Mesh Kit...", false, 2001)]
        static void FromAssets() { Open(); GetWindow<MeshDecimateWindow>().UseSelection(); }
        [MenuItem("Assets/Mesh Kit...", true)] static bool FromAssetsOk() => Selection.objects.Any(o => o is Mesh || o is GameObject);
        [MenuItem("CONTEXT/MeshFilter/Mesh Kit...")] static void FromFilter(MenuCommand c) { Open(); GetWindow<MeshDecimateWindow>().Add(((Component)c.context).gameObject, true); }
        [MenuItem("CONTEXT/SkinnedMeshRenderer/Mesh Kit...")] static void FromSkin(MenuCommand c) { Open(); GetWindow<MeshDecimateWindow>().Add(((Component)c.context).gameObject, true); }

        // ---------------------------------------------------------------- state
        [SerializeField] public DecimateSettings settings = new DecimateSettings();
        [SerializeField] List<Object> sources = new List<Object>();
        [SerializeField] int cur;
        [SerializeField] int view = 2; // 0 before, 1 after, 2 both
        [SerializeField] bool wires = true;
        [SerializeField] string folder = "Assets/Decimated";
        [SerializeField] int lodCount = 3;
        [SerializeField] float[] lodRatios = { 0.5f, 0.25f, 0.1f, 0.05f };
        [SerializeField] float yaw = 30, pitch = 18, zoom = 1;

        class Item { public Mesh mesh; public Renderer look; }
        class Done { public DecimateSettings s; public MeshDecimator.Result r; public Mesh shown; }
        readonly List<Item> items = new List<Item>();
        readonly Dictionary<Mesh, MeshSnapshot> snaps = new Dictionary<Mesh, MeshSnapshot>();
        readonly Dictionary<Mesh, Done> done = new Dictionary<Mesh, Done>();
        readonly Dictionary<Mesh, Mesh> wireOf = new Dictionary<Mesh, Mesh>();
        Task<MeshDecimator.Result> task; Mesh taskMesh; DecimateSettings taskS; CancellationTokenSource cts; float taskProg;
        double changedAt; DecimateSettings lastSeen; string status = ""; double statusUntil;
        PreviewRenderUtility pru; Material wireMat; readonly List<Action> later = new List<Action>();

        public Mesh Current => cur >= 0 && cur < items.Count ? items[cur].mesh : null;
        public MeshDecimator.Result CurrentResult => Current && done.TryGetValue(Current, out var d) && d.s.SameAs(settings) ? d.r : null;
        public bool Busy => task != null;

        // ---------------------------------------------------------------- look (the Cinematic Editor's palette)
        static readonly Color Accent = new Color(1f, 0.62f, 0.2f);
        static Color Bg => EditorGUIUtility.isProSkin ? new Color(0.12f, 0.125f, 0.15f) : new Color(0.72f, 0.73f, 0.76f);
        static Color Panel => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.215f) : new Color(0.83f, 0.84f, 0.87f);
        static Color PanelHi => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.235f, 0.28f) : new Color(0.9f, 0.9f, 0.93f);
        static Color Ink(float a) => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, a);
        GUIStyle sHead, sBig, sHuge, sHint, sRow, sRowSub, sPill, sCard, sEmpty, sNum; Texture2D panelTex;

        void Styles()
        {
            if (sHead != null && panelTex) return;
            panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; panelTex.SetPixel(0, 0, Panel); panelTex.Apply();
            sHead = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = Ink(0.45f) } };
            sBig = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = Ink(0.92f) } };
            sHuge = new GUIStyle(EditorStyles.boldLabel) { fontSize = 26, normal = { textColor = Ink(0.95f) } };
            sNum = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, normal = { textColor = Ink(0.92f) }, alignment = TextAnchor.MiddleLeft };
            sHint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Ink(0.5f) } };
            sRow = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Ink(0.9f) }, clipping = TextClipping.Clip };
            sRowSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Ink(0.55f) }, clipping = TextClipping.Clip };
            sPill = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 22, padding = new RectOffset(10, 10, 2, 2) };
            sCard = new GUIStyle { normal = { background = panelTex }, padding = new RectOffset(12, 12, 10, 12), margin = new RectOffset(6, 6, 6, 6) };
            sEmpty = new GUIStyle(EditorStyles.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink(0.55f) } };
        }
        bool Pill(string text, bool on, string tip = "", float width = 0)
        {
            var old = GUI.backgroundColor; if (on) GUI.backgroundColor = Accent;
            bool c = width > 0 ? GUILayout.Button(new GUIContent(text, tip), sPill, GUILayout.Width(width)) : GUILayout.Button(new GUIContent(text, tip), sPill);
            GUI.backgroundColor = old; return c;
        }
        bool Big(string text, string tip, float height = 30)
        {
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            bool c = GUILayout.Button(new GUIContent(text, tip), new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 }, GUILayout.Height(height));
            GUI.backgroundColor = old; return c;
        }
        void Toggle(ref bool v, string text, string tip) { if (Pill((v ? "✓  " : "") + text, v, tip)) v = !v; }

        // ---------------------------------------------------------------- picking models
        public void UseSelection()
        {
            var sel = Selection.objects.Where(o => o is Mesh || o is GameObject).ToList();
            if (sel.Count == 0) { Say("Pick a model in the Scene or the Project first."); return; }
            sources.Clear(); foreach (var o in sel) Add(o, false); cur = 0; Rebuild();
        }
        public void Add(Object o, bool select)
        {
            if (!o || sources.Contains(o)) return;
            sources.Add(o); Rebuild();
            if (select) { var l = new List<(Mesh, Renderer)>(); MeshDecimateActions.Collect(o, l); if (l.Count > 0) cur = Mathf.Max(0, items.FindIndex(i => i.mesh == l[0].Item1)); }
        }
        void Rebuild()
        {
            sources.RemoveAll(s => !s);
            var l = new List<(Mesh mesh, Renderer look)>(); foreach (var s in sources) MeshDecimateActions.Collect(s, l);
            items.Clear(); foreach (var (m, r) in l) items.Add(new Item { mesh = m, look = r });
            cur = Mathf.Clamp(cur, 0, Mathf.Max(0, items.Count - 1)); changedAt = 0; Repaint();
        }
        MeshSnapshot Snap(Mesh m)
        {
            if (!snaps.TryGetValue(m, out var s)) { try { s = MeshSnapshot.Take(m); } catch (Exception e) { Debug.LogWarning("Mesh Decimate can't read " + m.name + ": " + e.Message); s = null; } snaps[m] = s; }
            return s;
        }
        void Say(string s) { status = s; statusUntil = EditorApplication.timeSinceStartup + 5; Repaint(); }

        // ---------------------------------------------------------------- background work
        void OnEnable() { EditorApplication.update += Tick; Undo.undoRedoPerformed += AfterUndo; Rebuild(); }
        void AfterUndo() { foreach (var m in items.Select(i => i.mesh).ToList()) if (m) snaps.Remove(m); Rebuild(); }
        void OnDisable()
        {
            EditorApplication.update -= Tick; Undo.undoRedoPerformed -= AfterUndo; cts?.Cancel();
            foreach (var d in done.Values) if (d.shown) DestroyImmediate(d.shown);
            foreach (var w in wireOf.Values) if (w) DestroyImmediate(w);
            done.Clear(); wireOf.Clear(); pru?.Cleanup(); pru = null; if (wireMat) DestroyImmediate(wireMat); if (panelTex) DestroyImmediate(panelTex);
        }

        void Tick()
        {
            if (task != null)
            {
                if (!task.IsCompleted) { Repaint(); return; }
                if (task.IsFaulted) Debug.LogException(task.Exception);
                else if (task.Result != null && taskMesh)
                {
                    if (done.TryGetValue(taskMesh, out var old) && old.shown) { if (wireOf.TryGetValue(old.shown, out var ow)) { DestroyImmediate(ow); wireOf.Remove(old.shown); } snaps.Remove(old.shown); DestroyImmediate(old.shown); }
                    var shown = task.Result.mesh.ToMesh(); shown.hideFlags = HideFlags.HideAndDontSave;
                    done[taskMesh] = new Done { s = taskS, r = task.Result, shown = shown };
                }
                task = null; Repaint();
            }
            if (!settings.SameAs(lastSeen)) { lastSeen = settings.Clone(); changedAt = EditorApplication.timeSinceStartup; cts?.Cancel(); }
            if (EditorApplication.timeSinceStartup - changedAt < 0.2 || page != Page.Decimate) return;
            // the one on show first, then the rest of the list
            Mesh next = null;
            if (Current && !Fresh(Current)) next = Current;
            else foreach (var it in items) if (it.mesh && !Fresh(it.mesh)) { next = it.mesh; break; }
            if (next) Start(next);
        }
        bool Fresh(Mesh m) => (done.TryGetValue(m, out var d) && d.s.SameAs(settings)) || (snaps.TryGetValue(m, out var s) && s == null);
        void Start(Mesh m)
        {
            var snap = Snap(m); if (snap == null) return;
            cts = new CancellationTokenSource(); var tok = cts.Token; var s = settings.Clone(); taskS = s; taskMesh = m; taskProg = 0;
            task = Task.Run(() => MeshDecimator.Run(snap, s, p => taskProg = p, tok));
        }

        /// <summary>Waits for every mesh on the list (used by tests and before saving).</summary>
        public void Finish() { while (page == Page.Decimate) { Tick(); if (task == null && items.All(i => !i.mesh || Fresh(i.mesh))) return; changedAt = -1; Thread.Sleep(5); } }

        // ---------------------------------------------------------------- the window
        void OnGUI()
        {
            Styles();
            float W = position.width, H = position.height;
            EditorGUI.DrawRect(new Rect(0, 0, W, H), Bg);
            Drops(new Rect(0, 0, W, H));
            TopBar(new Rect(0, 0, W, 46));
            float lw = 220, rw = 300;
            Left(new Rect(0, 46, lw, H - 46));
            if (page == Page.Decimate) Right(new Rect(W - rw, 46, rw, H - 46)); else PageRight(new Rect(W - rw, 46, rw, H - 46));
            Middle(new Rect(lw, 46, W - lw - rw, H - 46));
            if (Event.current.type == EventType.Repaint && later.Count > 0) { var l = later.ToArray(); later.Clear(); EditorApplication.delayCall += () => { foreach (var a in l) a(); Repaint(); }; }
        }

        void Drops(Rect r)
        {
            var e = Event.current; if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            var objs = DragAndDrop.objectReferences.Where(o => o is Mesh || o is GameObject).ToArray(); if (objs.Length == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var o in objs) Add(o, true); }
            e.Use();
        }

        void TopBar(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Ink(0.06f));
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 11, r.width - 24, r.height - 12)); GUILayout.BeginHorizontal();
            GUILayout.Label("Mesh Kit", sBig, GUILayout.Width(90)); GUILayout.Space(8);
            PagePills();
            GUILayout.FlexibleSpace();
            if (page == Page.Decimate)
            {
                if (Pill("Before", view == 0, "The mesh as it is")) view = 0;
                if (Pill("After", view == 1, "The mesh with fewer triangles")) view = 1;
                if (Pill("Both", view == 2, "Before and after side by side")) view = 2;
                GUILayout.Space(8);
            }
            if (Pill("Wires", wires, "Show the triangles")) wires = !wires;
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }

        Vector2 listScroll, rightScroll;
        void Left(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), Ink(0.06f));
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20));
            GUILayout.Label("MODELS", sHead);
            if (Big("+  Use selection", "Takes the models picked in the Scene or the Project (you can also drag them in here)")) later.Add(UseSelection);
            GUILayout.Space(6);
            listScroll = GUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; if (!it.mesh) continue;
                var row = GUILayoutUtility.GetRect(10, 40, GUILayout.ExpandWidth(true));
                bool on = i == cur;
                EditorGUI.DrawRect(row, on ? new Color(Accent.r, Accent.g, Accent.b, 0.25f) : row.Contains(Event.current.mousePosition) ? PanelHi : Color.clear);
                if (on) EditorGUI.DrawRect(new Rect(row.x, row.y, 3, row.height), Accent);
                GUI.Label(new Rect(row.x + 8, row.y + 3, row.width - 12, 18), it.mesh.name, sRow);
                var snap = snaps.TryGetValue(it.mesh, out var sn) ? sn : null;
                string sub = page != Page.Decimate ? Num(MeshTris(it.mesh)) + " triangles" : snap == null ? Num(MeshTris(it.mesh)) + " triangles" : done.TryGetValue(it.mesh, out var d) && d.s.SameAs(settings) ? Num(d.r.trianglesBefore) + "  →  " + Num(d.r.trianglesAfter) : Num(snap.TriangleCount) + "  →  ...";
                GUI.Label(new Rect(row.x + 8, row.y + 20, row.width - 12, 16), sub, sRowSub);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { cur = i; Event.current.Use(); Repaint(); }
            }
            if (items.Count == 0) GUILayout.Label("Pick models in the Scene or the Project and press Use selection, or drag them in here.", sHint);
            GUILayout.EndScrollView();
            if (items.Count > 0 && GUILayout.Button("Clear the list", sPill)) later.Add(() => { sources.Clear(); Rebuild(); });
            GUILayout.EndArea();
        }
        static int MeshTris(Mesh m) { long n = 0; for (int i = 0; i < m.subMeshCount; i++) if (m.GetTopology(i) == MeshTopology.Triangles) n += m.GetIndexCount(i) / 3; return (int)n; }
        static string Num(int n) => n.ToString("N0");

        void Right(Rect r)
        {
            EditorGUI.DrawRect(r, Bg);
            GUILayout.BeginArea(r); rightScroll = GUILayout.BeginScrollView(rightScroll);
            var s = settings;
            GUILayout.BeginVertical(sCard);
            GUILayout.BeginHorizontal();
            if (Pill("Fewer triangles", s.mode == DecimateMode.Collapse, "Takes away the triangles that change the shape least, down to the amount you pick")) s.mode = DecimateMode.Collapse;
            if (Pill("Flat parts only", s.mode == DecimateMode.Planar, "Takes out points on flat areas and straight edges only, so the shape stays exactly the same")) s.mode = DecimateMode.Planar;
            GUILayout.EndHorizontal(); GUILayout.Space(8);
            if (s.mode == DecimateMode.Collapse)
            {
                GUILayout.Label("HOW MANY TRIANGLES TO KEEP", sHead);
                var res = CurrentResult;
                GUILayout.Label(s.targetTriangles > 0 ? Num(s.targetTriangles) : MeshDecimateActions.Pct(s.ratio), sHuge);
                EditorGUI.BeginChangeCheck();
                float pct = GUILayout.HorizontalSlider(Mathf.Sqrt(s.ratio), Mathf.Sqrt(0.005f), 1f); // finer at the low end
                if (EditorGUI.EndChangeCheck()) { s.ratio = Mathf.Round(pct * pct * 1000f) / 1000f; s.targetTriangles = 0; }
                GUILayout.Space(8);
                GUILayout.BeginHorizontal();
                foreach (var p in new[] { 0.75f, 0.5f, 0.25f, 0.1f, 0.05f }) if (Pill(MeshDecimateActions.Pct(p), s.targetTriangles == 0 && Mathf.Approximately(s.ratio, p))) { s.ratio = p; s.targetTriangles = 0; }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                GUILayout.BeginHorizontal(); GUILayout.Label("or exactly", sHint, GUILayout.Width(56));
                s.targetTriangles = Mathf.Max(0, EditorGUILayout.DelayedIntField(s.targetTriangles, GUILayout.Width(80))); GUILayout.Label("triangles", sHint); GUILayout.EndHorizontal();
                if (items.Count > 1) GUILayout.Label(s.targetTriangles > 0 ? "Every mesh on the list ends at this many." : "The same share of every mesh on the list.", sHint);
            }
            else
            {
                GUILayout.Label("FLAT MEANS BENT LESS THAN", sHead);
                GUILayout.Label(s.planarAngle.ToString("0.#") + "°", sHuge);
                s.planarAngle = Mathf.Round(GUILayout.HorizontalSlider(s.planarAngle, 0.5f, 45f) * 10f) / 10f;
                GUILayout.Space(8);
                GUILayout.BeginHorizontal(); foreach (var a in new[] { 1f, 5f, 10f, 20f, 30f }) if (Pill(a + "°", Mathf.Approximately(s.planarAngle, a))) s.planarAngle = a; GUILayout.EndHorizontal();
                GUILayout.Label("Bigger takes more away but rounds off gentle curves.", sHint);
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            GUILayout.Label("KEEP", sHead);
            GUILayout.BeginHorizontal(); Toggle(ref s.keepSeams, "Texture seams", "Textures stay where they were (no tearing or sliding)"); Toggle(ref s.keepSharp, "Hard edges", "Flat-shaded edges stay sharp"); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); Toggle(ref s.keepBorders, "Hole edges", "The edges of holes and open sides stay in place"); if (s.keepBorders) Toggle(ref s.lockBorders, "every point", "The edges of holes keep every point, so they still fit with other pieces"); GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("JOIN POINTS CLOSER THAN", sHead);
            float size = Current ? Mathf.Max(0.0001f, Current.bounds.size.magnitude) : 1;
            GUILayout.BeginHorizontal();
            float jd = GUILayout.HorizontalSlider(s.joinDistance, 0, size * 0.02f);
            if (!Mathf.Approximately(jd, s.joinDistance)) s.joinDistance = (float)Math.Round(jd, 5);
            s.joinDistance = Mathf.Max(0, EditorGUILayout.FloatField(s.joinDistance, GUILayout.Width(62)));
            GUILayout.EndHorizontal();
            GUILayout.Label(s.joinDistance > 0 ? "Pieces that touch are joined first, so they can share triangles." : "Only points in the very same place are joined.", sHint);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            GUILayout.Label("USE IT", sHead);
            GUI.enabled = items.Count > 0;
            if (Big("Swap it in", "Saves the new meshes and puts them on the scene objects (Ctrl+Z puts the old ones back). Prefabs and models in the Project get a new copy.", 34)) later.Add(() => SwapIn());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Save the meshes only", "Saves the new meshes as assets and changes nothing else"), sPill)) later.Add(() => SaveOnly());
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("LEVELS OF DETAIL", sHead);
            GUILayout.BeginHorizontal(); GUILayout.Label("Levels", sHint, GUILayout.Width(44)); for (int n = 1; n <= 4; n++) if (Pill(n.ToString(), lodCount == n, "", 30)) lodCount = n; GUILayout.EndHorizontal();
            if (lodRatios == null || lodRatios.Length < 4) lodRatios = new[] { 0.5f, 0.25f, 0.1f, 0.05f };
            GUILayout.BeginHorizontal();
            for (int l = 0; l < lodCount; l++)
            {
                GUILayout.BeginVertical(GUILayout.Width(56)); GUILayout.Label("LOD " + (l + 1), sHint);
                float v = EditorGUILayout.FloatField(Mathf.Round(lodRatios[l] * 1000f) / 10f, GUILayout.Width(50)) / 100f; lodRatios[l] = Mathf.Clamp(v, 0.005f, 1f);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("% of the triangles kept at each level. Far away objects switch to the lighter ones.", sHint);
            if (Big("Make LODs", "Adds an LOD Group with lighter copies. Prefabs and models in the Project get a new copy with the LODs.", 30)) later.Add(() => MakeLods());
            GUI.enabled = true;
            GUILayout.Space(10);
            GUILayout.Label("SAVE INTO", sHead);
            GUILayout.BeginHorizontal(); folder = EditorGUILayout.TextField(folder);
            if (GUILayout.Button("...", GUILayout.Width(26))) { var f = EditorUtility.OpenFolderPanel("Save the meshes into", "Assets", ""); if (!string.IsNullOrEmpty(f)) { f = f.Replace('\\', '/'); int a = f.IndexOf("/Assets"); if (a >= 0) folder = f.Substring(a + 1); else if (f.EndsWith("Assets")) folder = "Assets"; } }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- the preview
        void Middle(Rect r)
        {
            var m = Current;
            if (!m) { GUI.Label(r, "Pick a model in the Scene or the Project,\nthen press  + Use selection.\n\nYou can also drag models in here.", sEmpty); return; }
            var e = Event.current;
            if (e.type == EventType.MouseDrag && r.Contains(e.mousePosition) && (e.button == 0 || e.button == 1)) { yaw += e.delta.x * 0.5f; pitch = Mathf.Clamp(pitch + e.delta.y * 0.5f, -89, 89); e.Use(); Repaint(); }
            if (e.type == EventType.ScrollWheel && r.Contains(e.mousePosition)) { zoom = Mathf.Clamp(zoom * (1 - e.delta.y * 0.04f), 0.2f, 8f); e.Use(); Repaint(); }
            var d = done.TryGetValue(m, out var dd) ? dd : null; bool fresh = d != null && d.s.SameAs(settings);
            var view2 = new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 80);
            if (page != Page.Decimate)
            {
                if (e.type == EventType.Repaint) Draw(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16), m, m.name.ToUpperInvariant());
                var info = new Rect(r.x + 8, r.yMax - 60, r.width - 16, 52); EditorGUI.DrawRect(info, Panel);
                var sn0 = Snap(m); var bb = m.bounds;
                GUI.Label(new Rect(info.x + 14, info.y + 6, 600, 20), Num(sn0 != null ? sn0.TriangleCount : MeshTris(m)) + " triangles   ·   " + Num(m.vertexCount) + " vertices", sNum);
                GUI.Label(new Rect(info.x + 14, info.y + 28, 600, 18), "Size " + bb.size.x.ToString("0.##") + " × " + bb.size.y.ToString("0.##") + " × " + bb.size.z.ToString("0.##") + "   ·   " + m.subMeshCount + " material slot" + (m.subMeshCount == 1 ? "" : "s") + (EditorApplication.timeSinceStartup < statusUntil ? "   ·   " + status : ""), sHint);
                return;
            }
            if (e.type == EventType.Repaint)
            {
                if (view == 2)
                {
                    float hw = (view2.width - 6) / 2;
                    Draw(new Rect(view2.x, view2.y, hw, view2.height), m, "BEFORE");
                    Draw(new Rect(view2.x + hw + 6, view2.y, hw, view2.height), d?.shown, "AFTER");
                }
                else Draw(view2, view == 0 ? m : d?.shown, view == 0 ? "BEFORE" : "AFTER");
            }
            // the numbers
            var bar = new Rect(r.x + 8, r.yMax - 66, r.width - 16, 58); EditorGUI.DrawRect(bar, Panel);
            int before = snaps.TryGetValue(m, out var sn) && sn != null ? sn.TriangleCount : MeshTris(m);
            GUI.Label(new Rect(bar.x + 14, bar.y + 6, 300, 20), Num(before) + "  →  " + (fresh ? Num(d.r.trianglesAfter) : "...") + "  triangles", sNum);
            string sub = fresh ? Num(d.r.verticesBefore) + " → " + Num(d.r.verticesAfter) + " vertices   ·   " + (d.r.seconds < 1 ? (d.r.seconds * 1000).ToString("0") + " ms" : d.r.seconds.ToString("0.0") + " s") : Busy ? "Working... " + (taskProg * 100).ToString("0") + "%" : "";
            GUI.Label(new Rect(bar.x + 14, bar.y + 30, 420, 18), sub, sHint);
            if (fresh && before > 0)
            {
                float kept = d.r.trianglesAfter / (float)before;
                var pr = new Rect(bar.xMax - 190, bar.y + 14, 120, 10); EditorGUI.DrawRect(pr, Ink(0.12f)); EditorGUI.DrawRect(new Rect(pr.x, pr.y, pr.width * kept, pr.height), Accent);
                GUI.Label(new Rect(bar.xMax - 64, bar.y + 6, 60, 26), "-" + Mathf.RoundToInt((1 - kept) * 100) + "%", sBig);
            }
            if (EditorApplication.timeSinceStartup < statusUntil) GUI.Label(new Rect(bar.x + 14, bar.y - 22, bar.width - 28, 18), status, sHint);
            else GUI.Label(new Rect(view2.x + 8, view2.yMax - 18, 300, 16), "Drag to turn · scroll to zoom", sHint);
        }

        void Draw(Rect r, Mesh mesh, string label)
        {
            EditorGUI.DrawRect(r, Panel);
            if (mesh && r.width > 8 && r.height > 8)
            {
                if (pru == null) { pru = new PreviewRenderUtility(); pru.camera.fieldOfView = 30; pru.camera.nearClipPlane = 0.01f; pru.camera.farClipPlane = 1000; pru.camera.clearFlags = CameraClearFlags.SolidColor; }
                var src = Current; var b = src ? src.bounds : mesh.bounds; float rad = Mathf.Max(0.0001f, b.extents.magnitude);
                var rot = Quaternion.Euler(pitch, yaw, 0); float half = Mathf.Min(15f, Mathf.Atan(Mathf.Tan(15f * Mathf.Deg2Rad) * r.width / r.height) * Mathf.Rad2Deg); // fit the narrower side
                float dist = rad / Mathf.Sin(half * Mathf.Deg2Rad) / zoom;
                pru.camera.transform.position = b.center - rot * Vector3.forward * dist; pru.camera.transform.rotation = rot;
                pru.camera.nearClipPlane = Mathf.Max(0.001f, dist - rad * 4); pru.camera.farClipPlane = dist + rad * 4;
                pru.camera.backgroundColor = Panel;
                pru.lights[0].intensity = 1.2f; pru.lights[0].transform.rotation = rot * Quaternion.Euler(30, 30, 0); pru.lights[1].intensity = 0.6f;
                pru.ambientColor = new Color(0.35f, 0.35f, 0.4f);
                pru.BeginPreview(r, GUIStyle.none);
                var mats = Mats(mesh);
                for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) pru.DrawMesh(mesh, Matrix4x4.identity, mats[Mathf.Min(i, mats.Length - 1)], i);
                if (wires) { var wm = Wire(mesh); if (wm) pru.DrawMesh(wm, Matrix4x4.identity, WireMat(), 0); }
                pru.camera.Render();
                GUI.DrawTexture(r, pru.EndPreview(), ScaleMode.StretchToFill, false);
            }
            else GUI.Label(r, Busy ? "Working..." : "", sEmpty);
            GUI.Label(new Rect(r.x + 10, r.y + 8, 200, 16), label, sHead);
        }

        Material[] Mats(Mesh mesh)
        {
            var it = cur >= 0 && cur < items.Count ? items[cur] : null;
            var l = it != null && it.look ? it.look.sharedMaterials.Where(x => x).ToArray() : new Material[0];
            if (l.Length == 0) { var def = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"); l = new[] { def }; }
            return l;
        }
        Material WireMat()
        {
            if (!wireMat) { wireMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave }; wireMat.SetColor("_Color", new Color(0, 0, 0, 0.55f)); wireMat.SetInt("_ZWrite", 0); wireMat.SetFloat("_ZBias", -1f); wireMat.SetInt("_Cull", 0); wireMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); wireMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); }
            return wireMat;
        }
        Mesh Wire(Mesh m)
        {
            if (wireOf.TryGetValue(m, out var w) && w) return w;
            // read through a snapshot: models imported without Read/Write give nothing to GetTriangles/vertices
            var sn = Snap(m); if (sn == null) return null;
            var seen = new HashSet<long>(); var idx = new List<int>();
            for (int s = 0; s < sn.indices.Length; s++)
            {
                if (sn.topologies[s] != MeshTopology.Triangles) continue; var tris = sn.indices[s];
                for (int i = 0; i + 2 < tris.Length; i += 3) for (int k = 0; k < 3; k++) { int a = tris[i + k], b = tris[i + (k + 1) % 3]; long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a; if (seen.Add(key)) { idx.Add(a); idx.Add(b); } }
            }
            w = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 }; w.vertices = sn.positions; w.SetIndices(idx, MeshTopology.Lines, 0); w.bounds = m.bounds;
            wireOf[m] = w; return w;
        }

        // ---------------------------------------------------------------- using it
        /// <summary>Results for every mesh on the list with these settings (made now where missing). null if cancelled.</summary>
        Dictionary<Mesh, MeshSnapshot> All(DecimateSettings s, string title)
        {
            var want = items.Where(i => i.mesh && Snap(i.mesh) != null).Select(i => i.mesh).Distinct().ToList();
            var outp = new Dictionary<Mesh, MeshSnapshot>(); var jobs = new List<(MeshSnapshot, DecimateSettings)>(); var jm = new List<Mesh>();
            foreach (var m in want) { if (done.TryGetValue(m, out var d) && d.s.SameAs(s)) outp[m] = d.r.mesh; else { jobs.Add((Snap(m), s)); jm.Add(m); } }
            if (jobs.Count > 0) { var r = MeshDecimateActions.RunAll(jobs, title); if (r == null) return null; for (int i = 0; i < r.Length; i++) if (r[i] != null) outp[jm[i]] = r[i].mesh; }
            return outp;
        }

        public void SaveOnly()
        {
            var res = All(settings, "Saving meshes"); if (res == null) return;
            string sfx = MeshDecimateActions.Suffix(settings); Object last = null;
            foreach (var kv in res) last = MeshDecimateActions.Save(kv.Key, kv.Value, folder, sfx);
            AssetDatabase.SaveAssets(); if (last) EditorGUIUtility.PingObject(last);
            Say("Saved " + res.Count + " mesh" + (res.Count == 1 ? "" : "es") + " in " + MeshDecimateActions.Folder(folder) + ".");
        }

        public void SwapIn()
        {
            var res = All(settings, "Swapping in"); if (res == null) return;
            string sfx = MeshDecimateActions.Suffix(settings); var map = new Dictionary<Mesh, Mesh>();
            foreach (var kv in res) map[kv.Key] = MeshDecimateActions.Save(kv.Key, kv.Value, folder, sfx);
            Undo.SetCurrentGroupName("Decimate"); int swapped = 0, copies = 0; Object last = null;
            foreach (var src in sources.ToArray())
            {
                if (src is GameObject go)
                {
                    if (MeshDecimateActions.IsSceneObject(go)) swapped += MeshDecimateActions.Swap(go, map);
                    else { last = MeshDecimateActions.SaveCopy(go, folder, sfx, c => MeshDecimateActions.Swap(c, map)); copies++; }
                }
            }
            AssetDatabase.SaveAssets(); if (last) EditorGUIUtility.PingObject(last);
            // the list now holds the new meshes on scene objects; keep showing the old ones so it can be done again
            Say((swapped > 0 ? "Swapped " + swapped + " mesh" + (swapped == 1 ? "" : "es") + " in the scene (Ctrl+Z puts them back). " : "") + (copies > 0 ? copies + " new prefab" + (copies == 1 ? "" : "s") + " in " + MeshDecimateActions.Folder(folder) + "." : swapped == 0 ? "Saved the meshes in " + MeshDecimateActions.Folder(folder) + "." : ""));
        }

        public void MakeLods()
        {
            var levels = new List<Dictionary<Mesh, Mesh>>();
            for (int l = 0; l < lodCount; l++)
            {
                var s = settings.Clone(); s.mode = DecimateMode.Collapse; s.ratio = lodRatios[l]; s.targetTriangles = 0;
                var res = All(s, "Making LOD " + (l + 1)); if (res == null) return;
                var map = new Dictionary<Mesh, Mesh>(); foreach (var kv in res) map[kv.Key] = MeshDecimateActions.Save(kv.Key, kv.Value, folder, MeshDecimateActions.LodTag + (l + 1));
                levels.Add(map);
            }
            Undo.SetCurrentGroupName("Make LODs"); int n = 0, copies = 0; Object last = null;
            foreach (var src in sources.ToArray())
            {
                if (!(src is GameObject go)) continue;
                if (MeshDecimateActions.IsSceneObject(go)) { MeshDecimateActions.MakeLods(go, levels); n++; }
                else { last = MeshDecimateActions.SaveCopy(go, folder, " LODs", c => MeshDecimateActions.MakeLods(c, levels)); copies++; }
            }
            AssetDatabase.SaveAssets(); if (last) EditorGUIUtility.PingObject(last);
            Say(n + copies == 0 ? "Saved the LOD meshes in " + MeshDecimateActions.Folder(folder) + " (LOD Groups need a scene object or a prefab)." : (n > 0 ? "Added LOD Groups to " + n + " object" + (n == 1 ? "" : "s") + ". " : "") + (copies > 0 ? copies + " new prefab" + (copies == 1 ? "" : "s") + " with LODs in " + MeshDecimateActions.Folder(folder) + "." : ""));
        }
    }
}
