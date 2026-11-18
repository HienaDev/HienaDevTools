# HienaDev Dev Tools

Editor tools for every Unity project (Unity 6+). Everything is under the **Tools** menu.

| Tool | Menu | What it does |
|---|---|---|
| Build All | Tools > Build All (Windows + Web) | Name / version / options in one window, a pre-build check of the usual mistakes (with Fix buttons, run before every build) and quick player settings (icon, cursor, window, web size); builds Windows and Web in one click (it survives the platform switch), zips them as `[Name] v[version] [Platform].zip` in `Build/`, opens them, writes a build report and can publish to itch.io with butler. |
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
| Scene Gallery | Tools > Scene Gallery, or the toolbar's scenes list | Every scene as a picture (taken when you save it, so the gallery never opens scenes); click to open. |
| Asset Size Watcher | Tools > Asset Size Watcher | Warns when an import is much bigger than it needs to be (4K textures, uncompressed textures or sounds, long sounds unpacked in memory, readable models, huge videos) with one-click fixes; can check the whole project. |
| Auto Fit UI | Tools > Auto Fit UI; right-click a Canvas > Make It Fit Every Screen | Canvas UI that fits any screen, in builds too: scales without cutting anything off, keeps out of phone notches, re-pins each element to its edge or corner (keeping its look), big panels shrink (or grow on upright phones) to fit. One click in the editor (before/after previews on 6 screens), an AutoFitCanvas that fits itself when the game starts, or "Fit every Canvas in the game" for no setup at all. "Always 16:9" switch: the game keeps one shape (16:9, 16:10, 4:3, 21:9, 9:16, 1:1 or any custom size like 2560 x 1080) on every screen with black bars, and the UI stays inside, in Play mode and builds; change it while the game runs with AspectLock.Lock(16, 9) / AspectLock.Unlock() or the "+ Add a screen shape picker" button in the window (a ready-wired dropdown in the selected panel) or the Aspect Lock Control component (hook a Button, Toggle or Dropdown to it, remembers the choice). UI Toolkit: AutoFitDocument. Example scene included. |
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
