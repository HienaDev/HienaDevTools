using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Itch Page Kit: the pictures an itch.io page needs. Pictures (cover 630x500, 16:9 screenshots, banner, square,
    /// any size) from the running game or from an image in the project, cut or fitted to the exact size. A GIF maker that
    /// strings parts together: images held for a while, GIFs you already have, and live recordings of the game, with
    /// cross-fades (an image first = a GIF cover that shows that image until it plays). The game's UI can be hidden for
    /// shots. Everything goes to Build/Itch Page (git skips Build/), listed small at the bottom: click View to see it big.
    /// </summary>
    public class ItchPageKit : EditorWindow
    {
        [MenuItem("Tools/Itch Page Kit", false, 110)]
        static void Open() { var w = GetWindow<ItchPageKit>(false, "Itch Page Kit", true); w.minSize = new Vector2(400, 480); }

        static readonly (string name, int w, int h, string tip)[] Shots =
        {
            ("Cover", 630, 500, "The page's cover: what people see in itch.io's lists and search. 630x500 is itch's recommended size."),
            ("Screenshot", 1920, 1080, "A 16:9 screenshot for the page's screenshot column."),
            ("Screenshot (small)", 1280, 720, "A lighter 16:9 screenshot."),
            ("Banner", 960, 300, "A wide picture for the top of the page (Edit theme > Banner)."),
            ("Square", 1080, 1080, "A square picture (social media posts)."),
        };
        static readonly (string name, int w, int h)[] GifSizes = { ("Cover 630x500", 630, 500), ("640x360", 640, 360), ("480x270", 480, 270), ("800x450", 800, 450), ("315x250 (small cover)", 315, 250) };
        public static string Folder => Path.Combine(Directory.GetCurrentDirectory(), "Build", "Itch Page");

        [Serializable] class Part
        {
            public int kind; // 0 image, 1 GIF, 2 recording
            public Texture2D image;
            public UnityEngine.Object gifAsset; public string gifFile;
            public float seconds = 2f;
            public bool fit; // fit inside with bars instead of filling (cropping)
        }

        // settings (kept with the window)
        [SerializeField] bool m_HideUI;
        [SerializeField] int m_Source; // pictures from: 0 Game view, 1 an image
        [SerializeField] Texture2D m_SourceImage; [SerializeField] bool m_SourceFit; [SerializeField] Color m_Bars = Color.black;
        [SerializeField] int m_CustomW = 1600, m_CustomH = 900;
        [SerializeField] List<Part> m_Parts = new List<Part> { new Part { kind = 2, seconds = 4f } };
        [SerializeField] int m_GifSize; [SerializeField] int m_GifFps = 15; [SerializeField] float m_Fade = 0.3f;
        Vector2 m_Scroll;
        IEnumerator m_Job; string m_Status = "";
        readonly Dictionary<string, Texture2D> m_Thumbs = new Dictionary<string, Texture2D>();
        List<string> m_Recent; double m_RecentAt;

        void OnEnable() { EditorApplication.update += Step; }
        void OnDisable() { EditorApplication.update -= Step; foreach (var t in m_Thumbs.Values) if (t) DestroyImmediate(t); m_Thumbs.Clear(); }

        void Step()
        {
            if (m_Job == null) return;
            bool more;
            try { more = m_Job.MoveNext(); }
            catch (Exception e) { Debug.LogException(e); m_Status = "Stopped: " + e.Message; more = false; }
            if (!more) { m_Job = null; m_Recent = null; }
            Repaint();
        }

        bool Busy => m_Job != null || CaptureRunner.Busy;

        // ------------------------------------------------------------------ the window (three tabs, big buttons, little text)

        [SerializeField] int m_Tab; [SerializeField] int m_Sel;
        GUIStyle m_TabStyle, m_CardName, m_CardSize, m_Small, m_Block;
        static readonly Color CImage = new Color(0.33f, 0.6f, 0.95f), CGif = new Color(0.65f, 0.45f, 0.92f), CRec = new Color(0.93f, 0.35f, 0.32f), CGo = new Color(0.35f, 0.78f, 0.45f);
        readonly Dictionary<string, float> m_GifLength = new Dictionary<string, float>();

        void Styles()
        {
            if (m_TabStyle != null) return;
            m_TabStyle = new GUIStyle(EditorStyles.toolbarButton) { fixedHeight = 30, fontSize = 12, fontStyle = FontStyle.Bold };
            m_CardName = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            m_CardSize = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            m_Small = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            m_Block = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
        }

        void OnGUI()
        {
            Styles();
            int made = RecentFiles().Count;
            m_Tab = GUILayout.Toolbar(m_Tab, new[] { new GUIContent(" Pictures", EditorGUIUtility.IconContent("d_Texture Icon").image), new GUIContent(" GIF", EditorGUIUtility.IconContent("d_AnimationClip Icon").image), new GUIContent(" Made (" + made + ")", EditorGUIUtility.IconContent("d_Folder Icon").image) }, m_TabStyle);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.Space(6);
            if (m_Tab == 0) PicturesTab(); else if (m_Tab == 1) GifTab(); else MadeTab();
            EditorGUILayout.EndScrollView();
            if (Busy) { var r = EditorGUILayout.GetControlRect(false, 24); EditorGUI.DrawRect(r, new Color(0.2f, 0.45f, 0.7f)); GUI.Label(r, CaptureRunner.Busy ? CaptureRunner.Status : m_Status, m_Block); }
        }

        bool PlayFirst()
        {
            if (EditorApplication.isPlaying) return true;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(EditorGUIUtility.IconContent("d_PlayButton"), GUILayout.Width(20));
            GUILayout.Label("Press Play to shoot the game", EditorStyles.boldLabel);
            if (GUILayout.Button("Play", GUILayout.Width(60))) EditorApplication.isPlaying = true;
            EditorGUILayout.EndHorizontal();
            return false;
        }

        void HideUIChip()
        {
            m_HideUI = GUILayout.Toggle(m_HideUI, new GUIContent(" Hide the game's UI", EditorGUIUtility.IconContent(m_HideUI ? "d_scenevis_hidden_hover" : "d_scenevis_visible_hover").image,
                "Hides every screen Canvas and UI Toolkit menu for the moment of the shot, then shows them again. Nothing to set up."), "Button", GUILayout.Height(24));
        }

        // ---- Pictures: where from, then one card per size (click = take it)
        void PicturesTab()
        {
            int src = GUILayout.Toolbar(m_Source, new[] { new GUIContent(" From the game", EditorGUIUtility.IconContent("d_UnityEditor.GameView").image), new GUIContent(" From an image", EditorGUIUtility.IconContent("d_Image Icon").image) }, GUILayout.Height(26));
            m_Source = src;
            bool ready;
            if (m_Source == 1)
            {
                EditorGUILayout.BeginHorizontal();
                m_SourceImage = (Texture2D)EditorGUILayout.ObjectField(m_SourceImage, typeof(Texture2D), false, GUILayout.Width(70), GUILayout.Height(70));
                EditorGUILayout.BeginVertical();
                GUILayout.Label(m_SourceImage ? m_SourceImage.name : "Drop a picture here", EditorStyles.boldLabel);
                int f = GUILayout.Toolbar(m_SourceFit ? 1 : 0, new[] { new GUIContent("Fill", "Fills the shape, cutting the edges off"), new GUIContent("Fit", "Shows all of it, with bars at the edges") });
                m_SourceFit = f == 1;
                if (m_SourceFit) m_Bars = EditorGUILayout.ColorField(new GUIContent("Bars"), m_Bars, false, false, false);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                ready = m_SourceImage != null;
            }
            else { ready = PlayFirst(); if (ready) HideUIChip(); }
            EditorGUILayout.Space(6);

            GUI.enabled = ready && !Busy;
            float cardW = 118, cardH = 112;
            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 20) / (cardW + 6)));
            var cards = Shots.Select(s => (s.name, s.w, s.h, s.tip)).ToList();
            cards.Add(("Any size", m_CustomW, m_CustomH, "The size in the boxes below"));
            for (int i = 0; i < cards.Count; i++)
            {
                if (i % cols == 0) EditorGUILayout.BeginHorizontal();
                var c = cards[i];
                var r = GUILayoutUtility.GetRect(cardW, cardH, GUILayout.Width(cardW), GUILayout.Height(cardH));
                if (GUI.Button(r, new GUIContent("", c.tip))) TakePicture(c.name == "Any size" ? "Picture" : c.name, Mathf.Clamp(c.w, 16, 8192), Mathf.Clamp(c.h, 16, 8192));
                // the shape, drawn
                float maxW = cardW - 30, maxH = 54, a = c.w / (float)Mathf.Max(1, c.h);
                float sw = maxW, sh = sw / a; if (sh > maxH) { sh = maxH; sw = sh * a; }
                var shape = new Rect(r.center.x - sw / 2, r.y + 10 + (maxH - sh) / 2, sw, sh);
                EditorGUI.DrawRect(shape, GUI.enabled ? new Color(0.33f, 0.6f, 0.95f, 0.85f) : new Color(0.5f, 0.5f, 0.5f, 0.4f));
                GUI.Label(new Rect(r.x, r.y + 70, r.width, 18), c.name, m_CardName);
                GUI.Label(new Rect(r.x, r.y + 88, r.width, 16), c.w + " × " + c.h, m_CardSize);
                if (i % cols == cols - 1 || i == cards.Count - 1) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); }
            }
            GUI.enabled = true;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Any size:", GUILayout.Width(60));
            m_CustomW = EditorGUILayout.IntField(m_CustomW, GUILayout.Width(50)); GUILayout.Label("×", GUILayout.Width(12)); m_CustomH = EditorGUILayout.IntField(m_CustomH, GUILayout.Width(50));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ---- GIF: a strip of parts (click one to change it), then size, then Make
        void GifTab()
        {
            if (m_Parts.Count == 0) EditorGUILayout.LabelField("Add parts below: a picture, a GIF you have, or a recording of the game.", m_Small);
            else
            {
                m_Sel = Mathf.Clamp(m_Sel, 0, m_Parts.Count - 1);
                var strip = GUILayoutUtility.GetRect(10, 46, GUILayout.ExpandWidth(true));
                float total = m_Parts.Sum(PartLength), x = strip.x;
                for (int i = 0; i < m_Parts.Count; i++)
                {
                    var p = m_Parts[i];
                    float w = Mathf.Max(44, (strip.width - 2 * m_Parts.Count) * PartLength(p) / Mathf.Max(0.01f, total));
                    var b = new Rect(x, strip.y, Mathf.Min(w, strip.xMax - x), strip.height); x += w + 2;
                    var col = p.kind == 0 ? CImage : p.kind == 1 ? CGif : CRec;
                    EditorGUI.DrawRect(b, i == m_Sel ? col : col * new Color(1, 1, 1, 0.55f));
                    if (i == m_Sel) { EditorGUI.DrawRect(new Rect(b.x, b.yMax - 3, b.width, 3), Color.white); }
                    string name = p.kind == 0 ? "Picture" : p.kind == 1 ? "GIF" : "Recording";
                    GUI.Label(new Rect(b.x, b.y + 6, b.width, 18), name, m_Block);
                    GUI.Label(new Rect(b.x, b.y + 22, b.width, 16), PartLength(p).ToString("0.#") + " s", m_Block);
                    if (Event.current.type == EventType.MouseDown && b.Contains(Event.current.mousePosition)) { m_Sel = i; GUI.FocusControl(null); Event.current.Use(); Repaint(); }
                }
                EditorGUILayout.Space(4);
                PartPanel(m_Parts[m_Sel]);
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            var old = GUI.backgroundColor;
            GUI.backgroundColor = CImage; if (GUILayout.Button("+ Picture", GUILayout.Height(24))) { m_Parts.Add(new Part { kind = 0, seconds = 1.5f }); m_Sel = m_Parts.Count - 1; }
            GUI.backgroundColor = CGif; if (GUILayout.Button("+ GIF", GUILayout.Height(24))) { m_Parts.Add(new Part { kind = 1 }); m_Sel = m_Parts.Count - 1; }
            GUI.backgroundColor = CRec; if (GUILayout.Button("+ Recording", GUILayout.Height(24))) { m_Parts.Add(new Part { kind = 2, seconds = 4f }); m_Sel = m_Parts.Count - 1; }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Tip: a picture first = the cover itch shows before the GIF plays.", m_Small);

            EditorGUILayout.Space(8);
            m_GifSize = GUILayout.Toolbar(m_GifSize, GifSizes.Select(g => g.name.Replace(" (small cover)", "")).ToArray());
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Smooth", GUILayout.Width(48));
            int fi = Array.IndexOf(new[] { 10, 15, 20, 25 }, m_GifFps); if (fi < 0) fi = 1;
            m_GifFps = new[] { 10, 15, 20, 25 }[GUILayout.Toolbar(fi, new[] { "10", "15", "20", "25 fps" })];
            GUILayout.Space(10); GUILayout.Label(new GUIContent("Fade", "Fade between parts, in seconds"), GUILayout.Width(32));
            m_Fade = GUILayout.HorizontalSlider(m_Fade, 0f, 1f, GUILayout.Width(70)); GUILayout.Label(m_Fade.ToString("0.0") + " s", GUILayout.Width(36));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            bool needsPlay = m_Parts.Any(p => p.kind == 2);
            if (needsPlay && !PlayFirst()) { }
            GUI.enabled = !Busy && m_Parts.Count > 0 && (!needsPlay || EditorApplication.isPlaying);
            var ob = GUI.backgroundColor; GUI.backgroundColor = CGo;
            if (GUILayout.Button("Make the GIF  (" + m_Parts.Sum(PartLength).ToString("0.#") + " s)", GUILayout.Height(36))) m_Job = MakeGif();
            GUI.backgroundColor = ob; GUI.enabled = true;
            if (needsPlay && EditorApplication.isPlaying) HideUIChip();
        }

        void PartPanel(Part p)
        {
            var col = p.kind == 0 ? CImage : p.kind == 1 ? CGif : CRec;
            var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.DrawRect(new Rect(box.x, box.y, 4, box.height), col);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(6);
            if (p.kind == 0)
            {
                p.image = (Texture2D)EditorGUILayout.ObjectField(p.image, typeof(Texture2D), false, GUILayout.Width(56), GUILayout.Height(56));
                EditorGUILayout.BeginVertical();
                GUILayout.Label(p.image ? p.image.name : "Drop a picture", EditorStyles.boldLabel);
                p.seconds = EditorGUILayout.Slider(p.seconds, 0.1f, 10f);
                p.fit = GUILayout.Toolbar(p.fit ? 1 : 0, new[] { "Fill", "Fit" }) == 1;
                EditorGUILayout.EndVertical();
            }
            else if (p.kind == 1)
            {
                EditorGUILayout.BeginVertical();
                var o = EditorGUILayout.ObjectField(p.gifAsset, typeof(UnityEngine.Object), false);
                if (o != p.gifAsset) { p.gifAsset = o; if (o) p.gifFile = null; }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(string.IsNullOrEmpty(p.gifFile) ? (p.gifAsset ? "" : "Drop a .gif above, or") : Path.GetFileName(p.gifFile), EditorStyles.miniLabel);
                if (GUILayout.Button("Choose a file...", GUILayout.Width(110))) { string f = EditorUtility.OpenFilePanel("A GIF", Directory.Exists(Folder) ? Folder : Application.dataPath, "gif"); if (!string.IsNullOrEmpty(f)) { p.gifFile = f; p.gifAsset = null; } }
                EditorGUILayout.EndHorizontal();
                string path = GifPath(p);
                if (p.gifAsset && (path == null || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))) EditorGUILayout.HelpBox("That isn't a .gif", MessageType.Warning);
                p.fit = GUILayout.Toolbar(p.fit ? 1 : 0, new[] { "Fill", "Fit" }) == 1;
                EditorGUILayout.EndVertical();
            }
            else
            {
                GUILayout.Label(EditorGUIUtility.IconContent("d_Record Off"), GUILayout.Width(20));
                EditorGUILayout.BeginVertical();
                GUILayout.Label("The game, recorded when you press Make", EditorStyles.boldLabel);
                p.seconds = EditorGUILayout.Slider(p.seconds, 0.5f, 15f);
                EditorGUILayout.EndVertical();
            }
            // move / remove
            EditorGUILayout.BeginVertical(GUILayout.Width(26));
            int i = m_Parts.IndexOf(p);
            GUI.enabled = i > 0; if (GUILayout.Button(new GUIContent("◀", "Earlier"), EditorStyles.miniButton)) { m_Parts.RemoveAt(i); m_Parts.Insert(i - 1, p); m_Sel = i - 1; }
            GUI.enabled = i < m_Parts.Count - 1; if (GUILayout.Button(new GUIContent("▶", "Later"), EditorStyles.miniButton)) { m_Parts.RemoveAt(i); m_Parts.Insert(i + 1, p); m_Sel = i + 1; }
            GUI.enabled = true; if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton)) { m_Parts.Remove(p); m_Sel = Mathf.Max(0, i - 1); }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // how long a part plays (a GIF's own length, read once)
        float PartLength(Part p)
        {
            if (p.kind != 1) return p.seconds;
            string path = GifPath(p);
            if (path == null || !File.Exists(path)) return 2f;
            if (!m_GifLength.TryGetValue(path, out float s))
            {
                s = 2f;
                try { var g = GifCodec.Read(File.ReadAllBytes(path)); s = g.delaysCs.Sum() / 100f; } catch { }
                m_GifLength[path] = s;
            }
            return s;
        }

        // ---- Made: a grid of small pictures; click one to see it big
        List<string> RecentFiles()
        {
            if (m_Recent == null || EditorApplication.timeSinceStartup > m_RecentAt + 5)
            {
                m_RecentAt = EditorApplication.timeSinceStartup;
                m_Recent = Directory.Exists(Folder) ? new DirectoryInfo(Folder).GetFiles().Where(f => f.Extension == ".png" || f.Extension == ".gif").OrderByDescending(f => f.LastWriteTimeUtc).Take(30).Select(f => f.FullName).ToList() : new List<string>();
            }
            return m_Recent;
        }

        void MadeTab()
        {
            var files = RecentFiles();
            if (files.Count == 0) { EditorGUILayout.LabelField("Nothing yet: take a picture or make a GIF.", m_Small); }
            float tw = 132, th = 96;
            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 20) / (tw + 6)));
            for (int i = 0; i < files.Count; i++)
            {
                if (i % cols == 0) EditorGUILayout.BeginHorizontal();
                string path = files[i];
                var r = GUILayoutUtility.GetRect(tw, th, GUILayout.Width(tw), GUILayout.Height(th));
                var img = new Rect(r.x, r.y, r.width, r.height - 22);
                EditorGUI.DrawRect(img, new Color(0.13f, 0.13f, 0.13f));
                var thumb = Thumb(path);
                if (thumb) GUI.DrawTexture(img, thumb, ScaleMode.ScaleToFit);
                if (path.EndsWith(".gif")) { var tag = new Rect(img.x + 3, img.y + 3, 28, 14); EditorGUI.DrawRect(tag, CGif); GUI.Label(tag, "GIF", m_Block); }
                string name = Path.GetFileNameWithoutExtension(path);
                GUI.Label(new Rect(r.x, img.yMax + 1, r.width, 12), name.Length > 22 ? name.Substring(0, 22) + "…" : name, m_Small);
                GUI.Label(new Rect(r.x, img.yMax + 11, r.width, 11), ImageViewer.Size(path), m_Small);
                if (GUI.Button(r, new GUIContent("", "Click to see it big"), GUIStyle.none)) ImageViewer.Show(path);
                if (i % cols == cols - 1 || i == files.Count - 1) { GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal(); }
            }
            EditorGUILayout.Space(6);
            if (GUILayout.Button(new GUIContent(" Open the folder", EditorGUIUtility.IconContent("d_Folder Icon").image), GUILayout.Height(24))) { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder + Path.DirectorySeparatorChar); }
        }

        static string GifPath(Part p)
        {
            if (p.gifAsset) { var a = AssetDatabase.GetAssetPath(p.gifAsset); return string.IsNullOrEmpty(a) ? null : Path.GetFullPath(a); }
            return string.IsNullOrEmpty(p.gifFile) ? null : p.gifFile;
        }

        // ------------------------------------------------------------------ pictures

        void TakePicture(string name, int w, int h)
        {
            if (m_Source == 1)
            {
                var t = Pics.Fit(m_SourceImage, w, h, m_SourceFit, m_Bars);
                Save(name + " " + w + "x" + h, t.EncodeToPNG(), ".png");
                DestroyImmediate(t);
                return;
            }
            CaptureRunner.Status = "Taking the " + name.ToLower() + "...";
            CaptureRunner.Inst.Picture(w, h, m_HideUI, t =>
            {
                if (t == null) return;
                Save(name + " " + w + "x" + h, t.EncodeToPNG(), ".png");
                Destroy(t);
            });
        }

        void Save(string name, byte[] bytes, string ext)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, name + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ext);
            File.WriteAllBytes(path, bytes);
            Debug.Log("[Itch Page Kit] saved " + path + " (" + ImageViewer.Size(path) + ")");
            m_Recent = null; ShowNotification(new GUIContent("Saved: " + Path.GetFileName(path)), 2); Repaint();
        }

        // ------------------------------------------------------------------ the GIF maker

        IEnumerator MakeGif()
        {
            var size = GifSizes[m_GifSize]; int w = size.w, h = size.h;
            int frameCs = Mathf.Max(2, Mathf.RoundToInt(100f / m_GifFps));
            var frames = new List<Color32[]>(); var delays = new List<int>();
            for (int pi = 0; pi < m_Parts.Count; pi++)
            {
                var p = m_Parts[pi];
                var pf = new List<Color32[]>(); var pd = new List<int>();
                m_Status = "Part " + (pi + 1) + " of " + m_Parts.Count + "...";
                yield return null;
                if (p.kind == 0)
                {
                    if (!p.image) continue;
                    var t = Pics.Fit(p.image, w, h, p.fit, Color.black);
                    pf.Add(t.GetPixels32()); pd.Add(Mathf.RoundToInt(p.seconds * 100f)); DestroyImmediate(t);
                }
                else if (p.kind == 1)
                {
                    string path = GifPath(p);
                    if (path == null || !File.Exists(path)) { Debug.LogWarning("[Itch Page Kit] part " + (pi + 1) + ": no GIF picked"); continue; }
                    var g = GifCodec.Read(File.ReadAllBytes(path));
                    for (int i = 0; i < g.frames.Count; i++)
                    {
                        var src = Pics.FromPixels(g.frames[i], g.width, g.height);
                        var t = Pics.Fit(src, w, h, p.fit, Color.black);
                        pf.Add(t.GetPixels32()); pd.Add(g.delaysCs[i]);
                        DestroyImmediate(src); DestroyImmediate(t);
                        if (i % 10 == 9) { m_Status = "Part " + (pi + 1) + ": reading the GIF " + (i + 1) + " / " + g.frames.Count; yield return null; }
                    }
                }
                else
                {
                    if (!EditorApplication.isPlaying) { m_Status = "Recordings need Play mode."; yield break; }
                    List<Color32[]> rec = null;
                    CaptureRunner.Status = "Part " + (pi + 1) + ": recording...";
                    CaptureRunner.Inst.Record(w, h, p.seconds, m_GifFps, m_HideUI, r => rec = r);
                    while (rec == null) { if (!EditorApplication.isPlaying) yield break; yield return null; }
                    pf.AddRange(rec); foreach (var _ in rec) pd.Add(frameCs);
                }
                // a fade from the end of what's there into this part
                int n = Mathf.RoundToInt(m_Fade * m_GifFps);
                if (frames.Count > 0 && pf.Count > 0 && n > 0)
                {
                    var a = frames[frames.Count - 1]; var b = pf[0];
                    for (int k = 1; k <= n; k++)
                    {
                        float t = k / (float)(n + 1); var mix = new Color32[a.Length];
                        for (int j = 0; j < a.Length; j++) mix[j] = Color32.Lerp(a[j], b[j], t);
                        frames.Add(mix); delays.Add(frameCs);
                    }
                }
                frames.AddRange(pf); delays.AddRange(pd);
            }
            if (frames.Count == 0) { m_Status = "Nothing to put in the GIF (pick the pictures / GIFs first)."; yield break; }
            m_Status = "Making the GIF (" + frames.Count + " frames)...";
            byte[] bytes = null; Exception err = null;
            var job = new System.Threading.Thread(() => { try { bytes = GifCodec.Write(frames, delays, w, h); } catch (Exception e) { err = e; } }) { IsBackground = true };
            job.Start();
            while (job.IsAlive) yield return null;
            if (err != null) throw err;
            Save("GIF " + w + "x" + h, bytes, ".gif");
            m_Status = "";
        }

        // ------------------------------------------------------------------ the list of what's been made

        // a small picture (the first frame of a GIF), made once per file
        Texture2D Thumb(string path)
        {
            if (m_Thumbs.TryGetValue(path, out var t)) return t;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var g = GifCodec.Read(bytes);
                    t = Pics.FromPixels(g.frames[0], g.width, g.height);
                }
                else { t = new Texture2D(2, 2); t.LoadImage(bytes); }
                t.hideFlags = HideFlags.HideAndDontSave;
            }
            catch { t = null; }
            m_Thumbs[path] = t;
            return t;
        }

        void OnInspectorUpdate() { if (Busy) Repaint(); }
    }
}
