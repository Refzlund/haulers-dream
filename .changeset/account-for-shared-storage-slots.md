---
"haulers-dream": patch
---

Incoming hauls now account for the same physical storage slots and compatible stacks. A partially occupied multi-stack shelf can no longer promise its last vacant slot independently to different item types. Native hauling claims are published after source reservation succeeds and belong to the actual job activation.

Bulk hauling reserves storage only when the job is active and keeps incoming quantities tied to the actual cargo as it is picked up. Preparing a possible future job no longer claims room for work the pawn has not started.

Bulk pickup keeps every recovered stack fragment tracked even when an item notification throws, while preserving the original error for diagnosis.

Ordinary unloading preserves its exact carried cargo and storage responsibility across saving and loading, including saves made before the new tracking fields existed. Kept inventory and unrelated queued work remain intact.

If an inventory withdrawal callback replaces the pawn's job and then fails, recovery preserves the replacement work and reports the original error with any secondary recovery errors. Failures in the still-current unload job continue through normal game recovery.

After an incomplete shelf delivery, ordinary unloading returns the exact remainder to inventory and checks storage again before continuing. Destination changes during placement now release the reservation for the attempted destination while preserving any newly acquired reservation.

If a shelf's filter changes while a pawn approaches, ordinary unloading returns the rejected cargo to inventory and releases its invalid storage claim. The retained cargo can be delivered by a later unload when valid storage becomes available.

Native haulers keep their full trip budget for nearby duplicate pickups while each pickup respects the remaining shared storage allowance. Saving and loading preserves admitted source quantities separately from the trip budget, restores carried cargo first, and lets existing hauling jobs and their queued work continue.

Native pickup now keeps the exact carried quantity accounted for when an item callback fails after insertion. If that pickup was replaced while its callback ran, its late cargo returns to tracked inventory without cancelling the replacement job. The original failure remains reported, and failures in a still-current job retain normal game recovery.

When a callback merges unrelated items into a native hauler's carried stack, storage accounting keeps the quantity belonging to the actual haul instead of claiming the entire enlarged stack. A replacement job that already owns the carried items keeps them and its reservation.

Ordinary unloading returns tracked cargo safely when its path becomes blocked. If another operation returns that cargo to inventory before cancellation, finish cleanup restores its haul tag and releases the old destination reservation while preserving replacement and queued work.

Storage capacity and maintenance entry points now decline background-thread calls before accessing live game state. Main-thread hauling and saved-job continuation remain active.

If another mod detaches a source stack remainder while a bulk pickup fails, recovery tracks only the actual split cargo. It no longer adopts the detached original remainder as though the pawn had picked it up.

Player-ordered hauling can now take priority over automatic cargo that has not yet been picked up. Existing carried cargo, earlier player orders and explicit shelf reservations remain protected. A bulk haul keeps its unrelated useful pickups and queued work when only one planned parcel loses its storage space.
