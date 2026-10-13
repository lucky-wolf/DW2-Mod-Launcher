# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/open-pr.py` empties the list (in the PR's last commit) once it is in the PR description, so it never holds over;
`scripts/new-branch.py` also empties it when it creates a new branch.

---

- Did a sweep of error handling an improved a great many places
- We now assume JSON with Comments for settings.schema.json files
- Removed the dependency and load-order fields the launcher read from a mod's mod.json (Required, Optional, Incompatible, LoadBefore, LoadAfter): mod.json belongs to the game; launcher settings go in dw2modlauncher.json
- Mods can declare `dependencies` in dw2modlauncher.json (required or optional, a minimum version, load order the launcher enforces), and hand data to each other over named channels (`provides` / `receives`): the launcher merges every provider's JSON files in load order and gives the receiving DLL the result in a separate InitWithOptions argument
- The details pane lists what a mod provides, receives and depends on, and which enabled mods are on the other end (nothing is shown for a mod that declares none)
- Mods can declare `provides` and `receives` to have the mod loader automatically accumulate and transport data from providers to receivers for a named interface
- The mod list subtitle and the details pane's source line now show just the mod's folder name or "Steam Workshop"
- Mods record an `author` in dw2modlauncher.json: set your name in Settings and new or published mods are stamped with it
- The mod list has a sortable Author column
- The enabled checkbox and load-order number share one column (no number = not enabled), sorted by load order
- The details pane can be collapsed with an arrow beside it and brought back with the same arrow
- A file a mod promised on a data channel but the launcher could not hand over is now an error on the in-game status widget, not only a warning before launch
- The in-game status widget no longer overflows with many mods: a compact list with a details flyout per mod, paged to fit the screen
- A mod can translate its settings dialog with `settings.schema.<language>.json` sidecars (labels, descriptions, headings, option text; anything missing shows the author's text)
- The in-game status widget and Mods menu are translated: they follow the enabled translation mod's declared language, or the launcher's language by a setting
- Every code mod now receives the in-game language as a `Language` option (`en`, `ja`, `ru`), so a mod can translate its own status-widget text
- We now track whether a local mod is modified or up to date with steam
- You can change the publish status of your mods without re-publishing them
- We show the published status of your local mods on the list and in details
