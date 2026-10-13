# Mod dependencies and data channels in dw2modlauncher.json

Status: built (launcher side) on 2026-10-10. User-facing description: [../DLL Injection.md](../DLL%20Injection.md). The consumer is DW2-XL's Coherent Research, which receives its profile tables on the `coherent-research.profiles` channel; XL provides them.

## Two features

1. **Dependencies** (`dependencies`): a mod says which other mods it needs (`required`), the oldest version of them that works (`minVersion`) and its load order against them (`loadAfter` / `loadBefore`), which the launcher enforces. General, built and tested, currently used by no mod.
2. **Channels** (`provides` / `receives`): data handed from mods that have it to a DLL that can use it, with neither side naming the other. This is what Coherent Research uses; see below.

## Dependencies

A `dependencies` section in `dw2modlauncher.json`, one entry per mod depended on. Every field except `mod` is optional:

```json
"dependencies": [ { "mod": "XL", "required": false, "minVersion": "1.36.0", "loadAfter": true } ]
```

| Field | Meaning |
|---|---|
| `mod` | The other mod, matched by id, display name, folder or token, case-insensitively. |
| `required` | `true`: the mod does not work without it. `false` (default): the entry only applies when the other mod is present and enabled. |
| `minVersion` | The oldest version of the other mod that works, compared with its `mod.json` `version`. |
| `loadAfter` / `loadBefore` | Order against the other mod, applied only when it is present. |

`dw2modlauncher.json` is the only home for these fields; the launcher reads nothing about dependencies from `mod.json`, which is the game's file and the launcher adds nothing to it. The warnings the removed code gave (missing required mod, load order) come from this section.

Decisions made: load order is **enforced** (on Play the launcher sorts the enabled mods to meet every rule, keeping the user's order wherever the rules allow, writes `mods.json`, and says so in the status line; contradicting rules are reported and leave the order alone). A required mod that is missing or too old **warns loudly** (the pre-launch diagnostics ask whether to launch anyway). A dependency with no usable `version` cannot meet a `minVersion` and is reported as such, never guessed.

## Channels

Why channels and not "dependency + needs": a mod like Coherent Research is not dependent on any one mod; it is a consumer that works with whatever data is offered, and the mods that have the data (XL, a total overhaul) are the ones that opt in. So the receiving DLL declares the channels it receives (`receives`), and any mod declares what it supplies (`provides`: channel, and slot name to file path). Neither names the other, so a renamed mod, a beta folder or a fork needs no special case.

- Slots, formats and their `version` are the receiver's contract (for `coherent-research.profiles`: [DW2-XL docs/Profile Data Channel.md](../../../DW2-XL/docs/Profile%20Data%20Channel.md)). The `version` inside a file, not the other mod's package version, is how a reader handles old versus new data.
- The launcher reads the files (text only, 1 MB each) and merges every enabled provider's file for a slot **in load order**: a `.json` slot as an RFC 7396 merge patch (first provider the full set, later ones augment or override, `null` removes), any other slot replaced by the later provider. The receiver gets the combined text in the second argument of `InitWithOptions(string json, IReadOnlyDictionary<string, string> files)`, keyed `channel/slot`. Settings JSON is untouched.
- Disagreeing top-level `version` values between providers are reported before launch. Bad paths (absolute, `..`, links, missing, oversize, not UTF-8) and unparseable `.json` are reported and left out. No provider means no keys.

## Not included

- **Folder paths.** A mod is never told where another mod lives. This is a narrowed contract, not a sandbox: a code mod is a DLL with the user's full rights and can read any file anyway.
- **The other mod's version in the handoff.** What a reader needs to know is the file's format, so the format carries its own `version`.
- **Binary files, and the patched or cooked XML.** Text files only.
- **Incompatibility.** The removed code warned when an "incompatible" mod was enabled; that does not come back. No author can know for all time which other mods (or versions) clash with theirs; that belongs in an external database people maintain, if the launcher ever needs one.

## Work (done)

- `LauncherMeta`/`ModScanner`: `dependencies`, `provides`, `receives`.
- `ModDependencies` (problems, enforced order), `ModLibrary.ApplyDependencyOrder`, `ModChannels` (read, merge, problems, provenance for the log), `LoaderManifestBuilder`/`Loader` (second argument), `InjectionScanner.AcceptsFiles`, `LaunchDiagnostics`, tests beside `ModOrderAndConflictTests`.

## Hand-off

Where this stands and what is left, for whoever picks it up next (the DW2-XL side is in `DW2-XL/docs/plans/weapon-scoring.md` and `DW2-XL/docs/Profile Data Channel.md`).

- **Shipped in 1.0.9** (the current branch): `dependencies`, `provides` / `receives`, the second `InitWithOptions` argument, the "Works with other mods" lines in the details pane (`ModDetails.BuildLinks`, flat `label: value`, nothing shown for a mod that declares none). Mods that use it set `"minLauncherVersion": "1.0.9"` in their `dw2modlauncher.json` (Coherent Research and XL do).
- **mod.json is the game's file.** The launcher reads only keys the game itself reads (matched ignoring case) and writes nothing of its own there; everything launcher-specific lives in `dw2modlauncher.json`. The old `Required` / `Optional` / `Incompatible` / `LoadBefore` / `LoadAfter` readers and the alias key names were removed for that reason.
- **Strings.** The new diagnostics, the `ModDependencies` / `ModChannels` problems and the `BuildLinks` labels are English literals, like the older "files could not be scanned" messages. They need language keys in `Languages/en.json`, `ja.json` and `ru.json`.
- **`LauncherMetaReader.Read` returns null for a `dw2modlauncher.json` it cannot parse**, so a malformed file silently drops its dependencies, provisions and `receives`. The pre-launch check does report invalid JSON in that file (`ValidateJsonFile`); the reader itself should surface the error instead of returning null.
- **Merge limits worth knowing.** Arrays are replaced whole; objects merge field by field, so a later mod cannot replace an entry wholesale (it can change fields or `null` them). If that ever matters, a per-slot "replace" marker is the extension.
- **Idea for another day: a code-to-code interface.** Channels are for data from mods that may have no code. Code mods that want to offer behaviour or live values to each other could use a small shared registry in the loader modelled on `ModStatus` (publish a named object, look it up), formally defined and exposed to the launcher (details pane, diagnostics) the way channels are. Nothing is designed yet.
