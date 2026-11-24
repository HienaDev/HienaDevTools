using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Texture Studio: the panels' sizes, the never-repeating material, see-through pictures, what gives a repeat
    /// away, and putting stickers on.</summary>
    public partial class TextureStudioWindow
    {
        // ---- sizes you drag
        [SerializeField] float m_ToolsW = 312, m_StepsW = 340, m_SaveH = 250; int m_Splitting;

        /// <summary>A bar between two panels that can be dragged. Returns the new size. Double-click puts it back.</summary>
        float Splitter(int which, Rect bar, bool sideways, float size, float dir, float min, float max, float normal)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("TextureSplit".GetHashCode() + which, FocusType.Passive); EditorGUIUtility.AddCursorRect(bar, sideways ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            bool hot = GUIUtility.hotControl == id; if (hot || bar.Contains(e.mousePosition)) EditorGUI.DrawRect(sideways ? new Rect(bar.center.x - 1, bar.y, 2, bar.height) : new Rect(bar.x, bar.center.y - 1, bar.width, 2), Accent);
            if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition)) { if (e.clickCount == 2) size = normal; else GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && hot) { size += (sideways ? e.delta.x : e.delta.y) * dir; e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && hot) { GUIUtility.hotControl = 0; e.Use(); }
            return Mathf.Clamp(size, min, Mathf.Max(min, max));
        }

        // ---- the material
        [SerializeField] MatKind m_Kind = MatKind.Normal; [SerializeField] NeverRepeats m_Never = new NeverRepeats(); [SerializeField] SeeThrough m_See = SeeThrough.Auto; [SerializeField] bool m_NeverPreview = true;
        SeeThrough m_Guessed = SeeThrough.Solid; Pixels m_GuessedFor; Material m_FlatMat;

        SeeThrough SeeNow { get { if (m_See != SeeThrough.Auto) return m_See; if (m_SmallAfter != null && m_GuessedFor != m_SmallAfter) { m_GuessedFor = m_SmallAfter; m_Guessed = TextureAlpha.Guess(m_SmallAfter); } return m_SmallAfter != null ? m_Guessed : SeeThrough.Solid; } }

        void MaterialCard()
        {
            GUILayout.Space(2); GUILayout.BeginHorizontal();
            if (Pill("Normal", m_Kind == MatKind.Normal, "A URP Lit material")) { m_Kind = MatKind.Normal; Changed(); }
            if (Pill("Never repeats", m_Kind == MatKind.NeverRepeats, "A material that shuffles the texture as it's drawn: it never visibly repeats, however big the surface is, and the texture stays as small as it is")) { m_Kind = MatKind.NeverRepeats; Changed(); }
            if (Pill("My shader", m_Kind == MatKind.Mine, "Uses the shader and settings of a material of yours")) { m_Kind = MatKind.Mine; Changed(); }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            if (m_Kind == MatKind.NeverRepeats) NeverCard();
            if (m_Kind == MatKind.Mine)
            {
                var tm = (Material)EditorGUILayout.ObjectField(new GUIContent("Shader from", "Drop a material of yours: the new one uses its shader and settings (how it's lit, how fog works on it), with these textures put in. Yours isn't changed"), m_Template, typeof(Material), false);
                if (tm != m_Template) { m_Template = tm; m_Slot = tm ? TextureSlots.Guess(tm.shader) : new TextureSlots(); Changed(); }
                if (m_Template)
                {
                    var slots = TextureSlots.TexturesOf(m_Template.shader);
                    if (slots.Count == 0) GUILayout.Label("That shader has no textures.", m_Hint);
                    else
                    {
                        GUILayout.Label("Which texture goes where in " + m_Template.shader.name, m_Hint);
                        SlotPick(slots, ref m_Slot.colour, "   Texture"); if (m_SaveNormal) SlotPick(slots, ref m_Slot.normal, "   Normal map"); if (m_SaveMask) { SlotPick(slots, ref m_Slot.mask, "   AO + shine"); SlotPick(slots, ref m_Slot.occlusion, "   Occlusion"); } if (m_SaveHeight) SlotPick(slots, ref m_Slot.height, "   Height"); if (GlowOn != null) SlotPick(slots, ref m_Slot.glow, "   Glow");
                    }
                }
            }
            else
            {
                // see-through
                var now = SeeNow; int s = EditorGUILayout.Popup(new GUIContent("See-through", "What the empty parts of the picture do. Cut out: there or not (leaves, fences, holes); works with fog and shadows. Smooth: soft edges (glass, smoke)"), (int)m_See, new[] { new GUIContent("Find out (" + (now == SeeThrough.Solid ? "solid" : now == SeeThrough.CutOut ? "cut out" : "smooth") + ")"), new GUIContent("Solid"), new GUIContent("Cut out"), new GUIContent("Smooth") });
                if (s != (int)m_See) { m_See = (SeeThrough)s; Changed(); }
            }
        }

        void NeverCard()
        {
            bool ch = false;
            if (m_Never.snap == Vector2Int.zero) ch |= Num(ref m_Never.turn, "Turn patches", "How much each patch is turned. 0 for things with a direction (wood, planks)", 0f, 1f);
            ch |= Num(ref m_Never.patch, "Patch size", "Smaller numbers keep bigger pieces of the texture whole", 0.3f, 3f);
            GUILayout.BeginHorizontal(); bool b = EditorGUILayout.ToggleLeft(new GUIContent("Bricks or tiles", "Patches only move by whole bricks, so the rows stay lined up"), m_Never.snap != Vector2Int.zero, GUILayout.Width(118));
            if (b != (m_Never.snap != Vector2Int.zero)) { m_Never.snap = b ? FindBricks() : Vector2Int.zero; ch = true; }
            if (m_Never.snap != Vector2Int.zero) { var sn = new Vector2Int(Mathf.Max(0, EditorGUILayout.IntField(m_Never.snap.x, GUILayout.Width(36))), Mathf.Max(0, EditorGUILayout.IntField(m_Never.snap.y, GUILayout.Width(36)))); GUILayout.Label("across, down", m_Hint); if (sn != m_Never.snap) { m_Never.snap = sn == Vector2Int.zero ? Vector2Int.one : sn; ch = true; } }
            GUILayout.EndHorizontal();
            ch |= Num(ref m_Never.macro, "Big areas", "Large gentle light and dark areas", 0f, 1f); if (m_Never.macro > 0) ch |= Num(ref m_Never.macroSize, "   Their size", "In tiles", 1f, 16f);
            GUILayout.Label(NeverTiles != null ? "Swaps between your " + NeverTiles.Kinds + " tiles at random." : "Add Tile variations as the last step and it also swaps between the tiles at random.", m_Hint);
            var t = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Second texture", "In patches over it, like moss or dirt (optional)"), m_Never.second, typeof(Texture2D), false); if (t != m_Never.second) { m_Never.second = t; ch = true; }
            if (m_Never.second) { ch |= Num(ref m_Never.secondAmount, "   How much", "", 0f, 1f); ch |= Num(ref m_Never.secondSize, "   Patch size", "In tiles", 0.5f, 12f); ch |= Num(ref m_Never.secondSoft, "   Soft edge", "", 0.02f, 1f); }
            if (ch) Repaint();
        }

        Vector2Int FindBricks()
        {
            if (m_SmallAfter == null) return Vector2Int.one; var f = TextureOps.FindRepeat(m_SmallAfter); return new Vector2Int(f.x > 0 ? Mathf.Max(1, Mathf.RoundToInt(m_SmallAfter.w / (float)f.x)) : 1, f.y > 0 ? Mathf.Max(1, Mathf.RoundToInt(m_SmallAfter.h / (float)f.y)) : 1);
        }

        /// <summary>The result drawn the way the Never repeats material lays it out.</summary>
        void DrawNever(Rect r, Texture tex, float repeat, float up = 0)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!m_FlatMat) m_FlatMat = new Material(Shader.Find("Hidden/HienaDev/NeverRepeatsPreview")) { hideFlags = HideFlags.HideAndDontSave };
            m_Never.sideways = m_Bands.on; m_Never.To(m_FlatMat); m_FlatMat.SetTexture("_MainTex", tex); m_FlatMat.SetFloat("_Repeat", repeat); m_FlatMat.SetFloat("_RepeatY", up > 0 ? up : repeat); if (TilesReady) { tex = m_TileBase; m_FlatMat.SetTexture("_MainTex", tex); m_FlatMat.SetTexture("_Tiles", m_TileArr[0]); m_FlatMat.SetFloat("_TileCount", m_TileArr[0].depth); m_FlatMat.SetFloat("_TileRare", NeverTiles.rare); m_FlatMat.EnableKeyword("_TILES"); } else m_FlatMat.DisableKeyword("_TILES");
            // (drawn into a picture first: the window's own drawing can't be cut off at the panel's edge with another shader)
            float ppp = EditorGUIUtility.pixelsPerPoint; var rt = RenderTexture.GetTemporary(Mathf.Max(8, (int)(r.width * ppp)), Mathf.Max(8, (int)(r.height * ppp)), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var old = RenderTexture.active; Graphics.Blit(tex, rt, m_FlatMat); RenderTexture.active = old; GUI.DrawTexture(r, rt, ScaleMode.StretchToFill, false); m_Temps.Add(rt);
        }
        readonly List<RenderTexture> m_Temps = new List<RenderTexture>();
        void FreeTemps() { foreach (var t in m_Temps) RenderTexture.ReleaseTemporary(t); m_Temps.Clear(); }

        bool NeverShown => m_MakeMaterial && m_Kind == MatKind.NeverRepeats && m_NeverPreview;

        // ---- tile variations, swapped at random by the Never repeats material
        Texture2DArray[] m_TileArr; Texture2D m_TileBase; int m_TileVersion = -1; System.Threading.Tasks.Task<(Pixels b, List<Pixels> c, List<Pixels> n, List<Pixels> m)> m_TileTask; int m_TileWant = -1;
        VariationsStep NeverTiles => m_MakeMaterial && m_Kind == MatKind.NeverRepeats && m_Steps.LastOrDefault(s => s.on) is VariationsStep v && v.Kinds > 1 ? v : null;
        void TilesTick()
        {
            if (m_TileTask != null && m_TileTask.IsCompleted) { if (m_TileTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) { var r = m_TileTask.Result; Put(ref m_TileBase, r.b); FreeTiles(); m_TileArr = new[] { TextureArrays.Make(r.c, true, false), TextureArrays.Make(r.n, false, false), TextureArrays.Make(r.m, false, false) }; m_TileVersion = m_TileWant; } else m_TileVersion = m_TileWant; m_TileTask = null; Repaint(); }
            var v = NeverTiles; if (v == null || m_Between == null || m_FullDone != m_Version || m_TileVersion == m_Version || m_TileTask != null) return; int before = m_Steps.Count(s => s.on) - 1; if (before < 0 || before >= m_Between.Count) return; var basePix = m_Between[before]; m_TileWant = m_Version; var copy = (VariationsStep)JsonUtility.FromJson(JsonUtility.ToJson(v), typeof(VariationsStep)); copy.extras = v.extras.ToList(); copy.Prepare(); float ns = m_NormalStrength, ao = m_AO, sm = m_Smooth, hc = m_HeightContrast, hs = m_HeightSmooth; bool hi = m_HeightInvert;
            m_TileTask = System.Threading.Tasks.Task.Run(() => { var q = Shrunk(basePix, 320); var l = copy.Tiles(q, false); return (q, l, l.Select(x => Map(x, View.Normal, 1f, ns, ao, sm, hc, hs, hi)).ToList(), l.Select(x => Map(x, View.Mask, 0f, ns, ao, sm, hc, hs, hi)).ToList()); });
        }
        void FreeTiles() { if (m_TileArr != null) foreach (var a in m_TileArr) if (a) DestroyImmediate(a); m_TileArr = null; }
        bool TilesReady => NeverTiles != null && m_TileArr != null && m_TileBase;

        // ---- see-through pictures on a checkerboard
        void Pic(Rect r, Texture t, float n)
        {
            if (Event.current.type != EventType.Repaint) return; bool see = SeeNow != SeeThrough.Solid;
            if (see) GUI.DrawTextureWithTexCoords(r, TextureAlpha.Checks, new Rect(0, 0, r.width / 32, r.height / 32));
            GUI.DrawTextureWithTexCoords(r, t, new Rect(0, 0, n, n), see);
        }

        // ---- keeping a wall's foot
        [SerializeField] Bands m_Bands = new Bands(); int m_BandDrag;
        GlowStep GlowOn => m_Steps.LastOrDefault(s => s is GlowStep && s.on) as GlowStep;

        void BandsCard()
        {
            if (!m_Bands.on) return; GUILayout.BeginVertical(new GUIStyle(m_Card) { margin = new RectOffset(0, 0, 0, 6), padding = new RectOffset(10, 8, 6, 8) });
            var box = GUILayoutUtility.GetRect(0, 0); GUILayout.BeginHorizontal(); GUILayout.Label("THE FOOT STAYS AS IT IS", new GUIStyle(m_Head) { normal = { textColor = Accent } }); GUILayout.FlexibleSpace(); if (GUILayout.Button(new GUIContent("Find it", "Looks for the line where the foot ends"), EditorStyles.miniButton, GUILayout.Width(54)) && m_Small != null) { var f = TextureBands.Find(m_Small); if (f > 0.02f) { m_Bands.foot = f; Changed(); } else ShowNotification(new GUIContent("No clear line found: drag it on the picture")); } if (GUILayout.Button("\u2715", EditorStyles.miniButton, GUILayout.Width(22))) { m_Bands.on = false; Changed(true); } GUILayout.EndHorizontal();
            EditorGUIUtility.labelWidth = 112; if (Num(ref m_Bands.foot, "Foot", "How tall the foot is (or drag the orange line on the picture)", 0f, 0.7f)) Changed(); if (Num(ref m_Bands.top, "Top", "A band at the top that stays too (a cornice, for example)", 0f, 0.5f)) Changed(); EditorGUIUtility.labelWidth = 0;
            bool f2 = EditorGUILayout.ToggleLeft(new GUIContent("Its left and right edges may meet", "On: only the colours at the foot's two ends are matched so it loops sideways, nothing drawn on it changes. Off: not a single pixel of it changes"), m_Bands.fixSeam); if (f2 != m_Bands.fixSeam) { m_Bands.fixSeam = f2; Changed(); }
            m_Bands.apart = EditorGUILayout.ToggleLeft(new GUIContent("Also save the foot and the wall apart", "For tall walls: two more textures (and materials), so the wall can repeat upwards above one foot"), m_Bands.apart); GUILayout.EndVertical();
        }

        void StepFoot(TextureStep s)
        {
            if (!m_Bands.on || !s.SameSize || s is IPaintable || s is StickerStep) return; bool now = s.foot < 0 ? s.FootByDefault : s.foot == 1, n = EditorGUILayout.ToggleLeft(new GUIContent("Leave the foot as it is", "This step only works on the wall above the foot"), now); if (n != now) { s.foot = n ? 1 : 0; Changed(); }
            if (n && s is SeamlessStep sm) { bool u = EditorGUILayout.ToggleLeft(new GUIContent("The wall also loops upwards", "For tall walls where the part above the foot repeats"), sm.alsoUp); if (u != sm.alsoUp) { sm.alsoUp = u; Changed(); } }
            if (n && s is BreakRepeatsStep br && br.s.tiles > 1) GUILayout.Label("With a foot kept it stays the same size.", m_Hint); if (n && s is VariationsStep) GUILayout.Label("Each tile keeps the foot. They're made when you save.", m_Hint);
        }

        /// <summary>A wall: repeated sideways only, before above after, with the foot's line to drag.</summary>
        void BandsView(Rect area, Texture after, bool trying)
        {
            bool two = m_View == View.SideBySide; var tex = m_View == View.Before ? m_Before : after; float n = m_Repeat, half = two ? (area.height - 44) / 2 : area.height - 24, asp = m_Before.width / (float)m_Before.height * n;
            var a = Fit(new Rect(area.x, area.y + 20, area.width, half), asp, 6); var b = two ? Fit(new Rect(area.x, area.y + 42 + half, area.width, half), asp, 6) : a;
            void Wall(Rect r, Texture x, string name, bool never) { if (never) DrawNever(r, x, n, 1); else Wide(r, x, n); GUI.Label(new Rect(r.x, r.y - 19, r.width, 18), name, trying && name != "BEFORE" ? new GUIStyle(m_Head) { normal = { textColor = Accent } } : m_Head); if (m_Edges) for (int k = 1; k < n; k++) EditorGUI.DrawRect(new Rect(r.x + r.width * k / n, r.y, 1, r.height), new Color(Accent.r, Accent.g, Accent.b, 0.8f)); }
            if (two) Wall(a, m_Before, "BEFORE", false); Wall(b, two ? after : tex, trying ? "TRYING: + " + m_HoverTool.proto.Title.ToUpper() : m_View == View.Before ? "BEFORE" : "AFTER", NeverShown && !trying && m_View != View.Before); Handle(a); if (two) Handle(b, false);
        }
        void Wide(Rect r, Texture t, float n) { if (Event.current.type != EventType.Repaint) return; bool see = SeeNow != SeeThrough.Solid; if (see) GUI.DrawTextureWithTexCoords(r, TextureAlpha.Checks, new Rect(0, 0, r.width / 32, r.height / 32)); GUI.DrawTextureWithTexCoords(r, t, new Rect(0, 0, n, 1), see); }
        void Handle(Rect r, bool drag = true)
        {
            var e = Event.current; float yf = r.yMax - r.height * m_Bands.foot, yt = r.y + r.height * m_Bands.top; EditorGUI.DrawRect(new Rect(r.x, yf - 1, r.width, 2), Accent); if (m_Bands.top > 0.002f) EditorGUI.DrawRect(new Rect(r.x, yt - 1, r.width, 2), Accent);
            if (!drag) return; EditorGUI.DrawRect(new Rect(r.x, yf - 8, 62, 16), Accent); GUI.Label(new Rect(r.x + 4, yf - 8, 60, 16), "foot \u2195", new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.black } }); EditorGUI.DrawRect(new Rect(r.xMax - 62, yt - (m_Bands.top > 0.002f ? 8 : 0), 62, 16), new Color(Accent.r, Accent.g, Accent.b, m_Bands.top > 0.002f ? 1 : 0.55f)); GUI.Label(new Rect(r.xMax - 58, yt - (m_Bands.top > 0.002f ? 8 : 0), 60, 16), "top \u2195", new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.black } });
            int id = GUIUtility.GetControlID("TextureFoot".GetHashCode(), FocusType.Passive); EditorGUIUtility.AddCursorRect(new Rect(r.x, yf - 7, r.width, 14), MouseCursor.ResizeVertical); EditorGUIUtility.AddCursorRect(new Rect(r.xMax - 62, yt - 8, 62, 24), MouseCursor.ResizeVertical);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { m_BandDrag = new Rect(r.xMax - 62, yt - 8, 62, 24).Contains(e.mousePosition) || (m_Bands.top > 0.002f && Mathf.Abs(e.mousePosition.y - yt) < 7) ? 2 : Mathf.Abs(e.mousePosition.y - yf) < 8 ? 1 : 0; if (m_BandDrag > 0) { GUIUtility.hotControl = id; e.Use(); } }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { if (m_BandDrag == 1) m_Bands.foot = Mathf.Clamp((r.yMax - e.mousePosition.y) / r.height, 0, 0.9f - m_Bands.top); else { m_Bands.top = Mathf.Clamp((e.mousePosition.y - r.y) / r.height, 0, 0.9f - m_Bands.foot); if (m_Bands.top < 0.012f) m_Bands.top = 0; } Changed(); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
        }

        List<Pixels> VariationTiles(VariationsStep v, Pixels p)
        {
            if (!TextureBands.Around(v, m_Bands)) return v.Tiles(p, true); TextureBands.Split(p, m_Bands, out int y0, out int y1); return v.Tiles(TextureBands.Rows(p, y0, y1), true).Select(t => TextureBands.Together(p, t, y0, y1, m_Bands, false)).ToList();
        }

        /// <summary>The foot and the wall as textures of their own, so a wall can be as tall as you like above one foot.</summary>
        void SaveApart(Pixels result, string folder, string name, Func<Pixels, string, bool, Texture2D> put, List<UnityEngine.Object> outputs, SeeThrough see)
        {
            TextureBands.Split(result, m_Bands, out int y0, out int y1); var wall = TextureBands.Rows(result, y0, y1); bool up = m_Steps.Any(s => s.on && s is SeamlessStep sm && sm.alsoUp); if (!up) wall = TextureOps.MakeSeamless(wall, 0.2f, TextureOps.SeamStyle.Natural, true, TextureOps.Ways.UpDown);
            foreach (var part in new[] { (y0 > 2 ? TextureBands.Rows(result, 0, y0) : null, "_Foot"), (wall, "_Wall") })
            {
                if (part.Item1 == null) continue; var c = put(part.Item1, part.Item2, false); if (!m_MakeMaterial || !c) continue; var n = put(Map(part.Item1, View.Normal, 1f), part.Item2 + "_Normal", true); var m = put(Map(part.Item1, View.Mask, 0f), part.Item2 + "_Mask", false); var mat = TextureMaterials.Make(folder + "/" + name + part.Item2 + ".mat", c, n, m, null, m_Kind == MatKind.Mine ? m_Template : null, m_Slot, null, see); outputs.Add(mat); AssetDatabase.SaveAssetIfDirty(mat);
            }
        }

        // ---- the glow, with the lights down
        Texture2D m_DarkTex; string m_DarkKey = "", m_DarkRun = ""; System.Threading.Tasks.Task<Pixels> m_DarkJob; Pixels m_DarkSmallFor, m_DarkSmall, m_DarkBigFor, m_DarkBig; double m_DarkAt;
        // (worked out away from the window, small first while things move, then sharp once they rest: the window never waits)
        void GlowPreview(GlowStep g, Pixels pin)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (m_DarkJob != null && m_DarkJob.IsCompleted) { if (m_DarkJob.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && m_DarkJob.Result != null) Put(ref m_DarkTex, m_DarkJob.Result); m_DarkJob = null; m_DarkKey = m_DarkRun; }
            string key = JsonUtility.ToJson(g) + "|" + GlowPart.Id(pin); bool moving = GUIUtility.hotControl != 0 || EditorApplication.timeSinceStartup - m_DarkAt < 0.25; if (key != m_DarkWant) { m_DarkWant = key; m_DarkAt = EditorApplication.timeSinceStartup; moving = true; }
            string want = key + (moving ? "|small" : "|big"); if (m_DarkTex && (m_DarkKey == want || m_DarkKey == key + "|big")) { if (moving) Repaint(); return; } if (m_DarkJob != null) { Repaint(); return; }
            if (m_DarkSmallFor != pin) { m_DarkSmallFor = pin; m_DarkSmall = Shrunk(pin, 256); } if (m_DarkBigFor != pin) { m_DarkBigFor = pin; m_DarkBig = Shrunk(pin, 512); }
            var c = JsonUtility.FromJson<GlowStep>(JsonUtility.ToJson(g)); c.Prepare(); var src = moving ? m_DarkSmall : m_DarkBig; m_DarkRun = want; m_DarkJob = System.Threading.Tasks.Task.Run(() => c.Dark(src)); Repaint();
        }
        string m_DarkWant = "";

        // ---- straight onto the model
        void PaintOnModel(Rect pic, TextureStep step)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("TextureOnModel".GetHashCode(), FocusType.Passive); if (e.type == EventType.Layout || e.type == EventType.Repaint) return; bool over = pic.Contains(e.mousePosition); float rep = m_Repeat;
            bool At(out Vector2 uv, out Vector2 r, out Vector2 u) { bool h = m_ModelView.Hit(pic, e.mousePosition, m_Shape, m_Model, out uv, out r, out u); uv = new Vector2(Mathf.Repeat(uv.x * rep, 1), Mathf.Repeat(uv.y * rep, 1)); r *= rep; u *= rep; return h; }
            if (step is StickerStep st)
            {
                if (e.type == EventType.ScrollWheel && over && (e.shift || e.control) && !StickerStep.EditMode) { float d = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x; if (e.shift) st.turn = Mathf.Repeat(st.turn + (d > 0 ? 8 : -8) + 180, 360) - 180; else st.size = Mathf.Clamp(st.size * (d > 0 ? 0.92f : 1.08f), 0.02f, 1f); e.Use(); Repaint(); return; }
                void Put(StickerStep.Stamp s, Vector2 uv, Vector2 r, Vector2 u) { s.art.Prepare(); var a = s.art.Get(s.seed); float asp = a != null ? a.h / (float)a.w : 1, size = s.size * 2f, c = Mathf.Cos(s.turn * Mathf.Deg2Rad), n = Mathf.Sin(s.turn * Mathf.Deg2Rad); s.at = uv; s.ax = (r * c + u * n) * size; s.ay = (u * c - r * n) * size * asp; }
                Vector2 Off(Vector2 a, Vector2 b) { var d = a - b; d.x -= Mathf.Round(d.x); d.y -= Mathf.Round(d.y); return d; }
                int Under(Vector2 uv) { for (int i = st.stamps.Count - 1; i >= 0; i--) { var s = st.stamps[i]; float reach = s.OnModel ? Mathf.Max(s.ax.magnitude, s.ay.magnitude) * 0.33f : s.size * 0.33f; if (Off(uv, s.at).magnitude < reach) return i; } return -1; }
                if (e.type == EventType.MouseDown && e.button == 0 && over && At(out var uv0, out var r0, out var u0)) { int i = Under(uv0); if (e.control || e.command) { if (i >= 0) { st.stamps.RemoveAt(i); st.pick = -1; Changed(); } e.Use(); return; } if (!StickerStep.EditMode) i = -1; else if (i < 0) { st.pick = -1; e.Use(); Repaint(); return; } GUIUtility.hotControl = id; if (i < 0) { var s = st.New(uv0, m_StickerSeed); m_StickerSeed = UnityEngine.Random.Range(1, 999999); Put(s, uv0, r0, u0); st.stamps.Add(s); i = st.stamps.Count - 1; } m_StickerDrag = i; st.pick = i; Changed(); e.Use(); }
                if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && m_StickerDrag >= 0 && m_StickerDrag < st.stamps.Count) { if (At(out var uv, out var r, out var u)) { Put(st.stamps[m_StickerDrag], uv, r, u); Changed(); } e.Use(); Repaint(); }
            }
            else if (step is IPaintable po)
            {
                if (e.type == EventType.ScrollWheel && over && e.control) { po.Brush = Mathf.Clamp(po.Brush * (e.delta.y > 0 ? 0.9f : 1.1f), 0.005f, 0.4f); e.Use(); Repaint(); return; }
                if (e.type == EventType.MouseDown && e.button == 0 && over && At(out var uv, out _, out _)) { GUIUtility.hotControl = id; m_Stroke = new TextureHeal.Stroke { radius = po.Brush, erase = e.control || e.command }; m_Stroke.points.Add(uv); po.Strokes.Add(m_Stroke); Changed(); e.Use(); }
                if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && m_Stroke != null) { if (At(out var q, out _, out _) && (q - m_Stroke.points[m_Stroke.points.Count - 1]).magnitude > po.Brush * 0.25f) { m_Stroke.points.Add(q); Changed(); } e.Use(); Repaint(); }
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_StickerDrag = -1; m_Stroke = null; Changed(); e.Use(); }
        }

        // ---- the four corners of a photo
        int m_Corner = -1;
        bool StepPics(Rect area, out Rect lp, out Rect rp, out Pixels pin, string left, string right)
        {
            lp = rp = default; pin = null; int before = m_Steps.Take(m_Selected).Count(s => s.on); if (m_Between == null || before >= m_Between.Count) return false; pin = m_Between[before]; var pout = m_Between[Mathf.Min(before + 1, m_Between.Count - 1)]; if (pin != m_PaintInFor) { Put(ref m_PaintIn, pin); m_PaintInFor = pin; } if (pout != m_PaintOutFor) { Put(ref m_PaintOut, pout); m_PaintOutFor = pout; }
            float half = (area.width - 12) / 2; lp = Fit(new Rect(area.x, area.y + 18, half, area.height - 18), pin.w / (float)pin.h, 10); rp = Fit(new Rect(area.x + half + 12, area.y + 18, half, area.height - 18), pout.w / (float)pout.h, 10); GUI.Label(new Rect(lp.x, lp.y - 20, lp.width, 18), left, new GUIStyle(m_Head) { normal = { textColor = Accent } }); GUI.Label(new Rect(rp.x, rp.y - 20, rp.width, 18), right, m_Head); Pic(lp, m_PaintIn, 1); Pic(rp, m_PaintOut, 1); return true;
        }
        void StraightenView(Rect area, StraightenStep st)
        {
            if (!StepPics(area, out var lp, out _, out _, "DRAG THE CORNERS ONTO A REAL RECTANGLE", "FLAT")) return; var e = Event.current; int id = GUIUtility.GetControlID("TextureCorners".GetHashCode(), FocusType.Passive); var r = lp; Vector2 S(Vector2 c) => new Vector2(r.x + c.x * r.width, r.yMax - c.y * r.height);
            if (e.type == EventType.Repaint)
            {
                var pts = st.corners.Select(c => (Vector3)S(c)).ToArray(); Handles.color = new Color(Accent.r, Accent.g, Accent.b, 0.95f); Handles.DrawAAPolyLine(3f, pts[0], pts[1], pts[2], pts[3], pts[0]); Handles.color = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
                // lines as they'd be on the real thing
                for (int k = 1; k < 4; k++) { float q = k / 4f; Vector3 M(Vector2 uv) { var c = st.corners; float dx1 = c[1].x - c[2].x, dx2 = c[3].x - c[2].x, dx3 = c[0].x - c[1].x + c[2].x - c[3].x, dy1 = c[1].y - c[2].y, dy2 = c[3].y - c[2].y, dy3 = c[0].y - c[1].y + c[2].y - c[3].y, det = dx1 * dy2 - dx2 * dy1, g = Mathf.Abs(det) < 1e-9f ? 0 : (dx3 * dy2 - dx2 * dy3) / det, h = Mathf.Abs(det) < 1e-9f ? 0 : (dx1 * dy3 - dx3 * dy1) / det, w = g * uv.x + h * uv.y + 1; return S(new Vector2(((c[1].x - c[0].x + g * c[1].x) * uv.x + (c[3].x - c[0].x + h * c[3].x) * uv.y + c[0].x) / w, ((c[1].y - c[0].y + g * c[1].y) * uv.x + (c[3].y - c[0].y + h * c[3].y) * uv.y + c[0].y) / w)); } Handles.DrawLine(M(new Vector2(q, 0)), M(new Vector2(q, 1))); Handles.DrawLine(M(new Vector2(0, q)), M(new Vector2(1, q))); }
                for (int i = 0; i < 4; i++) { var h = new Rect(pts[i].x - 8, pts[i].y - 8, 16, 16); EditorGUI.DrawRect(h, new Color(0, 0, 0, 0.7f)); EditorGUI.DrawRect(new Rect(h.x + 2, h.y + 2, 12, 12), i == m_Corner ? Color.white : Accent); }
                // a magnifier while a corner is dragged, so it can go exactly there
                if (m_Corner >= 0) { var c = st.corners[m_Corner]; var z = new Rect(r.x + 8, r.y + 8, 150, 150); if (c.x < 0.4f && c.y > 0.6f) z.x = r.xMax - 158; GUI.DrawTextureWithTexCoords(z, m_PaintIn, new Rect(c.x - 0.05f, c.y - 0.05f * r.width / r.height, 0.1f, 0.1f * r.width / r.height), false); Handles.color = Accent; Handles.DrawLine(new Vector3(z.center.x, z.y), new Vector3(z.center.x, z.yMax)); Handles.DrawLine(new Vector3(z.x, z.center.y), new Vector3(z.xMax, z.center.y)); EditorGUI.DrawRect(new Rect(z.x, z.y, z.width, 1), Accent); EditorGUI.DrawRect(new Rect(z.x, z.yMax, z.width, 1), Accent); EditorGUI.DrawRect(new Rect(z.x, z.y, 1, z.height), Accent); EditorGUI.DrawRect(new Rect(z.xMax, z.y, 1, z.height), Accent); }
            }
            foreach (var c in st.corners) { var s = S(c); EditorGUIUtility.AddCursorRect(new Rect(s.x - 10, s.y - 10, 20, 20), MouseCursor.MoveArrow); }
            if (e.type == EventType.MouseDown && e.button == 0 && new Rect(r.x - 12, r.y - 12, r.width + 24, r.height + 24).Contains(e.mousePosition)) { int near = -1; float d = 26; for (int i = 0; i < 4; i++) { float q = (S(st.corners[i]) - e.mousePosition).magnitude; if (q < d) { d = q; near = i; } } if (near >= 0) { m_Corner = near; GUIUtility.hotControl = id; e.Use(); } }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && m_Corner >= 0) { st.corners[m_Corner] = new Vector2(Mathf.Clamp((e.mousePosition.x - r.x) / r.width, -0.2f, 1.2f), Mathf.Clamp((r.yMax - e.mousePosition.y) / r.height, -0.2f, 1.2f)); Changed(); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; m_Corner = -1; Changed(); e.Use(); }
        }

        void PickView(Rect area, RecolourStep rc)
        {
            if (!StepPics(area, out var lp, out _, out var pin, "CLICK THE COLOUR TO CHANGE", "AFTER")) return; var e = Event.current; EditorGUIUtility.AddCursorRect(lp, MouseCursor.Link); if (e.type != EventType.MouseDown || e.button != 0 || !lp.Contains(e.mousePosition)) return; int x = Mathf.Clamp((int)((e.mousePosition.x - lp.x) / lp.width * pin.w), 0, pin.w - 1), y = Mathf.Clamp((int)((lp.yMax - e.mousePosition.y) / lp.height * pin.h), 0, pin.h - 1); Color s = Color.clear; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) s += pin[x + i, y + j]; s /= 9; s.a = 1; if (rc.sel >= 0 && rc.sel < rc.pairs.Count) rc.pairs[rc.sel].from = s; rc.picking = false; Changed(); e.Use(); Repaint();
        }

        // ---- what gives the repeat away
        [SerializeField] bool m_ShowSpots; Texture2D m_Glow; Pixels m_GlowFor, m_ScoreFor, m_ScoreBeforeFor; int m_Score, m_ScoreBefore;
        void Spots(Rect r, float n)
        {
            if (!m_ShowSpots || m_SmallAfter == null) return; if (m_GlowFor != m_SmallAfter) { m_GlowFor = m_SmallAfter; Put(ref m_Glow, TextureRepeat.Glow(m_SmallAfter)); }
            if (Event.current.type == EventType.Repaint && m_Glow) GUI.DrawTextureWithTexCoords(r, m_Glow, new Rect(0, 0, n, n), true);
        }
        string ScoreLine()
        {
            if (m_SmallAfter == null || m_Small == null) return ""; if (m_ScoreFor != m_SmallAfter) { m_ScoreFor = m_SmallAfter; m_Score = TextureRepeat.Score(m_SmallAfter); } if (m_ScoreBeforeFor != m_Small) { m_ScoreBeforeFor = m_Small; m_ScoreBefore = TextureRepeat.Score(m_Small); }
            return "Repeat shows: " + m_ScoreBefore + (m_Score != m_ScoreBefore ? " → " + m_Score : "") + " of 100";
        }

        // ---- putting stickers on
        int m_StickerDrag = -1; bool m_StickerTrail; Vector2 m_StickerOff; int m_StickerSeed = 1;
    }
}
