---
"haulers-dream": patch
---

Close three more gaps around the "Pick up X" and "Keep X in inventory" orders.

"Keep X in inventory" now works on items stored inside container buildings, like the egg box or a storage mod's containers. Those items showed no right-click option at all before, even though the same items on a shelf could be kept. The pawn walks to the container and takes the item straight out of it.

When several different things lie under the cursor, each one now gets its own "Pick up" and "Keep" entry instead of only the first. That matches how the game lists one "Prioritize hauling" per thing.

"Haul everything nearby" ordered on a corpse headed for a grave used to quietly downgrade to a single hand haul with no sweep. It now pockets the corpse and sweeps the surroundings like the order promises, and the unload delivers the body to the grave.
