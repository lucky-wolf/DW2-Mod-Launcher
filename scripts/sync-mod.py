#!/usr/bin/env python3
"""Syncs the launcher's own Workshop item (the local mod folder DW2 Mod Launcher in the game's mods folder) with the repo:
mod/description.bbcode both ways, and changelog.txt with the launcher's publish stamp adopted (see lib/mod_sync.py).
No build, and neither the launcher nor the game needs to be closed. build.py, open-pr.py and new-branch.py run the same sync.

Usage:
  scripts/sync-mod.py
  scripts/sync-mod.py --dry-run
  scripts/sync-mod.py -y              # answer yes when a differing file has a newer copy
  scripts/sync-mod.py --game-path "C:/Steam/steamapps/common/Distant Worlds 2"
"""

import argparse
import sys
from pathlib import Path

from lib import mod_sync, output

REPO_ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game-path", type=Path, default=None, help="the Distant Worlds 2 install. Default: DW2_PATH, then the usual Steam location.")
    parser.add_argument("--dry-run", action="store_true", help="report what would change, change nothing")
    parser.add_argument("-y", "--yes", action="store_true", help="answer yes to every question instead of asking")
    args = parser.parse_args()

    mod_sync.sync(REPO_ROOT, args.game_path, dry_run=args.dry_run, assume_yes=args.yes)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
