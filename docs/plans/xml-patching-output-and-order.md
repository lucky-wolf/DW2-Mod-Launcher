# XML patching: patched-file dump and patch vs. data-file order

Status: not started. Follow-ups to the first XML patching release ([../xml-patching.md](../xml-patching.md)); nothing here changes the patch file format.

## 1. Write the patched data to disk

Patching happens in memory: `XmlPatchHooks.OpenStreamPostfix` loads the game's data file into an `XDocument`, `PatchRunner.Apply` patches it, and the game receives a `MemoryStream`. With three to ten mods patching the same entities there is no file to look at to see what the game really loaded.

What exists today: `patches.log` records every applied change as `old -> new` with the patch file and line, plus per-file tallies. That is the provenance (who changed what, in which order) but not the merged result.

Proposal, in order of cost:

1. **Patched-file dump (small).** After a successful `Apply`, save the patched document under a folder next to `patches.log`, keyed by the data file's virtual path (for example `patched/data/Races.xml`). The hook already holds the final `XDocument`, so this is a few lines. Only files that were actually patched are written; clear the folder at the start of each load pass so it never shows stale output. Consider a launcher setting to turn it off, since some data files are large.
2. **Offline preview (larger).** `DW2ModLauncher.XmlPatching` has no game dependency except the schema, which `SchemaReflector` builds by reflecting the game's `DistantWorlds.Types`. Run the same `PatchRunner` in the launcher over the game's data and the enabled mods' `patches/` to show the merged result without starting the game (the existing `ValidateAll` is already described as "Check patches"). Open question: load the game's types assembly in the launcher, or dump the schemas once from inside the game and ship them as JSON.

Both views are wanted: the log answers "who set this value", the dump answers "what did the game load".

## 2. Order of patches vs. full data files

Patch files apply in launcher load order (mods in order, files within a mod by path), and a later patch sees earlier results, so last wins among patches. But `PatchRunner.Apply` runs once per data file as the game opens it, and applies to every definition of an entity ("If several data files define the same entity, the patch applies to all of them").

So patches are not interleaved with data files: if mod B patches `Race 0` and a later mod C ships a full `Race 0` in a root `*.xml`, B's patch is still applied to C's version, where strict load order would have C's definition replace B's patch. For additive tweaks this is probably what authors want; for DW2-XL, which redefines whole entities, it decides which mod wins. Needs a deliberate, documented rule. Options:

- Keep it (patches always apply on top of whatever the data files define) and say so in `xml-patching.md`.
- Anchor each patch to its mod's position: apply it only to definitions from data files of mods earlier than it in load order. Needs the hook to know which mod a data file came from (the virtual path under `/mods/` or `/steam/` may be enough).
- A per-patch-file opt-in such as a `phase` attribute (early or late).

Discussion opened on the PR; decide there first.

## 3. Resolve everything before launch: one pre-merged mod

An alternative to patching in the running game: before launch, the launcher builds a single generated mod holding the already-resolved and already-patched data (merged in load order), and starts the game with only that mod in its list. The game never sees the individual mods' data files or the patches.

**What it would fix by construction**
- Visibility: the merged result is a folder you can diff, grep and attach to a bug report, not an in-memory stream plus a change log.
- Ordering: the generator resolves each mod in load order, so a later full-entity definition replaces an earlier patch exactly where strict load order says it should (section 2 disappears as a question; the generator defines the rule).
- No runtime hooks for data: no Harmony hook on `OpenStream`, no dependence on the data-group names (`ColonyEventDefinitions`, `SystemNames`) that mark a load pass, no patching cost per launch.
- The same `PatchRunner` is used unchanged; only the driver differs (files in, files out, instead of streams in, streams out).

**What the game is already known to do (stated by the mod author, not verified in code)**
- Saves do not record which mods were enabled. A save holds the full combined data, written to disk, without the bundle data (textures, sounds, music and similar).
- Bundle data is looked up from the source mods on disk at load time, so it is dynamic and never duplicated.

This removes two worries that would otherwise count against the idea: old saves cannot be broken by a synthetic mod list, and the generated mod does not need to copy or link gigabytes of assets. It would contain merged XML only; what is left is how the game finds each bundle's source mod when that mod is not in its list.

**Open questions (need the game's code, not the launcher's)**
1. How does the game merge data files from several mods today: which file wins when two mods ship the same name, how list-type files combine, how `/data/`, `/mods/` and `/steam/` rank? The generator must reproduce this exactly or the "pre-resolved" data differs from what the game would have built. The launcher's `ConflictRules` models file-level conflicts and may already encode part of it.
2. What does the save writer serialize, and which code path does it? If it writes the full combined data using the game's own serialization, a hook on that path could dump the post-patch state as the visibility artifact (a better version of section 1.1: exactly what a save would contain, written by the game's own writer, with no merge to reimplement).
3. How does loading a save consume that data? If it bypasses the mods' data files, the same loading path may accept a generated dataset, which is this section's idea using a mechanism the game already has.
4. How are bundles located when the owning mod is not in the game's list (see above)? A generated mod must supply whatever the game needs for that, with no copies.
5. Schemas offline: `SchemaReflector` reflects the game's `DistantWorlds.Types`. The launcher either loads that assembly (net8.0 game DLL into a net10.0 launcher; not yet tried) or ships schemas dumped once from inside the game as JSON. Shared with section 1.2.

**Costs that remain**
- Staleness: the generated mod must be rebuilt whenever the mod set, order, settings or any mod's files change, and a ten-mod stack takes time to build.
- Launch plumbing: the launcher has to start the game with a mod list that contains only the generated mod, while still injecting DLL mods through the loader as it does now (DLL mods are not part of the game's data mod list).
- Mod DLLs that read their own mod folder's data files at runtime must still find them; check each shipped DLL for such assumptions.
- Linux/Proton: the generated folder and any path in the manifest must be visible to the game process (`Z:\...`, as `GamePaths.ToGameVisiblePath` already does for patch files).

**Suggested order of work**
1. Section 1.1 (patched-file dump) as the quick visibility win; it also shows what the runtime merge produces today.
2. Answer questions 1 to 4 by reading the game's data-loading and save code (decompiled `DistantWorlds.Types` and the data-loading classes). Question 2 might replace 1.1 with something better.
3. Only then decide between runtime hooks with a mod-position rule (section 2, second option) and the pre-merged mod. They share the engine, so work on one is not wasted if the other wins.
