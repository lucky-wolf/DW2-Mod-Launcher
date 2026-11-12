# Mod catalog (browse and install mods outside Steam)

The launcher reads a public `index.json`, rebuilt daily by a GitHub Action in the catalog repository, and lists the mods in it. Players without Steam (Linux, other stores) can install and update from it. Design and decisions: DW2-XL `docs/ideas/Mod Release Catalog.md`. Last updated 2026-11-12.

## Next session handoff

1. Read this repo's AGENTS.md (strong rule), then `docs/Localization.md`.
2. **State at the end of 2026-11-12:** mod identity (UUID) is built and deployed (751 tests pass); every mod the owner publishes has a `uid` and was re-released with it; the catalog code (`build-index.py`, `pointers.json` with XL's `uid`) is pushed. Next: dependencies and settings, publishing from the launcher, signing; see "To do".
3. **Nothing below has run live yet:** the owner should try one catalog Download and one Update (installer, `uid`/`author` check, work-folder swap), the Steam subscriber sort, Create Mod and Publish stamping a `uid`, and Make This a New Mod. Report what they find; do not assume they work.
4. Owner items, not for you to chase: the owner re-released every mod with its `uid` (2026-11-12) and re-indexes with `publish-catalog.py` (which only re-indexes; `release-mod.py` makes releases, separately). Never run either, push, or trigger the workflow unless told it is time. Expected index: 19 mods, all with a `uid`, no "rejected"/"refused" lines in the workflow log.
5. Finish with `python scripts/build.py`; add a launcher `changelog.txt` line for user-visible work.

## How it works today

- **Releases repo** `lucky-wolf/Distant-Worlds-2-Mods` (public; topics `dw2-mod`, `distant-worlds-2`, `mods`). DW2-XL's `scripts/release-mod.py` makes one release per mod version: `<tag>.zip`, `<tag>.zip.sha256`, `<tag>.manifest.json`, and the preview `<tag>.preview.<png|jpg>`. Tag is `<Mod-name>-v<version>`. 18 releases exist: every published code mod and translation pack except XL. For an existing tag it adds a missing preview or manifest and replaces an out-of-date manifest (`backfill_assets`). A folder counts as a release only when every file matches the launcher's `PublishedState/<workshop id>.json` record; a differing folder is work in progress and is skipped.
- **Two separate steps for the owner:** DW2-XL `python scripts/release-mod.py --repo lucky-wolf/Distant-Worlds-2-Mods --execute` makes the releases (without `--execute` it only reports); `python scripts/publish-catalog.py` only starts the catalog's workflow (`gh workflow run catalog.yml`) and waits for it (`gh run watch`; `--no-watch`). Neither runs the other (they were one script until the owner asked for them to be split, after a day of doubled runs; repeating a release is harmless, since an unchanged release is left alone, but the first re-release of a changed one deletes and recreates it, which resets its download count). `publish-catalog.py` warns about unpushed catalog changes (the workflow runs what is on GitHub). Its name is now misleading; renaming it to something like `reindex-catalog.py` is an open question below.
- **Catalog repo** `lucky-wolf/Distant-Worlds-2-Mod-Catalog` (public). `build-index.py` + `.github/workflows/catalog.yml` run daily (05:17 UTC) and on demand, and commit `index.json` only when more than its timestamp changed. Discovery: every GitHub repo with the topic `dw2-mod`, plus `repos` in `pointers.json`, minus `blocked`. A release counts only if it has a `<tag>.manifest.json`; download counts and dates come from GitHub. GitLab projects are listed by hand in `pointers.json` (`gitlab`), the pointer carrying what a manifest would (`uid` and `author` required, `kinds` required, `tags`, `previewUrl`, `shortDescription`, `workshopId`, `dependencies`); GitLab has no public counts, so those are null. `pointers.json` also has `approved` (`{"<uid>": "owner/repo"}`, an owner's deliberate move of a mod to another repo).
- **One listing per `uid`**, from the repo that owns it (see 1 below); within that repo the most recently published release wins. A GitLab zip is downloaded and hashed once per tag (the hash is carried over from the last index). The catalog repo's README tells authors about the `uid` and `author` requirement.
- **XL** is listed from GitLab (`coyote-wolf/DW2-XL`, `kinds: ["xml"]`, `previewUrl` into the XL repo, unversioned so a changed image shows stale from the launcher's cache) because `release-mod.py` skips it while its folder differs from the published record. Its `uid` (`363fba16-301d-41d4-979e-3432c10cbd59`) was written by hand into both `XL/dw2modlauncher.json` and the pointer so they agree; XL has not been published through the launcher since, and the launcher keeps an existing `uid` when it does. The old GitLab zip has no `uid` inside; installing it is accepted and matched by Workshop id. Once XL has a GitHub release, that listing carries the same `uid` and the GitLab pointer can go.
- The launcher fetches `CatalogClient.DefaultUrl` (a raw GitHub address, no account). Nothing depends on a server of ours.

### index.json (schema 1)

`{ "schema": 1, "generated": "<utc>", "mods": [ ... ] }`, sorted by name. Each entry:

- `id` (a label, also the release file names), `uid` (**required**: the mod's identity, a lower-case hyphenated UUID, unique in the index), `name` (the mod's folder name), `version`, `author` (**required**), `shortDescription`, `workshopId` (number or null), `minLauncherVersion`, `dependencies` (`mod`, `workshopId`, `required`).
- `kinds`: non-empty, any of `code` (a .dll beside mod.json), `xml` (any .xml file), `translation` (a `language` in dw2modlauncher.json or a `translations/` folder), `font`. A mod may have several. Detected by `release-mod.py` from the published files.
- `tags`: the author's topics from `tags` in dw2modlauncher.json, lower case, single spaces (`CatalogTags.Normalize` and the same rule in `release-mod.py`). May be empty.
- `size`, `sha256` (of the zip), `downloadUrl`, `releaseUrl`, `previewUrl` (null when none), `publishedAt`.
- `downloads` (total over all releases of the mod), `peakDownloads` (unused by the launcher now), `latestDownloads`; all null on GitLab.
- `repo`, `tag`, `zip`. Zips hold one root folder named like the mod with the published files, plus `dw2modlauncher.hooks.json` when present.

## Built in the launcher (751 tests pass)

[src/DW2ModLauncher.Core/Services/Catalog/](../../src/DW2ModLauncher.Core/Services/Catalog/); tests in `CatalogTests`, `CatalogBrowseTests`, `CatalogInstallerTests`, `LauncherMetaTagsTests`.

- `CatalogParser`: strict schema 1 reader; a bad entry fails the read naming entry and field. An entry without `kinds`, `author` or a valid `uid` is rejected, as is a repeated `uid`.
- `CatalogStatusFinder`: the `uid` is the identity: an installed mod with the entry's `uid` (a Steam subscription means "from Steam", nothing to offer; a local one means up to date or update available), whatever it is called. A mod with a different `uid` is a different mod. Only installed mods with no `uid` yet are matched the old way (Workshop id; else same-named folder).
- `CatalogClient`, `CatalogTags` (normalization and the `Suggested` starting vocabulary), `CatalogPreviewCache` (previews cached under `<data root>/Catalog/Previews/`, max 8 MiB, 4 at once).
- `CatalogInstaller` + the Download / Update button on each row (`CatalogViewModel.InstallAsync`): refuses while the game runs or when `minLauncherVersion` is above the launcher; confirms (source URL, size, author, and for a code mod that it runs on the player's machine); downloads to `<data root>/Catalog/Downloads`, checks size and SHA-256; unpacks to a stage folder; copies into the mods folder. The new mod is first copied completely into a `.dw2ml-install-*` work folder beside the target (the mod scanner skips `.dw2ml-` folders); only then does an update send the old folder to the recycle bin and rename the work folder into its place, so any earlier failure leaves the installed copy untouched. The folder name is kept, so the mod stays enabled. Accepted zip layout: `mod.json` at the top, or exactly one root folder holding it. Entries with backslashes, absolute paths, `..` or `:` are refused; a fresh install never touches an existing folder of that name. **After an install or update the launcher stamps the entry's author into the mod's `dw2modlauncher.json` when the mod names none** (`LauncherMetaWriter.StampAuthor`, no overwrite).
- **Browse Mod Catalog window** (`CatalogDialog`, from the toolbar button at the right end of the Mods list): fetches on open; search box; kind, tag and sort drop-downs; reload; one list whose rows show a 120x80 thumbnail, name, version, kinds, author, tags, description, install state, size, popularity, date; "Open release page". A failed fetch or bad entry is shown in the list area and logged; a missing or failed image is an empty slot, never another image. Keys are `Catalog*`, `PickTag*`, `PublishTag*`.
- **Sorts:** *Most downloads* (default; `downloads`, total over all releases ever, no figure last), *Most subscribers* (only offered once Steam has answered, then the default unless the player already chose; Steam's unique-subscriber count from the batched `QueryItems`, asked for entries with a Workshop id, no figure last), *Recently released*, *Name*. Each row shows subscribers when known, else downloads, labeled. Known skew: every update adds its own downloads, so a mod with many releases outscores one with few for the same players.
- **Tags for authors:** Edit Properties / Publish shows the tags as chips and a `+` opening `TagPickerDialog` (known tags = `CatalogTags.Suggested` + installed mods' tags + the loaded catalog's). Saved only when changed (`LauncherMetaWriter.SetTags`).
- **Mod right-click menu** (reorganized 2026-11-12): Configure, View Readme, Open Steam Page; Open Mod Folder, Code Hooks Report; Check Updates and Unsubscribe (Workshop mods only); Delete Mod (any local mod, the catalog's counterpart of Unsubscribe; still only enabled inside the managed mods folder); a **Mod Author** submenu (Edit Properties, Add translation, Rename, Make This a New Mod, Publish, Mark Up to Date) for the player's *own* local mods only (`SelectedIsOwn`: author blank or equal to the Author setting). A catalog download by someone else shows no Author submenu.
- **Authorship rules:** Publish refuses when the Author setting is blank (`PublishNeedsAuthor`), so everything published carries an author; the setting defaults to the logged-in Steam account name.
- Dialog title is "Browse Mod Catalog" (`CatalogTitle`); ja/ru for changed keys are deleted for the release translation pass, new keys are English only.

## To do

### 1. Mod identity (UUID): built 2026-11-12, not yet tried live

**Why.** Identity was the Workshop id, or for a mod without one its name; the catalog's `id` came from the tag text. A rename split one mod into two listings, mods by authors and players without Steam had no stable identity, and "newest release per `id` wins" let anyone tag a release with the same name and take over a listing.

**What exists.**
- `uid` in the mod's `dw2modlauncher.json` (never `mod.json`): a random UUID, written by `LauncherMetaWriter.StampUid` (`ModUid` canonicalizes). Read into `ModInfo.Uid`; one that is not a UUID reads as none.
- **Stamped only at the origin:** Create Mod (so a new mod has one from its first moment) and Publish (a mod made before identities existed gets its one at its first publish from the new launcher; a malformed one is reported and the publish stops). **Never by scanning**, and not in bulk for existing mods: stamping would make every published mod look modified. A downloaded mod keeps the `uid` it came with; one with none stays without.
- **Make This a New Mod** (Mod Author submenu): a fresh `uid` for a copy going its own way.
- **Duplicate detection** (`ConflictAnalyzer.AnalyzeEnabledCopies`) flags enabled copies by `uid`, then by Workshop id.
- **Catalog matching** by `uid` (see above). `release-mod.py` (DW2-XL) writes `uid` into the manifest and **skips, with a warning, any mod with no valid `uid` or no author**. `build-index.py` rejects (stderr) any manifest or GitLab pointer without a valid `uid` and an author, keys listings on `uid`, and **pins each `uid` to the repo that owns it**: the repo in `pointers.json` `approved` (`{"<uid>": "owner/repo"}`), else the one the last index listed it from, else the one that released it first. Another repo's release of that `uid` is refused and reported; if the owner gave nothing this run, its last listing is kept. Tested offline with a mock (earliest claimant, pinned, approved move). Live (2026-11-12): the owner re-released the 17 mods that have a `uid` (XL excepted) and re-indexed; the expected index is 19 mods, all with a `uid`. Confirm that from the workflow log ("rejected"/"refused" lines) before relying on it.
- **Install check:** before anything in the mods folder is touched, the unpacked mod's own `uid` and `author`, when it states them, must match the catalog entry's (`CatalogInstaller.VerifyIdentity`); a mismatch or unreadable dw2modlauncher.json refuses the install and names both values. A mod that states neither (released before identities) is accepted and stays without a `uid`, matched by Workshop id.
- The Workshop id stays as an attribute (Steam operations, `PublishedState`, subscriber counts, the game's dependency resolution).
- **Compatibility:** launcher 1.0.39 (no catalog, no `uid`) ignores the new key (`System.Text.Json` skips unknown members, and nothing is set to reject them); the game reads only `mod.json`. The catalog and its stricter index format are Unreleased, so no released launcher reads the index.

**Limits, stated plainly.** Pinning by repo stops casual takeover of a listed mod. It does not stop someone claiming a `uid` before its owner lists the mod (a `uid` is readable in any public zip), and it cannot tell a hostile move from a real one without the owner approving. Only a signature proves authorship; see 4.

**Not done.** Dependencies in `dw2modlauncher.json` should gain an optional `uid` beside `workshopId` (with the dependencies feature, 3).

### 2. Publishing from the launcher (replaces `release-mod.py` for authors)

One Publish button does it all: stamp the `uid` if missing, bump the version, build the zip, `.sha256`, `.manifest.json` and preview in the format `release-mod.py` writes today, create the tag and release, upload, and also publish to Steam when the mod has a Workshop id. At that moment the folder is exactly the release, which removes `release-mod.py`'s match-against-`PublishedState` logic.

- **No credentials in the launcher.** It shells out to `git` and `gh` / `glab`, which use the machine's login; if the tool is missing it says so plainly. The author supplies only where to publish: the mod's release repo (host plus URL).
- Discovery needs nothing new: the catalog already finds repos by topic and reads manifests, so a third-party author needs only a repo with the topic.

### 3. Dependencies and settings

- **Dependencies:** offer the entry's required dependencies that are not installed (by `uid`, falling back to Workshop id); optional ones are listed, not forced.
- **Settings:** a catalog address setting (default `DefaultUrl`) for forks and test catalogs; an off switch; a cache of the last good index so Browse opens offline.

### 4. Signing (design, not built)

The proper answer to impersonation (agreed 2026-11-12 that repo pinning is only a partial guard). On an author's first publish the launcher makes a keypair and keeps the private half in the user data folder (ECDSA P-256: in .NET's BCL, and verifiable in the Action with a pip package). The manifest carries the public key and a signature over `uid`, version and zip hash. The catalog pins `uid` to the key on first sight and accepts only releases signed by it; a key change needs the owner's approval in `pointers.json`. The launcher can check the same pin before installing, which also covers a compromised catalog. Cost: an author who loses the key cannot update without an approval, so the key needs a backup story (export/import in Settings).

### Order

1 is done. Next 3 (dependencies, settings), then 2 (publish from the launcher), then 4 (signing).

## Open questions

- Per-mod release repo: in `dw2modlauncher.json` (visible to the catalog, travels with forks) or in launcher settings? Leaning to `dw2modlauncher.json`.
- Steam-only authors: stamp at their first Steam publish (assumed yes).
- Steam users: hide catalog mods that exist on the Workshop, or show them as "from Steam" with a link? `CatalogStatusFinder` already reports `FromSteam`.
- `release-mod.py` no longer invents a "Rebuilt, with no listed changes" note; seven existing releases still carry the old note in their GitHub text. The owner decides whether to edit them.
- Translation packs are tagged `xml` as well as `translation`. If the XML filter should list only mods that change game data, skip `xml` for them in `mod_kinds` in `release-mod.py`.
- Mods by authors who do not publish to the catalog (e.g. Sale's art mods): a pointer can carry their `kinds` and `tags`; no way yet to point at a Workshop-only mod's download.
- Rename `publish-catalog.py` (it only re-indexes now) to something like `reindex-catalog.py`?
- `LauncherMetaReader.Read` turns any parse error into "no launcher file" without a word, so a typo silently drops a mod's dependencies, tags and `uid`; that breaks this repo's fail-loudly rule. Offered to the owner, not changed.
- Catalog update, small remaining window: between recycling the old folder and renaming the work folder into place, a failed rename would leave the old copy only in the recycle bin. A same-volume rename practically cannot fail; restoring the old folder automatically in that case is possible.
- A Tags box in the Create Mod dialog; the suggested vocabulary is a guess and easy to change (`CatalogTags.Suggested`).

## Gotchas learned

- After any `.axaml` edit run `dotnet build src/DW2ModLauncher.Avalonia/DW2ModLauncher.Avalonia.csproj -c Release --no-incremental` and read the output for `AVLN2000`. A XAML compile error leaves a DLL with no compiled XAML, the launcher then dies at startup with "No precompiled XAML found for App", and later incremental builds say "0 errors". `ListBox` has no `HorizontalContentAlignment` (style `ListBoxItem` instead).
- While the owner's launcher is open it locks the DLLs and `build.py` cannot finish: compile into a scratch folder (`-p:OutDir=...`) to check code and run `dotnet test`, never close their launcher.
- When the owner says they must do something first, it has not happened yet.
