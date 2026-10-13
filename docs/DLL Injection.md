# DLL-injection launch mechanism

DW2 loads third-party code mods via a native command-line flag:

```
--low-level-inject <dllPath>!<Namespace.Type.Method>
```

`DWCommandLineArgs.LowLevelInjections` (see `decomp/DistantWorlds.Core/DistantWorlds.Core/DWCommandLineArgs.cs`)
is typed `IEnumerable<string>`, a `CommandLineParser` "sequence" option: **one** occurrence of
`--low-level-inject` carries several space-separated `dll!entryPoint` targets. A second
*occurrence* of the flag does not merge with the first, it replaces it. The game also invokes
each declared entry point with **zero arguments** - there is no way to hand a mod its settings
at injection time through this flag alone.

## A single fixed loader, not per-mod composition

Rather than composing every enabled mod's own `dll!entryPoint` into that one flag, the launcher
injects a single, fixed target: its own `DW2ModLauncher.Loader.dll` (shipped in a `Loader\`
subfolder next to the launcher's executable, never inside `GameRoot` or a Workshop folder).
`GameLauncher.BuildArguments` always emits exactly:

```
--low-level-inject "<path>\Loader\DW2ModLauncher.Loader.dll"!DW2ModLauncher.Loader.Entry.Init
```

`DW2ModLauncher.Loader.Entry.Init()` is the only thing the game ever calls directly. It then
loads every enabled mod **itself**, via reflection, from a manifest the launcher writes right
before launch (`GameLauncher.WriteLoaderManifest`, via `DW2ModLauncher.Core.Services.
LoaderManifestBuilder`) into `manifest.json`, next to the loader DLL. This sidesteps the "only one
`--low-level-inject` occurrence takes effect" limitation entirely - only one target is ever
declared - and, because the loader runs in-process before invoking anything, it can pass each mod
richer information than the game's fixed no-argument contract allows.

The loader project (`src/DW2ModLauncher.Loader`) intentionally has **no project reference to
Core or App**: it runs injected into the game process, so it stays minimal (BCL +
`System.Text.Json` only), matching how mods like `NoStarField`/`BetterMusicTransitions` keep
their own dependencies self-contained.

## Declaring and discovering injection DLLs

Nothing needs declaring: injection DLLs are **inferred**. `InjectionScanner` looks at every `*.dll`
below the mod's content folder (`ModInfo.ContentRoot`, falling back to `ModInfo.Folder`) and treats a
DLL as an injection DLL if it contains a **public static class named `Entry`** with a public static
`InitWithOptions(string)` or `Init()` method (see the convention below). The DLLs are read through
their metadata only - never loaded - so dependencies (Harmony, etc.) and native DLLs are skipped
automatically. The entry point becomes `<namespace>.Entry.Init`.

- Several DLLs in one mod are loaded in ordinal path order; across mods, in load order. Don't rely
  on ordering beyond that - ship one injection DLL per mod.
- This works identically for a local "managed" mod and a Steam Workshop mod, since the DLL is
  resolved in place.
- **`mod.json` is DW2's own file and is not read for injection.** Don't put a `launcher` block in it; any stale one
  is removed automatically whenever the launcher rewrites a `mod.json` (`ModJsonFile.Save`).

Manual override (rare): a `dw2modlauncher.json` next to the mod's `mod.json` replaces inference for that mod
(`LauncherMetaReader`):

```json
{
  "injection": {
    "dll": "MyMod.dll",
    "entryPoint": "MyMod.Bootstrap.Init"
  }
}
```

`dw2modlauncher.json` also takes `"minLauncherVersion": "1.0.6"`: the oldest launcher the mod works with. A
launcher that is older marks the mod Caution in the list and details pane and lists it in the pre-launch
diagnostics (launching anyway is allowed). Only the numeric `major.minor.patch` is compared, so a `-dev` build counts
as its base version, and an unreadable value is ignored. Launchers from before this key existed ignore it silently, so
a mod that depends on it should also say so in its description.

## Dependencies (`dependencies` in `dw2modlauncher.json`)

A mod says what it needs from other mods in its `dw2modlauncher.json` (never in `mod.json`, which is the game's file). One entry per mod; every field except `mod` is optional (names are matched ignoring case):

```json
{
  "dependencies": [
    {
      "mod": "XL",
      "required": false,
      "minVersion": "1.36.0",
      "loadAfter": true
    }
  ]
}
```

- `mod`: the other mod, matched against its id, display name, folder name or token.
- `required`: true lists a pre-launch warning when the mod is not installed or not enabled. False (default) means the entry applies only when the other mod is enabled.
- `minVersion`: the oldest version of the other mod that works, compared with the `version` in its `mod.json`. A mod that states no usable version cannot meet it, and that is reported (never guessed). A mod that is too old is warned about.
- `loadAfter` / `loadBefore`: order against the other mod, applied only when it is enabled. The launcher enforces them when you press Play: the enabled mods are put in an order that meets every rule (disturbing your order as little as it can), `mods.json` is rewritten to match, and the status line says so. Rules that contradict each other are reported and the order is left alone.
There is deliberately no "incompatible" field: no mod author can know for all time which other mods (or versions) clash with theirs. Code: `ModDependencies`, `ModLibrary.ApplyDependencyOrder`. Design notes: [plans/mod-dependencies.md](plans/mod-dependencies.md).

## Channels (`provides` / `receives` in `dw2modlauncher.json`)

A mod's DLL can be handed data by other mods, without naming them or being named by them. The DLL declares the channels it receives; any mod, at its own choice, provides files to a channel:

```json
{ "receives": ["coherent-research.profiles"] }
```
```json
{ "provides": [ { "channel": "coherent-research.profiles",
                  "files": { "extended race profiles.json": "profiles/extended race profiles.json" } } ] }
```

- A channel is a contract owned by the mod that receives on it: its name, its slot names (the keys of `files`) and each slot's format are that mod's to document. A provider maps a slot to wherever the file sits in its own folder.
- The launcher reads the provided files itself (never a folder path) and passes them to the receiving DLL in a separate argument of its entry point, not in the settings JSON:

```csharp
public static void InitWithOptions(string json, IReadOnlyDictionary<string, string> files)
// files["coherent-research.profiles/extended race profiles.json"] == "<the merged text>"
```

  The loader prefers this two-argument overload when the Entry class has it; the one-argument and `Init()` forms still work (a mod with only those that is handed files is reported before launch).
- **Merging, in load order.** Each enabled provider's file for a slot is merged into the result of the providers before it, so the first provider supplies the full set and a later mod adds to it or overrides parts of it (a new race, say) without replacing it. A slot whose name ends in `.json` is merged as a JSON merge patch (RFC 7396): objects merge key by key, a later value replaces an earlier one (arrays included), and `null` removes a key. Comments and trailing commas are allowed in provided JSON; the merged text has none. Any other slot's file is replaced as a whole by the later provider. If providers give different top-level `version` values for a slot it is reported before launch (the merged data takes the later one), so a format change is never merged silently. The launcher's log names, per slot, the mods that supplied it.
- A path that is absolute, leaves the mod's folder (`..`), is a link, is missing, is larger than 1 MB or is not UTF-8 text is reported before launch and left out; so is a `.json` slot that does not parse. With no provider enabled the DLL simply receives no keys and decides for itself what to do.
- This narrows what the DLL is told; it is not a sandbox, since a code mod runs with the user's rights.

`dll` is relative to the mod's content folder; `entryPoint` is the `Namespace.Type.Method` the loader
invokes. `LoaderManifestBuilder.Build` gathers every enabled mod's targets, in load order,
de-duplicated by `dll!entryPoint`, into a `DW2ModLauncher.Core.Models.LoaderManifest`.

## `InitWithOptions(string)` / `Init()` convention

For each manifest entry, in order, `DW2ModLauncher.Loader.Entry` resolves the declared type and:

1. Looks for `public static void InitWithOptions(string json, IReadOnlyDictionary<string, string> files)` first (the settings plus the data other mods supply on the channels it receives, see "Channels" above). Otherwise looks for `public static void InitWithOptions(string json)`. If present, calls it with
   the mod's settings as a raw JSON string (see "Settings schema" below), or `"{}"` if the mod
   has no stored settings. The JSON also always carries a reserved `"Language"` key, whether or not the mod has a schema (below).
2. Otherwise falls back to calling the declared method (typically `Init()`) with no arguments -
   this is why existing mods that only implement `Init()` (e.g. `NoStarField`) keep working
   unmodified.

One mod failing to load (missing DLL, exception during `Init`, etc.) is logged to `dw2modlauncher.log` (in the game's `data/Logs/`, next to `SessionLog.txt`)
next to the manifest and does not stop the rest from loading - see `Entry.LoadOne`. The same outcome is
recorded in the loader's status registry and shown by the in-game status line (red when a mod failed
to load), which mods can add to; see [Mod Status Line.md](Mod%20Status%20Line.md).

### The `Language` option

At launch the launcher adds `"Language": "<code>"` (`en`, `ja`, `ru`, the code of a launcher language pack) to the options JSON of every code mod. It is the **in-game language**, the same value the in-game widget uses (`WidgetLanguage.Choose`): the language the enabled translation mod declares, or the launcher's own language when Settings > *In-game widget language* is set to that; English when neither applies. It is not the language of the settings dialog, which always follows the launcher.

A mod that wants to speak it reads the key and translates the text it shows in the game (its status-widget lines, for instance); a mod that does not care ignores it. The key is reserved: a schema field of the same name is overridden. The mod's settings values on disk never contain it.

## Settings schema

A mod may optionally ship `settings.schema.json` next to its `mod.json`, describing the shape of
its own settings (read by `DW2ModLauncher.Core.Services.ModSettingsSchemaReader`):

```json
{
  "fields": [
    {"key": "SpeedPenaltyPercent", "type": "int", "label": "Nebula speed penalty (%)", "description": "Speed lost inside a nebula.", "min": 0, "max": 80, "default": 22},
    {"key": "Mode", "type": "enum", "label": "Mode", "options": ["a", "b"], "default": "a"},
    {"key": "LogSamples", "type": "bool", "label": "Log samples", "description": "Developer logging.", "default": false, "hidden": true},
    {"key": "OutputDirectory", "type": "folder", "label": "Output folder", "description": "Blank: the game's current working folder.", "default": "", "hidden": true}
  ]
}
```

Every root key whose value is an array of fields is a **group**, shown in the editor as its own block (the key is the
heading, and each group has its own two-column grid, so groups never share a row). Groups appear in file order. `fields`
is just the conventional name for the first group and is shown without a heading; other root keys such as `$schema` are
ignored, and a field `key` that already appeared in an earlier group is skipped:

```json
{
  "fields": [ {"key": "Enabled", "type": "bool", "default": true} ],
  "Speed penalties": [ {"key": "NebulaPercent", "type": "int", "min": 0, "max": 80, "default": 22} ],
  "Developer": [ {"key": "LogSamples", "type": "bool", "default": false, "hidden": true} ]
}
```

Per field:

| Attribute | Required | Meaning |
| --- | --- | --- |
| `key` | yes | The name the value is stored and handed to the mod under |
| `type` | yes | `bool`, `enum` (needs `options`), `int`, `float`, `string`, `folder` (folder picker), or `file` (file picker; `filename` also works). Folder and file values must exist, or be blank |
| `default` | recommended | Used until the user changes it; written into the values the mod receives |
| `label` | no | Text shown beside the control (falls back to `key`) |
| `description` | no | Help text under the label |
| `min` / `max` | no | Bounds for `int` and `float` |
| `options` | for `enum` | The allowed values |
| `hidden` | no | `true` hides the field for Steam Workshop copies of the mod, so developer controls (logging, output folders) show only for the author's local copy, in place within their group. Hidden fields still get their defaults. `localOnly` is the older name and still works |

A mod's declared schema and `ModSettingsStore` values feed the launcher's settings editor (`ModSettingsEditorViewModel`).

### Translating the settings dialog

The schema is written in whatever language its author uses; that text is the base. Another language is a **sidecar file** next to
`settings.schema.json`, named `settings.schema.<language code>.json` (`settings.schema.ja.json`, the code of a launcher language
pack). The settings dialog is the launcher's own window, so it uses the launcher's language. The sidecar only supplies display text,
looked up by the schema's own keys; anything it leaves out is shown as the schema has it, so a translator can do just the labels.
Limits, defaults and types never go in it.

```json
{
  "groups": { "Speed penalties": "速度ペナルティ" },
  "fields": {
    "Enabled": "有効",
    "NebulaPercent": { "label": "星雲での速度低下 (%)", "description": "星雲の中で失う速度です。" },
    "Mode": { "label": "モード", "options": { "a": "標準", "b": "高速" } }
  }
}
```

- `groups` maps a group's name (its root key) to the heading to show.
- `fields` maps a field `key` to its translation: an object with any of `label`, `description` and `options` (the schema's option text to the
  text shown; the stored value stays the schema's option), or just a string, which translates only the label.
- Keys are matched ignoring case. A sidecar for the base language is used like any other, so a mod written in Japanese can ship
  `settings.schema.en.json`.
- A sidecar that exists but is not valid is reported when the dialog is opened, rather than silently ignored.

The actual **values** for these fields are stored under the current user's AppData folder
(`%AppData%\DW2ModLauncher\ModSettings\<mod token>.json`, via `DW2ModLauncher.Core.Services.
ModSettingsStore`/`UserDataRoot`) - never inside the mod's own folder. This is deliberate: Steam
Workshop can re-sync a mod's folder at any time, and writing user settings there would risk
losing them. On first read, `ModSettingsStore.GetOrCreateValues` synthesizes and persists a
default value for every schema field so both the editor and the manifest always have real values
to work with.

`LoaderManifestBuilder` inlines each mod's resolved settings JSON directly into its manifest
entry when a schema is present, so nothing needs to be written into Workshop content at launch
time either.
