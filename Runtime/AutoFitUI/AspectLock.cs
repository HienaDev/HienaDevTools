using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Locks the game to one shape (16:9 by default) on every screen: the cameras draw in the biggest 16:9 box that fits,
    /// centred, with bars (black) around it; fitted UI keeps inside the box too (SafeArea follows Box). Switched on in
    /// Tools > Auto Fit UI ("Always 16:9"); works in builds on its own.
    /// </summary>
    public class AspectLock : MonoBehaviour
    {
        /// <summary>The box the game is drawn in, as 0..1 of the screen (the whole screen when nothing is locked).</summary>
        public static Rect Box { get; private set; } = new Rect(0, 0, 1, 1);
        public static bool Active { get; private set; }

        /// <summary>The biggest box of that shape centred in a w x h screen, as 0..1 of the screen.</summary>
        public static Rect BoxFor(float w, float h, float aspect)
        {
            if (w <= 0 || h <= 0 || aspect <= 0) return new Rect(0, 0, 1, 1);
            float screen = w / h;
            if (Mathf.Abs(screen - aspect) < 0.001f) return new Rect(0, 0, 1, 1);
            if (screen > aspect) { float bw = aspect / screen; return new Rect((1 - bw) / 2, 0, bw, 1); }
            float bh = screen / aspect; return new Rect(0, (1 - bh) / 2, 1, bh);
        }

        float m_Aspect = 16f / 9f; Color m_Bars = Color.black;
        Camera m_BarCam;
        Vector2Int m_LastScreen;
        readonly Dictionary<Camera, Rect> m_Original = new Dictionary<Camera, Rect>();
        readonly Dictionary<Camera, Rect> m_Set = new Dictionary<Camera, Rect>(); // (what we gave it last: anything else means the game changed it)

        static AspectLock s_Instance;
        internal static System.Action s_EnsureUIFits; // (set by Auto Fit UI: makes every Canvas keep inside the box)

        // ---- from your own scripts, at any time:  AspectLock.Lock(16, 9);  AspectLock.Lock(2560, 1080);  AspectLock.Unlock();
        /// <summary>Lock the game to this shape from now on (a ratio like 16, 9 or a size like 2560, 1080). Changing it while locked is fine.</summary>
        public static void Lock(float width, float height) { if (width > 0 && height > 0) Lock(width / height); }
        public static void Lock(float aspect) { Begin(aspect, s_Instance ? s_Instance.m_Bars : BarColour); }
        /// <summary>Back to the whole screen, no bars.</summary>
        public static void Unlock()
        {
            if (!s_Instance) return;
            var l = s_Instance; s_Instance = null; Active = false; Box = new Rect(0, 0, 1, 1);
            l.Restore(); Destroy(l.gameObject);
        }
        /// <summary>The locked shape (width / height), 0 when not locked.</summary>
        public static float Aspect => s_Instance && Active ? s_Instance.m_Aspect : 0f;
        /// <summary>The colour of the bars.</summary>
        public static Color BarColour { get => s_Instance ? s_Instance.m_Bars : s_BarColour; set { s_BarColour = value; if (s_Instance) s_Instance.m_Bars = value; } }
        static Color s_BarColour = Color.black;

        // (Enter Play Mode without a domain reload keeps statics: start clean every time)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Instance = null; Active = false; Box = new Rect(0, 0, 1, 1); }

        public static void Begin(float aspect, Color bars)
        {
            if (aspect <= 0) return;
            s_BarColour = bars;
            s_EnsureUIFits?.Invoke();
            if (s_Instance) { s_Instance.m_Aspect = aspect; s_Instance.m_Bars = bars; s_Instance.m_LastScreen = default; Box = BoxFor(Screen.width, Screen.height, aspect); Active = true; return; }
            // hidden, but NOT DontSave: it lives in the DontDestroyOnLoad scene, which Unity empties when Play mode ends
            var go = new GameObject("Aspect Lock") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable };
            DontDestroyOnLoad(go);
            var l = go.AddComponent<AspectLock>(); l.m_Aspect = aspect; l.m_Bars = bars; s_Instance = l;
            Box = BoxFor(Screen.width, Screen.height, aspect); // (right away: UI that starts this frame already keeps inside it)
            Active = true;
        }

        void Awake()
        {
            // a camera behind everything that only paints the bars
            var cg = new GameObject("Aspect Lock bars") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable };
            cg.transform.SetParent(transform, false);
            m_BarCam = cg.AddComponent<Camera>();
            m_BarCam.clearFlags = CameraClearFlags.SolidColor; m_BarCam.cullingMask = 0; m_BarCam.depth = -1000;
            m_BarCam.useOcclusionCulling = false; m_BarCam.allowHDR = false; m_BarCam.allowMSAA = false;
        }

        void LateUpdate()
        {
            m_BarCam.backgroundColor = m_Bars;
            var sc = new Vector2Int(Screen.width, Screen.height);
            if (sc != m_LastScreen) { m_LastScreen = sc; Box = BoxFor(sc.x, sc.y, m_Aspect); }
            // every camera (new ones too) draws inside the box; a camera that already used part of the screen
            // (split screen, picture-in-picture) keeps that part, inside the box
            foreach (var c in Camera.allCameras)
            {
                if (c == m_BarCam || c.targetTexture != null) continue;
                if (c.cullingMask == 0 && c.name == "Aspect Lock bars") continue;
                // the camera's own rect: the first one seen, or a new one the game set since
                if (!m_Original.TryGetValue(c, out var orig) || (m_Set.TryGetValue(c, out var given) && c.rect != given)) { orig = c.rect; m_Original[c] = orig; }
                var b = Box;
                var r = new Rect(b.x + orig.x * b.width, b.y + orig.y * b.height, orig.width * b.width, orig.height * b.height);
                if (c.rect != r) c.rect = r;
                m_Set[c] = c.rect;
            }
        }

        void Restore() { foreach (var kv in m_Original) if (kv.Key) kv.Key.rect = kv.Value; m_Original.Clear(); m_Set.Clear(); }

        void OnDestroy()
        {
            Restore();
            if (m_BarCam) Destroy(m_BarCam.gameObject);
            if (s_Instance == this) { s_Instance = null; Active = false; Box = new Rect(0, 0, 1, 1); }
        }
    }
}
