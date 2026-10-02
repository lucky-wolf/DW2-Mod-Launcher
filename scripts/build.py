#!/usr/bin/env python3
"""Build the Avalonia launcher for this OS.

By default it also applies `dotnet format` fixes and runs the unit tests (the same checks CI runs), then
builds. Pass --no-validate to skip both and just build.

Usage:
  scripts/build.py
  scripts/build.py --no-validate      # fast edit/build loop
  scripts/build.py --dry-run          # print the steps, build nothing
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
        REPO_ROOT, launcher_build.resolve_ui(args.ui), args.config, validate=not args.no_validate, dry_run=args.dry_run
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
