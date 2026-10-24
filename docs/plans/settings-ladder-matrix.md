# Settings ladder matrix

Status: **built, first version**. Needed by the planned Diplomatic Corps code mod (DW2-XL `docs/Diplomatic Corps.md`), whose 14 treaty rules are "allowed from this relationship up" enums.

## Decision

The matrix is a **layout over existing enum fields**, not a new storage type. Saved values stay flat enum keys, so the mod reads plain settings; translation, reset, "modified" and validation are the fields' own. A table of variable-length records is not needed (the game and the XML already hold those).

## Schema

A root `_layouts` object beside `_groups`:

```json
"_layouts": { "Treaties": { "kind": "ladder", "none": "Never", "short": { "CloseAlly": "CA" } } }
```

- Every field of the group is an `enum`. The columns are the longest option list (`none` must be in it); a row may offer fewer options in the same order, and its missing cells are blocked out (dark, disabled, no click). No extra metadata: the row's own options say what is possible.
- Columns are the longest option list in schema order, left to right; `none` must be its first or last option and is a column of its own (filled only while the row holds it). The bar grows from the `none` end: any other cell is filled from the column next to `none` out to the field's value. No reverse flag: the order in the file is the order on screen.
- Click a cell: the value becomes that column (a no-op when it already is). Choosing nothing means clicking the `none` column.
- `short` gives narrow headings; the full option text is the tooltip. A sidecar translates them (`layouts.<group>.short`).
- Anything that cannot be drawn throws `InvalidDataException` naming the group (no fallback to the plain list), reported when the dialog opens.

## Code

- Core: `ModSettingsLayout`, `ModSettingsGroup.Layout`, validation in `ModSettingsSchemaReader`, `ModSettingsTranslation.ShortHeading`.
- Avalonia: `LadderViewModel`/`LadderRowViewModel`/`LadderCellViewModel` in `ModSettingsEditorViewModel.cs`; `SettingGroupViewModel` shows the ladder or the field list; templates in `ModSettingsDialog.axaml`. Rows wrap the existing `SettingFieldViewModel`, so the group reset and Save work unchanged.
- Strings: `ModSettingsLadderSet` (cell tooltip) in every pack.

## Open

- Look and feel is unverified in the running launcher (cell width 84, gold selected cell); tune after a first look.
- Narrow panes: headings are fixed-width, no wrapping; revisit if a long row label or many columns overflow.
- Keyboard: cells are buttons, so Tab/Space works; no arrow-key navigation yet.
