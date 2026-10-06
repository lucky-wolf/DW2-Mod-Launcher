# Focus

What's been accomplished on this branch so far - read by `scripts/open-pr.py` as the PR description
(it falls back to the branch's commit subjects when this is empty).

Format: one `- ` line per finished piece of work, below the `---`: a short, user-facing one-liner.
Add the line when you actually finish the work (humans and AI agents alike) rather than trying to
reconstruct it right before opening the PR. Fold several small related lines into one when a theme is done.

`scripts/new-branch.py` empties the list when it creates a new branch.

---

- Mod properties / publish dialog: new "Get from Steam" button pulls the item's current Steam description into the description field, and an "Open Steam Page" button opens its Workshop page.
- The long (Steam) description is now always `description.bbcode` in the mod folder: one description box in the properties/publish dialog, legacy mod.json `description`/`descriptionFile` migrate into the file automatically (the file wins on conflict), an "open in editor" button, and external edits show up in the dialog live.
- Edit properties dialog: Bundles and Injected DLLs sit in collapsed expanders unless the mod uses them; scroll bars are now always visible at full width (no hover-to-expand) so they are easier to grab.
- Publishing an update asks Steam for the current description: the "replace the Steam description" option only appears (and is pre-checked) when your description differs from what is on Steam, with a note saying so.
- Mod settings schema: any root key holding an array of fields is now a group (its own headed block in the settings editor, own two-column grid); `fields` stays the headingless default. `localOnly` is renamed `hidden` (old name still works) and hidden fields now stay in their group instead of collecting under a separator.
- Mod settings editor: group headings sit on the same line as their separator, and local mods get a small "Show hidden" checkbox in the footer; hidden fields are only shown while it is ticked.
- Mod settings editor: every field with a schema default gets a small undo-icon "reset to default" button (tooltip shows the default; enabled once the value differs).
