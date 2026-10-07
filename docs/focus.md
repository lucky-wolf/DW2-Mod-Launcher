# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---

- Mod config dialog now distinguishes whether you've modified anything or not and ties the save button to that awareness
- Properties dialog: Can Sync Description only without updating your whole mod
- Properties dialog: Save only enabled when something changed; description toolbar reordered with icons, plus an upload-description-only button (writes the description file, touches nothing else on Steam); smaller bundles box
- Mod details pane re-reads a local mod's files each time, so it never shows stale info
- Remembers the selected mod across restarts and scrolls it to the centre of the list
