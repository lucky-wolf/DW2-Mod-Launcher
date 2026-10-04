# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- File-conflict detection now only considers files the game actually loads: `*.xml`, `*.atlas`, `music\*.mp3`, `dialog\*.txt`, `galactopedia\*.txt`, and root `GameText.txt` / `Hints.txt` / `SystemNames.txt`. This ends false conflicts on `settings.json`, `settings.schema.json`, docs, DLLs, textures and other mod-private files.
- The mod list remembers its sort column and direction between launches (stored in `launcher_settings.json`); dragging a row to reorder switches it back to manual load order.
- Code-mod injection DLLs are now discovered automatically (a public static `Entry` class with `Init()`/`InitWithOptions(string)`, read from DLL metadata without loading) instead of a non-standard `launcher` block in `mod.json`; an optional `dw2modlauncher.json` still overrides.
- Mod settings dialog: two-column layout, descriptions as row tooltips, `folder`/`filename` field types with a browse button and validation, and an icon toolbar (configure, create, properties, delete, publish).
- Remembers the last window position you chose and restores to that
- Improved the mod list and mod editor toolbars to be all icons
- Added a configure icon to that toolbar