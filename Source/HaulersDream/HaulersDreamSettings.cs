using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    // [StaticConstructorOnStartup]: this type holds static Texture2D fields (the settings header + per-category
    // icon caches, in the .Window.cs partial). RimWorld warns about any type with a static Texture2D/Material field
    // that lacks this attribute — a structural check that fires even though those textures are loaded lazily on the
    // main thread during the settings window draw. The attribute satisfies the check (its other static initializers
    // are plain data — translation KEYS, sizes, empty caches — so running the type initializer at startup is inert);
    // the texture fields stay null until the lazy properties first build them when the window opens.
    [StaticConstructorOnStartup]
    public partial class HaulersDreamSettings : ModSettings
    {
        // --- master enable (no restart): one switch to disable ALL of Hauler's Dream. Default ON. When OFF, HD
        // stops INITIATING new behavior (scoops / sweeps / bulk-haul / work-overrides) at the scoop entry points,
        // but a pawn already carrying scooped goods STILL unloads (never a black hole) and the Unload gizmo stays
        // available. Read live via MasterEnable.Active so it takes effect without a restart. ---
        public bool masterEnabled = true;
        // Dev-only: draw colored detour lines for en-route pickup / storage routing. DevMode only, NOT serialized,
        // reset to off on load (a transient diagnostic, never persisted).
        [System.NonSerialized] public bool drawDetourLines = false;

        // --- carry limit (the headline change: default = full max carrying capacity) ---
        public float carryLimitFraction = 1.0f;

        // --- unload defaults / sharing ---
        public bool markForUnload = true;     // automatic unloading (end of work run / checkpoints / full / interval); off = gizmo-only
        // Also put away surplus a colonist is carrying that HD did NOT scoop (trade/mod/manual stock), not just
        // HD-tagged loot. "Surplus" = above the pawn's keep-stock (food / drugs / inventoryStock / CE loadout +
        // vanilla addiction drugs + auto-detected Simple Sidearms / Smart Medicine / Dub's Bad Hygiene kept items),
        // and only stacks that have a real storage destination; caravan-loading inventory (IsFormingCaravan) is
        // left alone. DEFAULT OFF: out of the box HD only unloads what it scooped itself, so it never touches a
        // colonist's sidearms / carried medicine / water / traded goods — robust against every keep-mod, present
        // or future. Turn ON for the convenience of auto-hauling foreign surplus (e.g. traded jade) to storage;
        // the keep-item detection above keeps that safe with the supported mods.
        public bool unloadAllSurplus = false;
        // Per-item unload rules (mod options → "Individual Item Unload Settings", a stockpile-style categorized
        // picker). Each entry sets how HD treats one item def in a pawn's inventory: keep the whole stack (never
        // unload), keep at most N units (unload the excess), or always unload it — overriding HD's auto-detected
        // keep-mods and the global "unload surplus" toggle for that def. Stored as defName-keyed STRINGS encoded
        // "defName|modeInt|amount", NOT ThingDef refs: a modded item's rule survives the mod being removed (it
        // simply never matches a live item), is restored automatically if the mod returns, and can never break
        // save loading. The picker (Dialog_ItemUnloadSettings) edits a defName->rule dictionary built from this.
        public List<string> itemUnloadRules = new List<string>();
        [System.NonSerialized] private Dictionary<string, ItemUnloadRule> ruleMap; // lazy O(1) decode cache
        // Legacy (pre-1.1.x) "never unload" list — read once on load and migrated into itemUnloadRules as KeepAll
        // rules, then never written again. Kept only so older configs upgrade losslessly.
        [System.NonSerialized] private List<string> keepDefNames;

        // "Put it away before relaxing": when a pawn finishes its work run and is about to rest, recreate, or
        // eat, it makes its unload trip FIRST (bypassing the accumulate window), instead of carrying the load
        // to bed / the dinner table / the rec room. Continuous/intermittent work still accumulates — these only
        // fire once the pawn stops working and heads into the matching downtime. One toggle per activity.
        public bool unloadBeforeSleep = true;
        public bool unloadBeforeLeisure = true;
        public bool unloadBeforeEating = true;
        public bool shareForBuilding = true;  // carried materials count for construction
        // Build From Inventory: source construction material from ORGANIC inventory a pawn/animal is already
        // carrying (own → other colonists' → pack animals' / caravan cargo), not just HD-tagged scooped stock —
        // so steel carried in a caravan builds a sandbag/wall on a raid without first dropping it off a pack
        // animal. Extends the construction availability gate + the inventory-fetch to consider pre-existing
        // organic inventory; delivery uses the clean inventory→site HaulToContainer driver (no drop-at-feet).
        // Distinct from shareForBuilding (HD-tagged scooped stock only); when ON, the organic count REPLACES the
        // tagged count in the availability gate (organic is a superset) so no physical stack is counted twice.
        // DEFAULT ON: it only ADDS a source and (partial off) never starts an under-resourced frame, so it
        // delivers the headline use-case out of the box without changing vanilla's all-or-nothing build rule.
        public bool buildFromInventory = true;
        // Partial build from inventory: relax vanilla's "all materials must be present" gate — start/advance a
        // frame from whatever inventory stock exists even if it's less than the frame's full need (the rest is
        // delivered as more becomes available). Changes vanilla's all-or-nothing semantics (a vanilla-semantics
        // change, like unloadAllSurplus / shareHandHauledToStorage / batchByDefault), so OFF by default. Requires
        // buildFromInventory.
        public bool buildFromInventoryPartial = false;
        public bool shareForCrafting = true;  // carried ingredients count for crafting bills
        // Auto crafting bills: gather all ingredient stacks into inventory in one (overweight) sweep, then let
        // VANILLA's own DoBill craft from the carried stock (via the crafting-share relay). The earlier direct-craft
        // design was retired for a dup risk; this prep-gather design never touches the recipe flow.
        public bool inventoryCraftDeliver = true;
        public bool shareMeetInMiddle = true; // an idle carrier walks toward the fetcher
        public bool batchWorkDeliveries = true; // carry materials for many queued builds per trip (in hands; no slowdown)
        public bool inventoryConstructDeliver = true; // carry material for a big single needer in inventory (fewer trips)
        // Multi-site construction delivery (extends inventoryConstructDeliver to the nearby CLUSTER): when vanilla
        // batches several same-material build sites within 8 tiles, the AUTOMATIC and shift-click deliver paths
        // load the whole cluster's combined demand into inventory in ONE trip and deliver to each site, instead of
        // serving one site per trip (today only the route planner gathers for multiple sites at once). Respects the
        // "finish the current site first" rule (it only raises the load ceiling + iterates needers; it never changes
        // WHEN the pawn decides to load). Requires inventoryConstructDeliver. Default ON.
        public bool multiSiteConstructDeliver = true;
        public bool shareHandHauledToStorage = false; // let a worker claim a stack a colonist is hand-hauling TO STORAGE (opt-in)
        // Meals On Wheels: a hungry FREE COLONIST with no food found by vanilla eats acceptable food
        // another player-faction pawn (or pack animal) is carrying, instead of trekking to a distant
        // stockpile — fewer trips. Only when vanilla's own food search came up empty (never overrides a
        // good map/own-inventory/pack-animal meal). Stricter than the reference mod: drafted/downed/mental
        // HOLDERS are skipped, a baby's food being hand-fed is left alone, the STACK is reserved (not just
        // the holder) so two eaters can't race one meal, and rot-priority prefers a carried meal about to
        // spoil. Acceptability (food policy / ideology / teetotaler) is delegated to vanilla.
        public bool mealsOnWheels = true;

        // --- haul to stack (prefer topping up existing stacks; destination cells unreserved) ---
        public bool haulToStack = true;

        // --- ordered construction (F16): right-click orders tether haul+build; haul-to-site menu option ---
        public bool orderedConstructTether = true; // "prioritize constructing" hauls AND builds as one task
        public bool haulToSiteOption = true;       // the "Prioritize hauling materials to X" right-click order

        // --- planners (the right-click "Plan prioritized …" tools) ---
        public bool planRoutes = true;   // route planner (harvest/mine/clean/deconstruct/construction routes)
        // "Remember plan" interface toggle (bottom-right play-settings row) — the MASTER SWITCH for one-click
        // remembered routes. When ON (the default), a target type that has an explicit remembered template (saved via
        // the "Remember" button in a plan dialog — see rememberedRoutesByDef) shows "Plan prioritized … (remembered)"
        // and runs that template in one click; when OFF, the planner dialog always opens. The toggle alone is NOT
        // enough: with no saved template for a type its option stays the plain "Plan prioritized …" (opens the dialog),
        // even while the toggle is on — so ON-by-default never one-clicks a plan the player never chose to remember.
        // Lives on the interface toggle only (not the settings window, hence not drawn there).
        public bool rememberPlan = true;
        public bool planCrafting = true; // station crafting planner (batch a bill N times in one go)
        // Batch-Y bill mode: when ON, a newly-created batchable bill starts in batch mode at defaultBatchSize.
        public bool batchByDefault = false; // OFF by default so existing players see no change until they opt in
        public int defaultBatchSize = 10;   // initial per-batch quantity for a freshly-batched bill
        // Common Sense compat: HD normally cedes the whole DoBill flow to Common Sense (its cleaning / haul-all
        // toils re-deposit HD's gathered ingredients and would loop). Batch crafting is a SEPARATE driver
        // (HaulersDream_BatchCraft) Common Sense never patches, so it can run safely under CS. ON by default so
        // batch-flagged bills still batch under Common Sense; the looping inventory-gather and ingredient-share
        // paths stay ceded regardless. Turn OFF to hand the whole cook/craft flow to Common Sense (batching is
        // then suppressed and its dropdown options are hidden — see CommonSenseCompat.BatchSuppressedByCommonSense).
        public bool allowBatchUnderCommonSense = true;

        // --- bulk hauling (the native Pick-Up-And-Haul: a haul trip sweeps everything around into inventory) ---
        public bool bulkHaul = true;
        // Always = every haul sweeps; SecondTasked (default) = automatic hauls always sweep, but a player-ORDERED
        // haul sweeps only when a second nearby haul has also been ordered — so ordering one haul stays surgical.
        public BulkHaulTrigger bulkHaulTrigger = BulkHaulTrigger.SecondTasked;
        // The "Haul everything nearby" right-click order: start a bulk sweep directly (no need to prioritize two
        // hauls). Additive to vanilla "Prioritize hauling".
        public bool haulNearbyOption = true;
        // The "Pick up X" float-menu order on a haulable ground item (PUAH parity): a pawn with the comp picks
        // the clicked stack straight into its inventory as a TAGGED, forced HD bulk haul (serviced by the normal
        // unload), instead of vanilla's hand-haul-to-storage. Default ON for discoverability; tagged so it is
        // never a "black hole" even with auto-unload off. Additive to vanilla's right-click haul options.
        public bool manualPickupOption = true;
        // The "Keep X in inventory" float-menu order on a haulable ground item: take the clicked stack into the pawn's
        // inventory and HOLD it — HD never hauls it to storage and vanilla's drop-unused never sheds it (the sibling
        // of "Pick up X", which picks up to HAUL). For holding an item the pawn should carry (a mod's inventory item,
        // a caravan supply, a roleplay keepsake). Release it by consuming it or dropping it from the gear tab. Default
        // ON for discoverability; additive to vanilla's right-click options and shown alongside "Pick up X".
        public bool keepInInventoryOption = true;
        // When a single stack is too big to carry in one armful (e.g. 75 steel but the pawn can hold 72), take it
        // in the INVENTORY and deliver the whole stack in one trip, instead of hand-carrying a partial load and
        // leaving the rest behind. Applies to ordered and automatic single-stack hauls alike.
        public bool haulOversizedInInventory = true;

        // While a pawn scoops its own WORK yields (deconstruct/mine/harvest), also sweep OTHER loose haulable
        // items lying around the work spot into its inventory, so the area is cleared in the same consolidated
        // trip instead of being left for separate hand-hauls. (Bulk hauling above does the same for dedicated
        // HAUL jobs; this extends it to work jobs.)
        public bool sweepNearbyWhileWorking = true;

        // --- pack-animal loading on caravans / temporary maps ---
        public bool loadPackAnimalBulk = true;       // the manual "Load nearby items onto pack animal (bulk)" order
        public bool autoDivertToPackAnimal = true;   // an over-encumbered caravan pawn auto-loads the nearest pack animal

        // --- transporter / shuttle BULK LOADING (replaces vanilla's one-stack-per-walk transporter loading: claim a
        // slice of the group manifest, sweep nearby stacks into inventory, walk once, deposit them all). The ledger
        // splits a manifest across multiple haulers without double-haul; anti-conflict patches stop premature
        // board/launch + the false "loading stalled" alert + shuttle autoload churn while claims are live.
        public bool enableBulkLoadTransporters = true;
        // How often (ticks) the running load job re-validates that its carried stock is still wanted by the group
        // (mid-trip redirect within the group). 10..240; lower = more responsive redirect, higher = cheaper.
        public int bulkLoadAiUpdateFrequency = 60;

        // --- map-portal BULK LOADING (pit gates, cave / vault exits, "enter map" portals). Same claim-ledger + sweep
        // + deposit path as transporters, but the deposit teleports/consumes the Thing to the other map (the portal's
        // PortalContainerProxy), so there is NO group mass cap and the ledger settle is thing-less. Independent of the
        // transporter toggle so the two vanilla single-item paths can be replaced (or left) separately.
        public bool enableBulkLoadPortal = true;

        // --- Vehicle Framework (VF) compat. enableVehicleFramework is the MASTER: it gates only the NEW opt-in VF
        // features (the bulk-load-into-vehicle WorkGiver/float-menu/driver and the pack-animal event-correct deposit
        // redirect). It does NOT gate the safety/correctness guards (the [UC1]/[UC2] vehicle-skip and the MOW/ORG
        // embarked-holder skip) — those fix a PRE-EXISTING HD↔VF misfire and are gated on VehicleFrameworkCompat
        // .IsActive ONLY, so a feature toggle can never switch the bug back on. Eat-from / build-from a parked
        // vehicle's cargo stays governed by the existing mealsOnWheels / buildFromInventory toggles (no new gate),
        // and the master does NOT turn those off (turning the master off must never make HD break). With VF absent
        // both fields are inert (every VF consumer also gates on IsActive). enableBulkLoadVehicles is the SUB-toggle
        // for the active bulk-load feature; it requires the master.
        public bool enableVehicleFramework = true;
        public bool enableBulkLoadVehicles = true;

        // --- Storage Network (BlackMouse) compat — EXPERIMENTAL, default OFF. SN is a virtual/digital storage whose
        // items are DESPAWNED inside its server/terminal buildings, so HD's normal bulk-load sweep (loose + spawned
        // storage) can't see them and a network-stored manifest degrades to vanilla one-stack loading. When this is
        // on AND SN is installed, HD adds the network's stacks to the bulk-load plan and lets SN's own on-demand
        // auto-spawn materialise them at a terminal. Inert when off or when SN is absent (StorageNetworkCompat
        // .IsActive). Opt-in because it depends on an alpha, closed-source mod's auto-spawn behaviour.
        public bool enableStorageNetworkBulkLoad = false;

        // --- bulk REFUEL (replaces vanilla's one-stack-per-walk refuel of a CompRefuelable — a shuttle's chemfuel, a
        // deep drill, a generator, …): a hauler sweeps enough nearby fuel into inventory, walks to the refuelable
        // ONCE, and fills it in one trip. No shared manifest/ledger (vanilla CompRefuelable.Refuel just consumes up to
        // the deficit), so it's a standalone path; only fires when 2+ stacks/trips are needed (a single-stack refuel
        // is already one trip in vanilla). Atomic refuelables are left to vanilla RefuelAtomic.
        public bool enableBulkRefuel = true;

        // ===== Bulk Load For Transporters parity (added 2026-06) =====
        // A1: clean up HD bulk-load/haul jobs at SAVE time so a save written mid-load survives uninstalling HD
        // (no dead custom-JobDriver refs left in the save). Safe-uninstall data integrity; ON. It mutates job
        // state at save time, so it is behind a toggle, but the safe default is ON.
        public bool cleanupOnSave = true;
        // A2: drop HD-tagged items from a pawn that can no longer haul (hauling disabled, or a mech charging /
        // dormant / self-shutting-down) so swept cargo is never trapped forever. Anti-softlock recovery; ON.
        public bool enableSoftlockDrop = true;
        // B1: a pawn already carrying needed cargo diverts to deposit it into the nearest needy transporter /
        // portal / vehicle, instead of starting a fresh pickup. Behavior-changing autonomous trigger; OFF.
        public bool enableOpportunisticLoad = false;
        public int loadOpportunityScanRadius = 30;
        // B4: one right-click "load until complete" chains a (drafted) courier through every nearby transporter
        // group that still has loading work. Behavior-changing manual convenience; OFF.
        public bool enableContinuousLoading = false;
        // B3: order bulk-load pickup stops by real pathfinding (re-rank the top-N straight-line candidates)
        // instead of straight-line distance. Behavior-changing perf tradeoff on a hot scan path; OFF.
        public bool loadHybridPathing = false;
        public int loadPathfindingCandidates = 8;
        // C2: auto-open the Contents tab when a single transporter is selected (and the Gear tab for a carrier).
        // A UI override that changes the player's currently-open tab on selection; OFF.
        public bool autoOpenTransporterContents = false;
        public bool autoOpenCarrierGear = false;

        // --- pack-animal BULK UNLOAD (the inverse of loading: empty a flagged carrier into the hauler's backpack
        // in ONE visit, then HD's normal unload ships it to storage). Replaces vanilla's one-stack-per-walk unload.
        public bool enableBulkUnloadCarriers = true; // route WorkGiver_UnloadCarriers through the bulk-unload job
        // The hauler must have at least this fraction of its carry capacity free to START a bulk unload (else the
        // backpack overflows to hands immediately and the visit barely helps). 0.5 = at most 50% encumbered.
        public float minFreeSpaceToUnloadCarrierPct = 0.5f;
        // Reserve the carrier exclusively while unloading. OFF (default) = non-exclusive, so roping / caravan
        // formation / a second hauler can still interrupt (the driver's other-claimant check yields cleanly).
        public bool reserveCarrierOnUnload = false;
        // Per-stack visual pause (ticks) so the bulk unload reads as a deliberate action, like vanilla's per-stack
        // unload cadence. 0 = instant. (Reused name for a future shared load+unload delay.)
        public int visualUnloadDelay = 15;

        // --- auto strip on haul (corpse hauls strip the body; loot rides in the hauler's inventory) ---
        public AutoStripMode autoStripMode = AutoStripMode.AllHauls;
        public bool stripColonistCorpses = false;              // your own dead are not loot (opt-in)
        public TaintedApparelPolicy taintedSmeltablePolicy = TaintedApparelPolicy.Take;
        public TaintedApparelPolicy taintedNonSmeltablePolicy = TaintedApparelPolicy.Take;

        // --- haul after slaughter (a finished kill hauls the fresh carcass to storage so it doesn't rot) ---
        public bool haulWildKills = true;       // hunted (wild) carcasses — appends a haul on the hunter ONLY when the hunt was interrupted after the kill (JobDriver_Hunt finish action, non-Succeeded); a clean hunt self-hauls (vanilla), so HD stays out — no double-haul
        public bool haulTamedSlaughter = true;  // slaughtered (tamed) carcasses — appends a haul on the slaughterer (slaughter doesn't self-haul)

        // --- spoiling-first ingredient selection (prefer the most-perishable already-valid candidate, to
        // reduce overall spoilage). Two independent toggles, both default ON. They only REORDER preference
        // among already-valid candidates — never change recipe satisfaction, the bill's ingredientSearchRadius
        // / filters, or non-rottable crafts (steel/cloth/etc. are byte-identical). Among rottable+Fresh
        // candidates the one closest to spoiling (lowest CompRottable.TicksUntilRotAtCurrentTemp) is preferred. ---
        public bool butcherSpoilingFirst = true; // corpses chosen for a butcher bill: most-spoiled first
        public bool cookSpoilingFirst = true;    // rottable non-corpse food chosen for a cook bill: most-spoiled first

        // --- smart overload (carry past 100% capacity to save trips) ---
        // 0 = no slowdown (carry freely) ... FairLevel = balanced ... OffLevel = never overload.
        public int overloadLevel = OverloadTuning.FairLevel;

        // Strict carry weight: never go past 100% capacity (overrides overload to off), and don't break
        // off to unload when full — keep working and leave the surplus for normal hauling.
        public bool strictCarryWeight = false;

        // Keep working when full (opt-in, DEFAULT OFF so existing saves/behaviour are byte-identical until
        // opted in): when a pawn doing WORK (mining/harvesting/deconstructing) reaches its carry ceiling, it
        // does NOT break off to unload — it keeps working and overflow yields drop on the ground for normal
        // hauling. It only sheds the load before a LONG relocation: when its next work target is farther than
        // the dropoff (a weighted rule — see KeepWorkingPolicy). Downtime/idle/interval/end-of-run unloads
        // are UNCHANGED, and dedicated haul/load jobs still deliver when full. Distinct from strictCarryWeight,
        // which also caps at 100% capacity (this keeps the overload-and-accumulate ceiling intact).
        public bool keepWorkingWhenFull = false;
        // Hysteresis margin (tiles) for the keep-working unload-before-next-relocation rule: the next work
        // target must be farther than the dropoff by at least this many tiles before the full pawn detours to
        // unload. Avoids dithering when the two distances are nearly equal.
        public int keepWorkingMarginCells = 5;

        // Pass-by unload: when heading off on a long trip with a load and storage is on the way, drop it off.
        public bool opportunisticUnload = true;

        // ===== While You're Up parity (C1 — safety / strictly-better, default ON) =====
        // Don't START a new haul/scoop while a pawn is bleeding badly (it shouldn't detour into a sweep while
        // hemorrhaging). Gates INTAKE ONLY, at the explicit scoop entry points — a pawn already carrying a load
        // still unloads normally (never a "black hole"). OFF = vanilla HD (no health gate).
        public bool skipHaulWhileBleeding = true;
        // BleedRateTotal strictly above this (per-day rate) => unfit to start a haul. WYU parity = 0.001.
        public float bleedThresholdPerDay = 0.001f;
        // Order the consolidated unload visit by NEAREST destination first (empty one storage group fully before
        // walking to the next), instead of the category->defName order. Strictly-better; ON. OFF = existing order.
        public bool closestDestinationUnloadOrder = true;

        // ===== While You're Up parity (C2 — en-route pickup, default ON) =====
        // The signature WYU mechanic: when a pawn is about to start a job, and a loose haulable lies roughly
        // ALONG the way, grab it into inventory first (as a tagged HD bulk-haul pickup, serviced by the normal
        // unload) so the stray item rides to storage on a trip the pawn was making anyway. A behavior-CHANGING
        // feature that ships ON; when turned off, the postfix's first line returns, so it is fully inert.
        public bool enRoutePickup = true;
        // How strictly the "is the store roughly on the path?" check is confirmed after the cheap straight-line
        // ratio cascade. Vanilla = cheap bounded region-count flood (fastest, least accurate); Default / Pathfinding
        // = accurate A* path costs (Default ends the scan on a range failure, Pathfinding keeps scanning). DEFAULT
        // Vanilla here (the perf-conscious choice — the A* modes are opt-in), unlike WYU's own Default default.
        public EnRoutePathChecker enRoutePathChecker = EnRoutePathChecker.Vanilla;

        // ===== While You're Up parity (C3 — consumer-aware storage routing, default ON) =====
        // "Haul before carry": before a pawn carries a resource to a build site / crafting bill, relocate the
        // largest nearby stack of that material to storage CLOSER to the consuming job (so future fetches are
        // short), and grab same-/equal-priority extras. A behavior-CHANGING feature; the MASTER ships ON (when
        // off, StorageRouting's first line returns, so it is fully inert). The 4 sub-toggles default ON and are
        // inert while the master is OFF.
        public bool storageRouting = true;              // MASTER (default ON)
        public bool routeSupplies = true;               // relocate construction supplies closer to the build site
        public bool routeIngredients = true;            // relocate bill ingredients closer to the bench
        public bool routeToEqualPriority = true;        // allow relocating into an EQUAL-priority store (not just higher)
        public bool routeToStockpiles = true;           // plain stockpile zones are eligible relocation targets

        // ===== While You're Up parity (C4 — storage building permit/deny filter, default ON) =====
        // The SHARED filter (plan G4/G7): one object, one Scribe_Deep field, one dialog — read by en-route
        // (C2), before-carry routing (C3), and the permit/deny dialog (C4). MASTER toggle, default ON; when off,
        // StorageBuildingFilter.Enabled gates every query to allow-all and W3's funnel postfix early-returns
        // before any work, so the whole feature is fully inert when disabled.
        public bool storageFiltersEnabled = true;
        // When ON, the curated per-context default permit/deny sets apply (WYU "auto-manage"): opportunistic
        // = allow-all minus the slow set; before-carry = deny-all except a curated container allow-list.
        // When OFF, only the player's explicit overrides decide and everything else is allowed.
        public bool storageFilterUseDefaults = true;
        // Deny the "slow" storage set (LWM's Deep Storage) for opportunistic / before-carry hauls — a storing
        // DELAY there makes a stop not actually opportune. NEVER denies it for an unload (a carrying pawn must
        // always be able to put its load down). Inert under the OFF master.
        public bool storageFilterDenyLwmForOpportunistic = true;
        // The player's explicit per-building / per-mod overrides (deny beats allow beats the curated default).
        // The ONE serialized filter object — never add a second. null on an old save -> new (allow-all).
        public StorageBuildingFilter storageBuildingFilter = new StorageBuildingFilter();

        // --- plan-route dialog ---
        public int routeMaxAmount = 50;             // top of the "Amount" slider (before the "All" step); mod-wide
        // Seed defaults for a brand-new target type (carried into its per-type prefs the first time it's opened).
        public bool routeAllowHarvest = true;       // include nearby unmarked plants in a harvest route
        public int routeGrowthThreshold = 80;       // ...as long as they're at least this % grown
        // --- route calculation (how a route decides WHICH targets to keep + how it MEASURES distance) ---
        public RouteSelectionMethod routeSelectionMethod = RouteSelectionMethod.MostStopsPerTravel; // global default (per-route overridable)
        public RouteDistanceBasis routeDistanceBasis = RouteDistanceBasis.StraightLine;             // global default (per-route overridable)
        public int routeOrderExactMax = RouteOrderPolicy.ExactMax; // visiting-order: solve EXACTLY at/under this many stops (8..14); global
        // Per-target-type dialog options (keyed by the clicked thing's ThingDef.defName) so each kind of target
        // remembers how you last routed it. See RouteDialogPrefs.
        public Dictionary<string, RouteDialogPrefs> routePrefsByDef = new Dictionary<string, RouteDialogPrefs>();

        public RouteDialogPrefs GetRoutePrefs(string defName)
        {
            if (defName == null || routePrefsByDef == null)
                return null;
            return routePrefsByDef.TryGetValue(defName, out var p) ? p : null;
        }

        public void SetRoutePrefs(string defName, RouteDialogPrefs prefs)
        {
            if (defName == null || prefs == null)
                return;
            if (routePrefsByDef == null)
                routePrefsByDef = new Dictionary<string, RouteDialogPrefs>();
            routePrefsByDef[defName] = prefs;
        }

        // Explicit "remembered" route templates — a SEPARATE layer from routePrefsByDef above. routePrefsByDef is the
        // per-instance auto-restore the plan dialog writes on EVERY close (so the window reopens the way you left it);
        // these dictionaries are written ONLY when the player presses the "Remember" button in a plan dialog. Their
        // presence is what makes a specific type's right-click menu read "Plan prioritized … (remembered)" and run in
        // one click — rememberPlan (the interface toggle) is the master switch that must ALSO be on. Keyed by the
        // specific type: the target ThingDef (thing route), the growing zone's plant ThingDef (sow), or the clicked
        // cell's floor TerrainDef (remove-floor).
        public Dictionary<string, RouteDialogPrefs> rememberedRoutesByDef = new Dictionary<string, RouteDialogPrefs>();
        public Dictionary<string, SowRouteTemplate> rememberedSowRoutesByDef = new Dictionary<string, SowRouteTemplate>();
        public Dictionary<string, RemoveFloorRouteTemplate> rememberedRemoveFloorRoutesByDef = new Dictionary<string, RemoveFloorRouteTemplate>();

        public RouteDialogPrefs GetRememberedRoute(string defName)
            => defName != null && rememberedRoutesByDef != null && rememberedRoutesByDef.TryGetValue(defName, out var t) ? t : null;

        public void SetRememberedRoute(string defName, RouteDialogPrefs template)
        {
            if (defName == null || template == null)
                return;
            if (rememberedRoutesByDef == null)
                rememberedRoutesByDef = new Dictionary<string, RouteDialogPrefs>();
            rememberedRoutesByDef[defName] = template;
        }

        public SowRouteTemplate GetRememberedSowRoute(string defName)
            => defName != null && rememberedSowRoutesByDef != null && rememberedSowRoutesByDef.TryGetValue(defName, out var t) ? t : null;

        public void SetRememberedSowRoute(string defName, SowRouteTemplate template)
        {
            if (defName == null || template == null)
                return;
            if (rememberedSowRoutesByDef == null)
                rememberedSowRoutesByDef = new Dictionary<string, SowRouteTemplate>();
            rememberedSowRoutesByDef[defName] = template;
        }

        public RemoveFloorRouteTemplate GetRememberedRemoveFloorRoute(string defName)
            => defName != null && rememberedRemoveFloorRoutesByDef != null && rememberedRemoveFloorRoutesByDef.TryGetValue(defName, out var t) ? t : null;

        public void SetRememberedRemoveFloorRoute(string defName, RemoveFloorRouteTemplate template)
        {
            if (defName == null || template == null)
                return;
            if (rememberedRemoveFloorRoutesByDef == null)
                rememberedRemoveFloorRoutesByDef = new Dictionary<string, RemoveFloorRouteTemplate>();
            rememberedRemoveFloorRoutesByDef[defName] = template;
        }

        // --- station crafting planner ---
        public float craftBatchTimeoutHours = 2f;   // default wall-clock cap for a batch (0 = no limit); per-batch overridable

        // --- work-incapability overrides (OFF = vanilla). When on, no backstory/trait/gene/title/role/
        // hediff/quest incapability can block that work — for vanilla (work tab, prioritize, work scan)
        // AND this mod's planners alike. See WorkOverride.
        public bool allPawnsCanHaul = false;
        public bool allPawnsCanClean = false;
        public bool allPawnsCanCutPlants = false;

        // --- who ---
        public bool pauseWhileDrafted = true;
        public bool allowMechanoids = true;
        // Multiplier on a player MECHANOID's carrying capacity for HD's hauling, so a modded high-capacity lifter
        // mech hauls proportionally more per trip. 1.0 = the mech's true carrying capacity, unchanged (default —
        // byte-identical). Applied to the SINGLE capacity value both the overload ceiling and the move-speed
        // slowdown read (via CarryCapacity / PawnMassCache), so the "carry more / move slower" bargain stays in
        // lockstep. Inert under Combat Extended (CE owns the encumbrance model). Only affects player work mechs.
        public float mechHaulMultiplier = 1.0f;
        // Let colony hauling animals (not wild/enemy) scoop nearby loot into their inventory and run HD's
        // bulk-haul, the same way colonists do. Opt-in (default OFF) so the out-of-the-box scope stays
        // colonists + mechs; the eligibility branch + the animal think-tree haul redirect are gated on this.
        public bool allowAnimals = false;
        public bool allowIncapable = false;   // let pawns incapable of hauling still scoop their own yields

        // --- per-category yield behavior (issue #79): each work-result category independently chooses Off /
        // Drop & haul / Straight-to-inventory. Default DropThenHaul for every category == the legacy behavior
        // (all per-work toggles on + the old global pickupMode = DropThenHaul). Harvest split into {Harvest,
        // Logging}; Mining split into {Mining (ore/resources), Chunks}. Scribe writes the enum NAME, so the
        // integer order (which must match the UI segment order [Off, Drop & haul, To inventory]) is save-safe. ---
        public YieldBehavior yieldHarvest = YieldBehavior.DropThenHaul;     // crops / berries / food harvest
        public YieldBehavior yieldLogging = YieldBehavior.DropThenHaul;     // wood / cacti from felling trees
        public YieldBehavior yieldMining = YieldBehavior.DropThenHaul;      // ore / resources — NOT stone chunks
        public YieldBehavior yieldChunks = YieldBehavior.DropThenHaul;      // stone / slag chunks from mining
        public YieldBehavior yieldDeepDrill = YieldBehavior.DropThenHaul;   // deep-drill portions
        public YieldBehavior yieldDeconstruct = YieldBehavior.DropThenHaul; // deconstruction salvage
        public YieldBehavior yieldAnimals = YieldBehavior.DropThenHaul;     // milk / wool / animal products
        public YieldBehavior yieldStrip = YieldBehavior.DropThenHaul;       // gear removed by a strip order (always drop-then-haul; UI hides the "to inventory" option)
        public YieldBehavior yieldUninstall = YieldBehavior.DropThenHaul;   // the minified building from an uninstall order — scooped (non-home maps only, see YieldRouter) so it batches onto pack animals in one caravan-load trip
        // Fish catch (Odyssey fishing) — the colonist JobDriver_Fish (NOT JobDriver_FishAnimal, which is a hungry
        // animal feeding itself). A BRAND-NEW category with no legacy boolean to migrate, so MigrateLegacyYieldSettings
        // deliberately leaves it alone: on an old save the absent yieldFishing node simply stays at this
        // field-initializer default (DropThenHaul). The UI row is shown only when ModsConfig.OdysseyActive (the
        // fishing mechanic needs the Odyssey DLC).
        public YieldBehavior yieldFishing = YieldBehavior.DropThenHaul;     // fish catch (Odyssey fishing) — JobDriver_Fish

        // Settings schema version, bumped when a one-time on-load migration is needed. 0 = pre-#79 (per-work bools
        // + global pickupMode); 1 = the per-category yieldX model. The migration runs at most once (guarded on
        // settingsSchemaVersion < 1) and then stamps this to CurrentSettingsSchema so it never re-runs. [ProfileMeta]:
        // it is serialized plumbing, NOT a user-facing tunable, so the profile system must ignore it when comparing a
        // config against the defaults (the on-load stamp to 1 would otherwise read a pristine config as "Custom").
        [ProfileMeta] public int settingsSchemaVersion = 0;
        private const int CurrentSettingsSchema = 1;

        // --- unloading ---
        // The "settle" window: how long after its LAST pickup a pawn keeps accumulating before an automatic
        // unload trip. Default 2500 ticks (~1 in-game hour) so a pawn that is actively mining/deconstructing/
        // harvesting keeps scooping into inventory across the whole work run (each scoop resets the clock) and
        // only trips to storage once it's been done with that work for a while — or sooner if it fills up to
        // the smart-overload ceiling. A small value (the old 60 = ~1s) made pawns unload after almost every
        // item. Gates the idle backstop + interval (via UnloadPolicy.Decide) and the end-of-run trigger.
        public int unloadGraceTicks = 2500;
        // Periodic unload backstop; 0 = off. 1h: with the primary triggers (end of work run, meal/joy
        // checkpoints, over-encumbered, pass-by) this rarely fires — but when every one of them is
        // swallowed (drafts clearing the queue, lord duties, modded jobs), an hour is the longest a
        // pawn carries a load, not a quarter of a day. (Scribe omits a field that equals the default at
        // save time, so an old save left on the previous 6h default has no stored value and loads as 1h;
        // only a user who explicitly chose a non-default interval keeps their value.)
        public float intervalUnloadHours = 1f;
        public bool enableOnNonHomeMaps = true;  // work on caravans / temporary maps too

        // --- black-hole safety net: a red (critical) alert when a pawn is carrying scooped haul items it
        // cannot put away — nowhere on the map accepts them (no stockpile / dumping zone / reachable cell),
        // or it has held tagged items far longer than any normal unload should take (storage unreachable,
        // or another mod keeps cancelling the haul/unload job). One alert for all such pawns. ---
        public bool alertCannotUnload = true;
        public float alertStuckHours = 12f;       // condition B threshold: held tagged items this long (with a
                                                  // destination that exists) before the alert flags the pawn

        // --- misc ---
        public bool hideGizmo = false;
        // Show the per-pawn "Auto-haul yields" toggle gizmo on each eligible pawn. Default OFF (the toggle is
        // HIDDEN) — out of the box pawns auto-haul and the selection bar stays uncluttered. Turn this ON to expose
        // the per-pawn toggle so you can stop individual pawns from auto-hauling (their CompHauledToInventory
        // .autoHaulYields preference still applies whether or not the gizmo is shown).
        public bool showAutoHaulGizmo = false;
        public bool verboseLogging = false;

        // --- main-menu report notifications ---
        // How noisy the bottom-right notifications on the main menu are (new comments / status changes /
        // fixes on the player's own reports). A user-facing tunable, so it IS part of profile snapshots and
        // the reset. NotifyThreshold.Never is the full opt-out (it also stops the once-per-launch poll).
        public NotifyThreshold notifyThreshold = NotifyThreshold.All;

        // --- settings profiles (named presets) ---
        // Default = the built-in defaults (immutable; selecting it acts as "reset"). A named profile stores a full
        // snapshot of every setting; the selector shows "Custom (unsaved)" when the live values differ from the
        // active baseline. Profiles are USER DATA — ResetToDefaults never deletes them, so they're [ProfileMeta]
        // (excluded from the field==Scribe==Reset drift triple). See SettingsProfiles.cs for the logic.
        [ProfileMeta] public List<SettingsProfile> savedProfiles = new List<SettingsProfile>();
        [ProfileMeta] public string activeProfileName = "";
        // Stable per-install reporter token sent with every report so the player can view their OWN reports (and
        // their GitHub issue status) in the in-game "My reports" view. Generated once on first use (see ReporterId)
        // then persisted; [ProfileMeta] so it is exempt from the field==Scribe==Reset drift triple, never reset (it
        // is identity, not a tunable), and not captured by profile snapshots.
        [ProfileMeta] public string reporterId = "";
        // Per-report notification watermarks, keyed by report id. notifySeenComment[id] is the last comment
        // timestamp (ms) the player has seen for that report (advanced when they click its card, so a "comment"
        // card falls back to the plain status); notifyDismissed[id] is the activity time at which they pressed x
        // (the card stays hidden until something newer arrives). Per-install notification state, NOT a tunable:
        // [ProfileMeta] so they are exempt from the field==Scribe==Reset drift triple and are never reset or
        // captured by a profile snapshot (mirrors reporterId). See ReportNotifications.
        [ProfileMeta] public Dictionary<string, long> notifySeenComment = new Dictionary<string, long>();
        [ProfileMeta] public Dictionary<string, long> notifyDismissed = new Dictionary<string, long>();
        // Recursion guard: a profile snapshot is itself a HaulersDreamSettings; while (de)serializing it the nested
        // savedProfiles/activeProfileName section is skipped (a snapshot has no profiles of its own).
        public static bool SerializingSnapshot;
        [System.NonSerialized] private HaulersDreamSettings defaultsSnapshotCache;

        /// <summary>The per-install reporter token, generated (random GUID) and persisted on first access.</summary>
        public string ReporterId
        {
            get
            {
                if (string.IsNullOrEmpty(reporterId))
                {
                    reporterId = System.Guid.NewGuid().ToString("N");
                    Write(); // persist immediately so the id is stable from the very first report
                }
                return reporterId;
            }
        }

        /// <summary>The per-category <see cref="YieldBehavior"/> the player chose for a given work-result type.</summary>
        public YieldBehavior BehaviorFor(HaulSourceType type) => WorkTypePolicy.BehaviorFor(type,
            yieldHarvest, yieldLogging, yieldMining, yieldChunks, yieldDeepDrill,
            yieldDeconstruct, yieldAnimals, yieldStrip, yieldUninstall, yieldFishing);

        /// <summary>True if HD does ANYTHING for this category (Drop & haul or To inventory); false = Off/vanilla.</summary>
        public bool IsTypeEnabled(HaulSourceType type) => BehaviorFor(type) != YieldBehavior.Disabled;

        public float EffectiveCapacity(float maxCapacityKg) => CarryMath.EffectiveCapacity(maxCapacityKg, carryLimitFraction);

        // Shared labels for the route-calc choices (used by both the settings window and the route dialog).
        public static string SelectionMethodLabel(RouteSelectionMethod m)
            => (m == RouteSelectionMethod.NearestToTarget
                ? "HaulersDream.PlanRoute.SelNearest" : "HaulersDream.PlanRoute.SelMostStops").Translate();

        public static string DistanceBasisLabel(RouteDistanceBasis b)
            => (b == RouteDistanceBasis.WalkingPath
                ? "HaulersDream.PlanRoute.DistWalking" : "HaulersDream.PlanRoute.DistStraight").Translate();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref masterEnabled, "masterEnabled", true);
            Scribe_Values.Look(ref carryLimitFraction, "carryLimitFraction", 1.0f);
            Scribe_Values.Look(ref markForUnload, "markForUnload", true);
            Scribe_Values.Look(ref unloadBeforeSleep, "unloadBeforeSleep", true);
            Scribe_Values.Look(ref unloadBeforeLeisure, "unloadBeforeLeisure", true);
            Scribe_Values.Look(ref unloadBeforeEating, "unloadBeforeEating", true);
            Scribe_Values.Look(ref unloadAllSurplus, "unloadAllSurplus", false);
            Scribe_Collections.Look(ref itemUnloadRules, "itemUnloadRules", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (itemUnloadRules == null)
                    itemUnloadRules = new List<string>();
                // Read the legacy "never unload" list ONLY on load (never written again) and fold it in as KeepAll.
                keepDefNames = null;
                Scribe_Collections.Look(ref keepDefNames, "keepDefNames", LookMode.Value);
                MigrateLegacyKeepDefNames();
                ruleMap = null; // rebuild the decode cache from the freshly-loaded list on next query
            }
            Scribe_Values.Look(ref shareForBuilding, "shareForBuilding", true);
            Scribe_Values.Look(ref buildFromInventory, "buildFromInventory", true);
            Scribe_Values.Look(ref buildFromInventoryPartial, "buildFromInventoryPartial", false);
            Scribe_Values.Look(ref shareForCrafting, "shareForCrafting", true);
            Scribe_Values.Look(ref inventoryCraftDeliver, "inventoryCraftDeliver", true);
            Scribe_Values.Look(ref shareMeetInMiddle, "shareMeetInMiddle", true);
            Scribe_Values.Look(ref batchWorkDeliveries, "batchWorkDeliveries", true);
            Scribe_Values.Look(ref inventoryConstructDeliver, "inventoryConstructDeliver", true);
            Scribe_Values.Look(ref multiSiteConstructDeliver, "multiSiteConstructDeliver", true);
            Scribe_Values.Look(ref shareHandHauledToStorage, "shareHandHauledToStorage", false);
            Scribe_Values.Look(ref mealsOnWheels, "mealsOnWheels", true);
            Scribe_Values.Look(ref bulkHaul, "bulkHaul", true);
            Scribe_Values.Look(ref bulkHaulTrigger, "bulkHaulTrigger", BulkHaulTrigger.SecondTasked);
            Scribe_Values.Look(ref haulNearbyOption, "haulNearbyOption", true);
            Scribe_Values.Look(ref manualPickupOption, "manualPickupOption", true);
            Scribe_Values.Look(ref keepInInventoryOption, "keepInInventoryOption", true);
            Scribe_Values.Look(ref haulOversizedInInventory, "haulOversizedInInventory", true);
            Scribe_Values.Look(ref sweepNearbyWhileWorking, "sweepNearbyWhileWorking", true);
            Scribe_Values.Look(ref loadPackAnimalBulk, "loadPackAnimalBulk", true);
            Scribe_Values.Look(ref autoDivertToPackAnimal, "autoDivertToPackAnimal", true);
            Scribe_Values.Look(ref enableBulkLoadTransporters, "enableBulkLoadTransporters", true);
            Scribe_Values.Look(ref bulkLoadAiUpdateFrequency, "bulkLoadAiUpdateFrequency", 60);
            Scribe_Values.Look(ref enableBulkLoadPortal, "enableBulkLoadPortal", true);
            Scribe_Values.Look(ref enableVehicleFramework, "enableVehicleFramework", true);
            Scribe_Values.Look(ref enableBulkLoadVehicles, "enableBulkLoadVehicles", true);
            Scribe_Values.Look(ref enableStorageNetworkBulkLoad, "enableStorageNetworkBulkLoad", false);
            Scribe_Values.Look(ref enableBulkRefuel, "enableBulkRefuel", true);
            Scribe_Values.Look(ref enableBulkUnloadCarriers, "enableBulkUnloadCarriers", true);
            Scribe_Values.Look(ref cleanupOnSave, "cleanupOnSave", true);
            Scribe_Values.Look(ref enableSoftlockDrop, "enableSoftlockDrop", true);
            Scribe_Values.Look(ref enableOpportunisticLoad, "enableOpportunisticLoad", false);
            Scribe_Values.Look(ref loadOpportunityScanRadius, "loadOpportunityScanRadius", 30);
            Scribe_Values.Look(ref enableContinuousLoading, "enableContinuousLoading", false);
            Scribe_Values.Look(ref loadHybridPathing, "loadHybridPathing", false);
            Scribe_Values.Look(ref loadPathfindingCandidates, "loadPathfindingCandidates", 8);
            Scribe_Values.Look(ref autoOpenTransporterContents, "autoOpenTransporterContents", false);
            Scribe_Values.Look(ref autoOpenCarrierGear, "autoOpenCarrierGear", false);
            Scribe_Values.Look(ref minFreeSpaceToUnloadCarrierPct, "minFreeSpaceToUnloadCarrierPct", 0.5f);
            Scribe_Values.Look(ref reserveCarrierOnUnload, "reserveCarrierOnUnload", false);
            Scribe_Values.Look(ref visualUnloadDelay, "visualUnloadDelay", 15);
            Scribe_Values.Look(ref haulToStack, "haulToStack", true);
            Scribe_Values.Look(ref orderedConstructTether, "orderedConstructTether", true);
            Scribe_Values.Look(ref haulToSiteOption, "haulToSiteOption", true);
            Scribe_Values.Look(ref planRoutes, "planRoutes", true);
            Scribe_Values.Look(ref rememberPlan, "rememberPlan", true);
            Scribe_Values.Look(ref planCrafting, "planCrafting", true);
            Scribe_Values.Look(ref batchByDefault, "batchByDefault", false);
            Scribe_Values.Look(ref defaultBatchSize, "defaultBatchSize", 10);
            Scribe_Values.Look(ref allowBatchUnderCommonSense, "allowBatchUnderCommonSense", true);
            Scribe_Values.Look(ref autoStripMode, "autoStripMode", AutoStripMode.AllHauls);
            Scribe_Values.Look(ref stripColonistCorpses, "stripColonistCorpses", false);
            Scribe_Values.Look(ref taintedSmeltablePolicy, "taintedSmeltablePolicy", TaintedApparelPolicy.Take);
            Scribe_Values.Look(ref taintedNonSmeltablePolicy, "taintedNonSmeltablePolicy", TaintedApparelPolicy.Take);
            Scribe_Values.Look(ref haulWildKills, "haulWildKills", true);
            Scribe_Values.Look(ref haulTamedSlaughter, "haulTamedSlaughter", true);
            Scribe_Values.Look(ref butcherSpoilingFirst, "butcherSpoilingFirst", true);
            Scribe_Values.Look(ref cookSpoilingFirst, "cookSpoilingFirst", true);
            Scribe_Values.Look(ref overloadLevel, "overloadLevel", OverloadTuning.FairLevel);
            Scribe_Values.Look(ref strictCarryWeight, "strictCarryWeight", false);
            Scribe_Values.Look(ref keepWorkingWhenFull, "keepWorkingWhenFull", false);
            Scribe_Values.Look(ref keepWorkingMarginCells, "keepWorkingMarginCells", 5);
            Scribe_Values.Look(ref opportunisticUnload, "opportunisticUnload", true);
            Scribe_Values.Look(ref skipHaulWhileBleeding, "skipHaulWhileBleeding", true);
            Scribe_Values.Look(ref bleedThresholdPerDay, "bleedThresholdPerDay", 0.001f);
            Scribe_Values.Look(ref closestDestinationUnloadOrder, "closestDestinationUnloadOrder", true);
            Scribe_Values.Look(ref enRoutePickup, "enRoutePickup", true);
            Scribe_Values.Look(ref enRoutePathChecker, "enRoutePathChecker", EnRoutePathChecker.Vanilla);
            Scribe_Values.Look(ref storageRouting, "storageRouting", true);
            Scribe_Values.Look(ref routeSupplies, "routeSupplies", true);
            Scribe_Values.Look(ref routeIngredients, "routeIngredients", true);
            Scribe_Values.Look(ref routeToEqualPriority, "routeToEqualPriority", true);
            Scribe_Values.Look(ref routeToStockpiles, "routeToStockpiles", true);
            Scribe_Values.Look(ref storageFiltersEnabled, "storageFiltersEnabled", true);
            Scribe_Values.Look(ref storageFilterUseDefaults, "storageFilterUseDefaults", true);
            Scribe_Values.Look(ref storageFilterDenyLwmForOpportunistic, "storageFilterDenyLwmForOpportunistic", true);
            Scribe_Deep.Look(ref storageBuildingFilter, "storageBuildingFilter");
            if (Scribe.mode == LoadSaveMode.LoadingVars && storageBuildingFilter == null)
                storageBuildingFilter = new StorageBuildingFilter(); // old saves lack the node -> allow-all
            Scribe_Values.Look(ref routeAllowHarvest, "routeAllowHarvest", true);
            Scribe_Values.Look(ref routeGrowthThreshold, "routeGrowthThreshold", 80);
            Scribe_Values.Look(ref routeMaxAmount, "routeMaxAmount", 50);
            Scribe_Values.Look(ref routeSelectionMethod, "routeSelectionMethod", RouteSelectionMethod.MostStopsPerTravel);
            Scribe_Values.Look(ref routeDistanceBasis, "routeDistanceBasis", RouteDistanceBasis.StraightLine);
            Scribe_Values.Look(ref routeOrderExactMax, "routeOrderExactMax", RouteOrderPolicy.ExactMax);
            // A hand-edited config with k ≥ 22 would make the Held-Karp solver allocate gigabytes (2^k·k
            // arrays) or overflow at k ≥ 31 — clamp on load (RouteOrderPolicy.Order re-clamps as the backstop).
            routeOrderExactMax = Mathf.Clamp(routeOrderExactMax, 1, 16);
            Scribe_Values.Look(ref craftBatchTimeoutHours, "craftBatchTimeoutHours", 2f);
            Scribe_Collections.Look(ref routePrefsByDef, "routePrefsByDef", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && routePrefsByDef == null)
                routePrefsByDef = new Dictionary<string, RouteDialogPrefs>();
            // Explicit remembered templates (the "Remember" button). Separate stores from routePrefsByDef; see fields.
            Scribe_Collections.Look(ref rememberedRoutesByDef, "rememberedRoutesByDef", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref rememberedSowRoutesByDef, "rememberedSowRoutesByDef", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref rememberedRemoveFloorRoutesByDef, "rememberedRemoveFloorRoutesByDef", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (rememberedRoutesByDef == null) rememberedRoutesByDef = new Dictionary<string, RouteDialogPrefs>();
                if (rememberedSowRoutesByDef == null) rememberedSowRoutesByDef = new Dictionary<string, SowRouteTemplate>();
                if (rememberedRemoveFloorRoutesByDef == null) rememberedRemoveFloorRoutesByDef = new Dictionary<string, RemoveFloorRouteTemplate>();
            }
            Scribe_Values.Look(ref pauseWhileDrafted, "pauseWhileDrafted", true);
            Scribe_Values.Look(ref allowMechanoids, "allowMechanoids", true);
            Scribe_Values.Look(ref mechHaulMultiplier, "mechHaulMultiplier", 1.0f);
            Scribe_Values.Look(ref allowAnimals, "allowAnimals", false);
            Scribe_Values.Look(ref allowIncapable, "allowIncapable", false);
            Scribe_Values.Look(ref yieldHarvest, "yieldHarvest", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldLogging, "yieldLogging", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldMining, "yieldMining", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldChunks, "yieldChunks", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldDeepDrill, "yieldDeepDrill", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldDeconstruct, "yieldDeconstruct", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldAnimals, "yieldAnimals", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldStrip, "yieldStrip", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldUninstall, "yieldUninstall", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref yieldFishing, "yieldFishing", YieldBehavior.DropThenHaul);
            Scribe_Values.Look(ref settingsSchemaVersion, "settingsSchemaVersion", 0);
            // One-time migration of the pre-#79 per-work bools + global pickupMode into the 9 LEGACY yieldX values
            // (yieldFishing is a newer category with no legacy bool — it is left at its field default, never migrated).
            // Guarded on the schema version so it runs AT MOST ONCE: a fresh install / already-migrated save
            // (schemaVersion >= 1) keeps its 9 fields untouched. On a legacy save the old nodes are read into
            // locals (absent nodes yield the old defaults, which map to the correct new defaults — harmless on a
            // fresh state). The stamp afterwards guarantees we never re-migrate and clobber a player's choices.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (settingsSchemaVersion < 1)
                    MigrateLegacyYieldSettings();
                settingsSchemaVersion = CurrentSettingsSchema;
            }
            Scribe_Values.Look(ref allPawnsCanHaul, "allPawnsCanHaul", false);
            Scribe_Values.Look(ref allPawnsCanClean, "allPawnsCanClean", false);
            Scribe_Values.Look(ref allPawnsCanCutPlants, "allPawnsCanCutPlants", false);
            Scribe_Values.Look(ref unloadGraceTicks, "unloadGraceTicks", 2500);
            Scribe_Values.Look(ref intervalUnloadHours, "intervalUnloadHours", 1f);
            Scribe_Values.Look(ref alertCannotUnload, "alertCannotUnload", true);
            Scribe_Values.Look(ref alertStuckHours, "alertStuckHours", 12f);
            Scribe_Values.Look(ref enableOnNonHomeMaps, "enableOnNonHomeMaps", true);
            Scribe_Values.Look(ref hideGizmo, "hideGizmo", false);
            Scribe_Values.Look(ref showAutoHaulGizmo, "showAutoHaulGizmo", false);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            Scribe_Values.Look(ref notifyThreshold, "notifyThreshold", NotifyThreshold.All);

            // Profile list + active name. Skipped while serializing a profile's own snapshot (the recursion guard),
            // since a snapshot is itself a HaulersDreamSettings and must not carry a nested profile list.
            if (!SerializingSnapshot)
            {
                Scribe_Collections.Look(ref savedProfiles, "savedProfiles", LookMode.Deep);
                Scribe_Values.Look(ref activeProfileName, "activeProfileName", "");
                Scribe_Values.Look(ref reporterId, "reporterId", "");
                Scribe_Collections.Look(ref notifySeenComment, "notifySeenComment", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref notifyDismissed, "notifyDismissed", LookMode.Value, LookMode.Value);
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    if (savedProfiles == null) savedProfiles = new List<SettingsProfile>();
                    if (activeProfileName == null) activeProfileName = "";
                    if (reporterId == null) reporterId = "";
                    if (notifySeenComment == null) notifySeenComment = new Dictionary<string, long>();
                    if (notifyDismissed == null) notifyDismissed = new Dictionary<string, long>();
                }
            }
        }

        /// <summary>Reset EVERY persisted setting to its declared default (the same values the field initializers
        /// and ExposeData defaults use). Called from the "Reset to defaults" button after a confirmation dialog.
        /// Kept exhaustive: each Scribe_Values/Scribe_Collections/Scribe_Deep field above has a matching line here,
        /// so a reset never silently leaves a field stale. Transient/derived/cache fields (drawDetourLines,
        /// ruleMap, keepDefNames) are NOT serialized and intentionally excluded.</summary>
        public void ResetToDefaults()
        {
            masterEnabled = true;
            carryLimitFraction = 1.0f;
            markForUnload = true;
            unloadBeforeSleep = true;
            unloadBeforeLeisure = true;
            unloadBeforeEating = true;
            unloadAllSurplus = false;
            itemUnloadRules = new List<string>();
            ruleMap = null; // invalidate the decode cache so the cleared rule list takes effect immediately
            shareForBuilding = true;
            buildFromInventory = true;
            buildFromInventoryPartial = false;
            shareForCrafting = true;
            inventoryCraftDeliver = true;
            shareMeetInMiddle = true;
            batchWorkDeliveries = true;
            inventoryConstructDeliver = true;
            multiSiteConstructDeliver = true;
            shareHandHauledToStorage = false;
            mealsOnWheels = true;
            bulkHaul = true;
            bulkHaulTrigger = BulkHaulTrigger.SecondTasked;
            haulNearbyOption = true;
            manualPickupOption = true;
            keepInInventoryOption = true;
            haulOversizedInInventory = true;
            sweepNearbyWhileWorking = true;
            loadPackAnimalBulk = true;
            autoDivertToPackAnimal = true;
            enableBulkLoadTransporters = true;
            bulkLoadAiUpdateFrequency = 60;
            enableBulkLoadPortal = true;
            enableVehicleFramework = true;
            enableBulkLoadVehicles = true;
            enableStorageNetworkBulkLoad = false;
            enableBulkRefuel = true;
            enableBulkUnloadCarriers = true;
            cleanupOnSave = true;
            enableSoftlockDrop = true;
            enableOpportunisticLoad = false;
            loadOpportunityScanRadius = 30;
            enableContinuousLoading = false;
            loadHybridPathing = false;
            loadPathfindingCandidates = 8;
            autoOpenTransporterContents = false;
            autoOpenCarrierGear = false;
            minFreeSpaceToUnloadCarrierPct = 0.5f;
            reserveCarrierOnUnload = false;
            visualUnloadDelay = 15;
            haulToStack = true;
            orderedConstructTether = true;
            haulToSiteOption = true;
            planRoutes = true;
            rememberPlan = true;
            planCrafting = true;
            batchByDefault = false;
            defaultBatchSize = 10;
            allowBatchUnderCommonSense = true;
            autoStripMode = AutoStripMode.AllHauls;
            stripColonistCorpses = false;
            taintedSmeltablePolicy = TaintedApparelPolicy.Take;
            taintedNonSmeltablePolicy = TaintedApparelPolicy.Take;
            haulWildKills = true;
            haulTamedSlaughter = true;
            butcherSpoilingFirst = true;
            cookSpoilingFirst = true;
            overloadLevel = OverloadTuning.FairLevel;
            strictCarryWeight = false;
            keepWorkingWhenFull = false;
            keepWorkingMarginCells = 5;
            opportunisticUnload = true;
            skipHaulWhileBleeding = true;
            bleedThresholdPerDay = 0.001f;
            closestDestinationUnloadOrder = true;
            enRoutePickup = true;
            enRoutePathChecker = EnRoutePathChecker.Vanilla;
            storageRouting = true;
            routeSupplies = true;
            routeIngredients = true;
            routeToEqualPriority = true;
            routeToStockpiles = true;
            storageFiltersEnabled = true;
            storageFilterUseDefaults = true;
            storageFilterDenyLwmForOpportunistic = true;
            storageBuildingFilter = new StorageBuildingFilter();
            routeAllowHarvest = true;
            routeGrowthThreshold = 80;
            routeMaxAmount = 50;
            routeSelectionMethod = RouteSelectionMethod.MostStopsPerTravel;
            routeDistanceBasis = RouteDistanceBasis.StraightLine;
            routeOrderExactMax = RouteOrderPolicy.ExactMax;
            craftBatchTimeoutHours = 2f;
            routePrefsByDef = new Dictionary<string, RouteDialogPrefs>();
            rememberedRoutesByDef = new Dictionary<string, RouteDialogPrefs>();
            rememberedSowRoutesByDef = new Dictionary<string, SowRouteTemplate>();
            rememberedRemoveFloorRoutesByDef = new Dictionary<string, RemoveFloorRouteTemplate>();
            pauseWhileDrafted = true;
            allowMechanoids = true;
            mechHaulMultiplier = 1.0f;
            allowAnimals = false;
            allowIncapable = false;
            yieldHarvest = YieldBehavior.DropThenHaul;
            yieldLogging = YieldBehavior.DropThenHaul;
            yieldMining = YieldBehavior.DropThenHaul;
            yieldChunks = YieldBehavior.DropThenHaul;
            yieldDeepDrill = YieldBehavior.DropThenHaul;
            yieldDeconstruct = YieldBehavior.DropThenHaul;
            yieldAnimals = YieldBehavior.DropThenHaul;
            yieldStrip = YieldBehavior.DropThenHaul;
            yieldUninstall = YieldBehavior.DropThenHaul;
            yieldFishing = YieldBehavior.DropThenHaul;
            settingsSchemaVersion = 0;
            allPawnsCanHaul = false;
            allPawnsCanClean = false;
            allPawnsCanCutPlants = false;
            unloadGraceTicks = 2500;
            intervalUnloadHours = 1f;
            alertCannotUnload = true;
            alertStuckHours = 12f;
            enableOnNonHomeMaps = true;
            hideGizmo = false;
            showAutoHaulGizmo = false;
            verboseLogging = false;
            notifyThreshold = NotifyThreshold.All;
        }

        /// <summary>The decoded per-item rules keyed by defName, including entries whose mod is currently absent
        /// (kept verbatim so they restore when the mod returns). Lazily rebuilt from <see cref="itemUnloadRules"/>.</summary>
        private Dictionary<string, ItemUnloadRule> RuleMap
        {
            get
            {
                if (ruleMap == null)
                {
                    ruleMap = new Dictionary<string, ItemUnloadRule>();
                    if (itemUnloadRules != null)
                        foreach (var entry in itemUnloadRules)
                            if (TryDecodeRule(entry, out var name, out var rule))
                                ruleMap[name] = rule;
                }
                return ruleMap;
            }
        }

        /// <summary>The player's explicit per-item rule for this def, if any. O(1). Fallback-safe: a rule whose
        /// mod is absent simply never matches a live item.</summary>
        public bool TryGetItemRule(ThingDef def, out ItemUnloadRule rule)
        {
            rule = default;
            return def != null && RuleMap.TryGetValue(def.defName, out rule);
        }

        /// <summary>How many per-item rules are set (for the settings button label).</summary>
        public int ItemRuleCount => RuleMap.Count;

        /// <summary>True if this def has an explicit rule that can CREATE unload surplus (keep-at-most or
        /// always-unload). Such a rule is a deliberate per-item opt-in, so HD adopts/unloads that def's untagged
        /// stock independently of the global "unload all surplus" toggle. (KeepAll/Default never produce surplus.)</summary>
        public bool RuleProducesSurplus(ThingDef def)
            => def != null && RuleMap.TryGetValue(def.defName, out var r) && r.mode != ItemUnloadMode.KeepAll;

        /// <summary>True if ANY per-item rule can create unload surplus (keep-at-most / always-unload) — the cheap
        /// gate for "run the surplus-adoption pass even with the global toggle off".</summary>
        public bool HasAnySurplusProducingRule
        {
            get
            {
                foreach (var kv in RuleMap)
                    if (kv.Value.mode != ItemUnloadMode.KeepAll)
                        return true;
                return false;
            }
        }

        /// <summary>A mutable copy of all rules (live and absent-mod alike) for the picker dialog to edit.</summary>
        public Dictionary<string, ItemUnloadRule> GetItemRulesCopy()
            => new Dictionary<string, ItemUnloadRule>(RuleMap);

        /// <summary>Replace all per-item rules (called by the picker dialog on close). The dialog preserves
        /// entries whose mod is currently absent, so re-encoding the whole map stays fallback-safe.</summary>
        public void SetItemRules(Dictionary<string, ItemUnloadRule> map)
        {
            itemUnloadRules = new List<string>();
            if (map != null)
                foreach (var kv in map)
                    itemUnloadRules.Add(EncodeRule(kv.Key, kv.Value));
            ruleMap = null; // invalidate the decode cache
        }

        // --- fallback-safe string codec: "defName|modeInt|amount" ----------------------------------------------
        private static string EncodeRule(string defName, ItemUnloadRule rule)
            => defName + "|" + ((int)rule.mode).ToString() + "|" + rule.amount.ToString();

        private static bool TryDecodeRule(string entry, out string defName, out ItemUnloadRule rule)
        {
            defName = null;
            rule = default;
            if (string.IsNullOrEmpty(entry))
                return false;
            var parts = entry.Split('|');
            if (parts.Length < 1 || string.IsNullOrEmpty(parts[0]))
                return false;
            defName = parts[0];
            int modeInt = 0, amount = 0;
            if (parts.Length >= 2)
                int.TryParse(parts[1], out modeInt);   // bad/legacy data -> 0 = KeepAll (safe default)
            if (parts.Length >= 3)
                int.TryParse(parts[2], out amount);
            rule = new ItemUnloadRule((ItemUnloadMode)Mathf.Clamp(modeInt, 0, 2), Mathf.Max(0, amount));
            return true;
        }

        // Fold a legacy pre-1.1.x "never unload" defName list into itemUnloadRules as KeepAll rules (only entries
        // not already present), then drop it so it is never written again.
        private void MigrateLegacyKeepDefNames()
        {
            if (keepDefNames == null || keepDefNames.Count == 0)
                return;
            var have = new HashSet<string>();
            foreach (var entry in itemUnloadRules)
                if (TryDecodeRule(entry, out var n, out _))
                    have.Add(n);
            foreach (var name in keepDefNames)
                if (!string.IsNullOrEmpty(name) && have.Add(name))
                    itemUnloadRules.Add(EncodeRule(name, new ItemUnloadRule(ItemUnloadMode.KeepAll)));
            keepDefNames = null;
        }

        // ----- issue #79: one-time migration of the pre-#79 per-work bools + global pickupMode -> the 9 yieldX -----
        // Runs ONCE, only on a load whose settingsSchemaVersion < 1 (see ExposeData). Reads the OLD save nodes into
        // LOCALS via Scribe_Values.Look with the OLD defaults (old per-work bools defaulted true; old pickupMode
        // defaulted DropThenHaul), then maps them through the pure WorkTypePolicy.MapLegacyYield. This is LOSSLESS:
        // an off toggle -> Disabled, an on toggle -> the old global pickup mode (Drop/Direct); Strip was ALWAYS
        // drop-then-haul, so it ignores the global mode (forceDropOnly). The split categories inherit their parent's
        // legacy toggle (Logging<-haulHarvest, Chunks<-haulMining), matching the pre-split behavior exactly. On a
        // brand-new state the old nodes are absent, so every local reads its old default and maps to DropThenHaul —
        // identical to the field initializers, so a fresh install is unaffected. Mirrors the keepDefNames idiom.
        private void MigrateLegacyYieldSettings()
        {
            // Read the legacy nodes into locals with the OLD defaults (absent node -> old default).
            PickupMode legacyPickupMode = PickupMode.DropThenHaul;
            bool haulHarvest = true, haulMining = true, haulDeepDrill = true, haulDeconstruct = true,
                 haulAnimals = true, haulStrip = true, haulUninstall = true;
            Scribe_Values.Look(ref legacyPickupMode, "pickupMode", PickupMode.DropThenHaul);
            Scribe_Values.Look(ref haulHarvest, "haulHarvest", true);
            Scribe_Values.Look(ref haulMining, "haulMining", true);
            Scribe_Values.Look(ref haulDeepDrill, "haulDeepDrill", true);
            Scribe_Values.Look(ref haulDeconstruct, "haulDeconstruct", true);
            Scribe_Values.Look(ref haulAnimals, "haulAnimals", true);
            Scribe_Values.Look(ref haulStrip, "haulStrip", true);
            Scribe_Values.Look(ref haulUninstall, "haulUninstall", true);

            yieldHarvest     = WorkTypePolicy.MapLegacyYield(haulHarvest, legacyPickupMode, forceDropOnly: false);
            yieldLogging     = WorkTypePolicy.MapLegacyYield(haulHarvest, legacyPickupMode, forceDropOnly: false); // split inherits haulHarvest
            yieldMining      = WorkTypePolicy.MapLegacyYield(haulMining, legacyPickupMode, forceDropOnly: false);
            yieldChunks      = WorkTypePolicy.MapLegacyYield(haulMining, legacyPickupMode, forceDropOnly: false);  // split inherits haulMining
            yieldDeepDrill   = WorkTypePolicy.MapLegacyYield(haulDeepDrill, legacyPickupMode, forceDropOnly: false);
            yieldDeconstruct = WorkTypePolicy.MapLegacyYield(haulDeconstruct, legacyPickupMode, forceDropOnly: false);
            yieldAnimals     = WorkTypePolicy.MapLegacyYield(haulAnimals, legacyPickupMode, forceDropOnly: false);
            yieldStrip       = WorkTypePolicy.MapLegacyYield(haulStrip, legacyPickupMode, forceDropOnly: true);    // strip was ALWAYS drop-then-haul
            yieldUninstall   = WorkTypePolicy.MapLegacyYield(haulUninstall, legacyPickupMode, forceDropOnly: false);
        }

    }
}
