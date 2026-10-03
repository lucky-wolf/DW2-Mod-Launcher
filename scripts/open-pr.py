#!/usr/bin/env python3
"""Run the same checks CI runs, commit any uncommitted changes, push, and open a GitHub PR.

The PR is titled "Released as vX.Y.Z" (the version release.py will tag when it merges; see
docs/CI Releases.md) and its description is the list in docs/focus.md
(falling back to this branch's commit subjects when that is empty).

Usage:
  scripts/open-pr.py
  scripts/open-pr.py --target main --draft
  scripts/open-pr.py --skip-checks   # skip the checks, just push + open the PR
  scripts/open-pr.py --dry-run       # run the checks, commit/push/PR nothing
"""

import argparse
import subprocess
import sys
import webbrowser
from pathlib import Path

from lib import dotnet_checks, focus, github, launcher_build, output, proc, release_version


def compute_pr_title(repo_root: Path, fallback: str) -> str:
    """"Released as vX.Y.Z" when the next version can be predicted (relies on locally-known tags;
    new-branch.py fetches them), else the branch name."""
    tag = release_version.next_tag(repo_root)
    return f"Released as {tag}" if tag else fallback


def compute_pr_description(repo_root: Path, target: str) -> str:
    """The entries in docs/focus.md (see lib/focus.py), one bullet each; when there are none, one bullet per
    commit on this branch that isn't on origin/<target>, oldest first."""
    entries = focus.read_entries(repo_root)
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--target", default="main")
    parser.add_argument("--draft", action="store_true")
    parser.add_argument("--skip-checks", action="store_true")
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

    output.step("gh readiness")
    github.require_gh_auth()
    output.ok("gh logged in")

    if not args.skip_checks:
        output.step("code quality")
        dotnet_checks.run_dotnet_restore(repo_root)
        handle_format_check(repo_root)

        output.step("build & test")
        dotnet_checks.run_dotnet_build(repo_root)
        dotnet_checks.run_dotnet_test(repo_root)

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
    pr_title = compute_pr_title(repo_root, current_branch)
    pr_description = compute_pr_description(repo_root, target)

    if not focus.read_entries(repo_root):
        print("  note: docs/focus.md has no entries, so the description falls back to commit subjects")
    print(f"  Branch: {current_branch} -> {target}")
    print(f"  PR title: {pr_title}")
    print(f"  PR description:\n{pr_description}" if pr_description else "  PR description: (none)")

    if args.dry_run:
        print(f"  [dry-run] would push '{current_branch}' and open/update a PR{' (draft)' if args.draft else ''}")
        print("  [dry-run] no push, no GitHub calls, no browser opened")
        return 0

    if not output.confirm("\n  Push and open the PR with the above?", default=True):
        output.fail("aborted - nothing pushed")

    proc.run(repo_root, f"git push {current_branch}", ["git", "push", "-u", "origin", current_branch])
    output.ok("pushed")

    try:
        url, created = github.create_or_update_pr(repo_root, current_branch, target, pr_title, pr_description, args.draft)
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
