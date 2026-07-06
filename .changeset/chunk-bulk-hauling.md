---
"haulers-dream": patch
---

Fixed designated chunks being hauled one at a time under Combat Extended even with Bulk hauling set to Always (issue #124). The Combat Extended guard from the one round at a time ammo fix compared how many units fit in inventory (capped by the stack's live count) against a def level hand armful. With a chunk stacking mod raising the chunk stack limit above one, every lone field chunk (always a 1 count stack) compared 1 against that armful and wrongly declined the automatic bulk sweep, so vanilla hand hauled one chunk per trip. The hands side is now clamped by the stack's live count: a stack that fits whole in inventory is never declined, because hands cannot move more than the whole stack either. Bulky ammo in big shelf stacks still declines exactly as before, keeping the one round at a time fix intact. The explicit haul everything nearby order was unaffected and still sweeps.
