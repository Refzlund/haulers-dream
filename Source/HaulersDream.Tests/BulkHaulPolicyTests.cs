using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class BulkHaulPolicyTests
    {
        // ── CeilingKg: the worth-it mass ceiling derives from the smart-overload break-even ──────────

        [Test]
        public void Ceiling_NoSlowdownLevel_IsUnbounded()
        {
            // Slider at 0 = carrying more is free → more is always worth it.
            Assert.That(float.IsPositiveInfinity(BulkHaulPolicy.CeilingKg(0, false, 35f)), Is.True);
        }

        [Test]
        public void Ceiling_OffLevel_IsExactlyTheCarryLimit()
        {
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.OffLevel, false, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        [Test]
        public void Ceiling_StrictOverridesSliderToCarryLimit()
        {
            // Fair slider, but strict carry weight on → never overload.
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, true, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        [Test]
        public void Ceiling_FairLevel_IsTheBreakEvenRatioTimesBaseCap()
        {
            float expected = OverloadTuning.MaxOverloadRatio(OverloadTuning.FairLevel) * 35f;
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 35f), Is.EqualTo(expected).Within(0.001f));
            Assert.That(expected, Is.GreaterThan(35f)); // Fair overloads past 100%…
            Assert.That(expected, Is.LessThan(35f * 3f)); // …but not absurdly (≈2× capacity break-even)
        }

        [Test]
        public void Ceiling_SteeperSlope_LowersTheCeiling()
        {
            // Worth-it intuition: a harsher slowdown makes extra weight pay off later → lower ceiling.
            float fair = BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 35f);
            float cautious = BulkHaulPolicy.CeilingKg(9, false, 35f);
            Assert.That(cautious, Is.LessThan(fair));
            Assert.That(cautious, Is.GreaterThanOrEqualTo(35f));
        }

        [Test]
        public void Ceiling_NonPositiveBaseCap_IsZero()
        {
            Assert.That(BulkHaulPolicy.CeilingKg(OverloadTuning.FairLevel, false, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Ceiling_StrictBeatsNoSlowdownLevel()
        {
            // Level 0 alone is unbounded ("carrying more is free"), but strict carry weight still wins:
            // the ceiling is exactly the base cap, never infinity.
            Assert.That(BulkHaulPolicy.CeilingKg(0, true, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        [Test]
        public void Ceiling_OutOfRangeLevels_BehaveAsTheClampedLevels()
        {
            // OverloadTuning clamps internally: -3 acts as level 0 (unbounded), 99 as level 10 (Off).
            Assert.That(float.IsPositiveInfinity(BulkHaulPolicy.CeilingKg(-3, false, 35f)), Is.True);
            Assert.That(BulkHaulPolicy.CeilingKg(99, false, 35f), Is.EqualTo(35f).Within(0.001f));
        }

        // ── TriggerSatisfied: automatic always sweeps; forced respects the finer-control option ─────

        [Test]
        public void Trigger_AutomaticHaul_AlwaysSweeps_BothModes()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: false, secondNearbyTasked: false), Is.True);
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: false, secondNearbyTasked: false), Is.True);
        }

        [Test]
        public void Trigger_ForcedSingleOrder_SecondTaskedMode_DoesNotSweep()
        {
            // The finer-control default: ordering ONE haul truly hauls one thing.
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: false), Is.False);
        }

        [Test]
        public void Trigger_ForcedOrder_SweepsWhenSecondNearbyTasked()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: true), Is.True);
        }

        [Test]
        public void Trigger_ForcedOrder_AlwaysMode_Sweeps()
        {
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: true, secondNearbyTasked: false), Is.True);
        }

        [Test]
        public void Trigger_RemainingTruthTableCells_AllSweep()
        {
            // forced + Always: secondTasked is irrelevant — still sweeps.
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: true, secondNearbyTasked: true), Is.True);
            // Automatic hauls sweep regardless of a (vacuous) secondTasked flag, in both modes.
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.Always, forced: false, secondNearbyTasked: true), Is.True);
            Assert.That(BulkHaulPolicy.TriggerSatisfied(BulkHaulTrigger.SecondTasked, forced: false, secondNearbyTasked: true), Is.True);
        }

        // ── DecideOrderedHaul (#223): an oversized single order rides inventory, it does NOT sweep ─────

        private static BulkHaulPolicy.OrderedHaulPlan Plan(
            BulkHaulTrigger trigger = BulkHaulTrigger.SecondTasked, bool forced = true, bool forceSweep = false,
            bool secondNearbyTasked = false, bool oversizedRidesInventory = false)
            => BulkHaulPolicy.DecideOrderedHaul(trigger, forced, forceSweep, secondNearbyTasked, oversizedRidesInventory);

        [Test]
        public void OrderedHaul_ForceSweep_AlwaysSweeps_RegardlessOfTriggerForcedOrOversized()
        {
            // The explicit "Haul everything nearby" button always sweeps, whatever the trigger / oversized flags.
            foreach (var trigger in new[] { BulkHaulTrigger.Always, BulkHaulTrigger.SecondTasked })
                foreach (var oversized in new[] { false, true })
                    foreach (var forced in new[] { false, true })
                        Assert.That(Plan(trigger, forced: forced, forceSweep: true, oversizedRidesInventory: oversized),
                            Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors),
                            $"forceSweep must sweep (trigger={trigger}, forced={forced}, oversized={oversized})");
        }

        [Test]
        public void OrderedHaul_AutomaticHaul_AlwaysSweeps_BothTriggers()
        {
            // Automatic (non-forced) work-scan hauls always sweep: the nearby haulables are already tasked.
            Assert.That(Plan(BulkHaulTrigger.Always, forced: false), Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
            Assert.That(Plan(BulkHaulTrigger.SecondTasked, forced: false), Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
        }

        [Test]
        public void OrderedHaul_ForcedSingleOrder_NotOversized_IsVanillaSingle()
        {
            // The finer-control default: a lone forced order of a normal-size stack stays a vanilla single haul.
            Assert.That(Plan(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: false, oversizedRidesInventory: false),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.VanillaSingle));
        }

        [Test]
        public void OrderedHaul_ForcedOversizedSingle_RidesInventory_DoesNotSweep_223()
        {
            // REGRESSION PIN #223: a forced order of an OVERSIZED stack, no second order tasked, SecondTasked
            // trigger, must deliver JUST that stack via inventory (one trip), NOT sweep the neighborhood. The old
            // gate let the oversized carve-out fall through into the sweep, so with stack-size mods every big-stack
            // order behaved like "Haul everything nearby" and the SecondTasked setting had no effect.
            Assert.That(Plan(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: false, oversizedRidesInventory: true),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.InventorySingleStack));
        }

        [Test]
        public void OrderedHaul_ForcedSecondTasked_Sweeps_RegardlessOfOversized()
        {
            // A genuine second nearby order under SecondTasked is the "clean up this area" signal: sweep either way.
            Assert.That(Plan(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: true, oversizedRidesInventory: false),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
            Assert.That(Plan(BulkHaulTrigger.SecondTasked, forced: true, secondNearbyTasked: true, oversizedRidesInventory: true),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
        }

        [Test]
        public void OrderedHaul_AlwaysTrigger_ForcedSingle_Sweeps_RegardlessOfOversized()
        {
            // Under Always, even a lone forced order sweeps (the setting opts every order into bulk), oversized or not.
            Assert.That(Plan(BulkHaulTrigger.Always, forced: true, secondNearbyTasked: false, oversizedRidesInventory: false),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
            Assert.That(Plan(BulkHaulTrigger.Always, forced: true, secondNearbyTasked: false, oversizedRidesInventory: true),
                Is.EqualTo(BulkHaulPolicy.OrderedHaulPlan.SweepNeighbors));
        }

        // ── DecideTakeover: the 2nd nearby haul order takes over with an immediate sweep ───────────────

        private static BulkHaulPolicy.BulkTakeoverAction Takeover(
            BulkHaulTrigger trigger = BulkHaulTrigger.SecondTasked, bool incomingIsBulk = true,
            bool curIsLoadingBulk = false, bool curIsSoloHaulInSweep = false)
            => BulkHaulPolicy.DecideTakeover(trigger, incomingIsBulk, curIsLoadingBulk, curIsSoloHaulInSweep);

        [Test]
        public void Takeover_SecondOrderWhileHaulingSoloFirst_TakesOver()
        {
            // The reported bug: pawn hauling the first item solo, a 2nd nearby haul ordered (already a bulk) →
            // interrupt the solo haul and start the sweep NOW (the first item is part of the sweep).
            Assert.That(Takeover(curIsSoloHaulInSweep: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.TakeOverSoloHaul));
        }

        [Test]
        public void Takeover_ThirdOrderWhileSweeping_AppendsToRunningBulk()
        {
            // Idempotence: a sweep is already running → the new item folds into it (one trip), no interrupt.
            Assert.That(Takeover(curIsLoadingBulk: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.AppendToActiveBulk));
        }

        [Test]
        public void Takeover_AppendWinsWhenBothBulkRunningAndSoloPresent()
        {
            // A running sweep takes precedence over the solo-takeover branch (fold in, don't restart).
            Assert.That(Takeover(curIsLoadingBulk: true, curIsSoloHaulInSweep: true),
                Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.AppendToActiveBulk));
        }

        [Test]
        public void Takeover_LoneOrder_PassesThrough()
        {
            // No running sweep and no solo first haul to fold in (e.g. the FIRST order, or an idle pawn) →
            // vanilla handles it. (Under SecondTasked a lone first order isn't even a bulk, so it never gets here.)
            Assert.That(Takeover(), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
        }

        [Test]
        public void Takeover_UnrelatedCurrentJob_PassesThrough()
        {
            // The pawn's current job is not a sweep and not a first haul that's part of this sweep → don't
            // interrupt it (curIsSoloHaulInSweep is false because the target isn't in the incoming sweep).
            Assert.That(Takeover(curIsSoloHaulInSweep: false, curIsLoadingBulk: false),
                Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
        }

        [Test]
        public void Takeover_AlwaysMode_NeverTakesOver()
        {
            // Under Always the first order is itself already a sweep, so there's never a solo haul to absorb;
            // leave the existing per-order behavior untouched (pass through) regardless of the other flags.
            Assert.That(Takeover(BulkHaulTrigger.Always, curIsSoloHaulInSweep: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
            Assert.That(Takeover(BulkHaulTrigger.Always, curIsLoadingBulk: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
        }

        [Test]
        public void Takeover_NonBulkIncoming_PassesThrough()
        {
            // A vanilla single haul (the surgical FIRST order, or an isolated item) is never intercepted.
            Assert.That(Takeover(incomingIsBulk: false, curIsSoloHaulInSweep: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
            Assert.That(Takeover(incomingIsBulk: false, curIsLoadingBulk: true), Is.EqualTo(BulkHaulPolicy.BulkTakeoverAction.PassThrough));
        }

        // ── OversizedStackWorthInventory: bug 2 (a single stack too big for one armful goes via inventory) ──

        [Test]
        public void Oversized_UsersCase_StackExceedsCarryCapWithAmpleStorage_Converts()
        {
            // 75 steel, carry cap 72, shelf has room for the lot (deliverable 75) → carry it in inventory.
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 75, handCap: 72, deliverable: 75), Is.True);
        }

        [Test]
        public void Oversized_FitsInOneArmful_DoesNotConvert()
        {
            // Stack fits in hands (40 <= 72) → keep the vanilla single hand-haul.
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 40, handCap: 72, deliverable: 40), Is.False);
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 72, handCap: 72, deliverable: 72), Is.False);
        }

        [Test]
        public void Oversized_StorageStarved_DoesNotConvert_NoStranding()
        {
            // Oversized (75 > 72) but storage can only take 72 — inventory delivers no more than hands, so DON'T
            // convert (and the caller would never carry the un-storable remainder).
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 75, handCap: 72, deliverable: 72), Is.False);
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 75, handCap: 72, deliverable: 50), Is.False);
        }

        [Test]
        public void Oversized_StoragePartlyBeyondHands_Converts()
        {
            // Oversized AND storage takes a bit more than one armful (73 > 72) → convert; the caller clamps the
            // carried count to deliverable so the 2 that can't be stored never ride along.
            Assert.That(BulkHaulPolicy.OversizedStackWorthInventory(stackCount: 75, handCap: 72, deliverable: 73), Is.True);
        }

        // ── CountWithinCeiling ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Count_FitsUnderCeiling()
        {
            // 10 kg of room, 0.5 kg/unit → 20 fit; stack has 30.
            Assert.That(BulkHaulPolicy.CountWithinCeiling(45f, 35f, 0.5f, 30), Is.EqualTo(20));
        }

        [Test]
        public void Count_StackSmallerThanRoom_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(45f, 35f, 0.5f, 10), Is.EqualTo(10));
        }

        [Test]
        public void Count_NoRoom_TakesNothing()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(35f, 35f, 0.5f, 10), Is.EqualTo(0));
        }

        [Test]
        public void Count_UnboundedCeiling_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(float.PositiveInfinity, 9999f, 75f, 12), Is.EqualTo(12));
        }

        [Test]
        public void Count_MasslessItem_TakesWholeStack()
        {
            Assert.That(BulkHaulPolicy.CountWithinCeiling(35f, 35f, 0f, 40), Is.EqualTo(40));
        }

        [Test]
        public void Count_NaNCeiling_FailsClosed_TakesNothing()
        {
            // remaining = NaN − current = NaN, which fails the <= 0 early-out; the unchecked float→int
            // conversion of NaN is UNDEFINED by the C# spec (0 on common runtimes, but not guaranteed) —
            // in practice it lands non-positive and the fits <= 0 guard returns 0. Pinned on purpose: a
            // rounding/cast change in CarryMath.CountToPickUp could silently flip this from fail-closed
            // to fail-open.
            Assert.That(BulkHaulPolicy.CountWithinCeiling(float.NaN, 10f, 1f, 50), Is.EqualTo(0));
        }

        [Test]
        public void Count_MasslessItem_NegativeStack_TakesNothing()
        {
            // Guard ORDER pinned: the non-positive stack check runs BEFORE the massless take-all branch,
            // so a negative stackCount can never be returned as the count.
            Assert.That(BulkHaulPolicy.CountWithinCeiling(35f, 0f, 0f, -7), Is.EqualTo(0));
        }

        [Test]
        public void Count_NeverExceedsTheStack_AcrossInputGrid()
        {
            // The driver's shrink-only contract: the live re-clamp may REDUCE the planned take but can
            // never inflate it past what the stack holds (and never below zero) — across massless,
            // infinite-ceiling, NaN and overweight paths alike.
            float[] ceilings = { 0f, 35f, 100f, float.PositiveInfinity, float.NaN };
            float[] currents = { 0f, 10f, 35f, 200f };
            float[] units = { 0f, 0.008f, 0.5f, 75f };
            int[] stacks = { -3, 0, 1, 10, 75, 100000 };
            foreach (float c in ceilings)
                foreach (float m in currents)
                    foreach (float u in units)
                        foreach (int n in stacks)
                        {
                            int got = BulkHaulPolicy.CountWithinCeiling(c, m, u, n);
                            int max = n > 0 ? n : 0;
                            Assert.That(got, Is.InRange(0, max),
                                $"CountWithinCeiling({c}, {m}, {u}, {n})");
                        }
        }

        // ── InventoryHaulWorseThanHands: #115 (CE-bulky ammo delivered one round at a time via inventory) ──

        [Test]
        public void InvWorseThanHands_CE_BulkyShell_InventoryFewerThanHands_Declines()
        {
            // The reporter's case: a heavy cannon shell fits ~1 in CE-bulk-limited inventory but a hand-armful is
            // several → decline the inventory conversion so the vanilla hand-haul (which carries more) stands.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(ceActive: true, forceSweep: false, inventoryFit: 1, handArmful: 6), Is.True);
        }

        [Test]
        public void InvWorseThanHands_CE_InventoryAtLeastHands_Converts()
        {
            // Light ammo / normal goods: inventory holds a full armful or more → convert (sweep) as before.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit: 6, handArmful: 6), Is.False);
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit: 40, handArmful: 6), Is.False);
        }

        [Test]
        public void InvWorseThanHands_NoCE_NeverDeclines()
        {
            // Without CE, inventory and hands share the one mass/volume limit; a small inventoryFit means the pawn is
            // already loaded, where declining would abort a legitimate accumulation — so it must stay false.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(ceActive: false, forceSweep: false, inventoryFit: 1, handArmful: 6), Is.False);
        }

        [Test]
        public void InvWorseThanHands_ForceSweep_NeverDeclines()
        {
            // The explicit "haul everything nearby" order always sweeps — the player asked for it.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(ceActive: true, forceSweep: true, inventoryFit: 1, handArmful: 6), Is.False);
        }

        // ---- InventoryHaulWorseThanHands + stack clamp: #124 (chunks hauled one at a time under CE) ----
        //
        // Reporter setup (issue #124 follow-up + 124-hd.log): CE CarryWeight 215.6 kg with 41 in use, CE Bulk
        // capacity 530 with 25 in use, a chunk-stacking tweak raising chunk stackLimit above 1 (FrozenSnowFox
        // Tweaks "Stackable Chunks" in the reported mod list). Chunks: vanilla Mass 18 to 25 kg (granite 25),
        // CE Bulk 1 (CE's Bulk StatDef defaultBaseValue, no chunk patch exists). Every field chunk is its own
        // 1-count stack, so the CE inventory fit is clamped to 1 while the def-level hand armful is the modded
        // stackLimit: the old comparison (1 < N) declined every AUTOMATIC chunk haul and vanilla hand-hauled
        // one chunk per trip, while the forceSweep order (which skips the guard) swept 7 chunks (the log).

        /// <summary>CE's CanFitInInventory count for a stack, replicated from the decompile-verified source
        /// (CompInventory.cs: count = FloorToInt(Min(availBulk / unitBulk, availWeight / unitWeight, stackCount)),
        /// zero-guards per CE: a non-positive per-unit stat never binds and falls back to stackCount).</summary>
        private static int CeFitCount(float availWeight, float availBulk, float unitWeight, float unitBulk, int stackCount)
        {
            float byWeight = unitWeight <= 0f ? stackCount : availWeight / unitWeight;
            float byBulk = unitBulk <= 0f ? stackCount : availBulk / unitBulk;
            return (int)System.Math.Floor(System.Math.Min(byBulk, System.Math.Min(byWeight, (float)stackCount)));
        }

        [Test]
        public void InvWorseThanHands_124_LoneChunk_ModdedStackLimit_NowConverts()
        {
            // Reporter's numbers: available weight 215.6 - 41 = 174.6, available bulk 530 - 25 = 505;
            // granite chunk 25 kg, Bulk 1, lying alone (stackCount 1); modded chunk stackLimit 5 = handCap.
            int inventoryFit = CeFitCount(availWeight: 174.6f, availBulk: 505f, unitWeight: 25f, unitBulk: 1f, stackCount: 1);
            Assert.That(inventoryFit, Is.EqualTo(1), "CE fits the whole 1-count chunk stack in inventory");

            // Pre #124 comparison (def-level overload): fit 1 against armful 5 wrongly declined the sweep.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit, handArmful: 5), Is.True,
                "the old def-level comparison is what hauled chunks one at a time");

            // Post #124: hands cannot move more than the whole 1-count stack either, so the sweep proceeds.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit, handCap: 5, stackCount: 1), Is.False,
                "a whole stack that fits in inventory must never be declined");
        }

        [Test]
        public void InvWorseThanHands_124_ChunkFieldPlan_SweepsMultipleChunksPerTrip()
        {
            // End-to-end plan oracle mirroring BuildBulkJob's decision sequence for the reporter's chunk field:
            // ceiling = CE strict carry weight 215.6, running gear+inventory 41, granite chunks 25 kg / Bulk 1
            // each as 1-count stacks. Proves the post-fix AUTOMATIC decision is "sweep several chunks in one
            // trip" (the forced order already did exactly this in the reporter's log: 7 stacks, ceiling 215.6).
            const float ceiling = 215.6f;
            const float chunkMass = 25f;
            const float chunkBulk = 1f;
            const int handCap = 5;
            float running = 41f;
            float availWeight = 174.6f;
            float availBulk = 505f;

            // The primary chunk: fits under the ceiling and in CE inventory, whole stack -> not declined.
            int primaryTake = BulkHaulPolicy.CountWithinCeiling(ceiling, running, chunkMass, 1);
            primaryTake = System.Math.Min(primaryTake, CeFitCount(availWeight, availBulk, chunkMass, chunkBulk, 1));
            Assert.That(primaryTake, Is.EqualTo(1));
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, primaryTake, handCap, stackCount: 1), Is.False);
            running += primaryTake * chunkMass;

            // The sweep loop: keep taking 1-count chunk stacks while they fit under the ceiling (BuildBulkJob's
            // loop condition running < ceiling - 0.0001f with per-candidate CountWithinCeiling).
            int sweptChunks = 0;
            while (running < ceiling - 0.0001f)
            {
                int take = BulkHaulPolicy.CountWithinCeiling(ceiling, running, chunkMass, 1);
                if (take <= 0)
                    break;
                sweptChunks++;
                running += take * chunkMass;
            }

            // 41 + 6 * 25 = 191 fits; the 7th chunk would pass 215.6 -> six chunks ride in one trip (the live
            // log's forced sweep carried 7 stacks of mixed 18 to 25 kg chunks against the same ceiling).
            Assert.That(1 + sweptChunks, Is.EqualTo(6), "one trip now moves the whole nearby chunk cluster");
        }

        [Test]
        public void InvWorseThanHands_115_ShelfStackOfShells_StillDeclines()
        {
            // #115 regression pin with CE's real 155mm HE shell numbers (Defs/Ammo/Shell/155mmHowitzer.xml:
            // stackLimit 25, Mass 46.7, Bulk 47.67). A shelf stack of 25 shells, a strong pawn with bulk room
            // for one shell only: inventory trickles 1 while hands move a full armful of the SAME stack.
            int inventoryFit = CeFitCount(availWeight: 174.6f, availBulk: 55f, unitWeight: 46.7f, unitBulk: 47.67f, stackCount: 25);
            Assert.That(inventoryFit, Is.EqualTo(1), "CE bulk-binds the shell fit to a single round");
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit, handCap: 25, stackCount: 25), Is.True,
                "bulky ammo in a big stack keeps the vanilla hand-haul (#115 must not regress)");
        }

        [Test]
        public void InvWorseThanHands_PartialFitOfBiggerStack_StillDeclines()
        {
            // Inventory takes 2 of a 3-count stack while one armful would move all 3: hands still win.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit: 2, handCap: 10, stackCount: 3), Is.True);
        }

        [Test]
        public void InvWorseThanHands_LoneBulkyRound_WholeStackFits_Converts()
        {
            // A single heavy round on the ground (stackCount 1): hands also move exactly 1, and the sweep
            // additionally collects the neighbors, so inventory is never worse for a whole-stack fit.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, inventoryFit: 1, handCap: 10, stackCount: 1), Is.False);
        }

        [Test]
        public void InvWorseThanHands_DefLevelOverload_KeepsOldSemantics()
        {
            // The 4-argument overload is the pre #124 shape kept for compatibility: it behaves as if the stack
            // were at least an armful (stackCount unbounded), so the two overloads agree there.
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, 1, 6),
                Is.EqualTo(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, 1, 6, int.MaxValue)));
            Assert.That(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, 6, 6),
                Is.EqualTo(BulkHaulPolicy.InventoryHaulWorseThanHands(true, false, 6, 6, int.MaxValue)));
        }
    }
}
