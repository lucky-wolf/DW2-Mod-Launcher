#!/usr/bin/env python3
"""Auto-increment PATCH from the latest vMAJOR.MINOR.* tag (MAJOR.MINOR is read from .version)
and push the new tag. The single canonical implementation .github/workflows/release.yml calls.

This is CI-only automation (it pushes a tag through the runner's own GITHUB_TOKEN-authenticated
origin) - it is not meant to be run locally as part of normal dev flow. --dry-run exists so it can
be inspected safely. It never writes git config and never touches the origin remote. Bump
MAJOR.MINOR by editing .version by hand; PATCH is always automatic.

When $GITHUB_OUTPUT is set, writes `tag=<new tag>` and `version=<new version>` to it.

Usage:
  scripts/release.py --dry-run   # compute and print the next tag, mutate nothing
  scripts/release.py             # CI only: create and push the next tag
"""

import argparse
import os
import subprocess
import sys
from pathlib import Path

from lib import release_version

REPO_ROOT = Path(__file__).resolve().parent.parent


def git(*args: str) -> str:
    result = subprocess.run(["git", *args], cwd=REPO_ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"[fail] git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dry-run", action="store_true", help="compute and print the next tag, but don't create/push it")
    args = parser.parse_args()

    git("fetch", "--tags", "--quiet")
    new_tag = release_version.next_tag(REPO_ROOT)
    if new_tag is None:
        sys.exit("[fail] .version missing or not MAJOR.MINOR (e.g. 0.1)")

    if git("ls-remote", "--tags", "origin", f"refs/tags/{new_tag}").strip():
        print(f"Tag {new_tag} already exists on origin; nothing to do.")
        return 0

    if args.dry_run:
        print(f"[ok] would create and push tag {new_tag}")
        return 0

    if not os.environ.get("GITHUB_ACTIONS"):
        sys.exit("[fail] refusing to run for real outside CI (no $GITHUB_ACTIONS set); use --dry-run to inspect")

    # Bot identity goes on this one command only (-c), never into git config.
    git("-c", "user.name=github-actions[bot]", "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
        "tag", "-a", new_tag, "-m", f"Release {new_tag}")
    git("push", "origin", new_tag)
    print(f"[ok] created and pushed {new_tag}")

    out = os.environ.get("GITHUB_OUTPUT")
    if out:
        with open(out, "a", encoding="utf-8") as f:
            f.write(f"tag={new_tag}\nversion={new_tag[len(release_version.TAG_PREFIX):]}\n")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(130)
