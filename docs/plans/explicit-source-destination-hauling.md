# Explicit source-to-destination hauling — C425-S03/S04

Status: source/design investigation only. Required for AC-L46-C425-S03, AC-L02-C425-S04 and AC-L46-C425-S04 in the same PR. Root read the complete independent recommendation236E8F6732994FDD2473267702B1BE558715D0016BB6EBC7A22F54BD72B5479C in TEMP/haulersdream-explicit-haul-design-independent-20260908. Its51 source/image inputs and89 preservation checks are not implementation or gameplay acceptance. The root integration work owns implementation and subsequent independent QA.

## Existing behavior and recurrence boundary

Route start/end markers select work-stop order. RouteExecutor discards the planner's storageCell as delivery authority, and the resulting work-giver jobs choose their destinations. Nearby hauling and generic inventory unloading likewise choose automatic storage. None satisfies an explicit selected-shelf request.

Actual native HaulToCell can count capacity across the shelf's linked StorageGroup, collect duplicate sources and reroute delivery to other storage. Its ordinary placement toil therefore cannot preserve the new contract merely by supplying an initial destination. Actual native placement can partially top up a stack and then return false for the remainder; false is not evidence of zero delivery.

This is a requested feature gap, not an established regression of an earlier exact-shelf feature. The L02 history still requires same-tick capacity and native counting integration tests after the earlier failed fixes. C425 does not independently prove failure of the later capacity rewrite. Keep those recurrence claims distinct.

## Intended order and execution contract

- Offer a concrete-source **Haul to…** action, quantity selection and point/shelf targeting. Cancelling any UI stage creates no job, reservation or cargo. Capture the actual actor/source and queue intent, and revalidate before synchronization and execution. Preserve the nearby command's supported robot role/capability gates without requiring a different automatic scheduler or Biotech controller.
- Save one small explicit order in the pawn's existing HD component: deterministic per-pawn order ID, actor/map, original source identity, total requested amount, destination discriminator and exact shelf/cell identity, delivered amount, owned cargo slices, current job link, state and reason. Separate immutable order intent from the current trip's source, cell and count. Do not treat driver fields or a pooled Job reference as durable authority.
- A point order retains one exact cell. A shelf order may use another eligible cell on that same shelf, including after a first cell fills, but never a linked sibling or replacement at the same coordinate. Moving/despawning/minifying the shelf pauses the order while retaining identity. A changed map cannot silently retarget it.
- A requested amount is the total delivery. A100-unit order with35-unit carrying capacity needs repeated bounded trips. Source growth never increases the request; insufficient/replaced source produces recorded partial progress and a useful reason. Do not substitute unrelated same-def stock.
- Use a dedicated JobDef/driver with opportunistic collection disabled and identity-aware deduplication. Native default JobIsSameAs ignores count. StartJob creates a fresh driver on resumed starts, and queued jobs do not preserve cached driver fields. Restore order links against actual current/queued jobs on load; ambiguous links remain paused.
- Admission runs only for the verified current job/driver, not speculative or queued pre-toil probes. Preserve queued, active, suspended, blocked, completed and cancelled states. QueuedJob cleanup bypasses driver cleanup, so cancellation/queue-clear handling needs a narrow lifecycle integration. A cancelled order must not silently restart. No automatic tight retry loop.

## Physical delivery and shared capacity prerequisites

Start with bounded hand-carry trips to avoid personal inventory merges. Record the actual pickup split, carrying owner, placement callbacks, resulting stacks and remaining units. Supported interruption can drop/merge carried stock; preserve actual identities through that native path before claiming resumption works. Scanning for a matching def is not custody evidence. Cancellation releases future claims and explicitly transfers any actual remaining cargo to recovery, without recording it as delivered to the selected destination.

For a shelf, use canonical shared storage accounting while restricting eligible resources to the selected concrete shelf's current cells and parent identity. This avoids separate competing ledgers and does not turn shared settings into permission to deliver to another shelf. Preserve concrete fixed/effective filters, provider acceptance, directional resident.CanStackWith(incoming), current blockers/reach/faction/forbid and explicit HD storage denials. Do not infer all these from IsGoodStoreCell or same-def capacity arithmetic.

A bare non-storage point needs a small explicit physical-cell observation/admission path. The current group-only StorageResourceProjector cannot accept a fake/null SlotGroup. Reuse the existing physical slot/top-up representation and dependency guards, omitting only settings that do not exist on a bare cell. A storage point still honors its real filters while remaining restricted to that cell.

Allocate actual vacant slots and resident deficits against native and HD cargo before pickup, with source reservation and destination allocation reconciled around callbacks. Forced native reservation can steal and interrupt another pawn's job; existing forced bypasses are not evidence of available capacity. The shared admission owner needs an explicit no-steal or accounted-handoff policy. Do not temporarily change another live job's flags to bypass it.

Use the actual count-limited Direct carry-drop operation through a controlled toil, retaining native notifications and avoiding generic Toils_Haul fallback. Revalidate destination and allocated units before placement. Join actual callbacks, receiver identities and before/after quantities; release/decrement claims by accepted units exactly once. For example,7 requested with3 available may become3/7 delivered and4 remaining at a full shelf. It remains incomplete and must not send the remainder elsewhere or double-credit it on retry. Exceptions retain actual partial progress and the primary failure, without assuming rollback of an already performed transfer.

## Required acceptance

| Group | Discriminating evidence |
|---|---|
| Actual UI and point | Choose7 of15 units and a non-storage cell;8 stay at source and7 arrive exactly there. Cancel each UI stage independently. Verify quantity limits and nonstackables. |
| Exact shelf | A farther/lower-priority selected shelf wins over automatic alternatives. Another eligible cell belonging to that same selected shelf may be used; a linked sibling shelf may not. Full, forbidden, destroyed and same-coordinate replacement cases preserve identity/progress. |
| Partial placement | Resident72/75 and incoming7 with no vacant slot: native partial placement delivers3, retains4 and can later finish without duplicate credit. Retain callback/owner evidence even when the native return is false. |
| Same-tick competition | Five actors and3 free units, including explicit/automatic HD/native mixes and different defs competing for a vacant slot. Exercise forced handoff and actual reservations/claims. |
| Queue and lifetime | Different amounts at the same targets; queue clear before first execution; replacement; suspension before pickup, while carrying and after partial placement; saved queued/suspended orders; full restart. Fresh drivers retain the same order authority. |
| Cargo and failure | Cleanup drop/merge, another consumer, transfer failure, cancellation with cargo, death/despawn/map exit. No loss, double accounting, stale claims, unrelated-stock subtraction or restart after cancel. |
| Supported integrations | Actual native/linked shelf and supported ASF/minified/quantity-rule profiles, with quality/stuff/HP and provider dependencies. Follow existing guard/allocator work; do not claim support from native-only results. |
| Actor, synchronization and rendering | Human and hauling robot; incapable/specialist controls; selection changes during targeting; queued input; actual supported Multiplayer replay and rendered long translations/progress. |

Implement the saved intent/lifecycle boundary, controlled physical trip, shared admission and bare-cell observation as separately reviewable slices. Every relevant actual case must join command, current job/driver, pickup, delivery and cleanup under the tested product/provider build. Existing route/projector/control evidence is reusable only within its original scope. No criterion is closed by this design.


13 September — Source joins for the implementation boundary.
Root compared the currently read storage/admission sources with the exact425-input candidate used by HD995694/Core9F6A. StorageResourceProjector AB91C59B, StorageProjectionModel2DA7A795, HaulOrderGate6C36248D and InventoryDropCommand3FB2C4F6 match their selected copies. NearbyHaulCommand differs from the working tree; root therefore read the complete selected1955AE00 copy from the product build before drawing these integration conclusions.

The selected nearby CanOffer still requires TryFindBestBetterStorageFor. The explicit-destination action must not use that automatic-storage query as its admission gate: a chosen lower-priority shelf or valid bare ground cell can be the player's intended destination. Reuse appropriate actor/source permission checks while separately validating the exact requested destination. The selected identified-order predicate lists only the existing four JobDefs, and CanContinue enforces its per-instance workgiver/map/manifest identity; a new dedicated explicit-order job needs its own deliberate authority and persistence integration, not an accidental nearby marker or assumed queued-job admission.

The current projector is explicitly an unconnected observation API. Open accepts actual SlotGroup or StorageGroup, canonicalizes linked storage, and rejects a null group; ObserveCell resolves a real member. It cannot already certify arbitrary bare-cell delivery. Exact-shelf observation must retain the concrete selected parent while using the canonical group only for shared topology/accounting, and bare cells need the separately planned physical observation path. These are source-backed implementation requirements, not newly executed delivery evidence or feature completion.
