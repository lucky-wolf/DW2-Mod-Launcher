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
`MainForm.Launch.BuildLaunchArguments` always emits exactly:

```
--low-level-inject "<path>\Loader\DW2ModLauncher.Loader.dll"!DW2ModLauncher.Loader.Entry.Init
```

`DW2ModLauncher.Loader.Entry.Init()` is the only thing the game ever calls directly. It then
loads every enabled mod **itself**, via reflection, from a manifest the launcher writes right
before launch (`MainForm.Launch.WriteLoaderManifest`, via `DW2ModLauncher.Core.Services.
LoaderManifestBuilder`) into `manifest.json`, next to the loader DLL. This sidesteps the "only one
`--low-level-inject` occurrence takes effect" limitation entirely - only one target is ever
declared - and, because the loader runs in-process before invoking anything, it can pass each mod
richer information than the game's fixed no-argument contract allows.

The loader project (`src/DW2ModLauncher.Loader`) intentionally has **no project reference to
Core or App**: it runs injected into the game process, so it stays minimal (BCL +
`System.Text.Json` only), matching how mods like `NoStarField`/`BetterMusicTransitions` keep
their own dependencies self-contained.

## Manifest schema

A mod declares its injection target declaratively, in either `mod.json` (`launcher.injection`)
or `launcher.json` (`injection`) - both are read, see `ModScanner.ReadModInfo` and
`DW2ModLauncher.Core.Services.LauncherMetaReader.Read`:

```json
{
  "launcher": {
    "injection": {
      "dll": "MyMod.dll",
      "entryPoint": "MyMod.Bootstrap.Init"
    }
  }
}
```

- `dll` is a path **relative to the mod's own content folder** (`ModInfo.ContentRoot`, falling
  back to `ModInfo.Folder`). The launcher resolves it to an absolute path at launch time, so
  this works identically for a local "managed" mod and a Steam Workshop mod - nothing needs to
  be copied into a shared folder for the loader to find it.
- `entryPoint` is the `Namespace.Type.Method` the loader invokes on that DLL.

This is the same manifest schema mods have always declared - nothing here is new for mod
authors. `LoaderManifestBuilder.Build` gathers every enabled mod's target, in load order,
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
    { "key": "Enabled", "type": "bool", "label": "Enabled", "default": true },
    { "key": "Mode", "type": "enum", "options": ["a", "b"], "default": "a" },
    { "key": "Volume", "type": "float", "min": 0, "max": 1, "default": 0.5 }
  ]
}
```

Supported `type` values: `bool`, `enum` (with `options`), `int`, `float` (both with optional
`min`/`max`), and `string`. There is only one settings editor in the launcher
(`MainForm.ModSettings.OpenModSettingsEditor`), not a separate bespoke one for INI files: when a
mod ships `settings.schema.json`, its declared schema and `ModSettingsStore` values feed that
editor directly; when it doesn't but has an `iniPath` in `launcher.json` (or a loose `.ini` in its
folder), `DW2ModLauncher.Core.Services.IniSettingsSchemaBuilder` infers an equivalent schema (and
reads the current values) straight from the INI file - `true`/`false` becomes `bool`, the
well-known `Language` key becomes an `enum` of `["ja", "en"]`, everything else is a `string`, and
a `#`/`;` comment directly above a key becomes that field's description. Saving an INI-derived
form writes the values straight back to the same INI file (via `IniFile.Write`, after a
`.launcher_backup` copy) instead of `ModSettingsStore` - the two sources of truth for *where
values live* (INI file vs. `%AppData%` JSON) are unchanged, only the editor UI rendering them is
now shared.

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
