---
"haulers-dream": patch
---

Ordinary refuelling can use allowed fuel already in the pawn's inventory and gather only the remaining amount from the ground. Kept supplies, shared inventory reserves and specialized refuelling rules remain protected.

Refuelling tracks the exact fuel consumed or returned when an item callback fails. Recovery preserves replacement work, refuses to continue an order whose target changed, and offers explicit recovery choices without repeating an uncertain fuel payment. Saved recovery records retain the original item identities and inventory tags.

Restoring a previous unload instruction requires the original refuelling order, the same inventory and current player permission. Recovery no longer revives that instruction after the pawn's work or ownership changes.
