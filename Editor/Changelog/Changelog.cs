using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// What changed in each version, kept in CHANGELOG.md next to Assets (plain Markdown: readable on GitHub, shared
    /// through git). Jot things down as you go (Tools > Changelog: New / Changed / Fixed / Removed, Enter); when Build All
    /// bumps the version, the notes become that version's. Copy a version as an itch.io devlog or as plain text.
    /// </summary>
    public static class Changelog
    {
        public static readonly string[] Kinds = { "New", "Changed", "Fixed", "Removed" };
        public static readonly Color[] KindColours = { new Color(0.35f, 0.78f, 0.45f), new Color(0.33f, 0.6f, 0.95f), new Color(0.97f, 0.6f, 0.25f), new Color(0.93f, 0.35f, 0.32f) };
        public const string Unreleased = "Unreleased";

        [Serializable] public class Entry { public int kind; public string text; }
        [Serializable] public class Version { public string name, date; public List<Entry> entries = new List<Entry>(); }

        public static string FilePath => Path.GetFullPath("CHANGELOG.md");
        static List<Version> s_Versions; static DateTime s_ReadAt;

        public static List<Version> Versions
        {
            get
            {
                var t = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
                if (s_Versions == null || t != s_ReadAt) { s_Versions = Read(); s_ReadAt = t; }
                return s_Versions;
            }
        }

        public static Version Next
        {
            get
            {
                var v = Versions.FirstOrDefault(x => x.name == Unreleased);
                if (v == null) { v = new Version { name = Unreleased }; Versions.Insert(0, v); }
                return v;
            }
        }

        public static void Add(int kind, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Next.entries.Add(new Entry { kind = kind, text = text.Trim() });
            Save();
        }

        /// <summary>The notes so far become version v (called by Build All when it bumps the version).</summary>
        public static bool Release(string version)
        {
            var next = Versions.FirstOrDefault(x => x.name == Unreleased);
            if (next == null || next.entries.Count == 0) return false;
            string name = "v" + version.TrimStart('v', 'V');
            var existing = Versions.FirstOrDefault(x => x.name == name);
            if (existing != null) { existing.entries.AddRange(next.entries); Versions.Remove(next); }
            else { next.name = name; next.date = DateTime.Now.ToString("yyyy-MM-dd"); }
            Save();
            ChangeScanner.TakeSnapshot(); // (the next suggestions compare with the project as it is now)
            return true;
        }

        // ---- the file (Markdown, Keep-a-Changelog style)
        public static void Save()
        {
            var sb = new StringBuilder("# Changelog\n\nWhat changed in each version (Tools > Changelog).\n");
            foreach (var v in Versions)
            {
                if (v.name == Unreleased && v.entries.Count == 0) continue;
                sb.Append("\n## ").Append(v.name == Unreleased ? "Unreleased" : v.name + (string.IsNullOrEmpty(v.date) ? "" : " (" + v.date + ")")).Append('\n');
                for (int k = 0; k < Kinds.Length; k++)
                {
                    var es = v.entries.Where(e => e.kind == k).ToList(); if (es.Count == 0) continue;
                    sb.Append("\n### ").Append(Kinds[k]).Append('\n');
                    foreach (var e in es) sb.Append("- ").Append(e.text.Replace("\n", " ")).Append('\n');
                }
            }
            File.WriteAllText(FilePath, sb.ToString());
            s_ReadAt = File.GetLastWriteTimeUtc(FilePath);
        }

        static List<Version> Read()
        {
            var list = new List<Version>();
            if (!File.Exists(FilePath)) return list;
            Version cur = null; int kind = 0;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                string l = raw.Trim();
                if (l.StartsWith("## "))
                {
                    string h = l.Substring(3).Trim(); string date = null;
                    int p = h.IndexOf('('); if (p > 0 && h.EndsWith(")")) { date = h.Substring(p + 1, h.Length - p - 2); h = h.Substring(0, p).Trim(); }
                    h = h.Trim('[', ']');
                    cur = new Version { name = h.Equals(Unreleased, StringComparison.OrdinalIgnoreCase) ? Unreleased : h, date = date }; list.Add(cur); kind = 0;
                }
                else if (l.StartsWith("### ") && cur != null)
                {
                    string k = l.Substring(4).Trim();
                    kind = k.StartsWith("Fix", StringComparison.OrdinalIgnoreCase) ? 2 : k.StartsWith("Chang", StringComparison.OrdinalIgnoreCase) ? 1 : k.StartsWith("Remov", StringComparison.OrdinalIgnoreCase) ? 3 : 0;
                }
                else if ((l.StartsWith("- ") || l.StartsWith("* ")) && cur != null) cur.entries.Add(new Entry { kind = kind, text = l.Substring(2).Trim() });
            }
            return list;
        }

        // ---- copying
        public static string AsDevlog(Version v, string product)
        {
            string title = v.name == Unreleased ? "What's coming next" : "What's new in " + v.name;
            var sb = new StringBuilder("## " + title + "\n");
            string[] heads = { "**New**", "**Changed**", "**Fixed**", "**Removed**" };
            for (int k = 0; k < Kinds.Length; k++)
            {
                var es = v.entries.Where(e => e.kind == k).ToList(); if (es.Count == 0) continue;
                sb.Append('\n').Append(heads[k]).Append('\n');
                foreach (var e in es) sb.Append("- ").Append(e.text).Append('\n');
            }
            sb.Append("\nThanks for playing " + product + "!\n");
            return sb.ToString();
        }

        public static string AsText(Version v)
        {
            var sb = new StringBuilder((v.name == Unreleased ? "Next version" : v.name) + "\n");
            foreach (var e in v.entries.OrderBy(e => e.kind)) sb.Append("- ").Append(Kinds[e.kind]).Append(": ").Append(e.text).Append('\n');
            return sb.ToString();
        }
    }

    public class ChangelogWindow : EditorWindow
    {
        [MenuItem("Tools/Changelog", false, 152)]
        public static void Open() { var w = GetWindow<ChangelogWindow>(false, "Changelog", true); w.minSize = new Vector2(340, 360); }

        [SerializeField] int m_Kind; string m_Text = ""; Vector2 m_Scroll;
        List<ChangeScanner.Suggestion> m_Suggestions;
        readonly HashSet<string> m_Open = new HashSet<string>();
        GUIStyle m_Chip, m_Head, m_Item, m_Hint;

        void OnGUI()
        {
            if (m_Chip == null)
            {
                m_Chip = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                m_Head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
                m_Item = new GUIStyle(EditorStyles.label) { wordWrap = true };
                m_Hint = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }, alignment = TextAnchor.MiddleLeft };
            }
            var next = Changelog.Next;
            // ---- jot it down
            var top = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Next version (after v" + PlayerSettings.bundleVersion + ")", m_Head);
            var old = GUI.backgroundColor;
            EditorGUILayout.BeginHorizontal();
            for (int k = 0; k < Changelog.Kinds.Length; k++)
            {
                GUI.backgroundColor = m_Kind == k ? Changelog.KindColours[k] : Color.Lerp(Changelog.KindColours[k], Color.gray, 0.6f);
                if (GUILayout.Toggle(m_Kind == k, Changelog.Kinds[k], "Button", GUILayout.Height(22))) m_Kind = k;
            }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("HienaDevChangelogText");
            m_Text = EditorGUILayout.TextField(m_Text, GUILayout.Height(22));
            if (string.IsNullOrEmpty(m_Text) && Event.current.type == EventType.Repaint) { var hr = GUILayoutUtility.GetLastRect(); hr.x += 4; GUI.Label(hr, "What changed? (Enter)", m_Hint); }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "HienaDevChangelogText";
            GUI.backgroundColor = Changelog.KindColours[m_Kind];
            if ((GUILayout.Button("Add", GUILayout.Width(50), GUILayout.Height(22)) || enter) && !string.IsNullOrWhiteSpace(m_Text))
            {
                Changelog.Add(m_Kind, m_Text); m_Text = "";
                if (enter) Event.current.Use();
                GUI.FocusControl("HienaDevChangelogText");
            }
            GUI.backgroundColor = old;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // ---- suggestions from what changed in the project (and git, when there is one)
            EditorGUILayout.BeginHorizontal();
            var sb2 = GUI.backgroundColor; GUI.backgroundColor = new Color(0.65f, 0.45f, 0.92f);
            if (GUILayout.Button(new GUIContent(" Suggest from what changed", EditorGUIUtility.IconContent("d_Search Icon").image, "Looks at the scenes, prefabs, scripts, art and sounds added, changed or removed since the last version (and git commits if the project uses git)"), GUILayout.Height(24)))
            { m_Suggestions = ChangeScanner.Suggest(); if (m_Suggestions.Count == 0) ShowNotification(new GUIContent(ChangeScanner.HasSnapshot ? "Nothing changed yet" : "Watching for changes from now")); }
            GUI.backgroundColor = sb2;
            EditorGUILayout.EndHorizontal();
            if (m_Suggestions != null && m_Suggestions.Count > 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Tick the ones to keep (you can reword them):", EditorStyles.miniBoldLabel);
                foreach (var s in m_Suggestions)
                {
                    EditorGUILayout.BeginHorizontal();
                    s.use = EditorGUILayout.Toggle(s.use, GUILayout.Width(16));
                    var cr = GUILayoutUtility.GetRect(62, 16, GUILayout.Width(62), GUILayout.Height(16));
                    EditorGUI.DrawRect(cr, Changelog.KindColours[s.kind]);
                    if (GUI.Button(cr, Changelog.Kinds[s.kind], m_Chip)) s.kind = (s.kind + 1) % Changelog.Kinds.Length;
                    s.text = EditorGUILayout.TextField(s.text);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.BeginHorizontal();
                int n = m_Suggestions.Count(x => x.use);
                GUI.enabled = n > 0;
                if (GUILayout.Button("Add " + n + " to the notes")) { foreach (var s in m_Suggestions.Where(x => x.use)) Changelog.Add(s.kind, s.text); m_Suggestions = null; }
                GUI.enabled = true;
                if (GUILayout.Button("Close", GUILayout.Width(60))) m_Suggestions = null;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Click a coloured tag to change it (New / Changed / Fixed / Removed).", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            System.Action later = null;
            // ---- the notes so far
            if (next.entries.Count > 0) later = DrawVersion(next, true) ?? later;
            else EditorGUILayout.LabelField("Nothing yet for the next version. Build All (with Bump version) turns these notes into that version's.", EditorStyles.wordWrappedMiniLabel);
            // ---- earlier versions
            foreach (var v in Changelog.Versions.Where(x => x.name != Changelog.Unreleased).ToList())
            {
                EditorGUILayout.Space(2);
                bool open = m_Open.Contains(v.name);
                bool now = EditorGUILayout.Foldout(open, v.name + (string.IsNullOrEmpty(v.date) ? "" : "   " + v.date) + "   (" + v.entries.Count + ")", true);
                if (now != open) { if (now) m_Open.Add(v.name); else m_Open.Remove(v.name); }
                if (now) later = DrawVersion(v, false) ?? later;
            }
            EditorGUILayout.EndScrollView();
            later?.Invoke();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open CHANGELOG.md")) { if (!File.Exists(Changelog.FilePath)) Changelog.Save(); EditorUtility.OpenWithDefaultApp(Changelog.FilePath); }
            GUI.enabled = next.entries.Count > 0;
            if (GUILayout.Button(new GUIContent("Release as v" + PlayerSettings.bundleVersion, "Turn the notes so far into this version's (Build All does it by itself when it bumps the version)"))) Changelog.Release(PlayerSettings.bundleVersion);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // a version's notes as coloured rows; returns a change to make after drawing (or null)
        System.Action DrawVersion(Changelog.Version v, bool editable)
        {
            System.Action change = null;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (!editable) EditorGUILayout.Space(1);
            foreach (var e in v.entries.OrderBy(x => x.kind).ToList())
            {
                EditorGUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(62, 16, GUILayout.Width(62), GUILayout.Height(16));
                EditorGUI.DrawRect(r, Changelog.KindColours[e.kind]); GUI.Label(r, Changelog.Kinds[e.kind], m_Chip);
                GUILayout.Label(e.text, m_Item);
                if (editable && GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22))) { var ee = e; change = () => { v.entries.Remove(ee); Changelog.Save(); }; }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Copy as itch devlog", "Markdown for an itch.io devlog post"), EditorStyles.miniButtonLeft)) { EditorGUIUtility.systemCopyBuffer = Changelog.AsDevlog(v, PlayerSettings.productName); ShowNotification(new GUIContent("Copied")); }
            if (GUILayout.Button("Copy as text", EditorStyles.miniButtonRight)) { EditorGUIUtility.systemCopyBuffer = Changelog.AsText(v); ShowNotification(new GUIContent("Copied")); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return change;
        }

        void OnFocus() => Repaint();
    }
}
