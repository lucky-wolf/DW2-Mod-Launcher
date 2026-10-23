# AGENTS.md

Shared directives for anyone (human or AI) working in this repo. Keep it current as conventions and scope evolve.

## Project summary

DW2 Mod Launcher is an unofficial, open-source (MIT), cooperatively developed community launcher/mod manager for **Distant Worlds 2**; contributions and forks are welcome ([README.md](README.md)). Features: scanning/enabling/disabling Mods from Steam Workshop and the local Mod folder, drag-and-drop load order, mod profiles (the game's `mods.<name>.json`), creating/deleting local Mods, duplicate detection, file-conflict checks, Workshop update checks, Mod info/README/tool discovery, schema-driven JSON settings, per-Mod launch args, code-mod loading via an injected loader DLL ([docs/DLL Injection.md](docs/DLL%20Injection.md)), Workshop publishing via embedded Steamworks, EN/JP/RU UI. Windows and Linux (Avalonia; the game runs under Proton on Linux; [docs/archived/linux-support.md](docs/archived/linux-support.md)).

Not planned: merging Mods into a combined output folder, automatic conflict resolution (conflicts are reported, never merged).

## Repo structure

- [DW2ModLauncher.sln](DW2ModLauncher.sln): open in VS Code (C# Dev Kit) or Visual Studio.
- [src/DW2ModLauncher.Core/](src/DW2ModLauncher.Core/): all UI-independent logic, unit tested. Anything that isn't window/control code goes here. Folders: `Models/` (plain data types), `Services/` (one class per concern; browse it), `Services/Publishing/`, `Services/Updates/`, `Diagnostics/Logger.cs` (`DW2ModLauncher.log` in the user data folder). Facts not evident from the code:
  - User data root: `%AppData%\DW2ModLauncher`, or `~/.config/DW2ModLauncher` on Linux.
  - Mod profiles are DW2's own `currentProfile.txt` + `mods.<name>.json` beside `mods.json` (`GameProfileStore`; history in [docs/archived/mod-profiles.md](docs/archived/mod-profiles.md)).
  - `HookFiles`/`HookOverlapAnalyzer` find mods patching the same game code from the loader's `hooks-observed.json` and each mod's `dw2modlauncher.hooks.json`, plus live-channel version mismatches ([docs/Code Hooks.md](<docs/Code Hooks.md>); open items [docs/plans/hook-manifest.md](docs/plans/hook-manifest.md)).
  - `InjectionScanner` infers injection DLLs: public static `Entry` class with `Init()`/`InitWithOptions(string)`. `LoaderManifestBuilder` builds the manifest the loader reads. `LauncherMetaReader` reads the optional `dw2modlauncher.json` override.
  - `GameLauncher` launches the exe on Windows, `steam -applaunch` on Linux; `GamePaths` maps host paths to `Z:\...` as the game sees them under Proton.
  - Publishing: `IModPublisher` and friends are the stable surface; `SteamworksNetModPublisher` embeds Steamworks.NET (MIT, vendored per OS in [third_party/steamworks/](third_party/steamworks/README.md)) and uses the locally logged-in Steam client, so no credentials of its own. `ModPublisherFactory` reports which `ModVisibility` levels are supported.
  - `PublishedState` (`<data root>/PublishedState/<workshop id>.json`) records per file what the last publish uploaded, driving the "changed since last publish" status. It also keeps the description as last sent to Steam (placeholders filled), which the Publish and Edit Properties dialogs compare with Steam's copy for the red "edited on Steam" dot and the overwrite prompt. The same sweep that checks for deleted items learns each item's visibility via batched `IModPublisher.QueryItems`/`SteamItemStatus` ([docs/plans/unpublished-changes-indicator.md](docs/plans/unpublished-changes-indicator.md)).
  - Self-update: `UpdateChecker` asks GitHub's latest-release API; `LauncherUpdater` unpacks into `<data root>/Updates` and starts a copy with `--apply-update` (handled in `Program.Main`, like `--steam-worker`); `UpdateApplier` waits for exit, copies files over the install folder (exe last) and relaunches. Dev builds and `dotnet run` never self-update.
- [src/DW2ModLauncher.Loader/](src/DW2ModLauncher.Loader/): the standalone DLL injected via `--low-level-inject`. Runs inside the game, so no reference to `Core`/`Avalonia` (BCL + `System.Text.Json` only). Loads every enabled mod by reflection from a manifest the launcher writes before launch. Owns the in-game status line (`StatusRegistry`/`ModStatus`/`StatusText`/`StatusWidget`), built entirely by reflection because CI has no game to compile against ([docs/Mod Status Line.md](docs/Mod%20Status%20Line.md)).
- [src/DW2ModLauncher.Avalonia/](src/DW2ModLauncher.Avalonia/): the launcher, MVVM-lite. `ViewModels/` hold state and commands, `Views/` are thin XAML, `Services/DialogService` is the only code touching windows/pickers. Bind language strings with `{Binding L[Key]}`; packs are in `Languages/` ([docs/Localization.md](docs/Localization.md)). Any new user-facing text gets a key in every pack, and a new `Widget*` key also goes into `StatusStrings` in the loader. Run: `dotnet run --project src/DW2ModLauncher.Avalonia`.
- [src/DW2ModLauncher.Tests/](src/DW2ModLauncher.Tests/): xUnit tests against `Core` (`dotnet test`).
- [scripts/](scripts/): Python tooling, helpers in `scripts/lib/`.
  - `build.py`/`run.py` build (and run) the current OS's launcher; [build.cmd](build.cmd)/[run.cmd](run.cmd) are Windows batch equivalents.
  - `validate.py` is the CI gate. Locally it stops at once if the launcher or Distant Worlds 2 is running (they lock files and fail the load-order tests). `build.py` only needs the launcher closed; with the game open it skips tests and the Loader DLL copy (locked by the game), builds and deploys the rest, and says whether the Loader changed.
  - `new-branch.py`/`open-pr.py` handle branch and PR workflow. `build.py` and `open-pr.py` also sync the launcher's own Workshop item (local mod folder `DW2 Mod Launcher` in the game's mods folder) with the repo via `lib/mod_sync.py`: [mod/description.bbcode](mod/description.bbcode) both ways, and `changelog.txt` kept in step (the launcher stamps the folder's Unreleased on publish; the next sync adopts the stamp).
  - `release.py`/`release-notes.py` (CI release), `make-icon.py`.
- [docs/](docs/): design notes and specs too long-lived for a PR description.

## Build & run

`build.cmd` / `run.cmd` wrap `dotnet build DW2ModLauncher.sln -c Release` (.NET 10 SDK). They first validate formatting and run the tests (the CI gates) and fail fast; `--no-validate` skips to `dotnet build` for quick iteration. On Linux use `python3 scripts/validate.py`.

**Agents: build it yourself.** After finishing a feature or fix, run `python scripts/build.py` (not `build.cmd`, which pauses on errors) instead of leaving it to the owner. Report a failure prominently: the owner reads a failure as "I must build it" and success, or no mention, as "already built", so a success needs only a short line. If the running launcher stops the build overwriting files, say so plainly and don't retry in a loop. Never launch the launcher yourself.

## CI & Releases

[.github/workflows/ci.yml](.github/workflows/ci.yml) gates every pull request (forks included; `windows-latest` and `ubuntu-latest`; not pushes to `main`, since merges are rebases of validated branches) by running `scripts/validate.py`: `dotnet format --verify-no-changes`, `dotnet build -c Release`, `dotnet test` (fix formatting with `dotnet format DW2ModLauncher.sln`). [.github/workflows/release.yml](.github/workflows/release.yml) tags and publishes a release on every merge to `main` touching `src/`. Versions come from MinVer + [.version](.version); never hand-set a version in a `.csproj`. Details: [docs/CI Releases.md](docs/CI%20Releases.md).

## Conventions

- English is primary, Japanese a first-class translation. User-facing changes to [README.md](README.md) are mirrored in [README.ja.md](README.ja.md) in the same change. `scripts/validate.py` (so CI) fails if a `*.ja.md` drifts from its original's heading structure, code blocks or relative links (`scripts/lib/translation_checks.py`), so commands and paths stay identical and only prose is translated. If you can't write the Japanese, add the section in English as a placeholder and say so in the PR. Only the README and other user-facing docs are translated (`Foo.ja.md` beside `Foo.md`); design/plan docs and this file stay English.
- Community-maintained: prefer clear, approachable code and PRs over clever ones.
- MIT license: no dependencies with incompatible or unclear licensing. Steamworks.NET is MIT; the native Steam API library (`steam_api64.dll`/`libsteam_api.so`) is Valve's redistributable, vendored unmodified under [third_party/steamworks/](third_party/steamworks/README.md) as a matched pair with the wrapper (same revision/SDK version; update together). See README "License".
- Favor open, cross-platform tooling: C# Dev Kit's free-use license (relied on in VS Code) requires the project to stay open-source/non-commercial.
- **Tooltips on buttons at a window's bottom edge** (dialog footers): `ToolTip.VerticalOffset` defaults to +20 for every `Placement`, so a `Placement="Top"` tooltip is pushed back down over its own button and swallows the click. Set `ToolTip.Placement="Top" ToolTip.VerticalOffset="-8" ToolTip.HorizontalOffset="0"` together (see the Publish dialog footer). The tooltip anchors to the button's edge, not the pointer. The offset is applied on top of the anchor edge, so a tooltip below a button needs a positive offset big enough to clear it. Toolbar buttons are fine (default pointer placement). No transparency or click-through hacks.
- Document non-obvious decisions (conflict-detection rules, load-order semantics, injection launch flags) in [docs/](docs/), not only in commit messages.

## No defensive programming - fail loudly, never substitute

Defensive programming is anathema. Code must never quietly swap a plausible value for a missing or invalid one: that turns an error into a lie that outlives the bug.

- **No clever fallbacks.** If `mod.json` names a preview image that doesn't exist, the preview is *missing* (nothing shown, plus a visible warning), not some other image the code guessed. Any name, path, id, setting or file that fails to resolve is reported, not repaired.
- **No swallowed errors**: no `catch { return null; }` / `catch { }`. An empty result means "there is none", never "something went wrong".
- **Local try/catch is wanted, as a reporting point.** Never crash on the user. Catch at failure boundaries (file IO, parsing, reflection, in-game hooks), capture what was being done, to which file/value, and the exception message, surface it through a real channel, then carry on safely (perhaps skipping that item). A catch whose only action is to continue is a bug.
- **Channels**: UI code uses `Dialogs.ShowMessageAsync`/`SetStatus` and `Logger.LogException`. Code in the game (loader, code mods) uses the mod status line (`ModStatus`/`StatusRegistry`, `HookStatus.Warn`/`HookStatus.Failed`; [docs/Mod Status Line.md](docs/Mod%20Status%20Line.md)) plus its log, so it shows in-game. If there is no channel, add one.
- **No coercing bad input** (clamping, defaulting, trimming) unless documented, intended and visible.
- A fallback is acceptable only as the specified behavior for a value *never set*, never for one that was set and is wrong.
- **Validate at the boundary, then trust the data.**
- **Sensible defaults; user choices are law.** Every setting defaults to the right outcome and any default can be overridden. A wrong choice gets a visible warning or error saying exactly how it's wrong, never silently fixed or ignored.

Fix existing code that breaks this rather than building on it. **Reviews** (a human's or another agent's PR): hunt for it and report it as a finding, not a nit (swallowed/empty catches, return-null/empty on error, guessed substitutes, quietly clamped/defaulted input, error handling that avoids deciding what the error means). Say what the failure should do instead, and flag the PR as needing changes.

## Mod changes belong in the mod's changelog

The code mods live in DW2-XL, not here. When work from this repo edits a mod's source (`dll/<Mod>` in DW2-XL), add a one-line `- ` entry (~8 words, what not why) under `## Unreleased` in that mod's `dll/<Mod>/changelog.txt`; the launcher flags a mod as modified when its DLL changes and that line says why. **Changes only in DW2-XL's `dll/Shared` get no changelog entry in any mod**, even though every mod's DLL changes: the launcher reads a modified mod with an empty Unreleased section as "rebuilt only" and marks it up to date, while a rebuild-only entry would hold it for release. The launcher's own changes go in [changelog.txt](changelog.txt).

## Keeping changelog.txt current

[changelog.txt](changelog.txt) (repo root), newest first, same layout as the mods: `## Unreleased` on top with `- ` lines, then `## 1.2.3 (date)` per release. Unreleased is the branch in progress: scripts never stamp it (the launcher does on publish; `lib/mod_sync.py` brings the stamp back), `scripts/open-pr.py` uses its lines as the PR description, and nothing is ever emptied or deleted. Use them for commit messages too. On finishing a user-visible change, add a `- ` entry in the same change: one short, single-line, fairly high-level accomplishment, never a run-on. Keep the list short: don't add a line if one already covers that kind of change, and fold small lines on one theme into one. **Script and tooling changes (`scripts/`, CI, build/release plumbing, housekeeping) don't belong in it**; at most a commit message.

## Updating this file

When direction, structure or conventions change, update AGENTS.md in the same change.
