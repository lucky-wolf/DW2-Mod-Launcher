# Localization

Three different things get translated in this project, and they work differently:

| What | Where the words live | Who picks the language |
| --- | --- | --- |
| The launcher's own window | `src/DW2ModLauncher.Avalonia/Languages/<code>.json` | The language picker in the main window |
| The launcher's text inside the game (status widget, Mods menu) | The `Widget*` keys of the same packs | A translation mod, or the launcher's language (a setting) |
| A mod's own settings dialog | `settings.schema.<code>.json` sidecars shipped by the mod | The launcher's language |
| A mod's own lines in the in-game status panel | Whatever the mod ships (DW2-XL's mods: `status.<code>.json`) | The in-game language, passed to the mod as the `Language` option |
| The game itself (races, events, interface text) | A translation mod's `patches/` and `.txt` files | Which translation mod is enabled |

## The launcher's language packs

- A pack is a plain JSON file of key to text, named by language code (`en.json`, `ja.json`, `ru.json`). It sits in the `Languages` folder next to the launcher's executable, and every `.json` there is offered in the language picker.
- `en.json` is canonical: every key exists there. Another pack may contain only the keys translated so far. A key a pack lacks shows as the raw key name (not English), so gaps are obvious.
- `_displayName` in a pack is the name shown in the picker (`日本語`).
- Text with `{0}`, `{1}` placeholders is formatted with the values the code supplies. Keep the placeholders; the order may change.
- In the UI, text is `{Binding L[Key]}` in XAML and `T("Key", args)` in code. Core code has no lookup of its own; the few Core messages that reach the player take a `Func<string, string>` (`LaunchDiagnostics`).
- **First run:** with no saved settings the launcher speaks the system's language (`CultureInfo.CurrentUICulture`) if a pack matches: a pack named exactly like the culture (`pt-BR`) first, then its language (`ja` for `ja-JP`), else English. A saved choice always wins.

### Adding a language

1. Copy `en.json` to `<code>.json` in `src/DW2ModLauncher.Avalonia/Languages/` and set `_displayName`.
2. Translate. A shipped pack must contain every `Widget*` key with the same placeholders as English (a test checks that); everything else may be partial.
3. Nothing else needs to change: the picker lists the file, and the first-run default can pick it.

## The launcher's text inside the game

The in-game status line and the Mods menu are drawn by the loader (`DW2ModLauncher.Loader`) inside the game process, which cannot read the `Languages` folder. So:

- The loader's words are the `Widget*` keys. The launcher writes the chosen language's keys into the loader manifest (`Strings`) at launch. English is built into the loader (`StatusStrings`) for use without the launcher, and a test keeps it equal to `en.json`.
- **Which language:** the game has no language setting of its own (a game is "in French" because a translation mod replaces its text), so by default the widget follows the enabled translation mod. A translation mod declares `"language": "fr"` in its `dw2modlauncher.json`; the last enabled mod in load order that declares one wins, and with none the widget is English. In Settings, *Launch > In-game widget language* can instead be set to the launcher's own language. See [Mod Status Line.md](Mod%20Status%20Line.md#language).
- Log files stay English. What a mod reports itself (its details, errors, summaries) is shown as the mod wrote it.
- Text that is not Latin (Japanese, Russian) needs a font the game can draw: the translation mod's `font`, which the launcher passes to the game as `--font`.

## A mod's settings dialog

The dialog text comes from the mod's `settings.schema.json`, in the author's language. A mod (or its translator) adds a language with a sidecar,
`settings.schema.<code>.json`, that translates labels, descriptions, group headings and enum option text by the schema's keys; whatever it
leaves out is shown as the schema has it. The dialog follows the launcher's language. Format and an example: [DLL Injection.md](DLL%20Injection.md#translating-the-settings-dialog).

## A mod's own in-game text

The launcher adds `"Language": "<code>"` to every code mod's options JSON at launch. The value is the same in-game language the widget uses (the enabled translation mod's declared language, or the launcher's own when Settings says so; English otherwise), not the dialog's language. What a mod does with it is up to the mod: DW2-XL's mods translate the lines they report to the status panel from a `status.<code>.json` next to their DLL. The loader translates nothing a mod reports. Details: [DLL Injection.md](DLL%20Injection.md#the-language-option).

## Translating the game

**Create Mod... > Localization Mod** builds a mod holding all player-visible game text as patch files and `.txt` files. When the language you type is the code or display name of a launcher pack, it also records `language` in the new mod's `dw2modlauncher.json`. Details, the font and Galactopedia rules: [xml-patching.md](xml-patching.md#translating-the-game-the-localization-mod-shortcut).

## Translating the documentation

[README.md](../README.md) has a Japanese twin, [README.ja.md](../README.ja.md); a user-facing change to one must be mirrored in the other in the same change. `scripts/validate.py` (so CI) checks that a `*.ja.md` keeps its English original's heading structure, code blocks and relative links, so only prose differs. Design docs under `docs/` stay English. See [AGENTS.md](../AGENTS.md).

## Known gaps

- Messages from Core that are not routed through a lookup: the rule warnings of `ModChannels`, `ModDependencies` and `ModLibrary` (mod dependency, data channel and load-order problems).
- Text that comes out of exceptions (.NET, the game, a mod), which is shown as it is.
- The window banner and credits, and the "DW2 Mod Launcher" dialog title, which are branding.
