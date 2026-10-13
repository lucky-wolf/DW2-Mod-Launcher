# Unpublished-changes indicator

Status: built. Core is `Services/Publishing/PublishedState.cs` (tests: `PublishedStateTests`); the rest is wiring in `MainViewModel`, `ModRowViewModel`, `PublishDialogViewModel`, `ModsView` and `PublishDialog`, with the text in `ModDetails.PublishStatus*`.

## As built

- Records live in `<data root>/PublishedState/<workshop id>.json`, written right after a successful publish (after the id, version and author writes). A failed write is logged and appended to the publish result message; the publish itself still counts.
- Statuses: unpublished (local mod, no Workshop id), not tracked (published before this feature; tracking starts at its next publish), published <date> (the time too if it was today), modified. Workshop copies show none.
- List: a gold dot after the name, only for modified. Details pane: a short "Status: ..." line whose tooltip has the sentence and up to 12 changed files (`+` added, `-` removed, `~` modified). Publish / edit-properties dialog: the same status and sentence at the top, with the files listed.
- The selected mod is compared again each time its details are shown, so edits made while the launcher is open appear without a refresh; the whole list is compared on each scan.
- Steam status comes from one background sweep (`MainViewModel.RefreshSteamStatusAsync`) over every published local mod, at startup and on Refresh, in a single batched `IModPublisher.QueryItems` call (one Steam round trip per 50 items). It never blocks the UI. Each item comes back as a `SteamItemStatus` (today: deleted, and visibility); the same query also carries update time, moderation state and vote counts, so a new fact is one new field there. Deleted items get their Workshop id erased from mod.json; visibility shows as an icon beside the source in the list and a "Visibility: ..." line after the status in the details pane. Selecting a published mod whose visibility the sweep missed asks again for just that item. If Steam gave no answer at all (not running, not logged in) the launcher stops asking until the next Refresh, and the icons simply stay blank. The answers are dropped after a publish or either dialog closes.
- A corrupt record or unreadable folder is logged and shows as not tracked.
- Publish uploads the whole content folder with no exclusions, so the comparison has none either.

## Original design

Goal: for a local mod that has been published, show in the launcher whether its folder has changed since the last publish, so the author knows when to push again.

## Design

1. **Record at publish.** After a successful publish, store a per-file manifest of the content folder (relative path, size, content hash, and the mtime seen). Keep it in the launcher's AppData next to `ModSettings` (`UserDataRoot`), keyed by Workshop id. Never inside the mod folder: it would change the fingerprint, and a Workshop re-sync could overwrite it.
2. **Compare on scan.** For local mods with a record, walk the content folder and compare with the manifest. Changed means a file was added, removed, or has a different hash.
3. **Show it.**
   - A small badge in the mod list (a dot on the name or a narrow column), only for local mods that have a record.
   - A line in the details pane: "Changed since last publish: N files", listing the names. The per-file manifest is what makes the list possible; a single hash could not.

## Details that matter

- **Write order.** Take the manifest after the publish's own writes to the folder (the Workshop id written back by `ModJsonWorkshopIdWriter`, any version bump per `VersionBumpPolicy`). Otherwise every publish looks dirty at once.
- **Cost.** Compare size and mtime first and hash only files where those differ from the stored values, so repeat scans of a large mod (XL, with bundles) stay cheap.
- **What counts.** Hash exactly what is uploaded (`ModPublishRequest.ContentFolder`). If publish excludes anything, the comparison excludes the same, so `bin/` or `obj/` churn does not light the badge. Title, description and preview image changes belong in the fingerprint too (`DescriptionOnly` publishes touch only the description).
- **No record yet.** Mods published before this feature have no manifest. Show them as unknown, not changed; tracking starts at their next publish (no "mark as published" button: not worth the clutter).
- **Description-only publish** (and the visibility box in the dialogs) are metadata conveniences, not publishes: they never touch the record.

## Rejected

- Comparing against Steam's `time_updated` (`WorkshopRemoteDetail.TimeUpdated`): says when it was last uploaded, not whether the folder differs.
- Comparing file mtimes with the publish time: a touch or a rebuild with identical output gives a false positive.

## Where to start

Code touchpoints: `Services/Publishing/` (`ModPublishRequest`, `IModPublisher`, `ModPublishResult`, the publisher classes), `MainViewModel` (the publish call, near `ContentFolder = contentFolder`), `ModSettingsStore`/`UserDataRoot` (storage pattern to copy), `ModInfo` and `ModDetails` (state and details line), `ModsView` (badge), `Languages/*.json` (new strings in every pack; `en.json` canonical).

Order: manifest store and comparison with tests first, then record-on-publish, then the list badge and details line, then the mark-as-published action. Add a line to `docs/focus.md` when it lands.
