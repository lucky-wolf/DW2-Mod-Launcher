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
- Mod list toolbar: "Create Mod..." is now "Create", with a new "Publish" button beside it (same as Publish to Workshop); both have descriptive tooltips.
- Source column: "Game Mod Folder" is now "Local".
- Message/error dialogs have a copy-to-clipboard icon and an open-log (notebook icon) button (bottom left; the latter only when a log file exists) that opens DW2ModLauncher.log with the OS handler.
- Publish dialog reads the item's current visibility from Steam (replacing "Unchanged" and preselecting it); "Unchanged" remains only if Steam can't be queried.
- Publish dialog edits shortDescription, descriptionFile (browse + open-in-editor buttons) and description, all stored in mod.json; the description hint sits under its title.
- "Create" now opens the properties dialog for the new mod, and a new "Edit Properties..." context-menu item opens it for any local mod (saves to mod.json; visibility shown read-only from Steam when available).
- Publish dialog: hints under Short description and Description file, and a "Replace the Steam description when publishing" checkbox in the footer beside Publish (default on for a first publish, off for updates); the description comes from either a description file or typed text (radio buttons; saving drops the unchosen one), and the text box is half as tall.
- Publish dialog: the "this publishes via your Steam client" note is now the Publish button's tooltip instead of text at the bottom.
- Mod list toolbar: an edit (pencil) button between Create and Publish opens the same Edit Properties dialog as the context menu.
- Browsing for the preview image or description file only ever stores a path inside the mod folder; a file from elsewhere is copied in after a Yes/Cancel prompt (never overwriting a different file).
- Details panel: image, name + version, "Source: Local\<folder>" and "Workshop ID: <id or Unpublished>", a divider, then the description; the redundant Source/State lines in the text are gone.
- Publish success dialog opens the item in the Steam client (like "Open Steam Page"), not the browser.
- Dev builds (untagged, or with uncommitted changes) show the patch the next release will carry, with "-dev" (counts all `vMAJOR.MINOR.*` tags like `release.py`, not just those reachable from HEAD).
- build.py, run.py and validate.py now fail immediately if the launcher is running (it locks its exe) instead of retrying the copy for ~10s; shared with open-pr.py.
- Local mods whose Workshop item was deleted on Steam (Steam answers k_EResultFileNotFound) have their stale "workshopId" erased from mod.json at startup, on Refresh, and before publishing, so the list shows the truth and the mod publishes as new.
- Profile toolbar buttons (Save, Save As, Rename, New, Delete) now have tooltips that say they act on the profile.
