#if HIENADEV_UGUI
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The one-click fix in the editor: AutoFitCore with every change recorded for Ctrl+Z. Also the project
    /// switch "fit every Canvas automatically" (a Resources asset the build reads).</summary>
    public static class AutoFitFixer
    {
        public static bool IsFitted(Canvas c) => AutoFitCore.IsFitted(c);

        public static AutoFitCore.Result Fix(Canvas canvas, bool undo = true)
        {
            if (!undo) return AutoFitCore.Fit(canvas);
            Undo.SetCurrentGroupName("Make the UI fit every screen");
            int group = Undo.GetCurrentGroup();
            var ops = new AutoFitCore.Ops
            {
                record = o => Undo.RecordObject(o, "Auto Fit UI"),
                add = (g, t) => Undo.AddComponent(g, t),
                reparent = (t, p) => Undo.SetTransformParent(t, p, "Auto Fit UI"),
                created = g => Undo.RegisterCreatedObjectUndo(g, "Auto Fit UI"),
            };
            var r = AutoFitCore.Fit(canvas, ops);
            Undo.CollapseUndoOperations(group);
            return r;
        }

        // ---- the project switch
        const string AssetPath = "Assets/Resources/AutoFitUISettings.asset";
        public static bool FitEveryCanvas
        {
            get { var s = AssetDatabase.LoadAssetAtPath<AutoFitSettings>(AssetPath); return s && s.fitEveryCanvas; }
            set
            {
                var s = AssetDatabase.LoadAssetAtPath<AutoFitSettings>(AssetPath);
                if (!s)
                {
                    if (!value) return;
                    Directory.CreateDirectory("Assets/Resources");
                    s = ScriptableObject.CreateInstance<AutoFitSettings>();
                    AssetDatabase.CreateAsset(s, AssetPath);
                }
                s.fitEveryCanvas = value; EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); AutoFitSettings.Forget();
            }
        }

        static AutoFitSettings Asset(bool create)
        {
            var s = AssetDatabase.LoadAssetAtPath<AutoFitSettings>(AssetPath);
            if (!s && create)
            {
                Directory.CreateDirectory("Assets/Resources");
                s = ScriptableObject.CreateInstance<AutoFitSettings>(); s.fitEveryCanvas = false;
                AssetDatabase.CreateAsset(s, AssetPath);
            }
            return s;
        }

        /// <summary>The game always at one shape (16:9 by default), black bars around it.</summary>
        public static bool LockAspect
        {
            get { var s = Asset(false); return s && s.lockAspect; }
            set { var s = Asset(value); if (!s) return; s.lockAspect = value; EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); AutoFitSettings.Forget(); }
        }
        public static Vector2 Aspect
        {
            get { var s = Asset(false); return s ? s.aspect : new Vector2(16, 9); }
            set { var s = Asset(true); s.aspect = value; EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); AutoFitSettings.Forget(); }
        }

        [MenuItem("Tools/Auto Fit UI/Fit Every Canvas Automatically", false, 159)]
        static void Toggle() { FitEveryCanvas = !FitEveryCanvas; }
        [MenuItem("Tools/Auto Fit UI/Fit Every Canvas Automatically", true)]
        static bool ToggleCheck() { Menu.SetChecked("Tools/Auto Fit UI/Fit Every Canvas Automatically", FitEveryCanvas); return true; }
    }
}
#endif
