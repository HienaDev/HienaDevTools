#if HIENADEV_UGUI
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HienaDev.DevTools
{
    /// <summary>
    /// The fitting itself, used in the editor (one click, with Undo) and in builds (a Canvas fits itself when the game
    /// starts, on whatever screen it opens on). It keeps how the UI looks at its design size, then:
    /// 1. scales it with the screen in Expand mode (never cut off);
    /// 2. puts everything inside a Safe Area (phone notches);
    /// 3. pins each element to the edge or corner it sits at (and stretches bars that span the screen), so on other
    ///    shapes things follow the edges instead of drifting into the middle or falling off;
    /// 4. lets big panels shrink when a screen has no room for them.
    /// </summary>
    public static class AutoFitCore
    {
        public class Result { public int pinned, stretched, shrinkers; public bool safeArea; public GameObject createdSafeArea; public override string ToString() => pinned + " pinned to their edges, " + stretched + " stretched, " + shrinkers + " set to shrink when needed"; }

        /// <summary>How changes are made (the editor passes Undo-recording versions).</summary>
        public class Ops
        {
            public Action<UnityEngine.Object> record = _ => { };
            public Func<GameObject, Type, Component> add = (g, t) => g.AddComponent(t);
            public Action<Transform, Transform> reparent = (t, p) => t.SetParent(p, true);
            public Action<GameObject> created = _ => { };
        }

        public static bool IsFitted(Canvas c) => c && c.GetComponent<AutoFitCanvas>() && c.transform.Cast<Transform>().Any(t => t.GetComponent<SafeArea>());

        /// <summary>(true while Fit is moving things around)</summary>
        public static bool Busy { get; private set; }

        public static Result Fit(Canvas canvas, Ops ops = null, Vector2? designOverride = null)
        {
            Busy = true;
            try { return FitNow(canvas, ops, designOverride); }
            finally { Busy = false; }
        }

        static Result FitNow(Canvas canvas, Ops ops, Vector2? designOverride)
        {
            ops = ops ?? new Ops();
            var r = new Result();
            if (!canvas || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace) return r;
            var root = (RectTransform)canvas.transform;

            // 1. scaling. Designed at: the scaler's reference size, else the size the canvas has now
            var scaler = canvas.GetComponent<CanvasScaler>();
            Vector2 design = designOverride ?? root.rect.size;
            if (!designOverride.HasValue && scaler && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) design = scaler.referenceResolution;
            if (design.x < 10 || design.y < 10) design = new Vector2(1920, 1080);
            var fit = canvas.GetComponent<AutoFitCanvas>();
            if (!fit) fit = (AutoFitCanvas)ops.add(canvas.gameObject, typeof(AutoFitCanvas));
            if (scaler) ops.record(scaler);
            ops.record(fit);
            fit.designResolution = design;

            // 2. the safe area, holding all the canvas's children
            var safeT = root.Cast<Transform>().FirstOrDefault(t => t.GetComponent<SafeArea>());
            SafeArea safe;
            if (!safeT)
            {
                var go = new GameObject("Safe Area", typeof(RectTransform));
                ops.created(go);
                var srt = (RectTransform)go.transform;
                srt.SetParent(root, false);
                srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = srt.offsetMax = Vector2.zero; srt.pivot = new Vector2(0.5f, 0.5f);
                srt.localScale = Vector3.one;
                var kids = root.Cast<Transform>().Where(t => t != srt).ToList();
                foreach (var k in kids) ops.reparent(k, srt);
                srt.SetAsFirstSibling();
                safe = go.AddComponent<SafeArea>();
                r.safeArea = true; r.createdSafeArea = go;
            }
            else safe = safeT.GetComponent<SafeArea>();

            // 3. + 4. read where everything sits (relative to the middle, in design units), then re-pin it
            Pin((RectTransform)safe.transform, design, r, ops, 0);
            fit.Apply();
            return r;
        }

        static void Pin(RectTransform parent, Vector2 parentDesign, Result r, Ops ops, int depth)
        {
            if (depth > 6 || parent.GetComponent<LayoutGroup>()) return; // (a layout group places its own children)
            if (parentDesign.x < 1 || parentDesign.y < 1) return;
            var centre = parent.rect.center;
            foreach (Transform t in parent)
            {
                var rt = t as RectTransform; if (!rt) continue;
                if (rt.GetComponent<SafeArea>() || Mathf.Abs(rt.localEulerAngles.z) > 0.1f) continue;
                // where it sits now, around the parent's middle, as a share of the design size
                var c = new Vector3[4]; rt.GetWorldCorners(c);
                Vector2 min = (Vector2)parent.InverseTransformPoint(c[0]) - centre, max = (Vector2)parent.InverseTransformPoint(c[2]) - centre;
                // (an element already stretched with the screen: measure it at the design size)
                if (Mathf.Approximately(rt.anchorMin.x, 0) && Mathf.Approximately(rt.anchorMax.x, 1)) { min.x = -parentDesign.x / 2 + rt.offsetMin.x; max.x = parentDesign.x / 2 + rt.offsetMax.x; }
                if (Mathf.Approximately(rt.anchorMin.y, 0) && Mathf.Approximately(rt.anchorMax.y, 1)) { min.y = -parentDesign.y / 2 + rt.offsetMin.y; max.y = parentDesign.y / 2 + rt.offsetMax.y; }
                float x0 = min.x / parentDesign.x + 0.5f, x1 = max.x / parentDesign.x + 0.5f, y0 = min.y / parentDesign.y + 0.5f, y1 = max.y / parentDesign.y + 0.5f;
                float fw = x1 - x0, fh = y1 - y0, cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;

                Vector2 aMin, aMax;
                if (fw > 0.92f && fh > 0.92f)
                {
                    // a full-screen container (a background, a "HUD" group): stretched, and its children done too
                    Set(rt, new Vector2(0, 0), new Vector2(1, 1), min, max, parentDesign, ops);
                    r.stretched++;
                    Pin(rt, new Vector2((x1 - x0) * parentDesign.x, (y1 - y0) * parentDesign.y), r, ops, depth + 1);
                    continue;
                }
                if (fw > 0.8f) { aMin.x = 0; aMax.x = 1; r.stretched++; } else aMin.x = aMax.x = Side(cx, fw);
                if (fh > 0.8f) { aMin.y = 0; aMax.y = 1; r.stretched++; } else aMin.y = aMax.y = Side(cy, fh);
                Set(rt, aMin, aMax, min, max, parentDesign, ops);
                r.pinned++;
                if ((fw > 0.5f || fh > 0.6f) && fw <= 0.8f && fh <= 0.8f && !rt.GetComponent<ShrinkToFit>()) { ops.add(rt.gameObject, typeof(ShrinkToFit)); r.shrinkers++; }
            }
        }

        // which edge it belongs to: near the start (0), the end (1), or the middle (0.5)
        static float Side(float centre, float size)
        {
            if (size > 0.5f) return 0.5f;
            // (a row of things around the middle, like ability buttons, stays together in the middle)
            if (centre < 0.3f) return 0f;
            if (centre > 0.7f) return 1f;
            return 0.5f;
        }

        // new anchors; at the design size it sits exactly where it was
        static void Set(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 min, Vector2 max, Vector2 design, Ops ops)
        {
            ops.record(rt);
            var s = rt.localScale;
            Vector2 bmin = min + design / 2, bmax = max + design / 2; // (from the parent's bottom-left at the design size)
            Vector2 piv = rt.pivot, size = new Vector2((bmax.x - bmin.x) / Mathf.Max(1e-4f, s.x), (bmax.y - bmin.y) / Mathf.Max(1e-4f, s.y));
            Vector2 pivPos = bmin + new Vector2((bmax.x - bmin.x) * piv.x, (bmax.y - bmin.y) * piv.y);
            Vector2 aMinPos = Vector2.Scale(aMin, design), aMaxPos = Vector2.Scale(aMax, design);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.sizeDelta = size - (aMaxPos - aMinPos);
            rt.anchoredPosition = pivPos - aMinPos - Vector2.Scale(aMaxPos - aMinPos, piv);
        }

        // ---- "fit every Canvas automatically": no setup, every screen-space Canvas in every scene fits itself
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var s = AutoFitSettings.Get;
            if (s == null) return;
            if (s.lockAspect && s.aspect.x > 0 && s.aspect.y > 0) AspectLock.Begin(s.aspect.x / s.aspect.y, s.barColour);
            // (locked: every Canvas needs its Safe Area to keep inside the box, so they're all fitted)
            if (!s.fitEveryCanvas && !s.lockAspect) return;
            StartWatcher();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook() { AspectLock.s_EnsureUIFits = StartWatcher; }

        /// <summary>Every screen-space Canvas (now and later) gets an AutoFitCanvas.</summary>
        public static void StartWatcher()
        {
            if (!Application.isPlaying || AutoFitWatcher.Running) return;
            var go = new GameObject("Auto Fit UI") { hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable }; // (gone when Play mode ends)
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AutoFitWatcher>();
        }
    }

    /// <summary>Gives every screen-space Canvas that appears (scenes, popups made later) an AutoFitCanvas.</summary>
    class AutoFitWatcher : MonoBehaviour
    {
        static AutoFitWatcher s_On;
        public static bool Running => s_On;
        void Awake() { s_On = this; }
        void OnDestroy() { if (s_On == this) s_On = null; }
        void LateUpdate()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace && !c.GetComponent<AutoFitCanvas>() && !c.GetComponent<AutoFitSkip>())
                    c.gameObject.AddComponent<AutoFitCanvas>();
        }
    }

}
#endif
