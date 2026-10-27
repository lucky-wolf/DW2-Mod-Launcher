# Launcher localization: named placeholders and whole sentences

Plan and inventory (owner + Claude), 2026-10-26. The launcher is the first piece of the work described in DW2-XL `docs/plans/translation-keys.md`; the mods follow once they are out of flux. The game's own text and XL's XML are out of scope.

## Decisions

- **Keys stay PascalCase** (`DeleteProfileConfirm`). The launcher already had symbolic keys with `en.json` canonical; nothing is renamed.
- **Named placeholders, not numbers.** `{Profile}`, `{Count}`, `{Mod}`: short, more than one character, so a translator knows what each is. Code gives them as `("Profile", name)` pairs (`NamedText.Fill`). A key with a placeholder the code does not give shows the placeholder as written and is logged; a pack that disagrees with English fails `LanguagePackTests`.
- **Whole sentences.** No key ends where the code appends the rest, and no translated piece is placed inside another translated sentence. Where wording depends on the situation there is one key per situation.
- **A missing key still shows as the raw key name**, not English: this repo's rule is that a gap must be obvious ([Localization.md](../Localization.md)), and AGENTS.md forbids silent fallbacks. (The mods' plan proposed an English fallback for mods; that is a separate decision for them.)
- Community packs written with `{0}` stop working (they show `{0}` as written); `ja` and `ru` were converted with the rest.

## Done (step 1)

- `NamedText` (Core, compiled into the loader too) with tests; `T`, `LocalizedStrings.Format`, `HookOverlapAnalyzer.F` and `StatusStrings.Get` take named pairs.
- All 147 keys that had `{0}`-style placeholders now have named ones, in `en`, `ja` and `ru`.
- About 100 fragment keys became whole sentences (the profile dialogs, status bar, details panel, launch diagnostics, the Publish and Delete/Rename dialogs, the in-game status line and block headings, the hook report). Examples: `DeleteProfileConfirm` is `Delete the mod profile "{Profile}"?`; the details lines `Conflicts` + `Files` + `High` + `Low` became `ConflictsSummary`; `RenameModConfirm` and `RenameModConfirmWithDlls` replace a DLL line spliced on; `WidgetState*` words became `WidgetBlock*` lines that hold the heading.
- Unused keys removed (`Documents`, `IncludedTools`, `State`, `Location`, `Updated`) and the keys that the split made redundant.
- Guards in `LanguagePackTests` (see Localization.md).

## Done (step 2): English that never reached a pack

The messages below were built in Core as English text. They now take the lookup (`Func<string, string>`) and are whole-sentence keys with named placeholders in `en`, `ja` and `ru`: the dependency, data channel and load-order problems (each `why` of a channel file is its own key, `ChannelCannotSupply*`), the details panel link lines (`Link*`), `ModJsonNotValid`, the overflow line `MoreItems`, and the Workshop-folder and scan/settings notices in `MainViewModel`. A dependency problem now names its mod in the sentence (`{Mod} requires {Target}, ...`) instead of a prefix, so the same text serves the launch check and the details panel. `MessageDialog` no longer has English default button texts. The inventory, for the record:

| Where | What |
| --- | --- |
| `ModDependencies.cs` (~9) | "requires X, which is not installed", "must load after/before", the minVersion messages, and the `Moves` list shown after launch |
| `ModChannels.cs` (~14) | provision and slot problems ("a file for channel X has no slot name", "is not valid JSON", version mismatches, "cannot supply X/Y: why") with a free-form `why` fragment |
| `ModLibrary.cs` (~5) | "Load order rules ... contradict each other", "could not be fixed ... because mods.json could not be read / the game is running / no mods folder" |
| `ModDetails.BuildLinkLines` (~10) | the "Launcher: ... or newer", "Receives:", "Providers: none enabled", "Provides:", "Receivers:", "Depends on: ... - not enabled" lines |
| `ModDetails` problems | "mod.json is not valid JSON: ..." and the "... +N" overflow lines (also in `LaunchDiagnostics.BuildConflictWarning`) |
| `MainViewModel` | "The Workshop folder could not be read ..." |
| `MessageDialog` | default button texts "Copy to clipboard" and "Open log" |

Mod language switches re-word the stored dependency problems (`AnalyzeDependencies`).

## Smaller things noticed

- Done with step 2: `WidgetGameStateNotHooked` is only given a method name (a thrown error uses `WidgetGameStateNotTracked`), and name lists use `ListSeparator` (`WidgetListSeparator` in the loader). Internal log lines and the XML patch runner's own messages are English by design.
- `PublishVisibilityDiffers` and `VisibilityLine` place single-word labels (Public/Private) into a sentence, `HookProblemsSummary` places count phrases: controlled exceptions, listed in Localization.md.
- No plural forms are needed yet: counts are shown as labels (`Conflict files: {Count}`). Add `_ONE`/`_OTHER` keys when a sentence has to read "1 file" / "3 files".

## Step 3: translation packs

The pack features of DW2-XL `docs/plans/translation-keys.md`, independent of the mods' own key migration.

Done: `TranslationPacks` (Core: ordered files for a mod, kind and language from enabled mods, base language first), the settings dialog merge (`ModSettingsTranslation.Read` takes the pack files; per key, last wins; a broken pack file throws `InvalidDataException` naming it), and `TranslationFiles` in the options JSON of every code mod (`LoaderManifestBuilder`). Tests: `TranslationPacksTests`. Docs: [Localization.md](../Localization.md#translation-packs), [DLL Injection.md](../DLL%20Injection.md#the-translationfiles-option).

Still to do:

- Done: `Localization` also lists `translations/<code>/launcher.json` from *installed* packs (`InstalledLanguagePacks`, `Localization.UseInstalledPacks`, called once at start-up before the saved language is checked), so the language can be picked before anything is enabled. A pack code that a launcher pack already has adds keys over it (last wins, logged). Order is mod folder name, not load order, since the load order is not read yet; a pack installed while the launcher runs needs a restart.
- Coverage report at launch: per enabled mod and non-English language, keys without text, shown as Info. Needs the mods' English catalogue (`en.json` / `status.en.json`), so it waits for the key migration.
- *Create Mod... > Translation Pack*: a skeleton for a language and chosen target mods, prefilled with the untranslated keys (same dependency).
- XL side: `Loc.Use` merges `TranslationFiles`; ja/ru move out into first-party packs after the key migration.
