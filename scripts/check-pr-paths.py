#!/usr/bin/env python3
"""Lists the files a pull request touches and flags the ones outside the code: CI workflows, build/validation
scripts, repo config. Run it BEFORE approving a fork PR's workflow run - CI executes the PR's own copy of the
workflow files and of scripts/validate.py on GitHub's runners, so those are the files that must be read first.

Usage:
  scripts/check-pr-paths.py <PR number>

Exit code 0: only code/docs touched. 1: a protected path is touched (read it before approving anything).
"""

import argparse
import json
import subprocess
import sys

from lib import github, output

# Prefixes/names that run on a runner or change how the repo is built, validated, owned or released.
PROTECTED = (
    ".github/",
    "scripts/",
    ".config/",
    ".editorconfig",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "nuget.config",
    "NuGet.Config",
)
PROTECTED_SUFFIXES = (".csproj", ".props", ".targets", ".sln", ".slnx", ".sh", ".ps1", ".bat", ".cmd", ".py")


def is_protected(path: str) -> bool:
    return path.startswith(PROTECTED) or path.endswith(PROTECTED_SUFFIXES)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("pr", type=int, help="pull request number")
    args = parser.parse_args()

    github.require_gh_auth()
    result = subprocess.run(["gh", "pr", "view", str(args.pr), "--json", "files,title,author"], capture_output=True, text=True)
    if result.returncode != 0:
        output.fail("gh could not read PR #%d: %s" % (args.pr, result.stderr.strip()))
    pr = json.loads(result.stdout)

    output.step("PR #%d by %s: %s" % (args.pr, pr["author"]["login"], pr["title"]))
    flagged = [f["path"] for f in pr["files"] if is_protected(f["path"])]
    for f in pr["files"]:
        line = "%s  (+%d -%d)" % (f["path"], f["additions"], f["deletions"])
        if is_protected(f["path"]):
            output.warn("PROTECTED  " + line)
        else:
            output.info(line)

    if flagged:
        output.warn("%d protected file(s) touched - read them before approving the workflow run or the PR" % len(flagged))
        return 1
    output.ok("no protected paths touched")
    return 0


if __name__ == "__main__":
    sys.exit(main())
