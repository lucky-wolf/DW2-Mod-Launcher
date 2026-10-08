"""Release-version rules shared by release.py (CI), open-pr.py and new-branch.py.
usage: from lib import release_version; release_version.next_tag(repo_root)

The tag scheme is vMAJOR.MINOR.PATCH: MAJOR.MINOR comes from .version (hand-bumped), PATCH is one
past the latest existing v{MAJOR.MINOR}.* tag. See docs/CI Releases.md.
"""

import re
import subprocess
from pathlib import Path

TAG_PREFIX = "v"

# Paths whose change on main makes .github/workflows/release.yml cut a release. Keep in sync with
# that workflow's `paths:` filters (prefix match; a trailing "/" means a directory).
RELEASE_PATHS = ("src/", "Directory.Build.props", ".version")


def _git(repo_root: Path, *args: str) -> str:
    result = subprocess.run(["git", *args], cwd=repo_root, capture_output=True, text=True)
    return result.stdout if result.returncode == 0 else ""


def read_major_minor(repo_root: Path) -> str | None:
    """MAJOR.MINOR from .version, or None when missing/malformed."""
    version_file = repo_root / ".version"
    if not version_file.is_file():
        return None
    lines = version_file.read_text(encoding="utf-8").splitlines()
    value = lines[0].strip() if lines else ""
    return value if re.fullmatch(r"\d+\.\d+", value) else None


def next_tag(repo_root: Path) -> str | None:
    """The tag release.py will create next, from locally-known tags (`git fetch --tags` first if
    that might be stale). None when .version is unusable."""
    major_minor = read_major_minor(repo_root)
    if major_minor is None:
        return None
    pattern = re.compile(rf"{TAG_PREFIX}{re.escape(major_minor)}\.(\d+)")
    patches = [
        int(m.group(1))
        for t in _git(repo_root, "tag", "--list", f"{TAG_PREFIX}{major_minor}.*").splitlines()
        if (m := pattern.fullmatch(t.strip()))
    ]
    return f"{TAG_PREFIX}{major_minor}.{max(patches) + 1 if patches else 0}"


def tag_exists(repo_root: Path, tag: str) -> bool:
    return bool(_git(repo_root, "tag", "--list", tag).strip())


def release_tag_at_head(repo_root: Path) -> str | None:
    """The vMAJOR.MINOR.PATCH tag pointing exactly at HEAD, if any."""
    for tag in _git(repo_root, "tag", "--points-at", "HEAD").splitlines():
        if re.fullmatch(rf"{TAG_PREFIX}\d+\.\d+\.\d+", tag.strip()):
            return tag.strip()
    return None


def head_triggers_release(repo_root: Path) -> bool:
    """Whether HEAD (main's tip after a merge) has release-path changes that no release tag covers
    yet, i.e. whether the release workflow will tag it. Compares against the latest release tag
    rather than HEAD~1: a rebase-merge lands a multi-commit PR as several commits, and the last
    one alone may not touch a release path. No tag yet at all means the first release is pending."""
    last_tag = _git(repo_root, "describe", "--tags", "--abbrev=0", "--match", f"{TAG_PREFIX}[0-9]*").strip()
    if not last_tag:
        return True
    changed = _git(repo_root, "diff", "--name-only", last_tag, "HEAD").splitlines()
    return touches_release_path(changed)


def touches_release_path(files: list[str]) -> bool:
    return any(f == p or (p.endswith("/") and f.startswith(p)) for f in files for p in RELEASE_PATHS)


def branch_will_release(repo_root: Path, target: str) -> bool:
    """Whether merging the current branch into `target` will make the release workflow cut a release:
    the same path test as the workflow's `paths:` filter, over everything the branch changes relative
    to origin/<target> (committed, uncommitted and untracked, since open-pr.py may commit those next).
    Assumes a release when the merge base can't be found, so the title errs toward the usual one."""
    base = _git(repo_root, "merge-base", f"origin/{target}", "HEAD").strip()
    if not base:
        return True
    changed = _git(repo_root, "diff", "--name-only", base).splitlines()
    changed += _git(repo_root, "ls-files", "--others", "--exclude-standard").splitlines()
    return touches_release_path(changed)
