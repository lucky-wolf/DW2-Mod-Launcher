"""docs/focus.md: the running list of what a branch has accomplished, used to build the PR description.

Everything below the first line that is exactly '---' is the list; each '- ' line is one entry.
open-pr.py resets the file (header kept, entries dropped) in the PR's last commit, once the list is in the PR
 description, so main never carries entries; new-branch.py resets it too when it starts fresh work.
"""

import subprocess
from pathlib import Path

FOCUS_PATH = Path("docs") / "focus.md"

HEADER = """# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/open-pr.py` empties the list (in the PR's last commit) once it is in the PR description, so it never holds over;
`scripts/new-branch.py` also empties it when it creates a new branch.

---
"""


def parse_entries(text: str) -> list[str]:
    """The '- ' lines below the '---' of a focus.md's text, with the bullet marker stripped."""
    lines = text.splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line.strip() == "---") + 1
    except StopIteration:
        return []
    return [line.strip()[2:].strip() for line in lines[start:] if line.strip().startswith("- ") and line.strip()[2:].strip()]


def read_entries(repo_root: Path) -> list[str]:
    """The entries of the working-tree focus.md. Empty if the file is missing or has none."""
    path = repo_root / FOCUS_PATH
    if not path.exists():
        return []
    return parse_entries(path.read_text(encoding="utf-8"))


def _entries_at(repo_root: Path, rev: str) -> list[str]:
    shown = subprocess.run(
        ["git", "show", f"{rev}:{FOCUS_PATH.as_posix()}"], cwd=repo_root, capture_output=True, encoding="utf-8"
    )
    return parse_entries(shown.stdout) if shown.returncode == 0 else []


def same_as_last_release(repo_root: Path, target: str) -> bool:
    """True when the working-tree entries exactly match focus.md as of the newest tag reachable from origin/<target>,
    i.e. the list was never cleared after that release."""
    described = subprocess.run(
        ["git", "describe", "--tags", "--abbrev=0", f"origin/{target}"], cwd=repo_root, capture_output=True, encoding="utf-8"
    )
    if described.returncode != 0:
        return False
    entries = read_entries(repo_root)
    return bool(entries) and entries == _entries_at(repo_root, described.stdout.strip())


def stash_entries(repo_root: Path, stash_sha: str) -> list[str]:
    """The entries a stash added to focus.md: those in the stashed file but not in the commit it was
    made on. Everything already at that base belongs to earlier, merged work, so what's left is new.
    Lets new-branch carry them onto a fresh branch without ever merging the file itself."""
    base = set(_entries_at(repo_root, f"{stash_sha}^1"))
    return [entry for entry in _entries_at(repo_root, stash_sha) if entry not in base]


def append(repo_root: Path, entries: list[str]) -> None:
    """Adds each entry not already listed, after the existing ones."""
    path = repo_root / FOCUS_PATH
    if not path.exists():
        reset(repo_root)
    present = set(read_entries(repo_root))
    missing = [entry for entry in entries if entry not in present]
    if not missing:
        return
    text = path.read_text(encoding="utf-8")
    if not text.endswith("\n"):
        text += "\n"
    text += "".join(f"- {entry}\n" for entry in missing)
    path.write_text(text, encoding="utf-8", newline="\n")


def reset(repo_root: Path) -> None:
    """Drops every entry, rewriting the standard header."""
    (repo_root / FOCUS_PATH).write_text(HEADER, encoding="utf-8", newline="\n")
