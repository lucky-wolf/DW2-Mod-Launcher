# AGENTS.md

Collective directives for anyone (human or AI) working in this repo. This file is the shared source of truth for how the project should be approached — keep it current as conventions and scope evolve.

## Project summary

DW2 Mod Launcher is an unofficial, open-source (MIT) community launcher/mod manager for **Distant Worlds 2**. It is a hobby project developed cooperatively; contributions, forks, and continued community development are explicitly welcomed (see [README.md](README.md)).

Current features (implemented): scanning/enabling/disabling MODs from Steam Workshop and the local MOD folder, duplicate detection, file-conflict checks between enabled MODs, Workshop update checks, MOD info/README/tool discovery, INI editing, schema-driven JSON settings editing, per-MOD launch args, code-mod loading via an injected loader DLL (see [docs/dll-injection.md](docs/dll-injection.md)), publishing a local MOD to the Steam Workshop by embedding the Steamworks API directly (see [docs/workshop-publish.md](docs/workshop-publish.md)), EN/JP UI.

Planned/target scope (in progress or aspirational — confirm current state before assuming these exist):
- Load order selection for enabled MODs
- Merging selected MODs into a dedicated, curated "merged mod" output folder for use in-game
- AI-assisted review and resolution of MOD conflicts

## Repo structure

- [DW2ModLauncher.sln](DW2ModLauncher.sln) — solution; open this in VS Code (with C# Dev Kit) or Visual Studio
- [src/DW2ModLauncher.Core/](src/DW2ModLauncher.Core/) — UI-independent logic, safe to unit test:
  - `Models/` — plain data types (`ModInfo`, `LauncherSettings`, `ModProfile`, `LoaderManifest`,
    `ModSettingsSchema`, etc.)
  - `Services/` — `ModScanner` (mod.json discovery), `SteamLocator` (Steam/Workshop path detection),
    `ConflictRules` (which files are excluded from conflict checks), `IniFile`, `AcfManifest` (Steam manifest
    parsing), `LooseJson` (loose JSON parsing), `WorkshopApiClient` (Steam Workshop API),
    `LauncherMetaReader` (launcher.json reader), `LoaderManifestBuilder` (builds the manifest the loader DLL
    reads — see [docs/dll-injection.md](docs/dll-injection.md)), `ModSettingsSchemaReader`/`ModSettingsStore`
    (mod-authored `settings.schema.json` + per-user stored values), `IniSettingsSchemaBuilder` (infers an
    equivalent schema + values straight from a plain INI file, so INI-based MODs render through the same
    settings editor as schema-based ones), `IniKeyHumanizer`, `UserDataRoot` (`%AppData%\DW2ModLauncher`),
    `ModJsonWorkshopIdWriter`
  - `Services/Publishing/` — Steam Workshop publish (see [docs/workshop-publish.md](docs/workshop-publish.md)).
    `IModPublisher`/`ModPublishRequest`/`ModPublishResult`/`ModPublishMetadataEditor` (reads/writes
    mod.json's displayName/description/previewImage/version/bundles) are the stable surface;
    `SteamworksModPublisher` is the only `IModPublisher` implementation, embedding the Steamworks
    API directly (via the `Facepunch.Steamworks` NuGet package — MIT licensed) so publishing
    piggybacks on the locally logged-in Steam client instead of needing its own credentials. A
    future alternative implementation only means adding a new class here.
  - `Diagnostics/Logger.cs` — crash log writer
- [src/DW2ModLauncher.Loader/](src/DW2ModLauncher.Loader/) — the standalone DLL the launcher injects via
  `--low-level-inject` (see [docs/dll-injection.md](docs/dll-injection.md)). Deliberately has no project
  reference to `Core`/`App` — it runs inside the game process, so it stays minimal (BCL +
  `System.Text.Json` only). Loads every enabled mod itself, via reflection, from a manifest the launcher
  writes before launch.
- [src/DW2ModLauncher.App/](src/DW2ModLauncher.App/) — the WinForms launcher. `MainForm` owns UI state and is
  split across multiple `partial class` files by concern (`MainForm.Ui.cs`, `MainForm.Mods.cs`,
  `MainForm.Conflicts.cs`, `MainForm.Workshop.cs`, `MainForm.Ini.cs`, `MainForm.ModSettings.cs`,
  `MainForm.Launch.cs`, `MainForm.Settings.cs`, `MainForm.LoadOrder.cs`, `MainForm.Localization.cs`) rather
  than one class per file — this is one class organized across files, not several independent classes.
- [src/DW2ModLauncher.Tests/](src/DW2ModLauncher.Tests/) — xUnit tests against `Core` (run with `dotnet test`)
- [build.cmd](build.cmd) / [run.cmd](run.cmd) — build (and build+run) scripts, wrapping `dotnet build`
- [launcher_settings.example.json](launcher_settings.example.json) — example user config (game folder, Workshop folder, managed MOD folder)
- [docs/](docs/) — design notes, feature specs, and other documentation too long-lived for a PR description or issue thread

## Build & run

```text
build.cmd
run.cmd
```

Both wrap `dotnet build DW2ModLauncher.sln -c Release`. Requires the .NET 8 SDK and Windows (the app targets
`net8.0-windows` / WinForms). `build.cmd` (and `run.cmd`, which calls it) also validates formatting and runs the
test suite before building, the same gates CI uses, and fails fast with a message if either would fail CI; pass
`--no-validate` to skip straight to `dotnet build` for a fast local iteration loop. When adding new logic, prefer
putting anything that doesn't need a `Form`/`Control` in `DW2ModLauncher.Core` so it can be unit tested — this is
where load-order/merge logic and DLL-injection argument building should live as those features are built out.

## CI

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push to `main` and every pull request
(including from forks), on `windows-latest` since the app targets `net8.0-windows`/WinForms. It gates on, in order:
`dotnet format --verify-no-changes` (formatting), `dotnet build -c Release` (build), then `dotnet test` (unit
tests) — the same checks `build.cmd` runs locally by default. Run `dotnet format DW2ModLauncher.sln` locally
before pushing to fix formatting issues the check would otherwise catch.

## Conventions

- Keep the README's English and Japanese sections in sync when user-facing behavior changes.
- This is community-maintained: prefer clear, approachable code and PRs over clever ones — contributors will span a range of experience levels.
- License is MIT; don't introduce dependencies with incompatible or unclear licensing. (`Facepunch.Steamworks`, used for Workshop publish, is MIT and ships its own native `steam_api64.dll` — see [docs/workshop-publish.md](docs/workshop-publish.md) for what that requires at build/runtime.)
- Favor open, cross-platform-friendly tooling where practical, since C# Dev Kit's free-use license (relied on by contributors in VS Code) is conditioned on this project staying open-source/non-commercial.
- Document non-obvious decisions (mod conflict-detection rules, merge/load-order semantics, DLL-injection launch flags) in [docs/](docs/) rather than only in commit messages, since this shapes contributor and AI-agent understanding going forward.

## Updating this file

When project direction, structure, or collective conventions change, update AGENTS.md as part of that change — don't let it drift from reality.
