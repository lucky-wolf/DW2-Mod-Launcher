# Mod profiles on the main page

Status: planned (nothing implemented). Written 2026-10-02 after the launcher showed every mod as disabled on a
machine whose game uses named profiles.

## Problem
- DW2 has its own named mod profiles and the launcher ignores them. Nothing in the repo reads
  `currentProfile.txt`; the launcher only reads/writes the live `mods.json`.
- The launcher's own profile feature (`ProfileStore`, Settings tab: save / apply / delete) is parallel work to what
  DW2 already does, stores its profiles elsewhere (the launcher's data folder), and shows nothing for a user whose
  profiles live in the game folder.
- Profiles are the point of the app, not a setting, so a Settings-tab section is the wrong place for them.
- A launcher write to `mods.json` made without knowing the active profile can leave the live list and the profile
  file out of step. One observed incident: all mods disabled, the real list intact only in the profile file.

## What the game folder looks like (observed, not documented by the game)
In `<game>\mods\`:
- `mods.json` - the live order, `{"order":["steam/<id>","mods/<folder>", ...]}`. This is what DW2 loads.
- `currentProfile.txt` - the active profile's display name, e.g. `XL (Local)`.
- `mods.<name>.json` - one file per profile, same format. The name is escaped: a space becomes `_20_`
  (`XL (Local)` -> `mods.XL_20_(Local).json`, `XL Only (Steam)` -> `mods.XL_20_Only_20_(Steam).json`);
  other escapes are unknown (check the game's `Launcher.exe` behaviour for characters like `/`, `.`, non-ASCII).
- The game's own `Launcher.exe` is in the game folder; the user confirms DW2 understands the active profile.

## Must be verified before writing any code
These decide the design; test them against the real game (back up `mods\` first):
1. Who copies between `mods.json` and `mods.<name>.json`, and when: on profile switch only, on game exit, on every
   launch? Does editing `mods.json` get saved back into the active profile file, or overwritten from it?
2. The full name-escaping rule (round-trip a name with spaces, parentheses, `.`, `/`, non-ASCII).
3. What the game does when `currentProfile.txt` names a profile with no file, or `mods.json` disagrees with the
   profile file.

## Design
Source of truth: the game's own files. The launcher's `ProfileStore` is retired (see Migration).

UI: a profile control in the main page header area, next to the nav buttons, not under Settings:
- Dropdown listing every `mods.<name>.json` profile (unescaped names), with the active one (from
  `currentProfile.txt`) selected. Falls back to "Default" / the live `mods.json` when no profile is active.
- Selecting another profile switches to it (confirm if the live list has unsaved differences from the active
  profile; refuse while the game is running).
- Menu beside the dropdown: **Save** (write the live list into the active profile), **Save as / New...**,
  **Delete**, **Restore from backup...** (list of timestamped backups for that profile).
- A small "modified" marker when the live `mods.json` differs from the active profile file.

Core (UI-independent, unit tested; mirrors `ModOrderStore`):
- `GameProfileStore`: list profiles, read/write `currentProfile.txt`, escape/unescape names, read/write
  `mods.<name>.json`, delete. Never throws on a missing/bad file; reports it.
- Every write goes through the same atomic temp-file + timestamped-backup path as `ModOrderStore.Write`
  (newest 20 kept, already implemented for `mods.json`). Backups are per file, so restoring is per profile name:
  `mods.<name>.json.<timestamp>.launcher_backup`.
- Enable/disable and drag-reorder write `mods.json` and, if the verified game behaviour requires it, the active
  profile file too, in one operation.

Safety rules (these are what the incident would have needed):
- If `mods.json` is missing or empty but the active profile file has entries, do not offer to write; offer to load
  the profile instead, and say so in the status bar.
- Never overwrite a non-empty list with an empty one without an explicit confirm.
- Refuse all profile writes while DW2 is running (reuse `GameProcess`).

## Migration
- Launcher profiles already saved by `ProfileStore` (launcher data folder): on first run offer to import them as
  game profiles, then remove the Settings-tab profile section and its code (`SettingsViewModel` profile commands,
  `ProfileStore`, WinForms equivalents go with WinForms).
- Per-profile launch arguments (`ModProfile.ManualLaunchArguments`) have no game-side home. Decide: keep a
  launcher-side `profile name -> launch arguments` map, or drop the feature.
- Snapshots (Settings tab) stay for now; revisit once profile backups exist, since they overlap.

## Steps
1. Verify the three open behaviours above against the real game; record results here.
2. `GameProfileStore` + tests (escaping, round-trips, missing/bad files, backups).
3. Main-page profile dropdown + Save / Save as / Delete / Restore, with the safety rules.
4. Rewire enable/disable and reorder to keep profile and live file consistent.
5. Import launcher profiles; remove the Settings profile section and `ProfileStore`.
6. Update AGENTS.md and the README (both languages).

## Open questions
- Is a "Default" profile (no `currentProfile.txt`) a real game concept, or just "whatever `mods.json` holds"?
- Should switching profiles also change which mods the launcher's own `SelectedMods` setting marks enabled, or does
  that setting go away in favour of the profile file?
