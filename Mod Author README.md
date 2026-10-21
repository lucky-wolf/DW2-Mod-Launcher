# Mod author README

For anyone making a mod for Distant Worlds 2 with the DW2 Mod Launcher, and for the maintainer's future self. It says what the launcher gives a mod and where each piece is documented in full.

A data mod (XML, art) needs none of the code sections below: the launcher manages it, publishes it and tracks its changes the same way.

## Contents

- [Set your author name](#set-your-author-name)
- [The files of a mod](#the-files-of-a-mod)
- [A code mod's entry point](#a-code-mods-entry-point)
- [Settings the player can edit](#settings-the-player-can-edit)
- [The in-game status widget](#the-in-game-status-widget)
- [The in-game Mods menu](#the-in-game-mods-menu)
- [Logging](#logging)
- [Threads](#threads)
- [Be a good neighbour](#be-a-good-neighbour)
- [Channels: providing and receiving data](#channels-providing-and-receiving-data)
- [Dependencies and load order](#dependencies-and-load-order)
- [Code hooks: patching the same game code as another mod](#code-hooks-patching-the-same-game-code-as-another-mod)
- [Language](#language)
- [Building a DLL that change detection can trust](#building-a-dll-that-change-detection-can-trust)
- [Publishing](#publishing)

## Set your author name

Open Settings and fill in **Author name**. Use your handle, the name you want players to see. Every mod you create or publish through the launcher is stamped with it as `"author"` in the mod's `dw2modlauncher.json`, and the mod list shows it in the Author column. Blank means no stamping. Do this before you create your first mod.

## The files of a mod

| File | Whose | Purpose |
| --- | --- | --- |
| `mod.json` | The game's | `displayName`, `description`, `version` and the rest the game reads. **Never add your own fields to it**; the launcher removes a stale `launcher` block whenever it rewrites the file. |
| `dw2modlauncher.json` | The launcher's | Everything launcher-specific: `author`, `dependencies`, `provides`, `receives`, `hooksCoordinatedWith`, `minLauncherVersion`, `language`, and an optional `injection` override. |
| `settings.schema.json` | The launcher's | Describes the mod's settings so the launcher can draw an editor. Optional `settings.schema.<code>.json` sidecars translate it. |
| `description.bbcode` | The launcher's | The Workshop description. `{version}`, `{date}` and `{changes}` are replaced when you publish: the version, the publish date, and the latest `changelog.txt` entries as a bullet list (your `Unreleased` lines; with none, the newest release's). `{changes}` with no entries is an error. |
| `changelog.txt` | The launcher's | Lines under `Unreleased` go to Steam as the change note when you publish. |
| `*.dll` with a public static `Entry` class | You | A code mod. Found automatically, never loaded just to look. |

Reference: [DLL Injection.md](<docs/DLL Injection.md>).

## A code mod's entry point

Give the DLL a public static class named `Entry` with one of these (the first one that exists is used, in this order):

```csharp
public static void InitWithOptions(string json, IReadOnlyDictionary<string, string> files)  // settings + data from channels
public static void InitWithOptions(string json)                                             // settings
public static void Init()                                                                   // nothing
```

The loader calls it once, before the game's interface exists. One mod failing to load never stops the others; the failure is logged and shown in red on the status widget. Build for net8.0 (the game's runtime), compile against the game's own `0Harmony.dll` and never copy it. Ship one injection DLL per mod. Full rules: [DLL Injection.md](<docs/DLL Injection.md#initwithoptionsstring--init-convention>).

Everything below that touches the launcher is a soft dependency: it works when the launcher is there and does nothing when your DLL is injected directly, so the mod never needs the launcher at compile time.

## Settings the player can edit

Ship a `settings.schema.json` next to `mod.json`. Each field has a `key`, a `type` (`bool`, `enum`, `int`, `float`, `string`, `folder`, `file`), a `default`, and optionally `label`, `description`, `min`, `max`, `options` and `hidden`. The launcher draws the editor, stores the values under the user's AppData (never in the mod folder, where Steam could overwrite them) and hands them to `InitWithOptions` as JSON.

- A field with `"hidden": true` shows only for your local copy, not for the Workshop copy players have. Use it for developer switches such as logging.
- Make your code's defaults equal the schema's defaults. The launcher does not write a value that sits at its default.
- Each array in the schema is a group, headed by its key. To say what a group is about (a feature behind an "Enable for Player" switch, say), add a root `"_groups": { "GroupName": "text" }` object (underscore, so it sorts first and never clashes with a group of your own named "groups"); the text shows under the heading. A sidecar can translate it with `"groups": { "GroupName": { "heading": "…", "description": "…" } }`.
- Translations are sidecars (`settings.schema.ja.json`, `settings.schema.ru.json`) that translate field labels and descriptions, option text, group headings and group descriptions. No code is needed. All the launcher's translation paths are in [Localization.md](docs/Localization.md).

Reference: [DLL Injection.md](<docs/DLL Injection.md#settings-schema>).

## The in-game status widget

When the game is started through the launcher it draws one line of text in the lower left, and a dialog with one row per code mod when clicked. The loader reports by itself that your DLL loaded (or why it did not). You can add:

- features that installed (`Installed`)
- errors (`Error`), which turn your mod red
- named lines in your panel (`Detail(key, text, level)`; the same key again replaces the line, empty text removes it)
- an overall level and one-line summary (`SetLevel`)
- the path of your full log (`SetLog`)

Your mod reaches the widget by reflection. Copy the `LauncherStatus` class from that page into your project, change the namespace, and call it. The complete file and the rules (never throw, and don't draw a second line of your own when `LauncherStatus.Present`) are in [Mod Status Line.md](<docs/Mod Status Line.md#reporting-more-from-a-mod>).

## The in-game Mods menu

The Game Menu (Esc) gets a **Mods** button that opens a dialog with one button per mod that added an entry. Copy the `LauncherMenu` class from [Mod Menu.md](<docs/Mod Menu.md#adding-an-entry-from-a-mod>) (the same pattern as `LauncherStatus`) and call `Add(label, onClick)`. Your handler should open your own dialog or toggle something and return quickly; call `Show()` when your dialog closes to bring the player back to the Mods dialog. `SetEnabled(false)` greys the entry out (for example when no game is loaded).

Reference: [Mod Menu.md](<docs/Mod Menu.md>).

## Logging

The launcher has no logging API for mods; it keeps its own log (`dw2modlauncher.log`, in the game's `data/Logs`) for the loader. A mod writes its own. These are the policies to follow.

### Where it goes

- **Write to the game's `data/Logs` folder** (under the game's install folder, next to `SessionLog.txt`) by default. Let the player or developer override the folder with a setting, but a blank setting means this folder.
- **Name the file after your mod: `<your mod's name>.log`.** The name says whose data it is and that nobody else should write to it. Never write to, rename, truncate or delete another mod's file, the game's own logs, or the launcher's.
- **Rewrite it each run.** A player's folder must not fill up with your history.

### What goes in it

There are two kinds of line, and the difference is the heart of the policy.

- **General log (always on).** What a player could send you to help debug: your version, the features that installed, which options were chosen, warnings and errors. A distributed mod writes almost nothing beyond this.
- **Detail log (developers only).** Per-event and per-decision lines, statistics, snapshots, CSVs, anything that grows with play time. Put it behind a hidden setting (`"hidden": true` in the schema, so it shows only for your local copy, default off) and send it to a folder per run (`yyyy-MM-dd_HH-mm-ss`), keeping only the newest few. Nothing writes detail unless the developer switched it on.

Give every line a level chosen by the call, never guessed from its words: `Info`, `Warn`, `Error`, `Detail`, written as `HH:mm:ss LEVEL message`. A helper with those four methods is all it takes.

More policies:

- **Install each feature in its own try/catch** (and its own Harmony instance, if you use Harmony), so one failed patch is logged as an `Error` and the rest still load.
- **Never swallow a problem silently.** Report it, even when you carry on with a sensible default.
- **Oddities in other people's data are `Detail`, not `Warn`.** The game's own data is untidy, and other mods' will be too. A player reads a warning as "this mod is broken".
- **No stop-after-N caps.** A log that stops after N lines loses exactly the lines you need late in a session. Deduplicate, sample (the first few, then every Nth), or give each interval a line budget instead.
- **Your logger must be thread-safe** (next section).

### What the player sees

The status widget is the player's window, the log is yours. **Send the widget only what is appropriate for a player to see**, and put everything else in your log.

- `Installed(feature)`: a feature that is on.
- `Error(text)`: something is broken or a feature is off because it could not work. It turns your mod red, so reserve it for what the player should know about. Only the first line is kept; the full text belongs in your log.
- `Warn`-level panel lines: something not as intended that the mod survives (a mod or feature it relies on is absent, a version is wrong). Cap them at about ten per run.
- Statistics, per-event chatter, internal bookkeeping and data quirks never go to the widget. Diagnostics are `Detail`, never `Error`.
- Don't make a level louder to get attention. A mod that cries wolf teaches players to ignore the widget for every mod.

## Threads

The game is multi-threaded: it processes each empire on its own task, and its own shared state is plain collections with last-write-wins. Your hooks run inside those tasks, so **assume other threads are running your code at the same moment**, your logger included. The rules that have held up:

- **A collection that more than one task can reach is locked or concurrent.** Use `lock (gate)` around every access (reads and iteration included), or a `ConcurrentDictionary`. A plain `Dictionary` or `HashSet` written from two tasks can corrupt itself and hang the game, not only report wrong numbers.
- **Single fields are fine to read; compound updates are not.** An aligned reference or 32-bit write does not tear on x64, but `count++`, check-then-set, and two fields that must agree need a lock. A counter that only numbers log lines may stay unlocked.
- **Build a shared value whole, then publish it.** A table rebuilt in a local and then assigned (`volatile` where hot paths read it) is safe to read without a lock; one edited in place after it is shared is not.
- **A hand-off from a Harmony prefix to its postfix or finalizer is `[ThreadStatic]`,** never a plain static, or two empires will take each other's value.
- **Your log writer takes a lock** around every write, and a write that fails is reported, not ignored.
- Write down, for each static you add, how it is guarded. And re-check after a game patch: a patched method that starts using `await`, or a new `Parallel.For` around a hooked path, changes what is safe.

## Be a good neighbour

Every enabled mod runs in the same process, on the same game data, and writes to the same folders. Players load many mods together, and they will blame whichever one they notice. Some habits that keep the shared space healthy:

- **Touch only what is yours.** Your files, your settings, your log. Never edit, delete or "clean up" another mod's files, the game's files, or the launcher's.
- **Change the minimum.** Patch the narrowest method that does the job, name the parameter types when a method has overloads, and prefer a postfix that adjusts a result over a prefix that replaces the whole method. A mod that replaces a method outright silently disables every other mod that patched it.
- **Don't assume you are alone.** Another mod may have hooked the same method or edited the same data. Check the state you rely on instead of assuming it, and add to shared data rather than replacing it.
- **Give players a switch.** Anything that changes how the game plays should be an option the player can turn off, and an empire-level feature should let the player and the AI be set independently.
- **Fail small and say so.** If a feature cannot install (the game changed under you), turn off that feature, report it once as an `Error`, and leave the game alone. Never crash the game or half-apply a change.
- **Be quiet.** Don't spam logs, the widget, or the screen. Work you do every frame or per event should be cheap.
- **Declare your needs.** Use `dependencies` and `loadAfter`/`loadBefore` instead of hoping the load order works out, and use channels rather than reaching into another mod.
- **Be kind about clashes.** If your mod and another clash, talk to that author. Don't write code to disable theirs.
- **Say what you tested.** Name which game versions you tested against in your description, and say what you changed in your changelog so players can decide.

## Channels: providing and receiving data

A channel lets one mod hand files to another's DLL without either naming the other. Typical use: a base mod owns a data format (a table of profiles, say), and other mods add to it.

**To receive**, declare the channel in your `dw2modlauncher.json` and take the two-argument `InitWithOptions`:

```json
{ "receives": ["my-mod.profiles"] }
```

```csharp
public static void InitWithOptions(string json, IReadOnlyDictionary<string, string> files)
{
    // files["my-mod.profiles/profiles.json"] is the merged text from every enabled provider
}
```

The channel name, its slot names (the file keys) and each slot's format are a contract that you own and document.

**To provide**, map a slot to a file in your own folder:

```json
{ "provides": [ { "channel": "my-mod.profiles",
                  "files": { "profiles.json": "data/my-profiles.json" } } ] }
```

The launcher reads the files itself and merges them in load order. A slot ending in `.json` is merged as a JSON merge patch (RFC 7396): objects merge key by key, a later value replaces an earlier one (arrays included), and `null` removes a key. Any other slot is replaced whole by the later provider. Providers that disagree on a top-level `version` are reported before launch.

Limits: paths must stay inside the mod's folder, files are UTF-8 text up to 1 MB, and a `.json` slot must parse. A bad file is reported before launch and left out. With no provider enabled your DLL receives no keys and decides for itself. A provider needs no code at all, only the JSON and the file. Reference: [DLL Injection.md](<docs/DLL Injection.md#channels-provides--receives-in-dw2modlauncherjson>).

## Dependencies and load order

Say what you need in `dw2modlauncher.json`:

```json
{ "dependencies": [ { "mod": "SomeOtherMod", "required": false, "minVersion": "1.0.0", "loadAfter": true } ] }
```

`required` lists a warning before launch when the other mod is missing. `minVersion` is compared with the other mod's `version` in its `mod.json`. `loadAfter` and `loadBefore` are enforced when the player presses Play. There is deliberately no "incompatible" field. Set `"minLauncherVersion"` if your mod needs a recent launcher. Reference: [DLL Injection.md](<docs/DLL Injection.md#dependencies-dependencies-in-dw2modlauncherjson>).

## Code hooks: patching the same game code as another mod

You do not have to do anything: the launcher's loader records every Harmony patch each session, and warns players when two mods change the same game method in ways that can matter. To help it, set `author` (mods by one author never warn each other), and if you have already talked to another author about a shared method, list their mod in `dw2modlauncher.json` as `"hooksCoordinatedWith": ["Their Mod"]`. Live channels between code mods carry a version that both sides state; a mismatch is reported in game instead of failing quietly. You may also ship a `dw2modlauncher.hooks.json` declaring your hooks so the warning appears before the game starts. Everything, including the file format, is in [Code Hooks.md](<docs/Code Hooks.md>).

## Language

The launcher adds `"Language": "<code>"` to your options JSON: the in-game language (what the enabled translation mod declares, or the launcher's language). Read it if you want to translate what you show in the game. Status widget text is the mod's to translate; One approach is a flat `status.<code>.json` next to the DLL, from the English text to the translated text, with English as the fallback. A translation mod declares `{ "language": "ru" }` in its `dw2modlauncher.json`. References: [DLL Injection.md](<docs/DLL Injection.md#the-language-option>), [Localization.md](docs/Localization.md).

## Building a DLL that change detection can trust

The launcher shows whether a local mod has changed since its last Workshop publish. This section explains how it decides, and how to build a code mod so that it only reports a change when the mod really changed.

### How the launcher decides

At publish time the launcher records every file in the mod's content folder (size, last-write time, SHA-256) in `<data root>/PublishedState/<workshop id>.json`. The record is never kept in the mod folder, so it cannot change what it measures. It also keeps the description exactly as last sent to Steam (placeholders filled in): the Publish and Edit Properties dialogs compare Steam's copy with it, show a red dot when someone edited the description on the Steam page, and ask before replacing a differing copy (publish, push and pull alike).

Later it compares the folder with that record:

- A file whose size and last-write time match the record is unchanged, without hashing.
- Any other file is hashed and compared. A touch, or a rebuild with identical output, is not a change.
- Text files (no NUL byte in the first 8 KiB) are hashed with CRLF read as LF, so line-ending differences are not a change.
- Binary files, DLLs included, are hashed byte for byte.

So for a DLL the question is: does the same code compile to the same bytes? By default it does not.

### Why a DLL changes when its code didn't

The default .NET build puts more into the DLL than your code:

- **Source revision in the version.** The SDK appends the git commit to the informational version (`1.0.0+<sha>`), so every commit changes the DLL.
- **SourceLink.** The PDB carries a map holding the current commit and the repo path, and the DLL stores the PDB's checksum.
- **Repo path.** Embedded paths use the case and location the build ran from (`c:\...` and `C:\...` give different bytes).
- **PDB checksum of the sources.** The PDB hashes every source file, and the DLL's debug directory holds the PDB's id and checksum. Editing even a comment changes the DLL.

The result is a mod that shows as modified after a commit, after a rebuild from another folder, or after a comment edit.

### The settings to use

Put these in a props file imported by every code mod's `.csproj`:

```xml
<Project>
  <PropertyGroup>
    <!-- no git commit in the assembly's informational version -->
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
    <!-- no SourceLink map (it holds the commit and the repo path) -->
    <EnableSourceLink>false</EnableSourceLink>
    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
    <!-- no pdb: its id and checksum cover every source file, comments included -->
    <DebugType>none</DebugType>
  </PropertyGroup>
  <!-- keep the repo folder out of any embedded path, in either drive-letter case -->
  <PropertyGroup>
    <RepoRootForPathMap>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', '..', '..'))</RepoRootForPathMap>
    <PathMap>$(RepoRootForPathMap.Substring(0,1).ToUpperInvariant())$(RepoRootForPathMap.Substring(1))=/_/,$(RepoRootForPathMap.Substring(0,1).ToLowerInvariant())$(RepoRootForPathMap.Substring(1))=/_/</PathMap>
  </PropertyGroup>
</Project>
```

Adjust the `'..', '..'` so `RepoRootForPathMap` is your repository root. Keep the SDK default `Deterministic=true`.

What you give up: stack traces from your build no longer show line numbers. A Workshop mod ships only the DLL, so the game never had the PDB anyway.

Anything else that varies between builds breaks this too: a build date or timestamp in a version string, a generated GUID, a `*` in `AssemblyVersion`. Keep the version a literal.

### Checking your mod

1. Build, and note the DLL's hash.
2. Edit a comment, rebuild, and compare. The hash must be identical.
3. Build from a clean checkout in another folder. The hash must still be identical.

If it differs, something is varying; compare the two DLLs with a binary diff to find where. Your first build after adding these settings will differ from your last publish once. Publishing records the new bytes.

### Other files

- Text files may be LF or CRLF; the launcher ignores the difference.
- Don't write timestamps or machine-specific values into files in the mod folder.
- Files that the game or a tool rewrites in the mod folder (logs, caches) show up as changes. Keep them elsewhere.

## Publishing

Use the launcher's Publish dialog. It uploads the content folder, proposes the next version, sends `description.bbcode` and the `Unreleased` lines of `changelog.txt` as the change note, records the folder for change detection, and stamps `changelog.txt` with the version and date. Keep your Steam client running and logged in.
