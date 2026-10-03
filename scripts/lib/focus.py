"""docs/focus.md: the running list of what a branch has accomplished, used to build the PR description.

Everything below the first line that is exactly '---' is the list; each '- ' line is one entry.
new-branch.py resets the file (header kept, entries dropped) when it starts fresh work.
"""

from pathlib import Path

FOCUS_PATH = Path("docs") / "focus.md"

HEADER = """# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---
"""


def read_entries(repo_root: Path) -> list[str]:
    """The '- ' lines below the '---', with the bullet marker stripped. Empty if the file is missing or has none."""
    path = repo_root / FOCUS_PATH
    if not path.exists():
        return []
    lines = path.read_text(encoding="utf-8").splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line.strip() == "---") + 1
    except StopIteration:
        return []
    return [line.strip()[2:].strip() for line in lines[start:] if line.strip().startswith("- ") and line.strip()[2:].strip()]


def reset(repo_root: Path) -> None:
    """Drops every entry, rewriting the standard header."""
    (repo_root / FOCUS_PATH).write_text(HEADER, encoding="utf-8", newline="\n")
