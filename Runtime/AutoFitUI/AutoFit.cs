#if HIENADEV_UGUI
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Keeps a Canvas fitting every screen: scales with the screen in "Expand" mode (the whole design always fits, never
    /// cut off; on a different shape there's extra room at the sides or top and bottom, which the elements pinned to the
    /// edges use). Added by Tools > Auto Fit UI (one click), with SafeArea and ShrinkToFit where needed.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    [AddComponentMenu("Dev Tools/Auto Fit UI/Auto Fit Canvas")]
    public class AutoFitCanvas : MonoBehaviour
    {
        [Tooltip("The screen size the UI was designed at (the layout looks exactly like the design at this size)")]
        public Vector2 designResolution = new Vector2(1920, 1080);

        [Tooltip("In the game: a Canvas that hasn't been set up yet (no Safe Area) re-pins its elements by itself when it starts")]
        public bool fitWhenTheGameStarts = true;

        void OnEnable()
        {
            if (Application.isPlaying && fitWhenTheGameStarts && !AutoFitCore.IsFitted(GetComponent<Canvas>()))
            {
                // (a fixed-pixel canvas's design size isn't known in a build: the design resolution above stands in)
                var sc = GetComponent<CanvasScaler>();
                Vector2? design = sc && sc.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize ? (Vector2?)null : designResolution;
                AutoFitCore.Fit(GetComponent<Canvas>(), null, design);
            }
            Apply();
        }
        void OnValidate() => Apply();

        // in the game: UI that a script adds straight to this Canvas later (a popup, a toast, a spawned panel) goes into
        // the Safe Area too, so it keeps out of notches and inside a locked 16:9 box
        void OnTransformChildrenChanged()
        {
            if (!Application.isPlaying || AutoFitCore.Busy) return;
            var safe = transform.Cast<Transform>().FirstOrDefault(t => t.GetComponent<SafeArea>());
            if (!safe) return;
            var move = transform.Cast<Transform>().Where(t => t != safe && !t.GetComponent<AutoFitSkip>() && t.name != "Blocker").ToList();
            foreach (var k in move) k.SetParent(safe, false); // (keeps its anchors: what was pinned to the screen's corner now sits in the safe corner)
        }

        public void Apply()
        {
            var canvas = GetComponent<Canvas>();
            if (!canvas || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) return;
            var s = GetComponent<CanvasScaler>(); if (!s) s = gameObject.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(Mathf.Max(1, designResolution.x), Mathf.Max(1, designResolution.y));
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }
    }

    /// <summary>
    /// Keeps its children inside the part of the screen that's really visible (phone notches, rounded corners, camera
    /// holes): it shrinks itself to Screen.safeArea. Usually the one full-screen child of a fitted Canvas.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Dev Tools/Auto Fit UI/Safe Area")]
    public class SafeArea : MonoBehaviour
    {
        /// <summary>For previews: a pretend safe area (0..1 of the screen); null = the real one.</summary>
        public static Rect? Simulated;
        Rect m_Last; Vector2Int m_LastScreen;

        void OnEnable() { m_LastScreen = default; Apply(); }
        void LateUpdate() { var sa = Current(); var sc = new Vector2Int(Screen.width, Screen.height); if (sa != m_Last || sc != m_LastScreen) Apply(); }

        static Rect Current()
        {
            if (Simulated.HasValue) return Simulated.Value;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            var s = Screen.safeArea;
            var safe = new Rect(s.x / w, s.y / h, s.width / w, s.height / h);
            if (!AspectLock.Active) return safe;
            // locked to 16:9 (or another shape): inside that box too
            var b = AspectLock.Box;
            float x0 = Mathf.Max(safe.xMin, b.xMin), y0 = Mathf.Max(safe.yMin, b.yMin), x1 = Mathf.Min(safe.xMax, b.xMax), y1 = Mathf.Min(safe.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : b;
        }

        public void Apply()
        {
            var rt = (RectTransform)transform;
            var r = Current(); m_Last = r; m_LastScreen = new Vector2Int(Screen.width, Screen.height);
            if (r.width <= 0 || r.height <= 0) r = new Rect(0, 0, 1, 1);
            rt.anchorMin = r.min; rt.anchorMax = r.max;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// A big panel (a menu, a dialog) that scales itself down when the screen hasn't got room for it, instead of spilling
    /// off the edges or covering other UI. Never grows past its designed size; stays inside its parent.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Dev Tools/Auto Fit UI/Shrink To Fit")]
    public class ShrinkToFit : MonoBehaviour
    {
        [Tooltip("Space kept free around it, in canvas units")] public float margin = 16f;
        [Tooltip("It never gets smaller than this (so text stays readable)")] [Range(0.2f, 1f)] public float smallest = 0.4f;
        [Tooltip("On tall screens (phones held upright) a landscape design comes out small: let it grow up to this much to use the width")] [Range(1f, 3f)] public float growOnTallScreens = 1.8f;
        Vector2 m_LastParent; [SerializeField, HideInInspector] Vector2 m_Shift; // (the nudge it added last time, so the designed position is always known)

        void OnEnable() { m_LastParent = default; Apply(); }
        void LateUpdate() { var p = transform.parent as RectTransform; if (p && p.rect.size != m_LastParent) Apply(); }

        public void Apply()
        {
            var rt = (RectTransform)transform; var p = rt.parent as RectTransform; if (!p) return;
            m_LastParent = p.rect.size;
            var design = rt.anchoredPosition - m_Shift;
            var size = rt.rect.size; if (size.x <= 0 || size.y <= 0) return;
            var room = p.rect.size - Vector2.one * margin * 2;
            float fits = Mathf.Min(room.x / size.x, room.y / size.y);
            float most = p.rect.height > p.rect.width ? growOnTallScreens : 1f; // (upright screens: use the spare width)
            float s = Mathf.Clamp(Mathf.Min(most, fits * (most > 1f ? 0.92f : 1f)), smallest, Mathf.Max(1f, most));
            rt.localScale = new Vector3(s, s, 1);
            // and back inside the parent if the scaled panel still pokes out
            rt.anchoredPosition = design;
            var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            Vector2 min = p.InverseTransformPoint(corners[0]), max = p.InverseTransformPoint(corners[2]);
            var pr = p.rect; Vector2 shift = Vector2.zero;
            if (max.x - min.x <= pr.width) { if (min.x < pr.xMin + margin) shift.x = pr.xMin + margin - min.x; else if (max.x > pr.xMax - margin) shift.x = pr.xMax - margin - max.x; }
            if (max.y - min.y <= pr.height) { if (min.y < pr.yMin + margin) shift.y = pr.yMin + margin - min.y; else if (max.y > pr.yMax - margin) shift.y = pr.yMax - margin - max.y; }
            rt.anchoredPosition = design + shift; m_Shift = shift;
        }

        void OnDisable() { var rt = (RectTransform)transform; rt.localScale = Vector3.one; rt.anchoredPosition -= m_Shift; m_Shift = Vector2.zero; }
    }
}
#endif
