# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- Linux support (Avalonia launcher, game under Proton, Workshop publish via Steamworks.NET): done, plan archived
- Mod profiles now use DW2's own named profiles; the blank/detached state is shown as "(default profile)", as the game calls it
- Removed the Snapshot feature (profiles cover the mod order, and snapshots copied whole mod folders)
- Main window: ▶ Play button with tooltips, an "All" button (enables everything, with an "Analyzing Conflicts..." overlay while it works), Clear no longer asks to confirm, "Create Mod..." dialog cleanup, health shown as a coloured word
- Details panel: dropped the folder path and redundant "no conflicts / up to date" lines, and a "Select a mod" placeholder replaces the empty black box
- Mod descriptions follow mod.json's `descriptionFile` (long text) > `description` > `shortDescription`
- Conflict detection no longer flags `description.*` files
- Workshop publish no longer overwrites the Steam description when mod.json has no `description` (and no longer writes an empty one into mod.json)
- Settings dialog sizes itself to its contents; subtitle credits Serge and Mordachai
