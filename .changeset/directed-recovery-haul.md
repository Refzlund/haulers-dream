---
"haulers-dream": patch
---

Actively rescue colonists caught in the storage haul loop, instead of only pausing the item.

The previous fix stopped the endless pacing by leaving a repeatedly failing item on the floor for a short while. That worked, but it left the pawn idle and the item unhauled. Now, when Hauler's Dream notices a stackable item's hauls keep failing, it directs the next haul to a storage cell the pawn holds on its own, so no other hauler can fill it out from under them mid carry, and the haul finishes normally. The short pause is kept only as a last resort, for when storage really is unavailable. Other behaviours stay intact, so pawns still grab things on the way and consolidate loads as before.
