# HienaDev Dev Tools

Editor tools for every Unity project (Unity 6+). Everything is under the **Tools** menu.

| Tool | Menu | What it does |
|---|---|---|
| Build All | Tools > Build All (Windows + Web) | Name / version / options in one window; builds Windows and Web in one click (it survives the platform switch), zips them as `[Name] v[version] [Platform].zip` in `Build/`, opens them, writes a build report (sizes, biggest assets, files, warnings) and can publish to itch.io with butler. |
| Perf Doctor | Tools > Perf Doctor (easy profiler) | Records a few seconds of Play mode (or a development build) and says in plain words what takes the time: verdict, CPU or GPU bound, biggest costs, hitches, garbage. |
| Audio Trimmer | Tools > Audio Trimmer, or right-click a sound > Trim Audio | Waveform, select, play/loop, fades, volume, pitch, seamless loops, split into regions; saves a new .wav. |
| Screenshot | Ctrl+Shift+K, Tools > Screenshot | Game view screenshots (1x / 2x / 4x) into `Screenshots/` next to Assets. |
| Itch Page Kit | Tools > Itch Page Kit | Cover (630x500), 16:9 screenshots, banner, square or any size, from the running game or from an image in the project (fill or fit with bars). GIF maker: images held for a while, GIFs you already have and live recordings, one after another with fades (an image first = a GIF cover). Can hide the game's UI. Saved to `Build/Itch Page`, listed small with a full-size viewer. |
| Web Size Tester | Tools > Web Size Tester | Sets the Game view to real screen sizes (itch.io embeds, laptop, full HD, ultrawide, phones, tablet); "Check every size" takes a picture of each and lists UI cut off by the edges. |
| Play toolbar | Unity's top toolbar (right-click it to show/hide each) | Play from the first scene, game speed slider, restart the scene, frame-rate cap (15/30/60), Game view screenshot, clear saves with a backup (Unity 6.3+; older: Tools > Play Bar). |
| Scene switcher | Top toolbar, left | Every scene in one dropdown: the build's first, then the rest by folder. |
| Jam timer | Top toolbar, right | Countdown to the deadline with reminders (24 h, 6 h, 2 h, 1 h, 30 min, 10 min); the deadline is saved in ProjectSettings so the team shares it. |
| Notes & TODO | Hierarchy: right-click > Add Note or TODO; Notes & TODO tab: "+ In the scene" (Ctrl+Alt+N) | Notes/TODOs pinned to objects or placed anywhere in the scene (saved in the scene or prefab, so git shares them; left out of builds). Shown in the Hierarchy and as sticky cards in the Scene view (hover for the whole note), with checklists of steps, "for who", priority, colour, and "Look" to jump back to the view it was written from. One list of everything, plus project TODOs. |
| Hierarchy icons | automatic | Each object shows its main component's icon (or the icon you picked: right-click > Choose Icon...). Script icons: right-click a component > Choose Script Icon... |
| Folder colours | Project: right-click a folder > Folder Colour / Folder Icon | Saved in ProjectSettings/HienaDevTools.json (shared through git); common folder names get icons by themselves. |
| Find References | Project: right-click an asset > Find References | Every scene, prefab, material and asset that uses it, plus the objects in the open scene pointing at it; "Nothing uses it" when it's safe to delete. |
| Bulk Rename | Tools > Bulk Rename, or right-click > Bulk Rename... | In plain words: give everything one new name (Rock 01, Rock 02...) or change part of the names (swap a word, take off "(1)", cut letters, add words, capitals); see the new names before renaming; Ctrl+Z for objects. |
| Changelog | Tools > Changelog | New / Changed / Fixed / Removed notes per version in CHANGELOG.md (shared through git); "Suggest from what changed" lists the scenes, prefabs, scripts, art and sounds added, changed or removed since the last version (and git commits); Build All's version bump files the notes; copy as an itch.io devlog. |
| Missing Scripts | Tools > Missing Scripts | Objects in the open scenes and prefabs with missing scripts or broken references; click to go there, one button to remove the missing scripts. |
| Selection history | Toolbar ◀ ▶ and the clock list, Alt+Left / Alt+Right | Back and forward through what you selected, like a browser. |
| Favourites | Tools > Favourites, toolbar ★, right-click > Add to Favourites | Pinned assets and objects: click to select, double-click to open; per person (UserSettings). |

Switches for all of these: Edit > Project Settings > HienaDev Dev Tools.

## Add it to a project

Package Manager > **+** > **Install package from git URL**, then paste `https://github.com/HienaDev/HienaDevTools.git`.
Or add this line to `Packages/manifest.json`:

```
"com.hienadev.devtools": "https://github.com/HienaDev/HienaDevTools.git",
```

To update: Package Manager > HienaDev Dev Tools > **Update**.
