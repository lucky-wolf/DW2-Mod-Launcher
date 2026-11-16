# Hook compatibility claims (replaces version pins)

Status (2026-11-15): built and deployed, not yet tried in play. Facts, claims in the hooks file, analyzer, automatic conversion of "Compatible with" pins, loader signature, `hook-manifest.py` support, and the UI: the **Compatible with** list in Edit Properties/Publish, and the Details… dialog on the Conflict/Caution bars. How it works as built: [../Code Hooks.md](<../Code Hooks.md#compatibility-claims>). Author-facing text: Mod Author README, "Code hooks".

Decisions (owner): patch **shape** facts only, no body hash and no game-method hash; the digest is stored with the facts and is what claims compare. Claims are written only to the author's own mod (a Workshop partner is covered by carrying its patch facts in the claim). Existing "Compatible with" pins are converted automatically once, drift is handled from then on; the old key is to be phased out and finally ignored. UI: a real list view (tick, severity icon, mod + version, short hook name, argument count, copy icon per row, tooltip with everything seen), the same list in the Publish/Edit Properties section and in the dialog behind **Details…**; no per-level filter, the whole list is shown; a player sees it read-only and only for hooks that still clash. Keep titles short (Type:Method and an argument count, never the argument types) and put the long text in tooltips and the copy icon.

Superseded below: player-side claims in `launcher_settings.json` (not built; acknowledgements stay version-keyed, in the text Code Hooks Report).

## The change

Today a review says "mod A is fine with mod B **at version X**". Every update of either mod throws the review away (`ReviewCarryOver` exists only to copy it forward by hand), although most updates touch no shared hook.

New rule: a review is a **claim about the hooks themselves**. "A's patches on target T and B's patches on target T were looked at and coexist." The claim holds for as long as both sides' patches on T are exactly as they were when it was made. Any change to either side drops that one claim and says what changed. Mod versions no longer matter.

## What a claim records

One claim per shared target per pair of mods (the "per hook basis"), holding the **facts** of each side's patches on that target, as readable text, not a hash:

```
target:  DistantWorlds.Types.Galaxy:InflictDamage(System.Int32)
sides:
  <mod A key>: transpiler | rewrites | prio 400 | patch (IEnumerable<CodeInstruction>, ILGenerator) -> IEnumerable<CodeInstruction>
  <mod B key>: prefix | skips-original | prio 400 | after [a.id] | patch (ref int, Galaxy) -> bool
reviewed: <date>, note: "<why it is fine>"
```

A side is the sorted list of that mod's patches on the target (a mod can patch one method with a prefix and a postfix). Facts per patch: kind, effect, priority, before/after, and the **patch method's own signature** (parameter types and names such as `ref`/`__result`/`__instance`, return type). The target string already carries the game method's argument types, so a changed overload is simply a different target: the old claim is orphaned and the new overlap is unreviewed.

Storing facts, not a digest, buys three things: the report can show **what changed** ("B's prefix now returns `bool`, so it can skip the original"), a changed fingerprint algorithm re-compares from the stored facts instead of mass-invalidating, and the claim file reads for itself in a diff.

A claim covers **every subset of the patches it was made with**: a patch that is missing now (the author switched a feature off, so the mod installs less) cannot create a conflict and does not drop the claim; the report lists it as "not installed now". Marking again over an earlier claim keeps the union of both, so a review made with a feature off does not lose what an earlier one covered. A claim is lost when, for either side on that target: a patch is added that the claim does not list (an extra prefix, a second transpiler), kind changes (postfix to prefix), effect changes, patch signature changes, priority or before/after changes. Ids are not part of it (Harmony ID is free-form); the owning mod's key is.

Not part of the facts by default: the patch's method body. A body edit that changes nothing in the facts is a refactor far more often than a conflict, and counting it would put the claim back to per-release. See open question 1.

## Where claims live and how they travel

- **Author claims** go in the mod's own `dw2modlauncher.hooks.json` (new `claims` array, beside `hooks` and `channels`), so they ship with the mod and reach every player. A claim names the partner by the same key the dependency matching uses (name, case-insensitive) and carries both sides' facts. Either mod's file may hold it. The launcher accepts a claim from either file and **verifies it against the facts it computes itself**, so a stale or wrong claim from an author cannot hide a change.
- **Player claims** (the player pressing acknowledge in the Code Hooks Report) go in `launcher_settings.json`, same shape, replacing `AcknowledgedHooks`. They are for mods the player does not own; an author claim and a player claim are equivalent when matching.
- A claim in force makes the pair **Info** (not shown on the mod list), as `hooksCoordinatedWith` does now. Players who have only claims in force see nothing, except a quiet "Reviewed compatible with N mods" line in the mod's Code hooks section (transparent, no bar).
- A claim that no longer matches shows the overlap at its normal grade (Caution/Conflict) with the extra line "Hook changed since reviewed: ..." and the diff. Same bars as today; only the change line is new.

## Telling the owner

- On every scan and before launch, a claim whose side changed is listed in the launcher (a "Hook changes" section in the Code Hooks Report; badge on the mod list for the owner's own mods). It names the mod, the target and the diff.
- `scripts/hook-manifest.py --check` (DW2-XL) reports the same for XL's mods after a session, since it regenerates from observed data.
- **Marking investigated:** each row in the report gets *Mark compatible* (writes the claim with an optional note, into the owning local mod's hooks file, else the player's settings) and *Investigate* (copies the diff and both sides' ENGINE `why:` text). A pair-wide *Mark all* exists for the first pass, but it still writes one claim per hook.

## What the loader must record

`ObservedPatchInfo` gains the patch method's signature (parameter types and names, return type). Everything else needed is already there (kind, effect, priority, before/after). The declared file gets the same field, so a claim can be verified for a mod that has not run since updating. `hook-manifest.py` copies it through.

An updated mod that has neither run nor ships a regenerated manifest has unknown facts. Its claims are shown as **unverified** ("not run since 1.36.16"), neither valid nor lost, matching how `HookUnrunVersion` behaves today and the fail-loudly rule.

## Migration

- `hooksCoordinatedWith` pinned entries and `AcknowledgedHooks` / `AcknowledgedFileConflicts` keys carry versions and no facts, so they cannot be converted without looking at the current facts. See open question 2.
- File-collision acknowledgements are a different subject (files, not hooks) and stay version-keyed unless told otherwise.
- `ReviewCarryOver` loses its hook parts once claims exist (file parts remain).
- The Publish / Edit Properties "Compatible with" list becomes a list of claims with their state (in force, changed, unverified), edited by the same + / remove controls.

## Build order

1. Loader and manifest: record patch signature; `HookFacts` in Core builds the canonical facts text per side (unit tested: each kind of change in the list above flips it).
2. `HookClaim` model and reader/writer (hooks file `claims`; settings), analyzer consults claims instead of version pins; changed-claim diff.
3. Report UI: mark compatible, investigate, hook changes section; strings in every pack.
4. Publish dialog list; migration; `hook-manifest.py` support; update Code Hooks.md and AGENTS.md.

## Open items

1. **XL's mods lose claims on deploy (gap).** Claims are written into the installed mod folder; the repo copy in `dll/<project>/` is what `build-mods.py` deploys and `hook-manifest.py` regenerates (it keeps only the repo file's `claims`). Marking hooks on an XL mod in the launcher is therefore lost at the next build. Fix: have `hook-manifest.py` also read the installed file's claims (newer wins, like the other synced files) or add the hooks file to the two-way sync. Same for claims the automatic conversion writes into an XL mod.
2. **Phase out `hooksCoordinatedWith`**: see the next section; it has a target and a to-do.
3. **Player acknowledgements** (`AcknowledgedHooks`, keyed by mod versions) are not claims yet; they live in the text Code Hooks Report. Decide whether the read-only Details dialog gets an acknowledge button.
4. **A separate "Hook changes" list** was not built; a changed claim shows as a note on the mod's Code hooks and as the orange "changed" row in the list.
5. **Dead helpers:** `HookReview.Check` and `HookReview.Restamp` are used only by tests now; remove or use.
6. **Translations:** the ja, ru and zh texts for the new keys (claims notes, list columns and tooltips, dialog title) were written by the assistant and need an owner/translator check before release.
7. **Try it in play:** the automatic conversion needs one game session on the new loader (patch signatures) before pairs convert; confirm what the owner's mods do.

## Phase-out of `hooksCoordinatedWith` (the old whole-mod "Compatible with")

**Decision (owner, 2026-11-15):** the old system disappears. The key is superseded now, and about **three launcher releases after claims ship** it becomes illegal: the launcher stops reading it. If claims ship as 1.0.41 the removal is 1.0.44. **To-do: remove it then** (checklist below). Search for `PHASE-OUT` in the source to find every piece.

**Rules until then** (what the launcher does today, and must keep doing so nothing changes under authors without notice):
- An entry in `dw2modlauncher.json` `hooksCoordinatedWith`, from either mod's side, makes the pair's overlaps Info, as before. A pinned entry (`Name@1.2.3`) only while the other mod's installed version matches; an unpinned one never expires.
- As soon as a pair has any claim (in either mod's hooks file), the old entry is ignored for that pair: each hook then stands on its own claim, and a hook with none is unreviewed.
- Conversion (`HookClaimMigration`) runs on every scan: an entry that holds now becomes claims for every hook the pair shares, written into the entry's mod. Only when both mods' patches are known in full (one game session on a loader that records signatures), only for mods installed locally, a pair whole or not at all. The entries stay in the file.
- No dialog edits the key. It is documented as superseded in the Mod Author README and Code Hooks.md. `ReviewCarryOver` still moves pinned entries to the new version on a publish.
- A Workshop mod's entries cannot be converted (the launcher never writes into them); they keep working as the old whole-mod claim until removal, so authors of such mods must ship claims before then.

**Steps:**
1. *Next release or two:* a launcher note on every mod that still carries the key ("superseded by hook claims; stops working in launcher X"), shown in the mod's Code hooks notes (`HookNote`, a language-pack key in every pack), and a line in the launcher changelog and the Workshop description saying the old list is going away and when.
2. *Before removing:* run the game with all of the owner's mods; confirm every pair that relied on an entry has converted (no overlap is Info only because of an entry); fix mods that cannot convert; strip the entries from the owner's `dw2modlauncher.json` files (XL's code mods, translation packs).
3. *The removal release:* stop reading the key (`ModScanner`, `ModInfo.HooksCoordinatedWith`); delete the pin path in `HookOverlapAnalyzer.GradePair` and `Coordinates`/`SplitCoordinationEntry`/`CoordinationEntryFor`, `HookClaimMigration`, the pin parts of `ReviewCarryOver` (and `PublishCarryOverFailed` with its texts in every pack), `LauncherMetaWriter.SetCoordinatedWith`, and their tests; a mod that still carries the key gets a one-line note that it is ignored. Remove the mentions from the Mod Author README, Code Hooks.md and AGENTS.md, and add a short player-facing changelog line.
