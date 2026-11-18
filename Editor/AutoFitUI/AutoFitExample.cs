#if HIENADEV_UGUI
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Tools > Auto Fit UI > Example Scene: a new (unsaved) scene with a game HUD and a pause menu laid out the usual
    /// broken way: everything pinned to the middle, designed at 1920x1080, scaling by width only. On other screen shapes
    /// it falls off the edges or floats away from them. Then try "Make it fit every screen" on it.
    /// </summary>
    public static class AutoFitExample
    {
        [MenuItem("Tools/Auto Fit UI/Example Scene", false, 158)]
        public static void Make()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Auto Fit UI", "Stop Play mode first.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Build(scene);
            EditorWindow.GetWindow<AutoFitWindow>(false, "Auto Fit UI", true);
            Selection.activeGameObject = GameObject.Find("HUD (example)");
        }

        /// <summary>Puts the example HUD and menu into a scene (also used by tests).</summary>
        public static void Build(Scene scene)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var hud = Canvas("HUD (example)", scene, 0);
            // everything pinned to the middle, placed for 1920x1080: the classic mistake
            Bar(hud, "Top bar", new Vector2(0, 510), new Vector2(1920, 60), new Color(0.1f, 0.12f, 0.2f, 0.85f), "LEVEL 3 - THE CORAL CAVES", font, 28);
            var hp = Box(hud, "Health", new Vector2(-760, 420), new Vector2(360, 44), new Color(0.2f, 0.2f, 0.2f, 0.9f));
            var fill = Box(hp, "Fill", new Vector2(-40, 0), new Vector2(270, 32), new Color(0.35f, 0.85f, 0.4f)); fill.anchorMin = fill.anchorMax = new Vector2(0.5f, 0.5f);
            Label(hud, "Score", new Vector2(760, 420), new Vector2(360, 50), "SCORE 12,340", font, 34, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f));
            Box(hud, "Pause button", new Vector2(900, 470), new Vector2(70, 70), new Color(0.95f, 0.45f, 0.4f));
            var map = Box(hud, "Minimap", new Vector2(790, -370), new Vector2(280, 280), new Color(0.15f, 0.35f, 0.45f, 0.9f));
            Label(map, "Map", Vector2.zero, new Vector2(200, 40), "MAP", font, 26, TextAnchor.MiddleCenter, Color.white);
            for (int i = 0; i < 5; i++) Box(hud, "Ability " + (i + 1), new Vector2(-240 + i * 120, -470), new Vector2(100, 100), Color.HSVToRGB(i / 5f, 0.55f, 0.95f));
            Bar(hud, "Dialogue", new Vector2(-560, -330), new Vector2(700, 150), new Color(0.05f, 0.05f, 0.08f, 0.85f), "Captain Koi: Find the three pearls before the tide turns!", font, 26);

            var menu = Canvas("Pause Menu (example)", scene, 10);
            var panel = Box(menu, "Menu panel", Vector2.zero, new Vector2(900, 760), new Color(0.12f, 0.16f, 0.26f, 0.95f));
            Label(panel, "Title", new Vector2(0, 290), new Vector2(800, 90), "PAUSED", font, 72, TextAnchor.MiddleCenter, Color.white);
            string[] buttons = { "Resume", "Settings", "Quit to menu" };
            for (int i = 0; i < 3; i++)
            {
                var b = Box(panel, buttons[i], new Vector2(0, 130 - i * 150), new Vector2(600, 110), new Color(0.3f, 0.55f, 0.9f));
                Label(b, "Text", Vector2.zero, new Vector2(560, 100), buttons[i], font, 44, TextAnchor.MiddleCenter, Color.white);
            }
            menu.gameObject.SetActive(true);
        }

        static Canvas Canvas(string name, Scene scene, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(go, scene);
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; s.matchWidthOrHeight = 0f; // (width only: the usual default)
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        static RectTransform Box(Component parent, string name, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.GetComponent<Image>().color = c;
            return rt;
        }

        static void Bar(Component parent, string name, Vector2 pos, Vector2 size, Color c, string text, Font font, int fs)
        {
            var rt = Box(parent, name, pos, size, c);
            Label(rt, "Text", Vector2.zero, size - new Vector2(40, 10), text, font, fs, TextAnchor.MiddleCenter, Color.white);
        }

        static void Label(Component parent, string name, Vector2 pos, Vector2 size, string text, Font font, int fs, TextAnchor align, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform; rt.SetParent(parent.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = fs; t.text = text; t.alignment = align; t.color = c; t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }
}
#endif
