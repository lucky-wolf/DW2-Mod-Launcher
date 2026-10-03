# docs

Design notes, feature specs, and reference material for DW2 Mod Launcher that are too long-lived for a PR description or issue thread.

Suggested contents as the project grows:
- Mod conflict-detection rules and edge cases
- Load-order and merge-output semantics (how the merged mod folder is built/curated)
- AI-assisted conflict resolution design (inputs, prompts/heuristics, human-in-the-loop expectations)
- [DLL-injection launch mechanism](DLL%20Injection.md) (CLI args, manifest schema)
- [Steam Workshop publish](workshop-publish.md) (`--ugc-publish`, capturing a new item's id)

- [focus.md](focus.md): the running list of what the current branch has accomplished; becomes the PR description (reset by `scripts/new-branch.py`)

[archived/](archived/) holds finished plans kept for history (e.g. the [Linux support plan](archived/linux-support.md)).

See [../AGENTS.md](../AGENTS.md) for collective contributor/agent directives.
