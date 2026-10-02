# Linux support

Status: WinForms removed 2026-10-02 (Windows and Linux both ship the Avalonia launcher); references to the WinForms app below are historical. In progress (steps 1-2 done, see Progress). Release pipeline is done (see AGENTS.md "CI & Releases").

## Constraints
- Distant Worlds 2 has no native Linux build; the game always runs under Proton.
- Workshop publish must talk to Steam's Linux client (native `libsteam_api.so`), not a Windows
  `steam_api64.dll` bridged through Wine.
- Current code is Windows-only: Core/Tests/App target `net10.0-windows`; App is WinForms
  (~3,000 lines across `MainForm.*.cs`; Core is ~1,200).

## Options
|             | A. Existing WinForms launcher inside Proton                                                  | B. Native Linux launcher (recommended)                                                   |
| ----------- | -------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Effort      | Low: path fixes + docs                                                                       | High: UI rewrite (Avalonia)                                                              |
| Reliability | WinForms under Wine is fragile; needs a .NET Desktop Runtime in the prefix (wine-mono can't) | Solid                                                                                    |
| Workshop    | Only works if launcher shares the game's Proton prefix (Proton's `lsteamclient` bridge)      | Native `libsteam_api.so`                                                                 |
| Game launch | Same prefix, easy                                                                            | `steam -applaunch 1531540 <args>`; injection paths must be Windows-style (`Z:\home\...`) |
| Packaging   | Same zip                                                                                     | Separate `linux-x64` zip from CI                                                         |

## Plan (B, incremental)
1. Put platform-specific code in Core behind small interfaces: Steam path discovery
   (`SteamLocator`: Linux reads `~/.steam/steam` + `libraryfolders.vdf`), user-data root
   (`UserDataRoot`), open-folder (`xdg-open`), game launch. Multi-target Core as plain `net10.0`.
2. Make the Steamworks wrapper platform-aware. Facepunch.Steamworks 2.3.3 doesn't obviously bundle
   `libsteam_api.so`; confirm, or switch to Steamworks.NET (MIT, Linux support). Verify native
   publish from Linux with a small console harness before any UI work.
3. Port the UI to Avalonia (mostly mechanical, `MainForm` is already split by concern). The same app
   could then ship on Windows too.
4. Add a `linux-x64` job to `release.yml`.

## Progress
- Step 1 DONE: Core/Tests are plain `net10.0`; `SteamLocator` is cross-platform; `GamePaths` (`Z:\` mapping),
  `PlatformShell` (xdg-open), `GameLauncher` (`steam -applaunch`) added.
- Step 2 DONE (verified 2026-10-02): `SteamworksNetModPublisher` created and updated a real Workshop item
  from Linux against the native Steam client. Windows moved to the same publisher on 2026-10-02 (see below).
- Step 3 phase A DONE. Non-UI logic now lives in Core (tested): settings/profiles/snapshots/path detection
  (`LauncherSettingsStore`, `ProfileStore`, `SnapshotStore`, `PathDetector`, `FileNames`); mod order and
  conflicts (`ModOrderState`/`ModOrderStore`, `ConflictAnalyzer`, `LaunchDiagnostics`, `ModHealth`, `GameProcess`);
  Workshop updates and enabling (`WorkshopUpdateService`, `ModLibrary`); launch (`GameLauncher`).
  The WinForms `MainForm.*` partials are now thin glue over these. The WinForms app was not run after the refactor
  (Linux-only session) - it is being replaced, so that is accepted.
  Linux fixes made on the way: conflict file hashing was case-sensitive-broken (lowercased paths), game-process
  detection now matches Wine's truncated name, diagnostics check the host path of injected DLLs (not `Z:\`).
- Step 3 phase B DONE (2026-10-02): `src/DW2ModLauncher.Avalonia` (Avalonia 12.1.3, MVVM-lite, runs on Linux):
  DW2 theme, header/nav/status bar, language switcher (bindings via `L[Key]`), read-only mod list, full Settings
  tab (paths, launch args, profiles, snapshots), Play (diagnostics + conflict prompts, `steam -applaunch` on
  Linux). Run: `dotnet run --project src/DW2ModLauncher.Avalonia`.
- Step 3 phase C DONE (2026-10-02): Mods tab - sortable list with thumbnails, enable toggle, health colours, drag-reorder
  (writes `mods.json`), details panel with problems callout, open folder/docs, Workshop update check (also on startup).
- Step 3 phase D DONE (2026-10-02): mod settings editor (all field kinds), publish dialog with a Visibility dropdown
  (Private by default for new items, "Unchanged" for updates), success dialog.
  Verified end-to-end on Linux in a sandbox (fake game folder, own config dir): toggle, drag-reorder, sort,
  settings save, and a real private Workshop publish through the dialog (item deleted afterwards).
- Step 3 phase E (Linux part) DONE: native libs + Loader copied into the Avalonia output; self-contained
  `linux-x64` publish works and starts; CI runs on ubuntu too; `release.yml` has a `release-linux` job (tar.gz).
- Step 4 DONE in the workflows (untested until the first run on GitHub).

## Step 3 breakdown (Avalonia)
Findings from reading `MainForm.*.cs`: logic, state and widgets are entangled (scan/order/conflict/profile/
workshop code reads and writes `ListView`/`TextBox` directly), the mod list is owner-drawn, dialogs are built in
code with pixel coordinates, and there is a manual DPI-scaling pass (Avalonia does this natively). So this is a
rewrite of the view layer plus extracting the logic, not a control-for-control translation. Dialog count is small
(mod settings editor, publish + success, a few message boxes); `BackgroundWorker` is used twice.

Approach: new `DW2ModLauncher.Avalonia` project beside the WinForms App; WinForms stays shippable until parity,
then it is deleted. Phases, each runnable/testable on Linux:
- A. Extract non-UI logic from `MainForm` into Core (settings load/save, profiles + snapshots, mod order
  read/write, conflict analysis + launch diagnostics, workshop update check, mod scan/refresh, selection state),
  behavior-preserving, unit tested. WinForms calls the extracted code. Windows regression risk: verify on Windows
  before merging.
- B. Avalonia shell: theme (DW2 palette), header, nav, status bar, language switcher, Settings tab.
- C. Mods tab: DataGrid (sort, enable toggle, health, load order), details panel, drag-reorder.
- D. Dialogs: mod settings editor, publish (+ native folder/file pickers), conflict/diagnostic prompts.
- E. Launch + packaging, then remove WinForms and add the CI `linux-x64` job (step 4).

## Steam library findings
- Facepunch.Steamworks 2.3.3 is Windows-only (`Facepunch.Steamworks.Win64.dll`).
- NuGet Steamworks.NET 2024.8.0 (SDK 1.60) does NOT work with the SDK 1.65 `libsteam_api.so`
  (`EntryPointNotFoundException`). Wrapper and native lib must come from the same Steamworks.NET revision, so the
  matched pair is vendored in `third_party/steamworks/` (see its README). Wrapper must be built with the
  `OSX-Linux` configuration for Linux struct packing.
- `steam_appid.txt` next to the binary is required; the `SteamAppId` env var alone is not enough.
- Windows DONE (2026-10-02): `win-x64` wrapper + `steam_api64.dll` vendored from the same revision; a private throwaway
  item was created and deleted on Windows through the Steamworks.NET publisher. Facepunch, its extern alias and
  `SteamworksModPublisher` are removed. Core references the win-x64 wrapper at compile time only; each executable
  copies the matching OS build.

## Follow-ups
- Publish visibility: both publishers hardcode Public today. Add `Visibility` (Public/FriendsOnly/Private/Unlisted)
  to `ModPublishRequest`, default Private for new items, "unchanged" (skip `SetItemVisibility`) for updates, and a
  dropdown in the publish dialog that remembers the mod's last choice. Behavior change on Windows too.
- Add a "Delete" option (with are you sure) for any local mods
- Add a "Create" option to create a new local mod (takes a name & autosanitize for a folder name to place it in)

## Open questions
- Full Avalonia port vs. option A as a stopgap first.
- Loader manifest paths: confirm the in-game loader resolves `Z:\` paths under Proton.

