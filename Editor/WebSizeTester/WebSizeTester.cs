using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Web Size Tester: sets the Game view to the sizes the game will really be played at (itch.io's embed sizes,
    /// laptop, full HD, phones and a tablet) so you can see the layout there; "Check every size" goes through all of them
    /// in Play mode, takes a small picture of each and lists UI that ends up cut off by the screen's edges.
    /// </summary>
    public class WebSizeTester : EditorWindow
    {
        [MenuItem("Tools/Web Size Tester", false, 111)]
        static void Open() { var w = GetWindow<WebSizeTester>(false, "Web Sizes", true); w.minSize = new Vector2(380, 420); }

        static readonly (string group, string name, int w, int h, string tip)[] Sizes =
        {
            ("itch.io page", "Embed 960x600", 960, 600, "itch.io's default embed size (Edit game > Embed options)"),
            ("itch.io page", "Embed 800x600", 800, 600, "an older, squarer embed"),
            ("itch.io page", "Embed 1280x720", 1280, 720, "a big 16:9 embed"),
            ("Full screen", "Laptop 1366x768", 1366, 768, "the most common small laptop screen"),
            ("Full screen", "Full HD 1920x1080", 1920, 1080, "a usual desktop monitor"),
            ("Full screen", "Ultrawide 2560x1080", 2560, 1080, "21:9 monitors"),
            ("Phones and tablets", "Phone, upright 390x844", 390, 844, "a phone held upright (browsers on phones)"),
            ("Phones and tablets", "Phone, sideways 844x390", 844, 390, "a phone held sideways"),
            ("Phones and tablets", "Tablet 1024x768", 1024, 768, "an iPad-like 4:3 screen"),
        };

        class Result { public string name; public int w, h; public string picture; public List<string> cut = new List<string>(); }
        readonly List<Result> m_Results = new List<Result>();
        IEnumerator m_Job; string m_Status = "";
        uint m_StartW, m_StartH; bool m_HaveStart;
        Vector2 m_Scroll;
        readonly Dictionary<string, Texture2D> m_Thumbs = new Dictionary<string, Texture2D>();
        static string Folder => Path.GetFullPath("Library/HienaDevTools/WebSizes");

        void OnEnable() => EditorApplication.update += Step;
        void OnDisable() { EditorApplication.update -= Step; foreach (var t in m_Thumbs.Values) if (t) DestroyImmediate(t); m_Thumbs.Clear(); }
        void Step()
        {
            if (m_Job == null) return;
            bool more;
            try { more = m_Job.MoveNext(); } catch (Exception e) { Debug.LogException(e); m_Status = "Stopped: " + e.Message; more = false; }
            if (!more) m_Job = null;
            Repaint();
        }

        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.LabelField("Sets the Game view to a real screen size. The game must look right at all of them: menus not cut off, text readable.", EditorStyles.wordWrappedMiniLabel);
            PlayModeWindow.GetRenderingResolution(out uint cw, out uint ch);
            EditorGUILayout.LabelField("The Game view now: " + cw + " x " + ch, EditorStyles.miniBoldLabel);
            string group = null;
            GUI.enabled = m_Job == null;
            foreach (var s in Sizes)
            {
                if (s.group != group) { group = s.group; EditorGUILayout.Space(4); EditorGUILayout.LabelField(group, EditorStyles.boldLabel); }
                EditorGUILayout.BeginHorizontal();
                bool on = cw == s.w && ch == s.h;
                if (GUILayout.Toggle(on, new GUIContent(s.name, s.tip), "Button", GUILayout.Width(210)) && !on) SetSize(s.w, s.h, s.name);
                EditorGUILayout.LabelField(s.tip, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            GUI.enabled = true;

            EditorGUILayout.Space(8);
            if (m_Job != null) { EditorGUILayout.HelpBox(m_Status, MessageType.Info); if (GUILayout.Button("Stop")) { m_Job = null; Restore(); } }
            else
            {
                GUI.enabled = EditorApplication.isPlaying;
                if (GUILayout.Button(EditorApplication.isPlaying ? "Check every size" : "Check every size (press Play first)", GUILayout.Height(30))) m_Job = CheckAll();
                GUI.enabled = true;
                EditorGUILayout.LabelField("Goes through each size, takes a small picture and lists UI cut off by the screen's edges, then puts the Game view back.", EditorStyles.wordWrappedMiniLabel);
            }

            foreach (var r in m_Results)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                var thumb = Thumb(r.picture);
                var rect = GUILayoutUtility.GetRect(110, 64, GUILayout.Width(110), GUILayout.Height(64));
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
                if (thumb) GUI.DrawTexture(rect, thumb, ScaleMode.ScaleToFit);
                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition) && r.picture != null) { ImageViewer.Show(r.picture); Event.current.Use(); }
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(r.name, EditorStyles.boldLabel);
                if (r.cut.Count == 0) EditorGUILayout.LabelField("Nothing cut off", EditorStyles.miniLabel);
                else
                {
                    var old = GUI.color; GUI.color = new Color(1f, 0.7f, 0.5f);
                    EditorGUILayout.LabelField(r.cut.Count + " cut off: " + string.Join(", ", r.cut.Take(6)) + (r.cut.Count > 6 ? "..." : ""), EditorStyles.wordWrappedMiniLabel);
                    GUI.color = old;
                }
                EditorGUILayout.BeginHorizontal();
                if (r.picture != null && GUILayout.Button("View", EditorStyles.miniButtonLeft, GUILayout.Width(50))) ImageViewer.Show(r.picture);
                if (GUILayout.Button("Use this size", EditorStyles.miniButtonRight, GUILayout.Width(90))) SetSize(r.w, r.h, r.name);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        static void SetSize(int w, int h, string name)
        {
            PlayModeWindow.SetCustomRenderingResolution((uint)w, (uint)h, name);
            // the Game view redraws at once (and in Play mode the game lays itself out again)
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void Restore() { if (m_HaveStart) { SetSize((int)m_StartW, (int)m_StartH, "Before the size check"); m_HaveStart = false; } }

        IEnumerator CheckAll()
        {
            m_Results.Clear();
            PlayModeWindow.GetRenderingResolution(out m_StartW, out m_StartH); m_HaveStart = true;
            Directory.CreateDirectory(Folder);
            foreach (var s in Sizes)
            {
                if (!EditorApplication.isPlaying) { m_Status = "Play mode stopped."; Restore(); yield break; }
                m_Status = "Checking " + s.name + "...";
                SetSize(s.w, s.h, s.name);
                // a few frames for the game and its UI to lay out at the new size, then the picture and the check
                double until = EditorApplication.timeSinceStartup + 0.6;
                while (EditorApplication.timeSinceStartup < until) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                Result res = null;
                CaptureRunner.Inst.AfterFrames(1, runner =>
                {
                    res = new Result { name = s.name, w = s.w, h = s.h };
                    int tw = 320, th = Mathf.Max(1, Mathf.RoundToInt(320f * s.h / s.w));
                    if (th > 320) { th = 320; tw = Mathf.Max(1, Mathf.RoundToInt(320f * s.w / s.h)); }
                    var t = CaptureRunner.GrabNow(tw, th);
                    if (t != null)
                    {
                        res.picture = Path.Combine(Folder, s.w + "x" + s.h + ".png");
                        File.WriteAllBytes(res.picture, t.EncodeToPNG()); Destroy(t);
                        if (m_Thumbs.TryGetValue(res.picture, out var old)) { if (old) DestroyImmediate(old); m_Thumbs.Remove(res.picture); }
                    }
                    res.cut = CutOff();
                });
                double giveUp = EditorApplication.timeSinceStartup + 5;
                while (res == null && EditorApplication.timeSinceStartup < giveUp) { EditorApplication.QueuePlayerLoopUpdate(); yield return null; }
                if (res != null) m_Results.Add(res);
            }
            Restore();
            m_Status = "";
        }

        // UI that sticks out past the screen's edges (more than a few pixels), skipping things inside scroll views
        static List<string> CutOff()
        {
            var cut = new List<string>();
            float sw = Screen.width, sh = Screen.height;
            foreach (var d in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                var root = d.rootVisualElement; if (root == null || root.panel == null) continue;
                var panel = root.panel.visualTree.worldBound;
                root.Query<VisualElement>().ForEach(e =>
                {
                    if (!(e is TextElement) && !(e is Image)) return;
                    if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden || e.resolvedStyle.opacity < 0.05f) return;
                    var b = e.worldBound; if (b.width < 2 || b.height < 2) return;
                    for (var p = e.parent; p != null; p = p.parent) { if (p is ScrollView) return; if (p.resolvedStyle.display == DisplayStyle.None) return; }
                    if (b.xMin < panel.xMin - 3 || b.yMin < panel.yMin - 3 || b.xMax > panel.xMax + 3 || b.yMax > panel.yMax + 3)
                        cut.Add(Describe(e));
                });
            }
#if HIENADEV_UGUI
            var corners = new Vector3[4];
            foreach (var g in FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsSortMode.None))
            {
                if (!g.isActiveAndEnabled || g.canvas == null || g.canvas.renderMode == RenderMode.WorldSpace || g.color.a < 0.05f) continue;
                if (g.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null || g.GetComponentInParent<UnityEngine.UI.Mask>() != null) continue;
                var cam = g.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.worldCamera;
                g.rectTransform.GetWorldCorners(corners);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var c in corners) { var sp = RectTransformUtility.WorldToScreenPoint(cam, c); x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y); }
                if (x1 - x0 < 2 || y1 - y0 < 2) continue;
                if (x0 < -3 || y0 < -3 || x1 > sw + 3 || y1 > sh + 3) cut.Add(g is UnityEngine.UI.Text t && !string.IsNullOrEmpty(t.text) ? "\"" + Short(t.text) + "\"" : g.name);
            }
#endif
            return cut.Distinct().ToList();
        }

        static string Describe(VisualElement e)
        {
            if (e is TextElement t && !string.IsNullOrWhiteSpace(t.text)) return "\"" + Short(t.text) + "\"";
            return string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name;
        }
        static string Short(string s) { s = s.Replace("\n", " ").Trim(); return s.Length > 24 ? s.Substring(0, 24) + "…" : s; }

        Texture2D Thumb(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            if (m_Thumbs.TryGetValue(path, out var t) && t) return t;
            t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave }; t.LoadImage(File.ReadAllBytes(path));
            m_Thumbs[path] = t; return t;
        }
    }
}
