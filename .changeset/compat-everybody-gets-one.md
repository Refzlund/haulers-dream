---
"haulers-dream": patch
---

Fixed compatibility with **Everybody Gets One** (the "Everybody Gets One - Continued" mod): with Hauler's Dream enabled, the mod's custom bill repeat modes ("one per person", "X per person", "with surplus") disappeared from the repeat-mode dropdown, so you couldn't set a bill — e.g. clothing — to "one per person" at all. Hauler's Dream's batch feature rebuilds that dropdown and was fully replacing the vanilla menu, which skipped the hook Everybody Gets One uses to add its modes. Hauler's Dream now surfaces those modes (with their own labels and validity checks) alongside its batch options. It also makes its product-count correction mode-aware so an Everybody Gets One "one per person" bill correctly pauses once everyone has one instead of overproducing, and it leaves those bills' crafting to the other mod rather than batching them.
