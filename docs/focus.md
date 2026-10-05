# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- Settings schemas can mark fields `"localOnly": true`; the Configure window hides them for Workshop mods and shows them for local mods under a separator, so authors keep their debug controls without exposing them to players. Hidden fields are still passed to the mod with their defaults and their saved values are left untouched.
- Removed the name-based "duplicate installation" warning (it fired for every author with a local and a Workshop copy and missed short names like XL). Replaced with a Workshop-ID check that flags a mod only when two copies of it are actually enabled, which also catches DLL-only mods.
