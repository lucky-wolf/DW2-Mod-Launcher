"""Keeps the launcher's own Workshop item (the local mod folder `DW2 Mod Launcher` under the game's mods folder) in step with this repo.

What the Steam page says about the launcher lives in that mod folder, which the launcher publishes like any other mod:

  - mod/description.bbcode  <->  mods/DW2 Mod Launcher/description.bbcode
    Kept in step the way the DLL mods in DW2-XL do it (scripts/build-dlls.py): a copy missing from either side is copied over;
    when both exist and differ, the newer one is offered as the replacement for the older (default yes). The description may use
    {version}, {date} and {changes} (see "Mod Author README.md"); the launcher fills them in when it publishes.
  - changelog.txt  ->  mods/DW2 Mod Launcher/changelog.txt
    This repo's changelog has no Unreleased section: its newest section is the version about to be published. The mod folder gets the
    same sections (a leading "v" dropped from each heading, since the launcher names versions without it), so publishing that version
    sends its section as the Steam change note and `{changes}` in the description lists it. Nothing is stamped afterwards (there is no
    Unreleased section to stamp). An Unreleased section already in the mod folder, which the publish dialog's change notes box creates, is
    kept above the mirrored sections. The mod folder's version is not touched; the newest section's version is what to publish it as.

Run by scripts/build.py and scripts/open-pr.py. The game folder comes from --path, else DW2_PATH, else the platform's usual Steam
location. Without a game install or without the DW2 Mod Launcher folder there is nothing to sync, and it says so.
"""

import json
import os
import re
import shutil
from pathlib import Path

from . import changelog, output

MOD_FOLDER = "DW2 Mod Launcher"
REPO_DIR = "mod"
DESCRIPTION = "description.bbcode"
UNRELEASED = "unreleased"


def default_install_path() -> Path:
    env_path = os.environ.get("DW2_PATH")
    if env_path:
        return Path(env_path)
    if os.name == "nt":
        return Path("C:/Steam/steamapps/common/Distant Worlds 2")
    for candidate in (
        Path.home() / ".steam/steam/steamapps/common/Distant Worlds 2",
        Path.home() / ".local/share/Steam/steamapps/common/Distant Worlds 2",
    ):
        if candidate.is_dir():
            return candidate
    return Path.home() / ".steam/steam/steamapps/common/Distant Worlds 2"


def _normalized(path: Path) -> bytes:
    """The file's bytes as the repo wants them: no BOM, LF line endings."""
    data = path.read_bytes()
    if data.startswith(b"\xef\xbb\xbf"):
        data = data[3:]
    return data.replace(b"\r\n", b"\n")


def _copy_into_repo(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_bytes(_normalized(src))
    shutil.copystat(src, dst)


def _sync_file(repo_file: Path, mod_file: Path, dry_run: bool, assume_yes: bool) -> None:
    """One file kept in step between the repo and the mod folder; the newer copy replaces the older one (asked first when both exist)."""
    label = f"{repo_file.name} (repo <-> mods/{MOD_FOLDER})"
    if not repo_file.is_file() and not mod_file.is_file():
        return
    if not mod_file.is_file():
        output.replace(f"{label}: copied to mods/{MOD_FOLDER}, which had none")
        if not dry_run:
            shutil.copy2(repo_file, mod_file)
        return
    if not repo_file.is_file():
        output.replace(f"{label}: copied into the repo, which had none")
        if not dry_run:
            _copy_into_repo(mod_file, repo_file)
        return
    # the launcher's editor may save CRLF or a BOM, which is not a real difference
    if _normalized(repo_file) == _normalized(mod_file):
        output.info(f"{label}: in sync")
        return
    repo_newer = repo_file.stat().st_mtime >= mod_file.stat().st_mtime
    src, dst = (repo_file, mod_file) if repo_newer else (mod_file, repo_file)
    where_src, where_dst = ("the repo", f"mods/{MOD_FOLDER}") if repo_newer else (f"mods/{MOD_FOLDER}", "the repo")
    question = f"{repo_file.name} in {where_src} is newer than {where_dst}; copy it?"
    if dry_run:
        print(f"  [dry-run] {question} (would ask)")
        return
    if assume_yes:
        copy = True
    else:
        try:
            copy = output.confirm(question, default=True)
        except EOFError:
            copy = False
    if not copy:
        output.info(f"{label}: differs, left alone")
        return
    output.replace(f"{label}: copied from {where_src} to {where_dst}")
    if dst == repo_file:
        _copy_into_repo(src, dst)
    else:
        shutil.copy2(src, dst)


def _is_unreleased(section: changelog.Section) -> bool:
    return section.heading is not None and section.heading.strip().lower() == UNRELEASED


def _mod_heading(heading: str | None) -> str | None:
    """The repo's "v1.0.17" heading as the launcher names that version: "1.0.17"."""
    if heading is not None and re.fullmatch(r"v\d.*", heading.strip()):
        return heading.strip()[1:]
    return heading


def sync_changelog(repo_changelog: Path, mod_changelog: Path, mod_json: Path, dry_run: bool) -> None:
    """Mirrors the repo changelog's sections into the mod folder's changelog, keeping an Unreleased section the mod folder already has."""
    label = f"changelog.txt (repo -> mods/{MOD_FOLDER})"
    if not repo_changelog.is_file():
        output.info(f"{label}: the repo has no changelog.txt")
        return
    mirrored = [changelog.Section(_mod_heading(s.heading), list(s.lines)) for s in changelog.parse(repo_changelog.read_text(encoding="utf-8"))]
    if any(_is_unreleased(s) for s in mirrored):
        output.fail(f"{repo_changelog} has an Unreleased section; the launcher's changelog names its newest version instead")
    kept = []
    if mod_changelog.is_file():
        kept = [s for s in changelog.parse(mod_changelog.read_text(encoding="utf-8")) if _is_unreleased(s) and s.entries()]
    # the preamble (if any) stays first, then the kept Unreleased, then the versions newest first
    sections = [mirrored[0], *kept, *mirrored[1:]]
    text = changelog.render(sections)
    if mod_changelog.is_file() and mod_changelog.read_bytes().decode("utf-8").splitlines() == text.splitlines():
        output.info(f"{label}: in sync")
    else:
        output.replace(f"{label}: mirrored the repo's sections" + (" (kept the mod folder's Unreleased notes)" if kept else ""))
        if not dry_run:
            mod_changelog.write_bytes(text.encode("utf-8"))
    newest = next((s.heading for s in sections if s.heading is not None and not _is_unreleased(s)), None)
    version = _mod_version(mod_json)
    if newest is not None and version != newest.split()[0]:
        output.notice(f"the item's version is {version or 'unset'} but the newest changelog section is {newest}: publish it as {newest.split()[0]} so its section is the change note")


def _mod_version(mod_json: Path) -> str | None:
    if not mod_json.is_file():
        return None
    return json.loads(mod_json.read_text(encoding="utf-8-sig")).get("version")


def sync(repo_root: Path, game_path: Path | None = None, dry_run: bool = False, assume_yes: bool = False) -> None:
    """Brings mods/DW2 Mod Launcher in step with the repo (see the module docstring)."""
    install = game_path or default_install_path()
    output.step("sync launcher mod folder")
    if not install.is_dir():
        output.notice(f"skipped: no Distant Worlds 2 install at {install} (set DW2_PATH to sync the launcher's Workshop item)")
        return
    mod_dir = install / "mods" / MOD_FOLDER
    if not mod_dir.is_dir():
        output.notice(f"skipped: {mod_dir} does not exist (create the launcher's local mod there first)")
        return
    _sync_file(repo_root / REPO_DIR / DESCRIPTION, mod_dir / DESCRIPTION, dry_run, assume_yes)
    sync_changelog(repo_root / changelog.CHANGELOG_PATH, mod_dir / "changelog.txt", mod_dir / "mod.json", dry_run)
    output.ok("launcher mod folder in step")
