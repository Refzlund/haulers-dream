---
"haulers-dream": patch
---

**Fix two hauling misses around deconstruction and passing storage.**

- **Deconstruct yields are now reliably scooped.** Materials from a deconstructed building are captured at
  the moment the game places them — wherever they land — instead of only scanning the building's footprint.
  Previously, a leaving that spilled outside the footprint (e.g. a wall hemmed in by a full storage room) or
  merged into a stack already on the ground was missed and left lying around. Now they're picked up like the
  rest of the run's yields.

- **A loaded pawn drops its load when it finishes the run and moves on to other work near storage.** When a
  pawn stops mining/deconstructing/harvesting and picks up unrelated work (e.g. cleaning) while a stockpile is
  reasonably close, it now unloads first instead of carrying the load around. The accumulate-while-working
  behaviour is unchanged — it keeps overloading into its inventory for as long as it's still doing the
  yield-producing work, and only sheds the load once that run is over.
