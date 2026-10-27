
# Localization

Three different things get translated in this project, and they work differently:

| What                                                           | Where the words live                                                                                   | Who picks the language                                           |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------- |
| The launcher's own window                                      | `src/DW2ModLauncher.Avalonia/Languages/<code>.json`                                                    | The language picker in the main window                           |
| The launcher's text inside the game (status widget, Mods menu) | The `Widget*` keys of the same packs                                                                   | A translation mod, or the launcher's language (a setting)        |
| A mod's own settings dialog                                    | `settings.schema.<code>.json` sidecars shipped by the mod, and [translation packs](#translation-packs) | The launcher's language                                          |
| A mod's own lines in the in-game status panel                  | Whatever the mod ships (one approach: a `status.<code>.json` next to the DLL)                          | The in-game language, passed to the mod as the `Language` option |
| The game itself (races, events, interface text)                | A translation mod's `patches/` and `.txt` files                                                        | Which translation mod is enabled                                 |

## The launcher's language packs

- A pack is a plain JSON file of key to text, named by language code (`en.json`, `ja.json`, `ru.json`). It sits in the `Languages` folder next to the launcher's executable, and every `.json` there is offered in the language picker.
- `en.json` is canonical: every key exists there. Another pack may contain only the keys translated so far. A key a pack lacks shows as the raw key name (not English), so gaps are obvious.
- `_displayName` in a pack is the name shown in the picker (`日本語`).
- **Every text is a whole sentence (or paragraph).** Anything that varies is a named placeholder: `Delete the mod profile "{Profile}"?`, `Saved {Count} of {Total}`. A translator sees what each one is and may put it anywhere in the sentence. A name is a letter followed by letters and digits (`{Mod}`, `{Count}`); `{Count:0.#}` adds a number or date format; `{{` and `}}` are literal braces. Keep the placeholders of the English text, the order may change. Code supplies them by name, `T("DeleteProfileConfirm", ("Profile", name))` (`NamedText.Fill` in Core, also compiled into the loader). A placeholder the code does not give stays in the text as written and is logged, so a typo is visible.
- No text ends where the code appends the rest, and the code never puts a translated piece into another translated sentence. Where wording depends on the situation there is one key per situation (`HookSharedWith`, `HookSharedWithDisabled`), not a fragment glued on. Independent paragraphs of one dialog may be joined by a blank line. A count is shown as a label (`Conflict files: {Count}`), not as "N file(s)". The few controlled exceptions: a list of names is joined with the pack's `ListSeparator`, and a count phrase like `HookCountMods` is a complete phrase of its own that a summary sentence places.
- `LanguagePackTests` enforces this: no `{0}`-style placeholder, no text that ends with a space or line break, every pack uses the same placeholder names as English, no code appends to a translated text, and every key the code names exists in `en.json`.
- In the UI, text is `{Binding L[Key]}` in XAML and `T("Key", args)` in code. Core code has no lookup of its own; the Core messages that reach the player (launch diagnostics, dependency, data channel and load-order problems, the details panel) take a `Func<string, string>` and fill the named placeholders themselves. Problems that go to the in-game status widget (`ChannelProblems` in the loader manifest) are worded in the in-game language.
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
`settings.schema.<code>.json`, that translates by the schema's keys:

- field labels and descriptions, and enum option text (`fields`);
- group headings and group descriptions, the text under a heading that comes from the schema's `_groups` object (`groups`: a plain string translates the heading, `{ "heading": "…", "description": "…" }` either or both);
- the short column headings of a ladder group (`layouts`: `{ "Group": { "short": { "Option": "…" } } }`).

Whatever a sidecar leaves out is shown as the schema has it, so a translator can do just part of it. Names are matched ignoring case, a sidecar that
exists but is not valid is reported when the dialog opens, and the dialog follows the launcher's language. Format and an example: [DLL Injection.md](DLL%20Injection.md#translating-the-settings-dialog).

## Translation packs

A mod author can maintain English only: anyone may publish a **translation pack**, an ordinary mod that supplies a language for any other mod. Enable the pack and the text appears; the translated mod changes nothing. The launcher is the only place that knows which mods are enabled and in what order, so it does the lookup (`TranslationPacks` in Core).

A pack holds, by convention (no `mod.json` or `dw2modlauncher.json` field is involved):

```
translations/
  ru/                               a language code (a launcher pack's code)
    DW2TroopLogistics/              the mod it translates
      settings.schema.json          the format of today's settings.schema.<code>.json sidecar
      status.json                   the mod's own in-game text, in the mod's own format
```

- The folder under the code names the target mod, matched the way a dependency names one (id, display name, token or folder name, ignoring case).
- A pack may hold some of the files, and a file some of the keys. A mod may also be its own pack.
- **Last one wins, key by key.** For one mod, kind and language: the mod's own sidecar first, then the files of each *enabled* pack in load order. A part a later file leaves out (a field's description, an option) stays as an earlier file had it. A language with a base (`pt-BR` has `pt`) reads the pack's base files before its exact ones.
- A key nobody translated shows as the schema has it (settings dialog) or in English (a mod's in-game text); the launcher's own pack keys keep showing the raw key name.
- A pack file that exists but is not valid is reported by name when the settings dialog opens, never skipped.

What is built: the settings dialog merge, and `TranslationFiles` (the full paths of the `status.json` files, added to a code mod's options beside `Language`; see [DLL Injection.md](DLL%20Injection.md#the-translationfiles-option)). Also built: launcher languages from *installed* packs (below). Still to do: the coverage report and *Create Mod... > Translation Pack*. See [plans/launcher-localization.md](plans/launcher-localization.md).

### Languages from installed mods

An installed mod, enabled or not, may hold `translations/<code>/launcher.json`: the format of a `Languages/<code>.json` pack (key to text, `_displayName` for the picker). The launcher reads these when it starts (`InstalledLanguagePacks`), so a player can pick the language before enabling anything, and a saved choice of such a language survives a restart. A language the launcher has no pack for becomes a new entry in the picker; for one it has (`ru`), the file's keys replace the shipped ones, key by key, and each replacement is logged. Several files for one code apply in mod folder order (the load order is not known yet), the last winning per key. A key still missing shows as the raw key name, as with any pack. A file that is not valid JSON is reported in a message when the launcher starts and adds nothing. A pack installed while the launcher runs is found at the next start.

## A mod's own in-game text

The launcher adds `"Language": "<code>"` (and `"TranslationFiles"` when packs supply files) to every code mod's options JSON at launch. The value is the same in-game language the widget uses (the enabled translation mod's declared language, or the launcher's own when Settings says so; English otherwise), not the dialog's language. What a mod does with it is up to the mod; one approach is to translate the lines it reports to the status panel from a `status.<code>.json` next to its DLL, mapping the English text to the translated text. The loader translates nothing a mod reports. Details: [DLL Injection.md](DLL%20Injection.md#the-language-option).

## Translating the game

**Create Mod... > Localization Mod** builds a mod holding all player-visible game text as patch files and `.txt` files. When the language you type is the code or display name of a launcher pack, it also records `language` in the new mod's `dw2modlauncher.json`. Details, the font and Galactopedia rules: [xml-patching.md](xml-patching.md#translating-the-game-the-localization-mod-shortcut).

## Translating the documentation

[README.md](../README.md) has a Japanese twin, [README.ja.md](../README.ja.md); a user-facing change to one must be mirrored in the other in the same change. `scripts/validate.py` (so CI) checks that a `*.ja.md` keeps its English original's heading structure, code blocks and relative links, so only prose differs. Design docs under `docs/` stay English. See [AGENTS.md](../AGENTS.md).

## Known gaps

- Text that comes out of exceptions (.NET, the game, a mod), which is shown as it is.
- The window banner and credits, and the "DW2 Mod Launcher" dialog title, which are branding.
