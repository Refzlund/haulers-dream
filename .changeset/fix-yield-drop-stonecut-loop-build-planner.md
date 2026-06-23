---
"haulers-dream": patch
---

Three fixes:

- Pawns no longer drop their gathered crops, milk, or wool on the ground when they start their next job (issue #62). Hauler's Dream scoops those yields into the pawn to carry to storage, but the game's own "drop unused inventory" routine was throwing them on the floor once a colony had been running for a while (which is why it showed up on established saves but not a fresh test colony). Hauler's Dream now keeps the items it scooped until they're hauled to storage.

- Fixed an endless back-and-forth where a pawn with a bulk stone-cutting bill (e.g. the "Bulk Stonecutting" mod) kept carrying chunks between the stonecutter and storage without ever cutting them (issue #63). Hauler's Dream no longer reroutes bills whose ingredients can't stack (stone chunks), leaving them to the game's normal one-at-a-time gathering, which builds them correctly.

- Fixed the construction route planner leaving blueprints stuck and unbuildable until they were cancelled and re-placed (issue #64). Planning a build-order route was over-reserving materials across many blueprints, which made the game think they were already fully supplied so no one would build them. Route and player-prioritized deliveries no longer do that extra batching (automatic construction hauling still batches as before).
