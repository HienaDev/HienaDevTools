using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

namespace HienaDev.DevTools
{
    /// <summary>
    /// A countdown to the jam's deadline in Unity's toolbar, with reminders as it gets close (24 h, 6 h, 2 h, 1 h, 30 min,
    /// 10 min). The deadline is saved in ProjectSettings/HienaDevTools.json, so everyone on the team (through git) sees the
    /// same one. Click the timer to set it.
    /// </summary>
    [InitializeOnLoad]
    public static class JamTimer
    {
        public const string Path = "HienaDev/Jam Timer";
        static readonly (double hours, string text)[] Reminders =
        {
            (24, "One day left. Good time to cut what won't make it, and make a first build to check nothing breaks."),
            (6, "6 hours left. Stop adding features: polish, sound, menus, the itch page."),
            (2, "2 hours left. Make a build now (Tools > Build All) and play it start to finish."),
            (1, "1 hour left. Upload a build to the jam page now; you can replace it later."),
            (0.5, "30 minutes left. Only fixes from here. Upload what you have."),
            (1.0 / 6, "10 minutes left! Upload and submit to the jam now."),
            (0, "Time's up! Make sure the game is submitted."),
        };

        static double s_NextTick;
        static string s_Shown = "";

        static JamTimer() { EditorApplication.update += Tick; }

        public static DateTime? Deadline
        {
            get
            {
                var s = DevToolsSettings.Get.jamDeadline;
                return !string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : (DateTime?)null;
            }
            set
            {
                var st = DevToolsSettings.Get;
                st.jamDeadline = value.HasValue ? value.Value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) : "";
                st.Save(); s_Shown = ""; Refresh();
            }
        }

        public static string Label
        {
            get
            {
                var d = Deadline; if (!d.HasValue) return "Jam timer";
                var left = d.Value - DateTime.Now;
                if (left.TotalSeconds <= 0) return "Time's up!";
                if (left.TotalHours >= 48) return (int)left.TotalDays + "d " + left.Hours + "h left";
                if (left.TotalHours >= 1) return (int)left.TotalHours + "h " + left.Minutes.ToString("00") + "m left";
                return left.Minutes + "m " + left.Seconds.ToString("00") + "s left";
            }
        }

        static void Refresh()
        {
#if UNITY_6000_3_OR_NEWER
            MainToolbar.Refresh(Path);
#endif
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_NextTick) return;
            var d = Deadline;
            if (!d.HasValue) { s_NextTick = now + 10; return; }
            var left = d.Value - DateTime.Now;
            s_NextTick = now + (left.TotalMinutes < 60 ? 1 : 20);
            if (s_Shown != Label) { s_Shown = Label; Refresh(); }
            if (!DevToolsSettings.JamReminders) return;
            // each reminder once (per deadline, per computer)
            string key = "HienaDev.JamReminded." + Application.dataPath + "." + DevToolsSettings.Get.jamDeadline;
            double done = EditorPrefs.GetFloat(key, float.MaxValue);
            for (int i = Reminders.Length - 1; i >= 0; i--)
            {
                var (hours, text) = Reminders[i];
                if (left.TotalHours > hours || hours >= done) continue;
                // (only the newest one that's due: no flood of old ones when Unity opens late)
                EditorPrefs.SetFloat(key, (float)hours);
                Remind(text);
                break;
            }
        }

        static void Remind(string text)
        {
            string title = string.IsNullOrEmpty(DevToolsSettings.Get.jamName) ? "Jam timer" : DevToolsSettings.Get.jamName;
            Debug.LogWarning("[" + title + "] " + text);
            EditorApplication.Beep();
            var w = EditorWindow.focusedWindow ?? EditorWindow.mouseOverWindow;
            if (w) w.ShowNotification(new GUIContent(title + ": " + text), 8);
        }

        public static void Menu(Rect r)
        {
            var m = new GenericMenu();
            var d = Deadline;
            if (d.HasValue) { m.AddDisabledItem(new GUIContent("Deadline: " + d.Value.ToString("ddd d MMM, HH:mm"))); m.AddSeparator(""); }
            m.AddItem(new GUIContent("Set the deadline..."), false, () => JamDeadlineWindow.Open());
            m.AddItem(new GUIContent("Quick/48 hours from now"), false, () => Deadline = Round(DateTime.Now.AddHours(48)));
            m.AddItem(new GUIContent("Quick/72 hours from now"), false, () => Deadline = Round(DateTime.Now.AddHours(72)));
            m.AddItem(new GUIContent("Quick/7 days from now"), false, () => Deadline = Round(DateTime.Now.AddDays(7)));
            if (d.HasValue) { m.AddSeparator(""); m.AddItem(new GUIContent("Clear the timer"), false, () => Deadline = null); }
            m.AddItem(new GUIContent("Reminders"), DevToolsSettings.JamReminders, () => DevToolsSettings.JamReminders = !DevToolsSettings.JamReminders);
            m.DropDown(r);
        }

        static DateTime Round(DateTime t) => new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute >= 30 ? 30 : 0, 0);

        // on the settings page
        public static void DrawSettings()
        {
            var s = DevToolsSettings.Get;
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.DelayedTextField("Jam name", s.jamName);
            if (EditorGUI.EndChangeCheck()) { s.jamName = name; s.Save(); }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Deadline", Deadline.HasValue ? Deadline.Value.ToString("ddd d MMM yyyy, HH:mm") + "  (" + Label + ")" : "not set");
            if (GUILayout.Button("Set...", GUILayout.Width(60))) JamDeadlineWindow.Open();
            EditorGUILayout.EndHorizontal();
        }

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement(Path, defaultDockPosition = MainToolbarDockPosition.Right)]
        static MainToolbarElement Create()
        {
            var d = Deadline;
            string tip = d.HasValue ? (string.IsNullOrEmpty(DevToolsSettings.Get.jamName) ? "Jam" : DevToolsSettings.Get.jamName) + " deadline: " + d.Value.ToString("ddd d MMM, HH:mm") : "Set the jam's deadline to see a countdown here";
            return new MainToolbarDropdown(new MainToolbarContent(Label, EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow").image as Texture2D, tip), Menu);
        }
#endif
    }

    /// <summary>Pick the deadline: day, hour, minute (your computer's time).</summary>
    public class JamDeadlineWindow : EditorWindow
    {
        string m_Name; int m_Y, m_Mo, m_D, m_H, m_Mi;
        public static void Open()
        {
            var w = CreateInstance<JamDeadlineWindow>();
            w.titleContent = new GUIContent("Jam deadline");
            var d = JamTimer.Deadline ?? DateTime.Now.AddHours(48);
            w.m_Name = DevToolsSettings.Get.jamName; w.m_Y = d.Year; w.m_Mo = d.Month; w.m_D = d.Day; w.m_H = d.Hour; w.m_Mi = d.Minute;
            w.minSize = w.maxSize = new Vector2(320, 150);
            w.ShowUtility();
        }
        void OnGUI()
        {
            m_Name = EditorGUILayout.TextField("Jam name", m_Name);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Day");
            m_D = EditorGUILayout.IntField(m_D, GUILayout.Width(30)); EditorGUILayout.LabelField("/", GUILayout.Width(8));
            m_Mo = EditorGUILayout.IntField(m_Mo, GUILayout.Width(30)); EditorGUILayout.LabelField("/", GUILayout.Width(8));
            m_Y = EditorGUILayout.IntField(m_Y, GUILayout.Width(45));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Time");
            m_H = EditorGUILayout.IntField(m_H, GUILayout.Width(30)); EditorGUILayout.LabelField(":", GUILayout.Width(8));
            m_Mi = EditorGUILayout.IntField(m_Mi, GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();
            DateTime? when = null;
            try { when = new DateTime(m_Y, Mathf.Clamp(m_Mo, 1, 12), Mathf.Clamp(m_D, 1, DateTime.DaysInMonth(m_Y, Mathf.Clamp(m_Mo, 1, 12))), Mathf.Clamp(m_H, 0, 23), Mathf.Clamp(m_Mi, 0, 59), 0); } catch { }
            EditorGUILayout.LabelField(when.HasValue ? when.Value.ToString("dddd d MMMM yyyy, HH:mm") : "Not a real date", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            GUI.enabled = when.HasValue;
            if (GUILayout.Button("Start the countdown", GUILayout.Height(26)))
            {
                var s = DevToolsSettings.Get; s.jamName = m_Name ?? ""; s.Save();
                JamTimer.Deadline = when; Close();
            }
            GUI.enabled = true;
        }
    }
}
