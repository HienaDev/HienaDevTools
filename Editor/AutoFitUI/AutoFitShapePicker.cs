#if HIENADEV_UGUI
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace HienaDev.DevTools
{
    /// <summary>Builds a ready-wired "Screen shape" dropdown (an AspectLockControl) inside a Canvas, from the Auto Fit UI window.</summary>
    public static class AutoFitShapePicker
    {
        /// <summary>Where it goes: the selected object if it's inside that Canvas, else the Canvas's Safe Area (or the Canvas).</summary>
        public static RectTransform Parent(Canvas canvas)
        {
            if (!canvas) return null;
            var sel = Selection.activeTransform as RectTransform;
            if (sel && sel.GetComponentInParent<Canvas>() && sel.GetComponentInParent<Canvas>().rootCanvas == canvas) return sel;
            var safe = canvas.transform.Find("Safe Area") as RectTransform;
            return safe ? safe : (RectTransform)canvas.transform;
        }

        public static GameObject Add(RectTransform parent)
        {
            if (!parent) return null;
            var res = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            };
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // the row: "Screen shape" + the dropdown
            var row = new GameObject("Screen Shape Picker", typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420, 44); rt.anchoredPosition = Vector2.zero;
            row.layer = parent.gameObject.layer;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(rt, false);
            var lt = (RectTransform)label.transform; lt.anchorMin = new Vector2(0, 0); lt.anchorMax = new Vector2(0.42f, 1); lt.offsetMin = lt.offsetMax = Vector2.zero;
            var text = label.GetComponent<Text>(); text.text = "Screen shape"; text.font = font; text.fontSize = 22; text.color = Color.white; text.alignment = TextAnchor.MiddleLeft;

            var dd = DefaultControls.CreateDropdown(res);
            dd.name = "Shape Dropdown";
            dd.transform.SetParent(rt, false);
            var dt = (RectTransform)dd.transform; dt.anchorMin = new Vector2(0.44f, 0); dt.anchorMax = new Vector2(1, 1); dt.offsetMin = dt.offsetMax = Vector2.zero;
            foreach (var t in dd.GetComponentsInChildren<Text>(true)) { t.font = font; t.fontSize = 20; }
            var template = dd.transform.Find("Template") as RectTransform; if (template) template.sizeDelta = new Vector2(0, 220);
            var item = dd.transform.Find("Template/Viewport/Content/Item") as RectTransform; if (item) item.sizeDelta = new Vector2(item.sizeDelta.x, 34);
            var content = dd.transform.Find("Template/Viewport/Content") as RectTransform; if (content) content.sizeDelta = new Vector2(content.sizeDelta.x, 38);

            // the shapes: the project's own shape first (what the game starts with), then the usual ones, then Off
            var control = row.AddComponent<AspectLockControl>();
            var own = AutoFitFixer.Aspect;
            string ownName = own.x.ToString("0") + (own.x >= 100 ? "x" : ":") + own.y.ToString("0");
            var list = new[] { "16:9", "16:10", "4:3", "21:9" }.ToList();
            if (AutoFitFixer.LockAspect) { list.RemoveAll(s => Mathf.Abs(AspectLockControl.Parse(s) - own.x / own.y) < 0.01f); list.Insert(0, ownName); }
            list.Add("Off");
            control.shapes = list.ToArray();
            var drop = dd.GetComponent<Dropdown>();
            drop.options = list.Select(s => new Dropdown.OptionData(s == "Off" ? "Off (whole screen)" : s)).ToList();
            drop.value = AutoFitFixer.LockAspect ? 0 : list.Count - 1;
            UnityEventTools.AddPersistentListener(drop.onValueChanged, control.SetShapeByIndex);

            foreach (var g in row.GetComponentsInChildren<Transform>(true)) g.gameObject.layer = parent.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(row, "Add screen shape picker");
            Selection.activeGameObject = row; EditorGUIUtility.PingObject(row);
            return row;
        }
    }
}
#endif
