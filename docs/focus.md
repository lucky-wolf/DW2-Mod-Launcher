# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---

- Publish dialog: Bundles are auto-detected from the mod folder and shown as a table with total size (head file plus part files)
- Publish dialog: dropped the "(detected)" suffix from the Injected DLLs heading
- Publish dialog: missing bundles listed in mod.json stay visible with a warning, and saving asks before removing them
- Publish dialog: preview image size shown beside its file name; ID / Visibility / Preview columns are 25% / 25% / 50%
- File sizes in the publish dialog show three significant digits with an automatic unit (120 KiB, 3.29 MiB)
- Mod list: narrower #, Enabled and Health columns
- Publish dialog: every DLL without a valid entry point is listed with a red error and the reason (never injected)
- Publish dialog: orange warning when a mod has settings but its DLL has no InitWithOptions(string)
- In-game status line for code mods: lower-left line, click for a per-mod panel (loader-owned, drawn by reflection)
- Loader records every code mod's load result (missing DLL, exception from Init) and shows it in red
- Launcher checks GitHub for a newer release at startup and offers Update Now / Skip This Version / Later
- Self-update downloads with a progress bar (cancellable until install), then swaps its own files via a helper copy and restarts
- About box: Check for Updates button and a "Check for updates at startup" checkbox
- Mods can report features, errors and details to the status line through a copy-paste reflection snippet
- In-game Mods menu: a Mods button inside the game's Esc menu opens a Settings-style dialog listing the launcher and every code mod that added an entry (opt-in, `ModMenu.Add` / `LauncherMenu.cs` copy-paste snippet)
- Mods menu stack: Esc menu > Mods > the mod's own dialog; a mod calls `Show()` to return to Mods, and closing Mods closes the Esc menu
- The launcher's Mods entry shows a small dialog with its version and mod count
- Esc menu: the Random Seed is a button that copies the seed to the clipboard
- Status line and panel take their colors from the game's current theme (warnings and errors stay amber and red)
- The loader log is now `data/Logs/dw2modlauncher.log` in the game folder (next to `SessionLog.txt`)
- About box: "Latest Updates" subheading above the releases link
