---
"haulers-dream": patch
---

**Bulk hauling: the second order now takes over even after the first item is in hand, big stacks ride your inventory, and a one-click "Haul everything nearby" option.**

- **Second nearby haul takes over the sweep immediately — even mid-carry.** With bulk hauling set to "only when a
  second item is tasked" (the default), ordering a *second* nearby haul makes the pawn switch to the bulk sweep
  right away (loading the nearby items into its inventory for one storage trip) instead of carrying the first
  item to storage solo and coming back. This now works even when the pawn has *already picked the first stack
  into its hands* — previously the takeover silently did nothing in that case. Works with plain or shift-queued
  prioritize orders; the first item is folded into the one trip; unrelated queued work is preserved; a third or
  fourth nearby haul folds into the same trip.

- **Oversized stacks are carried in your inventory, not left behind.** When you order a haul of a stack bigger
  than the pawn can hold in its arms (e.g. 75 steel when it can only carry 72), it now routes the whole stack
  through the (mass-limited) inventory and delivers it in one trip, instead of hand-carrying a partial 72 and
  leaving the rest for later. The amount is clamped to the destination's real free space so nothing is stranded.

- **New "Haul everything nearby" right-click option.** For a hauling-capable colonist, right-clicking a
  haulable now offers "Haul everything nearby" alongside the vanilla "Prioritize hauling" — a one-click bulk
  sweep, so you don't have to prioritize two hauls just to trigger it. It always starts a bulk sweep, including
  when shift-clicked to queue it (previously a shift-clicked / repeated click whose neighbors were already being
  swept could degrade into a plain single haul).

Two new mod options (both on by default, under bulk hauling): the "Haul everything nearby" right-click option,
and routing oversized single stacks through the inventory.
