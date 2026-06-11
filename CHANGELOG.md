# haulers-dream

## 1.0.2

### Patch Changes

- 3be7d1b: Harvest and haul polish, from two independent top-to-bottom review rounds of the feature: pawns now scoop their pending drops and unload in one trip for every unload trigger (previously sometimes two); the unload respects what a pawn is supposed to keep in its inventory (drug policy doses, inventory stock like a doctor's medicine, and packed food), ending a dump-and-refetch loop when a harvest merged into personal stock; arriving caravan pawns no longer stall when their whole load is spoken for by other workers; the fog-extension of planned mining routes no longer dies at the moment it should fire; leavings from an instantly-cancelled frame can't be credited to a bystander; pawns no longer walk to drops that were forbidden in the meantime or left on another map; the yield hook is now immune to other mods nesting item placements; and under Combat Extended, every stack-merge path now keeps CE's loadout tracker in sync, so custom-loadout pawns no longer drop part of their load mid-run.
- 3be7d1b: Pick up and haul polish, from two independent top-to-bottom review rounds of the feature (six reviewers total). Round one: bulk sweeps no longer pull an item back out of storage when you order two hauls in a row (the first delivery stays delivered); a bulk hauler's end-of-sweep unload now waits behind an order you give mid-sweep instead of finishing its storage trip first; under Combat Extended, the unload keeps a pawn's own loadout reserve (ammo, sidearm stock) instead of shipping it to storage for CE to fetch right back; bulk-saturated CE pawns stop their sweep instead of visiting stacks they can't carry; and a set of robustness and performance improvements to the sweep planner (cheap checks before pathfinding, hardened plan cache, safe behavior if a future CE changes its internals). Round two: drafted pawns now stand to orders — drafting a hauler mid-sweep no longer makes it march off to storage afterwards, drafted pawns are never slowed by the overload penalty, and the unload button shows why it's unavailable instead of silently doing nothing; bulk sweeps now respect how much room the destination storage actually has for each item type instead of optimistically loading the full plan; mechanoid haulers no longer get the smart-overload capacity bonus they'd never pay the speed penalty for (all carry paths); forced sweeps planned while the game is paused use fresh numbers instead of a stale cached plan; and a sweep can no longer yank (or steal, on a forced order) a stack another pawn already reserved mid-walk — it skips it like vanilla would.

## 1.0.1

### Patch Changes

- 9af4687: Pick up and haul fixes: forbidding an item mid-haul is now respected (and the unload no longer clears your forbid flag), the default "stay surgical unless a second haul is ordered" trigger no longer counts the pawn's own automatic hauling as an order, sweeps honor the non-home-maps setting, and a stale-plan edge case after quickloading is gone.
- 9af4687: Fixed material deliveries to blueprints. Inventory deliveries for big builds and claim-from-hauler handoffs failed with red errors on the first delivery to any new blueprint (the geothermal-generator case): the load arrived but could not be deposited. Deliveries now convert the blueprint to a frame exactly like vanilla and deposit cleanly, including multi-trip loads.
- 9af4687: Fixed a "started 10 jobs in one tick" error loop in Haul to stack when haulers with different allowed areas worked the same items: a stack-destination computed for one pawn could be handed to another pawn it was invalid for. Destinations are now computed per hauler, and large colonies with many same-priority stockpiles find partial stacks more reliably (the scan starts in the right storage group instead of burning its budget elsewhere).
- 9af4687: Planned crafting now respects the bill's own settings. A fresh bill defaults to Do x1, so a planned batch of 10 gathered ingredients for all 10 but crafted only 1 and hauled the rest back. The planner now caps the batch by the bill's remaining repeat count (and tells you that's the limit), and suspended or paused bills can no longer be ordered.
- 9af4687: Full-mod audit sweep, smaller fixes: carried medicine now respects the patient's medical-care restriction; turning a work override off now takes effect immediately instead of after a reload; cancelled deconstruct orders can no longer be revived by route planning; the route must-include picker can now select filth and blueprints, and Escape exits picking mode; vein-mining routes can't extend onto another map; ordered construction routes no longer re-gather materials they already delivered, and multi-material sites keep their build step; small ordered hauls to nearby clusters keep vanilla's efficient batching; the overload slowdown is consistent across strict mode, the slider and Combat Extended; haulers no longer grab stacks a worker has reserved from a carrier's inventory; plus a batch of text and tooltip corrections.
- 9af4687: Strip fixes: the follow-up strip after stripping a living target now actually fires (it previously never ran due to a job-lifecycle quirk, which also suppressed the vanilla "stripped" tale on every strip - both restored). The tainted-apparel Destroy policy can no longer destroy quest items or relics (they are taken instead), smeltable classification now matches the smelter's real rules, and corpse stripping respects the non-home-maps setting.

## 1.0.0

Initial release. Colonists use their inventories smartly when moving items, and carry out their tasks more efficiently: fewer round-trips, less walking back and forth, more time actually working.

### Smart inventories

- **Harvest and haul**: work yields (plants, mining, deep drills, deconstruction, animals) are scooped into the worker's inventory as it works, then delivered in one storage trip at the end of the run. Realistic by default: the yield hits the floor first, then gets scooped. Per-work-type toggles.
- **Pick up and haul**: a pawn sent to haul one item sweeps everything haulable around it into its inventory and delivers the lot in one trip, planned the moment the haul starts. Two modes: every haul sweeps, or (default) manual orders stay surgical unless a second nearby haul is ordered.
- **Strip on haul**: corpse hauls strip the body first: gear into the hauler's inventory, body into its hands, one trip moves both. Configurable per haul type, colonist corpses left alone by default, and tainted apparel policies (take, leave on corpse, forbid, destroy; smeltable and non-smeltable separately). Strip orders on living targets haul the removed gear the same way, with a follow-up strip queued in case the target redresses.
- **Fewer round-trips**: builders and cooks gather everything the job needs into their inventory in one sweep and walk to the bench or site once.
- **Shared inventories**: a pawn carrying goods works like a walking stockpile: workers take what they need straight from the carrier, an idle carrier walks out to meet them halfway, and everyone uses their own carried stock first. Optional: builders may claim materials from a hauler mid-transit.

### Smarter hauling

- **Haul to stack**: haulers top up existing stacks instead of starting new ones, and several pawns can deliver to the same tile at once (destination tiles are no longer reserved). Works on the ground, on shelves, and in modded storage units.
- **Drop off in passing**: a pawn heading off on a long trip with a full backpack drops its load at a stockpile that is roughly on the way.
- **Overloaded**: pawns can carry past their max carry weight, slowed while encumbered, and only when it saves time over another round-trip (break-even math). One slider from "no slowdown" to "never overload". With Combat Extended loaded, CE's weight, bulk and encumbrance rules take over entirely.

### Planning tools (right-click "Plan prioritized [task]...")

- **Planned crafting**: batch a bill with one consolidated ingredient trip; the repeat count is capped by what is actually on the map, and products ride back with everything else.
- **Route planning**: travel-optimal routes over whole patches, veins, rooms or growing zones, previewed live on the map with a time estimate. Selection modes per work kind, smart routing that ends the trip near storage, must-include picks, pinned start and end, per-target-type remembered settings, and mining routes that extend as fog is revealed.
- **Smarter construction**: ordered builds haul materials (in inventory, fewer trips) and build as one job; plan whole fence lines as haul-only or haul and build; a separate order stocks a site before it is even buildable.

### Quality of life

- **Capable of dumb labor**: planners respect work incapability, plus three optional overrides (off by default) that let every pawn haul, clean, or cut plants, in this mod and in vanilla work assignment alike.
- Extensive mod settings: every feature has a working enable or disable, plus fine-tuning sliders.

### Compatibility

- Requires Harmony. Safe to add to existing saves.
- Compatible with Combat Extended and Adaptive Storage Framework (storage mods work by construction: every haul destination is validated through the game's own storage check).
- Replaces: Pick-up and Haul, Harvest and Haul, Auto Strip on Haul, Haul After Stripping, Everyone Hauls, Haul to Stack.
