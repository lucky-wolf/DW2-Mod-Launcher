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

from . import dotnet_checks, output, proc

TEST_PROJECT = dotnet_checks.TEST_PROJECT


@dataclass(frozen=True)
class Ui:
    name: str
    project: str
    apphost_suffix: str  # appended to the assembly name for the executable ("" or ".exe")


AVALONIA = Ui("avalonia", "src/DW2ModLauncher.Avalonia/DW2ModLauncher.Avalonia.csproj", ".exe" if sys.platform == "win32" else "")
UIS = {ui.name: ui for ui in (AVALONIA,)}


def launcher_is_running() -> bool:
    """True while a DW2ModLauncher process is alive. A running launcher locks its own exe, which breaks the release build."""
    try:
        if sys.platform == "win32":
            result = subprocess.run(
                ["tasklist", "/FI", "IMAGENAME eq DW2ModLauncher.exe", "/FO", "CSV", "/NH"], capture_output=True, text=True
            )
            return "DW2ModLauncher.exe" in result.stdout
        return subprocess.run(["pgrep", "-x", "DW2ModLauncher"], capture_output=True).returncode == 0
    except OSError:
        return False  # no tasklist/pgrep to ask: don't block the PR on a check we can't make


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


def build(repo_root: Path, ui: Ui, config: str, validate: bool, dry_run: bool = False) -> Path:
    """Restore, (unless validate is False) apply `dotnet format` and run the tests, build the launcher, and
    return the path of its executable. dry_run prints the steps and builds nothing."""
    plan = ["dotnet restore"]
    if validate:
        plan.append("dotnet format (applied)")
    plan.append(f"dotnet build {ui.project} -c {config}")
    if validate:
        plan.append(f"dotnet test {TEST_PROJECT}")
    if dry_run:
        output.notice(f"dry run - would build the {ui.name} launcher: " + " -> ".join(plan))
        return repo_root / Path(ui.project).parent / "bin" / config

    output.step("restore")
    dotnet_checks.run_dotnet_restore(repo_root)

    if validate:
        output.step("format")
        dotnet_checks.apply_dotnet_format(repo_root)
        output.ok("dotnet format")

    output.step(f"build ({ui.name}, {config})")
    proc.run(repo_root, f"dotnet build {ui.project}", ["dotnet", "build", ui.project, "-c", config, "--no-restore"])
    output.ok("dotnet build")

    if validate:
        output.step("test")
        # builds the tests too (the launcher project doesn't reference them), unlike validate.py's --no-build
        proc.run(repo_root, f"dotnet test {TEST_PROJECT}", ["dotnet", "test", TEST_PROJECT, "-c", config, "--no-restore"])
        output.ok("dotnet test")

    exe = executable_path(repo_root, ui, config)
    if not exe.exists():
        output.fail(f"build succeeded but the executable is missing: {exe}")
    output.done(f"built {exe.relative_to(repo_root) if exe.is_relative_to(repo_root) else exe}")
    return exe


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
    parser.add_argument("--dry-run", action="store_true", help="print what would happen; build and run nothing.")
    output.add_color_argument(parser)
