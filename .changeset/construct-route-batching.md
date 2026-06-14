---
"haulers-dream": patch
---

**Fix "plan construction" pawns topping off at the stockpile after every single wall.**

When you planned a construction route over a wall line, the pawn would pick up a big load of
material, build **one** wall, walk all the way back to the stockpile to top off, build **one
more** wall, and repeat — a pointless shuttle that defeated the whole point of carrying a batch.

Two underlying causes are fixed:

- The inventory-delivery driver decided whether to walk back to the stockpile by comparing what it
  carried against the **whole route's** remaining demand. Since a single carry can never hold an
  entire wall line, and the mass headroom reopened after each wall was filled, it tripped back to
  the stockpile after **every** deposit. It now decides based on the **immediate** frame's need:
  while the pawn still carries enough for the wall in front of it, it builds straight from
  inventory and only returns to the stockpile when it genuinely runs low — roughly one trip per
  carry-load instead of one per wall. When it does re-load, it still fills to its full smart-carry
  ceiling, so the "few trips" benefit is preserved.

- For walls that need **more than one material** (e.g. wood **and** steel), only the first material
  was gathered for the whole route; the others were re-fetched one wall at a time. The build tether
  now carries the whole route's remaining demand for every material, so steel/components batch the
  same way wood does.

Haul-only routes, the "haul materials to site" order, plain right-click "prioritize constructing",
and single large deliveries (e.g. a 340-steel generator) are unchanged. No save-compat impact.
