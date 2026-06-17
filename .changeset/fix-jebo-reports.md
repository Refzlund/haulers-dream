---
"haulers-dream": patch
---

Fixes for reported issues:

- **Batched crafting now sets the ingredient on the table.** When a pawn ran a *batched* production bill (butchering, stonecutting, cooking, drug lab, etc.) it crafted the item straight out of its inventory and never placed the corpse / chunk / ingredient on the worktable. It now carries each ingredient to the bench and sets it down before working — matching vanilla, across every batched recipe — and the placed ingredient is reserved so another colonist can't grab it mid-craft. The whole-batch single gather trip is preserved.
- **Explicit Strip orders are honored regardless of the per-pawn "Auto-haul yields" toggle.** A pawn with that toggle off now still scoops and hauls the gear from a Strip order you give it; the toggle continues to govern only autonomous yield scooping.
- **Clearer strip settings.** Relabeled the auto-strip controls so "never" plainly means "don't strip *while hauling*", with cross-references making it obvious that manually-ordered strips still scoop and haul their gear via the separate "Stripping — removed gear" toggle (the two were always independent; the old labels implied otherwise).
