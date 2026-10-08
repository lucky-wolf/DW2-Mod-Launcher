# XML patching for mod authors

Change a few values in the game's data **without copying whole entities**. Several mods can patch the same entity and
all of their changes apply.

Requires the DW2 Mod Launcher (the game must be started through it).

## Quick start

1. In your mod folder, create a `patches` folder.
2. Add an XML file there (any name), for example `patches/races.xml`:

```xml
<ArrayOfRace>
  <Race id="0">                                  <!-- the Race whose RaceId is 0 (Human) -->
    <Aggression>1.5</Aggression>                 <!-- set this field -->
    <MainColor><R>255</R><G>40</G></MainColor>   <!-- only R and G change; B and A stay -->
  </Race>
</ArrayOfRace>
```

3. Enable the mod in the launcher and start the game.

The file is a trimmed copy of the game's data: copy an entity out of the game's `data` folder, delete everything you
are not changing, and replace its key element (`<RaceId>0</RaceId>`) with `id="0"` on the entity tag.

Two rules to remember:
- The **root element** says which data you are patching (`ArrayOfRace`, `ArrayOfComponentDefinition`, ...), the same as in the game's files.
- Put patches in **`patches/`**, never in the mod root: root XML files are loaded by the game as full data.

## What you write

Whatever you write **overrides**; whatever you leave out stays as it is. Three attributes pick the target and the action:

| Attribute | Meaning |
|---|---|
| `id="…"` | which entity or list item (matched by its key, see [Keys](#keys)) |
| `index="N"` | which list item, by position. **Starts at 1** (V1, V2, V3 ... as the game shows levels) |
| `op="…"` | `add`, `remove` or `replace`. Without `op` the element just overrides |

Elements always mean "this field". Attributes are removed before the game sees the data.

### Set or change a field
```xml
<Race id="0"><Caution>1.3</Caution></Race>
```
A field that is missing in the data (the data leaves out default values) is created.

### Edit one item of a list
Items with a key are chosen with `id`; items without one (such as component levels) with `index`.
```xml
<ComponentDefinition id="20">
  <ResourcesRequired>
    <ResourceQuantity id="8"><Amount>6</Amount></ResourceQuantity>      <!-- resource 8: amount becomes 6 -->
  </ResourcesRequired>
  <Values>
    <ComponentStats index="2"><WeaponRawDamage>14</WeaponRawDamage></ComponentStats>   <!-- level V2 -->
  </Values>
</ComponentDefinition>
```

### Add a list item
`op="add"` appends the item **exactly as you write it** (fields you leave out get their defaults, so write the stats you
need). If the item has a key, write the key as a normal element. `index="N"` inserts it at position N instead.
```xml
<ComponentStats op="add"><CrewRequirement>5</CrewRequirement><WeaponRawDamage>40</WeaponRawDamage></ComponentStats>
```

### Remove
```xml
<Race id="0">
  <Aggression op="remove"/>                                  <!-- field goes back to its default -->
  <Bonuses><Bonus id="ResearchAll" op="remove"/></Bonuses>   <!-- list item -->
</Race>
<Race id="9" op="remove"/>                                   <!-- whole entity -->
```

### Replace
`op="replace"` rewrites a struct, one list item, or a whole list with exactly what you write (anything left out gets its
default). Inside it, write plain data: no `id`, `index` or `op`.
```xml
<DisplayTextureNames op="replace"><string>Effects/Weapons/GreenLaser1</string></DisplayTextureNames>
<Values op="replace"> ...all levels... </Values>
<ComponentStats index="2" op="replace"> ...a new V2... </ComponentStats>
```
(Entities themselves cannot be replaced by a patch; use a normal data file for that.)

### Lists of plain values (`short`, `string`, `float`)
These items have no key, so you say what to do:
```xml
<AllowableGovernmentIds><short op="add">9</short></AllowableGovernmentIds>               <!-- add -->
<AlternateFlagFilenames><string op="remove">UserInterface/Flags/Human_6</string></AlternateFlagFilenames>   <!-- remove by value -->
<IncomeFactors><float index="4">1.5</float></IncomeFactors>                               <!-- set the 4th -->
<PreferredGovernmentIds op="replace"><short>2</short><short>3</short></PreferredGovernmentIds>   <!-- whole list -->
```
Some fields are lists that must stay in step (for example `TreatyLevelModifierTypes` and `TreatyLevelModifierValues`):
change both together with `op="replace"`.

## Keys

`id` is matched against the entity's key field:

| Data | Key |
|---|---|
| Race | `RaceId` |
| ComponentDefinition | `ComponentId` |
| ResearchProjectDefinition | `ResearchProjectId` |
| ShipHull | `ShipHullId` |
| Government, Resource, OrbType, Artifact, TroopDefinition, ... | `GovernmentId`, `ResourceId`, `OrbTypeId`, `ArtifactId`, `TroopDefinitionId` (always `<Type>Id`) |
| GameEvent | `Name` (`id="Home Ruin Generation (Haakonish)"`) |
| TourItem | `Title` |

Common keyed list items: `ResourceQuantity` (`ResourceId`), `Component` (`ComponentId`), `ComponentBay` (`ComponentBayId`),
`Bonus` (`Type`), `RaceFactor` (`RaceId`), `OrbTypeFactor` (`OrbTypeId`). Not keyed, so use `index`:
`ComponentStats`, `GameEventAction`, `GameEventCondition`, `RunningLight`, `ResearchPath`, `ShipHullReference`.

## Good to know

- **Order.** Mods apply in launcher load order; inside a mod, patch files by name, top to bottom. A later patch sees the
  result of earlier ones (after you add a level, `index="5"` can address it).
- **Positions shift.** `index` counts the list as it is when your patch runs. Prefer `id` where an item has a key.
- **Every definition is patched.** If several data files define the same entity, the patch applies to all of them, so it
  works on entities added by other mods too.
- **New levels** of a component also need a research project that unlocks them (patch `ResearchProjectDefinition`).
- **File paths** (textures, sounds) are not checked; a wrong path fails when the game loads the asset. Ship your assets in the mod.
- Not supported: patching "every Rail Gun" at once, arithmetic like "+20%", creating whole new entities (use normal data files).

## When something does not work

A problem never stops the game from loading: the faulty item is skipped, everything else still applies, and the cause is
written to **`patches.log`** (next to `loader.log` in the launcher's `Loader` folder). Each line has the file and line number:

```
patches/races.xml:5: error: <Race> has no field 'Agression' - did you mean 'Aggression'?
patches/races.xml:9: error: Race id=99 not found in any ArrayOfRace file (has: 0, 1, 2, ...) - did you mean '9'?
patches/races.xml:12: error: <Bonus> is a list item of <Bonuses>; say which one: id="Type value", index="N" or op="add"
patches/races.xml:14: error: Race id=3 > PreferredGovernmentIds > short index=9: list has 1 item(s) (valid: 1..1)
patches/races.xml: 9 applied, 0 unchanged, 5 skipped
```

Every change that was applied is logged as `old -> new`, so you can confirm what happened. "unchanged" means the value was
already what you wrote. The usual causes of a patch that "does nothing": a typo in a field name, the wrong `id`, the file
sitting outside `patches/`, or the mod not enabled in the launcher.
