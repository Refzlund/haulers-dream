# F07 player guidance review — 2026-09-20

Scope: only the new RIMMSqol paragraphs in README.md, COMPATIBILITY.md, and `.changeset/rimmsqol-drafted-nearby-hauling.md`. This is a source/wording review, not native acceptance.

The setup is consistent with the acquired RIMMSqol 1.6 image `1152B0C1` (assembly 1.0.9591.34971): its shipped SettingsInit.cs exposes `directOrderable`, `canBeDoneWhileDrafted`, and `autoTakeablePriorityDrafted`; English Settings.xml names these **Can Be Ordered**, **Allow Drafted**, and **Autopickable Priority While Drafted**. Its actual Work Givers editor enumerates WorkGiverDefs. The HD provider's priority handling matches native FloatMenuOptionProvider_WorkGivers, including the `-1` sentinel. No new automatic-work behavior is promised or implemented.

Recommended exact corrections:

1. In README and the first COMPATIBILITY paragraph, capitalize the selected entry as **Haul everything nearby**, matching HD's English DefInjected label. Capitalize the setting **Pause while drafted** as well. This is a label consistency correction, not a functional blocker.
2. Replace COMPATIBILITY's delivery/interruption paragraph with:

   > After a drafted sweep finishes gathering, it queues delivery of the gathered quantities to suitable storage while keeping the pawn drafted. Kept quantities and unrelated personal inventory are left alone. If storage becomes unavailable, some gathered items can remain carried for a later suitable unload. This explicit order works with HD's **Pause while drafted** setting; it does not enable automatic hauling for drafted pawns. Cancelling or interrupting the sweep while the pawn remains drafted does not create a new delivery order for it.

   `NearbyHaulDelivery.cs:40–78` queues delivery behind existing work after successful gathering; it cannot guarantee that storage remains usable. `JobDriver_UnloadHauledInventory.Nearby.cs:209–255` returns cargo and can stop after failure/no progress. `JobDriver_BulkHaul.cs:227–241` restricts the interruption rule to a pawn that is still drafted; undrafted completion uses the ordinary unload path.
3. Replace the last COMPATIBILITY paragraph's first sentence with:

   > Turning off **Can Be Ordered** invalidates a waiting command or an already opened menu action. Turning off **Allow Drafted** does the same while the pawn is still drafted.

   `NearbyHaulCommand.cs:36–57` applies drafted permission only when `pawn.Drafted`; direct-order permission applies in either state. Retain the existing priority guidance.

For the changeset, use the same conditional delivery/interruption scope, for example: “Successful drafted sweeps queue delivery of their gathered quantities to suitable storage, preserving kept items and personal inventory. Waiting orders recheck permissions, and interrupted sweeps do not create a delivery continuation while the pawn remains drafted.” The existing save-continuity sentence describes intended implemented behavior (`NearbyDeliveryTransit.ExposeData`, plus saved job/cargo references), but should only become an accepted release claim after the already-planned actual fresh-process restart succeeds. No additional scenario is requested by this review.

No product defect was identified in this bounded review. Native editor, command, and save acceptance remain separate from these wording corrections.

## Root wording correction accepted

Re-read the changed README/COMPATIBILITY hunks and complete changeset after root's edit. The exact label capitalization, storage availability qualification, interruption scope, and separate drafted/direct-order permission wording above are now present and accepted as source-accurate player guidance. The changeset retains “An active delivery retains its exact cargo across saves”; that sentence remains pending the planned actual fresh-process restart, not accepted on this wording review. The first native producer's later fixture transition failure does not change this source-only decision or supply missing runtime acceptance.
