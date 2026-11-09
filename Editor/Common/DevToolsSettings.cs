using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Settings shared with the team through git: ProjectSettings/HienaDevTools.json (folder colours and icons, the jam
    /// deadline). Personal switches (on/off of each helper) live in EditorPrefs. Both are on the page
    /// Edit > Project Settings > HienaDev Dev Tools.
    /// </summary>
    [Serializable]
    public class DevToolsSettings
    {
        [Serializable] public class Folder { public string guid; public Color color = Color.clear; public string icon = ""; }
        public List<Folder> folders = new List<Folder>();
        public string jamName = "";
        public string jamDeadline = ""; // ISO local time "2026-11-10T18:00"

        static DevToolsSettings s_Instance; static DateTime s_ReadAt;
        static string FilePath => Path.GetFullPath("ProjectSettings/HienaDevTools.json");

        public static DevToolsSettings Get
        {
            get
            {
                // (re-read when git changes the file)
                var t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (s_Instance == null || t != s_ReadAt)
                {
                    s_Instance = new DevToolsSettings(); s_ReadAt = t;
                    try { if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_Instance); } catch (Exception e) { Debug.LogWarning("[Dev Tools] couldn't read " + FilePath + ": " + e.Message); }
                }
                return s_Instance;
            }
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
            s_ReadAt = File.GetLastWriteTimeUtc(FilePath);
        }

        public Folder FolderFor(string guid, bool create = false)
        {
            foreach (var f in folders) if (f.guid == guid) return f;
            if (!create) return null;
            var n = new Folder { guid = guid }; folders.Add(n); return n;
        }

        // ---- personal switches
        public static bool HierarchyIcons { get => EditorPrefs.GetBool("HienaDev.HierarchyIcons", true); set { EditorPrefs.SetBool("HienaDev.HierarchyIcons", value); EditorApplication.RepaintHierarchyWindow(); } }
        public static bool HierarchyNotes { get => EditorPrefs.GetBool("HienaDev.HierarchyNotes", true); set { EditorPrefs.SetBool("HienaDev.HierarchyNotes", value); EditorApplication.RepaintHierarchyWindow(); } }
        public static bool FolderLooks { get => EditorPrefs.GetBool("HienaDev.FolderLooks", true); set { EditorPrefs.SetBool("HienaDev.FolderLooks", value); EditorApplication.RepaintProjectWindow(); } }
        public static bool AutoFolderIcons { get => EditorPrefs.GetBool("HienaDev.AutoFolderIcons", true); set { EditorPrefs.SetBool("HienaDev.AutoFolderIcons", value); EditorApplication.RepaintProjectWindow(); } }
        public static bool JamReminders { get => EditorPrefs.GetBool("HienaDev.JamReminders", true); set => EditorPrefs.SetBool("HienaDev.JamReminders", value); }
    }

    static class DevToolsSettingsPage
    {
        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider("Project/HienaDev Dev Tools", SettingsScope.Project)
        {
            label = "HienaDev Dev Tools",
            keywords = new HashSet<string> { "hierarchy", "icons", "notes", "folder", "colour", "color", "jam", "timer", "toolbar" },
            guiHandler = _ =>
            {
                EditorGUILayout.LabelField("Just for you (this computer)", EditorStyles.boldLabel);
                DevToolsSettings.HierarchyIcons = EditorGUILayout.ToggleLeft("Hierarchy: show each object's main component icon", DevToolsSettings.HierarchyIcons);
                DevToolsSettings.HierarchyNotes = EditorGUILayout.ToggleLeft("Hierarchy: show notes and TODOs", DevToolsSettings.HierarchyNotes);
                DevToolsSettings.FolderLooks = EditorGUILayout.ToggleLeft("Project: folder colours and icons", DevToolsSettings.FolderLooks);
                DevToolsSettings.AutoFolderIcons = EditorGUILayout.ToggleLeft("Project: automatic icons for common folder names (Scripts, Audio, Scenes...)", DevToolsSettings.AutoFolderIcons);
                DevToolsSettings.JamReminders = EditorGUILayout.ToggleLeft("Jam timer reminders", DevToolsSettings.JamReminders);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Shared with the team (saved in ProjectSettings/HienaDevTools.json, kept by git)", EditorStyles.boldLabel);
                JamTimer.DrawSettings();
                EditorGUILayout.Space(6);
                var s = DevToolsSettings.Get;
                EditorGUILayout.LabelField("Folder colours and icons: right-click a folder > Folder Colour / Folder Icon. Set now: " + s.folders.Count, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Toolbar buttons (Play from first scene, speed, restart, step, scenes, jam timer): right-click Unity's top toolbar to show or hide each one.", EditorStyles.wordWrappedMiniLabel);
            }
        };
    }
}
