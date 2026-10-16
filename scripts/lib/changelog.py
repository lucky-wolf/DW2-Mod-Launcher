"""changelog.txt (repo root): the running record of what every release changed, newest first, in the same layout the mods use.

    ## v1.0.15
    - what the branch for that version has done so far

    ## v1.0.14
    - ...

The top section is the work in progress: new-branch.py starts it (a heading for the next release tag above the old ones) and
open-pr.py reads its '- ' lines as the PR description. Nothing is ever emptied or deleted, so the file grows into the full history.
Contributors' merged PRs leave their own section on main, which is why a rebase rebuilds the file from main plus this branch's lines.
"""

import subprocess
from pathlib import Path

from . import release_version

CHANGELOG_PATH = Path("changelog.txt")

HEADING_PREFIX = "## "


class Section:
    """A '## heading' and the lines under it; heading is None for anything before the first heading."""

    def __init__(self, heading: str | None, lines: list[str] | None = None):
        self.heading = heading
        self.lines = lines or []

    def entries(self) -> list[str]:
        return [line.strip()[2:].strip() for line in self.lines if line.strip().startswith("- ") and line.strip()[2:].strip()]


def parse(text: str) -> list[Section]:
    sections = [Section(None)]
    for line in text.replace("\r\n", "\n").split("\n"):
        if line.startswith(HEADING_PREFIX):
            sections.append(Section(line[len(HEADING_PREFIX):].strip()))
        else:
            sections[-1].lines.append(line)
    return sections


def _trimmed(lines: list[str]) -> list[str]:
    start, end = 0, len(lines)
    while start < end and not lines[start].strip():
        start += 1
    while end > start and not lines[end - 1].strip():
        end -= 1
    return [line.rstrip() for line in lines[start:end]]


def render(sections: list[Section]) -> str:
    parts = []
    for section in sections:
        body = _trimmed(section.lines)
        if section.heading is None:
            if body:
                parts.append("\n".join(body))
            continue
        parts.append("\n".join([HEADING_PREFIX + section.heading, *body]))
    return "\n\n".join(parts) + "\n"


def top_section(text: str) -> Section | None:
    """The newest section: the first one with a heading."""
    return next((s for s in parse(text) if s.heading is not None), None)


def _section_named(text: str, heading: str | None) -> Section | None:
    return next((s for s in parse(text) if s.heading is not None and s.heading == heading), None)


def _read(repo_root: Path) -> str:
    path = repo_root / CHANGELOG_PATH
    return path.read_text(encoding="utf-8") if path.exists() else ""


def _write(repo_root: Path, text: str) -> None:
    (repo_root / CHANGELOG_PATH).write_text(text, encoding="utf-8", newline="\n")


def _text_at(repo_root: Path, rev: str) -> str:
    shown = subprocess.run(
        ["git", "show", f"{rev}:{CHANGELOG_PATH.as_posix()}"], cwd=repo_root, capture_output=True, encoding="utf-8"
    )
    return shown.stdout if shown.returncode == 0 else ""


def current_heading(repo_root: Path) -> str | None:
    """The heading of the working-tree file's top section (the version being worked on), None when there is none."""
    top = top_section(_read(repo_root))
    return top.heading if top else None


def read_entries(repo_root: Path) -> list[str]:
    """The '- ' lines of the working-tree file's top section, bullet marker stripped. Empty when the file or section is missing."""
    top = top_section(_read(repo_root))
    return top.entries() if top else []


def _added(text: str, base_text: str) -> list[str]:
    """The top section's entries in `text` that the same-named section of `base_text` does not have."""
    top = top_section(text)
    if top is None:
        return []
    base = _section_named(base_text, top.heading)
    already = set(base.entries()) if base else set()
    return [entry for entry in top.entries() if entry not in already]


def entries_added_since(repo_root: Path, rev: str) -> list[str]:
    """This branch's own entries: the working-tree top section's lines that the same section at `rev` did not have
    (committed on the branch or not)."""
    return _added(_read(repo_root), _text_at(repo_root, rev))


def stash_entries(repo_root: Path, stash_sha: str) -> list[str]:
    """The entries a stash added to the top section: those in the stashed file but not in the commit it was made on.
    Lets new-branch carry them onto a fresh branch without ever merging the file itself."""
    return _added(_text_at(repo_root, stash_sha), _text_at(repo_root, f"{stash_sha}^1"))


def is_released(repo_root: Path) -> bool:
    """True when the top section's heading names a release tag that already exists: the section is finished work,
    so a branch that never started its own is about to describe its PR with someone else's lines."""
    heading = current_heading(repo_root)
    return heading is not None and release_version.tag_exists(repo_root, heading)


def start_section(repo_root: Path, heading: str) -> None:
    """Puts a section with this heading on top, above the older ones. Does nothing when the top section already has it
    (an earlier branch for the same version that never released, e.g. a housekeeping one)."""
    text = _read(repo_root)
    top = top_section(text)
    if top is not None and top.heading == heading:
        return
    sections = parse(text)
    first = next((i for i, s in enumerate(sections) if s.heading is not None), len(sections))
    sections.insert(first, Section(heading))
    _write(repo_root, render(sections))


def append(repo_root: Path, entries: list[str]) -> None:
    """Adds each entry the top section does not list yet, after its existing lines."""
    top = top_section(_read(repo_root))
    if top is None:
        raise RuntimeError(f"{CHANGELOG_PATH} has no '## <version>' section to add entries to - start_section first")
    present = set(top.entries())
    missing = []
    for entry in entries:
        if entry not in present:
            present.add(entry)
            missing.append(entry)
    if not missing:
        return
    sections = parse(_read(repo_root))
    section = next(s for s in sections if s.heading is not None)
    section.lines = _trimmed(section.lines) + [f"- {entry}" for entry in missing]
    _write(repo_root, render(sections))


def rebuild_from(repo_root: Path, rev: str, heading: str, entries: list[str]) -> None:
    """The file as it is at `rev`, plus a section for `heading` on top holding `entries`. What a rebase leaves behind."""
    _write(repo_root, _text_at(repo_root, rev))
    start_section(repo_root, heading)
    append(repo_root, entries)
