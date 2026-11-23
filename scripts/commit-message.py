#!/usr/bin/env python3
"""Prints the commit message for the current changes, built from changelog.txt.

It is the "## Unreleased" bullets plus the bullets of any version section the launcher stamped since HEAD
(a publish moves Unreleased into a stamped section, and mod_sync adopts it), minus whatever HEAD's
changelog.txt already had, so earlier commits' entries are not repeated. A newly stamped release heads the
message "## DW2 Mod Launcher <version>"; otherwise it is "## DW2 Mod Launcher".

Usage:
  python scripts/commit-message.py [--copy]

  --copy   also put the message on the clipboard (Windows clip.exe)
"""

import argparse
import subprocess
import sys
from pathlib import Path

from lib import changelog, output

REPO_ROOT = Path(__file__).resolve().parent.parent
NAME = "DW2 Mod Launcher"


def newest_stamped_version() -> str:
    for section in changelog.parse(changelog._read(REPO_ROOT)):
        if section.heading is not None and not changelog.is_unreleased(section):
            return changelog._version_of(section.heading)
    return ""


def build_message() -> str:
    stamped = changelog.stamped_entries_since(REPO_ROOT, "HEAD")
    unreleased = [e for e in changelog.entries_added_since(REPO_ROOT, "HEAD") if e not in stamped]
    entries = stamped + unreleased
    if not entries:
        output.fail("No new changelog.txt entries since HEAD (nothing under '## Unreleased' that HEAD lacks, no newly stamped version).")
    heading = f"{NAME} {newest_stamped_version()}" if stamped else NAME
    return f"## {heading}\n" + "\n".join(f"- {e}" for e in entries) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--copy", action="store_true", help="also copy the message to the clipboard")
    output.add_color_argument(parser)
    args = parser.parse_args()
    output.set_color(args.color)
    sys.stdout.reconfigure(encoding="utf-8")

    message = build_message()
    print(message, end="")
    if args.copy:
        subprocess.run(["clip"], input=message.encode("utf-16-le"), check=True)
        output.ok("Copied to the clipboard.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
