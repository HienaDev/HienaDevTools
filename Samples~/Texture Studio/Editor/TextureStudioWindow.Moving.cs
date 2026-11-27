using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The "Moving and shaders" page: a flipbook maker (fire, caustics, lava... as looping sprite sheets), a flow map
    /// painter, and a shader maker (HienaDev/Moving) with a live preview.</summary>
    public partial class TextureStudioWindow
    {
        [SerializeField] int m_MovTab; [SerializeField] TextureMoving.Settings m_Flip = new TextureMoving.Settings(); [SerializeField] TextureMoving.Flow m_Flow = new TextureMoving.Flow(); [SerializeField] string m_MovName = "", m_FlowName = "";
        [SerializeField] bool m_FlipParticles = true, m_FlipLoopToo; [SerializeField] int m_ShBakeSize = 1024; [SerializeField] float m_FlipFps = 12, m_ShZoom = 1; System.Threading.CancellationTokenSource m_FlipCts; Texture2D m_FlowLive; Pixels m_FlowLivePx; [SerializeField] float m_FlowBrush = 0.12f, m_FlowStrength = 0.5f; [SerializeField] Texture2D m_FlowUnder;
        Texture2D m_FlipSheetTex; Pixels m_FlipSheetPx; string m_FlipKey, m_FlipWant; System.Threading.Tasks.Task<Pixels> m_FlipTask; Vector2 m_MovScroll; Texture2D m_FlowTex; Vector2? m_FlowLast; double m_MovPaint;

        // ---- the shader maker
        Material m_ShMat; PreviewRenderUtility m_ShPru; [SerializeField] int m_ShMesh = 1; float m_ShYaw = 30, m_ShPitch = 15; [SerializeField] string m_ShName = "My moving material";
        static readonly string[] ShMeshes = { "Quad", "Sphere", "Cube", "Plane", "Cylinder", "Flag" };
        static readonly (string kw, string prop, string name, string tip)[] ShFx =
        {
            ("_SCROLL", "_UseScroll", "Scroll", "The texture slides (and a second layer at its own speed): conveyor belts, rivers, clouds"),
            ("_FLIPBOOK", "_UseFlip", "Flipbook", "Plays a sprite sheet frame by frame (made in the Flipbook tab)"),
            ("_FLOW", "_UseFlow", "Flow map", "The texture drifts the way a flow map says (painted in the Flow map tab): water round rocks, lava"),
            ("_PULSE", "_UsePulse", "Pulse glow", "Glows brighter and dimmer"),
            ("_DISSOLVE", "_UseDissolve", "Dissolve", "Burns away in noise, with a glowing edge"),
            ("_HOLO", "_UseHolo", "Hologram", "See-through with moving lines, flicker and glowing edges"),
            ("_WIND", "_UseWind", "Wind sway", "Bends in the wind, more at the top: plants, flags, hanging cloth"),
            ("_WAVES", "_UseWaves", "Waves", "The surface rises and falls (needs a mesh with enough vertices, like a plane)"),
            ("_RIM", "_UseRim", "Rim light", "Glowing edges where it turns away from you: force fields, ghosts"),
            ("_TOON", "_UseToon", "Toon", "Light in a few flat steps"),
            ("_HUE", "_UseHue", "Colour cycle", "The colours rotate through the rainbow"),
            ("_WOBBLE", "_UseWobble", "Wobble", "The picture wobbles: heat haze, underwater, magic"),
            ("_BILLBOARD", "_UseBillboard", "Face the camera", "A quad that always turns to face you (fires, sprites); not for particles"),
            ("_FLAG", "_UseFlag", "Flag", "Waves like cloth hanging from a pole on the texture's left edge, with light and dark folds (even on a flat quad). Best on a mesh with many vertices, like the Flag mesh"),
            ("_LAYERS", "_UseLayers", "Layers by height and slope", "Other textures on faces that point up (snow, moss, dust), higher up and lower down (rock over grass), with soft or hard edges"),
            ("_UNLIT", "_Unlit", "Unlit", "Ignores lights: glowing things, UI-like looks"),
        };
        static readonly string[] Blends = { "Solid", "Cut-out", "See-through", "Glow (add)" };

        void MovingPage(Rect body)
        {
            var e = Event.current; const float gap = 6; var left = new Rect(body.x + gap, body.y + gap, 340, body.height - gap * 2); var right = new Rect(left.xMax + gap, left.y, body.xMax - left.xMax - gap * 2, left.height);
            EditorGUI.DrawRect(left, Panel); EditorGUI.DrawRect(right, new Color(0.07f, 0.075f, 0.09f));
            GUILayout.BeginArea(new Rect(left.x + 10, left.y + 8, left.width - 20, left.height - 14)); GUILayout.BeginHorizontal(); if (Pill("Flipbook", m_MovTab == 0, "Looping fire, smoke, caustics, lava... as a sprite sheet")) m_MovTab = 0; if (Pill("Flow map", m_MovTab == 1, "Paint which way things drift")) m_MovTab = 1; if (Pill("Shader maker", m_MovTab == 2, "Moving materials: scroll, flipbook, flow, glow, dissolve, hologram, wind...")) m_MovTab = 2; GUILayout.EndHorizontal(); GUILayout.Space(6);
            m_MovScroll = GUILayout.BeginScrollView(m_MovScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none); EditorGUIUtility.labelWidth = 110; GUILayout.BeginVertical(GUILayout.Width(left.width - 36)); // keeps everything clear of the scroll bar
            if (m_MovTab == 0) FlipGUI(); else if (m_MovTab == 1) FlowGUI(); else ShaderGUI();
            GUILayout.EndVertical(); EditorGUIUtility.labelWidth = 0; GUILayout.EndScrollView(); GUILayout.EndArea();
            if (m_MovTab == 0) FlipView(right); else if (m_MovTab == 1) FlowView(right, e); else ShaderView(right, e);
        }
        void MovingTick()
        {
            if (m_MovTab == 0) FlipTick();
            if (EditorApplication.timeSinceStartup - m_MovPaint > 1 / 30.0) { m_MovPaint = EditorApplication.timeSinceStartup; Repaint(); }
        }

        // ================================================================ flipbook

        void FlipGUI()
        {
            var s = m_Flip; if (s.cols == null || s.cols.Length == 0) s.Defaults();
            GUILayout.Label("WHAT", m_Head); int k = (int)s.kind; int nk = GUILayout.SelectionGrid(k, TextureMoving.Names, 3, EditorStyles.miniButton); if (nk != k) { s.kind = (TextureMoving.Kind)nk; s.Defaults(); s.KnobDefaults(); m_MovName = ""; }
            GUILayout.Label(TextureMoving.About[(int)s.kind] + ".", m_Hint);
            if (s.kind >= TextureMoving.Kind.Scroll && m_SmallAfter == null) GUILayout.Label("Open a texture on the Edit page first: this one moves it.", m_Hint);
            GUILayout.Space(4); GUILayout.Label("HOW IT MOVES", m_Head);
            s.wild = EditorGUILayout.Slider(new GUIContent(s.kind == TextureMoving.Kind.Scroll ? "—" : "How wild", "More turbulent, more twisted"), s.wild, 0, 1); s.scale = EditorGUILayout.Slider(s.kind == TextureMoving.Kind.Caustics ? new GUIContent("Hole size", "Bigger bright holes (fewer of them) to the right") : s.kind == TextureMoving.Kind.Electric ? new GUIContent("Glow", "How far the soft glow reaches round the bolts") : new GUIContent("Size", "Bigger shapes to the right"), s.scale, 0, 1);
            s.speed = EditorGUILayout.IntSlider(new GUIContent(s.kind == TextureMoving.Kind.Electric ? "Jumpiness" : "Movement per loop", s.kind == TextureMoving.Kind.Electric ? "How often the bolts jump to a new shape" : "How far it moves in one loop: 1 is gentle, 4 is fast (whole steps, so it still loops)"), Mathf.RoundToInt(s.speed), 1, 4);
            m_FlipFps = EditorGUILayout.Slider(new GUIContent("Playback speed", "Frames a second when it plays (the preview and the saved material). Changes straight away"), m_FlipFps, 2, 30); if (s.kind == TextureMoving.Kind.Scroll) s.dirAngle = EditorGUILayout.Slider("Direction", s.dirAngle, 0, 360);
            s.seed = EditorGUILayout.IntSlider("Seed", s.seed, 1, 999);
            var knobs = TextureMoving.Knobs(s.kind); if (knobs.Length > 0) { GUILayout.Space(4); GUILayout.Label("SHAPE", m_Head); }
            for (int i = 0; i < knobs.Length; i++) { float v = i == 0 ? s.p1 : i == 1 ? s.p2 : s.p3; v = EditorGUILayout.Slider(new GUIContent(knobs[i].name, knobs[i].tip), v, 0, 1); if (i == 0) s.p1 = v; else if (i == 1) s.p2 = v; else s.p3 = v; }
            if (knobs.Length > 0 && GUILayout.Button("Back to its usual shape", EditorStyles.miniButton)) s.KnobDefaults();
            if (s.kind < TextureMoving.Kind.Scroll)
            {
                GUILayout.Space(4); GUILayout.Label("COLOURS (dark to bright)", m_Head); var r = GUILayoutUtility.GetRect(10, 14, GUILayout.ExpandWidth(true)); if (Event.current.type == EventType.Repaint) for (int x = 0; x < (int)r.width; x++) EditorGUI.DrawRect(new Rect(r.x + x, r.y, 1, r.height), FxOps.Ramp(s.cols, s.at, x / r.width));
                GUILayout.BeginHorizontal(); for (int i = 0; i < s.cols.Length; i++) s.cols[i] = EditorGUILayout.ColorField(GUIContent.none, s.cols[i], true, false, false, GUILayout.Width(44)); GUILayout.EndHorizontal(); if (GUILayout.Button("Its usual colours", EditorStyles.miniButton)) s.Defaults();
                PaletteLibrary.Buttons((l, nm) => { var c = PaletteLibrary.AtMost(PaletteLibrary.DarkToBright(l), 8); if (c.Count < 2) return; m_Flip.cols = c.ToArray(); m_Flip.at = Enumerable.Range(0, c.Count).Select(q => q / (float)(c.Count - 1)).ToArray(); Repaint(); }, s.cols.ToList(), TextureMoving.Names[(int)s.kind]);
            }
            GUILayout.Space(4); GUILayout.Label("SHEET", m_Head); s.frames = EditorGUILayout.IntPopup("Frames", s.frames, new[] { "8", "16", "25", "36", "49", "64" }, new[] { 8, 16, 25, 36, 49, 64 }); s.size = EditorGUILayout.IntPopup("Frame size", s.size, new[] { "32", "64", "128", "256", "512" }, new[] { 32, 64, 128, 256, 512 });
            var (c, rr) = TextureMoving.Grid(s.frames); GUILayout.Label(c + " × " + rr + " frames: a " + c * s.size + " × " + rr * s.size + " texture", m_Hint);
            s.dither = EditorGUILayout.ToggleLeft(new GUIContent(" Retro: few colours with dithering", "A PS1 / pixel-art flame"), s.dither); if (s.dither) s.levels = EditorGUILayout.IntSlider("Shades", s.levels, 2, 8);
            GUILayout.Space(6); GUILayout.Label("SAVE", m_Head); if (m_MovName.Length == 0) m_MovName = TextureMoving.Names[(int)s.kind].Replace(" it", ""); m_MovName = EditorGUILayout.TextField("Name", m_MovName);
            m_FlipParticles = EditorGUILayout.ToggleLeft(new GUIContent(" A Particle System too", "A prefab that plays it on particles"), m_FlipParticles);
            if (!TextureMoving.Billboard(s.kind)) m_FlipLoopToo = EditorGUILayout.ToggleLeft(new GUIContent(" Also one frame that scrolls", "A single looping texture with a scrolling material: cheaper on big surfaces"), m_FlipLoopToo);
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent; GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Save the flipbook", "The sprite sheet, a material that plays it, and the extras ticked above, in Assets/Textures/Moving"), GUILayout.Height(28))) SaveFlip(); GUI.backgroundColor = old;
            if (GUILayout.Button(new GUIContent("Only the texture", "Just the sprite sheet (a PNG), no material or anything else, in Assets/Textures/Moving"), GUILayout.Height(28), GUILayout.Width(110))) SaveFlip(true); GUILayout.EndHorizontal();
        }
        void FlipTick()
        {
            var s = m_Flip; if (s.cols == null || s.cols.Length == 0) s.Defaults(); string key = JsonUtility.ToJson(s) + (s.kind >= TextureMoving.Kind.Scroll && m_SmallAfter != null ? m_SmallAfter.GetHashCode().ToString() : "");
            if (m_FlipTask != null && m_FlipTask.IsCompleted) { if (m_FlipTask.IsCanceled || m_FlipTask.Exception?.GetBaseException() is OperationCanceledException) { m_FlipTask = null; } else if (m_FlipTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) { m_FlipSheetPx = m_FlipTask.Result; Put(ref m_FlipSheetTex, m_FlipSheetPx); if (m_FlipSheetTex) { m_FlipSheetTex.wrapMode = TextureWrapMode.Clamp; m_FlipSheetTex.filterMode = s.dither ? FilterMode.Point : FilterMode.Bilinear; } } else Debug.LogWarning("[Texture Studio] flipbook: " + m_FlipTask.Exception?.GetBaseException()); m_FlipKey = m_FlipWant; m_FlipTask = null; }
            // a change while one is being made: stop that one and start again straight away (no waiting for it to finish)
            if (m_FlipTask == null ? key != m_FlipKey : key != m_FlipWant)
            {
                m_FlipCts?.Cancel(); var cts = m_FlipCts = new System.Threading.CancellationTokenSource(); m_FlipWant = key; var copy = s.Copy(); var src = m_SmallAfter; int size = Mathf.Min(copy.size, 128);
                m_FlipTask = System.Threading.Tasks.Task.Run(() => TextureMoving.Sheet(copy, size, src, _ => cts.Token.ThrowIfCancellationRequested()), cts.Token);
            }
        }
        void FlipView(Rect area)
        {
            if (!m_FlipSheetTex) { GUI.Label(area, "Making it...", m_Empty); return; }
            var s = m_Flip; var (cols, rows) = TextureMoving.Grid(s.frames); float fps = Mathf.Max(1, m_FlipFps); int f = (int)(EditorApplication.timeSinceStartup * fps) % s.frames; bool bb = TextureMoving.Billboard(s.kind);
            var big = new Rect(area.x + 10, area.y + 10, Mathf.Min(area.width * 0.6f, area.height - 20), Mathf.Min(area.width * 0.6f, area.height - 20)); EditorGUI.DrawRect(big, bb ? new Color(0.03f, 0.03f, 0.04f) : Color.black);
            var cell = new Rect((f % cols) / (float)cols, (rows - 1 - f / cols) / (float)rows, 1f / cols, 1f / rows);
            if (bb) GUI.DrawTextureWithTexCoords(big, m_FlipSheetTex, cell, true); else { for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) GUI.DrawTextureWithTexCoords(new Rect(big.x + i * big.width / 2, big.y + j * big.height / 2, big.width / 2, big.height / 2), m_FlipSheetTex, cell, true); }
            GUI.Label(new Rect(big.x, big.yMax + 4, big.width, 18), "Playing at " + fps.ToString("0.#") + " frames a second" + (bb ? "" : " (shown 2 × 2, to see it loop across)") + (m_FlipTask != null ? "   updating..." : ""), m_Hint);
            var sheet = new Rect(big.xMax + 10, area.y + 10, area.xMax - big.xMax - 20, area.xMax - big.xMax - 20); if (sheet.width > 60) { EditorGUI.DrawRect(sheet, Color.black); GUI.DrawTexture(sheet, m_FlipSheetTex, ScaleMode.ScaleToFit, true); GUI.Label(new Rect(sheet.x, sheet.yMax + 4, sheet.width, 18), "The sprite sheet", m_Hint); }
        }
        void SaveFlip(bool onlyTexture = false)
        {
            var s = m_Flip.Copy(); var name = TextureShelf.Clean(m_MovName); if (name.Length == 0) name = "Flipbook"; string dir = "Assets/Textures/Moving"; MovFolder(dir); EditorUtility.DisplayProgressBar("Texture Studio", "Making " + name + "...", 0);
            try
            {
                var sheet = TextureMoving.Sheet(s, s.size, m_SmallAfter, k => EditorUtility.DisplayProgressBar("Texture Studio", "Making " + name + "...", k * 0.9f)); bool bb = TextureMoving.Billboard(s.kind), add = TextureMoving.Additive(s.kind);
                var tex = Pixels.SaveAt(sheet, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + " sheet.png"), null, false, s.dither, true); var (cols, rows) = TextureMoving.Grid(s.frames);
                if (onlyTexture) { EditorGUIUtility.PingObject(tex); ShowNotification(new GUIContent("Saved " + tex.name + " in " + dir + " (" + cols + " × " + rows + " frames)")); return; }
                var mat = MovingMaterial(tex, add ? 3 : bb ? 2 : 0); Fx(mat, "_FLIPBOOK", true); mat.SetVector("_Grid", new Vector4(cols, rows)); mat.SetFloat("_FPS", m_FlipFps); Fx(mat, "_UNLIT", add || s.kind == TextureMoving.Kind.Lava); if (s.kind == TextureMoving.Kind.Lava) { Fx(mat, "_PULSE", true); mat.SetColor("_EmissionColor", new Color(1.2f, 0.4f, 0.1f)); mat.SetFloat("_PulseMin", 0.6f); }
                AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + ".mat")); UnityEngine.Object ping = mat;
                if (m_FlipParticles)
                {
                    var pm = MovingMaterial(tex, add ? 3 : 2); Fx(pm, "_UNLIT", true); AssetDatabase.CreateAsset(pm, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + " particles.mat"));
                    var go = new GameObject(name); var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = s.frames / Mathf.Max(1f, m_FlipFps); main.startSpeed = s.kind == TextureMoving.Kind.Fire || s.kind == TextureMoving.Kind.Smoke ? 0.6f : 0.2f; main.startSize = 1.2f; main.maxParticles = 30;
                    var em = ps.emission; em.rateOverTime = s.kind == TextureMoving.Kind.Magic ? 1 : 6; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8; sh.radius = 0.15f;
                    // sparks and magic already move inside the picture: one particle that stays put, the next starting as it ends
                    if (s.kind == TextureMoving.Kind.Sparks || s.kind == TextureMoving.Kind.Magic) { main.startSpeed = 0; main.maxParticles = 1; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 1, 0, main.startLifetime.constant) }); sh.enabled = false; main.startSize = 1.5f; }
                    var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = cols; tsa.numTilesY = rows; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, s.frames / (float)(cols * rows))); tsa.cycleCount = 1;
                    var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) }); col.color = g;
                    go.GetComponent<ParticleSystemRenderer>().sharedMaterial = pm; var pf = PrefabUtility.SaveAsPrefabAsset(go, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + ".prefab")); DestroyImmediate(go); ping = pf;
                }
                if (m_FlipLoopToo && !bb)
                {
                    var one = TextureMoving.Frame(s, 0, s.frames, Mathf.Max(256, s.size * 2), m_SmallAfter); var t1 = Pixels.SaveAt(one, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + " loop.png"), null, false, s.dither, false);
                    var lm = MovingMaterial(t1, 0); Fx(lm, "_SCROLL", true); lm.SetVector("_Scroll", new Vector4(0.03f, 0.06f, -0.02f, 0.04f)); lm.SetTexture("_DetailMap", t1); lm.SetFloat("_DetailMix", 0.5f); Fx(lm, "_WOBBLE", true); lm.SetVector("_Wobble", new Vector4(0.01f, 0.6f, 3, 0)); AssetDatabase.CreateAsset(lm, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + " scrolling.mat"));
                }
                AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(ping); ShowNotification(new GUIContent("Saved " + name + " in " + dir));
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
        static void MovFolder(string dir) { var parts = dir.Split('/'); string cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var n = cur + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(n)) AssetDatabase.CreateFolder(cur, parts[i]); cur = n; } }

        // ================================================================ flow map

        void FlowGUI()
        {
            m_Flow.Ensure(); GUILayout.Label("WHAT IS A FLOW MAP?", m_Head);
            GUILayout.Label("A picture of directions. It tells a texture which way to drift at each spot, so water can flow round a rock, lava can creep down a slope, or a portal can swirl, all on one flat texture. You paint the arrows here; the box on the right shows your texture moving along them. Then in the Shader maker, the Flow map effect does the same in your game.", m_Hint);
            GUILayout.Label("PAINT THE WAY THINGS DRIFT", m_Head); GUILayout.Label("Drag on the left picture: things drift the way you drag. Shift-drag rubs it back to still.", m_Hint);
            m_FlowBrush = EditorGUILayout.Slider("Brush size", m_FlowBrush, 0.02f, 0.5f); m_FlowStrength = EditorGUILayout.Slider("Strength", m_FlowStrength, 0.05f, 1f);
            m_FlowUnder = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Picture under it", "To see where you paint (empty: the texture on the Edit page)"), m_FlowUnder, typeof(Texture2D), false);
            GUILayout.Label("START FROM", m_Head); GUILayout.BeginHorizontal(); if (GUILayout.Button("Still", EditorStyles.miniButton)) { Undo.RecordObject(this, "Flow"); m_Flow.Clear(); } if (GUILayout.Button("All down", EditorStyles.miniButton)) m_Flow.Fill(p => Vector2.down * 0.6f); if (GUILayout.Button("All right", EditorStyles.miniButton)) m_Flow.Fill(p => Vector2.right * 0.6f); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Swirl", EditorStyles.miniButton)) m_Flow.Fill(p => { var d = p - Vector2.one * 0.5f; return new Vector2(-d.y, d.x) * 2.5f; }); if (GUILayout.Button("Out from the middle", EditorStyles.miniButton)) m_Flow.Fill(p => (p - Vector2.one * 0.5f) * 2f); if (GUILayout.Button("Smooth it", EditorStyles.miniButton)) m_Flow.Smooth(); GUILayout.EndHorizontal();
            GUILayout.Space(6); GUILayout.Label("SAVE", m_Head); if (m_FlowName.Length == 0) m_FlowName = "Flow"; m_FlowName = EditorGUILayout.TextField("Name", m_FlowName);
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent; if (GUILayout.Button(new GUIContent("Save the flow map", "A red-green texture (grey is still) in Assets/Textures/Moving. Use it in the Shader maker's Flow map"), GUILayout.Height(28))) EditorGUIUtility.PingObject(SaveFlowMap()); GUI.backgroundColor = old;
            if (GUILayout.Button(new GUIContent("Save it and try it in the Shader maker", "Saves the flow map, then opens the Shader maker with the River preset using it"))) { var t = SaveFlowMap(); var sm = ShMat(); if (sm) { Preset(sm, 2); sm.SetTexture("_FlowMap", t); } m_MovTab = 2; }
        }
        Texture2D SaveFlowMap()
        {
            MovFolder("Assets/Textures/Moving"); var t = Pixels.SaveAt(m_Flow.ToPixels(256), AssetDatabase.GenerateUniqueAssetPath("Assets/Textures/Moving/" + TextureShelf.Clean(m_FlowName) + ".png"), null, false, false);
            var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter; if (ti) { ti.sRGBTexture = false; ti.SaveAndReimport(); } m_ShFlow = t; return t;
        }
        /// <summary>The texture drifting along the painted flow, the same way the shader does it (two copies that take turns, so it never stretches too far).</summary>
        void FlowLive(Rect r)
        {
            Pixels src = null; try { src = m_FlowUnder ? Pixels.ReadCached(m_FlowUnder) : m_SmallAfter; } catch { }
            if (src == null) { if (m_FlowLivePx == null || m_FlowLivePx.w != 64) { m_FlowLivePx = new Pixels(64, 64); for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) m_FlowLivePx.c[y * 64 + x] = ((x / 8 + y / 8) & 1) == 0 ? new Color(0.25f, 0.55f, 0.8f) : new Color(0.85f, 0.9f, 0.95f); } src = m_FlowLivePx; }
            const int N = 128; var o = new Color[N * N]; float t = (float)(EditorApplication.timeSinceStartup % 1000) * 0.4f, p0 = t - Mathf.Floor(t), p1 = (t + 0.5f) - Mathf.Floor(t + 0.5f), w = Mathf.Abs((0.5f - p0) / 0.5f); int res = m_Flow.res;
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N; var fl = m_Flow.d[Mathf.Min(res - 1, (int)(v * res)) * res + Mathf.Min(res - 1, (int)(u * res))] * 0.25f;
                    Color S(float a, float b) => src.Sample((a - Mathf.Floor(a)) * src.w, (b - Mathf.Floor(b)) * src.h);
                    var c = Color.Lerp(S(u - fl.x * p0, v - fl.y * p0), S(u - fl.x * p1 + 0.37f, v - fl.y * p1 + 0.37f), w); c.a = 1; o[y * N + x] = c;
                }
            if (!m_FlowLive) m_FlowLive = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp }; m_FlowLive.SetPixels(o); m_FlowLive.Apply();
            GUI.DrawTexture(r, m_FlowLive, ScaleMode.StretchToFill); GUI.Label(new Rect(r.x, r.yMax + 4, r.width, 18), "How it looks: your texture drifting along the arrows", m_Hint);
        }
        void FlowView(Rect area, Event ev)
        {
            float side = Mathf.Min(area.width * 0.6f, area.height - 40); var live = new Rect(area.x + side + 30, area.y + 10, Mathf.Min(area.width - side - 40, side), Mathf.Min(area.width - side - 40, side)); if (live.width > 60 && ev.type == EventType.Repaint) FlowLive(live); var r = new Rect(area.x + 10, area.y + 10, side, side); var under = m_FlowUnder ? (Texture)m_FlowUnder : m_SmallAfter != null ? m_After : null;
            if (under) GUI.DrawTexture(r, under, ScaleMode.StretchToFill); else EditorGUI.DrawRect(r, new Color(0.2f, 0.22f, 0.25f)); EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));
            m_Flow.Ensure(); int step = 4; Handles.BeginGUI();
            for (int y = 0; y < m_Flow.res; y += step) for (int x = 0; x < m_Flow.res; x += step)
                {
                    var d = m_Flow.d[y * m_Flow.res + x]; var c = new Vector2(r.x + (x + 0.5f) / m_Flow.res * r.width, r.yMax - (y + 0.5f) / m_Flow.res * r.height); var tip = c + new Vector2(d.x, -d.y) * r.width / m_Flow.res * step * 0.9f;
                    if (!r.Contains(tip)) continue; Handles.color = d.sqrMagnitude < 0.0004f ? new Color(1, 1, 1, 0.25f) : new Color(1f, 0.85f, 0.4f, 0.9f); Handles.DrawAAPolyLine(2, c, tip); Handles.DrawSolidDisc(tip, Vector3.forward, 1.6f);
                }
            Handles.EndGUI();
            if (r.Contains(ev.mousePosition)) { Handles.BeginGUI(); Handles.color = new Color(1, 1, 1, 0.6f); Handles.DrawWireDisc(ev.mousePosition, Vector3.forward, m_FlowBrush * r.width); Handles.EndGUI(); }
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition)) { GUIUtility.hotControl = id; m_FlowLast = ev.mousePosition; Undo.RecordObject(this, "Paint flow"); ev.Use(); }
            if (ev.type == EventType.MouseDrag && GUIUtility.hotControl == id) { var pos = ev.mousePosition; var dd = pos - (m_FlowLast ?? pos); if (dd.magnitude > 1) { var uv = new Vector2((pos.x - r.x) / r.width, (r.yMax - pos.y) / r.height); var dir = ev.shift ? Vector2.zero : new Vector2(dd.x, -dd.y).normalized; m_Flow.Paint(uv, dir, m_FlowBrush, ev.shift ? 0.5f : m_FlowStrength); m_FlowLast = pos; } ev.Use(); Repaint(); }
            if (ev.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_FlowLast = null; ev.Use(); }
        }

        // ================================================================ shader maker

        [SerializeField] Texture2D m_ShBase, m_ShFlow, m_ShSecond; [SerializeField] int m_ShBlend; [SerializeField] string m_ShState;
        Material ShMat()
        {
            if (m_ShMat) return m_ShMat; var sh = Shader.Find("HienaDev/Moving"); if (!sh) return null; m_ShMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (!string.IsNullOrEmpty(m_ShState)) { try { var keep = JsonUtility.FromJson<ShState>(m_ShState); keep.To(m_ShMat); } catch { } } else Preset(m_ShMat, 0);
            return m_ShMat;
        }
        [Serializable] class ShState
        {
            public string[] keys; public string[] fn; public float[] fv; public string[] cn; public Color[] cv; public string[] vn; public Vector4[] vv;
            public static ShState From(Material m) { var s = new ShState { keys = m.shaderKeywords }; var sh = m.shader; var f = new List<(string, float)>(); var c = new List<(string, Color)>(); var v = new List<(string, Vector4)>(); for (int i = 0; i < sh.GetPropertyCount(); i++) { var n = sh.GetPropertyName(i); var t = sh.GetPropertyType(i); if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) f.Add((n, m.GetFloat(n))); else if (t == UnityEngine.Rendering.ShaderPropertyType.Color) c.Add((n, m.GetColor(n))); else if (t == UnityEngine.Rendering.ShaderPropertyType.Vector) v.Add((n, m.GetVector(n))); } s.fn = f.Select(q => q.Item1).ToArray(); s.fv = f.Select(q => q.Item2).ToArray(); s.cn = c.Select(q => q.Item1).ToArray(); s.cv = c.Select(q => q.Item2).ToArray(); s.vn = v.Select(q => q.Item1).ToArray(); s.vv = v.Select(q => q.Item2).ToArray(); return s; }
            public void To(Material m) { m.shaderKeywords = keys ?? new string[0]; for (int i = 0; i < fn.Length; i++) m.SetFloat(fn[i], fv[i]); for (int i = 0; i < cn.Length; i++) m.SetColor(cn[i], cv[i]); for (int i = 0; i < vn.Length; i++) m.SetVector(vn[i], vv[i]); }
        }
        static void Fx(Material m, string kw, bool on) { if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw); var p = ShFx.FirstOrDefault(q => q.kw == kw).prop; if (p != null && m.HasProperty(p)) m.SetFloat(p, on ? 1 : 0); }
        /// <summary>Solid, cut-out, see-through or glowing (added on top).</summary>
        static void Blend(Material m, int b)
        {
            m.SetFloat("_Surface", b); m.SetFloat("_SrcBlend", b >= 2 ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1); m.SetFloat("_DstBlend", b == 2 ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : b == 3 ? 1 : 0); m.SetFloat("_ZWrite", b >= 2 ? 0 : 1);
            if (b == 1) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON"); m.renderQueue = b >= 2 ? 3000 : b == 1 ? 2450 : 2000; m.SetOverrideTag("RenderType", b >= 2 ? "Transparent" : b == 1 ? "TransparentCutout" : "Opaque");
        }
        /// <summary>A new material on HienaDev/Moving with this texture.</summary>
        public static Material MovingMaterial(Texture tex, int blend) { var m = new Material(Shader.Find("HienaDev/Moving")); m.SetTexture("_BaseMap", tex); Blend(m, blend); if (blend >= 2) m.SetFloat("_Cull", 0); return m; }
        static readonly string[] PresetNames = { "Plain", "Conveyor", "River (flow)", "Lava floor", "Fire quad", "Force field", "Hologram", "Dissolve", "Flag", "Glowing runes", "Portal", "Toon water", "Snowy top", "Mountain bands" };
        void Preset(Material m, int i)
        {
            m.shaderKeywords = new string[0]; foreach (var f in ShFx) m.SetFloat(f.prop, 0); Blend(m, 0); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Cull", 2); m_ShMesh = i == 3 || i == 11 ? 3 : i == 4 ? 0 : 1;
            switch (i)
            {
                case 1: Fx(m, "_SCROLL", true); m.SetVector("_Scroll", new Vector4(0, 0.4f, 0, 0)); break;
                case 2: Fx(m, "_FLOW", true); m.SetFloat("_FlowSpeed", 0.4f); m.SetFloat("_FlowStrength", 0.25f); if (m_ShFlow) m.SetTexture("_FlowMap", m_ShFlow); m.SetColor("_BaseColor", new Color(0.6f, 0.85f, 1f)); break;
                case 3: Fx(m, "_SCROLL", true); m.SetVector("_Scroll", new Vector4(0.02f, 0.03f, -0.015f, 0.02f)); m.SetFloat("_DetailMix", 0.6f); Fx(m, "_PULSE", true); m.SetColor("_EmissionColor", new Color(1.5f, 0.45f, 0.08f)); m.SetFloat("_PulseSpeed", 0.3f); Fx(m, "_WOBBLE", true); m.SetVector("_Wobble", new Vector4(0.015f, 0.5f, 3, 0)); break;
                case 4: Fx(m, "_FLIPBOOK", true); Fx(m, "_BILLBOARD", true); Fx(m, "_UNLIT", true); Blend(m, 3); m.SetFloat("_Cull", 0); break;
                case 5: Fx(m, "_RIM", true); Fx(m, "_SCROLL", true); Fx(m, "_UNLIT", true); Blend(m, 3); m.SetColor("_BaseColor", new Color(0.2f, 0.5f, 1f, 0.35f)); m.SetVector("_Scroll", new Vector4(0, 0.3f, 0, 0)); m.SetColor("_RimColor", new Color(0.5f, 1.2f, 2.5f)); break;
                case 6: Fx(m, "_HOLO", true); Fx(m, "_UNLIT", true); Blend(m, 2); break;
                case 7: Fx(m, "_DISSOLVE", true); m.SetFloat("_DissolveLoop", 1.2f); break;
                case 8: Fx(m, "_FLAG", true); m.SetVector("_Flag", new Vector4(0.14f, 3.5f, 1.6f, 0.12f)); m.SetFloat("_FlagShade", 0.7f); m.SetFloat("_Cull", 0); m_ShMesh = 5; break;
                case 9: Fx(m, "_PULSE", true); m.SetColor("_EmissionColor", new Color(0.2f, 1.4f, 2f)); m.SetFloat("_PulseSpeed", 0.8f); m.SetFloat("_PulseMin", 0.1f); break;
                case 10: Fx(m, "_HUE", true); Fx(m, "_WOBBLE", true); Fx(m, "_UNLIT", true); Fx(m, "_RIM", true); m.SetVector("_Wobble", new Vector4(0.05f, 1.5f, 4, 0)); m.SetFloat("_HueSpeed", 0.15f); break;
                case 12: Fx(m, "_LAYERS", true); m.SetFloat("_UseUp", 1); m.SetFloat("_UseHigh", 0); m.SetFloat("_UseLow", 0); m.SetTexture("_UpMap", null); m.SetColor("_UpColor", new Color(0.95f, 0.97f, 1f)); m.SetFloat("_UpAmount", 0.45f); m.SetFloat("_UpOnlyAbove", 0); m.SetFloat("_LayerBlend", 0.15f); m.SetFloat("_LayerNoise", 0.3f); break;
                case 13: Fx(m, "_LAYERS", true); m.SetFloat("_UseUp", 1); m.SetFloat("_UseHigh", 1); m.SetFloat("_UseLow", 1); m.SetColor("_UpColor", new Color(0.95f, 0.97f, 1f)); m.SetFloat("_UpAmount", 0.45f); m.SetFloat("_UpOnlyAbove", 1); m.SetFloat("_UpFromY", 0.15f); m.SetColor("_HighColor", new Color(0.55f, 0.55f, 0.58f)); m.SetFloat("_HighY", 0.15f); m.SetColor("_LowColor", new Color(0.35f, 0.55f, 0.25f)); m.SetFloat("_LowY", -0.2f); m.SetFloat("_LayerBlend", 0.1f); m.SetFloat("_LayerNoise", 0.4f); m.SetColor("_BaseColor", new Color(0.58f, 0.47f, 0.34f)); m.SetTexture("_UpMap", null); m.SetTexture("_HighMap", null); m.SetTexture("_LowMap", null); break;
                case 11: Fx(m, "_WAVES", true); Fx(m, "_TOON", true); Fx(m, "_SCROLL", true); m.SetVector("_Scroll", new Vector4(0.02f, 0.05f, 0, 0)); m.SetColor("_BaseColor", new Color(0.35f, 0.7f, 0.95f)); m.SetVector("_Waves", new Vector4(0.12f, 2.5f, 1.5f, 0)); break;
            }
            m_ShBlend = Mathf.RoundToInt(m.GetFloat("_Surface"));
        }
        void ShaderGUI()
        {
            var m = ShMat(); if (!m) { GUILayout.Label("The HienaDev/Moving shader is missing: reimport the Texture Studio sample.", m_Hint); return; }
            GUILayout.Label("START FROM", m_Head); int pi = GUILayout.SelectionGrid(-1, PresetNames, 2, EditorStyles.miniButton); if (pi >= 0) { Undo.RecordObject(this, "Shader preset"); Preset(m, pi); }
            GUILayout.Space(4); GUILayout.Label("TEXTURES", m_Head);
            var b = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Texture", "Empty: the texture on the Edit page"), m_ShBase, typeof(Texture2D), false, GUILayout.Height(18)); if (b != m_ShBase) m_ShBase = b;
            if (m.IsKeywordEnabled("_FLOW")) { var f = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Flow map", "Paint one in the Flow map tab"), m_ShFlow, typeof(Texture2D), false, GUILayout.Height(18)); if (f != m_ShFlow) m_ShFlow = f; }
            if (m.IsKeywordEnabled("_SCROLL")) { var sc = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Second layer", "Slides at its own speed over the first"), m_ShSecond, typeof(Texture2D), false, GUILayout.Height(18)); if (sc != m_ShSecond) m_ShSecond = sc; }
            var col = EditorGUILayout.ColorField("Colour", m.GetColor("_BaseColor")); m.SetColor("_BaseColor", col);
            GUILayout.Label("Drawn as", m_Hint); int nb = GUILayout.SelectionGrid(m_ShBlend, Blends, 2, EditorStyles.miniButton); if (nb != m_ShBlend) { m_ShBlend = nb; Blend(m, nb); }
            GUILayout.Space(4); GUILayout.Label("EFFECTS (switch any on)", m_Head);
            foreach (var f in ShFx)
            {
                bool on = m.IsKeywordEnabled(f.kw); bool n = EditorGUILayout.ToggleLeft(new GUIContent(" " + f.name, f.tip), on, on ? EditorStyles.boldLabel : EditorStyles.label); if (n != on) Fx(m, f.kw, n); if (!n) continue;
                EditorGUI.indentLevel++;
                switch (f.kw)
                {
                    case "_SCROLL": V2(m, "_Scroll", "Speed", 0, 1, -2, 2); V2(m, "_Scroll", "Second layer speed", 2, 3, -2, 2); Sl(m, "_DetailMix", "Second layer", 0, 1); break;
                    case "_FLIPBOOK": V2(m, "_Grid", "Columns, rows", 0, 1, 1, 16, true); Sl(m, "_FPS", "Frames a second", 1, 60); Sl(m, "_FlipBlend", "Blend frames", 0, 1); break;
                    case "_FLOW": Sl(m, "_FlowSpeed", "Speed", 0, 2); Sl(m, "_FlowStrength", "Strength", 0, 1); break;
                    case "_PULSE": Col(m, "_EmissionColor", "Glow", true); Sl(m, "_PulseSpeed", "Pulses a second", 0, 5); Sl(m, "_PulseMin", "Lowest", 0, 1); break;
                    case "_DISSOLVE": Sl(m, "_Dissolve", "Dissolved", 0, 1); Sl(m, "_DissolveScale", "Size", 1, 40); Sl(m, "_DissolveEdge", "Edge", 0, 0.3f); Col(m, "_DissolveEdgeColor", "Edge colour", true); Sl(m, "_DissolveLoop", "By itself (speed)", 0, 5); break;
                    case "_HOLO": Col(m, "_HoloColor", "Colour", true); Sl(m, "_HoloLines", "Lines", 1, 200); Sl(m, "_HoloSpeed", "Line speed", -5, 5); Sl(m, "_HoloFlicker", "Flicker", 0, 1); break;
                    case "_FLAG":
                        V1(m, "_Flag", "Strength", 0, 0, 0.4f); V1(m, "_Flag", "Speed", 1, 0, 10); V1(m, "_Flag", "Waves", 2, 0.3f, 5); V1(m, "_Flag", "Droop", 3, 0, 1); Sl(m, "_FlagShade", "Fold shading", 0, 1.5f);
                        GUILayout.Label("The pole is the texture's left edge. A Unity Quad only has 4 corners, so it can't ripple: use the Flag mesh.", m_Hint);
                        if (GUILayout.Button(new GUIContent("Save the Flag mesh", "A flat 1 × 0.66 mesh with many vertices, ready to put this material on"), EditorStyles.miniButton)) { MovFolder("Assets/Textures/Moving"); var fm = UnityEngine.Object.Instantiate(FlagMesh()); fm.name = "Flag mesh"; AssetDatabase.CreateAsset(fm, AssetDatabase.GenerateUniqueAssetPath("Assets/Textures/Moving/Flag mesh.asset")); AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(fm); }
                        break;
                    case "_WIND": V1(m, "_Wind", "Strength", 0, 0, 1); V1(m, "_Wind", "Speed", 1, 0, 6); V1(m, "_Wind", "Size", 2, 0.1f, 6); V1(m, "_Wind", "Height", 3, 0.1f, 10); break;
                    case "_WAVES": V1(m, "_Waves", "Height", 0, 0, 1); V1(m, "_Waves", "Length", 1, 0.2f, 10); V1(m, "_Waves", "Speed", 2, 0, 5); break;
                    case "_RIM": Col(m, "_RimColor", "Colour", true); Sl(m, "_RimPower", "Thin", 0.5f, 8); break;
                    case "_TOON": Sl(m, "_ToonSteps", "Steps", 1, 8); break;
                    case "_HUE": Sl(m, "_HueSpeed", "Speed", -2, 2); break;
                    case "_LAYERS":
                        Layer(m, "_UseUp", "On faces that point up", "_UpMap", "_UpColor", null, "Snow, moss, dust: wherever the surface faces up"); if (m.GetFloat("_UseUp") > 0.5f) { EditorGUI.indentLevel++; Sl(m, "_UpAmount", "How much", 0, 1); bool oa = m.GetFloat("_UpOnlyAbove") > 0.5f; bool na = EditorGUILayout.Toggle(new GUIContent("Only up high", "Only above a height: snow on the peaks, not in the valleys"), oa); if (na != oa) m.SetFloat("_UpOnlyAbove", na ? 1 : 0); if (na) m.SetFloat("_UpFromY", EditorGUILayout.FloatField(new GUIContent("Above", "World height in units (Y)"), m.GetFloat("_UpFromY"))); EditorGUI.indentLevel--; }
                        Layer(m, "_UseHigh", "Higher up", "_HighMap", "_HighColor", "_HighY", "Above this height (in the world): snowy peaks, rock above grass");
                        Layer(m, "_UseLow", "Lower down", "_LowMap", "_LowColor", "_LowY", "Below this height: mud, wet sand, grass at the foot");
                        Sl(m, "_LayerBlend", "Blend (0 hard)", 0, 2); Sl(m, "_LayerNoise", "Ragged edges", 0, 1); if (m.GetFloat("_LayerNoise") > 0) Sl(m, "_LayerNoiseSize", "Ragged size", 0.2f, 20); break;
                    case "_WOBBLE": V1(m, "_Wobble", "Strength", 0, 0, 0.2f); V1(m, "_Wobble", "Speed", 1, 0, 6); V1(m, "_Wobble", "Size", 2, 0.5f, 20); break;
                }
                EditorGUI.indentLevel--;
            }
            GUILayout.Space(6); GUILayout.Label("SAVE", m_Head); m_ShName = EditorGUILayout.TextField("Name", m_ShName);
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent; if (GUILayout.Button(new GUIContent("Save the material", "In Assets/Textures/Moving: drag it onto anything. In the Scene view, turn on Always Refresh to see it move"), GUILayout.Height(28))) { MovFolder("Assets/Textures/Moving"); var mm = new Material(m) { hideFlags = HideFlags.None }; ApplyTextures(mm); mm.SetFloat("_HmUseTime", 0); AssetDatabase.CreateAsset(mm, AssetDatabase.GenerateUniqueAssetPath("Assets/Textures/Moving/" + TextureShelf.Clean(m_ShName) + ".mat")); AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(mm); } GUI.backgroundColor = old;
            GUILayout.BeginHorizontal(); if (GUILayout.Button(new GUIContent("Save only the texture", "Bakes what you see into a PNG (no material): no lights, at this moment of the animation. In Assets/Textures/Moving"), GUILayout.Height(22))) SaveShaderTexture(m);
            m_ShBakeSize = EditorGUILayout.IntPopup(m_ShBakeSize, new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 }, GUILayout.Width(60)); GUILayout.EndHorizontal();
            GUILayout.Label("In the Scene view, switch on Always Refresh (the effects menu at the top) to see materials move while you're not playing.", m_Hint);
            m_ShState = JsonUtility.ToJson(ShState.From(m));
        }
        /// <summary>Bakes the shader's look (flat on a square, no lights, at this moment) into a PNG. Rendered over black and over white to get the see-through parts right.</summary>
        void SaveShaderTexture(Material m)
        {
            string dir = "Assets/Textures/Moving"; MovFolder(dir); int size = Mathf.Clamp(m_ShBakeSize, 64, 4096); var bake = new Material(m) { hideFlags = HideFlags.HideAndDontSave }; ApplyTextures(bake);
            foreach (var k in new[] { "_WIND", "_WAVES", "_WOBBLE", "_BILLBOARD" }) Fx(bake, k, false); Fx(bake, "_UNLIT", true); bake.SetFloat("_HmUseTime", 1); bake.SetFloat("_HmTime", (float)EditorApplication.timeSinceStartup);
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx"); var pru = new PreviewRenderUtility();
            try
            {
                var cam = pru.camera; cam.orthographic = true; cam.orthographicSize = 0.5f; cam.aspect = 1; cam.nearClipPlane = 0.01f; cam.farClipPlane = 10; cam.transform.position = new Vector3(0, 0, -2); cam.transform.rotation = Quaternion.identity; cam.clearFlags = CameraClearFlags.SolidColor;
                Pixels Shot(Color bg)
                {
                    cam.backgroundColor = bg; pru.BeginStaticPreview(new Rect(0, 0, size, size)); pru.DrawMesh(quad, Matrix4x4.identity, bake, 0); pru.Render(true); var t = pru.EndStaticPreview();
                    var px = Pixels.Read(t); DestroyImmediate(t); return px.w == size && px.h == size ? px : px.Resized(size, size);
                }
                var black = Shot(Color.black); var white = Shot(Color.white); var o = new Pixels(size, size); bool any = false;
                for (int i = 0; i < o.c.Length; i++)
                {
                    var b = black.c[i]; var w = white.c[i]; float a = Mathf.Clamp01(1 - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f); if (a < 0.999f) any = true;
                    o.c[i] = a > 0.002f ? new Color(Mathf.Clamp01(b.r / a), Mathf.Clamp01(b.g / a), Mathf.Clamp01(b.b / a), a) : new Color(0, 0, 0, 0);
                }
                var name = TextureShelf.Clean(m_ShName); if (name.Length == 0) name = "Shader"; var tex = Pixels.SaveAt(o, AssetDatabase.GenerateUniqueAssetPath(dir + "/" + name + ".png"), null, false, false, any);
                EditorGUIUtility.PingObject(tex); ShowNotification(new GUIContent("Saved " + tex.name + ".png in " + dir));
            }
            finally { pru.Cleanup(); DestroyImmediate(bake); }
        }
        static Mesh s_FlagMesh;
        /// <summary>A flat cloth: 1 wide, 0.66 tall, facing the camera like a Quad, with enough vertices to ripple.</summary>
        public static Mesh FlagMesh()
        {
            if (s_FlagMesh) return s_FlagMesh; const int cx = 40, cy = 24; var v = new Vector3[(cx + 1) * (cy + 1)]; var uv = new Vector2[v.Length]; var nr = new Vector3[v.Length]; var tri = new List<int>();
            for (int y = 0; y <= cy; y++) for (int x = 0; x <= cx; x++) { int i = y * (cx + 1) + x; uv[i] = new Vector2(x / (float)cx, y / (float)cy); v[i] = new Vector3(uv[i].x - 0.5f, (uv[i].y - 0.5f) * 0.66f, 0); nr[i] = Vector3.back; }
            for (int y = 0; y < cy; y++) for (int x = 0; x < cx; x++) { int bl = y * (cx + 1) + x, br = bl + 1, tl = bl + cx + 1, tr = tl + 1; tri.AddRange(new[] { bl, tl, br, br, tl, tr }); }
            s_FlagMesh = new Mesh { name = "Flag", hideFlags = HideFlags.HideAndDontSave, vertices = v, uv = uv, normals = nr, triangles = tri.ToArray() }; s_FlagMesh.RecalculateBounds(); s_FlagMesh.RecalculateTangents(); return s_FlagMesh;
        }
        /// <summary>One layer: on or off, its texture (empty: plain colour), its colour and, for heights, where it starts.</summary>
        void Layer(Material m, string use, string label, string map, string col, string height, string tip)
        {
            bool on = m.GetFloat(use) > 0.5f; bool n = EditorGUILayout.ToggleLeft(new GUIContent(label, tip), on); if (n != on) m.SetFloat(use, n ? 1 : 0); if (!n) return; EditorGUI.indentLevel++;
            var t = (Texture)EditorGUILayout.ObjectField(new GUIContent("Texture", "Empty: just the colour"), m.GetTexture(map), typeof(Texture), false, GUILayout.Height(18)); if (t != m.GetTexture(map)) m.SetTexture(map, t);
            var sc = m.GetTextureScale(map).x; float ns = EditorGUILayout.Slider("Tiling", sc, 0.1f, 20); if (!Mathf.Approximately(ns, sc)) m.SetTextureScale(map, Vector2.one * ns);
            Col(m, col, "Colour", false); if (height != null) { float h = EditorGUILayout.FloatField(new GUIContent("Height", "World height in units (Y)"), m.GetFloat(height)); m.SetFloat(height, h); } EditorGUI.indentLevel--;
        }
        void Sl(Material m, string p, string label, float lo, float hi) { float v = EditorGUILayout.Slider(label, m.GetFloat(p), lo, hi); m.SetFloat(p, v); }
        void Col(Material m, string p, string label, bool hdr) { var c = EditorGUILayout.ColorField(new GUIContent(label), m.GetColor(p), true, true, hdr); m.SetColor(p, c); }
        void V1(Material m, string p, string label, int i, float lo, float hi) { var v = m.GetVector(p); v[i] = EditorGUILayout.Slider(label, v[i], lo, hi); m.SetVector(p, v); }
        void V2(Material m, string p, string label, int i, int j, float lo, float hi, bool whole = false) { var v = m.GetVector(p); EditorGUILayout.BeginHorizontal(); EditorGUILayout.PrefixLabel(label); float a = EditorGUILayout.FloatField(v[i]), b = EditorGUILayout.FloatField(v[j]); EditorGUILayout.EndHorizontal(); a = Mathf.Clamp(whole ? Mathf.Round(a) : a, lo, hi); b = Mathf.Clamp(whole ? Mathf.Round(b) : b, lo, hi); v[i] = a; v[j] = b; m.SetVector(p, v); }
        void ApplyTextures(Material m)
        {
            Texture b = m_ShBase ? m_ShBase : m.IsKeywordEnabled("_FLIPBOOK") && m_FlipSheetTex ? (Texture)m_FlipSheetTex : m_After; if (b) m.SetTexture("_BaseMap", b);
            if (m_ShFlow) m.SetTexture("_FlowMap", m_ShFlow); m.SetTexture("_DetailMap", m_ShSecond ? m_ShSecond : b);
            if (m.IsKeywordEnabled("_FLIPBOOK") && !m_ShBase && m_FlipSheetTex) { var (c, r) = TextureMoving.Grid(m_Flip.frames); m.SetVector("_Grid", new Vector4(c, r)); }
        }
        void ShaderView(Rect area, Event ev)
        {
            var m = ShMat(); EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 30), Panel); GUILayout.BeginArea(new Rect(area.x + 8, area.y + 5, area.width - 16, 22)); GUILayout.BeginHorizontal(); for (int i = 0; i < ShMeshes.Length; i++) if (Pill(ShMeshes[i], m_ShMesh == i)) m_ShMesh = i; GUILayout.FlexibleSpace(); GUILayout.Label("drag to turn, scroll to zoom", m_Hint);
            if (Pill("\u2212", false, "Zoom out")) m_ShZoom = Mathf.Min(6, m_ShZoom * 1.25f); if (Pill("+", false, "Zoom in")) m_ShZoom = Mathf.Max(0.25f, m_ShZoom / 1.25f); if (Pill("Reset", false, "Back to the start view")) { m_ShZoom = 1; m_ShYaw = 30; m_ShPitch = 15; } GUILayout.EndHorizontal(); GUILayout.EndArea();
            var view = new Rect(area.x, area.y + 30, area.width, area.height - 30); if (!m) return;
            int id = GUIUtility.GetControlID(FocusType.Passive); if (ev.type == EventType.ScrollWheel && view.Contains(ev.mousePosition)) { m_ShZoom = Mathf.Clamp(m_ShZoom * (1 + ev.delta.y * 0.04f), 0.25f, 6); ev.Use(); Repaint(); } if (ev.type == EventType.MouseDown && view.Contains(ev.mousePosition)) { if (ev.clickCount == 2) { m_ShZoom = 1; m_ShYaw = 30; m_ShPitch = 15; } GUIUtility.hotControl = id; ev.Use(); } if (ev.type == EventType.MouseDrag && GUIUtility.hotControl == id) { m_ShYaw += ev.delta.x * 0.5f; m_ShPitch = Mathf.Clamp(m_ShPitch + ev.delta.y * 0.5f, -80, 80); ev.Use(); } if (ev.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; ev.Use(); }
            if (ev.type != EventType.Repaint || view.width < 20 || view.height < 20) return;
            if (m_ShPru == null) { m_ShPru = new PreviewRenderUtility(); m_ShPru.camera.fieldOfView = 30; m_ShPru.camera.nearClipPlane = 0.05f; m_ShPru.camera.farClipPlane = 100; m_ShPru.camera.clearFlags = CameraClearFlags.SolidColor; }
            ApplyTextures(m); m.SetFloat("_HmUseTime", 1); m.SetFloat("_HmTime", (float)(EditorApplication.timeSinceStartup % 3600));
            var mesh = m_ShMesh == 5 ? FlagMesh() : Resources.GetBuiltinResource<Mesh>(ShMeshes[m_ShMesh] + ".fbx"); float dist = (m_ShMesh == 3 ? 16 : 3.2f) * m_ShZoom; var rot = Quaternion.Euler(m_ShPitch + (m_ShMesh == 3 ? 35 : 0), m_ShYaw, 0);
            m_ShPru.camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f); m_ShPru.camera.transform.position = rot * new Vector3(0, 0, -dist); m_ShPru.camera.transform.rotation = rot; m_ShPru.lights[0].intensity = 1.1f; m_ShPru.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0); m_ShPru.lights[1].intensity = 0.4f; m_ShPru.ambientColor = new Color(0.35f, 0.36f, 0.4f);
            m_ShPru.BeginPreview(view, GUIStyle.none); m_ShPru.DrawMesh(mesh, Matrix4x4.TRS(Vector3.zero, m_ShMesh == 0 || m_ShMesh == 5 ? Quaternion.identity : Quaternion.Euler(0, (float)EditorApplication.timeSinceStartup * 10 % 360, 0), Vector3.one), m, 0); m_ShPru.Render(true); var tex = m_ShPru.EndPreview(); GUI.DrawTexture(view, tex, ScaleMode.StretchToFill, false);
        }
    }
}
