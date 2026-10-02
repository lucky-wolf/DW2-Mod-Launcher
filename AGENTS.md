# AGENTS.md

Collective directives for anyone (human or AI) working in this repo. This file is the shared source of truth for how the project should be approached — keep it current as conventions and scope evolve.

## Project summary

DW2 Mod Launcher is an unofficial, open-source (MIT) community launcher/mod manager for **Distant Worlds 2**. It is a hobby project developed cooperatively; contributions, forks, and continued community development are explicitly welcomed (see [README.md](README.md)).

Current features (implemented): scanning/enabling/disabling Mods from Steam Workshop and the local Mod folder, duplicate detection, file-conflict checks between enabled Mods, Workshop update checks, Mod info/README/tool discovery, schema-driven JSON settings editing, per-Mod launch args, code-mod loading via an injected loader DLL (see [docs/DLL Injection.md](docs/DLL%20Injection.md)), publishing a local Mod to the Steam Workshop by embedding the Steamworks API directly (see [docs/workshop-publish.md](docs/workshop-publish.md)), EN/JP UI. Runs on Windows and Linux (Avalonia; on Linux the game runs under Proton) — see [docs/plans/linux-support.md](docs/plans/linux-support.md).

Planned/target scope (in progress or aspirational — confirm current state before assuming these exist):
- Load order selection for enabled Mods
- Mod profiles on the main page, built on the game's own named profiles (see [docs/plans/mod-profiles.md](docs/plans/mod-profiles.md))
- Merging selected Mods into a dedicated, curated "merged mod" output folder for use in-game
- AI-assisted review and resolution of Mod conflicts

## Repo structure

- [DW2ModLauncher.sln](DW2ModLauncher.sln) — solution; open this in VS Code (with C# Dev Kit) or Visual Studio
- [src/DW2ModLauncher.Core/](src/DW2ModLauncher.Core/) — UI-independent logic, safe to unit test:
  - `Models/` — plain data types (`ModInfo`, `LauncherSettings`, `ModProfile`, `LoaderManifest`,
    `ModSettingsSchema`, etc.)
  - `Services/` — `ModScanner` (mod.json discovery), `SteamLocator` (Steam/Workshop path detection),
    `ConflictRules` (which files are excluded from conflict checks), `AcfManifest` (Steam manifest
    parsing), `LooseJson` (loose JSON parsing), `WorkshopApiClient` (Steam Workshop API),
    `LauncherMetaReader` (launcher.json reader), `LoaderManifestBuilder` (builds the manifest the loader DLL
    reads — see [docs/DLL Injection.md](docs/DLL%20Injection.md)), `ModSettingsSchemaReader`/`ModSettingsStore`
    (mod-authored `settings.schema.json` + per-user stored values), `UserDataRoot` (`%AppData%\DW2ModLauncher`, or `~/.config/DW2ModLauncher` on Linux),
    `ModJsonWorkshopIdWriter`. Everything that is logic rather than presentation lives here so both UIs
    share it and it is unit tested: `LauncherSettingsStore`/`ProfileStore`/`SnapshotStore`/`PathDetector`
    (settings, profiles, snapshots), `ModOrderState`/`ModOrderStore` (DW2's `mods.json` + the "is this mod
    enabled" rules), `ConflictAnalyzer`/`ModHealth`/`LaunchDiagnostics`, `WorkshopUpdateService`, `ModLibrary`,
    `ModDetails`, `ModSettingsValues`, `GameLauncher` (launch command: the exe on Windows, `steam -applaunch`
    on Linux), `GamePaths` (host path -> `Z:\...` as the game sees it under Proton), `PlatformShell`
    (explorer / xdg-open), `GameProcess`, `Localization`
  - `Services/Publishing/` — Steam Workshop publish (see [docs/workshop-publish.md](docs/workshop-publish.md)).
    `IModPublisher`/`ModPublishRequest`/`ModPublishResult`/`ModPublishMetadataEditor` (reads/writes
    mod.json's displayName/description/previewImage/version/bundles) are the stable surface;
    `SteamworksNetModPublisher` embeds the Steamworks API directly (Steamworks.NET, MIT, vendored per OS in
    [third_party/steamworks/](third_party/steamworks/README.md)) so publishing piggybacks on the locally
    logged-in Steam client instead of needing its own credentials. `ModPublisherFactory` creates it and
    reports which `ModVisibility` levels are supported.
  - `Diagnostics/Logger.cs` — crash log writer
- [src/DW2ModLauncher.Loader/](src/DW2ModLauncher.Loader/) — the standalone DLL the launcher injects via
  `--low-level-inject` (see [docs/DLL Injection.md](docs/DLL%20Injection.md)). Deliberately has no project
  reference to `Core`/`Avalonia` — it runs inside the game process, so it stays minimal (BCL +
  `System.Text.Json` only). Loads every enabled mod itself, via reflection, from a manifest the launcher
  writes before launch.
- [src/DW2ModLauncher.Avalonia/](src/DW2ModLauncher.Avalonia/) — the launcher (Windows and Linux). MVVM-lite:
  `ViewModels/` hold state and commands (`MainViewModel`, `SettingsViewModel`, `PublishDialogViewModel`, ...),
  `Views/` are thin XAML, `Services/DialogService` is the only code that touches windows/pickers. Bindings to
  language strings use `{Binding L[Key]}`; the EN/JP packs are in `Languages/`. Run with
  `dotnet run --project src/DW2ModLauncher.Avalonia`.
- [src/DW2ModLauncher.Tests/](src/DW2ModLauncher.Tests/) — xUnit tests against `Core` (run with `dotnet test`)
- [scripts/build.py](scripts/build.py) / [scripts/run.py](scripts/run.py) — cross-platform build (and build+run) of the current OS's launcher; [build.cmd](build.cmd) / [run.cmd](run.cmd) are the older Windows-only equivalents
- [launcher_settings.example.json](launcher_settings.example.json) — example user config (game folder, Workshop folder, managed Mod folder)
- [docs/](docs/) — design notes, feature specs, and other documentation too long-lived for a PR description or issue thread

## Build & run

```text
build.cmd
run.cmd
```

Both wrap `dotnet build DW2ModLauncher.sln -c Release`. Requires the .NET 10 SDK. On Linux use `python3 scripts/validate.py` to check everything; `dotnet run --project src/DW2ModLauncher.Avalonia`
runs the launcher on either OS. `build.cmd` (and `run.cmd`, which calls it) also validates formatting and runs the
test suite before building, the same gates CI uses, and fails fast with a message if either would fail CI; pass
`--no-validate` to skip straight to `dotnet build` for a fast local iteration loop. When adding new logic, prefer
putting anything that doesn't need a `Form`/`Control` in `DW2ModLauncher.Core` so it can be unit tested — this is
where load-order/merge logic and DLL-injection argument building should live as those features are built out.

## CI

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push to `main` and every pull request
(including from forks), on both `windows-latest` and `ubuntu-latest`. It gates on, in order:
`dotnet format --verify-no-changes` (formatting), `dotnet build -c Release` (build), then `dotnet test` (unit
tests) — the same checks `build.cmd` runs locally by default. Run `dotnet format DW2ModLauncher.sln` locally
before pushing to fix formatting issues the check would otherwise catch.

## CI & Releases

[.github/workflows/ci.yml](.github/workflows/ci.yml) gates PRs by running `scripts/validate.py`; `scripts/new-branch.py` and `scripts/open-pr.py` start and open PRs; [.github/workflows/release.yml](.github/workflows/release.yml)
tags and publishes a release on every merge to `main` that touches `src/`. Versions come from MinVer + [.version](.version);
never hand-set a version in a `.csproj`. Full writeup: [docs/CI Releases.md](docs/CI%20Releases.md). Linux plans:
[docs/plans/linux-support.md](docs/plans/linux-support.md).

## Conventions

- Keep the README's English and Japanese sections in sync when user-facing behavior changes.
- This is community-maintained: prefer clear, approachable code and PRs over clever ones — contributors will span a range of experience levels.
- License is MIT; don't introduce dependencies with incompatible or unclear licensing. (Steamworks.NET, used for Workshop publish, is MIT; the native Steam API library it needs is Valve's, see below.)
- Steam's native API library (`steam_api64.dll` / `libsteam_api.so`) is Valve's redistributable, not MIT. It is vendored unmodified under [third_party/steamworks/](third_party/steamworks/README.md) as a matched pair with the Steamworks.NET wrapper (same Steamworks.NET revision / SDK version); update both together. See README "License".
- Favor open, cross-platform-friendly tooling where practical, since C# Dev Kit's free-use license (relied on by contributors in VS Code) is conditioned on this project staying open-source/non-commercial.
- Document non-obvious decisions (mod conflict-detection rules, merge/load-order semantics, DLL-injection launch flags) in [docs/](docs/) rather than only in commit messages, since this shapes contributor and AI-agent understanding going forward.

## Updating this file

When project direction, structure, or collective conventions change, update AGENTS.md as part of that change — don't let it drift from reality.
