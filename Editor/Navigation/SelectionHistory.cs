using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace HienaDev.DevTools
{
    /// <summary>
    /// Back and forward through what you've selected, like a web browser: the ◀ ▶ buttons in the toolbar (or Alt+Left /
    /// Alt+Right), and a list of the recent ones. Works for assets and scene objects.
    /// </summary>
    [InitializeOnLoad]
    public static class SelectionHistory
    {
        public const string BackPath = "HienaDev/Selection Back", ForwardPath = "HienaDev/Selection Forward", RecentPath = "HienaDev/Recent Selections";
        const int Max = 60;
        [Serializable] class Entry { public List<string> ids = new List<string>(); public string label; }
        [Serializable] class State { public List<Entry> list = new List<Entry>(); public int at = -1; }
        static State s; static bool s_Moving;

        static SelectionHistory()
        {
            var j = SessionState.GetString("HienaDev.SelectionHistory", "");
            s = j.Length > 0 ? JsonUtility.FromJson<State>(j) : new State();
            Selection.selectionChanged += Changed;
            // (also checked each editor tick: the event doesn't always arrive, e.g. while Unity is in the background)
            EditorApplication.update += () => { int id = Selection.activeInstanceID; if (id != s_LastId) { s_LastId = id; Changed(); } };
        }
        static int s_LastId;

        static void Save() { SessionState.SetString("HienaDev.SelectionHistory", JsonUtility.ToJson(s)); Refresh(); }

        static void Changed()
        {
            if (s_Moving) return;
            var objs = Selection.objects.Where(o => o).ToArray();
            if (objs.Length == 0) return;
            var e = new Entry { ids = objs.Select(o => GlobalObjectId.GetGlobalObjectIdSlow(o).ToString()).ToList(), label = objs.Length == 1 ? objs[0].name : objs[0].name + " +" + (objs.Length - 1) };
            if (s.at >= 0 && s.at < s.list.Count && s.list[s.at].ids.SequenceEqual(e.ids)) return;
            // a new selection after going back drops the "forward" ones (like a browser)
            if (s.at < s.list.Count - 1) s.list.RemoveRange(s.at + 1, s.list.Count - s.at - 1);
            s.list.Add(e);
            if (s.list.Count > Max) s.list.RemoveAt(0);
            s.at = s.list.Count - 1;
            Save();
        }

        public static bool CanBack => s.at > 0;
        public static bool CanForward => s.at < s.list.Count - 1;

        [Shortcut("HienaDev/Selection Back", KeyCode.LeftArrow, ShortcutModifiers.Alt)] public static void Back() { if (CanBack) GoTo(s.at - 1, -1); }
        [Shortcut("HienaDev/Selection Forward", KeyCode.RightArrow, ShortcutModifiers.Alt)] public static void Forward() { if (CanForward) GoTo(s.at + 1, +1); }

        // goes to an entry; skips ones whose things are all gone (deleted, or in a scene that's closed)
        static void GoTo(int i, int dir)
        {
            while (i >= 0 && i < s.list.Count)
            {
                var objs = Resolve(s.list[i]);
                if (objs.Length > 0)
                {
                    s_Moving = true;
                    try { Selection.objects = objs; if (objs.Length == 1) EditorGUIUtility.PingObject(objs[0]); }
                    finally { s_Moving = false; }
                    s.at = i; Save(); return;
                }
                if (dir == 0) return;
                i += dir;
            }
        }

        static UnityEngine.Object[] Resolve(Entry e) =>
            e.ids.Select(id => GlobalObjectId.TryParse(id, out var g) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(g) : null).Where(o => o).ToArray();

        public static void RecentMenu(Rect r)
        {
            var m = new GenericMenu();
            for (int i = s.list.Count - 1, n = 0; i >= 0 && n < 25; i--, n++)
            {
                int k = i;
                m.AddItem(new GUIContent((n + 1).ToString("00") + "  " + s.list[i].label.Replace("/", "∕")), i == s.at, () => GoTo(k, 0));
            }
            if (s.list.Count == 0) m.AddDisabledItem(new GUIContent("Nothing selected yet"));
            else { m.AddSeparator(""); m.AddItem(new GUIContent("Clear the history"), false, () => { s = new State(); Save(); }); }
            m.DropDown(r);
        }

        static void Refresh()
        {
#if UNITY_6000_3_OR_NEWER
            MainToolbar.Refresh(BackPath); MainToolbar.Refresh(ForwardPath);
#endif
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(BackPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        static MainToolbarElement BackButton() => new MainToolbarButton(new MainToolbarContent(EditorGUIUtility.IconContent("d_back").image as Texture2D, "Back to the previous selection (Alt+Left)"), Back) { enabled = CanBack };
        [MainToolbarElement(ForwardPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        static MainToolbarElement ForwardButton() => new MainToolbarButton(new MainToolbarContent(EditorGUIUtility.IconContent("d_forward").image as Texture2D, "Forward again (Alt+Right)"), Forward) { enabled = CanForward };
        [MainToolbarElement(RecentPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        static MainToolbarElement RecentDropdown() => new MainToolbarDropdown(new MainToolbarContent(EditorGUIUtility.IconContent("d_UnityEditor.HistoryWindow").image as Texture2D, "Recently selected"), RecentMenu);
#endif
    }
}
