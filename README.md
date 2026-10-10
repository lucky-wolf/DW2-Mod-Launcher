# DW2 Mod Launcher

**English** | [日本語](README.ja.md)

A community-oriented mod launcher for **Distant Worlds 2**.

Steam Workshop Mods and Mods installed in the game folder can be managed from one launcher.
The project is developed as a hobby project, and contributions, improvements, forks, and continued development by the community are welcome.

> This is an unofficial community project and is not affiliated with or endorsed by CodeForce, Slitherine, or Matrix Games.

- [DW2 Mod Launcher](#dw2-mod-launcher)
  - [About](#about)
  - [Main Features](#main-features)
  - [Requirements](#requirements)
  - [Building](#building)
  - [Initial Setup](#initial-setup)
  - [Linux](#linux)
  - [Contributing](#contributing)
  - [License](#license)
  - [Disclaimer](#disclaimer)

---

## About

**DW2 Mod Launcher** is an unofficial Mod launcher for **Distant Worlds 2**.

It provides a single interface for managing Mods installed through Steam Workshop and Mods installed in the game's local Mod folders.

This is a hobby project. Community contributions, improvements, bug fixes, forks, alternate versions, and continued development are all welcome.

## Main Features

- Japanese, English & Russian
  - Translations for more languages are welcome!
- Windows & Linux native support
- Drag & drop your active mods
  - Full control over load order
  - Automatic conflict detection
  - Provides a configuration UI (for mods that support it)
- Run mods that use DLL injection automatically
  - Allows multiple mods to include DLLs seamlessly
- Manage any mods for DW2 of your own
  - Create new mods
  - Upload or update your mods to steam directly
  - Test your mods tha use DLL injection
  - Create a specification that users can easily configure your mod to their tastes

## Requirements

- Windows, or Linux (the game runs under Proton; see [Linux](#linux))
- Distant Worlds 2
- Steam version recommended
- [.NET 10 SDK](https://dotnet.microsoft.com/download) for building (the only SDK you need)

The launcher itself targets .NET 10 and is released self-contained, so players need no .NET install. The
injected loader DLL (`DW2ModLauncher.Loader`) targets .NET 8, because it runs inside Distant Worlds 2, which
currently requires .NET 8. The .NET 10 SDK fully supports targeting 8, so you don't need the .NET 8 SDK as well.

## Building

On any OS (needs Python 3 and the .NET 10 SDK), `scripts/build.py` builds the launcher for the OS you are on —
the Avalonia launcher — after fixing formatting and running the unit
tests; `scripts/run.py` does the same and then starts it:

```text
python3 scripts/run.py                # build + run (python scripts\run.py on Windows)
python3 scripts/run.py --no-validate  # skip the format fix and tests for a faster loop
python3 scripts/build.py              # build only
```

On Windows you can also use the batch files. Download or clone the repository, then run:

```text
build.cmd
```

To build and immediately launch the application, run:

```text
run.cmd
```

Both scripts call `dotnet build` on [`DW2ModLauncher.sln`](DW2ModLauncher.sln). The project is split into
`DW2ModLauncher.Core` (mod scanning, Steam/Workshop lookups, JSON helpers — no UI dependency),
`DW2ModLauncher.Avalonia` (the launcher UI), and `DW2ModLauncher.Tests` (unit tests for the Core logic);
see [AGENTS.md](AGENTS.md) for details. The built executable is
`src\DW2ModLauncher.Avalonia\bin\Release\net10.0\DW2ModLauncher.exe`.

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

## Initial Setup

On first launch, configure the paths as needed:

- Distant Worlds 2 game folder
- Steam Workshop folder for DW2
- Local/managed Mod folder

Typical Steam Workshop path:

```text
Steam\steamapps\workshop\content\1531540
```

Typical game path:

```text
Steam\steamapps\common\Distant Worlds 2
```

The actual drive and Steam Library location may be different on your system.

## Linux

Distant Worlds 2 has no native Linux build, so on Linux the game runs under Steam's Proton as usual; the
launcher itself is a native Linux app. Download the `linux-x64` `.tar.gz` from the releases page, extract it
anywhere, and run `./DW2ModLauncher`. No .NET install is needed.

- Steam must be installed and running. **Play** starts the game through Steam (`steam -applaunch 1531540`),
  so Proton, the right prefix and your Steam launch settings all apply.
- Steam, the game and the Workshop folder are found automatically in the usual places (`~/.steam/steam`,
  `~/.local/share/Steam`, Flatpak and Snap installs, and your other Steam library folders). Use the Settings
  tab if yours are elsewhere.
- Publishing to the Steam Workshop talks to your running, logged-in Linux Steam client directly.
- The launcher keeps its settings (`launcher_settings.json`), profiles and log in `~/.config/DW2ModLauncher` (on Windows, `%AppData%\DW2ModLauncher`).

To build and run from source on Linux (needs only the .NET 10 SDK):

```text
dotnet run --project src/DW2ModLauncher.Avalonia
```

## Contributing

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

## License

This project is released under the **MIT License**.

See [`LICENSE`](LICENSE) for details.

**Steamworks native libraries.** Workshop publishing needs Valve's Steam API native library
(`steam_api64.dll` on Windows, `libsteam_api.so` on Linux). These are not covered by the MIT License.
They are Valve's redistributable binaries, taken unmodified from the official Steamworks SDK and
bundled so the launcher can talk to the user's own Steam client. They remain subject to the
Steamworks SDK terms.

---

## Disclaimer

Distant Worlds 2 and related names and assets belong to their respective owners.
This launcher is an unofficial fan/community project.
