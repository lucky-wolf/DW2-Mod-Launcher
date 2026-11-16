# Code hooks: which mods patch the same game code

The launcher warns when two enabled code mods patch the same game method, and when a mod and the mod it talks to over a channel disagree on the contract's version. It works in two layers: what the loader **saw** patched in the last game session (needs nothing from mod authors), and what a mod **declares** in a file beside it (the warning before the game starts). Code: `HookFiles`, `HookOverlapAnalyzer`, `HookReport`, `SteamAuthors` in `DW2ModLauncher.Core`; `ObservedHooks` and `LiveChannels` in `DW2ModLauncher.Loader`. Tests: `HookOverlapAnalyzerTests`, `LiveChannelRegistryTests`.

## What the player sees

- The mod list's health column shows a **Caution** or **Conflict** when another enabled mod patches the same method in a way that can matter. The selected mod's details pane shows a red **Conflict** bar and a gold **Caution** bar, each collapsed to one line ("Conflict: 3 mods, 5 hooks": other mods, distinct game methods) and expanding to one line per other mod with its hook count in parentheses. No method names or Harmony terms there: players see who and how many. A bar with only hook overlaps gets that "N mods, N hooks" header; a bar that also lists other problems (file collisions, enabled twice) says "Conflict (N)", N being the lines listed. A mod with conflicts and overlaps against the same partner is listed only under Conflict. The details pane also has a **Code hooks** section: the mod's patched methods, who shares each, and any stale or mismatched data.
- Each bar's **Details…** button opens the report for that bar alone, for the selected mod: only its conflicts, or only the overlaps that are not conflicts (plus its channel and stale-declaration notes). Its action button acknowledges just those pairs.
- Right-click the mod list, **Code Hooks Report**: every method patched by more than one mod, with severity, author, version and kind (seen or declared). The copy button puts it on the clipboard for a message to another author. Its action button **acknowledges** what is listed (stored in `launcher_settings.json`, per target and per version of both mods, so a new version or a new partner asks again); once everything is acknowledged it offers to forget the acknowledgements.

## Grading

Per pair of mods on one target, using the worst combination of their patches:

| Severity | When |
|---|---|
| Info | Either patch only observes; or the mods are by one author; or either mod declares `hooksCoordinatedWith` the other. Not shown on the mod list. |
| Caution | Both patches change something (`changes-result`, `changes-args`, `rewrites`), including two transpilers (Harmony chains them; they break only if they match the same IL). |
| Conflict | One patch may skip the original (`skips-original`) and the other mod is by a different author. |

One level lower when the two patches order themselves explicitly (Harmony before/after naming the other's ID) or the mods declare a `loadAfter`/`loadBefore` between them. A declared hook whose `setting` is off is ignored.

**Same author** means the same `author` in `dw2modlauncher.json`, or Workshop items created by the same Steam user (`WorkshopCreator`, read by the update check). A Workshop mod that states no author is shown with its creator's Steam handle (looked up from the public profile, remembered by id in `launcher_settings.json`; never written into the mod's files; a failed lookup is logged and leaves the author blank). A mod with no author and no Workshop creator is never assumed to share an author.

`"hooksCoordinatedWith": ["Other Mod"]` in `dw2modlauncher.json` (names matched like a dependency's `mod`) says its author has already dealt with that mod; it is for mods by different authors. `"Other Mod@1.2.3"` covers only that version of it (compared with its mod.json `version`): once the other mod updates, the overlap is graded again, so the author re-checks it. Prefer the pinned form for mods you do not control. Older launchers do not know the `@` form and simply ignore the entry. This key is superseded by hook claims (below): no dialog edits it any more, and existing entries are converted into claims. **It will stop being read about three launcher releases after claims ship** (rules and removal checklist: [plans/hook-compatibility-claims.md](plans/hook-compatibility-claims.md#phase-out-of-hookscoordinatedwith-the-old-whole-mod-compatible-with)).

## Compatibility claims

A claim says "my patches on this game method and that mod's patches on it were looked at and coexist". It holds while each side's patches are among those reviewed (fewer is fine, a feature switched off installs less; a new or changed patch is not), whatever version either mod is (design and decisions: [plans/hook-compatibility-claims.md](plans/hook-compatibility-claims.md)). Code: `HookFacts`, `HookClaims`, `HookClaimMigration`.

- **Facts.** Per mod and target, one text line per patch: kind, effect, priority, before/after, and the patch method's own signature (parameter types and names, `ref`, return type; recorded by the loader as `signature`). Harmony IDs and method bodies are not part of it. A claim stores each side's facts and a 12-digit digest of them; an identical digest holds, otherwise every current patch must be one of the stored facts. Marking again over a claim keeps the union of the facts. A different overload is a different target, so a claim never covers it.
- **Where they live.** In `claims` of the hooks file of the mod whose author made it (`dw2modlauncher.hooks.json`), so they ship with the mod; either mod's file may hold the claim for a pair, and the launcher checks it against the patches it sees itself. It only ever writes to a mod installed locally, never a Workshop copy. `scripts/hook-manifest.py` in DW2-XL keeps `claims` when it regenerates the file and copies `signature`, `priority`, `before` and `after` from the observed file.
- **Effect.** A claim in force makes the pair Info, as a "Compatible with" entry does. A claim whose side changed is graded as if there were none, and the mod's Code hooks notes say what changed (`-` was reviewed, `+` is now). A claim that cannot be checked (a side's patches are not known in full: the mod has not run since updating and its hooks file has no signatures) stays Info and says so.
- **Older "Compatible with" entries** (`hooksCoordinatedWith`) still work. Whenever both mods' patches are known in full, each one that holds now (installed version matches the pin) is converted into claims for every target the pair shares, written into the entry's mod (`HookClaimMigration`, on every scan; a pair is converted whole or not at all). Once a pair has any claim, the entries are ignored for it: a target with no claim of its own is unreviewed. The entries themselves stay in `dw2modlauncher.json`.
- **Marking a hook investigated.** The **Compatible with** list of the Edit Properties and Publish dialogs (`PublishDialogViewModel`, `HookReview`) is a list of this mod's hooks that overlap another published mod's. Columns: a **tick** (investigated and marked compatible), a severity icon (red conflict, orange caution: what the overlap grades if unmarked), the **mod** with its installed version, the **hook** (Type:Method) and its **argument count**. Hovering a row, or clicking the **copy icon** at the end of the row (to hand to an LLM or another author), shows or copies the method in full and both mods' patches (and, for a changed mark, what changed: `-` reviewed, `+` now). Ticking marks the hook as the patches are now; unticking removes the mark. A mark whose patches changed shows unticked with "changed" (tick it again once investigated); other notes are "not installed", "no longer overlaps" and "not checked yet"; a hook whose patches are not known in full cannot be ticked until both mods have run. The orange count in the bar is the overlapping hooks still unticked. Nothing is written until Save or Publish, which write the marks into the mod's hooks file (never over a file that could not be read); Publish asks first when marked hooks changed or overlapping ones are unmarked.
- **From the details pane.** The **Details…** button of the red Conflict and gold Caution bars opens the list of the selected mod's hooks that overlap another mod's (`HookReviewDialog`; the list is `CompatibleListViewModel`/`CompatibleListView`, shared with the Edit Properties and Publish dialogs). Every row has the copy icon, for a report or a question online. For a mod installed locally (the author's own) the rows have ticks and Save writes the marks into the mod's hooks file, so the author investigates and marks hooks in one place. For a Workshop mod the list is read-only (no ticks, no Save) and shows only the hooks that still clash. The text Code Hooks Report (right-click the mod list) remains, and is where a player acknowledges overlaps.
- Not built yet: a separate "Hook changes" list, and the player's own acknowledgements as claims (they are still keyed by mod versions). The `hooksCoordinatedWith` entries are no longer edited anywhere; they stay honored (and converted) until every pair is claims.

## Observed hooks (written by the loader)

`hooks-observed.json`, in the launcher's data folder (`%AppData%\DW2ModLauncher`), written 60 seconds after the mods load and again at game exit (mods patch late). It lists the mods and versions of the session, every patched method (Harmony's spelling, `Namespace.Type:Method(argtypes)`) with each patch's owner, kind, effect, priority and before/after, and the live channels provided and consumed.

- **Owner** is the mod whose folder the patch code was loaded from, not the free-form Harmony ID; the loader's own patches are `launcher`.
- **Effect** is read from the patch method's shape: a prefix returning `bool` -> `skips-original`; a transpiler -> `rewrites`; a by-reference argument -> `changes-args`; a `__result` parameter or a value-returning postfix/finalizer -> `changes-result`; anything else -> `observes`. `observes` is therefore not proof a patch is harmless (a prefix that sets state around the call also reads as `observes`).
- It describes the last session only: each session rewrites it with just the mods that session loaded.
- **History.** The launcher therefore folds that file into `hooks-observed-history.json` (same folder, same shape) whenever it rescans and before every launch (`HookFiles.UpdateObservedHistory`). Each mod a session loaded replaces its own earlier record; a mod that session did not load keeps its record. A mod's observed data is used only when the record is of the same version of the mod as is installed; otherwise the declared file is used, and a mod with neither (updated since it last ran, no declared file) gets a note saying its new version has not been run so its overlaps are unknown; it is a note on the mod's Code hooks, not a health level. A history that cannot be read is reported and left alone, never overwritten.
- **Mods not in the launch list** are checked too, from that history (or their declared file, with every declared hook counted since their settings are not touched). Selecting one shows the Conflict/Caution bars and Code hooks section it would get if enabled, with the enabled mods it clashes with tagged "(not enabled)" where it is listed. Two mods that are both not enabled are never compared, an enabled mod's own health ignores not-enabled partners, and the Code Hooks Report of the whole launcher stays about the enabled mods (a mod's own report adds the pairs it would form). The mod list's health column stays blank for a mod that is not enabled.

## Declared hooks: `dw2modlauncher.hooks.json`

Beside `dw2modlauncher.json`; keys are matched ignoring case. Authors of XL's mods never write it by hand (below); other authors can, using this as the template:

```json
{
  "schema": 1,
  "modVersion": "1.36.5",
  "gameVersion": "1.3.8.0",
  "hooks": [
    { "target": "DistantWorlds.Types.Galaxy:InflictDamage(System.Int32)", "kind": "transpiler", "effect": "rewrites",
      "why": "Bombardment quality damage stalls near 0.2", "touches": "the 0.1 quality-fade constant",
      "feature": "Bombard Quality", "setting": "BombardQualityEnabled",
      "signature": "(System.Collections.Generic.IEnumerable`1[HarmonyLib.CodeInstruction] instructions) -> System.Collections.Generic.IEnumerable`1[HarmonyLib.CodeInstruction]",
      "priority": 400 }
  ],
  "channels": [
    { "name": "weapon-scoring", "role": "consumes", "version": 2 }
  ],
  "claims": [
    { "target": "DistantWorlds.Types.Galaxy:InflictDamage(System.Int32)", "partner": "Other Mod",
      "self": { "hash": "a1b2c3d4e5f6", "facts": [ "transpiler | rewrites | prio 400 | before [] | after [] | (...) -> ..." ] },
      "other": { "hash": "0f1e2d3c4b5a", "facts": [ "postfix | changes-result | prio 400 | before [] | after [] | (...) -> ..." ] },
      "reviewed": "2026-11-15", "note": "optional" }
  ]
}
```

`target` must be Harmony's spelling so it compares with other mods' and with the observed file. `effect` is one of `observes`, `changes-result`, `changes-args`, `skips-original`, `rewrites`; an unknown value is reported, not guessed. `setting` is a key of the mod's settings schema; with it off the hook is not counted. `touches` and `feature` are for people. `signature`, `priority`, `before` and `after` are the patch's own shape, what a claim compares (the loader records them; leave them out of a hand-written file and its hooks cannot be marked compatible until the mod has run). `claims` are written by the launcher (see Compatibility claims above), never by hand; anything that regenerates the file must keep them. A `modVersion` or `gameVersion` that differs from the installed ones marks the data as probably stale, and hooks declared but not seen last session (or seen but not declared) are listed under the mod's Code hooks.

### XL's mods: generated from a session

`python scripts/hook-manifest.py` (DW2-XL) copies each mod's slice of `hooks-observed.json` into `dll/<project>/dw2modlauncher.hooks.json`, hooks and channels. `why` comes from the `why:` line of the ENGINE block (convention: `dll/README.md`) that names the same game method; `touches`, `feature` and `setting` are written by hand and survive regeneration, and so do `claims`; the patch's `signature`, `priority`, `before` and `after` are copied from the observed file. Play a session with the features you want declared switched on first, run it again after each publish (`modVersion` is the version the session loaded), and use `--check` to see what differs. A patch with no ENGINE block, or whose block has no `why:`, is reported.

**Open gap (XL's mods).** The repo file in `dll/<project>/` is what `build-mods.py` deploys, but the launcher writes claims into the *installed* mod folder (and its automatic conversion does too). `hook-manifest.py` reads `claims` only from the repo copy, so claims marked in the launcher on an XL mod are overwritten by the next deploy and never reach the repo. Until that is fixed (read the installed file's claims, or add the file to the two-way sync in `build-mods.py`/`sync-mod.py`), copy `claims` back into `dll/<project>/dw2modlauncher.hooks.json` by hand after marking ([plans/hook-compatibility-claims.md](plans/hook-compatibility-claims.md)).

## Channels

A channel is a contract owned by its consumer. Using one is cooperation, never a warning by itself, except for a version or type mismatch.

- **Live channels** (`LiveChannels`, reached by reflection; see [DLL Injection.md](DLL%20Injection.md#live-channels-data-that-changes-while-the-game-runs)): `Provide(provider, channel, version, handler)` and `Get(consumer, channel, version, expectedType)`. If the provider's version or delegate type is not the consumer's, `Get` returns `null`, and the loader reports the mismatch once, naming both mods, in its log and as a status-line warning. The registry's providers, consumers and mismatches are in the observed file.
- **File channels** carry a `"version"` inside the data; differing versions between providers already warn (`ModChannels`).
- The declared `channels` section lets the launcher compare a consumer's expected version with the winning provider's before launch.
- Two providers of one live channel is by design (the last wins and is handed the earlier handler).
- A mod's details list its live channels from its declared `channels` plus those the last session saw it use (the loader records the mod each provider and consumer belongs to, as `providerKey` and `consumerKey`), so a mod that ships no manifest still shows them. The session only counts when it ran the installed version of the mod, and a consumer is seen only once it has asked for the channel.

## Limits

- **Effects are inferred from the shape of a patch method**, so `observes` can hide a patch that sets state around the call (a prefix that returns `void` and writes a flag, say). The grading cannot see that. If it proves too coarse, an optional author-stated `effect` in the declared file could override the inferred one (the analyzer would have to prefer it); nothing needs it yet.
Only Harmony patches are seen (not detours, direct IL edits, or reflection reads and writes). The declared file therefore has no list of reflection touches: nothing could verify it, and the ENGINE blocks already record every private member a mod reads or writes. If two mods are ever found writing the same private member, generate such a list from the ENGINE blocks rather than asking authors to keep a second one. Overloads are distinguished by signature, so two mods patching different overloads do not collide. All text a player reads (the report, the details section, the notes, the in-game channel warnings) is in the language packs, with `{0}`-style holes; hook kinds, effects and method names stay as they are, since they are what authors search for.
