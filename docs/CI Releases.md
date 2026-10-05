# CI & Releases

How the launcher is validated, versioned, and shipped. Modeled on critical-mass-client's flow
(MinVer + a hand-bumped `.version` + an auto-incrementing PATCH tag), adapted to GitHub Actions.

## Workflows

| Workflow                                        | Runs on                                                                                                                     | Does                                                                                           |
| ----------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| [ci.yml](../.github/workflows/ci.yml)           | every PR                                                                                                                    | `scripts/validate.py`: restore, `dotnet format --verify-no-changes`, build (Release), unit tests. `windows-latest` and `ubuntu-latest`. |
| [release.yml](../.github/workflows/release.yml) | push to `main` touching `src/`, `Directory.Build.props`, `.version`, or the workflow itself; same-path PRs; manual dispatch | tests → tag → publish → package → GitHub Release (tag and release only on push to `main`)      |

Run `build.cmd` or `python scripts/validate.py` locally; both run the same checks as `ci.yml`.

## Scripts

Python 3, standard library only, no submodule. Shared helpers live in [scripts/lib/](../scripts/lib/)
and are imported as `from lib import ...`. Every script has `--help` and, if it mutates anything,
`--dry-run`.

| Script | Purpose |
|---|---|
| [build.py](../scripts/build.py) / [run.py](../scripts/run.py) | Local: build (and run) the launcher for the current OS (Avalonia). Applies `dotnet format`, runs the tests, builds; `--no-validate` skips the first two. Shared logic in `lib/launcher_build.py`. |
| [validate.py](../scripts/validate.py) | The one validation suite; CI's entrypoint and `open-pr.py`'s pre-flight (`lib/dotnet_checks.py`). |
| [new-branch.py](../scripts/new-branch.py) | Sync `main`, delete merged/gone local branches, create `<user>/vX.Y.Z` (waits for the release tag if `main`'s tip should have one). |
| [open-pr.py](../scripts/open-pr.py) | Run the checks, commit, push, open the PR via `gh`, titled `Released as vX.Y.Z`; the description is the list in [focus.md](focus.md), or the commit subjects when that is empty. `new-branch.py` empties `focus.md` for the new branch. |
| [release.py](../scripts/release.py) | CI-only: tags the next version (see Versioning). |
| [release-notes.py](../scripts/release-notes.py) | CI-only: writes the GitHub Release notes from [focus.md](focus.md) (the merged PR's description); falls back to generated notes when it is empty. |

`open-pr.py` needs the GitHub CLI (`gh auth login`); it never handles tokens.

## Versioning

- Releases are git tags `vMAJOR.MINOR.PATCH` (SemVer).
- [.version](../.version) holds `MAJOR.MINOR`, bumped **by hand** when a release warrants it.
- `PATCH` is automatic: [scripts/release.py](../scripts/release.py) finds the latest `vMAJOR.MINOR.*`
  tag and pushes the next one (`v0.1.0` if none exist).
- [MinVer](https://github.com/adamralph/minver) (configured in [Directory.Build.props](../Directory.Build.props))
  reads the nearest `v*` tag and stamps `Version`/`AssemblyVersion`/`FileVersion`/`InformationalVersion`
  into every project. **Never hand-set a version in a `.csproj`.**
- Directory.Build.props then strips MinVer's pre-release suffix (`StampPlainSemver`), so every build is plain
  `MAJOR.MINOR.PATCH`: a dev build shows the version the next release will carry (MinVer's next patch, which
  matches `release.py` while `.version` is unchanged: for an untagged build `Directory.Build.props` asks git for *all* `vMAJOR.MINOR.*` tags, not just those reachable from HEAD as MinVer does, so a branch cut before the last release still shows the right patch; a new MAJOR.MINOR starts at `.0`). Tags must be fetched locally (`git fetch --tags`) for this to see a release CI just made. Untagged builds keep `-dev` in the InformationalVersion
  only (`0.1.1-dev`); the window title shows it as `v0.1.1 dev` (`AppVersion.Display`). Tagged release builds
  have no suffix.
- `release.py` is CI-only: it refuses to run for real outside GitHub Actions, never writes git config,
  and supports `--dry-run` (`python scripts/release.py --dry-run` prints the next tag, changes nothing).

## What a release run does

1. Checkout with full history (MinVer and `release.py` need the tags).
2. Restore, then run the unit tests (never release what wouldn't pass CI).
3. `release.py` tags and pushes `vX.Y.Z` (push to `main` only).
4. `dotnet publish` the App for `win-x64`: self-contained, single-file, compressed.
5. Package `DW2ModLauncher-vX.Y.Z-win-x64.zip` and upload it as a workflow artifact.
   A second job (`release-linux`, on `ubuntu-latest`, after the first so the tag exists) publishes the Avalonia
   launcher for `linux-x64` (self-contained, not single-file: Skia and the Steam library are native `.so`s), packages
   `DW2ModLauncher-vX.Y.Z-linux-x64.tar.gz`, and attaches it to the same release.
6. `gh release create` attaches the zip to a GitHub Release with generated notes (push to `main` only).

**Pull requests do not run this workflow** (`ci.yml` validates them; packaging is only exercised on merge to `main`,
since both distributions are known to work). A **manual dispatch** builds and uploads both packages as workflow
artifacts only: no tag, no release. Use it to inspect package contents. Artifacts are named from the exe's
`InformationalVersion` (Directory.Build.props): `DW2ModLauncher-vX.Y.Z-dev-win-x64` / `-linux-x64` on a manual run (the
version the next release will carry), and the plain tag on a release. Both jobs must read `InformationalVersion`, not
MinVer's raw `MinVerVersion` (which adds `-alpha.0.N`).

**Partly verified:** the `release-linux` job has run on a PR build (its artifact name was wrong, now fixed). It has not
yet attached a tar.gz to a real release; check the first real release after it lands and fix anything it turns up.

If a run is retried after the tag exists, `release.py` sees it and exits without output, so no
duplicate release is created.

## Release contents

Unzip and run; no .NET install needed (self-contained). The runtime and `Core` are compiled into the single exe;
only files that something else loads **by path** stay loose:

```
DW2ModLauncher-vX.Y.Z-win-x64/
  DW2ModLauncher.exe
  Loader/DW2ModLauncher.Loader.dll   injected into the game via --low-level-inject (docs/DLL Injection.md)
  Languages/en.json, ja.json         UI strings
  steam_api64.dll, steam_appid.txt   Steamworks (Workshop publish), loaded by the Steam API by name/CWD
  launcher_settings.example.json, LICENSE, README.md
```

The Linux archive is the same idea as a folder (`DW2ModLauncher` executable, `Loader/`, `Languages/`,
`libsteam_api.so` + `Steamworks.NET.dll`, `steam_appid.txt`, plus the self-contained runtime and Skia natives).

The Loader DLL is added to the publish output by the `AddLoaderToPublish` target in
[DW2ModLauncher.Avalonia.csproj](../src/DW2ModLauncher.Avalonia/DW2ModLauncher.Avalonia.csproj) and marked
`ExcludeFromSingleFile`; without it `dotnet publish` would bundle it into the exe or drop it.

Trimming is not used (compiled bindings and the reflection-based mod loading make it risky). Compression is the size lever.

## Target frameworks

Core, Avalonia and Tests target `net10.0`. The **Loader stays `net8.0`**: it is loaded into the
game's own process, which runs on .NET 8, so it must not target a newer runtime. The launcher's runtime
is independent of the game's because the launcher is a separate process.

## Repo requirements

- Settings → Actions → General → Workflow permissions: **read and write** (the release workflow requests
  `contents: write` to push tags and create releases; an org-level policy can override this).
- Bump `.version` by hand for MAJOR/MINOR changes; nothing else needs touching to cut a release.
