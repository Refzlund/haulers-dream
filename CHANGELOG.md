# haulers-dream

## 1.1.1

### Patch Changes

- ddded60: **Bulk hauling: the second order now takes over even after the first item is in hand, big stacks ride your inventory, and a one-click "Haul everything nearby" option.**

  - **Second nearby haul takes over the sweep immediately — even mid-carry.** With bulk hauling set to "only when a
    second item is tasked" (the default), ordering a _second_ nearby haul makes the pawn switch to the bulk sweep
    right away (loading the nearby items into its inventory for one storage trip) instead of carrying the first
    item to storage solo and coming back. This now works even when the pawn has _already picked the first stack
    into its hands_ — previously the takeover silently did nothing in that case. Works with plain or shift-queued
    prioritize orders; the first item is folded into the one trip; unrelated queued work is preserved; a third or
    fourth nearby haul folds into the same trip.

  - **Oversized stacks are carried in your inventory, not left behind.** When you order a haul of a stack bigger
    than the pawn can hold in its arms (e.g. 75 steel when it can only carry 72), it now routes the whole stack
    through the (mass-limited) inventory and delivers it in one trip, instead of hand-carrying a partial 72 and
    leaving the rest for later. The amount is clamped to the destination's real free space so nothing is stranded.

  - **New "Haul everything nearby" right-click option.** For a hauling-capable colonist, right-clicking a
    haulable now offers "Haul everything nearby" alongside the vanilla "Prioritize hauling" — a one-click bulk
    sweep, so you don't have to prioritize two hauls just to trigger it. It always starts a bulk sweep, including
    when shift-clicked to queue it (previously a shift-clicked / repeated click whose neighbors were already being
    swept could degrade into a plain single haul).

  Two new mod options (both on by default, under bulk hauling): the "Haul everything nearby" right-click option,
  and routing oversized single stacks through the inventory.

## 1.1.0

### Minor Changes

- ff25cd2: **Caravan pawns now offload onto pack animals on their own.** On a caravan or other away map there's no
  stockpile, so pawns used to just carry their scooped loot around in their own inventory — and could end up
  idle or asleep with a full pack while the red "Cannot unload inventory" alert fired. Now they offload onto a
  nearby owned pack animal at the same moments they'd unload to a stockpile at home: when their work run ends,
  before they rest / eat / relax at camp, on the periodic backstop, and immediately when over-encumbered. When
  no usable pack animal is reachable the loot still rides home in inventory as before.

  The "Offload onto pack animals (caravans)" setting (formerly "Auto-load pack animals when heavy") now governs
  all of this; it still requires automatic unloading to be on, and pawns keep accumulating their whole work run
  before making the trip (no change to that). The red alert no longer false-fires on caravans where loot is
  correctly riding home — it only flags a pawn that genuinely can't offload a reachable pack animal for hours.

- ff25cd2: **Caravan / pack-animal loading.** Two related improvements for hauling while away from your base:

  - **No more dropping loot on the ground while caravanning.** Previously, when a pawn scooped materials on a temporary map (a bandit camp, an ambush site — anywhere that isn't your base), the mod would try to "unload" them and, finding no stockpile, dump them on the ground — where they were abandoned when the caravan left. Now scooped loot simply accumulates in the pawn's inventory and travels home automatically as caravan inventory; it's never dropped on a temporary map. (At your base, unloading to storage is unchanged.)
  - **Pawns load pack animals instead of carrying everything themselves.** When a caravan pawn gets over-encumbered from scooping loot, it now automatically walks to the nearest pack animal and offloads onto it — keeping pawns mobile. And a new right-click order, **"Load nearby items onto pack animal (bulk)"**, makes one colonist sweep several nearby stacks into its inventory and load them onto a pack animal in a single trip (instead of vanilla's one-stack-at-a-time carry — which still works alongside it).
  - **Shift-clicking several "Load onto pack animal" orders now makes one trip.** Previously, queuing up multiple vanilla "Load onto pack animal" orders made the pawn carry one stack in its hands per order — a separate round-trip each. Those orders now coalesce: the pawn sweeps all the chosen items into its inventory and walks to the animal once.

  Both are on by default and have their own toggles in the mod options ("Auto-load pack animals when heavy" and "Bulk 'load onto pack animal' order"). The "work on caravans / temporary maps" setting now strictly controls whether the mod is active there at all; when it's on, the mod scoops and accumulates loot but never drops it on the ground.

- ff25cd2: **Pawns now put away surplus they're carrying — even if Hauler's Dream didn't pick it up.** Previously
  HD only unloaded items it had scooped itself; anything else a colonist ended up carrying (from a trade,
  another mod, or a manual move) was invisible to its auto-unload, so it could be hauled around forever.
  A new option, **"Also put away surplus inventory a pawn is carrying that Hauler's Dream did NOT pick up
  itself"** (on by default), makes colonists at home unload _any_ surplus they're carrying for no reason —
  not just HD-scooped loot.

  "Surplus" excludes the pawn's kept food, drugs, inventory-stock, and Combat Extended loadout (exactly the
  items vanilla keeps), and caravan-loading inventory is left alone. It's more thorough than vanilla's
  occasional auto-unload. If you use a mod that keeps items in a pawn's inventory through its _own_ system —
  e.g. **Smart Medicine** stock-up, or a sidearm mod — and you don't want those put away, turn the option off
  in the mod settings.

### Patch Changes

- ff25cd2: **Pawns now overload and accumulate, instead of tripping to storage after almost every item.** This
  restores the mod's core behaviour: a colonist deconstructing, mining, or harvesting keeps scooping the
  materials into its inventory — overloading past 100% up to the smart-overload ceiling (the Overload
  slider; ~2× capacity at the default "Fair") — and makes **one** trip when it's full, instead of
  hauling each stack to storage and walking back.

  Two over-eager triggers were the cause: pawns were unloading the instant they passed 100% capacity
  (which defeats the whole point of overloading), and again on the first momentary "no work right now"
  between items (constant in a busy colony). Now a pawn unloads only when it's genuinely **full** (at the
  overload ceiling) or **done** — i.e. it has stopped picking things up for a while (a new "accumulate
  window", ~1 in-game hour by default, adjustable in settings). It still always unloads eventually (when
  full, when its work is finished, or on the periodic sweep), so nothing is carried forever.

- ff25cd2: Hardened the "no black holes" guarantee so scooped items can never be silently lost or stranded, and the safety-net alert can't be fooled into staying quiet:

  - **Items are no longer lost when stacks merge.** When a carried stack is put back and merges into another stack of the same item (which happens with some interrupt mods), the merged-into stack is now re-tracked, instead of the item quietly losing its "needs unloading" mark and being left in the backpack forever.
  - **The "cannot unload" alert now catches pawns even when another mod keeps cancelling the unload.** Previously, a mod that interrupted and re-queued the unload job faster than the alert's grace window (e.g. an aggressive autocaster) could keep resetting the alert's timer so it never fired. The alert now times how long the _problem_ has persisted, independently of whether an unload happens to be running at that instant — so a genuinely stuck pawn always surfaces.
  - **No more phantom "Unload now" churn for personal kit.** Items a pawn legitimately keeps in its inventory (drug-policy stock, packable food, ammo/loadout under Combat Extended) stay tracked so any future surplus is never stranded — but they no longer trigger a no-op unload trip every cycle or a misleading permanent "Unload now" button.
  - The unload pass, the unload trigger, and the alert now share one definition of "what counts as surplus vs. the pawn's personal kit", so they can never disagree (which previously could cause either a nag or a missed item).

  Also added a `COMPATIBILITY.md` documenting how Hauler's Dream coexists with other mods (item-adders, unload/interrupt mods, Combat Extended), from a code-level review of a real load order.

- ff25cd2: Added a safety-net **red alert** (like vanilla "Fire!") in the bottom-right when one or more pawns are carrying scooped items they cannot put away — so inventories can never silently become "black holes". It fires when nothing on the map can store the items (no stockpile, no dumping zone, not even a reachable spot), or when a pawn has been carrying items far too long without unloading (storage unreachable, or another mod keeps cancelling the haul/unload job). One alert covers all affected pawns: hover it to point arrows at them, click to cycle the camera through them. Toggle and the "stuck for N hours" threshold are in the mod options (on by default).
- ff25cd2: **Compatibility: never strand items in a non-human pawn's inventory.** Bulk hauling (and the
  caravan pack-animal loader) now use the same "who may haul into inventory" rule as scooping and
  unloading — humanlike colonists, or colony mechs when _Allow mechanoids_ is on. Previously bulk
  hauling only excluded disallowed mechanoids, so a modded **non-mechanoid "robot/animal worker"**
  race (one that's set up to do colony hauling, e.g. the Housekeeper Cat) could have items swept into
  its inventory that the auto-unload — which never services non-human pawns — would then refuse to put
  away, leaving them stranded. Such pawns now simply keep vanilla single-stack hauling, untouched by
  Hauler's Dream. Normal colonists and mech haulers are unaffected.

  Also documented (no behavior change): verified compatible with **Allow Tool** and **Keyz' Allow
  Utilities** "Haul Urgently" (their urgent hauls are never swept by HD; HD is never mistaken for Pick
  Up And Haul; HD auto-honors Keyz' "Do Not Haul"), and with **Adaptive Storage Framework** / **Neat
  Storage** (HD composes through the same vanilla storage validators ASF extends). See COMPATIBILITY.md.

- ff25cd2: **"Load nearby items onto pack animal (bulk)" now appears for every pawn vanilla allows.** A colonist who
  is incapable of dumb-labor hauling (e.g. a doctor delivering materials to a construction site) was missing
  the bulk load option even though vanilla's own one-stack "Load onto pack animal" appeared for them. The bulk
  order is a player command — like vanilla, it no longer requires the Hauling work-tag, so any pawn that can
  physically pick things up can be ordered to load a pack animal. The same relaxation applies to coalescing
  several shift-clicked vanilla load orders into one trip.

  This only affects the **player-ordered** pack-animal paths (which deposit onto the animal, never stranding
  loot in the pawn's inventory). The automatic bulk-haul still keeps its full eligibility check.

- ff25cd2: **Fix two hauling misses around deconstruction and passing storage.**

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

- ff25cd2: Fixed the mod options window losing its scrollbar so the lower settings ran off the bottom and couldn't be reached. The settings list is rendered in a scroll view, but once the content grew past the last measured height (or you toggled an option that added rows, like bulk hauling or auto-strip), the underlying list silently wrapped into a second off-screen column — which collapsed the measured height back to the viewport, removed the scrollbar, and never recovered. The list is now pinned to a single column, so the scrollbar always tracks the real content height and every setting is reachable.
- ff25cd2: Stopped hiding errors. Hauler's Dream previously wrapped most of its logic in broad `try/catch` blocks that either swallowed exceptions outright or downgraded them to one-time warnings (or verbose-only debug lines) — which meant real bugs and mod-interaction issues were silently buried instead of being reported. Every one of those has been removed: errors now surface as normal red errors in the log so problems can actually be seen and fixed.

  This changes nothing about how the mod behaves when everything is working — it only affects what happens when something goes wrong (you now find out about it). Three deliberate, non-suppressing exceptions remain: the Combat Extended bridge still cleanly detects when CE simply isn't installed (via existence checks, not a catch); a single guard around third-party WorkGivers logs a red error naming the culprit mod and skips just that one (so one broken mod can't break the route menu); and the batch-crafting safety net still restores in-flight items before re-throwing, so a mid-craft failure can never lose items. If you see a new red error after updating, that's by design — please report it.

- ff25cd2: **Pawns now tidy up while they work** — a colonist deconstructing, mining, or harvesting doesn't just
  scoop _its own_ yields into its inventory; it also picks up _other loose haulable items lying around the
  work spot_ into its pack, so the whole area is cleared in the one consolidated trip instead of being
  left for separate hauls afterwards. This is the bulk-hauling sweep (which already fires on dedicated
  haul jobs) extended to work jobs.

  The pickup is into **inventory** (never hand-carried), respects the smart-overload ceiling, and only
  takes items that genuinely need hauling and have somewhere to go — never another hauler's target and
  never stock that's already in storage. Toggle it with **"Tidy up while working — scoop nearby loose
  items too"** in the mod settings (on by default).

- ff25cd2: **Pawns now put their load away before relaxing.** When a colonist finishes its work run and heads off to
  sleep, eat, or recreate, it makes its unload trip first — instead of carrying the ore it just mined to bed
  or to the dinner table. It still accumulates the whole run into its inventory while it's actually working
  (unchanged); the unload only kicks in once it stops working and turns to downtime.

  This fixes pawns being found asleep (or eating / relaxing) with a full pack even though stockpiles were
  available. Each activity has its own toggle in the mod settings — **"Put load away before sleeping"**,
  **"…before recreation"**, **"…before eating"** (all on by default). Critically tired or starving pawns skip
  the detour and rest/eat immediately; pawns in a party, ritual, medical bed-rest, or forming a caravan are
  left alone.

- ff25cd2: Pawns now properly put away items that no stockpile accepts — the most common being rock chunks (vanilla stockpiles exclude the Chunks category by default), but also mod-added materials (e.g. bronze from deconstruction) and mod-added crops whose category isn't in a default stockpile filter. Previously these were correctly picked up but, at unload time, dumped wherever the pawn happened to be standing (a workbench, the dining room) — and with no dumping zone they'd just get re-scooped on the next work run and carried around indefinitely, while everything else unloaded fine.

  The unload now mirrors vanilla's own behaviour: if no stockpile accepts an item, the pawn carries it to a dumping zone (if you have one) or a tidy spot near the colony, instead of dropping it underfoot. As a safety net, an item that genuinely can't be placed is no longer left silently stuck in the inventory. The "drop off in passing" trip also no longer gets skipped just because an un-storable item (like a chunk) happens to be in the backpack — it now checks the items it can actually store.

  Tip: if rock chunks pile up, add a Dumping Stockpile (and allow chunks on it) so pawns have somewhere to put them.

- ff25cd2: Pawns now actually put their hauled goods away promptly, instead of carrying materials around for a whole day. Previously the only reliable automatic unload was a slow timer, and a pawn could finish a big deconstruction or harvest, stuff its inventory, then work, eat, sleep and relax all day without ever unloading. Fixes:

  - **End of work run:** when a pawn runs out of work while carrying scooped goods, it now makes its unload trip right then — before drifting off to recreation or wandering — rather than holding the load indefinitely.
  - **Before meals and recreation:** a pawn that sits down to eat or relax with a full backpack queues an unload that runs the moment it's done (its meal/break is never interrupted).
  - **When overweight:** scooping that pushes a pawn over its carrying capacity now triggers an unload at the next job boundary, instead of letting it stay overloaded until it hits the much higher "smart overload" ceiling. A pawn shouldn't lug steel around all day.
  - **Heavier loads shed sooner:** a pawn carrying half its capacity or more now diverts to drop the load off on shorter trips and tolerates a slightly bigger detour to storage.
  - **Interval backstop** lowered from 6 in-game hours to 1, so even in the rare case every other trigger is missed, a load is never carried for more than about an hour. (Existing saves that changed this setting keep their value.)
  - Fixed a desync where a pawn whose meal was momentarily in its hands would silently skip an unload check.

  The "automatically unload" setting description now lists exactly when unloading happens, and what turning it off means (manual unload only, via the per-pawn button).

## 1.0.3

### Patch Changes

- e6e547d: Planned crafting polish, from a top-to-bottom review of the feature by three independent reviewers: recipes with fixed ingredients (make medicine, the whole drug lab, mortar shells) are no longer wrongly reported as having no ingredients — they can now actually be batched; a pawn that doesn't meet a recipe's skill requirement can no longer batch it (a level-3 cook could previously batch fine meals the game forbids); batches now respect the bill's own rules the way vanilla does — the ingredient search radius, rot and hit-point filters (no more cooking rotten meat the bill disallows, and batch-butchering a dessicated corpse for nothing is gone), and "make until you have X" bills no longer overshoot their target; and the per-repetition safety net around crafting was widened so even a misbehaving third-party recipe can't lose ingredients or products mid-batch.
- e6e547d: Strip on haul polish, from two independent top-to-bottom review rounds of the feature (six reviewers total). Round one: stripped loot that lands next to an existing pile of the same item no longer pulls that whole pile into the hauler's pocket — only what was actually on the body is scooped, and pieces that drop straight into valid storage stay where they landed; the destroy and drop-and-forbid tainted-apparel policies can never touch pre-existing ground stacks; quest lodgers no longer scoop strip loot (it could leave the map with them); follow-up strips are only queued for your own pawns; and two settings labels now tell the whole truth ("leave it on the corpse" notes that butchering drops it at the bench like vanilla, and "destroy it" notes that quest items and relics are always spared). Round two: a pawn that can't haul (a noble cook fetching a corpse for a butcher bill) no longer scoops loot it could never auto-unload — the body is still stripped, but the gear stays on the ground for real haulers, instead of weighing the cook down forever; manual Strip orders on corpses now honor the tainted-apparel choices just like automatic strips (and the tainted-apparel settings stay visible whenever either feature using them is on); and the strip-loot bookkeeping for modded stackable gear is now exact in every merge case.

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
