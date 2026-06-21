using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// MEALS ON WHEELS — when vanilla finds no food for a hungry FREE COLONIST, let them eat acceptable
    /// food carried in ANOTHER player-faction pawn's (or pack animal's) inventory, so they don't trek to
    /// a distant stockpile. A postfix on <see cref="FoodUtility.TryFindBestFoodSourceFor"/>
    /// (<see cref="Priority.Low"/>, only when vanilla failed) that sets foodSource/foodDef/__result and
    /// lets vanilla's own downstream do the rest.
    ///
    /// LOAD-BEARING PATH (decompile-verified against RW 1.6 Assembly-CSharp): once foodSource resolves to
    /// a Thing inside another pawn's <c>Pawn_InventoryTracker</c>, <c>JobGiver_GetFood.TryGiveJob</c>
    /// (JobGiver_GetFood.cs:118) reads <c>foodSource.ParentHolder as Pawn_InventoryTracker</c> and, when the
    /// holder != eater, issues <c>JobDefOf.TakeFromOtherInventory</c> (NOT Ingest) — the eater walks to the
    /// holder and the food is transferred into the EATER'S OWN inventory. On the next think-tick the still-
    /// hungry eater re-runs the search; vanilla's own-inventory pass now finds it (holder == eater) →
    /// <c>JobDefOf.Ingest</c> with <c>eatingFromInventory == true</c> → the meal is chewed. So the food is
    /// genuinely eaten via two vanilla jobs; NO custom HD job is required for correctness. The returned
    /// foodSource MUST be left IN the holder's inventory (never dropped/respawned) or the
    /// <c>ParentHolder is Pawn_InventoryTracker</c> detection breaks and the chain never fires.
    ///
    /// SCOPE (faithful to the reference mod): the postfix only acts when the caller passes BOTH
    /// canUseInventory AND canUsePackAnimalInventory — the implicit two-caller whitelist of
    /// <c>JobGiver_GetFood</c> + <c>WorkGiver_FeedPatient</c>, both of whose drivers can consume a food source
    /// living in a THIRD pawn's inventory. The baby-feed path (<c>ChildcareUtility.FindBabyFoodForBaby</c>) and the
    /// royal-title/lay-down availability probes pass canUsePackAnimalInventory:false and are NOT eligible
    /// (their drivers can't take inventory food, so resolving one would strand/churn the feeder).
    ///
    /// Stricter than the reference mod ("Meals On Wheels"): drafted/downed/mental holders are skipped
    /// (<see cref="InventoryShare.IsEligibleCarrier"/>), a baby's food being hand-fed is left alone, the
    /// STACK is reserved (not just the holder) so two eaters can't race one meal, and a rot-priority pass
    /// prefers a carried meal about to spoil. Acceptability (food policy / ideology / royal title /
    /// teetotaler / preferability window / nutrition) is delegated entirely to vanilla
    /// <see cref="FoodUtility.BestFoodInInventory"/>; the pure two-pass ordering lives in
    /// <see cref="MealsOnWheelsSelection"/> (NUnit-tested).
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
    public static class Patch_TryFindBestFoodSourceFor
    {
        // Priority.Low so HD runs AFTER any other food-finder postfix (e.g. a literal reference-mod
        // install or Smarter Food Selection); whichever sets __result first wins, the other early-outs.
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(
            ref bool __result, Pawn getter, Pawn eater,
            ref Thing foodSource, ref ThingDef foodDef,
            bool canUseInventory, bool canUsePackAnimalInventory)
        {
            // --- cheap early-outs (in order, before any scan/allocation) ---
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.mealsOnWheels)
                return;                          // single config gate, default ON
            if (__result)
                return;                          // vanilla already found food (map / own inv / pack animal) — NEVER override
            if (!canUseInventory)
                return;                          // caller forbade inventory use
            if (!canUsePackAnimalInventory)
                return;                          // caller-flag whitelist (faithful to the reference mod): only
                                                 // JobGiver_GetFood + WorkGiver_FeedPatient pass canUsePackAnimalInventory:true,
                                                 // and BOTH route through a driver that consumes a third-pawn-inventory food
                                                 // source (Ingest->TakeFromOtherInventory / FoodFeedPatient->CheckItemCarriedByOtherPawn).
                                                 // ChildcareUtility.FindBabyFoodForBaby and the royal-title/lay-down availability
                                                 // PROBES pass false; their drivers (BottleFeedBaby) can't take inventory food, so a
                                                 // resolved carried source would strand/churn the feeder. Excluding them here matches
                                                 // the reference mod's implicit two-caller scope (decompile-verified, RW 1.6).
            if (eater == null || !eater.IsFreeColonist)
                return;                          // eater scope = free colonists only (faithful to the reference mod)
            if (getter == null
                || !getter.RaceProps.ToolUser
                || !getter.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return;                          // the fetching hand must have hands (getter usually == eater)
            var map = eater.Map;
            if (map == null)
                return;                          // caravan / world-map is out of scope (mapPawns would NRE)

            bool allowDrug = !eater.IsTeetotaler();

            // --- scan eligible holders (this list includes animals, so pack/colony animals are covered) ---
            Thing best = null;
            MealCandidatePass bestPass = MealCandidatePass.None;
            int bestTicks = MealsOnWheelsSelection.NeverRots;
            int bestDist = int.MaxValue;
            int bestIndex = int.MaxValue;
            int idx = 0;

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var holder = pawns[i];

                // HD eligibility (excludes self/unspawned/dead/downed/drafted/mental/mid-HD-batch).
                if (!InventoryShare.IsEligibleCarrier(holder, getter))
                    continue;
                // [MOW] A VF vehicle is OFF-LIMITS as a food source: its cargo hold is a player-curated loadout VF
                // manages (e.g. road-trip rations), so meals-on-wheels must not eat OUT of it and undo the loadout.
                // Skip the vehicle itself. Gated on IsVehicle ONLY (a safety fix, not a feature): IsVehicle returns
                // false when VF is absent. (Distinct from the InVehicle skip below, which excludes a holder RIDING
                // inside a vehicle.)
                if (VehicleFrameworkCompat.IsVehicle(holder))
                    continue;
                // [MOW] Skip a holder riding INSIDE a vehicle (seat/cargo) — its inventory is unreachable, so pathing
                // to it is wasted. Gated on InVehicle ONLY (a safety fix, not a feature): InVehicle returns false when
                // VF is absent.
                if (VehicleFrameworkCompat.InVehicle(holder))
                    continue;
                // Don't steal the food a parent is mid-feeding to an infant.
                if (holder.jobs?.curDriver is JobDriver_FeedBaby)
                    continue;
                var inv = holder.inventory?.innerContainer;
                if (inv == null || inv.Count == 0)
                    continue;
                // Forbidden / allowed-area at the holder's cell (mirror vanilla's ground-food gate).
                if (holder.Position.IsForbidden(getter))
                    continue;

                // Acceptability via vanilla (folds in food policy / ideology / title / teetotaler /
                // preferability window / nutrition*stack). Two passes per holder, exactly like the
                // reference mod: Pass-1 queries the MealAwful floor (no drug) so a Fresh meal about to
                // spoil is found even when a lower-preferability item sits earlier in the inventory
                // (BestFoodInInventory returns the FIRST window-match by index, not the best); if that
                // stack is rot-rescue-eligible it wins. Pass-2 widens to DesperateOnly (drug allowed
                // unless teetotaler) for any acceptable carried food. Core then classifies the stack.
                Thing stack = null;
                MealCandidatePass pass = MealCandidatePass.None;
                int ticksUntilRot = MealsOnWheelsSelection.NeverRots;

                Thing rescueStack = FoodUtility.BestFoodInInventory(holder, eater,
                    FoodPreferability.MealAwful, FoodPreferability.MealLavish, 0f, allowDrug: false);
                if (rescueStack != null
                    && ClassifyStack(rescueStack, out var rescuePass, out var rescueTicks)
                    && rescuePass == MealCandidatePass.RotRescue)
                {
                    stack = rescueStack;
                    pass = rescuePass;
                    ticksUntilRot = rescueTicks;
                }
                else
                {
                    Thing anyStack = FoodUtility.BestFoodInInventory(holder, eater,
                        FoodPreferability.DesperateOnly, FoodPreferability.MealLavish, 0f, allowDrug);
                    if (anyStack != null && ClassifyStack(anyStack, out var anyPass, out var anyTicks))
                    {
                        stack = anyStack;
                        pass = anyPass;
                        ticksUntilRot = anyTicks;
                    }
                }
                if (stack == null)
                    continue;

                // HD stack reservation (the reference mod reserves only the holder) — so two eaters can't
                // both target the same meal. A read-gate (the postfix can't hold a reservation).
                if (!getter.CanReserve(stack))
                    continue;

                // Reach last (a pathfind) — only for a holder that actually has acceptable, reservable food.
                if (!getter.CanReach(holder, PathEndMode.Touch, Danger.Some))
                    continue;

                int dist = IntVec3Utility.ManhattanDistanceFlat(getter.Position, holder.Position);

                if (best == null || MealsOnWheelsSelection.Compare(
                        pass, ticksUntilRot, dist, idx,
                        bestPass, bestTicks, bestDist, bestIndex) < 0)
                {
                    best = stack;
                    bestPass = pass;
                    bestTicks = ticksUntilRot;
                    bestDist = dist;
                    bestIndex = idx;
                }
                idx++;
            }

            if (best == null)
                return;                          // nothing acceptable carried — leave __result false (pure vanilla)

            // --- the load-bearing write: leave the stack IN the holder's inventory ---
            foodSource = best;
            foodDef = FoodUtility.GetFinalIngestibleDef(foodSource, false);
            __result = true;

            // Meet-in-the-middle: nudge an IDLE holder toward the fetcher (free; self-skips for busy
            // holders and float-menu previews). Vanilla's TakeFromOtherInventory already walks the eater
            // to the holder, so this is pure convergence polish.
            if (s.shareMeetInMiddle)
                SharedInventoryApproach.MaybeApproach(best, getter);

            HDLog.Dbg($"MealsOnWheels: {eater} -> {foodDef?.defName ?? "?"} carried by " +
                      $"{(best.ParentHolder as Pawn_InventoryTracker)?.pawn} (pass {bestPass}).");
        }

        /// <summary>Extract the rot primitives off an already-acceptable <paramref name="stack"/> (the
        /// non-null result of FoodUtility.BestFoodInInventory) and classify it via the pure Core decision.
        /// Returns false only for the degenerate None (which Classify never produces for acceptable:true,
        /// so a non-null stack always yields a real candidate). ticksUntilRot uses the NeverRots sentinel
        /// for a frozen/inactive/non-rottable stack.</summary>
        private static bool ClassifyStack(Thing stack, out MealCandidatePass pass, out int ticksUntilRot)
        {
            var rot = stack.TryGetComp<CompRottable>();
            bool freshActiveRottable = rot != null && rot.Active && rot.Stage == RotStage.Fresh;
            ticksUntilRot = (rot != null && rot.Active)
                ? rot.TicksUntilRotAtCurrentTemp
                : MealsOnWheelsSelection.NeverRots;
            int prefRank = (int)stack.def.ingestible.preferability;
            pass = MealsOnWheelsSelection.Classify(true, prefRank, freshActiveRottable, ticksUntilRot);
            return MealsOnWheelsSelection.IsCandidate(pass);
        }
    }
}
