# Moving a setting from one mod to another

A mod author who splits a large mod, or hands a feature to a companion mod, can have the launcher carry each player's saved value across, so nobody finds a setting reset. Both mods say so in their own `settings.schema.json`; there is nothing to install and no version number to keep.

## The two declarations

The mod that **gave** the setting away lists it under a root `_moved` object, keyed by the name it had, naming the receiver by its `uid` (`dw2modlauncher.json`) and the key it has there:

```json
{
  "fields": [ { "key": "Enabled", "type": "bool", "default": true } ],
  "_moved": {
    "FlagColors": { "mod": "5b0c2d1e-3f4a-4b6c-8d7e-9f0a1b2c3d4e", "key": "FlagColors" }
  }
}
```

The mod that **received** it names the giver in the field's `movedFrom`, a list of `{ "mod": uid, "key": oldKey }` tried in order:

```json
{ "key": "FlagColors", "type": "bool", "default": true,
  "movedFrom": [ { "mod": "7a1e4c20-0d5b-4e8f-a3c6-2b9d8e7f6a51", "key": "FlagColors" } ] }
```

Both are needed. A mod that names another in `movedFrom` gets nothing unless that mod's own schema says it gave the key to it, so no mod can pull another's values. A `_moved` key may not still be a field of the same schema, and every `mod` must be a UUID; a schema that breaks either is reported by name, like any unreadable schema.

## What the launcher does

1. **Sets the value aside.** When the giver's schema lists a key in `_moved`, the player's stored value for it leaves the giver's settings file for `ModSettingsRetired/<token>.json` instead of being deleted. A value equal to the default the schema last declared is not set aside: the player never chose it, so the receiver's own default is right.
2. **Hands it over.** Each enabled mod that lists a source in `movedFrom` takes the value, but only while its own setting still holds its default. A choice the player already made there is never replaced.
3. **Once.** Taking removes the value from the giver's set-aside file, so a player who later changes the setting back is not overwritten again. Every giver that confirms the move gives up its value; the first source listed supplies it.

The pass runs on every scan and when a mod is enabled, so the order the mods arrive in does not matter: a receiver installed a year later still finds the value. A value that does not fit where it moved (wrong kind, outside min/max, not one of the options) is reported to the player naming both mods, and the receiver keeps its default; it is not coerced.

## Rules of thumb

- **A declaration is state, not an event.** Keep `_moved` and `movedFrom` in the schemas for good; a player who skipped five versions is handled the same as one who did not. Costs nothing.
- **A setting that passed through several mods:** the last receiver lists every earlier mod in `movedFrom`, oldest last or first as you like (the first one that has a value wins). The launcher does not walk a chain through a mod the player does not have.
- **The giver must still be installed** for its declaration to be read. A value set aside by a mod the player later removed waits until it returns.
- **Release the giver and the receiver in the same period** where you can; either order works, but the player only sees the value arrive once both are installed and the receiver is enabled.
- Only a setting the receiver's schema declares can receive; text, folder and file settings move as text.

Code: `ModSettingsMigration`, `ModSettingsStore.RetireMoved`/`TakeRetired`, `ModSettingsSchemaReader` (`_moved`, `movedFrom`).
