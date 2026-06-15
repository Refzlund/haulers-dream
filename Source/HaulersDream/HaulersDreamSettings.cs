using System.Collections.Generic;
using HaulersDream.Core;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    public class HaulersDreamSettings : ModSettings
    {
        // --- carry limit (the headline change: default = full max carrying capacity) ---
        public float carryLimitFraction = 1.0f;

        // --- pickup mode ---
        public PickupMode pickupMode = PickupMode.DropThenHaul;

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
        public bool planCrafting = true; // station crafting planner (batch a bill N times in one go)
        // Batch-Y bill mode: when ON, a newly-created batchable bill starts in batch mode at defaultBatchSize.
        public bool batchByDefault = false; // OFF by default so existing players see no change until they opt in
        public int defaultBatchSize = 10;   // initial per-batch quantity for a freshly-batched bill

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
        // Let colony hauling animals (not wild/enemy) scoop nearby loot into their inventory and run HD's
        // bulk-haul, the same way colonists do. Opt-in (default OFF) so the out-of-the-box scope stays
        // colonists + mechs; the eligibility branch + the animal think-tree haul redirect are gated on this.
        public bool allowAnimals = false;
        public bool allowIncapable = false;   // let pawns incapable of hauling still scoop their own yields

        // --- per-work-type toggles ---
        public bool haulHarvest = true;
        public bool haulMining = true;
        public bool haulDeepDrill = true;
        public bool haulDeconstruct = true;
        public bool haulAnimals = true;
        public bool haulStrip = true;   // gear removed by a strip order (pawn or corpse) gets scooped + hauled

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
        public bool verboseLogging = false;

        public bool IsTypeEnabled(HaulSourceType type)
            => WorkTypePolicy.IsTypeEnabled(type, haulHarvest, haulMining, haulDeepDrill, haulDeconstruct, haulAnimals, haulStrip);

        public float EffectiveCapacity(float maxCapacityKg) => CarryMath.EffectiveCapacity(maxCapacityKg, carryLimitFraction);

        private static string OverloadLevelLabel(int lv)
        {
            lv = OverloadTuning.ClampLevel(lv);
            if (lv == 0) return "HaulersDream.Overload.Free".Translate();
            if (lv >= OverloadTuning.OffLevel) return "HaulersDream.Overload.Off".Translate();
            if (lv == OverloadTuning.FairLevel) return "HaulersDream.Overload.Fair".Translate();
            return lv < OverloadTuning.FairLevel
                ? "HaulersDream.Overload.Eager".Translate(lv)
                : "HaulersDream.Overload.Cautious".Translate(lv);
        }

        private static string AutoStripModeLabel(AutoStripMode m)
        {
            switch (m)
            {
                case AutoStripMode.AllHauls: return "HaulersDream.Setting.AutoStripAll".Translate();
                case AutoStripMode.DisposalOnly: return "HaulersDream.Setting.AutoStripDisposal".Translate();
                default: return "HaulersDream.Setting.AutoStripOff".Translate();
            }
        }

        private static string TaintedPolicyLabel(TaintedApparelPolicy p)
        {
            switch (p)
            {
                case TaintedApparelPolicy.LeaveOnCorpse: return "HaulersDream.Setting.TaintedLeave".Translate();
                case TaintedApparelPolicy.DropAndForbid: return "HaulersDream.Setting.TaintedForbid".Translate();
                case TaintedApparelPolicy.Destroy: return "HaulersDream.Setting.TaintedDestroy".Translate();
                default: return "HaulersDream.Setting.TaintedTake".Translate();
            }
        }

        private static FloatMenu TaintedPolicyMenu(System.Action<TaintedApparelPolicy> set)
            => new FloatMenu(new List<FloatMenuOption>
            {
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.Take), () => set(TaintedApparelPolicy.Take)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.LeaveOnCorpse), () => set(TaintedApparelPolicy.LeaveOnCorpse)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.DropAndForbid), () => set(TaintedApparelPolicy.DropAndForbid)),
                new FloatMenuOption(TaintedPolicyLabel(TaintedApparelPolicy.Destroy), () => set(TaintedApparelPolicy.Destroy)),
            });

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
            Scribe_Values.Look(ref carryLimitFraction, "carryLimitFraction", 1.0f);
            Scribe_Values.Look(ref pickupMode, "pickupMode", PickupMode.DropThenHaul);
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
            Scribe_Values.Look(ref shareHandHauledToStorage, "shareHandHauledToStorage", false);
            Scribe_Values.Look(ref mealsOnWheels, "mealsOnWheels", true);
            Scribe_Values.Look(ref bulkHaul, "bulkHaul", true);
            Scribe_Values.Look(ref bulkHaulTrigger, "bulkHaulTrigger", BulkHaulTrigger.SecondTasked);
            Scribe_Values.Look(ref haulNearbyOption, "haulNearbyOption", true);
            Scribe_Values.Look(ref manualPickupOption, "manualPickupOption", true);
            Scribe_Values.Look(ref haulOversizedInInventory, "haulOversizedInInventory", true);
            Scribe_Values.Look(ref sweepNearbyWhileWorking, "sweepNearbyWhileWorking", true);
            Scribe_Values.Look(ref loadPackAnimalBulk, "loadPackAnimalBulk", true);
            Scribe_Values.Look(ref autoDivertToPackAnimal, "autoDivertToPackAnimal", true);
            Scribe_Values.Look(ref enableBulkLoadTransporters, "enableBulkLoadTransporters", true);
            Scribe_Values.Look(ref bulkLoadAiUpdateFrequency, "bulkLoadAiUpdateFrequency", 60);
            Scribe_Values.Look(ref enableBulkLoadPortal, "enableBulkLoadPortal", true);
            Scribe_Values.Look(ref enableVehicleFramework, "enableVehicleFramework", true);
            Scribe_Values.Look(ref enableBulkLoadVehicles, "enableBulkLoadVehicles", true);
            Scribe_Values.Look(ref enableBulkUnloadCarriers, "enableBulkUnloadCarriers", true);
            Scribe_Values.Look(ref minFreeSpaceToUnloadCarrierPct, "minFreeSpaceToUnloadCarrierPct", 0.5f);
            Scribe_Values.Look(ref reserveCarrierOnUnload, "reserveCarrierOnUnload", false);
            Scribe_Values.Look(ref visualUnloadDelay, "visualUnloadDelay", 15);
            Scribe_Values.Look(ref haulToStack, "haulToStack", true);
            Scribe_Values.Look(ref orderedConstructTether, "orderedConstructTether", true);
            Scribe_Values.Look(ref haulToSiteOption, "haulToSiteOption", true);
            Scribe_Values.Look(ref planRoutes, "planRoutes", true);
            Scribe_Values.Look(ref planCrafting, "planCrafting", true);
            Scribe_Values.Look(ref batchByDefault, "batchByDefault", false);
            Scribe_Values.Look(ref defaultBatchSize, "defaultBatchSize", 10);
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
            Scribe_Values.Look(ref pauseWhileDrafted, "pauseWhileDrafted", true);
            Scribe_Values.Look(ref allowMechanoids, "allowMechanoids", true);
            Scribe_Values.Look(ref allowAnimals, "allowAnimals", false);
            Scribe_Values.Look(ref allowIncapable, "allowIncapable", false);
            Scribe_Values.Look(ref haulHarvest, "haulHarvest", true);
            Scribe_Values.Look(ref haulMining, "haulMining", true);
            Scribe_Values.Look(ref haulDeepDrill, "haulDeepDrill", true);
            Scribe_Values.Look(ref haulDeconstruct, "haulDeconstruct", true);
            Scribe_Values.Look(ref haulAnimals, "haulAnimals", true);
            Scribe_Values.Look(ref haulStrip, "haulStrip", true);
            Scribe_Values.Look(ref allPawnsCanHaul, "allPawnsCanHaul", false);
            Scribe_Values.Look(ref allPawnsCanClean, "allPawnsCanClean", false);
            Scribe_Values.Look(ref allPawnsCanCutPlants, "allPawnsCanCutPlants", false);
            Scribe_Values.Look(ref unloadGraceTicks, "unloadGraceTicks", 2500);
            Scribe_Values.Look(ref intervalUnloadHours, "intervalUnloadHours", 1f);
            Scribe_Values.Look(ref alertCannotUnload, "alertCannotUnload", true);
            Scribe_Values.Look(ref alertStuckHours, "alertStuckHours", 12f);
            Scribe_Values.Look(ref enableOnNonHomeMaps, "enableOnNonHomeMaps", true);
            Scribe_Values.Look(ref hideGizmo, "hideGizmo", false);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
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

        // The settings list long ago outgrew Dialog_ModSettings' fixed height — without a scroll view the
        // bottom half of the options is invisible and uneditable.
        private static Vector2 settingsScroll;
        private static float settingsHeight = 1400f;

        public void DoWindowContents(Rect rect)
        {
            var view = new Rect(0f, 0f, rect.width - 16f, settingsHeight);
            Widgets.BeginScrollView(rect, ref settingsScroll, view);
            var l = new Listing_Standard
            {
                // CRITICAL: without this the scroll view silently breaks. Listing_Standard.NewColumnIfNeeded
                // wraps into a SECOND column the instant content exceeds listingRect.height (= settingsHeight)
                // in any frame — which happens whenever the options grow past the last measured height
                // (initial 1400 too small, or toggling bulk-haul / auto-strip adds rows). After a wrap,
                // CurHeight (curY) reports only the short wrapped column, so settingsHeight collapses to the
                // Mathf.Max floor (rect.height = the viewport). The view then equals the viewport, the
                // scrollbar vanishes, and the wrapped columns run off-screen — and it re-wraps from the
                // collapsed height every frame, so it never recovers. maxOneColumn forbids the wrap, so
                // CurHeight is always the true single-column height and settingsHeight tracks it correctly.
                maxOneColumn = true
            };
            l.Begin(view);

            l.Label("HaulersDream.Setting.CarryLimit".Translate(carryLimitFraction.ToStringPercent()),
                tooltip: "HaulersDream.Setting.CarryLimitDesc".Translate());
            carryLimitFraction = l.Slider(carryLimitFraction, CarryMath.MinFraction, CarryMath.MaxFraction);

            l.GapLine();
            bool dropThenHaul = pickupMode == PickupMode.DropThenHaul;
            l.CheckboxLabeled("HaulersDream.Setting.DropThenHaul".Translate(), ref dropThenHaul,
                "HaulersDream.Setting.DropThenHaulDesc".Translate());
            pickupMode = dropThenHaul ? PickupMode.DropThenHaul : PickupMode.DirectToInventory;
            l.CheckboxLabeled("HaulersDream.Setting.MarkForUnload".Translate(), ref markForUnload);
            l.CheckboxLabeled("HaulersDream.Setting.UnloadAllSurplus".Translate(), ref unloadAllSurplus,
                "HaulersDream.Setting.UnloadAllSurplusDesc".Translate());
            // Stockpile-style per-item picker: keep all / keep at most N / always unload, per item def.
            int ruleCount = ItemRuleCount;
            string itemBtn = ruleCount > 0
                ? "HaulersDream.Setting.ItemUnloadButtonN".Translate(ruleCount)
                : "HaulersDream.Setting.ItemUnloadButton".Translate();
            if (l.ButtonText(itemBtn))
                Find.WindowStack.Add(new Dialog_ItemUnloadSettings(this));
            if (markForUnload)
            {
                // "Put it away before relaxing" — unload before each downtime activity (bypassing the accumulate window).
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeSleep".Translate(), ref unloadBeforeSleep,
                    "HaulersDream.Setting.UnloadBeforeSleepDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeLeisure".Translate(), ref unloadBeforeLeisure,
                    "HaulersDream.Setting.UnloadBeforeLeisureDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.UnloadBeforeEating".Translate(), ref unloadBeforeEating,
                    "HaulersDream.Setting.UnloadBeforeEatingDesc".Translate());
            }

            l.GapLine();
            l.Label("HaulersDream.Setting.ShareHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareForBuilding".Translate(), ref shareForBuilding);
            l.CheckboxLabeled("HaulersDream.Setting.BuildFromInventory".Translate(), ref buildFromInventory,
                "HaulersDream.Setting.BuildFromInventoryDesc".Translate());
            if (buildFromInventory)
                l.CheckboxLabeled("HaulersDream.Setting.BuildFromInventoryPartial".Translate(), ref buildFromInventoryPartial,
                    "HaulersDream.Setting.BuildFromInventoryPartialDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareForCrafting".Translate(), ref shareForCrafting,
                "HaulersDream.Setting.ShareForCraftingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.InventoryCraftDeliver".Translate(), ref inventoryCraftDeliver,
                "HaulersDream.Setting.InventoryCraftDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareMeetInMiddle".Translate(), ref shareMeetInMiddle);
            l.CheckboxLabeled("HaulersDream.Setting.BatchWorkDeliveries".Translate(), ref batchWorkDeliveries,
                "HaulersDream.Setting.BatchWorkDeliveriesDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.InventoryConstructDeliver".Translate(), ref inventoryConstructDeliver,
                "HaulersDream.Setting.InventoryConstructDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareHandHauled".Translate(), ref shareHandHauledToStorage,
                "HaulersDream.Setting.ShareHandHauledDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.MealsOnWheels".Translate(), ref mealsOnWheels,
                "HaulersDream.Setting.MealsOnWheelsDesc".Translate());

            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.BulkHaul".Translate(), ref bulkHaul,
                "HaulersDream.Setting.BulkHaulDesc".Translate());
            if (bulkHaul)
            {
                if (l.RadioButton("HaulersDream.Setting.BulkHaulSecond".Translate(),
                        bulkHaulTrigger == BulkHaulTrigger.SecondTasked,
                        tooltip: "HaulersDream.Setting.BulkHaulSecondDesc".Translate()))
                    bulkHaulTrigger = BulkHaulTrigger.SecondTasked;
                if (l.RadioButton("HaulersDream.Setting.BulkHaulAlways".Translate(),
                        bulkHaulTrigger == BulkHaulTrigger.Always,
                        tooltip: "HaulersDream.Setting.BulkHaulAlwaysDesc".Translate()))
                    bulkHaulTrigger = BulkHaulTrigger.Always;
                l.CheckboxLabeled("HaulersDream.Setting.HaulNearbyOption".Translate(), ref haulNearbyOption,
                    "HaulersDream.Setting.HaulNearbyOptionDesc".Translate());
                l.CheckboxLabeled("HaulersDream.Setting.HaulOversized".Translate(), ref haulOversizedInInventory,
                    "HaulersDream.Setting.HaulOversizedDesc".Translate());
            }

            // The "Pick up X" right-click order is independent of the bulk-haul sweep (its provider gates only on
            // manualPickupOption), so it lives OUTSIDE the bulkHaul block — reachable even with bulk-haul turned off.
            l.CheckboxLabeled("HaulersDream.Setting.ManualPickup".Translate(), ref manualPickupOption,
                "HaulersDream.Setting.ManualPickupDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.SweepNearbyWhileWorking".Translate(), ref sweepNearbyWhileWorking,
                "HaulersDream.Setting.SweepNearbyWhileWorkingDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.LoadPackAnimalBulk".Translate(), ref loadPackAnimalBulk,
                "HaulersDream.Setting.LoadPackAnimalBulkDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AutoDivertToPackAnimal".Translate(), ref autoDivertToPackAnimal,
                "HaulersDream.Setting.AutoDivertToPackAnimalDesc".Translate());

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkUnloadCarriers".Translate(), ref enableBulkUnloadCarriers,
                "HaulersDream.Setting.EnableBulkUnloadCarriersDesc".Translate());
            if (enableBulkUnloadCarriers)
            {
                l.Label("HaulersDream.Setting.MinFreeSpaceToUnloadCarrier".Translate(minFreeSpaceToUnloadCarrierPct.ToStringPercent()));
                minFreeSpaceToUnloadCarrierPct = Mathf.Round(l.Slider(minFreeSpaceToUnloadCarrierPct, 0.1f, 0.9f) * 20f) / 20f;
                l.CheckboxLabeled("HaulersDream.Setting.ReserveCarrierOnUnload".Translate(), ref reserveCarrierOnUnload,
                    "HaulersDream.Setting.ReserveCarrierOnUnloadDesc".Translate());
                l.Label("HaulersDream.Setting.VisualUnloadDelay".Translate(visualUnloadDelay));
                visualUnloadDelay = Mathf.RoundToInt(l.Slider(visualUnloadDelay, 0f, 30f));
            }

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadTransporters".Translate(), ref enableBulkLoadTransporters,
                "HaulersDream.Setting.EnableBulkLoadTransportersDesc".Translate());
            if (enableBulkLoadTransporters)
            {
                l.Label("HaulersDream.Setting.BulkLoadAiUpdateFrequency".Translate(bulkLoadAiUpdateFrequency));
                bulkLoadAiUpdateFrequency = Mathf.RoundToInt(l.Slider(bulkLoadAiUpdateFrequency, 10f, 240f) / 10f) * 10;
            }

            l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadPortal".Translate(), ref enableBulkLoadPortal,
                "HaulersDream.Setting.EnableBulkLoadPortalDesc".Translate());

            // Vehicle Framework compat — only shown when VF is loaded (the rows are pure noise otherwise; every VF
            // consumer also gates on VehicleFrameworkCompat.IsActive, so a hidden row never changes behaviour). The
            // master's tooltip explains the §1 toggle semantics: the safety guards are NOT gated on it.
            if (VehicleFrameworkCompat.IsActive)
            {
                l.CheckboxLabeled("HaulersDream.Setting.EnableVehicleFramework".Translate(), ref enableVehicleFramework,
                    "HaulersDream.Setting.EnableVehicleFrameworkDesc".Translate());
                if (enableVehicleFramework)
                    l.CheckboxLabeled("HaulersDream.Setting.EnableBulkLoadVehicles".Translate(), ref enableBulkLoadVehicles,
                        "HaulersDream.Setting.EnableBulkLoadVehiclesDesc".Translate());
            }

            l.CheckboxLabeled("HaulersDream.Setting.HaulToStack".Translate(), ref haulToStack,
                "HaulersDream.Setting.HaulToStackDesc".Translate());

            l.Gap(6f);
            l.CheckboxLabeled("HaulersDream.Setting.ConstructTether".Translate(), ref orderedConstructTether,
                "HaulersDream.Setting.ConstructTetherDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulToSiteOption".Translate(), ref haulToSiteOption,
                "HaulersDream.Setting.HaulToSiteOptionDesc".Translate());

            l.Gap(6f);
            l.CheckboxLabeled("HaulersDream.Setting.PlanRoutes".Translate(), ref planRoutes,
                "HaulersDream.Setting.PlanRoutesDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.PlanCrafting".Translate(), ref planCrafting,
                "HaulersDream.Setting.PlanCraftingDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.BatchByDefault".Translate(), ref batchByDefault,
                "HaulersDream.Setting.BatchByDefaultDesc".Translate());
            l.Label("HaulersDream.Setting.DefaultBatchSize".Translate(defaultBatchSize));
            defaultBatchSize = Mathf.RoundToInt(l.Slider(defaultBatchSize, 1f, 200f));

            l.GapLine();
            l.Label("HaulersDream.Setting.AutoStripHeader".Translate());
            if (l.ButtonText("HaulersDream.Setting.AutoStripMode".Translate(AutoStripModeLabel(autoStripMode))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.AllHauls), () => autoStripMode = AutoStripMode.AllHauls),
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.DisposalOnly), () => autoStripMode = AutoStripMode.DisposalOnly),
                    new FloatMenuOption(AutoStripModeLabel(AutoStripMode.Off), () => autoStripMode = AutoStripMode.Off),
                }));
            if (autoStripMode != AutoStripMode.Off)
                l.CheckboxLabeled("HaulersDream.Setting.StripColonists".Translate(), ref stripColonistCorpses,
                    "HaulersDream.Setting.StripColonistsDesc".Translate());
            // Cross-reference: clarify that "never" only disables strip-WHILE-hauling, not the separate
            // manual-strip gear haul (haulStrip, in the work-types section below). Shown always — it explains
            // the "never" option, which is only meaningful when the mode is Off. NO gating-logic change.
            l.Label("HaulersDream.Setting.AutoStripCrossRef".Translate());
            // The tainted policies also govern corpse strip ORDERS (the haulStrip scoop), so they stay
            // visible while either consumer is on — hiding an active Destroy policy would be a trap.
            if (autoStripMode != AutoStripMode.Off || haulStrip)
            {
                if (l.ButtonText("HaulersDream.Setting.TaintedSmeltable".Translate(TaintedPolicyLabel(taintedSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedSmeltablePolicy = p));
                if (l.ButtonText("HaulersDream.Setting.TaintedNonSmeltable".Translate(TaintedPolicyLabel(taintedNonSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedNonSmeltablePolicy = p));
            }

            // Haul after slaughter — independent of the auto-strip mode (a fresh KILL hauls the carcass to
            // storage so it doesn't rot in place). Two independent toggles, both default ON.
            l.CheckboxLabeled("HaulersDream.Setting.HaulTamedSlaughter".Translate(), ref haulTamedSlaughter,
                "HaulersDream.Setting.HaulTamedSlaughterDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulWildKills".Translate(), ref haulWildKills,
                "HaulersDream.Setting.HaulWildKillsDesc".Translate());

            // Spoiling-first ingredient selection — prefer the most-perishable already-valid candidate when a
            // colonist picks ingredients for a bill (butcher = corpse; cook = rottable food). Two toggles, both ON.
            l.CheckboxLabeled("HaulersDream.Setting.ButcherSpoilingFirst".Translate(), ref butcherSpoilingFirst,
                "HaulersDream.Setting.ButcherSpoilingFirstDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.CookSpoilingFirst".Translate(), ref cookSpoilingFirst,
                "HaulersDream.Setting.CookSpoilingFirstDesc".Translate());

            l.GapLine();
            l.Label("HaulersDream.Setting.Overload".Translate(OverloadLevelLabel(overloadLevel)));
            overloadLevel = Mathf.RoundToInt(l.Slider(overloadLevel, 0f, OverloadTuning.MaxLevel));
            l.Label("HaulersDream.Setting.OverloadDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.StrictCarryWeight".Translate(), ref strictCarryWeight,
                "HaulersDream.Setting.StrictCarryWeightDesc".Translate());
            // Keep working when full (opt-in, default OFF). Distinct from strict carry weight: this keeps the
            // overload-and-accumulate ceiling but stops a full pawn breaking off to unload — it works on and
            // only sheds the load before a long relocation. The margin slider is the hysteresis for that rule.
            l.CheckboxLabeled("HaulersDream.Setting.KeepWorkingWhenFull".Translate(), ref keepWorkingWhenFull,
                "HaulersDream.Setting.KeepWorkingWhenFullDesc".Translate());
            if (keepWorkingWhenFull)
            {
                l.Label("HaulersDream.Setting.KeepWorkingMargin".Translate(keepWorkingMarginCells),
                    tooltip: "HaulersDream.Setting.KeepWorkingMarginDesc".Translate());
                keepWorkingMarginCells = Mathf.RoundToInt(l.Slider(keepWorkingMarginCells, 0f, 30f));
            }
            l.CheckboxLabeled("HaulersDream.Setting.OpportunisticUnload".Translate(), ref opportunisticUnload,
                "HaulersDream.Setting.OpportunisticUnloadDesc".Translate());

            l.GapLine();
            l.Label("HaulersDream.Setting.RouteMaxAmount".Translate(routeMaxAmount));
            routeMaxAmount = Mathf.RoundToInt(l.Slider(routeMaxAmount, 5f, RouteSelection.HardCap));

            // Route calculation (global defaults; each are also overridable per target type in the route dialog).
            l.Label("HaulersDream.Setting.RouteCalcHeader".Translate());
            if (l.ButtonText("HaulersDream.Setting.RouteSelection".Translate(SelectionMethodLabel(routeSelectionMethod))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(SelectionMethodLabel(RouteSelectionMethod.MostStopsPerTravel), () => routeSelectionMethod = RouteSelectionMethod.MostStopsPerTravel),
                    new FloatMenuOption(SelectionMethodLabel(RouteSelectionMethod.NearestToTarget), () => routeSelectionMethod = RouteSelectionMethod.NearestToTarget),
                }));
            if (l.ButtonText("HaulersDream.Setting.RouteDistance".Translate(DistanceBasisLabel(routeDistanceBasis))))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption(DistanceBasisLabel(RouteDistanceBasis.StraightLine), () => routeDistanceBasis = RouteDistanceBasis.StraightLine),
                    new FloatMenuOption(DistanceBasisLabel(RouteDistanceBasis.WalkingPath), () => routeDistanceBasis = RouteDistanceBasis.WalkingPath),
                }));
            l.Label("HaulersDream.Setting.RouteOrderEffort".Translate(routeOrderExactMax));
            routeOrderExactMax = Mathf.RoundToInt(l.Slider(routeOrderExactMax, 8f, 14f));

            l.Label(craftBatchTimeoutHours <= 0f
                ? "HaulersDream.Setting.CraftBatchTimeoutOff".Translate()
                : "HaulersDream.Setting.CraftBatchTimeout".Translate(craftBatchTimeoutHours.ToString("0.#")));
            craftBatchTimeoutHours = Mathf.Round(l.Slider(craftBatchTimeoutHours, 0f, 8f) * 2f) / 2f;

            l.GapLine();
            l.Label("HaulersDream.Setting.WorkTypesHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.HaulHarvest".Translate(), ref haulHarvest);
            l.CheckboxLabeled("HaulersDream.Setting.HaulMining".Translate(), ref haulMining);
            l.CheckboxLabeled("HaulersDream.Setting.HaulDeepDrill".Translate(), ref haulDeepDrill);
            l.CheckboxLabeled("HaulersDream.Setting.HaulDeconstruct".Translate(), ref haulDeconstruct);
            l.CheckboxLabeled("HaulersDream.Setting.HaulAnimals".Translate(), ref haulAnimals);
            l.CheckboxLabeled("HaulersDream.Setting.HaulStrip".Translate(), ref haulStrip,
                "HaulersDream.Setting.HaulStripDesc".Translate());
            // Cross-reference back to "Auto-strip while hauling" — independent control, not coupled. NO gating change.
            l.Label("HaulersDream.Setting.HaulStripCrossRef".Translate());

            l.GapLine();
            l.Label("HaulersDream.Setting.WorkOverrideHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanHaul".Translate(), ref allPawnsCanHaul,
                "HaulersDream.Setting.AllCanHaulDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanClean".Translate(), ref allPawnsCanClean,
                "HaulersDream.Setting.AllCanCleanDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllCanCutPlants".Translate(), ref allPawnsCanCutPlants,
                "HaulersDream.Setting.AllCanCutPlantsDesc".Translate());

            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.PauseWhileDrafted".Translate(), ref pauseWhileDrafted);
            l.CheckboxLabeled("HaulersDream.Setting.AllowMechanoids".Translate(), ref allowMechanoids,
                "HaulersDream.AllowMechanoidsDesc".Translate());
            l.CheckboxLabeled("HaulersDream.AllowAnimals".Translate(), ref allowAnimals,
                "HaulersDream.AllowAnimalsDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AllowIncapable".Translate(), ref allowIncapable);
            l.CheckboxLabeled("HaulersDream.Setting.EnableOnNonHomeMaps".Translate(), ref enableOnNonHomeMaps);
            l.CheckboxLabeled("HaulersDream.Setting.HideGizmo".Translate(), ref hideGizmo);

            l.GapLine();
            l.Label("HaulersDream.Setting.UnloadGrace".Translate(unloadGraceTicks));
            unloadGraceTicks = Mathf.RoundToInt(l.Slider(unloadGraceTicks, 0f, 7500f) / 50f) * 50;
            l.Label(intervalUnloadHours <= 0f
                ? "HaulersDream.Setting.IntervalUnloadOff".Translate()
                : "HaulersDream.Setting.IntervalUnload".Translate(intervalUnloadHours.ToString("0.#")));
            intervalUnloadHours = Mathf.Round(l.Slider(intervalUnloadHours, 0f, 24f) * 2f) / 2f;

            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.AlertCannotUnload".Translate(), ref alertCannotUnload,
                "HaulersDream.Setting.AlertCannotUnloadDesc".Translate());
            if (alertCannotUnload)
            {
                l.Label("HaulersDream.Setting.AlertStuckHours".Translate(alertStuckHours.ToString("0.#")));
                alertStuckHours = Mathf.Round(l.Slider(alertStuckHours, 1f, 72f) * 2f) / 2f;
            }

            l.GapLine();
            l.CheckboxLabeled("HaulersDream.Setting.VerboseLogging".Translate(), ref verboseLogging);

            settingsHeight = Mathf.Max(l.CurHeight + 12f, rect.height);
            l.End();
            Widgets.EndScrollView();
        }
    }
}
