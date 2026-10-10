# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/open-pr.py` empties the list (in the PR's last commit) once it is in the PR description, so it never holds over;
`scripts/new-branch.py` also empties it when it creates a new branch.

---
- Choosing a preview image in Edit Properties now always ends with a conformant file in the mod folder, under a name you can change (the original's name, selected, so Enter accepts; Cancel drops the pick and writes nothing): a plain copy for a file from outside the mod, or, for an image over Steam's 1 MiB limit, a smaller copy (first re-encoded at full size, then shrunk in 15% steps keeping its shape, down to 320 px on the long side). The original is never changed.
- Publishing with a preview image over 1 MiB offers Shrink and publish (a smaller copy beside the original, mod.json pointed at it), Publish anyway, or Cancel.
- With the list sorted by load order (#), enabling or disabling a mod moves its row to its new place, keeps it selected and scrolls it to the middle of the list where there is room.
- A local mod's folder now reads `mods\<folder>` (the path under the mods folder) in the details pane and as the subtitle under the source in the list (no longer under the title), instead of the full disk path.
- Mods can require a minimum launcher version with `"minLauncherVersion"` in their `dw2modlauncher.json`
