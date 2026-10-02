# CI & Releases

How the launcher is validated, versioned, and shipped. Modeled on critical-mass-client's flow
(MinVer + a hand-bumped `.version` + an auto-incrementing PATCH tag), adapted to GitHub Actions.

## Workflows

| Workflow                                        | Runs on                                                                                                                     | Does                                                                                           |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| [ci.yml](../.github/workflows/ci.yml)           | every PR, push to `main`                                                                                                    | `scripts/validate.py`: restore, `dotnet format --verify-no-changes`, build (Release), unit tests. `windows-latest` (WinForms). |
| [release.yml](../.github/workflows/release.yml) | push to `main` touching `src/`, `Directory.Build.props`, `.version`, or the workflow itself; same-path PRs; manual dispatch | tests → tag → publish → package → GitHub Release (tag and release only on push to `main`)      |

Run `build.cmd` or `python scripts/validate.py` locally; both run the same checks as `ci.yml`.

## Scripts

Python 3, standard library only, no submodule. Shared helpers live in [scripts/lib/](../scripts/lib/)
and are imported as `from lib import ...`. Every script has `--help` and, if it mutates anything,
`--dry-run`.

| Script | Purpose |
|---|---|
| [validate.py](../scripts/validate.py) | The one validation suite; CI's entrypoint and `open-pr.py`'s pre-flight (`lib/dotnet_checks.py`). |
| [new-branch.py](../scripts/new-branch.py) | Sync `main`, delete merged/gone local branches, create `<user>/vX.Y.Z` (waits for the release tag if `main`'s tip should have one). |
| [open-pr.py](../scripts/open-pr.py) | Run the checks, commit, push, open the PR via `gh`, titled `Released as vX.Y.Z` with a commit-subject description. |
| [release.py](../scripts/release.py) | CI-only: tags the next version (see Versioning). |

`open-pr.py` needs the GitHub CLI (`gh auth login`); it never handles tokens.

## Versioning

- Releases are git tags `vMAJOR.MINOR.PATCH` (SemVer).
- [.version](../.version) holds `MAJOR.MINOR`, bumped **by hand** when a release warrants it.
- `PATCH` is automatic: [scripts/release.py](../scripts/release.py) finds the latest `vMAJOR.MINOR.*`
  tag and pushes the next one (`v0.1.0` if none exist).
- [MinVer](https://github.com/adamralph/minver) (configured in [Directory.Build.props](../Directory.Build.props))
  reads the nearest `v*` tag and stamps `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`
  into every project. **Never hand-set a version in a `.csproj`.** Untagged (local/dev) builds get a
  pre-release version like `0.0.0-alpha.0.6`, so they can never be mistaken for a release.
- `release.py` is CI-only: it refuses to run for real outside GitHub Actions, never writes git config,
  and supports `--dry-run` (`python scripts/release.py --dry-run` prints the next tag, changes nothing).

## What a release run does

1. Checkout with full history (MinVer and `release.py` need the tags).
2. Restore, then run the unit tests (never release what wouldn't pass CI).
3. `release.py` tags and pushes `vX.Y.Z` (push to `main` only).
4. `dotnet publish` the App for `win-x64`: self-contained, single-file, compressed.
5. Package `DW2ModLauncher-vX.Y.Z-win-x64.zip` and upload it as a workflow artifact.
6. `gh release create` attaches the zip to a GitHub Release with generated notes (push to `main` only).

**PR runs and manual dispatch** build and upload the zip as a workflow artifact only: no tag, no release. Use
it to inspect package contents; a PR run shows packaging problems before merge.

If a run is retried after the tag exists, `release.py` sees it and exits without output, so no
duplicate release is created.

## Release contents

Unzip and run; no .NET install needed (self-contained). The runtime and `Core` are compiled into the single exe;
only files that something else loads **by path** stay loose:

```
DW2ModLauncher-vX.Y.Z-win-x64/
  DW2ModLauncherBeta.exe
  Loader/DW2ModLauncher.Loader.dll   injected into the game via --low-level-inject (docs/DLL Injection.md)
  Languages/en.json, ja.json         UI strings
  steam_api64.dll, steam_appid.txt   Steamworks (Workshop publish), loaded by the Steam API by name/CWD
  launcher_settings.example.json, LICENSE, README.md
```

The Loader DLL is added to the publish output by the `AddLoaderToPublish` target in
[DW2ModLauncher.App.csproj](../src/DW2ModLauncher.App/DW2ModLauncher.App.csproj) and marked
`ExcludeFromSingleFile`; without it `dotnet publish` would bundle it into the exe or drop it.

Trimming is not used: WinForms is not trim-compatible. Compression is the size lever.

## Target frameworks

Core, App, and Tests target `net10.0-windows`. The **Loader stays `net8.0`**: it is loaded into the
game's own process, which runs on .NET 8, so it must not target a newer runtime. The launcher's runtime
is independent of the game's because the launcher is a separate process.

## Repo requirements

- Settings → Actions → General → Workflow permissions: **read and write** (the release workflow requests
  `contents: write` to push tags and create releases; an org-level policy can override this).
- Bump `.version` by hand for MAJOR/MINOR changes; nothing else needs touching to cut a release.
