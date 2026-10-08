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

`dll` is relative to the mod's content folder; `entryPoint` is the `Namespace.Type.Method` the loader
invokes. `LoaderManifestBuilder.Build` gathers every enabled mod's targets, in load order,
de-duplicated by `dll!entryPoint`, into a `DW2ModLauncher.Core.Models.LoaderManifest`.

## `InitWithOptions(string)` / `Init()` convention

For each manifest entry, in order, `DW2ModLauncher.Loader.Entry` resolves the declared type and:

1. Looks for `public static void InitWithOptions(string json)` first. If present, calls it with
   the mod's settings as a raw JSON string (see "Settings schema" below), or `"{}"` if the mod
   has no stored settings.
2. Otherwise falls back to calling the declared method (typically `Init()`) with no arguments -
   this is why existing mods that only implement `Init()` (e.g. `NoStarField`) keep working
   unmodified.

One mod failing to load (missing DLL, exception during `Init`, etc.) is logged to `loader.log`
next to the manifest and does not stop the rest from loading - see `Entry.LoadOne`.

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
