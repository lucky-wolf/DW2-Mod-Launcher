# Setting previews

Optional hover images for mod settings, supplied by the mod author. One image per setting is the main case; the author bakes any with/without comparison into that image (e.g. a diagonal split). The launcher adds no split line or overlay.

## Schema

Root `_previews` object in `settings.schema.json`, like `_groups`, `_layouts` and `_enables`:

```json
"_previews": {
  "FuelFirst": "fuel-first.png",
  "Density": { "Sparse": "sparse.png", "Dense": "dense.png" }
}
```

- A string is one image for the whole setting (bool, enum, number).
- A map gives one image per enum option; options without an entry fall back to nothing.
- Values are bare image file names, looked up only in the mod's `_previews` folder; a name containing a path is an error. A missing file is logged and the tooltip says so.
- PNG, JPG or WebP; about 1 MB soft cap; shown at most about 480x320, aspect kept. Recommend 16:9.
- Keys compare ignoring case. No `_previews` means nothing changes.

## Display

- One shared preview popup: image plus the field description as a caption, no focus steal, placement flips at the dialog edge.
- Checkbox, dropdown and radio set: hovering the control, its label or the row shows the field's image.
- Dropdown with a map: hovering a flyout item shows that option's image; the closed box shows the selected one.

## Automatic radio set

- Applies to an enum that has `_previews`, has 4 or fewer options, and no option over about 24 characters.
- Drawn as a full-width block (label and reset on top, `WrapPanel` of radios below), not in the 2-column `UniformGrid`. The group's fields split into blocks: runs of ordinary fields stay in the grid, each radio field is its own block.
- Hint `"_layouts": { "Key": { "kind": "radio" } }` forces radios for 5 to 8 options (wraps to a second line). More than 8 options or very long labels stay a dropdown.
- Booleans stay checkboxes.

## Work

- Core: `ModSettingsSchemaReader` and `ModSettingsSchema` parse `_previews` into `ModSettingsField.Previews` (string or map) and validate paths.
- `ModSettingsEditorViewModel`: resolve field (and option) to a lazily loaded, cached bitmap.
- `ModSettingsDialog.axaml`: popup, hover handlers, radio block template, block splitting of groups.
- Tests in `ModSettingsSchemaReaderTests`: string and map forms, path escape, missing file, key case.
- Check the Workshop publish dialog lets `previews/` through.
- Author docs section and a changelog line.
