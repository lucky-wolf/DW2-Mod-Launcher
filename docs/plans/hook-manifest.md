# Code hooks: remaining work

The feature is built and documented in [../Code Hooks.md](<../Code Hooks.md>). What is left:

- **Warn on a missing author.** A local mod that states no author and was never fetched from the Workshop has no author to group by, so its overlaps show. Consider a launcher note for it.
- **Effect accuracy (a note for later).** Effects are inferred from the patch method's shape, so a prefix that sets state around the call reads as `observes`. There is nothing to do about it now; if it hides real overlaps, an optional author-stated `effect` in the declared file could override the inferred one.
