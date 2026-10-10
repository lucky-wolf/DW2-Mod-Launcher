# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/open-pr.py` empties the list (in the PR's last commit) once it is in the PR description, so it never holds over;
`scripts/new-branch.py` also empties it when it creates a new branch.

---

- New local mods keep spaces in their folder name ("My Mod" instead of "My_Mod"); only characters the file system rejects are replaced.
- Shrinking an oversized preview image that is already in the mod now replaces it in place (original to the recycle bin) instead of leaving a heavy copy beside it.
- A preview image picked from outside the mod is offered under the mod's own name (it is the cover art) rather than the source file's name.
- The publish / edit-properties dialog warns about loose image files in the mod folder that the mod doesn't use (only the preview is) but that would be uploaded to Steam; Save and Publish offer to move them to the recycle bin, and carry on regardless if declined.
- The preview image picker remembers the folder art was last picked from (used when the image field is blank); a field that already names a file opens in that file's folder instead.
