#!/usr/bin/env python3
"""Prepare a clean local repo state for starting new work.

Flow:
  0) if the branch being left has a PR that is still open, offer to wait for it to merge (its version is not released until it does)
  1) ensure the working tree is clean (offers to stash)
  2) switch to main, pull, fetch -p; if that PR was merged, wait until main really contains the merge
  3) if main's tip should have been released, wait for its release tag
  4) delete local branches that are merged or whose remote is gone (git branch -d only)
  5) optionally create a new branch, suggested as <user>/v<next version>, and start that version's section in changelog.txt
  6) bring the launcher's own Workshop item (the local mod folder DW2 Mod Launcher) in step with the repo, then set its mod.json
     version to the new version, so the launcher's publish dialog starts from it (see lib/mod_sync.py)

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

from lib import changelog, github, mod_sync, output, proc, release_version

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

    changelog.txt is the one file that always conflicts: the stash holds the old branch's section plus my new
    lines but the new branch's file has a different top. Its entries are carried over separately (see
    restore_stashes), so the file is always taken from the branch rather than the stash."""
    # git refuses to apply over local edits to a file the stash touches; the section is rewritten from the
    # carried entries afterwards anyway
    subprocess.run(["git", "checkout", "HEAD", "--", str(changelog.CHANGELOG_PATH)], cwd=repo_root, capture_output=True)
    applied = subprocess.run(["git", "stash", "apply", sha], cwd=repo_root, capture_output=True)
    if applied.returncode != 0:
        unmerged = proc.git(repo_root, "diff", "--name-only", "--diff-filter=U").stdout.split()
        if unmerged != [str(changelog.CHANGELOG_PATH.as_posix())]:
            return False
        subprocess.run(["git", "checkout", "HEAD", "--", str(changelog.CHANGELOG_PATH)], cwd=repo_root, capture_output=True)
        subprocess.run(["git", "reset", "-q"], cwd=repo_root, capture_output=True)
    else:
        # applied cleanly: drop the stash's copy of the file, its entries are carried instead
        subprocess.run(["git", "checkout", "HEAD", "--", str(changelog.CHANGELOG_PATH)], cwd=repo_root, capture_output=True)
    for _, ref, _ in own_stashes(repo_root):
        if proc.git(repo_root, "rev-parse", ref).stdout.strip() == sha:
            proc.git(repo_root, "stash", "drop", ref)
    return True


def restore_stashes(repo_root: Path, shas: list[str], heading: str) -> None:
    """Restores each stash, carrying the changelog.txt entries it added into the section for `heading` (started
    above the older ones when it is not already the top), so the previous branch's already-merged entries don't
    leak into the new one."""
    carried_all: list[str] = []
    for sha in shas:
        subject = proc.git(repo_root, "log", "-1", "--format=%s", sha).stdout.strip()
        carried = changelog.stash_entries(repo_root, sha)  # read before the stash is dropped
        if restore_stash(repo_root, sha):
            carried_all.extend(carried)
            output.ok(f"restored stash: {subject}" + (f" (+{len(carried)} changelog.txt entries)" if carried else ""))
        else:
            print(f"  could not restore stash cleanly ({subject}); it is still in 'git stash list' - resolve by hand")
    changelog.start_section(repo_root, heading)
    changelog.append(repo_root, carried_all)


def wait_for_open_pr(repo_root: Path, branch: str, pr: dict, poll_seconds: int = 15) -> dict:
    """The branch being left still has an open PR, so its version is not released and "latest tag + 1" would name the new
    branch after that same version. Offers to wait for the merge (Ctrl-C works) and returns the PR as it is afterwards."""
    print()
    print(f"  '{branch}' still has an open PR (#{pr['number']}): its version is not released until it merges.")
    if not output.confirm("  Wait for it to merge?", default=True):
        output.warn("not waiting: the suggested version below will be the one that open PR is about to release")
        return pr
    waited = 0
    while pr["state"] == "OPEN":
        print(f"  » waiting for PR #{pr['number']} to merge ({waited}s)...")
        time.sleep(poll_seconds)
        waited += poll_seconds
        try:
            pr = github.latest_pr_for_branch(repo_root, branch)
        except RuntimeError as e:
            output.fail(f"could not read the PR state via gh: {e}")
    if pr["state"] == "CLOSED":
        output.warn(f"PR #{pr['number']} was closed without merging; nothing was released for '{branch}'")
    return pr


def wait_for_merge_on_main(repo_root: Path, merge_commit: str, timeout_seconds: int = 300, poll_seconds: int = 10) -> None:
    """GitHub reports a PR as merged a moment before `git pull` can bring the merge down, so a script that pulls right after
    the merge sees the old main (and the old tags). Pull until the PR's merge commit is in HEAD; fail loudly if it never is."""
    def has_merge() -> bool:
        return subprocess.run(["git", "merge-base", "--is-ancestor", merge_commit, "HEAD"], cwd=repo_root).returncode == 0

    elapsed = 0
    while not has_merge():
        if elapsed >= timeout_seconds:
            output.fail(f"the merged PR's commit {merge_commit[:8]} did not reach main after {timeout_seconds}s; re-run in a moment")
        print(f"  » main does not have the merged PR yet ({elapsed}s/{timeout_seconds}s)...")
        time.sleep(poll_seconds)
        elapsed += poll_seconds
        subprocess.run(["git", "pull", "-q"], cwd=repo_root, capture_output=True)
        subprocess.run(["git", "fetch", "-p", "--tags", "-q"], cwd=repo_root, capture_output=True)
    output.ok("main has the merged PR")


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
    parser.add_argument("--no-mod-sync", action="store_true", help="leave the launcher's Workshop item (mods/DW2 Mod Launcher) alone")
    parser.add_argument("--game-path", type=Path, default=None, help="the Distant Worlds 2 install the mod folder sync uses. Default: DW2_PATH, then the usual Steam location.")
    args = parser.parse_args()

    repo_root = proc.repo_root()

    output.step("branch being left")
    leaving = proc.git(repo_root, "branch", "--show-current").stdout.strip()
    leaving_pr = None
    if leaving and leaving != "main":
        github.require_gh_auth()
        try:
            leaving_pr = github.latest_pr_for_branch(repo_root, leaving)
        except RuntimeError as e:
            output.fail(f"could not read the PR state of '{leaving}' via gh: {e}")
        if leaving_pr and leaving_pr["state"] == "OPEN":
            leaving_pr = wait_for_open_pr(repo_root, leaving, leaving_pr)
        output.ok(f"'{leaving}': " + (f"PR #{leaving_pr['number']} {leaving_pr['state'].lower()}" if leaving_pr else "no PR"))
    else:
        output.ok("on main already")

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
    if leaving_pr and leaving_pr["state"] == "MERGED":
        if leaving_pr["mergeCommit"]:
            wait_for_merge_on_main(repo_root, leaving_pr["mergeCommit"])
        else:
            output.warn(f"gh gave no merge commit for PR #{leaving_pr['number']}, so main cannot be checked for it")
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
    if not next_tag:
        output.fail("could not work out the next release version (.version is missing or malformed) to head the changelog.txt section")
    if release_version.tag_exists(repo_root, next_tag):
        output.fail(f"computed next version {next_tag} already exists as a tag; re-run in a moment")

    suggested = f"{branch_user}/{next_tag}" if branch_user else ""
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
                restore_stashes(repo_root, to_restore, next_tag)
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
        print(f"  [dry-run] would create and check out branch '{new_branch}' and start its {next_tag} section in changelog.txt")
        if to_restore:
            print("  [dry-run] stashes left in place; run again (without --dry-run) to restore them")
        return 0

    proc.run(repo_root, f"create branch {new_branch}", ["git", "checkout", "-b", new_branch])
    if to_restore:
        print("  » restore stashed changes")
    # starts the new version's section *after* the stashes are applied (see restore_stash), holding
    # only the entries the stashes added
    restore_stashes(repo_root, to_restore, next_tag)
    output.ok(f"changelog.txt: {next_tag} section started for the new branch")

    if not args.no_mod_sync:
        mod_sync.sync(repo_root, args.game_path)
        mod_sync.set_version(next_tag.removeprefix(release_version.TAG_PREFIX), args.game_path)

    output.ok(f"ready on branch '{new_branch}'")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
