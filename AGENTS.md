# AGENTS.md

Collective directives for anyone (human or AI) working in this repo. This file is the shared source of truth for how the project should be approached — keep it current as conventions and scope evolve.

## Project summary

DW2 Mod Launcher is an unofficial, open-source (MIT) community launcher/mod manager for **Distant Worlds 2**. It is a hobby project developed cooperatively; contributions, forks, and continued community development are explicitly welcomed (see [README.md](README.md)).

Current features (implemented): scanning/enabling/disabling MODs from Steam Workshop and the local MOD folder, duplicate detection, file-conflict checks between enabled MODs, Workshop update checks, MOD info/README/tool discovery, INI editing, per-MOD launch args, EN/JP UI.

Planned/target scope (in progress or aspirational — confirm current state before assuming these exist):
- Load order selection for enabled MODs
- Merging selected MODs into a dedicated, curated "merged mod" output folder for use in-game
- AI-assisted review and resolution of MOD conflicts
- Support for MODs requiring C# code injection: launching DW2 with the correct CLI args to load the required DLL(s)

## Repo structure

- [DW2ModLauncher.sln](DW2ModLauncher.sln) — solution; open this in VS Code (with C# Dev Kit) or Visual Studio
- [src/DW2ModLauncher.Core/](src/DW2ModLauncher.Core/) — UI-independent logic, safe to unit test:
  - `Models/` — plain data types (`ModInfo`, `LauncherSettings`, `ModProfile`, etc.)
  - `Services/` — `ModScanner` (mod.json discovery), `SteamLocator` (Steam/Workshop path detection),
    `ConflictRules` (which files are excluded from conflict checks), `IniFile`, `AcfManifest` (Steam manifest
    parsing), `LooseJson` (loose JSON parsing), `WorkshopApiClient` (Steam Workshop API)
  - `Diagnostics/Logger.cs` — crash log writer
- [src/DW2ModLauncher.App/](src/DW2ModLauncher.App/) — the WinForms launcher. `MainForm` owns UI state and is
  split across multiple `partial class` files by concern (`MainForm.Ui.cs`, `MainForm.Mods.cs`,
  `MainForm.Conflicts.cs`, `MainForm.Workshop.cs`, `MainForm.Ini.cs`, `MainForm.Launch.cs`,
  `MainForm.Settings.cs`, `MainForm.LoadOrder.cs`, `MainForm.Localization.cs`) rather than one class per file —
  this is one class organized across files, not several independent classes.
- [src/DW2ModLauncher.Tests/](src/DW2ModLauncher.Tests/) — xUnit tests against `Core` (run with `dotnet test`)
- [BUILD_BETA.cmd](BUILD_BETA.cmd) / [BUILD_AND_RUN_BETA.cmd](BUILD_AND_RUN_BETA.cmd) — build (and build+run) scripts, wrapping `dotnet build`
- [launcher_settings.example.json](launcher_settings.example.json) — example user config (game folder, Workshop folder, managed MOD folder)
- [docs/](docs/) — design notes, feature specs, and other documentation too long-lived for a PR description or issue thread

## Build & run

```text
BUILD_BETA.cmd
BUILD_AND_RUN_BETA.cmd
```

Both wrap `dotnet build DW2ModLauncher.sln -c Release`. Requires the .NET 8 SDK and Windows (the app targets
`net8.0-windows` / WinForms). Run tests with `dotnet test src/DW2ModLauncher.Tests`. When adding new logic, prefer
putting anything that doesn't need a `Form`/`Control` in `DW2ModLauncher.Core` so it can be unit tested — this is
where load-order/merge logic and DLL-injection argument building should live as those features are built out.

## Conventions

- Keep the README's English and Japanese sections in sync when user-facing behavior changes.
- This is community-maintained: prefer clear, approachable code and PRs over clever ones — contributors will span a range of experience levels.
- License is MIT; don't introduce dependencies with incompatible or unclear licensing.
- Favor open, cross-platform-friendly tooling where practical, since C# Dev Kit's free-use license (relied on by contributors in VS Code) is conditioned on this project staying open-source/non-commercial.
- Document non-obvious decisions (mod conflict-detection rules, merge/load-order semantics, DLL-injection launch flags) in [docs/](docs/) rather than only in commit messages, since this shapes contributor and AI-agent understanding going forward.

## Updating this file

When project direction, structure, or collective conventions change, update AGENTS.md as part of that change — don't let it drift from reality.
