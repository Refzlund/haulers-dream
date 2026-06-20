using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// A fully-resolved plan for batching ONE workbench bill: which ingredient def to source for each recipe
    /// slot, how many units of it per repetition, and the final number of reps after every cap
    /// (player-requested, ingredient availability, inventory mass, timeout) is applied. The dialog uses it to
    /// preview + clamp the sliders; the job uses it to pre-load and to consume per rep. The pure cap math lives
    /// in <see cref="CraftBatchMath"/> (unit-tested); this class is the RimWorld-facing layer that reads recipe
    /// ingredient counts (nutrition-aware via <see cref="IngredientCount.CountRequiredOfFor"/>), masses, work
    /// amount, and reachable stock, and feeds them in.
    /// </summary>
    public sealed class CraftBatchPlan
    {
        public Bill bill;
        public RecipeDef recipe;
        public readonly List<ThingDef> ingredientDefs = new List<ThingDef>(); // chosen source def, one per recipe slot
        public readonly List<int> perRepCounts = new List<int>();             // units of that def consumed per rep

        // True for an allowMixingIngredients recipe (cooked meals etc.): the plan does NOT freeze a per-slot def list
        // (ingredientDefs/perRepCounts stay EMPTY) — the batch driver recomputes each rep's mix from current stock by
        // value (CraftBatchMath.MixFillSlot). The batch is sized by VALUE in ResolveMixing, not by the scarcest def.
        public bool mixingRecipe;

        public int requestedReps;     // the player's slider value
        public int availabilityReps;  // floor by the scarcest ingredient's reachable stock
        public int massReps;          // floor by what fits in inventory on one pre-load (smart-overload aware)
        public int timeoutReps;       // floor by the wall-clock cap
        public int billReps = int.MaxValue; // floor by the bill's own remaining repeat count (RepeatCount mode; MaxValue = no bill cap)
        public int resolvedReps;      // final = min of the above

        public float massPerRepKg;
        public int ticksPerRep;       // pawn+bench-speed-adjusted estimate (for the timeout cap and time preview)
        public int timeoutTicks;      // 0 = no timeout

        public bool feasible;         // resolvedReps >= 1 and all slots sourceable
        public string blockReason;    // null when feasible; else a short human reason

        /// <summary>Whichever cap is the binding one, for the dialog to explain "why only N".</summary>
        public CraftBatchLimit BindingLimit
        {
            get
            {
                int r = resolvedReps;
                if (r >= requestedReps) return CraftBatchLimit.None;
                if (r == billReps) return CraftBatchLimit.BillRepeat;
                if (r == availabilityReps) return CraftBatchLimit.Resources;
                if (r == timeoutReps) return CraftBatchLimit.Timeout;
                // Mass only caps the count in no-overload mode — strict carry weight, slider Off, or Combat
                // Extended active (otherwise the pawn overloads/multi-trips).
                if (r == massReps && OverloadGate.NoOverload(HaulersDreamMod.Settings)) return CraftBatchLimit.Mass;
                return CraftBatchLimit.None;
            }
        }
    }

    public enum CraftBatchLimit { None, Resources, Mass, Timeout, BillRepeat }

    public static class CraftBatchPlanner
    {
        /// <summary>Recipes this feature can batch. We deliberately EXCLUDE unfinished-thing recipes (smithing,
        /// complex components) — their two-phase work/UFT flow is not what the pre-load+loop model handles — and
        /// require the bill to be an ordinary production bill that actually yields products.</summary>
        public static bool CanBatch(Bill bill)
        {
            // EXACT type, not "is Bill_Production": its subclasses (Bill_ProductionWithUft, Bill_Autonomous,
            // Bill_Mech) have their own finish/gestation flows our per-rep replica does NOT reproduce — batching
            // one would make products via the wrong path (or none). Plain production bills only.
            if (bill == null || bill.GetType() != typeof(Bill_Production) || bill.recipe == null)
                return false;
            // HD's batch layers a flag on top of the THREE vanilla repeat modes (its counting/planning models
            // their semantics). A modded repeat mode — e.g. Everybody Gets One's "one per person", whose target
            // scales with the live colonist count — must NOT be batched: HD doesn't model that dynamic target, so
            // it would mis-size / overshoot the batch. Such bills run their own (mod-provided) flow unbatched.
            var mode = ((Bill_Production)bill).repeatMode;
            if (mode != BillRepeatModeDefOf.RepeatCount && mode != BillRepeatModeDefOf.TargetCount
                && mode != BillRepeatModeDefOf.Forever)
                return false;
            var r = bill.recipe;
            if (r.UsesUnfinishedThing)
                return false;
            if (r.ingredients == null || r.ingredients.Count == 0)
                return false; // a no-ingredient recipe has nothing to pre-load → no benefit, and odd to "batch"
            // "Take entire stacks" recipes (smelt/burn) consume the WHOLE matched stack per run and scale their
            // special products by it — our fixed per-rep count model would under-consume and under-produce. Exclude.
            if (r.ignoreIngredientCountTakeEntireStacks)
                return false;
            // MIXING recipes (every vanilla cooked meal sets allowMixingIngredients=true — plus kibble, pemmican,
            // chemfuel, beer) fill ONE ingredient slot from MULTIPLE defs at craft time, choosing each rep's mix
            // from whatever stock remains, by VALUE (nutrition). These ARE batched now via a MIX-AWARE path: the
            // planner sizes the batch by total available VALUE (CraftBatchMath.MixAvailableReps) instead of freezing
            // one def per slot, and JobDriver_BatchCraft RECOMPUTES each rep's per-def mix from current inventory at
            // the start of the rep (CraftBatchMath.MixFillSlot, mirroring vanilla's AllowMix fill) before the
            // existing carry/place/consume loop runs unchanged. So a batched stew no longer "never mixes" or refuses
            // a rep no single def covers — it greedily value-fills like vanilla does, just N reps from one pre-load.
            return !r.products.NullOrEmpty() || !r.specialProducts.NullOrEmpty();
        }

        /// <summary>Does the bench have at least one bill this feature can batch right now?</summary>
        public static bool AnyBatchableBill(Building_WorkTable bench)
        {
            var bills = bench?.BillStack?.Bills;
            if (bills == null)
                return false;
            for (int i = 0; i < bills.Count; i++)
                if (CanBatch(bills[i]))
                    return true;
            return false;
        }

        /// <summary>A batchable bill THIS pawn is actually allowed to start (skill range, pawn restriction, slavery,
        /// etc.) — the exact gate vanilla's <c>WorkGiver_DoBill.JobOnThing</c> applies via
        /// <c>Bill.PawnAllowedToStartAnew</c>. Keeps the menu option + dialog list off bills the pawn can't do.</summary>
        public static bool CanPawnBatch(Pawn pawn, Bill bill)
        {
            if (pawn == null || !CanBatch(bill))
                return false;
            // The batch collects products + leftovers into inventory TAGGED — a pawn without the tracking comp
            // (an unpatched modded race) would strand them there with no unload pass to reclaim them.
            if (pawn.GetComp<CompHauledToInventory>() == null)
                return false;
            // A suspended bill — or one whose repeat counter is spent / pause-on-satisfied is holding it
            // (ShouldDoNow false) — is orderable for 0 reps: the job gates every rep on bill.ShouldDoNow(),
            // so it would gather ingredients and craft nothing. Never offer it.
            // No try/catch: these are vanilla bill/recipe predicates — a throw is a real bug to surface, not hide.
            return !bill.suspended && bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(pawn)
                   // Vanilla's WorkGiver_DoBill refuses these unconditionally (forced included):
                   // the RECIPE's skillRequirements — PawnAllowedToStartAnew covers only the
                   // player-set allowedSkillRange, so without this a Cooking-3 pawn could batch
                   // fine meals vanilla forbids — and a recipe claimed by another work type's
                   // giver (psychite tea is Cooking work at a drug lab) needs a pawn capable of
                   // THAT work type.
                   && bill.recipe.PawnSatisfiesSkillRequirements(pawn)
                   && (bill.recipe.requiredGiverWorkType == null
                       || !pawn.WorkTypeIsDisabled(bill.recipe.requiredGiverWorkType));
        }

        /// <summary>Does the bench have at least one bill this pawn can batch?</summary>
        public static bool AnyBatchableBillForPawn(Pawn pawn, Building_WorkTable bench)
        {
            var bills = bench?.BillStack?.Bills;
            if (bills == null)
                return false;
            for (int i = 0; i < bills.Count; i++)
                if (CanPawnBatch(pawn, bills[i]))
                    return true;
            return false;
        }

        /// <summary>
        /// The bill's product count as vanilla's <c>CountProducts</c> sees it — which, via
        /// <see cref="Patch_CountProducts_BankedInventory"/>, now ALSO includes the HD-banked in-flight products of
        /// the counted def that colonists carry in INVENTORY toward the unload pass (vanilla's CountProducts can't
        /// see those on its own). HD's scoop + batch driver bank products in inventory, so without that a
        /// "Do until you have X" bill never observes the target until the products reach storage and pawns
        /// overproduce (across the whole colony). Single-counted-product (TargetCount) recipes only; for others it is
        /// vanilla's count unchanged. NOTE: the banked products are added INSIDE CountProducts now, so this must NOT
        /// add them again here (that would double-count).
        /// </summary>
        public static int EffectiveProductCount(Bill_Production bp)
        {
            // CountProducts requires a live map; all callers pass a spawned-bench bill, but guard defensively.
            var counter = bp?.recipe?.WorkerCounter;
            if (bp?.Map == null || counter == null)
                return 0;
            return counter.CountProducts(bp);
        }

        /// <summary>
        /// HD-banked in-flight products of <paramref name="bp"/>'s single product def: the SURPLUS units (above the
        /// carrier's keep-stock — i.e. genuinely in-flight toward storage, not kept) held in a player pawn's INVENTORY
        /// (NOT the hands — vanilla's <c>GetCarriedCount</c> already counts those) that pass the bill's own per-thing
        /// validity (def + quality / HP / allowed-stuff / taint, via vanilla's <c>CountValidThing</c>). HD's scoop +
        /// batch driver bank freshly-made products in inventory, which vanilla's
        /// <c>CountProducts</c> never counts (it sees world/storage/hands only, unless <c>includeEquipped</c>). The
        /// CountProducts postfix (<see cref="Patch_CountProducts_BankedInventory"/>) adds this so vanilla's OWN
        /// <c>ShouldDoNow</c> / unpause-at hysteresis observes the true colony count and a "Do until you have X" bill
        /// actually pauses. Single-counted-product recipes only (<c>CanCountProducts</c>); 0 otherwise.
        /// </summary>
        public static int BankedInFlightProductCount(Bill_Production bp)
        {
            var map = bp?.Map;
            var counter = bp?.recipe?.WorkerCounter;
            if (map == null || counter == null || !counter.CanCountProducts(bp))
                return 0;
            var def = bp.recipe.products[0].thingDef;
            if (def == null)
                return 0;
            int n = 0;
            foreach (var p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                var tagged = p.GetComp<CompHauledToInventory>()?.PeekHashSet();
                if (tagged == null)
                    continue;
                // INVENTORY only (ParentHolder is Pawn_InventoryTracker) — never the hands (GetCarriedCount handles
                // those) — and only products passing the bill's validity, so a quality/HP/stuff-restricted "do until
                // X" bill counts only the VALID in-flight products, exactly like vanilla counts the stored ones.
                foreach (var t in tagged)
                {
                    if (t == null || t.Destroyed || !(t.ParentHolder is Pawn_InventoryTracker)
                        || !counter.CountValidThing(t, bp, def))
                        continue;
                    // #2 over-suppression fix: count only the units genuinely IN-FLIGHT toward storage — the SURPLUS
                    // above this pawn's keep-stock (InventorySurplus.SurplusOf). A product a pawn KEEPS (food/drugs/
                    // loadout: SurplusOf==0) never reaches storage, so counting it would PERMANENTLY inflate a "Do
                    // until you have X" bill's CountProducts (vanilla counts storage + hands, never kept inventory) —
                    // the count stays >= target, ShouldDoNow is false forever, and the bill is never offered to ANY
                    // pawn (the reported "batch does nothing / defaults to vanilla" on TargetCount bills). Counting the
                    // surplus AMOUNT (not the whole stack) also matches vanilla's intent — only what will actually land
                    // in storage is counted — and self-heals: once the surplus unloads it's counted in storage instead
                    // (no double-count across the transition), and a fully-kept stack contributes 0 (inert).
                    int surplus = InventorySurplus.SurplusOf(p, t);
                    if (surplus > 0)
                        n += surplus;
                }
            }
            return n;
        }

        /// <summary>
        /// Resolve the full plan for <paramref name="requestedReps"/> repetitions with a <paramref name="timeoutTicks"/>
        /// wall-clock cap (0 = none). Never throws; on any problem returns a plan with <see cref="CraftBatchPlan.feasible"/>
        /// false and a <see cref="CraftBatchPlan.blockReason"/>.
        /// </summary>
        public static CraftBatchPlan Resolve(Pawn pawn, Building_WorkTable bench, Bill bill, int requestedReps, int timeoutTicks)
        {
            var plan = new CraftBatchPlan
            {
                bill = bill,
                recipe = bill?.recipe,
                requestedReps = Mathf.Max(0, requestedReps),
                timeoutTicks = Mathf.Max(0, timeoutTicks),
                resolvedReps = 0,
            };

            if (pawn?.Map == null || bench == null || bill == null || !CanBatch(bill))
            {
                plan.blockReason = "HaulersDream.PlanCraft.BlockUnsupported".Translate();
                return plan;
            }

            // The bill's own runtime state gates + caps the batch (vanilla defaults: repeatMode = RepeatCount,
            // repeatCount = 1). The job's per-rep finish calls bill.Notify_IterationCompleted (which decrements
            // repeatCount) and gates every rep on bill.ShouldDoNow() — so planning past the bill's remaining
            // count would pre-load ingredients the job then refuses to craft (gather 10 reps' worth, craft 1,
            // unload 9). A suspended or pause-satisfied (TargetCount) bill is orderable for 0 reps.
            if (bill.suspended || !bill.ShouldDoNow())
            {
                plan.blockReason = "HaulersDream.PlanCraft.BlockBillNotActive".Translate();
                return plan;
            }
            if (bill is Bill_Production bp)
            {
                if (bp.repeatMode == BillRepeatModeDefOf.RepeatCount)
                    plan.billReps = bp.repeatCount;
                // Make-until-X: products banked in the crafter's INVENTORY are invisible to vanilla's
                // CountProducts (it counts storage + hands, never pawn inventory), so cap the plan by the
                // remaining shortfall against the EFFECTIVE count — vanilla's count PLUS the HD-banked
                // in-flight products across the colony — so repeated batches don't re-plan a stale full
                // shortfall and overshoot the target. The per-rep gate enforces the same effective target.
                else if (bp.repeatMode == BillRepeatModeDefOf.TargetCount
                         && !bill.recipe.products.NullOrEmpty() && bill.recipe.products[0].count > 0)
                {
                    int shortfall = bp.targetCount - EffectiveProductCount(bp);
                    plan.billReps = shortfall <= 0
                        ? 0
                        : Mathf.CeilToInt(shortfall / (float)bill.recipe.products[0].count);
                }
            }

            var recipe = bill.recipe;

            // MIXING recipes (cooked meals etc.) take a separate value-based path: the batch can draw each rep's mix
            // from MULTIPLE defs, so there is no single "scarcest def" — the batch is sized by total available VALUE
            // per slot and the driver recomputes the per-def mix per rep. ResolveMixing applies the same mass/timeout/
            // bill caps via FinalizeCaps. (The non-mixing slot loop below freezes one def per slot and is unchanged.)
            if (recipe.allowMixingIngredients)
                return ResolveMixing(plan, pawn, bench, bill, recipe);

            float massPerRep = 0f;

            for (int s = 0; s < recipe.ingredients.Count; s++)
            {
                var ing = recipe.ingredients[s];
                ThingDef bestDef = null;
                int bestReps = -1, bestAvail = 0, bestPerRep = 0;

                foreach (var cand in ing.filter.AllowedThingDefs)
                {
                    if (cand == null)
                        continue;
                    // Respect the bill's player-set allowed-ingredient list — but NOT for FIXED slots,
                    // exactly like vanilla (WorkGiver_DoBill checks !IsFixedIngredient before the bill
                    // filter): fixed slots bypass it, and implicit costList recipes (make medicine,
                    // the whole drug lab) have an EMPTY bill filter by construction — applying it here
                    // made every such bill read "no available ingredient" with full stockpiles.
                    if (!ing.IsFixedIngredient && bill.ingredientFilter != null && !bill.ingredientFilter.Allows(cand))
                        continue;
                    int perRep = ing.CountRequiredOfFor(cand, recipe, bill);
                    if (perRep <= 0)
                        continue;
                    int avail = AvailableUnits(pawn, cand, bill, bench);
                    if (avail < perRep)
                        continue; // can't even make one rep from this def
                    int reps = avail / perRep;
                    // Prefer the def that supports the most reps; tie-break on most absolute stock, then on a STABLE
                    // def key. The final shortHash tiebreak is load-bearing for Multiplayer DETERMINISM: AllowedThingDefs
                    // is backed by a HashSet<ThingDef> (verified against decompiled Verse.ThingFilter) whose iteration
                    // order is identity-hash dependent and can differ per client — so without a total order, two defs
                    // tying on (reps, avail) (two woods/leathers/meats with identical reachable stock and per-rep count)
                    // would let first-in-HashSet-order win and pick a DIFFERENT source def on different clients, which
                    // re-introduces the very desync this synced path fixes (different ingredient consumed → divergent
                    // world state). shortHash is a content-derived, cross-process-stable key (same idiom as
                    // BuildMixForRep's tiebreak), so the chosen def is identical everywhere. Pre-MP this only makes an
                    // arbitrary tie deterministic — single-player behaviour is unchanged for any non-tied case.
                    if (reps > bestReps
                        || (reps == bestReps && avail > bestAvail)
                        || (reps == bestReps && avail == bestAvail && bestDef != null && cand.shortHash < bestDef.shortHash))
                    {
                        bestReps = reps; bestDef = cand; bestAvail = avail; bestPerRep = perRep;
                    }
                }

                if (bestDef == null)
                {
                    plan.blockReason = "HaulersDream.PlanCraft.BlockNoIngredient".Translate(ing.filter.Summary);
                    return plan;
                }

                plan.ingredientDefs.Add(bestDef);
                plan.perRepCounts.Add(bestPerRep);
                massPerRep += bestPerRep * bestDef.GetStatValueAbstract(StatDefOf.Mass);
            }

            // Availability cap = scarcest def, counting TOTAL per-rep demand for that def across ALL slots that
            // source it (two slots both pulling steel draw from the same pool — dividing per-slot would over-promise).
            // Map each distinct def to a key, read its available units ONCE, and let the unit-tested pure helper
            // aggregate per-def demand and take the scarcest-def reps.
            var keyByDef = new Dictionary<ThingDef, int>();
            var availableByKey = new List<int>();
            var defKeys = new List<int>(plan.ingredientDefs.Count);
            for (int i = 0; i < plan.ingredientDefs.Count; i++)
            {
                var def = plan.ingredientDefs[i];
                if (!keyByDef.TryGetValue(def, out int key))
                {
                    key = availableByKey.Count;
                    keyByDef[def] = key;
                    availableByKey.Add(AvailableUnits(pawn, def, bill, bench));
                }
                defKeys.Add(key);
            }

            plan.availabilityReps = CraftBatchMath.ScarcestDefReps(defKeys, plan.perRepCounts, availableByKey);

            // Combat Extended adds a BULK dimension the weight math can't see — without this clamp the dialog
            // would promise reps the (bulk-clamped) gather can't pre-load in one trip. Weight stays the floor.
            // Compute the per-rep bulk from the frozen per-slot defs (no-op cost without CE: the per-def sum is
            // only used inside the CE branch of FinalizeCaps).
            float bulkPerRep = 0f;
            if (CECompat.IsActive)
                for (int i = 0; i < plan.ingredientDefs.Count; i++)
                    bulkPerRep += plan.perRepCounts[i] * CECompat.BulkPerUnitAbstract(plan.ingredientDefs[i]);

            FinalizeCaps(plan, pawn, bench, recipe, massPerRep, bulkPerRep);
            return plan;
        }

        /// <summary>
        /// Apply the mass / timeout / bill caps shared by the NON-mixing and MIXING resolve paths, given the already-
        /// computed <paramref name="massPerRep"/> (kg of one rep's ingredients) and <paramref name="bulkPerRep"/> (CE
        /// bulk of one rep, 0 when CE is inactive or for the mixing path which skips the CE-bulk clamp). The caller has
        /// already set <see cref="CraftBatchPlan.availabilityReps"/> (scarcest-def for NoMix, scarcest-slot-by-value
        /// for mixing). This is verbatim the former inline tail of <see cref="Resolve"/> with the per-def bulk loop
        /// hoisted to the caller — so the NON-mixing path is byte-identical, and the mixing path reuses the exact same
        /// RepsByMass / RepsByTimeout / Resolve calls.
        /// </summary>
        private static void FinalizeCaps(CraftBatchPlan plan, Pawn pawn, Building_WorkTable bench, RecipeDef recipe,
            float massPerRep, float bulkPerRep)
        {
            plan.massPerRepKg = massPerRep;
            plan.ticksPerRep = EstimateTicksPerRep(pawn, bench, recipe);

            var settings = HaulersDreamMod.Settings;
            float maxCap = MassUtility.Capacity(pawn);
            float baseCap = CarryMath.EffectiveCapacity(maxCap, settings?.carryLimitFraction ?? 1f);
            float curMass = MassUtility.GearAndInventoryMass(pawn);
            // Null settings means Off everywhere else (OverloadGate.NoOverload(null) is true) — never fall
            // back to Fair-level overloading here.
            int level = settings != null ? OverloadGate.EffectiveLevel(settings) : OverloadTuning.OffLevel;

            plan.massReps = CraftBatchMath.RepsByMass(level, maxCap, baseCap, curMass, massPerRep, plan.requestedReps);
            // CE bulk clamp (weight stays the floor). bulkPerRep is 0 when CE is inactive or for the mixing path.
            if (CECompat.IsActive && bulkPerRep > 0f)
            {
                int bulkReps = (int)System.Math.Floor(CECompat.AvailableBulk(pawn) / bulkPerRep);
                if (bulkReps < plan.massReps)
                    plan.massReps = bulkReps < 0 ? 0 : bulkReps;
            }
            plan.timeoutReps = CraftBatchMath.RepsByTimeout(plan.ticksPerRep, plan.timeoutTicks);
            // Mass does NOT cap the rep COUNT unless the player chose strict carry weight. Otherwise the pawn
            // OVERLOADS (carries overweight, accepting the speed debuff) and/or makes multiple trips to craft every
            // rep the resources allow — so the count is bounded by AVAILABLE RESOURCES (and the timeout safety),
            // never by "you can't carry it all at once". A heavy ingredient (an 18 kg stone chunk) must never make a
            // well-stocked batch read "not enough resources".
            int massCap = (settings != null && OverloadGate.NoOverload(settings)) ? plan.massReps : int.MaxValue;
            plan.resolvedReps = CraftBatchMath.Resolve(plan.requestedReps, plan.availabilityReps, massCap, plan.timeoutReps);
            if (plan.billReps < plan.resolvedReps)
                plan.resolvedReps = plan.billReps; // never plan more reps than the bill itself will run
            plan.feasible = plan.resolvedReps >= 1;
            if (!plan.feasible && plan.blockReason == null)
                plan.blockReason = "HaulersDream.PlanCraft.BlockNoReps".Translate();
        }

        /// <summary>
        /// Resolve a MIXING recipe's batch (allowMixingIngredients: cooked meals, kibble, pemmican, chemfuel, beer).
        /// Unlike the NoMix path, the plan does NOT freeze one def per slot — <see cref="CraftBatchPlan.ingredientDefs"/>
        /// / <see cref="CraftBatchPlan.perRepCounts"/> stay EMPTY and the driver recomputes each rep's per-def mix from
        /// current stock via <see cref="CraftBatchMath.MixFillSlot"/>. Here we only SIZE the batch: per ingredient slot,
        /// sum the available VALUE across every bill-usable candidate def (units × value-per-unit, mirroring vanilla's
        /// <c>IngredientValueGetter.ValuePerUnitOf</c>) and divide by the slot's per-rep value target
        /// (<c>GetBaseCount</c>) via <see cref="CraftBatchMath.MixAvailableReps"/>; the batch is bounded by the
        /// SCARCEST slot. The same mass/timeout/bill caps are applied via <see cref="FinalizeCaps"/> (the CE-bulk clamp
        /// is skipped for the mixing path — the per-def mix isn't known until craft time — so weight is the only
        /// carry cap, exactly as for a heavy NoMix ingredient).
        /// </summary>
        private static CraftBatchPlan ResolveMixing(CraftBatchPlan plan, Pawn pawn, Building_WorkTable bench, Bill bill, RecipeDef recipe)
        {
            plan.mixingRecipe = true; // leaves ingredientDefs/perRepCounts EMPTY — the driver fills them per rep

            int availabilityReps = int.MaxValue; // min over slots; int.MaxValue = no constraint (no positive-value slot)
            float massPerRep = 0f;
            var valueGetter = recipe.IngredientValueGetter;

            for (int s = 0; s < recipe.ingredients.Count; s++)
            {
                var ing = recipe.ingredients[s];
                // The slot's per-rep VALUE target (nutrition for food; count for a count-based value getter). Vanilla's
                // AllowMix fills exactly this much value per slot per craft (its `num2 = ingredientCount.GetBaseCount()`).
                double perRepValue = ing.GetBaseCount();

                double totalSlotValue = 0.0;     // Σ over candidate defs of availableUnits × value-per-unit
                float bestMassPerValue = 0f;      // max over candidate defs of (mass / value-per-unit) — conservative
                foreach (var cand in ing.filter.AllowedThingDefs)
                {
                    if (cand == null)
                        continue;
                    // Same bill-filter rule as the NoMix loop + vanilla's AllowMix inner test: fixed slots bypass the
                    // player's allowed-ingredient list; implicit-costList recipes have an empty bill filter by design.
                    if (!ing.IsFixedIngredient && bill.ingredientFilter != null && !bill.ingredientFilter.Allows(cand))
                        continue;
                    float vpu = valueGetter.ValuePerUnitOf(cand);
                    if (vpu <= 0f)
                        continue; // a valueless def can never satisfy the slot (and would /0 below)
                    int avail = AvailableUnits(pawn, cand, bill, bench);
                    if (avail <= 0)
                        continue;
                    totalSlotValue += avail * (double)vpu;
                    // Mass estimate: one rep needs perRepValue worth of value from SOME mix of these defs; the heaviest
                    // way to supply a unit of value is the def with the largest mass-per-value, so use that as a safe
                    // OVER-estimate (an over-estimate only ever lowers the mass cap, which is safe — never promises a
                    // batch the pawn can't carry). vpu > 0 here, so the divide is guarded.
                    float massPerValue = cand.GetStatValueAbstract(StatDefOf.Mass) / vpu;
                    if (massPerValue > bestMassPerValue)
                        bestMassPerValue = massPerValue;
                }

                int slotReps = CraftBatchMath.MixAvailableReps(perRepValue, totalSlotValue);
                if (slotReps <= 0)
                {
                    // No bill-usable def has enough VALUE to make even one rep of this slot → infeasible, exactly like
                    // the NoMix "no available ingredient" case (same translation key + the slot's filter summary).
                    plan.blockReason = "HaulersDream.PlanCraft.BlockNoIngredient".Translate(ing.filter.Summary);
                    return plan;
                }
                if (slotReps < availabilityReps)
                    availabilityReps = slotReps;
                if (perRepValue > 0.0)
                    massPerRep += (float)(perRepValue) * bestMassPerValue;
            }

            plan.availabilityReps = availabilityReps;
            // Mixing skips the CE-bulk clamp (bulkPerRep 0): the per-def mix isn't known until craft time, so weight is
            // the only carry cap here (consistent with how a heavy NoMix ingredient is handled — overload, don't block).
            FinalizeCaps(plan, pawn, bench, recipe, massPerRep, bulkPerRep: 0f);
            return plan;
        }

        /// <summary>How many reps the scarcest ingredient and the bill's own repeat count allow (ignores
        /// mass/timeout) — for the slider's max.</summary>
        public static int MaxAvailableReps(Pawn pawn, Building_WorkTable bench, Bill bill)
        {
            // A cheap pass: resolve with a huge requested count and no timeout, read the resource + bill caps.
            var p = Resolve(pawn, bench, bill, 9999, 0);
            int cap = Mathf.Min(p.availabilityReps, p.billReps);
            if (cap == int.MaxValue) return 9999; // no limit (shouldn't happen — CanBatch requires ingredients)
            return Mathf.Max(0, cap);
        }

        /// <summary>Units of <paramref name="def"/> the batch can ACTUALLY consume: non-forbidden, reservable,
        /// bill-usable floor stacks (thing-level filters — rot stage, hit points — and the bill's ingredient
        /// search radius, exactly like vanilla's ingredient scan) plus the worker's OWN carried stock (already at
        /// the bench, used before fetching). Reach is NOT pre-checked (a per-stack path test is too costly for a
        /// live dialog preview; the loader checks it, and the batch degrades to fewer reps when something is
        /// unreachable). Deliberately EXCLUDES other pawns' inventories — the pre-loader scans only floor stacks,
        /// so counting colleagues' carried stock would over-promise reps the batch can't reach.</summary>
        public static int AvailableUnits(Pawn pawn, ThingDef def, Bill bill, Building_WorkTable bench)
        {
            var map = pawn?.Map;
            if (map == null || def == null)
                return 0;
            // 999 = vanilla's "unlimited" sentinel; its square comfortably exceeds any map distance, so
            // applying the bound unconditionally mirrors vanilla's validator exactly.
            float radiusSq = bill != null ? bill.ingredientSearchRadius * bill.ingredientSearchRadius : float.MaxValue;
            IntVec3 root = bench?.Position ?? pawn.Position;
            int total = 0;
            var things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t == null || !t.Spawned || t.IsForbidden(pawn))
                    continue;
                // Thing-level bill constraints (rot stage, hit-point range, the bill's own filter) — the
                // def-level pass above can't see these; without this the plan would count rotten meat a
                // cooking bill forbids.
                if (bill != null && !InventoryShare.IsUsableForBill(t, bill))
                    continue;
                if ((t.Position - root).LengthHorizontalSquared >= radiusSq)
                    continue;
                if (!pawn.CanReserve(t))
                    continue;
                total += t.stackCount;
            }
            // The worker's own inventory of this def is consumed directly at the bench (NeededUnits subtracts it,
            // so the loader fetches less) — count it so a pawn already holding ingredients isn't under-counted.
            // Same thing-level vetting: personal stock the bill forbids must not inflate the preview.
            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
                for (int i = 0; i < inv.Count; i++)
                    if (inv[i]?.def == def && (bill == null || InventoryShare.IsUsableForBill(inv[i], bill)))
                        total += inv[i].stackCount;
            return total;
        }

        /// <summary>Per-rep crafting time in ticks, adjusted by the pawn's work-speed stat and the bench's
        /// work-table-speed stat — mirrors how <c>Toils_Recipe.DoRecipeWork</c> burns down workLeft.</summary>
        public static int EstimateTicksPerRep(Pawn pawn, Building_WorkTable bench, RecipeDef recipe)
        {
            float work = recipe.WorkAmountTotal((Thing)null);
            float speed = (recipe.workSpeedStat != null) ? Mathf.Max(0.01f, pawn.GetStatValue(recipe.workSpeedStat)) : 1f;
            if (recipe.workTableSpeedStat != null && bench != null)
                speed *= Mathf.Max(0.01f, bench.GetStatValue(recipe.workTableSpeedStat));
            return Mathf.Max(1, Mathf.RoundToInt(work / Mathf.Max(0.0001f, speed)));
        }
    }
}
