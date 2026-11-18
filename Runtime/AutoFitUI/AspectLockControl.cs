using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Change the locked shape while the game runs, with no code: put this on any object and hook a UI Button, Toggle or
    /// Dropdown to it (On Click / On Value Changed). Scripts can call AspectLock.Lock(16, 9) / AspectLock.Unlock() directly.
    /// </summary>
    [AddComponentMenu("Dev Tools/Auto Fit UI/Aspect Lock Control")]
    public class AspectLockControl : MonoBehaviour
    {
        [Tooltip("The shapes a Dropdown picks from, in the same order as its options. \"Off\" = the whole screen, no bars.")]
        public string[] shapes = { "16:9", "16:10", "4:3", "21:9", "Off" };
        [Tooltip("The player's choice is remembered for next time")] public bool remember = true;

        const string Key = "HienaDev.AspectLock";

        void Start()
        {
            if (remember && PlayerPrefs.HasKey(Key)) SetShape(PlayerPrefs.GetString(Key));
#if HIENADEV_UGUI
            // a Dropdown made by Tools > Auto Fit UI shows the shape that's on now
            var dd = GetComponentInChildren<UnityEngine.UI.Dropdown>(true);
            if (dd) { int i = IndexOfCurrent(); if (i >= 0) dd.SetValueWithoutNotify(i); }
#endif
        }

        /// <summary>Which entry of shapes is on right now (-1 if none).</summary>
        public int IndexOfCurrent()
        {
            float now = AspectLock.Aspect;
            for (int i = 0; i < shapes.Length; i++)
            {
                float a = Parse(shapes[i]);
                if ((now == 0 && a == 0) || (now > 0 && a > 0 && Mathf.Abs(a - now) < 0.01f)) return i;
            }
            return -1;
        }

        /// <summary>"16:9" -> 1.78, "Off" -> 0, unreadable -> -1.</summary>
        public static float Parse(string shape)
        {
            if (string.IsNullOrWhiteSpace(shape)) return -1;
            var s = shape.Trim().ToLowerInvariant();
            if (s == "off" || s == "none" || s == "free" || s == "unlocked") return 0;
            var p = s.Replace(" ", "").Split(':', 'x', '/', '*');
            if (p.Length != 2 || !float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) || w <= 0 || h <= 0) return -1;
            return w / h;
        }

        /// <summary>"16:9", "21:9", "2560x1080", "2560 x 1080" or "Off". For a Button's On Click.</summary>
        public void SetShape(string shape)
        {
            float a = Parse(shape);
            if (a < 0) { Debug.LogWarning("[Aspect Lock] Can't read the shape \"" + shape + "\". Use something like 16:9, 2560x1080 or Off."); return; }
            if (a == 0) AspectLock.Unlock(); else AspectLock.Lock(a);
            if (remember) { PlayerPrefs.SetString(Key, shape); PlayerPrefs.Save(); }
        }

        /// <summary>For a Dropdown's On Value Changed: picks shapes[index].</summary>
        public void SetShapeByIndex(int index) { if (index >= 0 && index < shapes.Length) SetShape(shapes[index]); }

        /// <summary>For a Toggle: on = the first shape in the list, off = the whole screen.</summary>
        public void SetLocked(bool locked) { SetShape(locked ? (shapes.Length > 0 ? shapes[0] : "16:9") : "Off"); }

        public void Lock16x9() => SetShape("16:9");
        public void Unlock() => SetShape("Off");
    }
}
