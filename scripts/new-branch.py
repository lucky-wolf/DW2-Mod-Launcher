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


# stashes made by this script carry this prefix so a later run can recognise them as its own
STASH_PREFIX = "new-branch auto-stash"


def own_stashes(repo_root: Path) -> list[tuple[str, str, str]]:
    """(commit sha, ref, subject) of every stash this script made, oldest first. The sha is the stable handle:
    a ref like stash@{0} shifts whenever another stash is pushed or dropped."""
    out = proc.git(repo_root, "stash", "list", "--format=%H%x09%gd%x09%gs").stdout
    found = []
    for line in out.splitlines():
        parts = line.split("\t", 2)
        if len(parts) == 3 and STASH_PREFIX in parts[2]:
            found.append((parts[0], parts[1], parts[2]))
    return list(reversed(found))


def restore_stash(repo_root: Path, sha: str) -> bool:
    """Applies the stash with this commit sha, then drops it. A stash that doesn't apply cleanly is kept
    (and reported) rather than lost.

    docs/focus.md is the one file that always conflicts: the stash holds "old list + my new lines" but
    the new branch's copy has just been emptied. Its entries are carried over separately (see
    restore_stashes), so a conflict confined to that file is resolved by taking the branch's version."""
    # git refuses to apply over local edits to a file the stash touches; focus.md is rewritten from the
    # carried entries afterwards anyway
    subprocess.run(["git", "checkout", "HEAD", "--", str(focus.FOCUS_PATH)], cwd=repo_root, capture_output=True)
    applied = subprocess.run(["git", "stash", "apply", sha], cwd=repo_root, capture_output=True)
    if applied.returncode != 0:
        unmerged = proc.git(repo_root, "diff", "--name-only", "--diff-filter=U").stdout.split()
        if unmerged != [str(focus.FOCUS_PATH.as_posix())]:
            return False
        subprocess.run(["git", "checkout", "HEAD", "--", str(focus.FOCUS_PATH)], cwd=repo_root, capture_output=True)
        subprocess.run(["git", "reset", "-q"], cwd=repo_root, capture_output=True)
    for _, ref, _ in own_stashes(repo_root):
        if proc.git(repo_root, "rev-parse", ref).stdout.strip() == sha:
            proc.git(repo_root, "stash", "drop", ref)
    return True


def restore_stashes(repo_root: Path, shas: list[str], reset_focus: bool = False) -> None:
    """Restores each stash, carrying the focus.md entries it added. With reset_focus (a new branch),
    focus.md is emptied once all stashes are applied and only those carried entries are put back, so the
    previous branch's already-merged entries don't leak into the new one."""
    carried_all: list[str] = []
    for sha in shas:
        subject = proc.git(repo_root, "log", "-1", "--format=%s", sha).stdout.strip()
        carried = focus.stash_entries(repo_root, sha)  # read before the stash is dropped
        if restore_stash(repo_root, sha):
            carried_all.extend(carried)
            output.ok(f"restored stash: {subject}" + (f" (+{len(carried)} focus.md entries)" if carried else ""))
        else:
            print(f"  could not restore stash cleanly ({subject}); it is still in 'git stash list' - resolve by hand")
    if reset_focus:
        focus.reset(repo_root)
    focus.append(repo_root, carried_all)


def landed_in_main(repo_root: Path, branch: str) -> bool:
    """Whether everything on the branch is already in main even though git can't see it as merged, which
    is what rebase and squash merges look like. True when every commit has a patch-equivalent in main
    (`git cherry`: no '+' lines), or GitHub reports a merged PR for the branch (covers squash merges)."""
    cherry = proc.git(repo_root, "cherry", "main", branch)
    if cherry.returncode == 0 and not any(line.startswith("+") for line in cherry.stdout.splitlines()):
        return True
    try:
        prs = subprocess.run(
            ["gh", "pr", "list", "--head", branch, "--state", "merged", "--json", "number", "--limit", "1"],
            cwd=repo_root, capture_output=True, text=True,
        )
    except FileNotFoundError:
        return False
    return prs.returncode == 0 and prs.stdout.strip() not in ("", "[]")


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

    output.step("working tree")
    to_restore: list[str] = []
    leftovers = own_stashes(repo_root)
    if leftovers:
        print("\n  Stashes left behind by an earlier run of this script:")
        for _, ref, subject in leftovers:
            print(f"    - {ref}: {subject}")
        if output.confirm("\n  Restore them onto the new branch once it is created?", default=True):
            to_restore = [sha for sha, _, _ in leftovers]
    elif proc.git(repo_root, "stash", "list").stdout.strip():
        print("  (note: there are stashes that this script didn't make; leaving them alone)")

    if proc.git(repo_root, "status", "--porcelain").stdout.strip():
        print("\n  Uncommitted changes:")
        subprocess.run(["git", "status", "--short"], cwd=repo_root)
        if not output.confirm("\n  Stash all changes (including untracked) and continue?", default=True):
            output.fail("working tree is not clean; aborted")
        stash_msg = f"{STASH_PREFIX} {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}"
        proc.run(repo_root, "git stash", ["git", "stash", "push", "-u", "-m", stash_msg])
        to_restore.append(proc.git(repo_root, "rev-parse", "stash@{0}").stdout.strip())
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
                # -d first (plain merges); a rebase or squash merge rewrites the commits so -d refuses,
                # in which case -D is used only once the work is verified to be in main
                result = subprocess.run(["git", "branch", "-d", branch], cwd=repo_root, capture_output=True)
                if result.returncode != 0 and landed_in_main(repo_root, branch):
                    result = subprocess.run(["git", "branch", "-D", branch], cwd=repo_root, capture_output=True)
                (deleted if result.returncode == 0 else kept).append(branch)
            if deleted:
                output.ok("deleted: " + ", ".join(deleted))
            if kept:
                print(f"  Kept (not verifiably merged into main): {', '.join(kept)}")

    output.step("new branch")
    if not wait_for_release_tag(repo_root) and release_version.head_triggers_release(repo_root):
        if not output.confirm("  Still no release tag after waiting - proceed with a best-guess version?", default=False):
            output.fail("aborted - re-run once the release tag lands")

    branch_user = get_branch_user(repo_root)
    next_tag = release_version.next_tag(repo_root)
    if next_tag and release_version.tag_exists(repo_root, next_tag):
        output.fail(f"computed next version {next_tag} already exists as a tag; re-run in a moment")

    suggested = f"{branch_user}/{next_tag}" if branch_user and next_tag else ""
    if suggested:
        print(f"  Suggested: {suggested}")
        prompt = "  New branch name (Enter to accept suggestion; type 'main' to stay on main): "
    else:
        prompt = "  New branch name (blank or 'main' to stay on main): "

    # a bad or taken name re-prompts instead of aborting the whole run (which has already synced main
    # and stashed the user's changes)
    while True:
        new_branch = input(prompt).strip() or suggested
        if not new_branch or new_branch.lower() in ("main", "skip"):
            output.ok("staying on main (no new branch)")
            if to_restore and not args.dry_run:
                restore_stashes(repo_root, to_restore)
            return 0
        if proc.git(repo_root, "check-ref-format", "--branch", new_branch).returncode != 0:
            output.warn(f"'{new_branch}' is not a valid git branch name (no spaces or ~ ^ : ? * [); try again")
            continue
        exists = subprocess.run(["git", "show-ref", "--verify", "--quiet", f"refs/heads/{new_branch}"], cwd=repo_root)
        if exists.returncode == 0:
            output.warn(f"branch '{new_branch}' already exists locally; try again")
            continue
        break

    if args.dry_run:
        print(f"  [dry-run] would create and check out branch '{new_branch}' and reset docs/focus.md")
        if to_restore:
            print("  [dry-run] stashes left in place; run again (without --dry-run) to restore them")
        return 0

    proc.run(repo_root, f"create branch {new_branch}", ["git", "checkout", "-b", new_branch])
    if to_restore:
        print("  » restore stashed changes")
    # resets focus.md for the new branch *after* the stashes are applied (see restore_stash), then
    # re-adds only the entries the stashes added
    restore_stashes(repo_root, to_restore, reset_focus=True)
    output.ok("docs/focus.md reset for the new branch")

    output.ok(f"ready on branch '{new_branch}'")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
