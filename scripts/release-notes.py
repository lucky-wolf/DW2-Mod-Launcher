#!/usr/bin/env python3
"""Write the GitHub Release notes: the description of the PR that was merged to produce the pushed commit.

CI-only helper for .github/workflows/release.yml (needs GH_TOKEN, GITHUB_REPOSITORY and GITHUB_SHA).
Writes nothing (and leaves the file absent) when no PR is found or its description is empty, so the
workflow can fall back to GitHub's generated notes. docs/focus.md is deliberately not read here: it is
only a local drafting aid for open-pr.py, and a stale copy on main must never end up in a release.

Usage:
  scripts/release-notes.py OUTPUT_FILE
"""

import json
import os
import subprocess
import sys
from pathlib import Path


def merged_pr_body(repo: str, sha: str) -> str:
    """The body of the first PR GitHub associates with the commit (works for rebase merges too)."""
    result = subprocess.run(
        ["gh", "api", f"repos/{repo}/commits/{sha}/pulls"], capture_output=True, encoding="utf-8"
    )
    if result.returncode != 0:
        print(f"gh api failed: {result.stderr.strip()}")
        return ""
    prs = json.loads(result.stdout or "[]")
    return (prs[0].get("body") or "").strip() if prs else ""


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0 if len(sys.argv) == 2 else 2
    repo, sha = os.environ.get("GITHUB_REPOSITORY"), os.environ.get("GITHUB_SHA")
    if not repo or not sha:
        print("GITHUB_REPOSITORY / GITHUB_SHA not set; no notes file written.")
        return 0
    body = merged_pr_body(repo, sha)
    if not body:
        print("no merged PR description found; no notes file written.")
        return 0
    Path(sys.argv[1]).write_text(body + "\n", encoding="utf-8", newline="\n")
    print(f"[ok] wrote release notes from the merged PR description to {sys.argv[1]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
