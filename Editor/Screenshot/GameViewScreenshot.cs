using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Game view screenshots: Ctrl+Shift+K (or Tools > Screenshot > Take Screenshot), saved as PNG in the project's
    /// Screenshots folder (next to Assets). Tools > Screenshot > Toolbar opens a tiny window with a capture button and the
    /// size (1x, 2x, 4x the Game view: sharper pictures for store pages).
    /// </summary>
    public class GameViewScreenshot : EditorWindow
    {
        const string ScaleKey = "HienaDev.Screenshot.Scale";
        static int Scale { get => EditorPrefs.GetInt(ScaleKey, 1); set => EditorPrefs.SetInt(ScaleKey, value); }
        public static string Folder => Path.Combine(Directory.GetCurrentDirectory(), "Screenshots");

        [MenuItem("Tools/Screenshot/Toolbar", false, 120)]
        static void ShowWindow()
        {
            var w = GetWindow<GameViewScreenshot>("Screenshot");
            w.minSize = new Vector2(150, 24); w.maxSize = new Vector2(400, 24);
        }

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var c = EditorGUIUtility.IconContent("ReflectionProbe.Capture"); c.tooltip = "Take a Game view screenshot (Ctrl+Shift+K)";
            if (GUILayout.Button(c, EditorStyles.toolbarButton, GUILayout.Width(32))) TakeScreenshot();
            Scale = EditorGUILayout.IntPopup(Scale, new[] { "1x", "2x", "4x" }, new[] { 1, 2, 4 }, EditorStyles.toolbarPopup, GUILayout.Width(44));
            if (GUILayout.Button(new GUIContent("Folder", "Open the Screenshots folder"), EditorStyles.toolbarButton)) { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder + Path.DirectorySeparatorChar); }
            EditorGUILayout.EndHorizontal();
        }

        [MenuItem("Tools/Screenshot/Take Screenshot %#k", false, 121)]
        public static void TakeScreenshot()
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "Screenshot_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + (Scale > 1 ? "_" + Scale + "x" : "") + ".png");
            ScreenCapture.CaptureScreenshot(path, Scale);
            // (the picture is written at the end of the Game view's next frame: make sure there is one)
            EditorApplication.QueuePlayerLoopUpdate();
            var gv = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gv != null) foreach (var w in Resources.FindObjectsOfTypeAll(gv)) ((EditorWindow)w).Repaint();
            Debug.Log("[Screenshot] saved " + path);
        }
    }
}
