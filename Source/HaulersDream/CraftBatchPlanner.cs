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
            var r = bill.recipe;
            if (r.UsesUnfinishedThing)
                return false;
            if (r.ingredients == null || r.ingredients.Count == 0)
                return false; // a no-ingredient recipe has nothing to pre-load → no benefit, and odd to "batch"
            // "Take entire stacks" recipes (smelt/burn) consume the WHOLE matched stack per run and scale their
            // special products by it — our fixed per-rep count model would under-consume and under-produce. Exclude.
            if (r.ignoreIngredientCountTakeEntireStacks)
                return false;
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
            try { return !bill.suspended && bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(pawn); }
            catch { return false; }
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
            if (bill is Bill_Production bp && bp.repeatMode == BillRepeatModeDefOf.RepeatCount)
                plan.billReps = bp.repeatCount;

            var recipe = bill.recipe;
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
                    // Respect the bill's player-set allowed-ingredient list, exactly like vanilla IsUsableIngredient.
                    if (bill.ingredientFilter != null && !bill.ingredientFilter.Allows(cand))
                        continue;
                    int perRep = ing.CountRequiredOfFor(cand, recipe, bill);
                    if (perRep <= 0)
                        continue;
                    int avail = AvailableUnits(pawn, cand);
                    if (avail < perRep)
                        continue; // can't even make one rep from this def
                    int reps = avail / perRep;
                    // Prefer the def that supports the most reps; tie-break on most absolute stock.
                    if (reps > bestReps || (reps == bestReps && avail > bestAvail))
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
                    availableByKey.Add(AvailableUnits(pawn, def));
                }
                defKeys.Add(key);
            }

            plan.availabilityReps = CraftBatchMath.ScarcestDefReps(defKeys, plan.perRepCounts, availableByKey);
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
            // Combat Extended adds a BULK dimension the weight math can't see — without this clamp the dialog
            // would promise reps the (bulk-clamped) gather can't pre-load in one trip. Weight stays the floor.
            if (CECompat.IsActive)
            {
                float bulkPerRep = 0f;
                for (int i = 0; i < plan.ingredientDefs.Count; i++)
                    bulkPerRep += plan.perRepCounts[i] * CECompat.BulkPerUnitAbstract(plan.ingredientDefs[i]);
                if (bulkPerRep > 0f)
                {
                    int bulkReps = (int)System.Math.Floor(CECompat.AvailableBulk(pawn) / bulkPerRep);
                    if (bulkReps < plan.massReps)
                        plan.massReps = bulkReps < 0 ? 0 : bulkReps;
                }
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

        /// <summary>Units of <paramref name="def"/> the batch can ACTUALLY consume: reachable, non-forbidden,
        /// reservable floor stacks (what the pre-load picks up) plus the worker's OWN carried stock (already at the
        /// bench, used before fetching). Deliberately EXCLUDES other pawns' inventories — the pre-loader scans only
        /// floor stacks, so counting colleagues' carried stock would over-promise reps the batch can't reach.</summary>
        public static int AvailableUnits(Pawn pawn, ThingDef def)
        {
            var map = pawn?.Map;
            if (map == null || def == null)
                return 0;
            int total = 0;
            var things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t == null || !t.Spawned || t.IsForbidden(pawn))
                    continue;
                if (!pawn.CanReserve(t))
                    continue;
                total += t.stackCount;
            }
            // The worker's own inventory of this def is consumed directly at the bench (NeededUnits subtracts it,
            // so the loader fetches less) — count it so a pawn already holding ingredients isn't under-counted.
            var inv = pawn.inventory?.innerContainer;
            if (inv != null)
                for (int i = 0; i < inv.Count; i++)
                    if (inv[i]?.def == def)
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
