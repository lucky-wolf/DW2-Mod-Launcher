"""Saves a file before a sync script overwrites it, so a run can always be backed out.

Everything one run saves goes under temp/backups/<run timestamp>/<side>/<file name>, and restore.txt in that folder lists
"backup -> original" for every file, one per line. To back out a run, copy each backup over its original.
"""

import shutil
import time
from pathlib import Path

RUN = time.strftime("%Y%m%d-%H%M%S")
ROOT = Path(__file__).resolve().parents[2] / "temp" / "backups" / RUN


def save(file: Path, side: str) -> Path:
    """Copies file (a "repo" or "mod" side copy) into this run's backup folder and returns where it went."""
    target = ROOT / side / file.name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(file, target)
    with (ROOT / "restore.txt").open("a", encoding="utf-8", newline="\n") as manifest:
        manifest.write(f"{target} -> {file}\n")
    return target
