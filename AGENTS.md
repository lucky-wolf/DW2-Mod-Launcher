# AGENTS.md

Collective directives for anyone (human or AI) working in this repo. This file is the shared source of truth for how the project should be approached — keep it current as conventions and scope evolve.

## Project summary

DW2 Mod Launcher is an unofficial, open-source (MIT) community launcher/mod manager for **Distant Worlds 2**. It is a hobby project developed cooperatively; contributions, forks, and continued community development are explicitly welcomed (see [README.md](README.md)).

Current features (implemented): scanning/enabling/disabling Mods from Steam Workshop and the local Mod folder, drag-and-drop load order, mod profiles (the game's own `mods.<name>.json` files), creating and deleting local Mods, duplicate detection, file-conflict checks between enabled Mods, Workshop update checks, Mod info/README/tool discovery, schema-driven JSON settings editing, per-Mod launch args, code-mod loading via an injected loader DLL (see [docs/DLL Injection.md](docs/DLL%20Injection.md)), publishing a local Mod to the Steam Workshop by embedding the Steamworks API directly, EN/JP UI. Runs on Windows and Linux (Avalonia; on Linux the game runs under Proton) — see [docs/archived/linux-support.md](docs/archived/linux-support.md).

Not planned: merging Mods into a combined output folder, and automatic conflict resolution (conflicts are reported, never merged).

## Repo structure

- [DW2ModLauncher.sln](DW2ModLauncher.sln) — solution; open this in VS Code (with C# Dev Kit) or Visual Studio
- [src/DW2ModLauncher.Core/](src/DW2ModLauncher.Core/) — UI-independent logic, safe to unit test:
  - `Models/` — plain data types (`ModInfo`, `LauncherSettings`, `LoaderManifest`,
    `ModSettingsSchema`, etc.)
  - `Services/` — `ModScanner` (mod.json discovery), `SteamLocator` (Steam/Workshop path detection),
    `ConflictRules` (which files are excluded from conflict checks), `AcfManifest` (Steam manifest
    parsing), `LooseJson` (loose JSON parsing), `WorkshopApiClient` (Steam Workshop API),
    `LauncherMetaReader` (optional dw2modlauncher.json injection override), `InjectionScanner` (infers injection DLLs: public static `Entry` class with `Init()`/`InitWithOptions(string)`), `LoaderManifestBuilder` (builds the manifest the loader DLL
    reads — see [docs/DLL Injection.md](docs/DLL%20Injection.md)), `ModSettingsSchemaReader`/`ModSettingsStore`
    (mod-authored `settings.schema.json` + per-user stored values), `UserDataRoot` (`%AppData%\DW2ModLauncher`, or `~/.config/DW2ModLauncher` on Linux),
    `ModJsonWorkshopIdWriter`, `LocalModManager` (create/delete a local Mod folder, folder-name sanitizing). Everything that is logic rather than presentation lives here so it is unit
    tested and independent of the UI: `LauncherSettingsStore`/`PathDetector`
    (settings, path detection), `GameProfileStore` (DW2's named mod profiles: `currentProfile.txt` + `mods.<name>.json` beside `mods.json`; design history in [docs/archived/mod-profiles.md](docs/archived/mod-profiles.md)), `ModOrderState`/`ModOrderStore` (DW2's `mods.json` + the "is this mod
    enabled" rules), `ConflictAnalyzer`/`ModHealth`/`LaunchDiagnostics`, `WorkshopUpdateService`, `ModLibrary`,
    `ModDetails`, `ModSettingsValues`, `ModFileImporter` (turns a file the author picked into a mod-relative path for mod.json, copying it into the mod folder if needed), `FileNames`, `GameLauncher` (launch command: the exe on Windows, `steam -applaunch` on Linux
    on Linux), `GamePaths` (host path -> `Z:\...` as the game sees it under Proton), `PlatformShell`
    (explorer / xdg-open), `GameProcess`, `Localization`
  - `Services/Publishing/` — Steam Workshop publish.
    `IModPublisher`/`ModPublishRequest`/`ModPublishResult`/`ModVersion` (next-version proposal)/`ModPublishMetadataEditor` (reads/writes
    mod.json's displayName/description/previewImage/version/bundles) are the stable surface;
    `SteamworksNetModPublisher` embeds the Steamworks API directly (Steamworks.NET, MIT, vendored per OS in
    [third_party/steamworks/](third_party/steamworks/README.md)) so publishing piggybacks on the locally
    logged-in Steam client instead of needing its own credentials. `ModPublisherFactory` creates it and
    reports which `ModVisibility` levels are supported.
  - `Services/Updates/` — launcher self-update. `UpdateChecker` asks GitHub's latest-release API (newer tag + the package for this OS), `LauncherUpdater` downloads/unpacks it into `<data root>/Updates` and starts a copy of the launcher with `--apply-update` (handled in `Program.Main`, like `--steam-worker`), and that copy (`UpdateApplier`) waits for the launcher to exit, copies the new files over the install folder (exe last) and relaunches. Dev builds and `dotnet run` never self-update.
  - `Diagnostics/Logger.cs` — log writer (`DW2ModLauncher.log` in the user data folder)
- [src/DW2ModLauncher.Loader/](src/DW2ModLauncher.Loader/) — the standalone DLL the launcher injects via
  `--low-level-inject` (see [docs/DLL Injection.md](docs/DLL%20Injection.md)). Deliberately has no project
  reference to `Core`/`Avalonia` — it runs inside the game process, so it stays minimal (BCL +
  `System.Text.Json` only). Loads every enabled mod itself, via reflection, from a manifest the launcher
  writes before launch. Also owns the in-game status line (`StatusRegistry`/`ModStatus`/`StatusText`/`StatusWidget`): the
  widget is built entirely by reflection because CI has no game to compile against - see [docs/Mod Status Line.md](docs/Mod%20Status%20Line.md).
- [src/DW2ModLauncher.Avalonia/](src/DW2ModLauncher.Avalonia/) — the launcher (Windows and Linux). MVVM-lite:
  `ViewModels/` hold state and commands (`MainViewModel`, `SettingsViewModel`, `PublishDialogViewModel`, ...),
  `Views/` are thin XAML, `Services/DialogService` is the only code that touches windows/pickers. Bindings to
  language strings use `{Binding L[Key]}`; the EN/JP packs are in `Languages/`. Run with
  `dotnet run --project src/DW2ModLauncher.Avalonia`.
- [src/DW2ModLauncher.Tests/](src/DW2ModLauncher.Tests/) — xUnit tests against `Core` (run with `dotnet test`)
- [scripts/](scripts/) — Python tooling: `build.py` / `run.py` (cross-platform build, and build+run, of the current OS's launcher; [build.cmd](build.cmd) / [run.cmd](run.cmd) are the Windows batch equivalents), `validate.py` (the CI gate), `new-branch.py` / `open-pr.py` (branch and PR workflow), `release.py` / `release-notes.py` (CI release), `make-icon.py`; shared helpers in `scripts/lib/`
- [tools/](tools/) — author tooling outside the launcher build (not in the solution). `vscode-dw2-patch/` is a VS Code extension (TypeScript, own `package.json`) that writes XML patches from the cursor position in a data file; its logic in `src/core` is `vscode`-free and tested with `npm test`, and it mirrors `XmlPatching/KeyMap.cs` (a test fails on drift). `export-schema/` (net8 console) dumps the game's patch schema to `vscode-dw2-patch/schema/schema.json`; re-run it after a game update
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
putting anything that doesn't need a window/control in `DW2ModLauncher.Core` so it can be unit tested (load-order logic and
DLL-injection argument building live there).

## CI & Releases

[.github/workflows/ci.yml](.github/workflows/ci.yml) gates every pull request (including from forks, on `windows-latest` and `ubuntu-latest`; not on pushes to `main`, since merges are rebases of already-validated branches) by running `scripts/validate.py` (`dotnet format --verify-no-changes`, `dotnet build -c Release`, then `dotnet test` - the same checks `build.cmd` runs; run `dotnet format DW2ModLauncher.sln` to fix formatting); `scripts/new-branch.py` and `scripts/open-pr.py` start and open PRs; [.github/workflows/release.yml](.github/workflows/release.yml)
tags and publishes a release on every merge to `main` that touches `src/`. Versions come from MinVer + [.version](.version);
never hand-set a version in a `.csproj`. Full writeup: [docs/CI Releases.md](docs/CI%20Releases.md). Linux plans:
[docs/archived/linux-support.md](docs/archived/linux-support.md).

## Conventions

- English is the primary language; Japanese is a first-class translation. [README.md](README.md) is the English README and [README.ja.md](README.ja.md) the Japanese one (each links to the other at the top). Any user-facing change to README.md must be mirrored in README.ja.md in the same change. `scripts/validate.py` (so CI) fails if a `*.ja.md` drifts from its English original's heading structure, code blocks or relative links (`scripts/lib/translation_checks.py`), so commands and paths stay identical and only prose is translated. If you can't write the Japanese yourself, still add the section (English text is acceptable as a placeholder) and say so in the PR so a Japanese speaker can fix it. Only the README and other user-facing docs are translated; design/plan docs under `docs/` and this file stay English-only. Add a translation of another doc as `Foo.ja.md` beside `Foo.md`.
- This is community-maintained: prefer clear, approachable code and PRs over clever ones — contributors will span a range of experience levels.
- License is MIT; don't introduce dependencies with incompatible or unclear licensing. (Steamworks.NET, used for Workshop publish, is MIT; the native Steam API library it needs is Valve's, see below.)
- Steam's native API library (`steam_api64.dll` / `libsteam_api.so`) is Valve's redistributable, not MIT. It is vendored unmodified under [third_party/steamworks/](third_party/steamworks/README.md) as a matched pair with the Steamworks.NET wrapper (same Steamworks.NET revision / SDK version); update both together. See README "License".
- Favor open, cross-platform-friendly tooling where practical, since C# Dev Kit's free-use license (relied on by contributors in VS Code) is conditioned on this project staying open-source/non-commercial.
- Document non-obvious decisions (mod conflict-detection rules, load-order semantics, DLL-injection launch flags) in [docs/](docs/) rather than only in commit messages, since this shapes contributor and AI-agent understanding going forward.

## Keeping docs/focus.md current

[docs/focus.md](docs/focus.md) is the running list of what the current branch has accomplished; `scripts/open-pr.py` uses it as the PR description and `scripts/new-branch.py` empties it for new work. When you finish a user-visible change (human or AI), add a one-line `- ` entry below the `---` in the same change. Each line is one short, single-line, fairly high-level accomplishment - never a long run-on sentence smashing several things together. If three things were done in the same subsystem, that is three short lines saying what they were, not one merged line.

## Updating this file

When project direction, structure, or collective conventions change, update AGENTS.md as part of that change — don't let it drift from reality.
