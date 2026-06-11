# haulers-dream

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
