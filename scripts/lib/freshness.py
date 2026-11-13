"""Whether a build output is older than its inputs, by modification time, so a step is only run when something changed.

This is a make-style check by hand (the same idea as DW2-XL's scripts/dw2/freshness.py): an output is stale when it is missing, or when any
input file is newer than the oldest output. It is only as good as the input list below, and a file restored from git or a backup keeps its
old time, so build.py also takes --force. The version MinVer stamps comes from git tags and commits, which this does not watch: --force
after tagging if the version number matters.
"""

from pathlib import Path

# build output and editor folders, never inputs
SKIPPED_DIRS = {"bin", "obj", ".vs", "node_modules", "__pycache__"}

# repo-root files every project is built from
ROOT_INPUTS = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig", ".version",
               "DW2ModLauncher.sln", "global.json", "nuget.config")


def _walk(root: Path):
    for path in root.rglob("*"):
        if path.is_file() and not any(part.lower() in SKIPPED_DIRS for part in path.relative_to(root).parts[:-1]):
            yield path


def build_inputs(repo_root: Path) -> list[Path]:
    """Everything the launcher, the Loader and the tests are built from: all of src/ and third_party/ except build output, and the repo-root build files."""
    files = []
    for folder in ("src", "third_party"):
        if (repo_root / folder).is_dir():
            files += list(_walk(repo_root / folder))
    files += [repo_root / name for name in ROOT_INPUTS if (repo_root / name).is_file()]
    return files


def stale_reason(outputs: list[Path], inputs: list[Path]) -> str | None:
    """Why the outputs need rebuilding, or None when they are up to date."""
    missing = [p.name for p in outputs if not p.is_file()]
    if missing:
        return f"{', '.join(missing)} does not exist"
    built = min(p.stat().st_mtime for p in outputs)
    newest = max(inputs, key=lambda p: p.stat().st_mtime, default=None)
    if newest is not None and newest.stat().st_mtime > built:
        return f"{newest.name} is newer than the output"
    return None


def touch(stamp: Path) -> None:
    """Record now as the time a step last succeeded (the stamp is an output for stale_reason)."""
    stamp.parent.mkdir(parents=True, exist_ok=True)
    stamp.write_text("", encoding="utf-8", newline="\n")
    stamp.touch()
