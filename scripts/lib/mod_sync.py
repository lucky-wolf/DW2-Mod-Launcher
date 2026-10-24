"""Keeps the launcher's own Workshop item (the local mod folder `DW2 Mod Launcher` under the game's mods folder) in step with this repo.

What the Steam page says about the launcher lives in that mod folder, which the launcher publishes like any other mod:

  - mod/description.bbcode  <->  mods/DW2 Mod Launcher/description.bbcode
    Kept in step the way the DLL mods in DW2-XL do it (scripts/build-dlls.py): a copy missing from either side is copied over;
    when both exist and differ, the newer one is offered as the replacement for the older (default yes). The description may use
    {version}, {date} and {changes} (see "Mod Author README.md"); the launcher fills them in when it publishes.
  - changelog.txt  <->  mods/DW2 Mod Launcher/changelog.txt
    Both have an "Unreleased" section on top, as every other mod does; nothing in these scripts stamps a version. The most recently
    saved copy wins, the way DW2-XL's scripts/dw2/changelog_sync.py does it, and nothing is merged line by line: the copy being
    overwritten is saved under temp/backups first (lib/backups.py) and every line it loses is reported. When the mod folder's copy is
    newer (edited there, or stamped by a publish) it replaces the repo's whole. When the repo's is newer, the launcher's newest stamp
    ("## <version> (<date>)", a version newer than any the repo lists) is adopted into it, the Unreleased lines that stamp shipped are
    dropped, and the result replaces the mod folder's copy (a leading "v" dropped from the old headings, since the launcher names
    versions without it). The mod folder's version is not touched.

Run by scripts/build.py and scripts/open-pr.py. The game folder comes from --path, else DW2_PATH, else the platform's usual Steam
location. Without a game install or without the DW2 Mod Launcher folder there is nothing to sync, and it says so.
"""

import os
import re
import shutil
from pathlib import Path

from . import backups, changelog, output

MOD_FOLDER = "DW2 Mod Launcher"
REPO_DIR = "mod"
DESCRIPTION = "description.bbcode"


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
    backup = backups.save(dst, "repo" if dst == repo_file else "mod")
    output.replace(f"{label}: copied from {where_src} to {where_dst} (previous copy: {backup})")
    if dst == repo_file:
        _copy_into_repo(src, dst)
    else:
        shutil.copy2(src, dst)


def _mod_heading(heading: str | None) -> str | None:
    """The repo's old "v1.0.17" heading as the launcher names that version: "1.0.17"."""
    if heading is not None and re.fullmatch(r"v\d.*", heading.strip()):
        return heading.strip()[1:]
    return heading


def _version_key(heading: str | None) -> tuple[int, ...]:
    m = re.match(r"^v?(\d+(?:\.\d+)*)", (heading or "").strip())
    return tuple(int(n) for n in m.group(1).split(".")) if m else ()


def _lost_lines(label: str, overwritten: list[changelog.Section], winner: list[changelog.Section], where: str, why: str) -> None:
    """Says which '- ' lines of the copy being overwritten the winning copy does not have."""
    kept = {entry for s in winner for entry in s.entries()}
    for section in overwritten:
        lost = [entry for entry in section.entries() if entry not in kept]
        if lost:
            output.warn(f"{label}: {why}; {where}'s '{section.heading}' lines it lacks:" + "".join(f"\n      - {entry}" for entry in lost))


def _repo_heading(heading: str | None, repo: list[changelog.Section]) -> str | None:
    """A mod folder heading as the repo spells it: "1.0.21" stays the repo's "v1.0.21"."""
    return next((s.heading for s in repo if s.heading is not None and _mod_heading(s.heading) == heading), heading)


def sync_changelog(repo_changelog: Path, mod_changelog: Path, dry_run: bool) -> None:
    """Keeps the repo's changelog and the mod folder's in step; the most recently saved copy wins and the other is saved under
    temp/backups before it is overwritten, every line it loses reported. No line-by-line merge: a line deleted from the winner stays deleted."""
    label = f"changelog.txt (repo <-> mods/{MOD_FOLDER})"
    if not repo_changelog.is_file():
        output.info(f"{label}: the repo has no changelog.txt")
        return
    repo = changelog.parse(repo_changelog.read_text(encoding="utf-8"))

    def mod_text_of(sections: list[changelog.Section]) -> str:
        return changelog.render([changelog.Section(_mod_heading(s.heading), s.lines) for s in sections])

    if not mod_changelog.is_file():
        output.replace(f"{label}: copied to mods/{MOD_FOLDER}, which had none")
        if not dry_run:
            mod_changelog.write_bytes(mod_text_of(repo).encode("utf-8"))
        return
    mod = changelog.parse(mod_changelog.read_text(encoding="utf-8"))

    if mod_changelog.stat().st_mtime > repo_changelog.stat().st_mtime:
        # the mod folder's copy was saved after the repo's (edited there, or stamped by a publish): it becomes the repo's
        sections = [changelog.Section(_repo_heading(s.heading, repo), s.lines) for s in mod]
        text = changelog.render(sections)
        if text.splitlines() == repo_changelog.read_bytes().decode("utf-8").splitlines():
            output.info(f"{label}: in sync")
            return
        _lost_lines(label, repo, sections, "the repo", f"mods/{MOD_FOLDER} is newer and replaces the repo copy")
        if dry_run:
            print(f"  [dry-run] would update {repo_changelog} from the newer {mod_changelog}")
            return
        backup = backups.save(repo_changelog, "repo")
        output.replace(f"{label}: repo copy replaced with the newer mods/{MOD_FOLDER} copy (previous copy: {backup})")
        repo_changelog.write_bytes(text.encode("utf-8"))
        return

    # the repo's copy is the newer: the launcher's newest stamp (a version newer than any the repo lists, with the lines it shipped) is
    # adopted into it, and the result overwrites the mod folder's copy
    repo_versions = [s for s in repo if s.heading is not None and not changelog.is_unreleased(s)]
    newest = max((_version_key(s.heading) for s in repo_versions), default=())
    # a publish stamps the lines it sent, so a newer version section with none is not a stamp (the old scripts mirrored an empty
    # "## v1.0.23" into the mod folder); it is not adopted and the repo's copy overwrites it
    adopted = [s for s in mod if s.heading is not None and not changelog.is_unreleased(s) and _version_key(s.heading) > newest and s.entries()]
    shipped = {entry for s in adopted for entry in s.entries()}
    unreleased = [e for s in repo if changelog.is_unreleased(s) for e in s.entries() if e not in shipped]
    versions = sorted(repo_versions + adopted, key=lambda s: _version_key(s.heading), reverse=True)
    sections = [repo[0], changelog.Section(changelog.UNRELEASED, [f"- {e}" for e in unreleased]), *versions]
    repo_text = changelog.render(sections)
    mod_text = mod_text_of(sections)

    if repo_changelog.read_bytes().decode("utf-8").splitlines() != repo_text.splitlines():
        for section in adopted:
            output.replace(f"{label}: adopted '{section.heading}', stamped by the launcher when it published")
        for entry in shipped:
            output.replace(f"{label}: no longer Unreleased, it shipped: {entry}")
        if dry_run:
            print(f"  [dry-run] would update {repo_changelog}")
        else:
            backup = backups.save(repo_changelog, "repo")
            output.replace(f"{label}: repo changelog updated (previous copy: {backup})")
            repo_changelog.write_bytes(repo_text.encode("utf-8"))
    if mod_changelog.read_bytes().decode("utf-8").splitlines() == mod_text.splitlines():
        output.info(f"{label}: in sync")
        return
    adopted_headings = {s.heading for s in adopted}
    _lost_lines(label, [s for s in mod if s.heading not in adopted_headings], [changelog.Section(_mod_heading(s.heading), s.lines) for s in sections],
                f"mods/{MOD_FOLDER}", "the repo's changelog is newer and overwrites the mod folder copy")
    if dry_run:
        print(f"  [dry-run] would update {mod_changelog} from the repo")
        return
    backup = backups.save(mod_changelog, "mod")
    output.replace(f"{label}: mods/{MOD_FOLDER} overwritten with the repo's changelog (previous copy: {backup})")
    mod_changelog.write_bytes(mod_text.encode("utf-8"))


def _mod_dir(game_path: Path | None) -> Path | None:
    """The launcher's local mod folder, or None (after saying why) when there is no game install or no such folder."""
    install = game_path or default_install_path()
    if not install.is_dir():
        output.notice(f"skipped: no Distant Worlds 2 install at {install} (set DW2_PATH to sync the launcher's Workshop item)")
        return None
    mod_dir = install / "mods" / MOD_FOLDER
    if not mod_dir.is_dir():
        output.notice(f"skipped: {mod_dir} does not exist (create the launcher's local mod there first)")
        return None
    return mod_dir


def sync(repo_root: Path, game_path: Path | None = None, dry_run: bool = False, assume_yes: bool = False) -> None:
    """Brings mods/DW2 Mod Launcher in step with the repo (see the module docstring)."""
    output.step("sync launcher mod folder")
    mod_dir = _mod_dir(game_path)
    if mod_dir is None:
        return
    _sync_file(repo_root / REPO_DIR / DESCRIPTION, mod_dir / DESCRIPTION, dry_run, assume_yes)
    sync_changelog(repo_root / changelog.CHANGELOG_PATH, mod_dir / "changelog.txt", dry_run)
    output.ok("launcher mod folder in step")
