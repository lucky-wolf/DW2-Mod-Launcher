"""GitHub helpers for scripts/*.py, all via the `gh` CLI (which owns auth - no tokens here).
usage: from lib import github; github.require_gh_auth()
"""

import json
import subprocess
from pathlib import Path

from . import output


def require_gh_auth() -> None:
    """Fails early, before anything irreversible, when gh is missing or not logged in."""
    try:
        result = subprocess.run(["gh", "auth", "status"], capture_output=True, text=True)
    except FileNotFoundError:
        output.fail("the GitHub CLI (gh) is not installed - see https://cli.github.com")
    if result.returncode != 0:
        output.fail("gh is not logged in - run `gh auth login`")


def create_or_update_pr(repo_root: Path, branch: str, target: str, title: str, body: str, draft: bool) -> tuple[str, bool]:
    """Creates the PR for branch, or updates the open one's title/body. Returns (url, created).
    Raises RuntimeError with gh's message on failure."""
    listing = subprocess.run(
        ["gh", "pr", "list", "--head", branch, "--state", "open", "--json", "url,number"],
        cwd=repo_root, capture_output=True, text=True,
    )
    if listing.returncode != 0:
        raise RuntimeError(listing.stderr.strip())
    existing = json.loads(listing.stdout or "[]")

    if existing:
        url = existing[0]["url"]
        # REST, not `gh pr edit`: that goes through GraphQL and fails on the deprecated "Projects
        # (classic)" field (repository.pullRequest.projectCards). gh substitutes {owner}/{repo}.
        edit = subprocess.run(
            ["gh", "api", "--method", "PATCH", f"repos/{{owner}}/{{repo}}/pulls/{existing[0]['number']}",
             "-f", f"title={title}", "-f", f"body={body}"],
            cwd=repo_root, capture_output=True, text=True,
        )
        if edit.returncode != 0:
            raise RuntimeError(edit.stderr.strip() or edit.stdout.strip())
        return url, False

    cmd = ["gh", "pr", "create", "--base", target, "--head", branch, "--title", title, "--body", body]
    if draft:
        cmd.append("--draft")
    create = subprocess.run(cmd, cwd=repo_root, capture_output=True, text=True)
    if create.returncode != 0:
        raise RuntimeError(create.stderr.strip())
    return create.stdout.strip().splitlines()[-1], True
