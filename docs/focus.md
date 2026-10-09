# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---

- Publish dialog: compact +/- buttons for major, minor and patch version
- Publish dialog: per-mod "When Publishing..." choice (do not increment / patch / minor / major), kept in launcher_settings.json
- No longer warns about the size of bundle files (they have much larger limits)
- Steam no longer thinks DW2 is running while the launcher is open: Steam API calls (publish, visibility, deleted-item check) run in a short-lived helper process
- Removed the launch-arguments box, the environment-variables box and "Import from Steam" (Steam owns launch options and the environment)
- Mod settings files are always complete: keys missing from the saved file (new mod, or settings added by a mod update) are filled with schema defaults and saved on rescan, enable, config-dialog open and launch, so Save only lights up for real user edits
- Localization Mod shortcut also collects army and fleet template names and copies the game's text files (GameText.txt, Hints.txt, SystemNames.txt, dialog, Galactopedia) into the new Mod
- Loader serves a mod's Hints.txt, dialog/*.txt and Galactopedia/**/*.txt, which the game reads straight from its data folder (translations of them were ignored before)
- Mods can add or replace Galactopedia articles by file name; dw2modlauncher.json "galactopedia": "replace" drops the game's and earlier mods' articles
- XML patches now also apply to the tour (tutorial) items, which the game opens outside the normal data load
- Tutorial tours and Galactopedia articles are found by their translated title even when the data keeps the English title
- XML patches follow the mod load order: a patch no longer touches data files of mods loaded after it (those override it, as with normal data files)
- Launch warnings for font mods: a declared font whose bundle file is missing is dropped with a warning, and a font replaced by a later mod's font is named
- In-game status line now shows when a loader feature (XML patching, fonts, text files, title lookup) cannot install, and summarizes XML patch errors/warnings
- Text-file hooks reject unrelated File calls cheaply, log which API served each file, and are covered by tests for Windows and Proton/Linux path shapes
