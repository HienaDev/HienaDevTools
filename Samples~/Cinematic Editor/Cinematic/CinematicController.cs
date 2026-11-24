using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;

// Plays a list of camera shots. Put it on the camera (or assign one).
// Each shot: go to a point (cut or move), hold for a duration, optionally animate FOV,
// rotate/drift at a constant speed for part of the shot, look at a target, and fire an event when it starts.
// A shot can also be a multiview (several cameras in a grid), show pop-up cameras on top,
// and give any camera a CCTV-style feed look (needs the "Cinematic/Camera Feed" shader).
// Edit Mode preview: tick "Preview", pick a shot and drag the scrub slider. Untick to put the camera back.
[ExecuteAlways]
public class CinematicController : MonoBehaviour
{
    public enum Transition { Cut, Move, Crossfade }

    // Camera feed look (CCTV style), set per camera. Drawn by the "Cinematic/Camera Feed" shader.
    [Serializable]
    public class Feed
    {
        [Tooltip("Multiview views only: this view has its own look. Off = same as the shot's camera (view 1).")]
        public bool customised;
        [Tooltip("Show this camera as a camera feed (CCTV style).")]
        public bool look;
        public Color tint = new Color(0.85f, 1f, 0.95f, 1f);
        [Range(0f, 1f)] public float desaturate = 0.7f;
        [Range(0f, 1f)] public float scanlines = 0.5f;
        [Tooltip("How many scanlines from top to bottom (the same at any resolution). Fewer = thicker lines.")]
        [Range(20, 1080)] public int scanlineCount = 360;
        [Range(0f, 1f)] public float noise = 0.35f;
        [Range(0f, 1f)] public float chromatic = 0.35f;
        [Range(0f, 1f)] public float distortion = 0.3f;
        [Range(0f, 1f)] public float vignette = 0.55f;
        [Range(0f, 1f)] public float glitch = 0.08f;
        [Tooltip("Blinking red recording dot in the corner.")]
        public bool recDot = true;
        [Range(0f, 1f), Tooltip("Thermal / false-colour look: brightness mapped to a heat gradient.")]
        public float heat;
        [Range(0f, 1f), Tooltip("Softness: a slight blur, like a cheap or old camera.")]
        public float blur;
        [Tooltip("Frame around this camera, in pixels at 1080p. 0 = none.")]
        public float border;
        public Color borderColor = new Color(0.45f, 1f, 0.85f, 1f);

        public bool NeedsShader => look || border > 0f;
    }

    // A camera shown on top of the shot in a small window.
    [Serializable]
    public class Popup
    {
        public string name = "Pop-up";
        [Tooltip("Draw order: higher numbers draw on top (other pop-ups, the scan and overlays too).")]
        public int layer;
        public Transform point;
        [Tooltip("Position and size on screen, 0-1 from the top-left corner.")]
        public Rect rect = new Rect(0.56f, 0.07f, 0.38f, 0.38f);
        public float fov = 60f;
        [Tooltip("Seconds into the shot.")]
        public float appearAt = 0.5f;
        [Tooltip("0 = until the end of the shot.")]
        public float duration;
        [Tooltip("Pop in and out instead of appearing instantly.")]
        public bool animate = true; // older versions: pop in / out (now inEffect / outEffect)
        [Tooltip("How it comes in.")]
        public OverlayTransition inEffect = OverlayTransition.Pop;
        public float inTime = 0.2f;
        [Tooltip("How it goes out (when it has its own length; \"until the end of the shot\" cuts with the shot).")]
        public OverlayTransition outEffect = OverlayTransition.Pop;
        public float outTime = 0.2f;
        [HideInInspector] public int effectsVersion; // 0 = made before in / out effects (uses animate)
        public Feed feed = new Feed { look = true, border = 3f };
        [NonSerialized] public Transform runtimePoint; // set from code with SetPopupPoint
    }

    public enum ImageAnimation { None, Fade, Pop }

    /// <summary>How an overlay comes in or goes out.</summary>
    public enum OverlayTransition { None, Fade, Pop, Zoom, SlideLeft, SlideRight, SlideUp, SlideDown, Wipe, Flicker, Glitch }
    /// <summary>What an overlay keeps doing while it's on screen.</summary>
    public enum OverlayLoop { None, Blink, Pulse, Flicker, Float, Shake, Glitch }
    public enum WaveStyle { Line, Bars, Filled }
    public enum TextAlignH { Left, Centre, Right }
    public enum TextAlignV { Top, Middle, Bottom }
    public enum BarDirection { LeftToRight, RightToLeft, BottomToTop, TopToBottom, FromCentre }
    public enum BarFill { Linear, EaseOut, EaseInOut, Stutter }
    public enum OverlayKind { Image, Text, Waveform, ProgressBar, UIObject }

    // A picture shown on top of the cinematic (logo, alert, title card...). Lives on the whole timeline, across shots.
    [Serializable]
    public class ImageLayer
    {
        public string name = "Image";
        public Texture texture;
        [Tooltip("Position and size on screen, 0-1 from the top-left corner.")]
        public Rect rect = new Rect(0.35f, 0.35f, 0.3f, 0.3f);
        [Tooltip("Tint and opacity.")]
        public Color color = Color.white;
        [Tooltip("Keep the image's proportions when resizing.")]
        public bool keepShape = true;
        [Tooltip("Seconds from the start of the cinematic.")]
        public float start;
        [Tooltip("How long it shows, in seconds. Can run across several shots.")]
        public float duration = 3f;
        public ImageAnimation animation = ImageAnimation.Fade;
        [HideInInspector] public float appearAt; // older versions: seconds into its shot

        [Tooltip("What this overlay draws.")]
        public OverlayKind kind = OverlayKind.Image;
        [Tooltip("Text overlays: what it says.")]
        [TextArea] public string text = "INITIALISING SYSTEMS...";
        [Tooltip("Text overlays: type the letters out one by one.")]
        public bool typewriter = true;
        [Tooltip("Blink on and off.")]
        public bool blink;
        [Tooltip("Blinks per second.")]
        public float blinkRate = 2f;
        [Tooltip("Waveform line / progress bar frame thickness, in pixels at 1080p.")]
        public float thickness = 3f;
        [Tooltip("Progress bar: seconds to fill up. 0 = its whole duration.")]
        public float fillTime;
        [Tooltip("UI object overlays: switched on while this overlay is on the timeline (Play Mode only).")]
        public GameObject target;
        [Tooltip("Progress bar: text shown above the bar, e.g. INITIALISING SYSTEMS...")]
        public string barText = "";
        [Tooltip("Progress bar: show the percentage above its right end.")]
        public bool showPercent;
        [Tooltip("None: clean on top. Own: gets the shot's camera effects by itself. " +
                 "Filmed: part of the picture, as if the camera filmed it (lens distortion bends it too).")]
        public CameraEffect cameraEffect = CameraEffect.None;
        [Tooltip("Draw order: higher numbers draw on top of lower ones (pop-ups, scans and other overlays too). " +
                 "On the same number, overlays later in the list are on top.")]
        public int layer;

        // ---- Coming in, going out, and while it's on screen
        [Tooltip("How it comes in.")]
        public OverlayTransition inEffect = OverlayTransition.Fade;
        [Tooltip("Seconds the coming-in takes.")]
        public float inTime = 0.25f;
        [Tooltip("How it goes out.")]
        public OverlayTransition outEffect = OverlayTransition.Fade;
        [Tooltip("Seconds the going-out takes.")]
        public float outTime = 0.25f;
        [Tooltip("What it keeps doing while it's on screen.")]
        public OverlayLoop loopEffect = OverlayLoop.None;
        [Tooltip("How fast the effect repeats (times per second).")]
        public float loopRate = 2f;
        [Tooltip("How strong the effect is.")]
        [Range(0f, 1f)] public float loopStrength = 0.85f;
        [HideInInspector] public int effectsVersion; // 0 = made before in/out effects existed (uses animation + blink)

        // ---- Glow
        [Tooltip("Glow around the overlay's visible shape (follows transparent edges). 0 = off.")]
        public float glow;
        [Tooltip("How far the glow spreads, in pixels at 1080p.")]
        public float glowSize = 12f;
        [Tooltip("Glow colour. Its alpha is how bright the glow is.")]
        public Color glowColor = new Color(0.45f, 1f, 0.85f, 1f);
        [Tooltip("Glow in the overlay's own colours (a bloom) instead of one colour.")]
        public bool glowOwnColours = true;

        // ---- Text
        [Tooltip("Typewriter: letters per second.")]
        public float typeSpeed = 22f;
        [Tooltip("Letters scramble through random characters before settling (decoding look).")]
        public bool scramble;
        [Tooltip("A blinking _ cursor at the end.")]
        public bool cursor;
        [Tooltip("Where the text sits across its box.")]
        public TextAlignH textAlign = TextAlignH.Left;
        [Tooltip("Where the text sits up and down its box.")]
        public TextAlignV textVAlign = TextAlignV.Middle;
        [Tooltip("Letter size in pixels at 1080p. 0 = fit the box's height.")]
        public float fontSize;
        public FontStyle fontStyle = FontStyle.Normal;
        [Tooltip("Space between lines (1 = normal).")]
        public float lineSpacing = 1f;
        [Tooltip("Extra space between letters, as a percentage of the letter size (can be negative), so it scales with the text.")]
        public float letterSpacing;
        [Tooltip("Break long lines onto the next line at the box's edge.")]
        public bool wrap;
        [Tooltip("Show the text in capitals.")]
        public bool allCaps;
        [Tooltip("Outline around the letters, in pixels at 1080p. 0 = none.")]
        public float outline;
        public Color outlineColor = Color.black;
        [Tooltip("Drop shadow distance, in pixels at 1080p. 0 = none.")]
        public float shadow;
        public Color shadowColor = new Color(0f, 0f, 0f, 0.6f);

        // ---- Waveform
        public WaveStyle waveStyle = WaveStyle.Line;
        [Tooltip("Where across the width the line starts moving (0 = left edge, 1 = right edge).")]
        [Range(0f, 1f)] public float waveFrom = 0.35f;
        [Tooltip("Where across the width the line stops moving.")]
        [Range(0f, 1f)] public float waveTo = 0.95f;
        [Tooltip("How tall the wiggle gets.")]
        public float waveHeight = 1f;
        [Tooltip("How fast it moves (without a sound).")]
        public float waveSpeed = 1f;
        [Tooltip("How busy the wiggle is (without a sound).")]
        public float waveDetail = 1f;
        [Tooltip("Bars style: how many bars.")]
        public int waveBars = 32;
        [Tooltip("Seconds after it appears that it starts moving (flat line before).")]
        public float waveMoveStart;
        [Tooltip("How long it keeps moving. 0 = until it's gone.")]
        public float waveMoveDuration;
        [Tooltip("Follow a sound: the line shows this sound's real wave, in sync with where it plays on the timeline.")]
        public AudioClip followSound;
        [Tooltip("How many seconds of the sound are shown across the moving part. Short = oscilloscope, long = scrolling audio.")]
        public float followWindow = 0.12f;
        [Tooltip("Extra height for quiet sounds.")]
        public float followGain = 1f;

        // ---- Progress bar
        public BarDirection barDirection = BarDirection.LeftToRight;
        [Tooltip("How the loading moves: steady, slowing down, or stopping and starting like a real loading bar.")]
        public BarFill barFill = BarFill.Linear;
        [Tooltip("Split the fill into blocks. 0 = smooth.")]
        public int barSegments;
        [Tooltip("Space between blocks, in pixels at 1080p.")]
        public float barGap = 3f;
        [Tooltip("Draw the frame around the bar.")]
        public bool barFrame = true;
        [Tooltip("Colour of the fill (the frame and text use the overlay colour).")]
        public Color barFillColor = Color.white;
        [Tooltip("How visible the empty part of the bar is.")]
        [Range(0f, 1f)] public float barTrack;
    }

    // A sound on the cinematic's timeline
    [Serializable]
    public class SoundClip
    {
        public string name = "Sound";
        public AudioClip clip;
        [Tooltip("Seconds from the start of the cinematic.")]
        public float start;
        [Tooltip("How long it plays. 0 = the rest of the clip.")]
        public float duration;
        [Tooltip("Skip this many seconds at the start of the clip (trim).")]
        public float clipStart;
        [Range(0f, 1f)] public float volume = 1f;
        public float fadeIn;
        public float fadeOut;
        public bool loop;
    }

    /// <summary>How long a sound plays on the timeline.</summary>
    public static float LengthOf(SoundClip sc)
    {
        if (sc == null) return 0f;
        if (sc.duration > 0f) return sc.duration;
        return sc.clip != null ? Mathf.Max(0.05f, sc.clip.length - sc.clipStart) : 1f;
    }

    public enum MotionKind { Rotate, Drift, Orbit, Shake }

    /// <summary>What a shot shows underneath its overlays.</summary>
    public enum ShotBackground { Camera, Colour, Image }

    // One camera movement in a shot. A shot can have any number; they add up.
    [Serializable]
    public class Motion
    {
        public string name = "Motion";
        public MotionKind kind;
        [Tooltip("Rotate: degrees per second (Y pans, X tilts, Z rolls). Drift: units per second.")]
        public Vector3 speed;
        [Tooltip("Drift relative to the camera (x right, y up, z forward) instead of world axes.")]
        public bool local = true;
        [Tooltip("Orbit: degrees per second around the Look At target.")]
        public float orbitSpeed = 10f;
        [Tooltip("Shake: strength in degrees.")]
        public float shakeAmount = 0.6f;
        [Tooltip("Shake: shakes per second.")]
        public float shakeSpeed = 1.2f;
        [Tooltip("Shake dies down over its duration (an impact) instead of staying constant (handheld).")]
        public bool shakeFades;
        [Tooltip("Seconds into the shot.")]
        public float start;
        [Tooltip("0 = until the end of the shot.")]
        public float duration;
        [Tooltip("Ease into and out of the movement instead of starting and stopping at full speed.")]
        public bool smooth = true;
        [Tooltip("The camera point is where the movement ENDS: it starts turned / moved the opposite way and arrives at the " +
                 "point when the clip finishes. Off: it starts at the point and moves away from it.")]
        public bool endAtPoint;
    }

    // Seconds' worth of movement done by this time (eased if smooth), so speed × this = how far it has gone
    static float MotionDone(Motion m, float time, float shotDuration)
    {
        float end = m.duration > 0f ? m.start + m.duration : shotDuration;
        float len = Mathf.Max(0.0001f, end - m.start);
        float t = Mathf.Clamp(time, m.start, end) - m.start;
        float done = m.smooth ? len * Mathf.SmoothStep(0f, 1f, t / len) : t;
        // Ending at the point: the whole movement happens "before" it, so it starts offset backwards and arrives at 0
        return m.endAtPoint ? done - len : done;
    }

    public static float MotionEnd(Motion m, float shotDuration) => m.duration > 0f ? m.start + m.duration : shotDuration;

    // An event at a moment in a shot (like an Animation event): drag objects into it and pick what to call
    [Serializable]
    public class ShotEvent
    {
        public string name = "Event";
        [Tooltip("Seconds into the shot.")]
        public float time;
        public UnityEvent action = new UnityEvent();
    }

    public enum ScanMode { Random, Targets, SearchThenLock }

    public enum LookScope { CameraOnly, EachOverlay, WholeScreen } // older versions; per-item CameraEffect now

    /// <summary>How an overlay relates to the shot's camera feed look.</summary>
    public enum CameraEffect
    {
        None,   // stays clean on top, like a HUD
        Own,    // gets the camera's effects on its own box (scanlines, noise, tint...)
        Filmed  // part of the picture, as if the camera filmed it: lens distortion bends it near the edges too
    }

    public static LookScope ScopeOf(Shot s) =>
        s.lookScope == LookScope.CameraOnly && s.lookOnOverlays ? LookScope.EachOverlay : s.lookScope;

    // A spot on screen the scan locks onto, with an optional label
    [Serializable]
    public class ScanTarget
    {
        public string label = "TARGET DETECTED";
        [Tooltip("Position and size on screen, 0-1 from the top-left corner.")]
        public Rect rect = new Rect(0.4f, 0.35f, 0.2f, 0.25f);
    }

    // Boxes that search the screen and/or lock onto targets, drawn on top of the shot
    [Serializable]
    public class Scan
    {
        [Tooltip("Draw order: higher numbers draw on top (pop-ups and overlays too).")]
        public int layer;
        [Tooltip("Search boxes: how each one appears every time it jumps to a new spot (and when the scan starts).")]
        public OverlayTransition boxIn = OverlayTransition.Zoom;
        public float boxInTime = 0.08f;
        [Tooltip("Search boxes: how each one disappears before it jumps (and when the scan ends or they lock on).")]
        public OverlayTransition boxOut = OverlayTransition.Fade;
        public float boxOutTime = 0.1f;
        [Tooltip("Target boxes: how each one appears when it locks on (Search then lock: it flies onto the target, then this plays).")]
        public OverlayTransition lockEffect = OverlayTransition.Zoom;
        [Tooltip("Seconds the lock-on takes.")]
        public float lockTime = 0.35f;
        [Tooltip("Target boxes blink a few times once they've locked on.")]
        public bool lockBlink = true;
        [Tooltip("Target boxes (and their labels): how they disappear when the scan ends. Needs the scan to have its own length; " +
                 "\"until the end of the shot\" cuts with the shot.")]
        public OverlayTransition targetOut = OverlayTransition.Fade;
        public float targetOutTime = 0.2f;
        public bool enabled;
        public ScanMode mode = ScanMode.SearchThenLock;
        [Tooltip("Seconds into the shot.")]
        public float start;
        [Tooltip("0 = until the end of the shot.")]
        public float duration;
        [Tooltip("Search Then Lock: seconds of random searching before locking onto the targets.")]
        public float searchTime = 1.5f;
        [Tooltip("Random boxes on screen at once.")]
        public int randomCount = 4;
        [Tooltip("How fast the random boxes jump around.")]
        public float speed = 1f;
        [Tooltip("Random box width, smallest and largest (0-1 of the screen).")]
        public Vector2 sizeRange = new Vector2(0.05f, 0.16f);
        [Tooltip("Where random boxes can appear, 0-1 from the top-left corner.")]
        public Rect searchArea = new Rect(0.05f, 0.08f, 0.9f, 0.84f);
        [Tooltip("Keep the random boxes going after locking on.")]
        public bool keepSearching;
        public Color color = new Color(0.45f, 1f, 0.85f, 1f);
        [Tooltip("Line thickness in pixels at 1080p.")]
        public float thickness = 2f;
        [Tooltip("Corner brackets instead of full rectangles.")]
        public bool brackets = true;
        [Range(0f, 0.5f), Tooltip("Faint fill inside the boxes.")]
        public float fill = 0.06f;
        public List<ScanTarget> targets = new List<ScanTarget>();
        [Tooltip("None: clean on top. Own: gets the shot's camera effects by itself. Filmed: part of the picture.")]
        public CameraEffect cameraEffect = CameraEffect.None;
    }

    [Serializable]
    public class Shot
    {
        public string name;
        [Tooltip("Camera: the camera's view. Colour / Image: no camera at all, for UI-style shots.")]
        public ShotBackground background = ShotBackground.Camera;
        public Color backgroundColor = Color.black;
        public Texture backgroundImage;
        public Transform point;
        [Tooltip("Total length of the shot in seconds (includes the move into it).")]
        public float duration = 3f;

        [Header("Getting here")]
        public Transition transition = Transition.Cut;
        public float transitionDuration = 1f;
        public AnimationCurve transitionEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Rotation")]
        [Tooltip("Keep the camera aimed at this. Overrides the point's rotation.")]
        public Transform lookAt;
        [Tooltip("Use the rotation below instead of the point's rotation (ignored if Look At is set).")]
        public bool overrideRotation;
        public Vector3 rotation;
        [Tooltip("Degrees per second. Y pans around world up, X tilts, Z rolls. Applied on top of Look At too.")]
        public Vector3 rotationSpeed;
        [Tooltip("Seconds into the shot.")]
        public float rotationStart;
        [Tooltip("0 = until the end of the shot.")]
        public float rotationDuration;

        [Header("Drift")]
        [Tooltip("Units per second on each axis.")]
        public Vector3 driftSpeed;
        [Tooltip("Relative to the shot's rotation (x = right, y = up, z = forward).")]
        public bool driftLocal = true;
        public float driftStart;
        [Tooltip("0 = until the end of the shot.")]
        public float driftDuration;

        [Header("Orbit (needs Look At)")]
        [Tooltip("Degrees per second around the Look At target (world up axis).")]
        public float orbitSpeed;
        public float orbitStart;
        [Tooltip("0 = until the end of the shot.")]
        public float orbitDuration;

        [Header("Motion")]
        [Tooltip("Camera movements in this shot. They add up, so you can chain or combine them.")]
        public List<Motion> motions = new List<Motion>();

        [Header("Shake (older versions; now a motion)")]
        [Tooltip("Degrees of shake. 0 = none.")]
        public float shakeAmount;
        [Tooltip("Shakes per second.")]
        public float shakeSpeed = 2f;
        public float shakeStart;
        [Tooltip("0 = until the end of the shot.")]
        public float shakeDuration;
        [Tooltip("Shake dies down over its duration (an impact) instead of staying constant (handheld).")]
        public bool shakeFades;

        [Header("Fades")]
        [Tooltip("Seconds fading in from the fade colour at the start of the shot. 0 = none.")]
        public float fadeIn;
        [Tooltip("Seconds fading out to the fade colour at the end of the shot. 0 = none.")]
        public float fadeOut;
        public Color fadeColor = Color.black;

        [Header("FOV")]
        public float fov = 60f;
        public bool animateFov;
        public float endFov = 40f;
        public float fovStart;
        [Tooltip("0 = until the end of the shot.")]
        public float fovDuration;
        public AnimationCurve fovEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Multiview")]
        [Tooltip("Extra camera points shown next to the main one in a grid, like a CCTV wall. Empty = normal shot.")]
        public List<Transform> extraViews = new List<Transform>();
        [Tooltip("Grid columns. 0 = automatic.")]
        public int columns;
        [Tooltip("Space between the cells and around the edges, in pixels at 1080p. 0 = no gaps.")]
        public float gap;
        [Tooltip("How many times a second the multiview views are re-rendered in game. 0 = every frame. " +
                 "Lower is much cheaper (each view is a full camera render) and suits a CCTV look.")]
        public float viewFps;
        [Tooltip("Resolution of the multiview views in game (1 = full, 0.5 = half). Lower is cheaper.")]
        [Range(0.25f, 1f)] public float viewResolution = 1f;
        [Tooltip("Colour shown in the gaps.")]
        public Color gapColor = Color.black;
        // Set from code at play time (SetMultiview / SetViews); they override the points set up in the editor
        [NonSerialized] public Transform runtimePoint;
        [NonSerialized] public List<Transform> runtimeViews;

        [Header("Camera feed look")]
        [Tooltip("Look of this shot's main camera (view 1).")]
        public Feed feed = new Feed();
        [Tooltip("Seconds into the shot when the camera look starts.")]
        public float lookStart;
        [Tooltip("How long the camera look lasts. 0 = until the end of the shot.")]
        public float lookDuration;
        [Tooltip("Seconds to fade the camera look in (and out, if it ends before the shot does).")]
        public float lookFade;
        [Tooltip("Camera only: just the camera. Each overlay: overlays get the look on their own. " +
                 "Whole screen: the look is applied once to the finished picture, overlays and text included.")]
        public LookScope lookScope = LookScope.CameraOnly;
        [HideInInspector] public bool lookOnOverlays; // older versions: same as Each Overlay
        [Tooltip("Look of each extra multiview view. Ones not customised use the main camera's look.")]
        public List<Feed> viewFeeds = new List<Feed>();
        [HideInInspector] public int viewFeedsVersion; // 0 = from before "customised" existed

        [Header("Pop-ups")]
        public List<Popup> popups = new List<Popup>();

        [Header("Scan")]
        public Scan scan = new Scan();

        // Older versions kept images per shot; they're moved to the cinematic's Images list automatically
        [HideInInspector] public List<ImageLayer> images = new List<ImageLayer>();

        [Header("Events")]
        public UnityEvent onStart;
        public UnityEvent onEnd;
        [Tooltip("Events at moments during the shot.")]
        public List<ShotEvent> events = new List<ShotEvent>();
    }

    public Camera cam;
    public bool playOnStart = true;
    public bool loop;
    [Tooltip("Draw order of multiview / pop-up / feed shots. Your own UI with a higher Sort Order draws on top of them.")]
    public int displaySortOrder = -100;
    [Tooltip("Font for all text (text overlays, scan labels, progress bars). Empty = a monospaced system font.")]
    public Font font;
    public List<Shot> shots = new List<Shot>();
    [Tooltip("Pictures on top of the cinematic, on its own timeline across shots. Later images draw on top.")]
    public List<ImageLayer> images = new List<ImageLayer>();
    [Tooltip("Sounds on the cinematic's own timeline, across shots.")]
    public List<SoundClip> sounds = new List<SoundClip>();
    [Header("Cinematic start / end")]
    [Tooltip("Turn off every other camera rendering to the screen while the cinematic plays, and back on afterwards.")]
    public bool disableOtherCameras = true;
    [Tooltip("Also turn off cameras that render into textures (minimaps, UI cameras...). Usually leave this off.")]
    public bool includeTextureCameras;
    [Tooltip("Cameras that are never turned off.")]
    public List<Camera> keepCameras = new List<Camera>();
    public UnityEvent onStartCinematic;
    public UnityEvent onEndCinematic;
    [HideInInspector] public UnityEvent onFinished; // older versions; still called at the end
    [HideInInspector] public CinematicAsset savedAs;

    /// <summary>A sound's waveform, read once in the editor (from the audio file itself), so waveform overlays can
    /// follow it without reading the sound's samples at run time.</summary>
    [Serializable]
    public class BakedWave
    {
        public AudioClip clip;
        public byte[] peaks;   // loudest sample in each slice, 128 = silence
        public float rate;     // slices per second
    }
    [HideInInspector] public List<BakedWave> bakedWaves = new List<BakedWave>();
    [Tooltip("While the cinematic draws its own picture (looks, multiviews, overlays...), the real camera keeps rendering " +
             "the scene in a tiny hidden corner so HDRP's automatic exposure stays right (otherwise the next plain shot can " +
             "flash white). If your exposure is Fixed, turn this off to save that render (it matters with splats).")]
    public bool keepExposureCamera = true;

    public enum ViewCamera { HelperCameras, MainViewWithRealCamera, AllViewsWithRealCamera }
    [Tooltip("Which camera renders the shot's views when the cinematic draws its own picture (looks, multiviews, pop-ups...). " +
             "Helper cameras: hidden cameras made by the cinematic. Real camera: your camera itself, moved to each view for a " +
             "moment, so anything that only works on your camera (e.g. a Gaussian splat pass that skips other cameras in builds) " +
             "shows too. All views: multiview cells and pop-ups as well (with temporal anti-aliasing they may smear a little).")]
    public ViewCamera viewCamera = ViewCamera.MainViewWithRealCamera;

    [Tooltip("Draw the cinematic's picture at the end of the frame, after plugins that queue their drawing late " +
             "(e.g. Gaussian splats) have done so. Without it, those don't show in the cinematic's views in builds.")]
    public bool drawAtEndOfFrame = true; // the file this cinematic was last saved to / loaded from

    [Header("Edit Mode Preview")]
    public bool preview;
    public int previewShot;
    [Range(0f, 1f)] public float previewScrub;

    int index = -1;
    float t;
    Vector3 fromPos;
    Quaternion fromRot;
    float fromFov;

    // Camera pose before previewing, so it can be put back
    bool previewing;
    Vector3 savedPos;
    Quaternion savedRot;
    float savedFov;

    readonly List<GameObject> poseAnchors = new List<GameObject>();
    List<Transform> currentViews;
    // Left over from an older version that resized the camera for multiviews; only used to undo that
    [SerializeField, HideInInspector] bool mainRectChanged;
    [SerializeField, HideInInspector] Rect savedRect = new Rect(0f, 0f, 1f, 1f);
    [SerializeField, HideInInspector] Shader feedShader; // referenced here so builds include it

    Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());
    public bool IsPlaying => index >= 0;

    void Start()
    {
        if (Application.isPlaying && playOnStart) StartCinematic();
    }

    // ---------------------------------------------------------------- Start / end

    // Cameras this cinematic switched off, and whether its own camera was on, so EndCinematic can put things back
    readonly List<Camera> switchedOff = new List<Camera>();
    bool cinematicActive, camWasEnabled;

    public bool IsCinematicActive => cinematicActive;

    /// <summary>
    /// Starts the cinematic from the first shot: turns on its camera, turns off the other cameras (see
    /// Disable Other Cameras) and fires On Start Cinematic. Safe to call from anywhere, e.g. a button or trigger.
    /// </summary>
    public void StartCinematic()
    {
        if (!Application.isPlaying || shots.Count == 0 || Cam == null) return;
        if (cinematicActive) EndCinematic(); // restart cleanly

        cinematicActive = true;
        camWasEnabled = Cam.enabled;
        Cam.enabled = true;

        switchedOff.Clear();
        if (disableOtherCameras)
        {
            foreach (Camera other in Camera.allCameras) // only cameras that are currently on
            {
                if (other == null || other == Cam || keepCameras.Contains(other)) continue;
                if (other.targetTexture != null && !includeTextureCameras) continue;
                if (displayRoot != null && other.transform.IsChildOf(displayRoot.transform)) continue; // our own helpers
                other.enabled = false;
                switchedOff.Add(other);
            }
        }

        onStartCinematic?.Invoke();
        if (cinematicActive && index < 0) StartShot(0); // the event may have ended it again
    }

    /// <summary>
    /// Ends the cinematic (also called automatically after the last shot): turns the other cameras back on exactly as
    /// they were, restores its own camera, and fires On End Cinematic.
    /// </summary>
    public void EndCinematic()
    {
        if (index >= 0)
        {
            Shot s = shots[index];
            index = -1;
            s.onEnd?.Invoke();
        }
        drawPending = false;
        HideDisplay();
        UpdateUIObjects(0f, false);
        UpdateSounds(0f, false);
        if (!cinematicActive) return;
        cinematicActive = false;

        foreach (Camera other in switchedOff)
            if (other != null) other.enabled = true;
        switchedOff.Clear();
        if (Cam != null) Cam.enabled = camWasEnabled;

        onEndCinematic?.Invoke();
        onFinished?.Invoke();
    }

    /// <summary>Same as StartCinematic.</summary>
    public void Play() => StartCinematic();

    /// <summary>Same as EndCinematic.</summary>
    public void Stop() => EndCinematic();

    void StartShot(int i)
    {
        index = i;
        t = 0f;
        fromPos = Cam.transform.position;
        fromRot = Cam.transform.rotation;
        fromFov = Cam.fieldOfView;
        shots[i].onStart?.Invoke();
        if (index != i) return; // the event stopped or changed the cinematic
        currentViews = new List<Transform>(ResolveViews(shots[i]));
        ApplyCurrent();
        FireEvents(i, -1f, 0f); // events right at the start of the shot
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            UpdatePreview();
            return;
        }
        if (index < 0) return;

        Shot s = shots[index];
        float before = t;
        t = Mathf.Min(t + Time.deltaTime, s.duration);
        ApplyCurrent();
        int now = index;
        FireEvents(index, before, t);
        if (index != now) return; // an event stopped or changed the cinematic

        if (t < s.duration) return;
        int ended = index;
        s.onEnd?.Invoke();
        if (index != ended) return; // the event stopped or changed the cinematic

        if (index + 1 < shots.Count) StartShot(index + 1);
        else if (loop) StartShot(0);
        else { index = -1; EndCinematic(); } // the shot's On End already fired
    }

    void ApplyCurrent()
    {
        Evaluate(shots[index], t, fromPos, fromRot, fromFov, out Vector3 pos, out Quaternion rot, out float fov);
        Apply(pos, rot, fov);
        float globalTime = StartTimeOf(index) + t;
        // Crossfade: the previous shot (held at its last moment) dissolves into this one
        crossAlpha = CrossfadeAmount(index, t);
        if (crossAlpha > 0f) BuildFrame(index - 1, globalTime, fromPos, fromRot, fromFov, crossFrame);
        BuildLayers(shots[index], t, globalTime, pos, rot, fov, currentViews, layers, texts);
        if (drawAtEndOfFrame) { drawPending = true; LateDraw.Add(this); } // drawn once the splats are queued
        else ShowLayers(layers, texts);
        UpdateUIObjects(globalTime, true);
        UpdateSounds(globalTime, true);
    }

    // Fires the shot's timed events that fall after "from" and up to "to" (seconds into the shot)
    readonly List<ShotEvent> dueEvents = new List<ShotEvent>();

    void FireEvents(int shotIndex, float from, float to)
    {
        Shot s = shots[shotIndex];
        if (s.events == null || s.events.Count == 0) return;
        dueEvents.Clear();
        foreach (ShotEvent ev in s.events)
            if (ev != null && ev.time > from && ev.time <= to) dueEvents.Add(ev);
        if (dueEvents.Count == 0) return;
        // In time order (same time: the order they're listed), so a fast frame doesn't mix them up
        for (int i = 1; i < dueEvents.Count; i++)
        {
            ShotEvent e = dueEvents[i];
            int j = i - 1;
            while (j >= 0 && dueEvents[j].time > e.time) { dueEvents[j + 1] = dueEvents[j]; j--; }
            dueEvents[j + 1] = e;
        }
        var fire = dueEvents.ToArray(); // an event may start another shot, which reuses the list
        foreach (ShotEvent ev in fire)
        {
            ev.action?.Invoke();
            if (index != shotIndex) return; // the event stopped or changed the cinematic
        }
    }

    void Apply(Vector3 pos, Quaternion rot, float fov)
    {
        Cam.transform.SetPositionAndRotation(pos, rot);
        Cam.fieldOfView = fov;
    }

    // Pose of the camera "time" seconds into a shot, given where the camera was when the shot began.
    static void Evaluate(Shot s, float time, Vector3 fromPos, Quaternion fromRot, float fromFov,
                         out Vector3 pos, out Quaternion rot, out float fov)
    {
        Transform point = s.runtimePoint != null ? s.runtimePoint : s.point;
        if (point == null)
        {
            pos = fromPos; rot = fromRot; fov = fromFov;
            return;
        }

        Quaternion targetRot = s.overrideRotation ? Quaternion.Euler(s.rotation) : point.rotation;

        // Transition: 1 = fully at this shot's point
        float k = 1f;
        if (s.transition == Transition.Move && s.transitionDuration > 0f)
            k = Ease(s.transitionEase, time / s.transitionDuration);

        // Add up every motion clip (plus older single settings, if any)
        Vector3 localDrift = s.driftLocal ? s.driftSpeed * ActiveTime(time, s.driftStart, s.driftDuration, s.duration) : Vector3.zero;
        Vector3 worldDrift = s.driftLocal ? Vector3.zero : s.driftSpeed * ActiveTime(time, s.driftStart, s.driftDuration, s.duration);
        Vector3 spin = s.rotationSpeed * ActiveTime(time, s.rotationStart, s.rotationDuration, s.duration);
        float orbitAngle = s.orbitSpeed * ActiveTime(time, s.orbitStart, s.orbitDuration, s.duration);
        if (s.motions != null)
        {
            foreach (Motion m in s.motions)
            {
                if (m == null) continue;
                float done = MotionDone(m, time, s.duration);
                switch (m.kind)
                {
                    case MotionKind.Rotate: spin += m.speed * done; break;
                    case MotionKind.Drift:
                        if (m.local) localDrift += m.speed * done;
                        else worldDrift += m.speed * done;
                        break;
                    case MotionKind.Orbit: orbitAngle += m.orbitSpeed * done; break;
                }
            }
        }

        // Position + drift
        pos = Vector3.LerpUnclamped(fromPos, point.position, k) + targetRot * localDrift + worldDrift;

        // Orbit around the look-at target
        if (s.lookAt != null && orbitAngle != 0f)
        {
            Vector3 center = s.lookAt.position;
            pos = center + Quaternion.AngleAxis(orbitAngle, Vector3.up) * (pos - center);
        }

        // Aim: look-at target if set, otherwise the point/override rotation
        Quaternion aim = targetRot;
        if (s.lookAt != null)
        {
            Vector3 dir = s.lookAt.position - pos;
            if (dir.sqrMagnitude > 1e-6f) aim = Quaternion.LookRotation(dir);
        }
        // Rotation: pan around world up (keeps the horizon level), tilt/roll locally
        rot = Quaternion.AngleAxis(spin.y, Vector3.up)
            * Quaternion.SlerpUnclamped(fromRot, aim, k)
            * Quaternion.Euler(spin.x, 0f, spin.z);

        // Shakes (smooth noise, so scrubbing always shows the same shake)
        rot *= Shake(time, s.shakeAmount, s.shakeSpeed, s.shakeStart, s.shakeDuration > 0f ? s.shakeStart + s.shakeDuration : s.duration, s.shakeFades, 0f);
        if (s.motions != null)
            for (int i = 0; i < s.motions.Count; i++)
            {
                Motion m = s.motions[i];
                if (m != null && m.kind == MotionKind.Shake)
                    rot *= Shake(time, m.shakeAmount, m.shakeSpeed, m.start, MotionEnd(m, s.duration), m.shakeFades, i * 13.7f);
            }

        // FOV
        float shotFov = s.fov;
        if (s.animateFov)
        {
            float len = s.fovDuration > 0f ? s.fovDuration : s.duration - s.fovStart;
            float p = len > 0f ? ActiveTime(time, s.fovStart, s.fovDuration, s.duration) / len : 1f;
            shotFov = Mathf.LerpUnclamped(s.fov, s.endFov, Ease(s.fovEase, p));
        }
        fov = Mathf.LerpUnclamped(fromFov, shotFov, k);
    }

    static Quaternion Shake(float time, float amount, float speed, float start, float end, bool fades, float seed)
    {
        if (amount <= 0f || time < start || time > end) return Quaternion.identity;
        float f = (speed > 0f ? speed : 2f) * time + seed;
        if (fades && end > start)
        {
            float left = 1f - (time - start) / (end - start);
            amount *= left * left;
        }
        return Quaternion.Euler((Mathf.PerlinNoise(f, 0.17f) - 0.5f) * 2f * amount,
                                (Mathf.PerlinNoise(0.53f, f) - 0.5f) * 2f * amount,
                                (Mathf.PerlinNoise(f + 7.1f, 3.3f) - 0.5f) * amount);
    }

    // Seconds an effect has been running, clamped to its start/duration window.
    static float ActiveTime(float time, float start, float duration, float shotDuration)
    {
        float end = duration > 0f ? start + duration : shotDuration;
        return Mathf.Max(0f, Mathf.Clamp(time, start, end) - start);
    }

    static float Ease(AnimationCurve curve, float x)
    {
        x = Mathf.Clamp01(x);
        return curve == null || curve.length == 0 ? Mathf.SmoothStep(0f, 1f, x) : curve.Evaluate(x);
    }

    // ---------------------------------------------------------------- Edit Mode preview

    public void UpdatePreview()
    {
        if (Cam == null) return;

        if (!preview || shots.Count == 0)
        {
            StopPreview();
            return;
        }

        if (!previewing)
        {
            previewing = true;
            savedPos = Cam.transform.position;
            savedRot = Cam.transform.rotation;
            savedFov = Cam.fieldOfView;
        }

        // Simulate from the camera's starting pose through every earlier shot, like Play does.
        int target = Mathf.Clamp(previewShot, 0, shots.Count - 1);
        Vector3 p = savedPos;
        Quaternion r = savedRot;
        float f = savedFov;
        for (int i = 0; i < target; i++)
            Evaluate(shots[i], shots[i].duration, p, r, f, out p, out r, out f);

        Evaluate(shots[target], previewScrub * shots[target].duration, p, r, f,
                 out Vector3 pos, out Quaternion rot, out float fov);
        Apply(pos, rot, fov);
        float localTime = previewScrub * shots[target].duration;
        crossAlpha = CrossfadeAmount(target, localTime);
        if (crossAlpha > 0f) BuildFrame(target - 1, StartTimeOf(target) + localTime, p, r, f, crossFrame);
        BuildLayers(shots[target], localTime, StartTimeOf(target) + localTime, pos, rot, fov, ResolveViews(shots[target]), layers, texts);
        ShowLayers(layers, texts);
    }

    void StopPreview()
    {
        if (!previewing) return;
        previewing = false;
        HideDisplay();
        Apply(savedPos, savedRot, savedFov);
    }

    // Older versions had one rotation / drift / orbit / shake per shot: turn them into motion clips
    bool MigrateMotions()
    {
        bool changed = false;
        foreach (Shot s in shots)
        {
            if (s.motions == null) s.motions = new List<Motion>();
            if (s.rotationSpeed != Vector3.zero)
            {
                s.motions.Add(new Motion { name = "Rotate", kind = MotionKind.Rotate, speed = s.rotationSpeed,
                                           start = s.rotationStart, duration = s.rotationDuration, smooth = false });
                s.rotationSpeed = Vector3.zero;
                changed = true;
            }
            if (s.driftSpeed != Vector3.zero)
            {
                s.motions.Add(new Motion { name = "Drift", kind = MotionKind.Drift, speed = s.driftSpeed, local = s.driftLocal,
                                           start = s.driftStart, duration = s.driftDuration, smooth = false });
                s.driftSpeed = Vector3.zero;
                changed = true;
            }
            if (s.orbitSpeed != 0f)
            {
                s.motions.Add(new Motion { name = "Orbit", kind = MotionKind.Orbit, orbitSpeed = s.orbitSpeed,
                                           start = s.orbitStart, duration = s.orbitDuration, smooth = false });
                s.orbitSpeed = 0f;
                changed = true;
            }
            if (s.shakeAmount > 0f)
            {
                s.motions.Add(new Motion { name = "Shake", kind = MotionKind.Shake, shakeAmount = s.shakeAmount,
                                           shakeSpeed = s.shakeSpeed, shakeFades = s.shakeFades,
                                           start = s.shakeStart, duration = s.shakeDuration, smooth = false });
                s.shakeAmount = 0f;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Moves images from older per-shot storage onto the cinematic's own timeline (keeps their timing).</summary>
    public bool MigrateImages()
    {
        bool moved = MigrateMotions();
        if (images == null) images = new List<ImageLayer>();
        float acc = 0f;
        foreach (Shot s in shots)
        {
            if (s.images != null && s.images.Count > 0)
            {
                foreach (ImageLayer im in s.images)
                {
                    if (im == null) continue;
                    im.start = acc + im.appearAt;
                    im.duration = im.duration > 0f ? im.duration : Mathf.Max(0.1f, s.duration - im.appearAt);
                    im.appearAt = 0f;
                    images.Add(im);
                }
                s.images.Clear();
                moved = true;
            }
            acc += s.duration;
        }
        return moved;
    }

    void OnEnable()
    {
        MigrateImages();
        // Hidden helpers from before a script reload are no longer tracked; remove them
        foreach (GameObject stray in Resources.FindObjectsOfTypeAll<GameObject>())
            if (stray != null && stray.transform.parent == null && (stray.hideFlags & HideFlags.DontSave) != 0 &&
                (stray.name == DisplayName || stray.name == PreviewCamName || stray.name == "Cinematic View (temp)" ||
                 stray.name == "Cinematic Sounds (temp)"))
                DestroyImmediate(stray);
        slots.Clear();
        displayRoot = null;
        previewCams.Clear();
        if (mainRectChanged && Cam != null)
        {
            Cam.rect = savedRect;
            mainRectChanged = false;
        }
    }

    void OnDisable()
    {
        drawPending = false;
        if (!Application.isPlaying) StopPreview();
        if (Application.isPlaying && cinematicActive) EndCinematic(); // never leave the other cameras switched off
        DestroyDisplay();
        if (soundRoot != null) Kill(soundRoot);
        soundRoot = null;
        soundSources.Clear();
        foreach (GameObject a in poseAnchors) Kill(a);
        poseAnchors.Clear();
        foreach (Camera pc in previewCams)
            if (pc != null) Kill(pc.gameObject);
        previewCams.Clear();
        Kill(previewMat);
        previewMat = null;
    }

    // ---------------------------------------------------------------- Offscreen preview
    // Used by the Cinematic Editor during Play Mode: renders any moment of the cinematic into a texture
    // with its own hidden cameras, without touching the real camera or the Game view.

    const string PreviewCamName = "Cinematic Preview (temp)";
    readonly List<Camera> previewCams = new List<Camera>();

    public float TotalDuration
    {
        get
        {
            float total = 0f;
            foreach (Shot s in shots) total += s.duration;
            return total;
        }
    }

    /// <summary>Seconds into the cinematic while it plays, or -1 when it isn't playing.</summary>
    public float CurrentTime
    {
        get
        {
            if (index < 0) return -1f;
            float acc = 0f;
            for (int i = 0; i < index; i++) acc += shots[i].duration;
            return acc + t;
        }
    }

    public Transform PointOf(Shot s) => s.runtimePoint != null ? s.runtimePoint : s.point;
    public List<Transform> ViewsOf(Shot s) => ResolveViews(s);

    public Transform PointOf(Popup p) => p.runtimePoint != null ? p.runtimePoint : p.point;

    Material previewMat;
    readonly List<Layer> previewLayers = new List<Layer>();
    readonly List<TextItem> previewTexts = new List<TextItem>();

    /// <summary>True when the last RenderPreview showed something that animates on its own (camera feed look, waveform).</summary>
    public bool PreviewAnimated { get; private set; }

    /// <summary>Text drawn by the last RenderPreview (the editor draws it on top of the viewer).</summary>
    public List<TextItem> PreviewTexts => previewTexts;

    public void RenderPreview(RenderTexture rt, float time)
    {
        if (rt == null || Cam == null) return;
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prevActive;
        if (shots.Count == 0 || !EnsureShader()) return;
        if (previewMat == null) previewMat = new Material(feedShader) { hideFlags = HideFlags.HideAndDontSave };

        // Which shot is at this time
        int target = shots.Count - 1;
        float local = shots[target].duration, acc = 0f;
        for (int i = 0; i < shots.Count; i++)
        {
            if (time < acc + shots[i].duration)
            {
                target = i;
                local = Mathf.Max(0f, time - acc);
                break;
            }
            acc += shots[i].duration;
        }

        // Start where Edit Mode preview starts, or (in Play Mode) from the first shot's point
        Transform first = PointOf(shots[0]);
        Vector3 p; Quaternion r; float f;
        if (!Application.isPlaying && previewing) { p = savedPos; r = savedRot; f = savedFov; }
        else
        {
            p = first != null ? first.position : Cam.transform.position;
            r = first != null ? first.rotation : Cam.transform.rotation;
            f = shots[0].fov;
        }
        for (int i = 0; i < target; i++)
            Evaluate(shots[i], shots[i].duration, p, r, f, out p, out r, out f);
        Evaluate(shots[target], local, p, r, f, out Vector3 pos, out Quaternion rot, out float fov);

        BuildLayers(shots[target], local, StartTimeOf(target) + local, pos, rot, fov, ResolveViews(shots[target]), previewLayers, previewTexts);
        PreviewAnimated = false; // feed noise and waveforms move even when paused
        foreach (Layer l in previewLayers)
            if ((l.feed != null && l.feed.look) || l.mode == 1) PreviewAnimated = true;
        foreach (TextItem item in previewTexts)
            if (item.look != null) PreviewAnimated = true;
        if (screenLook != null && screenLook.look) PreviewAnimated = true;

        CompositeInto(rt, previewLayers, previewTexts, screenLook);

        // Crossfade from the previous shot
        float cross = CrossfadeAmount(target, local);
        if (cross > 0f)
        {
            BuildFrame(target - 1, StartTimeOf(target) + local, p, r, f, previewCrossFrame);
            CompositeCrossfade(rt, previewCrossFrame, cross);
        }
    }

    // ---------------------------------------------------------------- Crossfade

    // One finished picture's worth of layers, kept aside so two shots can be blended
    class Frame
    {
        public readonly List<Layer> layers = new List<Layer>();
        public readonly List<TextItem> texts = new List<TextItem>();
        public Feed screen;
        public float screenOff;
        public Color bg = Color.black;
    }

    readonly Frame crossFrame = new Frame();
    readonly Frame previewCrossFrame = new Frame();
    float crossAlpha; // how much of the previous shot still shows right now (0 = none)

    /// <summary>How much of the previous shot still shows "local" seconds into shot i (1 = all of it, 0 = none).</summary>
    public float CrossfadeAmount(int i, float local)
    {
        if (i <= 0 || i >= shots.Count) return 0f;
        Shot s = shots[i];
        if (s.transition != Transition.Crossfade || s.transitionDuration <= 0f || local >= s.transitionDuration) return 0f;
        return 1f - Ease(s.transitionEase, local / s.transitionDuration);
    }

    // The previous shot's picture at its very end (its pose given), without touching the current shot's settings
    void BuildFrame(int i, float globalTime, Vector3 pos, Quaternion rot, float fov, Frame fr)
    {
        Feed sl = screenLook; float so = screenLookOff; Color bg = layersBg;
        Shot s = shots[i];
        BuildLayers(s, s.duration, globalTime, pos, rot, fov, ResolveViews(s), fr.layers, fr.texts);
        fr.screen = screenLook; fr.screenOff = screenLookOff; fr.bg = layersBg;
        screenLook = sl; screenLookOff = so; layersBg = bg;
    }

    // Draws a kept picture over rt, see-through by "alpha"
    void CompositeCrossfade(RenderTexture rt, Frame fr, float alpha)
    {
        bool wasGame = drawingGame;
        drawingGame = false; // the previous shot's views are drawn fresh, not from the current shot's kept views
        try { CompositeCrossfadeInner(rt, fr, alpha); }
        finally { drawingGame = wasGame; }
    }

    void CompositeCrossfadeInner(RenderTexture rt, Frame fr, float alpha)
    {
        Feed sl = screenLook; float so = screenLookOff; Color bg = layersBg;
        screenLook = fr.screen; screenLookOff = fr.screenOff; layersBg = fr.bg;
        RenderTexture tmp = RenderTexture.GetTemporary(rt.width, rt.height, 24);
        CompositeInto(tmp, fr.layers, fr.texts, fr.screen);
        screenLook = sl; screenLookOff = so; layersBg = bg;

        SetFeed(previewMat, new Layer { rect = Full, alpha = Mathf.Clamp01(alpha), color = Color.white }, rt.width / (float)rt.height, FeedTime);
        DrawQuad(rt, previewMat, tmp, Full);
        RenderTexture.ReleaseTemporary(tmp);
    }

    // Draws layers and text into a texture, then (for a whole-screen look) the look over all of it.
    // Used by the editor's viewer and, for whole-screen looks, by the screen display.
    void CompositeInto(RenderTexture rt, List<Layer> ls, List<TextItem> ts, Feed screen)
    {
        if (previewMat == null) previewMat = new Material(feedShader) { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture work = screen != null ? RenderTexture.GetTemporary(rt.width, rt.height, 24) : null;
        RenderTexture target = work != null ? work : rt;

        RenderTexture.active = target;
        GL.Clear(true, true, ls.Count > 0 && ls[0].rect != Full ? layersBg : Color.black); // multiview gap colour
        RenderTexture.active = prevActive;

        float aspect = rt.width / (float)rt.height, ft = FeedTime;
        // With a whole-screen look: first what's "filmed", then the look over it, then the rest on top
        DrawOrdered(target, ls, ts, aspect, ft, work != null ? 1 : 0);

        if (work != null)
        {
            // The look over the whole finished picture: lens distortion bends filmed overlays too
            SetFeed(previewMat, new Layer { feed = screen, rect = Full, alpha = 1f, color = Color.white, lookOff = screenLookOff }, aspect, ft);
            DrawQuad(rt, previewMat, work, Full);
            RenderTexture.ReleaseTemporary(work);
            DrawOrdered(rt, ls, ts, aspect, ft, 2);
        }
    }

    // Layers and text together, in draw order (both lists are already sorted by order).
    // which: 0 = everything, 1 = what's under the whole-screen look, 2 = what's on top of it.
    void DrawOrdered(RenderTexture target, List<Layer> ls, List<TextItem> ts, float aspect, float ft, int which)
    {
        // Under the look: everything "filmed", plus pop-ups and scan boxes that are below something filmed
        // (so the draw order is kept: they become part of the picture)
        float maxFilmed = float.MinValue;
        if (which != 0)
        {
            foreach (Layer l in ls) if (l.filmed) maxFilmed = Mathf.Max(maxFilmed, l.order);
            foreach (TextItem t in ts) if (t.filmed) maxFilmed = Mathf.Max(maxFilmed, t.order);
        }

        Font f = ts.Count > 0 ? TextFont() : null;
        Texture atlas = f != null ? PrepareTexts(f, ts) : null;

        int li = 0, ti = 0;
        while (li < ls.Count || ti < ts.Count)
        {
            bool takeLayer = ti >= ts.Count || (li < ls.Count && ls[li].order <= ts[ti].order);
            if (takeLayer)
            {
                Layer l = ls[li];
                bool under = l.filmed || (l.overlay == null && l.order < maxFilmed);
                if (which == 0 || (which == 1) == under) DrawLayerAt(target, ls, li, aspect, ft);
                li++;
            }
            else
            {
                TextItem t = ts[ti];
                bool under = t.filmed || (t.overlay == null && t.order < maxFilmed);
                if (atlas != null && (which == 0 || (which == 1) == under)) DrawTextAt(target, ts, ti, f, atlas, ft);
                ti++;
            }
        }
    }

    // The camera that renders view i in game: null = the real camera (borrowed for a moment), otherwise a helper
    Camera ViewCameraFor(int i, Layer l)
    {
        if (!Application.isPlaying) return null; // the editor always borrows the real camera
        bool isMain = i == 0 && l.order <= OrderCamera; // the shot's own camera (or multiview cell 1)
        bool isCell = l.order <= OrderCamera;
        switch (viewCamera)
        {
            case ViewCamera.AllViewsWithRealCamera: return null;
            case ViewCamera.MainViewWithRealCamera: return isMain ? null : GetPreviewCam(i);
            default: return GetPreviewCam(i);
        }
    }

    void DrawLayerAt(RenderTexture target, List<Layer> ls, int i, float aspect, float ft)
    {
        Layer l = ls[i];
        RenderTexture tmp = null;
        Texture src = l.tex;
        if (src == null)
        {
            float res = l.resolution > 0f ? Mathf.Clamp(l.resolution, 0.1f, 1f) : 1f;
            int w = Mathf.Max(16, Mathf.RoundToInt(target.width * l.rect.width * res));
            int h = Mathf.Max(16, Mathf.RoundToInt(target.height * l.rect.height * res));
            if (drawingGame && Application.isPlaying && (l.refreshFps > 0f || res < 1f))
                src = CachedView(i, l, w, h); // in game: kept between frames, re-rendered at its own rate
            else
            {
                tmp = RenderTexture.GetTemporary(w, h, 24);
                RenderView(ViewCameraFor(i, l), l.pos, l.rot, l.fov, tmp);
                src = tmp;
            }
        }
        DrawWithGlow(target, l, src, aspect, ft);
        if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
    }

    // ---- Views that don't re-render every frame (multiview "refresh rate" / "resolution")

    class ViewCache
    {
        public RenderTexture rt;
        public float next;    // when it's due again (unscaled time)
        public int shot = -1; // which shot it was rendered for (a new shot renders straight away)
    }

    readonly Dictionary<int, ViewCache> viewCache = new Dictionary<int, ViewCache>();
    bool drawingGame; // drawing the game's picture (not the editor's viewer)

    RenderTexture CachedView(int i, Layer l, int w, int h)
    {
        if (!viewCache.TryGetValue(i, out ViewCache vc)) viewCache[i] = vc = new ViewCache();
        float now = Time.unscaledTime;
        bool fresh = vc.rt == null || vc.rt.width != w || vc.rt.height != h || vc.shot != index;
        if (fresh)
        {
            if (vc.rt != null) { vc.rt.Release(); Kill(vc.rt); }
            vc.rt = new RenderTexture(w, h, 24) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (fresh || l.refreshFps <= 0f || now >= vc.next)
        {
            RenderView(ViewCameraFor(i, l), l.pos, l.rot, l.fov, vc.rt);
            float period = l.refreshFps > 0f ? 1f / l.refreshFps : 0f;
            // Views take turns: each one starts at a different point of the cycle, so they don't all render on the same frame
            vc.next = fresh ? now + period * (1f + (i % 6) / 6f) : Mathf.Max(vc.next + period, now);
            vc.shot = index;
        }
        return vc.rt;
    }

    void ClearViewCache()
    {
        foreach (ViewCache vc in viewCache.Values)
            if (vc.rt != null) { vc.rt.Release(); Kill(vc.rt); }
        viewCache.Clear();
    }

    /// <summary>The finished picture the game is showing right now (null when it's just the plain camera).
    /// The editor's viewer shows this while following the game, instead of rendering everything a second time.</summary>
    public Texture LiveTexture => compositeImage != null && compositeImage.gameObject.activeInHierarchy ? compositeRT : null;

    // Draws a texture into part of a render texture (viewport space) with the material
    static void DrawQuad(RenderTexture target, Material mat, Texture src, Rect q, int pass = 0)
    {
        RenderTexture prevActive = RenderTexture.active;
        mat.mainTexture = src;
        RenderTexture.active = target;
        // The editor GUI turns sRGB conversion off; without it linear colours are stored as-is and look far too dark
        bool oldSrgb = GL.sRGBWrite;
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
        GL.PushMatrix();
        GL.LoadOrtho();
        mat.SetPass(pass);
        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        GL.TexCoord2(0f, 0f); GL.Vertex3(q.xMin, q.yMin, 0f);
        GL.TexCoord2(0f, 1f); GL.Vertex3(q.xMin, q.yMax, 0f);
        GL.TexCoord2(1f, 1f); GL.Vertex3(q.xMax, q.yMax, 0f);
        GL.TexCoord2(1f, 0f); GL.Vertex3(q.xMax, q.yMin, 0f);
        GL.End();
        GL.PopMatrix();
        GL.sRGBWrite = oldSrgb;
        RenderTexture.active = prevActive;
    }

    // Draws a layer, with its glow underneath if it has one.
    // The glow: the shape is drawn small, blurred with a proper Gaussian blur (so its edges fade out smoothly
    // and transparent parts stay dark), then laid under the shape in the glow colour or its own colours.
    void DrawWithGlow(RenderTexture target, Layer l, Texture src, float aspect, float ft)
    {
        SetFeed(previewMat, l, aspect, ft);
        ImageLayer o = l.overlay;
        if (HasGlow(o))
        {
            float radius = o.glowSize * target.height / 1080f;                    // glow size in target pixels
            float k = Mathf.Clamp(4.8f / Mathf.Max(radius, 0.01f), 0.0625f, 0.5f); // blur at low res: smoother and cheap
            int w = Mathf.Max(8, Mathf.RoundToInt(target.width * l.rect.width * k));
            int h = Mathf.Max(8, Mathf.RoundToInt(target.height * l.rect.height * k));
            RenderTexture a = RenderTexture.GetTemporary(w, h, 0), b = RenderTexture.GetTemporary(w, h, 0);
            a.wrapMode = b.wrapMode = TextureWrapMode.Clamp;
            a.filterMode = b.filterMode = FilterMode.Bilinear;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = a;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            RenderTexture.active = prev;
            DrawQuad(a, previewMat, src, Full);                 // the shape itself, small

            float step = radius * k / 8f;                       // after both blurs the glow fades out at about its size
            for (int it = 0; it < 2; it++)                      // twice in each direction: rounder, smoother falloff
            {
                previewMat.SetVector(IdBlurStep, new Vector4(step / w, 0f, 0f, 0f));
                DrawQuad(b, previewMat, a, Full, 1);
                previewMat.SetVector(IdBlurStep, new Vector4(0f, step / h, 0f, 0f));
                DrawQuad(a, previewMat, b, Full, 1);
            }
            DrawQuad(target, previewMat, a, l.rect, 2);         // the glow, under the shape
            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
        }
        DrawQuad(target, previewMat, src, l.rect);
    }

    // Text drawn letter by letter into a texture, with the same font and look as in the game.
    // First every letter is asked for (at the 1080p size): the font texture may be rebuilt while doing so.
    Texture PrepareTexts(Font f, List<TextItem> ts)
    {
        foreach (TextItem item in ts)
            if (!string.IsNullOrEmpty(item.text)) f.RequestCharactersInTexture(item.hasCursor ? item.text + "_" : item.text, TextSize(item, 1080), StyleOf(item));
        return f.material != null ? f.material.mainTexture : null;
    }

    void DrawTextAt(RenderTexture rt, List<TextItem> ts, int i, Font f, Texture atlas, float time)
    {
        float aspect = rt.width / (float)rt.height;
        TextItem item = ts[i];
        if (string.IsNullOrEmpty(item.text)) return;
        TextBlock tb = LayoutText(item, f, rt.width, rt.height);
        float scale = tb.scale;

        bool glow = HasGlow(item.overlay);
        if (item.look == null && !glow)
        {
            DrawGlyphs(rt, item, f, atlas, tb, 0f, 0f, time, 500 + i);
            return;
        }

        // Text with its own camera effects or a glow: draw it into its own texture, then put that through the shader like an image
        ImageLayer to = item.label ? null : item.overlay;
        float extra = to != null ? Mathf.Max(to.outline, to.shadow) * scale : 0f;
        float pad = 6f * scale + 2f + extra + (glow ? item.overlay.glowSize * 1.3f * scale : 0f);
        float x0 = Mathf.Min(tb.xMin, item.label ? tb.xMin : item.rect.x * rt.width) - pad;
        float x1 = Mathf.Max(tb.xMax, item.label ? tb.xMax : item.rect.xMax * rt.width) + pad;
        float y0 = Mathf.Min(tb.yMin, item.label ? tb.yMin : item.rect.y * rt.height) - pad;
        float y1 = Mathf.Max(tb.yMax, item.label ? tb.yMax : item.rect.yMax * rt.height) + pad;
        int tw = Mathf.Max(4, Mathf.CeilToInt(x1 - x0)), th = Mathf.Max(4, Mathf.CeilToInt(y1 - y0));
        RenderTexture tmp = RenderTexture.GetTemporary(tw, th, 0);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = tmp;
        GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        RenderTexture.active = prev;
        DrawGlyphs(tmp, item, f, atlas, tb, x0, y0, time, 500 + i);

        Rect vr = new Rect(x0 / rt.width, y0 / rt.height, tw / (float)rt.width, th / (float)rt.height);
        DrawWithGlow(rt, new Layer { tex = tmp, premul = true, color = Color.white, feed = item.look, rect = vr,
                                     alpha = 1f, seed = 500 + i, lookOff = item.lookOff, overlay = glow ? item.overlay : null },
                     tmp, aspect, time);
        RenderTexture.ReleaseTemporary(tmp);
    }

    // A text laid out into lines, in pixels of a target this size. Laid out at 1080p and scaled, so it's the
    // same at any quality.
    class TextBlock
    {
        public readonly List<string> lines = new List<string>();
        public readonly List<float> xs = new List<float>();        // left end of each line
        public readonly List<float> baselines = new List<float>(); // baseline of each line (y up)
        public readonly List<int> starts = new List<int>();        // index in the text of each line's first letter
        public int refSize;    // font size at 1080p
        public float scale;    // target pixels per 1080p pixel
        public float letter;   // extra space between letters, target pixels
        public FontStyle style;
        public float xMin, xMax, yMin, yMax; // where the letters are
    }

    readonly TextBlock textBlock = new TextBlock();

    static FontStyle StyleOf(TextItem item) => !item.label && item.overlay != null ? item.overlay.fontStyle : FontStyle.Normal;

    float LineWidth(Font f, string line, int refSize, FontStyle style, float letterRef)
    {
        float w = 0f;
        int n = 0;
        foreach (char ch in line)
            if (f.GetCharacterInfo(ch, out CharacterInfo ci, refSize, style)) { w += ci.advance; n++; }
        return w + Mathf.Max(0, n - 1) * letterRef;
    }

    TextBlock LayoutText(TextItem item, Font f, int w, int h)
    {
        TextBlock tb = textBlock;
        tb.lines.Clear(); tb.xs.Clear(); tb.baselines.Clear(); tb.starts.Clear();
        ImageLayer o = item.label ? null : item.overlay;
        tb.refSize = TextSize(item, 1080);
        tb.scale = h / 1080f;
        tb.style = StyleOf(item);
        float letterRef = o != null ? o.letterSpacing * 0.01f * tb.refSize : 0f; // a share of the letter size
        tb.letter = letterRef * tb.scale;
        float size = tb.refSize * tb.scale;
        float boxX = item.rect.x * w, boxY = item.rect.y * h, boxW = item.rect.width * w, boxH = item.rect.height * h;

        // Lines: the text's own line breaks, then (with wrap) breaking at the box's edge between words
        // (The whole text is laid out, even while it's still typing, so lines and centring never change.)
        float maxRef = boxW / tb.scale;
        int offset = 0;
        foreach (string raw in (item.text ?? "").Split('\n'))
        {
            int rawStart = offset;
            offset += raw.Length + 1; // + the line break
            if (o == null || !o.wrap || LineWidth(f, raw, tb.refSize, tb.style, letterRef) <= maxRef)
            {
                tb.lines.Add(raw);
                tb.starts.Add(rawStart);
                continue;
            }
            string line = "";
            int lineStart = rawStart;
            foreach (string word in raw.Split(' '))
            {
                string tryLine = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && LineWidth(f, tryLine, tb.refSize, tb.style, letterRef) > maxRef)
                {
                    tb.lines.Add(line);
                    tb.starts.Add(lineStart);
                    lineStart += line.Length + 1; // + the space the line broke at
                    line = word;
                }
                else line = tryLine;
            }
            tb.lines.Add(line);
            tb.starts.Add(lineStart);
        }

        float lineH = size * 1.2f * (o != null ? Mathf.Max(0.3f, o.lineSpacing) : 1f);
        float ascent = size * 0.8f, descent = size * 0.25f;
        float blockH = ascent + (tb.lines.Count - 1) * lineH + descent;

        TextAlignH ha = item.label ? (item.right ? TextAlignH.Right : TextAlignH.Left) : o != null ? o.textAlign : TextAlignH.Left;
        float top;
        if (item.label) top = boxY + boxH + 4f * tb.scale + size * 0.25f + ascent; // small label just above the box
        else
        {
            TextAlignV va = o != null ? o.textVAlign : TextAlignV.Middle;
            top = va == TextAlignV.Top ? boxY + boxH
                : va == TextAlignV.Bottom ? boxY + blockH
                : boxY + boxH * 0.5f + blockH * 0.5f;
        }

        tb.xMin = float.MaxValue; tb.xMax = float.MinValue;
        for (int i = 0; i < tb.lines.Count; i++)
        {
            float lw = LineWidth(f, tb.lines[i], tb.refSize, tb.style, letterRef) * tb.scale;
            float x = ha == TextAlignH.Centre ? boxX + (boxW - lw) * 0.5f : ha == TextAlignH.Right ? boxX + boxW - lw : boxX;
            tb.xs.Add(x);
            tb.baselines.Add(top - ascent - i * lineH);
            tb.xMin = Mathf.Min(tb.xMin, x);
            tb.xMax = Mathf.Max(tb.xMax, x + lw);
        }
        if (tb.lines.Count == 0) { tb.xMin = boxX; tb.xMax = boxX; }
        tb.yMax = top;
        tb.yMin = top - blockH;
        return tb;
    }

    // Draws the letters (offset by ox, oy pixels) into a render texture: shadow, outline, then the letters
    void DrawGlyphs(RenderTexture rt, TextItem item, Font f, Texture atlas, TextBlock tb, float ox, float oy, float time, int seed)
    {
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;
        bool oldSrgb = GL.sRGBWrite;
        GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
        GL.PushMatrix();
        GL.LoadPixelMatrix(ox, ox + rt.width, oy, oy + rt.height);

        SetFeed(previewMat, new Layer { feed = null, alpha = 1f, mode = 3, tex = atlas, color = Color.white, seed = seed, rect = Full },
                rt.width / (float)rt.height, time);
        previewMat.mainTexture = atlas;
        previewMat.SetPass(0);
        GL.Begin(GL.QUADS);

        ImageLayer o = item.label ? null : item.overlay;
        float a = item.color.a * item.alpha;
        if (o != null && o.shadow > 0f)
        {
            float d = o.shadow * tb.scale;
            DrawLetters(f, tb, item, d, -d, new Color(o.shadowColor.r, o.shadowColor.g, o.shadowColor.b, o.shadowColor.a * a));
        }
        if (o != null && o.outline > 0f)
        {
            // The letters drawn around themselves in 8 directions make an even outline
            float d = o.outline * tb.scale;
            Color oc = new Color(o.outlineColor.r, o.outlineColor.g, o.outlineColor.b, o.outlineColor.a * a);
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f;
                DrawLetters(f, tb, item, Mathf.Cos(ang) * d, Mathf.Sin(ang) * d, oc);
            }
        }
        DrawLetters(f, tb, item, 0f, 0f, new Color(item.color.r, item.color.g, item.color.b, a));

        GL.End();
        GL.PopMatrix();
        GL.sRGBWrite = oldSrgb;
        RenderTexture.active = prevActive;
    }

    static void DrawLetters(Font f, TextBlock tb, TextItem item, float dx, float dy, Color col)
    {
        GL.Color(col);
        int shown = item.limit ? item.shown : int.MaxValue;
        bool cursorPlaced = false;
        float curX = tb.lines.Count > 0 ? tb.xs[0] : 0f, curY = tb.lines.Count > 0 ? tb.baselines[0] : 0f; // where the cursor goes
        for (int li = 0; li < tb.lines.Count; li++)
        {
            float x = tb.xs[li] + dx, baseline = tb.baselines[li] + dy;
            string line = tb.lines[li];
            for (int k = 0; k < line.Length; k++)
            {
                int gi = tb.starts[li] + k;
                if (!cursorPlaced && gi >= shown)
                {
                    // The next letter to type: the cursor sits here (or at the end of the previous line, if the
                    // next one to type is the line break)
                    if (gi == shown) { curX = x; curY = baseline; }
                    cursorPlaced = true;
                }
                if (!f.GetCharacterInfo(line[k], out CharacterInfo ci, tb.refSize, tb.style)) continue;
                if (gi < shown) Quad(ci, x, baseline, tb.scale);
                x += ci.advance * tb.scale + tb.letter;
                if (!cursorPlaced) { curX = x; curY = baseline; }
            }
        }
        if (item.hasCursor && item.cursorNow && f.GetCharacterInfo('_', out CharacterInfo cc, tb.refSize, tb.style))
            Quad(cc, curX, curY, tb.scale);
    }

    static void Quad(CharacterInfo ci, float x, float baseline, float scale)
    {
        float x0 = x + ci.minX * scale, x1 = x + ci.maxX * scale;
        float y0 = baseline + ci.minY * scale, y1 = baseline + ci.maxY * scale;
        GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x0, y0, 0f);
        GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x0, y1, 0f);
        GL.TexCoord(ci.uvTopRight); GL.Vertex3(x1, y1, 0f);
        GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x1, y0, 0f);
    }

    // Labels are a fixed size on screen; text overlays fill their box (or use their own size)
    static int TextSize(TextItem item, int screenHeight)
    {
        if (item.label) return Mathf.Max(4, Mathf.RoundToInt(screenHeight * 0.022f));
        ImageLayer o = item.overlay;
        if (o != null && o.fontSize > 0f) return Mathf.Max(4, Mathf.RoundToInt(o.fontSize * screenHeight / 1080f));
        float size = FitSize(o != null && o.text != null ? o.text : item.text, item.rect.height, o != null ? o.lineSpacing : 1f);
        return Mathf.Max(4, Mathf.RoundToInt(size * screenHeight / 1080f));
    }

    // "Fit the box" size at 1080p: several lines share its height (counted on the whole text, so it doesn't change while typing)
    static float FitSize(string text, float boxHeight, float lineSpacing)
    {
        int lines = 1;
        if (text != null) foreach (char ch in text) if (ch == '\n') lines++;
        float spacing = Mathf.Max(0.3f, lineSpacing);
        float size = 1080f * boxHeight * 0.6f;
        if (lines > 1) size = Mathf.Min(size, 1080f * boxHeight / (lines * 1.2f * spacing) * 0.95f);
        return size;
    }

    /// <summary>The size (pixels at 1080p) a text overlay set to "fit the box" gets right now.</summary>
    public static float AutoTextSize(ImageLayer im) => im == null ? 20f : FitSize(im.text, im.rect.height, im.lineSpacing);

    /// <summary>Text settings the game's plain UI Text can't do: these go through the shader instead.</summary>
    static bool TextNeedsShader(TextItem item)
    {
        ImageLayer o = item.label ? null : item.overlay;
        return o != null && (o.outline > 0f || o.shadow > 0f || Mathf.Abs(o.letterSpacing) > 0.001f);
    }

    Camera GetPreviewCam(int i)
    {
        while (previewCams.Count <= i) previewCams.Add(null);
        if (previewCams[i] == null)
        {
            var go = new GameObject(PreviewCamName) { hideFlags = HideFlags.HideAndDontSave };
            previewCams[i] = go.AddComponent<Camera>();
        }
        return previewCams[i];
    }

    // ---------------------------------------------------------------- Multiview

    // ---------------------------------------------------------------- Runtime views API

    // Finds a shot by name, ignoring upper/lower case and spaces at either end.
    public Shot GetShot(string shotName)
    {
        string wanted = (shotName ?? "").Trim();
        Shot s = shots.Find(x => x.name == shotName) ??
                 shots.Find(x => string.Equals((x.name ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (s == null)
            Debug.LogWarning($"[Cinematic] '{name}' (scene '{gameObject.scene.name}') has no shot named \"{shotName}\". " +
                             $"Its shots are: {string.Join(", ", shots.ConvertAll(x => $"\"{x.name}\""))}", this);
        return s;
    }

    /// <summary>
    /// Fill the whole multiview grid from code: the first point becomes view 1 (the main camera), the rest the other cells.
    /// Overrides the points set up in the editor for this play session.
    /// </summary>
    public void SetMultiview(string shotName, IList<Transform> cameraPoints)
    {
        Shot s = GetShot(shotName);
        if (s == null) return;
        int valid = 0;
        if (cameraPoints != null)
            foreach (Transform p in cameraPoints)
                if (p != null) valid++;
        if (valid == 0)
        {
            Debug.LogWarning($"[Cinematic] SetMultiview(\"{shotName}\") got no camera points (empty list or all missing), " +
                             "so the shot keeps its placeholder views.", this);
            return;
        }

        s.runtimePoint = cameraPoints[0];
        s.runtimeViews = new List<Transform>();
        for (int i = 1; i < cameraPoints.Count; i++) s.runtimeViews.Add(cameraPoints[i]);
        RefreshIfCurrent(s);
        Debug.Log($"[Cinematic] SetMultiview: shot \"{s.name}\" on '{name}' (scene '{gameObject.scene.name}') now uses " +
                  $"{valid} camera point(s) from code.", this);
    }

    /// <summary>Use these transforms as the shot's extra multiview cameras (view 1 stays the shot's own point).</summary>
    public void SetViews(string shotName, params Transform[] views)
    {
        Shot s = GetShot(shotName);
        if (s == null) return;
        s.runtimeViews = new List<Transform>(views);
        RefreshIfCurrent(s);
    }

    /// <summary>Same as SetViews, from positions and rotations (e.g. computed from where things spawned).</summary>
    public void SetViewPoses(string shotName, params Pose[] poses)
    {
        Shot s = GetShot(shotName);
        if (s == null) return;
        var list = new List<Transform>();
        foreach (Pose p in poses)
        {
            var go = new GameObject("Cinematic View Point (temp)") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetPositionAndRotation(p.position, p.rotation);
            poseAnchors.Add(go);
            list.Add(go.transform);
        }
        s.runtimeViews = list;
        RefreshIfCurrent(s);
    }

    /// <summary>Use this transform as the camera for one of the shot's pop-ups (0 = first pop-up).</summary>
    public void SetPopupPoint(string shotName, int popupIndex, Transform point)
    {
        Shot s = GetShot(shotName);
        if (s == null) return;
        if (s.popups == null || popupIndex < 0 || popupIndex >= s.popups.Count)
        {
            Debug.LogWarning($"[Cinematic] Shot '{shotName}' has no pop-up {popupIndex}");
            return;
        }
        s.popups[popupIndex].runtimePoint = point;
    }

    /// <summary>Go back to the points set up in the editor for this shot.</summary>
    public void ClearRuntimeViews(string shotName)
    {
        Shot s = GetShot(shotName);
        if (s == null) return;
        s.runtimePoint = null;
        s.runtimeViews = null;
        if (s.popups != null)
            foreach (Popup p in s.popups) p.runtimePoint = null;
        RefreshIfCurrent(s);
    }

    void RefreshIfCurrent(Shot s)
    {
        if (index >= 0 && shots[index] == s) currentViews = new List<Transform>(ResolveViews(s));
    }

    // Views set from code win; otherwise the ones set up in the editor.
    List<Transform> ResolveViews(Shot s) => s.runtimeViews ?? s.extraViews;

    // ---------------------------------------------------------------- Layers
    // Everything on screen during a shot: the main camera, multiview cells and pop-ups.

    struct Layer
    {
        public Vector3 pos;
        public Quaternion rot;
        public float fov;
        public Rect rect;   // viewport space: 0-1, from the bottom-left
        public Feed feed;
        public float alpha;
        public int seed;
        public Texture tex;   // set for image layers (no camera)
        public Color color;
        public float bracket;   // scan boxes: corner bracket length (0 = full frame)
        public int mode;        // 0 picture, 1 waveform, 2 progress bar
        public bool filmed;     // drawn underneath the whole-screen camera look
        public bool useTex;     // progress bar: fill with the texture
        public bool premul;     // texture colours are premultiplied (text drawn into a texture)
        public float lookOff;   // 0 = the look at full strength, 1 = none (the shot's look is timed on the timeline)
        public float progress;
        public float thickness;
        public ImageLayer overlay; // the overlay this came from (waveform / bar / glow settings), or null
        public float order;        // draw order: higher draws on top (see the Order constants)
        public float refreshFps;   // camera views in game: re-render this many times a second (0 = every frame)
        public float resolution;   // camera views in game: resolution scale (0 = full)
        public float wipe;         // 0 = all visible, 1 = all hidden (wiped away from the right)
        public Vector2 glowPad;    // the rect was grown by this much (0-1 of the rect) to make room for the glow
    }

    // Text drawn on top (text overlays and scan labels)
    public struct TextItem
    {
        public Rect rect;      // viewport space, 0-1 from the bottom-left
        public string text;
        public Color color;
        public float alpha;
        public bool label;     // true: small label above the box; false: fills the box
        public bool right;     // label above the box's right end instead of its left
        public Feed look;      // camera look to apply, or null
        public bool filmed;    // drawn underneath the whole-screen camera look
        public float lookOff;  // 0 = the look at full strength, 1 = none
        public ImageLayer overlay; // for glow, or null
        public float order;        // draw order, shared with the layers: higher draws on top
        public bool limit;         // only the first "shown" letters are drawn (typewriter / wipe); the layout uses all of them
        public int shown;
        public bool hasCursor;     // a typing cursor goes after the shown letters (it takes no space, so nothing shifts)
        public bool cursorNow;     // the cursor is visible right now (it blinks)
    }

    readonly List<TextItem> texts = new List<TextItem>();
    readonly Feed scanFeed = new Feed();
    readonly Feed overlayFeed = new Feed();

    /// <summary>How strong the shot's camera look is at this point (0-1): it has its own bar on the timeline.</summary>
    public static float LookAmount(Shot s, float local)
    {
        float end = s.lookDuration > 0f ? s.lookStart + s.lookDuration : s.duration;
        if (local < s.lookStart || local > end) return 0f;
        if (s.lookFade <= 0f) return 1f;
        float a = Mathf.Clamp01((local - s.lookStart) / s.lookFade);
        if (s.lookDuration > 0f) a = Mathf.Min(a, Mathf.Clamp01((end - local) / s.lookFade));
        return a;
    }

    // A shot's pop-ups at "local" seconds into it. carriedOnly: only ones running past the shot's end (shown in later shots)
    void AddPopups(Shot s, float local, List<Layer> list, int seedBase, bool carriedOnly)
    {
        if (carriedOnly && local <= s.duration) return;
        for (int i = 0; s.popups != null && i < s.popups.Count; i++)
        {
            Popup p = s.popups[i];
            Transform pt = PointOf(p);
            if (pt == null) continue;
            float end = p.duration > 0f ? p.appearAt + p.duration : s.duration;
            if (local < p.appearAt || local > end) continue;
            if (carriedOnly && end <= s.duration) continue; // only the part past its own shot

            if (p.effectsVersion == 0) UpgradePopup(p);
            Rect r = new Rect(p.rect.x, 1f - p.rect.y - p.rect.height, p.rect.width, p.rect.height);
            float wipe = 0f;
            float since = local - p.appearAt;
            float e = OverlayEffect(p.inEffect, p.inTime > 0f ? since / p.inTime : 1f, since, seedBase + i, ref r, ref wipe);
            if (p.duration > 0f) e *= OverlayEffect(p.outEffect, p.outTime > 0f ? (end - local) / p.outTime : 1f, since, seedBase + i + 50, ref r, ref wipe);

            list.Add(new Layer
            {
                pos = pt.position, rot = pt.rotation, fov = p.fov > 0f ? p.fov : 60f, rect = r,
                feed = p.feed, alpha = e, seed = seedBase + i, order = p.layer * LayerStep + OrderPopups + i * 0.01f, wipe = wipe
            });
        }

    }

    // ---- Effects that carry on into the next shot(s)
    // A camera look, pop-up or scan whose bar is dragged past its shot's end keeps going into the next shot(s),
    // with the settings of the shot it belongs to.

    Shot lookShot;         // while building a shot's layers: the shot whose camera look applies
    float lookShotLocal;   // and the time in that shot

    /// <summary>The earlier shot whose camera look is still running at this time (its bar goes past its end), or -1.</summary>
    public int CarriedLookFrom(int shotIndex, float globalTime)
    {
        for (int i = Mathf.Min(shotIndex, shots.Count) - 1; i >= 0; i--)
        {
            Shot p = shots[i];
            if (p == null || p.feed == null || !p.feed.look || p.lookDuration <= 0f) continue;
            float pl = globalTime - StartTimeOf(i);
            if (pl > p.duration && LookAmount(p, pl) > 0f) return i;
        }
        return -1;
    }

    // Decides whose camera look applies to this shot right now: its own while its look bar is running, otherwise
    // one carried over from an earlier shot (if any)
    void PickLook(Shot s, float local, float globalTime)
    {
        lookShot = s;
        lookShotLocal = local;
        if (s.feed != null && s.feed.look && LookAmount(s, local) > 0f) return;
        int idx = shots.IndexOf(s);
        int from = idx >= 0 ? CarriedLookFrom(idx, globalTime) : -1;
        if (from >= 0) { lookShot = shots[from]; lookShotLocal = globalTime - StartTimeOf(from); }
    }

    Feed CamLook(Shot s) => lookShot != null ? lookShot.feed : s.feed;

    // The shot's camera look without its frame or REC dot, for an overlay set to "Own" — otherwise null
    Feed LookFor(Shot s, CameraEffect effect)
    {
        bool own = effect == CameraEffect.Own || (effect == CameraEffect.None && ScopeOf(s) == LookScope.EachOverlay);
        Feed look = CamLook(s);
        if (!own || look == null || !look.look) return null;
        CopyLook(look, overlayFeed);
        overlayFeed.border = 0f;
        return overlayFeed;
    }

    static void CopyLook(Feed from, Feed to)
    {
        to.look = from != null && from.look;
        if (from == null) return;
        to.tint = from.tint;
        to.desaturate = from.desaturate;
        to.scanlines = from.scanlines;
        to.noise = from.noise;
        to.chromatic = from.chromatic;
        to.distortion = from.distortion;
        to.vignette = from.vignette;
        to.glitch = from.glitch;
        to.heat = from.heat;
        to.blur = from.blur;
        to.recDot = false;
    }

    static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);
    readonly List<Layer> layers = new List<Layer>();

    static Feed ViewFeed(Shot s, int i) => CustomViewFeed(s, i) ?? s.feed;

    // A multiview view's own look, or null when it's "same as view 1".
    // (Unity can't save an empty entry in a list of looks: it turns it into a blank look. So "same as view 1" is
    // a setting on the look itself instead.)
    public static Feed CustomViewFeed(Shot s, int i)
    {
        UpgradeViewFeeds(s);
        if (s.viewFeeds == null || i >= s.viewFeeds.Count) return null;
        Feed f = s.viewFeeds[i];
        return f != null && f.customised ? f : null;
    }

    static string blankFeedJson;

    // Looks saved before "customised" existed: ones that were saved as blanks were really "same as view 1"
    public static void UpgradeViewFeeds(Shot s)
    {
        if (s == null || s.viewFeedsVersion >= 1) return;
        s.viewFeedsVersion = 1;
        if (s.viewFeeds == null) return;
        if (blankFeedJson == null) blankFeedJson = JsonUtility.ToJson(new Feed());
        foreach (Feed f in s.viewFeeds)
            if (f != null) f.customised = JsonUtility.ToJson(f) != blankFeedJson;
    }

    public float StartTimeOf(int shotIndex)
    {
        float acc = 0f;
        for (int i = 0; i < shotIndex && i < shots.Count; i++) acc += shots[i].duration;
        return acc;
    }

    // local = seconds into the shot, globalTime = seconds into the whole cinematic (for images)
    Color layersBg = Color.black; // colour behind the cells (multiview gaps), set by BuildLayers
    Feed screenLook;              // look applied to the whole finished picture, or null, set by BuildLayers
    float screenLookOff;          // how much weaker it is right now (its timing on the timeline)

    void BuildLayers(Shot s, float local, float globalTime, Vector3 pos, Quaternion rot, float fov, List<Transform> views,
                     List<Layer> list, List<TextItem> textList)
    {
        list.Clear();
        textList.Clear();
        layersBg = new Color(s.gapColor.r, s.gapColor.g, s.gapColor.b, 1f);

        // Multiview grid: one cell per view slot, even empty ones (they show this shot's camera), so the layout
        // is always the one you set up. Views picked at play time fill the slots; missing ones fall back to the
        // views set in the editor, then to this shot's camera.
        int slotCount = Mathf.Max(views != null ? views.Count : 0, s.extraViews != null ? s.extraViews.Count : 0);
        int n = slotCount + 1;
        int cols = s.columns > 0 ? s.columns : Mathf.CeilToInt(Mathf.Sqrt(n));
        int rows = Mathf.CeilToInt(n / (float)cols);

        screenLook = null;
        PickLook(s, local, globalTime);
        Feed camLook = CamLook(s);
        Rect mainRect = n > 1 ? Cell(0, cols, rows, s.gap) : Full;
        if (s.background == ShotBackground.Camera)
            list.Add(new Layer { pos = pos, rot = rot, fov = fov, rect = mainRect, feed = camLook, alpha = 1f });
        else
        {
            // No camera: a colour or an image fills the main view
            bool image = s.background == ShotBackground.Image && s.backgroundImage != null;
            Color bg = image ? Color.white : new Color(s.backgroundColor.r, s.backgroundColor.g, s.backgroundColor.b, 1f);
            list.Add(new Layer { tex = image ? s.backgroundImage : Texture2D.whiteTexture, color = bg, rect = mainRect,
                                 feed = camLook, alpha = 1f });
        }
        int k = 1;
        for (int i = 0; i < slotCount; i++)
        {
            {
                Transform v = views != null && i < views.Count ? views[i] : null;
                if (v == null && !ReferenceEquals(views, s.extraViews) && s.extraViews != null && i < s.extraViews.Count) v = s.extraViews[i];
                // no camera here (or it was destroyed mid-shot, e.g. a collected pickup): this shot's own camera
                Vector3 vp = v != null ? v.position : pos;
                Quaternion vr = v != null ? v.rotation : rot;
                list.Add(new Layer
                {
                    pos = vp, rot = vr, fov = fov, rect = Cell(k, cols, rows, s.gap),
                    feed = CustomViewFeed(s, i) ?? camLook,
                    alpha = 1f, seed = k
                });
                k++;
            }
        }

        // Pop-ups on top: this shot's, plus earlier shots' that carry on into this one
        int curIdx = shots.IndexOf(s);

        for (int pi = 0; pi < curIdx; pi++)
            AddPopups(shots[pi], globalTime - StartTimeOf(pi), list, 1000 + pi * 20, true);
        AddPopups(s, local, list, 100, false);

        int scanFrom = list.Count, scanTextFrom = textList.Count;
        for (int pi = 0; pi < curIdx; pi++)
        {
            Shot ps = shots[pi];
            float pl = globalTime - StartTimeOf(pi);
            if (ps.scan != null && ps.scan.enabled && ps.scan.duration > 0f && pl > ps.duration)
            {
                int lf = list.Count, tf = textList.Count;
                AddScan(ps, pl, list, textList);
                SetOrder(list, lf, ps.scan.layer * LayerStep + OrderScan - 1f);
                SetOrder(textList, tf, ps.scan.layer * LayerStep + OrderScan - 1f);
            }
        }
        scanFrom = list.Count; scanTextFrom = textList.Count;
        AddScan(s, local, list, textList);
        float scanOrder = (s.scan != null ? s.scan.layer : 0) * LayerStep + OrderScan;
        SetOrder(list, scanFrom, scanOrder);
        SetOrder(textList, scanTextFrom, scanOrder);
        float fadeA = 0f;
        if (s.fadeIn > 0f) fadeA = Mathf.Max(fadeA, 1f - Mathf.Clamp01(local / s.fadeIn));
        if (s.fadeOut > 0f) fadeA = Mathf.Max(fadeA, 1f - Mathf.Clamp01((s.duration - local) / s.fadeOut));

        // Overlays on top of everything, timed on the whole cinematic
        for (int i = 0; images != null && i < images.Count; i++)
        {
            ImageLayer im = images[i];
            if (im == null || im.kind == OverlayKind.UIObject) continue;
            Feed ovLook = LookFor(s, im.cameraEffect);
            bool filmed = im.cameraEffect == CameraEffect.Filmed;
            if (im.kind == OverlayKind.Image && im.texture == null) continue;
            float end = im.start + Mathf.Max(0.05f, im.duration);
            if (globalTime < im.start || globalTime > end) continue;

            if (im.effectsVersion == 0) UpgradeOverlay(im);
            int ovFrom = list.Count, ovTextFrom = textList.Count;
            float since = globalTime - im.start, left = end - globalTime;
            Rect r = new Rect(im.rect.x, 1f - im.rect.y - im.rect.height, im.rect.width, im.rect.height);
            float wipe = 0f;
            float e = OverlayEffect(im.inEffect, im.inTime > 0f ? since / im.inTime : 1f, since, i, ref r, ref wipe);
            e *= OverlayEffect(im.outEffect, im.outTime > 0f ? left / im.outTime : 1f, since, i + 50, ref r, ref wipe);
            e *= OverlayLoopEffect(im, since, i, ref r);

            switch (im.kind)
            {
                case OverlayKind.Text:
                    string shownText = OverlayText(im, since, wipe, out int shownCount, out bool cursorNow);
                    textList.Add(new TextItem { rect = r, text = shownText, limit = true, shown = shownCount, hasCursor = im.cursor,
                                                cursorNow = cursorNow, color = im.color, alpha = e,
                                                look = ovLook, filmed = filmed, overlay = im });
                    break;
                case OverlayKind.Waveform:
                {
                    // Time range where it moves, eased in and out so it doesn't snap
                    float moveEnd = im.waveMoveDuration > 0f ? im.waveMoveStart + im.waveMoveDuration : float.MaxValue;
                    float amp = Mathf.Max(0f, im.waveHeight) *
                                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((since - im.waveMoveStart) / 0.3f)) *
                                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((moveEnd - since) / 0.3f));
                    Texture waveTex = null;
                    if (im.followSound != null)
                    {
                        try { waveTex = FollowTexture(im, globalTime, since); }
                        catch (Exception ex)
                        {
                            // Whatever goes wrong with the sound, the rest of the cinematic keeps drawing
                            if (unreadable.Add(im.followSound))
                                Debug.LogWarning($"[Cinematic] Waveform '{im.name}' can't follow '{im.followSound.name}': {ex.Message}", this);
                        }
                    }
                    list.Add(Glowing(new Layer { tex = waveTex != null ? waveTex : Texture2D.whiteTexture, useTex = waveTex != null,
                                                 color = im.color, rect = r, alpha = e, seed = 200 + i, mode = 1,
                                                 thickness = im.thickness, progress = amp, feed = ovLook, filmed = filmed,
                                                 overlay = im, wipe = wipe }));
                    break;
                }
                case OverlayKind.ProgressBar:
                    float fillLen = im.fillTime > 0f ? im.fillTime : Mathf.Max(0.05f, im.duration);
                    float progress = BarProgress(im, Mathf.Clamp01(since / fillLen), i);
                    list.Add(Glowing(new Layer { tex = im.texture != null ? im.texture : Texture2D.whiteTexture, useTex = im.texture != null,
                                                 color = im.color, rect = r, alpha = e, seed = 200 + i,
                                                 mode = 2, thickness = im.thickness, progress = progress, feed = ovLook, filmed = filmed,
                                                 overlay = im, wipe = wipe }));
                    if (!string.IsNullOrEmpty(im.barText))
                        textList.Add(new TextItem { rect = r, text = im.barText, color = im.color, alpha = e, label = true, look = ovLook,
                                                    filmed = filmed, overlay = im });
                    if (im.showPercent)
                        textList.Add(new TextItem { rect = r, text = Mathf.FloorToInt(progress * 100f) + "%", color = im.color,
                                                    alpha = e, label = true, right = true, look = ovLook, filmed = filmed, overlay = im });
                    break;
                default:
                    list.Add(Glowing(new Layer { tex = im.texture, color = im.color, rect = r, alpha = e, seed = 200 + i, feed = ovLook,
                                                 filmed = filmed, overlay = im, wipe = wipe }));
                    break;
            }
            // Overlays stack in their list order (later on top), in front of pop-ups and scans unless asked otherwise;
            // a bar's text goes just above its bar
            float ovOrder = im.layer * LayerStep + OrderOverlays + i * 0.01f;
            SetOrder(list, ovFrom, ovOrder);
            SetOrder(textList, ovTextFrom, ovOrder + 0.001f);
        }

        // Anything "filmed"? Then the camera's look goes over the whole finished picture instead of just the camera
        bool anyFilmed = false;
        foreach (Layer l in list) anyFilmed |= l.filmed;
        foreach (TextItem ti in textList) anyFilmed |= ti.filmed;
        if (ScopeOf(s) == LookScope.WholeScreen) anyFilmed = true; // older setting
        if (anyFilmed && camLook != null && camLook.NeedsShader)
        {
            screenLook = camLook;
            for (int i = 0; i < list.Count; i++)
            {
                Layer l = list[i];
                if (i == 0) l.feed = null;              // the main camera's look now covers the whole screen
                if (i < n) l.filmed = true;             // main camera + multiview cells are part of the picture
                if (ScopeOf(s) == LookScope.WholeScreen && l.seed >= 200 && l.seed < 999) l.filmed = true;
                list[i] = l;
            }
            if (ScopeOf(s) == LookScope.WholeScreen)
                for (int i = 0; i < textList.Count; i++) { TextItem ti = textList[i]; ti.filmed = true; textList[i] = ti; }
        }

        // The shot's look is timed: weaken it outside its bar on the timeline
        float lookOff = 1f - LookAmount(lookShot ?? s, lookShot != null ? lookShotLocal : local);
        screenLookOff = lookOff;
        if (lookOff > 0f)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Layer l = list[i];
                if (l.feed != null && (ReferenceEquals(l.feed, camLook) || ReferenceEquals(l.feed, overlayFeed) || ReferenceEquals(l.feed, scanFeed)))
                {
                    l.lookOff = lookOff;
                    list[i] = l;
                }
            }
            for (int i = 0; i < textList.Count; i++)
            {
                TextItem ti = textList[i];
                if (ti.look != null) { ti.lookOff = lookOff; textList[i] = ti; }
            }
        }

        // Fade to / from a colour covers everything
        if (fadeA > 0f)
            list.Add(new Layer { tex = Texture2D.whiteTexture, color = new Color(s.fadeColor.r, s.fadeColor.g, s.fadeColor.b, 1f),
                                 rect = Full, alpha = fadeA, seed = 999, order = OrderFade });

        // The camera (and multiview cells) always at the bottom, then everything by its draw order
        for (int i = 0; i < n && i < list.Count; i++)
        {
            Layer l = list[i];
            l.order = OrderCamera;
            if (n > 1) { l.refreshFps = s.viewFps; l.resolution = s.viewResolution; } // the grid's own speed / sharpness
            list[i] = l;
        }
        SortByOrder(list);
        SortByOrder(textList);
    }

    // ---- Draw order
    // Higher draws on top. Each item's Layer number counts most; on the same number: pop-ups, then the scan,
    // then overlays (in list order). The camera is always at the bottom and fades always on top.
    const float LayerStep = 1000f, OrderPopups = 10f, OrderScan = 20f, OrderOverlays = 100f;
    const float OrderCamera = -1e7f, OrderFade = 1e7f;

    static void SetOrder(List<Layer> list, int from, float order)
    {
        for (int i = from; i < list.Count; i++) { Layer l = list[i]; l.order = order; list[i] = l; }
    }

    static void SetOrder(List<TextItem> list, int from, float order)
    {
        for (int i = from; i < list.Count; i++) { TextItem t = list[i]; t.order = order; list[i] = t; }
    }

    // Stable: things with the same order keep the order they were added in
    static void SortByOrder(List<Layer> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            Layer x = list[i];
            int j = i - 1;
            while (j >= 0 && list[j].order > x.order) { list[j + 1] = list[j]; j--; }
            list[j + 1] = x;
        }
    }

    static void SortByOrder(List<TextItem> list)
    {
        for (int i = 1; i < list.Count; i++)
        {
            TextItem x = list[i];
            int j = i - 1;
            while (j >= 0 && list[j].order > x.order) { list[j + 1] = list[j]; j--; }
            list[j + 1] = x;
        }
    }

    // ---------------------------------------------------------------- Scan boxes

    static float Hash(float a, float b) => Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f, 1f);

    // ---------------------------------------------------------------- Overlay effects

    // Overlays made before the in / out effects existed: carry over their animation and blink
    // Pop-ups made before in / out effects: "pop in / out" on or off
    public static void UpgradePopup(Popup p)
    {
        p.inEffect = p.outEffect = p.animate ? OverlayTransition.Pop : OverlayTransition.None;
        p.inTime = p.outTime = 0.2f;
        p.effectsVersion = 1;
    }

    public static void UpgradeOverlay(ImageLayer im)
    {
        OverlayTransition fx = im.animation == ImageAnimation.None ? OverlayTransition.None
                             : im.animation == ImageAnimation.Pop ? OverlayTransition.Pop : OverlayTransition.Fade;
        im.inEffect = im.outEffect = fx;
        im.inTime = im.outTime = 0.25f;
        if (im.blink)
        {
            im.loopEffect = OverlayLoop.Blink;
            im.loopRate = im.blinkRate > 0f ? im.blinkRate : 2f;
            im.loopStrength = 0.85f;
        }
        im.effectsVersion = 1;
    }

    static void ScaleRect(ref Rect r, float k)
    {
        Vector2 center = r.center;
        r.size *= k;
        r.center = center;
    }

    // k: 0 = fully out, 1 = fully in. Returns the opacity; may move / resize the rect or wipe it.
    static float OverlayEffect(OverlayTransition fx, float k, float time, int seed, ref Rect r, ref float wipe)
    {
        if (fx == OverlayTransition.None) return 1f;
        k = Mathf.Clamp01(k);
        if (k >= 1f) return 1f;
        float e = 1f - (1f - k) * (1f - k); // ease out
        switch (fx)
        {
            case OverlayTransition.Pop:
                float x = k - 1f; // back ease: overshoots a little, then settles
                ScaleRect(ref r, Mathf.LerpUnclamped(0.6f, 1f, 1f + 2.70158f * x * x * x + 1.70158f * x * x));
                return e;
            case OverlayTransition.Zoom: ScaleRect(ref r, Mathf.Lerp(1.4f, 1f, e)); return e;
            case OverlayTransition.SlideLeft: r.x -= (1f - e) * 0.25f; return e;
            case OverlayTransition.SlideRight: r.x += (1f - e) * 0.25f; return e;
            case OverlayTransition.SlideUp: r.y += (1f - e) * 0.25f; return e;
            case OverlayTransition.SlideDown: r.y -= (1f - e) * 0.25f; return e;
            case OverlayTransition.Wipe: wipe = Mathf.Max(wipe, 1f - e); return 1f;
            case OverlayTransition.Flicker:
            {
                float f = Mathf.Floor(time * 30f);
                return Hash(f, seed * 7.13f + 1.1f) < 0.15f + 0.85f * k * k ? 1f : 0.06f;
            }
            case OverlayTransition.Glitch:
            {
                float f = Mathf.Floor(time * 24f);
                if (Hash(f, seed + 3.3f) > k)
                {
                    r.x += (Hash(f, seed + 9.1f) - 0.5f) * 0.08f;
                    r.y += (Hash(f, seed + 4.4f) - 0.5f) * 0.02f;
                    return Hash(f, seed + 5.7f) > 0.4f ? 0.85f : 0.1f;
                }
                return 1f;
            }
            default: return e; // Fade
        }
    }

    // What the overlay keeps doing while it's on screen. Returns the opacity; may move the rect.
    static float OverlayLoopEffect(ImageLayer im, float t, int seed, ref Rect r)
    {
        float rate = Mathf.Max(0.01f, im.loopRate), str = Mathf.Clamp01(im.loopStrength);
        switch (im.loopEffect)
        {
            case OverlayLoop.Blink: return Mathf.Repeat(t * rate, 1f) > 0.6f ? 1f - str : 1f;
            case OverlayLoop.Pulse: return 1f - str * 0.5f * (1f - Mathf.Cos(t * rate * Mathf.PI * 2f));
            case OverlayLoop.Flicker:
            {
                float f = Mathf.Floor(t * 12f);
                return Hash(f, seed + 1.7f) < str * Mathf.Clamp01(rate / 6f) ? Mathf.Lerp(0.1f, 0.6f, Hash(f, seed + 2.9f)) : 1f;
            }
            case OverlayLoop.Float: r.y += Mathf.Sin(t * rate * Mathf.PI * 2f) * str * 0.015f; return 1f;
            case OverlayLoop.Shake:
            {
                float f = Mathf.Floor(t * Mathf.Max(1f, rate));
                r.x += (Hash(f, seed + 0.3f) - 0.5f) * str * 0.012f;
                r.y += (Hash(f, seed + 4.2f) - 0.5f) * str * 0.02f;
                return 1f;
            }
            case OverlayLoop.Glitch:
            {
                float f = Mathf.Floor(t * 12f);
                if (Hash(f, seed + 6.1f) < Mathf.Clamp01(rate / 12f))
                {
                    r.x += (Hash(f, seed + 8.2f) - 0.5f) * 0.1f * str;
                    return Hash(f, seed + 3.6f) > 0.5f ? 1f : 1f - str * 0.7f;
                }
                return 1f;
            }
            default: return 1f;
        }
    }

    const string ScrambleChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#%&@$<>/";

    // The text as it looks right now: typed out, scrambling, wiped, with a cursor
    // The whole text (so it's laid out once and never shifts), how many letters show so far, and the cursor
    static string OverlayText(ImageLayer im, float since, float wipe, out int shown, out bool cursorNow)
    {
        string full = im.text ?? "";
        if (im.allCaps) full = full.ToUpperInvariant();
        int len = full.Length;
        float speed = im.typeSpeed > 0f ? im.typeSpeed : 22f;
        shown = im.typewriter ? Mathf.Clamp(Mathf.FloorToInt(since * speed), 0, len) : len;
        if (wipe > 0f) shown = Mathf.Min(shown, Mathf.CeilToInt(len * (1f - wipe)));
        bool typing = im.typewriter && shown < len;
        cursorNow = im.cursor && (typing || Mathf.Repeat(since * 2f, 1f) < 0.5f);
        if (!im.scramble) return full;

        var sb = new System.Text.StringBuilder(full);
        float frame = Mathf.Floor(since * 20f);
        for (int j = 0; j < shown; j++)
        {
            char ch = full[j];
            // Each letter settles a moment after it appears (or one after another, without typewriter)
            float settle = (im.typewriter ? j / speed : j / (speed * 2f)) + 0.35f;
            if (since < settle && !char.IsWhiteSpace(ch))
                sb[j] = ScrambleChars[Mathf.Min(ScrambleChars.Length - 1, (int)(Hash(j + 0.5f, frame) * ScrambleChars.Length))];
        }
        return sb.ToString();
    }

    // How full the bar is, with its loading style. x = 0..1 of the loading time
    static float BarProgress(ImageLayer im, float x, int seed)
    {
        x = Mathf.Clamp01(x);
        switch (im.barFill)
        {
            case BarFill.EaseOut: return 1f - (1f - x) * (1f - x);
            case BarFill.EaseInOut: return Mathf.SmoothStep(0f, 1f, x);
            case BarFill.Stutter:
            {
                // Like a real loading bar: jumps forward, stalls, jumps again. Same every time it plays.
                if (x >= 1f) return 1f;
                const int N = 7;
                float total = 0f;
                for (int j = 0; j < N; j++) total += 0.3f + Hash(j + 10f, seed);
                int k = Mathf.Min(N - 1, Mathf.FloorToInt(x * N));
                float before = 0f;
                for (int j = 0; j < k; j++) before += 0.3f + Hash(j + 10f, seed);
                float step = 0.3f + Hash(k + 10f, seed);
                float climb = 0.25f + 0.6f * Hash(k + 30f, seed); // part of the segment spent moving, the rest stalled
                float u = x * N - k;
                return (before + step * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / climb))) / total;
            }
            default: return x;
        }
    }

    // Grows the layer's rect to make room for its glow
    Layer Glowing(Layer l)
    {
        ImageLayer im = l.overlay;
        if (im == null || im.glow <= 0f || im.glowSize <= 0f) return l;
        float aspect = Cam != null && Cam.aspect > 0.01f ? Cam.aspect : 16f / 9f;
        float dy = im.glowSize * 1.3f / 1080f, dx = dy / aspect;
        Rect g = new Rect(l.rect.x - dx, l.rect.y - dy, l.rect.width + 2f * dx, l.rect.height + 2f * dy);
        l.glowPad = new Vector2(dx / g.width, dy / g.height);
        l.rect = g;
        return l;
    }

    static bool HasGlow(ImageLayer im) => im != null && im.glow > 0f && im.glowSize > 0f;

    // ---------------------------------------------------------------- Waveform following a sound

    class WaveData
    {
        public float[] peaks; // mono, the loudest sample (with its sign) in each slice
        public float rate;    // slices per second
        public float norm;    // the loudest sample in the whole sound
    }

    static readonly Dictionary<AudioClip, WaveData> waveCache = new Dictionary<AudioClip, WaveData>();
    readonly Dictionary<ImageLayer, Texture2D> followTex = new Dictionary<ImageLayer, Texture2D>();
    const int FollowRes = 256;
    Color32[] followPixels;

    /// <summary>A waveform can only read a sound's samples when its Load Type is Decompress On Load.</summary>
    public static bool CanReadSound(AudioClip clip) => clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad;

    /// <summary>Forget a sound's cached samples (after its import settings changed).</summary>
    public static void ForgetSound(AudioClip clip) { if (clip != null) { waveCache.Remove(clip); unreadable.Remove(clip); } }

    // Sounds whose samples couldn't be read: not tried again every frame (until their import settings change)
    static readonly HashSet<AudioClip> unreadable = new HashSet<AudioClip>();

    /// <summary>True when the sound was tried and its samples couldn't be read (the waveform falls back to its made-up wiggle).</summary>
    public static bool FailedToRead(AudioClip clip) => clip != null && unreadable.Contains(clip);

    static WaveData WaveOf(AudioClip clip)
    {
        if (!CanReadSound(clip) || unreadable.Contains(clip)) return null;
        if (waveCache.TryGetValue(clip, out WaveData d) && d != null) return d;
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded) return null; // still loading: try again next frame
        }

        int ch = Mathf.Max(1, clip.channels), total = clip.samples, freq = Mathf.Max(1, clip.frequency);
        int step = Mathf.Max(1, freq / 4000); // ~4000 slices a second is plenty for the look
        var peaks = new float[total / step + 1];
        float norm = 0f;
        int chunk = freq; // a second at a time, so long sounds don't need a huge buffer
        float[] buf = new float[chunk * ch];
        for (int off = 0; off < total; off += chunk)
        {
            int cnt = Mathf.Min(chunk, total - off);
            float[] b = cnt == chunk ? buf : new float[cnt * ch];
            if (!clip.GetData(b, off))
            {
                unreadable.Add(clip);
                Debug.LogWarning($"[Cinematic] Couldn't read the samples of '{clip.name}' for a waveform, so it shows its made-up wiggle. " +
                                 "Check the sound's import settings (Load Type: Decompress On Load).", clip);
                return null;
            }
            for (int si = 0; si < cnt; si++)
            {
                float v = 0f;
                for (int c = 0; c < ch; c++) v += b[si * ch + c];
                v /= ch;
                int idx = (off + si) / step;
                if (Mathf.Abs(v) > Mathf.Abs(peaks[idx])) peaks[idx] = v;
                if (Mathf.Abs(v) > norm) norm = Mathf.Abs(v);
            }
        }
        d = new WaveData { peaks = peaks, rate = freq / (float)step, norm = Mathf.Max(norm, 0.01f) };
        waveCache[clip] = d;
        return d;
    }

    // The bit of the sound that is playing right now, as a 256 x 1 texture for the shader
    // A baked waveform (see BakedWave) turned into WaveData, kept per sound
    readonly Dictionary<AudioClip, KeyValuePair<byte[], WaveData>> bakedData = new Dictionary<AudioClip, KeyValuePair<byte[], WaveData>>();

    WaveData BakedWaveOf(AudioClip clip)
    {
        if (clip == null || bakedWaves == null) return null;
        foreach (BakedWave bw in bakedWaves)
        {
            if (bw == null || bw.clip != clip || bw.peaks == null || bw.peaks.Length == 0 || bw.rate <= 0f) continue;
            if (bakedData.TryGetValue(clip, out var kept) && ReferenceEquals(kept.Key, bw.peaks)) return kept.Value;
            var peaks = new float[bw.peaks.Length];
            float norm = 0f;
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i] = (bw.peaks[i] - 128) / 127f;
                norm = Mathf.Max(norm, Mathf.Abs(peaks[i]));
            }
            var d = new WaveData { peaks = peaks, rate = bw.rate, norm = Mathf.Max(norm, 0.01f) };
            bakedData[clip] = new KeyValuePair<byte[], WaveData>(bw.peaks, d);
            return d;
        }
        return null;
    }

    /// <summary>True when this sound's waveform has been read and stored with the cinematic.</summary>
    public bool HasBakedWave(AudioClip clip) => BakedWaveOf(clip) != null;

    Texture2D FollowTexture(ImageLayer im, float globalTime, float since)
    {
        WaveData d = BakedWaveOf(im.followSound) ?? WaveOf(im.followSound);
        if (d == null) return null;

        // Where in the sound are we? Follows the sound's bar on the timeline; if it isn't on the timeline,
        // the sound starts when the waveform appears.
        float ct = -1f, level = 1f;
        bool onTimeline = false;
        if (sounds != null)
            foreach (SoundClip sc in sounds)
            {
                if (sc == null || sc.clip != im.followSound) continue;
                onTimeline = true;
                float len = LengthOf(sc), g = globalTime - sc.start;
                if (g < 0f || g >= len) continue;
                ct = sc.clipStart + g;
                if (sc.loop && sc.clip.length > 0f) ct = Mathf.Repeat(ct, sc.clip.length);
                if (sc.fadeIn > 0f) level = Mathf.Min(level, g / sc.fadeIn);
                if (sc.fadeOut > 0f) level = Mathf.Min(level, (len - g) / sc.fadeOut);
                break;
            }
        if (!onTimeline) ct = since;

        if (!followTex.TryGetValue(im, out Texture2D tex) || tex == null)
        {
            tex = new Texture2D(FollowRes, 1, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            followTex[im] = tex;
        }
        if (followPixels == null) followPixels = new Color32[FollowRes];

        float w = Mathf.Max(0.01f, im.followWindow);
        float gain = Mathf.Max(0f, im.followGain) * Mathf.Clamp01(level) / d.norm;
        int n = d.peaks.Length;
        for (int j = 0; j < FollowRes; j++)
        {
            float v = 0f;
            if (ct >= 0f)
            {
                // Newest sound on the right, like a scrolling audio editor
                float t0 = ct - w + w * j / FollowRes, t1 = t0 + w / FollowRes;
                int i0 = Mathf.FloorToInt(t0 * d.rate), i1 = Mathf.Max(i0 + 1, Mathf.FloorToInt(t1 * d.rate));
                for (int si = Mathf.Max(0, i0); si < i1 && si < n; si++)
                    if (Mathf.Abs(d.peaks[si]) > Mathf.Abs(v)) v = d.peaks[si];
            }
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v * gain * 0.5f + 0.5f) * 255f);
            followPixels[j] = new Color32(b, b, b, 255);
        }
        tex.SetPixels32(followPixels);
        tex.Apply(false);
        return tex;
    }

    static Rect ToViewport(Rect topLeft) =>
        new Rect(topLeft.x, 1f - topLeft.y - topLeft.height, topLeft.width, topLeft.height);

    static Rect Scaled(Rect r, float scale)
    {
        Vector2 center = r.center;
        r.size *= scale;
        r.center = center;
        return r;
    }

    void AddScan(Shot s, float local, List<Layer> list, List<TextItem> textList)
    {
        Scan sc = s.scan;
        if (sc == null || !sc.enabled) return;
        float end = sc.duration > 0f ? sc.start + sc.duration : s.duration;
        if (local < sc.start || local > end) return;

        float t = local - sc.start;
        float left = sc.duration > 0f ? end - local : float.MaxValue; // the exit only plays when the scan has its own end
        bool hasTargets = sc.targets != null && sc.targets.Count > 0;
        float lockAt = sc.mode == ScanMode.Targets ? 0f
                     : sc.mode == ScanMode.Random || !hasTargets ? float.MaxValue
                     : Mathf.Max(0f, sc.searchTime);

        Feed ovLook = LookFor(s, sc.cameraEffect);
        bool filmed = sc.cameraEffect == CameraEffect.Filmed;
        CopyLook(ovLook, scanFeed);
        scanFeed.border = Mathf.Max(0.5f, sc.thickness);
        scanFeed.borderColor = sc.color;
        Color fill = new Color(sc.color.r, sc.color.g, sc.color.b, sc.fill);
        float bracket = sc.brackets ? 0.25f : 0f;
        float sizeMin = Mathf.Max(0.01f, Mathf.Min(sc.sizeRange.x, sc.sizeRange.y));
        float sizeMax = Mathf.Max(sizeMin, Mathf.Max(sc.sizeRange.x, sc.sizeRange.y));
        Rect area = ToViewport(sc.searchArea);
        float outT = Mathf.Max(0.01f, sc.boxOutTime);

        // Search boxes: each one jumps to a new spot every so often, with its own entrance and exit
        if (sc.mode != ScanMode.Targets)
        {
            // Once the targets lock on, the decoys leave (unless they keep searching)
            float afterLock = t - lockAt;
            for (int k = 0; k < sc.randomCount; k++)
            {
                Rect r = SearchBox(sc, k, t, area, sizeMin, sizeMax, out int cycle, out float intoJump, out float leftInJump);
                float a = SearchBoxFx(sc, ref r, out float w, Mathf.Min(intoJump, t), Mathf.Min(Mathf.Min(leftInJump, left),
                                      afterLock >= 0f && !sc.keepSearching ? Mathf.Max(0f, outT - afterLock) : float.MaxValue), k * 31 + cycle);
                if (a <= 0f) continue;
                list.Add(new Layer { tex = Texture2D.whiteTexture, color = fill, rect = r, alpha = a, wipe = w,
                                     feed = scanFeed, bracket = bracket, seed = 300 + k, filmed = filmed });
            }
        }

        if (!hasTargets) return;
        bool seek = sc.mode == ScanMode.SearchThenLock;
        float tOut = Mathf.Max(0.01f, sc.targetOutTime);
        for (int i = 0; i < sc.targets.Count; i++)
        {
            ScanTarget target = sc.targets[i];
            if (target == null) continue;
            int seeker = 100 + i; // its own search pattern, separate from the decoys
            float lockTime = lockAt + i * 0.25f;
            Rect box = ToViewport(target.rect);

            // Before locking on: it searches like the other search boxes
            if (t < lockTime)
            {
                if (!seek) continue;
                Rect r = SearchBox(sc, seeker, t, area, sizeMin, sizeMax, out int cycle, out float intoJump, out float leftInJump);
                // its last search spot stays put until it flies off to the target
                float a = SearchBoxFx(sc, ref r, out float w, Mathf.Min(intoJump, t), Mathf.Min(leftInJump, left), seeker * 31 + cycle);
                if (a > 0f)
                    list.Add(new Layer { tex = Texture2D.whiteTexture, color = fill, rect = r, alpha = a, wipe = w,
                                         feed = scanFeed, bracket = bracket, seed = 400 + i, filmed = filmed });
                continue;
            }

            // Locking on
            float tt = t - lockTime;
            float lockDur = Mathf.Max(0.01f, sc.lockTime);
            Rect drawn;
            float lockAlpha = 1f, lockWipe = 0f;
            if (seek)
            {
                // Fly from where it was searching onto the target, then the lock-on effect
                float fly = Mathf.Clamp01(tt / lockDur);
                fly = 1f - (1f - fly) * (1f - fly);
                Rect from = SearchBox(sc, seeker, lockTime, area, sizeMin, sizeMax, out _, out _, out _);
                drawn = new Rect(Vector2.Lerp(from.position, box.position, fly), Vector2.Lerp(from.size, box.size, fly));
                if (sc.lockEffect != OverlayTransition.Zoom && sc.lockEffect != OverlayTransition.Fade && tt >= lockDur)
                    lockAlpha = OverlayEffect(sc.lockEffect, (tt - lockDur) / lockDur, tt, 400 + i, ref drawn, ref lockWipe);
            }
            else if (sc.lockEffect == OverlayTransition.Zoom)
            {
                float e = Mathf.Clamp01(tt / lockDur);
                e = 1f - (1f - e) * (1f - e);
                drawn = Scaled(box, Mathf.Lerp(1.6f, 1f, e)); // the classic: zooms in onto it
                lockAlpha = Mathf.Clamp01(tt / 0.1f);
            }
            else
            {
                drawn = box;
                lockAlpha = OverlayEffect(sc.lockEffect, tt / lockDur, tt, 400 + i, ref drawn, ref lockWipe);
            }

            // Leaving when the scan ends
            Rect labelRect = box;
            float outAlpha = 1f, labelWipe = 0f;
            if (left < tOut)
            {
                outAlpha = OverlayEffect(sc.targetOut, left / tOut, t, 450 + i, ref drawn, ref lockWipe);
                OverlayEffect(sc.targetOut, left / tOut, t, 450 + i, ref labelRect, ref labelWipe);
            }

            float blink = sc.lockBlink && tt > lockDur && tt < lockDur + 0.4f && Mathf.Repeat(tt * 8f, 1f) > 0.5f ? 0.35f : 1f;
            list.Add(new Layer { tex = Texture2D.whiteTexture, color = fill, rect = drawn,
                                 alpha = lockAlpha * blink * outAlpha, feed = scanFeed, wipe = lockWipe,
                                 bracket = bracket, seed = 400 + i, filmed = filmed });

            float labelAt = lockDur + 0.05f;
            if (string.IsNullOrEmpty(target.label) || tt < labelAt) continue;
            int chars = Mathf.Clamp(Mathf.FloorToInt((tt - labelAt) / 0.03f), 0, target.label.Length);
            if (labelWipe > 0f) chars = Mathf.Min(chars, Mathf.CeilToInt(target.label.Length * (1f - labelWipe)));
            textList.Add(new TextItem { rect = labelRect, text = target.label.Substring(0, chars), color = sc.color,
                                        alpha = outAlpha, label = true, look = ovLook, filmed = filmed });
        }
    }

    // A search box's entrance and exit: "into" seconds since it appeared, "left" seconds until it goes
    static float SearchBoxFx(Scan sc, ref Rect r, out float wipe, float into, float left, int seed)
    {
        wipe = 0f;
        float inT = Mathf.Max(0.01f, sc.boxInTime), outT = Mathf.Max(0.01f, sc.boxOutTime);
        float a = OverlayEffect(sc.boxIn, into / inT, into, seed, ref r, ref wipe);
        a *= OverlayEffect(sc.boxOut, left / outT, into, seed + 17, ref r, ref wipe);
        if (left <= 0f) a = 0f; // gone (also when its exit is "None": it's just gone)
        return a;
    }

    // A searching box: jumps to a new random spot every so often (same result for the same time).
    // intoJump / leftInJump: seconds since it arrived at this spot, and until it leaves it
    static Rect SearchBox(Scan sc, int k, float t, Rect area, float sizeMin, float sizeMax,
                          out int cycle, out float intoJump, out float leftInJump)
    {
        float period = Mathf.Lerp(0.35f, 0.9f, Hash(k, 1.7f)) / Mathf.Max(0.05f, sc.speed);
        float tt = t + Hash(k, 9.1f) * period;
        cycle = Mathf.FloorToInt(tt / period);
        intoJump = tt - cycle * period;
        leftInJump = period - intoJump;

        float w = Mathf.Lerp(sizeMin, sizeMax, Hash(k, cycle + 0.3f));
        float shape = Mathf.Lerp(0.7f, 1.6f, Hash(k, cycle + 7.7f)); // width / height in pixels
        float h = w * (16f / 9f) / shape;
        float x = area.x + Hash(k, cycle + 2.1f) * Mathf.Max(0f, area.width - w);
        float y = area.y + Hash(k, cycle + 5.3f) * Mathf.Max(0f, area.height - h);
        return new Rect(x, y, w, h);
    }

    // ---------------------------------------------------------------- Sounds

    GameObject soundRoot;
    readonly List<AudioSource> soundSources = new List<AudioSource>();

    /// <summary>Plays the cinematic's sounds as they'd sound at this time (the editor uses it for previewing).</summary>
    public void PreviewSounds(float time, bool playing) => UpdateSounds(time, playing);

    void UpdateSounds(float time, bool playing)
    {
        int count = sounds != null ? sounds.Count : 0;
        for (int i = 0; i < count; i++)
        {
            SoundClip sc = sounds[i];
            AudioSource src = i < soundSources.Count ? soundSources[i] : null;
            float len = LengthOf(sc);
            bool on = playing && sc != null && sc.clip != null && time >= sc.start && time < sc.start + len;
            if (!on)
            {
                if (src != null && src.isPlaying) src.Stop();
                continue;
            }

            float offset = time - sc.start + Mathf.Max(0f, sc.clipStart);
            if (sc.loop) offset = Mathf.Repeat(offset, sc.clip.length);
            else if (offset >= sc.clip.length)
            {
                if (src != null && src.isPlaying) src.Stop();
                continue;
            }

            float vol = sc.volume;
            float into = time - sc.start, left = sc.start + len - time;
            if (sc.fadeIn > 0f) vol *= Mathf.Clamp01(into / sc.fadeIn);
            if (sc.fadeOut > 0f) vol *= Mathf.Clamp01(left / sc.fadeOut);

            src = GetSoundSource(i);
            if (src.clip != sc.clip) { src.Stop(); src.clip = sc.clip; }
            src.loop = sc.loop;
            src.volume = vol;
            if (!src.isPlaying)
            {
                src.time = offset;
                src.Play();
            }
            else if (Mathf.Abs(src.time - offset) > 0.2f) src.time = offset; // keep in sync
        }
        for (int i = count; i < soundSources.Count; i++)
            if (soundSources[i] != null && soundSources[i].isPlaying) soundSources[i].Stop();
    }

    AudioSource GetSoundSource(int i)
    {
        if (soundRoot == null) soundRoot = new GameObject("Cinematic Sounds (temp)") { hideFlags = HideFlags.HideAndDontSave };
        while (soundSources.Count <= i) soundSources.Add(null);
        if (soundSources[i] == null)
        {
            var go = new GameObject("Sound") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(soundRoot.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D
            soundSources[i] = src;
        }
        return soundSources[i];
    }

    // ---------------------------------------------------------------- UI object overlays

    readonly Dictionary<GameObject, bool> uiShown = new Dictionary<GameObject, bool>();

    // Switches "UI object" overlays on while they're on the timeline (Play Mode only)
    void UpdateUIObjects(float globalTime, bool playing)
    {
        if (!Application.isPlaying || images == null) return;
        foreach (ImageLayer im in images)
        {
            if (im == null || im.kind != OverlayKind.UIObject || im.target == null) continue;
            bool on = playing && globalTime >= im.start && globalTime <= im.start + Mathf.Max(0.05f, im.duration);
            if (uiShown.TryGetValue(im.target, out bool was) && was == on) continue;
            if (on || was) im.target.SetActive(on); // only switch off what we switched on
            uiShown[im.target] = on;
        }
    }

    // ---------------------------------------------------------------- Screen display
    // Multiview cells, pop-ups and feed looks are drawn by hidden cameras into render textures,
    // shown on a hidden overlay canvas. A plain full-screen shot just uses the real camera.

    const string DisplayName = "Cinematic Display (temp)";
    const string FeedShaderName = "Cinematic/Camera Feed";

    class Slot
    {
        public Camera cam;
        public RenderTexture rt;
        public RawImage image;
        public Material mat;
        public bool live;
    }

    GameObject displayRoot;
    Canvas displayCanvas;
    RawImage displayBg;
    readonly List<Slot> slots = new List<Slot>();
    bool warnedShader;

    static float FeedTime => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    bool EnsureShader()
    {
        if (feedShader == null) feedShader = Shader.Find(FeedShaderName);
        if (feedShader != null) return true;
        if (!warnedShader)
        {
            warnedShader = true;
            Debug.LogWarning($"[Cinematic] Shader '{FeedShaderName}' not found. Add CinematicFeed.shader to the project " +
                             "for multiviews, pop-ups and camera feed looks.");
        }
        return false;
    }

    // ---- Drawing at the end of the frame
    // Some plugins (Gsplat among them) queue their drawing just before the frame is rendered (start of PostLateUpdate).
    // The cinematic's own views are rendered by hand, so they must happen after that, or those objects are missing.
    bool drawPending;

    void DrawPending()
    {
        if (!drawPending) return;
        drawPending = false;
        if (index < 0 || !isActiveAndEnabled || layers.Count == 0) return;
        ShowLayers(layers, texts);
    }

    static class LateDraw
    {
        static readonly List<CinematicController> waiting = new List<CinematicController>();
        static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { installed = false; waiting.Clear(); } // the player loop may have been reset

        public static void Add(CinematicController c)
        {
            Install();
            if (!waiting.Contains(c)) waiting.Add(c);
        }

        static void Run()
        {
            if (waiting.Count == 0) return;
            var now = waiting.ToArray();
            waiting.Clear();
            foreach (CinematicController c in now)
            {
                if (c == null) continue;
                try { c.DrawPending(); }
                catch (Exception e) { Debug.LogException(e, c); }
            }
        }

        static void Install()
        {
            if (installed) return;
            installed = true;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            if (root.subSystemList == null) return;
            for (int i = 0; i < root.subSystemList.Length; i++)
            {
                if (root.subSystemList[i].type != typeof(PostLateUpdate)) continue;
                var subs = new List<PlayerLoopSystem>(root.subSystemList[i].subSystemList ?? new PlayerLoopSystem[0]);
                if (subs.Exists(x => x.type == typeof(LateDraw))) return;
                // Right after any plugin step put at the start (e.g. Gsplat's), before the frame is rendered
                int at = 0;
                for (int k = 0; k < subs.Count; k++)
                    if (subs[k].type != null && subs[k].type.Namespace != "UnityEngine.PlayerLoop") at = k + 1;
                    else break;
                subs.Insert(at, new PlayerLoopSystem { type = typeof(LateDraw), updateDelegate = Run });
                root.subSystemList[i].subSystemList = subs.ToArray();
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }
    }

    void ShowLayers(List<Layer> list, List<TextItem> textList)
    {
        bool ownText = false;
        foreach (TextItem ti in textList) ownText |= ti.look != null || HasGlow(ti.overlay) || TextNeedsShader(ti); // these need the shader
        foreach (Layer gl in list) ownText |= HasGlow(gl.overlay);                             // glows are drawn in passes
        // The game's plain UI draws all text above all pictures: if a picture should be above some text, use the shader
        float lowestText = float.MaxValue;
        foreach (TextItem ti in textList) if (!string.IsNullOrEmpty(ti.text)) lowestText = Mathf.Min(lowestText, ti.order);
        foreach (Layer ol in list) ownText |= ol.order > lowestText;
        Layer first = list[0];
        bool onlyCamera = first.tex == null && first.rect == Full && (first.feed == null || !first.feed.NeedsShader) &&
                          list.Count == 1 && textList.Count == 0 && screenLook == null && crossAlpha <= 0f;
        if (!useUiLayers && !onlyCamera && Cam != null && EnsureShader())
        {
            // Built into one picture the same way the editor's viewer does it (so the game looks exactly like it)
            ShowComposite(list, textList);
            return;
        }
        if (!useUiLayers && onlyCamera)
        {
            HideDisplay(); // just the camera: nothing to draw on top
            return;
        }
        if (compositeImage != null) compositeImage.gameObject.SetActive(false);
        Layer main = list[0];
        if (main.tex != null) HideMask(); // no camera in this shot: the real camera has nothing to draw
        else RestoreMask();

        bool mainPlain = main.tex == null && main.rect == Full && (main.feed == null || !main.feed.NeedsShader);
        if ((mainPlain && list.Count == 1 && textList.Count == 0) || Cam == null || !EnsureShader())
        {
            HideDisplay();
            return;
        }

        EnsureDisplay();
        displayRoot.SetActive(true);
        displayCanvas.sortingOrder = displaySortOrder;
        displayBg.gameObject.SetActive(!mainPlain); // a plain main camera shows through underneath
        displayBg.color = layersBg;

        ScreenPixels(out int pw, out int ph);
        pw = Mathf.Max(1, pw); ph = Mathf.Max(1, ph);
        float aspect = pw / (float)ph, ft = FeedTime;
        int used = 0;
        for (int i = mainPlain ? 1 : 0; i < list.Count; i++)
        {
            Layer l = list[i];
            Slot slot = GetSlot(used++);

            if (l.tex != null)
            {
                // Image: just show the texture
                slot.live = false;
                slot.cam.gameObject.SetActive(false);
                slot.image.texture = l.tex;
            }
            else
            {
                int w = Mathf.Max(16, Mathf.RoundToInt(pw * l.rect.width));
                int h = Mathf.Max(16, Mathf.RoundToInt(ph * l.rect.height));
                if (slot.rt == null || slot.rt.width != w || slot.rt.height != h)
                {
                    if (slot.rt != null) { slot.rt.Release(); Kill(slot.rt); }
                    slot.rt = new RenderTexture(w, h, 24) { hideFlags = HideFlags.HideAndDontSave };
                }

                if (Application.isPlaying)
                {
                    // A helper camera renders this view every frame
                    if (!slot.live)
                    {
                        CopyCameraSettings(Cam, slot.cam); // when it first appears
                        slot.live = true;
                    }
                    slot.cam.targetTexture = slot.rt;
                    slot.cam.rect = Full;
                    slot.cam.enabled = true;
                    slot.cam.transform.SetPositionAndRotation(l.pos, l.rot);
                    slot.cam.fieldOfView = l.fov;
                    slot.cam.gameObject.SetActive(true);
                }
                else
                {
                    // Edit Mode: render it once now with the real camera
                    slot.live = false;
                    slot.cam.gameObject.SetActive(false);
                    RenderView(null, l.pos, l.rot, l.fov, slot.rt);
                }
                slot.image.texture = slot.rt;
            }

            RectTransform rt = slot.image.rectTransform;
            rt.anchorMin = l.rect.min;
            rt.anchorMax = l.rect.max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            SetFeed(slot.mat, l, aspect, ft);
            // Unity's UI keeps its own snapshot of a material's settings: without this it keeps drawing with the
            // shader's defaults (no look, no frame, and a fade that's plain white)
            if (slot.image.material != slot.mat) slot.image.material = slot.mat;
            slot.image.SetMaterialDirty();
            slot.image.gameObject.SetActive(true);
        }
        for (int i = used; i < slots.Count; i++) Park(slots[i]);
        ShowTexts(textList, ph);
    }

    readonly List<Text> textSlots = new List<Text>();
    readonly List<Material> textMats = new List<Material>();
    static Font defaultFont;

    // The font set on the controller, or a monospaced system font
    Font TextFont()
    {
        if (font != null) return font;
        if (defaultFont != null) return defaultFont;
        defaultFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New", "Arial" }, 32);
        if (defaultFont == null)
        {
            try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (Exception) { defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
        }
        return defaultFont;
    }

    // For the game's UI Text: letters not typed yet are still there, just invisible, so the text doesn't shift.
    // The cursor always takes its space (invisible while it blinks off).
    static string UiText(TextItem item)
    {
        string text = item.text ?? "";
        if (!item.limit && !item.hasCursor) return text;
        int shown = item.limit ? Mathf.Clamp(item.shown, 0, text.Length) : text.Length;
        const string Clear = "<color=#00000000>", End = "</color>";
        var sb = new System.Text.StringBuilder(text.Length + 40);
        sb.Append(text, 0, shown);
        if (item.hasCursor) sb.Append(item.cursorNow ? "_" : Clear + "_" + End);
        if (shown < text.Length) sb.Append(Clear).Append(text, shown, text.Length - shown).Append(End);
        return sb.ToString();
    }

    void ShowTexts(List<TextItem> textList, int screenHeight)
    {
        int used = 0;
        for (int i = 0; i < textList.Count; i++)
        {
            TextItem item = textList[i];
            if (string.IsNullOrEmpty(item.text)) continue;
            while (textSlots.Count <= used)
            {
                var go = new GameObject("Text", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(displayCanvas.transform, false);
                var txt = go.AddComponent<Text>();
                txt.raycastTarget = false;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                textSlots.Add(txt);
            }
            int slotIndex = used;
            Text t = textSlots[used++];
            t.font = TextFont();
            t.supportRichText = true;
            t.text = UiText(item);
            t.color = new Color(item.color.r, item.color.g, item.color.b, item.color.a * item.alpha);

            // With a camera look, the text goes through the feed shader too
            if (item.look != null && feedShader != null)
            {
                while (textMats.Count <= slotIndex) textMats.Add(null);
                if (textMats[slotIndex] == null) textMats[slotIndex] = new Material(feedShader) { hideFlags = HideFlags.HideAndDontSave };
                SetFeed(textMats[slotIndex], new Layer { feed = item.look, alpha = 1f, mode = 3, tex = t.mainTexture, color = Color.white,
                                                         seed = 500 + i, rect = Full }, 16f / 9f, FeedTime);
                t.material = textMats[slotIndex];
                t.SetMaterialDirty(); // see ShowLayers: the UI needs telling that the material's settings changed
            }
            else t.material = null;

            RectTransform rt = t.rectTransform;
            if (item.label)
            {
                // small label just above the box's top corner
                t.alignment = item.right ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
                t.fontSize = TextSize(item, screenHeight);
                t.fontStyle = FontStyle.Normal;
                t.lineSpacing = 1f;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                rt.anchorMin = rt.anchorMax = new Vector2(item.right ? item.rect.xMax : item.rect.xMin, item.rect.yMax);
                rt.pivot = new Vector2(item.right ? 1f : 0f, 0f);
                rt.anchoredPosition = new Vector2(0f, 4f);
                rt.sizeDelta = new Vector2(800f, t.fontSize * 1.4f);
            }
            else
            {
                // fills its box, with the overlay's alignment and style
                ImageLayer o = item.overlay;
                int h = o != null ? (int)o.textAlign : 0, v = o != null ? (int)o.textVAlign : 1;
                t.alignment = (TextAnchor)(v * 3 + h); // TextAnchor: UpperLeft..LowerRight, row by row
                t.fontSize = TextSize(item, screenHeight);
                t.fontStyle = o != null ? o.fontStyle : FontStyle.Normal;
                t.lineSpacing = o != null ? Mathf.Max(0.3f, o.lineSpacing) : 1f;
                t.horizontalOverflow = o != null && o.wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                rt.anchorMin = item.rect.min;
                rt.anchorMax = item.rect.max;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            t.gameObject.SetActive(true);
            t.transform.SetAsLastSibling();
        }
        for (int i = used; i < textSlots.Count; i++)
            if (textSlots[i] != null) textSlots[i].gameObject.SetActive(false);
    }

    // Whole-screen look: everything is put together in one texture and shown full screen
    RawImage compositeImage;
    RenderTexture compositeRT;
    // The older way of showing layers in game: one UI image per layer with the feed shader. Some setups (seen with
    // HDRP) draw those UI images without the shader's settings (no looks, no frames, white fades), so the game
    // now builds one picture like the editor's viewer instead.
    static readonly bool useUiLayers = false;

    bool maskHidden;
    int savedMask;
    Rect savedCamRect;

    void ShowComposite(List<Layer> list, List<TextItem> textList)
    {
        EnsureDisplay();
        displayRoot.SetActive(true);
        displayCanvas.sortingOrder = displaySortOrder;
        displayBg.gameObject.SetActive(false);
        foreach (Slot slot in slots) Park(slot);
        foreach (Text t in textSlots)
            if (t != null) t.gameObject.SetActive(false);

        ScreenPixels(out int pw, out int ph);
        pw = Mathf.Max(16, pw); ph = Mathf.Max(16, ph);
        if (compositeRT == null || compositeRT.width != pw || compositeRT.height != ph)
        {
            if (compositeRT != null) { compositeRT.Release(); Kill(compositeRT); }
            compositeRT = new RenderTexture(pw, ph, 24) { hideFlags = HideFlags.HideAndDontSave };
        }

        // In Play Mode the real camera would draw the same picture underneath: make it draw nothing meanwhile
        HideMask();

        drawingGame = true;
        try
        {
            CompositeInto(compositeRT, list, textList, screenLook);
            if (crossAlpha > 0f) CompositeCrossfade(compositeRT, crossFrame, crossAlpha);
        }
        finally { drawingGame = false; }
        if (compositeImage == null) compositeImage = NewImage("Whole screen");
        compositeImage.texture = compositeRT;
        compositeImage.gameObject.SetActive(true);
        compositeImage.transform.SetAsLastSibling();
    }

    // While the cinematic's own picture covers the screen, the real camera doesn't need to draw it full size.
    // It keeps drawing the scene, just tiny and hidden underneath: if it drew nothing, HDRP's automatic exposure
    // would drift to "pitch dark" and the next plain shot would come out blown-out white.
    void HideMask()
    {
        if (!Application.isPlaying || maskHidden || Cam == null) return;
        savedCamRect = Cam.rect;
        savedMask = Cam.cullingMask;
        Cam.rect = new Rect(0f, 0f, 0.08f, 0.08f);
        if (!keepExposureCamera) Cam.cullingMask = 0; // draws nothing at all: cheapest, but auto exposure drifts
        maskHidden = true;
    }

    // The size of the camera's real picture on screen, even while it's shrunk out of the way (see HideMask)
    void ScreenPixels(out int w, out int h)
    {
        if (maskHidden && Cam != null && Cam.rect.width > 0f && Cam.rect.height > 0f)
        {
            w = Mathf.RoundToInt(Cam.pixelWidth / Cam.rect.width * savedCamRect.width);
            h = Mathf.RoundToInt(Cam.pixelHeight / Cam.rect.height * savedCamRect.height);
            return;
        }
        w = Cam != null ? Cam.pixelWidth : Screen.width;
        h = Cam != null ? Cam.pixelHeight : Screen.height;
    }

    void RestoreMask()
    {
        if (!maskHidden) return;
        maskHidden = false;
        if (Cam == null) return;
        Cam.rect = savedCamRect;
        Cam.cullingMask = savedMask;
    }

    void HideDisplay()
    {
        RestoreMask();
        if (compositeImage != null) compositeImage.gameObject.SetActive(false);
        if (displayRoot == null) return;
        foreach (Slot slot in slots) Park(slot);
        foreach (Text t in textSlots)
            if (t != null) t.gameObject.SetActive(false);
        displayRoot.SetActive(false);
    }

    static void Park(Slot slot)
    {
        slot.live = false;
        if (slot.cam != null) slot.cam.gameObject.SetActive(false);
        if (slot.image != null) slot.image.gameObject.SetActive(false);
    }

    void EnsureDisplay()
    {
        if (displayRoot != null) return;
        displayRoot = new GameObject(DisplayName) { hideFlags = HideFlags.HideAndDontSave };

        var canvasGo = new GameObject("Canvas", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        canvasGo.transform.SetParent(displayRoot.transform, false);
        displayCanvas = canvasGo.AddComponent<Canvas>();
        displayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        displayBg = NewImage("Background");
        displayBg.color = Color.black;
    }

    RawImage NewImage(string name)
    {
        var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
        go.transform.SetParent(displayCanvas.transform, false);
        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    Slot GetSlot(int i)
    {
        while (slots.Count <= i)
        {
            var camGo = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.SetParent(displayRoot.transform, false);
            var slot = new Slot
            {
                cam = camGo.AddComponent<Camera>(),
                image = NewImage("Layer"),
                mat = new Material(feedShader) { hideFlags = HideFlags.HideAndDontSave }
            };
            slot.image.material = slot.mat;
            slots.Add(slot);
        }
        return slots[i];
    }

    void DestroyDisplay()
    {
        ClearViewCache();
        foreach (Texture2D wt in followTex.Values) if (wt != null) Kill(wt);
        followTex.Clear();
        RestoreMask();
        compositeImage = null; // destroyed with the display
        if (compositeRT != null) { compositeRT.Release(); Kill(compositeRT); }
        compositeRT = null;
        textSlots.Clear(); // destroyed with the display
        foreach (Material m in textMats) Kill(m);
        textMats.Clear();
        foreach (Slot slot in slots)
        {
            if (slot.rt != null) { slot.rt.Release(); Kill(slot.rt); }
            Kill(slot.mat);
        }
        slots.Clear();
        Kill(displayRoot);
        displayRoot = null;
        displayCanvas = null;
        displayBg = null;
    }

    static void Kill(UnityEngine.Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    static readonly int IdLook = Shader.PropertyToID("_Look"), IdTint = Shader.PropertyToID("_Tint"),
        IdDesat = Shader.PropertyToID("_Desaturate"), IdScan = Shader.PropertyToID("_Scanlines"),
        IdNoise = Shader.PropertyToID("_Noise"), IdChroma = Shader.PropertyToID("_Chromatic"),
        IdDistort = Shader.PropertyToID("_Distortion"), IdVignette = Shader.PropertyToID("_Vignette"),
        IdGlitch = Shader.PropertyToID("_Glitch"), IdRec = Shader.PropertyToID("_RecDot"),
        IdBorder = Shader.PropertyToID("_Border"), IdBorderColor = Shader.PropertyToID("_BorderColor"),
        IdAlpha = Shader.PropertyToID("_Alpha"), IdTime = Shader.PropertyToID("_FeedTime"),
        IdSeed = Shader.PropertyToID("_Seed"), IdSize = Shader.PropertyToID("_Size"),
        IdColor = Shader.PropertyToID("_Color"), IdTexAlpha = Shader.PropertyToID("_TexAlpha"),
        IdBracket = Shader.PropertyToID("_Bracket"), IdMode = Shader.PropertyToID("_Mode"),
        IdProgress = Shader.PropertyToID("_Progress"), IdThickness = Shader.PropertyToID("_Thickness"),
        IdHeat = Shader.PropertyToID("_Heat"), IdBlur = Shader.PropertyToID("_Blur"),
        IdUseTex = Shader.PropertyToID("_UseTex"), IdPremul = Shader.PropertyToID("_Premul"),
        IdWave = Shader.PropertyToID("_Wave"), IdWave2 = Shader.PropertyToID("_Wave2"),
        IdBar = Shader.PropertyToID("_Bar"), IdBar2 = Shader.PropertyToID("_Bar2"), IdFillColor = Shader.PropertyToID("_FillColor"),
        IdGlow = Shader.PropertyToID("_Glow"), IdGlowColor = Shader.PropertyToID("_GlowColor"),
        IdGlowPad = Shader.PropertyToID("_GlowPad"), IdWipe = Shader.PropertyToID("_Wipe"),
        IdBlurStep = Shader.PropertyToID("_BlurStep"), IdScanCount = Shader.PropertyToID("_ScanCount");

    static void SetFeed(Material m, Layer l, float screenAspect, float time)
    {
        Feed f = l.feed;
        m.SetFloat(IdLook, f != null && f.look ? 1f - Mathf.Clamp01(l.lookOff) : 0f);
        if (f != null)
        {
            m.SetColor(IdTint, f.tint.maxColorComponent > 0f ? f.tint : Color.white);
            m.SetFloat(IdDesat, f.desaturate);
            m.SetFloat(IdScan, f.scanlines);
            m.SetFloat(IdScanCount, f.scanlineCount > 0 ? f.scanlineCount : 360f);
            m.SetFloat(IdNoise, f.noise);
            m.SetFloat(IdChroma, f.chromatic);
            m.SetFloat(IdDistort, f.distortion);
            m.SetFloat(IdVignette, f.vignette);
            m.SetFloat(IdGlitch, f.glitch);
            m.SetFloat(IdHeat, f.heat);
            m.SetFloat(IdBlur, f.blur);
            m.SetFloat(IdRec, f.recDot ? 1f : 0f);
            m.SetFloat(IdBorder, f.border);
            m.SetColor(IdBorderColor, f.borderColor);
        }
        else m.SetFloat(IdBorder, 0f);
        m.SetFloat(IdAlpha, l.alpha);
        bool image = l.tex != null;
        m.SetColor(IdColor, image ? l.color : Color.white);
        m.SetFloat(IdTexAlpha, image ? 1f : 0f); // images keep their transparency; camera images are opaque
        m.SetFloat(IdBracket, l.bracket);
        m.SetFloat(IdMode, l.mode);
        m.SetFloat(IdProgress, l.progress);
        m.SetFloat(IdThickness, l.thickness);
        m.SetFloat(IdUseTex, l.useTex ? 1f : 0f);
        m.SetFloat(IdPremul, l.premul ? 1f : 0f);
        m.SetFloat(IdTime, time);
        m.SetFloat(IdSeed, l.seed * 17.31f);
        // Size in "pixels at 1080p", so borders and scanlines look the same at any resolution (the shape itself, without the glow's room)
        Vector2 gp = l.glowPad;
        m.SetVector(IdSize, new Vector4(l.rect.width * (1f - 2f * gp.x) * 1080f * screenAspect,
                                        l.rect.height * (1f - 2f * gp.y) * 1080f, 0f, 0f));
        m.SetVector(IdGlowPad, new Vector4(gp.x, gp.y, 0f, 0f));
        m.SetFloat(IdWipe, Mathf.Clamp01(l.wipe));

        ImageLayer o = l.overlay;
        if (HasGlow(o))
        {
            m.SetVector(IdGlow, new Vector4(o.glow, o.glowSize, o.glowOwnColours ? 1f : 0f, 0f));
            m.SetColor(IdGlowColor, o.glowColor);
        }
        else m.SetVector(IdGlow, Vector4.zero);

        if (l.mode == 1 && o != null)
        {
            float from = Mathf.Clamp01(Mathf.Min(o.waveFrom, o.waveTo)), to = Mathf.Clamp01(Mathf.Max(o.waveFrom, o.waveTo));
            m.SetVector(IdWave, new Vector4(from, Mathf.Max(to, from + 0.01f), o.waveSpeed, o.waveDetail));
            m.SetVector(IdWave2, new Vector4((int)o.waveStyle, Mathf.Max(1, o.waveBars), 0f, 0f));
        }
        else
        {
            m.SetVector(IdWave, new Vector4(0.35f, 0.95f, 1f, 1f));
            m.SetVector(IdWave2, new Vector4(0f, 32f, 0f, 0f));
        }

        if (l.mode == 2 && o != null)
        {
            m.SetVector(IdBar, new Vector4((int)o.barDirection, Mathf.Max(0, o.barSegments), o.barFrame ? 1f : 0f, Mathf.Max(0f, o.barGap)));
            m.SetVector(IdBar2, new Vector4(Mathf.Clamp01(o.barTrack), 0f, 0f, 0f));
            m.SetColor(IdFillColor, o.barFillColor);
        }
        else
        {
            m.SetVector(IdBar, new Vector4(0f, 0f, 1f, 3f));
            m.SetVector(IdBar2, Vector4.zero);
            m.SetColor(IdFillColor, Color.white);
        }
    }

    // Grid cell i, filled left to right, top to bottom (viewport y goes up)
    public static Rect Cell(int i, int cols, int rows) => Cell(i, cols, rows, 0f);

    // Same, with a gap (pixels at 1080p, 16:9 screen) between cells and around the edges
    public static Rect Cell(int i, int cols, int rows, float gapPx)
    {
        float gx = Mathf.Max(0f, gapPx) / 1920f, gy = Mathf.Max(0f, gapPx) / 1080f;
        float w = Mathf.Max(0.01f, (1f - gx * (cols + 1)) / cols);
        float h = Mathf.Max(0.01f, (1f - gy * (rows + 1)) / rows);
        int col = i % cols, row = i / cols;
        return new Rect(gx + col * (w + gx), 1f - (gy + row * (h + gy)) - h, w, h);
    }

    // Renders one view into a texture.
    // Edit Mode borrows the real camera for a moment (so exposure, volumes and post effects match exactly,
    // which matters in HDRP, where a new camera's auto exposure doesn't settle outside Play Mode).
    // Play Mode uses the given helper camera, so the game's camera is never touched.
    void RenderView(Camera helper, Vector3 pos, Quaternion rot, float fov, RenderTexture target)
    {
        Camera cam0 = Cam;
        if (cam0 == null || target == null) return;

        if (helper == null)
        {
            Transform tr = cam0.transform;
            Vector3 p0 = tr.position;
            Quaternion r0 = tr.rotation;
            float f0 = cam0.fieldOfView;
            Rect rect0 = cam0.rect;
            RenderTexture t0 = cam0.targetTexture;

            int mask0 = cam0.cullingMask;
            tr.SetPositionAndRotation(pos, rot);
            cam0.fieldOfView = fov;
            cam0.rect = Full;
            if (maskHidden) cam0.cullingMask = savedMask; // it may be set to draw nothing while hidden
            cam0.targetTexture = target;
            cam0.Render();

            cam0.targetTexture = t0;
            cam0.cullingMask = mask0;
            cam0.rect = rect0;
            cam0.fieldOfView = f0;
            tr.SetPositionAndRotation(p0, r0);
            return;
        }

        CopyCameraSettings(cam0, helper);
        helper.enabled = false; // only renders when asked, never to the screen
        helper.rect = Full;
        if (maskHidden) helper.cullingMask = savedMask; // the real camera may be set to draw nothing: not the helpers
        helper.transform.SetPositionAndRotation(pos, rot);
        helper.fieldOfView = fov;
        helper.targetTexture = target;
        helper.Render();
        helper.targetTexture = null;
    }

    static readonly string[] PipelineProps =
    {
        "renderPostProcessing", "antialiasing", "antialiasingQuality", "renderShadows",
        "volumeLayerMask", "stopNaN", "dithering", "requiresColorTexture", "requiresDepthTexture"
    };

    // HDRP throws away a camera's history (auto exposure, temporal effects) when it skips a few frames, so a helper
    // camera that renders now and then starts from a default exposure each time, which can come out blown-out
    // white. "Persistent history" keeps it.
    static void KeepHistory(Component hdData)
    {
        if (hdData == null) return;
        Type t = hdData.GetType();
        var prop = t.GetProperty("hasPersistentHistory");
        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool)) { prop.SetValue(hdData, true); return; }
        var field = t.GetField("hasPersistentHistory");
        if (field != null && field.FieldType == typeof(bool)) field.SetValue(hdData, true);
    }

    static void CopyCameraSettings(Camera src, Camera dst)
    {
        dst.CopyFrom(src);

        // Render pipeline settings (e.g. URP's post-processing toggle) live on a separate component
        foreach (MonoBehaviour comp in src.GetComponents<MonoBehaviour>())
        {
            if (comp == null) continue;
            Type type = comp.GetType();
            if (!type.Name.EndsWith("AdditionalCameraData")) continue;

            Component copy = dst.GetComponent(type);
            if (copy == null) copy = dst.gameObject.AddComponent(type);

            // HDRP's camera data can copy itself completely (exposure, volumes, post-processing...)
            var copyTo = type.GetMethod("CopyTo", new[] { type });
            if (copyTo != null)
            {
                copyTo.Invoke(comp, new object[] { copy });
                KeepHistory(copy);
                continue;
            }

            if (type.Name == "HDAdditionalCameraData" && !warnedNoCopy)
            {
                // In a build this usually means Unity stripped the code: the cinematic's cameras then miss the camera's
                // HDRP settings (custom passes such as Gaussian splats, volumes, exposure). link.xml keeps it.
                warnedNoCopy = true;
                Debug.LogWarning("[Cinematic] Couldn't copy the camera's HDRP settings to the cinematic's cameras, so custom passes " +
                                 "(e.g. Gaussian splats), volumes or exposure may be missing in camera looks / multiviews. Add the " +
                                 "link.xml that comes with the Cinematic Controller to your Assets folder.");
            }

            foreach (string name in PipelineProps)
            {
                var prop = type.GetProperty(name);
                if (prop != null && prop.CanRead && prop.CanWrite) prop.SetValue(copy, prop.GetValue(comp));
            }
        }
    }

    static bool warnedNoCopy;

    void OnValidate()
    {
        MigrateImages();
        if (feedShader == null) feedShader = Shader.Find(FeedShaderName);

        // Unity leaves new list entries zeroed, so give fresh shots sane defaults.
        foreach (Shot s in shots)
        {
            if (s.fov > 0f) continue;
            s.duration = 3f;
            s.transitionDuration = 1f;
            s.fov = 60f;
            s.endFov = 40f;
        }

#if UNITY_EDITOR
        // Refresh the preview straight away when values change in the Inspector.
        if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }
}
