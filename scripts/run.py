#!/usr/bin/env python3
"""Build the launcher for this OS (see build.py), then run it.

Runs in the foreground, attached to this terminal, so its output is visible and ^C closes it; pass
--detach to start it and return immediately. Anything after `--` is passed to the launcher.

Usage:
  scripts/run.py
  scripts/run.py --no-validate        # skip the format fix and tests; just build and run
  scripts/run.py --detach
  scripts/run.py --dry-run            # print what would happen, build and run nothing
"""

import argparse
import subprocess
import sys
from pathlib import Path

from lib import launcher_build, output, proc

REPO_ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    launcher_build.add_arguments(parser)
    parser.add_argument("--detach", action="store_true", help="start the launcher and return immediately.")
    parser.add_argument("launcher_args", nargs="*", help="arguments for the launcher itself (put them after `--`).")
    args = parser.parse_args()
    output.set_color(args.color)

    ui = launcher_build.resolve_ui(args.ui)
    exe = launcher_build.build(REPO_ROOT, ui, args.config, validate=not args.no_validate, dry_run=args.dry_run)
    command = [str(exe), *args.launcher_args]

    output.step(f"run ({ui.name})")
    if args.dry_run:
        output.notice("dry run - would then run the built launcher" + (" with: " + " ".join(args.launcher_args) if args.launcher_args else ""))
        return 0
    if args.detach:
        # Cut the launcher loose from our stdio, or whatever is reading this script's output (a pipe, a CI log)
        # keeps waiting for it to exit.
        subprocess.Popen(
            command,
            cwd=exe.parent,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=sys.platform != "win32",
        )
        output.done("started")
        return 0

    output.info("running; close the window or press ^C to stop")
    # Not proc.run_foreground: it ignores SIGINT while waiting, and an ignored signal stays ignored in the child, so
    # ^C would do nothing to the launcher. Here the terminal's ^C reaches both of us; we just wait for it to go.
    child = subprocess.Popen(command, cwd=exe.parent)
    try:
        code = child.wait()
    except KeyboardInterrupt:
        try:
            code = child.wait(timeout=5)
        except subprocess.TimeoutExpired:
            child.terminate()
            code = child.wait()
        output.abort("stopped")
        return output.INTERRUPTED_EXIT_CODE
    if proc.was_interrupted(code):
        output.abort("stopped")
        return output.INTERRUPTED_EXIT_CODE
    if code != 0:
        output.fail(f"the launcher exited with code {code}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
