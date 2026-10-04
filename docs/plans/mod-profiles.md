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

### Verified results
- **Nothing present (2026-10-02):** with `currentProfile.txt` and every `mods.<name>.json` removed from `mods\`, starting
  the game recreated only `mods.json` as `{"order":[]}`. It created no `currentProfile.txt` and no profile file, so the
  game has no "Default" profile: no active profile is just a live `mods.json`. (Answers the open question below and
  part of item 3.) `mods.None.json` in the user's folder is a profile they made by hand, not something the game writes.

- **Model as the user recalls it (2026-10-02, "IIRC", not yet tested):** selecting a profile copies that profile's
  contents into the base `mods.json`. The live list is just `mods.json` and is unnamed; you save it under any name
  to make a profile, and select that name later to load it. `currentProfile.txt` is then likely only the game's
  memory of the last profile selected, not a binding between the live list and a profile file. The launcher's
  profile UI follows this model; the "modified" marker means "live list differs from the last selected profile".
- **Confirmed by the user (2026-10-02):** editing the active list never saves it to the named profile in the game; saving
  is a manual step. The launcher deliberately does better than the game: it keeps the live list associated with the
  selected profile (`currentProfile.txt`) until the user chooses "Don't save" on the unsaved-changes prompt (Play or
  exit). That keeps the edited list but sets the active profile to blank (disconnected), and Save As can name it
  again. Switching profiles asks to discard instead, since a different profile replaces the list.
- **Terminology (2026-10-02):** DW2 calls the blank/unnamed/detached state "(default profile)". The launcher uses that
  term everywhere (the drop-down shows "(default profile)" as its first entry when `currentProfile.txt` is blank;
  picking it detaches the live list; Save As / Rename also offer it). Internally the profile name stays `""`.

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
- Snapshots (Settings tab) were removed (2026-10-02): profiles cover the mod order, so they were redundant.

## Idea: a launcher-side companion file per profile (not built)
The game's profile files hold only the load order, so anything launcher-specific needs its own home: a paired file in
the launcher's user data (`%AppData%\DW2ModLauncher`, keyed by the profile name) that is read and written alongside the
game's `mods.<name>.json`. It would be optional and best-effort: a profile with no companion file works exactly as it
does today.

What the old `ProfileStore` actually saved (`ModProfile`: `Name`, `Order`, `ManualLaunchArguments`, `Versions`). Only
the first two overlap with the game's file; the other two are the launcher-specific data a companion file could hold:
- `ManualLaunchArguments` - extra command-line arguments for that profile (the Settings tab has one global value).
- `Versions` - a map of mod token -> version at save time, used on Apply to warn "mod versions differ from the
  saved profile". Dropped for now; a companion file is where it could come back.
- It did **not** store which DLLs to load. That is not profile data at all: code mods are already self-describing
  (see [DLL Injection.md](../DLL%20Injection.md)). Each mod declares `dll` + `entryPoint` under `injection` in its own
  `dw2modlauncher.json` (or `mod.json`; both are read by `ModScanner.ReadModInfo`), and `LoaderManifestBuilder` builds the
  loader manifest from whichever mods are enabled. So the profile's order decides which DLLs load, and nothing
  DLL-related needs saving per profile.

On inferring DLLs without any declaration (scan for `*.dll` in the mod folder): it would avoid a file, but a mod can
ship helper or dependency DLLs that are not loader entry points, and the loader needs the entry-point name anyway, so
a small explicit `dw2modlauncher.json` stays safer. If we want less ceremony, the fallback could be: no `injection` block and
exactly one DLL with a conventional entry point -> offer to generate the `dw2modlauncher.json`, not to guess silently.

Open: decide whether a companion file is worth it before building any of it. If `ManualLaunchArguments` per profile is
not wanted, there may be nothing left to put in it.

## Steps
1. Verify the three open behaviours above against the real game; record results here.
2. `GameProfileStore` + tests (escaping, round-trips, missing/bad files, backups).
3. Main-page profile dropdown + Save / Save as / Delete / Restore, with the safety rules.
4. Rewire enable/disable and reorder to keep profile and live file consistent.
5. Import launcher profiles; remove the Settings profile section and `ProfileStore`.
6. Update AGENTS.md and the README (both languages).

## Open questions
- ~~Is a "Default" profile (no `currentProfile.txt`) a real game concept?~~ Answered: no, it is just whatever
  `mods.json` holds (see Verified results). The launcher shows a blank profile in that case.
- Should switching profiles also change which mods the launcher's own `SelectedMods` setting marks enabled, or does
  that setting go away in favour of the profile file?
