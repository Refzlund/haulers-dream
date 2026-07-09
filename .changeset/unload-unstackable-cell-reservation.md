---
"haulers-dream": patch
---

Fix colonists endlessly pacing when unloading unstackable items (extracted organs, body parts) from inventory in a crowded hospital or prison.

The unload driver dropped the destination cell reservation for ALL items when Haul To Stack was on — including unstackables like kidneys, which can never share a cell. Without the reservation, another hauler could fill the same cell mid-carry, invalidating it, causing the carry toil to fail and drop the item at the pawn's feet. The item was then re-scooped and re-unloaded, creating the reported endless pacing loop. Stackable items (hemogen packs) were unaffected because the in-flight re-route layer actively redirected them — but that layer deliberately skipped unstackables, leaving them with zero protection on the unload path. The unload driver now reserves the destination cell for unstackables, mirroring the guard the vanilla HaulToCell patch already applies. Also fixes a NullReferenceException in the cannot-unload alert when a scanned item is destroyed mid-scan.
