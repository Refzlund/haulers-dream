using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure math for the "batch a crafting bill" feature: how many repetitions of one bill a pawn can commit to
    /// in a single prioritized batch, capped by three independent limits:
    /// <list type="number">
    /// <item><b>Ingredient availability</b> — you can't run a recipe more times than you have ingredients for
    /// (per ingredient: <c>floor(available / perRep)</c>, and the batch is bounded by the SCARCEST ingredient).</item>
    /// <item><b>Inventory mass</b> — the pawn pre-loads ALL the batch's ingredients in one trip, so the batch can't
    /// exceed what fits under the (smart-overload) carry ceiling (one "unit" here = one rep's worth of ingredients,
    /// fed through <see cref="OverloadPolicy"/> so the carry economics match the rest of the mod).</item>
    /// <item><b>Timeout</b> — a wall-clock cap so a long recipe can't trap the pawn in a multi-day loop
    /// (<c>floor(timeoutTicks / ticksPerRep)</c>).</item>
    /// </list>
    /// All functions are coordinate/Verse-free → unit-testable headlessly. The RimWorld layer reads the recipe's
    /// ingredient counts/masses + work amount and feeds them in.
    /// </summary>
    public static class CraftBatchMath
    {
        /// <summary>
        /// Reps a single ingredient supports: <c>floor(availableUnits / perRepUnits)</c>. A recipe that needs none
        /// of this ingredient (<paramref name="perRepUnits"/> &lt;= 0) imposes no limit (returns <c>int.MaxValue</c>).
        /// </summary>
        public static int RepsByAvailability(int perRepUnits, int availableUnits)
        {
            if (perRepUnits <= 0) return int.MaxValue;
            if (availableUnits <= 0) return 0;
            return availableUnits / perRepUnits;
        }

        /// <summary>
        /// Reps whose TOTAL ingredient mass fits in the pawn's inventory on one pre-load trip. Treats one rep's
        /// worth of ingredients as a single "unit" of mass <paramref name="massPerRepKg"/> and runs it through
        /// <see cref="OverloadPolicy.UnitsToCarry"/> (so a big batch overloads exactly like a big haul does).
        /// Massless ingredients (<paramref name="massPerRepKg"/> &lt;= 0) impose no mass limit.
        /// </summary>
        public static int RepsByMass(int overloadLevel, float maxCapacityKg, float baseCapKg, float currentMassKg,
            float massPerRepKg, int wantReps)
        {
            if (wantReps <= 0) return 0;
            if (massPerRepKg <= 0f) return wantReps; // massless ingredients → mass never limits the batch
            return OverloadPolicy.UnitsToCarry(overloadLevel, maxCapacityKg, baseCapKg, currentMassKg,
                massPerRepKg, demandUnits: wantReps, availableUnits: wantReps);
        }

        /// <summary>
        /// Reps that finish before the timeout: <c>floor(timeoutTicks / ticksPerRep)</c>, at least 1. A non-positive
        /// timeout means "no time limit" (<c>int.MaxValue</c>); a non-positive per-rep work also means no limit.
        /// </summary>
        public static int RepsByTimeout(int ticksPerRep, int timeoutTicks)
        {
            if (timeoutTicks <= 0) return int.MaxValue; // 0 / negative = no timeout
            if (ticksPerRep <= 0) return int.MaxValue;
            return Math.Max(1, timeoutTicks / ticksPerRep);
        }

        /// <summary>The smaller of two caps, treating either as "no cap" when it is <c>int.MaxValue</c>.</summary>
        public static int Min(int a, int b) => a < b ? a : b;

        /// <summary>
        /// Units to carry into the hands THIS pass when setting a recipe slot's ingredients down on the bench, given
        /// how many of that slot still need placing (<paramref name="slotRemaining"/>), how much is already in the
        /// hands (<paramref name="alreadyCarried"/>, same def), and the carry-tracker's free stack space for that def
        /// (<paramref name="availableStackSpace"/>). A single slot can need more than one handful (its per-rep count
        /// can exceed the carry/stack ceiling), so the slot is placed across multiple passes; each pass tops the hands
        /// up to <c>min(slotRemaining, alreadyCarried + availableStackSpace)</c> and the caller places it, repeating
        /// until the slot is exhausted. Never negative; 0 means "nothing to add this pass" (hands already hold the
        /// whole slot, or there is no free stack space). Pure: no Verse types, so it is unit-testable headlessly.
        /// </summary>
        public static int CarryPassTarget(int slotRemaining, int alreadyCarried, int availableStackSpace)
        {
            if (slotRemaining <= 0)
                return 0;
            if (alreadyCarried < 0) alreadyCarried = 0;
            if (availableStackSpace < 0) availableStackSpace = 0;
            int target = Min(slotRemaining, alreadyCarried + availableStackSpace);
            return target < 0 ? 0 : target;
        }

        /// <summary>
        /// Reps the SCARCEST ingredient allows, correctly handling a def that sources MORE THAN ONE recipe slot:
        /// demand for the same def must be SUMMED across slots before dividing into the shared stock pool (two slots
        /// each needing 10 steel = 20/rep against one steel pool, not 10/rep twice). <paramref name="defKeys"/>[i]
        /// is the distinct-def index of slot i (into <paramref name="availableByKey"/>), <paramref name="perRep"/>[i]
        /// is that slot's per-rep unit count. Returns <c>int.MaxValue</c> when there is no constraint (no slots).
        /// </summary>
        public static int ScarcestDefReps(System.Collections.Generic.IReadOnlyList<int> defKeys,
            System.Collections.Generic.IReadOnlyList<int> perRep,
            System.Collections.Generic.IReadOnlyList<int> availableByKey)
        {
            if (defKeys == null || perRep == null || availableByKey == null || availableByKey.Count == 0)
                return int.MaxValue;
            var demand = new int[availableByKey.Count];
            for (int i = 0; i < defKeys.Count; i++)
            {
                int k = defKeys[i];
                if (k < 0 || k >= demand.Length)
                    continue;
                int p = i < perRep.Count ? perRep[i] : 0;
                if (p > 0)
                    demand[k] += p;
            }
            int reps = int.MaxValue;
            for (int k = 0; k < demand.Length; k++)
            {
                if (demand[k] <= 0)
                    continue; // a def with no positive demand imposes no limit
                int r = availableByKey[k] / demand[k];
                if (r < reps)
                    reps = r;
            }
            return reps;
        }

        /// <summary>
        /// Final batch size = the smallest of (player-requested, availability-capped, mass-capped, timeout-capped),
        /// floored at 0. Used by the dialog to clamp the repeat slider and by the job to size the pre-load.
        /// </summary>
        public static int Resolve(int requested, int byAvailability, int byMass, int byTimeout)
        {
            int r = Min(Min(requested, byAvailability), Min(byMass, byTimeout));
            return r < 0 ? 0 : r;
        }

        // ---- MIXING-recipe math (allowMixingIngredients == true: meals, kibble, pemmican, chemfuel, beer) ----
        //
        // A mixing recipe fills ONE ingredient slot from MULTIPLE candidate defs by VALUE (nutrition for food,
        // "value-per-unit" generally), greedily, PER REP from CURRENT stock — vanilla's
        // WorkGiver_DoBill.TryFindBestBillIngredientsInSet_AllowMix walks the value-sorted candidates and, for each,
        // takes Min(CeilToInt(remainingValue / valuePerUnit), stackCount) until the slot's GetBaseCount value target
        // is met. The NoMix batch plan freezes one def per slot for all reps; a MIXING slot cannot be frozen because
        // each rep draws a fresh mix from whatever stock remains. So the batch driver RECOMPUTES the mix per rep via
        // MixFill (below) and the planner sizes the batch by VALUE via MixAvailableReps. Both are pure / Verse-free;
        // the RimWorld layer reads each candidate def's value-per-unit + available unit count and feeds them in.

        /// <summary>
        /// Greedily fill a single mixing ingredient slot to <paramref name="target"/> total VALUE from the supplied
        /// candidate defs, mirroring vanilla's AllowMix per-slot fill EXACTLY: walk the candidates IN THE GIVEN ORDER
        /// (the caller pre-sorts them — value-per-unit ascending like vanilla, or spoiling-first), and for each take
        /// <c>Min(availableUnits, Ceil(remaining / valuePerUnit))</c> units, decrementing the remaining value by the
        /// units taken × their value-per-unit, stopping once the target is met. A candidate with value-per-unit
        /// &lt;= 0 or zero available units contributes nothing (skipped). Pure — no Verse types, unit-testable.
        ///
        /// The <c>- EPS</c> inside the ceiling guards float noise: an EXACT multiple (target 1.0, value-per-unit 0.5)
        /// yields exactly 2 units, never an over-rounded 3. EPS == 1e-4 matches vanilla's <c>0.0001f</c> threshold.
        ///
        /// NOTE (per-def, not per-stack): vanilla rounds the ceiling PER THING (stack), so a def split across several
        /// floor stacks can round up once per stack; this aggregates a def's units into ONE candidate bucket and
        /// rounds once per def. That is the intended shape for the batch driver — it consumes per def, so a single
        /// ceil per def is the faithful (and slightly tighter) equivalent. The result <see cref="MixFill.filled"/>
        /// flag lets the caller refuse a rep it cannot complete (vanilla's "missingIngredients" / return-false case).
        /// </summary>
        public static MixFill MixFillSlot(double target,
            System.Collections.Generic.IReadOnlyList<double> valuePerUnit,
            System.Collections.Generic.IReadOnlyList<int> availableUnits)
        {
            int n = valuePerUnit?.Count ?? 0;
            var counts = new int[n];
            const double EPS = 1e-4;
            double remaining = target;
            // A non-positive value target is trivially satisfied (take nothing) — matches vanilla's `num2 <= 0.0001f`
            // short-circuit before the first candidate.
            if (target <= EPS)
                return new MixFill { counts = counts, filled = true, remaining = 0.0 };
            for (int i = 0; i < n; i++)
            {
                if (remaining <= EPS)
                    break;
                double vpu = valuePerUnit[i];
                if (vpu <= 0.0)
                    continue; // a valueless candidate can never reduce the remaining value → skip (avoids /0)
                int avail = (availableUnits != null && i < availableUnits.Count) ? availableUnits[i] : 0;
                if (avail <= 0)
                    continue;
                // Take just enough to cover the remaining value (Ceil), but never more than is available. The `- EPS`
                // keeps an exact multiple from rounding up an extra unit (Ceil(1.0/0.5 - eps) == 2, not 3).
                int take = Min(avail, (int)System.Math.Ceiling((remaining / vpu) - EPS));
                if (take < 0)
                    take = 0;
                counts[i] = take;
                remaining -= take * vpu;
            }
            return new MixFill { counts = counts, filled = remaining <= EPS, remaining = remaining };
        }

        /// <summary>Alias type name kept short for call sites: the result of <see cref="MixFillSlot"/>.</summary>
        public struct MixFill
        {
            public int[] counts;
            public bool filled;
            public double remaining;
        }

        /// <summary>
        /// Reps a single mixing ingredient slot supports by VALUE: <c>floor(totalAvailableValue / perRepValue)</c>
        /// (the slot consumes <paramref name="perRepValue"/> total value per rep, drawn from a shared pool of
        /// <paramref name="totalAvailableValue"/> across all its candidate defs). A slot needing no value
        /// (<paramref name="perRepValue"/> &lt;= 0) imposes no limit (<c>int.MaxValue</c>); the batch is bounded by
        /// the SCARCEST slot. Floored at 0.
        /// </summary>
        public static int MixAvailableReps(double perRepValue, double totalAvailableValue)
        {
            if (perRepValue <= 0.0)
                return int.MaxValue;
            if (totalAvailableValue <= 0.0)
                return 0;
            int r = (int)System.Math.Floor(totalAvailableValue / perRepValue);
            return r < 0 ? 0 : r;
        }
    }
}
