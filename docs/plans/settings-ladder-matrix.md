# Settings ladder matrix

Status: **built, first version**. Needed by the planned Diplomatic Corps code mod (DW2-XL `docs/Diplomatic Corps.md`), whose 14 treaty rules are "allowed from this relationship up" enums.

## Decision

The matrix is a **layout over existing enum fields**, not a new storage type. Saved values stay flat enum keys, so the mod reads plain settings; translation, reset, "modified" and validation are the fields' own. A table of variable-length records is not needed (the game and the XML already hold those).

## Schema

A root `_layouts` object beside `_groups`:

```json
"_layouts": { "Treaties": { "kind": "ladder", "none": "Never", "short": { "CloseAlly": "CA" } } }
```

- Every field of the group is an `enum` with identical options; `none` is one of them and has no column.
- Columns are the other options in order. A cell is filled from the first column up to the field's value.
- Click a cell: the value moves there. Click the value's own cell: the value becomes `none`.
- `short` gives narrow headings; the full option text is the tooltip. A sidecar translates them (`layouts.<group>.short`).
- Anything that cannot be drawn throws `InvalidDataException` naming the group (no fallback to the plain list), reported when the dialog opens.

## Code

- Core: `ModSettingsLayout`, `ModSettingsGroup.Layout`, validation in `ModSettingsSchemaReader`, `ModSettingsTranslation.ShortHeading`.
- Avalonia: `LadderViewModel`/`LadderRowViewModel`/`LadderCellViewModel` in `ModSettingsEditorViewModel.cs`; `SettingGroupViewModel` shows the ladder or the field list; templates in `ModSettingsDialog.axaml`. Rows wrap the existing `SettingFieldViewModel`, so the group reset and Save work unchanged.
- Strings: `ModSettingsLadderSet` (cell tooltip) in every pack.

## Open

- Look and feel is unverified in the running launcher (cell width 64, gold underline for the default); tune after a first look.
- Narrow panes: headings are fixed-width, no wrapping; revisit if a long row label or many columns overflow.
- Keyboard: cells are buttons, so Tab/Space works; no arrow-key navigation yet.
