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

`"hooksCoordinatedWith": ["Other Mod"]` in `dw2modlauncher.json` (names matched like a dependency's `mod`) says its author has already dealt with that mod; it is for mods by different authors. `"Other Mod@1.2.3"` covers only that version of it (compared with its mod.json `version`): once the other mod updates, the overlap is graded again, so the author re-checks it. Prefer the pinned form for mods you do not control. Older launchers do not know the `@` form and simply ignore the entry.

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
      "feature": "Bombard Quality", "setting": "BombardQualityEnabled" }
  ],
  "channels": [
    { "name": "weapon-scoring", "role": "consumes", "version": 2 }
  ]
}
```

`target` must be Harmony's spelling so it compares with other mods' and with the observed file. `effect` is one of `observes`, `changes-result`, `changes-args`, `skips-original`, `rewrites`; an unknown value is reported, not guessed. `setting` is a key of the mod's settings schema; with it off the hook is not counted. `touches` and `feature` are for people. A `modVersion` or `gameVersion` that differs from the installed ones marks the data as probably stale, and hooks declared but not seen last session (or seen but not declared) are listed under the mod's Code hooks.

### XL's mods: generated from a session

`python scripts/hook-manifest.py` (DW2-XL) copies each mod's slice of `hooks-observed.json` into `dll/<project>/dw2modlauncher.hooks.json`, hooks and channels. `why` comes from the `why:` line of the ENGINE block (convention: `dll/README.md`) that names the same game method; `touches`, `feature` and `setting` are written by hand and survive regeneration. Play a session with the features you want declared switched on first, run it again after each publish (`modVersion` is the version the session loaded), and use `--check` to see what differs. A patch with no ENGINE block, or whose block has no `why:`, is reported.

## Channels

A channel is a contract owned by its consumer. Using one is cooperation, never a warning by itself, except for a version or type mismatch.

- **Live channels** (`LiveChannels`, reached by reflection; see [DLL Injection.md](DLL%20Injection.md#live-channels-data-that-changes-while-the-game-runs)): `Provide(provider, channel, version, handler)` and `Get(consumer, channel, version, expectedType)`. If the provider's version or delegate type is not the consumer's, `Get` returns `null`, and the loader reports the mismatch once, naming both mods, in its log and as a status-line warning. The registry's providers, consumers and mismatches are in the observed file.
- **File channels** carry a `"version"` inside the data; differing versions between providers already warn (`ModChannels`).
- The declared `channels` section lets the launcher compare a consumer's expected version with the winning provider's before launch.
- Two providers of one live channel is by design (the last wins and is handed the earlier handler).

## Limits

- **Effects are inferred from the shape of a patch method**, so `observes` can hide a patch that sets state around the call (a prefix that returns `void` and writes a flag, say). The grading cannot see that. If it proves too coarse, an optional author-stated `effect` in the declared file could override the inferred one (the analyzer would have to prefer it); nothing needs it yet.
Only Harmony patches are seen (not detours, direct IL edits, or reflection reads and writes). The declared file therefore has no list of reflection touches: nothing could verify it, and the ENGINE blocks already record every private member a mod reads or writes. If two mods are ever found writing the same private member, generate such a list from the ENGINE blocks rather than asking authors to keep a second one. Overloads are distinguished by signature, so two mods patching different overloads do not collide. All text a player reads (the report, the details section, the notes, the in-game channel warnings) is in the language packs, with `{0}`-style holes; hook kinds, effects and method names stay as they are, since they are what authors search for.
