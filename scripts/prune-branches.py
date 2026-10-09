#!/usr/bin/env python3
"""Delete every local branch except main and the one currently checked out.

Local branches are never removed by git when a PR merges (the remote copy is deleted, this one lingers), and rebase
merges rewrite the commits so `git branch -d` refuses them. This deletes with -D, so it lists what each branch holds
that main does not and asks first.

Usage:
  scripts/prune-branches.py            # list, confirm, delete
  scripts/prune-branches.py --yes      # no confirmation
  scripts/prune-branches.py --dry-run  # list only
"""

import argparse
import sys

from lib import output, proc

KEEP = {"main"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--yes", action="store_true", help="delete without asking")
    parser.add_argument("--dry-run", action="store_true", help="only list what would be deleted")
    output.add_color_argument(parser)
    args = parser.parse_args()
    output.set_color(args.color)

    repo_root = proc.repo_root()
    proc.git(repo_root, "fetch", "--prune")
    current = proc.git(repo_root, "branch", "--show-current").stdout.strip()
    names = proc.git(repo_root, "for-each-ref", "--format=%(refname:short)", "refs/heads/").stdout.split()
    doomed = [n for n in names if n != current and n not in KEEP]

    if not doomed:
        output.ok("nothing to delete (only main and the current branch exist)")
        return 0

    print(f"  Keeping: {', '.join(sorted(KEEP | {current}))}")
    print("  Deleting:")
    for branch in doomed:
        ahead = proc.git(repo_root, "rev-list", "--count", f"main..{branch}").stdout.strip()
        remote = proc.git(repo_root, "rev-parse", "--verify", "--quiet", f"refs/remotes/origin/{branch}").returncode == 0
        note = "no commits beyond main" if ahead == "0" else f"{ahead} commit(s) not in main" + ("" if remote else ", NOT on origin")
        print(f"    - {branch}  ({note})")

    if args.dry_run:
        return 0
    if not args.yes and not output.confirm("  Delete these branches?", default=False):
        output.abort("nothing deleted")
        return 1

    for branch in doomed:
        result = proc.git(repo_root, "branch", "-D", branch)
        if result.returncode == 0:
            output.ok(f"deleted {branch}")
        else:
            output.warn(f"could not delete {branch}: {result.stderr.strip()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
