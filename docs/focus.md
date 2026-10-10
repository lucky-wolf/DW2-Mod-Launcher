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
- Shrinking an oversized preview image that is already in the mod now replaces it in place (original to the recycle bin) instead of leaving a heavy copy beside it. "Shrink and publish" does the same (same name, original to the recycle bin); the launcher no longer makes "(resized)" files.
- A mod whose mod.json names a preview image that doesn't exist now shows no preview, instead of silently showing some other image from its folder.
- AGENTS.md now forbids defensive programming: no silent fallbacks or swallowed errors that hide a problem.
- AGENTS.md says to catch errors at failure points and report them to the user through a real channel (UI message, or the in-game status line), never to crash or hide them.
- A preview image picked from outside the mod is offered under the mod's own name (it is the cover art) rather than the source file's name.
- The publish / edit-properties dialog warns about loose image files in the mod folder that the mod doesn't use (only the preview is) but that would be uploaded to Steam; Save and Publish offer to move them to the recycle bin, and carry on regardless if declined.
- The preview image picker remembers the folder art was last picked from (used when the image field is blank); a field that already names a file opens in that file's folder instead.
- Saving a mod now writes `descriptionFile` into mod.json even when it is the default `description.bbcode` (as long as the file exists), so the file's location is explicit in the mod.json contract; Edit Properties enables Save for an older mod whose mod.json lacks the key, so one click brings it up to date.
- New setting "Description file type" (Settings): the extension of the description file for a mod that names none (default `.bbcode`; pick `.txt` or type your own, a leading dot is added). Mods that already name or have a description file keep it.
