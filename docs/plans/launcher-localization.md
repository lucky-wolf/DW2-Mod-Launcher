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

## Still to do (step 2): English that never reaches a pack

Messages built in Core as English text, shown to the player:

| Where | What |
| --- | --- |
| `ModDependencies.cs` (~9) | "requires X, which is not installed", "must load after/before", the minVersion messages, and the `Moves` list shown after launch |
| `ModChannels.cs` (~14) | provision and slot problems ("a file for channel X has no slot name", "is not valid JSON", version mismatches, "cannot supply X/Y: why") with a free-form `why` fragment |
| `ModLibrary.cs` (~5) | "Load order rules ... contradict each other", "could not be fixed ... because mods.json could not be read / the game is running / no mods folder" |
| `ModDetails.BuildLinkLines` (~10) | the "Launcher: ... or newer", "Receives:", "Providers: none enabled", "Provides:", "Receivers:", "Depends on: ... - not enabled" lines |
| `ModDetails` problems | "mod.json is not valid JSON: ..." and the "... +N" overflow lines (also in `LaunchDiagnostics.BuildConflictWarning`) |
| `MainViewModel` | "The Workshop folder could not be read ..." |
| `MessageDialog` | default button texts "Copy to clipboard" and "Open log" |

The approach: these return a key plus named values (or take the lookup like `LaunchDiagnostics`), and each `why` becomes its own whole-sentence key instead of a fragment.

Smaller things noticed:

- `WidgetGameStateNotHooked` is given an exception message in `Entry.cs` where the sentence expects a method name.
- Lists of names are still joined with a literal ", " in places; `ListSeparator` exists and is used by the hook report, the rename dialog and the details lines.
- `PublishVisibilityDiffers` and `VisibilityLine` place single-word labels (Public/Private) into a sentence, `HookProblemsSummary` places count phrases: controlled exceptions, listed in Localization.md.
- No plural forms are needed yet: counts are shown as labels (`Conflict files: {Count}`). Add `_ONE`/`_OTHER` keys when a sentence has to read "1 file" / "3 files".

## Step 3

The pack features of the mods plan (settings-dialog merge, `TranslationFiles`, packs from installed mods, the coverage report) build on this and are independent of the mods' own key migration.
