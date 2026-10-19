#!/usr/bin/env python3
"""Run the same checks CI runs, commit any uncommitted changes, push, and open a GitHub PR.

The PR is titled "Released as vX.Y.Z" (the version release.py will tag when it merges; see
docs/CI Releases.md), or "Housekeeping (no release)" when the branch changes no file the release
workflow watches. Its description is the newest section of changelog.txt
(falling back to this branch's commit subjects when that is empty). When the branch's PR is already open with a
description, it offers to keep that and append the new changelog.txt entries rather than replace it.

Merges here are rebases, so it first checks the branch is on top of origin/<target> and offers to rebase it (aborting the
whole run if the rebase conflicts).

Usage:
  scripts/open-pr.py
  scripts/open-pr.py --target main --draft
  scripts/open-pr.py --skip-checks   # skip the checks, just push + open the PR
  scripts/open-pr.py --dry-run       # run the checks, commit/push/PR nothing
  scripts/open-pr.py --no-mod-sync   # leave mods/DW2 Mod Launcher (the launcher's Workshop item) alone
"""

import argparse
import subprocess
import sys
import webbrowser
from pathlib import Path

from lib import changelog, dotnet_checks, github, launcher_build, mod_sync, output, proc, release_version


def compute_pr_title(repo_root: Path, target: str, fallback: str) -> str:
    """"Housekeeping (no release)" when the branch touches nothing the release workflow watches (CI
    won't tag or build anything); else "Released as vX.Y.Z" when the next version can be predicted
    (relies on locally-known tags; new-branch.py fetches them), else the branch name."""
    if not release_version.branch_will_release(repo_root, target):
        return "Housekeeping (no release)"
    tag = release_version.next_tag(repo_root)
    return f"Released as {tag}" if tag else fallback


def compute_pr_description(repo_root: Path, target: str, use_changelog: bool = True) -> str:
    """The entries of changelog.txt's newest section (see lib/changelog.py), one bullet each; when there are none (or
    use_changelog is False), one bullet per commit on this branch that isn't on origin/<target>, oldest first."""
    entries = changelog.read_entries(repo_root) if use_changelog else []
    if entries:
        return "\n".join(f"- {entry}" for entry in entries)
    log = proc.git(repo_root, "log", f"origin/{target}..HEAD", "--reverse", "--format=%s").stdout
    return "\n".join(f"- {line}" for line in log.splitlines() if line.strip())


def handle_format_check(repo_root: Path) -> None:
    if dotnet_checks.check_dotnet_format(repo_root):
        return
    print()
    if output.confirm("  Run 'dotnet format' now to fix (not staged)?", default=True):
        dotnet_checks.apply_dotnet_format(repo_root)
        output.ok("dotnet format applied (not staged) - review before committing")
    else:
        print("  continuing with formatting issues present")


def ensure_rebased(repo_root: Path, target: str, dry_run: bool) -> bool:
    """Merges here are rebases, so the branch has to sit on top of origin/<target>. When it doesn't, offers to rebase it
    (autostashing uncommitted changes); a rebase that conflicts is aborted and so is the whole run. Returns whether the
    branch was rebased, which means a branch already on origin has to be pushed with --force-with-lease."""
    proc.run(repo_root, f"git fetch origin {target}", ["git", "fetch", "origin", target])
    behind = int(proc.git(repo_root, "rev-list", "--count", f"HEAD..origin/{target}").stdout.strip())
    if behind == 0:
        output.ok(f"on top of origin/{target}")
        return False

    print(f"  This branch is {behind} commit(s) behind origin/{target}.")
    if dry_run:
        print(f"  [dry-run] would offer to rebase onto origin/{target}")
        return False
    if not output.confirm(f"  Rebase onto origin/{target} now?", default=True):
        output.fail(f"aborted - rebase onto origin/{target} first (merges here are rebases)")

    # changelog.txt: main can carry sections this branch lacks (merged PRs, outside contributors' included), and both sides adding a
    # section at the top always conflicts. So the branch's own entries are set aside, the file is taken from main, and the
    # branch's section is rebuilt on top of it after the rebase.
    changelog_file = repo_root / changelog.CHANGELOG_PATH
    original_changelog = changelog_file.read_bytes() if changelog_file.exists() else None
    base = proc.git(repo_root, "merge-base", "HEAD", f"origin/{target}").stdout.strip()
    mine = changelog.entries_added_since(repo_root, base)
    tracked = proc.git(repo_root, "cat-file", "-e", f"HEAD:{changelog.CHANGELOG_PATH.as_posix()}").returncode == 0
    if tracked:
        proc.git(repo_root, "checkout", "HEAD", "--", str(changelog.CHANGELOG_PATH))
    elif changelog_file.exists():
        changelog_file.unlink()

    def give_up(message: str) -> None:
        if original_changelog is not None:
            changelog_file.write_bytes(original_changelog)
        output.fail(message)

    result = proc.run(repo_root, f"git rebase origin/{target}", ["git", "rebase", "--autostash", f"origin/{target}"], check=False)
    while result.returncode != 0:
        unmerged = proc.git(repo_root, "diff", "--name-only", "--diff-filter=U").stdout.split()
        if unmerged != [changelog.CHANGELOG_PATH.as_posix()]:
            proc.run(repo_root, "git rebase --abort", ["git", "rebase", "--abort"], check=False)
            give_up(f"rebase onto origin/{target} failed (conflicts in {', '.join(unmerged) or 'unknown files'}) and was aborted - resolve it by hand, then run this again")
        # in a rebase "ours" is the branch being rebased onto, i.e. main's file; the branch's section is rebuilt below
        proc.run(repo_root, "take main's changelog.txt", ["git", "checkout", "--ours", "--", str(changelog.CHANGELOG_PATH)])
        proc.run(repo_root, "git add changelog.txt", ["git", "add", "--", str(changelog.CHANGELOG_PATH)])
        result = proc.run(repo_root, "git rebase --continue", ["git", "-c", "core.editor=true", "rebase", "--continue"], check=False)
    if proc.git(repo_root, "diff", "--name-only", "--diff-filter=U").stdout.strip():
        give_up("rebased, but re-applying your uncommitted changes conflicted - resolve them (git stash list has the copy), then run this again")

    # main's tags are only known locally once fetched, so the heading is the next release version as of now
    heading = release_version.next_tag(repo_root)
    if not heading:
        output.fail("could not work out the next release version (.version is missing or malformed) to head the changelog.txt section")
    changelog.rebuild_from(repo_root, f"origin/{target}", heading, mine)
    output.ok(f"rebased onto origin/{target}")
    return True


def choose_stale_changelog_action(heading: str) -> str:
    """What to do about a changelog.txt whose newest section is a version that is already released: "reuse" it anyway,
    describe the PR by its "commits" instead, or "abort" the whole run. Letters, not
    digits, and anything else is re-asked - like output.confirm, so a stray key never picks an action."""
    print(f"    [r] use the {heading} section as the PR description")
    print("    [c] ignore it: describe the PR by its commit subjects")
    print("    [a] abort: nothing is pushed, so you can add a section to changelog.txt first")
    while True:
        answer = input("  Stale changelog.txt - what now? [r/c/a] ").strip().lower()
        if answer in ("r", "reuse"):
            return "reuse"
        if answer in ("c", "commits"):
            return "commits"
        if answer in ("a", "abort", "n", "no", "q"):
            return "abort"
        print("  please answer r, c or a")


def offer_to_extend_open_pr(repo_root: Path, branch: str, entries: list[str], new_description: str) -> str:
    """When the branch's PR is already open with a description, offers to keep it and append the entries it lacks (the
    default) instead of replacing it with just the changelog.txt section. Returns the description to use."""
    try:
        current = github.open_pr_body(repo_root, branch)
    except RuntimeError as e:
        output.fail(f"could not read the open PR's description: {e}")
    if not current or not current.strip():
        return new_description
    present = {line.strip() for line in current.splitlines()}
    missing = [entry for entry in entries if f"- {entry}" not in present]
    if not missing:
        print("  note: the open PR's description already has every changelog.txt entry, so it is left as it is")
        return current
    print(f"\n  The open PR already has a description; changelog.txt adds {len(missing)} entr{'y' if len(missing) == 1 else 'ies'}:")
    for entry in missing:
        print(f"    - {entry}")
    if output.confirm("  Keep the existing description and append these?", default=True):
        return current.rstrip("\n") + "\n" + "\n".join(f"- {entry}" for entry in missing)
    print("  replacing the description with the changelog.txt section")
    return new_description


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--target", default="main")
    parser.add_argument("--draft", action="store_true")
    parser.add_argument("--skip-checks", action="store_true")
    parser.add_argument("--no-mod-sync", action="store_true", help="do not sync the launcher's Workshop item (mods/DW2 Mod Launcher) with the repo")
    parser.add_argument("--game-path", type=Path, default=None, help="the Distant Worlds 2 install for the mod folder sync. Default: DW2_PATH, then the usual Steam location.")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="run the checks as normal, but commit nothing and stop before the push/PR step (no push, no GitHub calls)",
    )
    args = parser.parse_args()
    target = args.target

    repo_root = proc.repo_root()
    current_branch = proc.git(repo_root, "branch", "--show-current").stdout.strip()
    if current_branch == target:
        output.fail(f"already on '{target}' - switch to a feature branch first")

    launcher_build.require_launcher_closed()
    launcher_build.require_game_closed()

    output.step("gh readiness")
    github.require_gh_auth()
    output.ok("gh logged in")

    # Before the checks, so they test the code that will actually merge.
    output.step("up to date with " + target)
    rebased = ensure_rebased(repo_root, target, args.dry_run)

    if not args.skip_checks:
        output.step("code quality")
        dotnet_checks.run_dotnet_restore(repo_root)
        handle_format_check(repo_root)

        output.step("build & test")
        dotnet_checks.run_dotnet_build(repo_root)
        dotnet_checks.run_dotnet_test(repo_root)

    # After the changelog is final, so the launcher's Workshop item has this release's entries ready to publish; a description
    # copied into the repo from the mod folder shows up as an uncommitted change just below.
    if not args.no_mod_sync:
        mod_sync.sync(repo_root, args.game_path, dry_run=args.dry_run)

    output.step("uncommitted changes")
    if proc.git(repo_root, "status", "--porcelain").stdout.strip():
        print("\n  Uncommitted changes:")
        subprocess.run(["git", "status", "--short"], cwd=repo_root)

        if args.dry_run:
            print("  [dry-run] would offer to commit these")
        elif output.confirm("\n  Commit all changes?", default=True):
            msg = input("  Commit message: ").strip()
            if not msg:
                output.fail("empty commit message - aborted")
            proc.run(repo_root, "git add", ["git", "add", "-A"])
            proc.run(repo_root, "git commit", ["git", "commit", "-m", msg])
            output.ok(f"committed: {msg}")
        else:
            print("  Continuing without committing...")
    else:
        output.ok("working tree clean")

    output.step("push & open PR")
    pr_title = compute_pr_title(repo_root, target, current_branch)

    use_changelog = True
    entries = changelog.read_entries(repo_root)
    if not entries:
        print("  note: the newest changelog.txt section has no entries, so the description falls back to commit subjects")
    elif changelog.is_released(repo_root):
        heading = changelog.current_heading(repo_root)
        print(f"  WARNING: the newest changelog.txt section ({heading}) is a version that is already released - stale entries from earlier work?")
        print("           (the release notes come from this description)")
        choice = choose_stale_changelog_action(heading)
        if choice == "abort":
            output.fail("aborted - nothing pushed (add a section for the next version to changelog.txt, then run this again)")
        use_changelog = choice == "reuse"
    pr_description = compute_pr_description(repo_root, target, use_changelog)
    # Re-running for an open PR whose description was edited since: replacing it would drop those bullets, so offer to keep
    # them and add the new entries after them.
    if entries and use_changelog:
        pr_description = offer_to_extend_open_pr(repo_root, current_branch, entries, pr_description)
    # Re-running for an open PR whose list was already cleared by an earlier run: the description written then is the
    # real one; rebuilding it from commit subjects would overwrite it with a worse one.
    keep_description = not entries and github.open_pr_exists(repo_root, current_branch)
    if keep_description:
        print("  note: the newest changelog.txt section is empty and the PR is already open, so its description is left as it is")
    print(f"  Branch: {current_branch} -> {target}")
    print(f"  PR title: {pr_title}")
    print(f"  PR description:\n{pr_description}" if pr_description else "  PR description: (none)")

    if args.dry_run:
        print(f"  [dry-run] would push '{current_branch}' and open/update a PR{' (draft)' if args.draft else ''}")
        print("  [dry-run] no push, no GitHub calls, no browser opened")
        return 0

    if not output.confirm("\n  Push and open the PR with the above?", default=True):
        output.fail("aborted - nothing pushed")

    # A rebased branch that is already on origin has different commits there, so a plain push would be rejected.
    push = ["git", "push", "-u", "origin", current_branch]
    if rebased:
        push.insert(2, "--force-with-lease")
    proc.run(repo_root, f"git push {current_branch}", push)
    output.ok("pushed")

    try:
        url, created = github.create_or_update_pr(repo_root, current_branch, target, pr_title, None if keep_description else pr_description, args.draft)
    except RuntimeError as e:
        output.fail(f"branch is pushed, but creating/updating the PR via gh failed: {e}")
    output.ok("PR created" if created else "existing PR title/description updated")
    print(f"\n  {url}")
    webbrowser.open(url)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
