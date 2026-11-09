using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HienaDev.DevTools
{
    /// <summary>Picture helpers shared by the Itch Page Kit and the Web Size Tester: fitting a picture to an exact size,
    /// grabbing the Game view, recording frames, hiding the game's UI for a shot.</summary>
    static class Pics
    {
        /// <summary>The picture scaled to exactly w x h. fit = false: cut from the middle at w:h (fills, crops the
        /// sides); fit = true: whole picture inside, bars of the bar colour. Works on any texture (readable or not).</summary>
        public static Texture2D Fit(Texture src, int w, int h, bool fit = false, Color bars = default)
        {
            // keep the colours as they are: sRGB pictures go through sRGB render textures, raw ones (screen grabs) through linear
            bool srgb = src is Texture2D t2 && t2.isDataSRGB;
            var rw = srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            float sa = src.width / (float)src.height, ta = w / (float)h;
            Texture cur = src; RenderTexture curRT = null;
            int cw = src.width, ch = src.height;
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            if (!fit)
            {
                if (sa > ta) { scale.x = ta / sa; offset.x = (1f - scale.x) * 0.5f; } else { scale.y = sa / ta; offset.y = (1f - scale.y) * 0.5f; }
                cw = Mathf.Max(1, Mathf.RoundToInt(src.width * scale.x)); ch = Mathf.Max(1, Mathf.RoundToInt(src.height * scale.y));
                curRT = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGB32, rw);
                Graphics.Blit(src, curRT, scale, offset); cur = curRT;
            }
            // the size it ends up inside the frame
            int iw = w, ih = h;
            if (fit) { if (sa > ta) ih = Mathf.Max(1, Mathf.RoundToInt(w / sa)); else iw = Mathf.Max(1, Mathf.RoundToInt(h * sa)); }
            // halve step by step down towards the size (one big shrink shimmers)
            while (cur.width >= iw * 2 && cur.height >= ih * 2)
            {
                var half = RenderTexture.GetTemporary(cur.width / 2, cur.height / 2, 0, RenderTextureFormat.ARGB32, rw);
                Graphics.Blit(cur, half);
                if (curRT) RenderTexture.ReleaseTemporary(curRT);
                curRT = half; cur = half;
            }
            var fin = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, rw);
            if (!fit) Graphics.Blit(cur, fin);
            else
            {
                var prevA = RenderTexture.active; RenderTexture.active = fin;
                GL.Clear(true, true, srgb ? bars : bars.linear);
                GL.PushMatrix(); GL.LoadPixelMatrix(0, w, h, 0);
                Graphics.DrawTexture(new Rect((w - iw) * 0.5f, (h - ih) * 0.5f, iw, ih), cur);
                GL.PopMatrix(); RenderTexture.active = prevA;
            }
            if (curRT) RenderTexture.ReleaseTemporary(curRT);
            var prev = RenderTexture.active; RenderTexture.active = fin;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(fin);
            return tex;
        }

        /// <summary>A texture from frame pixels (bottom-up), treated as raw colours.</summary>
        public static Texture2D FromPixels(Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.SetPixels32(px); t.Apply(false);
            return t;
        }

        // ---- the game's UI, hidden for a shot (screen-space Canvases and UI Toolkit documents; world-space canvases stay)
        public class HiddenUI { public List<(VisualElement e, StyleEnum<DisplayStyle> was)> docs = new List<(VisualElement, StyleEnum<DisplayStyle>)>(); public List<Behaviour> canvases = new List<Behaviour>(); }
        public static HiddenUI HideUI()
        {
            var h = new HiddenUI();
            foreach (var d in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (d.isActiveAndEnabled && d.rootVisualElement != null) { h.docs.Add((d.rootVisualElement, d.rootVisualElement.style.display)); d.rootVisualElement.style.display = DisplayStyle.None; }
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.enabled && c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; h.canvases.Add(c); }
            return h;
        }
        public static void ShowUI(HiddenUI h)
        {
            if (h == null) return;
            foreach (var (e, was) in h.docs) if (e != null) e.style.display = was;
            foreach (var c in h.canvases) if (c) c.enabled = true;
        }
    }

    /// <summary>A hidden helper in the running game: grabs the Game view at the end of a frame (that's when it's drawn).</summary>
    class CaptureRunner : MonoBehaviour
    {
        public static bool Busy; public static string Status = "";
        static CaptureRunner s_Inst;
        public static CaptureRunner Inst
        {
            get
            {
                if (!s_Inst) { var go = new GameObject("Dev Tools capture") { hideFlags = HideFlags.HideAndDontSave }; s_Inst = go.AddComponent<CaptureRunner>(); }
                return s_Inst;
            }
        }
        void OnDestroy() { Busy = false; } // (Play mode stopped half-way)

        /// <summary>The Game view now, as w x h (cut from the middle at that shape).</summary>
        public static Texture2D GrabNow(int w, int h)
        {
            int sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            int super = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(w / (float)sw, h / (float)sh)), 1, 4);
            var src = ScreenCapture.CaptureScreenshotAsTexture(super);
            if (src == null) return null;
            var t = Pics.Fit(src, w, h);
            Destroy(src);
            return t;
        }

        public void Picture(int w, int h, bool hideUI, Action<Texture2D> done) { Busy = true; StartCoroutine(DoPicture(w, h, hideUI, done)); }
        IEnumerator DoPicture(int w, int h, bool hideUI, Action<Texture2D> done)
        {
            var ui = hideUI ? Pics.HideUI() : null;
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D t = null;
            try { t = GrabNow(w, h); } finally { Pics.ShowUI(ui); Busy = false; }
            done?.Invoke(t);
        }

        /// <summary>Records seconds of the Game view at fps, w x h; frames bottom-up.</summary>
        public void Record(int w, int h, float seconds, int fps, bool hideUI, Action<List<Color32[]>> done)
        { Busy = true; StartCoroutine(DoRecord(w, h, seconds, fps, hideUI, done)); }
        IEnumerator DoRecord(int w, int h, float seconds, int fps, bool hideUI, Action<List<Color32[]>> done)
        {
            var ui = hideUI ? Pics.HideUI() : null;
            var frames = new List<Color32[]>();
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * fps));
            float step = 1f / fps, next = Time.realtimeSinceStartup;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    while (Time.realtimeSinceStartup < next) yield return null;
                    next += step;
                    yield return new WaitForEndOfFrame();
                    var t = GrabNow(w, h);
                    if (t == null) break;
                    frames.Add(t.GetPixels32()); Destroy(t);
                    Status = "Recording... " + (i + 1) + " / " + count;
                }
            }
            finally { Pics.ShowUI(ui); Busy = false; }
            done?.Invoke(frames);
        }

        /// <summary>Waits a few frames (after a Game view size change), then hands back the Game view (full shape).</summary>
        public void AfterFrames(int frames, Action<CaptureRunner> then) => StartCoroutine(DoAfter(frames, then));
        IEnumerator DoAfter(int frames, Action<CaptureRunner> then)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            then?.Invoke(this);
        }
    }
}
