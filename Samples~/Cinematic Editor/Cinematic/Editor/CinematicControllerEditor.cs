#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using Shot = CinematicController.Shot;
using Transition = CinematicController.Transition;

// Editor tooling for CinematicController. Wrapped in UNITY_EDITOR so it can live in any folder.
//  - Inspector: a button to open the Cinematic Editor + the few global settings.
//  - Cinematic Editor window (Window > Cinematic Editor): timeline + shot details.

[CustomEditor(typeof(CinematicController))]
public class CinematicControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var c = (CinematicController)target;
        serializedObject.Update();

        var big = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 34 };
        if (GUILayout.Button("Open Cinematic Editor", big)) CinematicWindow.Open(c);

        float total = 0f;
        foreach (var s in c.shots) total += s.duration;
        EditorGUILayout.LabelField($"{c.shots.Count} shots · {total:0.##}s", EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("cam"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playOnStart"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("loop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("displaySortOrder"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("font"));
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("disableOtherCameras"));
        if (c.disableOtherCameras)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("includeTextureCameras"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keepCameras"), true);
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("drawAtEndOfFrame"), new GUIContent("Draw at end of frame",
            "Draw the cinematic's picture after plugins that queue their drawing late (e.g. Gaussian splats). Keep on."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("viewCamera"), new GUIContent("Views rendered by",
            "Which camera renders the views when the cinematic draws its own picture. Real camera: your own camera, so effects that " +
            "only work on it (e.g. a Gaussian splat pass in builds) show too."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("keepExposureCamera"),
            new GUIContent("Keep exposure camera", "While the cinematic draws its own picture, the real camera keeps rendering the " +
                           "scene tiny and hidden so HDRP's auto exposure stays right. Turn off if your exposure is Fixed, to save that render."));
        EditorGUILayout.HelpBox("Call StartCinematic() from anywhere to play it, EndCinematic() to stop it early. " +
                                "It ends by itself after the last shot and puts the other cameras back as they were.",
                                MessageType.None);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onStartCinematic"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("onEndCinematic"));

        if (serializedObject.ApplyModifiedProperties()) CinematicPlayModeEdits.Changed(c);
    }

    void OnSceneGUI()
    {
        if (EditorWindow.HasOpenInstances<CinematicWindow>()) return; // the window draws them
        var c = (CinematicController)target;
        int clicked = CinematicWindow.DrawScene(c, -1, false, out _);
        if (clicked >= 0) CinematicWindow.Open(c, clicked);
    }
}

// Unity throws away Play Mode changes when you stop playing. This keeps the ones made with the Cinematic Editor
// (shot settings, captured or moved camera points, new points, images, events) and re-applies them in Edit Mode.
[InitializeOnLoad]
public static class CinematicPlayModeEdits
{
    const string Key = "CinematicEditor.PlayModeEdits";
    const string PointsParentName = "Cinematic Points";

    [Serializable] class SavedPose { public long id; public Vector3 pos; public Quaternion rot; public Vector3 scale; }
    [Serializable] class SavedPoint { public long oldId; public string name; public Vector3 pos; public Quaternion rot; }
    [Serializable] class SavedController { public long id; public string json; }
    [Serializable]
    class Payload
    {
        public List<SavedController> controllers = new List<SavedController>();
        public List<SavedPose> poses = new List<SavedPose>();
        public List<SavedPoint> created = new List<SavedPoint>();
        public List<long> destroyed = new List<long>();
    }

    static readonly HashSet<CinematicController> controllers = new HashSet<CinematicController>();
    static readonly HashSet<Transform> moved = new HashSet<Transform>();
    static readonly HashSet<GameObject> created = new HashSet<GameObject>();
    static readonly HashSet<long> destroyed = new HashSet<long>();

    // An object's id that survives leaving Play Mode (Unity 6.6 replaced instance ids with entity ids)
#if UNITY_6000_6_OR_NEWER
    static long Id(UnityEngine.Object o) => (long)EntityId.ToULong(o.GetEntityId());
    static UnityEngine.Object FromId(long id) => EditorUtility.EntityIdToObject(EntityId.FromULong((ulong)id));
#else
    static long Id(UnityEngine.Object o) => o.GetInstanceID();
    static UnityEngine.Object FromId(long id) => EditorUtility.InstanceIDToObject((int)id);
#endif

    static CinematicPlayModeEdits() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    public static void Changed(CinematicController c) { if (Application.isPlaying && c != null) controllers.Add(c); }
    public static void Moved(Transform t) { if (Application.isPlaying && t != null) moved.Add(t); }
    public static void Created(GameObject go) { if (Application.isPlaying && go != null) created.Add(go); }

    public static void Destroying(GameObject go)
    {
        if (!Application.isPlaying || go == null) return;
        if (!created.Remove(go)) destroyed.Add(Id(go)); // existed before Play: remove it afterwards too
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) Forget();
        else if (state == PlayModeStateChange.ExitingPlayMode) Save();
        else if (state == PlayModeStateChange.EnteredEditMode) Restore();
    }

    static void Forget()
    {
        controllers.Clear();
        moved.Clear();
        created.Clear();
        destroyed.Clear();
    }

    // Still in Play Mode: write down everything that was changed
    static void Save()
    {
        if (controllers.Count + moved.Count + created.Count + destroyed.Count == 0) return;
        var p = new Payload();
        foreach (GameObject go in created)
            if (go != null)
                p.created.Add(new SavedPoint
                {
                    oldId = Id(go.transform), name = go.name,
                    pos = go.transform.position, rot = go.transform.rotation
                });
        foreach (Transform t in moved)
            if (t != null && !created.Contains(t.gameObject))
                p.poses.Add(new SavedPose { id = Id(t), pos = t.localPosition, rot = t.localRotation, scale = t.localScale });
        foreach (CinematicController c in controllers)
            if (c != null)
                p.controllers.Add(new SavedController { id = Id(c), json = EditorJsonUtility.ToJson(c) });
        p.destroyed.AddRange(destroyed);
        SessionState.SetString(Key, JsonUtility.ToJson(p));
        Forget();
    }

    // Back in Edit Mode: apply it to the scene (one undo step)
    static void Restore()
    {
        string data = SessionState.GetString(Key, "");
        SessionState.EraseString(Key);
        if (string.IsNullOrEmpty(data)) return;
        var p = JsonUtility.FromJson<Payload>(data);
        const string undo = "Keep Play Mode Cinematic Edits";

        // Camera points made during Play Mode are gone now: make them again
        var newIds = new Dictionary<long, long>();
        Transform parent = null;
        foreach (SavedPoint sp in p.created)
        {
            if (parent == null)
            {
                GameObject root = GameObject.Find(PointsParentName);
                if (root == null)
                {
                    root = new GameObject(PointsParentName);
                    Undo.RegisterCreatedObjectUndo(root, undo);
                }
                parent = root.transform;
            }
            var go = new GameObject(sp.name);
            Undo.RegisterCreatedObjectUndo(go, undo);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(sp.pos, sp.rot);
            newIds[sp.oldId] = Id(go.transform);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // Points that were moved or captured
        foreach (SavedPose pose in p.poses)
        {
            var t = FromId(pose.id) as Transform;
            if (t == null) continue;
            Undo.RecordObject(t, undo);
            t.localPosition = pose.pos;
            t.localRotation = pose.rot;
            t.localScale = pose.scale;
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }

        // Shot settings, pointing at the re-made points where needed
        foreach (SavedController sc in p.controllers)
        {
            var c = FromId(sc.id) as CinematicController;
            if (c == null) continue;
            string json = Regex.Replace(sc.json, "\"(instanceID|entityId)\":(-?\\d+)", m =>
                newIds.TryGetValue(long.Parse(m.Groups[2].Value), out long id) ? "\"" + m.Groups[1].Value + "\":" + id : m.Value);

            bool preview = c.preview;
            int previewShot = c.previewShot;
            float previewScrub = c.previewScrub;
            Undo.RecordObject(c, undo);
            EditorJsonUtility.FromJsonOverwrite(json, c);
            c.preview = preview; // keep the Edit Mode preview as it was
            c.previewShot = previewShot;
            c.previewScrub = previewScrub;
            EditorUtility.SetDirty(c);
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        // Points deleted during Play Mode (only the editor's own, under "Cinematic Points")
        foreach (long id in p.destroyed)
        {
            var go = FromId(id) as GameObject;
            if (go != null && go.transform.parent != null && go.transform.parent.name == PointsParentName)
                Undo.DestroyObjectImmediate(go);
        }

        Debug.Log("[Cinematic] Kept the changes you made in the Cinematic Editor during Play Mode (Ctrl+Z to undo them).");
    }
}

public class CinematicWindow : EditorWindow
{
    const float Gutter = 70f, RulerH = 22f, TrackH = 52f, ScrollH = 14f, ToolbarH = 46f;

    // Lane height: drag the line under the timeline to make it taller or shorter
    static float LaneH
    {
        get => EditorPrefs.GetFloat("CinematicEditor.LaneHeight", 18f);
        set => EditorPrefs.SetFloat("CinematicEditor.LaneHeight", Mathf.Clamp(value, 14f, 40f));
    }

    // Overlays and sounds that overlap in time go on separate rows (like a video editor's tracks)
    int[] imageRow = new int[0], soundRow = new int[0];
    int imageRows = 1, soundRows = 1, motionRows = 1, popupRows = 1;
    readonly Dictionary<Shot, int[]> motionRowOf = new Dictionary<Shot, int[]>();
    readonly Dictionary<Shot, int[]> popupRowOf = new Dictionary<Shot, int[]>();

    int SubRow(Shot s, int lane, int sub)
    {
        int[] rows = null;
        if (lane == MotionLane) motionRowOf.TryGetValue(s, out rows);
        else if (lane == PopupLane) popupRowOf.TryGetValue(s, out rows);
        return rows != null && sub >= 0 && sub < rows.Length ? rows[sub] : 0;
    }

    void PackRows()
    {
        int ni = c.images != null ? c.images.Count : 0, ns = c.sounds != null ? c.sounds.Count : 0;
        var iStart = new float[ni]; var iEnd = new float[ni];
        for (int j = 0; j < ni; j++)
        {
            var im = c.images[j];
            iStart[j] = im != null ? im.start : float.MaxValue;
            iEnd[j] = im != null ? im.start + im.duration : float.MaxValue;
        }
        imageRow = Pack(iStart, iEnd, out imageRows);

        var sStart = new float[ns]; var sEnd = new float[ns];
        for (int j = 0; j < ns; j++)
        {
            var snd = c.sounds[j];
            sStart[j] = snd != null ? snd.start : float.MaxValue;
            sEnd[j] = snd != null ? snd.start + CinematicController.LengthOf(snd) : float.MaxValue;
        }
        soundRow = Pack(sStart, sEnd, out soundRows);

        motionRows = popupRows = 1;
        motionRowOf.Clear();
        popupRowOf.Clear();
        foreach (Shot sh in c.shots)
        {
            int nm = sh.motions != null ? sh.motions.Count : 0;
            var ms = new float[nm]; var me = new float[nm];
            for (int j = 0; j < nm; j++)
            {
                var mo = sh.motions[j];
                ms[j] = mo != null ? mo.start : float.MaxValue;
                me[j] = mo != null ? CinematicController.MotionEnd(mo, sh.duration) : float.MaxValue;
            }
            motionRowOf[sh] = Pack(ms, me, out int mr);
            motionRows = Mathf.Max(motionRows, mr);

            int np = sh.popups != null ? sh.popups.Count : 0;
            var ps = new float[np]; var pe = new float[np];
            for (int j = 0; j < np; j++)
            {
                var pp = sh.popups[j];
                ps[j] = pp != null ? pp.appearAt : float.MaxValue;
                pe[j] = pp != null ? (pp.duration > 0f ? pp.appearAt + pp.duration : sh.duration) : float.MaxValue;
            }
            popupRowOf[sh] = Pack(ps, pe, out int pr);
            popupRows = Mathf.Max(popupRows, pr);
        }
    }

    // Puts each item on the first row where it doesn't overlap anything
    static int[] Pack(float[] start, float[] end, out int rowCount)
    {
        int n = start.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        var keys = (float[])start.Clone();
        Array.Sort(keys, order);
        var rowEnds = new List<float>();
        var rows = new int[n];
        foreach (int i in order)
        {
            int r = rowEnds.FindIndex(e => e <= start[i] + 0.0001f);
            if (r < 0) { rowEnds.Add(end[i]); r = rowEnds.Count - 1; }
            else rowEnds[r] = end[i];
            rows[i] = r;
        }
        rowCount = Mathf.Max(1, rowEnds.Count);
        return rows;
    }

    float LaneHeightOf(int lane) => LaneH * (lane == ImageLane ? imageRows : lane == AudioLane ? soundRows
                                           : lane == MotionLane ? motionRows : lane == PopupLane ? popupRows : 1);

    // Top of a lane, measured from the first lane
    float LaneTop(int lane)
    {
        float y = 0f;
        for (int l = 0; l < lane && l < LaneNames.Length; l++) y += LaneHeightOf(l);
        return y;
    }

    int LaneAt(float dy)
    {
        if (dy < 0f) return -1;
        for (int l = 0; l < LaneNames.Length; l++)
            if (dy < LaneTop(l + 1)) return l;
        return -1;
    }
    const string PointsParentName = "Cinematic Points";
    static readonly string[] LaneNames = { "FOV", "Look", "Motion", "Pop-ups", "Overlays", "Scan", "Audio", "Events" };
    static readonly string[] LaneIcons = { "Camera Icon", "SceneViewFx", "MoveTool", "UnityEditor.GameView", "Image Icon", "ViewToolZoom", "AudioSource Icon", "Animation.EventMarker" };
    const int LookLane = 1, MotionLane = 2, PopupLane = 3, ImageLane = 4, ScanLane = 5, AudioLane = 6, EventLane = 7;
    static readonly Color ImageCol = new Color(0.55f, 0.85f, 1f);
    static readonly string[] LookPresets = { "CCTV", "Night", "Robot", "Thermal", "VHS", "Glitch", "Clean frame" };
    static readonly string[] Presets = { "Pan left", "Pan right", "Tilt up", "Tilt down", "Roll",
                                         "Push in", "Pull out", "Truck left", "Truck right", "Rise",
                                         "Lower", "Orbit", "Dolly zoom", "Handheld", "Impact shake" };

    static readonly Color Accent = new Color(1f, 0.62f, 0.2f);
    static readonly Color[] ShotCols =
    {
        new Color(0.27f, 0.45f, 0.75f), new Color(0.45f, 0.35f, 0.72f), new Color(0.22f, 0.56f, 0.53f),
        new Color(0.62f, 0.36f, 0.48f), new Color(0.40f, 0.54f, 0.28f)
    };
    static readonly Color[] LaneCols =
    {
        new Color(0.35f, 0.8f, 0.9f), new Color(0.45f, 1f, 0.6f), new Color(0.75f, 0.58f, 1f), new Color(1f, 0.85f, 0.35f),
        new Color(0.55f, 0.85f, 1f), new Color(0.45f, 1f, 0.85f), new Color(0.6f, 0.75f, 1f), new Color(1f, 0.85f, 0.3f)
    };

    static Color Bg => EditorGUIUtility.isProSkin ? new Color(0.12f, 0.125f, 0.15f) : new Color(0.72f, 0.73f, 0.76f);
    static Color Panel => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.18f, 0.215f) : new Color(0.83f, 0.84f, 0.87f);
    static Color Ink(float a) => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, a) : new Color(0f, 0f, 0f, a);

    // Shared by every Cinematic window, so popped-out viewer / timeline / settings windows stay in sync
    static CinematicController c;
    static int selected = -1;
    static float time;
    static readonly List<CinematicWindow> openWindows = new List<CinematicWindow>();

    public static void RepaintAll()
    {
        foreach (CinematicWindow w in openWindows)
            if (w != null) { w.viewerDirty = true; w.Repaint(); }
    }

    // Which parts this window shows, and how they're arranged
    [SerializeField] bool showTimeline = true, showDetails = true;
    [SerializeField] int layout = 2;        // 0 stacked, 1 side by side, 2 Unity-like (settings right), 3 settings left
    [SerializeField] bool fullscreenView;   // this window is the full-screen viewer
    double lastMouseMove;                   // full screen: controls show while the mouse moves
    Rect layoutButtonRect;
    [SerializeField] float viewerHeight;    // stacked: viewer height (0 = automatic)
    [SerializeField] float detailsWidth;    // side by side: settings width (0 = automatic)
    [SerializeField] float pxPerSec = 60f;
    float scrollX;
    Vector2 detailScroll;
    int pendingLookAt = -1;
    bool showEvents;
    [SerializeField] bool showViewer = true;
    [SerializeField] int viewQuality;      // viewer resolution: 0 full (default), 1 half, 2 quarter
    [SerializeField] bool showGizmos = true; // boxes, labels and the info bar over the viewer
    static Tab tab = Tab.Camera;

    enum Tab { Camera, Motion, Multiview, Popups, Scan, Overlays, Audio, Events }

    // Preview the cinematic in the Game view too (moves the real camera). Off = only this window, which is faster.
    static bool GameSync
    {
        get => EditorPrefs.GetBool("CinematicEditor.GameSync", true);
        set => EditorPrefs.SetBool("CinematicEditor.GameSync", value);
    }

    float snapLine = -1f; // time of the edge the current drag snapped to (drawn as a line)
    RenderTexture viewerRT;
    float viewZoom = 1f;   // viewer zoom (1 = whole frame fits)
    Vector2 viewPan;       // viewer pan, in pixels
    bool viewPanning;
    bool viewerDirty = true;

    bool playing;
    double lastTick;
    bool follow = true;          // Play Mode: playhead follows the running cinematic
    double lastLiveRepaint;

    enum Drag { None, Scrub, Select, Add, ShotEnd, Transition, FxBody, FxStart, FxEnd, Reorder, Event }

    // Dragging a shot block to a new place
    float reorderFromX;
    bool reorderActive;
    int reorderTarget = -1;

    // Built-in editor icons (missing ones just show no icon)
    static readonly Dictionary<string, Texture> icons = new Dictionary<string, Texture>();

    static Texture Icon(string name)
    {
        if (icons.TryGetValue(name, out Texture tex)) return tex;
        tex = EditorGUIUtility.isProSkin ? EditorGUIUtility.FindTexture("d_" + name) : null;
        if (tex == null) tex = EditorGUIUtility.FindTexture(name);
        icons[name] = tex;
        return tex;
    }

    static string OverlayIcon(CinematicController.OverlayKind kind)
    {
        switch (kind)
        {
            case CinematicController.OverlayKind.Text: return "Text Icon";
            case CinematicController.OverlayKind.Waveform: return "AudioClip Icon";
            case CinematicController.OverlayKind.ProgressBar: return "Slider Icon";
            case CinematicController.OverlayKind.UIObject: return "Canvas Icon";
            default: return "Image Icon";
        }
    }

    static GUIContent IconLabel(string text, string icon, string tooltip = null) =>
        new GUIContent(string.IsNullOrEmpty(text) ? "" : " " + text, Icon(icon), tooltip);

    // Icon with a label; if the icon doesn't exist in this Unity version, a text-only fallback instead
    static GUIContent IconOr(string text, string fallback, string icon, string tooltip = null) =>
        Icon(icon) != null ? new GUIContent(string.IsNullOrEmpty(text) ? "" : " " + text, Icon(icon), tooltip)
                           : new GUIContent(fallback, tooltip);
    Drag drag;
    int dragShot, dragLane, dragSub;
    readonly HashSet<string> openFeeds = new HashSet<string>();

    // Dragging pop-up windows and images in the viewer
    int boxDragShot = -1, boxDragIndex = -1;
    int boxDragKind; // 0 overlay, 1 pop-up, 2 scan target
    bool boxResize, boxKeepShape;
    int boxCorner;   // which corner is being dragged: 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
    Rect boxStart;   // the box when the resize started
    Vector2 boxGrab;
    const float HandleGrab = 9f; // how far from a corner still grabs it (pixels)
    float dragGrab;

    GUIStyle cardStyle, cardTitle, blockTitle, blockSub, timeStyle, titleStyle, laneLabel, barLabel,
             plusStyle, hintStyle, nameStyle, numberStyle, emptyStyle, overlayStyle, overlayCenter;
    Texture2D panelTex;

    // ---------------------------------------------------------------- Open / lifecycle

    [MenuItem("Window/Cinematic Editor")]
    static void OpenFromMenu() => Open(null);

    public static void Open(CinematicController target, int shot = -1)
    {
        var w = GetWindow<CinematicWindow>();
        w.titleContent = new GUIContent("Cinematic", Icon("Animation Icon"));
        w.minSize = new Vector2(420f, 300f);
        if (target != null && target != c) w.SetTarget(target);
        if (shot >= 0 && c != null && shot < c.shots.Count) selected = shot;
        w.Repaint();
    }

    void SetTarget(CinematicController t)
    {
        if (c != null && c != t && c.preview)
        {
            c.preview = false;
            Refresh();
        }
        c = t;
        if (c != null && c.MigrateImages()) EditorUtility.SetDirty(c);
        selected = c != null && c.shots.Count > 0 ? 0 : -1;
        time = 0f;
        scrollX = 0f;
    }

    // ---------------------------------------------------------------- Remembering the window's settings

    // Kept in Unity's editor preferences, so a window opened later (even after restarting Unity) looks the same.
    // The main window and each popped-out part (viewer, timeline, settings) remember their own.
    [SerializeField] string prefsRole = "Main";
    [SerializeField] bool prefsLoaded;
    string PrefsKey => "CinematicEditor.Window." + (string.IsNullOrEmpty(prefsRole) ? "Main" : prefsRole) + ".";

    void LoadPrefs()
    {
        string k = PrefsKey;
        if (EditorPrefs.HasKey(k + "layout"))
        {
            layout = EditorPrefs.GetInt(k + "layout", layout);
            showViewer = EditorPrefs.GetBool(k + "viewer", showViewer);
            showTimeline = EditorPrefs.GetBool(k + "timeline", showTimeline);
            showDetails = EditorPrefs.GetBool(k + "details", showDetails);
            viewerHeight = EditorPrefs.GetFloat(k + "viewerHeight", viewerHeight);
            detailsWidth = EditorPrefs.GetFloat(k + "detailsWidth", detailsWidth);
            pxPerSec = EditorPrefs.GetFloat(k + "zoom", pxPerSec);
            viewQuality = EditorPrefs.GetInt(k + "quality", viewQuality);
            showGizmos = EditorPrefs.GetBool(k + "gizmos", showGizmos);
            if (!showViewer && !showTimeline && !showDetails) showViewer = showTimeline = showDetails = true;
        }
        // Shared by every window
        tab = (Tab)Mathf.Clamp(EditorPrefs.GetInt("CinematicEditor.Tab", (int)tab), 0, (int)Tab.Events);
        string feeds = EditorPrefs.GetString("CinematicEditor.OpenTweaks", "");
        openFeeds.Clear();
        foreach (string f in feeds.Split('|')) if (!string.IsNullOrEmpty(f)) openFeeds.Add(f);
    }

    void SavePrefs()
    {
        if (fullscreenView || this == null) return; // the full-screen viewer is temporary
        string k = PrefsKey;
        EditorPrefs.SetInt(k + "layout", layout);
        EditorPrefs.SetBool(k + "viewer", showViewer);
        EditorPrefs.SetBool(k + "timeline", showTimeline);
        EditorPrefs.SetBool(k + "details", showDetails);
        EditorPrefs.SetFloat(k + "viewerHeight", viewerHeight);
        EditorPrefs.SetFloat(k + "detailsWidth", detailsWidth);
        EditorPrefs.SetFloat(k + "zoom", pxPerSec);
        EditorPrefs.SetInt(k + "quality", viewQuality);
        EditorPrefs.SetBool(k + "gizmos", showGizmos);
        EditorPrefs.SetInt("CinematicEditor.Tab", (int)tab);
        EditorPrefs.SetString("CinematicEditor.OpenTweaks", string.Join("|", openFeeds));
    }

    void OnLostFocus() => SavePrefs();

    void OnEnable()
    {
        if (!prefsLoaded) { LoadPrefs(); prefsLoaded = true; } // a newly opened window: bring back its last settings
        wantsMouseMove = true; // corner handles light up under the mouse
        if (!openWindows.Contains(this)) openWindows.Add(this);
        SceneView.duringSceneGui += OnScene;
        Undo.undoRedoPerformed += OnUndo;
        Undo.postprocessModifications += OnSceneEdited;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    void OnDisable()
    {
        SavePrefs();
        openWindows.Remove(this);
        SceneView.duringSceneGui -= OnScene;
        Undo.undoRedoPerformed -= OnUndo;
        Undo.postprocessModifications -= OnSceneEdited;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        StopPlaying();
        ReleaseViewer();
    }

    // The viewer only re-renders when something changed (much cheaper than redrawing all the time)
    UndoPropertyModification[] OnSceneEdited(UndoPropertyModification[] mods)
    {
        viewerDirty = true;
        Repaint();
        return mods;
    }

    void OnHierarchyChanged()
    {
        viewerDirty = true;
        Repaint();
    }

    void Update()
    {
        if (c == null) return;
        double now = EditorApplication.timeSinceStartup;

        // Play Mode: follow the running cinematic
        if (Application.isPlaying && follow && c.IsPlaying)
        {
            if (now - lastLiveRepaint < 1.0 / 30.0) return;
            lastLiveRepaint = now;
            time = c.CurrentTime;
            selected = ShotAt(time, out _);
            viewerDirty = true;
            Repaint();
            return;
        }

        // Paused on something animated (feed noise, waveform): keep it moving smoothly
        if (!playing && showViewer && c.PreviewAnimated && now - lastLiveRepaint >= 1.0 / 24.0)
        {
            lastLiveRepaint = now;
            viewerDirty = true;
            Repaint();
        }
    }

    void OnDestroy()
    {
        // Closing the last Cinematic window puts the camera back where it was.
        if (openWindows.Count == 0 && c != null && c.preview && !Application.isPlaying)
        {
            c.preview = false;
            Refresh();
        }
    }

    void OnUndo()
    {
        if (c != null) SetTime(time);
        Repaint();
    }

    void OnSelectionChange()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null) return;

        var ctrl = go.GetComponent<CinematicController>();
        if (ctrl != null && ctrl != c)
        {
            SetTarget(ctrl);
            Repaint();
            return;
        }

        if (c != null)
        {
            int i = c.shots.FindIndex(s => s.point == go.transform ||
                                           (s.extraViews != null && s.extraViews.Contains(go.transform)) ||
                                           (s.popups != null && s.popups.Exists(pp => pp.point == go.transform)));
            if (i >= 0 && i != selected)
            {
                selected = i;
                Repaint();
            }
        }
    }

    // ---------------------------------------------------------------- Main GUI

    void OnGUI()
    {
        EnsureStyles();
        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), Bg);

        if (c == null)
        {
            var found = FindObjectsByType<CinematicController>(FindObjectsSortMode.None);
            if (found.Length > 0) SetTarget(found[0]);
        }
        if (c == null)
        {
            DrawEmpty();
            return;
        }

        if (selected >= c.shots.Count) selected = c.shots.Count - 1;
        if (selected < 0 && c.shots.Count > 0) selected = 0;
        // One rule: the shot being edited is the one under the playhead. If a button or menu picked another
        // shot (Earlier / Later, duplicate, insert...), the playhead jumps to it.
        if (selected >= 0 && !(Application.isPlaying && follow && c.IsPlaying) && selected != ShotAt(time, out _))
            SetTime(StartOf(selected));

        if (fullscreenView)
        {
            DrawFullScreen();
            return;
        }
        if (Event.current.type == EventType.MouseUp) EditorApplication.delayCall += SavePrefs; // after drags and clicks
        if (c != null && c.shots.Count > 0) HandleCommands();

        HandleKeys();

        float W = position.width, H = position.height;
        PackRows();
        float timelineH = showTimeline ? RulerH + TrackH + 4f + LaneTop(LaneNames.Length) + 6f + ScrollH : 0f;
        DrawToolbar(new Rect(0f, 0f, W, ToolbarH));
        float top = ToolbarH, body = Mathf.Max(0f, H - top - timelineH);
        const float Split = 5f;

        if ((layout == 2 || layout == 3) && showDetails && (showViewer || showTimeline))
        {
            // Unity-like: viewer above the timeline in one column, settings the full height beside them
            bool right = layout == 2;
            float dw = detailsWidth > 0f ? detailsWidth : W * 0.36f;
            dw = Mathf.Clamp(dw, 320f, Mathf.Max(320f, W - 300f));
            float colW = W - dw - Split, colX = right ? 0f : dw + Split, colH = H - top;
            if (showViewer)
            {
                float tlH = showTimeline ? Mathf.Min(timelineH, colH - 100f - Split) : 0f;
                float vh = showTimeline ? colH - tlH - Split : colH;
                DrawViewer(new Rect(colX, top, colW, vh));
                if (showTimeline)
                {
                    float lanes = LaneNames.Length + (imageRows - 1) + (soundRows - 1) + (motionRows - 1) + (popupRows - 1);
                    float nh = Splitter(new Rect(colX, top + vh, colW, Split), false, LaneH * lanes, 14f * lanes, 40f * lanes, -1f);
                    LaneH = nh / lanes;
                    DrawTimeline(new Rect(colX, top + vh + Split, colW, tlH));
                }
            }
            else DrawTimeline(new Rect(colX, top, colW, Mathf.Min(timelineH, colH)));
            detailsWidth = Splitter(new Rect(right ? W - dw - Split : dw, top, Split, colH), true, dw, 320f, W - 300f, right ? -1f : 1f);
            DrawDetails(new Rect(right ? W - dw : 0f, top, dw, colH));
        }
        else if (layout == 1 && showViewer && showDetails)
        {
            // Side by side: viewer | settings, timeline along the bottom
            float dw = detailsWidth > 0f ? detailsWidth : W * 0.42f;
            dw = Mathf.Clamp(dw, 260f, Mathf.Max(260f, W - 220f));
            DrawViewer(new Rect(0f, top, W - dw - Split, body));
            detailsWidth = Splitter(new Rect(W - dw - Split, top, Split, body), true, dw, 260f, W - 220f, -1f);
            DrawDetails(new Rect(W - dw, top, dw, body - Split));
            if (showTimeline)
            {
                // Drag the line above the timeline to make its lanes taller or shorter
                float lanes = LaneNames.Length + (imageRows - 1) + (soundRows - 1) + (motionRows - 1) + (popupRows - 1);
                float nh = Splitter(new Rect(0f, top + body - Split, W, Split), false, LaneH * lanes, 14f * lanes, 40f * lanes, -1f);
                LaneH = nh / lanes;
                DrawTimeline(new Rect(0f, top + body, W, timelineH));
            }
        }
        else
        {
            // Stacked: viewer, timeline, settings
            float y = top;
            if (showViewer)
            {
                float vh = !showDetails ? body
                         : Mathf.Clamp(viewerHeight > 0f ? viewerHeight : body * 0.5f, 100f, Mathf.Max(100f, body - 100f));
                DrawViewer(new Rect(0f, y, W, vh - (showDetails ? Split : 0f)));
                if (showDetails) viewerHeight = Splitter(new Rect(0f, y + vh - Split, W, Split), false, vh, 100f, body - 100f, 1f);
                y += vh;
            }
            if (showTimeline)
            {
                DrawTimeline(new Rect(0f, y, W, timelineH));
                y += timelineH;
                if (showDetails)
                {
                    // Drag to make the timeline lanes taller or shorter
                    float lanes = LaneNames.Length + (imageRows - 1) + (soundRows - 1) + (motionRows - 1) + (popupRows - 1);
                    float nh = Splitter(new Rect(0f, y, W, Split), false, LaneH * lanes, 14f * lanes, 40f * lanes, 1f);
                    LaneH = nh / lanes;
                    y += Split;
                }
            }
            if (showDetails) DrawDetails(new Rect(0f, y, W, H - y));
        }
    }

    // A draggable divider between two parts of the window
    float Splitter(Rect r, bool vertical, float value, float min, float max, float sign)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        EditorGUIUtility.AddCursorRect(r, vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
        switch (e.GetTypeForControl(id))
        {
            case EventType.Repaint:
                EditorGUI.DrawRect(r, GUIUtility.hotControl == id ? new Color(Accent.r, Accent.g, Accent.b, 0.6f) : Ink(0.07f));
                break;
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;
                value = Mathf.Clamp(value + (vertical ? e.delta.x : e.delta.y) * sign, min, Mathf.Max(min, max));
                viewerDirty = true;
                e.Use();
                Repaint();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }
        return value;
    }

    void ShowFileMenu()
    {
        var menu = new GenericMenu();
        bool linked = c.savedAs != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(c.savedAs));
        if (linked)
        {
            menu.AddItem(new GUIContent($"Save  (to {c.savedAs.name})"), false, () => CinematicSaving.Save(c));
            menu.AddItem(new GUIContent("Save as a new file…"), false, () => CinematicSaving.SaveAs(c));
        }
        else menu.AddItem(new GUIContent("Save to a file…"), false, () => CinematicSaving.SaveAs(c));
        menu.AddItem(new GUIContent("Load from a file…"), false, () => CinematicSaving.LoadWithPicker(c));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Fill empty camera points with random ones"), false, () =>
        {
            int n = CinematicSaving.FillEmptyPoints(c);
            ShowNotification(new GUIContent(n > 0 ? $"Made {n} random camera point(s)" : "Every camera point is already set"), 1.5);
        });
        if (linked)
        {
            menu.AddItem(new GUIContent($"Reload {c.savedAs.name}"), false, () => CinematicSaving.Load(c, c.savedAs));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Show the file in the Project window"), false, () =>
            {
                EditorGUIUtility.PingObject(c.savedAs);
                Selection.activeObject = c.savedAs;
            });
        }
        menu.AddSeparator("");
        menu.AddDisabledItem(new GUIContent("Everything is saved; camera points are kept as positions"));
        menu.ShowAsContext();
    }

    // Layout menu: what this window shows, how it's arranged, and popping parts out into their own dockable windows
    void ShowLayoutMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Show/Viewer"), showViewer, () => { showViewer = !showViewer; viewerDirty = true; Repaint(); });
        menu.AddItem(new GUIContent("Show/Timeline"), showTimeline, () => { showTimeline = !showTimeline; Repaint(); });
        menu.AddItem(new GUIContent("Show/Shot settings"), showDetails, () => { showDetails = !showDetails; Repaint(); });
        foreach (var p in LayoutPresets)
        {
            var preset = p;
            menu.AddItem(new GUIContent("Arrange/" + p.name), IsPreset(p), () => ApplyPreset(preset));
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Move to its own window/Viewer"), false, () => PopOut(true, false, false, "Cinematic Viewer"));
        menu.AddItem(new GUIContent("Move to its own window/Timeline"), false, () => PopOut(false, true, false, "Cinematic Timeline"));
        menu.AddItem(new GUIContent("Move to its own window/Shot settings"), false, () => PopOut(false, false, true, "Cinematic Shot"));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Reset this window's layout"), false, () =>
        {
            showViewer = showTimeline = showDetails = true;
            layout = 0;
            viewerHeight = detailsWidth = 0f;
            viewerDirty = true;
            Repaint();
        });
        menu.ShowAsContext();
    }

    // ---------------------------------------------------------------- Layout presets

    class LayoutPreset
    {
        public string name, tip;
        public int layout;
        public bool viewer, timeline, details;
        public Rect v, t, d; // where each part goes in the little picture (0-1, from the top-left)
    }

    static readonly LayoutPreset[] LayoutPresets =
    {
        new LayoutPreset { name = "Unity-like", tip = "Viewer above the timeline on the left, all settings on the right (like Unity's Inspector)",
            layout = 2, viewer = true, timeline = true, details = true,
            v = new Rect(0f, 0f, 0.64f, 0.55f), t = new Rect(0f, 0.58f, 0.64f, 0.42f), d = new Rect(0.67f, 0f, 0.33f, 1f) },
        new LayoutPreset { name = "Side by side", tip = "Viewer and settings side by side, a wide timeline along the bottom",
            layout = 1, viewer = true, timeline = true, details = true,
            v = new Rect(0f, 0f, 0.6f, 0.6f), d = new Rect(0.63f, 0f, 0.37f, 0.6f), t = new Rect(0f, 0.63f, 1f, 0.37f) },
        new LayoutPreset { name = "Stacked", tip = "Viewer, timeline and settings one under the other",
            layout = 0, viewer = true, timeline = true, details = true,
            v = new Rect(0f, 0f, 1f, 0.45f), t = new Rect(0f, 0.48f, 1f, 0.24f), d = new Rect(0f, 0.75f, 1f, 0.25f) },
        new LayoutPreset { name = "Settings on the left", tip = "Like Unity-like, mirrored",
            layout = 3, viewer = true, timeline = true, details = true,
            d = new Rect(0f, 0f, 0.33f, 1f), v = new Rect(0.36f, 0f, 0.64f, 0.55f), t = new Rect(0.36f, 0.58f, 0.64f, 0.42f) },
        new LayoutPreset { name = "Big preview", tip = "Just the viewer and timeline, as big as possible (settings hidden)",
            layout = 0, viewer = true, timeline = true, details = false,
            v = new Rect(0f, 0f, 1f, 0.66f), t = new Rect(0f, 0.69f, 1f, 0.31f) },
        new LayoutPreset { name = "Editing only", tip = "Timeline and settings, no viewer (use it with a popped-out viewer)",
            layout = 0, viewer = false, timeline = true, details = true,
            t = new Rect(0f, 0f, 1f, 0.4f), d = new Rect(0f, 0.43f, 1f, 0.57f) },
    };

    bool IsPreset(LayoutPreset p) =>
        showViewer == p.viewer && showTimeline == p.timeline && showDetails == p.details &&
        (layout == p.layout || !(p.viewer && p.details));

    void ApplyPreset(LayoutPreset p)
    {
        layout = p.layout;
        showViewer = p.viewer;
        showTimeline = p.timeline;
        showDetails = p.details;
        viewerHeight = detailsWidth = 0f;
        viewerDirty = true;
        SavePrefs();
        Repaint();
    }

    static Rect MapThumb(Rect area, Rect q) =>
        new Rect(area.x + q.x * area.width, area.y + q.y * area.height, q.width * area.width - 1f, q.height * area.height - 1f);

    // A little picture of a layout: toolbar on top, then the viewer, timeline and settings boxes
    static void DrawLayoutThumb(Rect r, LayoutPreset p, bool current)
    {
        EditorGUI.DrawRect(r, current ? Accent : new Color(0.12f, 0.12f, 0.13f));
        Rect inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
        EditorGUI.DrawRect(inner, new Color(0.17f, 0.17f, 0.18f));
        EditorGUI.DrawRect(new Rect(inner.x, inner.y, inner.width, 5f), new Color(0.3f, 0.3f, 0.32f)); // toolbar
        Rect area = new Rect(inner.x + 2f, inner.y + 7f, inner.width - 4f, inner.height - 9f);

        if (p.viewer)
        {
            Rect v = MapThumb(area, p.v);
            EditorGUI.DrawRect(v, new Color(0.2f, 0.36f, 0.55f));
            // a little picture inside: horizon and sun
            EditorGUI.DrawRect(new Rect(v.x, v.y + v.height * 0.62f, v.width, v.height * 0.38f), new Color(0.16f, 0.28f, 0.4f));
            EditorGUI.DrawRect(new Rect(v.x + v.width * 0.7f, v.y + v.height * 0.2f, Mathf.Max(3f, v.width * 0.08f), Mathf.Max(3f, v.width * 0.08f)),
                               new Color(0.95f, 0.8f, 0.4f));
        }
        if (p.timeline)
        {
            Rect t = MapThumb(area, p.t);
            EditorGUI.DrawRect(t, new Color(0.22f, 0.22f, 0.24f));
            int lanes = 4;
            for (int i = 0; i < lanes; i++)
            {
                float y = t.y + 2f + i * (t.height - 4f) / lanes;
                float lh = Mathf.Max(1f, (t.height - 4f) / lanes - 1.5f);
                float x0 = t.x + 2f + (i * 7 % 5) * t.width * 0.06f;
                EditorGUI.DrawRect(new Rect(x0, y, t.width * (0.35f + 0.1f * (i % 2)), lh),
                                   i == 0 ? new Color(0.85f, 0.55f, 0.25f) : new Color(0.45f, 0.65f, 0.55f, 0.9f));
            }
            EditorGUI.DrawRect(new Rect(t.x + t.width * 0.42f, t.y, 1f, t.height), Color.white); // playhead
        }
        if (p.details)
        {
            Rect d = MapThumb(area, p.d);
            EditorGUI.DrawRect(d, new Color(0.26f, 0.26f, 0.28f));
            int lines = Mathf.Max(2, Mathf.FloorToInt((d.height - 4f) / 5f));
            for (int i = 0; i < lines; i++)
            {
                float w = d.width * (i % 3 == 0 ? 0.45f : 0.75f) - 4f;
                EditorGUI.DrawRect(new Rect(d.x + 3f, d.y + 3f + i * 5f, Mathf.Max(2f, w), 2f),
                                   i % 3 == 0 ? new Color(0.75f, 0.75f, 0.78f) : new Color(0.45f, 0.45f, 0.48f));
            }
        }
    }

    // The Layout button's drop-down: pictures of each layout, then the other options
    class LayoutPicker : PopupWindowContent
    {
        readonly CinematicWindow w;
        public LayoutPicker(CinematicWindow window) { w = window; }

        const float ThumbW = 128f, ThumbH = 76f, Pad = 8f;
        const int Cols = 3;

        public override Vector2 GetWindowSize()
        {
            int rows = Mathf.CeilToInt(LayoutPresets.Length / (float)Cols);
            return new Vector2(Pad + Cols * (ThumbW + Pad), Pad + rows * (ThumbH + 22f + Pad) + 64f);
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.17f));
            for (int i = 0; i < LayoutPresets.Length; i++)
            {
                var p = LayoutPresets[i];
                int col = i % Cols, row = i / Cols;
                Rect cell = new Rect(Pad + col * (ThumbW + Pad), Pad + row * (ThumbH + 22f + Pad), ThumbW, ThumbH + 20f);
                Rect thumb = new Rect(cell.x, cell.y, ThumbW, ThumbH);
                bool current = w.IsPreset(p);
                bool hover = cell.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    DrawLayoutThumb(thumb, p, current);
                    if (hover && !current) EditorGUI.DrawRect(new Rect(thumb.x, thumb.yMax - 2f, thumb.width, 2f), Accent);
                }
                GUI.Label(new Rect(cell.x, thumb.yMax + 2f, ThumbW, 18f), new GUIContent(p.name, p.tip), EditorStyles.centeredGreyMiniLabel);
                if (GUI.Button(cell, new GUIContent("", p.tip), GUIStyle.none))
                {
                    w.ApplyPreset(p);
                    editorWindow.Close();
                    GUIUtility.ExitGUI();
                }
                EditorGUIUtility.AddCursorRect(cell, MouseCursor.Link);
            }
            if (Event.current.type == EventType.MouseMove) editorWindow.Repaint();

            float y = rect.yMax - 56f;
            GUI.Label(new Rect(Pad, y, rect.width - 2f * Pad, 18f), "Drag the lines between the parts to resize them.", EditorStyles.centeredGreyMiniLabel);
            y += 22f;
            float bw = (rect.width - 3f * Pad) / 2f;
            if (GUI.Button(new Rect(Pad, y, bw, 24f), new GUIContent("Show / hide / pop out…", "Show or hide parts, or move them into their own windows")))
            {
                editorWindow.Close();
                w.ShowLayoutMenu();
                GUIUtility.ExitGUI();
            }
            if (GUI.Button(new Rect(2f * Pad + bw, y, bw, 24f), new GUIContent("Full screen viewer", "Esc to leave")))
            {
                editorWindow.Close();
                w.OpenFullScreen();
                GUIUtility.ExitGUI();
            }
        }
    }

    // ---------------------------------------------------------------- Full screen viewer

    void OpenFullScreen()
    {
        var w = CreateInstance<CinematicWindow>();
        w.fullscreenView = true;
        w.prefsRole = "FullScreen";
        w.showViewer = true;
        w.showTimeline = w.showDetails = false;
        w.showGizmos = false;
        w.viewQuality = viewQuality;
        w.titleContent = new GUIContent("Cinematic (full screen)");

        // The whole screen the editor is on: the primary display, or the main editor window if it's elsewhere
        float ppp = EditorGUIUtility.pixelsPerPoint;
        Rect screen = new Rect(0f, 0f, Screen.currentResolution.width / ppp, Screen.currentResolution.height / ppp);
        Rect main = EditorGUIUtility.GetMainWindowPosition();
        if (!screen.Contains(position.center)) screen = main;

        w.ShowPopup();
        w.position = screen;
        w.wantsMouseMove = true;
        w.lastMouseMove = EditorApplication.timeSinceStartup;
        w.Focus();
    }

    void DrawFullScreen()
    {
        Event e = Event.current;
        float W = position.width, H = position.height;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || (e.keyCode == KeyCode.F11)))
        {
            e.Use();
            Close();
            GUIUtility.ExitGUI();
        }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space) { TogglePlay(); e.Use(); }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.G) { showGizmos = !showGizmos; e.Use(); }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) { lastMouseMove = EditorApplication.timeSinceStartup; Repaint(); }

        DrawViewer(new Rect(0f, 0f, W, H));

        // Controls appear while the mouse moves, then get out of the way
        bool show = EditorApplication.timeSinceStartup - lastMouseMove < 2.0;
        if (show)
        {
            Rect bar = new Rect(10f, 10f, 300f, 28f);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.55f));
            if (GUI.Button(new Rect(bar.x + 3f, bar.y + 3f, 90f, 22f), new GUIContent("✕  Exit  (Esc)"))) { Close(); GUIUtility.ExitGUI(); }
            if (GUI.Button(new Rect(bar.x + 97f, bar.y + 3f, 70f, 22f), new GUIContent(c.IsPlaying || playing ? "❚❚ Pause" : "▶ Play", "Space"))) TogglePlay();
            GUI.Label(new Rect(bar.x + 175f, bar.y + 5f, 120f, 18f), Fmt(time), overlayStyle);
        }
        else if (e.type == EventType.Repaint) EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, W, H), MouseCursor.Arrow);
    }

    // Opens a new Cinematic window with just that part (dock it anywhere) and hides it here
    void PopOut(bool viewer, bool timeline, bool details, string title)
    {
        var w = CreateWindow<CinematicWindow>(title);
        w.titleContent = new GUIContent(title, Icon(viewer ? "UnityEditor.GameView" : timeline ? "Animation Icon" : "Camera Icon"));
        w.prefsRole = viewer ? "Viewer" : timeline ? "Timeline" : "Settings";
        w.LoadPrefs();
        w.showViewer = viewer;
        w.showTimeline = timeline;
        w.showDetails = details;
        w.minSize = new Vector2(320f, 200f);
        w.Show();
        if (viewer) showViewer = false;
        if (timeline) showTimeline = false;
        if (details) showDetails = false;
        if (!showViewer && !showTimeline && !showDetails) showDetails = true; // keep something here
        w.SavePrefs();
        SavePrefs();
        Repaint();
    }

    void DrawEmpty()
    {
        GUILayout.BeginArea(new Rect(0f, 0f, position.width, position.height));
        GUILayout.FlexibleSpace();
        GUILayout.Label("No Cinematic Controller in this scene.", emptyStyle);
        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUI.backgroundColor = Accent;
        if (GUILayout.Button("Add one to the Main Camera", GUILayout.Height(30f), GUILayout.Width(220f)))
        {
            Camera cam = Camera.main;
            if (cam != null) SetTarget(Undo.AddComponent<CinematicController>(cam.gameObject));
            else ShowNotification(new GUIContent("No camera tagged MainCamera"));
        }
        GUI.backgroundColor = Color.white;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }

    // The item last clicked on the timeline or in the viewer: outlined, and Delete removes it
    static object selItem;

    // Selecting the FOV bar: Delete turns the shot's FOV animation off
    class FovMarker
    {
        public readonly Shot shot;
        public FovMarker(Shot s) { shot = s; }
    }

    static bool IsSelectedItem(object item) => item != null && ReferenceEquals(item, selItem);

    // What a bar on a per-shot lane stands for
    static object ItemOf(Shot s, int lane, int sub)
    {
        switch (lane)
        {
            case 0: return s;                  // the shot's FOV animation (deleting turns it off)
            case LookLane: return s.feed;
            case MotionLane: return MotionAt(s, sub);
            case PopupLane: return PopupAt(s, sub);
            case ScanLane: return s.scan;
            default: return null;
        }
    }

    static void Outline(Rect b)
    {
        if (Event.current.type != EventType.Repaint) return;
        Color w = Color.white;
        EditorGUI.DrawRect(new Rect(b.x - 1f, b.y - 1f, b.width + 2f, 2f), w);
        EditorGUI.DrawRect(new Rect(b.x - 1f, b.yMax - 1f, b.width + 2f, 2f), w);
        EditorGUI.DrawRect(new Rect(b.x - 1f, b.y - 1f, 2f, b.height + 2f), w);
        EditorGUI.DrawRect(new Rect(b.xMax - 1f, b.y - 1f, 2f, b.height + 2f), w);
    }

    void DeleteSelectedItem()
    {
        object item = selItem;
        if (item == null) return;
        selItem = null;

        if (item is FovMarker fm) { Record("Turn Off FOV Animation"); fm.shot.animateFov = false; Changed(); return; }
        if (item is CinematicController.Feed look) { Record("Turn Off Camera Look"); look.look = false; Changed(); return; }
        if (item is CinematicController.ImageLayer im) { Record("Remove Overlay"); c.images.Remove(im); Changed(); return; }
        if (item is CinematicController.SoundClip snd) { Record("Remove Sound"); c.sounds.Remove(snd); Changed(); return; }
        for (int i = 0; i < c.shots.Count; i++)
        {
            Shot s = c.shots[i];
            if (item is CinematicController.Motion mo && s.motions != null && s.motions.Contains(mo))
            { Record("Remove Motion"); s.motions.Remove(mo); Changed(); return; }
            if (item is CinematicController.Popup pp && s.popups != null && s.popups.Contains(pp))
            { RemovePopup(s, s.popups.IndexOf(pp)); return; }
            if (item is CinematicController.ScanTarget tg && s.scan != null && s.scan.targets != null && s.scan.targets.Contains(tg))
            { Record("Remove Scan Target"); s.scan.targets.Remove(tg); Changed(); return; }
            if (ReferenceEquals(item, s.scan)) { Record("Turn Off Scan"); s.scan.enabled = false; Changed(); return; }
            if (item is CinematicController.ShotEvent sev && s.events != null && s.events.Contains(sev))
            { Record("Remove Event"); s.events.Remove(sev); Changed(); return; }
        }
    }

    // ---------------------------------------------------------------- Copy / paste (Ctrl+C, Ctrl+V)

    static string clipJson;
    static Type clipType;

    static string ClipName(Type t) =>
        t == typeof(CinematicController.ImageLayer) ? "overlay" : t == typeof(CinematicController.SoundClip) ? "sound" :
        t == typeof(CinematicController.Motion) ? "motion clip" : t == typeof(CinematicController.Popup) ? "pop-up" :
        t == typeof(CinematicController.ShotEvent) ? "event" : t == typeof(CinematicController.ScanTarget) ? "scan target" :
        t == typeof(CinematicController.Feed) ? "camera look" : t == typeof(Shot) ? "shot" : "item";

    bool CanCopy() => selItem != null && !(selItem is FovMarker);

    void CopySelected()
    {
        if (!CanCopy()) return;
        clipJson = EditorJsonUtility.ToJson(selItem);
        clipType = selItem.GetType();
        if (selItem is CinematicController.Feed) SessionState.SetString(LookClipboardKey, clipJson); // the look's Paste ▾ button too
        ShowNotification(new GUIContent($"Copied {ClipName(clipType)}"), 1.0);
    }

    // Pastes into the shot under the playhead, at the playhead's time
    void Paste()
    {
        if (clipJson == null || clipType == null || selected < 0 || selected >= c.shots.Count) return;
        Shot s = c.shots[selected];
        float shotStart = StartOf(selected);
        float local = Mathf.Clamp(time - shotStart, 0f, s.duration);
        object copy = Activator.CreateInstance(clipType);
        EditorJsonUtility.FromJsonOverwrite(clipJson, copy);
        Record("Paste " + ClipName(clipType));

        if (copy is CinematicController.ImageLayer im)
        {
            im.start = Snap(time);
            if (!string.IsNullOrEmpty(im.name)) im.name += " copy";
            c.images.Add(im);
            tab = Tab.Overlays;
        }
        else if (copy is CinematicController.SoundClip sc)
        {
            sc.start = Snap(time);
            c.sounds.Add(sc);
            tab = Tab.Audio;
        }
        else if (copy is CinematicController.Motion mo)
        {
            if (s.motions == null) s.motions = new List<CinematicController.Motion>();
            float mlen = mo.duration;
            mo.start = Snap(local);
            if (mlen > 0f) mo.duration = Mathf.Min(mlen, Mathf.Max(0.05f, s.duration - mo.start));
            s.motions.Add(mo);
            tab = Tab.Motion;
        }
        else if (copy is CinematicController.Popup pp)
        {
            if (s.popups == null) s.popups = new List<CinematicController.Popup>();
            pp.appearAt = Snap(local);
            pp.name += " copy";
            s.popups.Add(pp);
            tab = Tab.Popups;
        }
        else if (copy is CinematicController.ShotEvent ev)
        {
            if (s.events == null) s.events = new List<CinematicController.ShotEvent>();
            ev.time = Snap(local);
            s.events.Add(ev);
            tab = Tab.Events;
        }
        else if (copy is CinematicController.ScanTarget tg)
        {
            if (s.scan == null) { ShowNotification(new GUIContent("This shot has no scan to paste into")); return; }
            if (s.scan.targets == null) s.scan.targets = new List<CinematicController.ScanTarget>();
            s.scan.targets.Add(tg);
            s.scan.enabled = true;
            tab = Tab.Scan;
        }
        else if (copy is CinematicController.Feed)
        {
            if (s.feed == null) s.feed = new CinematicController.Feed();
            EditorJsonUtility.FromJsonOverwrite(clipJson, s.feed); // a look replaces this shot's look
            copy = s.feed;
            tab = Tab.Camera;
        }
        else if (copy is Shot shot)
        {
            // A whole shot: goes in right after the shot under the playhead
            var anchors = CaptureAnchors();
            c.shots.Insert(selected + 1, shot);
            RestoreAnchors(anchors);
            selected++;
            SetTime(StartOf(selected));
        }
        else return;
        selItem = copy;
        if (!(copy is Shot) && !(copy is CinematicController.Feed)) itemOpen[copy] = true;
        Changed();
        ShowNotification(new GUIContent($"Pasted {ClipName(clipType)} into shot {selected + 1}"), 1.0);
    }

    // Unity sends Ctrl+C / Ctrl+V (Cmd on Mac) to the window as Copy / Paste commands
    void HandleCommands()
    {
        Event e = Event.current;
        if ((e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand) || GUIUtility.keyboardControl != 0) return;
        bool copy = e.commandName == "Copy" && CanCopy();
        bool paste = e.commandName == "Paste" && clipJson != null;
        bool dup = e.commandName == "Duplicate" && CanCopy() && !(selItem is CinematicController.Feed);
        if (!copy && !paste && !dup) return;
        if (e.type == EventType.ValidateCommand) { e.Use(); return; }
        e.Use();
        if (copy) CopySelected();
        else if (paste) EditorApplication.delayCall += () => { if (this != null && c != null) { Paste(); Repaint(); } };
        else
        {
            // Ctrl+D: copy and paste in one go
            CopySelected();
            EditorApplication.delayCall += () => { if (this != null && c != null) { Paste(); Repaint(); } };
        }
    }

    void HandleKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0 || c.shots.Count == 0) return;

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (selItem is Shot shot)
            {
                int i = c.shots.IndexOf(shot);
                selItem = null;
                if (i >= 0) { selected = i; DeleteShot(); }
            }
            else if (selItem != null)
            {
                // the shot itself stands for its FOV animation on the FOV lane
                DeleteSelectedItem();
            }
            e.Use();
            return;
        }
        if (e.keyCode == KeyCode.Escape) { selItem = null; Repaint(); e.Use(); return; }

        bool arrow = e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow ||
                     e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow;
        if (arrow && NudgeSelected(e)) { e.Use(); return; }

        if (e.keyCode == KeyCode.G) { showGizmos = !showGizmos; SavePrefs(); Repaint(); e.Use(); return; }
        if (e.keyCode == KeyCode.Space) { TogglePlay(); e.Use(); }
        else if (e.keyCode == KeyCode.LeftArrow) { GoToShot(Mathf.Max(0, selected - 1)); e.Use(); }
        else if (e.keyCode == KeyCode.RightArrow) { GoToShot(Mathf.Min(c.shots.Count - 1, selected + 1)); e.Use(); }
    }

    // Arrow keys move what's selected. On screen (overlays, pop-ups, scan targets): one pixel (at 1080p) per press,
    // Shift = 10. Alt + left / right, or anything that only lives on the timeline (sounds, motion clips): one frame
    // (1/30 s) in time per press, Shift = 10 frames.
    bool NudgeSelected(Event e)
    {
        if (selItem == null || selItem is Shot || selItem is FovMarker) return false;
        float mul = e.shift ? 10f : 1f;
        int dx = e.keyCode == KeyCode.LeftArrow ? -1 : e.keyCode == KeyCode.RightArrow ? 1 : 0;
        int dy = e.keyCode == KeyCode.UpArrow ? -1 : e.keyCode == KeyCode.DownArrow ? 1 : 0;
        float dt = dx * mul / 30f;

        var im = selItem as CinematicController.ImageLayer;
        var pp = selItem as CinematicController.Popup;
        var tg = selItem as CinematicController.ScanTarget;
        bool onScreen = (im != null && im.kind != CinematicController.OverlayKind.UIObject) || pp != null || tg != null;

        if (onScreen && !e.alt)
        {
            Rect r = im != null ? im.rect : pp != null ? pp.rect : tg.rect;
            r.x = Mathf.Clamp(r.x + dx * mul / 1920f, -r.width + 0.01f, 0.99f);
            r.y = Mathf.Clamp(r.y + dy * mul / 1080f, -r.height + 0.01f, 0.99f);
            Record("Nudge");
            if (im != null) im.rect = r; else if (pp != null) pp.rect = r; else tg.rect = r;
            Changed();
            Repaint();
            return true;
        }

        if (dx == 0) return onScreen; // up / down only mean something on screen
        Record("Nudge in Time");
        if (im != null) im.start = Mathf.Max(0f, im.start + dt);
        else if (pp != null) pp.appearAt = Mathf.Max(0f, pp.appearAt + dt);
        else if (selItem is CinematicController.SoundClip sc) sc.start = Mathf.Max(0f, sc.start + dt);
        else if (selItem is CinematicController.Motion mo) mo.start = Mathf.Max(0f, mo.start + dt);
        else if (selItem is CinematicController.ShotEvent sev)
        {
            Shot owner = null;
            int oi = -1;
            for (int i = 0; i < c.shots.Count && owner == null; i++)
                if (c.shots[i].events != null && c.shots[i].events.Contains(sev)) { owner = c.shots[i]; oi = i; }
            if (owner == null) return false;
            sev.time = Mathf.Clamp(sev.time + dt, 0f, owner.duration);
            SetTime(StartOf(oi) + sev.time);
        }
        else return false;
        Changed();
        Repaint();
        return true;
    }

    // ---------------------------------------------------------------- Toolbar

    void DrawToolbar(Rect r)
    {
        EditorGUI.DrawRect(r, Panel);
        GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 10f, r.width - 20f, r.height - 12f));
        GUILayout.BeginHorizontal();

        GUILayout.Label(c.name, titleStyle, GUILayout.MaxWidth(160f));
        GUILayout.Space(10f);

        Color old = GUI.backgroundColor;
        using (new EditorGUI.DisabledScope(c.shots.Count == 0))
        {
            if (GUILayout.Button(IconOr(null, "|◀", "Animation.PrevKey", "Previous shot (Left arrow)"), GUILayout.Width(34f), GUILayout.Height(24f)))
            {
                StopPlaying();
                int shot = ShotAt(time, out float local);
                GoToShot(local < 0.1f ? Mathf.Max(0, shot - 1) : shot);
            }
            if (playing) GUI.backgroundColor = Accent;
            if (GUILayout.Button(IconOr(null, playing ? "❚❚" : "▶", playing ? "PauseButton" : "PlayButton", "Play / pause (Space)"), GUILayout.Width(42f), GUILayout.Height(24f))) TogglePlay();
            GUI.backgroundColor = old;
            if (GUILayout.Button(IconOr(null, "▶|", "Animation.NextKey", "Next shot (Right arrow)"), GUILayout.Width(34f), GUILayout.Height(24f)))
            {
                StopPlaying();
                GoToShot(Mathf.Min(ShotAt(time, out _) + 1, c.shots.Count - 1));
            }

            GUILayout.Space(10f);
            GUILayout.Label($"{Fmt(time)}  /  {Fmt(Total())}", timeStyle, GUILayout.Width(150f));

            if (Application.isPlaying)
            {
                if (follow) GUI.backgroundColor = Accent;
                if (GUILayout.Button("Follow game", GUILayout.Width(92f), GUILayout.Height(24f)))
                {
                    follow = !follow;
                    if (follow) StopPlaying();
                }
            }
            else
            {
                if (GameSync) GUI.backgroundColor = Accent;
                if (GUILayout.Button(new GUIContent(GameSync ? "Game view: on" : "Game view: off",
                        "Also preview in the Game view (moves the real camera). Turn off for a faster editor."),
                        GUILayout.Width(104f), GUILayout.Height(24f)))
                {
                    GameSync = !GameSync;
                    SetPreview(GameSync);
                }
            }
            GUI.backgroundColor = old;
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button(IconOr("File ▾", "File ▾", "SaveAs", "Save this cinematic to a file in your project, or load one"),
                GUILayout.Width(70f), GUILayout.Height(24f)))
            ShowFileMenu();
        if (GUILayout.Button(IconOr("Layout ▾", "Layout ▾", "UnityEditor.SceneHierarchyWindow", "Pick a layout, or show, arrange and pop out the viewer, timeline and settings"),
                GUILayout.Width(86f), GUILayout.Height(24f)))
            PopupWindow.Show(layoutButtonRect, new LayoutPicker(this));
        if (Event.current.type == EventType.Repaint) layoutButtonRect = GUILayoutUtility.GetLastRect();
        int q = EditorGUILayout.Popup(viewQuality, new[] { "Full quality", "Half quality", "Quarter quality" }, GUILayout.Width(104f));
        if (q != viewQuality) { viewQuality = q; viewerDirty = true; SavePrefs(); }
        bool g = GUILayout.Toggle(showGizmos, IconOr("Gizmos", "Gizmos", "SceneViewVisibility",
                                  "Show the boxes, labels and info bar over the viewer (G). Turn off to see the shot exactly as it looks."),
                                  EditorStyles.miniButton, GUILayout.Height(22f), GUILayout.Width(64f));
        if (g != showGizmos) { showGizmos = g; SavePrefs(); Repaint(); }
        if (GUILayout.Button(IconOr("Full screen", "⛶", "Fullscreen", "Watch the viewer full screen (Esc to leave)"),
                             EditorStyles.miniButton, GUILayout.Height(22f), GUILayout.Width(30f)))
            OpenFullScreen();
        GUILayout.Space(10f);
        GUILayout.Label("Zoom", laneLabel);
        pxPerSec = GUILayout.HorizontalSlider(pxPerSec, 15f, 250f, GUILayout.Width(90f));
        GUILayout.Space(12f);

        GUI.backgroundColor = Accent;
        if (GUILayout.Button(IconLabel("Shot from Scene View", "Toolbar Plus", "Adds a shot at the end, framed like the Scene view"), GUILayout.Height(24f))) AddShot(true);
        GUI.backgroundColor = old;

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------- Viewer

    void DrawViewer(Rect outer)
    {
        EditorGUI.DrawRect(outer, new Color(0.05f, 0.05f, 0.06f));
        GUI.BeginClip(outer); // zoomed in, the picture is bigger than the viewer: keep it inside
        Rect r = new Rect(0f, 0f, outer.width, outer.height);

        const float aspect = 16f / 9f;
        float h = r.height - 12f, w = h * aspect;
        if (w > r.width - 12f) { w = r.width - 12f; h = w / aspect; }
        Rect fit = new Rect((r.width - w) * 0.5f, (r.height - h) * 0.5f, w, h);
        Event e = Event.current;
        HandleViewerZoom(r, fit, e);
        Rect view = ZoomedView(fit, r);

        Camera cam = c.cam != null ? c.cam : c.GetComponent<Camera>();
        if (cam == null)
        {
            GUI.Label(fit, "No camera on the controller", overlayCenter);
            GUI.EndClip();
            return;
        }
        if (e.type == EventType.Repaint)
        {
            // Zoomed in, render more pixels so the details are really there (up to 4K wide)
            float res = EditorGUIUtility.pixelsPerPoint * (viewQuality == 0 ? 1f : viewQuality == 1 ? 0.5f : 0.25f);
            int pw = Mathf.Max(16, Mathf.RoundToInt(view.width * res));
            if (pw > 3840) pw = 3840;
            int ph = Mathf.Max(16, Mathf.RoundToInt(pw / aspect));
            if (viewerRT == null || viewerRT.width != pw || viewerRT.height != ph)
            {
                ReleaseViewer();
                viewerRT = new RenderTexture(pw, ph, 24) { hideFlags = HideFlags.DontSave };
                viewerDirty = true;
            }

            // Following the running game: show the game's own finished picture, instead of rendering every camera a
            // second time (a big saving with several views)
            Texture live = Application.isPlaying && follow && c.IsPlaying ? c.LiveTexture : null;
            if (live != null)
            {
                GUI.DrawTexture(view, live, ScaleMode.StretchToFill, false);
                viewerDirty = false;
            }
            else
            {
                if (viewerDirty)
                {
                    c.RenderPreview(viewerRT, time); // own hidden cameras: never touches the Game view
                    viewerDirty = false;
                }
                GUI.DrawTexture(view, viewerRT, ScaleMode.StretchToFill, false);
            }
        }

        if (c.shots.Count > 0) HandleTextureDrop(view);

        if (c.shots.Count == 0)
        {
            EditorGUI.DrawRect(view, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(view, "Fly the Scene view to your first shot and press  + Shot from Scene View", overlayCenter);
        }
        else
        {
            int i = ShotAt(time, out _);
            if (showGizmos) DrawBoxHandles(view, i);
        }
        if (c.shots.Count > 0 && showGizmos)
        {
            int i = ShotAt(time, out _);
            // Info bar stays at the bottom of the viewer, even when zoomed
            float barY = Mathf.Min(view.yMax, r.yMax) - 24f;
            Rect bar = new Rect(Mathf.Max(view.x, 0f), barY, Mathf.Min(view.xMax, r.xMax) - Mathf.Max(view.x, 0f), 24f);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.45f));
            string mode = !Application.isPlaying ? "" : follow && c.IsPlaying ? "● LIVE      " : "IN-GAME PREVIEW      ";
            GUI.Label(new Rect(bar.x + 8f, bar.y + 3f, bar.width - 16f, 18f), $"{mode}{i + 1}  {c.shots[i].name}      {Fmt(time)}", overlayStyle);
        }
        if (showGizmos || viewZoom > 1f) DrawZoomControls(r, fit);
        GUI.EndClip();
    }

    Rect ZoomedView(Rect fit, Rect area)
    {
        if (viewZoom <= 1.001f) { viewZoom = 1f; viewPan = Vector2.zero; return fit; }
        Vector2 size = fit.size * viewZoom;
        Vector2 pos = fit.center - size * 0.5f + viewPan;
        // Keep the picture covering the middle of the viewer, so it can't get lost off-screen
        pos.x = Mathf.Clamp(pos.x, area.center.x - size.x, area.center.x);
        pos.y = Mathf.Clamp(pos.y, area.center.y - size.y, area.center.y);
        viewPan = pos - (fit.center - size * 0.5f);
        return new Rect(pos, size);
    }

    // Mouse wheel zooms towards the cursor; middle-drag (or Alt + drag) pans; F or double middle-click fits
    void HandleViewerZoom(Rect area, Rect fit, Event e)
    {
        bool over = area.Contains(e.mousePosition);
        if (e.type == EventType.ScrollWheel && over)
        {
            float factor = e.delta.y < 0f ? 1.15f : 1f / 1.15f;
            ZoomAround(fit, area, e.mousePosition, viewZoom * factor);
            e.Use();
        }
        else if (e.type == EventType.MouseDown && over && (e.button == 2 || (e.button == 0 && e.alt)))
        {
            if (e.button == 2 && e.clickCount == 2) { viewZoom = 1f; viewPan = Vector2.zero; viewerDirty = true; }
            else viewPanning = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && viewPanning)
        {
            viewPan += e.delta;
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && viewPanning)
        {
            viewPanning = false;
            e.Use();
        }
        else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F && over && !EditorGUIUtility.editingTextField)
        {
            viewZoom = 1f; viewPan = Vector2.zero; viewerDirty = true;
            e.Use();
            Repaint();
        }
        if (viewPanning) EditorGUIUtility.AddCursorRect(area, MouseCursor.Pan);
    }

    void ZoomAround(Rect fit, Rect area, Vector2 point, float newZoom)
    {
        newZoom = Mathf.Clamp(newZoom, 1f, 8f);
        Rect before = ZoomedView(fit, area);
        Vector2 p = new Vector2((point.x - before.x) / before.width, (point.y - before.y) / before.height); // spot under the cursor
        Vector2 size = fit.size * newZoom;
        viewZoom = newZoom;
        viewPan = point - Vector2.Scale(p, size) - (fit.center - size * 0.5f);
        viewerDirty = true;
        Repaint();
    }

    void DrawZoomControls(Rect area, Rect fit)
    {
        Rect box = new Rect(area.xMax - 150f, area.y + 6f, 144f, 20f);
        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.5f));
        Rect b = new Rect(box.x + 2f, box.y + 1f, 22f, 18f);
        if (GUI.Button(b, new GUIContent("−", "Zoom out (mouse wheel)"), EditorStyles.miniButtonLeft))
            ZoomAround(fit, area, area.center, viewZoom / 1.5f);
        b.x += 22f; b.width = 44f;
        GUI.Label(b, Mathf.RoundToInt(viewZoom * 100f) + "%", overlayCenter);
        b.x += 44f; b.width = 22f;
        if (GUI.Button(b, new GUIContent("+", "Zoom in (mouse wheel). Middle-drag or Alt + drag to move around"), EditorStyles.miniButtonMid))
            ZoomAround(fit, area, area.center, viewZoom * 1.5f);
        b.x += 22f; b.width = 50f;
        using (new EditorGUI.DisabledScope(viewZoom <= 1f))
            if (GUI.Button(b, new GUIContent("Fit", "Show the whole frame again (F)"), EditorStyles.miniButtonRight))
            {
                viewZoom = 1f; viewPan = Vector2.zero; viewerDirty = true;
            }
    }

    // Overlays, pop-up windows and scan targets of the shot, drawn as boxes in the viewer:
    // drag to move, drag the corner to resize
    Rect BoxRect(Shot s, int kind, int i) =>
        kind == 0 ? c.images[i].rect : kind == 1 ? s.popups[i].rect : s.scan.targets[i].rect;

    void SetBoxRect(Shot s, int kind, int i, Rect r)
    {
        if (kind == 0) c.images[i].rect = r;
        else if (kind == 1) s.popups[i].rect = r;
        else s.scan.targets[i].rect = r;
    }

    bool ImageInShot(CinematicController.ImageLayer im, int shotIndex)
    {
        if (shotIndex < 0 || shotIndex >= c.shots.Count) return false;
        float a = StartOf(shotIndex), b = a + c.shots[shotIndex].duration;
        return im != null && im.start < b && im.start + im.duration > a;
    }

    void DrawBoxHandles(Rect view, int shotIndex)
    {
        Shot s = c.shots[shotIndex];
        int nImages = c.images != null ? c.images.Count : 0;
        int nPopups = s.popups != null ? s.popups.Count : 0;
        int nTargets = s.scan != null && s.scan.enabled && s.scan.mode != CinematicController.ScanMode.Random && s.scan.targets != null
            ? s.scan.targets.Count : 0;
        int total = nImages + nTargets + nPopups;
        if (total == 0) return;
        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        ShotAt(time, out float local);

        // Top-most first: overlays, then scan targets, then pop-ups (last of each first)
        for (int n = 0; n < total; n++)
        {
            int kind, j;
            if (n < nImages) { kind = 0; j = nImages - 1 - n; }
            else if (n < nImages + nTargets) { kind = 2; j = nTargets - 1 - (n - nImages); }
            else { kind = 1; j = nPopups - 1 - (n - nImages - nTargets); }

            string label;
            bool visible;
            Color baseCol;
            if (kind == 0)
            {
                var im = c.images[j];
                if (!ImageInShot(im, shotIndex) || im.kind == CinematicController.OverlayKind.UIObject) continue;
                label = im.name;
                visible = time >= im.start && time <= im.start + im.duration;
                baseCol = ImageCol;
            }
            else if (kind == 1)
            {
                var pp = s.popups[j];
                label = pp.name;
                float end = pp.duration > 0f ? pp.appearAt + pp.duration : s.duration;
                visible = local >= pp.appearAt && local <= end;
                baseCol = Accent;
            }
            else
            {
                var sc = s.scan;
                label = string.IsNullOrEmpty(sc.targets[j].label) ? $"Target {j + 1}" : sc.targets[j].label;
                float end = sc.duration > 0f ? sc.start + sc.duration : s.duration;
                visible = local >= sc.start && local <= end;
                baseCol = sc.color;
            }

            Rect nr = BoxRect(s, kind, j);
            Rect r = new Rect(view.x + nr.x * view.width, view.y + nr.y * view.height, nr.width * view.width, nr.height * view.height);
            int cornerHit = CornerAt(r, e.mousePosition);

            if (e.type == EventType.Repaint)
            {
                object boxItem = kind == 0 ? c.images[j] : kind == 1 ? (object)s.popups[j] : s.scan.targets[j];
                if (IsSelectedItem(boxItem)) baseCol = Color.white;
                Color col = new Color(baseCol.r, baseCol.g, baseCol.b, visible ? 0.9f : 0.3f);
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), col);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), col);
                EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), col);
                EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), col);
                // Handles on every corner (bigger and brighter when the mouse is near)
                for (int k = 0; k < 4; k++)
                {
                    Vector2 p = CornerPoint(r, k);
                    float hs = cornerHit == k ? 10f : 7f;
                    EditorGUI.DrawRect(new Rect(p.x - hs * 0.5f - 1f, p.y - hs * 0.5f - 1f, hs + 2f, hs + 2f), new Color(0f, 0f, 0f, 0.6f));
                    EditorGUI.DrawRect(new Rect(p.x - hs * 0.5f, p.y - hs * 0.5f, hs, hs), cornerHit == k ? Color.white : col);
                }
                if (!visible || kind != 2)
                    GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 12f, 16f), visible ? label : label + " (not showing now)", overlayStyle);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.MoveArrow);
                for (int k = 0; k < 4; k++)
                {
                    Vector2 p = CornerPoint(r, k);
                    EditorGUIUtility.AddCursorRect(new Rect(p.x - HandleGrab, p.y - HandleGrab, HandleGrab * 2f, HandleGrab * 2f),
                                                   k == 0 || k == 3 ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
                }
            }
            else if (e.type == EventType.MouseMove && (cornerHit >= 0 || r.Contains(e.mousePosition))) Repaint();
            else if (e.type == EventType.MouseDown && e.button == 0 && (r.Contains(e.mousePosition) || cornerHit >= 0))
            {
                boxDragShot = shotIndex;
                boxDragIndex = j;
                boxDragKind = kind;
                tab = kind == 0 ? Tab.Overlays : kind == 1 ? Tab.Popups : Tab.Scan;
                if (kind == 0) Focus(c.images[j]);
                else if (kind == 1) Focus(s.popups[j]);
                selItem = kind == 0 ? c.images[j] : kind == 1 ? (object)s.popups[j] : s.scan.targets[j];
                GUI.FocusControl(null); // so Delete goes to the timeline selection
                boxResize = cornerHit >= 0;
                boxCorner = cornerHit;
                boxStart = nr;
                boxKeepShape = kind == 1 || (kind == 0 && c.images[j].keepShape && c.images[j].kind == CinematicController.OverlayKind.Image);
                boxGrab = new Vector2((e.mousePosition.x - view.x) / view.width - nr.x,
                                      (e.mousePosition.y - view.y) / view.height - nr.y);
                selected = shotIndex;
                GUIUtility.hotControl = id;
                e.Use();
                return;
            }
        }

        // Guide lines while a drag is snapping
        if (e.type == EventType.Repaint && GUIUtility.hotControl == id && boxDragShot == shotIndex)
        {
            Color guide = new Color(1f, 0.25f, 0.75f, 0.9f);
            foreach (float gx in guideX) EditorGUI.DrawRect(new Rect(view.x + gx * view.width - 0.5f, view.y, 1f, view.height), guide);
            foreach (float gy in guideY) EditorGUI.DrawRect(new Rect(view.x, view.y + gy * view.height - 0.5f, view.width, 1f), guide);
        }

        int count = boxDragKind == 0 ? nImages : boxDragKind == 1 ? nPopups : nTargets;
        if (GUIUtility.hotControl == id && e.type == EventType.MouseDrag && boxDragShot == shotIndex &&
            boxDragIndex >= 0 && boxDragIndex < count)
        {
            Vector2 m = new Vector2((e.mousePosition.x - view.x) / view.width, (e.mousePosition.y - view.y) / view.height);
            Record(boxDragKind == 0 ? "Move Overlay" : boxDragKind == 1 ? "Move Pop-up" : "Move Scan Target");
            Rect r = BoxRect(s, boxDragKind, boxDragIndex);
            guideX.Clear();
            guideY.Clear();
            bool snap = !e.alt;
            if (boxResize)
            {
                // Snap the dragged corner to the screen and the other boxes
                if (snap)
                {
                    BoxSnapTargets(s, boxDragKind, boxDragIndex, shotIndex, out List<float> tx, out List<float> ty);
                    m.x += SnapAxis(new[] { m.x }, tx, 6f / view.width, guideX);
                    m.y += SnapAxis(new[] { m.y }, ty, 6f / view.height, guideY);
                }
                r = ResizeBox(boxStart, boxCorner, m, e.control || e.command, boxKeepShape != e.shift);
            }
            else
            {
                r.x = Mathf.Clamp(m.x - boxGrab.x, 0f, 1f - r.width);
                r.y = Mathf.Clamp(m.y - boxGrab.y, 0f, 1f - r.height);
                if (snap)
                {
                    // Snap its left, centre or right (and top, middle or bottom) to the screen and to the other boxes
                    BoxSnapTargets(s, boxDragKind, boxDragIndex, shotIndex, out List<float> tx, out List<float> ty);
                    r.x += SnapAxis(new[] { r.xMin, r.center.x, r.xMax }, tx, 6f / view.width, guideX);
                    r.y += SnapAxis(new[] { r.yMin, r.center.y, r.yMax }, ty, 6f / view.height, guideY);
                }
            }
            SetBoxRect(s, boxDragKind, boxDragIndex, r);
            Changed();
            e.Use();
        }
        else if (GUIUtility.hotControl == id && e.type == EventType.MouseUp)
        {
            GUIUtility.hotControl = 0;
            boxDragIndex = -1;
            guideX.Clear();
            guideY.Clear();
            e.Use();
        }
    }

    readonly List<float> guideX = new List<float>(), guideY = new List<float>();

    // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right (screen pixels, y down)
    static Vector2 CornerPoint(Rect r, int k) => new Vector2(k == 1 || k == 3 ? r.xMax : r.xMin, k >= 2 ? r.yMax : r.yMin);

    // Which corner the mouse is on (a generous area around each), or -1
    static int CornerAt(Rect r, Vector2 m)
    {
        int best = -1;
        float bestD = HandleGrab;
        for (int k = 0; k < 4; k++)
        {
            Vector2 p = CornerPoint(r, k);
            float d = Mathf.Max(Mathf.Abs(m.x - p.x), Mathf.Abs(m.y - p.y));
            if (d <= bestD) { bestD = d; best = k; }
        }
        return best;
    }

    // Resize a box (0-1, top-left) by dragging one corner to m. The opposite corner stays put, or, with
    // fromCentre (Ctrl), the centre stays put so it grows evenly on every side.
    static Rect ResizeBox(Rect start, int corner, Vector2 m, bool fromCentre, bool keepShape)
    {
        bool right = corner == 1 || corner == 3, bottom = corner >= 2;
        float ax = fromCentre ? start.center.x : right ? start.xMin : start.xMax;
        float ay = fromCentre ? start.center.y : bottom ? start.yMin : start.yMax;
        float k = fromCentre ? 2f : 1f;
        float w = Mathf.Max(0.02f, (right ? m.x - ax : ax - m.x) * k);
        float h = Mathf.Max(0.01f, (bottom ? m.y - ay : ay - m.y) * k);
        if (keepShape && start.width > 0f && start.height > 0f)
        {
            // Follow whichever way the mouse moved more, keeping the proportions
            float sx = w / start.width, sy = h / start.height, sc = Mathf.Max(sx, sy);
            w = Mathf.Max(0.02f, start.width * sc);
            h = Mathf.Max(0.01f, start.height * sc);
        }
        float x = fromCentre ? ax - w * 0.5f : right ? ax : ax - w;
        float y = fromCentre ? ay - h * 0.5f : bottom ? ay : ay - h;
        return new Rect(x, y, w, h);
    }

    // Lines a box can snap to: the screen's edges and centre, and every other box's edges and centre (0-1, from the top-left)
    void BoxSnapTargets(Shot s, int kind, int index, int shotIndex, out List<float> xs, out List<float> ys)
    {
        xs = new List<float> { 0f, 0.5f, 1f };
        ys = new List<float> { 0f, 0.5f, 1f };
        for (int j = 0; c.images != null && j < c.images.Count; j++)
        {
            if ((kind == 0 && j == index) || !ImageInShot(c.images[j], shotIndex) || c.images[j].kind == CinematicController.OverlayKind.UIObject) continue;
            AddEdges(c.images[j].rect, xs, ys);
        }
        for (int j = 0; s.popups != null && j < s.popups.Count; j++)
            if (!(kind == 1 && j == index)) AddEdges(s.popups[j].rect, xs, ys);
        if (s.scan != null && s.scan.enabled && s.scan.targets != null)
            for (int j = 0; j < s.scan.targets.Count; j++)
                if (!(kind == 2 && j == index)) AddEdges(s.scan.targets[j].rect, xs, ys);
    }

    static void AddEdges(Rect r, List<float> xs, List<float> ys)
    {
        xs.Add(r.xMin); xs.Add(r.center.x); xs.Add(r.xMax);
        ys.Add(r.yMin); ys.Add(r.center.y); ys.Add(r.yMax);
    }

    // How far to move so the closest of "mine" lines up with one of "targets" (0 if none is within reach)
    static float SnapAxis(float[] mine, List<float> targets, float reach, List<float> guides)
    {
        float best = reach, move = 0f, line = -1f;
        foreach (float a in mine)
            foreach (float b in targets)
            {
                float d = Mathf.Abs(b - a);
                if (d < best) { best = d; move = b - a; line = b; }
            }
        if (line >= 0f) guides.Add(line);
        return move;
    }

    // ---------------------------------------------------------------- Rows of buttons that fit the panel

    static float panelWidth = 400f; // width of the settings panel being drawn (set by DrawDetails)

    // Room for a row inside a card, after the scrollbar, card padding and indentation
    static float rowInset;          // extra space taken on the left by the current block (e.g. an indented view look)
    static float RowWidth => Mathf.Max(60f, panelWidth - 44f - rowInset - EditorGUI.indentLevel * 15f);

    static GUIContent[] Contents(params string[] labels)
    {
        var r = new GUIContent[labels.Length];
        for (int i = 0; i < labels.Length; i++) r[i] = new GUIContent(labels[i]);
        return r;
    }

    // Joined buttons that carry on onto the next line when they don't fit. Returns the one clicked, or -1.
    // With a label, it goes in front (like a field label), or on its own line above when the panel is narrow.
    static int WrapButtons(GUIContent[] items, float height = 20f, GUIContent label = null)
    {
        float avail = RowWidth;
        float lead = 0f;
        if (label != null)
        {
            if (avail - EditorGUIUtility.labelWidth >= 150f) lead = EditorGUIUtility.labelWidth - 2f;
            else GUILayout.Label(label, EditorStyles.miniBoldLabel);
        }
        avail -= lead;

        int clicked = -1, i = 0;
        bool first = true;
        while (i < items.Length)
        {
            int start = i;
            float used = 0f;
            while (i < items.Length)
            {
                float w = EditorStyles.miniButtonMid.CalcSize(items[i]).x + 4f;
                if (i > start && used + w > avail) break;
                used += w;
                i++;
            }
            GUILayout.BeginHorizontal();
            if (lead > 0f)
            {
                if (first) GUILayout.Label(label, GUILayout.Width(lead));
                else GUILayout.Space(lead + 4f);
            }
            for (int k = start; k < i; k++)
            {
                GUIStyle st = i - start == 1 ? EditorStyles.miniButton : k == start ? EditorStyles.miniButtonLeft
                            : k == i - 1 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;
                if (GUILayout.Button(items[k], st, GUILayout.Height(height), GUILayout.MinWidth(10f))) clicked = k;
            }
            GUILayout.EndHorizontal();
            first = false;
        }
        return clicked;
    }

    // One-click alignment buttons for a box (0-1 from the top-left)
    Rect AlignButtons(Rect r)
    {
        int k = WrapButtons(new[]
        {
            new GUIContent("Left", "Left edge of the screen"), new GUIContent("Centre ↔", "Centre horizontally"),
            new GUIContent("Right", "Right edge of the screen"), new GUIContent("Top", "Top of the screen"),
            new GUIContent("Centre ↕", "Centre vertically"), new GUIContent("Bottom", "Bottom of the screen"),
            new GUIContent("Centre", "Centre of the screen")
        }, 18f, new GUIContent("Align", "Line it up on the screen"));
        switch (k)
        {
            case 0: r.x = 0f; break;
            case 1: r.x = 0.5f - r.width * 0.5f; break;
            case 2: r.x = 1f - r.width; break;
            case 3: r.y = 0f; break;
            case 4: r.y = 0.5f - r.height * 0.5f; break;
            case 5: r.y = 1f - r.height; break;
            case 6: r.x = 0.5f - r.width * 0.5f; r.y = 0.5f - r.height * 0.5f; break;
        }
        return r;
    }

    // Drop audio files or images onto the timeline: they're added where you drop them
    void HandleTimelineDrop(Rect area)
    {
        Event e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !area.Contains(e.mousePosition)) return;
        var clips = new List<AudioClip>();
        Texture tex = null;
        foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
        {
            if (o is AudioClip a) clips.Add(a);
            else if (o is Texture t && tex == null) tex = t;
            else if (o is Sprite sp && tex == null) tex = sp.texture;
        }
        if (clips.Count == 0 && tex == null) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            float at = SnapTime(Mathf.Clamp(TimeAt(e.mousePosition.x), 0f, Mathf.Max(0f, Total())));
            snapLine = -1f;
            if (clips.Count > 0) AddSounds(clips, at);
            else AddOverlay(CinematicController.OverlayKind.Image, tex, null, at);
        }
        e.Use();
    }

    void AddSounds(List<AudioClip> clips, float at)
    {
        Record("Add Sound");
        if (c.sounds == null) c.sounds = new List<CinematicController.SoundClip>();
        foreach (AudioClip clip in clips)
            c.sounds.Add(new CinematicController.SoundClip { name = clip != null ? clip.name : "Sound", clip = clip, start = Mathf.Max(0f, at) });
        if (c.shots.Count > 0) selected = ShotAt(at + 0.0001f, out _);
        tab = Tab.Audio;
        Changed();
        GUIUtility.ExitGUI();
    }

    // Drop an image from the Project window onto the viewer to add it to the shot at the playhead
    void HandleTextureDrop(Rect view)
    {
        Event e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !view.Contains(e.mousePosition)) return;

        Texture tex = null;
        var clips = new List<AudioClip>();
        foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
        {
            if (o is AudioClip a) clips.Add(a);
            else if (o is Texture t && tex == null) tex = t;
            else if (o is Sprite sp && tex == null) tex = sp.texture;
        }
        if (tex == null && clips.Count == 0) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform && clips.Count > 0)
        {
            DragAndDrop.AcceptDrag();
            AddSounds(clips, time); // sounds dropped on the viewer start at the playhead
        }
        else if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            Vector2 m = new Vector2((e.mousePosition.x - view.x) / view.width, (e.mousePosition.y - view.y) / view.height);
            selected = ShotAt(time, out _);
            if (!Application.isPlaying && GameSync && !c.preview) SetPreview(true);
            tab = Tab.Overlays;
            AddOverlay(CinematicController.OverlayKind.Image, tex, m, time);
        }
        e.Use();
    }

    void ReleaseViewer()
    {
        if (viewerRT == null) return;
        viewerRT.Release();
        DestroyImmediate(viewerRT);
        viewerRT = null;
    }

    // ---------------------------------------------------------------- Timeline

    float X(float t) => Gutter + t * pxPerSec - scrollX;
    float TimeAt(float x) => (x - Gutter + scrollX) / pxPerSec;

    float timelineWidth = 600f; // width of the timeline as last drawn

    void DrawTimeline(Rect r)
    {
        if (Event.current.type == EventType.Repaint) timelineWidth = r.width;
        float total = Total();
        float content = total * pxPerSec + 80f;
        float viewW = r.width - Gutter;
        float maxScroll = Mathf.Max(0f, content - viewW);
        scrollX = Mathf.Clamp(scrollX, 0f, maxScroll);

        Rect area = new Rect(r.x, r.y, r.width, r.height - ScrollH);
        float trackY = RulerH, laneY = RulerH + TrackH + 4f;

        GUI.BeginClip(area);
        float w = area.width, h = area.height;

        // Ruler + grid
        EditorGUI.DrawRect(new Rect(0f, 0f, w, RulerH), Panel);
        float step = pxPerSec >= 45f ? 1f : pxPerSec >= 20f ? 2f : 5f;
        for (float s = 0f; s <= total + step; s += step)
        {
            float x = X(s);
            if (x < Gutter - 1f || x > w) continue;
            EditorGUI.DrawRect(new Rect(x, RulerH - 6f, 1f, 6f), Ink(0.35f));
            EditorGUI.DrawRect(new Rect(x, RulerH, 1f, h - RulerH), Ink(0.05f));
            GUI.Label(new Rect(x + 3f, 2f, 40f, 18f), $"{s:0}s", laneLabel);
        }

        for (int l = 0; l < LaneNames.Length; l++)
            EditorGUI.DrawRect(new Rect(Gutter, laneY + LaneTop(l), w - Gutter, LaneHeightOf(l) - 2f), l % 2 == 0 ? Ink(0.03f) : Ink(0.015f));

        // Shots + effect bars
        float start = 0f;
        for (int i = 0; i < c.shots.Count; i++)
        {
            Shot s = c.shots[i];
            Rect b = new Rect(X(start) + 1f, trackY + 4f, Mathf.Max(2f, s.duration * pxPerSec - 2f), TrackH - 8f);
            DrawShotBlock(b, s, i);
            for (int l = 0; l < LaneNames.Length; l++)
                for (int sub = 0; sub < SubCount(s, l); sub++) DrawFxBar(s, i, l, sub, start, laneY);
            DrawEventMarkers(s, i, start, laneY);
            start += s.duration;
        }

        // Images: on the cinematic's own timeline, free to span shots
        float imgY = laneY + LaneTop(ImageLane);
        for (int j = 0; c.images != null && j < c.images.Count; j++)
        {
            var im = c.images[j];
            if (im == null) continue;
            int row = j < imageRow.Length ? imageRow[j] : 0;
            Rect b = new Rect(X(im.start), imgY + row * LaneH + 1f, Mathf.Max(3f, im.duration * pxPerSec), LaneH - 4f);
            Color col = LaneCols[ImageLane];
            col.a = ImageInShot(im, selected >= 0 ? selected : 0) ? 0.95f : 0.55f;
            EditorGUI.DrawRect(b, col);
            if (im.kind == CinematicController.OverlayKind.ProgressBar && im.fillTime > 0f && im.fillTime < im.duration)
            {
                // The time it stays full after loading: shaded, with a line where it reaches 100%
                float fx = X(im.start + im.fillTime);
                EditorGUI.DrawRect(new Rect(fx, b.y, b.xMax - fx, b.height), new Color(0f, 0f, 0f, 0.25f));
                EditorGUI.DrawRect(new Rect(fx - 0.5f, b.y, 1f, b.height), new Color(1f, 1f, 1f, 0.8f));
            }
            if (IsSelectedItem(im)) Outline(b);
            if (b.width > 40f)
            {
                EditorGUIUtility.SetIconSize(new Vector2(11f, 11f));
                GUI.Label(new Rect(b.x + 2f, b.y - 1f, b.width - 4f, b.height + 2f), IconLabel(im.name, OverlayIcon(im.kind)), barLabel);
                EditorGUIUtility.SetIconSize(Vector2.zero);
            }
            EditorGUIUtility.AddCursorRect(new Rect(b.x - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(b.xMax - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(b.x + 3f, b.y, Mathf.Max(0f, b.width - 6f), b.height), MouseCursor.Pan);
        }

        // Sounds: on the cinematic's own timeline, with their waveform
        float audY = laneY + LaneTop(AudioLane);
        for (int j = 0; c.sounds != null && j < c.sounds.Count; j++)
        {
            var snd = c.sounds[j];
            if (snd == null) continue;
            float len = CinematicController.LengthOf(snd);
            int row = j < soundRow.Length ? soundRow[j] : 0;
            Rect b = new Rect(X(snd.start), audY + row * LaneH + 1f, Mathf.Max(3f, len * pxPerSec), LaneH - 4f);
            Color col = LaneCols[AudioLane];
            col.a = IsSelectedItem(snd) ? 0.85f : 0.55f;
            EditorGUI.DrawRect(b, col);
            DrawSoundWave(b, snd, w);
            if (IsSelectedItem(snd)) Outline(b);
            if (b.width > 40f)
            {
                EditorGUIUtility.SetIconSize(new Vector2(11f, 11f));
                GUI.Label(new Rect(b.x + 2f, b.y - 1f, b.width - 4f, b.height + 2f), IconLabel(snd.name, "AudioClip Icon"), barLabel);
                EditorGUIUtility.SetIconSize(Vector2.zero);
            }
            EditorGUIUtility.AddCursorRect(new Rect(b.x - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(b.xMax - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(new Rect(b.x + 3f, b.y, Mathf.Max(0f, b.width - 6f), b.height), MouseCursor.Pan);
        }

        // Where a dragged shot will land
        if (drag == Drag.Reorder && reorderActive && reorderTarget >= 0)
        {
            float at = ReorderLineTime(dragShot, reorderTarget);
            EditorGUI.DrawRect(new Rect(X(at) - 1.5f, trackY, 3f, TrackH), Color.white);
        }

        // "+" block
        Rect add = new Rect(X(total) + 6f, trackY + 4f, 40f, TrackH - 8f);
        EditorGUI.DrawRect(add, Ink(0.07f));
        GUI.Label(add, "+", plusStyle);

        // Edge the current drag snapped to
        if (snapLine >= 0f && drag != Drag.None && drag != Drag.Scrub)
            EditorGUI.DrawRect(new Rect(X(snapLine) - 0.5f, 0f, 1f, h), new Color(1f, 1f, 1f, 0.8f));

        // Playhead
        float px = X(time);
        if (px >= Gutter)
        {
            EditorGUI.DrawRect(new Rect(px - 1f, 0f, 2f, h), Accent);
            EditorGUI.DrawRect(new Rect(px - 5f, 0f, 10f, 8f), Accent);
        }

        // Gutter labels (drawn last so scrolled content slides under them)
        EditorGUI.DrawRect(new Rect(0f, 0f, Gutter, h), Panel);
        EditorGUIUtility.SetIconSize(new Vector2(13f, 13f));
        GUI.Label(new Rect(6f, trackY, Gutter - 8f, TrackH), IconLabel("Shots", "Camera Icon"), laneLabel);
        for (int l = 0; l < LaneNames.Length; l++)
            GUI.Label(new Rect(6f, laneY + LaneTop(l), Gutter - 8f, LaneH - 2f), IconLabel(LaneNames[l], LaneIcons[l]), laneLabel);
        EditorGUIUtility.SetIconSize(Vector2.zero);
        EditorGUI.DrawRect(new Rect(Gutter - 1f, 0f, 1f, h), Ink(0.08f));

        HandleTimelineDrop(new Rect(Gutter, 0f, w - Gutter, h));
        HandleMouse(new Rect(0f, 0f, w, h), trackY, laneY);
        GUI.EndClip();

        if (maxScroll > 0f)
            scrollX = GUI.HorizontalScrollbar(new Rect(r.x + Gutter, r.yMax - ScrollH, r.width - Gutter, ScrollH),
                                              scrollX, viewW, 0f, content);
    }

    void DrawShotBlock(Rect b, Shot s, int i)
    {
        bool sel = i == selected;
        Color col = ShotCols[i % ShotCols.Length];
        if (!sel) col = Color.Lerp(col, Bg, 0.3f);
        if (drag == Drag.Reorder && reorderActive && i == dragShot) col = Color.Lerp(col, Bg, 0.6f);
        EditorGUI.DrawRect(b, col);

        bool blend = s.transition == Transition.Crossfade && i > 0;
        if ((s.transition == Transition.Move || blend) && s.transitionDuration > 0f)
        {
            float tw = Mathf.Min(s.transitionDuration, s.duration) * pxPerSec;
            const int slices = 10;
            // Move: darkens in; Crossfade: the previous shot's colour fading out
            Color fromCol = blend ? Color.Lerp(ShotCols[(i - 1) % ShotCols.Length], Bg, 0.3f) : new Color(0f, 0f, 0f, 0.45f);
            for (int k = 0; k < slices; k++)
                EditorGUI.DrawRect(new Rect(b.x + tw * k / slices, b.y, tw / slices + 1f, b.height),
                                   new Color(fromCol.r, fromCol.g, fromCol.b, (blend ? 0.8f : fromCol.a) * (1f - k / (float)slices)));
            Rect handle = new Rect(b.x + tw - 1f, b.y, 2f, b.height);
            EditorGUI.DrawRect(handle, new Color(1f, 1f, 1f, 0.55f));
            EditorGUIUtility.AddCursorRect(new Rect(b.x + tw - 4f, b.y + b.height * 0.5f, 8f, b.height * 0.5f),
                                           MouseCursor.ResizeHorizontal);
        }
        else
        {
            EditorGUI.DrawRect(new Rect(b.x, b.y, 3f, b.height), new Color(1f, 1f, 1f, 0.85f));
        }

        // Timed events: little markers along the top, like Animation events
        if (s.events != null)
            foreach (var ev in s.events)
            {
                if (ev == null) continue;
                float ex = b.x - 1f + Mathf.Clamp(ev.time, 0f, s.duration) * pxPerSec;
                bool evSel = IsSelectedItem(ev);
                Color mc = evSel ? Color.white : new Color(1f, 0.85f, 0.3f);
                EditorGUI.DrawRect(new Rect(ex - 0.5f, b.y, 1f, b.height), new Color(mc.r, mc.g, mc.b, 0.35f));
                EditorGUI.DrawRect(new Rect(ex - 3f, b.y - 4f, 6f, 8f), new Color(0f, 0f, 0f, 0.5f));
                EditorGUI.DrawRect(new Rect(ex - 2f, b.y - 3f, 4f, 6f), mc);
                EditorGUIUtility.AddCursorRect(new Rect(ex - 4f, b.y - 5f, 8f, 12f), MouseCursor.ResizeHorizontal);
            }

        if (IsSelectedItem(s)) Outline(b);
        if (sel)
        {
            EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 2f), Accent);
            EditorGUI.DrawRect(new Rect(b.x, b.yMax - 2f, b.width, 2f), Accent);
            EditorGUI.DrawRect(new Rect(b.x, b.y, 2f, b.height), Accent);
            EditorGUI.DrawRect(new Rect(b.xMax - 2f, b.y, 2f, b.height), Accent);
        }

        if (b.width > 30f)
        {
            GUI.Label(new Rect(b.x + 7f, b.y + 4f, b.width - 12f, 18f), $"{i + 1}  {s.name}", blockTitle);
            string sub = s.background == CinematicController.ShotBackground.Colour ? $"{s.duration:0.0#}s · colour, no camera"
                : s.background == CinematicController.ShotBackground.Image ? $"{s.duration:0.0#}s · image, no camera"
                : s.point == null
                ? "⚠ no camera point"
                : $"{s.duration:0.0#}s · {(s.transition == Transition.Move ? "move" : s.transition == Transition.Crossfade && i > 0 ? "crossfade" : "cut")} · {s.fov:0}°" +
                  (s.extraViews != null && s.extraViews.Count > 0 ? $" · {s.extraViews.Count + 1} views" : "");
            GUI.Label(new Rect(b.x + 7f, b.y + 22f, b.width - 12f, 16f), sub, blockSub);
        }

        EditorGUIUtility.AddCursorRect(new Rect(b.xMax - 4f, b.y, 8f, b.height), MouseCursor.ResizeHorizontal);
    }

    // Events lane: a pin for each event with its name; hover to see what it calls, click to open it
    void DrawEventMarkers(Shot s, int i, float shotStart, float laneY)
    {
        if (s.events == null || s.events.Count == 0) return;
        float y = laneY + LaneTop(EventLane);
        // In time order, so each name can use the room up to the next pin
        var order = new List<int>();
        for (int k = 0; k < s.events.Count; k++) if (s.events[k] != null) order.Add(k);
        order.Sort((a, b) => s.events[a].time.CompareTo(s.events[b].time));
        float shotEndX = X(shotStart + s.duration);
        for (int n = 0; n < order.Count; n++)
        {
            var ev = s.events[order[n]];
            float x = X(shotStart + Mathf.Clamp(ev.time, 0f, s.duration));
            float nextX = n + 1 < order.Count ? X(shotStart + Mathf.Clamp(s.events[order[n + 1]].time, 0f, s.duration)) : shotEndX;
            bool sel = IsSelectedItem(ev);
            Color col = LaneCols[EventLane];
            if (i != selected && !sel) col = Color.Lerp(col, Bg, 0.35f);
            if (sel) col = Color.white;

            // Pin: a line down the lane and a diamond-ish head
            EditorGUI.DrawRect(new Rect(x - 0.5f, y + 1f, 1f, LaneH - 3f), col);
            EditorGUI.DrawRect(new Rect(x - 4f, y + 2f, 8f, 8f), new Color(0f, 0f, 0f, 0.55f));
            EditorGUI.DrawRect(new Rect(x - 3f, y + 3f, 6f, 6f), col);

            // Name (and what it calls) next to it, as much as fits before the next pin
            float room = nextX - x - 8f;
            if (room > 18f)
            {
                string label = ev.name;
                string calls = EventCallsShort(ev);
                if (!string.IsNullOrEmpty(calls)) label += "  ·  " + calls;
                GUI.Label(new Rect(x + 6f, y - 1f, room, LaneH), label, sel ? barLabel : laneLabel);
            }
            GUI.Label(new Rect(x - 6f, y, Mathf.Max(12f, Mathf.Min(room, 140f) + 12f), LaneH),
                      new GUIContent("", EventTooltip(ev)));
            EditorGUIUtility.AddCursorRect(new Rect(x - 6f, y, 12f, LaneH), MouseCursor.ResizeHorizontal);
        }
    }

    // "Door.Open, Light.SetActive" — what an event calls, short
    static string EventCallsShort(CinematicController.ShotEvent ev)
    {
        if (ev.action == null) return "";
        int n = ev.action.GetPersistentEventCount();
        if (n == 0) return "(nothing yet)";
        var parts = new List<string>();
        for (int k = 0; k < n && k < 3; k++)
        {
            var target = ev.action.GetPersistentTarget(k);
            string method = ev.action.GetPersistentMethodName(k);
            parts.Add((target != null ? target.name : "None") + (string.IsNullOrEmpty(method) ? "" : "." + method));
        }
        return string.Join(", ", parts) + (n > 3 ? $" +{n - 3}" : "");
    }

    static string EventTooltip(CinematicController.ShotEvent ev)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{ev.name}  at {ev.time:0.##}s into the shot");
        int n = ev.action != null ? ev.action.GetPersistentEventCount() : 0;
        if (n == 0) sb.Append("\nNo calls yet: click to open it, then drag an object into its box.");
        for (int k = 0; k < n; k++)
        {
            var target = ev.action.GetPersistentTarget(k);
            string method = ev.action.GetPersistentMethodName(k);
            sb.Append("\n• ").Append(target != null ? target.name : "None")
              .Append(" → ").Append(string.IsNullOrEmpty(method) ? "(no function picked)" : method + "()");
        }
        sb.Append("\nDrag to move it · Delete to remove it");
        return sb.ToString();
    }

    // Shows the effects from earlier shots that are still running in this one, with a button to go and edit them
    void DrawCarriedIn(int shotIndex)
    {
        if (shotIndex <= 0 || shotIndex >= c.shots.Count) return;
        float myStart = StartOf(shotIndex);
        var notes = new List<string>();
        int fromShot = -1;
        for (int i = 0; i < shotIndex; i++)
        {
            Shot p = c.shots[i];
            float pStart = StartOf(i);
            float off = pStart - myStart; // this shot's time = that shot's time + off
            if (p.feed != null && p.feed.look && p.lookDuration > 0f && pStart + p.lookStart + p.lookDuration > myStart)
            { notes.Add($"shot {i + 1}'s camera look (until {off + p.lookStart + p.lookDuration:0.##}s)"); fromShot = i; }
            if (p.popups != null)
                foreach (var pp in p.popups)
                    if (pp != null && pp.duration > 0f && pStart + pp.appearAt + pp.duration > myStart)
                    { notes.Add($"shot {i + 1}'s pop-up '{pp.name}' (until {off + pp.appearAt + pp.duration:0.##}s)"); fromShot = i; }
            if (p.scan != null && p.scan.enabled && p.scan.duration > 0f && pStart + p.scan.start + p.scan.duration > myStart)
            { notes.Add($"shot {i + 1}'s scan (until {off + p.scan.start + p.scan.duration:0.##}s)"); fromShot = i; }
        }
        if (notes.Count == 0) return;
        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label(new GUIContent("Carried in: " + string.Join(", ", notes) + ". " +
                                       "They keep the settings of the shot they belong to." +
                                       (c.shots[shotIndex].feed != null && c.shots[shotIndex].feed.look ? " This shot's own look takes over while its look bar runs." : "")),
                        hintStyle);
        if (GUILayout.Button(new GUIContent($"Edit in shot {fromShot + 1}", "Go to the shot these belong to"), EditorStyles.miniButton, GUILayout.Width(96f)))
            GoToShot(fromShot);
        GUILayout.EndHorizontal();
    }

    static int SubCount(Shot s, int lane) =>
        lane == MotionLane ? (s.motions != null ? s.motions.Count : 0) :
        lane == PopupLane ? (s.popups != null ? s.popups.Count : 0) :
        lane == ImageLane || lane == AudioLane || lane == EventLane ? 0 : 1; // drawn separately (whole timeline, or as markers)

    void DrawFxBar(Shot s, int i, int lane, int sub, float shotStart, float laneY)
    {
        if (!GetFx(s, lane, sub, out float fs, out float fl)) return;

        Rect b = new Rect(X(shotStart + fs), laneY + LaneTop(lane) + SubRow(s, lane, sub) * LaneH + 1f, Mathf.Max(3f, fl * pxPerSec), LaneH - 4f);
        Color col = LaneCols[lane];
        col.a = i == selected ? 0.95f : 0.45f;
        EditorGUI.DrawRect(b, col);
        if (lane == 0 ? selItem is FovMarker fm && fm.shot == s : IsSelectedItem(ItemOf(s, lane, sub))) Outline(b);
        if (b.width > 40f) GUI.Label(new Rect(b.x + 4f, b.y - 1f, b.width - 6f, b.height + 2f), FxLabel(s, lane, sub), barLabel);
        if (CanCarry(lane) && fs + fl > s.duration + 0.001f)
        {
            // The part past its own shot: striped, with an arrow, so it's clear it belongs to this shot
            float bx = X(shotStart + s.duration);
            Rect carry = new Rect(bx, b.y, b.xMax - bx, b.height);
            for (float x = carry.x; x < carry.xMax; x += 6f)
                EditorGUI.DrawRect(new Rect(x, carry.y, Mathf.Min(3f, carry.xMax - x), carry.height), new Color(0f, 0f, 0f, 0.22f));
            EditorGUI.DrawRect(new Rect(bx - 0.5f, b.y, 1f, b.height), new Color(1f, 1f, 1f, 0.7f));
            if (carry.width > 26f) GUI.Label(new Rect(bx + 2f, b.y - 1f, carry.width - 4f, b.height + 2f),
                                             new GUIContent("→", $"Carries on into the next shot (it belongs to shot {i + 1})"), barLabel);
        }

        EditorGUIUtility.AddCursorRect(new Rect(b.x - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(new Rect(b.xMax - 3f, b.y, 6f, b.height), MouseCursor.ResizeHorizontal);
        EditorGUIUtility.AddCursorRect(new Rect(b.x + 3f, b.y, Mathf.Max(0f, b.width - 6f), b.height), MouseCursor.Pan);
    }

    void HandleMouse(Rect area, float trackY, float laneY)
    {
        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);

        switch (e.GetTypeForControl(id))
        {
            case EventType.ScrollWheel:
                if (!area.Contains(e.mousePosition)) break;
                if (e.control || e.command)
                {
                    float tAt = TimeAt(e.mousePosition.x);
                    pxPerSec = Mathf.Clamp(pxPerSec * (e.delta.y > 0f ? 0.9f : 1.1f), 15f, 250f);
                    scrollX = tAt * pxPerSec - (e.mousePosition.x - Gutter);
                }
                else
                {
                    scrollX += (Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y) ? e.delta.x : e.delta.y) * 12f;
                }
                e.Use();
                break;

            case EventType.MouseDown:
                if (e.button == 1 && area.Contains(e.mousePosition) && e.mousePosition.x >= Gutter)
                {
                    ShowTimelineMenu(e.mousePosition, trackY, laneY);
                    e.Use();
                    break;
                }
                if (e.button != 0 || !area.Contains(e.mousePosition) || e.mousePosition.x < Gutter) break;

                drag = Hit(e.mousePosition, trackY, laneY, out dragShot, out dragLane, out dragSub, out dragGrab);
                if (drag == Drag.Add)
                {
                    drag = Drag.None;
                    AddShot(true);
                    e.Use();
                    break;
                }
                if (drag == Drag.Select)
                {
                    // Click a shot: select it and jump there. Drag it: move it to another place.
                    selected = dragShot;
                    selItem = c.shots[dragShot];
                    drag = Drag.Reorder;
                    reorderFromX = e.mousePosition.x;
                    reorderActive = false;
                    reorderTarget = -1;
                    StopPlaying();
                    if (Application.isPlaying) follow = false;
                    else if (GameSync && !c.preview) SetPreview(true);
                    SetTime(TimeAt(e.mousePosition.x));
                }
                else if (drag != Drag.Scrub)
                {
                    tab = drag == Drag.ShotEnd || drag == Drag.Transition ? Tab.Camera : TabForLane(dragLane);
                    selItem = dragLane == ImageLane && c.images != null && dragSub < c.images.Count ? c.images[dragSub]
                            : dragLane == AudioLane && c.sounds != null && dragSub < c.sounds.Count ? (object)c.sounds[dragSub]
                            : dragShot >= 0 ? (dragLane == 0 ? c.shots[dragShot] : ItemOf(c.shots[dragShot], dragLane, dragSub))
                            : null;
                    if (dragLane == 0 && dragShot >= 0) selItem = new FovMarker(c.shots[dragShot]);
                    if (drag == Drag.Event && dragShot >= 0 && c.shots[dragShot].events != null && dragSub < c.shots[dragShot].events.Count)
                    {
                        var ev = c.shots[dragShot].events[dragSub];
                        selItem = ev;
                        tab = Tab.Events;
                        itemOpen[ev] = true;
                        StopPlaying();
                        SetTime(StartOf(dragShot) + ev.time);
                    }
                    if (dragLane == MotionLane && dragShot >= 0) Focus(MotionAt(c.shots[dragShot], dragSub));
                    // The playhead moves onto what was clicked (unless it's already over it), so the settings
                    // shown are always for the shot under the playhead
                    float spanA = 0f, spanB = 0f;
                    bool hasSpan = true;
                    if (dragShot >= 0) { spanA = StartOf(dragShot); spanB = spanA + c.shots[dragShot].duration; }
                    else if (dragLane == ImageLane)
                    {
                        spanA = c.images[dragSub].start; spanB = spanA + c.images[dragSub].duration;
                        Focus(c.images[dragSub]);
                    }
                    else if (dragLane == AudioLane)
                    {
                        spanA = c.sounds[dragSub].start; spanB = spanA + CinematicController.LengthOf(c.sounds[dragSub]);
                        Focus(c.sounds[dragSub]);
                    }
                    else hasSpan = false;
                    if (hasSpan && (time < spanA || time >= spanB))
                    {
                        StopPlaying();
                        SetTime(Mathf.Clamp(TimeAt(e.mousePosition.x), spanA, Mathf.Max(spanA, spanB - 0.001f)));
                    }
                    if (dragLane == PopupLane && dragShot >= 0) Focus(PopupAt(c.shots[dragShot], dragSub));
                }

                if (drag == Drag.Scrub)
                {
                    StopPlaying();
                    if (Application.isPlaying) follow = false;
                    else if (GameSync && !c.preview) SetPreview(true);
                    SetTime(TimeAt(e.mousePosition.x));
                }

                GUIUtility.hotControl = id;
                GUI.FocusControl(null);
                e.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) break;
                if (drag == Drag.Reorder)
                {
                    if (!reorderActive && Mathf.Abs(e.mousePosition.x - reorderFromX) > 6f) reorderActive = true;
                    if (reorderActive) reorderTarget = ReorderIndexAt(dragShot, TimeAt(e.mousePosition.x));
                }
                else ApplyDrag(e.mousePosition);
                e.Use();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != id) break;
                GUIUtility.hotControl = 0;
                if (drag == Drag.Reorder && reorderActive && reorderTarget >= 0) MoveShotTo(dragShot, reorderTarget);
                drag = Drag.None;
                reorderActive = false;
                snapLine = -1f;
                e.Use();
                break;
        }
    }

    Drag Hit(Vector2 m, float trackY, float laneY, out int shot, out int lane, out int sub, out float grab)
    {
        shot = -1;
        lane = -1;
        sub = 0;
        grab = 0f;
        if (m.y < RulerH) return Drag.Scrub;

        float t = TimeAt(m.x);
        bool inTrack = m.y >= trackY && m.y < trackY + TrackH;
        float start = 0f;

        // Event pins (and their names) on the Events lane
        float evY = laneY + LaneTop(EventLane);
        if (m.y >= evY && m.y < evY + LaneHeightOf(EventLane))
        {
            float st0 = 0f;
            for (int i = 0; i < c.shots.Count; i++)
            {
                var sh = c.shots[i];
                if (sh.events != null)
                    for (int k = sh.events.Count - 1; k >= 0; k--)
                    {
                        var ev = sh.events[k];
                        if (ev == null) continue;
                        if (Mathf.Abs(m.x - X(st0 + Mathf.Clamp(ev.time, 0f, sh.duration))) <= 6f)
                        {
                            shot = i; lane = EventLane; sub = k;
                            return Drag.Event;
                        }
                    }
                st0 += sh.duration;
            }
            return Drag.Scrub;
        }

        // Sound bars (whole timeline)
        float ay = laneY + LaneTop(AudioLane);
        if (m.y >= ay && m.y < ay + LaneHeightOf(AudioLane))
        {
            int rowHit = Mathf.FloorToInt((m.y - ay) / LaneH);
            for (int j = (c.sounds != null ? c.sounds.Count : 0) - 1; j >= 0; j--)
            {
                var snd = c.sounds[j];
                if (snd == null || (j < soundRow.Length && soundRow[j] != rowHit)) continue;
                float bx0 = X(snd.start), bx1 = X(snd.start + CinematicController.LengthOf(snd));
                lane = AudioLane;
                sub = j;
                if (Mathf.Abs(m.x - bx0) <= 4f) return Drag.FxStart;
                if (Mathf.Abs(m.x - bx1) <= 4f) return Drag.FxEnd;
                if (m.x > bx0 && m.x < bx1) { grab = t - snd.start; return Drag.FxBody; }
            }
            lane = -1;
            sub = 0;
            return Drag.Scrub;
        }

        // Image bars (whole timeline)
        float iy = laneY + LaneTop(ImageLane);
        if (m.y >= iy && m.y < iy + LaneHeightOf(ImageLane))
        {
            int rowHit = Mathf.FloorToInt((m.y - iy) / LaneH);
            for (int j = (c.images != null ? c.images.Count : 0) - 1; j >= 0; j--)
            {
                var im = c.images[j];
                if (im == null || (j < imageRow.Length && imageRow[j] != rowHit)) continue;
                float bx0 = X(im.start), bx1 = X(im.start + im.duration);
                lane = ImageLane;
                sub = j;
                if (Mathf.Abs(m.x - bx0) <= 4f) return Drag.FxStart;
                if (Mathf.Abs(m.x - bx1) <= 4f) return Drag.FxEnd;
                if (m.x > bx0 && m.x < bx1) { grab = t - im.start; return Drag.FxBody; }
            }
            lane = -1;
            sub = 0;
            return Drag.Scrub;
        }

        for (int i = 0; i < c.shots.Count; i++)
        {
            Shot s = c.shots[i];
            float x0 = X(start), x1 = X(start + s.duration);

            // Event markers along the top of the shot (just above it too, so they're easy to grab)
            if (s.events != null && m.y >= trackY && m.y < trackY + 14f)
                for (int k = s.events.Count - 1; k >= 0; k--)
                {
                    var ev = s.events[k];
                    if (ev == null) continue;
                    if (Mathf.Abs(m.x - X(start + Mathf.Clamp(ev.time, 0f, s.duration))) <= 5f) { shot = i; sub = k; return Drag.Event; }
                }

            if (inTrack)
            {
                if (Mathf.Abs(m.x - x1) <= 5f) { shot = i; return Drag.ShotEnd; }
                if (s.transition != Transition.Cut && s.transitionDuration > 0f)
                {
                    float tx = X(start + Mathf.Min(s.transitionDuration, s.duration));
                    if (Mathf.Abs(m.x - tx) <= 4f && m.y > trackY + TrackH * 0.5f) { shot = i; return Drag.Transition; }
                }
                if (m.x > x0 && m.x < x1) { shot = i; return Drag.Select; }
            }

            for (int l = 0; l < LaneNames.Length; l++)
            {
                float ly = laneY + LaneTop(l);
                if (m.y < ly || m.y >= ly + LaneHeightOf(l)) continue;
                int rowHit = Mathf.FloorToInt((m.y - ly) / LaneH);
                for (int k = SubCount(s, l) - 1; k >= 0; k--)
                {
                    if (SubRow(s, l, k) != rowHit) continue;
                    if (!GetFx(s, l, k, out float fs, out float fl)) continue;

                    float bx0 = X(start + fs), bx1 = X(start + fs + fl);
                    shot = i;
                    lane = l;
                    sub = k;
                    if (Mathf.Abs(m.x - bx0) <= 4f) return Drag.FxStart;
                    if (Mathf.Abs(m.x - bx1) <= 4f) return Drag.FxEnd;
                    if (m.x > bx0 && m.x < bx1) { grab = t - (start + fs); return Drag.FxBody; }
                    shot = -1;
                    lane = -1;
                    sub = 0;
                }
            }

            start += s.duration;
        }

        if (inTrack && m.x >= X(start) + 6f && m.x <= X(start) + 46f) return Drag.Add;
        return Drag.Scrub;
    }

    void ApplyDrag(Vector2 m)
    {
        float t = TimeAt(m.x);
        if (drag == Drag.Scrub)
        {
            SetTime(t);
            return;
        }
        // Overlay bars: move or stretch freely across shots
        if (dragLane == ImageLane && c.images != null && dragSub >= 0 && dragSub < c.images.Count)
        {
            var im = c.images[dragSub];
            Record("Edit Overlay Timing");
            switch (drag)
            {
                case Drag.FxBody:
                    im.start = Mathf.Clamp(SnapSpan(t - dragGrab, im.duration, im), 0f, Mathf.Max(0f, Total() - 0.1f));
                    break;
                case Drag.FxStart:
                {
                    float end = im.start + im.duration;
                    float ns = Mathf.Clamp(SnapTime(t, im), 0f, end - 0.1f);
                    im.start = ns;
                    im.duration = end - ns;
                    break;
                }
                case Drag.FxEnd:
                    im.duration = Mathf.Max(0.1f, SnapTime(t, im) - im.start);
                    break;
            }
            Changed();
            return;
        }

        // Sound bars: move, or trim either end like a video editor
        if (dragLane == AudioLane && c.sounds != null && dragSub >= 0 && dragSub < c.sounds.Count)
        {
            var snd = c.sounds[dragSub];
            float len = CinematicController.LengthOf(snd);
            Record("Edit Sound Timing");
            switch (drag)
            {
                case Drag.FxBody:
                    snd.start = Mathf.Clamp(SnapSpan(t - dragGrab, len, snd), 0f, Mathf.Max(0f, Total() - 0.1f));
                    break;
                case Drag.FxStart:
                {
                    float end = snd.start + len;
                    float earliest = snd.loop ? 0f : Mathf.Max(0f, snd.start - snd.clipStart); // can't go before the clip's start
                    float ns = Mathf.Clamp(SnapTime(t, snd), earliest, end - 0.1f);
                    snd.clipStart = Mathf.Max(0f, snd.clipStart + (ns - snd.start));
                    snd.start = ns;
                    snd.duration = end - ns;
                    break;
                }
                case Drag.FxEnd:
                {
                    float maxLen = snd.loop || snd.clip == null ? 3600f : Mathf.Max(0.1f, snd.clip.length - snd.clipStart);
                    snd.duration = Mathf.Clamp(SnapTime(t, snd) - snd.start, 0.1f, maxLen);
                    break;
                }
            }
            Changed();
            return;
        }

        if (dragShot < 0 || dragShot >= c.shots.Count) return;

        Shot s = c.shots[dragShot];
        float shotStart = StartOf(dragShot);
        float local = t - shotStart;
        Record("Edit Cinematic");

        switch (drag)
        {
            case Drag.ShotEnd:
            {
                var anchors = CaptureAnchors(); // overlays and sounds in later shots move with them
                s.duration = Mathf.Max(0.1f, SnapTime(t, null, shotEdges: false) - shotStart);
                RestoreAnchors(anchors);
                break;
            }
            case Drag.Transition:
                s.transitionDuration = Mathf.Clamp(SnapTime(t) - shotStart, 0.05f, s.duration);
                break;
            case Drag.Event:
                if (s.events != null && dragSub >= 0 && dragSub < s.events.Count && s.events[dragSub] != null)
                {
                    s.events[dragSub].time = Mathf.Clamp(SnapTime(t) - shotStart, 0f, s.duration);
                    SetTime(shotStart + s.events[dragSub].time); // the playhead follows, to see the moment
                }
                break;
            case Drag.FxBody:
            {
                GetFx(s, dragLane, dragSub, out _, out float fl);
                float ns = SnapSpan(t - dragGrab, fl, null) - shotStart;
                // It must start in its own shot; carrying lanes may run on past the end
                float maxStart = CanCarry(dragLane) ? Mathf.Max(0f, Mathf.Min(s.duration - 0.05f, Total() - shotStart - fl)) : s.duration - fl;
                SetFx(s, dragLane, dragSub, Mathf.Clamp(ns, 0f, Mathf.Max(0f, maxStart)), fl, false); // keeps its length
                break;
            }
            case Drag.FxStart:
            {
                GetFx(s, dragLane, dragSub, out float fs, out float fl);
                float end = fs + fl;
                float ns = Mathf.Clamp(SnapTime(t) - shotStart, 0f, end - 0.1f);
                SetFx(s, dragLane, dragSub, ns, end - ns, true);
                break;
            }
            case Drag.FxEnd:
            {
                GetFx(s, dragLane, dragSub, out float fs, out _);
                // Looks, pop-ups and scans can be stretched past the shot's end into the next shot(s)
                float maxEnd = CanCarry(dragLane) ? Mathf.Max(s.duration, Total() - shotStart) : s.duration;
                float ne = Mathf.Clamp(SnapTime(t) - shotStart, fs + 0.1f, maxEnd);
                SetFx(s, dragLane, dragSub, fs, ne - fs, true);
                break;
            }
        }
        Changed();
    }

    // Snaps a time to nearby edges: shot boundaries, the playhead and other overlay bars (hold Alt to turn off)
    const float SnapPixels = 8f;

    float SnapTime(float t, object skip = null, bool shotEdges = true)
    {
        snapLine = -1f;
        if (Event.current != null && Event.current.alt) return Snap(t);
        float best = t, bestPx = SnapPixels;
        TrySnap(t, time, ref best, ref bestPx); // playhead
        if (shotEdges)
        {
            float acc = 0f;
            TrySnap(t, 0f, ref best, ref bestPx);
            foreach (Shot sh in c.shots) { acc += sh.duration; TrySnap(t, acc, ref best, ref bestPx); }
        }
        for (int j = 0; c.images != null && j < c.images.Count; j++)
        {
            if (c.images[j] == null || ReferenceEquals(c.images[j], skip)) continue;
            TrySnap(t, c.images[j].start, ref best, ref bestPx);
            TrySnap(t, c.images[j].start + c.images[j].duration, ref best, ref bestPx);
        }
        for (int j = 0; c.sounds != null && j < c.sounds.Count; j++)
        {
            if (c.sounds[j] == null || ReferenceEquals(c.sounds[j], skip)) continue;
            TrySnap(t, c.sounds[j].start, ref best, ref bestPx);
            TrySnap(t, c.sounds[j].start + CinematicController.LengthOf(c.sounds[j]), ref best, ref bestPx);
        }

        if (bestPx < SnapPixels)
        {
            snapLine = best;
            return best;
        }
        return Snap(t);
    }

    void TrySnap(float t, float candidate, ref float best, ref float bestPx)
    {
        float d = Mathf.Abs(X(candidate) - X(t));
        if (d < bestPx) { bestPx = d; best = candidate; }
    }

    // Moving a whole bar: snaps its start, or else its end, to a nearby edge
    float SnapSpan(float start, float length, object skip)
    {
        float s0 = SnapTime(start, skip);
        if (snapLine >= 0f) return s0;
        float s1 = SnapTime(start + length, skip);
        if (snapLine >= 0f) return s1 - length;
        return Snap(start);
    }

    static Tab TabForLane(int lane)
    {
        switch (lane)
        {
            case 0: return Tab.Camera;
            case LookLane: return Tab.Camera;
            case MotionLane: return Tab.Motion;
            case AudioLane: return Tab.Audio;
            case PopupLane: return Tab.Popups;
            case ImageLane: return Tab.Overlays;
            case ScanLane: return Tab.Scan;
            case EventLane: return Tab.Events;
            default: return Tab.Camera;
        }
    }

    // ---------------------------------------------------------------- Right-click menu

    void Safe(Action a)
    {
        try { a(); }
        catch (ExitGUIException) { } // some actions end the GUI pass early; harmless outside OnGUI
        viewerDirty = true;
        Repaint();
    }

    void ShowTimelineMenu(Vector2 m, float trackY, float laneY)
    {
        float t = Mathf.Clamp(TimeAt(m.x), 0f, Mathf.Max(0f, Total()));
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Move playhead here"), false, () => Safe(() => SetTime(t)));
        if (CanCopy()) menu.AddItem(new GUIContent($"Copy the selected {ClipName(selItem.GetType())}   (Ctrl+C)"), false, () => Safe(CopySelected));
        if (clipJson != null)
            menu.AddItem(new GUIContent($"Paste {ClipName(clipType)} here   (Ctrl+V pastes at the playhead)"), false, () => Safe(() => { SetTime(t); Paste(); }));

        int shot = c.shots.Count > 0 ? ShotAt(t, out _) : -1;
        float shotStart = shot >= 0 ? StartOf(shot) : 0f;
        Shot s = shot >= 0 ? c.shots[shot] : null;
        bool inTrack = m.y >= trackY && m.y < trackY + TrackH;
        int lane = LaneAt(m.y - laneY);
        string shotName = s != null ? $"shot {shot + 1} ({s.name})" : "";

        if (lane == EventLane && s != null)
        {
            float evAt = t - shotStart;
            menu.AddSeparator("");
            menu.AddItem(new GUIContent($"Add event here ({evAt:0.##}s into {shotName})"), false, () => Safe(() => AddEvent(shot, evAt)));
        }
        if (inTrack || lane < 0)
        {
            menu.AddSeparator("");
            if (s != null)
            {
                float evAt = t - shotStart;
                menu.AddItem(new GUIContent($"Add event here ({evAt:0.##}s into {shotName})"), false, () => Safe(() => AddEvent(shot, evAt)));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Insert shot before {shotName} (from Scene View)"), false, () => Safe(() => InsertShot(shot)));
                menu.AddItem(new GUIContent($"Insert shot after {shotName} (from Scene View)"), false, () => Safe(() => InsertShot(shot + 1)));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Capture {shotName} from Scene View"), false, () => Safe(() => { selected = shot; CapturePoint(shot); }));
                menu.AddItem(new GUIContent($"Duplicate {shotName}"), false, () => Safe(() => { selected = shot; DuplicateShot(); }));
                menu.AddItem(new GUIContent($"Delete {shotName}"), false, () => Safe(() => { selected = shot; DeleteShot(); }));
            }
            menu.AddItem(new GUIContent("Add shot from Scene View (at the end)"), false, () => Safe(() => AddShot(true)));
        }
        else if (lane == AudioLane)
        {
            int hit = -1;
            for (int j = (c.sounds != null ? c.sounds.Count : 0) - 1; j >= 0 && hit < 0; j--)
                if (c.sounds[j] != null && t >= c.sounds[j].start && t <= c.sounds[j].start + CinematicController.LengthOf(c.sounds[j])) hit = j;
            menu.AddSeparator("");
            if (hit >= 0)
            {
                var snd = c.sounds[hit];
                if (s != null)
                    menu.AddItem(new GUIContent($"'{snd.name}': last exactly {shotName}"), false, () => Safe(() =>
                    {
                        Record("Fit Sound To Shot");
                        snd.start = shotStart;
                        snd.duration = s.duration;
                        snd.loop = snd.clip != null && snd.clip.length - snd.clipStart < s.duration; // loop if it's too short
                        Changed();
                    }));
                menu.AddItem(new GUIContent($"'{snd.name}': start at the playhead"), false, () => Safe(() =>
                {
                    Record("Move Sound");
                    snd.start = time;
                    Changed();
                }));
                menu.AddItem(new GUIContent($"Delete '{snd.name}'"), false, () => Safe(() =>
                {
                    Record("Remove Sound");
                    c.sounds.Remove(snd);
                    Changed();
                }));
            }
            else menu.AddDisabledItem(new GUIContent("Drag audio files here from the Project window"));
        }
        else if (lane == ImageLane)
        {
            int hit = -1;
            for (int j = (c.images != null ? c.images.Count : 0) - 1; j >= 0 && hit < 0; j--)
                if (c.images[j] != null && t >= c.images[j].start && t <= c.images[j].start + c.images[j].duration) hit = j;
            if (hit >= 0 && s != null)
            {
                var im = c.images[hit];
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"'{im.name}': last exactly {shotName}"), false, () => Safe(() =>
                {
                    Record("Fit Overlay To Shot");
                    im.start = shotStart;
                    im.duration = s.duration;
                    Changed();
                }));
                menu.AddItem(new GUIContent($"'{im.name}': start at the playhead"), false, () => Safe(() =>
                {
                    Record("Move Overlay");
                    im.start = time;
                    Changed();
                }));
                menu.AddItem(new GUIContent($"Delete '{im.name}'"), false, () => Safe(() =>
                {
                    Record("Remove Overlay");
                    c.images.Remove(im);
                    Changed();
                }));
            }
            menu.AddSeparator("");
            foreach (CinematicController.OverlayKind kind in Enum.GetValues(typeof(CinematicController.OverlayKind)))
            {
                var k = kind;
                string label = k == CinematicController.OverlayKind.ProgressBar ? "Progress bar"
                             : k == CinematicController.OverlayKind.UIObject ? "UI object" : k.ToString();
                menu.AddItem(new GUIContent($"Add overlay here/{label}"), false, () => Safe(() =>
                {
                    selected = shot;
                    tab = Tab.Overlays;
                    AddOverlay(k, null, null, t);
                }));
            }
        }
        else if (s != null && lane == PopupLane)
        {
            menu.AddSeparator("");
            for (int j = 0; s.popups != null && j < s.popups.Count; j++)
            {
                var pp = s.popups[j];
                menu.AddItem(new GUIContent($"'{pp.name}': last the whole shot"), false, () => Safe(() =>
                {
                    Record("Fit Pop-up To Shot");
                    pp.appearAt = 0f;
                    pp.duration = 0f;
                    Changed();
                }));
            }
            menu.AddItem(new GUIContent($"Add pop-up to {shotName} from Scene View"), false, () => Safe(() =>
            {
                selected = shot;
                tab = Tab.Popups;
                AddPopup(s);
            }));
        }
        else if (s != null && lane == ScanLane)
        {
            menu.AddSeparator("");
            bool on = s.scan != null && s.scan.enabled;
            float local = Snap(t - shotStart);
            if (!on)
                menu.AddItem(new GUIContent($"Start scanning here in {shotName}"), false, () => Safe(() =>
                {
                    Record("Turn On Scan");
                    if (s.scan == null || (s.scan.color.maxColorComponent <= 0f && s.scan.randomCount == 0)) s.scan = new CinematicController.Scan();
                    s.scan.enabled = true;
                    s.scan.start = local;
                    s.scan.duration = 0f;
                    selected = shot;
                    tab = Tab.Scan;
                    Changed();
                }));
            else
            {
                menu.AddItem(new GUIContent("Scan: last the whole shot"), false, () => Safe(() =>
                {
                    Record("Fit Scan To Shot");
                    s.scan.start = 0f;
                    s.scan.duration = 0f;
                    Changed();
                }));
                menu.AddItem(new GUIContent($"Turn off scanning in {shotName}"), false, () => Safe(() =>
                {
                    Record("Turn Off Scan");
                    s.scan.enabled = false;
                    Changed();
                }));
            }
        }
        else if (s != null && lane == MotionLane)
        {
            menu.AddSeparator("");
            float local = Snap(t - shotStart);
            for (int j = 0; s.motions != null && j < s.motions.Count; j++)
            {
                var mo = s.motions[j];
                if (mo == null || local < mo.start || local > CinematicController.MotionEnd(mo, s.duration)) continue;
                menu.AddItem(new GUIContent($"'{mo.name}': last the whole shot"), false, () => Safe(() =>
                {
                    Record("Fit Motion To Shot");
                    mo.start = 0f;
                    mo.duration = 0f;
                    Changed();
                }));
                menu.AddItem(new GUIContent($"Delete '{mo.name}'"), false, () => Safe(() =>
                {
                    Record("Remove Motion");
                    s.motions.Remove(mo);
                    Changed();
                }));
            }
            foreach (string preset in Presets)
            {
                string pr = preset;
                menu.AddItem(new GUIContent($"Add motion here/{pr}"), false, () => Safe(() =>
                {
                    selected = shot;
                    tab = Tab.Motion;
                    AddMotionPreset(s, pr, local);
                }));
            }
        }
        else if (s != null && lane == LookLane)
        {
            menu.AddSeparator("");
            float local = Snap(t - shotStart);
            if (s.feed != null && s.feed.look)
            {
                menu.AddItem(new GUIContent("Camera look: last the whole shot"), false, () => Safe(() =>
                {
                    Record("Fit Look To Shot");
                    s.lookStart = 0f;
                    s.lookDuration = 0f;
                    Changed();
                }));
                menu.AddItem(new GUIContent("Camera look: start here"), false, () => Safe(() =>
                {
                    Record("Move Look");
                    float end = s.lookDuration > 0f ? s.lookStart + s.lookDuration : 0f;
                    s.lookStart = local;
                    s.lookDuration = end > local ? end - local : 0f;
                    Changed();
                }));
                menu.AddItem(new GUIContent("Camera look: end here"), false, () => Safe(() =>
                {
                    Record("Move Look");
                    if (local > s.lookStart) s.lookDuration = local - s.lookStart;
                    Changed();
                }));
                menu.AddItem(new GUIContent("Turn the camera look off"), false, () => Safe(() =>
                {
                    Record("Turn Off Camera Look");
                    s.feed.look = false;
                    Changed();
                }));
            }
            else
                menu.AddItem(new GUIContent($"Turn on a camera look from here (CCTV) in {shotName}"), false, () => Safe(() =>
                {
                    Record("Turn On Camera Look");
                    if (s.feed == null) s.feed = new CinematicController.Feed();
                    ApplyLookPreset(s.feed, "CCTV");
                    s.lookStart = local;
                    s.lookDuration = 0f;
                    selected = shot;
                    tab = Tab.Camera;
                    Changed();
                }));
        }
        else if (s != null && lane == 0 && s.animateFov)
        {
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("FOV animation: last the whole shot"), false, () => Safe(() =>
            {
                Record("Fit FOV To Shot");
                s.fovStart = 0f;
                s.fovDuration = 0f;
                Changed();
            }));
        }
        menu.ShowAsContext();
    }

    static float Snap(float v) => Mathf.Round(v * 20f) / 20f;

    static CinematicController.Motion MotionAt(Shot s, int i) =>
        s.motions != null && i >= 0 && i < s.motions.Count ? s.motions[i] : null;

    static CinematicController.Popup PopupAt(Shot s, int i) =>
        s.popups != null && i >= 0 && i < s.popups.Count ? s.popups[i] : null;

    static CinematicController.ImageLayer ImageAt(Shot s, int i) =>
        s.images != null && i >= 0 && i < s.images.Count ? s.images[i] : null;

    static bool GetFx(Shot s, int lane, int sub, out float start, out float len)
    {
        bool on;
        float st, du;
        switch (lane)
        {
            case 0: on = s.animateFov; st = s.fovStart; du = s.fovDuration; break;
            case LookLane:
                on = s.feed != null && s.feed.look;
                st = s.lookStart;
                du = s.lookDuration;
                break;
            case MotionLane:
                var mo = MotionAt(s, sub);
                on = mo != null;
                st = mo != null ? mo.start : 0f;
                du = mo != null ? mo.duration : 0f;
                break;
            case PopupLane:
                var pp = PopupAt(s, sub);
                on = pp != null;
                st = pp != null ? pp.appearAt : 0f;
                du = pp != null ? pp.duration : 0f;
                break;
            case ImageLane:
                var im = ImageAt(s, sub);
                on = im != null;
                st = im != null ? im.appearAt : 0f;
                du = im != null ? im.duration : 0f;
                break;
            default:
                on = s.scan != null && s.scan.enabled;
                st = s.scan != null ? s.scan.start : 0f;
                du = s.scan != null ? s.scan.duration : 0f;
                break;
        }
        start = Mathf.Clamp(st, 0f, s.duration);
        // Looks, pop-ups and scans can run past the shot's end into the next shot(s)
        len = du > 0f ? (CanCarry(lane) ? du : Mathf.Min(du, s.duration - start)) : s.duration - start;
        return on;
    }

    // Lanes whose bars can be dragged past the shot's end, carrying on into the next shot(s)
    static bool CanCarry(int lane) => lane == LookLane || lane == PopupLane || lane == ScanLane;

    // len reaching the end of the shot is stored as 0 ("until the end") when allowed
    static void SetFx(Shot s, int lane, int sub, float start, float len, bool allowToEnd)
    {
        float du = allowToEnd && Mathf.Abs(start + len - s.duration) <= 0.001f ? 0f : len; // exactly the shot's end = "until the end"
        switch (lane)
        {
            case 0: s.fovStart = start; s.fovDuration = du; break;
            case LookLane: s.lookStart = start; s.lookDuration = du; break;
            case MotionLane:
                var mo = MotionAt(s, sub);
                if (mo != null) { mo.start = start; mo.duration = du; }
                break;
            case PopupLane:
                var pp = PopupAt(s, sub);
                if (pp != null) { pp.appearAt = start; pp.duration = du; }
                break;
            case ImageLane:
                var im = ImageAt(s, sub);
                if (im != null) { im.appearAt = start; im.duration = du; }
                break;
            default:
                if (s.scan != null) { s.scan.start = start; s.scan.duration = du; }
                break;
        }
    }

    static string FxLabel(Shot s, int lane, int sub)
    {
        switch (lane)
        {
            case 0: return $"{s.fov:0}° → {s.endFov:0}°";
            case LookLane: return "Camera look" + (s.lookFade > 0f ? $" (fade {s.lookFade:0.##}s)" : "");
            case MotionLane: return MotionAt(s, sub)?.name ?? "";
            case PopupLane: return PopupAt(s, sub)?.name ?? "";
            case ImageLane: return ImageAt(s, sub)?.name ?? "";
            default:
                return s.scan == null ? "" : s.scan.mode == CinematicController.ScanMode.Random ? "Scanning"
                     : s.scan.mode == CinematicController.ScanMode.Targets ? "Targets" : "Search → lock";
        }
    }

    static string Vec(Vector3 v, string unit)
    {
        var parts = new List<string>();
        if (v.x != 0f) parts.Add($"X {v.x:0.##}");
        if (v.y != 0f) parts.Add($"Y {v.y:0.##}");
        if (v.z != 0f) parts.Add($"Z {v.z:0.##}");
        return string.Join("  ", parts) + " " + unit;
    }

    // ---------------------------------------------------------------- Details panel

    void DrawDetails(Rect r)
    {
        GUILayout.BeginArea(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 10f));
        panelWidth = r.width - 12f;
        rowInset = 0f;

        float oldLabel = EditorGUIUtility.labelWidth;
        bool oldWide = EditorGUIUtility.wideMode;
        EditorGUIUtility.labelWidth = 120f;
        EditorGUIUtility.wideMode = true;

        if (selected < 0)
        {
            GUILayout.Space(30f);
            GUILayout.Label("No shots yet.\nFly the Scene view to your first shot and press  + Shot from Scene View.", emptyStyle);
        }
        else
        {
            Shot s = c.shots[selected];
            DrawHeader(s);
            DrawTabs(s);
            DrawCarriedIn(selected);
            detailScroll = GUILayout.BeginScrollView(detailScroll);
            switch (tab)
            {
                case Tab.Camera:
                    bool wide = r.width > 820f;
                    if (wide)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.BeginVertical(GUILayout.Width((r.width - 40f) * 0.5f));
                    }
                    CardPoint(s);
                    CardTransition(s);
                    if (wide)
                    {
                        GUILayout.EndVertical();
                        GUILayout.BeginVertical();
                    }
                    CardAim(s);
                    CardLens(s);
                    CardFades(s);
                    if (wide)
                    {
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                    }
                    break;
                case Tab.Motion: CardMotion(s); break;
                case Tab.Multiview: CardMultiview(s); break;
                case Tab.Popups: CardPopups(s); break;
                case Tab.Scan: CardScan(s); break;
                case Tab.Overlays: CardImages(s); break;
                case Tab.Audio: CardAudio(s); break;
                default:
                    showEvents = true;
                    CardEvents(s);
                    break;
            }
            GUILayout.EndScrollView();
        }

        EditorGUIUtility.labelWidth = oldLabel;
        EditorGUIUtility.wideMode = oldWide;
        GUILayout.EndArea();
    }

    // Tabs for the shot's settings; a dot or count shows what's in use
    void DrawTabs(Shot s)
    {
        int views = s.extraViews != null ? s.extraViews.Count : 0;
        int pops = s.popups != null ? s.popups.Count : 0;
        int overlays = 0;
        for (int j = 0; c.images != null && j < c.images.Count; j++)
            if (ImageInShot(c.images[j], selected)) overlays++;
        int events = (s.onStart != null ? s.onStart.GetPersistentEventCount() : 0) + (s.onEnd != null ? s.onEnd.GetPersistentEventCount() : 0)
                   + (s.events != null ? s.events.Count : 0);
        int motionCount = s.motions != null ? s.motions.Count : 0;
        bool scan = s.scan != null && s.scan.enabled;

        int soundCount = 0;
        float a0 = StartOf(selected), a1 = a0 + s.duration;
        for (int j = 0; c.sounds != null && j < c.sounds.Count; j++)
            if (c.sounds[j] != null && c.sounds[j].start < a1 && c.sounds[j].start + CinematicController.LengthOf(c.sounds[j]) > a0) soundCount++;

        var labels = new[]
        {
            IconLabel("Camera", "Camera Icon", "Camera point, move-in, aim, lens, fades and camera feed look"),
            IconLabel(motionCount > 0 ? $"Motion ({motionCount})" : "Motion", "MoveTool", "Camera moves: pans, pushes, orbits, shakes"),
            IconLabel(views > 0 ? $"Multiview ({views + 1})" : "Multiview", "GridLayoutGroup Icon", "Several cameras at once in a grid"),
            IconLabel(pops > 0 ? $"Pop-ups ({pops})" : "Pop-ups", "UnityEditor.GameView", "Camera windows on top of the shot"),
            IconLabel(scan ? "Scan ●" : "Scan", "ViewToolZoom", "Boxes searching and locking onto things"),
            IconLabel(overlays > 0 ? $"Overlays ({overlays})" : "Overlays", "Image Icon", "Images, text, waveform, progress bar, your own UI"),
            IconLabel(soundCount > 0 ? $"Audio ({soundCount})" : "Audio", "AudioSource Icon", "Sounds playing during this shot"),
            IconLabel(events > 0 ? $"Events ({events})" : "Events", "EventSystem Icon", "Events during the shot, and on its start / end")
        };
        GUILayout.Space(4f);
        EditorGUIUtility.SetIconSize(new Vector2(16f, 16f));
        tab = (Tab)GUILayout.Toolbar((int)tab, labels, GUILayout.Height(28f));
        EditorGUIUtility.SetIconSize(Vector2.zero);
        GUILayout.Space(2f);
    }

    void DrawHeader(Shot s)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(6f);
        GUILayout.Label(new GUIContent($"{selected + 1}", "The shot under the playhead. Everything in this panel edits this shot; " +
                                       "move the playhead (or click a shot) to edit another one."),
                        numberStyle, GUILayout.Width(26f), GUILayout.Height(28f));

        EditorGUI.BeginChangeCheck();
        string n = EditorGUILayout.TextField(s.name, nameStyle, GUILayout.Height(28f), GUILayout.MinWidth(80f));
        if (EditorGUI.EndChangeCheck()) { Record("Rename Shot"); s.name = n; Changed(); }

        bool narrowHeader = panelWidth < 470f;
        if (narrowHeader)
        {
            GUILayout.Space(6f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Space(38f);
        }
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(selected == 0))
            if (GUILayout.Button(IconOr("Earlier", "◀ Earlier", "Animation.PrevKey", "Move this shot one place earlier"), EditorStyles.miniButtonLeft, GUILayout.Height(22f))) MoveShot(-1);
        using (new EditorGUI.DisabledScope(selected == c.shots.Count - 1))
            if (GUILayout.Button(IconOr("Later", "Later ▶", "Animation.NextKey", "Move this shot one place later"), EditorStyles.miniButtonMid, GUILayout.Height(22f))) MoveShot(1);
        if (GUILayout.Button(IconOr("Duplicate", "Duplicate", "TreeEditor.Duplicate"), EditorStyles.miniButtonMid, GUILayout.Height(22f))) DuplicateShot();
        if (GUILayout.Button(IconOr("Delete", "Delete", "TreeEditor.Trash"), EditorStyles.miniButtonRight, GUILayout.Height(22f))) DeleteShot();
        GUILayout.Space(6f);
        GUILayout.EndHorizontal();
    }

    void CardPoint(Shot s)
    {
        BeginCard("CAMERA POINT", "Camera Icon");

        // What the shot shows: the camera, or (for UI-style shots) just a colour or an image
        EditorGUIUtility.SetIconSize(new Vector2(14f, 14f));
        int bgNow = (int)s.background;
        int bg = GUILayout.Toolbar(bgNow, new[]
        {
            IconLabel("Camera", "Camera Icon", "The camera's view of the scene"),
            IconLabel("Colour", "Material Icon", "No camera: a plain colour behind the overlays"),
            IconLabel("Image", "Image Icon", "No camera: an image behind the overlays")
        }, GUILayout.Height(24f));
        EditorGUIUtility.SetIconSize(Vector2.zero);
        if (bg != bgNow) { Record("Edit Shot Background"); s.background = (CinematicController.ShotBackground)bg; Changed(); }
        if (s.background != CinematicController.ShotBackground.Camera)
        {
            EditorGUI.BeginChangeCheck();
            Color bgCol = EditorGUILayout.ColorField(s.background == CinematicController.ShotBackground.Image ? "Behind the image" : "Colour", s.backgroundColor);
            Texture bgTex = s.background == CinematicController.ShotBackground.Image
                ? (Texture)EditorGUILayout.ObjectField("Image", s.backgroundImage, typeof(Texture), false) : s.backgroundImage;
            if (EditorGUI.EndChangeCheck()) { Record("Edit Shot Background"); s.backgroundColor = bgCol; s.backgroundImage = bgTex; Changed(); }
            Hint("No camera in this shot: overlays, text, scan and sound play on top of this. In the game, the real camera stops drawing meanwhile.");
            GUILayout.Space(4f);
        }

        EditorGUI.BeginChangeCheck();
        var point = (Transform)EditorGUILayout.ObjectField("Point", s.point, typeof(Transform), true);
        float dur = EditorGUILayout.FloatField("Length (s)", s.duration);
        if (EditorGUI.EndChangeCheck())
        {
            Record("Edit Shot");
            var anchors = CaptureAnchors();
            s.point = point;
            s.duration = Mathf.Max(0.1f, dur);
            RestoreAnchors(anchors);
            Changed();
        }

        if (s.point == null) Hint("No camera point yet. Fly the Scene view to the shot and capture it.");

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = Accent;
        if (GUILayout.Button(IconOr("Capture from Scene View", "Capture from Scene View", "Camera Icon", "Frame this shot like the Scene view"), GUILayout.Height(24f))) CapturePoint(selected);
        GUI.backgroundColor = old;
        using (new EditorGUI.DisabledScope(s.point == null))
        {
            if (GUILayout.Button(IconOr("Look through", "Look through", "ViewToolOrbit", "Move the Scene view to this camera"), GUILayout.Height(24f)) && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.AlignViewToObject(s.point);
            if (GUILayout.Button(IconOr("Select", "Select", "Search Icon", "Select the camera point in the scene"), GUILayout.Height(24f), GUILayout.Width(74f)))
                Selection.activeTransform = s.point;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        if (s.feed == null) s.feed = new CinematicController.Feed();
        DrawFeed(s.feed, selected + "/main");
        if (s.feed.look)
        {
            EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal();
            float ls = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Look from (s)", "When the look starts in this shot"), s.lookStart));
            GUILayout.EndHorizontal();
            float ld = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Look lasts (s)", "0 = until the end of the shot. Longer than what's left of the shot: it carries on into the next shot(s)"), s.lookDuration));
            float lf = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Look fade (s)", "Blend the look in (and out) instead of switching it on"), s.lookFade));
            if (EditorGUI.EndChangeCheck()) { Record("Edit Look Timing"); s.lookStart = ls; s.lookDuration = ld; s.lookFade = lf; Changed(); }
            Hint("The look has its own bar on the Look lane: drag it to make it start halfway or stop early. " +
                 "Overlays, text and the scan choose for themselves whether they get it: see Camera effect on each one.");
        }
        EndCard();
    }

    void CardTransition(Shot s)
    {
        BeginCard("GETTING HERE", "MoveTool");

        EditorGUI.BeginChangeCheck();
        int mode = GUILayout.Toolbar((int)s.transition, new[]
        {
            new GUIContent("Cut", "Jump straight to this shot"),
            new GUIContent("Move", "The camera flies from the previous shot to this one"),
            new GUIContent("Crossfade", "The previous shot dissolves into this one: camera, looks, multiview, overlays, everything")
        }, GUILayout.Height(24f));
        float td = s.transitionDuration;
        AnimationCurve ease = s.transitionEase ?? new AnimationCurve();
        AnimationCurve preset = null;

        if (mode == 1)
        {
            td = Mathf.Max(0.01f, EditorGUILayout.FloatField("Move time (s)", s.transitionDuration));
            ease = EditorGUILayout.CurveField("Easing", ease);
            preset = EasePresets();
            Hint(selected == 0
                ? "The first shot moves from wherever the camera starts."
                : "Drag the white line inside the shot block to change the move time.");
        }
        else if (mode == 2)
        {
            td = Mathf.Max(0.01f, EditorGUILayout.FloatField(new GUIContent("Blend time (s)",
                     "How long the previous shot takes to dissolve away"), s.transitionDuration));
            ease = EditorGUILayout.CurveField("Easing", ease);
            preset = EasePresets();
            Hint(selected == 0
                ? "The first shot has nothing before it, so it just starts (works like a cut)."
                : "The previous shot, held on its last moment, fades out over this one. Great for softening a jump " +
                  "from a CCTV look to a clean one. Drag the white line inside the shot block to change the blend time.");
        }
        else
        {
            Hint("Jumps straight to this shot.");
        }

        if (EditorGUI.EndChangeCheck() || preset != null)
        {
            Record("Edit Transition");
            s.transition = (Transition)mode;
            s.transitionDuration = td;
            s.transitionEase = preset ?? ease;
            Changed();
        }

        EndCard();
    }

    void CardAim(Shot s)
    {
        BeginCard("AIM", "RotateTool");

        int aim = s.lookAt != null || pendingLookAt == selected ? 2 : s.overrideRotation ? 1 : 0;
        int newAim = GUILayout.Toolbar(aim, new[] { "Point's rotation", "Custom", "Look at target" }, GUILayout.Height(24f));
        if (newAim != aim)
        {
            Record("Change Aim");
            s.lookAt = null;
            s.overrideRotation = newAim == 1;
            pendingLookAt = newAim == 2 ? selected : -1;
            if (newAim == 1 && s.rotation == Vector3.zero && s.point != null) s.rotation = s.point.eulerAngles;
            Changed();
        }

        EditorGUI.BeginChangeCheck();
        Vector3 rot = s.rotation;
        Transform look = s.lookAt;
        if (newAim == 0) Hint("Uses the camera point's rotation.");
        else if (newAim == 1) rot = EditorGUILayout.Vector3Field("Rotation", s.rotation);
        else
        {
            look = (Transform)EditorGUILayout.ObjectField("Target", s.lookAt, typeof(Transform), true);
            Hint("Keeps the camera aimed at this object for the whole shot.");
        }
        if (EditorGUI.EndChangeCheck()) { Record("Edit Aim"); s.rotation = rot; s.lookAt = look; Changed(); }

        EndCard();
    }

    void CardLens(Shot s)
    {
        BeginCard("LENS", "ViewToolZoom");

        EditorGUI.BeginChangeCheck();
        float fov = EditorGUILayout.Slider("Field of view", s.fov, 1f, 179f);
        int mode = GUILayout.Toolbar(s.animateFov ? 1 : 0, new[] { "Fixed", "Animate" }, GUILayout.Height(22f));
        float end = s.endFov, st = s.fovStart, du = s.fovDuration;
        AnimationCurve ease = s.fovEase ?? new AnimationCurve();
        AnimationCurve preset = null;

        if (mode == 1)
        {
            end = EditorGUILayout.Slider("End FOV", s.endFov, 1f, 179f);
            st = Mathf.Max(0f, EditorGUILayout.FloatField("Starts at (s)", s.fovStart));
            du = Mathf.Max(0f, EditorGUILayout.FloatField("Lasts (s)", s.fovDuration));
            ease = EditorGUILayout.CurveField("Easing", ease);
            preset = EasePresets();
            Hint("Lasts 0 = until the shot ends. You can also drag the FOV bar on the timeline.");
        }

        if (EditorGUI.EndChangeCheck() || preset != null)
        {
            Record("Edit Lens");
            s.fov = fov;
            s.animateFov = mode == 1;
            s.endFov = end;
            s.fovStart = st;
            s.fovDuration = du;
            s.fovEase = preset ?? ease;
            Changed();
        }

        EndCard();
    }

    void CardMotion(Shot s)
    {
        BeginCard("MOTION", "MoveTool");
        if (s.motions == null) s.motions = new List<CinematicController.Motion>();

        // Presets add a clip at the playhead (a clip of the same kind still running there is ended where the new one starts)
        GUILayout.Label("Add at the playhead", EditorStyles.miniBoldLabel);
        string picked = null;
        int pk = WrapButtons(Contents(Presets), 20f);
        if (pk >= 0) picked = Presets[pk];
        if (picked != null)
        {
            float shotStart = StartOf(selected);
            float local = time >= shotStart && time < shotStart + s.duration ? Snap(time - shotStart) : 0f;
            AddMotionPreset(s, picked, local);
        }
        GUILayout.Space(6f);

        if (s.motions.Count > 3) CollapseAllButtons(s.motions);
        int remove = -1;
        for (int j = 0; j < s.motions.Count; j++)
        {
            var mo = s.motions[j];
            if (mo == null) continue;
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            FoldArrow(mo, s.motions.Count);
            GUILayout.Label(Icon(MotionIcon(mo.kind)), GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.TextField(mo.name, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck()) { Record("Rename Motion"); mo.name = n; Changed(); }
            if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = j;
            GUILayout.EndHorizontal();
            if (!IsOpen(mo, s.motions.Count))
            {
                GUILayout.Label($"   {mo.kind}  ·  {mo.start:0.##}s → " +
                                (mo.duration > 0f ? $"{mo.start + mo.duration:0.##}s" : "end of shot") + (mo.smooth ? "  ·  smooth" : "") +
                                (mo.endAtPoint && mo.kind != CinematicController.MotionKind.Shake ? "  ·  ends at the point" : ""), hintStyle);
                GUILayout.EndVertical();
                continue;
            }

            EditorGUI.BeginChangeCheck();
            var kind = (CinematicController.MotionKind)GUILayout.Toolbar((int)mo.kind, new[] { "Rotate / pan", "Move (drift)", "Orbit", "Shake" });
            Vector3 speed = mo.speed;
            bool local = mo.local, fades = mo.shakeFades;
            float orbit = mo.orbitSpeed, amount = mo.shakeAmount, sspeed = mo.shakeSpeed;
            switch (kind)
            {
                case CinematicController.MotionKind.Rotate:
                    speed = EditorGUILayout.Vector3Field("Speed (°/s)", mo.speed);
                    Hint("Y pans (horizon stays level), X tilts, Z rolls.");
                    break;
                case CinematicController.MotionKind.Drift:
                    speed = EditorGUILayout.Vector3Field("Speed (units/s)", mo.speed);
                    local = GUILayout.Toolbar(mo.local ? 0 : 1, new[] { "Relative to camera", "World axes" }) == 0;
                    break;
                case CinematicController.MotionKind.Orbit:
                    orbit = EditorGUILayout.FloatField("Speed (°/s)", mo.orbitSpeed);
                    if (s.lookAt == null) Hint("Needs a Look at target. Set one in the Camera tab under Aim.");
                    break;
                default:
                    amount = EditorGUILayout.Slider("Strength (°)", mo.shakeAmount, 0.01f, 10f);
                    sspeed = EditorGUILayout.Slider("Speed", mo.shakeSpeed, 0.2f, 25f);
                    fades = EditorGUILayout.Toggle("Dies down (impact)", mo.shakeFades);
                    break;
            }
            float st = Mathf.Max(0f, EditorGUILayout.FloatField("Starts at (s)", mo.start));
            float du = Mathf.Max(0f, EditorGUILayout.FloatField("Lasts (s)", mo.duration));
            bool smooth = kind != CinematicController.MotionKind.Shake && EditorGUILayout.Toggle("Smooth start & stop", mo.smooth);
            bool endAt = mo.endAtPoint;
            if (kind != CinematicController.MotionKind.Shake)
            {
                endAt = GUILayout.Toolbar(mo.endAtPoint ? 1 : 0, new[]
                {
                    new GUIContent("Starts at the point", "Begins exactly on the camera point and moves away from it"),
                    new GUIContent("Ends at the point", "Begins turned / moved the opposite way and arrives exactly on the camera point")
                }) == 1;
                if (endAt)
                    Hint(kind == CinematicController.MotionKind.Rotate
                        ? "Comes in from the other side: e.g. a pan right starts turned left and lands on your framing when the clip ends."
                        : "Comes in from the other side and lands on your camera point when the clip ends.");
            }
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Motion");
                mo.kind = kind;
                mo.speed = speed;
                mo.local = local;
                mo.orbitSpeed = orbit;
                mo.shakeAmount = amount;
                mo.shakeSpeed = sspeed;
                mo.shakeFades = fades;
                mo.start = st;
                mo.duration = du;
                mo.smooth = kind == CinematicController.MotionKind.Shake ? mo.smooth : smooth;
                mo.endAtPoint = endAt;
                Changed();
            }
            GUILayout.EndVertical();
        }
        if (remove >= 0)
        {
            Record("Remove Motion");
            s.motions.RemoveAt(remove);
            Changed();
            GUIUtility.ExitGUI();
        }

        Hint(s.motions.Count == 0
            ? "Add camera moves with the buttons above. They add up, so you can chain them (pan left, then pan right) or " +
              "combine them (push in while shaking)."
            : "Clips add up. Lasts 0 = until the shot ends. Drag them on the Motion lane; overlapping ones stack on rows.");
        EndCard();
    }

    static string MotionIcon(CinematicController.MotionKind kind)
    {
        switch (kind)
        {
            case CinematicController.MotionKind.Drift: return "MoveTool";
            case CinematicController.MotionKind.Orbit: return "ViewToolOrbit";
            case CinematicController.MotionKind.Shake: return "Animator Icon";
            default: return "RotateTool";
        }
    }

    void CardEvents(Shot s)
    {
        if (s.events == null) s.events = new List<CinematicController.ShotEvent>();
        var so = new SerializedObject(c);
        SerializedProperty shot = so.FindProperty("shots").GetArrayElementAtIndex(selected);

        // Timed events: like Animation events, at a moment in the shot
        BeginCard("EVENTS DURING THE SHOT", "Animation.AddEvent");
        if (s.events.Count > 3) CollapseAllButtons(s.events);
        SerializedProperty list = shot.FindPropertyRelative("events");
        int remove = -1;
        // Listed in time order
        var order = new List<int>();
        for (int j = 0; j < s.events.Count; j++) order.Add(j);
        order.Sort((x, y) => s.events[x].time.CompareTo(s.events[y].time));
        foreach (int j in order)
        {
            var ev = s.events[j];
            if (ev == null || j >= list.arraySize) continue;
            SerializedProperty p = list.GetArrayElementAtIndex(j);
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            FoldArrow(ev, s.events.Count);
            GUILayout.Label(Icon("Animation.EventMarker"), GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUILayout.PropertyField(p.FindPropertyRelative("name"), GUIContent.none);
            EditorGUIUtility.labelWidth = 14f;
            EditorGUILayout.PropertyField(p.FindPropertyRelative("time"), new GUIContent("@", "Seconds into the shot"), GUILayout.Width(70f));
            EditorGUIUtility.labelWidth = 120f;
            if (GUILayout.Button(new GUIContent("Go", "Move the playhead to this event"), EditorStyles.miniButton, GUILayout.Width(28f)))
                SetTime(StartOf(selected) + ev.time);
            if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = j;
            GUILayout.EndHorizontal();
            if (IsOpen(ev, s.events.Count))
                EditorGUILayout.PropertyField(p.FindPropertyRelative("action"), new GUIContent($"{ev.name}  ({ev.time:0.##}s)"));
            else
            {
                int n = ev.action != null ? ev.action.GetPersistentEventCount() : 0;
                GUILayout.Label($"   {n} call{(n == 1 ? "" : "s")}", hintStyle);
            }
            GUILayout.EndVertical();
        }
        if (so.ApplyModifiedProperties())
        {
            foreach (var ev in s.events) if (ev != null) ev.time = Mathf.Clamp(ev.time, 0f, s.duration);
            CinematicPlayModeEdits.Changed(c);
            viewerDirty = true;
        }
        if (remove >= 0)
        {
            Record("Remove Event");
            s.events.RemoveAt(remove);
            Changed();
            GUIUtility.ExitGUI();
        }

        float shotStart = StartOf(selected);
        float at = time >= shotStart && time <= shotStart + s.duration ? time - shotStart : 0f;
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = Accent;
        if (GUILayout.Button(IconLabel($"Add event at the playhead ({at:0.##}s)", "Animation.AddEvent"), GUILayout.Height(24f)))
            AddEvent(selected, at);
        GUI.backgroundColor = old;
        Hint("Events show as markers along the top of the shot on the timeline: drag them to change when they happen, " +
             "click one and press Delete to remove it, or right-click the timeline to add one there. Drag an object into " +
             "an event's box and pick what to call, like any Unity event. They only fire when the cinematic plays, not while previewing.");
        EndCard();

        // Start / end of the shot
        BeginCard(null);
        int starts = s.onStart != null ? s.onStart.GetPersistentEventCount() : 0;
        int ends = s.onEnd != null ? s.onEnd.GetPersistentEventCount() : 0;
        showEvents = EditorGUILayout.Foldout(showEvents, $"START / END  ({starts} on start, {ends} on end)", true);
        if (showEvents)
        {
            so.Update();
            shot = so.FindProperty("shots").GetArrayElementAtIndex(selected);
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("onStart"), new GUIContent("On Shot Start"));
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("onEnd"), new GUIContent("On Shot End"));
            Hint("On Shot End also fires if the cinematic is stopped during this shot. Events don't fire while previewing.");
            if (so.ApplyModifiedProperties()) CinematicPlayModeEdits.Changed(c);
        }
        EndCard();
    }

    void AddEvent(int shotIndex, float at)
    {
        if (shotIndex < 0 || shotIndex >= c.shots.Count) return;
        Shot s = c.shots[shotIndex];
        Record("Add Event");
        if (s.events == null) s.events = new List<CinematicController.ShotEvent>();
        var ev = new CinematicController.ShotEvent { name = $"Event {s.events.Count + 1}", time = Mathf.Clamp(Snap(at), 0f, s.duration) };
        s.events.Add(ev);
        itemOpen[ev] = true;
        selItem = ev;
        tab = Tab.Events;
        Changed();
        GUIUtility.ExitGUI();
    }

    void CardMultiview(Shot s)
    {
        BeginCard("MULTIVIEW", "GridLayoutGroup Icon");
        List<Transform> views = s.extraViews;

        if (views == null || views.Count == 0)
        {
            Hint("Show several cameras at once in a grid, like a wall of CCTV feeds. This shot's camera point is view 1. " +
                 "Fly the Scene view to each extra camera and add it.");
        }
        else
        {
            int n = views.Count + 1;
            int cols = s.columns > 0 ? s.columns : Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt(n / (float)cols);

            bool stackGrid = RowWidth < 440f; // narrow panel: picture above the sliders
            if (stackGrid) GUILayout.BeginVertical(); else GUILayout.BeginHorizontal();
            Rect grid = GUILayoutUtility.GetRect(128f, 72f, GUILayout.Width(128f), GUILayout.Height(72f));
            DrawGridPreview(grid, n, cols, rows, s.gap, s.gapColor);
            GUILayout.Space(stackGrid ? 4f : 10f);
            GUILayout.BeginVertical();
            EditorGUI.BeginChangeCheck();
            int newCols = EditorGUILayout.IntSlider("Columns", s.columns, 0, 6);
            float newGap = EditorGUILayout.Slider("Gap (px)", s.gap, 0f, 60f);
            Color newGapCol = s.gap > 0f ? EditorGUILayout.ColorField("Gap colour", s.gapColor) : s.gapColor;
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Multiview");
                s.columns = newCols;
                s.gap = newGap;
                s.gapColor = new Color(newGapCol.r, newGapCol.g, newGapCol.b, 1f);
                Changed();
            }
            Hint($"Columns 0 = automatic. {n} views in a {cols} × {rows} grid. All views use this shot's FOV; extra views stay still. " +
                 "For a frame around each feed as well, use Frame in each view's Tweak settings.");

            // Performance: every view is a full camera render
            GUILayout.Space(4f);
            GUILayout.Label("In game", EditorStyles.miniBoldLabel);
            float[] rates = { 0f, 30f, 24f, 15f, 12f, 10f, 5f };
            string[] rateNames = { "Every frame", "30 fps", "24 fps", "15 fps", "12 fps", "10 fps", "5 fps" };
            int rate = 0;
            for (int k = 0; k < rates.Length; k++) if (Mathf.Abs(rates[k] - s.viewFps) < 0.01f) rate = k;
            float[] scales = { 1f, 0.75f, 0.5f, 0.35f, 0.25f };
            string[] scaleNames = { "Full", "3/4", "Half", "Third", "Quarter" };
            int sc = 0;
            for (int k = 0; k < scales.Length; k++) if (Mathf.Abs(scales[k] - s.viewResolution) < 0.01f) sc = k;
            EditorGUI.BeginChangeCheck();
            int newRate = EditorGUILayout.Popup(new GUIContent("Views refresh", "How often the views are re-rendered in game. Each view " +
                                                "is a full camera render (with splats, a big one), so a lower rate is much cheaper. " +
                                                "Feeds that update at 10-15 fps also look more like real CCTV."), rate, rateNames);
            int newSc = EditorGUILayout.Popup(new GUIContent("Views resolution", "Render the views smaller in game: cheaper, and softer " +
                                              "(a camera look hides it well)."), sc, scaleNames);
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Multiview");
                s.viewFps = rates[newRate];
                s.viewResolution = scales[newSc];
                Changed();
            }
            if (s.viewFps > 0f || s.viewResolution < 1f)
                Hint("Only in game: the editor's viewer always shows every frame at full resolution, so you can still scrub precisely.");
            GUILayout.EndVertical();
            if (stackGrid) GUILayout.EndVertical(); else GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            GUILayout.Label("View 1:  this shot's camera point (its look is set in Camera Point)", hintStyle);
            if (s.transition == Transition.Move || s.lookAt != null || s.driftSpeed != Vector3.zero ||
                s.rotationSpeed != Vector3.zero || s.orbitSpeed != 0f)
                Hint("⚠ View 1 is the shot's own camera, so it also gets this shot's " +
                     (s.transition == Transition.Move ? "move-in from the previous shot, " : "") +
                     "aim and motion settings. For a still feed like the others, set Getting Here to Cut, " +
                     "Aim to the point's rotation, and turn off Motion.");
            int remove = -1;
            for (int i = 0; i < views.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(IconLabel($"View {i + 2}", "Camera Icon"), GUILayout.Width(RowWidth < 420f ? 54f : 68f), GUILayout.Height(18f));
                EditorGUI.BeginChangeCheck();
                var t = (Transform)EditorGUILayout.ObjectField(views[i], typeof(Transform), true);
                if (EditorGUI.EndChangeCheck()) { Record("Edit Multiview"); views[i] = t; Changed(); }
                bool slim = RowWidth < 420f; // narrow panel: icon-only buttons
                if (GUILayout.Button(IconOr(slim ? null : "Capture", slim ? "Cap" : "Capture", "Camera Icon", "Capture: frame it like the Scene view"),
                                     GUILayout.Width(slim ? 28f : 80f))) CaptureView(s, i);
                using (new EditorGUI.DisabledScope(views[i] == null))
                    if (GUILayout.Button(IconOr(slim ? null : "Look", slim ? "Look" : "Look through", "ViewToolOrbit", "Look through: move the Scene view to this camera"),
                                         GUILayout.Width(slim ? 28f : 70f)) && SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.AlignViewToObject(views[i]);
                if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = i;
                GUILayout.EndHorizontal();
                DrawViewFeed(s, i);
                GUILayout.Space(4f);
            }
            if (remove >= 0) RemoveView(s, remove);
        }

        GUILayout.Space(4f);
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = Accent;
        if (GUILayout.Button("+ Add view from Scene View", GUILayout.Height(24f))) AddView(s);
        GUI.backgroundColor = old;

        // Fill the whole grid from existing scene objects, e.g. every spawn location's camera point
        GUILayout.Space(4f);
        Rect drop = GUILayoutUtility.GetRect(10f, 40f, GUILayout.ExpandWidth(true));
        DrawFillDropArea(drop, s);
        if (GUILayout.Button("Use the objects selected in the Hierarchy", EditorStyles.miniButton))
        {
            var picked = new List<Transform>();
            foreach (Transform t in Selection.transforms) picked.Add(t);
            FillMultiview(s, picked);
        }
        Hint("Handy for previewing the cameras your code picks at play time: select or drop all of them here " +
             "(e.g. your spawn locations; an object with a 'cameraPoint' field uses that point). The first one becomes " +
             "view 1. Capture then moves those real objects, so you can tweak each camera without pressing Play.");

        GUILayout.Space(6f);
        if (Application.isPlaying)
        {
            int fromCode = (s.runtimePoint != null ? 1 : 0);
            if (s.runtimeViews != null)
                foreach (Transform v in s.runtimeViews)
                    if (v != null) fromCode++;
            bool set = s.runtimePoint != null || s.runtimeViews != null;
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                wordWrap = true,
                normal = { textColor = set ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.6f, 0.3f) }
            };
            Transform first = s.runtimePoint != null ? s.runtimePoint : s.point;
            GUILayout.Label(set
                ? $"● Play Mode: using {fromCode} camera point(s) set from code. View 1 is '{(first != null ? first.name : "none")}'" +
                  (s.runtimePoint != null && s.runtimePoint.parent != null ? $" (under '{s.runtimePoint.parent.name}')." : ".")
                : "● Play Mode: SetMultiview hasn't reached this shot, so the placeholder views are used. " +
                  "Check the Console for a [Cinematic] message.", style);
        }
        Hint("Picking the cameras at play time? Call SetMultiview(\"" + s.name + "\", cameraPoints) from your code. " +
             "The views above are then just placeholders for previewing.");

        EndCard();
    }

    void DrawFillDropArea(Rect r, Shot s)
    {
        Event e = Event.current;
        bool over = r.Contains(e.mousePosition) && DragAndDrop.objectReferences.Length > 0;
        if (e.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, over ? new Color(Accent.r, Accent.g, Accent.b, 0.25f) : Ink(0.06f));
            GUI.Label(r, "Drop camera points here to fill the grid (first = view 1)", overlayCenter);
        }
        if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && r.Contains(e.mousePosition))
        {
            var dropped = new List<Transform>();
            foreach (UnityEngine.Object o in DragAndDrop.objectReferences)
            {
                if (o is GameObject go) dropped.Add(go.transform);
                else if (o is Component comp) dropped.Add(comp.transform);
            }
            if (dropped.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                FillMultiview(s, dropped);
            }
            e.Use();
        }
    }

    // Makes these objects the shot's views: the first becomes view 1 (the shot's camera point), the rest the other cells
    void FillMultiview(Shot s, List<Transform> objects)
    {
        var points = new List<Transform>();
        foreach (Transform t in objects)
        {
            if (t == null) continue;
            Transform p = CameraPointOf(t.gameObject);
            if (!points.Contains(p)) points.Add(p);
        }
        if (points.Count == 0)
        {
            ShowNotification(new GUIContent("Select or drop objects from the scene first"));
            return;
        }
        points.Sort((a, b) => string.CompareOrdinal(HierarchyKey(a), HierarchyKey(b)));

        Record("Fill Multiview");
        Transform oldPoint = s.point;
        var oldViews = s.extraViews != null ? new List<Transform>(s.extraViews) : new List<Transform>();
        s.point = points[0];
        s.extraViews = points.GetRange(1, points.Count - 1);

        // Remove the editor's own placeholder points that nothing uses any more
        DestroyIfUnused(oldPoint);
        foreach (Transform v in oldViews) DestroyIfUnused(v);

        ShowNotification(new GUIContent($"{points.Count} cameras in the grid"), 1f);
        Changed();
        GUIUtility.ExitGUI();
    }

    // Objects like your spawn locations keep their camera in a 'cameraPoint' field: use that if there is one
    static Transform CameraPointOf(GameObject go)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
        foreach (MonoBehaviour comp in go.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;
            FieldInfo f = comp.GetType().GetField("cameraPoint", flags);
            if (f != null && typeof(Transform).IsAssignableFrom(f.FieldType) && f.GetValue(comp) is Transform tf && tf != null) return tf;
        }
        return go.transform;
    }

    // Sort key that follows the Hierarchy order
    static string HierarchyKey(Transform t)
    {
        var parts = new List<string>();
        for (Transform x = t; x != null; x = x.parent) parts.Insert(0, x.GetSiblingIndex().ToString("D5"));
        return string.Join(".", parts);
    }

    // A "how it comes in / goes out" row: the effect and how many seconds it takes. Returns true if changed.
    static bool InOutRow(string label, string tip, ref CinematicController.OverlayTransition fx, ref float seconds)
    {
        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        var nf = (CinematicController.OverlayTransition)EditorGUILayout.Popup(new GUIContent(label, tip), (int)fx, InOutNames);
        float ns = seconds;
        if (nf != CinematicController.OverlayTransition.None)
        {
            int ind = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            ns = Mathf.Max(0.01f, EditorGUILayout.FloatField(seconds, GUILayout.Width(44f)));
            GUILayout.Label("s", GUILayout.Width(10f));
            EditorGUI.indentLevel = ind;
        }
        GUILayout.EndHorizontal();
        if (!EditorGUI.EndChangeCheck()) return false;
        fx = nf;
        seconds = ns;
        return true;
    }

    // ---------------------------------------------------------------- Layer order

    const string LayerTip = "Draw order: higher numbers draw on top of lower ones, whatever they are (pop-ups, the scan, " +
                            "overlays and text). On the same number: pop-ups, then the scan, then overlays (later in the list on top). " +
                            "The camera is always at the bottom and fades always cover everything.";

    int LayerField(int value)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("Layer", LayerTip));
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        int v = EditorGUILayout.IntField(value, GUILayout.Width(44f));
        if (GUILayout.Button(new GUIContent("▼", "Send backward (one layer lower)"), EditorStyles.miniButtonLeft, GUILayout.Width(24f))) v--;
        if (GUILayout.Button(new GUIContent("▲", "Bring forward (one layer higher)"), EditorStyles.miniButtonRight, GUILayout.Width(24f))) v++;
        GUILayout.Label(new GUIContent(v > 0 ? "  above" : v < 0 ? "  below" : "  normal", LayerTip), hintStyle);
        EditorGUI.indentLevel = indent;
        GUILayout.EndHorizontal();
        return v;
    }

    // One thing on screen in this shot, for the layer order list
    class LayerEntry
    {
        public string name, icon;
        public float key;
        public object item;
        public Func<int> get;
        public Action<int> set;
    }

    // Everything drawn in this shot, top to bottom, with its layer number: see and change what's above what
    void CardLayerOrder(Shot s)
    {
        var entries = new List<LayerEntry>();
        if (s.popups != null)
            for (int i = 0; i < s.popups.Count; i++)
            {
                var p = s.popups[i];
                if (p == null) continue;
                entries.Add(new LayerEntry { name = p.name, icon = "UnityEditor.GameView", item = p, key = p.layer * 1000f + 10f + i * 0.01f,
                                             get = () => p.layer, set = v => p.layer = v });
            }
        if (s.scan != null && s.scan.enabled)
        {
            var sc = s.scan;
            entries.Add(new LayerEntry { name = "Scan boxes", icon = "ViewToolZoom", item = sc, key = sc.layer * 1000f + 20f,
                                         get = () => sc.layer, set = v => sc.layer = v });
        }
        for (int i = 0; c.images != null && i < c.images.Count; i++)
        {
            var im = c.images[i];
            if (im == null || im.kind == CinematicController.OverlayKind.UIObject || !ImageInShot(im, selected)) continue;
            entries.Add(new LayerEntry { name = im.name, icon = OverlayIcon(im.kind), item = im, key = im.layer * 1000f + 100f + i * 0.01f,
                                         get = () => im.layer, set = v => im.layer = v });
        }
        if (entries.Count < 2) return;
        entries.Sort((a, b) => b.key.CompareTo(a.key)); // top first

        BeginCard(null);
        bool open = Section(c, "layers", $"LAYER ORDER  ({entries.Count} things, top to bottom)", false);
        if (open)
        {
            foreach (var en in entries)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Icon(en.icon), GUILayout.Width(18f), GUILayout.Height(18f));
                GUIStyle st = IsSelectedItem(en.item) ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(new GUIContent(en.name, "Select it"), st, GUILayout.MinWidth(60f)))
                {
                    selItem = en.item;
                    Focus(en.item);
                    tab = en.item is CinematicController.ImageLayer ? Tab.Overlays : en.item is CinematicController.Popup ? Tab.Popups : Tab.Scan;
                }
                GUILayout.FlexibleSpace();
                int v = en.get();
                GUILayout.Label(new GUIContent(v.ToString(), LayerTip), hintStyle, GUILayout.Width(26f));
                if (GUILayout.Button(new GUIContent("▼", "One layer lower"), EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                { Record("Change Layer"); en.set(v - 1); Changed(); }
                if (GUILayout.Button(new GUIContent("▲", "One layer higher"), EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                { Record("Change Layer"); en.set(v + 1); Changed(); }
                GUILayout.EndHorizontal();
            }
            Hint(LayerTip);
        }
        EndCard();
    }

    void CardPopups(Shot s)
    {
        CardLayerOrder(s);
        BeginCard("POP-UPS", "UnityEditor.GameView");
        if (s.popups != null && s.popups.Count > 3) CollapseAllButtons(s.popups);
        if (s.popups == null) s.popups = new List<CinematicController.Popup>();

        if (s.popups.Count == 0)
            Hint("Show another camera on top of this shot in a small window, like a security feed popping up. " +
                 "Fly the Scene view to that camera and add it, then drag the window around in the viewer above.");

        int remove = -1;
        for (int j = 0; j < s.popups.Count; j++)
        {
            var p = s.popups[j];
            if (p.feed == null) p.feed = new CinematicController.Feed { look = true, border = 3f };
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            FoldArrow(p, s.popups.Count);
            GUILayout.Label(Icon("UnityEditor.GameView"), GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.TextField(p.name, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck()) { Record("Rename Pop-up"); p.name = n; Changed(); }
            bool slimP = RowWidth < 420f;
            if (GUILayout.Button(IconOr(slimP ? null : "Capture", slimP ? "Cap" : "Capture", "Camera Icon", "Capture: frame it like the Scene view"),
                                 GUILayout.Width(slimP ? 28f : 80f))) CapturePopup(s, j);
            using (new EditorGUI.DisabledScope(p.point == null))
                if (GUILayout.Button(IconOr(slimP ? null : "Look", slimP ? "Look" : "Look through", "ViewToolOrbit", "Look through: move the Scene view to this camera"),
                                     GUILayout.Width(slimP ? 28f : 70f)) && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.AlignViewToObject(p.point);
            if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = j;
            GUILayout.EndHorizontal();
            if (!IsOpen(p, s.popups.Count))
            {
                GUILayout.Label($"   appears at {p.appearAt:0.##}s  ·  " + (p.duration > 0f ? $"lasts {p.duration:0.##}s" : "until the shot ends"), hintStyle);
                GUILayout.EndVertical();
                continue;
            }

            EditorGUI.BeginChangeCheck();
            var point = (Transform)EditorGUILayout.ObjectField("Camera point", p.point, typeof(Transform), true);
            Vector2 pos = EditorGUILayout.Vector2Field("Position (0-1)", p.rect.position);
            Vector2 size = EditorGUILayout.Vector2Field("Size (0-1)", p.rect.size);
            float fov = EditorGUILayout.Slider("Field of view", p.fov > 0f ? p.fov : 60f, 1f, 179f);
            float at = Mathf.Max(0f, EditorGUILayout.FloatField("Appears at (s)", p.appearAt));
            float dur = Mathf.Max(0f, EditorGUILayout.FloatField("Lasts (s)", p.duration));
            if (p.effectsVersion == 0) CinematicController.UpgradePopup(p);
            var pin = p.inEffect; float pinT = p.inTime;
            var pout = p.outEffect; float poutT = p.outTime;
            InOutRow("Comes in", "How the window appears", ref pin, ref pinT);
            InOutRow("Goes out", "How the window disappears (when it has its own length: \"Lasts 0\" cuts with the shot)", ref pout, ref poutT);
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Pop-up");
                p.point = point;
                size = new Vector2(Mathf.Clamp(size.x, 0.05f, 1f), Mathf.Clamp(size.y, 0.05f, 1f));
                pos = new Vector2(Mathf.Clamp(pos.x, 0f, 1f - size.x), Mathf.Clamp(pos.y, 0f, 1f - size.y));
                p.rect = new Rect(pos, size);
                p.fov = fov;
                p.appearAt = at;
                p.duration = dur;
                p.inEffect = pin; p.inTime = pinT;
                p.outEffect = pout; p.outTime = poutT;
                Changed();
            }
            Rect alignedPop = AlignButtons(p.rect);
            if (alignedPop != p.rect) { Record("Align Pop-up"); p.rect = alignedPop; Changed(); }
            int lay = LayerField(p.layer);
            if (lay != p.layer) { Record("Change Layer"); p.layer = lay; Changed(); }
            if (p.point == null) Hint("No camera point yet: press Capture with the Scene view where this camera should be.");
            Hint("Lasts 0 = until the shot ends. Drag the window in the viewer, or its bar on the Pop-ups lane.");

            GUILayout.Space(4f);
            DrawFeed(p.feed, selected + "/pop" + j);
            GUILayout.EndVertical();
        }
        if (remove >= 0) RemovePopup(s, remove);

        GUILayout.Space(4f);
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = Accent;
        if (GUILayout.Button("+ Add pop-up from Scene View", GUILayout.Height(24f))) AddPopup(s);
        GUI.backgroundColor = old;
        if (s.popups.Count > 0)
            Hint("Picking the camera at play time? Call SetPopupPoint(\"" + s.name + "\", 0, cameraPoint) from your code.");

        EndCard();
    }

    // "Camera effect" for one overlay: None / Own / Filmed
    CinematicController.CameraEffect EffectPicker(CinematicController.CameraEffect current, Shot s)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("Camera effect", "How this relates to the shot's camera feed look"), GUILayout.Width(EditorGUIUtility.labelWidth - 2f));
        var options = new[]
        {
            new GUIContent("None", "Clean, on top of the camera, like a HUD"),
            new GUIContent("Own", "Gets the camera's effects (scanlines, noise, tint...) by itself, keeping its own shape"),
            new GUIContent("Filmed", "Part of the picture, as if the camera filmed it: lens distortion bends it near the edges too")
        };
        int v = GUILayout.Toolbar((int)current, options, EditorStyles.miniButton, GUILayout.Height(20f));
        GUILayout.EndHorizontal();
        if (v != 0 && (s.feed == null || !s.feed.look))
            Hint("This shot's camera has no feed look yet, so nothing changes (turn it on in the Camera tab).");
        return (CinematicController.CameraEffect)v;
    }

    // Collapsible rows: open by default while a list is short
    // Open / closed state of items (overlays, sounds, pop-ups, motion clips) and their sections. Kept by the item's
    // place in the cinematic (e.g. "overlay/3"), so it survives script reloads and Play Mode, and saved with the
    // editor preferences so it's still there after restarting Unity.
    static readonly OpenState itemOpen = new OpenState();

    class OpenState
    {
        Dictionary<string, bool> map;

        Dictionary<string, bool> Map
        {
            get
            {
                if (map != null) return map;
                map = new Dictionary<string, bool>();
                foreach (string pair in EditorPrefs.GetString("CinematicEditor.OpenItems", "").Split('|'))
                {
                    int eq = pair.LastIndexOf('=');
                    if (eq > 0) map[pair.Substring(0, eq)] = pair.Substring(eq + 1) == "1";
                }
                return map;
            }
        }

        readonly Dictionary<object, bool> loose = new Dictionary<object, bool>(); // items without a lasting name

        public bool TryGetValue(object item, out bool open)
        {
            open = false;
            string k = KeyOf(item);
            return k != null ? Map.TryGetValue(k, out open) : item != null && loose.TryGetValue(item, out open);
        }

        public void SetLoose(object item, bool open) { if (item != null) loose[item] = open; }

        public bool TryGetKey(string key, out bool open) => Map.TryGetValue(key, out open);

        public void SetKey(string key, bool open)
        {
            if (key == null) return;
            Map[key] = open;
            var parts = new List<string>();
            foreach (var kv in Map) parts.Add(kv.Key + "=" + (kv.Value ? "1" : "0"));
            if (parts.Count > 400) parts.RemoveRange(0, parts.Count - 400); // keep it small
            EditorPrefs.SetString("CinematicEditor.OpenItems", string.Join("|", parts));
        }

        public bool this[object item]
        {
            set
            {
                string k = KeyOf(item);
                if (k != null) SetKey(k, value); else SetLoose(item, value);
            }
        }
    }

    // A name for an item that stays the same between reloads: its controller and its place in the lists
    static string KeyOf(object item)
    {
        if (item == null || c == null) return null;
        string pre = c.gameObject.scene.name + "/" + c.name + "/"; // stays the same after restarting Unity
        if (item is CinematicController.ImageLayer im) { int i = c.images.IndexOf(im); return i >= 0 ? pre + "overlay/" + i : null; }
        if (item is CinematicController.SoundClip sc) { int i = c.sounds.IndexOf(sc); return i >= 0 ? pre + "sound/" + i : null; }
        for (int si = 0; si < c.shots.Count; si++)
        {
            var sh = c.shots[si];
            if (item is CinematicController.Popup pp && sh.popups != null && sh.popups.Contains(pp)) return pre + $"shot/{si}/popup/{sh.popups.IndexOf(pp)}";
            if (item is CinematicController.Motion mo && sh.motions != null && sh.motions.Contains(mo)) return pre + $"shot/{si}/motion/{sh.motions.IndexOf(mo)}";
            if (item is CinematicController.ShotEvent se && sh.events != null && sh.events.Contains(se)) return pre + $"shot/{si}/event/{sh.events.IndexOf(se)}";
            if (item is CinematicController.ScanTarget tg && sh.scan != null && sh.scan.targets != null && sh.scan.targets.Contains(tg))
                return pre + $"shot/{si}/target/{sh.scan.targets.IndexOf(tg)}";
        }
        return null;
    }

    static bool IsOpen(object item, int listCount) =>
        item != null && (itemOpen.TryGetValue(item, out bool o) ? o : listCount <= 3);

    void FoldArrow(object item, int listCount)
    {
        bool o = IsOpen(item, listCount);
        if (GUILayout.Button(o ? "▾" : "▸", EditorStyles.label, GUILayout.Width(14f), GUILayout.Height(18f))) itemOpen[item] = !o;
    }

    // Opens one item's row (when it's clicked on the timeline or in the viewer)
    static void Focus(object item)
    {
        if (item != null) itemOpen[item] = true;
    }

    void CollapseAllButtons(System.Collections.IEnumerable items)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Collapse all", EditorStyles.miniButtonLeft))
            foreach (object o in items) if (o != null) itemOpen[o] = false;
        if (GUILayout.Button("Expand all", EditorStyles.miniButtonRight))
            foreach (object o in items) if (o != null) itemOpen[o] = true;
        GUILayout.EndHorizontal();
    }

    void CardFades(Shot s)
    {
        BeginCard("FADES", "SceneViewLighting");
        string preset = null;
        int fp = WrapButtons(Contents("Fade from black", "Fade to black", "White flash", "None"));
        if (fp >= 0) preset = new[] { "in", "out", "flash", "none" }[fp];

        EditorGUI.BeginChangeCheck();
        float fin = Mathf.Max(0f, EditorGUILayout.FloatField("Fade in (s)", s.fadeIn));
        float fout = Mathf.Max(0f, EditorGUILayout.FloatField("Fade out (s)", s.fadeOut));
        Color col = s.fadeIn > 0f || s.fadeOut > 0f ? EditorGUILayout.ColorField("Colour", s.fadeColor) : s.fadeColor;
        if (EditorGUI.EndChangeCheck() || preset != null)
        {
            Record("Edit Fades");
            s.fadeIn = fin;
            s.fadeOut = fout;
            s.fadeColor = new Color(col.r, col.g, col.b, 1f);
            switch (preset)
            {
                case "in": s.fadeIn = 0.6f; s.fadeColor = Color.black; break;
                case "out": s.fadeOut = 0.6f; s.fadeColor = Color.black; break;
                case "flash": s.fadeIn = 0.3f; s.fadeOut = 0f; s.fadeColor = Color.white; break;
                case "none": s.fadeIn = s.fadeOut = 0f; break;
            }
            Changed();
        }
        Hint("Fades the start and/or end of this shot from or to a colour, on top of everything.");
        EndCard();
    }

    void CardAudio(Shot s)
    {
        BeginCard("AUDIO", "AudioSource Icon");
        if (c.sounds != null && c.sounds.Count > 3) CollapseAllButtons(c.sounds);
        if (c.sounds == null) c.sounds = new List<CinematicController.SoundClip>();
        float a0 = StartOf(selected), a1 = a0 + s.duration;

        bool any = false;
        int remove = -1;
        for (int j = 0; j < c.sounds.Count; j++)
        {
            var snd = c.sounds[j];
            if (snd == null || !(snd.start < a1 && snd.start + CinematicController.LengthOf(snd) > a0)) continue;
            any = true;
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            FoldArrow(snd, c.sounds.Count);
            GUILayout.Label(Icon("AudioClip Icon"), GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.TextField(snd.name, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck()) { Record("Rename Sound"); snd.name = n; Changed(); }
            if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = j;
            GUILayout.EndHorizontal();
            if (!IsOpen(snd, c.sounds.Count))
            {
                GUILayout.Label($"   {snd.start:0.##}s → {snd.start + CinematicController.LengthOf(snd):0.##}s  ·  volume {snd.volume:0%}" +
                                (snd.loop ? "  ·  loops" : ""), hintStyle);
                GUILayout.EndVertical();
                continue;
            }

            EditorGUI.BeginChangeCheck();
            var clip = (AudioClip)EditorGUILayout.ObjectField("Clip", snd.clip, typeof(AudioClip), false);
            float st = Mathf.Max(0f, EditorGUILayout.FloatField("Starts at (s)", snd.start));
            float du = Mathf.Max(0f, EditorGUILayout.FloatField("Plays for (s)", snd.duration));
            float trim = Mathf.Max(0f, EditorGUILayout.FloatField("Trim start (s)", snd.clipStart));
            float vol = EditorGUILayout.Slider("Volume", snd.volume, 0f, 1f);
            float fin = Mathf.Max(0f, EditorGUILayout.FloatField("Fade in (s)", snd.fadeIn));
            float fout = Mathf.Max(0f, EditorGUILayout.FloatField("Fade out (s)", snd.fadeOut));
            bool loop = EditorGUILayout.Toggle("Loop", snd.loop);
            if (EditorGUI.EndChangeCheck())
            {
                Record("Edit Sound");
                if (clip != snd.clip && clip != null && (snd.name == "Sound" || snd.clip != null && snd.name == snd.clip.name)) snd.name = clip.name;
                snd.clip = clip;
                snd.start = st;
                snd.duration = du;
                snd.clipStart = trim;
                snd.volume = vol;
                snd.fadeIn = fin;
                snd.fadeOut = fout;
                snd.loop = loop;
                Changed();
            }
            Hint("Plays for 0 = the rest of the clip. Drag the bar on the Audio lane to move it; drag its ends to trim.");
            GUILayout.EndVertical();
        }
        if (remove >= 0)
        {
            Record("Remove Sound");
            c.sounds.RemoveAt(remove);
            Changed();
            GUIUtility.ExitGUI();
        }
        if (!any)
            Hint("Drag audio files from the Project window onto the timeline (or the viewer) to add them where you drop " +
                 "them. Sounds aren't tied to a shot: drag them anywhere, trim their ends, and press Play to hear them.");

        GUILayout.Space(4f);
        float addAt = time >= a0 && time <= a1 ? time : a0;
        EditorGUIUtility.SetIconSize(new Vector2(16f, 16f));
        if (GUILayout.Button(IconLabel("Add sound here", "AudioClip Icon"), GUILayout.Height(26f)))
            AddSounds(new List<AudioClip> { null }, addAt);
        EditorGUIUtility.SetIconSize(Vector2.zero);
        EndCard();
    }

    // Waveform of a sound drawn inside its bar (only for clips Unity has decompressed in memory)
    readonly Dictionary<AudioClip, float[]> soundPeaks = new Dictionary<AudioClip, float[]>();

    float[] PeaksOf(AudioClip clip)
    {
        if (clip == null) return null;
        if (soundPeaks.TryGetValue(clip, out float[] peaks)) return peaks;
        peaks = null;
        try
        {
            if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded && clip.samples > 256)
            {
                const int buckets = 256, window = 256;
                peaks = new float[buckets];
                int ch = Mathf.Max(1, clip.channels);
                var buf = new float[window * ch];
                for (int b = 0; b < buckets; b++)
                {
                    int offset = Mathf.Min((int)((long)clip.samples * b / buckets), clip.samples - window);
                    clip.GetData(buf, offset);
                    float peak = 0f;
                    foreach (float v in buf) peak = Mathf.Max(peak, Mathf.Abs(v));
                    peaks[b] = peak;
                }
            }
        }
        catch (Exception) { peaks = null; }
        soundPeaks[clip] = peaks;
        return peaks;
    }

    void DrawSoundWave(Rect b, CinematicController.SoundClip snd, float visibleWidth)
    {
        if (Event.current.type != EventType.Repaint || snd.clip == null) return;
        float[] peaks = PeaksOf(snd.clip);
        if (peaks == null) return;
        Color col = new Color(1f, 1f, 1f, 0.55f);
        float mid = b.center.y, len = snd.clip.length;
        for (float x = Mathf.Max(b.x, Gutter); x < Mathf.Min(b.xMax, visibleWidth); x += 2f)
        {
            float clipTime = snd.clipStart + (x - b.x) / pxPerSec;
            if (snd.loop) clipTime = Mathf.Repeat(clipTime, len);
            if (clipTime >= len) break;
            float amp = peaks[Mathf.Clamp((int)(clipTime / len * peaks.Length), 0, peaks.Length - 1)];
            float hh = Mathf.Max(1f, amp * b.height * 0.9f);
            EditorGUI.DrawRect(new Rect(x, mid - hh * 0.5f, 1.5f, hh), col);
        }
    }

    void CardScan(Shot s)
    {
        BeginCard("SCAN  (boxes searching and locking onto things)", "ViewToolZoom");
        if (s.scan == null) s.scan = new CinematicController.Scan();
        var sc = s.scan;

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft("Scanning boxes", sc.enabled, EditorStyles.boldLabel);
        var mode = sc.mode;
        float st = sc.start, du = sc.duration, search = sc.searchTime, speed = sc.speed, thick = sc.thickness, fill = sc.fill;
        int count = sc.randomCount;
        float minSize = sc.sizeRange.x, maxSize = sc.sizeRange.y;
        bool keep = sc.keepSearching, brackets = sc.brackets;
        Color col = sc.color;
        if (on)
        {
            mode = (CinematicController.ScanMode)GUILayout.Toolbar((int)sc.mode, new[] { "Random search", "Targets only", "Search → lock on" }, GUILayout.Height(22f));
            st = Mathf.Max(0f, EditorGUILayout.FloatField("Starts at (s)", sc.start));
            du = Mathf.Max(0f, EditorGUILayout.FloatField("Lasts (s)", sc.duration));
            if (mode == CinematicController.ScanMode.SearchThenLock)
                search = Mathf.Max(0f, EditorGUILayout.FloatField("Search for (s)", sc.searchTime));
            if (mode != CinematicController.ScanMode.Targets)
            {
                count = EditorGUILayout.IntSlider("Random boxes", sc.randomCount, 1, 12);
                speed = EditorGUILayout.Slider("Speed", sc.speed, 0.2f, 4f);
                EditorGUILayout.MinMaxSlider("Box size", ref minSize, ref maxSize, 0.02f, 0.4f);
                if (mode == CinematicController.ScanMode.SearchThenLock)
                    keep = EditorGUILayout.Toggle("Keep searching after lock", sc.keepSearching);
            }
            col = EditorGUILayout.ColorField("Colour", sc.color);
            thick = Mathf.Max(0.5f, EditorGUILayout.FloatField("Line (px)", sc.thickness));
            brackets = EditorGUILayout.Toggle("Corner brackets", sc.brackets);
            fill = EditorGUILayout.Slider("Fill", sc.fill, 0f, 0.5f);
        }
        bool scanChanged = EditorGUI.EndChangeCheck();
        if (scanChanged && on && !sc.enabled && sc.color.maxColorComponent <= 0f && sc.randomCount == 0)
        {
            // Shots saved before scanning existed come back blank: start from the defaults
            Record("Edit Scan");
            s.scan = new CinematicController.Scan { enabled = true };
            Changed();
            GUIUtility.ExitGUI();
        }
        if (scanChanged)
        {
            Record("Edit Scan");
            sc.enabled = on;
            sc.mode = mode;
            sc.start = st;
            sc.duration = du;
            sc.searchTime = search;
            sc.randomCount = count;
            sc.speed = speed;
            sc.sizeRange = new Vector2(minSize, maxSize);
            sc.keepSearching = keep;
            sc.color = col;
            sc.thickness = thick;
            sc.brackets = brackets;
            sc.fill = fill;
            Changed();
        }

        if (on)
        {
            var fx = EffectPicker(sc.cameraEffect, s);
            if (fx != sc.cameraEffect) { Record("Edit Scan"); sc.cameraEffect = fx; Changed(); }
            int lay = LayerField(sc.layer);
            if (lay != sc.layer) { Record("Change Layer"); sc.layer = lay; Changed(); }

            // Entrances and exits: the search boxes and the final target boxes separately
            if (Section(sc, "scanfx", "Comes in / goes out", true))
            {
                EditorGUI.indentLevel++;
                var bi = sc.boxIn; float biT = sc.boxInTime;
                var bo = sc.boxOut; float boT = sc.boxOutTime;
                var li = sc.lockEffect; float liT = sc.lockTime;
                var to = sc.targetOut; float toT = sc.targetOutTime;
                bool lb = sc.lockBlink;
                bool ch = false;
                if (sc.mode != CinematicController.ScanMode.Targets)
                {
                    GUILayout.Label("Search boxes", EditorStyles.miniBoldLabel);
                    ch |= InOutRow("Appear", "Every time a search box jumps to a new spot (and when the scan starts)", ref bi, ref biT);
                    ch |= InOutRow("Disappear", "Before each jump, when the targets lock on, and when the scan ends", ref bo, ref boT);
                }
                if (sc.mode != CinematicController.ScanMode.Random)
                {
                    GUILayout.Label("Target boxes", EditorStyles.miniBoldLabel);
                    ch |= InOutRow("Lock on", sc.mode == CinematicController.ScanMode.SearchThenLock
                        ? "It flies from its search spot onto the target in this time, then this effect plays (Zoom / Fade: just the fly)"
                        : "How each target box appears on its target", ref li, ref liT);
                    EditorGUI.BeginChangeCheck();
                    lb = EditorGUILayout.Toggle(new GUIContent("Blink when locked", "Blinks a few times right after locking on"), sc.lockBlink);
                    ch |= EditorGUI.EndChangeCheck();
                    ch |= InOutRow("Disappear", "How the target boxes and their labels leave when the scan ends", ref to, ref toT);
                }
                if (ch)
                {
                    Record("Edit Scan");
                    sc.boxIn = bi; sc.boxInTime = biT;
                    sc.boxOut = bo; sc.boxOutTime = boT;
                    sc.lockEffect = li; sc.lockTime = liT;
                    sc.lockBlink = lb;
                    sc.targetOut = to; sc.targetOutTime = toT;
                    Changed();
                }
                if (sc.duration <= 0f)
                    Hint("The disappear effects at the end need the scan to have its own length (Lasts > 0). " +
                         "Lasting until the end of the shot, it cuts with the shot.");
                EditorGUI.indentLevel--;
            }
        }

        if (!on)
        {
            Hint("Boxes that dart around the screen as if analysing it, and/or lock onto spots you choose with a label " +
                 "that types itself out. Drawn on the screen, not in the scene.");
            EndCard();
            return;
        }

        if (mode != CinematicController.ScanMode.Random)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Targets (locked onto in this order)", EditorStyles.miniBoldLabel);
            if (sc.targets == null) sc.targets = new List<CinematicController.ScanTarget>();
            int remove = -1;
            for (int j = 0; j < sc.targets.Count; j++)
            {
                var tg = sc.targets[j];
                GUILayout.BeginHorizontal();
                GUILayout.Label(IconLabel($"{j + 1}", "ViewToolZoom"), GUILayout.Width(36f), GUILayout.Height(18f));
                EditorGUI.BeginChangeCheck();
                string label = EditorGUILayout.TextField(tg.label);
                if (EditorGUI.EndChangeCheck()) { Record("Edit Scan Target"); tg.label = label; Changed(); }
                if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), GUILayout.Width(28f))) remove = j;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                Record("Remove Scan Target");
                sc.targets.RemoveAt(remove);
                Changed();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("+ Add target", GUILayout.Height(22f)))
            {
                Record("Add Scan Target");
                var tg = new CinematicController.ScanTarget { label = "TARGET DETECTED" };
                tg.rect = new Rect(0.15f + 0.12f * (sc.targets.Count % 5), 0.3f, 0.18f, 0.22f);
                sc.targets.Add(tg);
                Changed();
                GUIUtility.ExitGUI();
            }
            Hint("Drag each target box into place in the viewer; the corner resizes it. Labels can be left empty.");
        }
        EndCard();
    }

    void CardImages(Shot s)
    {
        CardLayerOrder(s);
        BeginCard("OVERLAYS  (images, text, animated UI)", "Image Icon");
        if (c.images != null && c.images.Count > 3) CollapseAllButtons(c.images);
        EditorGUI.BeginChangeCheck();
        var newFont = (Font)EditorGUILayout.ObjectField(new GUIContent("Font (all text)",
            "Used by text overlays, scan labels and progress bars. Empty = a monospaced system font."), c.font, typeof(Font), false);
        if (EditorGUI.EndChangeCheck()) { Record("Change Font"); c.font = newFont; Changed(); }
        GUILayout.Space(4f);
        if (c.images == null) c.images = new List<CinematicController.ImageLayer>();
        float shotStart = StartOf(selected);

        bool any = false;
        int remove = -1, moveUp = -1;
        for (int j = 0; j < c.images.Count; j++)
        {
            var im = c.images[j];
            if (!ImageInShot(im, selected)) continue;
            any = true;
            GUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.BeginHorizontal();
            FoldArrow(im, c.images.Count);
            GUILayout.Label(Icon(OverlayIcon(im.kind)), GUILayout.Width(18f), GUILayout.Height(18f));
            EditorGUI.BeginChangeCheck();
            string n = EditorGUILayout.TextField(im.name, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck()) { Record("Rename Image"); im.name = n; Changed(); }
            using (new EditorGUI.DisabledScope(j == 0))
                if (GUILayout.Button("Send back", EditorStyles.miniButtonLeft, GUILayout.Width(70f))) moveUp = j;
            if (GUILayout.Button(IconOr(null, "✕", "TreeEditor.Trash", "Remove"), EditorStyles.miniButtonRight, GUILayout.Width(28f))) remove = j;
            GUILayout.EndHorizontal();
            if (!IsOpen(im, c.images.Count))
            {
                GUILayout.Label($"   {im.kind}  ·  {im.start:0.##}s → {im.start + im.duration:0.##}s" +
                                (im.cameraEffect != CinematicController.CameraEffect.None ? $"  ·  camera effect: {im.cameraEffect}" : ""), hintStyle);
                GUILayout.EndVertical();
                continue;
            }

            if (im.effectsVersion == 0) CinematicController.UpgradeOverlay(im);
            var kindNow = im.kind;
            EditorGUI.BeginChangeCheck();
            int kind = GUILayout.Toolbar((int)im.kind, new[] { "Image", "Text", "Waveform", "Progress bar", "UI object" });
            Texture tex = im.texture;
            string text = im.text;
            bool typewriter = im.typewriter;
            float thick = im.thickness, fillTime = im.fillTime;
            string barText = im.barText;
            bool showPercent = im.showPercent;
            GameObject target = im.target;
            var k = (CinematicController.OverlayKind)kind;
            if (k == CinematicController.OverlayKind.Image)
                tex = (Texture)EditorGUILayout.ObjectField("Image", im.texture, typeof(Texture), false);
            else if (k == CinematicController.OverlayKind.Text)
            {
                GUILayout.Label(new GUIContent("Text", "Press Enter for a new line"));
                text = EditorGUILayout.TextArea(im.text ?? "", GUILayout.MinHeight(38f));
                typewriter = EditorGUILayout.Toggle("Typewriter", im.typewriter);
            }
            else if (k == CinematicController.OverlayKind.Waveform)
                thick = Mathf.Max(0.5f, EditorGUILayout.FloatField("Line (px)", im.thickness));
            else if (k == CinematicController.OverlayKind.ProgressBar)
            {
                thick = Mathf.Max(0f, EditorGUILayout.FloatField("Frame (px)", im.thickness));
                tex = (Texture)EditorGUILayout.ObjectField(new GUIContent("Fill sprite (optional)",
                    "Leave empty for a plain square fill. A sprite here is revealed left to right as it fills."), im.texture, typeof(Texture), false);
                barText = EditorGUILayout.TextField("Text above", im.barText ?? "");
                showPercent = EditorGUILayout.Toggle("Show percentage", im.showPercent);
            }
            else
            {
                target = (GameObject)EditorGUILayout.ObjectField("Object", im.target, typeof(GameObject), true);
                Hint("Switched on while its bar is on the timeline, then off again (Play Mode only). Build it as your own " +
                     "Screen Space - Overlay canvas or panel and animate it with an Animator or DOTween. Leave it switched " +
                     "off in the scene. The viewer can't show it; the Game view can.");
            }

            Vector2 pos = im.rect.position, size = im.rect.size;
            bool keep = im.keepShape, fit = false;
            Color col = im.color;
            if (k != CinematicController.OverlayKind.UIObject)
            {
                pos = EditorGUILayout.Vector2Field("Position (0-1)", im.rect.position);
                size = EditorGUILayout.Vector2Field("Size (0-1)", im.rect.size);
                if (k == CinematicController.OverlayKind.Image)
                {
                    GUILayout.BeginHorizontal();
                    keep = EditorGUILayout.Toggle("Keep shape", im.keepShape);
                    using (new EditorGUI.DisabledScope(im.texture == null))
                        fit = GUILayout.Button("Fit to image", EditorStyles.miniButton, GUILayout.Width(80f));
                    GUILayout.EndHorizontal();
                }
                col = EditorGUILayout.ColorField(k == CinematicController.OverlayKind.Image ? "Colour / opacity" : "Colour", im.color);
            }
            float st = Mathf.Max(0f, EditorGUILayout.FloatField("Starts at (s)", im.start));
            float dur;
            if (k == CinematicController.OverlayKind.ProgressBar)
            {
                // Loading time + time it stays full afterwards = how long it's on screen
                float load = im.fillTime > 0f ? Mathf.Min(im.fillTime, im.duration) : im.duration;
                float hold = Mathf.Max(0f, im.duration - load);
                load = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Loading takes (s)", "How long the bar takes to fill up to 100%"), load));
                hold = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Stays full for (s)", "How long it stays on screen after it's done loading"), hold));
                fillTime = load;
                dur = load + hold;
            }
            else dur = Mathf.Max(0.1f, EditorGUILayout.FloatField("Lasts (s)", im.duration));
            if (EditorGUI.EndChangeCheck() || fit)
            {
                Record("Edit Overlay");
                im.kind = k;
                if (k != kindNow) ApplyKindDefaults(im);
                else
                {
                    im.text = text;
                    im.typewriter = typewriter;
                    im.thickness = thick;
                    im.fillTime = fillTime;
                    im.barText = barText;
                    im.showPercent = showPercent;
                    im.target = target;
                }
                bool newTex = tex != im.texture;
                im.texture = tex;
                if (newTex && tex != null && (string.IsNullOrEmpty(im.name) || im.name.StartsWith("Image "))) im.name = tex.name;
                if (k == kindNow)
                {
                    size = new Vector2(Mathf.Clamp(size.x, 0.02f, 1f), Mathf.Clamp(size.y, 0.01f, 1f));
                    pos = new Vector2(Mathf.Clamp(pos.x, 0f, 1f - size.x), Mathf.Clamp(pos.y, 0f, 1f - size.y));
                    im.rect = new Rect(pos, size);
                }
                im.keepShape = keep;
                im.color = col;
                im.start = st;
                im.duration = dur;
                if (k == CinematicController.OverlayKind.Image && (fit || (newTex && keep))) FitToTexture(im);
                Changed();
            }
            if (im.kind != CinematicController.OverlayKind.UIObject) OverlayExtras(im);
            float from = im.start - shotStart;
            Hint(from >= 0f
                ? $"Starts {from:0.##}s into this shot" + (im.start + im.duration > shotStart + s.duration ? " and carries on into the next shot(s)." : ".")
                : "Started in an earlier shot and is still showing here.");
            if (im.kind != CinematicController.OverlayKind.UIObject)
            {
                Rect aligned = AlignButtons(im.rect);
                if (aligned != im.rect) { Record("Align Overlay"); im.rect = aligned; Changed(); }
                var fx = EffectPicker(im.cameraEffect, s);
                if (fx != im.cameraEffect) { Record("Edit Overlay"); im.cameraEffect = fx; Changed(); }
                int lay = LayerField(im.layer);
                if (lay != im.layer) { Record("Change Layer"); im.layer = lay; Changed(); }
            }
            if (im.kind == CinematicController.OverlayKind.Image && im.texture == null)
                Hint("Pick an image (a texture or sprite file from your project).");
            if (im.kind == CinematicController.OverlayKind.UIObject && im.target == null)
                Hint("Pick the canvas or panel to switch on.");
            GUILayout.EndVertical();
        }

        if (!any)
            Hint("Put things on top of the cinematic: images (drag one from the Project window onto the viewer), text, " +
                 "an animated audio waveform, a progress bar, or your own UI. Overlays aren't tied to a shot: drag their " +
                 "bar on the Overlays lane to move them across shots or make them longer.");

        if (remove >= 0)
        {
            Record("Remove Image");
            c.images.RemoveAt(remove);
            Changed();
            GUIUtility.ExitGUI();
        }
        if (moveUp > 0)
        {
            Record("Reorder Images");
            var swap = c.images[moveUp];
            c.images[moveUp] = c.images[moveUp - 1];
            c.images[moveUp - 1] = swap;
            Changed();
            GUIUtility.ExitGUI();
        }

        GUILayout.Space(4f);
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = Accent;
        // New overlays start at the playhead if it's in this shot, otherwise at the start of the shot
        float addAt = time >= shotStart && time <= shotStart + s.duration ? time : shotStart;
        EditorGUIUtility.SetIconSize(new Vector2(16f, 16f));
        int add = WrapButtons(new[]
        {
            IconLabel("Image", "Image Icon"), IconLabel("Text", "Text Icon"), IconLabel("Waveform", "AudioClip Icon"),
            IconLabel("Progress bar", "Slider Icon"), IconLabel("UI object", "Canvas Icon")
        }, 26f);
        EditorGUIUtility.SetIconSize(Vector2.zero);
        if (add >= 0)
            AddOverlay(new[] { CinematicController.OverlayKind.Image, CinematicController.OverlayKind.Text, CinematicController.OverlayKind.Waveform,
                               CinematicController.OverlayKind.ProgressBar, CinematicController.OverlayKind.UIObject }[add], null, null, addAt);
        GUI.backgroundColor = old;
        if (any)
            Hint("Overlays run on the cinematic's own timeline: drag a bar on the Overlays lane to move it into other shots " +
                 "or stretch it. Later ones draw on top; use Layer (or the Layer order list) to put things above or below each other, pop-ups included. In the viewer, drag to move and drag the corner to resize.");

        EndCard();
    }

    // ---------------------------------------------------------------- Overlay style, animation and glow

    static bool Section(object owner, string id, string label, bool startOpen)
    {
        string baseKey = KeyOf(owner);
        string key = (baseKey ?? owner.GetHashCode().ToString()) + "#" + id;
        bool open = startOpen;
        if (itemOpen.TryGetKey(key, out bool saved)) open = saved;
        bool now = EditorGUILayout.Foldout(open, label, true);
        if (now != open) itemOpen.SetKey(key, now);
        return now;
    }

    static readonly string[] InOutNames =
        { "None", "Fade", "Pop", "Zoom", "Slide left", "Slide right", "Slide up", "Slide down", "Wipe", "Flicker", "Glitch" };
    static readonly string[] LoopNames = { "Nothing", "Blink", "Pulse", "Flicker", "Float", "Shake", "Glitch" };
    static readonly float[] LoopDefaultRate = { 2f, 2f, 1f, 6f, 0.5f, 20f, 1.5f };

    // Everything below the basics: edits the overlay directly, then turns the whole change into one undo step
    void OverlayExtras(CinematicController.ImageLayer im)
    {
        string before = EditorJsonUtility.ToJson(im);
        EditorGUI.BeginChangeCheck();
        bool fixSound = false;

        switch (im.kind)
        {
            case CinematicController.OverlayKind.Text: TextStyle(im); break;
            case CinematicController.OverlayKind.Waveform: fixSound = WaveStyleUI(im); break;
            case CinematicController.OverlayKind.ProgressBar: BarStyle(im); break;
        }
        AnimationUI(im);
        GlowUI(im);

        if (EditorGUI.EndChangeCheck())
        {
            AudioClip followBefore = JsonFollow(before);
            string after = EditorJsonUtility.ToJson(im);
            EditorJsonUtility.FromJsonOverwrite(before, im);
            Record("Edit Overlay");
            EditorJsonUtility.FromJsonOverwrite(after, im);
            Changed();
            // Picking a sound changes which settings show under it: stop drawing this pass so the editor
            // doesn't trip over the different set of controls (it redraws straight away)
            if (im.followSound != followBefore) GUIUtility.ExitGUI();
        }
        if (fixSound) FixSoundImport(im.followSound);
    }

    // The followed sound in an overlay saved as JSON (before an edit)
    static AudioClip JsonFollow(string json)
    {
        var tmp = new CinematicController.ImageLayer();
        EditorJsonUtility.FromJsonOverwrite(json, tmp);
        return tmp.followSound;
    }

    void TextStyle(CinematicController.ImageLayer im)
    {
        if (!Section(im, "text", "Text style", true)) return;
        EditorGUI.indentLevel++;

        // Alignment inside its own box
        GUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("Align in box", "Where the text sits inside its box (the box itself is moved with Align below)"));
        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        im.textAlign = (CinematicController.TextAlignH)GUILayout.Toolbar((int)im.textAlign, new[]
        {
            IconOr(null, "Left", "align_horizontally_left", "Left"),
            IconOr(null, "Centre", "align_horizontally_center", "Centre"),
            IconOr(null, "Right", "align_horizontally_right", "Right")
        }, GUILayout.Height(20f));
        GUILayout.Space(6f);
        im.textVAlign = (CinematicController.TextAlignV)GUILayout.Toolbar((int)im.textVAlign, new[]
        {
            IconOr(null, "Top", "align_vertically_top", "Top"),
            IconOr(null, "Middle", "align_vertically_center", "Middle"),
            IconOr(null, "Bottom", "align_vertically_bottom", "Bottom")
        }, GUILayout.Height(20f));
        EditorGUI.indentLevel = indent;
        GUILayout.EndHorizontal();

        // Size: a slider that's always there. Moving it sets a fixed size; "Fit box" goes back to filling the box.
        float shownSize = im.fontSize > 0f ? im.fontSize : CinematicController.AutoTextSize(im);
        GUILayout.BeginHorizontal();
        float ns = EditorGUILayout.Slider(new GUIContent("Text size (px)", "Letter size in pixels at 1080p. Spacing scales with it. " +
                                                         "Changing it keeps the box as it is"), Mathf.Round(shownSize), 4f, 300f);
        if (Mathf.Abs(ns - Mathf.Round(shownSize)) > 0.01f) im.fontSize = ns;
        int indentNow = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        bool fit = GUILayout.Toggle(im.fontSize <= 0f, new GUIContent("Fit box", "Letters fill the box's height: resize the box to resize the text"),
                                    EditorStyles.miniButton, GUILayout.Width(56f));
        EditorGUI.indentLevel = indentNow;
        GUILayout.EndHorizontal();
        if (fit && im.fontSize > 0f) im.fontSize = 0f;
        else if (!fit && im.fontSize <= 0f) im.fontSize = Mathf.Round(shownSize);

        im.fontStyle = (FontStyle)GUILayout.Toolbar((int)im.fontStyle, new[] { "Regular", "Bold", "Italic", "Bold italic" });
        im.allCaps = EditorGUILayout.Toggle(new GUIContent("ALL CAPS"), im.allCaps);
        im.wrap = EditorGUILayout.Toggle(new GUIContent("Wrap at the box edge", "Long lines carry on onto the next line"), im.wrap);
        im.lineSpacing = EditorGUILayout.Slider(new GUIContent("Line spacing", "For text over several lines"), im.lineSpacing, 0.5f, 3f);
        im.letterSpacing = EditorGUILayout.Slider(new GUIContent("Letter spacing (%)", "Wider or tighter letters, as a share of the letter size, " +
                                                  "so it looks the same at any text size"), im.letterSpacing, -20f, 100f);

        // Outline and shadow
        GUILayout.BeginHorizontal();
        im.outline = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Outline (px)", "0 = none"), im.outline), 0f, 20f);
        if (im.outline > 0f) im.outlineColor = EditorGUILayout.ColorField(GUIContent.none, im.outlineColor, GUILayout.Width(60f));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        im.shadow = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Shadow (px)", "Drop shadow down and to the right. 0 = none"), im.shadow), 0f, 40f);
        if (im.shadow > 0f) im.shadowColor = EditorGUILayout.ColorField(GUIContent.none, im.shadowColor, GUILayout.Width(60f));
        GUILayout.EndHorizontal();
        GUILayout.Space(2f);

        if (im.typewriter)
            im.typeSpeed = Mathf.Max(1f, EditorGUILayout.FloatField(new GUIContent("Letters per second", "Typewriter speed"), im.typeSpeed));
        // Blinking is one of the "While showing" effects (in Animation below); here as a shortcut for text
        bool blinking = im.loopEffect == CinematicController.OverlayLoop.Blink;
        GUILayout.BeginHorizontal();
        bool blinkNow = EditorGUILayout.Toggle(new GUIContent("Blink", "The whole text blinks on and off. Same as Animation → While showing → Blink"), blinking);
        if (blinkNow)
        {
            int ind = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            GUILayout.Label("per second", hintStyle, GUILayout.Width(60f));
            im.loopRate = Mathf.Max(0.1f, EditorGUILayout.FloatField(im.loopRate, GUILayout.Width(40f)));
            EditorGUI.indentLevel = ind;
        }
        GUILayout.EndHorizontal();
        if (blinkNow != blinking)
        {
            im.loopEffect = blinkNow ? CinematicController.OverlayLoop.Blink : CinematicController.OverlayLoop.None;
            if (blinkNow) { im.loopRate = 2f; im.loopStrength = 0.85f; }
        }
        im.scramble = EditorGUILayout.Toggle(new GUIContent("Decode (scramble)", "Letters flicker through random characters before settling"), im.scramble);
        im.cursor = EditorGUILayout.Toggle(new GUIContent("Cursor _", "A cursor at the end: solid while typing, then blinking"), im.cursor);
        EditorGUI.indentLevel--;
    }

    // Returns true when the "make it readable" button was pressed
    bool WaveStyleUI(CinematicController.ImageLayer im)
    {
        bool fix = false;
        if (!Section(im, "wave", "Waveform style", true)) return false;
        EditorGUI.indentLevel++;
        im.waveStyle = (CinematicController.WaveStyle)GUILayout.Toolbar((int)im.waveStyle, new[] { "Line", "Bars", "Filled" });
        if (im.waveStyle == CinematicController.WaveStyle.Bars)
            im.waveBars = Mathf.Clamp(EditorGUILayout.IntField("Bars", im.waveBars), 1, 256);
        im.waveHeight = Mathf.Max(0f, EditorGUILayout.Slider(new GUIContent("Height", "How tall the wiggle gets"), im.waveHeight, 0f, 2.4f));

        float from = im.waveFrom, to = im.waveTo;
        EditorGUILayout.MinMaxSlider(new GUIContent("Moves across", "The part of the width that moves (left edge to right edge). " +
                                                    "Outside it the line stays flat."), ref from, ref to, 0f, 1f);
        im.waveFrom = from;
        im.waveTo = Mathf.Max(to, from + 0.01f);
        GUILayout.BeginHorizontal();
        im.waveMoveStart = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Starts moving at (s)",
                               "Seconds after it appears. Flat line before that."), im.waveMoveStart));
        GUILayout.EndHorizontal();
        im.waveMoveDuration = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Moves for (s)",
                                  "Then it calms down to a flat line again. 0 = keeps moving until it's gone."), im.waveMoveDuration));

        // Following a sound
        GUILayout.Space(2f);
        var clips = new List<AudioClip>();
        if (c.sounds != null)
            foreach (var sc in c.sounds)
                if (sc != null && sc.clip != null && !clips.Contains(sc.clip)) clips.Add(sc.clip);
        var names = new List<string> { "Nothing (made-up wiggle)" };
        foreach (var cl in clips) names.Add("Sound: " + cl.name);
        int cur = im.followSound == null ? 0 : clips.IndexOf(im.followSound) + 1;
        if (im.followSound != null && cur == 0) { names.Add("Other: " + im.followSound.name); cur = names.Count - 1; }
        int pick = EditorGUILayout.Popup(new GUIContent("Follow sound", "Show a sound's real wave, in sync with where it plays on the " +
                                                        "timeline. Sounds on the Audio lane are listed; any other clip can go below."),
                                         cur, names.ToArray());
        if (pick != cur) im.followSound = pick == 0 ? null : pick <= clips.Count ? clips[pick - 1] : im.followSound;
        im.followSound = (AudioClip)EditorGUILayout.ObjectField(" ", im.followSound, typeof(AudioClip), false);

        if (im.followSound != null)
        {
            im.followWindow = EditorGUILayout.Slider(new GUIContent("Seconds shown", "How much of the sound fits across the moving part. " +
                                                         "Short (0.05-0.2) = oscilloscope wiggle, long (1-4) = scrolling audio like an editor."),
                                                     im.followWindow, 0.02f, 5f);
            im.followGain = EditorGUILayout.Slider(new GUIContent("Boost", "Makes quiet parts taller"), im.followGain, 0f, 4f);
            // Read the sound's waveform once and keep it with the cinematic (works in builds too, any Load Type)
            if (!c.HasBakedWave(im.followSound) && !bakeFailed.Contains(im.followSound)) BakeWave(im.followSound);
            if (c.HasBakedWave(im.followSound))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("   ✓ Waveform read from the sound", hintStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Read again", "If you've edited the sound file"), EditorStyles.miniButton, GUILayout.Width(80f)))
                    BakeWave(im.followSound);
                GUILayout.EndHorizontal();
            }
            else if (CinematicController.FailedToRead(im.followSound) || bakeFailed.Contains(im.followSound))
            {
                EditorGUILayout.HelpBox("This sound's waveform couldn't be read, so the waveform shows its made-up wiggle. " +
                                        "A .wav file always works; for others, Unity has to be able to read the sound (Decompress On Load).",
                                        MessageType.Warning);
                if (GUILayout.Button("Try again")) { bakeFailed.Remove(im.followSound); BakeWave(im.followSound); }
                fix = !CinematicController.CanReadSound(im.followSound) && GUILayout.Button("Set Decompress On Load and try again");
            }
            else
            {
                bool onTimeline = clips.Contains(im.followSound);
                Hint(onTimeline
                    ? "Follows the sound's bar on the Audio lane: flat while it's silent, moving while it plays (fades included)."
                    : "This sound isn't on the Audio lane, so it's followed as if it started when the waveform appears (and isn't heard).");
            }
        }
        else
        {
            im.waveSpeed = EditorGUILayout.Slider("Speed", im.waveSpeed, 0f, 4f);
            im.waveDetail = EditorGUILayout.Slider(new GUIContent("Detail", "How busy the wiggle is"), im.waveDetail, 0.1f, 4f);
        }
        EditorGUI.indentLevel--;
        return fix;
    }

    // ---------------------------------------------------------------- Reading a sound's waveform (for waveform overlays)

    static readonly HashSet<AudioClip> bakeFailed = new HashSet<AudioClip>();

    // Reads the sound once and stores its waveform on the controller. Tries the audio file itself first
    // (.wav: plain samples), then Unity's own reading.
    void BakeWave(AudioClip clip)
    {
        if (clip == null) return;
        float[] mono = null;
        int rate = 0;
        string path = AssetDatabase.GetAssetPath(clip);
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            TryReadWav(System.IO.Path.GetFullPath(path), out mono, out rate);
        if (mono == null) TryUnitySamples(clip, out mono, out rate);
        if (mono == null || mono.Length == 0 || rate <= 0)
        {
            bakeFailed.Add(clip);
            Debug.LogWarning($"[Cinematic] Couldn't read the waveform of '{clip.name}' ({System.IO.Path.GetExtension(path)}). " +
                             "A .wav version of the sound always works.", clip);
            return;
        }

        // Keep it small: up to 2000 slices a second, and at most ~120,000 for long sounds
        float seconds = mono.Length / (float)rate;
        float sliceRate = Mathf.Min(2000f, 120000f / Mathf.Max(0.01f, seconds));
        int count = Mathf.Max(1, Mathf.CeilToInt(seconds * sliceRate));
        var peaks = new byte[count];
        for (int k = 0; k < count; k++)
        {
            int a = Mathf.FloorToInt(k / sliceRate * rate), b = Mathf.Min(mono.Length, Mathf.FloorToInt((k + 1) / sliceRate * rate));
            float v = 0f;
            for (int i = a; i < Mathf.Max(a + 1, b) && i < mono.Length; i++) if (Mathf.Abs(mono[i]) > Mathf.Abs(v)) v = mono[i];
            peaks[k] = (byte)Mathf.Clamp(Mathf.RoundToInt(128f + Mathf.Clamp(v, -1f, 1f) * 127f), 0, 255);
        }

        Record("Read Sound Waveform");
        if (c.bakedWaves == null) c.bakedWaves = new List<CinematicController.BakedWave>();
        c.bakedWaves.RemoveAll(bw => bw == null || bw.clip == null || bw.clip == clip);
        c.bakedWaves.Add(new CinematicController.BakedWave { clip = clip, peaks = peaks, rate = sliceRate });
        bakeFailed.Remove(clip);
        Changed();
    }

    // Plain .wav files: 8 / 16 / 24 / 32-bit samples or 32-bit float, any number of channels (mixed to one)
    static bool TryReadWav(string file, out float[] mono, out int rate)
    {
        mono = null;
        rate = 0;
        try
        {
            byte[] d = System.IO.File.ReadAllBytes(file);
            if (d.Length < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' || d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E')
                return false;
            int format = 0, channels = 0, bits = 0, dataStart = -1, dataLen = 0;
            int p = 12;
            while (p + 8 <= d.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(d, p, 4);
                int len = BitConverter.ToInt32(d, p + 4);
                int body = p + 8;
                if (id == "fmt ")
                {
                    format = BitConverter.ToUInt16(d, body);
                    channels = BitConverter.ToUInt16(d, body + 2);
                    rate = BitConverter.ToInt32(d, body + 4);
                    bits = BitConverter.ToUInt16(d, body + 14);
                    if (format == 0xFFFE && len >= 26) format = BitConverter.ToUInt16(d, body + 24); // "extensible": the real format
                }
                else if (id == "data")
                {
                    dataStart = body;
                    dataLen = Mathf.Min(len < 0 ? int.MaxValue : len, d.Length - body);
                    break;
                }
                p = body + len + (len & 1); // chunks are padded to an even size
            }
            if (dataStart < 0 || channels <= 0 || rate <= 0 || (format != 1 && format != 3)) return false;
            int bytes = bits / 8;
            if (bytes < 1 || (format == 3 && bits != 32)) return false;
            int frames = dataLen / (bytes * channels);
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    int o = dataStart + (f * channels + ch) * bytes;
                    float v;
                    if (format == 3) v = BitConverter.ToSingle(d, o);
                    else if (bytes == 1) v = (d[o] - 128) / 128f;
                    else if (bytes == 2) v = BitConverter.ToInt16(d, o) / 32768f;
                    else if (bytes == 3) v = ((d[o] << 8 | d[o + 1] << 16 | d[o + 2] << 24) >> 8) / 8388608f;
                    else v = BitConverter.ToInt32(d, o) / 2147483648f;
                    sum += v;
                }
                mono[f] = sum / channels;
            }
            return true;
        }
        catch { mono = null; return false; }
    }

    // Unity's own reading of a sound (needs Decompress On Load for compressed sounds)
    static bool TryUnitySamples(AudioClip clip, out float[] mono, out int rate)
    {
        mono = null;
        rate = 0;
        try
        {
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            int ch = Mathf.Max(1, clip.channels);
            var all = new float[clip.samples * ch];
            if (!clip.GetData(all, 0)) return false;
            mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int k = 0; k < ch; k++) sum += all[i * ch + k];
                mono[i] = sum / ch;
            }
            rate = clip.frequency;
            return true;
        }
        catch { mono = null; return false; }
    }

    static void FixSoundImport(AudioClip clip)
    {
        if (clip == null) return;
        var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
        if (imp == null) return;
        var st = imp.defaultSampleSettings;
        st.loadType = AudioClipLoadType.DecompressOnLoad;
        imp.defaultSampleSettings = st;
        imp.SaveAndReimport();
        CinematicController.ForgetSound(clip);
        GUIUtility.ExitGUI();
    }

    void BarStyle(CinematicController.ImageLayer im)
    {
        if (!Section(im, "bar", "Progress bar style", true)) return;
        EditorGUI.indentLevel++;
        im.barDirection = (CinematicController.BarDirection)EditorGUILayout.Popup("Fills", (int)im.barDirection,
            new[] { "Left to right", "Right to left", "Bottom to top", "Top to bottom", "From the centre out" });
        im.barFill = (CinematicController.BarFill)EditorGUILayout.Popup(new GUIContent("Loading", "Stutter: jumps forward and stalls, " +
                         "like a real loading bar (same every time)"), (int)im.barFill, new[] { "Steady", "Slows down", "Smooth start and end", "Stutter" });
        im.barSegments = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Blocks", "Split the fill into blocks that light up one by one. 0 = smooth"),
                                                              im.barSegments), 0, 100);
        if (im.barSegments > 0) im.barGap = Mathf.Max(0f, EditorGUILayout.FloatField("Gap between blocks (px)", im.barGap));
        im.barFrame = EditorGUILayout.Toggle("Frame", im.barFrame);
        im.barFillColor = EditorGUILayout.ColorField(new GUIContent("Fill colour", "Multiplied with the overlay colour. White = same as the frame"), im.barFillColor);
        im.barTrack = EditorGUILayout.Slider(new GUIContent("Empty part", "How visible the not-yet-filled part (or blocks) is"), im.barTrack, 0f, 1f);
        EditorGUI.indentLevel--;
    }

    void AnimationUI(CinematicController.ImageLayer im)
    {
        if (!Section(im, "anim", "Animation", true)) return;
        EditorGUI.indentLevel++;
        GUILayout.BeginHorizontal();
        im.inEffect = (CinematicController.OverlayTransition)EditorGUILayout.Popup("Comes in", (int)im.inEffect, InOutNames);
        if (im.inEffect != CinematicController.OverlayTransition.None)
            im.inTime = Mathf.Max(0.01f, EditorGUILayout.FloatField(im.inTime, GUILayout.Width(50f)));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        im.outEffect = (CinematicController.OverlayTransition)EditorGUILayout.Popup("Goes out", (int)im.outEffect, InOutNames);
        if (im.outEffect != CinematicController.OverlayTransition.None)
            im.outTime = Mathf.Max(0.01f, EditorGUILayout.FloatField(im.outTime, GUILayout.Width(50f)));
        GUILayout.EndHorizontal();

        var loopWas = im.loopEffect;
        im.loopEffect = (CinematicController.OverlayLoop)EditorGUILayout.Popup(new GUIContent("While showing", "Keeps doing this the whole time it's on screen"),
                                                                              (int)im.loopEffect, LoopNames);
        if (im.loopEffect != loopWas) im.loopRate = LoopDefaultRate[(int)im.loopEffect];
        if (im.loopEffect != CinematicController.OverlayLoop.None)
        {
            string rateLabel = im.loopEffect == CinematicController.OverlayLoop.Flicker ? "How often" :
                               im.loopEffect == CinematicController.OverlayLoop.Glitch ? "Glitches per second" :
                               im.loopEffect == CinematicController.OverlayLoop.Shake ? "Jitters per second" : "Times per second";
            im.loopRate = Mathf.Max(0.01f, EditorGUILayout.FloatField(rateLabel, im.loopRate));
            im.loopStrength = EditorGUILayout.Slider("Strength", im.loopStrength, 0f, 1f);
        }
        Hint("The number next to Comes in / Goes out is how many seconds it takes. Wipe reveals it left to right " +
             "(text: letter by letter).");

        // Quick combinations
        int combo = WrapButtons(new[]
        {
            new GUIContent("Hologram", "Flickers on, glitches now and then, glows"),
            new GUIContent("Alert", "Pops in, blinks, red glow"),
            new GUIContent("Terminal", "Wipes in, small flicker"),
            new GUIContent("Smooth", "Slides up, floats gently"),
            new GUIContent("Plain", "Just fades in and out")
        }, 18f, new GUIContent("Quick looks"));
        if (combo == 0)
        {
            im.inEffect = CinematicController.OverlayTransition.Flicker; im.inTime = 0.4f;
            im.outEffect = CinematicController.OverlayTransition.Glitch; im.outTime = 0.3f;
            im.loopEffect = CinematicController.OverlayLoop.Glitch; im.loopRate = 0.8f; im.loopStrength = 0.5f;
            if (im.glow <= 0f) { im.glow = 0.8f; im.glowSize = 10f; }
        }
        else if (combo == 1)
        {
            im.inEffect = CinematicController.OverlayTransition.Pop; im.inTime = 0.25f;
            im.outEffect = CinematicController.OverlayTransition.Fade; im.outTime = 0.2f;
            im.loopEffect = CinematicController.OverlayLoop.Blink; im.loopRate = 2f; im.loopStrength = 0.85f;
            im.glow = 1f; im.glowSize = 14f; im.glowOwnColours = false; im.glowColor = new Color(1f, 0.15f, 0.1f, 1f);
        }
        else if (combo == 2)
        {
            im.inEffect = CinematicController.OverlayTransition.Wipe; im.inTime = 0.6f;
            im.outEffect = CinematicController.OverlayTransition.Flicker; im.outTime = 0.25f;
            im.loopEffect = CinematicController.OverlayLoop.Flicker; im.loopRate = 3f; im.loopStrength = 0.4f;
            if (im.kind == CinematicController.OverlayKind.Text) { im.cursor = true; im.typewriter = true; }
        }
        else if (combo == 3)
        {
            im.inEffect = CinematicController.OverlayTransition.SlideUp; im.inTime = 0.5f;
            im.outEffect = CinematicController.OverlayTransition.Fade; im.outTime = 0.4f;
            im.loopEffect = CinematicController.OverlayLoop.Float; im.loopRate = 0.4f; im.loopStrength = 0.4f;
        }
        else if (combo == 4)
        {
            im.inEffect = im.outEffect = CinematicController.OverlayTransition.Fade; im.inTime = im.outTime = 0.25f;
            im.loopEffect = CinematicController.OverlayLoop.None;
        }
        if (combo >= 0) GUI.changed = true;
        EditorGUI.indentLevel--;
    }

    void GlowUI(CinematicController.ImageLayer im)
    {
        if (!Section(im, "glow", im.glow > 0f ? "Glow  (on)" : "Glow", im.glow > 0f)) return;
        EditorGUI.indentLevel++;
        im.glow = EditorGUILayout.Slider(new GUIContent("Amount", "0 = off. Follows the overlay's visible shape, so transparent " +
                                                         "parts of an image don't glow"), im.glow, 0f, 2f);
        if (im.glow > 0f)
        {
            im.glowSize = Mathf.Clamp(EditorGUILayout.FloatField(new GUIContent("Size (px)", "How far it spreads, in pixels at 1080p"), im.glowSize), 1f, 80f);
            im.glowOwnColours = EditorGUILayout.Toggle(new GUIContent("Use its own colours", "Bloom in the overlay's own colours " +
                                                                       "instead of one glow colour"), im.glowOwnColours);
            Color gc = EditorGUILayout.ColorField(new GUIContent(im.glowOwnColours ? "Brightness (alpha)" : "Glow colour",
                                                                 "The alpha is how bright the glow is"), im.glowColor);
            im.glowColor = gc;
        }
        EditorGUI.indentLevel--;
    }

    void AddOverlay(CinematicController.OverlayKind kind, Texture tex, Vector2? center, float start)
    {
        Record("Add Overlay");
        if (c.images == null) c.images = new List<CinematicController.ImageLayer>();
        var im = new CinematicController.ImageLayer
        {
            kind = kind,
            texture = tex,
            start = Mathf.Clamp(Snap(start), 0f, Mathf.Max(0f, Total() - 0.1f)),
            duration = 3f
        };
        ApplyKindDefaults(im);
        if (tex != null)
        {
            im.name = tex.name;
            FitToTexture(im);
        }
        if (center.HasValue)
        {
            Rect r = im.rect;
            r.x = Mathf.Clamp(center.Value.x - r.width * 0.5f, 0f, 1f - r.width);
            r.y = Mathf.Clamp(center.Value.y - r.height * 0.5f, 0f, 1f - r.height);
            im.rect = r;
        }
        c.images.Add(im);
        Changed();
        GUIUtility.ExitGUI();
    }

    // Sensible starting name, size and colour for each kind
    void ApplyKindDefaults(CinematicController.ImageLayer im)
    {
        int n = c.images != null ? c.images.Count + 1 : 1;
        var cyan = new Color(0.45f, 1f, 0.85f, 1f);
        im.effectsVersion = 1;
        im.inEffect = im.outEffect = CinematicController.OverlayTransition.Fade;
        im.inTime = im.outTime = 0.25f;
        switch (im.kind)
        {
            case CinematicController.OverlayKind.Text:
                im.name = $"Text {n}";
                if (string.IsNullOrEmpty(im.text)) im.text = "INITIALISING SYSTEMS...";
                im.rect = new Rect(0.05f, 0.82f, 0.6f, 0.05f);
                im.color = cyan;
                im.animation = CinematicController.ImageAnimation.None;
                im.inEffect = im.outEffect = CinematicController.OverlayTransition.None;
                im.glowColor = cyan;
                break;
            case CinematicController.OverlayKind.Waveform:
                im.name = $"Waveform {n}";
                im.rect = new Rect(0.5f, 0.45f, 0.3f, 0.1f);
                im.color = cyan;
                im.thickness = 2f;
                break;
            case CinematicController.OverlayKind.ProgressBar:
                im.name = $"Progress bar {n}";
                im.rect = new Rect(0.3f, 0.6f, 0.4f, 0.035f);
                im.barText = "INITIALISING SYSTEMS...";
                im.showPercent = true;
                im.fillTime = 2f; // loading takes 2s, then it stays full for the rest of its 3s
                im.color = cyan;
                im.thickness = 2f;
                break;
            case CinematicController.OverlayKind.UIObject:
                im.name = $"UI object {n}";
                break;
            default:
                im.name = $"Image {n}";
                im.rect = new Rect(0.35f, 0.35f, 0.3f, 0.3f);
                im.color = Color.white;
                break;
        }
    }

    // Match the box to the picture's proportions (the screen is 16:9), keeping its width and centre
    static void FitToTexture(CinematicController.ImageLayer im)
    {
        if (im.texture == null || im.texture.width == 0 || im.texture.height == 0) return;
        Rect r = im.rect;
        Vector2 center = r.center;
        float h = r.width * (16f / 9f) * im.texture.height / im.texture.width;
        float w = r.width;
        if (h > 1f) { w *= 1f / h; h = 1f; }
        r.size = new Vector2(w, h);
        r.center = center;
        r.x = Mathf.Clamp(r.x, 0f, 1f - r.width);
        r.y = Mathf.Clamp(r.y, 0f, 1f - r.height);
        im.rect = r;
    }

    void AddPopup(Shot s)
    {
        Record("Add Pop-up");
        var p = new CinematicController.Popup { name = $"Pop-up {s.popups.Count + 1}" };
        s.popups.Add(p);
        CapturePopup(s, s.popups.Count - 1);
        Changed();
        GUIUtility.ExitGUI();
    }

    void CapturePopup(Shot s, int j)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
        {
            ShowNotification(new GUIContent("Open a Scene view first"));
            return;
        }

        var p = s.popups[j];
        if (p.point == null)
        {
            var go = new GameObject($"{s.name} {p.name}");
            Undo.RegisterCreatedObjectUndo(go, "Create Pop-up Point"); CinematicPlayModeEdits.Created(go);
            go.transform.SetParent(GetPointsParent(), false);
            Record("Capture Pop-up");
            p.point = go.transform;
        }

        Undo.RecordObject(p.point, "Capture Pop-up"); CinematicPlayModeEdits.Moved(p.point);
        p.point.SetPositionAndRotation(sv.camera.transform.position, sv.camera.transform.rotation);
        ShowNotification(new GUIContent($"Captured {p.name}"), 0.8f);
        Changed();
    }

    void RemovePopup(Shot s, int j)
    {
        Record("Remove Pop-up");
        Transform t = s.popups[j].point;
        s.popups.RemoveAt(j);
        DestroyIfUnused(t);
        Changed();
        GUIUtility.ExitGUI();
    }

    // Look for one multiview view: "same as view 1" until customised
    void DrawViewFeed(Shot s, int i)
    {
        if (s.viewFeeds == null) s.viewFeeds = new List<CinematicController.Feed>();
        var vf = CinematicController.CustomViewFeed(s, i); // null = same as view 1

        GUILayout.BeginHorizontal();
        GUILayout.Space(56f);
        GUILayout.BeginVertical();
        rowInset += 56f;
        if (vf == null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Look: same as view 1", hintStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Customise", EditorStyles.miniButton, GUILayout.Width(74f)))
            {
                Record("Customise View Look");
                while (s.viewFeeds.Count <= i) s.viewFeeds.Add(new CinematicController.Feed());
                var copy = new CinematicController.Feed();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(s.feed ?? new CinematicController.Feed()), copy);
                copy.customised = true; // starts as a copy of view 1's look
                s.viewFeeds[i] = copy;
                Changed();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            DrawFeed(vf, selected + "/view" + i);
            if (GUILayout.Button("Same as view 1", EditorStyles.miniButton, GUILayout.Width(100f)))
            {
                Record("Reset View Look");
                vf.customised = false; // back to the shot's own look (kept, in case you customise it again)
                Changed();
            }
        }
        rowInset = Mathf.Max(0f, rowInset - 56f);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    // Camera feed look controls for any camera: toggle, quick presets, and optional fine-tuning
    void DrawFeed(CinematicController.Feed f, string key)
    {
        var tmp = new CinematicController.Feed();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(f), tmp);
        bool open = openFeeds.Contains(key);
        string preset = null;

        EditorGUI.BeginChangeCheck();
        GUILayout.BeginHorizontal();
        tmp.look = EditorGUILayout.ToggleLeft("Camera feed look", tmp.look, EditorStyles.boldLabel, GUILayout.Width(140f));
        if (RowWidth < 320f) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
        GUILayout.FlexibleSpace();
        // Copy this look, paste it onto another camera (here, every shot, or everything)
        if (GUILayout.Button(new GUIContent("Copy", "Copy this camera look (all its tweaks)"), EditorStyles.miniButtonLeft, GUILayout.Width(42f)))
        {
            SessionState.SetString(LookClipboardKey, EditorJsonUtility.ToJson(f));
            ShowNotification(new GUIContent("Camera look copied"), 1.0);
        }
        bool canPaste = !string.IsNullOrEmpty(SessionState.GetString(LookClipboardKey, ""));
        using (new EditorGUI.DisabledScope(!canPaste))
            if (GUILayout.Button(new GUIContent("Paste ▾", "Paste a copied camera look here or onto many cameras at once"),
                                 EditorStyles.miniButtonRight, GUILayout.Width(56f)))
                ShowPasteLookMenu(f);
        GUILayout.Space(4f);
        if (GUILayout.Button(open ? "Tweak ▴" : "Tweak ▾", EditorStyles.miniButton, GUILayout.Width(62f)))
        {
            if (open) openFeeds.Remove(key); else openFeeds.Add(key);
            open = !open;
        }
        GUILayout.EndHorizontal();

        // One-click looks (they also switch the look on)
        int lp = WrapButtons(Contents(LookPresets));
        if (lp >= 0) preset = LookPresets[lp];

        // Turned on for the first time on a blank look: start from the CCTV preset
        if (tmp.look && !f.look && tmp.tint.maxColorComponent <= 0f) preset = "CCTV";

        if (open)
        {
            EditorGUI.indentLevel++;
            if (tmp.look)
            {
                tmp.tint = EditorGUILayout.ColorField("Tint", tmp.tint);
                tmp.desaturate = EditorGUILayout.Slider("Desaturate", tmp.desaturate, 0f, 1f);
                tmp.scanlines = EditorGUILayout.Slider("Scanlines", tmp.scanlines, 0f, 1f);
                if (tmp.scanlines > 0f)
                {
                    EditorGUI.indentLevel++;
                    tmp.scanlineCount = EditorGUILayout.IntSlider(new GUIContent("How many", "Number of lines from the top to the bottom of the " +
                                                                  "picture (at any resolution). Fewer = thicker, chunkier lines"), tmp.scanlineCount, 20, 1080);
                    EditorGUI.indentLevel--;
                }
                tmp.noise = EditorGUILayout.Slider("Noise", tmp.noise, 0f, 1f);
                tmp.chromatic = EditorGUILayout.Slider("Colour split", tmp.chromatic, 0f, 1f);
                tmp.distortion = EditorGUILayout.Slider("Lens distortion", tmp.distortion, 0f, 1f);
                tmp.vignette = EditorGUILayout.Slider("Vignette", tmp.vignette, 0f, 1f);
                tmp.glitch = EditorGUILayout.Slider("Glitch", tmp.glitch, 0f, 1f);
                tmp.heat = EditorGUILayout.Slider("Thermal", tmp.heat, 0f, 1f);
                tmp.blur = EditorGUILayout.Slider("Softness", tmp.blur, 0f, 1f);
                tmp.recDot = EditorGUILayout.Toggle("REC dot", tmp.recDot);
            }
            tmp.border = Mathf.Max(0f, EditorGUILayout.FloatField("Frame (px)", tmp.border));
            if (tmp.border > 0f) tmp.borderColor = EditorGUILayout.ColorField("Frame colour", tmp.borderColor);
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck() || preset != null)
        {
            if (preset != null) ApplyLookPreset(tmp, preset);
            Record("Edit Camera Look");
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(tmp), f);
            Changed();
        }
    }

    const string LookClipboardKey = "Cinematic.LookClipboard";

    void ShowPasteLookMenu(CinematicController.Feed target)
    {
        string json = SessionState.GetString(LookClipboardKey, "");
        if (string.IsNullOrEmpty(json)) return;
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Paste here"), false, () =>
        {
            Record("Paste Camera Look");
            bool wasCustom = target.customised; // a pasted look doesn't change whether a view has its own look
            EditorJsonUtility.FromJsonOverwrite(json, target);
            target.customised = wasCustom;
            Changed();
        });
        menu.AddItem(new GUIContent("Paste onto every shot's camera"), false, () =>
        {
            Record("Paste Camera Look");
            foreach (var sh in c.shots) { if (sh.feed == null) sh.feed = new CinematicController.Feed(); EditorJsonUtility.FromJsonOverwrite(json, sh.feed); }
            Changed();
        });
        menu.AddItem(new GUIContent("Paste onto every camera (shots, multiview views, pop-ups)"), false, () =>
        {
            Record("Paste Camera Look");
            foreach (var sh in c.shots)
            {
                if (sh.feed == null) sh.feed = new CinematicController.Feed();
                EditorJsonUtility.FromJsonOverwrite(json, sh.feed);
                if (sh.viewFeeds != null) foreach (var vf in sh.viewFeeds) if (vf != null && vf.customised) { EditorJsonUtility.FromJsonOverwrite(json, vf); vf.customised = true; }
                if (sh.popups != null) foreach (var pp in sh.popups) if (pp != null && pp.feed != null) EditorJsonUtility.FromJsonOverwrite(json, pp.feed);
            }
            Changed();
        });
        menu.ShowAsContext();
    }

    static void ApplyLookPreset(CinematicController.Feed f, string preset)
    {
        f.look = true;
        f.heat = 0f;
        f.blur = 0f;
        switch (preset)
        {
            case "Thermal":
                Set(f, Color.white, 0f, 0.3f, 0.25f, 0.2f, 0.3f, 0.4f, 0.05f, false);
                f.heat = 1f;
                break;
            case "VHS":
                Set(f, new Color(1f, 0.95f, 0.88f), 0.25f, 0.65f, 0.5f, 0.7f, 0.25f, 0.5f, 0.3f, true);
                f.blur = 0.5f;
                break;
            case "Clean frame":
                f.look = false; // no camera effects, just a frame
                if (f.border <= 0f) f.border = 3f;
                break;
            case "Night":
                Set(f, new Color(0.55f, 1f, 0.6f), 1f, 0.35f, 0.6f, 0.15f, 0.35f, 0.8f, 0.05f, true);
                f.blur = 0.3f;
                break;
            case "Robot":
                Set(f, new Color(0.6f, 0.9f, 1f), 0.35f, 0.7f, 0.25f, 0.6f, 0.45f, 0.4f, 0.2f, false);
                break;
            case "Glitch":
                Set(f, new Color(0.9f, 1f, 1f), 0.4f, 0.6f, 0.5f, 0.8f, 0.3f, 0.5f, 0.85f, true);
                break;
            default: // CCTV
                Set(f, new Color(0.85f, 1f, 0.95f), 0.7f, 0.5f, 0.35f, 0.35f, 0.3f, 0.55f, 0.08f, true);
                break;
        }
    }

    static void Set(CinematicController.Feed f, Color tint, float desat, float scan, float noise, float chroma,
                    float distort, float vignette, float glitch, bool rec)
    {
        f.tint = tint;
        f.desaturate = desat;
        f.scanlines = scan;
        f.noise = noise;
        f.chromatic = chroma;
        f.distortion = distort;
        f.vignette = vignette;
        f.glitch = glitch;
        f.recDot = rec;
    }

    void DrawGridPreview(Rect r, int n, int cols, int rows, float gap, Color gapColor)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, gap > 0f ? new Color(gapColor.r, gapColor.g, gapColor.b, 1f) : new Color(0f, 0f, 0f, 0.5f));
        for (int i = 0; i < n; i++)
        {
            // Same layout as the real grid (viewport space, y up), drawn into this small box
            Rect v = CinematicController.Cell(i, cols, rows, gap);
            Rect cell = new Rect(r.x + v.x * r.width + 1f, r.y + (1f - v.yMax) * r.height + 1f,
                                 v.width * r.width - 2f, v.height * r.height - 2f);
            EditorGUI.DrawRect(cell, i == 0 ? new Color(Accent.r, Accent.g, Accent.b, 0.8f) : Ink(0.18f));
            GUI.Label(cell, (i + 1).ToString(), overlayCenter);
        }
    }

    void AddView(Shot s)
    {
        Record("Add View");
        if (s.extraViews == null) s.extraViews = new List<Transform>();
        s.extraViews.Add(null);
        CaptureView(s, s.extraViews.Count - 1);
        Changed();
        GUIUtility.ExitGUI();
    }

    void CaptureView(Shot s, int i)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
        {
            ShowNotification(new GUIContent("Open a Scene view first"));
            return;
        }

        if (s.extraViews[i] == null)
        {
            var go = new GameObject($"{s.name} View {i + 2}");
            Undo.RegisterCreatedObjectUndo(go, "Create View Point"); CinematicPlayModeEdits.Created(go);
            go.transform.SetParent(GetPointsParent(), false);
            Record("Capture View");
            s.extraViews[i] = go.transform;
        }

        Transform t = s.extraViews[i];
        Undo.RecordObject(t, "Capture View"); CinematicPlayModeEdits.Moved(t);
        t.SetPositionAndRotation(sv.camera.transform.position, sv.camera.transform.rotation);
        ShowNotification(new GUIContent($"Captured view {i + 2}"), 0.8f);
        Changed();
    }

    void RemoveView(Shot s, int i)
    {
        Record("Remove View");
        Transform t = s.extraViews[i];
        s.extraViews.RemoveAt(i);
        if (s.viewFeeds != null && i < s.viewFeeds.Count) s.viewFeeds.RemoveAt(i);
        DestroyIfUnused(t);
        Changed();
        GUIUtility.ExitGUI();
    }

    // Deletes a point we created, unless another shot or view still uses it
    void DestroyIfUnused(Transform t)
    {
        if (t == null || t.parent == null || t.parent.name != PointsParentName) return;
        foreach (Shot o in c.shots)
            if (o.point == t || (o.extraViews != null && o.extraViews.Contains(t)) ||
                (o.popups != null && o.popups.Exists(pp => pp.point == t))) return;
        CinematicPlayModeEdits.Destroying(t.gameObject);
        Undo.DestroyObjectImmediate(t.gameObject);
    }

    // Adds a motion clip from a preset, starting at "local" seconds into the shot
    void AddMotionPreset(Shot s, string preset, float local)
    {
        Record("Add Motion");
        if (s.motions == null) s.motions = new List<CinematicController.Motion>();
        var mo = new CinematicController.Motion { name = preset, start = Mathf.Clamp(local, 0f, Mathf.Max(0f, s.duration - 0.1f)) };
        switch (preset)
        {
            case "Pan left": mo.kind = CinematicController.MotionKind.Rotate; mo.speed = new Vector3(0f, -8f, 0f); break;
            case "Pan right": mo.kind = CinematicController.MotionKind.Rotate; mo.speed = new Vector3(0f, 8f, 0f); break;
            case "Tilt up": mo.kind = CinematicController.MotionKind.Rotate; mo.speed = new Vector3(-5f, 0f, 0f); break;
            case "Tilt down": mo.kind = CinematicController.MotionKind.Rotate; mo.speed = new Vector3(5f, 0f, 0f); break;
            case "Roll": mo.kind = CinematicController.MotionKind.Rotate; mo.speed = new Vector3(0f, 0f, 6f); break;
            case "Push in": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(0f, 0f, 0.5f); break;
            case "Pull out": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(0f, 0f, -0.5f); break;
            case "Truck left": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(-0.5f, 0f, 0f); break;
            case "Truck right": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(0.5f, 0f, 0f); break;
            case "Rise": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(0f, 0.4f, 0f); mo.local = false; break;
            case "Lower": mo.kind = CinematicController.MotionKind.Drift; mo.speed = new Vector3(0f, -0.4f, 0f); mo.local = false; break;
            case "Orbit":
                mo.kind = CinematicController.MotionKind.Orbit;
                mo.orbitSpeed = 10f;
                if (s.lookAt == null) ShowNotification(new GUIContent("Orbit needs a Look at target (Camera tab → Aim)"));
                break;
            case "Dolly zoom":
                // Move in while widening the lens: the background stretches, the subject stays a similar size
                mo.kind = CinematicController.MotionKind.Drift;
                mo.speed = new Vector3(0f, 0f, 0.8f);
                mo.smooth = false;
                s.animateFov = true;
                s.endFov = Mathf.Min(s.fov + 30f, 120f);
                s.fovStart = mo.start;
                s.fovDuration = 0f;
                s.fovEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                break;
            case "Handheld":
                mo.kind = CinematicController.MotionKind.Shake;
                mo.shakeAmount = 0.6f;
                mo.shakeSpeed = 1.2f;
                mo.smooth = false;
                break;
            case "Impact shake":
                mo.kind = CinematicController.MotionKind.Shake;
                mo.shakeAmount = 3f;
                mo.shakeSpeed = 12f;
                mo.shakeFades = true;
                mo.duration = Mathf.Min(0.7f, s.duration - mo.start);
                mo.smooth = false;
                break;
        }

        // A clip of the same kind still running at this point ends where the new one starts (pan left, then pan right)
        if (mo.kind != CinematicController.MotionKind.Shake)
            foreach (var other in s.motions)
            {
                if (other == null || other.kind != mo.kind) continue;
                float end = CinematicController.MotionEnd(other, s.duration);
                if (other.start < mo.start - 0.01f && end > mo.start + 0.01f) other.duration = mo.start - other.start;
            }

        s.motions.Add(mo);
        Focus(mo);
        selItem = mo;
        Changed();
        GUIUtility.ExitGUI();
    }

    static AnimationCurve EasePresets()
    {
        AnimationCurve r = null;
        int k = WrapButtons(Contents("Linear", "Smooth", "Ease in", "Ease out"), 18f, new GUIContent(" "));
        if (k == 0) r = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        else if (k == 1) r = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        else if (k == 2) r = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));
        else if (k == 3) r = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));
        return r;
    }

    void BeginCard(string title, string icon = null)
    {
        GUILayout.BeginVertical(cardStyle);
        if (title != null)
        {
            EditorGUIUtility.SetIconSize(new Vector2(14f, 14f));
            GUILayout.Label(icon != null ? IconLabel(title, icon) : new GUIContent(title), cardTitle);
            EditorGUIUtility.SetIconSize(Vector2.zero);
            GUILayout.Space(2f);
        }
    }

    static void EndCard() => GUILayout.EndVertical();

    void Hint(string text) => GUILayout.Label(text, hintStyle);

    // ---------------------------------------------------------------- Shot operations

    void Record(string name)
    {
        Undo.RecordObject(c, name);
        CinematicPlayModeEdits.Changed(c); // kept after Play Mode ends
    }

    void Changed()
    {
        EditorUtility.SetDirty(c);
        SetTime(time);
    }

    void AddShot(bool fromSceneView)
    {
        Record("Add Shot");
        Shot prev = c.shots.Count > 0 ? c.shots[c.shots.Count - 1] : null;
        var s = new Shot { name = $"Shot {c.shots.Count + 1}" };
        if (prev != null)
        {
            s.duration = prev.duration;
            s.fov = prev.fov;
            s.transition = prev.transition;
            s.transitionDuration = prev.transitionDuration;
        }
        c.shots.Add(s);
        selected = c.shots.Count - 1;
        if (fromSceneView) CapturePoint(selected);
        scrollX = float.MaxValue; // clamped on draw: scrolls to the end
        Changed();
    }

    void CapturePoint(int index)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null)
        {
            ShowNotification(new GUIContent("Open a Scene view first"));
            return;
        }

        Shot s = c.shots[index];
        if (s.point == null)
        {
            var go = new GameObject($"{s.name} Point");
            Undo.RegisterCreatedObjectUndo(go, "Create Shot Point"); CinematicPlayModeEdits.Created(go);
            go.transform.SetParent(GetPointsParent(), false);
            Record("Capture Shot");
            s.point = go.transform;
        }

        Undo.RecordObject(s.point, "Capture Shot"); CinematicPlayModeEdits.Moved(s.point);
        s.point.SetPositionAndRotation(sv.camera.transform.position, sv.camera.transform.rotation);
        ShowNotification(new GUIContent($"Captured {s.name}"), 0.8f);
        Changed();
    }

    void MoveShot(int dir)
    {
        MoveShotTo(selected, selected + dir);
        GUIUtility.ExitGUI();
    }

    // Moves a shot to a new position; overlays and sounds that start in a shot move with it
    void MoveShotTo(int from, int to)
    {
        if (from < 0 || from >= c.shots.Count) return;
        to = Mathf.Clamp(to, 0, c.shots.Count - 1);
        if (to == from) return;
        Record("Move Shot");
        var anchors = CaptureAnchors();
        Shot sh = c.shots[from];
        c.shots.RemoveAt(from);
        c.shots.Insert(to, sh);
        selected = to;
        RestoreAnchors(anchors);
        SetTime(StartOf(to));
        Changed();
    }

    // Where a dragged shot would go: its new index among the other shots
    int ReorderIndexAt(int from, float t)
    {
        int index = 0;
        float acc = 0f;
        for (int i = 0; i < c.shots.Count; i++)
        {
            float d = c.shots[i].duration;
            if (i != from && t > acc + d * 0.5f) index++;
            acc += d;
        }
        return index;
    }

    // Time of the gap a dragged shot would drop into (drawn as a white line)
    float ReorderLineTime(int from, int target)
    {
        int k = 0;
        float acc = 0f;
        for (int i = 0; i < c.shots.Count; i++)
        {
            if (i == from) { acc += c.shots[i].duration; continue; }
            if (k == target) return acc;
            acc += c.shots[i].duration;
            k++;
        }
        return acc;
    }

    // Overlays and sounds belong to the shot they start in, so they follow it when shots move or change length
    class Anchor
    {
        public object item;
        public Shot shot;
        public float offset;
    }

    List<Anchor> CaptureAnchors()
    {
        var list = new List<Anchor>();
        if (c.shots.Count == 0) return list;
        for (int j = 0; c.images != null && j < c.images.Count; j++)
        {
            var im = c.images[j];
            if (im == null) continue;
            int si = ShotAt(im.start + 0.0001f, out _);
            list.Add(new Anchor { item = im, shot = c.shots[si], offset = im.start - StartOf(si) });
        }
        for (int j = 0; c.sounds != null && j < c.sounds.Count; j++)
        {
            var snd = c.sounds[j];
            if (snd == null) continue;
            int si = ShotAt(snd.start + 0.0001f, out _);
            list.Add(new Anchor { item = snd, shot = c.shots[si], offset = snd.start - StartOf(si) });
        }
        return list;
    }

    void RestoreAnchors(List<Anchor> anchors)
    {
        foreach (Anchor a in anchors)
        {
            int si = c.shots.IndexOf(a.shot);
            if (si < 0) continue; // its shot was deleted: it stays where it is
            float start = Mathf.Max(0f, StartOf(si) + a.offset);
            if (a.item is CinematicController.ImageLayer im) im.start = start;
            else if (a.item is CinematicController.SoundClip snd) snd.start = start;
        }
    }

    // Adds a new shot at this position, framed like the Scene view
    void InsertShot(int index)
    {
        index = Mathf.Clamp(index, 0, c.shots.Count);
        Record("Insert Shot");
        var anchors = CaptureAnchors();
        Shot near = c.shots.Count > 0 ? c.shots[Mathf.Clamp(index - 1, 0, c.shots.Count - 1)] : null;
        var s = new Shot { name = $"Shot {c.shots.Count + 1}" };
        if (near != null)
        {
            s.duration = near.duration;
            s.fov = near.fov;
            s.transition = near.transition;
            s.transitionDuration = near.transitionDuration;
        }
        c.shots.Insert(index, s);
        RestoreAnchors(anchors);
        selected = index;
        tab = Tab.Camera;
        CapturePoint(index);
        SetTime(StartOf(index));
        Changed();
    }

    void DuplicateShot()
    {
        Record("Duplicate Shot");
        Shot src = c.shots[selected];
        var copy = new Shot();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(src), copy);
        copy.name = src.name + " copy";

        // Give the copy its own point so capturing one doesn't move the other
        if (src.point != null)
        {
            GameObject go = Instantiate(src.point.gameObject, src.point.parent);
            go.name = copy.name + " Point";
            Undo.RegisterCreatedObjectUndo(go, "Duplicate Shot Point"); CinematicPlayModeEdits.Created(go);
            copy.point = go.transform;
        }

        // ...and its own multiview points
        copy.extraViews = new List<Transform>();
        if (src.extraViews != null)
        {
            foreach (Transform v in src.extraViews)
            {
                if (v == null) { copy.extraViews.Add(null); continue; }
                GameObject vgo = Instantiate(v.gameObject, v.parent);
                vgo.name = v.name + " copy";
                Undo.RegisterCreatedObjectUndo(vgo, "Duplicate View Point"); CinematicPlayModeEdits.Created(vgo);
                copy.extraViews.Add(vgo.transform);
            }
        }

        // ...and its own pop-up points
        if (copy.popups != null && src.popups != null)
        {
            for (int j = 0; j < copy.popups.Count && j < src.popups.Count; j++)
            {
                Transform pt = src.popups[j].point;
                if (pt == null) continue;
                GameObject pgo = Instantiate(pt.gameObject, pt.parent);
                pgo.name = pt.name + " copy";
                Undo.RegisterCreatedObjectUndo(pgo, "Duplicate Pop-up Point"); CinematicPlayModeEdits.Created(pgo);
                copy.popups[j].point = pgo.transform;
            }
        }

        var anchors = CaptureAnchors(); // later overlays and sounds move along
        c.shots.Insert(selected + 1, copy);
        RestoreAnchors(anchors);
        selected++;
        Changed();
        GUIUtility.ExitGUI();
    }

    void DeleteShot()
    {
        Record("Delete Shot");
        Shot s = c.shots[selected];
        var anchors = CaptureAnchors();
        c.shots.RemoveAt(selected);
        RestoreAnchors(anchors); // later overlays and sounds close the gap

        // Clean up points we created that nothing else uses
        DestroyIfUnused(s.point);
        if (s.extraViews != null)
            foreach (Transform v in s.extraViews) DestroyIfUnused(v);
        if (s.popups != null)
            foreach (var pp in s.popups) DestroyIfUnused(pp.point);

        selected = Mathf.Min(selected, c.shots.Count - 1);
        Changed();
        GUIUtility.ExitGUI();
    }

    static Transform GetPointsParent()
    {
        GameObject parent = GameObject.Find(PointsParentName);
        if (parent == null)
        {
            parent = new GameObject(PointsParentName);
            Undo.RegisterCreatedObjectUndo(parent, "Create Cinematic Points");
        }
        return parent.transform;
    }

    // ---------------------------------------------------------------- Preview / playback

    float Total()
    {
        float total = 0f;
        foreach (var s in c.shots) total += s.duration;
        return total;
    }

    float StartOf(int index)
    {
        float t = 0f;
        for (int i = 0; i < index && i < c.shots.Count; i++) t += c.shots[i].duration;
        return t;
    }

    int ShotAt(float t, out float local)
    {
        float acc = 0f;
        for (int i = 0; i < c.shots.Count; i++)
        {
            float d = c.shots[i].duration;
            if (t < acc + d || i == c.shots.Count - 1)
            {
                local = Mathf.Clamp(t - acc, 0f, d);
                return i;
            }
            acc += d;
        }
        local = 0f;
        return 0;
    }

    void SetTime(float t)
    {
        if (c == null || c.shots.Count == 0) return;
        time = Mathf.Clamp(t, 0f, Total());
        int i = ShotAt(time, out float local);
        selected = i; // settings always show the shot under the playhead
        float d = c.shots[i].duration;
        c.previewShot = i;
        c.previewScrub = d > 0f ? local / d : 1f;
        viewerDirty = true;
        Refresh();
    }

    void SetPreview(bool on)
    {
        if (Application.isPlaying) return;
        c.preview = on;
        SetTime(time);
        EditorApplication.QueuePlayerLoopUpdate(); // lets the controller move (or put back) the camera
    }

    void GoToShot(int i)
    {
        selected = i;
        if (Application.isPlaying) follow = false;
        else if (GameSync && !c.preview) SetPreview(true);
        SetTime(StartOf(i));
    }

    void TogglePlay()
    {
        if (playing)
        {
            StopPlaying();
            return;
        }
        if (c.shots.Count == 0) return;

        if (Application.isPlaying) follow = false;
        else if (GameSync && !c.preview) SetPreview(true);
        if (time >= Total() - 0.001f) time = 0f;
        playing = true;
        lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    void StopPlaying()
    {
        if (!playing) return;
        playing = false;
        EditorApplication.update -= Tick;
        if (c != null && !c.IsPlaying) c.PreviewSounds(time, false);
    }

    void Tick()
    {
        if (c == null) { StopPlaying(); return; }

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;

        SetTime(time + dt);
        if (!c.IsPlaying) c.PreviewSounds(time, true); // hear the sounds (the game plays its own while it runs)

        // Keep the playhead in view
        float px = X(time);
        if (px > timelineWidth - 30f) scrollX = time * pxPerSec - (timelineWidth - Gutter) * 0.2f;

        if (time >= Total()) StopPlaying();
        Repaint();
    }

    // Only update what needs it: the Game view when it's previewing, the Scene view, and this window
    void Refresh()
    {
        if (c != null && c.preview && !Application.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
        foreach (CinematicWindow w in openWindows)
        {
            w.viewerDirty = true;
            w.Repaint();
        }
    }

    static string Fmt(float t)
    {
        int m = (int)(t / 60f);
        return $"{m:00}:{t - m * 60f:00.00}";
    }

    // ---------------------------------------------------------------- Scene view

    void OnScene(SceneView sv)
    {
        if (c == null || openWindows.Count == 0 || openWindows[0] != this) return; // one window draws them
        int clicked = DrawScene(c, selected, true, out bool moved);
        if (clicked >= 0 && clicked != selected)
        {
            selected = clicked;
            Repaint();
        }
        if (moved)
        {
            SetTime(time);
            Repaint();
        }
    }

    public static int DrawScene(CinematicController c, int selected, bool editable, out bool moved)
    {
        moved = false;
        int clicked = -1;
        Vector3? prev = null;

        for (int i = 0; i < c.shots.Count; i++)
        {
            Shot s = c.shots[i];
            Transform pt = c.PointOf(s);
            if (pt == null) continue;

            Vector3 p = pt.position;
            float size = HandleUtility.GetHandleSize(p);
            bool sel = i == selected;

            // Path into this shot: solid = Move, dotted = Cut
            Handles.color = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            if (prev.HasValue)
            {
                if (s.transition == Transition.Move) Handles.DrawLine(prev.Value, p);
                else Handles.DrawDottedLine(prev.Value, p, 4f);
            }

            Quaternion aim = s.overrideRotation ? Quaternion.Euler(s.rotation) : pt.rotation;
            if (s.lookAt != null && s.lookAt.position != p) aim = Quaternion.LookRotation(s.lookAt.position - p);

            Handles.color = sel ? Color.white : Accent;
            Handles.ArrowHandleCap(0, p, aim, size * 0.6f, EventType.Repaint);
            if (Handles.Button(p, Quaternion.identity, size * 0.08f, size * 0.12f, Handles.SphereHandleCap)) clicked = i;
            Handles.Label(p + Vector3.up * size * 0.25f, $"{i + 1}. {s.name}", EditorStyles.whiteBoldLabel);

            if (s.lookAt != null)
            {
                Handles.color = Color.yellow;
                Handles.DrawDottedLine(p, s.lookAt.position, 2f);
            }

            if (sel)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                DrawFrustum(p, aim, s.fov, size * 1.5f);
            }

            // Multiview points, linked to their shot
            List<Transform> views = c.ViewsOf(s);
            if (views != null)
            {
                for (int k = 0; k < views.Count; k++)
                {
                    Transform v = views[k];
                    if (v == null) continue;
                    Vector3 vp = v.position;
                    float vs = HandleUtility.GetHandleSize(vp);

                    Handles.color = new Color(1f, 1f, 1f, 0.35f);
                    Handles.DrawDottedLine(p, vp, 2f);
                    Handles.color = sel ? Color.white : new Color(Accent.r, Accent.g, Accent.b, 0.7f);
                    Handles.ArrowHandleCap(0, vp, v.rotation, vs * 0.4f, EventType.Repaint);
                    if (Handles.Button(vp, Quaternion.identity, vs * 0.06f, vs * 0.1f, Handles.SphereHandleCap)) clicked = i;
                    Handles.Label(vp + Vector3.up * vs * 0.2f, $"{i + 1}.{k + 2}", EditorStyles.whiteMiniLabel);
                    if (sel)
                    {
                        Handles.color = new Color(1f, 1f, 1f, 0.4f);
                        DrawFrustum(vp, v.rotation, s.fov, vs * 1.2f);
                    }
                }
            }

            // Pop-up points, linked to their shot
            if (s.popups != null)
            {
                for (int k = 0; k < s.popups.Count; k++)
                {
                    Transform v = c.PointOf(s.popups[k]);
                    if (v == null) continue;
                    Vector3 vp = v.position;
                    float vs = HandleUtility.GetHandleSize(vp);

                    Handles.color = new Color(1f, 0.85f, 0.35f, 0.5f);
                    Handles.DrawDottedLine(p, vp, 2f);
                    Handles.color = sel ? Color.white : new Color(1f, 0.85f, 0.35f, 0.9f);
                    Handles.ArrowHandleCap(0, vp, v.rotation, vs * 0.4f, EventType.Repaint);
                    if (Handles.Button(vp, Quaternion.identity, vs * 0.06f, vs * 0.1f, Handles.SphereHandleCap)) clicked = i;
                    Handles.Label(vp + Vector3.up * vs * 0.2f, $"{i + 1} {s.popups[k].name}", EditorStyles.whiteMiniLabel);
                    if (sel)
                    {
                        Handles.color = new Color(1f, 0.85f, 0.35f, 0.5f);
                        DrawFrustum(vp, v.rotation, s.popups[k].fov > 0f ? s.popups[k].fov : 60f, vs * 1.2f);
                    }
                }
            }

            prev = p;
        }

        // Move / rotate the selected shot's point directly (follows the W / E tool)
        if (editable && selected >= 0 && selected < c.shots.Count && c.PointOf(c.shots[selected]) != null)
        {
            Transform t = c.PointOf(c.shots[selected]);
            EditorGUI.BeginChangeCheck();
            if (Tools.current == Tool.Rotate)
            {
                Quaternion r = Handles.RotationHandle(t.rotation, t.position);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Rotate Shot Point"); CinematicPlayModeEdits.Moved(t);
                    t.rotation = r;
                    moved = true;
                }
            }
            else
            {
                Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local ? t.rotation : Quaternion.identity;
                Vector3 np = Handles.PositionHandle(t.position, handleRot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Move Shot Point"); CinematicPlayModeEdits.Moved(t);
                    t.position = np;
                    moved = true;
                }
            }
        }

        return clicked;
    }

    static void DrawFrustum(Vector3 p, Quaternion r, float fov, float dist)
    {
        float h = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * dist;
        float w = h * 16f / 9f;
        Vector3 f = p + r * Vector3.forward * dist;
        Vector3 up = r * Vector3.up * h, right = r * Vector3.right * w;
        Vector3 a = f + up - right, b = f + up + right, cc = f - up + right, d = f - up - right;
        Handles.DrawLine(p, a);
        Handles.DrawLine(p, b);
        Handles.DrawLine(p, cc);
        Handles.DrawLine(p, d);
        Handles.DrawLine(a, b);
        Handles.DrawLine(b, cc);
        Handles.DrawLine(cc, d);
        Handles.DrawLine(d, a);
    }

    // ---------------------------------------------------------------- Styles

    void EnsureStyles()
    {
        if (panelTex != null && cardStyle != null && overlayStyle != null) return;

        panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
        panelTex.SetPixel(0, 0, Panel);
        panelTex.Apply();

        cardStyle = new GUIStyle
        {
            normal = { background = panelTex },
            padding = new RectOffset(12, 12, 10, 12),
            margin = new RectOffset(6, 6, 6, 6)
        };
        cardTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = Ink(0.45f) } };
        blockTitle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, clipping = TextClipping.Clip };
        blockSub = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }, clipping = TextClipping.Clip };
        timeStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = Ink(0.9f) } };
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Ink(0.9f) } };
        laneLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink(0.5f) } };
        barLabel = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0f, 0f, 0f, 0.8f) }, clipping = TextClipping.Clip };
        plusStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink(0.5f) } };
        hintStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Ink(0.45f) } };
        nameStyle = new GUIStyle(EditorStyles.textField) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        numberStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleLeft, normal = { textColor = Accent } };
        overlayStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.white }, clipping = TextClipping.Clip };
        overlayCenter = new GUIStyle(overlayStyle) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        emptyStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink(0.55f) } };
    }
}

// ==================================================================== Saving cinematics to files

/// <summary>Saves a cinematic to a CinematicAsset file and loads it back into a controller (in any scene).</summary>
static class CinematicSaving
{
    // ---------------------------------------------------------------- Save

    public static void SaveAs(CinematicController c)
    {
        string start = c.savedAs != null ? AssetDatabase.GetAssetPath(c.savedAs) : "";
        string folder = string.IsNullOrEmpty(start) ? "Assets" : System.IO.Path.GetDirectoryName(start);
        string path = EditorUtility.SaveFilePanelInProject("Save cinematic", c.name + " Cinematic", "asset",
                                                           "Save this cinematic to a file in your project.", folder);
        if (string.IsNullOrEmpty(path)) return;
        Save(c, path);
    }

    public static void Save(CinematicController c)
    {
        if (c.savedAs == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(c.savedAs))) { SaveAs(c); return; }
        Save(c, AssetDatabase.GetAssetPath(c.savedAs));
    }

    static void Save(CinematicController c, string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<CinematicAsset>(path);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<CinematicAsset>();

        // Copy everything (asset links like images, sounds and fonts stay as they are)
        var d = new CinematicAsset.Data
        {
            playOnStart = c.playOnStart, loop = c.loop, displaySortOrder = c.displaySortOrder, font = c.font,
            disableOtherCameras = c.disableOtherCameras, includeTextureCameras = c.includeTextureCameras,
            shots = c.shots, images = c.images, sounds = c.sounds
        };
        var copy = new CinematicAsset.Data();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(d), copy);

        // Links to scene objects can't live in a file: remember them as name, place and position instead
        var points = new List<CinematicAsset.PointRecord>();
        for (int i = 0; i < copy.shots.Count; i++)
        {
            var s = copy.shots[i];
            s.point = Keep(points, $"shots/{i}/point", s.point);
            s.lookAt = Keep(points, $"shots/{i}/lookAt", s.lookAt);
            if (s.extraViews != null)
                for (int j = 0; j < s.extraViews.Count; j++) s.extraViews[j] = Keep(points, $"shots/{i}/views/{j}", s.extraViews[j]);
            if (s.popups != null)
                for (int j = 0; j < s.popups.Count; j++) s.popups[j].point = Keep(points, $"shots/{i}/popups/{j}/point", s.popups[j].point);
            s.onStart = new UnityEngine.Events.UnityEvent(); // events point at scene objects: they stay with the scene
            s.onEnd = new UnityEngine.Events.UnityEvent();
            if (s.events != null) foreach (var ev in s.events) if (ev != null) ev.action = new UnityEngine.Events.UnityEvent(); // the moments are kept
            if (s.images != null) s.images.Clear();       // old-style per-shot images were moved to the cinematic's timeline
        }
        for (int i = 0; i < copy.images.Count; i++)
        {
            var im = copy.images[i];
            if (im.target == null || !im.target.scene.IsValid()) continue;
            points.Add(Record($"images/{i}/target", im.target.transform, true));
            im.target = null;
        }

        Undo.RecordObject(asset, "Save Cinematic");
        asset.data = copy;
        asset.points = points;
        int baked = asset.bakeMedia ? BakeMedia(asset) : 0;
        if (!asset.bakeMedia) { asset.media.Clear(); asset.mediaSlots.Clear(); }
        asset.sourceScene = c.gameObject.scene.name;
        asset.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (isNew) AssetDatabase.CreateAsset(asset, path);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(c, "Save Cinematic");
        c.savedAs = asset;
        EditorUtility.SetDirty(c);
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[Cinematic] Saved '{c.name}' to {path} ({copy.shots.Count} shots, {points.Count} scene links kept as positions" +
                  (asset.bakeMedia ? $", {baked} images / sounds / fonts baked in" : "") + ").", asset);
    }

    static Transform Keep(List<CinematicAsset.PointRecord> points, string slot, Transform t)
    {
        if (t == null) return null;
        if (!t.gameObject.scene.IsValid()) return t; // a prefab or other asset: can stay as it is
        points.Add(Record(slot, t, false));
        return null;
    }

    static CinematicAsset.PointRecord Record(string slot, Transform t, bool ui) => new CinematicAsset.PointRecord
    {
        slot = slot, name = t.name, path = PathOf(t), position = t.position, rotation = t.rotation, scale = t.localScale, uiObject = ui
    };

    static string PathOf(Transform t)
    {
        string p = t.name;
        for (Transform up = t.parent; up != null; up = up.parent) p = up.name + "/" + p;
        return p;
    }


    // ---------------------------------------------------------------- Baking images, sounds and fonts into the file

    // A field somewhere in the cinematic that points at an image, sound or font
    class RefSlot
    {
        public string path;
        public Type type;
        public Func<UnityEngine.Object> get;
        public Action<UnityEngine.Object> set;
    }

    static bool IsMediaType(Type t) => typeof(Texture).IsAssignableFrom(t) || typeof(AudioClip).IsAssignableFrom(t) || typeof(Font).IsAssignableFrom(t);

    // Walks the cinematic's own classes (shots, overlays, sounds...) and lists every image / sound / font field
    static void FindMediaSlots(object obj, string path, List<RefSlot> slots, int depth)
    {
        if (obj == null || depth > 10) return;
        foreach (FieldInfo f in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.IsNotSerialized || (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null)) continue;
            Type ft = f.FieldType;
            string p = path.Length == 0 ? f.Name : path + "." + f.Name;
            object owner = obj;
            FieldInfo field = f;
            if (IsMediaType(ft))
            {
                slots.Add(new RefSlot { path = p, type = ft, get = () => field.GetValue(owner) as UnityEngine.Object, set = v => field.SetValue(owner, v) });
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(ft) && ft.IsGenericType)
            {
                var list = f.GetValue(obj) as System.Collections.IList;
                Type et = ft.GetGenericArguments()[0];
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    if (IsMediaType(et))
                        slots.Add(new RefSlot { path = $"{p}[{i}]", type = et, get = () => list[idx] as UnityEngine.Object, set = v => list[idx] = v });
                    else if (et.Assembly == typeof(CinematicController).Assembly) FindMediaSlots(list[i], $"{p}[{i}]", slots, depth + 1);
                }
            }
            else if (ft.IsClass && ft.Assembly == typeof(CinematicController).Assembly)
                FindMediaSlots(f.GetValue(obj), p, slots, depth + 1);
        }
    }

    // Stores the original file of every image / sound / font the cinematic uses inside the asset
    static int BakeMedia(CinematicAsset asset)
    {
        var slots = new List<RefSlot>();
        FindMediaSlots(asset.data, "", slots, 0);
        var oldMedia = new Dictionary<string, CinematicAsset.BakedMedia>();
        foreach (var m in asset.media) if (m != null && !string.IsNullOrEmpty(m.guid) && !oldMedia.ContainsKey(m.guid)) oldMedia[m.guid] = m;
        asset.media = new List<CinematicAsset.BakedMedia>();
        asset.mediaSlots = new List<CinematicAsset.MediaSlot>();
        var byGuid = new Dictionary<string, int>();

        foreach (var slot in slots)
        {
            UnityEngine.Object o = slot.get();
            if (o == null) continue;
            string path = AssetDatabase.GetAssetPath(o);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(guid) || path.StartsWith("Resources/unity_builtin") || path.StartsWith("Library/"))
                continue; // built into Unity: every project has it
            if (!byGuid.TryGetValue(guid, out int index))
            {
                byte[] bytes = null;
                string full = System.IO.Path.GetFullPath(path);
                if (System.IO.File.Exists(full)) bytes = System.IO.File.ReadAllBytes(full);
                else if (oldMedia.TryGetValue(guid, out var prev)) bytes = prev.bytes; // keep what was baked before
                if (bytes == null) continue;
                index = asset.media.Count;
                asset.media.Add(new CinematicAsset.BakedMedia
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path),
                    extension = System.IO.Path.GetExtension(path),
                    kind = o is AudioClip ? "Audio" : o is Font ? "Font" : "Texture",
                    guid = guid,
                    bytes = bytes
                });
                byGuid[guid] = index;
            }
            asset.mediaSlots.Add(new CinematicAsset.MediaSlot { path = slot.path, media = index });
        }
        return asset.media.Count;
    }

    static string MediaFolder(CinematicAsset asset) => "Assets/Cinematic Media/" + asset.name;

    // Fills in images / sounds / fonts this project doesn't have, from the files baked into the asset
    static int UnpackMedia(CinematicAsset asset, CinematicAsset.Data data)
    {
        if (asset.media == null || asset.media.Count == 0 || asset.mediaSlots == null) return 0;
        var slots = new Dictionary<string, RefSlot>();
        var found = new List<RefSlot>();
        FindMediaSlots(data, "", found, 0);
        foreach (var sl in found) slots[sl.path] = sl;

        var made = new Dictionary<int, UnityEngine.Object>();
        int count = 0;
        foreach (var ms in asset.mediaSlots)
        {
            if (ms == null || !slots.TryGetValue(ms.path, out RefSlot slot) || slot.get() != null) continue; // still there: nothing to do
            if (ms.media < 0 || ms.media >= asset.media.Count) continue;
            if (!made.TryGetValue(ms.media, out UnityEngine.Object o))
            {
                o = Unpack(asset, asset.media[ms.media], slot.type);
                made[ms.media] = o;
                if (o != null) count++;
            }
            if (o != null && slot.type.IsInstanceOfType(o)) slot.set(o);
            // A waveform following this sound needs to read it
            if (o is AudioClip clip && ms.path.EndsWith("followSound") && !CinematicController.CanReadSound(clip))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
                if (imp != null)
                {
                    var st = imp.defaultSampleSettings;
                    st.loadType = AudioClipLoadType.DecompressOnLoad;
                    imp.defaultSampleSettings = st;
                    imp.SaveAndReimport();
                }
            }
        }
        if (count > 0) AssetDatabase.SaveAssets();
        return count;
    }

    static UnityEngine.Object Unpack(CinematicAsset asset, CinematicAsset.BakedMedia m, Type type)
    {
        if (m == null || m.bytes == null || m.bytes.Length == 0) return null;
        // This project may already have the very same file
        if (!string.IsNullOrEmpty(m.guid))
        {
            string existing = AssetDatabase.GUIDToAssetPath(m.guid);
            if (!string.IsNullOrEmpty(existing))
            {
                var e = AssetDatabase.LoadAssetAtPath(existing, type);
                if (e != null) return e;
            }
        }
        string folder = MediaFolder(asset);
        string full = System.IO.Path.GetFullPath(folder);
        System.IO.Directory.CreateDirectory(full);
        string file = folder + "/" + m.name + m.extension;
        // Same name already unpacked with the same bytes: reuse it; different: a new name
        if (System.IO.File.Exists(System.IO.Path.GetFullPath(file)) &&
            !BytesEqual(System.IO.File.ReadAllBytes(System.IO.Path.GetFullPath(file)), m.bytes))
            file = AssetDatabase.GenerateUniqueAssetPath(file);
        if (!System.IO.File.Exists(System.IO.Path.GetFullPath(file))) System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(file), m.bytes);
        AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath(file, type);
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---------------------------------------------------------------- Random camera points (to preview a template)

    /// <summary>Creates camera points for every shot, multiview view and pop-up that has none, scattered around what's in
    /// the scene and pointed at it. Handy to preview a cinematic loaded from another project.</summary>
    public static int FillEmptyPoints(CinematicController c)
    {
        // Where things are: around every visible object in the scene (or the origin, in an empty scene)
        Bounds b = new Bounds(Vector3.zero, Vector3.one * 10f);
        bool any = false;
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || r.gameObject.scene != c.gameObject.scene || r.GetComponent<CinematicController>() != null) continue;
            if (r.transform.root.name.EndsWith(" Points")) continue; // our own spheres
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) b = new Bounds(Vector3.zero, Vector3.one * 10f);
        float radius = Mathf.Clamp(b.extents.magnitude, 3f, 200f);

        Undo.SetCurrentGroupName("Fill Empty Camera Points");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(c, "Fill Empty Camera Points");
        Transform parent = null;
        int made = 0, seed = Environment.TickCount;
        var rng = new System.Random(seed);

        Func<string, float, Transform> Make = (name, fov) =>
        {
            if (parent == null)
            {
                parent = new GameObject(c.name + " Random Points").transform;
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent.gameObject, c.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(parent.gameObject, "Fill Empty Camera Points");
            }
            // Somewhere around the scene, a bit above it, looking at a random spot inside it
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = radius * Mathf.Lerp(0.6f, 1.3f, (float)rng.NextDouble());
            Vector3 pos = b.center + new Vector3(Mathf.Cos(ang) * dist, b.extents.y * Mathf.Lerp(0.2f, 1.2f, (float)rng.NextDouble()) + 1f, Mathf.Sin(ang) * dist);
            Vector3 look = b.center + Vector3.Scale(b.extents, new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f));
            var rec = new CinematicAsset.PointRecord { name = name, position = pos, rotation = Quaternion.LookRotation(look - pos), scale = Vector3.one * 0.3f };
            made++;
            return MakePoint(rec, parent, fov);
        };

        for (int i = 0; i < c.shots.Count; i++)
        {
            Shot s = c.shots[i];
            if (s.point == null && s.background == CinematicController.ShotBackground.Camera) s.point = Make($"Shot {i + 1}", s.fov);
            if (s.extraViews != null)
                for (int j = 0; j < s.extraViews.Count; j++)
                    if (s.extraViews[j] == null) s.extraViews[j] = Make($"Shot {i + 1} view {j + 2}", 60f);
            if (s.popups != null)
                foreach (var pp in s.popups)
                    if (pp != null && pp.point == null) pp.point = Make($"Shot {i + 1} {pp.name}", pp.fov > 0f ? pp.fov : 60f);
        }
        EditorUtility.SetDirty(c);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        Undo.CollapseUndoOperations(group);
        CinematicWindow.RepaintAll();
        return made;
    }

    // ---------------------------------------------------------------- Load

    public static void LoadWithPicker(CinematicController c)
    {
        string start = c.savedAs != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(c.savedAs)) : "Assets";
        string full = EditorUtility.OpenFilePanel("Load cinematic", start, "asset");
        if (string.IsNullOrEmpty(full)) return;
        full = System.IO.Path.GetFullPath(full).Replace('\\', '/');
        string project = System.IO.Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/').TrimEnd('/') + "/";

        // Inside this project: use it where it is. Elsewhere (Downloads, another project...): bring a copy in first,
        // since Unity can only open assets that are in the project.
        string path;
        if (full.StartsWith(project, StringComparison.OrdinalIgnoreCase)) path = full.Substring(project.Length);
        else
        {
            if (!LooksLikeCinematic(full))
            {
                EditorUtility.DisplayDialog("Load cinematic", "That file isn't a saved cinematic.", "OK");
                return;
            }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath("Assets/Cinematics"));
            path = AssetDatabase.GenerateUniqueAssetPath("Assets/Cinematics/" + System.IO.Path.GetFileName(full));
            System.IO.File.Copy(full, System.IO.Path.GetFullPath(path));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Cinematic] Copied '{full}' into the project as {path}.");
        }

        var asset = AssetDatabase.LoadAssetAtPath<CinematicAsset>(path) ?? RepairScriptLink(path);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Load cinematic",
                LooksLikeCinematic(System.IO.Path.GetFullPath(path))
                    ? "That's a cinematic file, but Unity couldn't open it. Make sure CinematicAsset.cs is in this project and has no " +
                      "compile errors (check the Console), then try again."
                    : "That file isn't a saved cinematic.", "OK");
            return;
        }
        Load(c, asset);
    }

    // ---------------------------------------------------------------- Files from another project

    // A saved cinematic, judging by its contents (works even when Unity can't open it)
    public static bool LooksLikeCinematic(string fullPath)
    {
        try
        {
            if (!System.IO.File.Exists(fullPath)) return false;
            string text = System.IO.File.ReadAllText(fullPath);
            return text.StartsWith("%YAML") && text.Contains("sourceScene:") && text.Contains("points:") && text.Contains("shots:");
        }
        catch { return false; }
    }

    // The ID of this project's CinematicAsset script
    static string ScriptGuid()
    {
        var tmp = ScriptableObject.CreateInstance<CinematicAsset>();
        var script = MonoScript.FromScriptableObject(tmp);
        UnityEngine.Object.DestroyImmediate(tmp);
        return script != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)) : null;
    }

    static readonly Regex ScriptLine = new Regex(@"m_Script: \{fileID: 11500000, guid: ([0-9a-fA-F]{32}), type: 3\}");

    /// <summary>A cinematic saved in another project points at that project's copy of the script (by an ID kept in
    /// its .meta file). If the scripts were copied without their .meta files, the IDs differ and Unity can't open it:
    /// this points it at this project's script instead.</summary>
    public static CinematicAsset RepairScriptLink(string assetPath)
    {
        string full = System.IO.Path.GetFullPath(assetPath);
        if (!LooksLikeCinematic(full)) return null;
        string guid = ScriptGuid();
        if (string.IsNullOrEmpty(guid)) return null;
        string text = System.IO.File.ReadAllText(full);
        Match m = ScriptLine.Match(text);
        if (!m.Success || m.Groups[1].Value == guid) return AssetDatabase.LoadAssetAtPath<CinematicAsset>(assetPath);
        text = ScriptLine.Replace(text, $"m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}", 1);
        System.IO.File.WriteAllText(full, text);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[Cinematic] '{assetPath}' came from another project: pointed it at this project's CinematicAsset script.");
        return AssetDatabase.LoadAssetAtPath<CinematicAsset>(assetPath);
    }

    /// <summary>Asks how to bring the camera points back, then replaces the controller's cinematic with the file's.</summary>
    public static void Load(CinematicController c, CinematicAsset asset)
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Load cinematic", "Leave Play Mode first.", "OK");
            return;
        }
        int choice = EditorUtility.DisplayDialogComplex("Load cinematic",
            $"Replace the cinematic on '{c.name}' with '{asset.name}' ({asset.data.shots.Count} shots)?\n\n" +
            "Reconnect: uses objects in this scene with the same names (e.g. the same camera points), and creates " +
            "new points for any that are missing.\n\nNew points: always creates new camera points where they were saved.\n\n" +
            "You can undo this.",
            "Reconnect", "Cancel", "New points");
        if (choice == 1) return;
        Load(c, asset, choice == 2);
    }

    public static void Load(CinematicController c, CinematicAsset asset, bool alwaysNewPoints)
    {
        Undo.SetCurrentGroupName("Load Cinematic");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(c, "Load Cinematic");

        // Keep the events each shot already has (matched by name): they point at things in this scene
        var oldEvents = new Dictionary<string, CinematicController.Shot>();
        foreach (var s in c.shots)
            if (s != null && !string.IsNullOrEmpty(s.name) && !oldEvents.ContainsKey(s.name)) oldEvents[s.name] = s;

        var copy = new CinematicAsset.Data();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(asset.data), copy);
        int unpacked = UnpackMedia(asset, copy); // images / sounds / fonts this project doesn't have
        c.playOnStart = copy.playOnStart;
        c.loop = copy.loop;
        c.displaySortOrder = copy.displaySortOrder;
        c.font = copy.font;
        c.disableOtherCameras = copy.disableOtherCameras;
        c.includeTextureCameras = copy.includeTextureCameras;
        c.shots = copy.shots ?? new List<CinematicController.Shot>();
        c.images = copy.images ?? new List<CinematicController.ImageLayer>();
        c.sounds = copy.sounds ?? new List<CinematicController.SoundClip>();
        foreach (var s in c.shots)
            if (s != null && oldEvents.TryGetValue(s.name ?? "", out var old))
            {
                s.onStart = old.onStart;
                s.onEnd = old.onEnd;
                // Timed events: keep the calls of ones with the same name
                if (s.events != null && old.events != null)
                    foreach (var ev in s.events)
                        foreach (var oe in old.events)
                            if (ev != null && oe != null && oe.name == ev.name) { ev.action = oe.action; break; }
            }

        // Camera points: reconnect or recreate
        int reconnected = 0, created = 0, missingUi = 0;
        Transform parent = null;
        var made = new Dictionary<string, Transform>(); // the same saved object used twice gets one new point
        foreach (var rec in asset.points)
        {
            Transform t = alwaysNewPoints && !rec.uiObject ? null : FindInScene(c.gameObject.scene, rec.path);
            if (t != null) reconnected++;
            else if (rec.uiObject) { missingUi++; continue; }
            else if (!made.TryGetValue(rec.path, out t))
            {
                if (parent == null)
                {
                    parent = new GameObject(asset.name + " Points").transform;
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent.gameObject, c.gameObject.scene);
                    Undo.RegisterCreatedObjectUndo(parent.gameObject, "Load Cinematic");
                }
                t = MakePoint(rec, parent, ShotFov(c, rec.slot));
                made[rec.path] = t;
                created++;
            }
            Assign(c, rec.slot, t);
        }

        c.savedAs = asset;
        EditorUtility.SetDirty(c);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        Undo.CollapseUndoOperations(group);

        string msg = $"Loaded '{asset.name}': {c.shots.Count} shots. Camera points: {reconnected} reconnected, {created} created" +
                     (created > 0 ? $" (under '{asset.name} Points')" : "") + ".";
        if (missingUi > 0) msg += $" {missingUi} UI object(s) for overlays weren't found in this scene: pick them again in those overlays.";
        if (unpacked > 0) msg += $" {unpacked} image(s) / sound(s) / font(s) unpacked into '{MediaFolder(asset)}'.";
        Debug.Log("[Cinematic] " + msg, c);
        CinematicWindow.RepaintAll();

        int empty = 0;
        foreach (var s in c.shots)
        {
            if (s.point == null && s.background == CinematicController.ShotBackground.Camera) empty++;
            if (s.extraViews != null) foreach (var v in s.extraViews) if (v == null) empty++;
            if (s.popups != null) foreach (var pp in s.popups) if (pp != null && pp.point == null) empty++;
        }
        if (empty > 0 && EditorUtility.DisplayDialog("Load cinematic",
                $"{empty} camera point(s) are empty. Fill them with random positions around the scene, just to see how it looks? " +
                "(You can do it later from File ▾, and replace them with Capture.)", "Fill with random points", "Leave empty"))
            FillEmptyPoints(c);
    }

    // Finds an object by its hierarchy path in a scene, including switched-off ones
    static Transform FindInScene(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (string.IsNullOrEmpty(path) || !scene.IsValid()) return null;
        string[] parts = path.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != parts[0]) continue;
            Transform t = root.transform;
            for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
            if (t != null) return t;
        }
        return null;
    }

    static float ShotFov(CinematicController c, string slot)
    {
        string[] p = slot.Split('/');
        if (p.Length >= 2 && p[0] == "shots" && int.TryParse(p[1], out int i) && i < c.shots.Count) return c.shots[i].fov;
        return 60f;
    }

    // A small sphere like the ones the fly camera drops, at the saved position
    static Transform MakePoint(CinematicAsset.PointRecord rec, Transform parent, float fov)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.DestroyImmediate(col);
        go.name = rec.name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(rec.position, rec.rotation);
        go.transform.localScale = rec.scale;

        // If the project has the CameraShotMarker script, add it so the point shows its frustum
        Type marker = Type.GetType("CameraShotMarker, Assembly-CSharp");
        if (marker != null)
        {
            Component m = go.AddComponent(marker);
            FieldInfo f = marker.GetField("fieldOfView");
            if (f != null && f.FieldType == typeof(float)) f.SetValue(m, fov);
        }
        Undo.RegisterCreatedObjectUndo(go, "Load Cinematic");
        return go.transform;
    }

    static void Assign(CinematicController c, string slot, Transform t)
    {
        string[] p = slot.Split('/');
        if (p.Length < 3 || !int.TryParse(p[1], out int i)) return;
        if (p[0] == "images")
        {
            if (i < c.images.Count) c.images[i].target = t != null ? t.gameObject : null;
            return;
        }
        if (p[0] != "shots" || i >= c.shots.Count) return;
        var s = c.shots[i];
        switch (p[2])
        {
            case "point": s.point = t; break;
            case "lookAt": s.lookAt = t; break;
            case "views":
                if (p.Length > 3 && int.TryParse(p[3], out int v) && s.extraViews != null && v < s.extraViews.Count) s.extraViews[v] = t;
                break;
            case "popups":
                if (p.Length > 3 && int.TryParse(p[3], out int q) && s.popups != null && q < s.popups.Count) s.popups[q].point = t;
                break;
        }
    }
}

// Cinematic files from another project get fixed as soon as they're in this one (see RepairScriptLink)
class CinematicAssetImport : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (string path in imported)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
            if (AssetDatabase.LoadAssetAtPath<CinematicAsset>(path) != null) continue; // opens fine
            string p = path;
            if (CinematicSaving.LooksLikeCinematic(System.IO.Path.GetFullPath(p)))
                EditorApplication.delayCall += () => CinematicSaving.RepairScriptLink(p);
        }
    }
}

// The saved file's Inspector: what's in it, and loading it into a controller
[CustomEditor(typeof(CinematicAsset))]
class CinematicAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var a = (CinematicAsset)target;
        float total = 0f;
        foreach (var s in a.data.shots) if (s != null) total += s.duration;
        EditorGUILayout.HelpBox($"{a.data.shots.Count} shots · {total:0.#}s · {a.data.images.Count} overlays · {a.data.sounds.Count} sounds\n" +
                                $"{a.points.Count} camera points kept as positions\n" +
                                $"Saved from scene '{a.sourceScene}' on {a.savedAt}", MessageType.Info);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("notes"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("bakeMedia"), new GUIContent("Bake images, sounds & fonts in",
            "Keep copies of the images, sounds and fonts inside this file (applies the next time it's saved), so it works in other projects"));
        serializedObject.ApplyModifiedProperties();
        if (a.media != null && a.media.Count > 0)
        {
            long bytes = 0;
            int tex = 0, aud = 0, fnt = 0;
            foreach (var m in a.media)
            {
                if (m == null) continue;
                bytes += m.bytes != null ? m.bytes.Length : 0;
                if (m.kind == "Audio") aud++; else if (m.kind == "Font") fnt++; else tex++;
            }
            EditorGUILayout.HelpBox($"Baked in: {tex} image(s), {aud} sound(s), {fnt} font(s) — {bytes / 1048576f:0.0} MB. " +
                                    "A project that doesn't have them gets them unpacked into 'Assets/Cinematic Media/' when it's loaded. " +
                                    "Camera points are kept as positions; anything else in a scene (events, UI objects) stays with its scene.",
                                    MessageType.None);
        }

        GUILayout.Space(6f);
        var controllers = UnityEngine.Object.FindObjectsByType<CinematicController>(FindObjectsSortMode.None);
        if (controllers.Length == 0)
        {
            EditorGUILayout.HelpBox("To use it, add a Cinematic Controller to a camera in this scene, then load it here " +
                                    "(or with the Cinematic window's Save / Load button).", MessageType.None);
            if (GUILayout.Button("Create a Cinematic Controller here and load it", GUILayout.Height(26f)))
            {
                var cam = Camera.main;
                GameObject go = cam != null ? cam.gameObject : new GameObject("Cinematic Camera", typeof(Camera));
                if (cam == null) Undo.RegisterCreatedObjectUndo(go, "Create Cinematic");
                var c = Undo.AddComponent<CinematicController>(go);
                CinematicSaving.Load(c, a, false);
            }
            return;
        }
        foreach (var c in controllers)
        {
            if (GUILayout.Button($"Load into '{c.name}'", GUILayout.Height(26f))) CinematicSaving.Load(c, a);
            if (GUILayout.Button(new GUIContent($"Fill '{c.name}''s empty camera points with random ones",
                    "Creates points around the scene for every shot, view and pop-up that has none, just to see how it looks")))
                CinematicSaving.FillEmptyPoints(c);
        }
    }
}

#endif
