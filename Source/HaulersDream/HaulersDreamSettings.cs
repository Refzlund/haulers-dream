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
        // HD-tagged loot. "Surplus" = above the pawn's keep-stock (food / drugs / inventoryStock / CE loadout), the
        // exact set vanilla itself treats as unloadable; caravan-loading inventory (IsFormingCaravan) is left alone.
        // More aggressive than vanilla's occasional auto-unload — turn OFF if a loadout/stock mod (e.g. Smart
        // Medicine stock-up, a sidearm mod) keeps items in inventory WITHOUT registering them as keep-stock.
        public bool unloadAllSurplus = true;
        public bool shareForBuilding = true;  // carried materials count for construction
        public bool shareForCrafting = true;  // carried ingredients count for crafting bills
        // Auto crafting bills: gather all ingredient stacks into inventory in one (overweight) sweep, then let
        // VANILLA's own DoBill craft from the carried stock (via the crafting-share relay). The earlier direct-craft
        // design was retired for a dup risk; this prep-gather design never touches the recipe flow.
        public bool inventoryCraftDeliver = true;
        public bool shareMeetInMiddle = true; // an idle carrier walks toward the fetcher
        public bool batchWorkDeliveries = true; // carry materials for many queued builds per trip (in hands; no slowdown)
        public bool inventoryConstructDeliver = true; // carry material for a big single needer in inventory (fewer trips)
        public bool shareHandHauledToStorage = false; // let a worker claim a stack a colonist is hand-hauling TO STORAGE (opt-in)

        // --- haul to stack (prefer topping up existing stacks; destination cells unreserved) ---
        public bool haulToStack = true;

        // --- ordered construction (F16): right-click orders tether haul+build; haul-to-site menu option ---
        public bool orderedConstructTether = true; // "prioritize constructing" hauls AND builds as one task
        public bool haulToSiteOption = true;       // the "Prioritize hauling materials to X" right-click order

        // --- planners (the right-click "Plan prioritized …" tools) ---
        public bool planRoutes = true;   // route planner (harvest/mine/clean/deconstruct/construction routes)
        public bool planCrafting = true; // station crafting planner (batch a bill N times in one go)

        // --- bulk hauling (the native Pick-Up-And-Haul: a haul trip sweeps everything around into inventory) ---
        public bool bulkHaul = true;
        // Always = every haul sweeps; SecondTasked (default) = automatic hauls always sweep, but a player-ORDERED
        // haul sweeps only when a second nearby haul has also been ordered — so ordering one haul stays surgical.
        public BulkHaulTrigger bulkHaulTrigger = BulkHaulTrigger.SecondTasked;

        // --- pack-animal loading on caravans / temporary maps ---
        public bool loadPackAnimalBulk = true;       // the manual "Load nearby items onto pack animal (bulk)" order
        public bool autoDivertToPackAnimal = true;   // an over-encumbered caravan pawn auto-loads the nearest pack animal

        // --- auto strip on haul (corpse hauls strip the body; loot rides in the hauler's inventory) ---
        public AutoStripMode autoStripMode = AutoStripMode.AllHauls;
        public bool stripColonistCorpses = false;              // your own dead are not loot (opt-in)
        public TaintedApparelPolicy taintedSmeltablePolicy = TaintedApparelPolicy.Take;
        public TaintedApparelPolicy taintedNonSmeltablePolicy = TaintedApparelPolicy.Take;

        // --- smart overload (carry past 100% capacity to save trips) ---
        // 0 = no slowdown (carry freely) ... FairLevel = balanced ... OffLevel = never overload.
        public int overloadLevel = OverloadTuning.FairLevel;

        // Strict carry weight: never go past 100% capacity (overrides overload to off), and don't break
        // off to unload when full — keep working and leave the surplus for normal hauling.
        public bool strictCarryWeight = false;

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
        public bool allowIncapable = false;   // let pawns incapable of hauling still scoop their own yields

        // --- per-work-type toggles ---
        public bool haulHarvest = true;
        public bool haulMining = true;
        public bool haulDeepDrill = true;
        public bool haulDeconstruct = true;
        public bool haulAnimals = true;
        public bool haulStrip = true;   // gear removed by a strip order (pawn or corpse) gets scooped + hauled

        // --- unloading ---
        public int unloadGraceTicks = 60;       // don't unload within this many ticks of the last pickup
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
            Scribe_Values.Look(ref unloadAllSurplus, "unloadAllSurplus", true);
            Scribe_Values.Look(ref shareForBuilding, "shareForBuilding", true);
            Scribe_Values.Look(ref shareForCrafting, "shareForCrafting", true);
            Scribe_Values.Look(ref inventoryCraftDeliver, "inventoryCraftDeliver", true);
            Scribe_Values.Look(ref shareMeetInMiddle, "shareMeetInMiddle", true);
            Scribe_Values.Look(ref batchWorkDeliveries, "batchWorkDeliveries", true);
            Scribe_Values.Look(ref inventoryConstructDeliver, "inventoryConstructDeliver", true);
            Scribe_Values.Look(ref shareHandHauledToStorage, "shareHandHauledToStorage", false);
            Scribe_Values.Look(ref bulkHaul, "bulkHaul", true);
            Scribe_Values.Look(ref bulkHaulTrigger, "bulkHaulTrigger", BulkHaulTrigger.SecondTasked);
            Scribe_Values.Look(ref loadPackAnimalBulk, "loadPackAnimalBulk", true);
            Scribe_Values.Look(ref autoDivertToPackAnimal, "autoDivertToPackAnimal", true);
            Scribe_Values.Look(ref haulToStack, "haulToStack", true);
            Scribe_Values.Look(ref orderedConstructTether, "orderedConstructTether", true);
            Scribe_Values.Look(ref haulToSiteOption, "haulToSiteOption", true);
            Scribe_Values.Look(ref planRoutes, "planRoutes", true);
            Scribe_Values.Look(ref planCrafting, "planCrafting", true);
            Scribe_Values.Look(ref autoStripMode, "autoStripMode", AutoStripMode.AllHauls);
            Scribe_Values.Look(ref stripColonistCorpses, "stripColonistCorpses", false);
            Scribe_Values.Look(ref taintedSmeltablePolicy, "taintedSmeltablePolicy", TaintedApparelPolicy.Take);
            Scribe_Values.Look(ref taintedNonSmeltablePolicy, "taintedNonSmeltablePolicy", TaintedApparelPolicy.Take);
            Scribe_Values.Look(ref overloadLevel, "overloadLevel", OverloadTuning.FairLevel);
            Scribe_Values.Look(ref strictCarryWeight, "strictCarryWeight", false);
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
            Scribe_Values.Look(ref unloadGraceTicks, "unloadGraceTicks", 60);
            Scribe_Values.Look(ref intervalUnloadHours, "intervalUnloadHours", 1f);
            Scribe_Values.Look(ref alertCannotUnload, "alertCannotUnload", true);
            Scribe_Values.Look(ref alertStuckHours, "alertStuckHours", 12f);
            Scribe_Values.Look(ref enableOnNonHomeMaps, "enableOnNonHomeMaps", true);
            Scribe_Values.Look(ref hideGizmo, "hideGizmo", false);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
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

            l.Label("HaulersDream.Setting.CarryLimit".Translate(carryLimitFraction.ToStringPercent()));
            carryLimitFraction = l.Slider(carryLimitFraction, CarryMath.MinFraction, CarryMath.MaxFraction);

            l.GapLine();
            bool dropThenHaul = pickupMode == PickupMode.DropThenHaul;
            l.CheckboxLabeled("HaulersDream.Setting.DropThenHaul".Translate(), ref dropThenHaul,
                "HaulersDream.Setting.DropThenHaulDesc".Translate());
            pickupMode = dropThenHaul ? PickupMode.DropThenHaul : PickupMode.DirectToInventory;
            l.CheckboxLabeled("HaulersDream.Setting.MarkForUnload".Translate(), ref markForUnload);
            l.CheckboxLabeled("HaulersDream.Setting.UnloadAllSurplus".Translate(), ref unloadAllSurplus,
                "HaulersDream.Setting.UnloadAllSurplusDesc".Translate());

            l.GapLine();
            l.Label("HaulersDream.Setting.ShareHeader".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareForBuilding".Translate(), ref shareForBuilding);
            l.CheckboxLabeled("HaulersDream.Setting.ShareForCrafting".Translate(), ref shareForCrafting);
            l.CheckboxLabeled("HaulersDream.Setting.InventoryCraftDeliver".Translate(), ref inventoryCraftDeliver,
                "HaulersDream.Setting.InventoryCraftDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareMeetInMiddle".Translate(), ref shareMeetInMiddle);
            l.CheckboxLabeled("HaulersDream.Setting.BatchWorkDeliveries".Translate(), ref batchWorkDeliveries,
                "HaulersDream.Setting.BatchWorkDeliveriesDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.InventoryConstructDeliver".Translate(), ref inventoryConstructDeliver,
                "HaulersDream.Setting.InventoryConstructDeliverDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.ShareHandHauled".Translate(), ref shareHandHauledToStorage,
                "HaulersDream.Setting.ShareHandHauledDesc".Translate());

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
            }

            l.CheckboxLabeled("HaulersDream.Setting.LoadPackAnimalBulk".Translate(), ref loadPackAnimalBulk,
                "HaulersDream.Setting.LoadPackAnimalBulkDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.AutoDivertToPackAnimal".Translate(), ref autoDivertToPackAnimal,
                "HaulersDream.Setting.AutoDivertToPackAnimalDesc".Translate());

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
            // The tainted policies also govern corpse strip ORDERS (the haulStrip scoop), so they stay
            // visible while either consumer is on — hiding an active Destroy policy would be a trap.
            if (autoStripMode != AutoStripMode.Off || haulStrip)
            {
                if (l.ButtonText("HaulersDream.Setting.TaintedSmeltable".Translate(TaintedPolicyLabel(taintedSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedSmeltablePolicy = p));
                if (l.ButtonText("HaulersDream.Setting.TaintedNonSmeltable".Translate(TaintedPolicyLabel(taintedNonSmeltablePolicy))))
                    Find.WindowStack.Add(TaintedPolicyMenu(p => taintedNonSmeltablePolicy = p));
            }

            l.GapLine();
            l.Label("HaulersDream.Setting.Overload".Translate(OverloadLevelLabel(overloadLevel)));
            overloadLevel = Mathf.RoundToInt(l.Slider(overloadLevel, 0f, OverloadTuning.MaxLevel));
            l.Label("HaulersDream.Setting.OverloadDesc".Translate());
            l.CheckboxLabeled("HaulersDream.Setting.StrictCarryWeight".Translate(), ref strictCarryWeight,
                "HaulersDream.Setting.StrictCarryWeightDesc".Translate());
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
            l.CheckboxLabeled("HaulersDream.Setting.AllowMechanoids".Translate(), ref allowMechanoids);
            l.CheckboxLabeled("HaulersDream.Setting.AllowIncapable".Translate(), ref allowIncapable);
            l.CheckboxLabeled("HaulersDream.Setting.EnableOnNonHomeMaps".Translate(), ref enableOnNonHomeMaps);
            l.CheckboxLabeled("HaulersDream.Setting.HideGizmo".Translate(), ref hideGizmo);

            l.GapLine();
            l.Label("HaulersDream.Setting.UnloadGrace".Translate(unloadGraceTicks));
            unloadGraceTicks = Mathf.RoundToInt(l.Slider(unloadGraceTicks, 0f, 600f));
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
