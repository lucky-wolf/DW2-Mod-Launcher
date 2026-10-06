# Shared in-game status line for code mods, hosted by the loader

Status: planned (not started). A working single-mod prototype exists in DW2-XL (see "Prototype").

## Goal

A code mod that fails to load, fails to patch, or silently does nothing is hard to notice: the game gives no sign, and a whole test run can be wasted on a stale or half-loaded build. One line of text in the lower-left corner of the screen shows, at a glance, that the injected code is loaded and running, in an error colour with a warning icon when something is wrong.

The launcher's loader already loads every code mod, so it should own that line. Every mod adds its own data to it cumulatively, and the one line expands into a per-mod panel when someone wants the specifics. No mod draws its own text, and two mods never fight over the same corner.

## Prototype (what exists, in DW2-XL `dll/DW2FreighterLogistics`)

- `Status.cs`: name, version, build time (the dll's file time, so a stale build is visible), the features that installed, an error list, and a heartbeat. Every log line containing "failed" or "fatal" is an error, so call sites do not each have to report.
- `StatusLine.cs`: a `DWLabel` subclass registered through `UserInterfaceController.AddCustomControl`, `PostScene = true`, with a Harmony postfix on the per-frame `UserInterfaceController.Update` that re-adds it when the game clears `CustomControls` (scaling change, new game) and refreshes text, colour and position. Icon drawn with `DrawingHelper` lines and rectangles. Anchored lower left, 1px from the edges.
- Output: `DW2FreighterLogistics.dll 0.1.0 (built 10-06 15:05) running: Hubs, FreighterBoost, FuelFirst`, or the same in red with a warning triangle and `ERROR: <first message>, see freighter-logistics.log`.
- What it already caught: a Harmony patch that threw at install (ambiguous overload) and took the rest of startup with it. The status line plus install isolation (`Try` around each feature) would have shown it in red.

## Design

### Ownership

`DW2ModLauncher.Loader` owns the registry and the control. Mods contribute data; they never draw.

- The loader also reports for every mod it loads, with no cooperation from the mod: loaded or not, load time, and the exception text when `Entry.LoadOne` catches one. A mod that does nothing but ship `Init()` still appears, and a mod that crashes on load shows in red.
- A mod that wants more (feature list, heartbeat, its own errors and details) registers a source and updates it.

### Contract between loader and mod

The loader stays minimal (BCL plus `System.Text.Json`) and mods must keep working when the launcher is not used (direct `--low-level-inject`). So the contract is a soft dependency by reflection, with no compile-time reference:

- The loader exposes a public static class (for example `DW2ModLauncher.Loader.ModStatus`) with a small surface: `Register(id, displayName, version, built) -> handle`, and on the handle `SetLevel(Ok|Info|Warn|Error, summary)`, `Detail(key, text, level)`, `Error(text)`, `Heartbeat()`, `Installed(feature)`.
- A mod resolves it with `Type.GetType("DW2ModLauncher.Loader.ModStatus, DW2ModLauncher.Loader")`. Absent: the mod falls back to its own standalone line (or none). Present: it registers and shows nothing of its own, so there is exactly one line.
- Ship the thin reflection wrapper as a single copy-paste `.cs` snippet in the docs (about 40 lines), so mods do not each reinvent it. Delegates are created once, then calls are cheap.
- Alternative considered: a tiny `DW2ModLauncher.Loader.Api.dll` mods compile against with `Private=False`. Cleaner types, but adds a deploy dependency and a version-skew problem. Revisit only if the reflection wrapper proves awkward.

### Model

- Level per source: Ok, Info, Warn, Error. The aggregate is the worst level.
- "Running" means a heartbeat in the last 30 seconds (the prototype's rule); "loaded" means registered, no heartbeat yet (main menu).
- Details are ordered key and text pairs plus a level, shown in the expanded panel and as the hover text.

### Display

- **Collapsed (default):** one line, lower left, 1px margin. One mod: `name.dll version (built time) running: features`. Several: `DW2 mods: 4 loaded, all ok`. Any error: red, warning icon, and the worst message (`2 mods with problems: DW2FreighterLogistics: FuelFirst failed to install`).
- **Expanded:** a panel above the line, one row per mod (icon, name, version, build time, state), with its details under it. Errors listed first, with a button to open the mod's log folder.
- **How to expand:** click on the line, and a hotkey. Not yet decided; see questions.
- Hides with the rest of the UI (the `userInterfaceDisplayLevel` rule in `DrawPostScene`), and never takes mouse input except on the line itself.

### Where the UI code lives

The loader process has no reference to the game assemblies today. The rendering needs `DistantWorlds.UI`, `DistantWorlds.Types` (`DrawingHelper`), `Stride.Core.Mathematics`, `Stride.Graphics`, `Stride.Rendering` and Harmony. Keep the registry in the loader (BCL only) and put the rendering in a second assembly, loaded by the loader after the game assemblies are available, so the loader itself keeps its minimal footprint and still works if the UI assembly fails to load (the registry and `loader.log` still record everything).

## Rules

- The loader never lets a status failure affect a mod or the game: every status call is wrapped, the frame hook reports once and goes quiet.
- Registration is idempotent by id, so reloads and duplicate injection do not double-list a mod.
- A mod's own errors are never hidden: the status line is an index, and `loader.log` plus the mod's own log keep the full text.
- No per-mod on/off setting for the line itself; one launcher-level setting at most.

## Steps

- [ ] Research the control side: mouse hit-testing and click handling on a `DWControl` (can a label take clicks without the game's UI swallowing them?), and whether the game has a hotkey system a mod can add to.
- [ ] Loader: registry (`ModStatus`), automatic per-mod load status from `Entry.LoadOne`, heartbeat expiry.
- [ ] UI assembly: lift `StatusLine` from the DW2-XL prototype; collapsed line first, then the expanded panel.
- [ ] Reflection wrapper snippet and a short doc for mod authors (add to `docs/DLL Injection.md`).
- [ ] Port DW2FreighterLogistics to it: register with the loader when present, keep the standalone line as the fallback.
- [ ] Tests for the registry (levels, worst-of aggregation, heartbeat expiry, idempotent register) in `DW2ModLauncher.Tests`; the rendering is checked by hand in the game.
- [ ] Linux and Proton: check that the UI hooks behave the same (the game runs the same assemblies, so it should).

## Open questions

- Expand trigger: click on the line, a hotkey, or both. Does a click get through the game's own input handling?
- Should the expanded panel stay open across scene changes, and should the state persist between launches?
- Scaling: the prototype follows the game's font scaling through `DoLayout`; confirm the panel does at every `ScalingSize`.
- Do we want a "copy report" button (all mod states and errors as text) for bug reports?
- Localisation: status words (running, loaded, error) go through the launcher's language packs, or stay English because they appear in the game window.
- What the line shows for a mod using the old `Init()` only and nothing else: loaded, with no heartbeat, which reads as "loaded" forever. Probably show just the name and a neutral icon.
