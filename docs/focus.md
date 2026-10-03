# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- new-branch.py re-prompts when the branch name is invalid or taken, accepts "main" to stay on main, and deletes local branches that were rebase- or squash-merged (verified via git cherry or a merged GitHub PR) instead of keeping them.
- Settings, profiles, Workshop backups and the log now live in the per-user config folder (`%AppData%\DW2ModLauncher` / `~/.config/DW2ModLauncher`) instead of next to the binary; existing settings/profiles are moved over on first run.
