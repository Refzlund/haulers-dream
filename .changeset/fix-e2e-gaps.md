---
"haulers-dream": patch
---

End-to-end polish for the player-feedback features:

- **"Pick up X" now works on any item.** Previously, right-clicking an item already in its best stockpile (or with no accepting storage) showed the option but did nothing. It now reliably picks the stack into the pawn's inventory regardless of storage (matching Pick Up And Haul) — still tracked, so it gets put away later.
- **The per-pawn "Auto-haul yields" toggle is reachable while drafted** (it's a standing preference; a drafted pawn still won't scoop), and it no longer appears on animals that can't be Haul-trained (e.g. cats).
- **Auto-strip respects the toggle** — a pawn with auto-haul off no longer pockets stripped corpse loot.
- **Honest setting tooltips** — the animal-hauling tooltip now states only Haul-trained animals benefit; the mechanoid tooltip names harvest/mine/deconstruct-salvage scooping and points to the inventory-delivery setting; the crafting-share tooltips note the automatic stand-down while Common Sense is active; the carry-limit tooltip clarifies the Pick Up And Haul mass parity.
- Minor allocation cleanup in the bulk-haul work scan.
