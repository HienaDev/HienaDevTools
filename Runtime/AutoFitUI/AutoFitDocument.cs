using UnityEngine;
using UnityEngine.UIElements;

namespace HienaDev.DevTools
{
    /// <summary>
    /// UI Toolkit: keeps a UIDocument fitting every screen: its PanelSettings scale with the screen in "Expand" mode, and
    /// the root keeps out of phone notches (safe area padding). Add it next to a UIDocument (or Tools > Auto Fit UI).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(UIDocument))]
    [AddComponentMenu("Dev Tools/Auto Fit UI/Auto Fit Document (UI Toolkit)")]
    public class AutoFitDocument : MonoBehaviour
    {
        [Tooltip("The screen size the UI was designed at")] public Vector2Int designResolution = new Vector2Int(1920, 1080);
        [Tooltip("Keep the UI out of phone notches and rounded corners")] public bool safeArea = true;
        Rect m_Last; Vector2Int m_LastScreen;

        void OnEnable() { Apply(); }

        public void Apply()
        {
            var doc = GetComponent<UIDocument>();
            if (!doc || !doc.panelSettings) return;
            var ps = doc.panelSettings;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = designResolution;
            ps.screenMatchMode = PanelScreenMatchMode.Expand;
            m_LastScreen = default;
        }

        void LateUpdate()
        {
            if (!safeArea && !AspectLock.Active) return;
            var doc = GetComponent<UIDocument>(); var root = doc ? doc.rootVisualElement : null;
            if (root == null || root.panel == null) return;
            var sa = Screen.safeArea; var sc = new Vector2Int(Screen.width, Screen.height);
            if (AspectLock.Active)
            {
                // locked to 16:9: the UI keeps inside the box as well
                var bx = AspectLock.Box; var box = new Rect(bx.x * sc.x, bx.y * sc.y, bx.width * sc.x, bx.height * sc.y);
                sa = Rect.MinMaxRect(Mathf.Max(sa.xMin, box.xMin), Mathf.Max(sa.yMin, box.yMin), Mathf.Min(sa.xMax, box.xMax), Mathf.Min(sa.yMax, box.yMax));
            }
            if (sa == m_Last && sc == m_LastScreen) return;
            m_Last = sa; m_LastScreen = sc;
            // the notch's size in the panel's units
            var tl = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sa.xMin, Screen.height - sa.yMax));
            var br = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sa.xMax, Screen.height - sa.yMin));
            var full = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(Screen.width, Screen.height));
            root.style.paddingLeft = Mathf.Max(0, tl.x);
            root.style.paddingTop = Mathf.Max(0, tl.y);
            root.style.paddingRight = Mathf.Max(0, full.x - br.x);
            root.style.paddingBottom = Mathf.Max(0, full.y - br.y);
        }
    }
}
