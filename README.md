# HienaDev Dev Tools

Editor tools for every Unity project (Unity 6+). Everything is under the **Tools** menu.

| Tool | Menu | What it does |
|---|---|---|
| Build All | Tools > Build All (Windows + Web) | Name / version / options in one window; builds Windows and Web in one click (it survives the platform switch), zips them as `[Name] v[version] [Platform].zip` in `Build/`, opens them, writes a build report (sizes, biggest assets, files, warnings) and can publish to itch.io with butler. |
| Perf Doctor | Tools > Perf Doctor (easy profiler) | Records a few seconds of Play mode (or a development build) and says in plain words what takes the time: verdict, CPU or GPU bound, biggest costs, hitches, garbage. |
| Audio Trimmer | Tools > Audio Trimmer, or right-click a sound > Trim Audio | Waveform, select, play/loop, fades, volume, pitch, seamless loops, split into regions; saves a new .wav. |
| Screenshot | Ctrl+Shift+K, Tools > Screenshot | Game view screenshots (1x / 2x / 4x) into `Screenshots/` next to Assets. |

## Add it to a project

Package Manager > **+** > **Install package from git URL**, then paste the repository's URL (ending in `.git`).
Or add this line to `Packages/manifest.json`:

```
"com.hienadev.devtools": "https://github.com/<you>/<repo>.git",
```

To update: Package Manager > HienaDev Dev Tools > **Update**.
