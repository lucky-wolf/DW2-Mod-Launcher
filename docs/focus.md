# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/open-pr.py` empties the list (in the PR's last commit) once it is in the PR description, so it never holds over;
`scripts/new-branch.py` also empties it when it creates a new branch.

---
- The loader now installs its XML patch, font, text-file and title-lookup hooks before it calls the code mods' entry points, so a mod with a slow start-up can no longer make the game read its data before the patches.
