using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Texture Studio. Edit a texture as one session: click tools on the left to stack them up (in any order), see the
    /// result live on the right (compare slider, repeat 1/2/3, tile edges), then save: the texture and, if wanted, its
    /// normal, height and mask maps and a ready material. Originals are never changed. Also: Shrink many (import
    /// presets with undo) and Make a mask map (pack greyscale maps). Painted in the same style as the Cinematic Editor.
    /// </summary>
    public partial class TextureStudioWindow : EditorWindow
    {
        enum Page { Edit, Shrink, Pack, Library, Decals, Patterns, Paint, Atlas }
        enum View { Compare, After, Before, Normal, Height, Mask, SideBySide, Model, Glow }

        [SerializeField] Page m_Page = Page.Edit;

        // ---- the session
        [SerializeField] Texture2D m_Source;
        [SerializeReference] List<TextureStep> m_Steps = new List<TextureStep>();
        [SerializeField] int m_Selected = -1;
        [SerializeField] int m_Repeat = 2;
        [SerializeField] View m_View = View.SideBySide;
        [SerializeField] float m_Split = 0.5f;
        [SerializeField] bool m_Edges;
        [SerializeField] bool m_SaveNormal = true, m_SaveHeight, m_SaveMask = true, m_MakeMaterial = true;
        [SerializeField] float m_NormalStrength = 2f, m_AO = 1f, m_Smooth = 0.4f;
        [SerializeField] int m_ImportAs = -1;
        [SerializeField] string m_SaveName = "";
        [SerializeField] float m_HeightContrast = 1.2f, m_HeightSmooth = 0.6f; [SerializeField] bool m_HeightInvert;
        [SerializeField] TextureRecipe m_Recipe;          // what this session was opened from (or saved as)
        [SerializeField] bool m_Follow = true;             // clicking a texture in the Project window opens it here
        [SerializeField] TextureModelView.Shape m_Shape = TextureModelView.Shape.Ball; [SerializeField] GameObject m_Model;
        TextureRecipe m_Offer; Texture2D m_OfferTexture;
        TextureModelView m_ModelView; Texture2D m_ModelNormal, m_ModelMask, m_ModelGlow;
        List<Pixels> m_Between; Texture2D m_PaintIn, m_PaintOut, m_PaintMask; Pixels m_PaintInFor, m_PaintOutFor; int m_PaintKey; TextureHeal.Stroke m_Stroke;
        Vector2 m_SaveScroll, m_LibScroll; [SerializeField] string m_LibSearch = ""; List<TextureMaterials.Made> m_Lib;
        [SerializeField] Material m_Template; [SerializeField] TextureSlots m_Slot = new TextureSlots(); [SerializeField] bool m_OwnInPreview = true, m_BumpTouched;
        [SerializeField] string m_UpdateFolder = "", m_UpdateName = ""; // something saved before steps were kept, opened to do it again

        Pixels m_Read; Texture2D m_ReadFor; Pixels m_Small, m_SmallAfter;
        Texture2D m_Before, m_After, m_MapTex;
        readonly Dictionary<Type, Texture2D> m_Thumbs = new Dictionary<Type, Texture2D>();

        // ---- the picture is worked out in the background: a small quick one while a slider moves, then the sharp one
        class Job { public int version; public Task<Pixels> task; public Task<Pixels[]> map; public CancellationTokenSource stop; public View view; }
        const int DraftSide = 224, FullSide = 512;
        Pixels m_Draft;
        readonly TextureChain m_DraftChain = new TextureChain(), m_FullChain = new TextureChain();
        Job m_DraftJob, m_FullJob; int m_Version = 1, m_ShownVersion, m_FullDone; double m_ChangedAt;
        Task<Dictionary<Type, Pixels>> m_ThumbJob; int m_ThumbVersion;
        Task<Pixels> m_HoverJob;
        bool m_Dirty => m_FullDone != m_Version;
        Texture2D m_LastSaved; Material m_LastMat;
        Vector2 m_ToolScroll, m_StepScroll, m_PageScroll;
        bool m_DragSplit;
        Tool m_HoverTool, m_HoverFor; Texture2D m_HoverTex; double m_HoverSince;

        // ---- shrink many / pack
        [SerializeField] TextureShrink.Preset m_Preset = TextureShrink.Preset.Small;
        List<TextureShrink.Row> m_Rows; bool m_WholeProject;
        TextureOps.Slot[] m_Slots = MaskSlots();

        // ---- the tools on the left
        class Tool { public string group; public Func<TextureStep> make; public TextureStep proto; }
        static readonly Tool[] Tools =
        {
            T("PHOTO", () => new StraightenStep()), T("PHOTO", () => new ShadowsStep()), T("MAKE IT TILE", () => new EvenLightingStep()), T("MAKE IT TILE", () => new SeamlessStep()),
            T("MAKE IT TILE", () => new BreakRepeatsStep()), T("MAKE IT TILE", () => new VariationsStep()), T("MAKE IT TILE", () => new ShiftStep()),
            T("FIX", () => new PaintOutStep()), T("MIX", () => new MixStep()), T("MIX", () => new StickerStep()), T("MIX", () => new GlowStep()),
            T("LOOK", () => new ColourStep()), T("LOOK", () => new RecolourStep()), T("LOOK", () => new LikeStep()), T("LOOK", () => new PaletteStep { colours = TexturePalette.ReadyColours(0), paletteName = TexturePalette.Ready[0].name }), T("LOOK", () => new DirtStep()), T("LOOK", () => new EdgeWearStep()), T("LOOK", () => new CrunchStep()),
            T("LOOK", () => new SharpenStep()), T("LOOK", () => new BlurStep()), T("LOOK", () => new PixelArtStep()),
            T("SIZE AND SHAPE", () => new ResizeStep()), T("SIZE AND SHAPE", () => new FlipTurnStep()),
        };
        static Tool T(string g, Func<TextureStep> m) => new Tool { group = g, make = m, proto = m() };

        // ---- look (the Cinematic Editor's palette)
        static readonly Color Accent = new Color(1f, 0.62f, 0.2f);
        static Color Bg => EditorGUIUtility.isProSkin ? new Color(0.12f, 0.125f, 0.15f) : new Color(0.72f, 0.73f, 0.76f);
        static Color Panel => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.215f) : new Color(0.83f, 0.84f, 0.87f);
        static Color PanelHi => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.235f, 0.28f) : new Color(0.9f, 0.9f, 0.93f);
        static Color Ink(float a) => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, a);
        GUIStyle m_Card, m_Head, m_Title, m_Big, m_Name, m_Hint, m_TileTitle, m_TileSub, m_BlockTitle, m_BlockSub, m_Badge, m_Empty, m_Pill, m_PillOn;
        Texture2D m_PanelTex;

        [MenuItem("Tools/Texture Studio", false, 160)]
        public static void Open() { Open(false); var w = GetWindow<TextureStudioWindow>(); if (!w.m_Source) w.UseSelection(); }
        static void Open(bool selection) { var w = GetWindow<TextureStudioWindow>("Texture Studio"); w.minSize = new Vector2(760, 480); if (selection) w.UseSelection(); }
        [MenuItem("Assets/Texture Studio/Edit This Texture", false, 1100)]
        static void OpenWith() { Open(true); GetWindow<TextureStudioWindow>().m_Page = Page.Edit; }
        [MenuItem("Assets/Texture Studio/Shrink Selected Textures", false, 1101)]
        static void OpenShrink() { Open(); var w = GetWindow<TextureStudioWindow>(); w.m_Page = Page.Shrink; w.m_Rows = null; }
        [MenuItem("Assets/Texture Studio/Reopen", false, 1099)]
        static void Reopen() { var o = Selection.activeObject; Open(false); GetWindow<TextureStudioWindow>().ReopenAny(o); }
        [MenuItem("Assets/Texture Studio/Reopen", true)]
        static bool CanReopen() => Selection.activeObject is Material || Selection.activeObject is Texture2D || Selection.activeObject is TextureRecipe;
        [MenuItem("CONTEXT/Material/Reopen In Texture Studio")]
        static void ReopenContext(MenuCommand c) { Open(false); GetWindow<TextureStudioWindow>().ReopenAny(c.context); }
        /// <summary>Opens Texture Studio on a material (how it was made, if it was made here; its texture if not).</summary>
        public static void Edit(UnityEngine.Object materialOrTexture) { Open(false); GetWindow<TextureStudioWindow>().ReopenAny(materialOrTexture); }
        [UnityEditor.Callbacks.OnOpenAsset]
#if UNITY_6000_6_OR_NEWER
        static bool OpenRecipe(EntityId id, int line) { if (!(EditorUtility.EntityIdToObject(id) is TextureRecipe r)) return false; Open(false); GetWindow<TextureStudioWindow>().Load(r); return true; }
#else
        static bool OpenRecipe(int id, int line) { if (!(EditorUtility.InstanceIDToObject(id) is TextureRecipe r)) return false; Open(false); GetWindow<TextureStudioWindow>().Load(r); return true; }
#endif
        [MenuItem("Assets/Texture Studio/Edit This Texture", true)]
        static bool HasTexture() => Selection.activeObject is Texture2D;

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            DecalPlacer.Changed += Repaint; ScenePainter.Changed += Repaint; PaletteStep.Source = () => Source(); PaletteStep.Poke = () => { Changed(true); Repaint(); };
            wantsMouseMove = true;
            if (m_Steps == null) m_Steps = new List<TextureStep>();
            m_Steps.RemoveAll(s => s == null);
            if (!m_Source) RestoreSession();
            Changed(true);
        }
        void OnDisable() { m_DecalModel?.Dispose(); m_DecalModel = null; if (m_DecalTemp) DestroyImmediate(m_DecalTemp); if (m_DecalTempTex) DestroyImmediate(m_DecalTempTex); DecalPlacer.Changed -= Repaint; DecalPlacer.Set(false); ScenePainter.Changed -= Repaint; ScenePainter.Set(false); FreeTiles(); m_PatModel?.Dispose(); m_PatModel = null; m_PaintModel?.Dispose(); m_PaintModel = null; foreach (var x in new UnityEngine.Object[] { m_PatTex, m_PatNormal, m_PatMask, m_PaintTemp, m_TileBase }) if (x) DestroyImmediate(x); FreeTemps(); if (m_FlatMat) DestroyImmediate(m_FlatMat); if (m_Glow) DestroyImmediate(m_Glow); KeepSession(); Undo.undoRedoPerformed -= OnUndo; m_DraftJob?.stop.Cancel(); m_FullJob?.stop.Cancel(); m_DraftJob = m_FullJob = null; Free(); FreeThumbs(); m_ModelView?.Dispose(); m_ModelView = null; foreach (var x in new[] { m_HoverTex, m_ModelNormal, m_ModelMask, m_ModelGlow, m_DarkTex, m_PaintIn, m_PaintOut, m_PaintMask }) if (x) DestroyImmediate(x); }
        void OnUndo() { m_Steps.RemoveAll(s => s == null); if (m_Selected >= m_Steps.Count) m_Selected = m_Steps.Count - 1; Changed(true); Repaint(); }
        void Free() { foreach (var t in new[] { m_Before, m_After, m_MapTex }) if (t) DestroyImmediate(t); m_Before = m_After = m_MapTex = null; }
        void FreeThumbs() { foreach (var t in m_Thumbs.Values) if (t) DestroyImmediate(t); m_Thumbs.Clear(); m_ThumbVersion = 0; }

        void UseSelection() { if (Selection.activeObject is Texture2D t && t != m_Source) SetSource(t); }
        void SetSource(Texture2D t)
        {
            Undo.RecordObject(this, "Texture Studio: pick a texture");
            m_Source = t; m_Read = null; m_Small = null; m_LastSaved = null; m_LastMat = null; m_Recipe = null; m_Offer = null; m_UpdateFolder = m_UpdateName = "";
            m_SaveName = t ? t.name : "";
            // (painted spots belong to the texture they were painted on)
            foreach (var s in m_Steps) { if (s is IPaintable pb) pb.Strokes.Clear(); if (s is StickerStep sk) sk.stamps.Clear(); }
            Changed(true);
        }

        // ---- what you were doing, kept in the project's Library folder for the next time the window opens
        public static string SessionFile = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library", "HienaDevTools", "TextureStudio.json"); // (tests point this somewhere else)
        public static bool KeepSessions = true; // (tests switch it off)

        void KeepSession()
        {
            if (!KeepSessions) return;
            try
            {
                var r = CreateInstance<TextureRecipe>(); Fill(r); r.name = m_SaveName;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SessionFile));
                System.IO.File.WriteAllText(SessionFile, EditorJsonUtility.ToJson(r)); DestroyImmediate(r);
            }
            catch (Exception e) { Debug.LogWarning("[Texture Studio] Couldn't keep what you were doing: " + e.Message); }
        }
        void OnLostFocus() => KeepSession();

        void RestoreSession()
        {
            if (!KeepSessions || !System.IO.File.Exists(SessionFile)) return;
            try
            {
                var r = CreateInstance<TextureRecipe>(); EditorJsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(SessionFile), r);
                if (r.source) { var saved = r.outputs.OfType<TextureRecipe>().FirstOrDefault(); Take(r); m_Recipe = saved; m_SaveName = string.IsNullOrEmpty(r.name) ? r.source.name : r.name; }
                DestroyImmediate(r);
            }
            catch (Exception e) { Debug.LogWarning("[Texture Studio] Couldn't go back to what you were doing: " + e.Message); }
        }

        /// <summary>Writes everything in the window into a recipe.</summary>
        void Fill(TextureRecipe rec)
        {
            rec.bands = m_Bands.Copy(); rec.kind = m_Kind; rec.never = m_Never.Copy(); rec.see = m_See; rec.source = m_Source; rec.steps = m_Steps.Where(s => s != null).Select(Clone).ToList(); rec.normal = m_SaveNormal; rec.height = m_SaveHeight; rec.mask = m_SaveMask; rec.material = m_MakeMaterial;
            rec.bumpiness = m_NormalStrength; rec.shine = m_Smooth; rec.ao = m_AO; rec.heightContrast = m_HeightContrast; rec.heightSmooth = m_HeightSmooth; rec.heightInvert = m_HeightInvert; rec.importAs = m_ImportAs; rec.template = m_Template; rec.slots = m_Slot.Copy();
            if (m_Recipe && rec != m_Recipe && !EditorUtility.IsPersistent(rec)) rec.outputs = new List<UnityEngine.Object> { m_Recipe };
        }
        /// <summary>Puts a recipe's contents in the window.</summary>
        void Take(TextureRecipe r)
        {
            SetSource(r.source);
            m_Steps = r.steps.Where(s => s != null).Select(Clone).ToList(); // (changing things here doesn't change the saved one until you save)
            m_Selected = m_Steps.Count - 1; m_Page = Page.Edit; m_Offer = null; m_BumpTouched = true;
            m_SaveNormal = r.normal; m_SaveHeight = r.height; m_SaveMask = r.mask; m_MakeMaterial = r.material;
            m_NormalStrength = r.bumpiness; m_Smooth = r.shine; m_AO = r.ao; m_HeightContrast = r.heightContrast; m_HeightSmooth = r.heightSmooth; m_HeightInvert = r.heightInvert; m_ImportAs = r.importAs; m_Template = r.template; m_Slot = r.slots != null ? r.slots.Copy() : new TextureSlots();
            m_Bands = r.bands != null ? r.bands.Copy() : new Bands();
            m_Kind = m_Template && r.kind == MatKind.Normal ? MatKind.Mine : r.kind; m_Never = r.never != null ? r.never.Copy() : new NeverRepeats(); m_See = r.see;
            if (m_Template && m_Slot.colour == "") m_Slot = TextureSlots.Guess(m_Template.shader);
            Changed(true); Repaint();
        }

        /// <summary>Everything empty again.</summary>
        void StartOver()
        {
            Undo.RecordObject(this, "Texture Studio: start over");
            SetSource(null); m_Steps.Clear(); m_Selected = -1; m_SaveName = ""; m_Kind = MatKind.Normal; m_Template = null; m_Bands = new Bands(); m_Never = new NeverRepeats(); m_See = SeeThrough.Auto; m_BumpTouched = false; m_NormalStrength = 2f; m_Smooth = 0.4f; m_HeightContrast = 1.2f; m_HeightSmooth = 0.6f; m_HeightInvert = false;
            Free(); FreeThumbs(); m_SmallAfter = null; m_Between = null; m_View = View.SideBySide; m_Page = Page.Edit; KeepSession(); Changed(true);
        }

        /// <summary>Opens anything made here. With its steps when they were kept; otherwise the original (or the texture
        /// itself), ready to do it again and save over it.</summary>
        void ReopenAny(UnityEngine.Object o)
        {
            var r = TextureRecipe.Of(o); if (r) { Load(r); return; }
            var tex = o as Texture2D;
            if (o is Material m) { tex = null; foreach (var slot in new[] { TextureSlots.Guess(m.shader).colour, "_BaseMap", "_MainTex" }) if (!tex && slot != "" && m.HasProperty(slot)) tex = m.GetTexture(slot) as Texture2D; }
            if (!tex) { EditorUtility.DisplayDialog("Texture Studio", (o ? o.name : "That") + " has no texture to open.", "OK"); return; }
            var original = TextureMaterials.OriginalOf(tex); Undo.RecordObject(this, "Texture Studio: reopen");
            SetSource(original ? original : tex); m_Page = Page.Edit; m_Steps.Clear(); m_Selected = -1; m_Template = null; m_Bands = new Bands(); m_Kind = MatKind.Normal; m_See = SeeThrough.Auto; m_BumpTouched = false; m_Offer = null;
            var path = AssetDatabase.GetAssetPath(tex);
            if (original && path.StartsWith("Assets/")) { m_UpdateFolder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); m_UpdateName = System.IO.Path.GetFileNameWithoutExtension(path); m_SaveName = m_UpdateName; }
            ShowNotification(new GUIContent(original ? "Saved before steps were kept: here's its original" : "Opened its texture (it wasn't made here, or before steps were kept)"));
            Repaint();
        }

        /// <summary>Opens a saved texture again: the original, every step and the save options.</summary>
        void Load(TextureRecipe r)
        {
            if (!r.source) { EditorUtility.DisplayDialog("Texture Studio", "The original texture of " + r.name + " isn't in the project any more, so it can't be opened again.", "OK"); return; }
            Undo.RecordObject(this, "Texture Studio: reopen");
            Take(r);
            m_Recipe = r; m_SaveName = TextureMaterials.NameOf(r);
            Repaint();
        }

        static TextureStep Clone(TextureStep s) => (TextureStep)JsonUtility.FromJson(JsonUtility.ToJson(s), s.GetType());
        void OnSelectionChange()
        {
            if (m_Page == Page.Shrink) { m_Rows = null; Repaint(); return; }
            if (m_Page != Page.Edit) return;
            // something made here: offer to open it again with its steps (instead of starting over on top of it)
            m_Offer = TextureRecipe.Of(Selection.activeObject); m_OfferTexture = Selection.activeObject as Texture2D;
            if (m_Offer && m_Offer == m_Recipe) m_Offer = null;
            if (!m_Offer && m_Follow && Selection.activeObject is Texture2D t && t != m_Source && !(m_Recipe && m_Recipe.outputs.Contains(t))) SetSource(t);
            Repaint();
        }

        void Changed(bool now = false)
        {
            // hard pixels make every bump a cliff: they start much lower (until you set it yourself)
            if (!m_BumpTouched) m_NormalStrength = m_Steps.Any(s => s != null && s.on && s.HardPixels) ? 0.35f : 2f;
            m_Version++; m_ChangedAt = EditorApplication.timeSinceStartup - (now ? 1 : 0);
        }

        void Update()
        {
            if (m_Page == Page.Patterns && (m_PatTask != null && m_PatTask.IsCompleted)) Repaint();
            if (m_Page != Page.Edit) return; TilesTick();
            if (m_HoverTool != null && mouseOverWindow != this) { m_HoverTool = null; Repaint(); }
            Hover();
            Work();
            Thumbs();
        }

        void Work()
        {
            if (Source() == null) return;
            if (m_Small == null)
            {
                m_Small = Shrunk(m_Read, FullSide); m_Draft = Shrunk(m_Read, DraftSide);
                Put(ref m_Before, m_Small);
            }
            // the quick one starts at once, and again as soon as it's done if something changed meanwhile
            if (m_DraftJob == null && m_ShownVersion < m_Version && m_FullDone != m_Version && (m_FullJob == null || m_FullJob.version != m_Version)) m_DraftJob = BeginJob(m_DraftChain, m_Draft);
            if (m_DraftJob != null && Done(m_DraftJob)) { if (m_DraftJob.version > m_ShownVersion) Show(m_DraftJob, false); m_DraftJob = null; }
            // the sharp one waits until the slider rests a moment; a newer change stops it
            if (m_FullJob != null && m_FullJob.version != m_Version) m_FullJob.stop.Cancel();
            if (m_FullJob == null && m_FullDone != m_Version && EditorApplication.timeSinceStartup > m_ChangedAt + 0.12) m_FullJob = BeginJob(m_FullChain, m_Small);
            if (m_FullJob != null && Done(m_FullJob)) { if (m_FullJob.version == m_Version) Show(m_FullJob, true); m_FullJob = null; }
        }

        static bool Done(Job j) => j.task.IsCompleted && (j.map == null || j.map.IsCompleted);

        Job BeginJob(TextureChain chain, Pixels from)
        {
            var steps = TextureChain.Copy(m_Steps, out var keys);
            var job = new Job { version = m_Version, stop = new CancellationTokenSource(), view = m_View };
            var stop = job.stop.Token; var real = new Vector2Int(m_Read.w, m_Read.h);
            var bands = m_Bands.Copy(); job.task = Task.Run(() => chain.Run(steps, keys, from, real, stop, bands));
            if (IsMap(m_View) || m_View == View.Model)
            {
                var fin = FinalSize(); bool shrink = m_Steps.Any(s => s.on && s.HardPixels);
                var glow = GlowOn != null ? JsonUtility.FromJson<GlowStep>(JsonUtility.ToJson(GlowOn)) : null; glow?.Prepare(); var view = m_View; float ns = m_NormalStrength, ao = m_AO, sm = m_Smooth, hc = m_HeightContrast, hs = m_HeightSmooth; bool hi = m_HeightInvert;
                job.map = job.task.ContinueWith(k =>
                {
                    var q = k.Result; bool small = shrink && fin.x < q.w && fin.y < q.h; if (small) q = TextureOpsMore.Nearest(q, fin.x, fin.y);
                    Pixels Up(Pixels m) => small ? TextureOpsMore.Nearest(m, k.Result.w, k.Result.h) : m;
                    if (view == View.Glow) return new[] { Up(glow != null ? glow.Map(q) : new Pixels(q.w, q.h)) };
                    return view == View.Model ? new[] { Up(Map(q, View.Normal, 1f, ns, ao, sm, hc, hs, hi)), Up(Map(q, View.Mask, 0f, ns, ao, sm, hc, hs, hi)), glow != null && glow.Any ? Up(glow.Map(q)) : null } : new[] { Up(Map(q, view, 1f, ns, ao, sm, hc, hs, hi)) };
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
            }
            return job;
        }

        void Show(Job j, bool full)
        {
            if (j.task.IsFaulted) { Debug.LogWarning("[Texture Studio] preview failed: " + j.task.Exception.GetBaseException().Message); m_FullDone = m_ShownVersion = j.version; return; }
            if (j.task.IsCanceled) return;
            Put(ref m_After, j.task.Result);
            if (j.map != null && j.map.Status == TaskStatus.RanToCompletion)
            {
                if (j.view == View.Model) { Put(ref m_ModelNormal, j.map.Result[0]); Put(ref m_ModelMask, j.map.Result[1]); if (j.map.Result[2] != null) Put(ref m_ModelGlow, j.map.Result[2]); }
                else Put(ref m_MapTex, j.map.Result[0]);
            }
            m_ShownVersion = j.version;
            m_Between = full ? m_FullChain.Between : m_DraftChain.Between;
            if (full) { m_FullDone = j.version; m_SmallAfter = j.task.Result; }
            Repaint();
        }

        static bool IsMap(View v) => v == View.Normal || v == View.Height || v == View.Mask || v == View.Glow;

        static Pixels Shrunk(Pixels p, int side)
        {
            float k = Mathf.Min(1f, side / (float)Mathf.Max(p.w, p.h));
            return k < 1 ? p.Resized(Mathf.Max(8, Mathf.RoundToInt(p.w * k)), Mathf.Max(8, Mathf.RoundToInt(p.h * k))) : p;
        }

        /// <summary>Puts a picture in a texture, keeping the texture when the size is the same (much quicker).</summary>
        static void Put(ref Texture2D t, Pixels p)
        {
            if (t && t.width == p.w && t.height == p.h) { t.SetPixels(p.c); t.Apply(true); return; }
            if (t) DestroyImmediate(t);
            t = p.ToTexture();
        }

        // pointing at a tool: what it would do, a moment after the mouse stops on it (in the background too)
        void Hover()
        {
            if (m_HoverTool != null && m_HoverFor != m_HoverTool && m_HoverJob == null && m_SmallAfter != null && EditorApplication.timeSinceStartup > m_HoverSince + 0.1)
            {
                m_HoverFor = m_HoverTool; if (m_HoverTex) DestroyImmediate(m_HoverTex);
                var src = m_SmallAfter; var step = m_HoverTool.make().Demo(); if (step == null) { m_HoverTex = null; Repaint(); return; }
                step.Prepare(); var z = FinalSize(); var hb = m_Bands.Copy();
                m_HoverJob = Task.Run(() => { var s = Shrunk(src, DraftSide); return TextureBands.Run(step, s, new TextureRun { scale = s.w / (float)Mathf.Max(1, z.x) }, hb); });
            }
            if (m_HoverJob != null && m_HoverJob.IsCompleted)
            {
                if (m_HoverJob.Status == TaskStatus.RanToCompletion && m_HoverTool != null && m_HoverFor == m_HoverTool) m_HoverTex = m_HoverJob.Result.ToTexture();
                m_HoverJob = null; Repaint();
            }
            if (m_HoverTool == null && m_HoverFor != null) { m_HoverFor = null; if (m_HoverTex) DestroyImmediate(m_HoverTex); m_HoverTex = null; Repaint(); }
        }

        // the little pictures on the tools: once things have been still for a moment
        void Thumbs()
        {
            if (m_ThumbJob == null && m_SmallAfter != null && m_FullDone == m_Version && m_ThumbVersion != m_Version && EditorApplication.timeSinceStartup > m_ChangedAt + 0.8)
            {
                m_ThumbVersion = m_Version; var src = m_SmallAfter; var made = Tools.Select(k => k.make()).ToList();
                m_ThumbJob = Task.Run(() =>
                {
                    var d = new Dictionary<Type, Pixels>(); var mid = Shrunk(src, 192);
                    foreach (var s in made) { try { var q = TextureDemos.Of(s, mid); d[s.GetType()] = TextureDemos.Pair(q.before, q.after); } catch { } }
                    return d;
                });
            }
            if (m_ThumbJob != null && m_ThumbJob.IsCompleted)
            {
                if (m_ThumbJob.Status == TaskStatus.RanToCompletion)
                    foreach (var kv in m_ThumbJob.Result) { m_Thumbs.TryGetValue(kv.Key, out var tex); Put(ref tex, kv.Value); m_Thumbs[kv.Key] = tex; }
                m_ThumbJob = null; Repaint();
            }
        }

        // ================================================================ styles

        void Styles()
        {
            if (m_Card != null && m_PanelTex) return;
            m_PanelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; m_PanelTex.SetPixel(0, 0, Panel); m_PanelTex.Apply();
            m_Card = new GUIStyle { normal = { background = m_PanelTex }, padding = new RectOffset(12, 12, 10, 12), margin = new RectOffset(6, 6, 6, 6) };
            m_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = Ink(0.45f) } };
            m_Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Ink(0.9f) } };
            m_Big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = Ink(0.92f) } };
            m_Name = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = Ink(0.92f) }, clipping = TextClipping.Clip };
            m_Hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Ink(0.5f) } };
            m_TileTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Ink(0.9f) }, clipping = TextClipping.Clip };
            m_TileSub = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Ink(0.5f) }, clipping = TextClipping.Clip };
            m_BlockTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Color.white }, clipping = TextClipping.Clip };
            m_BlockSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }, clipping = TextClipping.Clip };
            m_Badge = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } };
            m_Empty = new GUIStyle(EditorStyles.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink(0.55f) } };
            m_Pill = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 22, padding = new RectOffset(10, 10, 2, 2) };
            m_PillOn = new GUIStyle(m_Pill) { fontStyle = FontStyle.Bold };
        }

        bool Pill(string text, bool on, string tip = "", float width = 0)
        {
            var old = GUI.backgroundColor; if (on) GUI.backgroundColor = Accent;
            bool click = width > 0 ? GUILayout.Button(new GUIContent(text, tip), on ? m_PillOn : m_Pill, GUILayout.Width(width)) : GUILayout.Button(new GUIContent(text, tip), on ? m_PillOn : m_Pill);
            GUI.backgroundColor = old;
            return click;
        }

        // ================================================================ frame

        void OnGUI()
        {
            Styles();
            if (Event.current.type == EventType.MouseMove) Repaint();
            // everything the window holds (the texture, the steps and their settings) can be undone with Ctrl+Z
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint && Event.current.type != EventType.MouseMove) Undo.RecordObject(this, "Texture Studio");
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), Bg);
            const float barH = 46;
            DrawTopBar(new Rect(0, 0, position.width, barH));
            var body = new Rect(0, barH, position.width, position.height - barH);
            if (Event.current.type == EventType.Layout) FreeTemps();
            if (m_Page == Page.Edit) EditPage(body);
            else if (m_Page == Page.Decals) DecalsPage(body);
            else if (m_Page == Page.Patterns) PatternsPage(body);
            else if (m_Page == Page.Paint) PaintPage(body);
            else
            {
                GUILayout.BeginArea(new Rect(body.x + 10, body.y + 6, body.width - 20, body.height - 10));
                m_PageScroll = GUILayout.BeginScrollView(m_PageScroll);
                if (m_Page == Page.Shrink) ShrinkGUI(); else if (m_Page == Page.Pack) PackGUI(); else if (m_Page == Page.Atlas) AtlasGUI(); else LibraryGUI();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        void DrawTopBar(Rect r)
        {
            EditorGUI.DrawRect(r, Panel);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Ink(0.06f));
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 11, r.width - 24, r.height - 12));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Texture Studio", m_Big, GUILayout.Width(140));
            GUILayout.Space(8);
            if (Pill("Edit a texture", m_Page == Page.Edit, "Make it loop, hide the repeat, change its look, then save it (and its maps)")) m_Page = Page.Edit;
            if (Pill("Shrink many", m_Page == Page.Shrink, "Make lots of textures smaller in the build at once")) { if (m_Page != Page.Shrink) m_Rows = null; m_Page = Page.Shrink; }
            if (Pill("Patterns", m_Page == Page.Patterns, "Make bricks, planks, tiles, stones... from sliders, with no photo")) m_Page = Page.Patterns;
            if (Pill("Paint", m_Page == Page.Paint, "Brush moss, dirt or snow onto things in the scene")) m_Page = Page.Paint;
            if (Pill("Decals", m_Page == Page.Decals, "Make blood, posters and bullet holes, and stick them on things in the scene")) m_Page = Page.Decals;
            if (Pill("Library", m_Page == Page.Library, "Everything made with Texture Studio in this project")) { m_Page = Page.Library; m_Lib = null; }
            if (Pill("Make a mask map", m_Page == Page.Pack, "Join greyscale maps (metallic, AO, smoothness) into one texture")) m_Page = Page.Pack;
            if (Pill("Atlas", m_Page == Page.Atlas, "Many materials into one texture and one material (fewer draw calls, PS1 texture pages)")) m_Page = Page.Atlas;
            GUILayout.FlexibleSpace();
            if (m_Page == Page.Edit)
            {
                GUILayout.Label(m_Dirty && m_Source ? "working..." : "", m_Hint, GUILayout.Width(62));
                if (GUILayout.Button(new GUIContent("Start over", "Empties everything: the texture, the steps and the settings (Undo brings it back)"), GUILayout.Height(26), GUILayout.Width(78))) StartOver();
                if (GUILayout.Button(new GUIContent("\u21B6 Undo", "Undo the last change (Ctrl+Z)"), GUILayout.Height(26), GUILayout.Width(70))) { Undo.PerformUndo(); }
                if (GUILayout.Button(new GUIContent("Redo \u21B7", "Redo (Ctrl+Y)"), GUILayout.Height(26), GUILayout.Width(70))) { Undo.PerformRedo(); }
                GUILayout.Space(8);
                using (new EditorGUI.DisabledScope(!m_Source))
                {
                    var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
                    if (GUILayout.Button(new GUIContent("  Save  ", "Asks for a name and puts the texture, its maps and its material in a folder with that name"), new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 }, GUILayout.Height(26), GUILayout.Width(110))) EditorApplication.delayCall += AskSave; // (a window that asks for the name can't open in the middle of drawing)
                    GUI.backgroundColor = old;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ================================================================ edit page: tools | steps + save | viewer

        void EditPage(Rect body)
        {
            const float gap = 6; float most = body.width - 320;
            m_ToolsW = Splitter(1, new Rect(body.x + gap + m_ToolsW, body.y, gap, body.height), true, m_ToolsW, 1, 130, most - m_StepsW, 312);
            m_StepsW = Splitter(2, new Rect(body.x + gap * 2 + m_ToolsW + m_StepsW, body.y, gap, body.height), true, m_StepsW, 1, 250, most - m_ToolsW, 340);
            var tools = new Rect(body.x + gap, body.y + gap, m_ToolsW, body.height - gap * 2);
            var steps = new Rect(tools.xMax + gap, tools.y, m_StepsW, tools.height);
            var view = new Rect(steps.xMax + gap, tools.y, body.xMax - steps.xMax - gap * 2, tools.height);
            m_SaveH = Splitter(3, new Rect(steps.x, steps.yMax - m_SaveH - 4, steps.width, 8), false, m_SaveH, -1, 90, steps.height - 140, 250);
            ToolsColumn(tools);
            StepsColumn(steps);
            Viewer(view);
        }

        const int PickerId = 812345;

        // ---- left: the texture and the tools
        void ToolsColumn(Rect r)
        {
            EditorGUI.DrawRect(r, Panel);
            // the texture
            var src = new Rect(r.x + 10, r.y + 10, r.width - 20, 84);
            var thumb = new Rect(src.x, src.y, 84, 84);
            EditorGUI.DrawRect(thumb, Ink(0.06f));
            if (m_Source) GUI.DrawTexture(thumb, m_Source, ScaleMode.ScaleAndCrop);
            else { EditorGUI.DrawRect(thumb, new Color(Accent.r, Accent.g, Accent.b, thumb.Contains(Event.current.mousePosition) ? 0.35f : 0.18f)); GUI.Label(thumb, "Choose\na texture", new GUIStyle(m_TileTitle) { alignment = TextAnchor.MiddleCenter }); }
            if (m_Source && thumb.Contains(Event.current.mousePosition)) { EditorGUI.DrawRect(new Rect(thumb.x, thumb.yMax - 18, thumb.width, 18), new Color(0, 0, 0, 0.6f)); GUI.Label(new Rect(thumb.x, thumb.yMax - 18, thumb.width, 18), "change", new GUIStyle(m_BlockSub) { alignment = TextAnchor.MiddleCenter }); }
            EditorGUIUtility.AddCursorRect(thumb, MouseCursor.Link);
            var info = new Rect(thumb.xMax + 10, src.y, Mathf.Max(0, src.width - 94), 84);
            if (info.width < 70) { }
            else if (m_Source)
            {
                GUI.Label(new Rect(info.x, info.y + 2, info.width, 20), m_Source.name, m_Name);
                var full = Source();
                GUI.Label(new Rect(info.x, info.y + 22, info.width, 16), full != null ? full.w + " x " + full.h + " pixels" : "", m_Hint);
                if (m_Recipe) GUI.Label(new Rect(info.x, info.y + 37, info.width, 30), "Opened from " + m_Recipe.name, new GUIStyle(m_Hint) { normal = { textColor = Accent } });
                m_Follow = GUI.Toggle(new Rect(info.x, info.y + 64, info.width, 18), m_Follow, new GUIContent(" Follow the Project window", "On: clicking a texture in the Project window opens it here (your steps stay). Off: this one stays open"), new GUIStyle(EditorStyles.toggle) { fontSize = 10, normal = { textColor = Ink(0.5f) } });
            }
            else
            {
                GUI.Label(new Rect(info.x, info.y + 2, info.width, 20), "Pick a texture", m_Name);
                GUI.Label(new Rect(info.x, info.y + 24, info.width, 56), "Click here to choose one, click one in the Project window, or drop one here.", m_Hint);
            }
            // drag and drop onto the square (or the whole source area)
            var evt = Event.current;
            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && src.Contains(evt.mousePosition))
            {
                var t = DragAndDrop.objectReferences.OfType<Texture2D>().FirstOrDefault();
                if (t) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (evt.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); SetSource(t); } evt.Use(); }
            }
            if (evt.type == EventType.MouseDown && evt.button == 0 && (thumb.Contains(evt.mousePosition) || (!m_Source && src.Contains(evt.mousePosition)))) { EditorGUIUtility.ShowObjectPicker<Texture2D>(m_Source, false, "", PickerId); evt.Use(); }
            if (evt.type == EventType.ExecuteCommand && EditorGUIUtility.GetObjectPickerControlID() == PickerId && (evt.commandName == "ObjectSelectorUpdated" || evt.commandName == "ObjectSelectorClosed"))
            {
                if (EditorGUIUtility.GetObjectPickerObject() is Texture2D got && got != m_Source) SetSource(got);
                evt.Use();
            }

            if (m_Offer)
            {
                var o = new Rect(r.x + 8, src.yMax + 8, r.width - 16, 62); EditorGUI.DrawRect(o, new Color(Accent.r, Accent.g, Accent.b, 0.18f)); EditorGUI.DrawRect(new Rect(o.x, o.y, 3, o.height), Accent);
                GUI.Label(new Rect(o.x + 10, o.y + 4, o.width - 16, 24), m_Offer ? TextureMaterials.NameOf(m_Offer) + " was made here" : "", m_TileTitle);
                if (GUI.Button(new Rect(o.x + 10, o.y + 30, 130, 24), new GUIContent("Reopen its steps", "Opens the original with every step, so you can change them"))) { var rec = m_Offer; Load(rec); }
                using (new EditorGUI.DisabledScope(!m_OfferTexture)) if (GUI.Button(new Rect(o.x + 146, o.y + 30, o.width - 154, 24), new GUIContent("Start from it", "Uses the finished texture as a new original"))) { var tx = m_OfferTexture; m_Offer = null; SetSource(tx); }
                src.yMax = o.yMax;
            }
            // the tools
            var list = new Rect(r.x, src.yMax + 12, r.width, r.yMax - src.yMax - 12);
            GUILayout.BeginArea(list);
            m_ToolScroll = GUILayout.BeginScrollView(m_ToolScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            GUILayout.Label(r.width < 260 ? "TOOLS" : "TOOLS: POINT AT ONE TO TRY IT, CLICK TO ADD", new GUIStyle(m_Head) { padding = new RectOffset(10, 0, 0, 0), clipping = TextClipping.Clip }, GUILayout.Width(r.width - 24));
            string group = null;
            Tool hovered = null;
            foreach (var tool in Tools)
            {
                if (tool.group != group)
                {
                    group = tool.group; GUILayout.Space(6);
                    GUILayout.Label(group, new GUIStyle(m_Head) { padding = new RectOffset(10, 0, 0, 0), normal = { textColor = Ink(0.32f) }, clipping = TextClipping.Clip }, GUILayout.Width(r.width - 24));
                }
                var tr = GUILayoutUtility.GetRect(r.width - 24, 52, GUILayout.Width(r.width - 24));
                tr = new Rect(tr.x + 8, tr.y + 2, tr.width - 14, tr.height - 4);
                if (m_Source && tr.Contains(Event.current.mousePosition)) hovered = tool;
                if (ToolTile(tr, tool)) { AddStep(tool.make()); hovered = null; }
            }
            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove)
            {
                // (the scroll view's own coordinates: only count it while the mouse is really over the list)
                var mp = Event.current.mousePosition;
                if (hovered != m_HoverTool) { m_HoverTool = hovered; m_HoverSince = EditorApplication.timeSinceStartup; Repaint(); }
            }
            GUILayout.Space(10);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        bool ToolTile(Rect r, Tool tool)
        {
            bool hover = r.Contains(Event.current.mousePosition) && m_Source;
            EditorGUI.DrawRect(r, hover ? PanelHi : Ink(0.035f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), tool.proto.Tint);
            bool narrow = r.width < 220; var th = new Rect(r.x + 8, r.y + 4, narrow ? Mathf.Min((r.height - 8) * 2 + 2, r.width - 14) : (r.height - 8) * 2 + 2, r.height - 8); // (before | after)
            m_Thumbs.TryGetValue(tool.proto.GetType(), out var tex);
            if (tex) GUI.DrawTexture(th, tex, ScaleMode.StretchToFill);
            else EditorGUI.DrawRect(th, new Color(tool.proto.Tint.r, tool.proto.Tint.g, tool.proto.Tint.b, 0.35f));
            if (narrow) { var nb = new Rect(th.x, th.yMax - 17, th.width, 17); EditorGUI.DrawRect(nb, new Color(0, 0, 0, 0.6f)); GUI.Label(nb, tool.proto.Title, m_BlockSub); }
            else GUI.Label(new Rect(th.xMax + 8, r.y + 4, r.xMax - th.xMax - 12, 18), tool.proto.Title, m_TileTitle);
            if (!narrow) GUI.Label(new Rect(th.xMax + 8, r.y + 20, r.xMax - th.xMax - 12, 28), tool.proto.Blurb, m_TileSub);
            if (hover) { EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), Accent); }
            using (new EditorGUI.DisabledScope(!m_Source))
                return GUI.Button(r, new GUIContent("", tool.proto.Blurb + ". Click to add it to your steps."), GUIStyle.none);
        }

        void AddStep(TextureStep s) { m_Steps.Add(s); m_Selected = m_Steps.Count - 1; Changed(true); }

        // ---- middle: the steps, the selected step's settings, saving
        void StepsColumn(Rect r)
        {
            EditorGUI.DrawRect(r, Panel);
            float saveH = m_SaveH;
            var top = new Rect(r.x, r.y, r.width, r.height - saveH);
            var save = new Rect(r.x, r.yMax - saveH, r.width, saveH);
            EditorGUI.DrawRect(new Rect(save.x, save.y, save.width, 1), Ink(0.06f));

            GUILayout.BeginArea(new Rect(top.x + 10, top.y + 10, top.width - 20, top.height - 14));
            GUILayout.BeginHorizontal();
            GUILayout.Label("YOUR STEPS", m_Head);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Recipes \u25BE", "Your saved step lists: use one, or save these steps as one"), EditorStyles.miniButton, GUILayout.Width(74))) RecipeMenu();
            if (m_Steps.Count > 0 && GUILayout.Button(new GUIContent("Clear steps", "Remove every step, keep the texture"), EditorStyles.miniButton, GUILayout.Width(78))) { m_Steps.Clear(); m_Selected = -1; Changed(true); }
            GUILayout.EndHorizontal();
            m_StepScroll = GUILayout.BeginScrollView(m_StepScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            BandsCard();
            if (m_Steps.Count == 0) EmptySteps();
            int remove = -1, swap = -1; if (m_StepRects.Count != m_Steps.Count) { m_StepRects.Clear(); for (int i = 0; i < m_Steps.Count; i++) m_StepRects.Add(default); }
            for (int i = 0; i < m_Steps.Count; i++)
            {
                var b = GUILayoutUtility.GetRect(top.width - 30, 44, GUILayout.ExpandWidth(true));
                b = new Rect(b.x, b.y + 2, b.width, b.height - 4); if (Event.current.type == EventType.Repaint) m_StepRects[i] = b;
                var act = StepBlock(b, i);
                if (act == 1) remove = i; else if (act == 2) swap = i; else if (act == 3) swap = i + 1;
                if (i == m_Selected && m_Steps[i] != null)
                {
                    // its settings, right under it
                    GUILayout.BeginVertical(new GUIStyle(m_Card) { normal = { background = null }, margin = new RectOffset(0, 0, 0, 6), padding = new RectOffset(10, 6, 4, 8) });
                    var sr = GUILayoutUtility.GetRect(0, 0);
                    GUILayout.Label(m_Steps[i].Explain, m_Hint);
                    GUILayout.Space(4);
                    EditorGUIUtility.labelWidth = 112;
                    using (new EditorGUI.DisabledScope(!m_Steps[i].on)) { SeamlessStep.FootOn = TextureBands.Around(m_Steps[i], m_Bands); if (m_Steps[i].Settings()) Changed(); StepFoot(m_Steps[i]); }
                    EditorGUIUtility.labelWidth = 0;
                    GUILayout.EndVertical();
                    var last = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(last.x, last.y, 2, last.height), m_Steps[i].Tint);
                }
            }
            StepDrop();
            if (remove >= 0) { m_Steps.RemoveAt(remove); if (m_Selected >= m_Steps.Count) m_Selected = m_Steps.Count - 1; Changed(true); }
            if (swap > 0 && swap < m_Steps.Count) { var t = m_Steps[swap]; m_Steps[swap] = m_Steps[swap - 1]; m_Steps[swap - 1] = t; m_Selected = m_Selected == swap ? swap - 1 : m_Selected == swap - 1 ? swap : m_Selected; Changed(true); }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            SaveCard(save);
        }

        void EmptySteps()
        {
            GUILayout.Space(10);
            GUILayout.Label(m_Source ? "Click tools on the left to add them here. They're done from top to bottom, and you can change, reorder or switch them off at any time.\n\nOr start from a recipe:" : "Pick a texture first (left).", new GUIStyle(m_Hint) { fontSize = 11 });
            GUILayout.Space(6);
            using (new EditorGUI.DisabledScope(!m_Source))
            {
                Recipe("Make it loop", "Even lighting, then make it loop", new EvenLightingStep(), new SeamlessStep());
                Recipe("Make it loop, no visible repeat", "Even lighting, make it loop, then hide the repeat", new EvenLightingStep(), new SeamlessStep(), new BreakRepeatsStep());
                Recipe("Already loops: hide the repeat", "For textures that already tile, like most downloaded ones", new BreakRepeatsStep());
                Recipe("Old and dirty", "Dirt and wear, then a little less colour", new DirtStep(), new ColourStep { a = new TextureOps.AdjustSettings { saturation = -0.25f } });
                Recipe("Pixel art", "Big pixels and 16 colours", new PixelArtStep());
                var mine = TextureShelf.Recipes;
                if (mine.Count > 0) { GUILayout.Space(6); GUILayout.Label("YOUR RECIPES", m_Head); }
                foreach (var n in mine) { if (!m_MyRecipes.TryGetValue(n, out var st)) m_MyRecipes[n] = st = TextureShelf.LoadRecipe(n).ToArray(); Recipe(n, "Your own recipe", st); }
            }
        }
        readonly Dictionary<string, TextureStep[]> m_MyRecipes = new Dictionary<string, TextureStep[]>();

        void RecipeMenu()
        {
            var menu = new GenericMenu();
            if (m_Steps.Count > 0) menu.AddItem(new GUIContent("Save these steps as a recipe..."), false, () => EditorApplication.delayCall += () =>
            {
                var n = NamePrompt.Ask("Save recipe", "Name for this list of steps", "My recipe", x => TextureShelf.HasRecipe(x) ? "There's one with that name: it'll be replaced." : "You'll find it under Recipes, in every project.");
                if (n != null) { TextureShelf.SaveRecipe(n, m_Steps); m_MyRecipes.Clear(); ShowNotification(new GUIContent("Saved the recipe " + n)); }
            });
            else menu.AddDisabledItem(new GUIContent("Save these steps as a recipe..."));
            menu.AddSeparator("");
            var mine = TextureShelf.Recipes;
            if (mine.Count == 0) menu.AddDisabledItem(new GUIContent("(no recipes yet)"));
            foreach (var n in mine)
            {
                var nn = n;
                menu.AddItem(new GUIContent("Add to my steps/" + n), false, () => { Undo.RecordObject(this, "Texture Studio: recipe"); m_Steps.AddRange(TextureShelf.LoadRecipe(nn)); m_Selected = m_Steps.Count - 1; Changed(true); Repaint(); });
                menu.AddItem(new GUIContent("Use instead of my steps/" + n), false, () => { Undo.RecordObject(this, "Texture Studio: recipe"); m_Steps = TextureShelf.LoadRecipe(nn); m_Selected = m_Steps.Count - 1; Changed(true); Repaint(); });
                menu.AddItem(new GUIContent("Delete/" + n), false, () => { if (EditorUtility.DisplayDialog("Delete recipe", "Delete the recipe \"" + nn + "\"? Textures already made with it don't change.", "Delete", "Cancel")) { TextureShelf.DeleteRecipe(nn); m_MyRecipes.Clear(); Repaint(); } });
            }
            menu.ShowAsContext();
        }

        void Recipe(string name, string tip, params TextureStep[] steps)
        {
            var r = GUILayoutUtility.GetRect(10, 32, GUILayout.ExpandWidth(true));
            r = new Rect(r.x, r.y + 2, r.width, r.height - 4);
            bool hover = r.Contains(Event.current.mousePosition) && GUI.enabled;
            EditorGUI.DrawRect(r, hover ? PanelHi : Ink(0.04f));
            float x = r.x + 8;
            foreach (var s in steps) { EditorGUI.DrawRect(new Rect(x, r.y + 9, 10, 10), s.Tint); x += 13; }
            GUI.Label(new Rect(x + 6, r.y, r.width - x, r.height), name, new GUIStyle(m_TileTitle) { alignment = TextAnchor.MiddleLeft });
            if (GUI.Button(r, new GUIContent("", tip), GUIStyle.none)) { m_Steps.AddRange(steps.Select(Clone)); m_Selected = m_Steps.Count - 1; Changed(true); }
        }

        /// <summary>A step as a coloured block. Returns 1 remove, 2 up, 3 down.</summary>
        int StepBlock(Rect b, int i)
        {
            var st = m_Steps[i];
            bool sel = i == m_Selected;
            var col = st.on ? st.Tint : new Color(0.35f, 0.35f, 0.37f);
            EditorGUI.DrawRect(b, new Color(col.r * 0.62f, col.g * 0.62f, col.b * 0.62f, 1));
            if (sel) { var w = Color.white; EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 2), w); EditorGUI.DrawRect(new Rect(b.x, b.yMax - 2, b.width, 2), w); EditorGUI.DrawRect(new Rect(b.x, b.y, 2, b.height), w); EditorGUI.DrawRect(new Rect(b.xMax - 2, b.y, 2, b.height), w); }
            EditorGUI.DrawRect(new Rect(b.x, b.y, 30, b.height), new Color(0, 0, 0, 0.18f));
            GUI.Label(new Rect(b.x, b.y, 30, b.height), (i + 1).ToString(), m_Badge);
            GUI.Label(new Rect(b.x + 38, b.y + 5, b.width - 116, 18), st.Title, m_BlockTitle);
            GUI.Label(new Rect(b.x + 38, b.y + 23, b.width - 116, 16), st.on ? st.Summary : "switched off", m_BlockSub);
            // buttons on the right
            int act = 0;
            var bx = new Rect(b.xMax - 54, b.y + 11, 22, 22);
            if (IconButton(bx, st.on ? "◉" : "○", st.on ? "Switch this step off (to compare)" : "Switch it back on")) { st.on = !st.on; Changed(true); }
            bx.x += 25; if (IconButton(bx, "✕", "Remove this step")) act = 1;
            // grab it anywhere else: a click opens it, a drag moves it up or down the list
            var grab = new Rect(b.x, b.y, b.width - 58, b.height); var e = Event.current; int id = GUIUtility.GetControlID("TextureStep".GetHashCode(), FocusType.Passive, grab); EditorGUIUtility.AddCursorRect(grab, m_StepDrag == i && m_StepMoving ? MouseCursor.MoveArrow : MouseCursor.Link);
            GUI.Label(new Rect(b.xMax - 72, b.y + 13, 12, 18), new GUIContent("\u2261", "Drag to move it up or down"), new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1, 1, 1, 0.45f) }, fontSize = 13 });
            if (act == 0 && e.type == EventType.MouseDown && e.button == 0 && grab.Contains(e.mousePosition)) { GUIUtility.hotControl = id; m_StepDrag = i; m_StepMoving = false; m_StepFrom = e.mousePosition; e.Use(); }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag) { if (!m_StepMoving && (e.mousePosition - m_StepFrom).magnitude > 5) m_StepMoving = true; m_StepY = e.mousePosition.y; e.Use(); Repaint(); }
            else if (GUIUtility.hotControl == id && e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; if (!m_StepMoving) { m_Selected = sel ? -1 : i; m_StepDrag = -1; } else m_StepDropNow = true; e.Use(); }
            if (m_StepMoving && m_StepDrag == i && e.type == EventType.Repaint) EditorGUI.DrawRect(b, new Color(0, 0, 0, 0.45f));
            return act;
        }

        readonly List<Rect> m_StepRects = new List<Rect>(); int m_StepDrag = -1; bool m_StepMoving, m_StepDropNow; Vector2 m_StepFrom; float m_StepY;
        int StepSlot() { int to = 0; for (int k = 0; k < m_StepRects.Count; k++) if (m_StepY > m_StepRects[k].center.y) to = k + 1; return to; }
        // (where it would land is shown as a line; letting go puts it there)
        void StepDrop()
        {
            if (m_StepDrag < 0 || m_StepDrag >= m_Steps.Count) { m_StepDrag = -1; m_StepMoving = m_StepDropNow = false; return; }
            int to = StepSlot();
            if (m_StepMoving && Event.current.type == EventType.Repaint && m_StepRects.Count > 0) { float y = to < m_StepRects.Count ? m_StepRects[to].y - 2 : m_StepRects[m_StepRects.Count - 1].yMax + 1; var r0 = m_StepRects[0]; EditorGUI.DrawRect(new Rect(r0.x, y - 1, r0.width, 3), Accent); }
            if (!m_StepDropNow) return; m_StepDropNow = false; m_StepMoving = false; int from = m_StepDrag; m_StepDrag = -1; if (to > from) to--; if (to == from) return;
            var s = m_Steps[from]; var picked = m_Selected >= 0 && m_Selected < m_Steps.Count ? m_Steps[m_Selected] : null; m_Steps.RemoveAt(from); m_Steps.Insert(to, s); m_Selected = picked != null ? m_Steps.IndexOf(picked) : -1; Changed(true); GUIUtility.ExitGUI();
        }

        bool IconButton(Rect r, string glyph, string tip)
        {
            bool hover = r.Contains(Event.current.mousePosition) && GUI.enabled;
            EditorGUI.DrawRect(r, hover ? new Color(1, 1, 1, 0.22f) : new Color(0, 0, 0, 0.18f));
            var st = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = GUI.enabled ? Color.white : new Color(1, 1, 1, 0.3f) } };
            GUI.Label(r, glyph, st);
            return GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
        }

        void SaveCard(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 12));
            m_SaveScroll = GUILayout.BeginScrollView(m_SaveScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            GUILayout.Label("WHAT SAVE MAKES", m_Head);
            GUILayout.BeginHorizontal();
            bool n = Check(m_SaveNormal, "Normal map", "Bumps for lighting: bright parts come out raised");
            bool h = Check(m_SaveHeight, "Height map", "Greyscale height: the material uses it for depth (parallax), and terrain can too. See it with the Height button over the picture");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool m = Check(m_SaveMask, "AO + smoothness", "Darker cracks (ambient occlusion) and shine, packed the way URP's Lit material reads them");
            bool mat = Check(m_MakeMaterial, "A material", "A material using everything above, ready to drag onto a model");
            GUILayout.EndHorizontal();
            if (n != m_SaveNormal || h != m_SaveHeight || m != m_SaveMask || mat != m_MakeMaterial) { m_SaveNormal = n; m_SaveHeight = h; m_SaveMask = m; m_MakeMaterial = mat; if (mat) { m_SaveNormal = true; m_SaveMask = true; } }
            EditorGUIUtility.labelWidth = 100;
            bool model = m_View == View.Model;
            using (new EditorGUI.DisabledScope(!m_SaveNormal)) if (Num(ref m_NormalStrength, "Bumpiness", "How deep the normal map's bumps look", 0.05f, 8f)) { m_BumpTouched = true; if (model || m_View == View.Normal) Changed(); }
            using (new EditorGUI.DisabledScope(!m_SaveMask)) if (Num(ref m_Smooth, "Shine", "How smooth and shiny the surface is", 0f, 1f) && (model || m_View == View.Mask)) Changed();
            if (m_SaveHeight)
            {
                if (Num(ref m_HeightContrast, "Height contrast", "How far apart high and low are", 0.3f, 4f)) Changed();
                if (Num(ref m_HeightSmooth, "Height smoothing", "Rounds off small noise", 0f, 4f)) Changed();
                bool inv = EditorGUILayout.ToggleLeft(new GUIContent("Dark parts are high", "For textures where the cracks are bright"), m_HeightInvert); if (inv != m_HeightInvert) { m_HeightInvert = inv; Changed(); }
            }
            if (m_MakeMaterial) MaterialCard();
            var names = new[] { new GUIContent("Same as the original") }.Concat(TextureShrink.PresetNames.Select(x => new GUIContent(x))).ToArray();
            m_ImportAs = EditorGUILayout.IntPopup(new GUIContent("Build size", "How big and how compressed the new textures are in the game"), m_ImportAs, names, new[] { -1, 0, 1, 2, 3 });
            EditorGUIUtility.labelWidth = 0;
            if (m_LastSaved && GUILayout.Button("Saved: " + m_LastSaved.name + (m_LastMat ? " + material" : "") + "  (show)", EditorStyles.linkLabel)) EditorGUIUtility.PingObject(m_LastMat ? (UnityEngine.Object)m_LastMat : m_LastSaved);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void SlotPick(List<string> slots, ref string v, string label)
        {
            var show = new[] { "(not used)" }.Concat(slots.Select(s => s.TrimStart('_'))).ToArray(); int i = Mathf.Max(0, slots.IndexOf(v) + 1), n = EditorGUILayout.Popup(label, i, show);
            if (n != i) { v = n == 0 ? "" : slots[n - 1]; if (m_View == View.Model) Changed(); }
        }

        static bool Num(ref float v, string label, string tip, float min, float max) { float x = EditorGUILayout.Slider(new GUIContent(label, tip), v, min, max); if (Mathf.Approximately(x, v)) return false; v = x; return true; }

        bool Check(bool v, string label, string tip) => EditorGUILayout.ToggleLeft(new GUIContent(label, tip), v, GUILayout.Width(150));

        // ---- right: the picture
        [SerializeField] bool m_PrevCompare; Texture2D m_PrevTex; Pixels m_PrevFor;
        /// <summary>The picture just before the open step (or the last one), at the scale the result is shown.</summary>
        bool PrevPicture(out Texture2D tex, out float ratio, out string name)
        {
            tex = null; ratio = 1; name = "BEFORE"; if (m_Between == null || m_Between.Count < 2) return false; var on = m_Steps.Where(s => s.on).ToList(); if (on.Count < 2) return false;
            int k = m_Selected >= 0 && m_Selected < m_Steps.Count && m_Steps[m_Selected].on ? on.IndexOf(m_Steps[m_Selected]) : on.Count - 1; if (k < 0 || k >= m_Between.Count) return false;
            var p = m_Between[k]; if (p != m_PrevFor) { m_PrevFor = p; Put(ref m_PrevTex, p); } tex = m_PrevTex; var last = m_Between[m_Between.Count - 1]; ratio = last.w / (float)Mathf.Max(1, p.w); name = "BEFORE " + on[k].Title.ToUpper(); return tex;
        }

        void Viewer(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(0.07f, 0.075f, 0.09f));
            // the bar on top
            var bar = new Rect(r.x, r.y, r.width, 36);
            EditorGUI.DrawRect(bar, Panel);
            GUILayout.BeginArea(new Rect(bar.x + 8, bar.y + 7, bar.width - 16, 26));
            GUILayout.BeginHorizontal();
            if (Pill("Side by side", m_View == View.SideBySide, "Before and after next to each other")) SetView(View.SideBySide);
            if (Pill("Slider", m_View == View.Compare, "Before and after in one picture: drag the line")) SetView(View.Compare);
            if (Pill("After", m_View == View.After, "The result")) SetView(View.After);
            if (Pill("Before", m_View == View.Before, "The original")) SetView(View.Before);
            if ((m_View == View.SideBySide || m_View == View.Compare || m_View == View.Before) && m_Steps.Count(s => s.on) > 1 && Pill("Last step only", m_PrevCompare, "Before shows the picture just before the last step (or the step you have open), instead of the start")) m_PrevCompare = !m_PrevCompare;
            if (Pill("3D", m_View == View.Model, "On a ball, a wall, a floor or your own model")) SetView(View.Model);
            GUILayout.Space(8);
            if (Pill("Normal", m_View == View.Normal, "The normal map it'll make")) SetView(View.Normal);
            if (Pill("Height", m_View == View.Height, "The height map it'll make")) SetView(View.Height);
            if (Pill("AO + shine", m_View == View.Mask, "Ambient occlusion and smoothness")) SetView(View.Mask);
            if (GlowOn != null && Pill("Glow", m_View == View.Glow, "The glow map it'll make")) SetView(View.Glow);
            if (m_MakeMaterial && m_Kind == MatKind.NeverRepeats) { GUILayout.Space(8); if (Pill("Never repeats", m_NeverPreview, "Show it the way the Never repeats material draws it. Off: the plain texture")) m_NeverPreview = !m_NeverPreview; }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(r.xMax - 386, r.yMax - 28, 378, 26));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Repeat", m_Hint, GUILayout.Width(40));
            foreach (int k in new[] { 1, 2, 3, 8 }) if (Pill(k + "x" + k, m_Repeat == k, "Show it repeated " + k + " x " + k + (k == 8 ? ", like a big surface from far away" : ""), 36)) m_Repeat = k;
            if (Pill("Edges", m_Edges, "Show where each tile starts, to hunt for seams", 50)) m_Edges = !m_Edges;
            if (Pill("Spots", m_ShowSpots, "Glows over the spots that give the repeat away. Paint them out, or use Hide the repeat", 50)) m_ShowSpots = !m_ShowSpots;
            if (Pill("Foot", m_Bands.on, "For walls: keeps a band at the bottom (and one at the top if you want) while the rest is made to loop", 46)) { m_Bands.on = !m_Bands.on; if (m_Bands.on && m_SmallAfter != null) { var f = TextureBands.Find(m_Small ?? m_SmallAfter); if (f > 0.02f) m_Bands.foot = f; } Changed(true); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            var area = new Rect(r.x, bar.yMax, r.width, r.height - bar.height - 32);
            if (!m_Source) { GUI.Label(area, "Pick a texture on the left to start.", m_Empty); return; }
            if (m_Selected >= 0 && m_Selected < m_Steps.Count && m_Steps[m_Selected] is IPaintable po && m_Steps[m_Selected].on && po.Painting && m_Between != null && !IsMap(m_View) && m_View != View.Model) { PaintView(area, po); FooterLine(r, AfterTiles()); return; }
            if (m_Selected >= 0 && m_Selected < m_Steps.Count && m_Steps[m_Selected] is StickerStep sks && sks.on && m_Between != null && !IsMap(m_View) && m_View != View.Model) { StickerView(area, sks); FooterLine(r, AfterTiles()); return; }
            if (m_Selected >= 0 && m_Selected < m_Steps.Count && m_Steps[m_Selected].on && m_Between != null && !IsMap(m_View) && m_View != View.Model) { if (m_Steps[m_Selected] is StraightenStep str) { StraightenView(area, str); FooterLine(r, AfterTiles()); return; } if (m_Steps[m_Selected] is RecolourStep rcs && rcs.picking) { PickView(area, rcs); FooterLine(r, AfterTiles()); return; } }
            if (m_View == View.Model) { ModelArea(area); FooterLine(r, AfterTiles()); return; }
            var m_Before = this.m_Before; bool map = IsMap(m_View);
            var after = map ? m_MapTex : m_After;
            bool trying = m_HoverTool != null && m_HoverTex && !map;
            if (trying) after = m_HoverTex;
            if (!m_Before || !after) { GUI.Label(area, "working...", m_Empty); return; }

            int bt = AfterTiles();
            float nA = m_Repeat, nB = m_Repeat * bt;          // before is shown at the same scale as after
            if (trying) nB *= m_HoverTool.proto.Across;
            string beforeName = "BEFORE"; if (m_PrevCompare && !trying && !map && PrevPicture(out var prev, out float ratio, out beforeName)) { m_Before = prev; nB = nA * ratio; }
            if (m_Bands.on && !map && m_View != View.Compare) { BandsView(area, after, trying); FooterLine(r, bt); return; }
            if (m_View == View.SideBySide)
            {
                // two pictures, same scale, no slider
                float half = (area.width - 12) / 2;
                var la = new Rect(area.x, area.y + 18, half, area.height - 18); var ra = new Rect(area.x + half + 12, area.y + 18, half, area.height - 18);
                var lp = Fit(la, m_Before.width / (float)m_Before.height, 10); var rp = Fit(ra, after.width / (float)after.height, 10);
                float side = Mathf.Min(lp.width, rp.width);
                lp = new Rect(la.center.x - side / 2, la.center.y - side / 2, side, side); rp = new Rect(ra.center.x - side / 2, ra.center.y - side / 2, side, side);
                EditorGUI.DrawRect(new Rect(lp.x - 1, lp.y - 1, lp.width + 2, lp.height + 2), new Color(0, 0, 0, 0.6f));
                EditorGUI.DrawRect(new Rect(rp.x - 1, rp.y - 1, rp.width + 2, rp.height + 2), trying ? Accent : new Color(0, 0, 0, 0.6f));
                Pic(lp, m_Before, nB); bool never = NeverShown && !map && !trying;
                if (never) DrawNever(rp, after, TilesReady ? nB : nA); else Pic(rp, after, nA);
                if (!map && !trying && !never) Spots(rp, nA);
                GUI.Label(new Rect(lp.x, lp.y - 20, lp.width, 18), beforeName, m_Head);
                GUI.Label(new Rect(rp.x, rp.y - 20, rp.width, 18), trying ? "TRYING: + " + m_HoverTool.proto.Title.ToUpper() + "   (an example, click to add it)" : "AFTER", trying ? new GUIStyle(m_Head) { normal = { textColor = Accent } } : m_Head);
                if (m_Edges) { Edges(lp, (int)nB); Edges(rp, (int)nA); }
                FooterLine(r, bt);
                return;
            }
            var pic = Fit(area, after.width / (float)after.height, 14);
            EditorGUI.DrawRect(new Rect(pic.x - 1, pic.y - 1, pic.width + 2, pic.height + 2), trying ? Accent : new Color(0, 0, 0, 0.6f));
            if (trying) GUI.Label(new Rect(pic.x + 8, pic.yMax - 22, pic.width, 18), "TRYING: + " + m_HoverTool.proto.Title.ToUpper() + "   (click to add it)", new GUIStyle(m_Head) { normal = { textColor = Accent } });
            if (m_View == View.Compare)
            {
                float s = Mathf.Clamp01(m_Split);
                float sx = pic.x + pic.width * s;
                if (SeeNow != SeeThrough.Solid) GUI.DrawTextureWithTexCoords(pic, TextureAlpha.Checks, new Rect(0, 0, pic.width / 32, pic.height / 32));
                GUI.DrawTextureWithTexCoords(new Rect(pic.x, pic.y, pic.width * s, pic.height), m_Before, new Rect(0, 0, nB * s, nB));
                GUI.DrawTextureWithTexCoords(new Rect(sx, pic.y, pic.width * (1 - s), pic.height), after, new Rect(nA * s, 0, nA * (1 - s), nA));
                EditorGUI.DrawRect(new Rect(sx - 1, pic.y, 2, pic.height), Color.white);
                var knob = new Rect(sx - 14, pic.center.y - 14, 28, 28);
                EditorGUI.DrawRect(knob, Accent);
                GUI.Label(knob, "◀▶", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9, normal = { textColor = Color.black } });
                GUI.Label(new Rect(pic.x + 8, pic.y + 6, 300, 18), beforeName, new GUIStyle(m_Head) { normal = { textColor = new Color(1, 1, 1, 0.8f) } });
                GUI.Label(new Rect(pic.xMax - 60, pic.y + 6, 60, 18), "AFTER", new GUIStyle(m_Head) { normal = { textColor = new Color(1, 1, 1, 0.8f) } });
                EditorGUIUtility.AddCursorRect(new Rect(sx - 10, pic.y, 20, pic.height), MouseCursor.ResizeHorizontal);
                var e = Event.current;
                if (e.type == EventType.MouseDown && pic.Contains(e.mousePosition)) { m_DragSplit = true; m_Split = (e.mousePosition.x - pic.x) / pic.width; e.Use(); }
                if (e.type == EventType.MouseDrag && m_DragSplit) { m_Split = Mathf.Clamp01((e.mousePosition.x - pic.x) / pic.width); e.Use(); Repaint(); }
                if (e.type == EventType.MouseUp) m_DragSplit = false;
            }
            else
            {
                var tex = m_View == View.Before ? m_Before : after;
                float n = m_View == View.Before ? nB : nA;
                if (NeverShown && !map && !trying && m_View == View.After) DrawNever(pic, tex, TilesReady ? nB : n); else { Pic(pic, tex, n); if (m_View == View.After && !trying) Spots(pic, n); }
            }
            if (m_Edges) Edges(pic, m_View == View.Before ? (int)nB : m_Repeat);
            FooterLine(r, bt);
        }

        static void Edges(Rect pic, int n)
        {
            var c = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            for (int k = 1; k < n; k++)
            {
                EditorGUI.DrawRect(new Rect(pic.x + pic.width * k / n - 0.5f, pic.y, 1, pic.height), c);
                EditorGUI.DrawRect(new Rect(pic.x, pic.y + pic.height * k / n - 0.5f, pic.width, 1), c);
            }
        }

        void FooterLine(Rect r, int bt)
        {
            var size = FinalSize();
            string line = m_Steps.Any(s => s.on) ? "Result: " + size.x + " x " + size.y + " pixels" + (bt > 1 ? "  (" + bt + "x bigger than the original)" : "") + ".  " + ScoreLine()
                                                  : "No steps yet: click a tool on the left.";
            GUI.Label(new Rect(r.x + 10, r.yMax - 24, r.width - 406, 18), new GUIContent(line, "The preview is a smaller copy; saving uses the full size. The number is how plainly the repeat shows when you squint: under 15 it's hard to see"), new GUIStyle(m_Hint) { wordWrap = false, clipping = TextClipping.Clip });
        }

        void SetView(View v) { if (m_View == v) return; m_View = v; if (IsMap(v)) { if (m_MapTex) DestroyImmediate(m_MapTex); m_MapTex = null; Changed(true); } if (v == View.Model) Changed(true); }

        // ---- the result on a 3D shape
        void ModelArea(Rect area)
        {
            if (m_ModelView == null) m_ModelView = new TextureModelView();
            var top = new Rect(area.x + 8, area.y + 6, area.width - 16, 24);
            GUILayout.BeginArea(top); GUILayout.BeginHorizontal();
            for (int k = 0; k < TextureModelView.ShapeNames.Length; k++)
                if (Pill(TextureModelView.ShapeNames[k], (int)m_Shape == k)) { m_Shape = (TextureModelView.Shape)k; m_ModelView.Face(m_Shape); }
            if (m_Shape == TextureModelView.Shape.Model) m_Model = (GameObject)EditorGUILayout.ObjectField(m_Model, typeof(GameObject), false, GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            if (m_Kind == MatKind.Mine && m_Template && Pill("My material", m_OwnInPreview, "Shows it with the material under Shader from (if that shader can be shown here)")) m_OwnInPreview = !m_OwnInPreview;
            GUILayout.EndHorizontal(); GUILayout.EndArea();
            var pic = new Rect(area.x + 8, top.yMax + 8, area.width - 16, area.height - 44);
            var paintStep = m_Selected >= 0 && m_Selected < m_Steps.Count && m_Steps[m_Selected].on ? m_Steps[m_Selected] : null; bool onModel = paintStep is StickerStep || paintStep is IPaintable ipa && ipa.Painting; if (onModel) PaintOnModel(pic, paintStep); if (m_ModelView.Mouse(pic, onModel ? 1 : 0)) Repaint();
            if (m_Shape == TextureModelView.Shape.Model && !m_Model) { GUI.Label(pic, "Drop a model or prefab in the box above.", m_Empty); return; }
            if (!m_After || !m_ModelNormal || !m_ModelMask) { GUI.Label(pic, "working...", m_Empty); return; }
            if (Event.current.type != EventType.Repaint) return;
            // the same size of stones whatever the result covers: a result that's 3 tiles across is repeated 3 times less
            m_ModelView.Set(m_After, m_SaveNormal ? m_ModelNormal : null, m_SaveMask ? m_ModelMask : null, m_Repeat, m_Steps.Any(s => s.on && s.HardPixels), m_MakeMaterial ? SeeNow : SeeThrough.Solid);
            if (NeverShown) m_ModelView.SetNever(m_Never, TilesReady ? m_TileBase : m_After, m_SaveNormal ? m_ModelNormal : null, m_SaveMask ? m_ModelMask : null, TilesReady ? m_Repeat * AfterTiles() : m_Repeat, SeeNow, TilesReady ? m_TileArr : null, NeverTiles != null ? NeverTiles.rare : 0.3f);
            if (m_Kind == MatKind.Mine && m_Template && m_OwnInPreview) m_ModelView.SetOwn(m_Template, m_Slot, m_After, m_SaveNormal ? m_ModelNormal : null, m_SaveMask ? m_ModelMask : null, m_Repeat);
            m_ModelView.SetGlow(GlowOn != null && GlowOn.Any && m_MakeMaterial && (m_Kind != MatKind.Mine || m_Slot.glow != "") ? m_ModelGlow : null, GlowOn != null ? GlowOn.strength : 1, m_Repeat, m_Kind == MatKind.Mine ? m_Slot.glow : "_EmissionMap");
            var tex = m_ModelView.Render(pic, m_Shape, m_Model);
            if (tex) GUI.DrawTexture(pic, tex, ScaleMode.StretchToFill, false); else GUI.Label(pic, "That one has no mesh.", m_Empty);
            GUI.Label(new Rect(pic.x + 8, pic.yMax - 20, pic.width, 18), onModel ? paintStep is StickerStep ? "Click: put a sticker on the model     Drag: move it     Ctrl + click: take it off     Right-drag: turn the model     Shift + wheel: turn the sticker     Ctrl + wheel: its size" : "Drag: paint on the model     Ctrl + drag: take it away     Right-drag: turn the model     Ctrl + wheel: brush size" : "Drag to turn, wheel to come closer, Shift-drag up and down for the light", new GUIStyle(m_Hint) { normal = { textColor = new Color(1, 1, 1, 0.45f) } });
        }

        // ---- painting spots out
        void PaintView(Rect area, IPaintable po)
        {
            int before = m_Steps.Take(m_Selected).Count(s => s.on);
            if (before >= m_Between.Count) return;
            var pin = m_Between[before]; var pout = m_Between[Mathf.Min(before + 1, m_Between.Count - 1)];
            if (pin != m_PaintInFor) { Put(ref m_PaintIn, pin); m_PaintInFor = pin; }
            var glowing = po as GlowStep; if (glowing != null) GlowPreview(glowing, pin); else if (pout != m_PaintOutFor) { Put(ref m_PaintOut, pout); m_PaintOutFor = pout; }
            float half = (area.width - 12) / 2, aspect = pin.w / (float)pin.h;
            var lp = Fit(new Rect(area.x, area.y + 18, half, area.height - 18), aspect, 10); var rp = Fit(new Rect(area.x + half + 12, area.y + 18, half, area.height - 18), aspect, 10);
            EditorGUI.DrawRect(new Rect(lp.x - 2, lp.y - 2, lp.width + 4, lp.height + 4), Accent);
            // (repeated, like everywhere else: what gives the repeat away only shows then; every copy can be painted on)
            float rep = m_Repeat;
            Pic(lp, m_PaintIn, rep); Pic(rp, glowing != null ? m_DarkTex : m_PaintOut, rep); Spots(lp, rep);
            GUI.Label(new Rect(lp.x, lp.y - 20, lp.width, 18), po.PaintLabel, new GUIStyle(m_Head) { normal = { textColor = Accent } });
            GUI.Label(new Rect(rp.x, rp.y - 20, rp.width, 18), glowing != null ? "WITH THE LIGHTS DOWN" : "AFTER", m_Head);
            if (m_Edges) { Edges(lp, m_Repeat); Edges(rp, m_Repeat); }
            // what's painted
            int key = po.Strokes.Count * 7919 + po.GetHashCode(); foreach (var s in po.Strokes) key += s.points.Count * 31 + (s.erase ? 5 : 0);
            if (po.Strokes.Count > 0)
            {
                if (key != m_PaintKey || !m_PaintMask)
                {
                    m_PaintKey = key; int mw = 224, mh = Mathf.Max(8, Mathf.RoundToInt(mw / aspect)); var mask = TextureHeal.Mask(po.Strokes, mw, mh); var pc = po.PaintColour;
                    var mp = new Pixels(mw, mh); for (int i = 0; i < mask.Length; i++) mp.c[i] = mask[i] ? pc : new Color(pc.r, pc.g, pc.b, 0); Put(ref m_PaintMask, mp);
                }
                GUI.DrawTextureWithTexCoords(lp, m_PaintMask, new Rect(0, 0, rep, rep), true);
            }
            // the brush
            var e = Event.current; int id = GUIUtility.GetControlID("TexturePaintOut".GetHashCode(), FocusType.Passive);
            Vector2 At() => new Vector2(Mathf.Repeat((e.mousePosition.x - lp.x) / lp.width * rep, 1f), Mathf.Repeat((1 - (e.mousePosition.y - lp.y) / lp.height) * rep, 1f));
            if (lp.Contains(e.mousePosition) && e.type == EventType.Repaint) { Handles.color = new Color(1, 1, 1, 0.9f); Handles.DrawWireDisc(e.mousePosition, Vector3.forward, po.Brush * lp.width / rep); }
            if (e.type == EventType.ScrollWheel && lp.Contains(e.mousePosition)) { po.Brush = Mathf.Clamp(po.Brush * (e.delta.y > 0 ? 0.9f : 1.1f), 0.005f, 0.4f); e.Use(); Repaint(); }
            if (glowing != null && glowing.Sel != null && (glowing.Sel.Click || glowing.Sel.Drag) && e.button == 0 && (e.type == EventType.MouseDown && lp.Contains(e.mousePosition) || e.type == EventType.MouseDrag && GUIUtility.hotControl == id || e.type == EventType.MouseUp && GUIUtility.hotControl == id)) { var q = At(); var gp = glowing.Sel; if (e.type == EventType.MouseDown) { GUIUtility.hotControl = id; m_Stroke = null; if (gp.kind == GlowPart.Kind.Wand) gp.clicks.Add(q); else gp.at = q; if (gp.Drag) gp.to = q; } else if (e.type == EventType.MouseDrag) { if (gp.Drag) gp.to = q; else if (gp.kind != GlowPart.Kind.Wand) gp.at = q; } else GUIUtility.hotControl = 0; Changed(); e.Use(); Repaint(); return; }
            if (glowing != null && glowing.picking && e.type == EventType.MouseDown && e.button == 0 && lp.Contains(e.mousePosition)) { var q = At(); glowing.colours.Add(pin[Mathf.Min(pin.w - 1, (int)(q.x * pin.w)), Mathf.Min(pin.h - 1, (int)(q.y * pin.h))]); glowing.picking = false; Changed(); e.Use(); return; }
            if (e.type == EventType.MouseDown && e.button < 2 && lp.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id; m_Stroke = new TextureHeal.Stroke { radius = po.Brush, erase = e.button == 1 }; m_Stroke.points.Add(At()); po.Strokes.Add(m_Stroke); Changed(); e.Use();
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && m_Stroke != null)
            {
                var q = At(); if ((q - m_Stroke.points[m_Stroke.points.Count - 1]).magnitude > po.Brush * 0.25f) { m_Stroke.points.Add(q); Changed(); }
                e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_Stroke = null; Changed(); e.Use(); }
        }

        static Rect Fit(Rect area, float aspect, float pad)
        {
            area = new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2);
            float w = area.width, h = w / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            return new Rect(area.center.x - w / 2, area.center.y - h / 2, w, h);
        }

        int AfterTiles() => m_Steps.Where(s => s.on && !TextureBands.Around(s, m_Bands)).Aggregate(1, (n, b) => n * b.Across);

        Vector2Int FinalSize()
        {
            var full = Source(); if (full == null) return Vector2Int.zero;
            var z = new Vector2Int(full.w, full.h);
            foreach (var s in m_Steps) if (s.on && !TextureBands.Around(s, m_Bands)) z = s.SizeAfter(z);
            return z;
        }

        Pixels Source()
        {
            if (!m_Source) return null;
            if (m_ReadFor != m_Source || m_Read == null) { m_Read = TextureAlpha.Bleed(Pixels.Read(m_Source)); m_ReadFor = m_Source; m_Small = null; }
            return m_Read;
        }

        Pixels Chain(Pixels p, Action<string, float> progress = null)
        {
            var steps = m_Steps.Where(s => s.on).ToList();
            for (int i = 0; i < steps.Count; i++) { progress?.Invoke(steps[i].Title, i / (float)steps.Count); steps[i].Prepare(); p = TextureBands.Run(steps[i], p, TextureRun.Final, m_Bands); }
            return p;
        }

        Pixels Map(Pixels result, View v, float scale) => Map(result, v, scale, m_NormalStrength, m_AO, m_Smooth, m_HeightContrast, m_HeightSmooth, m_HeightInvert);

        static Pixels Map(Pixels result, View v, float scale, float bumps, float aoAmount, float shine, float heightContrast, float heightSmooth, bool heightInvert)
        {
            if (v == View.Normal) return TextureOps.NormalFromHeight(result, bumps * scale, 0.25f, false, false);
            if (v == View.Height) return TextureOpsMore.HeightMap(result, heightContrast, heightSmooth, heightInvert);
            var m = TextureOpsMore.MaskMap(result, aoAmount, shine, 0);
            // shown as two greys side by side would be odd: AO in the picture, shine as a blue tint
            if (scale > 0 && v == View.Mask) { var o = new Pixels(m.w, m.h, true); for (int i = 0; i < m.c.Length; i++) { var c = m.c[i]; o.c[i] = new Color(c.g * (1 - c.a * 0.4f), c.g * (1 - c.a * 0.15f), c.g, 1); } return o; }
            return m;
        }

        // ================================================================ saving

        /// <summary>Asks for a name, then saves everything in a folder with that name.</summary>
        void AskSave()
        {
            if (Source() == null) return;
            if (m_Recipe || m_UpdateName.Length > 0)
            {
                var was = m_Recipe ? TextureMaterials.NameOf(m_Recipe) : m_UpdateName;
                int pick = EditorUtility.DisplayDialogComplex("Save", "Update \"" + was + "\"? Everything that uses it changes with it.\n\nOr save this as something new.", "Update " + was, "Cancel", "Save as new...");
                if (pick == 1) return;
                if (pick == 0) { SaveTo(m_Recipe ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(m_Recipe)).Replace('\\', '/') : m_UpdateFolder, was, false); return; }
            }
            // (a window that's been closed meanwhile has nothing to save)
            string name = NamePrompt.Ask("Save", "Name (everything goes in a folder with this name)", m_SaveName, n =>
            {
                var f = FolderFor(n); var taken = Taken(f, n);
                return f + "/   " + (!AssetDatabase.IsValidFolder(f) ? "new folder" : taken.Count > 0 ? "already there, with " + string.Join(", ", taken.Select(System.IO.Path.GetFileName).Take(3)) + (taken.Count > 3 ? "..." : "") : "already there, goes in the same folder");
            });
            if (name == null) return;
            var folder = FolderFor(name); var there = Taken(folder, name); bool keepBoth = false;
            if (there.Count > 0)
            {
                int pick = EditorUtility.DisplayDialogComplex("Names taken", "That folder already has:\n\n" + string.Join("\n", there.Select(System.IO.Path.GetFileName)) + "\n\nReplacing changes everything that uses them, and can't be undone.", "Replace them", "Cancel", "Keep both");
                if (pick == 1) return;
                keepBoth = pick == 2;
            }
            SaveTo(folder, name, keepBoth);
        }

        /// <summary>The folder with that name next to the original (or the one the original is already in, if that's its name).</summary>
        string FolderFor(string name)
        {
            var src = AssetDatabase.GetAssetPath(m_Source); var dir = src.StartsWith("Assets/") ? System.IO.Path.GetDirectoryName(src).Replace('\\', '/') : "Assets";
            return string.Equals(System.IO.Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase) ? dir : dir + "/" + name;
        }

        List<string> FilesOf(string folder, string name)
        {
            var l = new List<string> { folder + "/" + name + ".png" };
            if (m_SaveNormal || m_MakeMaterial) l.Add(folder + "/" + name + "_Normal.png");
            if (m_SaveHeight) l.Add(folder + "/" + name + "_Height.png"); if (GlowOn != null && GlowOn.Any) l.Add(folder + "/" + name + "_Glow.png");
            if (m_Bands.on && m_Bands.apart) foreach (var part in new[] { "_Foot", "_Wall" }) { l.Add(folder + "/" + name + part + ".png"); if (m_MakeMaterial) l.Add(folder + "/" + name + part + ".mat"); }
            if (m_SaveMask || m_MakeMaterial) l.Add(folder + "/" + name + "_Mask.png");
            if (m_MakeMaterial) l.Add(folder + "/" + name + ".mat");
            if (m_Steps.LastOrDefault(s => s.on) is VariationsStep v && v.save != VariationsStep.Save.OneBig) for (int i = 1; i <= v.Kinds; i++) l.Add(folder + "/" + name + "_Tile" + i + ".png");
            if (NeverTiles != null) foreach (var end in new[] { "_Tiles", "_TilesNormal", "_TilesMask" }) l.Add(folder + "/" + name + end + ".asset");
            l.Add(folder + "/" + name + "_Studio.asset");
            return l;
        }
        List<string> Taken(string folder, string name) => FilesOf(folder, name).Where(System.IO.File.Exists).ToList();

        void SaveTo(string folder, string name, bool keepBoth)
        {
            var full = Source(); if (full == null) return;
            string original = AssetDatabase.GetAssetPath(m_Source);
            // (with everything in one folder and the same name, the result could land on the original: never)
            if (string.Equals(folder + "/" + name + ".png", original, StringComparison.OrdinalIgnoreCase)) name += "_Edited";
            if (keepBoth) { string b = name; for (int n = 2; Taken(folder, name).Count > 0; n++) name = b + " " + n; }
            Texture2D made = null; Material mat = null; var outputs = new List<UnityEngine.Object>();
            try
            {
                if (!AssetDatabase.IsValidFolder(folder)) { var up = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'); AssetDatabase.CreateFolder(up, System.IO.Path.GetFileName(folder)); }
                var on = m_Steps.Where(s => s.on).ToList(); bool hard = on.Any(s => s.HardPixels);
                var vary = on.LastOrDefault() as VariationsStep;
                // the variations are made from what the steps before them give
                Pixels beforeLast = null; var p = full;
                for (int i = 0; i < on.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Texture Studio", on[i].Title + "...", i * 0.75f / on.Count);
                    if (i == on.Count - 1) beforeLast = p;
                    on[i].Prepare(); p = TextureBands.Run(on[i], p, TextureRun.Final, m_Bands);
                }
                var tilesNow = NeverTiles; if (tilesNow != null) p = beforeLast; var result = on.Count > 0 ? p : full.Copy(); var see = m_See == SeeThrough.Auto ? TextureAlpha.Guess(result) : m_See; if (see != SeeThrough.Solid) result = TextureAlpha.Bleed(result);
                Texture2D Put2(Pixels px, string end, bool normal)
                {
                    EditorUtility.DisplayProgressBar("Texture Studio", "Saving " + name + end + "...", 0.85f);
                    var tx = Pixels.SaveAt(px, folder + "/" + name + end + ".png", m_Source, normal, hard && !normal, end == "" && see != SeeThrough.Solid);
                    if (tx && m_ImportAs >= 0) ApplyPreset(tx, (TextureShrink.Preset)m_ImportAs);
                    if (tx) outputs.Add(tx);
                    return tx;
                }
                made = Put2(result, "", false);
                if (!made) return;
                Texture2D normal = null, height = null, mask = null;
                if (m_SaveNormal || m_MakeMaterial) normal = Put2(Map(result, View.Normal, 1f), "_Normal", true);
                if (m_SaveHeight) height = Put2(Map(result, View.Height, 1f), "_Height", false);
                if (m_SaveMask || m_MakeMaterial) mask = Put2(Map(result, View.Mask, 0f), "_Mask", false);
                if (vary != null && vary.save != VariationsStep.Save.OneBig) { var tiles = VariationTiles(vary, beforeLast); for (int i = 0; i < tiles.Count; i++) Put2(tiles[i], "_Tile" + (i + 1), false); }
                Texture2D glowTex = null; var glow = GlowOn; if (glow != null && glow.Any) glowTex = Put2(glow.Map(result), "_Glow", false);
                if (m_Bands.on && m_Bands.apart) SaveApart(result, folder, name, Put2, outputs, see);
                m_Never.sideways = m_Bands.on;
                if (m_MakeMaterial) { mat = TextureMaterials.Make(folder + "/" + name + ".mat", made, normal, mask, height, m_Kind == MatKind.Mine ? m_Template : null, m_Slot, m_Kind == MatKind.NeverRepeats ? m_Never : null, see); if (m_Kind != MatKind.Mine || m_Slot.glow != "") TextureMaterials.Glow(mat, glowTex, glow != null ? glow.strength : 1, m_Kind == MatKind.Mine ? m_Slot.glow : "_EmissionMap"); outputs.Add(mat); }
                if (mat && m_Kind == MatKind.NeverRepeats)
                {
                    if (tilesNow != null)
                    {
                        EditorUtility.DisplayProgressBar("Texture Studio", "Saving the tiles...", 0.92f); tilesNow.Prepare(); var l = VariationTiles(tilesNow, beforeLast).Select(x => x.w > 1024 || x.h > 1024 ? Shrunk(x, 1024) : x).ToList(); Texture2DArray A(string end, List<Pixels> q, bool srgb) { var a = TextureArrays.Save(q, srgb, folder + "/" + name + end + ".asset"); outputs.Add(a); return a; }
                        NeverRepeats.Tiles(mat, A("_Tiles", l, true), A("_TilesNormal", l.Select(x => Map(x, View.Normal, 1f)).ToList(), false), A("_TilesMask", l.Select(x => Map(x, View.Mask, 0f)).ToList(), false), tilesNow.rare);
                    }
                    else NeverRepeats.Tiles(mat, null, null, null, 0);
                    EditorUtility.SetDirty(mat);
                }
                // everything needed to open it again
                var rp = folder + "/" + name + "_Studio.asset"; var rec = AssetDatabase.LoadAssetAtPath<TextureRecipe>(rp); bool isNew = !rec; if (isNew) rec = CreateInstance<TextureRecipe>();
                Fill(rec); rec.outputs = outputs;
                if (isNew) AssetDatabase.CreateAsset(rec, rp); else EditorUtility.SetDirty(rec);
                AssetDatabase.SaveAssetIfDirty(rec); if (mat) AssetDatabase.SaveAssetIfDirty(mat); // (only these: other unsaved work in the project stays as it is)
                m_Recipe = rec; m_SaveName = name; m_UpdateFolder = m_UpdateName = ""; m_Lib = null; KeepSession();
            }
            finally { EditorUtility.ClearProgressBar(); }
            m_LastSaved = made; m_LastMat = mat; EditorGUIUtility.PingObject(mat ? (UnityEngine.Object)mat : made); ShowNotification(new GUIContent("Saved in " + folder));
        }

        static void ApplyPreset(Texture2D t, TextureShrink.Preset p)
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter; if (!imp) return;
            TextureShrink.Set(imp, p, Mathf.Max(t.width, t.height), imp.textureType == TextureImporterType.Sprite);
            imp.SaveAndReimport();
        }

        // ================================================================ library: everything made here

        void OnProjectChange() { m_Lib = null; m_DecalList = null; m_PaintList = null; if (m_Page == Page.Library) Repaint(); }

        void LibraryGUI()
        {
            if (m_Lib == null) m_Lib = TextureMaterials.All();
            GUILayout.BeginVertical(m_Card); GUILayout.BeginHorizontal();
            GUILayout.Label("Made in this project (" + m_Lib.Count + ")", m_Title); GUILayout.FlexibleSpace();
            m_LibSearch = EditorGUILayout.TextField(m_LibSearch, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            if (GUILayout.Button("Look again", EditorStyles.miniButton, GUILayout.Width(78))) { m_Lib = null; GUILayout.EndHorizontal(); GUILayout.EndVertical(); Repaint(); return; }
            GUILayout.EndHorizontal(); GUILayout.Label("Drag one onto a model in the scene, or open it to change it.", m_Hint); GUILayout.EndVertical();
            var list = m_Lib.Where(x => (x.material || x.texture) && (m_LibSearch.Length == 0 || x.name.IndexOf(m_LibSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (list.Count == 0) { GUILayout.Space(30); GUILayout.Label(m_Lib.Count == 0 ? "Nothing yet. Save a texture and it shows up here." : "Nothing called that.", m_Empty); return; }
            const float cw = 168, chh = 226; int per = Mathf.Max(1, Mathf.FloorToInt((position.width - 40) / cw));
            for (int i = 0; i < list.Count; i += per)
            {
                var row = GUILayoutUtility.GetRect(10, chh, GUILayout.ExpandWidth(true));
                for (int k = i; k < Mathf.Min(list.Count, i + per); k++) Card(new Rect(row.x + 6 + (k - i) * cw, row.y + 4, cw - 8, chh - 8), list[k]);
            }
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }

        void Card(Rect r, TextureMaterials.Made m)
        {
            var e = Event.current; bool hover = r.Contains(e.mousePosition); EditorGUI.DrawRect(r, hover ? PanelHi : Panel);
            var pic = new Rect(r.x + 6, r.y + 6, r.width - 12, r.width - 12); var main = m.material ? (UnityEngine.Object)m.material : m.texture; var prev = AssetPreview.GetAssetPreview(main); if (!prev) prev = m.texture ? m.texture : AssetPreview.GetMiniThumbnail(main); // (a material that can't be drawn shows its texture)
            EditorGUI.DrawRect(pic, new Color(0.07f, 0.075f, 0.09f)); if (prev) GUI.DrawTexture(pic, prev, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x + 6, pic.yMax + 3, r.width - 12, 18), new GUIContent(m.name, m.path), m_TileTitle);
            GUI.Label(new Rect(r.x + 6, pic.yMax + 19, r.width - 12, 14), m.HasSteps ? m.recipe.steps.Count + (m.recipe.steps.Count == 1 ? " step" : " steps") : "saved before steps were kept", new GUIStyle(m_TileSub) { wordWrap = false });
            float bw = (r.width - 18) / 2; var b1 = new Rect(r.x + 6, r.yMax - 26, bw, 21); var b2 = new Rect(b1.xMax + 6, b1.y, bw, 21); var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            if (GUI.Button(b1, new GUIContent("Open", m.HasSteps ? "With all its steps" : "Its original texture, to do it again"))) { if (m.recipe) Load(m.recipe); else ReopenAny(main); }
            GUI.backgroundColor = old; if (GUI.Button(b2, new GUIContent("Show", "Find it in the Project window"))) { EditorGUIUtility.PingObject(main); Selection.activeObject = main; }
            if (pic.Contains(e.mousePosition)) EditorGUIUtility.AddCursorRect(pic, MouseCursor.Pan);
            if (e.type == EventType.MouseDrag && pic.Contains(e.mousePosition) && main) { DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new[] { main }; DragAndDrop.StartDrag(m.name); e.Use(); }
        }

        // ================================================================ make a mask map

        static TextureOps.Slot[] MaskSlots() => new[]
        {
            new TextureOps.Slot("Metallic  (red)", 0), new TextureOps.Slot("Ambient occlusion  (green)", 1),
            new TextureOps.Slot("Detail mask  (blue)", 0), new TextureOps.Slot("Smoothness  (alpha)", 0.5f),
        };

        void PackGUI()
        {
            GUILayout.BeginVertical(m_Card);
            GUILayout.Label("Make a mask map", m_Title);
            GUILayout.Label("Join up to four greyscale maps into one texture's red, green, blue and alpha. URP's Lit material reads metallic from red, smoothness from alpha, and occlusion from green. Drop a texture in each slot, or leave it empty and set a value.", m_Hint);
            GUILayout.EndVertical();
            GUILayout.BeginHorizontal();
            foreach (var s in m_Slots)
            {
                GUILayout.BeginVertical(m_Card, GUILayout.Width(220));
                GUILayout.Label(s.label, m_TileTitle);
                s.tex = (Texture2D)EditorGUILayout.ObjectField(s.tex, typeof(Texture2D), false, GUILayout.Width(110), GUILayout.Height(110));
                EditorGUIUtility.labelWidth = 70;
                if (s.tex)
                {
                    s.from = (TextureOps.From)EditorGUILayout.EnumPopup(new GUIContent("Take its", "Which part of that texture to use"), s.from);
                    s.invert = EditorGUILayout.ToggleLeft(new GUIContent("Invert (roughness → smoothness)", "Flips dark and light"), s.invert);
                }
                else s.value = EditorGUILayout.Slider(new GUIContent("Value", "Used everywhere when there's no texture"), s.value, 0, 1);
                EditorGUIUtility.labelWidth = 0;
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            if (GUILayout.Button("Make the mask map", GUILayout.Height(32), GUILayout.Width(220)))
            {
                var outp = TextureOps.Pack(m_Slots, out var problem);
                if (outp == null) ShowNotification(new GUIContent(problem));
                else
                {
                    var like = m_Slots.FirstOrDefault(s => s.tex)?.tex;
                    var made = Pixels.SaveNextTo(outp, like, "_Mask");
                    if (made) { EditorGUIUtility.PingObject(made); ShowNotification(new GUIContent("Saved " + made.name)); }
                }
            }
            GUI.backgroundColor = old;
            if (GUILayout.Button("Clear the slots", GUILayout.Height(32), GUILayout.Width(120))) m_Slots = MaskSlots();
            GUILayout.EndHorizontal();
        }

        // ================================================================ shrink many

        void ShrinkGUI()
        {
            GUILayout.BeginVertical(m_Card);
            GUILayout.Label("Shrink many textures", m_Title);
            GUILayout.Label("Makes textures take less space in the build (and download faster), many at once. Only their import settings change; the picture files aren't touched, and Undo puts everything back.", m_Hint);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (Pill("The selected textures or folders", !m_WholeProject, "Select them in the Project window")) { m_WholeProject = false; m_Rows = null; }
            if (Pill("Every texture in the project", m_WholeProject)) { m_WholeProject = true; m_Rows = null; }
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!TextureShrink.CanUndo))
                if (GUILayout.Button(new GUIContent("Undo last shrink", "Puts back the settings the textures had before"), GUILayout.Width(130), GUILayout.Height(22))) { int n = TextureShrink.Undo(); ShowNotification(new GUIContent("Put back " + n + " textures")); m_Rows = null; }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // the presets as big cards
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TextureShrink.PresetNames.Length; i++)
            {
                var r = GUILayoutUtility.GetRect(150, 74, GUILayout.ExpandWidth(true));
                r = new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8);
                bool on = (int)m_Preset == i, hover = r.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(r, on ? new Color(Accent.r, Accent.g, Accent.b, 0.25f) : hover ? PanelHi : Panel);
                if (on) EditorGUI.DrawRect(new Rect(r.x, r.yMax - 3, r.width, 3), Accent);
                GUI.Label(new Rect(r.x + 10, r.y + 8, r.width - 20, 18), TextureShrink.PresetNames[i], m_TileTitle);
                GUI.Label(new Rect(r.x + 10, r.y + 26, r.width - 20, 40), TextureShrink.PresetHelp[i], m_TileSub);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { m_Preset = (TextureShrink.Preset)i; m_Rows = null; }
            }
            GUILayout.EndHorizontal();

            if (m_Rows == null)
            {
                IEnumerable<Texture2D> texs = m_WholeProject
                    ? AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }).Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
                    : Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets);
                m_Rows = TextureShrink.Rows(texs.Where(t => t && AssetDatabase.GetAssetPath(t).StartsWith("Assets/")), m_Preset);
            }
            GUILayout.BeginVertical(m_Card);
            if (m_Rows.Count == 0) { GUILayout.Label("Select textures or a folder in the Project window, or press \"Every texture in the project\".", m_Empty); GUILayout.EndVertical(); return; }
            long now = m_Rows.Sum(r => r.memNow), after = m_Rows.Sum(r => r.memAfter);
            GUILayout.BeginHorizontal();
            GUILayout.Label(m_Rows.Count + " textures   " + TextureShrink.Size(now) + "  →  about " + TextureShrink.Size(after) + (after < now ? "   (" + Mathf.RoundToInt(100f * (now - after) / Mathf.Max(1, now)) + "% smaller)" : ""), m_Big);
            GUILayout.FlexibleSpace();
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            if (GUILayout.Button("Shrink " + (m_Rows.Count == 1 ? "it" : "all " + m_Rows.Count), GUILayout.Height(30), GUILayout.Width(160)))
            {
                if (m_Rows.Count < 40 || EditorUtility.DisplayDialog("Texture Studio", "Change the import settings of " + m_Rows.Count + " textures? (You can undo it.)", "Shrink", "Cancel"))
                { TextureShrink.Apply(m_Rows, m_Preset); m_Rows = null; ShowNotification(new GUIContent("Done")); GUI.backgroundColor = old; GUILayout.EndHorizontal(); GUILayout.EndVertical(); return; }
            }
            GUI.backgroundColor = old;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("BIGGEST FIRST", m_Head);
            long most = Math.Max(1, m_Rows.Max(r => r.memNow));
            foreach (var r in m_Rows.Take(200))
            {
                var row = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
                bool hover = row.Contains(Event.current.mousePosition);
                if (hover) EditorGUI.DrawRect(row, Ink(0.04f));
                if (r.tex) GUI.DrawTexture(new Rect(row.x + 2, row.y + 3, 20, 20), AssetPreview.GetMiniThumbnail(r.tex), ScaleMode.ScaleToFit);
                GUI.Label(new Rect(row.x + 28, row.y + 4, 220, 18), r.tex.name, m_TileTitle);
                GUI.Label(new Rect(row.x + 250, row.y + 6, 90, 18), r.tex.width + " x " + r.tex.height, m_Hint);
                // a bar: now (grey) and after (accent)
                var bar = new Rect(row.x + 345, row.y + 8, Mathf.Max(60, row.width - 480), 10);
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * r.memNow / most, bar.height), Ink(0.18f));
                EditorGUI.DrawRect(new Rect(bar.x, bar.y + 3, bar.width * r.memAfter / most, 4), Accent);
                GUI.Label(new Rect(bar.xMax + 10, row.y + 6, 130, 18), TextureShrink.Size(r.memNow) + " → " + TextureShrink.Size(r.memAfter), m_Hint);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) EditorGUIUtility.PingObject(r.tex);
            }
            GUILayout.EndVertical();
        }
    }
}
