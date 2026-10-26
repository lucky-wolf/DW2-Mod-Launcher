"""Builds the launcher for the current OS. Shared by scripts/build.py and scripts/run.py.
usage: from lib import launcher_build; exe = launcher_build.build(repo_root, ui, config, validate)

Which launcher is "the current OS's target" lives here in one place: the Avalonia launcher on every
OS (the WinForms app it replaced has been removed).
"""

import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from . import dotnet_checks, mod_sync, output, proc

TEST_PROJECT = dotnet_checks.TEST_PROJECT


@dataclass(frozen=True)
class Ui:
    name: str
    project: str
    apphost_suffix: str  # appended to the assembly name for the executable ("" or ".exe")


AVALONIA = Ui("avalonia", "src/DW2ModLauncher.Avalonia/DW2ModLauncher.Avalonia.csproj", ".exe" if sys.platform == "win32" else "")
UIS = {ui.name: ui for ui in (AVALONIA,)}


def _process_is_running(windows_image: str, unix_pattern: str) -> bool:
    """True while a process with that name is alive (tasklist on Windows; pgrep elsewhere, where Wine/Proton cut names to 15 characters)."""
    try:
        if sys.platform == "win32":
            result = subprocess.run(
                ["tasklist", "/FI", f"IMAGENAME eq {windows_image}", "/FO", "CSV", "/NH"], capture_output=True, text=True
            )
            return windows_image in result.stdout
        return subprocess.run(["pgrep", "-f", unix_pattern], capture_output=True).returncode == 0
    except OSError:
        return False  # no tasklist/pgrep to ask: don't block the PR on a check we can't make


def _launcher_paths() -> list[Path | None]:
    """The executable path of every running DW2ModLauncher process (None where the OS would not tell us)."""
    if sys.platform == "win32":
        result = subprocess.run(
            ["powershell", "-NoProfile", "-Command", "Get-Process -Name DW2ModLauncher -ErrorAction SilentlyContinue | ForEach-Object { if ($_.Path) { $_.Path } else { '?' } }"],
            capture_output=True, text=True,
        )
        return [None if line.strip() == "?" else Path(line.strip()) for line in result.stdout.splitlines() if line.strip()]
    result = subprocess.run(["pgrep", "-f", "^(.*/)?DW2ModLauncher$"], capture_output=True, text=True)
    paths: list[Path | None] = []
    for pid in result.stdout.split():
        try:
            paths.append(Path(os.readlink(f"/proc/{pid}/exe")))
        except OSError:
            paths.append(None)
    return paths


def launcher_is_running(repo_root: Path | None = None) -> bool:
    """True while a launcher that this build would overwrite is alive. A running launcher locks its own exe, which breaks the
    build, but only the copy in this repo's output folders: a release edition installed elsewhere has its own files and does
    not matter. A process whose path cannot be read counts as running, since it might be the one in the way."""
    root = (repo_root or Path(__file__).resolve().parents[2]).resolve()
    try:
        paths = _launcher_paths()
    except OSError:
        return False  # no powershell/pgrep to ask: don't block the build on a check we can't make
    return any(path is None or path.resolve().is_relative_to(root) for path in paths)


def game_is_running() -> bool:
    """True while Distant Worlds 2 is alive. The tests refuse to save a load order while it runs, and it keeps mod DLLs locked."""
    return _process_is_running("DistantWorlds2.exe", "DistantWorlds2")


def require_launcher_closed() -> None:
    """Fail immediately if the launcher is running: it locks its own exe, and MSBuild would otherwise retry the copy
    for many seconds before failing with a less obvious error."""
    output.step("launcher not running")
    if launcher_is_running():
        output.fail("A DW2 Mod Launcher started from this repo is running - close it first (it locks its own exe and breaks the build), then re-run. A launcher installed elsewhere does not matter.")
    output.ok("launcher is closed")


def require_game_closed() -> None:
    """Fail immediately if Distant Worlds 2 is running: it locks mod DLLs and makes the load-order tests fail."""
    output.step("game not running")
    if game_is_running():
        output.fail("Distant Worlds 2 is running (it locks mod DLLs and fails the load-order tests) - exit the game first, then re-run")
    output.ok("game is closed")


def default_ui() -> Ui:
    return AVALONIA


def resolve_ui(name: str) -> Ui:
    """"auto" and "avalonia" both give the Avalonia launcher (the flag is kept so existing invocations work)."""
    return default_ui() if name == "auto" else UIS[name]


def _msbuild_properties(repo_root: Path, ui: Ui, config: str, *names: str) -> dict[str, str]:
    args = ["dotnet", "msbuild", ui.project, f"-p:Configuration={config}"] + [f"-getProperty:{n}" for n in names]
    result = subprocess.run(args, cwd=repo_root, capture_output=True, text=True)
    if result.returncode != 0:
        output.fail(f"could not query {ui.project} (exit {result.returncode}):\n{result.stdout}{result.stderr}")
    data = json.loads(result.stdout)
    return data["Properties"] if "Properties" in data else {names[0]: result.stdout.strip()}


def executable_path(repo_root: Path, ui: Ui, config: str) -> Path:
    props = _msbuild_properties(repo_root, ui, config, "TargetDir", "AssemblyName")
    return Path(props["TargetDir"]) / (props["AssemblyName"] + ui.apphost_suffix)  # TargetDir is absolute


def build(repo_root: Path, ui: Ui, config: str, validate: bool, dry_run: bool = False, sync_mod: bool = True,
          game_path: Path | None = None) -> Path:
    """Restore, (unless validate is False) apply `dotnet format` and run the tests, build the launcher, and
    return the path of its executable. Then (unless sync_mod is False) brings the launcher's own Workshop item, the local mod folder
    under the game's mods folder, in step with the repo (see mod_sync). dry_run prints the steps and builds nothing."""
    plan = ["dotnet restore"]
    if validate:
        plan.append("dotnet format (applied)")
    plan.append(f"dotnet build {ui.project} -c {config}")
    if validate:
        plan.append(f"dotnet test {TEST_PROJECT}")
    if dry_run:
        output.notice(f"dry run - would build the {ui.name} launcher: " + " -> ".join(plan))
        return repo_root / Path(ui.project).parent / "bin" / config

    require_launcher_closed()
    # A running game has the Loader DLL (the copy in the launcher's output folder) loaded and locked, and the load-order tests
    # refuse to run beside it. Those are the only two things that need the game closed, so with it open they are skipped and
    # everything else is built and deployed.
    game_open = game_is_running()
    if game_open:
        output.notice("Distant Worlds 2 is running: the tests and the Loader DLL update are skipped; everything else is built")

    output.step("restore")
    dotnet_checks.run_dotnet_restore(repo_root)

    if validate:
        output.step("format")
        dotnet_checks.apply_dotnet_format(repo_root)
        output.ok("dotnet format")

    output.step(f"build ({ui.name}, {config})")
    build_args = ["dotnet", "build", ui.project, "-c", config, "--no-restore"]
    if game_open:
        build_args.append("-p:SkipLoaderCopy=true")
    proc.run(repo_root, f"dotnet build {ui.project}", build_args)
    output.ok("dotnet build")

    if validate and not game_open:
        output.step("test")
        # builds the tests too (the launcher project doesn't reference them), unlike validate.py's --no-build
        proc.run(repo_root, f"dotnet test {TEST_PROJECT}", ["dotnet", "test", TEST_PROJECT, "-c", config, "--no-restore"])
        output.ok("dotnet test")

    exe = executable_path(repo_root, ui, config)
    if not exe.exists():
        output.fail(f"build succeeded but the executable is missing: {exe}")
    if game_open:
        _report_loader(repo_root, config, exe)
    if sync_mod:
        mod_sync.sync(repo_root, game_path)
    output.done(f"built {exe.relative_to(repo_root) if exe.is_relative_to(repo_root) else exe}")
    return exe


def _report_loader(repo_root: Path, config: str, exe: Path) -> None:
    """With the game open the deployed Loader DLL was not updated: say whether the freshly built one differs from it."""
    built = repo_root / "src" / "DW2ModLauncher.Loader" / "bin" / config / "net8.0" / "DW2ModLauncher.Loader.dll"
    deployed = exe.parent / "Loader" / "DW2ModLauncher.Loader.dll"
    if not built.is_file() or not deployed.is_file():
        output.warn(f"could not compare the Loader DLL ({built} / {deployed}): one of them is missing")
    elif built.read_bytes() == deployed.read_bytes():
        output.ok("Loader DLL unchanged, nothing was skipped")
    else:
        output.warn("the Loader DLL changed and was NOT deployed (the game has it loaded): close the game and build again to update it")


def add_arguments(parser) -> None:
    """The flags build.py and run.py share."""
    parser.add_argument(
        "--ui",
        choices=["auto", *UIS],
        default="auto",
        help=f"which launcher to build. Default: auto ({AVALONIA.name}).",
    )
    parser.add_argument("--config", default="Release", help="build configuration. Default: Release.")
    parser.add_argument(
        "--no-validate",
        action="store_true",
        help="skip the formatting fix and the unit tests; just build (faster edit/build loop).",
    )
    parser.add_argument(
        "--no-mod-sync",
        action="store_true",
        help="do not sync description.bbcode and changelog entries into the game's mods/DW2 Mod Launcher folder after building.",
    )
    parser.add_argument(
        "--game-path",
        type=Path,
        default=None,
        help="the Distant Worlds 2 install the mod folder sync uses. Default: DW2_PATH, then the usual Steam location.",
    )
    parser.add_argument("--dry-run", action="store_true", help="print what would happen; build and run nothing.")
    output.add_color_argument(parser)
