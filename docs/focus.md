# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
- Workshop publish now names the exact step and path that failed (e.g. preview image not found, with a case/backslash hint) and logs it, instead of a bare Steam EResult.
- CI no longer builds throw-away release packages on pull requests; packaging runs only on merge to main (or manual dispatch).
- Dev builds (untagged, or with uncommitted changes) show the patch the next release will carry, with "-dev" (counts all `vMAJOR.MINOR.*` tags like `release.py`, not just those reachable from HEAD).
- build.py, run.py and validate.py now fail immediately if the launcher is running (it locks its exe) instead of retrying the copy for ~10s; shared with open-pr.py.
