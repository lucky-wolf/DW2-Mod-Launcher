# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- README is now bilingual as separate files (README.md English, README.ja.md Japanese, linked to each other), restoring the Japanese README and bringing it up to date; validation now fails if a *.ja.md drifts from its English original.
- new-branch.py re-prompts when the branch name is invalid or already taken instead of aborting.
- new-branch.py recognises stashes left by an earlier run, offers to restore them onto the new branch (also when branch creation is skipped), and carries over only the docs/focus.md entries you added instead of conflicting on that file.
