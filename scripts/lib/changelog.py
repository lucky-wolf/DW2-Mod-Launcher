"""changelog.txt (repo root): the running record of what every release changed, newest first, in the same layout the mods use.

    ## Unreleased
    - what the branch has done so far

    ## 1.0.14 (2026-10-01)
    - ...

The top section is always "Unreleased": it is the work in progress, and open-pr.py reads its '- ' lines as the PR description.
Nothing here stamps it. The launcher stamps it with the version and date when it publishes (on the copy in the mod folder, which
lib/mod_sync.py pulls back into this file). Contributors' merged PRs add lines to Unreleased on main, which is why a rebase rebuilds the
file from main plus this branch's lines.
"""

import subprocess
from pathlib import Path

CHANGELOG_PATH = Path("changelog.txt")

HEADING_PREFIX = "## "
UNRELEASED = "Unreleased"


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


def is_unreleased(section: Section) -> bool:
    return section.heading is not None and section.heading.strip().lower() == UNRELEASED.lower()


def unreleased_section(text: str) -> Section | None:
    return next((s for s in parse(text) if is_unreleased(s)), None)


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


def read_entries(repo_root: Path) -> list[str]:
    """The '- ' lines of the working-tree file's Unreleased section, bullet marker stripped. Empty when the file or section is missing."""
    section = unreleased_section(_read(repo_root))
    return section.entries() if section else []


def _added(text: str, base_text: str) -> list[str]:
    """The Unreleased entries in `text` that the Unreleased section of `base_text` does not have."""
    section = unreleased_section(text)
    if section is None:
        return []
    base = unreleased_section(base_text)
    already = set(base.entries()) if base else set()
    return [entry for entry in section.entries() if entry not in already]


def entries_added_since(repo_root: Path, rev: str) -> list[str]:
    """This branch's own entries: the working-tree Unreleased lines that the same section at `rev` did not have
    (committed on the branch or not)."""
    return _added(_read(repo_root), _text_at(repo_root, rev))


def _version_of(heading: str | None) -> str:
    """"v1.0.21", "1.0.21 (2026-10-23)" and "1.0.21" all name version 1.0.21."""
    return (heading or "").strip().split()[0].lstrip("vV") if (heading or "").strip() else ""


def stamped_entries_since(repo_root: Path, rev: str) -> list[str]:
    """The entries of version sections the working-tree file has that the file at `rev` lacks, newest first: what the launcher
    stamped when it published (and mod_sync adopted) since `rev`. After a publish, Unreleased is empty and these are the release."""
    base_versions = {_version_of(s.heading) for s in parse(_text_at(repo_root, rev)) if s.heading is not None}
    entries: list[str] = []
    for section in parse(_read(repo_root)):
        if section.heading is None or is_unreleased(section) or _version_of(section.heading) in base_versions:
            continue
        entries.extend(e for e in section.entries() if e not in entries)
    return entries


def stash_entries(repo_root: Path, stash_sha: str) -> list[str]:
    """The entries a stash added to Unreleased: those in the stashed file but not in the commit it was made on.
    Lets new-branch carry them onto a fresh branch without ever merging the file itself."""
    return _added(_text_at(repo_root, stash_sha), _text_at(repo_root, f"{stash_sha}^1"))


def ensure_unreleased(repo_root: Path) -> None:
    """Puts an empty Unreleased section on top, above the versions, when the file has none. Does nothing otherwise."""
    text = _read(repo_root)
    if unreleased_section(text) is not None:
        return
    sections = parse(text)
    first = next((i for i, s in enumerate(sections) if s.heading is not None), len(sections))
    sections.insert(first, Section(UNRELEASED))
    _write(repo_root, render(sections))


def append(repo_root: Path, entries: list[str]) -> None:
    """Adds each entry Unreleased does not list yet, after its existing lines."""
    text = _read(repo_root)
    section = unreleased_section(text)
    if section is None:
        raise RuntimeError(f"{CHANGELOG_PATH} has no '## {UNRELEASED}' section to add entries to - ensure_unreleased first")
    present = set(section.entries())
    missing = []
    for entry in entries:
        if entry not in present:
            present.add(entry)
            missing.append(entry)
    if not missing:
        return
    sections = parse(text)
    target = next(s for s in sections if is_unreleased(s))
    target.lines = _trimmed(target.lines) + [f"- {entry}" for entry in missing]
    _write(repo_root, render(sections))


def rebuild_from(repo_root: Path, rev: str, entries: list[str]) -> None:
    """The file as it is at `rev`, plus these entries in its Unreleased section. What a rebase leaves behind."""
    _write(repo_root, _text_at(repo_root, rev))
    ensure_unreleased(repo_root)
    append(repo_root, entries)
