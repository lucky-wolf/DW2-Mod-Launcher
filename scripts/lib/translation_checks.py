"""Checks that each translated doc (`Foo.ja.md`) still mirrors its English original (`Foo.md`).

English is the primary language; a translation must have the same shape: the same sequence of heading
levels, the same fenced code blocks (commands and paths are never translated) and the same relative
links. Prose is free to differ. This catches the usual drift - a section added to the English README
but never translated, or the reverse.
"""

import re
from pathlib import Path

_TRANSLATION_SUFFIXES = (".ja",)
_SKIP_DIRS = {".git", "bin", "obj", "node_modules", "third_party", "archived", "plans"}

_FENCE = re.compile(r"^```")
_HEADING = re.compile(r"^(#{1,6})\s")
_LINK = re.compile(r"\]\(([^)#\s][^)\s]*)")


def find_pairs(root: Path) -> list[tuple[Path, Path]]:
    """(translation, english original) for every `*.ja.md` under root."""
    pairs = []
    for path in sorted(root.rglob("*.md")):
        if any(part in _SKIP_DIRS for part in path.relative_to(root).parts):
            continue
        for suffix in _TRANSLATION_SUFFIXES:
            if path.name.endswith(suffix + ".md"):
                pairs.append((path, path.with_name(path.name[: -len(suffix + ".md")] + ".md")))
    return pairs


def _shape(text: str) -> tuple[list[int], list[str], list[str]]:
    """(heading levels, code block bodies, relative link targets) of a markdown document."""
    levels: list[int] = []
    blocks: list[str] = []
    links: list[str] = []
    current: list[str] | None = None
    for line in text.splitlines():
        if _FENCE.match(line):
            if current is None:
                current = []
            else:
                blocks.append("\n".join(current))
                current = None
            continue
        if current is not None:
            current.append(line)
            continue
        heading = _HEADING.match(line)
        if heading:
            levels.append(len(heading.group(1)))
        links.extend(m for m in _LINK.findall(line) if "://" not in m and not m.startswith("mailto:"))
    return levels, blocks, links


def _sans_translations(links: list[str]) -> list[str]:
    # the language switcher legitimately points at a different file in each version
    return [link for link in links if not any(link.endswith(s + ".md") for s in _TRANSLATION_SUFFIXES)
            and not link.endswith("README.md")]


def check_pair(translation: Path, original: Path) -> list[str]:
    """Human-readable problems with the translation; empty when it mirrors the original."""
    if not original.exists():
        return [f"{translation.name}: no English original {original.name}"]
    problems = []
    t_levels, t_blocks, t_links = _shape(translation.read_text(encoding="utf-8"))
    o_levels, o_blocks, o_links = _shape(original.read_text(encoding="utf-8"))
    if t_levels != o_levels:
        problems.append(f"{translation.name}: heading structure differs from {original.name} "
                        f"({len(t_levels)} headings vs {len(o_levels)}) - sections added, removed or reordered?")
    if t_blocks != o_blocks:
        problems.append(f"{translation.name}: code blocks differ from {original.name} "
                        "(commands and paths must be identical, only prose is translated)")
    if _sans_translations(t_links) != _sans_translations(o_links):
        problems.append(f"{translation.name}: relative links differ from {original.name}")
    return problems


def check_all(root: Path) -> list[str]:
    problems: list[str] = []
    for translation, original in find_pairs(root):
        problems.extend(check_pair(translation, original))
    return problems
