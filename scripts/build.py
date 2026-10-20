#!/usr/bin/env python3
"""Build the Avalonia launcher for this OS.

By default it also applies `dotnet format` fixes and runs the unit tests (the same checks CI runs), then
builds. Pass --no-validate to skip both and just build. With Distant Worlds 2 running, the tests and the Loader DLL copy (which the
game has locked) are skipped and everything else is built and deployed; it says whether the Loader DLL needs deploying later. After the build it syncs the launcher's own Workshop item (the
local mod folder DW2 Mod Launcher in the game's mods folder) with the repo: mod/description.bbcode both ways and the newest
changelog.txt entries into its Unreleased section (see lib/mod_sync.py); --no-mod-sync skips that.

Usage:
  scripts/build.py
  scripts/build.py --no-validate      # fast edit/build loop
  scripts/build.py --dry-run          # print the steps, build nothing
  scripts/build.py --no-mod-sync      # do not touch mods/DW2 Mod Launcher
"""

import argparse
import sys
from pathlib import Path

from lib import launcher_build, output

REPO_ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    launcher_build.add_arguments(parser)
    args = parser.parse_args()
    output.set_color(args.color)

    launcher_build.build(
        REPO_ROOT, launcher_build.resolve_ui(args.ui), args.config, validate=not args.no_validate, dry_run=args.dry_run,
        sync_mod=not args.no_mod_sync, game_path=args.game_path,
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
