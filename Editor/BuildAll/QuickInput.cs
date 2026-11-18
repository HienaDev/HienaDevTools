using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A small window to fill in one missing thing (a name, a version, the icon, the build's scenes), opened from
    /// the pre-build check's ✎ buttons.</summary>
    public class QuickInput : EditorWindow
    {
        enum Mode { Text, Icon, Scenes }
        Mode m_Mode; string m_Explain = "", m_Text = ""; Action<string> m_OkText; Texture2D m_Icon;
        List<(string path, bool on)> m_Scenes; Vector2 m_Scroll; bool m_Focus;

        static QuickInput Make(string title, string explain, Mode mode, float h)
        {
            var w = CreateInstance<QuickInput>();
            w.titleContent = new GUIContent(title); w.m_Explain = explain; w.m_Mode = mode;
            w.minSize = w.maxSize = new Vector2(340, h);
            return w;
        }

        public static void Text(string title, string explain, string value, Action<string> ok)
        {
            var w = Make(title, explain, Mode.Text, 120); w.m_Text = value ?? ""; w.m_OkText = ok; w.m_Focus = true;
            w.ShowUtility();
        }

        public static void Icon()
        {
            var w = Make("Game icon", "A square picture, 256x256 or bigger. It's the .exe's icon, the taskbar's and the window's.", Mode.Icon, 190);
            var icons = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
            w.m_Icon = icons != null && icons.Length > 0 ? icons[0] : null;
            w.ShowUtility();
        }

        public static void Scenes()
        {
            var w = Make("Scenes in the build", "Tick the scenes the game has. The first one is where the game starts (drag order: File > Build Profiles).", Mode.Scenes, 300);
            var inBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            w.m_Scenes = AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(p => (p, inBuild.Contains(p))).ToList();
            w.ShowUtility();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(m_Explain, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4);
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (m_Mode == Mode.Text)
            {
                GUI.SetNextControlName("QuickInputText");
                m_Text = EditorGUILayout.TextField(m_Text, GUILayout.Height(22));
                if (m_Focus) { EditorGUI.FocusTextInControl("QuickInputText"); m_Focus = false; }
                GUILayout.FlexibleSpace();
                if (Buttons() || (enter && !string.IsNullOrWhiteSpace(m_Text))) { m_OkText?.Invoke(m_Text.Trim()); Close(); GUIUtility.ExitGUI(); }
            }
            else if (m_Mode == Mode.Icon)
            {
                m_Icon = (Texture2D)EditorGUILayout.ObjectField(m_Icon, typeof(Texture2D), false, GUILayout.Width(96), GUILayout.Height(96));
                GUILayout.FlexibleSpace();
                if (Buttons()) { PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, m_Icon ? new[] { m_Icon } : new Texture2D[0], IconKind.Any); Close(); GUIUtility.ExitGUI(); }
            }
            else
            {
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
                for (int i = 0; i < m_Scenes.Count; i++)
                {
                    var (path, on) = m_Scenes[i];
                    bool now = EditorGUILayout.ToggleLeft(new GUIContent(Path.GetFileNameWithoutExtension(path), path), on);
                    if (now != on) m_Scenes[i] = (path, now);
                }
                if (m_Scenes.Count == 0) EditorGUILayout.LabelField("No scenes in Assets yet.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndScrollView();
                if (Buttons())
                {
                    // keeps the build's order for the ones already in it, then adds the new ones
                    var list = EditorBuildSettings.scenes.Where(s => m_Scenes.Any(x => x.path == s.path && x.on)).Select(s => new EditorBuildSettingsScene(s.path, true)).ToList();
                    foreach (var (path, on) in m_Scenes) if (on && !list.Any(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
                    EditorBuildSettings.scenes = list.ToArray();
                    Close(); GUIUtility.ExitGUI();
                }
            }
        }

        bool Buttons()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(70))) { Close(); GUIUtility.ExitGUI(); }
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
            bool ok = GUILayout.Button("OK", GUILayout.Width(70));
            GUI.backgroundColor = ob;
            EditorGUILayout.EndHorizontal();
            return ok;
        }
    }
}
