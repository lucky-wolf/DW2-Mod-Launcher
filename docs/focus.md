# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---

- No longer warns about the size of bundle files (they have much larger limits)
- Steam no longer thinks DW2 is running while the launcher is open: Steam API calls (publish, visibility, deleted-item check) run in a short-lived helper process
- Removed the launch-arguments box, the environment-variables box and "Import from Steam" (Steam owns launch options and the environment)
