using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A picture or animated GIF at full size (scaled to the window, or 1:1 with scrolling).</summary>
    public class ImageViewer : EditorWindow
    {
        string m_Path;
        readonly List<Texture2D> m_Frames = new List<Texture2D>();
        readonly List<int> m_Delays = new List<int>();
        int m_Frame; double m_NextAt; bool m_Actual; Vector2 m_Scroll; string m_Error;

        public static void Show(string path)
        {
            var w = GetWindow<ImageViewer>(false, "Picture", true);
            w.Load(path);
            w.minSize = new Vector2(320, 240);
            w.Show(); w.Focus();
        }

        void Load(string path)
        {
            Clear(); m_Path = path; m_Error = null;
            titleContent = new GUIContent(Path.GetFileName(path));
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                {
                    var g = GifCodec.Read(bytes);
                    for (int i = 0; i < g.frames.Count; i++) { var t = Pics.FromPixels(g.frames[i], g.width, g.height); t.hideFlags = HideFlags.HideAndDontSave; m_Frames.Add(t); m_Delays.Add(g.delaysCs[i]); }
                }
                else
                {
                    var t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    t.LoadImage(bytes); m_Frames.Add(t); m_Delays.Add(0);
                }
            }
            catch (Exception e) { m_Error = e.Message; }
            m_Frame = 0; m_NextAt = EditorApplication.timeSinceStartup;
        }

        void Clear() { foreach (var t in m_Frames) if (t) DestroyImmediate(t); m_Frames.Clear(); m_Delays.Clear(); }
        void OnDestroy() => Clear();
        void OnEnable() { EditorApplication.update += Tick; if (m_Frames.Count == 0 && !string.IsNullOrEmpty(m_Path) && File.Exists(m_Path)) Load(m_Path); }
        void OnDisable() => EditorApplication.update -= Tick;

        void Tick()
        {
            if (m_Frames.Count < 2 || EditorApplication.timeSinceStartup < m_NextAt) return;
            m_Frame = (m_Frame + 1) % m_Frames.Count;
            m_NextAt = EditorApplication.timeSinceStartup + Math.Max(2, m_Delays[m_Frame]) / 100.0;
            Repaint();
        }

        void OnGUI()
        {
            if (m_Error != null) { EditorGUILayout.HelpBox("Couldn't open it: " + m_Error, MessageType.Error); return; }
            if (m_Frames.Count == 0) return;
            var tex = m_Frames[Mathf.Clamp(m_Frame, 0, m_Frames.Count - 1)];
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(tex.width + " x " + tex.height + (m_Frames.Count > 1 ? "   ·   " + m_Frames.Count + " frames" : "") + "   ·   " + Size(m_Path), EditorStyles.miniLabel);
            m_Actual = GUILayout.Toggle(m_Actual, "Actual size", EditorStyles.toolbarButton, GUILayout.Width(80));
            if (GUILayout.Button("Show in folder", EditorStyles.toolbarButton, GUILayout.Width(95))) EditorUtility.RevealInFinder(m_Path);
            EditorGUILayout.EndHorizontal();
            if (m_Actual)
            {
                m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
                GUILayout.Label(tex, GUIStyle.none, GUILayout.Width(tex.width), GUILayout.Height(tex.height));
                EditorGUILayout.EndScrollView();
            }
            else
            {
                var r = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            }
        }

        public static string Size(string path) { try { long b = new FileInfo(path).Length; return b >= 1024 * 1024 ? (b / 1048576f).ToString("0.0") + " MB" : (b / 1024f).ToString("0") + " KB"; } catch { return "?"; } }
    }
}
