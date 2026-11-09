using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Finds what changed since the last version, to suggest changelog lines: the project's assets compared with a
    /// snapshot taken when the last version was filed (new / changed / removed scenes, prefabs, scripts, art, sounds...),
    /// plus the git commit messages since then when the project uses git. Suggestions only: you pick and reword them.
    /// </summary>
    static class ChangeScanner
    {
        [Serializable] class Snap { public string date, commit; public List<string> paths = new List<string>(); public List<long> stamps = new List<long>(); }
        public class Suggestion { public bool use; public int kind; public string text; }

        static string SnapPath => Path.GetFullPath("Library/HienaDevTools/changelog_snapshot.json");
        public static bool HasSnapshot => File.Exists(SnapPath);
        public static string SnapshotDate { get { try { return JsonUtility.FromJson<Snap>(File.ReadAllText(SnapPath)).date; } catch { return null; } } }

        // every asset under Assets with its last-change time (meta files left out)
        static Dictionary<string, long> Current()
        {
            var d = new Dictionary<string, long>();
            foreach (var p in AssetDatabase.GetAllAssetPaths())
            {
                if (!p.StartsWith("Assets/") || AssetDatabase.IsValidFolder(p)) continue;
                try { d[p] = File.GetLastWriteTimeUtc(p).Ticks / TimeSpan.TicksPerSecond; } catch { }
            }
            return d;
        }

        /// <summary>Remembers the project as it is now (called when a version is filed).</summary>
        public static void TakeSnapshot()
        {
            var s = new Snap { date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), commit = Git("rev-parse HEAD")?.Trim() };
            foreach (var kv in Current()) { s.paths.Add(kv.Key); s.stamps.Add(kv.Value); }
            Directory.CreateDirectory(Path.GetDirectoryName(SnapPath));
            File.WriteAllText(SnapPath, JsonUtility.ToJson(s));
        }

        public static List<Suggestion> Suggest()
        {
            var list = new List<Suggestion>();
            if (!HasSnapshot)
            {
                // the first time: nothing to compare with yet, so list what was worked on since the last version's date
                // (or the last two weeks), then start watching from now
                DateTime since = DateTime.Now.AddDays(-14);
                var last = Changelog.Versions.FirstOrDefault(v => v.name != Changelog.Unreleased && !string.IsNullOrEmpty(v.date));
                if (last != null && DateTime.TryParse(last.date, out var d)) since = d;
                var touched = Current().Where(kv => new DateTime(kv.Value * TimeSpan.TicksPerSecond, DateTimeKind.Utc).ToLocalTime() >= since).Select(kv => kv.Key).ToList();
                foreach (var g in touched.GroupBy(Group).Where(g => g.Key != null).OrderBy(g => g.Key))
                {
                    var names = g.Select(x => Path.GetFileNameWithoutExtension(x)).Distinct().OrderBy(n => n).ToList();
                    list.Add(new Suggestion { kind = 1, text = "Worked on " + g.Key + (names.Count == 1 ? "" : "s") + ": " + string.Join(", ", names.Take(4)) + (names.Count > 4 ? " (+" + (names.Count - 4) + " more)" : "") });
                }
                TakeSnapshot();
                return list;
            }
            var snap = JsonUtility.FromJson<Snap>(File.ReadAllText(SnapPath));
            var before = new Dictionary<string, long>(); for (int i = 0; i < snap.paths.Count; i++) before[snap.paths[i]] = snap.stamps[i];
            var now = Current();
            var added = now.Keys.Where(p => !before.ContainsKey(p)).ToList();
            var removed = before.Keys.Where(p => !now.ContainsKey(p)).ToList();
            var changed = now.Keys.Where(p => before.TryGetValue(p, out var t) && t != now[p]).ToList();

            // git commit messages since the last version
            string log = !string.IsNullOrEmpty(snap.commit) ? Git("log --pretty=%s " + snap.commit + "..HEAD") : null;
            if (log != null)
                foreach (var msg in log.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("Merge")).Distinct())
                    list.Add(new Suggestion { kind = msg.IndexOf("fix", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : msg.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ? 0 : 1, text = msg });

            // assets, grouped by what they are
            foreach (var (kind, verb, paths) in new[] { (0, "New", added), (1, "Changed", changed), (3, "Removed", removed) })
                foreach (var g in paths.GroupBy(Group).Where(g => g.Key != null).OrderBy(g => g.Key))
                {
                    var names = g.Select(p => Path.GetFileNameWithoutExtension(p)).Distinct().OrderBy(n => n).ToList();
                    string shown = string.Join(", ", names.Take(4)) + (names.Count > 4 ? " (+" + (names.Count - 4) + " more)" : "");
                    list.Add(new Suggestion { kind = kind, text = verb + " " + g.Key + (names.Count == 1 ? "" : "s") + ": " + shown });
                }
            return list;
        }

        // what kind of thing a file is, in words (null: not worth a changelog line)
        static string Group(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            switch (e)
            {
                case ".unity": return "scene";
                case ".prefab": return "prefab";
                case ".cs": return p.Contains("/Editor/") ? null : "script";
                case ".png": case ".jpg": case ".jpeg": case ".psd": case ".tga": case ".exr": return "picture";
                case ".wav": case ".mp3": case ".ogg": case ".aif": case ".aiff": return "sound";
                case ".fbx": case ".obj": case ".blend": case ".glb": case ".gltf": return "model";
                case ".mat": return "material";
                case ".shader": case ".shadergraph": case ".hlsl": return "shader";
                case ".anim": case ".controller": return "animation";
                case ".ttf": case ".otf": return "font";
                case ".mp4": case ".webm": case ".mov": return "video";
                case ".uxml": case ".uss": return "UI layout";
                default: return null;
            }
        }

        // a git command in the project folder (null when there's no git or no repository)
        static string Git(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args) { WorkingDirectory = Path.GetFullPath("."), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
                    return p.ExitCode == 0 ? o : null;
                }
            }
            catch { return null; }
        }
    }
}
