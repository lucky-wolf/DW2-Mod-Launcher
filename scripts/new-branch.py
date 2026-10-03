#!/usr/bin/env python3
"""Prepare a clean local repo state for starting new work.

Flow:
  1) ensure the working tree is clean (offers to stash)
  2) switch to main, pull, fetch -p
  3) if main's tip should have been released, wait for its release tag
  4) delete local branches that are merged or whose remote is gone (git branch -d only)
  5) optionally create a new branch, suggested as <user>/v<next version>, and empty docs/focus.md for it

Usage:
  scripts/new-branch.py
  scripts/new-branch.py --dry-run   # sync as normal, but don't delete or create branches
"""

import argparse
import os
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

from lib import focus, output, proc, release_version

# a "gone" tracking annotation like "[origin/branch: gone]"
_GONE_BRANCH_PATTERN = re.compile(r"^\s*(\*?)\s*(\S+)\s+[0-9a-f]+\s+\[[^\]]*:\s*gone\]")


def get_branch_user(repo_root: Path) -> str:
    username = ""
    for config_args in (("config", "--get", "user.username"), ("config", "--get", "user.name")):
        result = proc.git(repo_root, *config_args)
        if result.returncode == 0 and result.stdout.strip():
            username = result.stdout.strip()
            break
    username = username or os.environ.get("USER") or os.environ.get("USERNAME") or ""
    username = re.sub(r"[^a-z0-9._/-]", "-", re.sub(r"\s+", "-", username.lower()))
    return username.strip("-")


def wait_for_release_tag(repo_root: Path, timeout_seconds: int = 300, poll_seconds: int = 10) -> str | None:
    """A merge landing on main and release.yml tagging it are not atomic: the tag trails the merge
    by however long the workflow takes. Suggesting "latest tag + 1" in that window would name the
    branch after a version that is about to be published, so wait the tag out (bounded, so a
    failed release run doesn't hang this forever; Ctrl-C works too). Only applies when main's tip
    touched release paths - other merges are never tagged."""
    tag = release_version.release_tag_at_head(repo_root)
    if tag or not release_version.head_triggers_release(repo_root):
        return tag

    print("  main's tip should have been released but has no tag yet - waiting for the release workflow...")
    elapsed = 0
    while elapsed < timeout_seconds:
        print(f"  » waiting ({elapsed}s/{timeout_seconds}s)...")
        time.sleep(poll_seconds)
        elapsed += poll_seconds
        subprocess.run(["git", "fetch", "--tags", "-q"], cwd=repo_root, capture_output=True)
        tag = release_version.release_tag_at_head(repo_root)
        if tag:
            output.redraw_last_line(f"  release tag published: {tag}")
            return tag
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--dry-run", action="store_true",
        help="sync/cleanup as normal, but don't delete local branches or create the new branch",
    )
    args = parser.parse_args()

    repo_root = proc.repo_root()
    did_auto_stash = False

    output.step("working tree")
    if proc.git(repo_root, "status", "--porcelain").stdout.strip():
        print("\n  Uncommitted changes:")
        subprocess.run(["git", "status", "--short"], cwd=repo_root)
        if not output.confirm("\n  Stash all changes (including untracked) and continue?", default=True):
            output.fail("working tree is not clean; aborted")
        stash_msg = f"new-branch auto-stash {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}"
        proc.run(repo_root, "git stash", ["git", "stash", "push", "-u", "-m", stash_msg])
        did_auto_stash = True
        output.ok("stashed changes")
    else:
        output.ok("working tree clean")

    output.step("sync main")
    proc.run(repo_root, "checkout main", ["git", "checkout", "main"])
    proc.run(repo_root, "git pull", ["git", "pull"])
    proc.run(repo_root, "git fetch -p --tags", ["git", "fetch", "-p", "--tags"])
    output.ok("main updated")

    output.step("cleanup local branches")
    candidates: set[str] = set()
    merged = proc.git(repo_root, "branch", "--format=%(refname:short)", "--merged", "main").stdout
    candidates.update(b.strip() for b in merged.splitlines() if b.strip() and b.strip() != "main")
    for line in proc.git(repo_root, "branch", "-vv").stdout.splitlines():
        m = _GONE_BRANCH_PATTERN.match(line)
        if m and m.group(1) != "*" and m.group(2) != "main":
            candidates.add(m.group(2))

    if not candidates:
        output.ok("no deletable local branches found")
    else:
        print("  Candidates:")
        for candidate in sorted(candidates):
            print(f"    - {candidate}")
        if args.dry_run:
            print(f"  [dry-run] would delete: {', '.join(sorted(candidates))}")
        else:
            deleted, kept = [], []
            for branch in sorted(candidates):
                result = subprocess.run(["git", "branch", "-d", branch], cwd=repo_root, capture_output=True)
                (deleted if result.returncode == 0 else kept).append(branch)
            if deleted:
                output.ok("deleted: " + ", ".join(deleted))
            if kept:
                print(f"  Kept (not deletable with -d): {', '.join(kept)}")

    output.step("new branch")
    if not wait_for_release_tag(repo_root) and release_version.head_triggers_release(repo_root):
        if not output.confirm("  Still no release tag after waiting - proceed with a best-guess version?", default=False):
            output.fail("aborted - re-run once the release tag lands")

    branch_user = get_branch_user(repo_root)
    next_tag = release_version.next_tag(repo_root)
    if next_tag and release_version.tag_exists(repo_root, next_tag):
        output.fail(f"computed next version {next_tag} already exists as a tag; re-run in a moment")

    if branch_user and next_tag:
        suggested = f"{branch_user}/{next_tag}"
        print(f"  Suggested: {suggested}")
        new_branch = input("  New branch name (Enter to accept suggestion; type 'skip' to skip): ").strip() or suggested
    else:
        new_branch = input("  New branch name (leave blank to skip): ").strip()

    if not new_branch or new_branch.lower() == "skip":
        output.ok("skipped branch creation")
        return 0

    exists = subprocess.run(["git", "show-ref", "--verify", "--quiet", f"refs/heads/{new_branch}"], cwd=repo_root)
    if exists.returncode == 0:
        output.fail(f"branch '{new_branch}' already exists locally")

    if args.dry_run:
        print(f"  [dry-run] would create and check out branch '{new_branch}' and reset docs/focus.md")
        if did_auto_stash:
            print("  [dry-run] stashed changes left in place; 'git stash pop' to restore them")
        return 0

    proc.run(repo_root, f"create branch {new_branch}", ["git", "checkout", "-b", new_branch])
    focus.reset(repo_root)
    output.ok("docs/focus.md reset for the new branch")
    if did_auto_stash:
        print("  » restore stashed changes")
        pop = subprocess.run(["git", "stash", "pop"], cwd=repo_root, capture_output=True)
        if pop.returncode != 0:
            print("  stash pop reported conflicts or a partial apply; resolve and run 'git stash list'")
        else:
            output.ok("restored stashed changes")

    output.ok(f"ready on branch '{new_branch}'")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
