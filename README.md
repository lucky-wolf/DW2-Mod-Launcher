# DW2 Mod Launcher BETA

A community-oriented MOD launcher for **Distant Worlds 2**.

Steam Workshop MODs and MODs installed in the game folder can be managed from one launcher.
The project is developed as a hobby project, and contributions, improvements, forks, and continued development by the community are welcome.

> This is an unofficial community project and is not affiliated with or endorsed by CodeForce, Slitherine, or Matrix Games.

---
## English

### About

**DW2 Mod Launcher BETA** is an unofficial MOD launcher for **Distant Worlds 2**.

It provides a single interface for managing MODs installed through Steam Workshop and MODs installed in the game's local MOD folders.

This is a hobby project. Community contributions, improvements, bug fixes, forks, alternate versions, and continued development are all welcome.

### Main Features

- Scan and display MODs installed in the game MOD folder
- Enable / disable MODs
- Detect duplicate MOD installations
- Check file conflicts between enabled MODs
- Check Steam Workshop MOD update status
- Display MOD information and descriptions
- Detect and open included README/manual files
- Detect included BAT/EXE tools
- Open MOD folders directly
- View and edit a MOD's settings through one schema-driven form, whether the MOD stores them in an INI file or a MOD-provided `settings.schema.json`
- Support per-MOD launch arguments
- Load code MODs via a bundled loader DLL, so a MOD's settings can be handed to it directly
- Publish a local MOD to the Steam Workshop (or push an update to one already published), and save the new item's Workshop ID into its `mod.json`
- Japanese / English UI switching

### Requirements

- Windows
- Distant Worlds 2
- Steam version recommended
- [.NET 8 SDK](https://dotnet.microsoft.com/download) for building

### Building

Download or clone the repository, then run:

```text
build.cmd
```

To build and immediately launch the application, run:

```text
run.cmd
```

Both scripts call `dotnet build` on [`DW2ModLauncher.sln`](DW2ModLauncher.sln). The project is split into
`DW2ModLauncher.Core` (mod scanning, Steam/Workshop lookups, INI/JSON helpers — no UI dependency),
`DW2ModLauncher.App` (the WinForms launcher), and `DW2ModLauncher.Tests` (unit tests for the Core logic);
see [AGENTS.md](AGENTS.md) for details. The built executable is
`src\DW2ModLauncher.App\bin\Release\net8.0-windows\DW2ModLauncherBeta.exe`.

By default, `build.cmd` (and `run.cmd`, which calls it) also fixes code formatting and runs the unit test suite —
the same checks that run in CI — before building. Pass `--no-validate` to skip both and just build, for a faster
local edit/build loop:

```text
build.cmd --no-validate
run.cmd --no-validate
```

**Before opening a pull request, run `build.cmd` without `--no-validate`.** CI (GitHub Actions) runs the same
formatting check, build, and unit tests on every PR, including from forks — if it doesn't pass locally, it won't
pass there either, and you'll wait on a red build for nothing.

### Initial Setup

On first launch, configure the paths as needed:

- Distant Worlds 2 game folder
- Steam Workshop folder for DW2
- Local/managed MOD folder

`launcher_settings.example.json` is provided as an example configuration file.

Typical Steam Workshop path:

```text
Steam\steamapps\workshop\content\1531540
```

Typical game path:

```text
Steam\steamapps\common\Distant Worlds 2
```

The actual drive and Steam Library location may be different on your system.

### Contributing

Community development is welcome.

You are free to contribute:

- Bug fixes
- UI improvements
- New features
- Code cleanup
- Documentation
- Translations
- Forks
- Alternate versions

Pull Requests / Merge Requests, Issues, and suggestions are welcome.

Before opening a PR, run `build.cmd` (without `--no-validate`) — see [Building](#building) — so formatting, build,
and tests are checked locally first, the same way CI checks them.

There is no guarantee that the original developer will maintain this project indefinitely.
If maintenance stops, the community is welcome to continue development under the terms of the MIT License.

### License

This project is released under the **MIT License**.

See [`LICENSE`](LICENSE) for details.

---

## Disclaimer

Distant Worlds 2 and related names and assets belong to their respective owners.
This launcher is an unofficial fan/community project.
