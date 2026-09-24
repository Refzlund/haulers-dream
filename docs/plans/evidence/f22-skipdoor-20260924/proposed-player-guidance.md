# Proposed answer to the Skipdoor Pathing part of C001

Draft only; not posted. This wording describes the current source-reviewed state and must be updated if native validation or a compatibility guard is subsequently completed.

There are several mods named Skipdoor Pathing. The original [abandoned release](https://steamcommunity.com/sharedfiles/filedetails/?id=2995157602) lists RimWorld 1.4, so it is not the 1.6 compatibility target.

[VPE Skipdoor Pathing Redux](https://steamcommunity.com/sharedfiles/filedetails/?id=3645096243) changes the native movement path without replacing Hauler's Dream's hauling job. We have reviewed its current 1.6 implementation; runtime compatibility has not yet been verified. Its own pawn eligibility rules still apply. Destinations must remain reachable normally, and Hauler's Dream's pickup radii still use physical distance. Skipdoors do not enable hauling between maps or make distant stacks count as nearby.

[Skipdoor Pathing (Unlimited)](https://steamcommunity.com/sharedfiles/filedetails/?id=3605522450) uses a different approach. Its current movement redirection can complete an HD movement step at the entrance, and its drafted teleport continuation replaces the active job with a move order. We cannot currently claim safe unrestricted portal use for HD jobs with that version.

For limited coexistence with Unlimited, use its **Custom JobDef Exclusions (Comma separated defNames)** setting so HD jobs walk normally. Append these names to any exclusions you already use:

```text
HaulersDream_UnloadInventory, HaulersDream_SelfPickup, HaulersDream_OverloadConstructDeliver, HaulersDream_ConstructDeliverBuild, HaulersDream_ClaimFromHauler, HaulersDream_BatchCraft, HaulersDream_InventoryDoBill, HaulersDream_BillPrepGather, HaulersDream_GatherBillIngredients, HaulersDream_BulkHaul, HaulersDream_KeepInInventory, HaulersDream_LoadPackAnimal, HaulersDream_UnloadCarrierInBulk, HaulersDream_LoadTransportersInBulk, HaulersDream_LoadPortalInBulk, HaulersDream_LoadVehicleInBulk, HaulersDream_BulkRefuel
```

That excludes HD's own job definitions; it is not a promise about every other mod's jobs. The setting's behavior was checked in the current Unlimited code, and the resulting HD movement still needs the focused runtime check before we label the combination tested. Use only one Skipdoor Pathing implementation at a time.

Stack gap limits are being tracked separately from this pathing question.
