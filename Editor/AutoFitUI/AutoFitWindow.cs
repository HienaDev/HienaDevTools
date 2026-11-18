#if HIENADEV_UGUI
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Auto Fit UI: pick a Canvas, see it on six kinds of screen (before and after the fix, side by side), press
    /// Fix. The previews are drawn from a copy in a hidden scene, so nothing in your scene changes until you press Fix
    /// (and Ctrl+Z undoes that). Also: right-click a Canvas > Make It Fit Every Screen, and the example scene.
    /// </summary>
    public class AutoFitWindow : EditorWindow
    {
        public static readonly (string name, int w, int h, Rect safe)[] Screens =
        {
            ("Laptop 16:9", 1920, 1080, new Rect(0, 0, 1, 1)),
            ("Old monitor 4:3", 1024, 768, new Rect(0, 0, 1, 1)),
            ("Ultrawide 21:9", 2560, 1080, new Rect(0, 0, 1, 1)),
            ("Phone sideways (notch)", 2400, 1080, new Rect(0.055f, 0.04f, 0.89f, 0.96f)),
            ("Phone upright (notch)", 1080, 2400, new Rect(0, 0.04f, 1, 0.905f)),
            ("Tablet", 2048, 1536, new Rect(0, 0, 1, 1)),
        };

        [MenuItem("Tools/Auto Fit UI/Open", false, 157)]
        public static void Open() { var w = GetWindow<AutoFitWindow>(false, "Auto Fit UI", true); w.minSize = new Vector2(420, 420); }

        [MenuItem("GameObject/UI/Make It Fit Every Screen", false, 2000)]
        [MenuItem("CONTEXT/Canvas/Make It Fit Every Screen")]
        static void FixSelected(MenuCommand cmd)
        {
            var canvas = cmd.context as Canvas ?? (Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<Canvas>() : null);
            if (canvas) canvas = canvas.rootCanvas;
            if (!canvas) { EditorUtility.DisplayDialog("Auto Fit UI", "Select a Canvas first.", "OK"); return; }
            var r = AutoFitFixer.Fix(canvas);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log("[Auto Fit UI] " + canvas.name + ": " + r);
        }

        [SerializeField] Canvas m_Canvas;
        readonly Dictionary<string, Texture2D> m_Pics = new Dictionary<string, Texture2D>();
        Vector2 m_Scroll; string m_Last = "";
        GUIStyle m_Head, m_Small;

        void OnSelectionChange()
        {
            var c = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<Canvas>() : null;
            if (c && c.rootCanvas != m_Canvas) { m_Canvas = c.rootCanvas; Clear(); Repaint(); }
        }
        void OnDisable() => Clear();
        void Clear() { foreach (var t in m_Pics.Values) if (t) DestroyImmediate(t); m_Pics.Clear(); }

        void OnGUI()
        {
            if (m_Head == null) { m_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 }; m_Small = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter }; }
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace && c.gameObject.scene.IsValid()).ToList();
            if (!m_Canvas && canvases.Count > 0) m_Canvas = canvases[0];

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int i = Mathf.Max(0, canvases.IndexOf(m_Canvas));
            if (canvases.Count > 0)
            {
                int n = EditorGUILayout.Popup(i, canvases.Select(c => c.name + (AutoFitFixer.IsFitted(c) ? "  ✓ fits" : "")).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(220));
                if (canvases[n] != m_Canvas) { m_Canvas = canvases[n]; Clear(); }
            }
            GUILayout.FlexibleSpace();
            bool every = AutoFitFixer.FitEveryCanvas;
            bool nowEvery = GUILayout.Toggle(every, new GUIContent("Fit every Canvas in the game", "No setup at all: in Play mode and in builds, every screen-space Canvas fits itself to the screen when it appears (add 'Leave This Canvas Alone' to skip one)"), EditorStyles.toolbarButton);
            if (nowEvery != every) AutoFitFixer.FitEveryCanvas = nowEvery;
            if (GUILayout.Button("Example scene", EditorStyles.toolbarButton)) EditorApplication.delayCall += AutoFitExample.Make;
            EditorGUILayout.EndHorizontal();

            // ---- lock the game to one shape (16:9 or any other) on every screen
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            bool locked = AutoFitFixer.LockAspect;
            var asp = AutoFitFixer.Aspect;
            var lb = GUI.backgroundColor; if (locked) GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
            bool nowLocked = GUILayout.Toggle(locked, new GUIContent(locked ? " Always " + Shape(asp) + " ✓" : " Always " + Shape(asp), "On every screen the game is drawn in the biggest box of this shape, with black bars around it, and all the UI keeps inside the box. Works in Play mode and builds, nothing to set up."), "Button", GUILayout.Width(locked && asp.x >= 100 ? 165 : 130), GUILayout.Height(24));
            GUI.backgroundColor = lb;
            if (nowLocked != locked) { AutoFitFixer.LockAspect = nowLocked; Clear(); Live(); }
            int si = System.Array.FindIndex(LockShapes, v => v == asp);
            bool custom = m_CustomShape || si < 0;
            int shown = custom ? LockShapes.Length : si;
            int ns = EditorGUILayout.Popup(shown, LockShapeNames, GUILayout.Width(190), GUILayout.Height(22));
            if (ns != shown)
            {
                if (ns == LockShapes.Length) m_CustomShape = true;
                else { m_CustomShape = false; AutoFitFixer.Aspect = LockShapes[ns]; Clear(); Live(); }
            }
            if (custom || ns == LockShapes.Length)
            {
                // any size: a resolution (2560 x 1080) or a ratio (5 x 4), it's the shape that counts
                EditorGUIUtility.labelWidth = 12;
                int cw = EditorGUILayout.DelayedIntField(new GUIContent(" ", "Width: a resolution (like 2560) or a ratio (like 5)"), Mathf.RoundToInt(asp.x), GUILayout.Width(62));
                GUILayout.Label("x", GUILayout.Width(10));
                int ch = EditorGUILayout.DelayedIntField(new GUIContent(" ", "Height: a resolution (like 1080) or a ratio (like 4)"), Mathf.RoundToInt(asp.y), GUILayout.Width(62));
                EditorGUIUtility.labelWidth = 0;
                if ((cw != Mathf.RoundToInt(asp.x) || ch != Mathf.RoundToInt(asp.y)) && cw > 0 && ch > 0) { AutoFitFixer.Aspect = new Vector2(cw, ch); Clear(); Live(); }
            }
            EditorGUILayout.LabelField(locked ? "Whole game, every scene: this shape on every screen, black bars around it. All UI keeps inside, also UI that only shows up while playing. In Play mode and builds." : "Click to lock the game to this shape on every screen (black bars around it).", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();

            // ---- try it: the Game view at other screen sizes; in Play mode the switch above changes the running game at once
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (Application.isPlaying)
            {
                PlayModeWindow.GetRenderingResolution(out uint gw, out uint gh); // (Screen.width here would be this window's size)
                var st = new GUIStyle(EditorStyles.boldLabel); st.normal.textColor = AspectLock.Active ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
                GUILayout.Label(AspectLock.Active ? "●  LIVE: the game is locked to " + Shape(AutoFitFixer.Aspect) + "  (" + gw + " x " + gh + " screen)" : "●  LIVE: not locked, the game uses the whole " + gw + " x " + gh + " screen", st);
            }
            else GUILayout.Label("Press Play, then flip the switch or pick a shape above: the game changes right away.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Game view as:", "Sets the Game view to that screen size, to see what players on that screen get"), GUILayout.Width(88));
            foreach (var g in TrySizes)
                if (GUILayout.Button(new GUIContent(g.name, g.w + " x " + g.h), EditorStyles.miniButton)) { PlayModeWindow.SetCustomRenderingResolution((uint)g.w, (uint)g.h, "Auto Fit: " + g.name); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
            if (GUILayout.Button(new GUIContent("Free", "Back to the Game view's own size (Free Aspect)"), EditorStyles.miniButton)) FreeAspect();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // ---- let the players pick the shape in the game (a settings menu, a pause menu)
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            using (new EditorGUI.DisabledScope(!m_Canvas))
            {
                var where = AutoFitShapePicker.Parent(m_Canvas);
                if (GUILayout.Button(new GUIContent(" + Add a screen shape picker", "Adds a ready-made dropdown (16:9, 16:10, 4:3, 21:9, Off) that changes the shape while the game runs and remembers the player's choice. Select a panel first (your settings menu) to put it there."), GUILayout.Width(190), GUILayout.Height(22)))
                    AutoFitShapePicker.Add(where);
                EditorGUILayout.LabelField(m_Canvas ? "Lets players change the shape in the game. It goes into: " + (where ? where.name : "-") + " (select a panel to put it somewhere else)" : "Needs a Canvas in the scene.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (!m_Canvas)
            {
                EditorGUILayout.HelpBox("No Canvas in the open scene. Make one (or open the example scene) to try it.", MessageType.Info);
                return;
            }
            bool fitted = AutoFitFixer.IsFitted(m_Canvas);
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fitted ? m_Canvas.name + " already fits every screen" : m_Canvas.name + ": how it looks on other screens", m_Head);
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
            GUI.enabled = !fitted;
            if (GUILayout.Button(fitted ? "Done ✓" : "Make it fit every screen", GUILayout.Width(190), GUILayout.Height(26)))
            {
                var c = m_Canvas;
                EditorApplication.delayCall += () => { var r = AutoFitFixer.Fix(c); EditorSceneManager.MarkSceneDirty(c.gameObject.scene); m_Last = r.ToString() + ". Ctrl+Z undoes it."; Clear(); Repaint(); };
            }
            GUI.enabled = true; GUI.backgroundColor = ob;
            EditorGUILayout.EndHorizontal();
            // every Canvas in the open scenes at once (a game usually has several: HUD, menus, popups)
            var unfitted = canvases.Where(c => !AutoFitFixer.IsFitted(c)).ToList();
            if (unfitted.Count > 0 && (unfitted.Count > 1 || unfitted[0] != m_Canvas))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(unfitted.Count + " Canvas" + (unfitted.Count == 1 ? "" : "es") + " in the scene don't fit yet: " + string.Join(", ", unfitted.Select(c => c.name)), EditorStyles.wordWrappedMiniLabel);
                GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
                if (GUILayout.Button("Fit all " + unfitted.Count, GUILayout.Width(110), GUILayout.Height(22)))
                    EditorApplication.delayCall += () =>
                    {
                        Undo.SetCurrentGroupName("Make every Canvas fit");
                        int g = Undo.GetCurrentGroup();
                        foreach (var c in unfitted) { if (!c) continue; AutoFitFixer.Fix(c); EditorSceneManager.MarkSceneDirty(c.gameObject.scene); }
                        Undo.CollapseUndoOperations(g);
                        m_Last = unfitted.Count + " Canvases now fit every screen. Ctrl+Z undoes it."; Clear(); Repaint();
                    };
                GUI.backgroundColor = ob;
                EditorGUILayout.EndHorizontal();
            }
            if (AutoFitFixer.FitEveryCanvas && unfitted.Count > 0)
                EditorGUILayout.LabelField("\"Fit every Canvas in the game\" is on: these fit themselves when the game runs (Play mode, builds). The scene itself only changes with the buttons.", EditorStyles.wordWrappedMiniLabel);
            if (m_Last.Length > 0) EditorGUILayout.LabelField(m_Last, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(fitted ? "Below: how it looks now on each screen." : "Left: now.  Right: after the fix.  (Nothing changes in your scene until you press the button.)", EditorStyles.wordWrappedMiniLabel);

            // the previews
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            float colW = fitted ? position.width - 30 : (position.width - 36) / 2f;
            if (!fitted)
            {
                // the two columns, named (the right one isn't in the scene until the button is pressed)
                var hr = EditorGUILayout.GetControlRect(false, 22);
                var left = new Rect(hr.x, hr.y, colW, hr.height); var right = new Rect(hr.x + colW + 6, hr.y, colW, hr.height);
                EditorGUI.DrawRect(left, new Color(0.75f, 0.35f, 0.3f, 0.6f)); EditorGUI.DrawRect(right, new Color(0.3f, 0.65f, 0.4f, 0.6f));
                var cs = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(left, "NOW (what the Game view shows)", cs); GUI.Label(right, "AFTER THE FIX (press the green button)", cs);
            }
            foreach (var s in Screens)
            {
                EditorGUILayout.LabelField(s.name + "   " + s.w + " × " + s.h, EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                float h = Mathf.Min(colW * s.h / s.w, 260); float w = h * s.w / s.h;
                foreach (bool fix in fitted ? new[] { false } : new[] { false, true })
                {
                    var key = s.name + (fix ? "+" : "");
                    if (!m_Pics.TryGetValue(key, out var tex) || !tex) { tex = AutoFitPreview.Render(m_Canvas, fix, s.w, s.h, s.safe); m_Pics[key] = tex; }
                    var r = GUILayoutUtility.GetRect(colW, h + 2, GUILayout.Width(colW), GUILayout.Height(h + 2));
                    var pic = new Rect(r.x + (colW - w) / 2, r.y, w, h);
                    if (tex) GUI.DrawTexture(pic, tex, ScaleMode.StretchToFill);
                    DrawSafe(pic, s.safe);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(6);
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Refresh the previews", EditorStyles.miniButton)) Clear();
        }

        static readonly (string name, int w, int h)[] TrySizes = { ("16:9", 1920, 1080), ("Ultrawide", 2560, 1080), ("4:3", 1600, 1200), ("16:10", 1920, 1200), ("Phone sideways", 2340, 1080), ("Phone upright", 1080, 2340) };

        // in Play mode: the running game follows the switch straight away
        static void Live()
        {
            if (!Application.isPlaying) return;
            var a = AutoFitFixer.Aspect;
            if (AutoFitFixer.LockAspect && a.x > 0 && a.y > 0) AspectLock.Lock(a.x, a.y); else AspectLock.Unlock();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void FreeAspect()
        {
            try
            {
                var gv = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                foreach (var w in Resources.FindObjectsOfTypeAll(gv))
                    gv.GetMethod("SizeSelectionCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.Invoke(w, new object[] { 0, null });
            }
            catch (Exception e) { Debug.LogWarning("[Auto Fit UI] couldn't set Free Aspect: " + e.Message); }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

        static string Shape(Vector2 a) => a.x.ToString("0") + (a.x >= 100 ? " x " : ":") + a.y.ToString("0");
        static readonly Vector2[] LockShapes = { new Vector2(16, 9), new Vector2(16, 10), new Vector2(4, 3), new Vector2(21, 9), new Vector2(9, 16), new Vector2(1, 1) };
        static readonly string[] LockShapeNames = { "16:9  (most screens, TVs)", "16:10  (laptops, Steam Deck)", "4:3  (retro, old monitors)", "21:9  (ultrawide, cinematic)", "9:16  (phone held upright)", "1:1  (square)", "Custom size..." };
        bool m_CustomShape;

        // the notch area, shaded
        static void DrawSafe(Rect pic, Rect safe)
        {
            if (safe == new Rect(0, 0, 1, 1)) return;
            var c = new Color(0, 0, 0, 0.45f);
            float x0 = pic.x + pic.width * safe.xMin, x1 = pic.x + pic.width * safe.xMax, yTop = pic.y + pic.height * (1 - safe.yMax), yBot = pic.y + pic.height * (1 - safe.yMin);
            EditorGUI.DrawRect(new Rect(pic.x, pic.y, x0 - pic.x, pic.height), c);
            EditorGUI.DrawRect(new Rect(x1, pic.y, pic.xMax - x1, pic.height), c);
            EditorGUI.DrawRect(new Rect(x0, pic.y, x1 - x0, yTop - pic.y), c);
            EditorGUI.DrawRect(new Rect(x0, yBot, x1 - x0, pic.yMax - yBot), c);
        }
    }

    /// <summary>A Canvas drawn at any screen size, from a copy in a hidden scene (the real one isn't touched).</summary>
    public static class AutoFitPreview
    {
        public static Texture2D Render(Canvas source, bool fix, int w, int h, Rect safe, int maxSide = 640)
        {
            if (!source) return null;
            var scene = EditorSceneManager.NewPreviewScene();
            var savedSafe = SafeArea.Simulated;
            RenderTexture rt = null; GameObject camGo = null;
            try
            {
                var copy = UnityEngine.Object.Instantiate(source.gameObject);
                SceneManager.MoveGameObjectToScene(copy, scene);
                copy.SetActive(true);
                var canvas = copy.GetComponent<Canvas>();
                camGo = new GameObject("Preview camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.14f, 0.17f, 1);
                cam.orthographic = true; cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f; cam.cullingMask = ~0;
                float k = Mathf.Min(1f, maxSide / (float)Mathf.Max(w, h));
                int rw = Mathf.Max(8, Mathf.RoundToInt(w * k)), rh = Mathf.Max(8, Mathf.RoundToInt(h * k));
                rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                if (fix && AutoFitFixer.LockAspect)
                {
                    var asp = AutoFitFixer.Aspect;
                    var box = AspectLock.BoxFor(w, h, asp.x / Mathf.Max(0.01f, asp.y));
                    var pa = RenderTexture.active; RenderTexture.active = rt; GL.Clear(true, true, Color.black); RenderTexture.active = pa;
                    cam.rect = box;
                    if (box != new Rect(0, 0, 1, 1)) safe = new Rect(0, 0, 1, 1); // (the box is the screen now)
                }
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 10f;
                // the copy is made to think the screen is w x h: the camera's size times the scale
                var scaler = canvas.GetComponent<CanvasScaler>();
                // (drawn smaller than the real screen: a fixed-pixel canvas is scaled down to match)
                if (scaler && scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) scaler.scaleFactor *= k;
                else if (!scaler) { scaler = copy.AddComponent<CanvasScaler>(); scaler.scaleFactor = k; }
                SafeArea.Simulated = safe;
                cam.Render(); // (the canvas takes the camera's size)
                if (fix) AutoFitFixer.Fix(canvas, undo: false);
                Settle(canvas, scaler, k);
                cam.Render();
                Settle(canvas, scaler, k);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rw, rh, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                tex.ReadPixels(new Rect(0, 0, rw, rh), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
            catch (Exception e) { Debug.LogWarning("[Auto Fit UI] preview failed: " + e.Message); return null; }
            finally
            {
                SafeArea.Simulated = savedSafe;
                if (rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // runs what the canvas would do on its own at that screen size (the preview scene doesn't tick)
        static void Settle(Canvas canvas, CanvasScaler scaler, float k)
        {
            scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler)
            {
                // the preview is drawn smaller than the real screen (k): the scaler's maths runs at the real size
                var m = typeof(CanvasScaler).GetMethod("Handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                m?.Invoke(scaler, null);
            }
            Canvas.ForceUpdateCanvases();
            foreach (var s in canvas.GetComponentsInChildren<SafeArea>(true)) s.Apply();
            Canvas.ForceUpdateCanvases();
            foreach (var s in canvas.GetComponentsInChildren<ShrinkToFit>(true)) s.Apply();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
            Canvas.ForceUpdateCanvases();
        }
    }
}
#endif
