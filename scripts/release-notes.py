#!/usr/bin/env python3
"""Write the GitHub Release notes: the docs/focus.md list that was the merged PR's description.

CI-only helper for .github/workflows/release.yml. Writes nothing (and leaves the file absent) when
focus.md has no entries, so the workflow can fall back to GitHub's generated notes.

Usage:
  scripts/release-notes.py OUTPUT_FILE
"""

import sys
from pathlib import Path

from lib import focus

REPO_ROOT = Path(__file__).resolve().parent.parent


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 0 if len(sys.argv) == 2 else 2
    entries = focus.read_entries(REPO_ROOT)
    if not entries:
        print("docs/focus.md has no entries; no notes file written.")
        return 0
    Path(sys.argv[1]).write_text("".join(f"- {entry}\n" for entry in entries), encoding="utf-8", newline="\n")
    print(f"[ok] wrote {len(entries)} release note(s) to {sys.argv[1]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
