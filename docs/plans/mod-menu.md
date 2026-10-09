# Mods menu (in-game)

Goal: the launcher adds a **Mods** item to the game's own Game Menu (the Esc / "Distant Worlds 2" panel). Code mods that opt in add entries under it, so a mod's UI has one place to live instead of each mod inventing its own. The launcher is always the first entry; it opens the status panel that the status line already opens.

## What the game gives us (from the decompiled dump, `DW2-XL/temp/game-dump`)

- The Game Menu is `DistantWorlds.UI.GameMenuPanel` (`DWRendererBase.GameMenuPanel`), a `DWPanel` with eight fixed buttons and a seed label. `BindData` lays them out by hand every time the menu is shown (`ScaledRenderer.ShowGameMenu`), and the panel height comes from `UserInterfaceHelper.GameMenuPanelSize` (9 rows). There is no generic menu, no fly-out, and no extension point.
- The panel is anchored to the right edge, below the Game Menu button, so a sub-list has to open to its **left**.
- `PopupList` exists, but it is built for pick-an-item-and-close with the game's own handler delegates. It is not a fit for a persistent fly-out.
- We already have the machinery for the alternative: `StatusWidget` creates `DWButton` custom controls, keeps them alive across interface rebuilds from the per-frame `UserInterfaceController.Update` postfix, and reaches everything by reflection.

## Decision: a click-to-open column, not a hover fly-out

A fly-out is a column of buttons placed beside the Mods button. Hover-to-open needs hover tracking across two controls and a grace gap, and gives nothing a click does not. So: click **Mods**, a column of one button per entry appears to its left, click an entry to run it, click anywhere else to close. It is the same code as the status panel rows (`NewButton`, `PlaceAboveEdge`, `EnsurePresent/Absent`) with a different anchor.

If the click-to-open column turns out to look wrong in game, the fallback is a separate dialog (dim layer plus a list), which is what the status panel already is.

## Adding the Mods item to the Game Menu

Postfix `ScaledRenderer.ShowGameMenu` (runs after `BindData` has laid the panel out):

1. Make the panel one row taller (`Size.Y += rowHeight + gap`; the seed label stays last, so the new row goes between Return to Game and the label, or the label moves down by one row).
2. Create the **Mods** `DWButton` once and `AddControl` it to the panel (like the game's own buttons), sized and positioned with the same formula `BindData` uses. Re-create/re-add if `GameMenuPanel` was rebuilt (null/`Initialized` check per `ShowGameMenu`).
3. Hide the column in a postfix on `HideGameMenu`, and whenever the panel is not `Visible`.

Open to verify in game: that adding a control to an already-initialised `DWPanel` after `SetSizeAndPosition` is drawn and hit-tested; whether `ShowGameMenu` is the only place the panel is sized (`ScaledRenderer` line ~5324 sets it up too).

## Mod API

Same soft-dependency pattern as `LauncherStatus` (no compile-time reference, delegates bound once, a no-op when the launcher is absent). A new `ModMenu` class in the loader beside `ModStatus`, primitives and delegates only:

| Call | Meaning |
| --- | --- |
| `Add(modId, label, Action onClick)` | Adds (or replaces) the mod's entry under Mods. `modId` is the assembly name, as for status. |
| `Remove(modId)` | Removes it. |
| `SetEnabled(modId, bool)` | Greys the entry out (for example, not in a game). |

One entry per mod to start. A mod that wants several actions opens its own dialog from `onClick`, or registers more than one id (`DW2Mod.Settings`, `DW2Mod.Report`). Real nested groups are deferred until a mod asks for them.

Ordering: **DW2 Mod Launcher** first, then mods alphabetically by label.

Each mod gets a `LauncherMenu.cs` copy-paste snippet next to `LauncherStatus.cs`, or the two are merged into one `LauncherHost.cs` (decide when the second consumer exists).

## The launcher's own entry

Opens the existing status dialog (the panel behind the status line), expanded with a short blurb: launcher version, mod count, link to the docs. This reuses `StatusWidget._expanded`; the Mods column closes first.

## Pieces

1. `ModMenu` + `MenuRegistry` (loader, BCL only, unit tested like `StatusRegistry`): entries, order, revision counter.
2. `MenuWidget` (loader, reflection): the Mods button on the Game Menu, the column, click handling, Harmony postfixes on `ShowGameMenu`/`HideGameMenu` plus the existing `Update` postfix for upkeep. Share `Game.Bind` and `Slot` with `StatusWidget` (extract to their own file).
3. Docs: `docs/Mod Menu.md` (user and mod-author facing), snippet in the DW2-XL `dll/README.md` convention.
4. First consumer: DW2FreighterLogistics or DW2SpiesHaveLimits (whichever already has a UI to open); the second proves the ordering.

## What was built (revised after the first in-game test)

The first version (a custom-control Mods button under the panel and a column to its left) worked but did not look like the game. Now: the Mods button and a Random Seed (click to copy) button are real game buttons added to `GameMenuPanel` each frame, the panel grows one row, and Mods opens a Settings-style modal `Dialog` listing the entries (a dialog instead of the fly-out, as it matches how Settings works). Code: `MenuWidget.cs` (`MenuGame.Bind` resolves the game members once), `Clipboard.cs` (Win32, no Windows Forms).

## Open questions

- Should the Mods item also appear on the **main menu** (before a game is loaded)? The main menu is a different screen, and mod entries that need a game would be disabled there. Not in the first cut.
- Hotkey for opening the column? No.
