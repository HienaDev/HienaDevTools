#if HIENADEV_UGUI
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Outside Play mode no aspect lock or canvas watcher should exist: clears any left over (1.9.0/1.9.1 kept them
    /// after Play mode ended, which stacked black bars and drew the UI twice).</summary>
    [InitializeOnLoad]
    static class AutoFitCleanup
    {
        static AutoFitCleanup()
        {
            EditorApplication.delayCall += () => Clean();
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) Clean(); };
        }

        public static int Clean()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return 0;
            int n = 0;
            foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (!mb || EditorUtility.IsPersistent(mb)) continue;
                var t = mb.GetType();
                if (t != typeof(AspectLock) && t.Name != "AutoFitWatcher") continue;
                Object.DestroyImmediate(mb.gameObject); n++;
            }
            return n;
        }
    }
}
#endif
