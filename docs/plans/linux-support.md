# Linux support

Status: planned, not started. Do after the release pipeline (see AGENTS.md "CI & Releases").

## Constraints
- Distant Worlds 2 has no native Linux build; the game always runs under Proton.
- Workshop publish must talk to Steam's Linux client (native `libsteam_api.so`), not a Windows
  `steam_api64.dll` bridged through Wine.
- Current code is Windows-only: Core/Tests/App target `net10.0-windows`; App is WinForms
  (~3,000 lines across `MainForm.*.cs`; Core is ~1,200).

## Options
| | A. Existing WinForms launcher inside Proton | B. Native Linux launcher (recommended) |
|---|---|---|
| Effort | Low: path fixes + docs | High: UI rewrite (Avalonia) |
| Reliability | WinForms under Wine is fragile; needs a .NET Desktop Runtime in the prefix (wine-mono can't) | Solid |
| Workshop | Only works if launcher shares the game's Proton prefix (Proton's `lsteamclient` bridge) | Native `libsteam_api.so` |
| Game launch | Same prefix, easy | `steam -applaunch 1531540 <args>`; injection paths must be Windows-style (`Z:\home\...`) |
| Packaging | Same zip | Separate `linux-x64` zip from CI |

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

## Open questions
- Full Avalonia port vs. option A as a stopgap first.
- Loader manifest paths: confirm the in-game loader resolves `Z:\` paths under Proton.
