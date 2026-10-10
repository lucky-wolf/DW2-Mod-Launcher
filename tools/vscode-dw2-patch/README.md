# DW2 XML Patch (VS Code extension)

Open one of the game's data files (`Races.xml`, `ComponentDefinitions.xml`, ...), put the cursor on the line you want to
change and run **DW2: Patch this** (right-click menu, or `Ctrl+Alt+P`). The extension writes the patch for it into your
mod's `patches` folder, in the format described in [docs/xml-patching.md](../../docs/xml-patching.md), and shows it next to
the data file with the cursor on the value to edit.

```xml
<!-- cursor on <Aggression>1.5</Aggression> of the Race with RaceId 0 -> patches/Races.xml -->
<ArrayOfRace>
  <Race id="0">
    <Aggression>1.5</Aggression>
  </Race>
</ArrayOfRace>
```

## What the cursor means

| Cursor on | Result |
|---|---|
| a plain field (`<Aggression>`) | the field with its current value, to edit (no prompt; **Patch this… (choose what to do)** also offers *remove*) |
| a field inside a struct (`<G>` in `<MainColor>`) | the same, wrapped in the struct |
| an item of a keyed list (`<Bonus>` with `<Type>`) | `<Bonus id="…">`; an unkeyed one (`<ComponentStats>`) gets `index="N"` |
| an item of a list of plain values (`<short>`) | set that position, *add* a value, or *remove* by value |
| a struct, list item or whole list | change chosen fields, *replace* with an editable copy, *add* another like it, or *remove* |
| the entity tag or its key (`<Race>`, `<RaceId>`) | pick which fields to change, or remove the entity |
| a selection covering several fields | all of them in one patch |

The patch is **merged** into `patches/<same file name>` (created if missing): new elements are added inside the right
entity, nothing else in the file is touched, and what is already patched is left as it is and reported.

## Setup

Nothing, when the workspace contains your mod (a folder with `mod.json`, at the root or one level down). Otherwise the
extension asks once for the mod folder and remembers it for the workspace. Settings (`dw2Patch.*`): `modFolder`,
`patchFile` (one fixed patch file instead of one per data file), `indent`.

## How it knows what a field is

`schema/schema.json` is the game's data schema, exported from `DistantWorlds.Types.dll` by [tools/export-schema](../export-schema)
(the same reflection the launcher's loader uses at run time). It tells a list from a struct, which matters for lists with a
single item or none. The entity keys come from `src/core/keyMap.ts`, a copy of `XmlPatching/KeyMap.cs` (a test fails
when they differ). Data that the schema does not cover is classified by the shape of the XML instead.

Regenerate the schema after a game update:

```text
dotnet run --project tools/export-schema -c Release -- "<game folder>" tools/vscode-dw2-patch/schema/schema.json
```

## Develop

The logic is in `src/core` and does not import `vscode`; `src/extension.ts` is the thin shell.

```text
npm install
npm test                  # node >= 22.18 (runs the TypeScript tests directly)
npm run build             # tsc -> out/
npm run package           # tests + build + dw2-xml-patch.vsix
npm run install:local     # package, then install it into your VS Code (reload the window afterwards)
npm run publish:marketplace
```

CI builds the extension on every PR and attaches `dw2-xml-patch-X.Y.Z.vsix` to each launcher release (see
[docs/CI Releases.md](../../docs/CI%20Releases.md)).

`package` and `publish:marketplace` run the tests and the build first (`vscode:prepublish`). Publishing to the Marketplace
needs a publisher you own (change `publisher` in `package.json`) and a personal access token:
`npx vsce login <publisher>` once, then the script. Without a Marketplace listing, share the `.vsix` file (for example as a
GitHub release asset); it installs with `code --install-extension dw2-xml-patch.vsix`.

Press F5 in VS Code on this folder to try it (needs a `.vscode/launch.json` of type `extensionHost`).
