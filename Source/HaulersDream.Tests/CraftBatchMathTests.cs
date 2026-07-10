using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CraftBatchMathTests
    {
        // Reference pawn: 35 kg capacity, carrying 5 kg of gear.
        const float Cap = 35f;
        const float Base = 35f;
        const float Gear = 5f;

        [Test]
        public void RepsByAvailability_FloorsDivision()
        {
            Assert.That(CraftBatchMath.RepsByAvailability(perRepUnits: 10, availableUnits: 35), Is.EqualTo(3)); // 3×10=30 ≤ 35
            Assert.That(CraftBatchMath.RepsByAvailability(10, 30), Is.EqualTo(3));
            Assert.That(CraftBatchMath.RepsByAvailability(10, 9), Is.EqualTo(0));  // not even one rep
        }

        [Test]
        public void RepsByAvailability_NoIngredientNeed_IsUnbounded()
        {
            Assert.That(CraftBatchMath.RepsByAvailability(perRepUnits: 0, availableUnits: 5), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByAvailability(-3, 5), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RepsByMass_MasslessIngredients_NeverLimit()
        {
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 0f, wantReps: 7),
                Is.EqualTo(7));
        }

        [Test]
        public void RepsByMass_OffLevel_FitsUnderBaseCap()
        {
            // room = 35 - 5 = 30 kg; 10 kg per rep → 3 reps fit (no overload at OffLevel).
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 10f, wantReps: 10),
                Is.EqualTo(3));
        }

        [Test]
        public void RepsByMass_NeverExceedsWanted()
        {
            // plenty of room (light ingredients) but only 2 reps wanted → 2.
            Assert.That(CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 0.5f, wantReps: 2),
                Is.EqualTo(2));
        }

        [Test]
        public void RepsByMass_Overload_CarriesMoreThanBaseCap()
        {
            // At a generous overload level, room extends past 100% capacity, so more reps fit than the base cap allows.
            int off = CraftBatchMath.RepsByMass(OverloadTuning.OffLevel, Cap, Base, Gear, massPerRepKg: 5f, wantReps: 100);
            int on = CraftBatchMath.RepsByMass(OverloadTuning.MaxLevel, Cap, Base, Gear, massPerRepKg: 5f, wantReps: 100);
            Assert.That(off, Is.EqualTo(6));                 // (35-5)/5 = 6
            Assert.That(on, Is.GreaterThanOrEqualTo(off));   // overload never carries fewer
        }

        [Test]
        public void RepsByTimeout_NoTimeout_IsUnbounded()
        {
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 500, timeoutTicks: 0), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByTimeout(500, -1), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 0, timeoutTicks: 60000), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RepsByTimeout_FloorsButKeepsAtLeastOne()
        {
            // 2-hour timeout (1 hour = 2500 ticks → 5000), 1200-tick recipe → 4 reps.
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 1200, timeoutTicks: 5000), Is.EqualTo(4));
            // recipe longer than the whole timeout → still allow 1 (don't make the batch impossible).
            Assert.That(CraftBatchMath.RepsByTimeout(ticksPerRep: 9000, timeoutTicks: 5000), Is.EqualTo(1));
        }

        [Test]
        public void Resolve_IsTheSmallestCap_FlooredAtZero()
        {
            Assert.That(CraftBatchMath.Resolve(requested: 10, byAvailability: 3, byMass: 7, byTimeout: int.MaxValue),
                Is.EqualTo(3));
            Assert.That(CraftBatchMath.Resolve(10, int.MaxValue, int.MaxValue, int.MaxValue), Is.EqualTo(10));
            Assert.That(CraftBatchMath.Resolve(10, 0, 5, 5), Is.EqualTo(0)); // an ingredient with none → batch impossible
            Assert.That(CraftBatchMath.Resolve(-2, 5, 5, 5), Is.EqualTo(0)); // negative request floored
        }

        [Test]
        public void ScarcestDefReps_SingleDefPerSlot_MatchesPerSlotFloor()
        {
            // slot0: def 0 needs 10, 100 available → 10 reps.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0 }, new[] { 10 }, new[] { 100 }), Is.EqualTo(10));
            // two distinct defs → min of each slot's floor.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 1 }, new[] { 10, 5 }, new[] { 100, 40 }), Is.EqualTo(8));   // min(10, 8)
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 1 }, new[] { 10, 5 }, new[] { 100, 12 }), Is.EqualTo(2));   // min(10, 2)
        }

        [Test]
        public void ScarcestDefReps_SharedDefAcrossSlots_SumsDemand()
        {
            // Two slots BOTH source def 0 (10 each = 20/rep) against one pool of 100 → 5 reps, NOT 10.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 0 }, new[] { 10, 10 }, new[] { 100 }), Is.EqualTo(5));
            // Three slots on def 0 (5+5+5=15/rep) + one on def 1 (10/rep); pools 90 & 100 → min(6, 10) = 6.
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0, 0, 0, 1 }, new[] { 5, 5, 5, 10 }, new[] { 90, 100 }), Is.EqualTo(6));
        }

        [Test]
        public void ScarcestDefReps_Empty_IsUnbounded()
        {
            Assert.That(CraftBatchMath.ScarcestDefReps(new int[0], new int[0], new int[0]), Is.EqualTo(int.MaxValue));
            // A def with zero demand imposes no limit (skipped).
            Assert.That(CraftBatchMath.ScarcestDefReps(new[] { 0 }, new[] { 0 }, new[] { 50 }), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void CarryPassTarget_FitsWholeSlotInOnePass()
        {
            // Slot needs 10 of a def, hands empty, plenty of stack space (75) → carry the whole 10 this pass.
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 10, alreadyCarried: 0, availableStackSpace: 75),
                Is.EqualTo(10));
        }

        [Test]
        public void CarryPassTarget_SlotExceedsCeiling_FillsToCeilingAcrossPasses()
        {
            // Slot needs 100 but only 75 fits → first pass carries 75; after placing those 75, the slot has 25 left,
            // hands empty again, space 75 → second pass carries the remaining 25. The passes sum to the slot count.
            int pass1 = CraftBatchMath.CarryPassTarget(slotRemaining: 100, alreadyCarried: 0, availableStackSpace: 75);
            Assert.That(pass1, Is.EqualTo(75));
            int pass2 = CraftBatchMath.CarryPassTarget(slotRemaining: 100 - pass1, alreadyCarried: 0, availableStackSpace: 75);
            Assert.That(pass2, Is.EqualTo(25));
            Assert.That(pass1 + pass2, Is.EqualTo(100)); // never over- or under-carries the slot
        }

        [Test]
        public void CarryPassTarget_TopsUpPartiallyCarriedHands()
        {
            // Hands already hold 6 of the def, slot needs 10, space for 30 more → target 10 (top up by 4).
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 10, alreadyCarried: 6, availableStackSpace: 30),
                Is.EqualTo(10));
            // Hands already hold the whole slot → nothing to add (but target equals what's held, so remaining is 0).
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 10, alreadyCarried: 10, availableStackSpace: 30),
                Is.EqualTo(10));
        }

        [Test]
        public void CarryPassTarget_NoSpace_CannotCarryMoreThanAlreadyHeld()
        {
            // No free stack space and empty hands → 0 (the slot can't progress this pass; caller will not loop).
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 10, alreadyCarried: 0, availableStackSpace: 0),
                Is.EqualTo(0));
        }

        [Test]
        public void CarryPassTarget_NothingRemaining_IsZero()
        {
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 0, alreadyCarried: 0, availableStackSpace: 75),
                Is.EqualTo(0));
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: -5, alreadyCarried: 0, availableStackSpace: 75),
                Is.EqualTo(0));
        }

        [Test]
        public void CarryPassTarget_NegativeInputs_ClampToZero()
        {
            // Defensive: negative already/space are treated as 0, never producing a negative target.
            Assert.That(CraftBatchMath.CarryPassTarget(slotRemaining: 10, alreadyCarried: -3, availableStackSpace: -7),
                Is.EqualTo(0));
        }

        [Test]
        public void Resolve_TheUserScenario_CookFourSimpleMeals_TimesThree()
        {
            // "Cook simple meal" needs 10 raw food/rep. Player wants 12 reps (4 meals × 3 batches = 120 raw food).
            // Stockpile has 200 raw food; raw food ~0.5 kg; a strong pawn (75 kg cap). Timeout 2 in-game hours.
            const int perRep = 10;
            int byAvail = CraftBatchMath.RepsByAvailability(perRep, availableUnits: 200);            // 20
            int byMass = CraftBatchMath.RepsByMass(OverloadTuning.MaxLevel, 75f, 75f, 5f,
                massPerRepKg: perRep * 0.5f, wantReps: 12);                                          // 12 (5 kg/rep fits)
            int byTimeout = CraftBatchMath.RepsByTimeout(ticksPerRep: 1300, timeoutTicks: 5000);    // 3
            Assert.That(CraftBatchMath.Resolve(12, byAvail, byMass, byTimeout), Is.EqualTo(3));      // timeout is the binding cap
        }

        // ---- WholeRoundsThatFit (per-trip round gather bound for the no-overload / Combat Extended batch loop) ----

        [Test]
        public void WholeRoundsThatFit_WeightBinds()
        {
            // 30 kg room, 10 kg per round -> 3 rounds. Bulk unconstrained (per-round bulk 0); reps not the limit.
            Assert.That(CraftBatchMath.WholeRoundsThatFit(freeWeightKg: 30f, massPerRoundKg: 10f,
                freeBulk: float.PositiveInfinity, bulkPerRound: 0f, remainingReps: 100), Is.EqualTo(3));
        }

        [Test]
        public void WholeRoundsThatFit_BulkBinds()
        {
            // Weight unconstrained (massless round); 5 bulk room, 2 bulk per round -> floor(5/2) = 2 rounds.
            Assert.That(CraftBatchMath.WholeRoundsThatFit(freeWeightKg: 1000f, massPerRoundKg: 0f,
                freeBulk: 5f, bulkPerRound: 2f, remainingReps: 100), Is.EqualTo(2));
        }

        [Test]
        public void WholeRoundsThatFit_RemainingBinds()
        {
            // Weight + bulk both allow many, but only 4 reps remain to craft -> 4.
            Assert.That(CraftBatchMath.WholeRoundsThatFit(100f, 1f, 100f, 1f, remainingReps: 4), Is.EqualTo(4));
        }

        [Test]
        public void WholeRoundsThatFit_ZeroCostDimensions_DoNotConstrain_NoDivideByZero()
        {
            // Both per-round costs 0 (a massless AND bulkless round, or CE absent) -> only remaining bounds it (no /0).
            Assert.That(CraftBatchMath.WholeRoundsThatFit(0f, 0f, 0f, 0f, remainingReps: 7), Is.EqualTo(7));
            // Zero weight cost but a real bulk cost -> bulk still binds (dimensions are independent).
            Assert.That(CraftBatchMath.WholeRoundsThatFit(0f, 0f, 9f, 3f, remainingReps: 7), Is.EqualTo(3));
        }

        [Test]
        public void WholeRoundsThatFit_OneRoundExceedsBudget_IsZero()
        {
            // One round's weight exceeds the room -> 0 (the caller cedes to vanilla one-at-a-time crafting).
            Assert.That(CraftBatchMath.WholeRoundsThatFit(5f, 10f, float.PositiveInfinity, 0f, 100), Is.EqualTo(0));
            // One round's bulk exceeds the room -> 0, even with ample weight room.
            Assert.That(CraftBatchMath.WholeRoundsThatFit(1000f, 1f, 1f, 3f, 100), Is.EqualTo(0));
        }

        [Test]
        public void WholeRoundsThatFit_NoRemaining_IsZero()
        {
            Assert.That(CraftBatchMath.WholeRoundsThatFit(100f, 1f, 100f, 1f, remainingReps: 0), Is.EqualTo(0));
            Assert.That(CraftBatchMath.WholeRoundsThatFit(100f, 1f, 100f, 1f, remainingReps: -3), Is.EqualTo(0));
        }

        [Test]
        public void WholeRoundsThatFit_NegativeFreeWeight_IsZero_ProductsFilledCapacity()
        {
            // Over capacity (banked products eating weight) -> negative room -> 0 rounds fit. The driver then drops
            // the products to reopen capacity and recomputes; the pure math just reports "none fit right now".
            Assert.That(CraftBatchMath.WholeRoundsThatFit(-4f, 10f, float.PositiveInfinity, 0f, 50), Is.EqualTo(0));
        }

        [Test]
        public void WholeRoundsThatFit_CombatExtendedAbsent_BulkNeverBinds()
        {
            // CE absent: caller passes +inf bulk room and 0 per-round bulk -> bulk imposes no cap, weight decides.
            Assert.That(CraftBatchMath.WholeRoundsThatFit(20f, 4f, float.PositiveInfinity, 0f, 100), Is.EqualTo(5));
        }

        [Test]
        public void WholeRoundsThatFit_MultiSlotRound_AggregatesEverySlotCost()
        {
            // A multi-slot round's mass/bulk is the SUM over slots (e.g. 6 kg steel + 4 kg cloth = 10 kg per round);
            // R is computed against that aggregate, so every slot is gathered in lockstep. 25 kg room / 10 = 2 rounds.
            float massPerRound = 6f + 4f;
            float bulkPerRound = 0.3f + 0.5f; // both slots contribute bulk under CE
            Assert.That(CraftBatchMath.WholeRoundsThatFit(25f, massPerRound, 100f, bulkPerRound, 100), Is.EqualTo(2));
        }

        // ---- Mixing-recipe math (allowMixingIngredients: cooked meals, kibble, pemmican, chemfuel, beer) ----

        /// <summary>An INDEPENDENT, different-looking reimplementation of the greedy per-slot value-fill — used to
        /// oracle-check <see cref="CraftBatchMath.MixFillSlot"/>. Walks the candidates in order, takes the integer
        /// number of units needed to cover the remaining value (rounding UP, but never an exact-multiple extra),
        /// clamped to availability, and stops once the value target is (numerically) met. Returns the counts and
        /// reports <paramref name="filled"/> via an out-param (no value tuples — matches the codebase's style).</summary>
        private static int[] MixFillOracle(double target, double[] vpu, int[] avail, out bool filled)
        {
            const double eps = 1e-4;
            int n = vpu.Length;
            var counts = new int[n];
            double rem = target;
            if (target > eps)
            {
                for (int i = 0; i < n; i++)
                {
                    if (rem <= eps) break;
                    if (vpu[i] <= 0.0 || avail[i] <= 0) continue;
                    // How many whole units cover the remaining value? rem/vpu, rounded up — but if rem is (within eps)
                    // an exact multiple of vpu, take exactly that many, not one more. Computed with a different shape
                    // than the production code (an explicit floor + fractional-remainder test) so a shared bug is unlikely.
                    double exact = rem / vpu[i];
                    int floor = (int)System.Math.Floor(exact + eps);     // exact multiple → the multiple itself
                    int need = (exact - floor > eps) ? floor + 1 : floor; // any real shortfall → one more unit
                    if (need < 0) need = 0;
                    int take = System.Math.Min(avail[i], need);
                    counts[i] = take;
                    rem -= take * vpu[i];
                }
            }
            filled = rem <= eps;
            return counts;
        }

        [Test]
        public void MixFillSlot_MatchesIndependentOracle_OverManyRandomCases()
        {
            // Fixed-seed System.Random (NOT Verse.Rand — Core is Verse-free + deterministic) so the case set is
            // reproducible. Vary candidate count, value target, value-per-unit (incl. a zero), and availability
            // (incl. zero + genuine shortage), and assert MixFillSlot agrees with the oracle on counts AND filled.
            var rng = new System.Random(12345);
            for (int iter = 0; iter < 5000; iter++)
            {
                int n = 1 + rng.Next(5); // 1..5 candidates
                var vpu = new double[n];
                var avail = new int[n];
                for (int i = 0; i < n; i++)
                {
                    // ~1 in 6 candidates is valueless (0 vpu) → must be skipped; others get a coarse fractional vpu.
                    vpu[i] = (rng.Next(6) == 0) ? 0.0 : System.Math.Round(0.05 + rng.NextDouble() * 1.45, 2);
                    // ~1 in 5 candidates has zero stock; others 0..20 units.
                    avail[i] = (rng.Next(5) == 0) ? 0 : rng.Next(21);
                }
                double target = System.Math.Round(rng.NextDouble() * 6.0, 2); // 0..6 value (incl. 0 sometimes)

                var got = CraftBatchMath.MixFillSlot(target, vpu, avail);
                var oracleCounts = MixFillOracle(target, vpu, avail, out bool oracleFilled);

                Assert.That(got.filled, Is.EqualTo(oracleFilled),
                    $"filled mismatch @iter {iter}: target={target}, vpu=[{string.Join(",", vpu)}], avail=[{string.Join(",", avail)}]");
                Assert.That(got.counts, Is.EqualTo(oracleCounts),
                    $"counts mismatch @iter {iter}: target={target}, vpu=[{string.Join(",", vpu)}], avail=[{string.Join(",", avail)}]");
            }
        }

        [Test]
        public void MixFillSlot_SingleDef_ExactMultiple_NoOverRound()
        {
            // target 1.0 value, 0.5 value/unit, 10 available → EXACTLY 2 units (1.0 value), not 3 (the `- EPS` guard).
            var r = CraftBatchMath.MixFillSlot(1.0, new[] { 0.5 }, new[] { 10 });
            Assert.That(r.counts, Is.EqualTo(new[] { 2 }));
            Assert.That(r.filled, Is.True);
        }

        [Test]
        public void MixFillSlot_SingleDef_CeilRounding()
        {
            // target 0.6, 0.5 value/unit → ceil(1.2) = 2 units (1.0 value ≥ 0.6) → filled.
            var r = CraftBatchMath.MixFillSlot(0.6, new[] { 0.5 }, new[] { 10 });
            Assert.That(r.counts, Is.EqualTo(new[] { 2 }));
            Assert.That(r.filled, Is.True);
        }

        [Test]
        public void MixFillSlot_MultiDef_SpansCandidatesInOrder()
        {
            // target 1.0; candidate0 (0.5/unit, only 1 avail) covers 0.5 → remaining 0.5; candidate1 (0.4/unit, plenty)
            // needs ceil(0.5/0.4) = ceil(1.25) = 2 units (0.8 value) → filled. counts = [1, 2].
            var r = CraftBatchMath.MixFillSlot(1.0, new[] { 0.5, 0.4 }, new[] { 1, 10 });
            Assert.That(r.counts, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(r.filled, Is.True);
        }

        [Test]
        public void MixFillSlot_Infeasible_NotEnoughStock()
        {
            // target 5.0 value but only 4 units of a 0.5/unit def = 2.0 value available → can't fill. counts take all 4,
            // filled is false (remaining 3.0 > EPS) so the caller refuses this rep.
            var r = CraftBatchMath.MixFillSlot(5.0, new[] { 0.5 }, new[] { 4 });
            Assert.That(r.counts, Is.EqualTo(new[] { 4 }));
            Assert.That(r.filled, Is.False);
            Assert.That(r.remaining, Is.EqualTo(3.0).Within(1e-6));
        }

        [Test]
        public void MixFillSlot_ZeroValueCandidate_IsSkipped()
        {
            // candidate0 is valueless (0 vpu) → skipped entirely; candidate1 (0.5/unit) fills the target.
            var r = CraftBatchMath.MixFillSlot(1.0, new[] { 0.0, 0.5 }, new[] { 99, 10 });
            Assert.That(r.counts, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(r.filled, Is.True);
        }

        [Test]
        public void MixFillSlot_ZeroTarget_TakesNothing()
        {
            // A non-positive value target is satisfied by taking nothing (matches vanilla's pre-loop short-circuit).
            var r = CraftBatchMath.MixFillSlot(0.0, new[] { 0.5, 0.4 }, new[] { 10, 10 });
            Assert.That(r.counts, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(r.filled, Is.True);
        }

        [Test]
        public void MixAvailableReps_FloorsByValue()
        {
            Assert.That(CraftBatchMath.MixAvailableReps(perRepValue: 0.9, totalAvailableValue: 5.0), Is.EqualTo(5)); // 5×0.9=4.5 ≤ 5
            Assert.That(CraftBatchMath.MixAvailableReps(0.9, 0.8), Is.EqualTo(0));   // not even one rep's value
            Assert.That(CraftBatchMath.MixAvailableReps(2.0, 10.0), Is.EqualTo(5));  // exact
        }

        [Test]
        public void MixAvailableReps_NoValueNeed_IsUnbounded_AndNegativesFloor()
        {
            Assert.That(CraftBatchMath.MixAvailableReps(perRepValue: 0.0, totalAvailableValue: 5.0), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.MixAvailableReps(-1.0, 5.0), Is.EqualTo(int.MaxValue));
            Assert.That(CraftBatchMath.MixAvailableReps(0.5, 0.0), Is.EqualTo(0));
        }
    }
}
