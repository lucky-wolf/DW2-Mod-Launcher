# Long description lives in description.bbcode (BBCode is what Steam descriptions are, so the extension says so)

Status: done (needs a hands-on check of the file watcher in the running launcher).

## Goal

A mod has two descriptions: the **short description** (mod.json `shortDescription`, what DW2 shows) and the **long
description** (what goes on the Steam page). The long one is an implementation detail modders shouldn't manage:
it is always the file `description.bbcode` in the mod root. mod.json's `description` and `descriptionFile` keys are
legacy and go away.

## Rules

- The long description is a text file in the mod's content root: the one mod.json `descriptionFile` already names (kept as-is, never forced to rename), otherwise `description.bbcode`.
- The properties/publish dialog shows one description box (no radio buttons, no file path), reflecting that file.
- Migration is automatic when the dialog opens/saves: if that file does not exist, its first content comes from
  the legacy inline mod.json `description`.
- Conflict (file exists AND mod.json has `description`): the file wins; the mod.json value is discarded.
- Save / publish writes the box to `description.bbcode`, then removes the inline `description` from mod.json (`descriptionFile` stays when custom, is dropped when it is the default)
  (the file is written first, so a failed write never loses text).
- "Open in editor" button opens `description.bbcode` (creating it from the box if missing). The dialog watches the file and
  reflects external edits into the box.
- The launcher's own details pane reads `description.bbcode` by convention (falling back to a legacy `descriptionFile`).
- "Open Steam Page" and a cloud-download "Get from Steam" icon button (published mods only) sit on the same row.

## Steps

- [x] Get from Steam / Open Steam Page buttons
- [x] Core: `ModDescriptionFile` (path, load with legacy migration + file-wins, save)
- [x] Core: `ModPublishMetadataEditor.Write` drops legacy keys; `ResolveSteamDescription` goes through `ModDescriptionFile`
- [x] Core: `ModScanner` reads `description.bbcode` by convention for the details pane
- [x] Dialog VM: single description, file-backed; remove radio buttons / browse / file-path members; open-in-editor command; FileSystemWatcher with debounce; IDisposable
- [x] Dialog XAML + view: remove radios/file row, add editor button, dispose on close
- [x] Strings (EN/JA): remove dead keys, add new ones
- [x] Tests: update/replace the old descriptionFile tests, add migration + conflict + save tests
- [x] focus.md entry; AGENTS.md mention if conventions shift

## Follow-up: earlier default name

`description.txt` was briefly the default. A mod with a `description.txt`, no `description.bbcode` and no `descriptionFile` key keeps using the `.txt`; saving writes `"descriptionFile": "description.txt"` so it is never orphaned. Once a `description.bbcode` exists it wins.
