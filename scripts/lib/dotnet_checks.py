"""Canonical .NET verbs (format check, build, test) shared by scripts/*.py.
usage: from lib import dotnet_checks; dotnet_checks.run_dotnet_build(repo_root)

Each verb is defined exactly once here and used identically by scripts/validate.py (what
.github/workflows/ci.yml runs) and open-pr.py (local pre-flight), so CI and local tooling can't
drift apart.
"""

import re
import subprocess
from pathlib import Path

from . import output

SOLUTION_FILE = "DW2ModLauncher.sln"
TEST_PROJECT = "src/DW2ModLauncher.Tests"


def run_dotnet_restore(repo_root: Path) -> None:
    print("  » dotnet restore")
    result = subprocess.run(["dotnet", "restore", SOLUTION_FILE], cwd=repo_root)
    if result.returncode != 0:
        output.fail(f"dotnet restore failed (exit {result.returncode})")
    output.ok("dotnet restore")


_FORMAT_ISSUE_RE = re.compile(r"^(?P<file>.+?)\(\d+,\d+\): \w+ \w+: ", re.MULTILINE)


def check_dotnet_format(repo_root: Path) -> bool:
    """Runs `dotnet format --verify-no-changes` and returns whether it passed, rather than failing
    outright: CI must fail immediately, while open-pr.py offers to run `dotnet format` first.
    On failure the per-line diagnostics aren't actionable (the fix is always re-running
    `dotnet format`), so the output collapses to the distinct file list."""
    print("  » dotnet format --verify-no-changes")
    result = subprocess.run(
        ["dotnet", "format", SOLUTION_FILE, "--verify-no-changes", "--no-restore"],
        cwd=repo_root,
        capture_output=True,
        text=True,
    )
    if result.returncode == 0:
        output.ok("dotnet format")
        return True

    combined = result.stdout + result.stderr
    files = sorted({m.group("file").strip() for m in _FORMAT_ISSUE_RE.finditer(combined)})
    if files:
        print(f"  {len(files)} file(s) need formatting:")
        for f in files:
            print(f"    {f}")
    else:
        # Unexpected output shape (a real error, not diagnostics) - show it raw.
        print(combined.strip())
    return False


def apply_dotnet_format(repo_root: Path) -> None:
    print("  » dotnet format")
    result = subprocess.run(["dotnet", "format", SOLUTION_FILE, "--no-restore"], cwd=repo_root)
    if result.returncode != 0:
        output.fail(f"dotnet format failed (exit {result.returncode})")


def run_dotnet_build(repo_root: Path) -> None:
    print("  » dotnet build")
    result = subprocess.run(["dotnet", "build", SOLUTION_FILE, "-c", "Release", "--no-restore"], cwd=repo_root)
    if result.returncode != 0:
        output.fail(f"dotnet build failed (exit {result.returncode})")
    output.ok("dotnet build")


def run_dotnet_test(repo_root: Path) -> None:
    print("  » dotnet test")
    result = subprocess.run(["dotnet", "test", TEST_PROJECT, "-c", "Release", "--no-build"], cwd=repo_root)
    if result.returncode != 0:
        output.fail(f"dotnet test failed (exit {result.returncode})")
    output.ok("dotnet test")
