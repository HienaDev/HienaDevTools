using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Rename lots of things at once, in plain words. Select objects (Hierarchy) or files (Project), then either give them
    /// all one new name ("Rock" → Rock 1, Rock 2, Rock 3...) or change parts of their names (swap a word, take off the
    /// "(1)" copies get, cut letters, add words, capitals). The new names show beside the old ones before anything
    /// changes; Ctrl+Z undoes it for objects. Tools > Bulk Rename, or right-click > Bulk Rename....
    /// </summary>
    public class BulkRename : EditorWindow
    {
        [MenuItem("Tools/Bulk Rename", false, 151)] static void Open() { var w = GetWindow<BulkRename>(false, "Bulk Rename", true); w.minSize = new Vector2(360, 420); }
        [MenuItem("GameObject/Bulk Rename...", false, 1)] static void FromHierarchy() => Open();
        [MenuItem("Assets/Bulk Rename...", false, 26)] static void FromProject() => Open();

        // mode 0: one new name for all (+ numbers); mode 1: change parts of the names
        [SerializeField] int m_Mode;
        [SerializeField] string m_Name = "";
        // numbers: style 0 = 1 2 3, 1 = 01 02 03, 2 = 001 002 003; where 0 = after, 1 = before
        [SerializeField] bool m_Number = true; [SerializeField] int m_NumStyle, m_NumWhere, m_Start = 1; [SerializeField] int m_Joiner; // 0 space, 1 _, 2 -, 3 nothing
        // change parts
        [SerializeField] string m_Find = "", m_With = "", m_Before = "", m_After = "";
        [SerializeField] bool m_RemoveCopies = true, m_MatchCase;
        [SerializeField] int m_CutStart, m_CutEnd, m_Case; // case: 0 keep, 1 lower, 2 UPPER, 3 Each Word
        [SerializeField] bool m_Expert; [SerializeField] string m_Pattern = "", m_PatternWith = "";
        [SerializeField] int m_Order; // 0 as listed (Hierarchy / folder), 1 A to Z
        Vector2 m_Scroll;
        GUIStyle m_Old, m_New, m_Bad, m_Big, m_Step;

        static readonly string[] Joiners = { " ", "_", "-", "" };

        void OnSelectionChange() => Repaint();

        List<UnityEngine.Object> Targets()
        {
            var gos = Selection.gameObjects.Where(g => g.scene.IsValid()).Cast<UnityEngine.Object>().ToList();
            List<UnityEngine.Object> list;
            if (gos.Count > 0) list = gos.OrderBy(o => HierarchyPath((GameObject)o)).ToList();
            else list = Selection.objects.Where(o => !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o)) && AssetDatabase.IsMainAsset(o)).OrderBy(o => AssetDatabase.GetAssetPath(o), StringComparer.OrdinalIgnoreCase).ToList();
            if (m_Order == 1) list = list.OrderBy(o => o.name, StringComparer.OrdinalIgnoreCase).ToList();
            return list;
        }

        static string HierarchyPath(GameObject g)
        {
            var parts = new List<string>();
            for (var t = g.transform; t != null; t = t.parent) parts.Insert(0, t.GetSiblingIndex().ToString("D5"));
            return g.scene.name + "/" + string.Join("/", parts);
        }

        string Number(int i)
        {
            int n = m_Start + i;
            return m_NumStyle == 0 ? n.ToString() : n.ToString(m_NumStyle == 1 ? "00" : "000");
        }

        string NewName(string name, int i, int count)
        {
            string s;
            if (m_Mode == 0)
            {
                s = string.IsNullOrWhiteSpace(m_Name) ? name : m_Name.Trim();
                if (m_Number && count > 1) s = m_NumWhere == 1 ? Number(i) + Joiners[m_Joiner] + s : s + Joiners[m_Joiner] + Number(i);
                return s;
            }
            s = name;
            if (m_RemoveCopies) s = Regex.Replace(s, @"\s*\(\d+\)$", "");
            if (!string.IsNullOrEmpty(m_Find))
                s = Regex.Replace(s, Regex.Escape(m_Find), (m_With ?? "").Replace("$", "$$"), m_MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            if (m_Expert && !string.IsNullOrEmpty(m_Pattern)) { try { s = Regex.Replace(s, m_Pattern, m_PatternWith ?? ""); } catch { } }
            if (m_CutStart > 0) s = m_CutStart >= s.Length ? "" : s.Substring(m_CutStart);
            if (m_CutEnd > 0) s = m_CutEnd >= s.Length ? "" : s.Substring(0, s.Length - m_CutEnd);
            if (m_Case == 1) s = s.ToLowerInvariant();
            else if (m_Case == 2) s = s.ToUpperInvariant();
            else if (m_Case == 3) s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
            s = (m_Before ?? "") + s + (m_After ?? "");
            if (m_Number && count > 1) s = m_NumWhere == 1 ? Number(i) + Joiners[m_Joiner] + s : s + Joiners[m_Joiner] + Number(i);
            return s.Trim();
        }

        void Styles()
        {
            if (m_Old != null) return;
            m_Old = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            m_New = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip, normal = { textColor = new Color(0.45f, 0.85f, 0.5f) } };
            m_Bad = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip, normal = { textColor = new Color(0.95f, 0.45f, 0.4f) } };
            m_Big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            m_Step = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        void OnGUI()
        {
            Styles();
            var targets = Targets();
            bool objects = targets.Count > 0 && targets[0] is GameObject;

            // ---- what's selected
            if (targets.Count == 0)
            {
                var r = EditorGUILayout.GetControlRect(false, 54);
                EditorGUI.DrawRect(r, new Color(0.33f, 0.6f, 0.95f, 0.85f));
                GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 20), "First, select what to rename", m_Big);
                GUI.Label(new Rect(r.x + 10, r.y + 28, r.width - 20, 18), "Objects in the Hierarchy, or files in the Project window.", EditorStyles.label);
            }
            else EditorGUILayout.LabelField("Renaming " + targets.Count + (objects ? " object" : " file") + (targets.Count == 1 ? "" : "s"), m_Big);

            // ---- how: one name for all, or change parts
            EditorGUILayout.Space(4);
            m_Mode = GUILayout.Toolbar(m_Mode, new[] { "Give them one new name", "Change part of the names" }, GUILayout.Height(26));
            EditorGUILayout.Space(4);
            if (m_Mode == 0)
            {
                Line("New name", () => { m_Name = EditorGUILayout.TextField(m_Name); });
                if (string.IsNullOrWhiteSpace(m_Name)) EditorGUILayout.LabelField("Type a name, e.g. Rock, Tree, Enemy...", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                m_RemoveCopies = EditorGUILayout.ToggleLeft("Take off the (1), (2)... that copies get", m_RemoveCopies);
                Line("Change", () => { m_Find = EditorGUILayout.TextField(m_Find); GUILayout.Label("into", GUILayout.Width(30)); m_With = EditorGUILayout.TextField(m_With); });
                Line("Add at the start", () => { m_Before = EditorGUILayout.TextField(m_Before); });
                Line("Add at the end", () => { m_After = EditorGUILayout.TextField(m_After); });
                Line("Cut letters off", () => { GUILayout.Label("the start", GUILayout.Width(52)); m_CutStart = Mathf.Max(0, EditorGUILayout.IntField(m_CutStart, GUILayout.Width(34))); GUILayout.Label("the end", GUILayout.Width(48)); m_CutEnd = Mathf.Max(0, EditorGUILayout.IntField(m_CutEnd, GUILayout.Width(34))); GUILayout.FlexibleSpace(); });
                Line("Capitals", () => { m_Case = GUILayout.Toolbar(m_Case, new[] { "As they are", "lowercase", "UPPERCASE", "Each Word" }); });
                m_MatchCase = EditorGUILayout.ToggleLeft("\"Change\" only matches the exact capitals", m_MatchCase);
                m_Expert = EditorGUILayout.Foldout(m_Expert, "For experts: regular expression", true);
                if (m_Expert) Line("Pattern", () => { m_Pattern = EditorGUILayout.TextField(m_Pattern); GUILayout.Label("into", GUILayout.Width(30)); m_PatternWith = EditorGUILayout.TextField(m_PatternWith); });
            }

            // ---- numbers (both modes)
            EditorGUILayout.Space(4);
            m_Number = EditorGUILayout.ToggleLeft("Number them", m_Number, EditorStyles.boldLabel);
            if (m_Number)
            {
                Line("Style", () => { m_NumStyle = GUILayout.Toolbar(m_NumStyle, new[] { "1, 2, 3", "01, 02, 03", "001, 002" }); });
                Line("Put it", () => { m_NumWhere = GUILayout.Toolbar(m_NumWhere, new[] { "after the name", "before the name" }); });
                Line("Joined with", () => { m_Joiner = GUILayout.Toolbar(m_Joiner, new[] { "a space", "_", "-", "nothing" }); });
                Line("Start at", () => { m_Start = EditorGUILayout.IntField(m_Start, GUILayout.Width(50)); GUILayout.FlexibleSpace(); });
                Line("In the order", () => { m_Order = GUILayout.Toolbar(m_Order, new[] { objects ? "of the Hierarchy" : "of the folder", "A to Z" }); });
            }

            // ---- the new names
            EditorGUILayout.Space(6);
            var names = targets.Select((o, i) => NewName(o.name, i, targets.Count)).ToList();
            var dupes = objects ? new HashSet<string>() : new HashSet<string>(names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            if (targets.Count > 0)
            {
                var hr = EditorGUILayout.GetControlRect(false, 16);
                GUI.Label(new Rect(hr.x, hr.y, hr.width * 0.45f, hr.height), "Now", EditorStyles.miniBoldLabel);
                GUI.Label(new Rect(hr.x + hr.width * 0.45f + 18, hr.y, hr.width * 0.5f, hr.height), "Will be", EditorStyles.miniBoldLabel);
            }
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < targets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(targets[i].name, m_Old, GUILayout.Width((position.width - 40) * 0.45f));
                GUILayout.Label("→", GUILayout.Width(14));
                bool bad = string.IsNullOrEmpty(names[i]) || dupes.Contains(names[i]) || (!objects && names[i].IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0);
                GUILayout.Label(names[i] == targets[i].name ? "(no change)" : names[i], bad ? m_Bad : names[i] == targets[i].name ? m_Old : m_New);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            int changes = targets.Where((o, i) => names[i] != o.name).Count();
            bool anyBad = names.Any(string.IsNullOrEmpty) || dupes.Count > 0;
            if (anyBad) EditorGUILayout.LabelField(dupes.Count > 0 ? "Two files would end up with the same name (in red): turn on Number them." : "Some names would be empty (in red).", m_Bad);
            GUI.enabled = changes > 0 && !anyBad;
            var ob = GUI.backgroundColor; GUI.backgroundColor = new Color(0.35f, 0.78f, 0.45f);
            if (GUILayout.Button(changes == 0 ? "Nothing to rename yet" : "Rename " + changes + (objects ? " object" : " file") + (changes == 1 ? "" : "s"), GUILayout.Height(34))) Apply(targets, names);
            GUI.backgroundColor = ob; GUI.enabled = true;
            if (objects) EditorGUILayout.LabelField("Changed your mind? Ctrl+Z", EditorStyles.centeredGreyMiniLabel);
        }

        static void Line(string label, Action draw)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            draw();
            EditorGUILayout.EndHorizontal();
        }

        void Apply(List<UnityEngine.Object> targets, List<string> names)
        {
            int group = Undo.GetCurrentGroup(); var errors = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                var o = targets[i]; if (names[i] == o.name) continue;
                if (o is GameObject go) { Undo.RecordObject(go, "Bulk rename"); go.name = names[i]; }
                else
                {
                    string err = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(o), names[i]);
                    if (!string.IsNullOrEmpty(err)) errors.Add(o.name + ": " + err);
                }
            }
            Undo.CollapseUndoOperations(group); Undo.SetCurrentGroupName("Bulk rename");
            if (errors.Count > 0) EditorUtility.DisplayDialog("Bulk Rename", "Some files couldn't be renamed:\n" + string.Join("\n", errors.Take(10)), "OK");
            AssetDatabase.SaveAssets();
            Repaint();
        }
    }
}
