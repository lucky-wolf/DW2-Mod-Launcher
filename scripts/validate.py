#!/usr/bin/env python3
"""The single canonical validation suite: restore, `dotnet format --verify-no-changes`, build,
test. .github/workflows/ci.yml calls this directly and open-pr.py runs the same verbs
(lib/dotnet_checks.py) as its local pre-flight, so CI and local checks can't drift apart.

Usage:
  scripts/validate.py
"""

import argparse
import sys
from pathlib import Path

from lib import dotnet_checks, output

REPO_ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.parse_args()

    output.step("restore")
    dotnet_checks.run_dotnet_restore(REPO_ROOT)

    output.step("format")
    if not dotnet_checks.check_dotnet_format(REPO_ROOT):
        output.fail("formatting issues - run `dotnet format DW2ModLauncher.sln`")

    output.step("build & test")
    dotnet_checks.run_dotnet_build(REPO_ROOT)
    dotnet_checks.run_dotnet_test(REPO_ROOT)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        output.abort("interrupted")
        sys.exit(130)
