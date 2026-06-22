# haulers-dream

## 1.9.0

### Minor Changes

- 351a90b: feat/fix: five player-reported improvements.

  Mechanoid carry capacity now tracks the mech's own "carrying capacity" (the value shown on the mech's UI panel) instead of a flat amount. A vanilla lifter and a modded high-capacity loader now haul amounts that match their carrying capacity rather than the same small default. The per-mech haul multiplier still applies on top, and humanlikes, animals, and Combat Extended users are unchanged.

  Fixed red errors when right-clicking eggs (or other items held inside a container building, like an egg box) with a colonist selected. Those items are not lying on the floor, so the pickup and haul-nearby orders now skip them instead of throwing.

  You can now order a pawn to pick an item straight into its inventory while DRAFTED, and the order works on forbidden items (for example, food dropped in a prison cell that got auto-forbidden). The picked item is carried until the pawn is undrafted, then put away in normal storage, unforbidden.

  Fixed pawns getting stuck in an endless "gathering ingredients" loop when crafting or cooking a recipe with many ingredients under the "Do until you have X" bill setting (for example baking pies in a large multi-ingredient oven). Such recipes now use the game's normal ingredient gathering.

  Fixed Hauler's Dream interfering with order-based recycling mods (such as Recycle This): an item you have marked for recycling is no longer scooped into a pawn's inventory before the recycling job can carry it to the workbench. Hauler's Dream now leaves items alone when another mod has claimed them with an order.

### Patch Changes

- 50a1dab: fix: under Combat Extended, mechanoids now haul according to their carrying capacity.

  With Combat Extended installed, a hauler mechanoid was limited by Combat Extended's carry weight (a flat body-size value, around 42 kg for most lifters) regardless of its carrying capacity, so a vanilla lifter (carrying capacity 52) and a modded advanced loader (158) both hauled about the same small amount, and a fuller mech crawled once it passed that tiny limit. Hauler's Dream now sets a player mechanoid's Combat Extended carry weight to its carrying capacity, so it loads up to that capacity without hitting Combat Extended's over-capacity slowdown (Combat Extended's encumbrance and fit checks now follow the carrying-capacity number). The mech-haul multiplier in the settings now also applies under Combat Extended. Pawns other than your own mechanoids, and games without Combat Extended, are unaffected.

## 1.8.1

### Patch Changes

- 45b3fc6: fix: two crash fixes.

  Fixed a startup crash ("Could not resolve type ... Multiplayer.API.SyncMethodAttribute") that prevented the game from loading when RimWorld Multiplayer was not installed. Hauler's Dream now registers its Multiplayer sync handlers by name instead of through a baked attribute, so no part of the mod's metadata references the Multiplayer API when that mod is absent. Multiplayer behaviour is unchanged when Multiplayer is installed.

  Fixed a case where a broken work giver from another mod could stall a colony's work. RimWorld runs every work giver's eligibility check inside one unguarded loop, so a single work giver that throws there aborts the pawn's entire work selection every tick (all hauling, cleaning, and so on stall). Hauler's Dream now contains such a throw: if a work giver that is not Hauler's Dream's own throws while RimWorld checks whether a pawn can use it, the error is logged once and that one work giver is skipped for that scan, so the rest of the pawn's work keeps running. Hauler's Dream's own work givers still surface their errors normally.

## 1.8.0

### Minor Changes

- 4e70022: feat: report bugs, request features, and ask for mod compatibility from inside the game. A new "Report an issue" action opens a short form where you pick the report type (bug, feature request, mod compatibility, or something else), describe what happened, and send it straight to the developer. Your active mod list, game version, OS, and Hauler's Dream's own diagnostic log are attached automatically, with an option to also include the tail of your full Player.log for trickier bugs.

  You can attach Steam screenshots straight from a picker (take one with the Steam overlay and it shows up), and a "My reports" view lets you check back on what you sent and read the developer's replies, so a report becomes a short conversation rather than a one-way message.

  Hauler's Dream now also keeps a small always-on diagnostic log in the background (independent of the verbose-logging setting) so a bug report carries the context needed to track the problem down, without you having to reproduce it with logging turned on first.

## 1.7.0

### Minor Changes

- 79afbf2: feat: full **RimWorld Multiplayer** compatibility. Hauler's Dream now works in multiplayer — every feature (smart inventories, bulk loading, route/craft planning, the per-pawn gizmos, batch sizing) runs deterministically across all clients.

  Under the hood this routes every player-initiated action that changes saved state (the auto-haul toggle, "Unload inventory", Plan Route, Plan Craft, batch-size edits, carrier unload) through Multiplayer's command-sync, and makes the autonomous hauling/sweeping/bulk-loading logic pick the same targets on every client (deterministic tiebreaks), so the simulation never diverges. Multiplayer support is a soft dependency — it adds nothing and changes nothing when the Multiplayer mod isn't installed.

  A note for multiplayer hosts: Hauler's Dream settings are host-authoritative — they sync to everyone when you join (accept Multiplayer's "Apply configs" prompt), and the settings window is locked during a multiplayer session so a mid-game change can't desync the game.

- 74d881e: Four fixes from player reports:

  **Fixed a NullReferenceException flood after migrating a save off Pick Up And Haul.** When you remove Pick Up And Haul (which Hauler's Dream replaces) and load a save where a pawn (often a Lifter mech) was mid-haul, that pawn's old job can no longer be loaded and deserializes broken. RimWorld's own cleanup of such a job crashes on it, so the broken job is never cleared and the pawn throws an error every tick. Hauler's Dream now repairs these orphaned jobs, and the reservations they leave behind, when the save loads, so the affected pawns simply pick new jobs and the errors stop. It is a one-time cleanup per save and does nothing on a clean game.

  **Mechanoids now haul in proportion to their carrying capacity, with an optional multiplier.** Hauler's Dream already sizes each pawn's haul by its carrying capacity, so a modded high-capacity lifter already carries more than a vanilla one. A new "Mechanoid carrying capacity" slider (Who can haul, default ×1.0) lets you push your work mechs further, so a dedicated lifter makes fewer, bigger trips. The mech is slowed by the extra load the same way a colonist is, so the smart-overload trade stays balanced. No effect at ×1.0, and Combat Extended keeps managing carry weight when it is installed.

  **Added a setting to show the per-pawn auto-haul toggle (Unloading, off by default).** The "Auto-haul yields" toggle on each pawn is now hidden unless you turn this on, keeping the command bar uncluttered. Pawns still auto-haul exactly as before; turn the setting on if you want to stop individual pawns from auto-hauling.

  **Fixed vehicle cargo loading being silently off when Vehicle Framework is installed.** Hauler's Dream checks for Vehicle Framework as the game loads, but that check happened slightly too early, before the game had finished setting up Vehicle Framework's stats. Because of the timing it switched the whole integration off for the rest of the session even though it is on by default, so colonists never bulk-loaded a vehicle's cargo in one trip and eating or building from a parked vehicle's cargo did not work. The check now reads the vehicle stat the first time it is actually needed during play, so the integration turns on as intended.

## 1.6.0

### Minor Changes

- e168b2b: feat: full localization + translations for 14 languages. Every piece of player-facing text now goes through RimWorld's translation system (the last few hardcoded fallback strings were externalized), and the mod ships with translations for **Chinese (Simplified), Danish, Dutch, French, German, Italian, Japanese, Korean, Polish, Portuguese (Brazilian), Russian, Spanish, Thai and Ukrainian** alongside English — settings, menus, alerts, planners, job reports, everything.

  The non-English translations are a complete first pass (AI-assisted, using RimWorld's established per-language terminology); native-speaker corrections and additional languages are very welcome via a quick pull request — see the new CONTRIBUTING translation guide. A build-time parity check (`scripts/check-translations.ts`) guarantees every language defines exactly the English key set with matching placeholders, so a translation can never silently fall out of sync.

## 1.5.0

### Minor Changes

- 0d2f6d7: Added experimental, opt-in bulk-loading from the Storage Network mod's servers. Storage Network keeps stored items virtually (despawned inside its servers), so they were invisible to the bulk-load sweep — a transporter, pod, portal or vehicle whose manifest lived in the network loaded one stack per trip instead of everything at once. With the new "Bulk-load from Storage Network (experimental)" setting enabled (off by default; the option only appears when Storage Network is installed), Hauler's Dream now adds the network's stored stacks to the load plan, pulled through a usable and reachable terminal, and lets Storage Network materialise them on demand so the whole load is gathered in one trip. The amount is still bound to the manifest, the pawn's carry capacity and the claim ledger, and any stack the network can't hand over is simply left for the normal one-stack loading — nothing is over-pulled or stranded. It is opt-in because it relies on Storage Network's own on-demand behaviour.
- 0d2f6d7: feat: new **"Migration"** settings tab — a clean-transition guide that appears only when you still have a mod Hauler's Dream replaces active (Pick Up And Haul, While You're Up, Meals on Wheels, Harvest and Haul, Auto Strip on Haul, Haul After Stripping, Everyone Hauls, Haul to Stack, Bulk Load For Transporters, Haul After Slaughter).

  Running one of those alongside Hauler's Dream makes them fight over the same hauling jobs — the usual reason pickup looks broken or flaky right after switching. The tab sits at the bottom of the settings tab list with a warning-amber icon and label, lists exactly which replaced mods you still have on, and offers two ways to fix it: a **"Disable them for me"** button that (after a confirmation warning you to save first) turns the replaced mods off and restarts the game, or the manual safe steps — draft your colonists and save, disable the mods, reload and save, then carry on.

  Detection now catches **community translations and continuations**, not just the exact original mods: each active mod is matched both by packageId and by a normalized substring of its name and packageId, so a translated "Pick Up And Haul 日本語" or a "…(Continued)" reupload is still recognized. The tab hides itself automatically once none of the replaced mods are active. No setting and no save data are added.

### Patch Changes

- 0d2f6d7: Fixed compatibility with **Everybody Gets One** (the "Everybody Gets One - Continued" mod): with Hauler's Dream enabled, the mod's custom bill repeat modes ("one per person", "X per person", "with surplus") disappeared from the repeat-mode dropdown, so you couldn't set a bill — e.g. clothing — to "one per person" at all. Hauler's Dream's batch feature rebuilds that dropdown and was fully replacing the vanilla menu, which skipped the hook Everybody Gets One uses to add its modes. Hauler's Dream now surfaces those modes (with their own labels and validity checks) alongside its batch options. It also makes its product-count correction mode-aware so an Everybody Gets One "one per person" bill correctly pauses once everyone has one instead of overproducing, and it leaves those bills' crafting to the other mod rather than batching them.
- 0d2f6d7: Broadened mod compatibility with a set of general patterns (each helps any mod with the same kind of feature, not just the named one):

  - **Item Policy**: Hauler's Dream now respects a pawn's per-pawn Item Policy inventory-stock counts, so it no longer strips items the policy wants kept (which previously fought Item Policy's re-fetch in an unload/re-fetch loop). The kept count feeds Hauler's Dream's existing count-aware keep, so the surplus above the policy amount still unloads normally. Inert without Item Policy.
  - **Foreign unload jobs**: Hauler's Dream's inventory-unload substitution now only replaces vanilla's own unload, never another mod's custom unload job (e.g. Common Sense's marked-items unload, or a carrier-unload routed through the work scan such as Bulk Load For Transporters), so those mods' unload flows are left intact.
  - **Work-selection ordering**: Hauler's Dream's two opportunistic work-scan hooks now run last, so they react to the final chosen job after a job-substituting mod (e.g. While You Are Nearby) has had its say, instead of racing it.

- 0d2f6d7: Vehicle Framework: a vehicle's cargo hold is now treated as the player's to manage. Hauler's Dream no longer sources build materials (build-from-inventory) or meals (meals-on-wheels) out of a parked, loaded vehicle's cargo, so a trip loadout you packed isn't silently undone — matching how it already declines to bulk-unload a vehicle. And when Hauler's Dream's Vehicle Framework support is turned off, it now ignores vehicles entirely, no longer depositing into a vehicle's cargo via the pack-animal loading path (at both job selection and the in-flight deposit loop). Inert without Vehicle Framework.
- 0d2f6d7: Bulk refuel: fix a crash and revive the feature for impassable refuelables (e.g. Advanced Power Plus's advanced nuclear generator). Hauler's Dream anchored its one-trip fuel sweep at the refuelable's own cell, but a generator, deep drill or reactor sits on an impassable footprint with no passable region there — which made RimWorld's fuel finder dereference a null region and throw, freezing colonists in a job-search loop and breaking the building's right-click menu. The sweep now starts from the hauler's own (always-passable) cell, exactly as vanilla's normal refueling does, so it no longer crashes and once again bulk-refuels generators and drills instead of silently falling back to one-stack-at-a-time. Fixes #34.
- 0d2f6d7: Silence the startup debug-log warnings about types holding texture/material fields without `[StaticConstructorOnStartup]`. RimWorld structurally checks every type with a static `Texture2D`/`Material` field for that attribute (so its assets are guaranteed to load on the main thread) and logs a warning when it's missing — Hauler's Dream tripped it on three: the per-pawn unload gizmo (`Patch_Pawn_GetGizmos`), the settings window's header/icon textures (`HaulersDreamSettings`), and the route-preview line material (`MapComponent_RoutePreview`). All three now carry the attribute (matching the existing `DetourOverlay` usage), so the warnings are gone; the textures still load lazily on the main thread exactly as before, so there's no behavior change.

## 1.4.1

### Patch Changes

- 432b283: Fixed bulk-loading transporters, shuttles, drop pods, portals and vehicles when the goods live in a storage building such as shelves, deep storage or ordinary stockpiles. The bulk-load sweep only ever looked at loose items lying on the ground (the haulables list excludes anything already in valid storage), so when everything was stored the sweep found nothing and the pawn fell back to the vanilla one-stack-per-trip behaviour — taking a single pack instead of everything the manifest needed. Hauler's Dream now also sweeps the stacks held in storage for the items being loaded, so the whole load is gathered in one trip as intended. The amount taken is still bound to the manifest and the pawn's carry capacity, and anything a storage keeps off-map (rather than as a normal on-map stack) is left to vanilla, so nothing is over-pulled or stranded. (This covers storage that keeps its items spawned on the map; a virtual/digital storage such as Storage Network, whose items are held despawned inside its servers, is handled separately by the opt-in setting added in a later version.)
- 432b283: Fixed colonists being left with no way to load a transporter, shuttle, drop pod or map portal once they were already assigned to board it. While a pawn was under the caravan/portal boarding lord, Hauler's Dream suppressed vanilla's "Load X" right-click option, but its own bulk-load option also intentionally stands aside there (to let the vanilla gather-and-board flow run) — so right-clicking the transporter offered nothing and the pawn could not be hand-directed to load. Hauler's Dream now keeps vanilla's load option whenever its own bulk option declines, so there is always a way to order the load.
- 432b283: Fixed a "started 10 jobs in one tick" error and pawn stall when a colonist hauled a corpse (or any other unstackable item) to a storage cell. Haul To Stack deliberately leaves the destination cell unreserved so several haulers can top up the same tile at once — but an unstackable thing can never share a cell, and removing the reservation also removed the vanilla throttle that stops the same cell from being re-picked every tick. When two haulers contended the same one-capacity corpse cell, the work scan re-issued the identical haul over and over until the engine's safety guard tripped and the pawn froze doing nothing. Unstackable items (corpses, minified buildings, weapons) now keep vanilla's cell reservation; stackable hauling is unchanged.

## 1.4.0

### Minor Changes

- fb03a04: feat: on caravan/away maps, a building you uninstall is now scooped into the worker's inventory so several uninstalled structures load onto the pack animals in one trip, instead of one cross-map back-and-forth walk per item. New "Uninstalling — minified buildings" toggle (on by default) under the per-work-type yield settings. Only fires on non-home maps and only where the item can actually be delivered; on your home colony an uninstalled building is left on the ground for normal hauling or re-installation, unchanged.

### Patch Changes

- fb03a04: fix: every message, warning, and error Hauler's Dream writes to the log now carries the `[Hauler's Dream]` tag from a **single source of truth** (so the tag can be changed in one place), and a universal breadcrumb is attached to **every method the mod patches**. If an exception passes through Hauler's Dream's code, it is now logged with the tag — identifying that the mod is in the call stack, _without_ falsely claiming the mod caused it — and then **re-thrown unchanged**. Errors are never swallowed or downgraded; the game still reports them exactly as before. The breadcrumb is logged once per method so a per-tick fault can't flood the log. The single intentional fail-open path (the redundant Vehicle Framework reservation, which is safe to skip because a separate authoritative check already stood the pawn down) also now logs a tagged once-per-session warning instead of failing silently.
- fb03a04: fix: batch-crafting now mixes ingredients correctly across repetitions. Recipes that allow mixing (every cooked meal, plus kibble/pemmican/chemfuel/beer) couldn't batch properly — the batch planner froze a single ingredient def per slot, so a meal bill used only potatoes _or_ only rat meat and refused to craft when no single ingredient alone covered a serving. The batch planner and driver are now mix-aware: each repetition's ingredient mix is chosen by value from current stock at craft time (mirroring vanilla's own mixing fill), and the batch is sized by total available nutrition. Meals and other mixing recipes batch many reps from one pre-load again, mixing exactly as a normal single craft would.
- fb03a04: fix: a "Do until you have X" bill no longer silently stops being worked while finished products sit in a colonist's inventory. Hauler's Dream counts products in flight toward storage so the colony doesn't overproduce — but it was also counting products a pawn keeps (food, drugs, loadout) that never reach storage, permanently inflating the count so the bill read "already satisfied" and was never offered to any pawn (the bench appeared dead with no error). Only the surplus actually heading to storage is now counted, so the bill resumes correctly.
- fb03a04: fix: high-capacity refuelables (e.g. a large bulk-fed cooking pot from a mod) can now be bulk-refuelled. The bulk-refuel order required the ENTIRE remaining fuel deficit to be reachable in a single sweep, which rarely holds for a big refuelable, so the order silently did nothing — the "Prioritize bulk refuelling" option no-op'd and the fill couldn't be forced. It now accepts a partial sweep, filling what it can reach now and topping up on a later trip, the same way vanilla's single-stack refuel already tolerates fuelling a little at a time. (Other refuel mods' work — turrets, Combat Extended, Vehicle Framework — is untouched, as before.)
- fb03a04: fix: builders no longer zig-zag across a wall/fence line when delivering construction materials in one inventory trip. A multi-site delivery now drives to the **nearest remaining build site from where the pawn is standing** on each hop (a greedy nearest-neighbour route), instead of following the queue's fixed distance-from-a-single-anchor order — which sent the pawn concentrically around the first-filled site in an alternating-sides pattern, turning short walks into long back-and-forth trips. Single-site deliveries are byte-identical; vanilla's own hand-carry batching is unchanged.
- fb03a04: fix: colony-wide hauling/cleanup no longer silently stalls after some saves. The pre-save cleanup interrupted a pawn's in-flight bulk-load job _during_ save serialization, which could tear the bulk-load claim ledger and leave phantom claims on reload — making the planners believe all work was already taken. The save-time interruption is removed (queued-job cleanup is kept), a load-time validator releases any orphaned claims to self-heal existing affected saves, and the work/haul/rest/eat/strip seams HD hooks now log a clear, attributed error (and still rethrow) instead of failing silently if anything throws there.
- fb03a04: fix: Hauler's Dream no longer injects its "share carried ingredients for crafting" candidates into a **mechanoid's** crafting bill, nor reroutes a mech's ingredient gather through inventory. A colony mech ignores forbidden / allowed-area when sourcing ingredients and is bounded by its work range, so an injected or rerouted candidate could feed a vanilla `DoBill` the mech can't complete — a contributor to the _"started 10 jobs in one tick"_ crafting loop (e.g. a Fabricor at a stonecutter's table). Share-for-crafting is a colonist scoop feature, and the ingredient injection was previously the only such path that ran for mechs **regardless of the "allow mechanoids" setting** — inconsistent with the gather conversions, which already respected it. All of HD's share-for-crafting machinery is now consistently mech-excluded (single source of truth). Colonist crafting is byte-identical. Note: the underlying loop is primarily vanilla mech behaviour; this removes Hauler's Dream as any possible contributor.
- fb03a04: fix: the right-click "Pick up" order is no longer offered for an item already sitting in its best storage — where the pawn would pick it up only to immediately carry it back, looking like it refused the order. Items in a worse stockpile (or no storage) can still be picked up and upgraded exactly as before.
- fb03a04: fix: pawns inside a Vehicle Framework RV (or any non-home map that has player storage) now unload scooped items into the local shelves/zones instead of looping pick-up → drop forever. The unload routing treated every non-home map as "caravan, load a pack animal", which dead-ended inside an RV that has real storage but no reachable pack animal — and a first attempt to special-case it checked for a "pocket map", which a VF RV interior is not, so the loop persisted. Routing now keys purely on whether the map has player storage; genuine storage-less caravan/raid maps still load pack animals exactly as before.
- fb03a04: fix: bulk-refuelling a building whose own cell is impassable — a deep drill, a generator, a Save Our Ship 2 engine, a mod's bulk-fed pot — no longer throws a `NullReferenceException`. Vanilla's fuel finder dereferences the _region_ of the refuelable's cell with no null check, and an impassable cell has no passable region; Hauler's Dream now detects this up front (mirroring vanilla's own region test) and cleanly defers to vanilla's single-stack refuel, which works from the pawn's position. This removes the continuous SOS2 ship-engine refuel error and the float-menu error. It is a precondition guard, not a swallow: the bulk optimization is simply skipped for such buildings (the refuel still happens), and any other fault still surfaces.
- fb03a04: fix: Hauler's Dream no longer empties a pawn's inventory out from under a ritual, ceremony, or other directed group activity. A pawn gathering offerings for a ritual (for example bioferrite for an Anomaly psychic ritual) carries them on purpose, but HD's automatic unload would haul them off to storage before the ritual ran, failing it. HD now stands down its automatic scoop / adopt / unload for any pawn currently engaged in a Lord-directed activity — rituals and ceremonies, caravan forming, parties and gatherings, quest lords (vanilla and DLC) — and resumes normally once the activity ends. Explicit player orders are unaffected.
- fb03a04: fix: colonists no longer freeze "standing" next to a transport pod (or map portal) being loaded. When the remaining manifest was something the one-trip bulk sweep couldn't pick up — pawns/corpses to board, or items that are forbidden, out of the loading radius, or too heavy — Hauler's Dream told the game "there is loading work here" but then built no job, so vanilla issued a target-less haul that ended and re-fired every tick (the "started 10 jobs in one tick" error → forced wait). The "is there work?" check now builds the actual bulk job first and only claims work when one exists, otherwise letting vanilla's own loading decide — so the answer can never disagree with what gets built, and the loop is gone.
- fb03a04: fix: bulk/batch jobs no longer send a pawn through a deadly environment for _bonus_ targets. When a colonist starts a hauling / loading / construction-supply / crafting job, Hauler's Dream adds nearby items to the same trip — but those extra targets were inheriting the "ignore danger" exemption that only the single, explicitly-clicked target is meant to get (a job becomes danger-exempt while it is player-forced, or while its right-click menu is open). The most visible symptom (Save Our Ship 2 / Odyssey): a suit-less colonist the player set to mine or deconstruct would sweep up scrap sitting in vacuum and walk into space to fetch it.

  Now every UNCLICKED extra is held to the pawn's normal danger ceiling — it will never path through vacuum, fire, or deadly temperature for a bonus pickup — while the single target you explicitly ordered still obeys your forced command exactly as before. Your drawn allowed-area zones were always respected; this closes the separate danger-avoidance gap. Existing saves self-heal (an already-queued, now-unreachable self-pickup is dropped and left for normal hauling rather than walked to). On maps with no vacuum or lethal temperatures, behaviour is unchanged.

## 1.3.1

### Patch Changes

- bcf00d4: Fixed "Do until you have X" bills ignoring **Pause when satisfied** and **Unpause at** — pawns kept crafting until the target was full and the bill never paused. Hauler's Dream banks freshly-made products in a pawn's inventory (to deliver a whole batch in one trip), but the vanilla product count that drives the pause/target/unpause decision only counts items in storage and in the hands, never in inventory. So the banked products were invisible, the bill never saw its target met, the paused state never latched, and pawns overproduced. The product count now includes the in-flight banked products colony-wide, so the bill's own pause-when-satisfied and unpause-at hysteresis work exactly as they do for ordinary one-at-a-time crafting.
- 7e46eed: Fixed pawns hand-carrying construction material to a single site while ignoring identical nearby sites they could have served from the same inventory load — e.g. right-clicking to build a wall delivered one armful to one wall and skipped six others within reach. Hauler's Dream's multi-site construction delivery relied on vanilla's nearby-needer batch, but vanilla caps that batch at one hand-load of demand (and an 8-tile radius), so it could never load the inventory for more sites than a single armful already covered. Hauler's Dream now discovers the nearby same-material construction cluster itself — scanning blueprints and frames around the site, nearest-first, up to one overloaded trip's worth — and loads the combined material in one go, then delivers to each site. This applies to both right-clicked (prioritized) and automatic construction; planned routes already loaded for the whole route and are unchanged.
- 750499b: Fixed pawns dropping scooped work-yields on the ground while they keep working — e.g. a grower scoops a harvest, then drops it on the field as it carries on sowing. The anti-stranding auto-drop was treating a Hauling work-type priority of 0 as "can never haul, so this cargo is stranded." But a pawn the player set to never haul (a dedicated grower or crafter) still scoops its yields and still delivers them through Hauler's Dream's own unload trips, which don't use the vanilla hauling job — so its cargo was never actually stranded. A Hauling priority of 0 no longer triggers the drop; only a pawn that is genuinely incapable of hauling, or a stuck mechanoid, does.

## 1.3.0

### Minor Changes

- 84c9dbf: **Full "Bulk Load for Transport" parity.** Hauler's Dream now covers the remaining behaviors from the Bulk Load for Transport mod, so it works as a complete drop-in replacement. New safety nets are on by default; the more opinionated behaviors are opt-in.

  On by default (safety / anti-loss / correctness):

  - **Save survives uninstall** — a save written while a colonist is mid-bulk-load no longer leaves a broken job reference if you later remove Hauler's Dream.
  - **Softlock auto-drop** — if swept cargo ends up stranded on a pawn that can no longer haul (work disabled, hauling priority 0, or a dormant/charging/shut-down mech), only that tagged cargo is dropped so another hauler reclaims it.
  - **In-transit cargo shows in the loading dialog** — items already on their way inside a hauler's pack are counted as accounted-for, so the dialog and vanilla don't think they still need hauling.
  - **Shuttle boarding sync** — a colonist boarding a shuttle deposits its manifest cargo into the shuttle instead of flying off with it in their backpack (manifest decrements exactly).
  - **All-pawn manifests keep the vanilla option** — when a manifest is only pawns/corpses (which bulk-load can't carry), the normal "load" option is preserved instead of a dead end.
  - A small per-frame work-availability cache trims the load-path scan cost on big colonies.

  Opt-in (off by default, in settings):

  - **Opportunistic loading** — a hauler already carrying matching cargo diverts to top off a nearby needy transporter/portal/vehicle.
  - **Hybrid pathfinding** — re-ranks the nearest load targets by real walkable distance instead of straight-line.
  - **Continuous loading** — a player-forced load chains to the next group until everything reachable is loaded.

  Plus polish: optional auto-open of the contents/gear tab on select, verbose logging gated to dev mode, and a "Reset to defaults" button in settings.

- 84c9dbf: Build From Inventory: a constructing pawn now sources build materials from carried stock — its own inventory, other colonists', and pack animals' / caravan cargo — not just loose stacks on the ground. The headline case: carry steel in a caravan and order a sandbag or wall on a raid, and it builds straight from the carried steel without you manually dropping it off a pack animal. Two toggles: Build from inventory (default ON) and an opt-in Partial build (default OFF) that lets a frame progress with whatever a single carried stack provides instead of requiring the full amount up front. Floor stacks are still preferred; the common home-map build is unchanged.
- 84c9dbf: Bulk load map portals: extends bulk loading to pit gates, cave/vault exits, and "enter map" portals, reusing the transporter loading engine (same claim-ledger, planner and sweep). Items are swept into inventory and deposited through the portal in one trip, with the manifest reaching exactly empty even though each deposited stack teleports away. Portal-side anti-conflict (no false "loading stalled" alert, no premature enter) and the vanilla single-item portal-load option replacement are included, independently gated by a new toggle (default ON). Right-click a portal for "Prioritize bulk loading". (Completes the Bulk Load for Transport replacement.)
- 84c9dbf: Bulk load transport pods & shuttles: colonists now load transporters in bulk — sweeping many item stacks into inventory and depositing them in a single trip instead of one stack per trip — and multiple haulers split one transporter group's manifest without double-hauling, coordinated by a per-save claim-ledger. The manifest decrements exactly (never over- or under-count), the loading-stalled alert no longer false-fires, shuttles won't board or launch while hauling is still in flight, and the vanilla single-item load option is replaced. Right-click "Prioritize bulk loading", or let it run as ordinary hauling. The ledger survives save/load and is cleaned up on map removal; interrupting a hauler returns its claim and its swept items to the normal unload. Toggle (default ON). (Second part of the Bulk Load for Transport replacement.)
- 84c9dbf: Bulk refuel: colonists now fill refuelables — a shuttle's chemfuel, deep drills, generators, anything refuelable — in a single trip. Instead of vanilla's one fuel stack carried in hands per walk, a hauler sweeps enough nearby fuel into its inventory, walks to the refuelable once, and deposits it all at once. It only kicks in when more than one trip's worth of fuel is needed (a single-stack refuel is left to vanilla, which already does it in one trip), and reuses vanilla's own fuel finder so it picks exactly the stacks vanilla would. Runs automatically as ordinary refuelling work, or right-click a refuelable for "Prioritize bulk refuelling". Any fuel swept over what the refuelable needs stays tracked and is put away by the normal unload, so nothing is stranded. Atomic-fuel reloads (e.g. mortar barrels) and turret/Combat Extended/Vehicle Framework refuel jobs are left to their own handling. Toggle in Bulk & Carriers (default ON).
- 84c9dbf: Bulk unload pack animals: vanilla pulls one stack to a hauler's hands per trip when unloading a pack animal; Hauler's Dream now pulls many stacks into the hauler's backpack in a single visit and then ships them to storage, so emptying a loaded caravan animal takes one walk instead of dozens. Right-click a pack animal for "Prioritize bulk unloading", or let it run as ordinary hauling work. Respects Combat Extended weight/bulk, leaves the carrier interruptible for roping/caravan-forming by default, and defers mechanoid carriers to vanilla. (First part of the Bulk Load for Transport replacement.)
- 84c9dbf: Carry-weight overhaul + a "keep working when full" option:

  - **Colonists carry more freely.** The move-speed penalty for an overloaded inventory is now a gentle _curve_ instead of a straight line — a light overload is nearly free, and the slowdown only ramps up as the load gets heavy. At the default ("Fair"), colonists now fill to ~275% of capacity before it stops paying off (up from ~200%), and they're still moving at ~65% speed there instead of crawling. The overload slider scales the whole curve: looser settings carry farther with a gentler slope, stricter settings bite sooner. (The carry ceiling is still derived from the trip-vs-speed break-even, which is distance-independent — far hauls don't change how much it's worth carrying.)
  - **New "Keep working when full" option (default off).** When enabled, a pawn doing a job that scoops yields (mining, harvesting, etc.) keeps working when its inventory fills up, instead of breaking off to unload — the overflow is left on the ground for haulers. It only makes an unload trip when it's about to travel farther than its nearest dropoff (so it regains speed before a long haul) or at downtime. Lets a miner keep mining while dedicated haulers move the output. Off by default, so existing behavior is unchanged until you enable it.

- 84c9dbf: **Smarter unload ordering: nearest destination first (on by default).** When a pawn makes its unload trip carrying several different items, it now empties the nearest storage destination fully before walking to the next, instead of going in item-category order — less zig-zagging across the base. Items with nowhere to go are never stranded (they're just visited last, exactly as before). Ported from "While You're Up"'s efficient-unloading, re-expressed on Hauler's Dream's own unload. Toggle in settings.
- 84c9dbf: **En-route pickup — grab loose items on the way to a job (opt-in, off by default).** The signature "While You're Up" mechanic, re-expressed on Hauler's Dream's inventory hauling: when a pawn sets off on any job and a loose haulable lies roughly along the path, it scoops the item into its inventory first (serviced by the normal storage-aware unload), so the stray item rides to storage on a trip the pawn was making anyway — zero extra round-trips. The detour is tightly bounded by a trip-ratio check (a faithful port of WYU's `CanHaul` cascade) with a Vanilla/Default/Pathfinding accuracy knob, and it respects the per-pawn auto-haul toggle, the carry-weight ceiling, the bleeding gate, anti-double-haul, and (when enabled) the storage-building filter. Enable it in **En-route & Routing** in settings.
- 84c9dbf: Player-feedback features:

  - **Per-pawn "Auto-haul yields" toggle.** Every eligible colonist and work-mech now has a gizmo to turn its automatic yield-scooping and bulk-haul sweeping on or off individually — so you can leave a skilled miner or grower working and let dedicated haulers move the output, without touching the global settings. Default on (unchanged behavior); forced orders ("Prioritize hauling", "Haul everything nearby", "Pick up X") still work regardless.
  - **High-capacity haulers (incl. mechs) carry a real load.** Work-mechanoids are no longer capped at exactly 100% — they now use the same smart-overload as colonists (and are slowed for it by the same overload slider), so a high-capacity hauler fills a worthwhile load before its trip instead of leaving on a single stack. (A deliberate, slider-controlled balance change; set the overload slider to "no slowdown" to carry freely, or lower for less overloading.)
  - **"Pick up X" right-click order.** Optional manual right-click on a ground item to send a pawn to grab that stack (and fit more) into its inventory and make one stockpile trip — the picked items are tracked exactly like scooped yields, so they always get put away. Default on; toggle in mod settings (independent of the bulk-haul option).
  - **Optional animal inventory hauling.** A new "Allow colony animals to scoop and haul" toggle (default off) lets Haul-trained colony animals carry multiple stacks in their inventory like colonists, instead of one item at a time.

- 84c9dbf: Haul After Slaughter: when a colonist finishes killing an animal, the fresh carcass is hauled straight to storage (a freezer or corpse stockpile) so it doesn't rot where it fell. Two independent toggles, both default ON — slaughtered (tamed) carcasses, which vanilla never hauls itself, and hunted (wild) carcasses, where the hunter promptly grabs its kill if a hunt was interrupted right after the killing blow (a clean hunt already self-hauls, so this never double-hauls). Only hauls when a reachable storage spot accepts the body; otherwise the carcass is left exactly as vanilla.
- 84c9dbf: "Haul Urgently" now bulk-hauls. Allow Tool and Keyz' Allow Utilities both build their "Haul Urgently" job as a plain vanilla single-stack haul that bypassed Hauler's Dream's bulk sweep — so urgently-marked items were carried one at a time. HD now intercepts both mods' urgent-haul work giver and runs the same bulk conversion it uses for ordinary hauls: a colonist sweeps the nearby urgently-marked (and other) items into its inventory and makes one storage trip instead of dozens. It's a soft dependency (no effect unless one of those mods is installed) and inherits all of HD's bulk-haul settings — turn HD's bulk hauling off and urgent hauls revert to vanilla one-at-a-time, exactly as before.
- 84c9dbf: **Master enable switch, tabbed settings, and routing inspector text.** Quality-of-life parity items from "While You're Up":

  - **Master enable switch (on by default, no restart).** One toggle to stop Hauler's Dream starting its automatic hauling behaviors — handy for troubleshooting whether the mod is involved in something. A pawn that already scooped goods still unloads them (nothing is ever stranded) and the "Unload inventory" button stays available; the work-incapability overrides and right-click orders have their own toggles.
  - **Tabbed settings window.** The (now sizable) settings list is organized into tabs — General, Sharing & Delivery, Bulk & Carriers, En-route & Routing, Sources & Who, Planners & Advanced — each with its own scroll. No setting was removed or renamed.
  - **Routing-aware inspector text.** A pawn diverting to grab something en route shows "… (on the way to …)" / "… (closer to …)" in its job text.
  - **Dev tools** (dev mode only): a "make colony" hauling stress test in the debug menu, and an optional colored detour-line overlay.

- 84c9dbf: Meals On Wheels: when there's no food on the map for a hungry colonist, they'll now eat acceptable food carried in another colonist's (or a pack animal's) inventory instead of trekking to a far stockpile or going hungry — fewer trips, less wasted time. One toggle (default ON). Vanilla map/own/pack-animal food is always preferred first; drafted, downed and berserk carriers are left alone, a parent's in-progress baby feeding is never interrupted, and a carried meal about to spoil is grabbed first.
- 84c9dbf: Multi-site construction delivery: automatic and shift-clicked ("Prioritize construct") builds now load materials for several nearby sites into inventory in one trip, instead of serving one site per trip — previously only the route planner did this. When a pawn delivers to a cluster of same-material build sites within 8 tiles whose combined demand exceeds one armful, it loads the whole cluster's demand at once and delivers to each site in turn, far fewer stockpile trips for a fence line, a row of sandbags, or a batch of small builds. It still finishes the site it's already working before making a load trip (no abrupt interruptions), and a single-material-per-job rule keeps deliveries clean. Default on; a new "Load several nearby sites' materials in one trip" toggle sits under "Carry materials in inventory for big single builds" in the settings (and requires it).
- 84c9dbf: Settings profiles. The settings window now has a profile selector beside a new logo header: save your current settings as a named profile, switch between profiles from the dropdown, and see at a glance whether you're on a saved profile or have unsaved changes ("Custom (unsaved)" / "<name> (profile, unsaved changes)"). The built-in **Default** profile can never be changed and doubles as "reset to defaults". Profiles are stored with your mod settings and survive restarts; resetting to Default never deletes them.

  Profiles can also be **copied and pasted as a short share code** (Copy/Paste in the dropdown). The code holds the mod version plus only the settings you've changed from that version's defaults, so it stays compact, and pasting it on another setup recreates the profile (you choose the name, pre-filled with the original). The window chrome was tidied too — the redundant bottom Close button is gone and the corner ✕ now has padding.

- 84c9dbf: Opportunistic hauling is now ON by default — en-route pickup, consumer-aware storage routing, and storage building filters all start enabled (these were off before). After updating they'll be enabled even on existing setups, so turn off any you don't want on the **Routing & storage** page. Settings labels and descriptions no longer reference other mods by name; features are described by what they do. The settings info panel is polished too: every option now shows a coloured On/Off (or current value) line above its description, the Smart-overload page draws a live move-speed-vs-carry-weight curve, and section spacing/headings were tidied so descriptions no longer clip.
- 84c9dbf: Settings window redesigned. The old tabbed window is replaced by a three-pane layout — an icon navigation list on the left, the options in the centre, and a contextual description panel on the right that updates as you hover. Settings are reorganised into ten clear categories, including a new **Features** page that puts every "incorporated mod" family (bulk hauling, pack-animal/transporter/portal/refuel/vehicle loading, build- and craft-from-inventory, the planners, While-You're-Up routing, …) on one page as on/off cards, so you can switch off any family you don't want at a glance. Multiple-choice options (pick-up handling, strip policy, route selection, …) are now inline segmented buttons instead of dropdowns, sliders show a value readout, and sub-options stay visible but greyed when their master is off. This also fixes the previous panel's scroll bug, where the taller pages clipped their bottom controls and the scrollbar couldn't reach them.
- 84c9dbf: **Bleeding pawns no longer start a haul (on by default).** A pawn that's bleeding above a small threshold won't _start_ a new scoop or bulk-haul sweep — it should get treated, not detour to tidy up. This only blocks _starting_ a haul: a pawn already carrying scooped goods still unloads them normally, and explicit Strip orders you give still scoop their gear. Ported from "While You're Up". Turn it off in settings if you prefer the old behavior.
- 84c9dbf: Spoiling-First ingredient selection: when a colonist picks ingredients for a bill, they now reach for the rottable ingredient closest to spoiling, cutting overall waste. Two independent toggles, both default ON — Butcher (the most-spoiled corpse is butchered first) and Cook (meals, pemmican and kibble use the most-perishable food first). Recipe satisfaction, the ingredient search radius, multi-slot meals (meat + veg), and non-perishable crafts (steel, cloth, chemfuel, leather) are all unaffected; frozen food is left for last.
- 84c9dbf: **Storage-building permit/deny filters (opt-in, off by default).** Ported from "While You're Up": choose which storage _buildings_ Hauler's Dream's opportunistic behaviors (en-route pickup, storage routing) may use, via a per-mod foldable rules dialog. Includes curated defaults for known storage mods and special handling for LWM's Deep Storage (kept out of opportunistic hauls because of its slower access). The filter never blocks a pawn from putting its load away (the unload path is always allow-all), so it can't strand anything. One shared filter drives every behavior. Enable it in **En-route & Routing** in settings.
- 84c9dbf: **Consumer-aware storage routing (opt-in, off by default).** Ported from "While You're Up"'s "haul before carry": before a pawn carries a resource to a build site or crafting bill, it can relocate the largest nearby stack of that material to storage _closer to the consuming job_, so future fetches are short — plus optional same-/equal-priority relocation. Four sub-toggles (supplies, ingredients, equal-priority, stockpiles). It's carefully guarded so it never double-acts with Hauler's Dream's own build-from-inventory / batch-craft / bulk systems, and it stands down rather than risk a double-haul. Enable it in **En-route & Routing** in settings.
- 84c9dbf: Full Vehicle Framework compatibility (optional, reflection-only soft dependency — inert and byte-identical when Vehicle Framework is not installed, and gated behind a master toggle that defaults on).

  - **Bulk-load vehicle cargo.** Colonists load a vehicle's designated cargo the same way they bulk-load transporters and portals — sweeping many stacks into inventory and depositing them in one trip, with idle haulers splitting a single manifest via the shared claim-ledger. It works autonomously the moment you set a vehicle's cargo (HD upgrades the framework's single-stack loader in place), and a right-click "Prioritize bulk loading" is available too. Aerial vehicles load identically. Deposits go through the framework's own event-correct path and are clamped to exactly what you ordered (stuff/quality-precise), so a mixed manifest is never over-loaded.
  - **All existing features understand vehicles.** A hungry colonist will eat from a parked vehicle's cargo, a builder will pull construction materials from one, and pack-animal loading routes into a vehicle's cargo when one is present (now event-correct). Defensive guards stop a vehicle from being mistaken for a pack animal by the bulk-unload option, and skip a colonist who is riding inside a vehicle as a food/material source.
  - **Configurable.** A master "Vehicle Framework integration" toggle plus a "Bulk-load vehicles" sub-toggle, both default on. The safety guards always apply when Vehicle Framework is present.

### Patch Changes

- 84c9dbf: Robustness pass (mostly internal): clearer diagnostics and a settings-integrity guard. Hauler's Dream now logs a one-line warning when a supported mod (Combat Extended, Vehicle Framework, Common Sense) is present but an expected member it integrates with isn't found — so partial incompatibilities show up in the log for bug reports instead of failing silently. Corpse-hauling auto-strip now follows the same race-eligibility rule as every other auto-haul (so it correctly includes Haul-trained animals when "allow animals" is enabled). A build-time check now guards the 108 settings against default-value drift across their save/reset wiring.
- 84c9dbf: Internal hardening (no behavior change): the per-tick caches Hauler's Dream clears on game load are now tracked by a self-registering registry instead of a hand-maintained list, so a future cache can't be forgotten and leak stale data across a save/load. This also closes two pre-existing gaps where the route-claim cache (cleared only indirectly) and the Common Sense compat cache (never cleared) could carry a stale value into a freshly loaded game.
- 84c9dbf: Internal hardening (no behavior change): consolidated several pieces of duplicated logic into single sources of truth so they can't drift apart in future edits — the overload capacity-gate and its movement-speed penalty now derive their pawn set from one shared rule (guarded by a test so the "extra capacity costs speed" balance can't silently break), and the various hauling-eligibility job-def sets, carrier-liveness check, and "is Hauler's Dream active on this map" gate are now defined once and reused.
- 84c9dbf: Internal hardening (no behavior change): the three bulk-loading jobs that fill transporters/shuttles, map portals, and Vehicle Framework vehicles shared a near-identical multi-phase scaffold — sweep loose stock into the backpack, carry it to the target, then deposit while conserving exact item counts. That scaffold is the most safety-critical code in the mod (a mistake means lost or duplicated player cargo), and the three copies had already begun to drift apart. It is now a single shared base class (`JobDriver_LoadInBulkBase`) with only the genuinely target-specific deposit step left per job, removing ~640 lines of duplicated logic and the drift risk. The pack-animal loader and the carrier-unload job were deliberately left separate (they predate this design and differ too much to fold in safely). Save games and in-game behavior are byte-for-byte unchanged.
- 84c9dbf: Internal hardening (no behavior change): the inventory "self-heal" that decides which carried stacks Hauler's Dream owns — the single most load-bearing piece of logic in the mod — and the vein-mining route-extension decision are now pure, unit-tested functions in the Core library instead of being tangled inside Verse runtime code. This adds 32 oracle tests pinning the historically bug-prone cases (a single scoop landing across several inventory stacks, a stack merge destroying a tag's last reference, a Simple Sidearms weapon that must never be auto-unloaded, a harvested-vs-personal medicine def overlap, and the per-tick re-heal gate) so a future edit can no longer silently regress them. The runtime behavior is byte-for-byte identical.
- 84c9dbf: Internal hardening (no behavior change): the two largest source files are split into focused `partial class` files by concern. The game component that bundled five unrelated subsystems — the bulk-load claim ledger, batch-bill config, the softlock-drop driver, the vein-reveal driver, and the idle backstop — now lives across one file per subsystem, each with its own scribe block, so editing one can no longer accidentally disturb another's save logic. The 1290-line settings class likewise moves its ~480-line GUI into a separate partial file, leaving the model and persistence on their own. Because each type remains a single compiled class with identical fields, scribe labels, and scribe order, save games and in-game behavior are byte-for-byte unchanged.
- 84c9dbf: **Mechs put down what they're carrying before charging.** A mechanoid (e.g. an Agrihand that auto-hauled its own harvest) that goes to a charger while still carrying picked-up goods now delivers them to storage first — or drops them nearby if there's nowhere to put them or it's very low on energy — so the goods don't spoil or take up its carrying capacity while it sits on the charger. Governed by the existing "Free trapped cargo from stuck pawns" setting (on by default).
- 84c9dbf: Batch crafting now respects a bill's "Do until you have X" target, "Pause when satisfied", and "Unpause at" settings. Because HD's batch driver banks freshly-made products in the crafter's inventory (to deliver a whole batch in one trip), and RimWorld's product counter can't see pawn inventory, the batch never noticed the target was reached — colonists kept crafting past it and the bill never paused. The target count now includes the in-flight products colonists are carrying toward storage, so a batch stops at the target (across the whole colony, not just the crafting pawn) and the bill pauses on delivery exactly like a normal one-at-a-time bill. Repeat-count and repeat-forever bills were unaffected and are unchanged.
- 84c9dbf: Common Sense compatibility: Hauler's Dream now detects when Common Sense's "haul ingredients" / "advanced cleaning" takes over the vanilla crafting flow and steps aside, fixing the rare infinite loop where a crafter would repeatedly pick up ingredients, walk to the bench, then unload them again. Also hardened HD so it never ships a bill's ingredients off to storage while the crafter is about to consume them — protecting against any mod that rewrites the bill flow.
- 84c9dbf: End-to-end polish for the player-feedback features:

  - **"Pick up X" now works on any item.** Previously, right-clicking an item already in its best stockpile (or with no accepting storage) showed the option but did nothing. It now reliably picks the stack into the pawn's inventory regardless of storage (matching Pick Up And Haul) — still tracked, so it gets put away later.
  - **The per-pawn "Auto-haul yields" toggle is reachable while drafted** (it's a standing preference; a drafted pawn still won't scoop), and it no longer appears on animals that can't be Haul-trained (e.g. cats).
  - **Auto-strip respects the toggle** — a pawn with auto-haul off no longer pockets stripped corpse loot.
  - **Honest setting tooltips** — the animal-hauling tooltip now states only Haul-trained animals benefit; the mechanoid tooltip names harvest/mine/deconstruct-salvage scooping and points to the inventory-delivery setting; the crafting-share tooltips note the automatic stand-down while Common Sense is active; the carry-limit tooltip clarifies the Pick Up And Haul mass parity.
  - Minor allocation cleanup in the bulk-haul work scan.

- 84c9dbf: Player-feedback fixes & polish:

  - **Microstutters fixed.** The automatic bulk-haul planner no longer runs its full "is this sweep worth it?" computation (and allocations) for every loose item a colonist considers — a cheap allocation-free pre-check rejects the common no-sweep case first, the cross-pawn claim scan is cached per tick, and scratch buffers are reused. Smooths the camera/character jitter on cluttered maps with several haulers.
  - **Bulk-sweep keeps tidy stacks.** When a pawn sweeps many loose stacks of the same thing (e.g. scattered harvested food) into its inventory, they now consolidate into one stack instead of staying as many small ones — without ever merging into the pawn's own carried/personal stock.
  - **Crafting-loop conflict (with Common Sense) fully closed.** Completes the Common Sense compatibility: the last ingredient-sharing path now also cedes to Common Sense when it owns the crafting flow, so the "gather → walk to bench → turn back → empty inventory" loop can no longer occur with both mods' default options on.
  - **"Allow mechanoids" setting now has a description** explaining it governs mech scooping/hauling (vanilla mech construction delivery is separate).
  - Clarified the carry-limit setting tooltip: the limit is a mass budget over apparel + equipment + inventory; items carried in the hands are not counted.

- 84c9dbf: Fixes for reported issues:

  - **Batched crafting now sets the ingredient on the table.** When a pawn ran a _batched_ production bill (butchering, stonecutting, cooking, drug lab, etc.) it crafted the item straight out of its inventory and never placed the corpse / chunk / ingredient on the worktable. It now carries each ingredient to the bench and sets it down before working — matching vanilla, across every batched recipe — and the placed ingredient is reserved so another colonist can't grab it mid-craft. The whole-batch single gather trip is preserved.
  - **Explicit Strip orders are honored regardless of the per-pawn "Auto-haul yields" toggle.** A pawn with that toggle off now still scoops and hauls the gear from a Strip order you give it; the toggle continues to govern only autonomous yield scooping.
  - **Clearer strip settings.** Relabeled the auto-strip controls so "never" plainly means "don't strip _while hauling_", with cross-references making it obvious that manually-ordered strips still scoop and haul their gear via the separate "Stripping — removed gear" toggle (the two were always independent; the old labels implied otherwise).

- 84c9dbf: **Fix: colonists now correctly load the mech gestator.** Previously a colonist would pick up the ingredients, walk to the gestator, fail to deposit them, and carry them back to a stockpile. Autonomous worktables (the mech gestator family) deposit ingredients into the building's own container, which Hauler's Dream's gather-into-inventory routing couldn't satisfy — so those bills are now left on vanilla's native carry-in-hands-and-deposit flow. (Surfaces in combination with mods that act at job-toil transitions, e.g. Grab Your Tool!.) Normal workbenches are unaffected. The subcore scanner was never affected by Hauler's Dream.
- 84c9dbf: Hardened against future RimWorld updates: Hauler's Dream now applies each of its game patches independently, so if a single hooked vanilla method is renamed or removed in a future RimWorld build, only that one feature is disabled (with a clear log line) instead of the whole mod failing to load. Also made the partial-build "deliver from inventory" feature resolve its one reflected field lazily so a future rename degrades to vanilla behavior rather than erroring.
- 84c9dbf: Review fixes:

  - **Mixed-quality/material bulk transporter & portal loads now credit the correct manifest entry.** When a transporter or map-portal manifest held several entries of the same item at different quality or material, a bulk deposit could decrement the wrong entry, so that load would never read as "finished". Bulk loading now resolves each deposited item to its manifest entry with the exact same matcher vanilla uses (the vehicle path already did this), and the clamp, work-gate, and decrement all share that one matcher so they can't disagree. Single-entry manifests (the common case) are unchanged.
  - Hardening (no behavior change in normal play): the per-tick availability caches are now thread-local and cleared on quickload, matching the rest of the mod's caches; the two job-takeover Harmony patches have an explicit, pinned order; and a few inaccurate code comments were corrected.

- 84c9dbf: Performance: reduced micro-stutter on busy, heavily-modded colonies.

  A repo-wide allocation/CPU audit eliminated per-tick and per-scan heap allocations and redundant recomputation on the hottest paths (the usual cause of RimWorld gen0-GC micro-jitter):

  - The movement-speed overload penalty no longer re-walks a pawn's full apparel + equipment + inventory mass every cell it moves — it's computed once per pawn per tick.
  - Removed per-frame work and a game-state side effect from the inspect pane when a loaded pawn is selected.
  - Eliminated boxed enumerators and throwaway collections from the haul/load work scans, and per-call reflection allocations in the Combat Extended / Common Sense / Vehicle Framework integrations.
  - Various smaller allocation cleanups (debug logging, spoiling-first sort, route selection).

  Also adds an allocation-assertion performance test harness (`bun run test:perf`) that keeps the pure decision logic provably allocation-free going forward.

## 1.2.0

### Minor Changes

- f3fc4f6: **New "Batch" bill mode — make a whole batch of a bill in one work session, with a single ingredient trip.**

  Crafting and cooking bills now have three extra options in the repeat-mode dropdown, next to vanilla's "Do X times / Do until X / Do forever":

  - **Batch: do X times**
  - **Batch: do until X**
  - **Batch: do forever**

  When a bill is set to batch, the colonist fetches enough ingredients for the whole batch in **one trip**, makes them all at the bench one after another, then hauls everything to storage in one go — exactly the "plan prioritized crafting" flow, but automatic and per-bill. Because each item finishes individually, an interruption (drafting, power/fuel loss) only ever loses the single in-progress item, never the whole batch. If the bill's own count is reached partway through a batch (e.g. "Batch 10, until 40" when you're already at 35), only the remaining 5 are made and any unused ingredients are carried back to storage with the products.

  **Food doesn't spoil while the colonist is working.** Raw ingredients carried for the batch are frozen for the duration of the bench work, then resume spoiling normally while walking to and from the bench — so a big cooking batch won't rot the ingredients mid-session.

  **Setting the batch size.** Pick "Batch size: N…" from the same dropdown to set a per-bill amount with a slider. A new mod setting, **"Batch new bills by default"** (off by default), makes every newly-added batchable bill start in batch mode at a configurable **default batch size**, so you don't have to set it each time.

  Applies to ordinary production bills (cooking, tailoring, simple crafting, etc.). Recipes that build an "unfinished thing" — sculpting, complex components, advanced weapons/armour — are not batched, because they already keep their progress across interruptions in vanilla.

## 1.1.4

### Patch Changes

- 55e3cac: **Fix "plan construction" pawns topping off at the stockpile after every single wall.**

  When you planned a construction route over a wall line, the pawn would pick up a big load of
  material, build **one** wall, walk all the way back to the stockpile to top off, build **one
  more** wall, and repeat — a pointless shuttle that defeated the whole point of carrying a batch.

  Two underlying causes are fixed:

  - The inventory-delivery driver decided whether to walk back to the stockpile by comparing what it
    carried against the **whole route's** remaining demand. Since a single carry can never hold an
    entire wall line, and the mass headroom reopened after each wall was filled, it tripped back to
    the stockpile after **every** deposit. It now decides based on the **immediate** frame's need:
    while the pawn still carries enough for the wall in front of it, it builds straight from
    inventory and only returns to the stockpile when it genuinely runs low — roughly one trip per
    carry-load instead of one per wall. When it does re-load, it still fills to its full smart-carry
    ceiling, so the "few trips" benefit is preserved.

  - For walls that need **more than one material** (e.g. wood **and** steel), only the first material
    was gathered for the whole route; the others were re-fetched one wall at a time. The build tether
    now carries the whole route's remaining demand for every material, so steel/components batch the
    same way wood does.

  Haul-only routes, the "haul materials to site" order, plain right-click "prioritize constructing",
  and single large deliveries (e.g. a 340-steel generator) are unchanged. No save-compat impact.

- 704fe59: **Fix a hauled weapon being kept (and never put away) when it matches a Simple Sidearms sidearm's type.**

  The previous Simple Sidearms fix kept _any_ carried weapon whose type+material matched one of a colonist's
  sidearms. So if a colonist with, say, a steel ikwa sidearm was told to "haul everything nearby" and that included
  a loose steel ikwa, it kept _both_ — the unload job found nothing to do and flickered away, leaving the hauled
  ikwa stuck in the colonist's pack.

  Now Hauler's Dream keeps exactly as many of each weapon type+material as Simple Sidearms actually wants
  (it tracks sidearms by count), and treats any extra copies as normal haulable loot:

  - A loose steel ikwa hauled while carrying a steel ikwa sidearm → the sidearm is kept, the spare is put away.
  - A loose _plasteel_ ikwa hauled while carrying a _steel_ ikwa sidearm → the steel one is kept, the plasteel
    one is put away (it matches on material, not just type).
  - The spare stays tracked, so it still gets put away later even if the colonist is interrupted or drafted
    in the meantime.

  It also now always puts away the **actual hauled (or freshly-crafted) weapon**, never the equipped one — even
  when the equipped sidearm is higher quality. Previously the auto-pickup and inventory-crafting paths could tag
  the colonist's own sidearm by weapon type, so a colonist carrying a 99%-quality steel ikwa that hauled a
  3%-quality steel ikwa could end up storing the _good_ one and keeping the _bad_ one. Now it tracks and stores
  the specific item it just picked up or made, so the equipped sidearm is always the one kept.

  **Most importantly,** it fixes the case where the matching weapon is the colonist's **equipped main weapon**
  (their primary), not a pack sidearm. Simple Sidearms records the equipped primary in its remembered-weapons
  list, but that weapon lives in the _equipment slot_, not the pack — so Hauler's Dream was counting it toward
  the keep total while never seeing it in the inventory count. The result: a hauled weapon matching your colonist's
  equipped weapon computed surplus = `inventory(1) − remembered(1) = 0` and was **never unloaded** — it sat stuck
  in the pack (or, on a "haul everything nearby", got scooped into the pack and never taken back out). Hauler's
  Dream now subtracts the equipped primary from the keep total (mirroring Simple Sidearms' own unload logic), so a
  hauled weapon matching your equipped weapon is correctly put away while the equipped weapon is untouched.

  A diagnostic line (gated behind the mod's _verbose logging_ setting) now reports the surplus math for carried
  weapons, to make any future Simple-Sidearms edge case easy to pin down from a log.

  No change when Simple Sidearms isn't installed.

- ef74084: **Fix colonists occasionally stopping work to unload their own Simple Sidearms sidearm.**

  A remembered Simple Sidearms weapon could be hauled off to storage (and immediately re-fetched by Simple
  Sidearms) when a colonist happened to be carrying loose weapons that shared a ThingDef with one of its sidearms.
  Because weapons don't stack, Hauler's Dream's "same-def" inventory bookkeeping was mistaking the pawn's own
  sidearm for hauled loot of the same type and marking it surplus.

  Now a genuine remembered sidearm (matched precisely by weapon + material) is never treated as surplus: it is
  protected both where Hauler's Dream tags carried items and in the keep check itself, so it always wins over a
  mistaken tag. Loose weapons the colonist actually picked up off the ground are still put away normally, and
  nothing changes when Simple Sidearms isn't installed.

- 64b72e5: **Fix pawns freezing in the "unloading inventory" job over Yayo's Combat 3 ammo (and harden the unload against any item it can't move).**

  A colonist returning from a caravan could get stuck standing in the "unloading inventory" state; manually
  dropping their Yayo's Combat 3 ammunition fixed it. Cause: Hauler's Dream only recognised Combat Extended
  ammo as "keep in inventory", so it treated YC3 ammo as surplus and kept trying to haul it off — fighting
  YC3 (which re-stocks the pawn's ammo), and churning the unload job.

  - **Yayo's Combat 3 ammo is now kept in inventory** (auto-detected, no setup, nothing changes if you don't
    run YC3), the same way Combat Extended ammo already was. A pawn's own ammo is never hauled to storage; HD
    only ever moves _loose_ ammo it scooped off the ground. If you actually want a pawn's ammo put away, the
    per-item "always unload" rule in mod options still overrides this.

  - **The unload job can no longer get stuck on a single item it can't move.** If something can't be taken out
    of a pawn's inventory (another mod is holding it, or the pawn's hands are momentarily full), the pawn now
    skips it and unloads the rest, instead of standing in place retrying the same item. The skipped item keeps
    its place in the queue and is retried later — and still raises the "cannot unload" alert if it's genuinely
    stuck — so nothing is silently abandoned. This also covers carried grenades (More Useful Grenade) and any
    other mod that keeps combat consumables in a pawn's inventory.

  If you have an existing save where ammo got dropped during this bug, it will be picked back up by YC3 as
  normal.

## 1.1.3

### Patch Changes

- e905a9a: **Harden the "cannot unload inventory" alert so a bug in it can never blank the game's UI.**

  The black-hole safety-net alert (`Alert_CannotUnloadInventory`) recomputes its report on the UI render
  path — RimWorld calls it when you hover or click the alert, and the vanilla alerts readout does _not_
  wrap that call in a try/catch. So if that recompute ever threw an exception, it would abort the rest of
  the frame's UI drawing before the window layer, leaving the whole HUD invisible-but-still-clickable until
  you moved off the alert. Its report code is now guarded: on any unexpected error it logs the problem
  loudly (so the bug is still reported, never silently swallowed) and falls back to its last good report,
  keeping the HUD alive.

  This is defensive hardening — no behaviour change in normal play. (Investigated alongside player reports
  of disappearing UI; the most likely causes of that symptom are an unrelated mod throwing on the UI layer
  or save corruption from swapping inventory mods mid-save, which a Player.log will pinpoint.)

## 1.1.2

### Patch Changes

- fe5369d: **Fix inventory unload loops with Simple Sidearms / Smart Medicine / Dub's Bad Hygiene, and stop pawns dropping un-haulable items at random spots.**

  - **No more unload↔pickup loops with mods that keep items in inventory.** Hauler's Dream used to treat a
    colonist's carried kit as "surplus" and ship it to storage, which mods like Simple Sidearms (remembered
    sidearms), Smart Medicine (stock-up medicine), Dub's Bad Hygiene (carried water), and Combat Extended
    (loadout ammo) would then immediately re-fetch — an endless drop-and-grab loop that could leave pawns walking
    back and forth until they collapse. Those items are now auto-detected (no extra setup, and nothing changes if
    you don't run those mods) and left in the pawn's inventory. Vanilla addiction/chemical-dependency drugs are
    kept too, matching vanilla.

  - **New "Individual Item Unload Settings" picker (mod options).** A stockpile-style categorized, foldable,
    searchable list where you set how Hauler's Dream treats specific items in a pawn's inventory — per item, choose
    **Never unload** (keep the whole stack), **Keep at most N** (carry up to N and unload the rest), or **Always
    unload** (put it away even if another mod would otherwise keep it). A rule overrides the auto-detected mod
    keeps above for that item. It's fully fallback-safe: choices for items from a mod you later remove won't break
    your save, and they're restored automatically if you reinstall the mod. (Built on the vanilla item tree
    directly, so it also no longer throws errors when opened from the main-menu mod options — the old picker used
    an in-game-only UI that spammed the log when no save was loaded.)

  - **Pawns no longer carry un-storable items to a random spot.** If a harvested/mined/deconstructed yield (or
    any swept item) has nowhere it can be stored, the pawn now leaves it on the ground where it was produced,
    instead of scooping it into inventory and later dropping it at a random home-area cell. Items are only picked
    up into inventory when there's actually somewhere to deliver them.

  - **"Unload foreign surplus" is now off by default.** Out of the box, Hauler's Dream only puts away goods it
    picked up itself — it never touches a colonist's sidearms, carried medicine/water, or traded goods. You can
    still turn this on (mod options) for the convenience of auto-hauling surplus a pawn is carrying for no reason;
    it's now safe with the supported mods. Existing saves keep whatever you had set.

  The red "Cannot unload inventory" alert still fires for anything that genuinely has nowhere to go, so nothing
  is silently stuck.

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
