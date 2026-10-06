# Launcher: BBCode in the mod description preview

Status: idea, not started. Repo: DW2-Mod-Launcher.

## Today

`ModDetails.BuildText` (`DW2ModLauncher.Core/Services/ModDetails.cs`) strips tags with a regex (`\[/?[^\]]+\]`). The result is one plain string, `DetailText`, shown in a `SelectableTextBlock` (`ModsView.axaml`). That string also carries the tool and document lists, conflict files and Workshop info, not just the description.

## Why

Local mods write their description in BBCode (`description.bbcode`). Rendering it would show the author what Steam will show, and make Workshop descriptions readable.

## Estimate

Small to medium: about half a day for the useful 90%.

1. **Parser in Core (1-2 h).** Pure `BbCode` parser producing a flat list of styled runs, unit tested next to the existing `BuildText_...StripsBbCode` test. Tags: `[b] [i] [u] [strike]`, `[h1]`-`[h3]`, `[url=...]`, `[list]` with `[*]`, `[olist]`, `[quote]`, `[code]`, `[noparse]`, `[hr]`. Unknown or unbalanced tags fall back to plain text.
2. **Renderer in Avalonia (2-3 h).** `SelectableTextBlock.Inlines` is not bindable, so use an attached property or a small `BbCodeText` control that builds the inlines. Bold/italic/underline map to `Run` properties, headings are larger text, links are gold and underlined with a click handler that opens the URL, lists and quotes are indentation plus bullet glyphs. Selection keeps working.
3. **Split description from the rest (1 h).** Expose the description separately from `BuildText` and render only it as BBCode; keep the other sections as plain text. Second view-model property plus a second block in `ModsView.axaml`.

## Costlier, probably skip

- `[img]`: async download, caching, sizing. Another half day. Show a link or placeholder instead.
- `[table]`: needs a real layout grid. Fall back to raw text.
- `[spoiler]`: needs a collapse control. Cheap if wanted, otherwise plain text.
