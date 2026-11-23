#!/usr/bin/env python3
"""Prints the release message for what the launcher has PUBLISHED to the Workshop and you have not yet announced.

commit-message.py answers "what changed since HEAD"; this answers "what went out". The only source of what is
published is the launcher's own record, %AppData%\\DW2ModLauncher\\PublishedState\\<workshopId>.json (Version and
PublishedUtc), so nothing is guessed from git or from Unreleased. Each published version's bullets come from that
version's stamped section in changelog.txt.

Announced versions are recorded in docs/announced-releases.json. A version recorded there is never printed again.
Running the script records what it prints (--peek looks without recording). --last reprints the previous message,
so a lost clipboard never needs a second announcement.

With no record yet, only the latest published version is announced.

Usage:
  python scripts/release-message.py [--copy] [--peek] [--last] [--no-sync]

  --copy     also put the message on the clipboard (Windows clip.exe)
  --peek     print only; record nothing
  --last     reprint the previous message; records nothing
  --no-sync  skip the sync-mod.py run that normally comes first (adopts the launcher's changelog stamp)

Format: "## DW2 Mod Launcher <version>" and one "- " line per bullet; several unannounced versions are one
section each, oldest first.
"""

import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

from lib import changelog, output

REPO_ROOT = Path(__file__).resolve().parent.parent
STATE_FILE = REPO_ROOT / "docs" / "announced-releases.json"
MOD_JSON = REPO_ROOT / "mod" / "mod.json"


def version_key(version: str) -> tuple[int, ...]:
    return tuple(int(p) for p in version.split("."))


def lower_keys(data: dict) -> dict:
    return {k.lower(): v for k, v in data.items()}


def read_json(path: Path) -> dict:
    return lower_keys(json.loads(path.read_text(encoding="utf-8-sig")))


def load_state() -> dict:
    if STATE_FILE.is_file():
        return json.loads(STATE_FILE.read_text(encoding="utf-8"))
    return {"announced": "", "last": ""}


def save_state(state: dict) -> None:
    STATE_FILE.write_text(json.dumps(state, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def published() -> tuple[str, str]:
    """(display name, published version) from the launcher's PublishedState record."""
    meta = read_json(MOD_JSON)
    workshop_id = str(meta.get("workshopid") or "")
    record = Path(os.environ["APPDATA"]) / "DW2ModLauncher" / "PublishedState" / f"{workshop_id}.json"
    if not workshop_id or not record.is_file():
        output.fail(f"The launcher has no published record for this mod ({record}); publish it from the launcher first.")
    return meta.get("displayname") or "DW2 Mod Launcher", read_json(record)["version"]


def stamped_sections() -> dict[str, list[str]]:
    sections: dict[str, list[str]] = {}
    for section in changelog.parse(changelog._read(REPO_ROOT)):
        if section.heading is None or changelog.is_unreleased(section):
            continue
        sections[changelog._version_of(section.heading)] = section.entries()
    return sections


def build(name: str, version: str, announced: str) -> list[tuple[str, list[str]]]:
    sections = stamped_sections()
    if announced:
        versions = sorted((v for v in sections if version_key(announced) < version_key(v) <= version_key(version)), key=version_key)
        if version_key(version) > version_key(announced) and version not in versions:
            versions.append(version)
    else:
        versions = [version]
    result = []
    for v in versions:
        bullets = sections.get(v, [])
        if not bullets:
            output.warn(f"{name} {v} was published but changelog.txt has no bullets for it")
            continue
        result.append((f"{name} {v}", bullets))
    return result


def format_message(sections: list[tuple[str, list[str]]]) -> str:
    return "\n\n".join(f"## {title}\n" + "\n".join(f"- {e}" for e in entries) for title, entries in sections) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--copy", action="store_true")
    parser.add_argument("--peek", action="store_true")
    parser.add_argument("--last", action="store_true")
    parser.add_argument("--no-sync", action="store_true")
    output.add_color_argument(parser)
    args = parser.parse_args()
    output.set_color(args.color)
    sys.stdout.reconfigure(encoding="utf-8")

    state = load_state()
    if args.last:
        if not state.get("last"):
            output.fail("No previous message recorded.")
        message = state["last"]
        print(message, end="")
    else:
        if not args.no_sync:
            result = subprocess.run([sys.executable, str(REPO_ROOT / "scripts" / "sync-mod.py"), "-y"], cwd=REPO_ROOT)
            if result.returncode != 0:
                output.fail("sync-mod.py failed; fix that first (or pass --no-sync).")
        name, version = published()
        sections = build(name, version, state.get("announced", ""))
        if not sections:
            output.fail("Nothing new: the published version has already been announced (--last reprints the previous message).")
        message = format_message(sections)
        print(message, end="")
        if not args.peek:
            state["announced"] = version
            state["recordedUtc"] = datetime.now(timezone.utc).isoformat(timespec="seconds")
            state["last"] = message
            save_state(state)
            output.ok(f"Recorded {version} as announced; this will not print again (--last reprints it).")
        else:
            output.info("--peek: nothing recorded.")
    if args.copy:
        subprocess.run(["clip"], input=message.encode("utf-16-le"), check=True)
        output.ok("Copied to the clipboard.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
