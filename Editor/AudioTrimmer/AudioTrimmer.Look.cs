using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The Audio Trimmer's window: painted like Texture Studio and the Cinematic Editor. The waveform on top, the
    /// tools on the left, the picked tool's settings in the middle, the cut pieces and saving on the right.</summary>
    public partial class AudioTrimmer
    {
        enum Page { Edit, Batch, Layers, Make, Record, Ambience, Check, Stems }
        enum Tool { Trim, Level, Fades, Pitch, Tone, Reverb, Clean, Loop, Vary, Find, Echo, Match, Draw, Voice }
        [SerializeField] float muffle, thin, grit; [SerializeField] int fadeShape = 1;
        enum Hand { Select, Move, Volume }

        [SerializeField] Page page;
        [SerializeField] Hand hand;
        // level: 0 as it is, 1 as loud as it goes, 2 a loudness
        [SerializeField] int levelMode; [SerializeField] float loudness = -18f;
        [SerializeField] float bass, middle, treble;
        [SerializeField] float revAmount, revSize = 0.4f, revDull = 0.4f;
        // variations
        [SerializeField] int varCount = 5, varSeed = 1; [SerializeField] float varPitch = 3f, varVolume = 3f, varTone = 8f, varStart = 15f;
        struct Vary { public float pitch, gain, tilt, skip; public bool isOne; }
        float[] varTimes; int varFirst, lastVar = -2; // the variations being played one after another, and when each starts
        // batch
        [SerializeField] List<AudioClip> batch = new List<AudioClip>();
        [SerializeField] bool bTrim = true, bClean, bLoud = true, bFade = true, bMono, bTone, bReverb, bMatch; [SerializeField] AudioClip bMatchTo; [SerializeField] float bStrength = 0.5f, bCleanDb = 20f, bTarget = -18f, bFadeMs = 5f, bQuiet = -45f;
        [SerializeField] string bName = "", bFolder = "Processed"; [SerializeField] bool bReplace;
        readonly Dictionary<AudioClip, string> bInfo = new Dictionary<AudioClip, string>();
        Vector2 setScroll, rightScroll, batchScroll;

        // ---- look (the Cinematic Editor's palette)
        static readonly Color Accent = new Color(1f, 0.62f, 0.2f);
        static Color Bg => EditorGUIUtility.isProSkin ? new Color(0.12f, 0.125f, 0.15f) : new Color(0.72f, 0.73f, 0.76f);
        static Color Panel => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.215f) : new Color(0.83f, 0.84f, 0.87f);
        static Color PanelHi => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.235f, 0.28f) : new Color(0.9f, 0.9f, 0.93f);
        static Color Ink(float a) => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, a);
        GUIStyle sHead, sBig, sHint, sTileTitle, sTileSub, sPill, sPillWide, sPillNarrow, sEmpty, sTime, sCard; Texture2D panelTex;

        void Styles()
        {
            if (sHead != null && panelTex) return;
            panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; panelTex.SetPixel(0, 0, Panel); panelTex.Apply();
            sHead = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = Ink(0.45f) } };
            sBig = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = Ink(0.92f) } };
            sHint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Ink(0.5f) } };
            sTileTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Ink(0.9f) }, clipping = TextClipping.Clip };
            sTileSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Ink(0.5f) }, clipping = TextClipping.Clip };
            sPill = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, fixedHeight = 22, padding = new RectOffset(10, 10, 2, 2) }; sPillWide = sPill; sPillNarrow = new GUIStyle(sPill) { padding = new RectOffset(5, 5, 2, 2), margin = new RectOffset(1, 1, 2, 2) };
            sEmpty = new GUIStyle(EditorStyles.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink(0.55f) } };
            sTime = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Ink(0.9f) }, alignment = TextAnchor.MiddleRight };
            sCard = new GUIStyle { normal = { background = panelTex }, padding = new RectOffset(12, 12, 10, 12), margin = new RectOffset(6, 6, 6, 6) };
        }

        bool Pill(string text, bool on, string tip = "", float width = 0)
        {
            var old = GUI.backgroundColor; if (on) GUI.backgroundColor = Accent;
            bool click = width > 0 ? GUILayout.Button(new GUIContent(text, tip), sPill, GUILayout.Width(width)) : GUILayout.Button(new GUIContent(text, tip), sPill);
            GUI.backgroundColor = old;
            return click;
        }
        bool Big(string text, string tip, float height = 28, float width = 0)
        {
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            var st = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            bool c = width > 0 ? GUILayout.Button(new GUIContent(text, tip), st, GUILayout.Height(height), GUILayout.Width(width)) : GUILayout.Button(new GUIContent(text, tip), st, GUILayout.Height(height));
            GUI.backgroundColor = old; return c;
        }

        void OnGUI()
        {
            Styles(); sPill = sPillWide;
            wantsMouseMove = true;
            if (Event.current.type == EventType.MouseMove) Repaint();
            if (page == Page.Edit) HandleKeys();
            // every change made in this window can be undone with Ctrl+Z (Unity groups a whole drag into one step)
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint && Event.current.type != EventType.MouseMove) Undo.RecordObject(this, "Audio Trimmer");
            float W = position.width, H = position.height;
            EditorGUI.DrawRect(new Rect(0, 0, W, H), Bg);
            TopBar(new Rect(0, 0, W, 46));
            var body = new Rect(0, 46, W, H - 46);
            if (page == Page.Batch) { BatchPage(body); return; }
            if (page == Page.Layers) { LayersPage(body); return; }
            if (page == Page.Make) { MakePage(body); return; }
            if (page == Page.Record) { RecordPage(body); return; }
            if (page == Page.Ambience) { AmbiencePage(body); return; }
            if (page == Page.Check) { CheckPage(body); return; }
            if (page == Page.Stems) { StemsPage(body); return; }
            if (!clip || samples == null) { NoSound(body); return; }
            EditLayout(body);
            // the picked piece follows its edges
            if (activeRegion >= 0 && activeRegion < regions.Count) { regions[activeRegion].start = selStart; regions[activeRegion].end = selEnd; }
        }

        [SerializeField] float waveSize, rightSize; 

        /// <summary>A bar between two panels that can be dragged.</summary>
        float SplitBar(int which, Rect bar, bool sideways, float size, float dir, float min, float max)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("AudioSplit".GetHashCode() + which, FocusType.Passive); EditorGUIUtility.AddCursorRect(bar, sideways ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical); bool hot = GUIUtility.hotControl == id;
            if (hot || bar.Contains(e.mousePosition)) EditorGUI.DrawRect(sideways ? new Rect(bar.center.x - 1, bar.y, 2, bar.height) : new Rect(bar.x, bar.center.y - 1, bar.width, 2), Accent);
            if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && hot) { size += (sideways ? e.delta.x : e.delta.y) * dir; e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && hot) { GUIUtility.hotControl = 0; e.Use(); }
            return Mathf.Clamp(size, min, max);
        }

        void TopBar(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Ink(0.06f));
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 11, r.width - 24, r.height - 12)); GUILayout.BeginHorizontal();
            GUILayout.Label("Audio Trimmer", sBig, GUILayout.Width(132)); GUILayout.Space(8);
            if (Pill("Edit a sound", page == Page.Edit, "Cut, clean and change one sound")) { if (page != Page.Edit) Stop(); page = Page.Edit; }
            if (Pill("Make", page == Page.Make, "Make a sound from nothing: whooshes, hits, steps, explosions...") && page != Page.Make) { Stop(); page = Page.Make; }
            if (Pill("Record", page == Page.Record, "From the microphone") && page != Page.Record) { Stop(); page = Page.Record; }
            if (Pill("Layers", page == Page.Layers, "A few sounds on top of each other, saved as one") && page != Page.Layers) { SyncLinked(true); Stop(); page = Page.Layers; }
            if (Pill("Ambience", page == Page.Ambience, "A background loop with sounds scattered over it") && page != Page.Ambience) { SyncLinked(true); Stop(); page = Page.Ambience; }
            if (Pill("Music parts", page == Page.Stems, "Split a song into its calm part and its drums") && page != Page.Stems) { SyncLinked(true); Stop(); page = Page.Stems; }
            if (Pill("Check", page == Page.Check, "What's wrong with the project's sounds") && page != Page.Check) { SyncLinked(true); Stop(); page = Page.Check; }
            if (Pill("Batch", page == Page.Batch, "Do the same things to lots of sounds at once")) { page = Page.Batch; Stop(); }
            GUILayout.FlexibleSpace();
            if (page == Page.Edit && clip && samples != null)
            {
                LayoutMenu(); GUILayout.Space(6);
                if (GUILayout.Button(new GUIContent("↶ Undo", "Ctrl+Z"), GUILayout.Width(70), GUILayout.Height(26))) Undo.PerformUndo();
                if (GUILayout.Button(new GUIContent("Redo ↷", "Ctrl+Y"), GUILayout.Width(70), GUILayout.Height(26))) Undo.PerformRedo();
                if (GUILayout.Button(new GUIContent("Reset", "Back to the whole sound with no changes (can be undone)"), GUILayout.Width(60), GUILayout.Height(26))) ResetAll();
            }
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }

        void NoSound(Rect r)
        {
            GUILayout.BeginArea(new Rect(r.x + 20, r.y, r.width - 40, r.height)); GUILayout.FlexibleSpace();
            GUILayout.Label(clip ? loadError : "Drop a sound here, or pick one.", sEmpty); GUILayout.Space(10);
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            var c = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(260), GUILayout.Height(22)); if (c != clip) Load(c);
            if (GUILayout.Button("Use selected", GUILayout.Width(100), GUILayout.Height(22)) && Selection.activeObject is AudioClip sel) Load(sel);
            if (clip && samples == null && GUILayout.Button("Set Load Type to Decompress On Load", GUILayout.Height(22))) MakeReadable();
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.FlexibleSpace(); GUILayout.EndArea();
            DropSound(r);
        }

        void DropSound(Rect r)
        {
            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition))
            {
                var c = DragAndDrop.objectReferences.OfType<AudioClip>().FirstOrDefault(); if (!c) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Load(c); }
                e.Use();
            }
        }

        // ================================================================ the waveform

        void WaveArea(Rect r)
        {
            EditorGUI.DrawRect(r, Panel);
            // the name, the sound
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, 28)); GUILayout.BeginHorizontal();
            NameRow();
            GUILayout.Space(10);
            var c = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(170), GUILayout.Height(24)); if (c != clip) { Load(c); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal(); GUILayout.EndArea();

            var ruler = new Rect(r.x + 10, r.y + 42, r.width - 20, 18); var strip = new Rect(ruler.x, ruler.yMax, ruler.width, 16);
            var mini = new Rect(ruler.x, r.yMax - 66, ruler.width, 22); var wave = new Rect(ruler.x, strip.yMax, ruler.width, mini.y - strip.yMax - 4);
            DrawRuler(ruler); DrawStrip(strip); DrawWave(wave); Minimap(mini);
            HandleMouse(wave, ruler, strip, Event.current); DropSound(wave);

            // playing
            GUILayout.BeginArea(new Rect(r.x + 10, r.yMax - 38, r.width - 20, 32)); GUILayout.BeginHorizontal();
            bool isPlaying = source && source.isPlaying;
            if (Big(isPlaying ? "❚❚" : "▶", "Play / pause (Space)", 28, 46)) TogglePlay();
            if (GUILayout.Button(new GUIContent("■", "Stop"), GUILayout.Height(28), GUILayout.Width(32))) { Stop(); cursor = playSelectionOnly ? selStart : 0f; }
            if (GUILayout.Button(new GUIContent("|◀", "To the start (Home)"), GUILayout.Height(28), GUILayout.Width(34))) { Stop(); cursor = selStart; }
            GUILayout.Space(6);
            GUILayout.BeginVertical(); GUILayout.Space(3); GUILayout.BeginHorizontal();
            if (Pill("Loop", loop, "Play it again and again")) loop = !loop;
            bool slim = r.width < 800, tight = r.width < 660; if (Pill(tight ? "Part" : slim ? "Selection" : "Only the selection", playSelectionOnly, "Off: play the whole sound")) { playSelectionOnly = !playSelectionOnly; Stop(); }
            GUILayout.Space(slim ? 5 : 14);
            if (Pill(slim ? "✂" : "✂ Select", hand == Hand.Select, "Dragging picks the part to keep")) hand = Hand.Select;
            if (Pill(slim ? "✎" : "✎ Volume", hand == Hand.Volume, "Dragging draws how loud it is")) hand = hand == Hand.Volume ? Hand.Select : Hand.Volume;
            if (Pill(slim ? "✋" : "✋ Move", hand == Hand.Move, "Dragging moves the view; nothing gets cut (key H swaps, middle-drag always moves)")) hand = Hand.Move;
            GUILayout.Space(slim ? 5 : 14);
            if (Pill("All", false, "See the whole sound")) { viewStart = 0f; viewEnd = Length; }
            if (Pill(slim ? "Zoom" : "Zoom to selection", false, "Zoom to the selection")) { float p = (selEnd - selStart) * 0.06f; viewStart = Mathf.Max(0, selStart - p); viewEnd = Mathf.Min(Length, selEnd + p); }
            if (!tight && Pill(slim ? "Keep all" : "Keep everything", false, "Takes the selection away: the whole sound is kept (Escape)")) { SelectAll(); }
            GUILayout.EndHorizontal(); GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (!tight) GUILayout.Label($"{Fmt(Playhead())} / {Fmt(Length)}", sTime, GUILayout.Width(140), GUILayout.Height(28));
            GUILayout.EndHorizontal(); GUILayout.EndArea();
        }

        void SelectAll() { selStart = 0f; selEnd = Length; activeRegion = -1; previewDirty = true; }

        // the whole sound, small: drag the bright part to move along it, its ends to zoom
        void Minimap(Rect r)
        {
            var e = Event.current; int id = GUIUtility.GetControlID("AudioMini".GetHashCode(), FocusType.Passive); float k = r.width / Mathf.Max(1e-4f, Length);
            var view = new Rect(r.x + viewStart * k, r.y, Mathf.Max(6, (viewEnd - viewStart) * k), r.height);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(0.07f, 0.075f, 0.09f));
                int w = Mathf.Max(1, (int)r.width);
                if (miniPeaks == null || miniPeaks.Length != w || miniFor != Src) { miniPeaks = new float[w]; miniFor = Src; for (int x = 0; x < w; x++) { Peak((int)((long)x * frames / w), Mathf.Max((int)((long)x * frames / w) + 1, (int)((long)(x + 1) * frames / w)), out float lo, out float hi); miniPeaks[x] = Mathf.Max(-lo, hi); } }
                for (int x = 0; x < w; x++) { float h = Mathf.Max(1, miniPeaks[x] * (r.height - 2)); EditorGUI.DrawRect(new Rect(r.x + x, r.center.y - h / 2, 1, h), new Color(0.45f, 0.5f, 0.58f)); }
                EditorGUI.DrawRect(new Rect(r.x + selStart * k, r.yMax - 2, (selEnd - selStart) * k, 2), new Color(0.4f, 0.75f, 1f));
                EditorGUI.DrawRect(view, new Color(1f, 1f, 1f, 0.1f));
                EditorGUI.DrawRect(new Rect(view.x, r.y, 2, r.height), Accent); EditorGUI.DrawRect(new Rect(view.xMax - 2, r.y, 2, r.height), Accent);
                EditorGUI.DrawRect(new Rect(r.x + Playhead() * k, r.y, 1, r.height), new Color(1f, 0.35f, 0.3f));
            }
            EditorGUIUtility.AddCursorRect(new Rect(view.x - 5, r.y, 10, r.height), MouseCursor.ResizeHorizontal); EditorGUIUtility.AddCursorRect(new Rect(view.xMax - 5, r.y, 10, r.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(view.x + 5, r.y, Mathf.Max(1, view.width - 10), r.height), MouseCursor.Pan);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id; GUIUtility.keyboardControl = 0; float x = e.mousePosition.x; miniPart = Mathf.Abs(x - view.x) < 6 ? 1 : Mathf.Abs(x - view.xMax) < 6 ? 2 : 0;
                if (miniPart == 0 && !view.Contains(e.mousePosition)) { float span = viewEnd - viewStart, mid = (x - r.x) / k; viewStart = Mathf.Clamp(mid - span / 2, 0, Length - span); viewEnd = viewStart + span; }
                e.Use();
            }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                float d = e.delta.x / k, span = viewEnd - viewStart;
                if (miniPart == 0) { viewStart = Mathf.Clamp(viewStart + d, 0, Mathf.Max(0, Length - span)); viewEnd = viewStart + span; }
                else if (miniPart == 1) viewStart = Mathf.Clamp(viewStart + d, 0, viewEnd - 0.02f);
                else viewEnd = Mathf.Clamp(viewEnd + d, viewStart + 0.02f, Length);
                e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
        }
        float[] miniPeaks, miniFor; int miniPart;

        Vector2 downAt; const float DragNeeded = 12f;

        /// <summary>
        /// A click only moves the playhead. Cutting needs a real drag; until it's long enough nothing shows, and once the
        /// new selection shows it stays.
        /// </summary>
        void HandleMouse(Rect wave, Rect ruler, Rect strip, Event e)
        {
            var all = new Rect(ruler.x, ruler.y, ruler.width, wave.yMax - ruler.y); if (VolumeMouse(wave, e)) return;
            if (e.type == EventType.Repaint) EditorGUIUtility.AddCursorRect(wave, hand == Hand.Move ? MouseCursor.Pan : MouseCursor.Arrow);
            switch (e.type)
            {
                case EventType.ScrollWheel when all.Contains(e.mousePosition):
                {
                    float span = viewEnd - viewStart;
                    if (e.shift || Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y)) { float d = (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y) * span * 0.04f; viewStart = Mathf.Clamp(viewStart + d, 0, Mathf.Max(0, Length - span)); viewEnd = viewStart + span; }
                    else { float at = T(e.mousePosition.x, wave), k = e.delta.y > 0 ? 1.25f : 0.8f; viewStart = Mathf.Clamp(at - (at - viewStart) * k, 0f, Length); viewEnd = Mathf.Clamp(at + (viewEnd - at) * k, 0f, Length); if (viewEnd - viewStart < 0.01f) viewEnd = Mathf.Min(Length, viewStart + 0.01f); }
                    e.Use(); break;
                }
                case EventType.MouseDown when all.Contains(e.mousePosition):
                {
                    GUIUtility.keyboardControl = 0; // so Space and A work again after typing in a box
                    float t = T(e.mousePosition.x, wave); downAt = e.mousePosition;
                    bool whole = selStart <= 0.0001f && selEnd >= Length - 0.0001f;
                    if (e.button == 2 || e.alt || (hand == Hand.Move && wave.Contains(e.mousePosition) && e.button == 0)) { drag = Drag.Pan; dragAnchor = t; }
                    else if (e.button != 0) break;
                    else if (strip.Contains(e.mousePosition)) { int hit = regions.FindIndex(rg => t >= rg.start && t <= rg.end); if (hit >= 0) SelectRegion(hit, e.clickCount > 1); e.Use(); break; }
                    else if (ruler.Contains(e.mousePosition)) { drag = Drag.Seek; Seek(t); }
                    else if (!LoopOn && Near(FadeInHandle(wave), e.mousePosition)) drag = Drag.FadeIn;
                    else if (!LoopOn && Near(FadeOutHandle(wave), e.mousePosition)) drag = Drag.FadeOut;
                    else if (e.clickCount == 2) { SelectAll(); drag = Drag.None; e.Use(); break; }
                    else if (!whole && Mathf.Abs(e.mousePosition.x - X(selStart, wave)) < 6) drag = Drag.Start;
                    else if (!whole && Mathf.Abs(e.mousePosition.x - X(selEnd, wave)) < 6) drag = Drag.End;
                    else if (whole && e.mousePosition.x - wave.x < 5) drag = Drag.Start;
                    else if (whole && wave.xMax - e.mousePosition.x < 5 && viewEnd >= Length - 0.0001f) drag = Drag.End;
                    else if (Mathf.Abs(e.mousePosition.x - X(Playhead(), wave)) < 5) drag = Drag.Seek; // the red line can be dragged
                    else { drag = Drag.Make; dragAnchor = t; making = false; }
                    e.Use(); break;
                }
                case EventType.MouseDrag when drag != Drag.None:
                {
                    float t = T(e.mousePosition.x, wave);
                    switch (drag)
                    {
                        case Drag.Start: selStart = Mathf.Min(t, selEnd - 0.005f); break;
                        case Drag.End: selEnd = Mathf.Max(t, selStart + 0.005f); break;
                        case Drag.Make:
                            if (!making && Mathf.Abs(e.mousePosition.x - downAt.x) < DragNeeded) { e.Use(); return; }
                            if (!making) { making = true; activeRegion = -1; }
                            selStart = Mathf.Min(dragAnchor, t); selEnd = Mathf.Max(dragAnchor, t); if (selEnd - selStart < 0.005f) { selStart = Mathf.Clamp(selStart, 0f, Length - 0.005f); selEnd = selStart + 0.005f; } break;
                        case Drag.FadeIn: fadeIn = Mathf.Clamp(t - selStart, 0f, Mathf.Max(0f, selEnd - selStart - fadeOut)); break;
                        case Drag.FadeOut: fadeOut = Mathf.Clamp(selEnd - t, 0f, Mathf.Max(0f, selEnd - selStart - fadeIn)); break;
                        case Drag.Seek: Seek(t); break;
                        case Drag.Pan: { float shift = dragAnchor - t, span = viewEnd - viewStart; viewStart = Mathf.Clamp(viewStart + shift, 0f, Mathf.Max(0, Length - span)); viewEnd = viewStart + span; break; }
                    }
                    if (drag != Drag.Seek && drag != Drag.Pan) previewDirty = true;
                    e.Use(); break;
                }
                case EventType.MouseUp when drag != Drag.None:
                {
                    var was = drag; drag = Drag.None;
                    if (was == Drag.Make && !making) Seek(T(downAt.x, wave)); // just a click
                    else if (was == Drag.Pan && Mathf.Abs(e.mousePosition.x - downAt.x) < 4 && e.button == 0) Seek(T(downAt.x, wave));
                    else if (was == Drag.FadeIn || was == Drag.FadeOut) { if (source && source.isPlaying) Restart(); }
                    else if (was == Drag.Make || was == Drag.Start || was == Drag.End) { cursor = selStart; if (source && source.isPlaying) { Stop(); Play(selStart); } }
                    e.Use(); break;
                }
            }
        }
        bool making;
        static bool Near(Rect h, Vector2 p) => new Rect(h.x - 3, h.y - 2, h.width + 6, h.height + 6).Contains(p);

        // ================================================================ the tools

        static readonly Dictionary<Tool, (string title, Color tint, string help)> Info = new Dictionary<Tool, (string, Color, string)>
        {
            { Tool.Trim, ("Trim", new Color(0.4f, 0.75f, 1f), "Drag over the waveform to pick the part to keep. Click to move the red line. Drag an edge to change it.") },
            { Tool.Fades, ("Fades", new Color(1f, 0.85f, 0.35f), "Starts and ends softly. The yellow squares at the top of the waveform do the same.") },
            { Tool.Loop, ("Seamless loop", new Color(0.45f, 1f, 0.6f), "Blends the end into the start so it loops without a jump. The saved sound is shorter by the blend.") },
            { Tool.Find, ("Split into sounds", new Color(0.8f, 0.65f, 0.5f), "For one file with many sounds (footsteps, hits): finds each one and makes a piece of it.") },
            { Tool.Level, ("Volume", new Color(0.95f, 0.75f, 0.3f), "Loudness is how loud it sounds, not its tallest spike. Give all your sounds the same number and they sit together.") },
            { Tool.Pitch, ("Pitch and length", new Color(0.75f, 0.58f, 1f), "Like speeding up a tape, or the pitch and the length on their own.") },
            { Tool.Tone, ("Tone", new Color(0.55f, 0.85f, 1f), "The character of the sound. Click a ready-made one, then change it.") },
            { Tool.Clean, ("Remove background", new Color(0.45f, 0.85f, 0.7f), "For steady sounds behind the one you want: hiss, a fan, hum, room noise. It can't take out music or voices.") },
            { Tool.Reverb, ("Room", new Color(0.6f, 0.75f, 1f), "Puts the sound in a room. The saved sound gets longer, because the echo rings out (in a seamless loop it goes round to the start).") },
            { Tool.Echo, ("Echo", new Color(0.65f, 0.9f, 0.95f), "Repeats after a while, each quieter than the one before.") },
            { Tool.Match, ("Match another sound", new Color(1f, 0.75f, 0.6f), "Makes it as deep, bright and loud as a sound that already fits your game.") },
            { Tool.Draw, ("Volume by hand", new Color(0.45f, 1f, 0.6f), "Draw how loud it is along the wave.") },
            { Tool.Voice, ("Voice effects", new Color(0.8f, 0.6f, 1f), "Monsters, robots, ghosts, radios... Pick one, then change it. It also sets the pitch, tone, echo and room that go with it.") },
            { Tool.Vary, ("Variations", new Color(0.95f, 0.55f, 0.75f), "The same sound a bit different each time, so a footstep or a hit doesn't sound copied.") },
        };
        bool Same(AudioFx.ToneSettings s) => Mathf.Approximately(s.bass, bass) && Mathf.Approximately(s.mid, middle) && Mathf.Approximately(s.treble, treble) && Mathf.Approximately(s.muffle, muffle) && Mathf.Approximately(s.thin, thin) && Mathf.Approximately(s.grit, grit);
        bool HasTone => ToneNow.Any;

        bool IsOn(Tool t) => t == Tool.Trim ? !inLayer && selStart > 0.0001f || !inLayer && selEnd < Length - 0.0001f : t == Tool.Clean ? cleanOn : t == Tool.Level ? levelMode > 0 || Mathf.Abs(gainDb) > 0.05f : t == Tool.Fades ? !LoopOn && (fadeIn > 0 || fadeOut > 0) : t == Tool.Pitch ? Mathf.Abs(pitch) >= 0.05f || apart && Mathf.Abs(stretch - 1) > 0.005f : t == Tool.Tone ? HasTone : t == Tool.Reverb ? revAmount > 0.001f : t == Tool.Echo ? echo > 0.001f : t == Tool.Match ? matchTo && (matchTone > 0.01f || matchLoud > 0.01f) : t == Tool.Draw ? volPts.Count > 0 : t == Tool.Voice ? voice.Any : t == Tool.Loop && LoopOn && !inLayer;
        void ResetTool(Tool t)
        {
            switch (t)
            {
                case Tool.Trim: SelectAll(); break;
                case Tool.Clean: cleanOn = hearRemoved = false; rumble = gaps = 0f; bgFrom = bgTo = -1f; SourceChanged(); break;
                case Tool.Level: levelMode = 0; gainDb = 0f; break;
                case Tool.Fades: fadeIn = fadeOut = 0f; break;
                case Tool.Pitch: pitch = 0f; stretch = 1f; break;
                case Tool.Echo: echo = 0f; break;
                case Tool.Match: matchTo = null; break;
                case Tool.Voice: voice = new VoiceFx(); pitch = 0; stretch = 1; bass = middle = treble = muffle = thin = grit = 0f; echo = 0; revAmount = 0; break;
                case Tool.Draw: volPts.Clear(); if (hand == Hand.Volume) hand = Hand.Select; break;
                case Tool.Tone: bass = middle = treble = muffle = thin = grit = 0f; break;
                case Tool.Reverb: revAmount = 0f; break;
                case Tool.Loop: loopFade = 0f; break;
            }
            previewDirty = true; GUIUtility.keyboardControl = 0; GUI.changed = true;
        }

        // a slider that only writes when it's moved (its limits can shrink with the selection without changing the value)
        static bool Slide(string label, string tip, ref float v, float lo, float hi)
        {
            EditorGUI.BeginChangeCheck(); float n = EditorGUILayout.Slider(new GUIContent(label, tip), Mathf.Clamp(v, lo, hi), lo, hi);
            if (!EditorGUI.EndChangeCheck()) return false; v = n; return true;
        }

        void TrimGUI()
        {
            GUILayout.BeginHorizontal(); float lw = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 36;
            selStart = Mathf.Clamp(EditorGUILayout.FloatField("Start", selStart), 0f, Length); GUILayout.Space(4); selEnd = Mathf.Clamp(EditorGUILayout.FloatField("End", selEnd), 0f, Length);
            EditorGUIUtility.labelWidth = lw; GUILayout.EndHorizontal();
            if (selEnd < selStart + 0.005f) selEnd = Mathf.Min(Length, selStart + 0.005f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Cut the silence off", "Takes the quiet bits off both ends"))) TrimSilence();
            if (GUILayout.Button(new GUIContent("No clicks", "Moves both ends to where the wave crosses zero"))) SnapToZero();
            GUILayout.EndHorizontal();
        }

        void FadesGUI()
        {
            float len = selEnd - selStart; bool ch = false;
            ch |= Slide("Fade in", "Seconds", ref fadeIn, 0f, Mathf.Max(0.001f, len - fadeOut)); ch |= Slide("Fade out", "Seconds", ref fadeOut, 0f, Mathf.Max(0.001f, len - fadeIn));
            GUILayout.BeginHorizontal();
            if (Pill("Tiny", false, "5 ms at both ends, so it can't click")) { fadeIn = fadeOut = Mathf.Min(0.005f, len * 0.4f); ch = true; } if (Pill("Soft", false)) { fadeIn = Mathf.Min(0.05f, len * 0.2f); fadeOut = Mathf.Min(0.3f, len * 0.4f); ch = true; } if (Pill("Long", false)) { fadeIn = len * 0.25f; fadeOut = len * 0.4f; ch = true; }
            GUILayout.FlexibleSpace();
            foreach (var s in new[] { ("Straight", 0), ("Smooth", 1), ("Slow", 2) }) if (Pill(s.Item1, fadeShape == s.Item2, "The shape of both fades")) { fadeShape = s.Item2; ch = true; }
            GUILayout.EndHorizontal();
            if (LoopOn) GUILayout.Label("The seamless loop is on, which has no start or end to fade. Moving these turns it off.", sHint);
            if (ch && LoopOn) loopFade = 0f;
        }

        void FindGUI()
        {
            Slide("Quiet below", "dB. Anything quieter than this, compared to the loudest part, counts as silence", ref threshDb, -70f, -6f); Slide("Gap needed", "Seconds of quiet that split two sounds", ref minGap, 0.01f, 1f);
            Slide("Shortest", "Blips shorter than this (seconds) are ignored", ref minLen, 0.005f, 0.5f); Slide("Padding", "A little kept before and after each (seconds)", ref pad, 0f, 0.2f);
            if (GUILayout.Button("Find the sounds")) FindSounds();
        }

        void LevelGUI()
        {
            GUILayout.BeginHorizontal(); if (Pill("By hand", levelMode == 0)) levelMode = 0; if (Pill("Full", levelMode == 1, "As loud as it goes: its tallest spike just under the limit")) levelMode = 1; if (Pill("A loudness", levelMode == 2, "Makes it sound as loud as your other sounds")) levelMode = 2; GUILayout.FlexibleSpace(); GUILayout.Label(float.IsNaN(levelNow) ? "" : $"{levelNow:0.0} LUFS", new GUIStyle(sHint) { alignment = TextAnchor.MiddleRight }); GUILayout.EndHorizontal();
            if (levelMode == 2)
            {
                Slide("Loudness", "LUFS, higher is louder", ref loudness, -40f, -8f);
                GUILayout.BeginHorizontal(); foreach (var p in new[] { ("Ambience", -26f), ("Effects", -18f), ("Music", -16f), ("Loud hit", -12f) }) if (Pill(p.Item1, Mathf.Abs(loudness - p.Item2) < 0.01f, p.Item2 + " LUFS")) loudness = p.Item2; GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                if (levelShort > 0.2f) GUILayout.Label($"It can't get there without crackling: {levelShort:0.0} dB short (its spikes are already at the limit).", sHint);
            }
            else if (levelMode == 0) Slide("Volume", "dB", ref gainDb, -24f, 24f);
        }

        static readonly (string name, string tip, AudioFx.ToneSettings s)[] Tones =
        {
            ("Behind a wall", "", new AudioFx.ToneSettings { bass = 3, muffle = 0.72f }), ("Underwater", "", new AudioFx.ToneSettings { bass = 6, muffle = 0.92f }),
            ("Radio", "", new AudioFx.ToneSettings { mid = 8, muffle = 0.4f, thin = 0.6f, grit = 0.35f }), ("Phone", "", new AudioFx.ToneSettings { mid = 5, muffle = 0.36f, thin = 0.62f }),
            ("Broken", "A speaker that's had it", new AudioFx.ToneSettings { mid = 6, thin = 0.4f, muffle = 0.25f, grit = 0.9f }), ("Deep", "", new AudioFx.ToneSettings { bass = 15, mid = -4, treble = -10 }),
            ("Crisp", "", new AudioFx.ToneSettings { bass = -5, mid = 1, treble = 13 }), ("Warm", "", new AudioFx.ToneSettings { bass = 7, mid = 2, treble = -9, grit = 0.15f }), ("Punchy", "", new AudioFx.ToneSettings { bass = 9, mid = -7, treble = 8, grit = 0.25f }),
        };
        void ToneGUI(float width)
        {
            float x = 0; GUILayout.BeginHorizontal();
            foreach (var p in Tones)
            {
                float w = sPill.CalcSize(new GUIContent(p.name)).x + 4; if (x + w > width - 26 && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w;
                bool same = Same(p.s);
                if (Pill(p.name, same, p.tip)) { bass = p.s.bass; middle = p.s.mid; treble = p.s.treble; muffle = p.s.muffle; thin = p.s.thin; grit = p.s.grit; }
            }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            Slide("Bass", "dB", ref bass, -24f, 24f); Slide("Middle", "dB", ref middle, -24f, 24f); Slide("Treble", "dB", ref treble, -24f, 24f);
            Slide("Muffled", "Takes the high part away: behind a door, under water", ref muffle, 0f, 1f); Slide("Thin", "Takes the low part away: small and far", ref thin, 0f, 1f); Slide("Grit", "Pushes it until it crackles", ref grit, 0f, 1f);
        }

        void RoomGUI(float width)
        {
            float x = 0; GUILayout.BeginHorizontal();
            foreach (var p in new[] { ("Small", 0.22f, 0.12f, 0.6f), ("Bathroom", 0.35f, 0.3f, 0.05f), ("Hall", 0.35f, 0.65f, 0.45f), ("Cave", 0.5f, 0.88f, 0.3f), ("Cathedral", 0.55f, 0.97f, 0.5f) })
            {
                float w = sPill.CalcSize(new GUIContent(p.Item1)).x + 4; if (x + w > width - 26 && x > 0) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; } x += w;
                if (Pill(p.Item1, revAmount > 0 && Mathf.Approximately(revAmount, p.Item2) && Mathf.Approximately(revSize, p.Item3))) { revAmount = p.Item2; revSize = p.Item3; revDull = p.Item4; }
            }
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            Slide("Amount", "", ref revAmount, 0f, 1f); Slide("Size", "Closet to cathedral", ref revSize, 0f, 1f); Slide("Softness", "Hard bright walls, or carpets and curtains", ref revDull, 0f, 1f);
        }

        // ---- random variations
        Vary VaryOf(int i)
        {
            var rnd = new System.Random(varSeed * 1009 + i * 31 + 7); float R() => (float)rnd.NextDouble() * 2 - 1;
            // (spread out: the pitches and the tones are dealt across the range, so no two come out the same)
            float Dealt(int salt) { if (varCount < 2) return R(); var order = Enumerable.Range(0, varCount).OrderBy(k => new System.Random(varSeed * salt + k * 13).Next()).ToList(); return (order[i] + 0.5f + R() * 0.35f) / varCount * 2 - 1; }
            return new Vary { pitch = Dealt(77) * varPitch, gain = R() * varVolume, tilt = Dealt(131) * varTone, skip = Mathf.Abs(R()) * varStart / 1000f, isOne = true };
        }

        void VaryGUI(float width)
        {
            float lw = EditorGUIUtility.labelWidth; EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal(); foreach (var p in new[] { ("Subtle", 1.2f, 1.5f, 3f, 6f), ("Clear", 3f, 3f, 8f, 15f), ("Wild", 6f, 6f, 16f, 30f) }) if (Pill(p.Item1, Mathf.Approximately(varPitch, p.Item2) && Mathf.Approximately(varTone, p.Item4), "How different they are")) { varPitch = p.Item2; varVolume = p.Item3; varTone = p.Item4; varStart = p.Item5; }
            GUILayout.FlexibleSpace(); if (Pill("Shuffle", false, "Other random ones")) varSeed = UnityEngine.Random.Range(1, 99999); GUILayout.EndHorizontal();
            varCount = EditorGUILayout.IntSlider("How many", varCount, 2, 16); Slide("Pitch ±", "Semitones", ref varPitch, 0f, 12f); Slide("Volume ±", "dB", ref varVolume, 0f, 12f); Slide("Tone ±", "Duller or brighter (dB)", ref varTone, 0f, 24f); Slide("Late start", "Starts up to this many milliseconds in, which changes the attack", ref varStart, 0f, 60f);
            bool ch = EditorGUI.EndChangeCheck(); if (ch && varTimes != null) Stop();
            // all of them in a row: press one, or hear them all; the one you hear lights up
            GUILayout.Space(3); bool all = varTimes != null && varTimes.Length > 1; int now = VarNow;
            if (Big(all ? "■  Stop" : "▶  Hear them all in a row", "The original first, then every variation with a short pause in between", 26)) { if (all) Stop(); else PlayVariations(-1, varCount); }
            int per = Mathf.Max(3, Mathf.FloorToInt((width - 24) / 38f));
            for (int i = -1; i < varCount; i++)
            {
                if ((i + 1) % per == 0) GUILayout.BeginHorizontal();
                var v = i < 0 ? default : VaryOf(i); var old = GUI.backgroundColor; if (varTimes != null && now == i) GUI.backgroundColor = Info[Tool.Vary].tint;
                if (GUILayout.Button(new GUIContent(i < 0 ? "orig" : (i + 1).ToString(), i < 0 ? "The sound as it is now" : $"{CleanBase()}_{i + 1:00}: pitch {v.pitch:+0.0;-0.0}, volume {v.gain:+0.0;-0.0}, tone {v.tilt:+0.0;-0.0}"), GUILayout.Width(i < 0 ? 36 : 34), GUILayout.Height(24))) PlayVariations(i, i + 1);
                GUI.backgroundColor = old; if ((i + 2) % per == 0 || i == varCount - 1) GUILayout.EndHorizontal();
            }
            if (selEnd - selStart > 8f) GUILayout.Label("It's a long sound: you hear the first 5 seconds of each.", sHint);
            if (GUILayout.Button(new GUIContent("Save all " + varCount, "Saved as " + CleanBase() + "_01, _02... next to the original"), GUILayout.Height(22))) SaveVariations();
            EditorGUIUtility.labelWidth = lw;
        }
        string CleanBase() { var n = CleanName(saveName); foreach (var e in new[] { "_trim", "_clean", "_loop" }) if (n.EndsWith(e)) n = n.Substring(0, n.Length - e.Length); return n.Length > 0 ? n : BaseName; }

        /// <summary>Which one is being heard (-1 the original, -2 none).</summary>
        int VarNow
        {
            get
            {
                if (varTimes == null || !source || !source.isPlaying) return -2;
                float at = source.time; int k = 0; while (k + 1 < varTimes.Length && at >= varTimes[k + 1]) k++; return varFirst + k;
            }
        }

        /// <summary>The variations from one to another (-1 is the original), put one after another with a pause.</summary>
        float[] VariationsInARow(int from, int to, out float[] starts)
        {
            float end = selEnd - selStart > 8f ? selStart + 5f : selEnd; var parts = new List<float[]>(); starts = new float[to - from]; int gap = Mathf.RoundToInt(rate * 0.35f) * channels, n = 0;
            for (int i = from; i < to; i++) { var d = Render(selStart, end, true, i < 0 ? default : VaryOf(i)); starts[i - from] = n / (float)channels / rate; parts.Add(d); n += d.Length + (i < to - 1 ? gap : 0); }
            var all = new float[n]; int at = 0; foreach (var d in parts) { Array.Copy(d, 0, all, at, d.Length); at += d.Length + gap; }
            return all;
        }

        void PlayVariations(int from, int to)
        {
            Stop(); var data = VariationsInARow(from, to, out var starts); Make(data); cursor = selStart;
            restartAt = -1; varTimes = starts; varFirst = from; lastVar = -3; source.loop = false; source.time = 0; source.Play(); playing = true; previewDirty = true;
        }

        void SaveVariations()
        {
            FinishCleaning(); Stop();
            var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip)); var list = Enumerable.Range(0, varCount).Select(i => ($"{CleanBase()}_{i + 1:00}", Render(selStart, selEnd, true, VaryOf(i)))).ToList();
            WriteAll(dir, list);
        }

        /// <summary>Saves several sounds next to the original; asks first when names are taken.</summary>
        void WriteAll(string dir, List<(string name, float[] data)> list)
        {
            var targets = list.Select(l => Path.Combine(dir, l.name + ".wav").Replace('\\', '/')).ToList(); bool both = false;
            if (targets.Any(File.Exists)) { int pick = EditorUtility.DisplayDialogComplex("Names taken", "Some of these names already have a sound here.", "Replace them", "Cancel", "Keep both"); if (pick == 1) return; both = pick == 2; }
            for (int i = 0; i < list.Count; i++) { if (both && File.Exists(targets[i])) targets[i] = AssetDatabase.GenerateUniqueAssetPath(targets[i]); File.WriteAllBytes(targets[i], WriteWav(list[i].data, channels, rate)); }
            foreach (var t in targets) AssetDatabase.ImportAsset(t, ImportAssetOptions.ForceUpdate);
            var first = AssetDatabase.LoadAssetAtPath<AudioClip>(targets[0]); if (first) EditorGUIUtility.PingObject(first);
            ShowNotification(new GUIContent("Saved " + targets.Count + " sounds"));
        }

        // ---- removing the background
        void CleanGUI()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Find it for me", "Listens to the quietest bit of the sound and takes that as the background"))) LearnQuietest();
            if (GUILayout.Button(new GUIContent("It's the selection", "Select a bit where only the background plays, then press this. Best result."))) { bgFrom = selStart; bgTo = selEnd; cleanOn = true; SelectAll(); }
            GUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(bgTo <= bgFrom && rumble <= 0f && gaps <= 0f))
            {
                GUILayout.BeginHorizontal();
                if (Pill("On", cleanOn, "Switch it off and on to compare")) cleanOn = !cleanOn;
                using (new EditorGUI.DisabledScope(!cleanOn || cleaned == null)) if (Pill("Hear what's taken out", hearRemoved, "If you hear the sound itself in here, lower Strength")) { hearRemoved = !hearRemoved; SourceChanged(); if (source && source.isPlaying) Restart(); }
                GUILayout.FlexibleSpace(); GUILayout.Label(new GUIContent(cleanJob != null || CleanKey != cleanedKey ? "cleaning..." : "", ""), sHint, GUILayout.Width(62)); GUILayout.EndHorizontal();
            }
            float r0 = rumble, g0 = gaps;
            Slide("Strength", "More takes out more background, too much eats into the sound", ref cleanStrength, 0f, 1f); Slide("Turn down by", "dB: 12 is gentle, 24 is a lot, 40 is nearly gone", ref cleanAmount, 3f, 48f);
            Slide("Cut rumble", "Wind, bumps, traffic below this many Hz. 0 = off, 80 is usual", ref rumble, 0f, 300f); Slide("Quiet the gaps", "dB: turns the parts between sounds down even more. 0 = off", ref gaps, 0f, 60f);
            if (!cleanOn && (rumble != r0 || gaps != g0) && (rumble > 0f || gaps > 0f)) cleanOn = true;
        }

        // ================================================================ pieces and saving

        void RightColumn(Rect r)
        {
            EditorGUI.DrawRect(r, Panel); const float saveH = 128; var top = new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - saveH - 8);
            GUILayout.BeginArea(top); GUILayout.BeginHorizontal(); GUILayout.Label(regions.Count > 0 ? "PIECES (" + regions.Count + ")" : "PIECES", sHead); GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("+ Add the selection", "Key A"), EditorStyles.miniButton)) AddRegion();
            GUILayout.EndHorizontal(); rightScroll = GUILayout.BeginScrollView(rightScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            if (regions.Count == 0) GUILayout.Label("Several sounds in one file? Pick a part and add it here, or use Split into sounds.", sHint);
            if (regions.Count > 1)
            {
                EditorGUIUtility.labelWidth = 92; EditorGUI.BeginChangeCheck(); nameAll = EditorGUILayout.TextField(new GUIContent("Name them all", "Type one name: they become name_01, name_02... as you type"), nameAll);
                if (EditorGUI.EndChangeCheck() && CleanName(nameAll).Length > 0) for (int i = 0; i < regions.Count; i++) regions[i].name = $"{CleanName(nameAll)}_{i + 1:00}"; EditorGUIUtility.labelWidth = 0;
            }
            int remove = -1;
            for (int i = 0; i < regions.Count; i++)
            {
                var row = EditorGUILayout.BeginHorizontal(GUILayout.Height(24)); if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(row, i == activeRegion ? new Color(Accent.r, Accent.g, Accent.b, 0.22f) : Ink(0.03f));
                if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(24))) SelectRegion(i, true);
                regions[i].name = EditorGUILayout.TextField(regions[i].name);
                if (GUILayout.Button(new GUIContent(Fmt(regions[i].end - regions[i].start), "Show this piece"), sTileSub, GUILayout.Width(58))) SelectRegion(i, false);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) remove = i; EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) { regions.RemoveAt(remove); if (activeRegion == remove) activeRegion = -1; else if (activeRegion > remove) activeRegion--; }
            GUILayout.EndScrollView();
            if (regions.Count > 0) { GUILayout.BeginHorizontal(); if (Big("Save all " + regions.Count + " pieces", "Each one as its own .wav next to the original", 24)) SaveRegions(); if (GUILayout.Button("Clear", GUILayout.Width(54), GUILayout.Height(24))) { regions.Clear(); activeRegion = -1; } GUILayout.EndHorizontal(); }
            GUILayout.EndArea();

            var s = new Rect(r.x + 10, r.yMax - saveH + 6, r.width - 20, saveH - 12); EditorGUI.DrawRect(new Rect(r.x, r.yMax - saveH, r.width, 1), Ink(0.06f));
            GUILayout.BeginArea(s); GUILayout.Label("SAVE THE SELECTION AS", sHead); saveName = EditorGUILayout.TextField(saveName, new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 22 }, GUILayout.Height(22));
            GUILayout.BeginHorizontal(); foreach (var end in new[] { "trim", "clean", "loop", "01" }) if (GUILayout.Button(new GUIContent("_" + end, "Call it " + BaseName + "_" + end), EditorStyles.miniButton)) { saveName = BaseName + "_" + end; GUIUtility.keyboardControl = 0; } GUILayout.EndHorizontal();
            string will = CleanName(saveName); if (will.Length == 0) will = BaseName + "_trim";
            if (Big("Save  " + will + ".wav", "Next to the original, which isn't changed", 30)) Save(false);
            bool isWav = AssetDatabase.GetAssetPath(clip).EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            using (new EditorGUI.DisabledScope(!isWav)) if (GUILayout.Button(isWav ? "Overwrite the original..." : "Overwrite (only .wav files)", EditorStyles.miniButton) && EditorUtility.DisplayDialog("Overwrite?", $"Replace {Path.GetFileName(AssetDatabase.GetAssetPath(clip))} with the version you hear? This can't be undone.", "Overwrite", "Cancel")) Save(true);
            GUILayout.EndArea();
        }

        // ================================================================ batch

        void BatchPage(Rect r)
        {
            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition)) { var l = Sounds(DragAndDrop.objectReferences); if (l.Count > 0) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); AddBatch(l); } e.Use(); } }
            float lw = Mathf.Max(300, r.width * 0.5f); var left = new Rect(r.x + 6, r.y + 6, lw - 9, r.height - 12); var right = new Rect(left.xMax + 6, left.y, r.width - lw - 9, left.height); EditorGUI.DrawRect(left, Panel); EditorGUI.DrawRect(right, Panel);
            GUILayout.BeginArea(new Rect(left.x + 12, left.y + 10, left.width - 24, left.height - 16)); GUILayout.BeginHorizontal(); GUILayout.Label("SOUNDS (" + batch.Count(c => c) + ")", sHead); GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("+ Add selected", "The sounds or folders selected in the Project window"), EditorStyles.miniButton)) AddBatch(Sounds(Selection.objects));
            if (GUILayout.Button(new GUIContent("Measure", "How loud each one is"), EditorStyles.miniButton)) MeasureBatch();
            if (GUILayout.Button("Clear", EditorStyles.miniButton)) { batch.Clear(); bInfo.Clear(); }
            GUILayout.EndHorizontal(); batchScroll = GUILayout.BeginScrollView(batchScroll);
            if (batch.Count == 0) GUILayout.Label("Drop sounds or folders here.", sEmpty, GUILayout.Height(80)); int remove = -1;
            for (int i = 0; i < batch.Count; i++)
            {
                if (!batch[i]) { remove = i; continue; } GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(batch[i].name, "Open it in the editor"), sTileTitle)) { page = Page.Edit; Load(batch[i]); GUIUtility.ExitGUI(); }
                GUILayout.Label(bInfo.TryGetValue(batch[i], out var inf) ? inf : "", sHint, GUILayout.Width(190)); if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) remove = i; GUILayout.EndHorizontal();
            }
            if (remove >= 0) batch.RemoveAt(remove); GUILayout.EndScrollView(); GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(right.x + 14, right.y + 10, right.width - 28, right.height - 16)); GUILayout.Label("DO THIS TO ALL OF THEM", sHead); EditorGUIUtility.labelWidth = 150;
            if (Tick(ref bTrim, "Cut the silence off both ends")) bQuiet = EditorGUILayout.Slider("   Quiet below (dB)", bQuiet, -70f, -15f);
            if (Tick(ref bClean, "Remove the background (found in each one)")) { bStrength = EditorGUILayout.Slider("   Strength", bStrength, 0f, 1f); bCleanDb = EditorGUILayout.Slider("   Turn it down by (dB)", bCleanDb, 3f, 48f); }
            Tick(ref bTone, "Tone, as set in the editor now" + (HasTone ? "" : " (nothing set)"));
            Tick(ref bReverb, "Reverb, as set in the editor now" + (revAmount > 0.001f ? $" ({Mathf.RoundToInt(revAmount * 100)}%)" : " (nothing set)"));
            if (Tick(ref bMatch, "Match another sound (tone and loudness)")) { var mt = (AudioClip)EditorGUILayout.ObjectField("   Sound like", bMatchTo, typeof(AudioClip), false); if (mt != bMatchTo) { bMatchTo = mt; if (mt) AudioDsp.Learn(mt); } }
            if (Tick(ref bFade, "A tiny fade at both ends (no clicks)")) bFadeMs = EditorGUILayout.Slider("   Milliseconds", bFadeMs, 1f, 100f);
            if (Tick(ref bLoud, "Make them all equally loud")) bTarget = EditorGUILayout.Slider("   Loudness (LUFS)", bTarget, -40f, -8f);
            Tick(ref bMono, "Make them mono");
            GUILayout.Space(8); GUILayout.Label("SAVE", sHead);
            GUILayout.BeginHorizontal(); if (Pill("In a new folder", !bReplace, "The originals aren't changed")) bReplace = false; if (Pill("Replace the originals", bReplace, "Only .wav files")) bReplace = true; GUILayout.EndHorizontal();
            if (!bReplace) { bFolder = EditorGUILayout.TextField(new GUIContent("Folder", "Made next to each sound"), bFolder); bName = EditorGUILayout.TextField(new GUIContent("Name", "Empty keeps their names. A name gives name_01, name_02..."), bName); }
            else GUILayout.Label("The original .wav files are replaced and can't be put back. Other kinds are skipped.", sHint);
            EditorGUIUtility.labelWidth = 0; GUILayout.Space(6);
            using (new EditorGUI.DisabledScope(batch.Count == 0)) if (Big("Do it to " + batch.Count + (batch.Count == 1 ? " sound" : " sounds"), "", 34)) if (!bReplace || EditorUtility.DisplayDialog("Replace the originals?", "Replace the .wav files of these sounds? This can't be undone.", "Replace", "Cancel")) EditorApplication.delayCall += () => RunBatch();
            GUILayout.EndArea();
        }
        bool Tick(ref bool v, string label) { v = EditorGUILayout.ToggleLeft(label, v); return v; }

        static List<AudioClip> Sounds(IEnumerable<UnityEngine.Object> objs)
        {
            var l = new List<AudioClip>();
            foreach (var o in objs) { if (o is AudioClip c) l.Add(c); else { var p = AssetDatabase.GetAssetPath(o); if (AssetDatabase.IsValidFolder(p)) l.AddRange(AssetDatabase.FindAssets("t:AudioClip", new[] { p }).Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)))); } }
            return l.Where(c => c).Distinct().ToList();
        }
        void AddBatch(List<AudioClip> l) { foreach (var c in l) if (!batch.Contains(c)) batch.Add(c); }

        void MeasureBatch()
        {
            try { for (int i = 0; i < batch.Count; i++) { var c = batch[i]; if (!c) continue; if (EditorUtility.DisplayCancelableProgressBar("Audio Trimmer", "Measuring " + c.name, i / (float)batch.Count)) break; bInfo[c] = AudioIO.Read(c, out var s, out int ch, out int rt, out var problem) ? $"{AudioFx.Loudness(s, ch, rt):0.0} LUFS, peak {20 * Mathf.Log10(Mathf.Max(1e-5f, AudioFx.PeakOf(s))):0.0} dB" : "can't be read"; } }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>One sound with the ticked things done to it.</summary>
        float[] Process(float[] s, ref int ch, int rt, out float shortBy)
        {
            shortBy = 0;
            if (bMono && ch > 1) { int n = s.Length / ch; var m = new float[n]; for (int f = 0; f < n; f++) { float v = 0; for (int c = 0; c < ch; c++) v += s[f * ch + c]; m[f] = v / ch; } s = m; ch = 1; }
            if (bClean && AudioCleaner.FindQuietest(s, ch, rt, 0.4f, out int q0, out int q1)) s = AudioCleaner.Clean(s, ch, rt, AudioCleaner.Learn(s, ch, q0, q1), new AudioCleaner.Settings { strength = bStrength, amountDb = bCleanDb }, default);
            if (bTrim) { AudioFx.Sounding(s, ch, rt, bQuiet, 0.01f, out int a, out int b); if (b > a) { var t = new float[(b - a) * ch]; Array.Copy(s, a * ch, t, 0, t.Length); s = t; } }
            s = (float[])s.Clone();
            if (bTone) AudioFx.Tone(s, ch, rt, ToneNow);
            if (bMatch && bMatchTo) { AudioDsp.Learn(bMatchTo); var like = AudioDsp.Known(bMatchTo); if (like != null) s = AudioDsp.Match(s, ch, rt, like, 0.8f, bLoud ? 0f : 1f); }
            if (bFade) { int n = s.Length / ch, f = Mathf.Min(n / 2, Mathf.RoundToInt(bFadeMs * 0.001f * rt)); for (int i = 0; i < f; i++) { float k = i / (float)f; for (int c = 0; c < ch; c++) { s[i * ch + c] *= k; s[(n - 1 - i) * ch + c] *= k; } } }
            if (bReverb) s = AudioFx.Reverb(s, ch, rt, revSize, revDull, revAmount, false);
            if (bLoud) shortBy = AudioFx.ToLoudness(s, ch, rt, bTarget); else AudioFx.Limit(s);
            return s;
        }

        List<string> RunBatch()
        {
            var made = new List<string>(); int skipped = 0, number = 0;
            try
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    var c = batch[i]; if (!c) continue; if (EditorUtility.DisplayCancelableProgressBar("Audio Trimmer", c.name, i / (float)batch.Count)) break;
                    var path = AssetDatabase.GetAssetPath(c); bool wav = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase); number++;
                    if ((bReplace && !wav) || !AudioIO.Read(c, out var s, out int ch, out int rt, out var problem)) { skipped++; bInfo[c] = bReplace && !wav ? "skipped: not a .wav" : "skipped: can't be read"; continue; }
                    var o = Process(s, ref ch, rt, out float shortBy); string target = path;
                    if (!bReplace)
                    {
                        var dir = Path.GetDirectoryName(path).Replace('\\', '/'); var folder = CleanName(bFolder); if (folder.Length > 0) { if (!AssetDatabase.IsValidFolder(dir + "/" + folder)) AssetDatabase.CreateFolder(dir, folder); dir += "/" + folder; }
                        var n = CleanName(bName).Length > 0 ? $"{CleanName(bName)}_{number:00}" : Path.GetFileNameWithoutExtension(path) + (folder.Length == 0 ? "_processed" : ""); target = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + n + ".wav"); // (never over something that's there)
                    }
                    File.WriteAllBytes(target, WriteWav(o, ch, rt)); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate); made.Add(target);
                    bInfo[c] = $"done, {AudioFx.Loudness(o, ch, rt):0.0} LUFS" + (shortBy > 0.2f ? $" ({shortBy:0.0} short)" : "");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            if (made.Count > 0) { var f = AssetDatabase.LoadAssetAtPath<AudioClip>(made[0]); if (f) EditorGUIUtility.PingObject(f); }
            ShowNotification(new GUIContent("Done: " + made.Count + (skipped > 0 ? ", skipped " + skipped : ""))); Repaint(); return made;
        }
    }
}
