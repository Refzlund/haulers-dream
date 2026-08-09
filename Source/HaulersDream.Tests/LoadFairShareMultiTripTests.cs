using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The multi-trip oracle for the bulk-load claim split, with <b>N real askers</b>. Sibling of
    /// <see cref="LoadFairShareTests"/>, which owns the unit contract of
    /// <see cref="LoadFairShare.ShareMassBudget"/>, the <see cref="LoadFairShare.CountsAsCoLoader"/> predicate, and
    /// the single-asker multi-trip runs; this fixture owns everything those cannot see, and states the whole
    /// behaviour as three named properties swept over the axes that matter.
    ///
    /// <para>WHY A SECOND FIXTURE. The "one item per trip" symptom has shipped TWICE with a green suite. The first
    /// escape was that nothing simulated successive TRIPS at all; the second was that the multi-trip runs which
    /// closed it all used ONE asker with a synthetic divisor, so a crew that genuinely interleaves trips against one
    /// shrinking pool was still unmodelled. Every run here has real pawns that claim, deliver and come back — the
    /// divisor is recounted from who actually holds a claim at that instant, exactly as the runtime recounts it, and
    /// it therefore CHANGES across rounds instead of being held constant.</para>
    ///
    /// <para>THE THREE PROPERTIES (from <c>docs/reports/phase2-per-trip-quantity.md</c> §8), each checked by a
    /// function that returns the violations it found rather than asserting directly, so the same code can pin the
    /// real rule (expect none) and convict a broken one (expect some):</para>
    /// <list type="number">
    /// <item><see cref="ConservationViolations"/> — every unit is delivered exactly once and the ledger ends
    /// empty.</item>
    /// <item><see cref="FullnessViolations"/> — no trip carries less than its pack could hold unless a genuine crew
    /// split explains it; and a remainder that fits one trip is NEVER split, whatever the crew.</item>
    /// <item><see cref="TripBoundViolations"/> — at most <c>ceil(pool / capacity) + crew − 1</c> trips.</item>
    /// </list>
    ///
    /// <para>→ GOTCHA: the report states P1 and P3 "for uniform units", and they mean it. A pool of mixed masses has
    /// no closed-form trip optimum (packing three 9 kg anvils into a 15 kg pack takes three trips, not the two a
    /// mass ratio suggests), and it can hold an item no pack can lift at all. So the sweep that pins P1/P3 is
    /// uniform-mass by construction, and the mixed-mass cases are pinned by P2 (which is stated against what the
    /// pack could actually have taken) plus named scenarios — see
    /// <see cref="MixedPool_OneHeavyItemAmongManyLight_IsNeverStarvedOrStalled"/> and
    /// <see cref="AnItemNoPackCanLift_IsLeftBehindWithoutStallingTheRest"/>.</para>
    ///
    /// <para>→ NOTE: what the simulation still does not model, deliberately — walk time and distance ordering, the
    /// destination mass cap shrinking as a transporter fills, and inventory residue carried between trips. Those are
    /// per-trip BUDGET terms audited in the report's §3 table, not claim-splitting terms, and modelling them here
    /// would make this a route simulator rather than an oracle for the split.</para>
    /// </summary>
    [TestFixture]
    public class LoadFairShareMultiTripTests
    {
        private const float Inf = float.PositiveInfinity;

        /*
            ──────────────────────────────────────────────────────────────────────
                                    The crew simulation
            ──────────────────────────────────────────────────────────────────────
            One whole load, run the way the game runs it: every claimless crew member plans a trip and takes a
            claim, then everyone in flight deposits and the ledger settles. The pool shrinks only on DEPOSIT, so
            while a peer is walking its slice is claimed-but-still-on-the-ground — which is precisely why the
            divisor drops for the next asker (LoadLedger excludes claim holders) and why this differs from
            LoadFairShareTests.RunTripsToCompletion, where the divisor is a constant.

            → KEY: a peer that is counted in the divisor but holds no claim and is not about to take one is
              arithmetically INDISTINGUISHABLE from a phantom. That is not a modelling artefact, it is issue #167
              in one sentence, and it is why P2 carries a witness clause instead of a bare "every trip is full".
        */

        /// <summary>One ground stack in the simulated pool.</summary>
        private sealed class Stack
        {
            /// <summary>Fake def id (the ledger's TDef).</summary>
            public string def;
            /// <summary>Units still lying on the ground; drops only when a pawn DEPOSITS them.</summary>
            public int count;
            /// <summary>Mass of one unit, kg. Zero models a massless item (the mass budget cannot bound it).</summary>
            public float unitMass;
            /// <summary>Which pawn currently has this exact stack queued in a live plan, or
            /// <see cref="NoOwner"/>. Only consulted when the scenario models per-THING exclusivity; see
            /// <see cref="LoadScenario.perThingExclusive"/>.</summary>
            public int owner = NoOwner;

            public Stack(string def, int count, float unitMass)
            {
                this.def = def;
                this.count = count;
                this.unitMass = unitMass;
            }
        }

        /// <summary>Sentinel for a stack no pawn has queued.</summary>
        private const int NoOwner = 0;

        /// <summary>Whole simulated task: the three ledger dictionaries plus the ground pool.</summary>
        private sealed class CrewSim
        {
            public Dictionary<string, int> needed = new Dictionary<string, int>();
            public Dictionary<string, int> claimed = new Dictionary<string, int>();
            public Dictionary<int, Dictionary<string, int>> pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            public List<Stack> pool = new List<Stack>();

            public static CrewSim FromPool(params Stack[] stacks)
            {
                var sim = new CrewSim();
                foreach (var s in stacks)
                {
                    sim.pool.Add(s);
                    sim.needed[s.def] = (sim.needed.TryGetValue(s.def, out int cur) ? cur : 0) + s.count;
                }
                return sim;
            }
        }

        /// <summary>
        /// The share DECISION under test, in the exact shape <c>TransportLoad.TryGiveBulkJob</c> calls it. Taking it
        /// as a delegate is what lets a historical or deliberately-broken rule be driven through the identical
        /// simulation, so "this property would have caught that bug" is demonstrated rather than asserted.
        /// </summary>
        /// <param name="claimableMassKg">Mass of what this asker could claim right now.</param>
        /// <param name="heaviestUnitMassKg">Heaviest single claimable unit — the no-starvation floor.</param>
        /// <param name="loaderCount">The divisor: the asker plus every counted claimless co-loader.</param>
        /// <param name="askerTripBudgetKg">What one trip is worth to the asker, already substituted for the
        /// unbounded sentinel by <see cref="LoadFairShare.AskerTripBudgetKg"/> where the runtime substitutes it.</param>
        /// <returns>The mass a claim may cover, or <see cref="float.PositiveInfinity"/> for "no clamp".</returns>
        private delegate float ShareRule(float claimableMassKg, float heaviestUnitMassKg, int loaderCount,
            float askerTripBudgetKg);

        /// <summary>The shipped rule (v1.22.0 + v1.23.0, identical to branch HEAD) — the subject of every positive
        /// assertion in this fixture.</summary>
        private static readonly ShareRule CurrentRule = LoadFairShare.ShareMassBudget;

        /// <summary>
        /// The rule as it shipped in v1.18.0–v1.21.0, reconstructed verbatim from
        /// <c>git show v1.19.0:Source/HaulersDream.Core/LoadFairShare.cs</c>: three arguments, so no
        /// fit-in-one-trip short-circuit and no unbounded-asker guard — the shrinking remainder was re-divided on
        /// every trip until the no-starvation floor bottomed it out at one item.
        ///
        /// <para>This is the negative control the whole fixture is calibrated against. A property no broken rule
        /// violates is decoration, so every property below is run against THIS as well, and
        /// <see cref="LegacyRule_ReproducesTheReportedTripsExactly"/> first proves the reconstruction is faithful by
        /// replaying issue #247's own numbers.</para>
        /// </summary>
        /// <param name="askerTripBudgetKg">Accepted and IGNORED — the v1.19.0 signature had no such parameter, and
        /// that absence is the defect being modelled.</param>
        private static float LegacyRuleV1190(float claimableMassKg, float heaviestUnitMassKg, int loaderCount,
            float askerTripBudgetKg)
        {
            if (loaderCount <= 1)
                return float.PositiveInfinity;
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;
            float share = claimableMassKg / loaderCount;
            if (heaviestUnitMassKg > 0f && share < heaviestUnitMassKg)
                share = heaviestUnitMassKg;
            return share;
        }

        /// <summary>
        /// A rule that clamps every claim to nothing. Not a historical bug — it is the discriminator for
        /// <see cref="ConservationViolations"/>, which the v1.19.0 rule does NOT violate (it moves the same goods,
        /// just in far too many trips). Without this control, "conservation holds" would be a claim no mutant in
        /// this file could falsify.
        /// </summary>
        private static float StallingRule(float claimableMassKg, float heaviestUnitMassKg, int loaderCount,
            float askerTripBudgetKg)
            => 0f;

        /// <summary>
        /// The shipped rule with ONE clause removed: the fit-in-one-trip short-circuit. Everything else — the lone
        /// loader, the empty pool, the unbounded guard, the no-starvation floor — is untouched, so whatever this
        /// oracle convicts it of is attributable to that clause alone. This is the half of the #167 fix that stops
        /// a remainder the asker could clear from being re-divided every round.
        /// </summary>
        private static float RuleWithoutTheFitInOneTripShortCircuit(float claimableMassKg, float heaviestUnitMassKg,
            int loaderCount, float askerTripBudgetKg)
        {
            if (loaderCount <= 1)
                return float.PositiveInfinity;
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;
            if (askerTripBudgetKg >= float.MaxValue)
                return float.PositiveInfinity;
            float share = claimableMassKg / loaderCount;
            if (heaviestUnitMassKg > 0f && share < heaviestUnitMassKg)
                share = heaviestUnitMassKg;
            return share;
        }

        /// <summary>
        /// The shipped rule with ONE clause removed: the unbounded-asker guard
        /// (<c>askerTripBudgetKg &gt;= float.MaxValue</c>). Kept as a mutant precisely because it turns out to be
        /// INERT — see <see cref="EachClauseOfTheShippedRule_IsLoadBearing_ExceptTheOneThatIsNot"/>.
        /// </summary>
        private static float RuleWithoutTheUnboundedGuard(float claimableMassKg, float heaviestUnitMassKg,
            int loaderCount, float askerTripBudgetKg)
        {
            if (loaderCount <= 1)
                return float.PositiveInfinity;
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;
            if (askerTripBudgetKg > 0f && claimableMassKg <= askerTripBudgetKg)
                return float.PositiveInfinity;
            float share = claimableMassKg / loaderCount;
            if (heaviestUnitMassKg > 0f && share < heaviestUnitMassKg)
                share = heaviestUnitMassKg;
            return share;
        }

        /// <summary>
        /// The shipped rule with ONE clause removed: the no-starvation floor, so a share may fall below the mass of
        /// a single item. The sweep contains orders smaller than the crew (two 4 kg items split eight ways), which
        /// is exactly where a floorless share buys less than one item and the whole crew stalls with goods still on
        /// the ground.
        /// </summary>
        private static float RuleWithoutTheStarvationFloor(float claimableMassKg, float heaviestUnitMassKg,
            int loaderCount, float askerTripBudgetKg)
        {
            if (loaderCount <= 1)
                return float.PositiveInfinity;
            if (claimableMassKg <= 0f)
                return float.PositiveInfinity;
            if (askerTripBudgetKg >= float.MaxValue)
                return float.PositiveInfinity;
            if (askerTripBudgetKg > 0f && claimableMassKg <= askerTripBudgetKg)
                return float.PositiveInfinity;
            return claimableMassKg / loaderCount;
        }

        /// <summary>How one whole load is to be run.</summary>
        private sealed class LoadScenario
        {
            /// <summary>How many real pawns ask, claim and deliver. Pawn ids are 1..crewSize.</summary>
            public int crewSize = 1;
            /// <summary>Extra terms added to the divisor for pawns that never ask — the issue #167 shape (a
            /// constructoid and a cleansweeper counted beside the one hauler mech that can actually haul). Zero in
            /// every honest scenario; the negative controls raise it.</summary>
            public int phantomPeers;
            /// <summary>What the planner tells the share rule one trip is worth: already substituted, so never a
            /// sentinel (see <see cref="LoadFairShare.AskerTripBudgetKg"/>).</summary>
            public float decisionBudgetKg = 35f;
            /// <summary>What actually bounds the trip once the share is known — the raw budget, which MAY be a
            /// sentinel (no carry ceiling and an uncapped destination).</summary>
            public float clampBudgetKg = 35f;
            /// <summary>Model vanilla's whole-stack reservation: a stack queued by one pawn is invisible to every
            /// other until that pawn deposits or is interrupted (see the per-THING section).</summary>
            public bool perThingExclusive;
            /// <summary>Interrupt (draft / down / cancel) every Nth plan instead of delivering it, so the claim goes
            /// back through <see cref="LoadLedger{TDef,TPawn}.Release"/>. Zero disables interrupts.</summary>
            public int interruptEveryNthPlan;
            /// <summary>The share decision to drive the run with.</summary>
            public ShareRule rule = CurrentRule;
        }

        /// <summary>One delivered trip, with everything the properties need to judge it AFTER the fact.</summary>
        private sealed class Trip
        {
            /// <summary>Who carried it.</summary>
            public int pawn;
            /// <summary>Units carried.</summary>
            public int units;
            /// <summary>Units this pawn could still claim when it planned — the remainder it was looking at.</summary>
            public int availableUnits;
            /// <summary>How many of the HEAVIEST claimable unit fit in the pawn's own trip budget, ignoring the
            /// share — the legible "pack size in items" for a uniform pool, and <see cref="int.MaxValue"/> for a
            /// massless pool or an unbounded budget. Only meaningful where the pool is uniform, which is why the
            /// fullness property measures against <see cref="unclampedUnits"/> instead and this is used for the
            /// sweep's harness self-check.</summary>
            public int packUnits;
            /// <summary>What the identical sweep would have taken with the share removed — "what the pack could
            /// really have carried", which is the only fullness measure that stays honest on a mixed-mass pool.</summary>
            public int unclampedUnits;
            /// <summary>The divisor used, exactly as the rule saw it.</summary>
            public int divisor;
            /// <summary>The real crew members counted into that divisor. Its size plus one is the divisor UNLESS
            /// phantom peers padded it — which is how <see cref="FullnessViolations"/> tells a genuine crew split
            /// from issue #167.</summary>
            public List<int> countedPeers;
            /// <summary>Everything this asker could claim fitted inside its own single trip, so the rule was
            /// required to hand it over whole.</summary>
            public bool remainderFittedOneTrip;
        }

        /// <summary>Everything one whole simulated load produced.</summary>
        private sealed class LoadRun
        {
            /// <summary>Delivered trips, in the order they were planned.</summary>
            public List<Trip> trips = new List<Trip>();
            /// <summary>Trips each pawn actually delivered — the P2 witness's strongest evidence that a counted peer
            /// was real.</summary>
            public Dictionary<int, int> deliveries = new Dictionary<int, int>();
            /// <summary>Pawns that took a claim at least once, whether or not they got to deliver it. An
            /// interrupted peer belongs here and nowhere else: it committed to this manifest and the draft took the
            /// claim back, which is nothing like a phantom that never asked.</summary>
            public HashSet<int> everClaimed = new HashSet<int>();
            /// <summary>Pawns that asked and found the manifest already empty. The honest reason a counted peer
            /// never delivered: the crew finished the job first.</summary>
            public HashSet<int> ranOutOfWork = new HashSet<int>();
            /// <summary>Pawns with claimable units on the books that their pack could take NOTHING of — the goods
            /// are too heavy to lift, or locked whole inside another pawn's reservation. Not starvation (the share
            /// is not why), and deliberately NOT counted as engaging with the load: such a peer is in the divisor
            /// shrinking everyone else's trip while being unable to lift a gram.</summary>
            public HashSet<int> couldTakeNothing = new HashSet<int>();
            /// <summary>Pawns whose pack could have taken something and whose plan came back EMPTY anyway —
            /// starvation, the failure the no-starvation floor exists to prevent. Goods no pack can lift, and goods
            /// locked inside another pawn's whole-stack reservation, are not starvation: the unclamped sweep cannot
            /// take those either, so they never reach this set.</summary>
            public HashSet<int> starved = new HashSet<int>();
            /// <summary>Claims handed back through <see cref="LoadLedger{TDef,TPawn}.Release"/>.</summary>
            public int releases;
            /// <summary>Trips the share rule made SMALLER than the pack allowed. The positive control: a sweep
            /// where this is zero never exercised the clamp at all and could only ever pass.</summary>
            public int clampedTrips;
            /// <summary>The run stopped making progress with goods still owed.</summary>
            public bool stalled;
            /// <summary>The sim at rest, for the end-state ledger assertions.</summary>
            public CrewSim sim;
        }

        /// <summary>
        /// Spread one order over several ground stacks of one def, so the sweep has to fill ACROSS piles rather than
        /// out of a single tidy one. The last pile takes the remainder, so the units always sum to
        /// <paramref name="total"/>.
        /// </summary>
        /// <param name="total">Units in the whole order; at least 1.</param>
        /// <param name="unitMass">Mass of one unit, kg — uniform, so the unit and mass views of a trip stay
        /// interchangeable and the closed-form trip optimum is meaningful.</param>
        /// <param name="stackCount">How many piles; more piles than units simply runs out early.</param>
        private static Stack[] SpreadIntoStacks(int total, float unitMass, int stackCount)
        {
            var stacks = new List<Stack>();
            int left = total;
            for (int i = 0; i < stackCount && left > 0; i++)
            {
                int units = (i == stackCount - 1) ? left : Math.Max(1, left / (stackCount - i));
                stacks.Add(new Stack("cargo", units, unitMass));
                left -= units;
            }
            return stacks.ToArray();
        }

        // The runtime's fair-share mass pre-pass: pool stacks of claimable defs counted up to the per-def claimable
        // units (decrementing, so over-supply cannot inflate the total), heaviest counted unit reported for the
        // floor. Stacks another pawn holds whole are skipped only when the scenario models that exclusivity.
        private static float ClaimableMass(CrewSim sim, Dictionary<string, int> available, int asker,
            bool perThingExclusive, out float heaviest)
        {
            heaviest = 0f;
            float total = 0f;
            var left = new Dictionary<string, int>(available);
            foreach (var s in sim.pool)
            {
                if (s.count <= 0)
                    continue;
                if (perThingExclusive && s.owner != NoOwner && s.owner != asker)
                    continue;
                if (!left.TryGetValue(s.def, out int rem) || rem <= 0)
                    continue;
                int units = Math.Min(s.count, rem);
                total += units * s.unitMass;
                left[s.def] = rem - units;
                if (s.unitMass > heaviest)
                    heaviest = s.unitMass;
            }
            return total;
        }

        // The runtime's sweep, reduced to the claim math: greedy in pool order (stands in for nearest-first), each
        // take clamped by DeliverableUnits under the remaining mass budget. Carry/CE clamps are held infinite so the
        // fairness clamp and the trip budget are the binding terms under test. `touched` is what the pawn would put
        // in its job's target queue, i.e. what it reserves whole under per-THING exclusivity.
        private static Dictionary<string, int> BuildPlan(CrewSim sim, Dictionary<string, int> available,
            float massBudget, int asker, bool perThingExclusive, List<Stack> touched, out int units)
        {
            var plan = new Dictionary<string, int>();
            var claimLeft = new Dictionary<string, int>(available);
            float massLeft = massBudget;
            units = 0;
            foreach (var s in sim.pool)
            {
                if (massLeft <= 0.0001f)
                    break;
                if (s.count <= 0)
                    continue;
                if (perThingExclusive && s.owner != NoOwner && s.owner != asker)
                    continue;
                if (!claimLeft.TryGetValue(s.def, out int avail) || avail <= 0)
                    continue;
                int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, s.unitMass, s.count);
                int take = TransportLoadPlan.DeliverableUnits(s.count, avail, avail, massAffordable);
                if (take <= 0)
                    continue;
                plan[s.def] = (plan.TryGetValue(s.def, out int cur) ? cur : 0) + take;
                claimLeft[s.def] = avail - take;
                massLeft -= take * s.unitMass;
                units += take;
                touched?.Add(s);
            }
            return plan;
        }

        // TryGiveBulkJob lowers its trip budget to the share ONLY when the share is smaller. Modelling the min is
        // the whole point: a share can shrink a trip, never grow one.
        private static float ClampedTripBudget(float share, float tripBudgetKg)
            => share < tripBudgetKg ? share : tripBudgetKg;

        // The end of a trip: the goods are in the container, so the ledger SETTLES them (needed, claimed and this
        // pawn's claim all drop, leaving it claimless for the next round) and the ground pool loses what was
        // carried away.
        private static void Deliver(CrewSim sim, int pawn, Dictionary<string, int> plan)
        {
            foreach (var kv in plan)
            {
                LoadLedger<string, int>.Settle(sim.needed, sim.claimed, sim.pawnClaims, pawn, kv.Key, kv.Value);
                int left = kv.Value;
                foreach (var s in sim.pool)
                {
                    if (left <= 0)
                        break;
                    if (s.def != kv.Key || s.count <= 0)
                        continue;
                    int taken = Math.Min(s.count, left);
                    s.count -= taken;
                    left -= taken;
                }
            }
            ReleaseStacksOwnedBy(sim, pawn);
        }

        /// <summary>Give back every stack this pawn had queued — on deposit or on interrupt, its job's target queue
        /// is gone either way, so the reservation another pawn was excluded by is gone with it.</summary>
        private static void ReleaseStacksOwnedBy(CrewSim sim, int pawn)
        {
            foreach (var s in sim.pool)
                if (s.owner == pawn)
                    s.owner = NoOwner;
        }

        /// <summary>
        /// Run one whole load to completion with a real crew and report every trip.
        ///
        /// <para>One ROUND is: each pawn not already carrying something asks, is clamped, sweeps and takes a claim;
        /// then everyone in flight deposits at once. Claims therefore overlap — which is the point, since a peer
        /// holding a claim leaves the divisor for the next asker, and the single-asker oracles cannot express
        /// that.</para>
        /// </summary>
        /// <param name="sim">The task; mutated throughout (needed shrinks, the pool empties).</param>
        /// <param name="scenario">Crew size, budgets, rule and the optional phantom/interrupt/exclusivity twists.</param>
        /// <returns>The trips, plus the per-pawn evidence the properties need. A run that stops while goods are
        /// still owed comes back with <see cref="LoadRun.stalled"/> set rather than looping — the caller's
        /// conservation check is what turns that into a readable failure.</returns>
        private static LoadRun RunCrewLoad(CrewSim sim, LoadScenario scenario)
        {
            var run = new LoadRun { sim = sim };
            var inFlight = new Dictionary<int, Dictionary<string, int>>();
            var crew = new List<int>();
            for (int p = 1; p <= scenario.crewSize; p++)
                crew.Add(p);

            int plansMade = 0;
            for (int round = 0; round < 4000; round++)
            {
                bool progressed = false;

                foreach (int pawn in crew)
                {
                    if (inFlight.ContainsKey(pawn))
                        continue;

                    var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
                    if (available.Count == 0)
                    {
                        run.ranOutOfWork.Add(pawn);
                        continue;
                    }

                    // The divisor the runtime would recount right now: the asker plus every OTHER crew member that
                    // holds no live claim (a claim holder's slice is already out of `available`), plus whatever
                    // phantoms this scenario is modelling.
                    var countedPeers = new List<int>();
                    foreach (int peer in crew)
                        if (peer != pawn && !sim.pawnClaims.ContainsKey(peer))
                            countedPeers.Add(peer);
                    int divisor = 1 + countedPeers.Count + scenario.phantomPeers;

                    float mass = ClaimableMass(sim, available, pawn, scenario.perThingExclusive, out float heaviest);
                    float share = scenario.rule(mass, heaviest, divisor, scenario.decisionBudgetKg);

                    var touched = new List<Stack>();
                    var plan = BuildPlan(sim, available, ClampedTripBudget(share, scenario.clampBudgetKg), pawn,
                        scenario.perThingExclusive, touched, out int units);
                    // The same sweep with the share removed: what this pack could really have carried this trip.
                    BuildPlan(sim, available, scenario.clampBudgetKg, pawn, scenario.perThingExclusive, null,
                        out int unclampedUnits);

                    if (units == 0)
                    {
                        // Nothing planned. Either there was genuinely nothing this pack could take (too heavy, or
                        // locked inside someone else's whole-stack reservation) or the clamp starved it, and only
                        // the second is a defect.
                        if (unclampedUnits > 0)
                            run.starved.Add(pawn);
                        else
                            run.couldTakeNothing.Add(pawn);
                        continue;
                    }
                    if (units < unclampedUnits)
                        run.clampedTrips++;

                    int availableUnits = 0;
                    foreach (var kv in available)
                        availableUnits += kv.Value;

                    var trip = new Trip
                    {
                        pawn = pawn,
                        units = units,
                        availableUnits = availableUnits,
                        packUnits = TransportLoadPlan.UnitsWithinMassBudget(scenario.clampBudgetKg, heaviest, int.MaxValue),
                        unclampedUnits = unclampedUnits,
                        divisor = divisor,
                        countedPeers = countedPeers,
                        remainderFittedOneTrip = scenario.decisionBudgetKg > 0f && mass <= scenario.decisionBudgetKg
                    };

                    LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, pawn, plan);
                    run.everClaimed.Add(pawn);
                    foreach (var s in touched)
                        s.owner = pawn;
                    plansMade++;
                    progressed = true;

                    // An interrupt is not a trip: the claim goes back and the goods stay owed.
                    if (scenario.interruptEveryNthPlan > 0 && plansMade % scenario.interruptEveryNthPlan == 0)
                    {
                        LoadLedger<string, int>.Release(sim.claimed, sim.pawnClaims, pawn);
                        ReleaseStacksOwnedBy(sim, pawn);
                        run.releases++;
                        continue;
                    }

                    run.trips.Add(trip);
                    inFlight[pawn] = plan;
                }

                var delivering = new List<int>(inFlight.Keys);
                foreach (int pawn in delivering)
                {
                    Deliver(sim, pawn, inFlight[pawn]);
                    inFlight.Remove(pawn);
                    run.deliveries[pawn] = (run.deliveries.TryGetValue(pawn, out int cur) ? cur : 0) + 1;
                    progressed = true;
                }

                if (sim.needed.Count == 0)
                    return run;
                if (!progressed)
                {
                    run.stalled = true;
                    return run;
                }
            }
            run.stalled = true;
            return run;
        }

        /*
            ──────────────────────────────────────────────────────────────────────
                                    The three properties
            ──────────────────────────────────────────────────────────────────────
            Each returns the violations it found instead of asserting, so one implementation serves both duties:
            proving the shipped rule clean, and proving a broken rule dirty. A property that convicts nothing is
            decoration, so every one of these is run against LegacyRuleV1190 (P2, P3) or StallingRule (P1) below.
        */

        /// <summary>
        /// P1 — CONSERVATION. Every unit of the order is delivered exactly once, the run terminates, and the ledger
        /// is left clean: nothing still needed, nothing still claimed, no pawn holding a slice.
        /// </summary>
        /// <param name="run">A finished run.</param>
        /// <param name="expectedUnits">Units the pool started with; the trips must sum to exactly this.</param>
        /// <returns>One readable line per violation; empty when the load conserved.</returns>
        private static List<string> ConservationViolations(LoadRun run, int expectedUnits)
        {
            var bad = new List<string>();
            int carried = 0;
            foreach (var t in run.trips)
                carried += t.units;

            if (run.stalled)
                bad.Add($"the run stalled with {expectedUnits - carried} of {expectedUnits} units still owed");
            if (carried != expectedUnits)
                bad.Add($"trips sum to {carried} units, order was {expectedUnits}");
            if (run.sim.needed.Count != 0)
                bad.Add($"{run.sim.needed.Count} def(s) still needed after the run");
            if (run.sim.claimed.Count != 0)
                bad.Add($"{run.sim.claimed.Count} def(s) still claimed after the run — a leaked claim");
            if (run.sim.pawnClaims.Count != 0)
                bad.Add($"{run.sim.pawnClaims.Count} pawn(s) still holding a claim after the run");
            if (run.starved.Count != 0)
                bad.Add($"pawn(s) {string.Join(",", run.starved)} had claimable work yet planned nothing");

            // The GROUND must agree with the ledger. Everything not yet carried away is still lying there, so
            // `onGround + carried == the order`. Without this the two halves of the simulation can drift apart in
            // the one direction nothing else notices — the ledger declaring units delivered while they are still on
            // the floor — and a harness that quietly leaves goods behind would grade the rule as clean.
            int onGround = 0;
            foreach (var s in run.sim.pool)
                onGround += s.count;
            if (onGround != expectedUnits - carried)
                bad.Add($"{onGround} unit(s) still on the ground but {carried} of {expectedUnits} were carried away");

            // totalClaimed == Σ pawnClaims must survive every claim, settle and release in the run.
            var recomputed = LoadLedger<string, int>.RecomputeClaimed(run.sim.pawnClaims);
            foreach (var kv in recomputed)
            {
                int scribed = run.sim.claimed.TryGetValue(kv.Key, out int c) ? c : 0;
                if (scribed != kv.Value)
                    bad.Add($"totalClaimed[{kv.Key}] is {scribed}, the pawns' own claims sum to {kv.Value}");
            }
            return bad;
        }

        /// <summary>
        /// P2 — FULLNESS UNLESS A CREW SPLIT EXPLAINS IT. The property both shipped regressions violated, in two
        /// clauses:
        ///
        /// <list type="bullet">
        /// <item>(a) unconditional: when everything the asker could claim already fitted inside ONE of its trips,
        /// that trip carries ALL of it. No crew, however large and however honest, may split a remainder that fits
        /// — splitting only converts one full trip into several partial ones. This is the clause the v1.19.0 rule
        /// fails, and it is issues #167 and #243 in a single sentence.</item>
        /// <item>(b) otherwise: a trip may come up short only against a GENUINE crew — the divisor must be exactly
        /// one plus the real peers counted (a padded divisor is #167's phantom mechs), and every one of those peers
        /// must have ENGAGED with this load (see <see cref="Engaged"/>). A counted peer that never did was never
        /// going to load, and every gram it took off a real hauler's trip was taken for nothing.</item>
        /// </list>
        ///
        /// <para>→ GOTCHA: fullness is measured against <see cref="Trip.unclampedUnits"/> — what the identical sweep
        /// would have taken with the share removed — not against the pack size. On a mixed-mass pool a pack can be
        /// left part-empty simply because the next item does not fit in the gap, which is the sweep's granularity,
        /// not the share's doing; measuring against the pack would convict the rule of that.</para>
        /// </summary>
        /// <param name="run">A finished run.</param>
        /// <returns>One readable line per offending trip; empty when every trip was full or honestly shared.</returns>
        private static List<string> FullnessViolations(LoadRun run)
        {
            var bad = new List<string>();
            foreach (var t in run.trips)
            {
                if (t.remainderFittedOneTrip && t.units < t.unclampedUnits)
                {
                    bad.Add($"pawn {t.pawn} carried {t.units} of the {t.availableUnits} left, which all fitted one " +
                        $"trip (divisor {t.divisor}) — a remainder that fits was split");
                    continue;
                }
                if (t.units >= t.unclampedUnits)
                    continue;

                // Short. Only a genuine crew excuses it.
                //
                // → NOTE: this first branch cannot fire against the shipped rule, and that is not an oversight. A
                // divisor of 1 or less returns the no-clamp sentinel, so a lone asker's trip is never shrunk and can
                // never be short. It is written out anyway so that a future rule which DID clamp a lone loader — the
                // one shape none of the historical bugs took — is convicted here instead of quietly excused by a
                // property that only knows how to think about crews.
                if (t.divisor <= 1)
                {
                    bad.Add($"pawn {t.pawn} carried {t.units} of a possible {t.unclampedUnits} with NO crew to share with");
                    continue;
                }
                if (t.divisor != 1 + t.countedPeers.Count)
                {
                    bad.Add($"pawn {t.pawn} carried {t.units} of a possible {t.unclampedUnits}, divided by " +
                        $"{t.divisor} where only {t.countedPeers.Count} real peer(s) existed — a padded divisor");
                    continue;
                }
                foreach (int peer in t.countedPeers)
                    if (!Engaged(run, peer))
                        bad.Add($"pawn {t.pawn} carried {t.units} of a possible {t.unclampedUnits} to make room for " +
                            $"pawn {peer}, which never loaded anything");
            }
            return bad;
        }

        /// <summary>
        /// Did this peer genuinely take part in the load — the P2 witness. Three ways, and each is a different
        /// honest answer to "where did the share I gave up actually go?":
        ///
        /// <list type="bullet">
        /// <item>it delivered a trip of its own — the plain case;</item>
        /// <item>it took a claim at some point, even if a draft handed the claim straight back. Committing to the
        /// manifest is what makes a peer real; an interrupt afterwards is the player's doing, not a phantom;</item>
        /// <item>it asked and found the manifest already empty — the crew simply finished first, which is what
        /// happens whenever a large crew shares a small order, and is the design working rather than failing.</item>
        /// </list>
        ///
        /// <para>→ GOTCHA: a peer that could take NOTHING (the goods outweigh its pack, or the only stack is locked
        /// whole inside another pawn's reservation) is deliberately NOT engaged, and running out LATER does not
        /// redeem it. Without that exclusion this witness could only ever pass: every load ends with the manifest
        /// empty, so every peer eventually asks and finds nothing, and a single run-level "it ran out at some point"
        /// flag would retroactively excuse every short trip in the whole run — including the ones taken while the
        /// peer was standing there unable to lift a gram. It cost a genuine finding to notice: with the flag
        /// unqualified, <see cref="OneMegaStack_ShrinksTheLoadersTrip_ForPeersThatCannotTouchIt"/> reported a clean
        /// bill of health for a run in which seven of eight pawns never touched the manifest.</para>
        /// </summary>
        /// <param name="run">A finished run.</param>
        /// <param name="peer">The counted co-loader whose reality is in question.</param>
        private static bool Engaged(LoadRun run, int peer)
        {
            if (run.deliveries.TryGetValue(peer, out int n) && n > 0)
                return true;
            if (run.everClaimed.Contains(peer))
                return true;
            return run.ranOutOfWork.Contains(peer) && !run.couldTakeNothing.Contains(peer);
        }

        /// <summary>
        /// P3 — TRIP-COUNT BOUND. An honest crew costs at most one partial trip per extra member:
        /// <c>trips ≤ ceil(pool / capacity) + crew − 1</c>. Never a multiplicative blow-up, which is what the
        /// reported decays were (issue #247: sixteen trips where one would do).
        /// </summary>
        /// <param name="run">A finished run.</param>
        /// <param name="totalUnits">Units the order started with.</param>
        /// <param name="packUnits">Units of this pool's uniform item that fit in one trip;
        /// <see cref="int.MaxValue"/> for an unbounded pack or a massless pool.</param>
        /// <param name="crewSize">Real askers.</param>
        /// <returns>One line when the bound is exceeded; empty otherwise.</returns>
        /// <remarks>Only meaningful for a UNIFORM-mass pool: with mixed masses <c>ceil(pool / capacity)</c> is not
        /// the achievable optimum (indivisible heavy items cannot be packed to a mass ratio), so the bound would
        /// convict a correct rule.</remarks>
        private static List<string> TripBoundViolations(LoadRun run, int totalUnits, int packUnits, int crewSize)
        {
            int bound = MinimumTrips(totalUnits, packUnits) + crewSize - 1;
            if (run.trips.Count <= bound)
                return new List<string>();
            return new List<string>
            {
                $"{run.trips.Count} trips for {totalUnits} units in packs of {packUnits} with a crew of " +
                $"{crewSize} — the bound is {bound}"
            };
        }

        /// <summary>Fewest trips the order could physically take: <c>ceil(total / pack)</c>, written so an unbounded
        /// pack (<see cref="int.MaxValue"/>) cannot overflow the ceiling arithmetic.</summary>
        private static int MinimumTrips(int totalUnits, int packUnits)
            => packUnits >= totalUnits ? 1 : (totalUnits + packUnits - 1) / packUnits;

        /*
            ──────────────────────────────────────────────────────────────────────
                            The sweep, and what it is allowed to see
            ──────────────────────────────────────────────────────────────────────
            Crews 1..8 × unit masses (including massless, and masses that divide a pack evenly and that do not) ×
            orders that do and do not fit one trip × one pile or three × three real pack sizes and three shapes of
            unbounded budget. 2,880 runs. The count is asserted, because a sweep that silently iterates nothing
            passes perfectly.
        */

        /// <summary>Crew sizes swept: one pawn through a full boarding party. The divisor follows from these.</summary>
        private static readonly int[] CrewSizes = { 1, 2, 3, 4, 5, 6, 7, 8 };

        /// <summary>Unit masses. 0 is a massless pool (no mass term can bound it); 0.25 and 1 divide the 5/9/35 kg
        /// packs evenly, 2 and 4 do not, so a part-full last unit is exercised. All are exact binary fractions, so
        /// no assertion here can fail on float drift rather than on behaviour.</summary>
        private static readonly float[] UnitMasses = { 0f, 0.25f, 1f, 2f, 4f };

        /// <summary>Order sizes, from "one item" up through orders far beyond any pack.</summary>
        private static readonly int[] OrderSizes = { 1, 2, 7, 33, 75, 200 };

        /// <summary>Piles the order is spread over, so the sweep has to fill across stacks as well as within one.</summary>
        private static readonly int[] StackLayouts = { 1, 3 };

        /// <summary>One budget pairing: what the share rule is told a trip is worth, and what actually bounds it.
        /// The unbounded pair is the runtime's own substitution — a real pack for the decision, the raw sentinel for
        /// the clamp — which is the divergence issue #243 lived in.</summary>
        private sealed class BudgetKind
        {
            public string name;
            public float decisionKg;
            public float clampKg;

            public BudgetKind(string name, float decisionKg, float clampKg)
            {
                this.name = name;
                this.decisionKg = decisionKg;
                this.clampKg = clampKg;
            }
        }

        private static readonly BudgetKind[] Budgets =
        {
            new BudgetKind("pack 5kg", 5f, 5f),
            new BudgetKind("pack 9kg", 9f, 9f),
            new BudgetKind("pack 35kg", 35f, 35f),
            new BudgetKind("no ceiling (float.MaxValue)", LoadFairShare.AskerTripBudgetKg(float.MaxValue, 35f), float.MaxValue),
            new BudgetKind("uncapped destination (infinity)", LoadFairShare.AskerTripBudgetKg(Inf, 35f), Inf),
            // The pre-v1.23.0 CALLER, simulated: the raw sentinel handed straight to the decision with no
            // substitution. The rule must still refuse to split — an asker with no bound fits everything.
            new BudgetKind("raw sentinel to the decision", float.MaxValue, float.MaxValue)
        };

        /// <summary>
        /// Runs the sweep produces: 8 crew sizes × 5 unit masses × 6 order sizes × 2 pile layouts × 6 budget kinds.
        /// Written as literal factors rather than as <c>CrewSizes.Length * …</c> ON PURPOSE — derived from the
        /// arrays it would follow any edit to them silently, and the whole point of asserting it is that a sweep
        /// which quietly iterates fewer cases (or none at all) passes perfectly.
        /// </summary>
        private const int SweptRuns = 8 * 5 * 6 * 2 * 6;

        /// <summary>
        /// Drive every swept case through <paramref name="rule"/> and collect the violations of all three
        /// properties, keeping them apart so a caller can say WHICH property convicted a broken rule.
        /// </summary>
        /// <param name="rule">The share decision to test.</param>
        /// <param name="phantomPeers">Divisor padding to model (0 for an honest run).</param>
        /// <returns>The tallies, plus the first few violation lines of each kind for a readable failure.</returns>
        private static SweepResult SweepAllAxes(ShareRule rule, int phantomPeers = 0)
        {
            var result = new SweepResult();
            foreach (int crew in CrewSizes)
                foreach (float unitMass in UnitMasses)
                    foreach (int order in OrderSizes)
                        foreach (int layout in StackLayouts)
                            foreach (var budget in Budgets)
                            {
                                var sim = CrewSim.FromPool(SpreadIntoStacks(order, unitMass, layout));
                                var run = RunCrewLoad(sim, new LoadScenario
                                {
                                    crewSize = crew,
                                    phantomPeers = phantomPeers,
                                    decisionBudgetKg = budget.decisionKg,
                                    clampBudgetKg = budget.clampKg,
                                    rule = rule
                                });

                                string where = $"crew {crew}, {order} x {unitMass}kg over {layout} pile(s), {budget.name}";
                                int packUnits = TransportLoadPlan.UnitsWithinMassBudget(budget.clampKg, unitMass, int.MaxValue);

                                result.runs++;
                                result.trips += run.trips.Count;
                                result.clampedTrips += run.clampedTrips;
                                result.conservation.Add(ConservationViolations(run, order), where);
                                result.fullness.Add(FullnessViolations(run), where);
                                result.tripBound.Add(TripBoundViolations(run, order, packUnits, crew), where);

                                // Harness self-check: on a uniform pool "what the pack could have taken" must equal
                                // the closed form min(pack, remainder). If those ever disagree, the fullness measure
                                // has drifted from the thing a reader would count, and P2 is no longer the property
                                // its own doc-comment claims.
                                foreach (var t in run.trips)
                                    if (t.unclampedUnits != Math.Min(t.packUnits, t.availableUnits))
                                        result.harness.Add(new List<string>
                                        {
                                            $"pack could take {t.unclampedUnits} but min(pack {t.packUnits}, " +
                                            $"remainder {t.availableUnits}) says {Math.Min(t.packUnits, t.availableUnits)}"
                                        }, where);
                            }
            return result;
        }

        /// <summary>
        /// One property's verdict over a whole sweep: how many violations there really were, plus the first few
        /// verbatim. The samples are capped and the COUNT is not, and the asymmetry is the point — a broken rule
        /// produces tens of thousands of lines, and an unreadable failure is barely better than none, but a count
        /// truncated to the sample cap would quietly understate how broken the rule is, and any assertion phrased
        /// against it would be measuring the cap instead of the code.
        /// </summary>
        private sealed class ViolationBucket
        {
            /// <summary>Every violation found across the sweep.</summary>
            public int count;
            /// <summary>The first few, for a failure message that names a concrete case.</summary>
            public List<string> samples = new List<string>();

            /// <summary>Fold one case's violations in.</summary>
            /// <param name="found">What the property returned for this case.</param>
            /// <param name="where">The case, as a reader would describe it.</param>
            public void Add(List<string> found, string where)
            {
                foreach (var line in found)
                {
                    count++;
                    if (samples.Count < 20)
                        samples.Add($"{where}: {line}");
                }
            }

            /// <summary>The count and the samples as one assertion message.</summary>
            /// <param name="what">The property's name, for the reader.</param>
            public string Describe(string what)
                => count == 0 ? $"{what}: no violations" : $"{what}: {count} violation(s), e.g.\n  " + string.Join("\n  ", samples);
        }

        /// <summary>Tallies from one whole sweep.</summary>
        private sealed class SweepResult
        {
            /// <summary>Whole loads simulated.</summary>
            public int runs;
            /// <summary>Trips inspected across them.</summary>
            public int trips;
            /// <summary>Trips the share rule made smaller than the pack allowed — the sweep's positive control.</summary>
            public int clampedTrips;
            /// <summary>P1.</summary>
            public ViolationBucket conservation = new ViolationBucket();
            /// <summary>P2.</summary>
            public ViolationBucket fullness = new ViolationBucket();
            /// <summary>P3.</summary>
            public ViolationBucket tripBound = new ViolationBucket();
            /// <summary>The harness disagreeing with itself, not the rule misbehaving.</summary>
            public ViolationBucket harness = new ViolationBucket();
        }

        // ============ P1/P2/P3 against the shipped rule ============

        /// <summary>
        /// The exit criterion, as one sweep: the shipped rule satisfies all three properties over crews 1..8 crossed
        /// with bounded and both unbounded budgets, divisors 1..8, orders that do and do not fit one trip, unit
        /// masses that divide a pack evenly and that do not, and a massless pool.
        ///
        /// <para>The tallies are asserted as well as the violations. A sweep whose axes were mis-edited into
        /// iterating nothing would report zero violations and pass perfectly; and a sweep in which the share never
        /// actually clamped a trip would never exercise the branch under test, so
        /// <see cref="SweepResult.clampedTrips"/> is a positive control, not a statistic. Measured at the time of
        /// writing: 2,880 runs, 18,146 trips, 2,010 of them shrunk by the share.</para>
        /// </summary>
        [Test]
        public void AllThreeProperties_HoldForTheShippedRule_AcrossEveryAxis()
        {
            var swept = SweepAllAxes(CurrentRule);

            Assert.That(swept.runs, Is.EqualTo(SweptRuns), "the sweep did not run the cases it claims to");
            Assert.That(swept.trips, Is.GreaterThan(10000), "the sweep inspected implausibly few trips");
            Assert.That(swept.clampedTrips, Is.GreaterThan(500),
                "no trip was ever shrunk by the share — the branch this whole fixture exists to police was never taken");

            Assert.That(swept.harness.count, Is.Zero, swept.harness.Describe("the fullness measure disagrees with min(pack, remainder)"));
            Assert.That(swept.conservation.count, Is.Zero, swept.conservation.Describe("P1 conservation"));
            Assert.That(swept.fullness.count, Is.Zero, swept.fullness.Describe("P2 fullness unless a crew split explains it"));
            Assert.That(swept.tripBound.count, Is.Zero, swept.tripBound.Describe("P3 trip-count bound"));
        }

        // ============ N real askers, legibly (the gap the report calls the biggest) ============

        /// <summary>
        /// A crew of four ordered aboard, 200 units of 1 kg, 40 kg packs — four pawns genuinely interleaving trips
        /// against one shrinking pool, which no oracle covered before. Every trip is full and the order takes
        /// exactly the five trips one pawn with the same pack would have needed: an honest crew of four costs
        /// nothing at all here, because the divisor drops as each peer takes its claim.
        /// </summary>
        [Test]
        public void FourRealAskers_InterleaveTrips_AndEveryTripIsFull()
        {
            var sim = CrewSim.FromPool(new Stack("steel", 200, 1f));
            var run = RunCrewLoad(sim, new LoadScenario { crewSize = 4, decisionBudgetKg = 40f, clampBudgetKg = 40f });

            Assert.That(ConservationViolations(run, 200), Is.Empty);
            Assert.That(FullnessViolations(run), Is.Empty);
            Assert.That(run.trips.Count, Is.EqualTo(5), "ceil(200 / 40) trips — a real crew must not cost extra ones");
            foreach (var t in run.trips)
                Assert.That(t.units, Is.EqualTo(Math.Min(40, t.availableUnits)), "a trip came up short");
            Assert.That(run.deliveries.Count, Is.EqualTo(4), "every pawn of the crew actually carried something");
        }

        /// <summary>
        /// The same order that issue #167 reported (33 units, a pack that holds 9) but with THREE real haulers
        /// instead of one hauler and two mechs that cannot haul. The trips are 9, 9, 9, 6 — identical to the
        /// single-hauler run in <see cref="LoadFairShareTests.SingleEffectiveHauler_EveryTripIsFull_UntilOrderMet"/>,
        /// which is the point: sharing between pawns that really load costs no trips, while dividing for pawns that
        /// do not cost the reporter six extra.
        /// </summary>
        [Test]
        public void ThreeRealHaulers_CostExactlyTheSameTripsAsOne()
        {
            var sim = CrewSim.FromPool(new Stack("steel", 33, 1f));
            var run = RunCrewLoad(sim, new LoadScenario { crewSize = 3, decisionBudgetKg = 9f, clampBudgetKg = 9f });

            var carried = new List<int>();
            foreach (var t in run.trips)
                carried.Add(t.units);

            Assert.That(carried, Is.EqualTo(new[] { 9, 9, 9, 6 }));
            Assert.That(ConservationViolations(run, 33), Is.Empty);
            Assert.That(FullnessViolations(run), Is.Empty);
            Assert.That(TripBoundViolations(run, 33, 9, 3), Is.Empty);
        }

        /// <summary>
        /// The cave-exit crew (issue #243) with all four pawns really asking, and the two budgets the runtime
        /// actually feeds: a substituted full pack for the decision, the raw sentinel for the clamp. Whichever pawn
        /// asks first clears the whole order, and the rest correctly find nothing left — a pack with no ceiling
        /// makes the crew irrelevant.
        /// </summary>
        [Test]
        public void CaveExitCrew_WithNoCarryCeiling_ClearsTheOrderInOneTrip()
        {
            foreach (float unbounded in new[] { float.MaxValue, Inf })
            {
                var sim = CrewSim.FromPool(new Stack("jelly", 200, 0.025f));
                var run = RunCrewLoad(sim, new LoadScenario
                {
                    crewSize = 4,
                    decisionBudgetKg = LoadFairShare.AskerTripBudgetKg(unbounded, 35f),
                    clampBudgetKg = unbounded
                });

                Assert.That(run.trips.Count, Is.EqualTo(1), $"budget {unbounded}: the order was shuttled");
                Assert.That(run.trips[0].units, Is.EqualTo(200));
                Assert.That(ConservationViolations(run, 200), Is.Empty);
                Assert.That(FullnessViolations(run), Is.Empty);
            }
        }

        /// <summary>
        /// One heavy item among many light ones — the shape the no-starvation floor exists for. The floor lifts the
        /// share to the sculpture's own mass, so the first asker can take it; the rest of the pool then fits one
        /// trip and goes whole. Nobody stalls waiting for a share smaller than the item they are standing next to.
        /// </summary>
        [Test]
        public void MixedPool_OneHeavyItemAmongManyLight_IsNeverStarvedOrStalled()
        {
            foreach (int crew in CrewSizes)
            {
                var sim = CrewSim.FromPool(new Stack("sculpture", 1, 12f), new Stack("jelly", 200, 0.025f));
                var run = RunCrewLoad(sim, new LoadScenario
                {
                    crewSize = crew,
                    decisionBudgetKg = 15f,
                    clampBudgetKg = 15f
                });

                Assert.That(ConservationViolations(run, 201), Is.Empty, $"crew {crew}");
                Assert.That(FullnessViolations(run), Is.Empty, $"crew {crew}");
                Assert.That(run.trips.Count, Is.LessThanOrEqualTo(2 + crew), $"crew {crew}: too many trips");
            }
        }

        /// <summary>
        /// A 12 kg sculpture in a 9 kg pack: no pawn can ever carry it. The rest of the order must still move, the
        /// run must stop rather than spin, and no claim may be left behind on the item nobody can lift.
        ///
        /// <para>→ NOTE: this is the one shape where <see cref="ConservationViolations"/> is expected to speak, so
        /// it is asserted case by case instead — an unliftable item is a physical fact, not a rule defect, and a
        /// conservation property that pretended otherwise would be convicting the wrong thing.</para>
        /// </summary>
        [Test]
        public void AnItemNoPackCanLift_IsLeftBehindWithoutStallingTheRest()
        {
            var sim = CrewSim.FromPool(new Stack("sculpture", 1, 12f), new Stack("jelly", 200, 0.025f));
            var run = RunCrewLoad(sim, new LoadScenario { crewSize = 4, decisionBudgetKg = 9f, clampBudgetKg = 9f });

            int carried = 0;
            foreach (var t in run.trips)
                carried += t.units;

            Assert.That(carried, Is.EqualTo(200), "everything liftable must still be delivered");
            Assert.That(sim.needed.ContainsKey("sculpture"), Is.True, "the unliftable item stays owed");
            Assert.That(sim.needed.ContainsKey("jelly"), Is.False, "the liftable goods were all delivered");
            Assert.That(sim.claimed, Is.Empty, "no claim may be left standing on goods nobody can carry");
            Assert.That(sim.pawnClaims, Is.Empty);
        }

        // ============ Negative controls: the properties must convict a broken rule ============

        /// <summary>
        /// Fidelity of the reconstruction, before it is used to grade anything. Replaying issue #247's own scenario
        /// through <see cref="LegacyRuleV1190"/> — 75 units at 0.25 kg, one hauler mech, a divisor padded to four by
        /// bystanders, the reporter's 25.99 kg Lifter ceiling — must give back the exact trip sequence the report
        /// reconstructed from the reporter's attached log, whose first three terms (18, 14, 10 at 4.5 / 3.5 / 2.5 kg)
        /// are what the player counted in game.
        ///
        /// <para>Without this, "the properties reject the v1.19.0 rule" would only mean they reject whatever this
        /// file happens to have written down.</para>
        /// </summary>
        [Test]
        public void LegacyRule_ReproducesTheReportedTripsExactly()
        {
            var sim = CrewSim.FromPool(new Stack("jelly", 75, 0.25f));
            var run = RunCrewLoad(sim, new LoadScenario
            {
                crewSize = 1,
                phantomPeers = 3,
                decisionBudgetKg = 25.99f,
                clampBudgetKg = 25.99f,
                rule = LegacyRuleV1190
            });

            var carried = new List<int>();
            foreach (var t in run.trips)
                carried.Add(t.units);

            Assert.That(carried, Is.EqualTo(new[] { 18, 14, 10, 8, 6, 4, 3, 3, 2, 1, 1, 1, 1, 1, 1, 1 }),
                "the reconstructed v1.19.0 rule no longer reproduces the reported decay");
            Assert.That(carried.Count, Is.EqualTo(16), "sixteen trips, where the shipped rule takes one");

            // And the shipped rule on the identical scenario, so the contrast is stated here rather than inferred.
            var fixedSim = CrewSim.FromPool(new Stack("jelly", 75, 0.25f));
            var fixedRun = RunCrewLoad(fixedSim, new LoadScenario
            {
                crewSize = 1,
                phantomPeers = 3,
                decisionBudgetKg = 25.99f,
                clampBudgetKg = 25.99f
            });
            Assert.That(fixedRun.trips.Count, Is.EqualTo(1), "the shipped rule clears 18.75kg in one 25.99kg trip");
            Assert.That(fixedRun.trips[0].units, Is.EqualTo(75));
        }

        /// <summary>
        /// P2 convicts the v1.19.0 rule, and names the reason: it split a remainder that already fitted one trip.
        /// P3 convicts it too, on the same scenario, by sixteen trips against a bound of one.
        ///
        /// <para>P1 does NOT convict it, and that is worth stating: the legacy rule delivered every one of the 75
        /// units exactly once. Conservation is a different failure class — see
        /// <see cref="ConservationRejects_ARuleThatClampsEveryClaimToNothing"/> for the mutant it does catch. A
        /// property is only worth its lines if something falsifies it, and these three are falsified by different
        /// things.</para>
        /// </summary>
        [Test]
        public void FullnessAndTripBound_RejectTheV1190Rule_OnTheReportedScenario()
        {
            var sim = CrewSim.FromPool(new Stack("jelly", 75, 0.25f));
            var run = RunCrewLoad(sim, new LoadScenario
            {
                crewSize = 1,
                phantomPeers = 3,
                decisionBudgetKg = 25.99f,
                clampBudgetKg = 25.99f,
                rule = LegacyRuleV1190
            });

            var fullness = FullnessViolations(run);
            Assert.That(fullness, Is.Not.Empty, "P2 failed to notice a rule that shipped this exact bug twice");
            Assert.That(fullness[0], Does.Contain("a remainder that fits was split"),
                "P2 must convict it for the right reason — the fit-in-one-trip clause");

            // 103 units of 0.25kg fit a 25.99kg trip, so one trip is the whole order; the legacy rule took sixteen.
            Assert.That(TripBoundViolations(run, 75, 103, 1), Is.Not.Empty, "P3 failed to notice sixteen trips for one");

            Assert.That(ConservationViolations(run, 75), Is.Empty,
                "P1 is not the discriminator here — the legacy rule lost nothing, it just took forever");
        }

        /// <summary>
        /// P2 convicts the v1.19.0 rule across the whole sweep too, not only on the one reported scenario — 3,094
        /// offending trips at the time of writing. Run with an honest crew and no phantoms, so nothing but the rule
        /// itself is broken.
        ///
        /// <para>P3 is deliberately NOT asserted here, because it convicts the legacy rule ZERO times on this
        /// sweep. With a real crew the divisor falls as each peer takes its claim, so the legacy decay is largely
        /// absorbed, and the <c>+ crew − 1</c> slack covers what is left. That is a true fact about the bound rather
        /// than a hole in it: P3's teeth are on the single-hauler scenario above (sixteen trips against a bound of
        /// one) and on a padded divisor below, where the slack is small. Stating it here stops the next reader from
        /// assuming this sweep covers P3 against the legacy rule.</para>
        /// </summary>
        [Test]
        public void Fullness_RejectsTheV1190Rule_AcrossTheWholeSweep()
        {
            var swept = SweepAllAxes(LegacyRuleV1190);

            Assert.That(swept.runs, Is.EqualTo(SweptRuns));
            Assert.That(swept.fullness.count, Is.GreaterThan(1000),
                "P2 caught few or none of the legacy rule's short trips — check it is measuring what it claims");
            Assert.That(swept.conservation.count, Is.Zero,
                swept.conservation.Describe("the legacy rule still moved every unit exactly once"));
        }

        /// <summary>
        /// P1's own discriminator: a rule that clamps every claim to nothing. Every run stalls with the whole order
        /// still owed, which conservation catches and neither of the other two properties would — a load that never
        /// starts has no short trip and no excess trip to convict.
        /// </summary>
        [Test]
        public void ConservationRejects_ARuleThatClampsEveryClaimToNothing()
        {
            var swept = SweepAllAxes(StallingRule);

            Assert.That(swept.runs, Is.EqualTo(SweptRuns));
            Assert.That(swept.trips, Is.EqualTo(0), "a zero share must plan nothing at all");
            Assert.That(swept.conservation.count, Is.GreaterThan(SweptRuns), "P1 accepted a load that never moved a unit");
            Assert.That(swept.conservation.samples[0], Does.Contain("stalled"));
            Assert.That(swept.fullness.count, Is.Zero, "P2 cannot see this one: there are no trips to judge");
            Assert.That(swept.tripBound.count, Is.Zero, "P3 cannot see this one either");
        }

        /// <summary>
        /// P2's witness clause, tested as the thing it exists for: the SHIPPED rule, fed a divisor padded with peers
        /// that never load (issue #167's constructoid and cleansweeper). The rule behaves correctly given its
        /// argument — that is exactly why the unit contract could not catch #167 — so the only signal is that trips
        /// came up short for peers that delivered nothing, and P2 must say so.
        ///
        /// <para>Measured at the time of writing: 6,308 short trips, every one of them convicted, and 536 runs over
        /// the trip bound. This is what <see cref="LoadFairShare.CountsAsCoLoader"/> is protecting, expressed at the
        /// multi-trip level rather than as a predicate truth table.</para>
        /// </summary>
        [Test]
        public void Fullness_RejectsAPaddedDivisor_EvenWithTheShippedRule()
        {
            var swept = SweepAllAxes(CurrentRule, phantomPeers: 3);

            Assert.That(swept.runs, Is.EqualTo(SweptRuns));
            Assert.That(swept.clampedTrips, Is.GreaterThan(1000), "the padding must actually be shrinking trips");
            Assert.That(swept.fullness.count, Is.GreaterThan(1000), "P2 accepted trips shrunk for pawns that never loaded");
            Assert.That(swept.fullness.samples[0], Does.Contain("a padded divisor"));
            Assert.That(swept.tripBound.count, Is.GreaterThan(100), "P3 accepted the extra trips a padded divisor costs");
            Assert.That(swept.conservation.count, Is.Zero,
                swept.conservation.Describe("a padded divisor wastes trips; it loses nothing"));
        }

        /// <summary>
        /// Mutation testing, one clause at a time: remove exactly one branch of
        /// <see cref="LoadFairShare.ShareMassBudget"/>, leave the rest identical, run the whole sweep, and record
        /// not merely THAT the oracle noticed but WHICH property did. A property that no surgical break falsifies is
        /// decoration, and a mutant killed by "something, somewhere" teaches nothing.
        ///
        /// <list type="bullet">
        /// <item><b>fit-in-one-trip short-circuit</b> → P2 convicts it on 2,150 trips, for splitting a remainder that
        /// fitted. That is issues #167 and #243 in one clause.</item>
        /// <item><b>no-starvation floor</b> → P1 convicts it 288 times: the sweep contains orders smaller than the
        /// crew (two 4 kg items split eight ways), where a floorless share buys less than one item, every pawn plans
        /// nothing and the load stalls with goods still on the ground. P2 cannot see it — a trip that never happens
        /// is not a short trip.</item>
        /// <item><b>unbounded-asker guard</b> → NOTHING convicts it, and that is the finding. For any finite pool
        /// the clause below it reaches the same answer: an <c>askerTripBudgetKg</c> of
        /// <see cref="float.MaxValue"/> or infinity is positive, and every real claimable mass is <c>&lt;=</c> it,
        /// so the fit-in-one-trip short-circuit already returns "no clamp". The guard is a statement of intent and a
        /// NaN-safety boundary (its own doc-comment calls it a fail-safe against a caller bug), not separately
        /// observable behaviour. Asserted here so no future reader mistakes this file for coverage of it — if the
        /// two clauses are ever reordered or the second is narrowed, this assertion flips and says so.</item>
        /// </list>
        /// </summary>
        [Test]
        public void EachClauseOfTheShippedRule_IsLoadBearing_ExceptTheOneThatIsNot()
        {
            var noShortCircuit = SweepAllAxes(RuleWithoutTheFitInOneTripShortCircuit);
            Assert.That(noShortCircuit.runs, Is.EqualTo(SweptRuns));
            Assert.That(noShortCircuit.fullness.count, Is.GreaterThan(1000),
                "deleting the fit-in-one-trip short-circuit must be visible — it is the whole of the reported bug");
            Assert.That(noShortCircuit.fullness.samples[0], Does.Contain("a remainder that fits was split"));
            Assert.That(noShortCircuit.conservation.count, Is.Zero, "it wastes trips; it loses nothing");

            var noFloor = SweepAllAxes(RuleWithoutTheStarvationFloor);
            Assert.That(noFloor.runs, Is.EqualTo(SweptRuns));
            Assert.That(noFloor.conservation.count, Is.GreaterThan(100),
                "deleting the no-starvation floor must be visible — a share below one item stalls the load");
            Assert.That(string.Join(" | ", noFloor.conservation.samples), Does.Contain("planned nothing"),
                "and it must be convicted for STARVATION, not for some unrelated symptom");

            var noUnboundedGuard = SweepAllAxes(RuleWithoutTheUnboundedGuard);
            Assert.That(noUnboundedGuard.runs, Is.EqualTo(SweptRuns));
            Assert.That(noUnboundedGuard.conservation.count, Is.Zero);
            Assert.That(noUnboundedGuard.fullness.count, Is.Zero);
            Assert.That(noUnboundedGuard.tripBound.count, Is.Zero);
            Assert.That(noUnboundedGuard.trips, Is.EqualTo(SweepAllAxes(CurrentRule).trips),
                "the unbounded guard is subsumed by the fit-in-one-trip clause for every finite pool: removing it " +
                "changes not one trip. This oracle does NOT cover that branch, and says so rather than implying it");
        }

        /*
            ──────────────────────────────────────────────────────────────────────
                            Interrupts — the Release path (report §8, gap 2)
            ──────────────────────────────────────────────────────────────────────
            A pawn that claims and is then drafted, downed or cancelled must hand its slice back. Release differs
            from Settle in exactly one way that matters here: it does NOT touch `needed`, because an interrupt is
            not progress — the goods never reached the container and are still owed. No multi-trip oracle exercised
            it before, so the divisor recount that follows an interrupt was unpinned too.
        */

        /// <summary>
        /// An interrupt, step by step, with every number checked. Crew of three, 100 units of 1 kg, 40 kg packs:
        /// pawn 1 takes a third, pawn 2 takes half of what is left (the divisor is 2 now — pawn 1 holds a claim and
        /// is out of it), then pawn 2 is drafted before delivering.
        ///
        /// <para>Three separate facts fall out, and the runtime depends on all three: the units come BACK (pawn 3
        /// sees 67 again, not 34); <c>needed</c> is untouched by the interrupt, so the order is still for 100; and
        /// the drafted pawn leaves the divisor, so pawn 3 — now genuinely alone — is not clamped at all.</para>
        /// </summary>
        [Test]
        public void DraftedMidWalk_HandsTheClaimBack_AndLeavesTheDivisor()
        {
            var sim = CrewSim.FromPool(new Stack("steel", 100, 1f));

            var first = PlanOneTrip(sim, pawn: 1, countedPeers: 2, tripBudgetKg: 40f);
            Assert.That(first.divisor, Is.EqualTo(3), "three claimless pawns, three ways");
            Assert.That(first.units, Is.EqualTo(33), "100kg split three ways, clamped by nothing smaller");

            var second = PlanOneTrip(sim, pawn: 2, countedPeers: 1, tripBudgetKg: 40f);
            Assert.That(second.availableUnits, Is.EqualTo(67), "pawn 1's claim is already out of the pool");
            Assert.That(second.divisor, Is.EqualTo(2), "a claim holder is not counted — it has had its share");
            Assert.That(second.units, Is.EqualTo(33), "67kg split two ways is 33.5, and a unit is 1kg");

            Assert.That(sim.claimed["steel"], Is.EqualTo(66));
            Assert.That(sim.needed["steel"], Is.EqualTo(100), "nothing has been deposited yet");

            // Drafted mid-walk: the whole claim goes back.
            LoadLedger<string, int>.Release(sim.claimed, sim.pawnClaims, 2);

            Assert.That(sim.claimed["steel"], Is.EqualTo(33), "only pawn 1's claim survives");
            Assert.That(sim.needed["steel"], Is.EqualTo(100), "an interrupt is not progress — the goods are still owed");
            Assert.That(sim.pawnClaims.ContainsKey(2), Is.False);

            // Pawn 3 asks with pawn 2 now drafted, so CountsAsCoLoader rejects it and the divisor is 1.
            Assert.That(LoadFairShare.CountsAsCoLoader(
                isBoardingPassengerOfThisLoadable: true,
                canDoHaulingWorkType: true,
                hasClaimableWork: true,
                downed: false,
                drafted: true,
                inMentalState: false,
                capableOfManipulation: true,
                hasCarrierComp: true), Is.False, "a drafted pawn must not count toward anyone's divisor");

            var third = PlanOneTrip(sim, pawn: 3, countedPeers: 0, tripBudgetKg: 40f);
            Assert.That(third.availableUnits, Is.EqualTo(67), "the released units are available to OTHER pawns too");
            Assert.That(third.units, Is.EqualTo(40), "alone in the divisor, pawn 3 fills its pack");
        }

        /// <summary>
        /// Release is idempotent and total: releasing a pawn that holds nothing changes nothing, and releasing
        /// twice does not double-subtract. The runtime calls it from several lifecycle seams (job end, despawn, the
        /// load-time self-heal), so a second call is ordinary, not exceptional.
        /// </summary>
        [Test]
        public void ReleasingAPawnTwice_IsANoOpTheSecondTime()
        {
            var sim = CrewSim.FromPool(new Stack("steel", 100, 1f));
            PlanOneTrip(sim, pawn: 1, countedPeers: 1, tripBudgetKg: 40f);
            int claimedOnce = sim.claimed["steel"];
            Assert.That(claimedOnce, Is.GreaterThan(0));

            LoadLedger<string, int>.Release(sim.claimed, sim.pawnClaims, 1);
            Assert.That(sim.claimed.ContainsKey("steel"), Is.False);
            LoadLedger<string, int>.Release(sim.claimed, sim.pawnClaims, 1);
            LoadLedger<string, int>.Release(sim.claimed, sim.pawnClaims, 99);
            Assert.That(sim.claimed.ContainsKey("steel"), Is.False, "a second release must not subtract again");
            Assert.That(sim.needed["steel"], Is.EqualTo(100));
        }

        /// <summary>
        /// The properties, swept again with a crew that keeps being interrupted — every second, third or fifth plan
        /// is drafted away instead of delivered. Conservation must survive it exactly: an interrupted claim comes
        /// back in full, so the order still completes and the ledger still ends clean, however many times the crew
        /// is pulled off the job.
        ///
        /// <para>Fullness is checked too, and holds — with the witness reading an interrupted peer as engaged, which
        /// it is: it committed to the manifest and a draft took the claim back. The trip BOUND is deliberately NOT
        /// asserted, because an interrupt genuinely costs a re-plan and nothing here claims otherwise. Measured at
        /// the time of writing: 648 runs, 5,480 trips, 2,803 interrupts.</para>
        /// </summary>
        [Test]
        public void Interrupts_NeverLoseAUnit_HoweverOftenTheCrewIsPulledOff()
        {
            int runs = 0, trips = 0, releases = 0;
            var conservation = new List<string>();
            var fullness = new List<string>();

            foreach (int interruptEvery in new[] { 2, 3, 5 })
                foreach (int crew in new[] { 2, 4, 8 })
                    foreach (float unitMass in new[] { 0f, 0.25f, 1f, 4f })
                        foreach (int order in new[] { 7, 33, 200 })
                            foreach (var budget in Budgets)
                            {
                                var sim = CrewSim.FromPool(SpreadIntoStacks(order, unitMass, 2));
                                var run = RunCrewLoad(sim, new LoadScenario
                                {
                                    crewSize = crew,
                                    decisionBudgetKg = budget.decisionKg,
                                    clampBudgetKg = budget.clampKg,
                                    interruptEveryNthPlan = interruptEvery
                                });

                                runs++;
                                trips += run.trips.Count;
                                releases += run.releases;
                                string where = $"interrupt every {interruptEvery}, crew {crew}, {order} x {unitMass}kg, {budget.name}";
                                foreach (var line in ConservationViolations(run, order))
                                    conservation.Add($"{where}: {line}");
                                foreach (var line in FullnessViolations(run))
                                    fullness.Add($"{where}: {line}");
                            }

            // 3 interrupt rates × 3 crew sizes × 4 unit masses × 3 order sizes × every budget kind.
            Assert.That(runs, Is.EqualTo(3 * 3 * 4 * 3 * Budgets.Length),
                "the interrupt sweep did not run the cases it claims to");
            Assert.That(releases, Is.GreaterThan(500),
                "hardly anything was ever interrupted — the Release path is not really being exercised");
            Assert.That(trips, Is.GreaterThan(1000));
            Assert.That(conservation, Is.Empty, "an interrupted claim lost or duplicated units");
            Assert.That(fullness, Is.Empty, "a trip came up short after an interrupt, with no crew to explain it");
        }

        /// <summary>Plan and claim ONE trip for one pawn against a stated number of counted peers, without
        /// delivering it — the step-by-step form the interrupt tests need. Mirrors one iteration of
        /// <see cref="RunCrewLoad"/>'s inner loop.</summary>
        /// <param name="sim">The task; its ledger is updated with the new claim.</param>
        /// <param name="pawn">Who is asking.</param>
        /// <param name="countedPeers">How many other pawns the runtime would count into the divisor right now.</param>
        /// <param name="tripBudgetKg">The asker's own per-trip mass budget, used for both the decision and the clamp.</param>
        private static Trip PlanOneTrip(CrewSim sim, int pawn, int countedPeers, float tripBudgetKg)
        {
            var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
            float mass = ClaimableMass(sim, available, pawn, false, out float heaviest);
            int divisor = 1 + countedPeers;
            float share = LoadFairShare.ShareMassBudget(mass, heaviest, divisor, tripBudgetKg);
            var plan = BuildPlan(sim, available, ClampedTripBudget(share, tripBudgetKg), pawn, false, null, out int units);
            BuildPlan(sim, available, tripBudgetKg, pawn, false, null, out int unclampedUnits);
            LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, pawn, plan);

            int availableUnits = 0;
            foreach (var kv in available)
                availableUnits += kv.Value;
            return new Trip
            {
                pawn = pawn,
                units = units,
                availableUnits = availableUnits,
                packUnits = TransportLoadPlan.UnitsWithinMassBudget(tripBudgetKg, heaviest, int.MaxValue),
                unclampedUnits = unclampedUnits,
                divisor = divisor,
                countedPeers = new List<int>(),
                remainderFittedOneTrip = mass <= tripBudgetKg
            };
        }

        /*
            ──────────────────────────────────────────────────────────────────────
                    Per-THING granularity — one stack, many askers (report §8, gap 4)
            ──────────────────────────────────────────────────────────────────────
            The ledger claims UNITS per def, but the driver reserves whole THINGS: `pawn.Reserve(stack, job, 1, -1)`
            takes the entire stack, exactly as vanilla's own JobDriver_HaulToTransporter does, and every other
            pawn's plan then excludes it (it sits in the first pawn's target queue) while vanilla's fallback
            excludes it too (its candidate validator requires CanReserve). With a stack-size mod putting 5,000 steel
            in one Thing, the whole manifest IS one Thing — which is why kousaka4656 saw a single pawn loading an
            SRTS ship.

            What is modelled: the exclusion. A stack in one pawn's live plan is invisible to the others until that
            pawn deposits or is interrupted. What is NOT modelled: vanilla's CanReserve fallback and walk time —
            neither changes who may touch the stack, which is the whole question here.

            → KEY: this is the axis that shows the fair share is NOT the mechanism. Serialisation is identical with
              the share doing nothing at all, and the SAME goods in eight stacks are loaded by eight pawns in the
              same number of trips.
        */

        /// <summary>
        /// One mega-stack, crews of one to eight: every trip is carried by the SAME pawn, whatever the crew size.
        /// This is kousaka4656's report reproduced headlessly — and the report's §6 conclusion, that the mechanism
        /// is whole-Thing reservation rather than the fair share, is what the second half of the assertion pins:
        /// spread the identical 5,000 units over eight stacks and all eight pawns load, in the same number of trips.
        /// </summary>
        [Test]
        public void OneMegaStack_SerialisesToASinglePawn_HoweverLargeTheCrew()
        {
            foreach (int crew in CrewSizes)
            {
                var oneStack = CrewSim.FromPool(new Stack("steel", 5000, 0.5f));
                var serialised = RunCrewLoad(oneStack, new LoadScenario
                {
                    crewSize = crew,
                    decisionBudgetKg = 50f,
                    clampBudgetKg = 50f,
                    perThingExclusive = true
                });

                var loaders = new HashSet<int>();
                foreach (var t in serialised.trips)
                    loaders.Add(t.pawn);

                Assert.That(ConservationViolations(serialised, 5000), Is.Empty, $"crew {crew}");
                Assert.That(loaders.Count, Is.EqualTo(1),
                    $"crew {crew}: a manifest that is one Thing must be loaded by exactly one pawn at a time");

                // The same goods, same crew, same rule — but eight Things instead of one.
                var manyStacks = CrewSim.FromPool(SpreadIntoStacks(5000, 0.5f, 8));
                var parallel = RunCrewLoad(manyStacks, new LoadScenario
                {
                    crewSize = crew,
                    decisionBudgetKg = 50f,
                    clampBudgetKg = 50f,
                    perThingExclusive = true
                });

                var parallelLoaders = new HashSet<int>();
                foreach (var t in parallel.trips)
                    parallelLoaders.Add(t.pawn);

                Assert.That(ConservationViolations(parallel, 5000), Is.Empty, $"crew {crew}");
                Assert.That(parallelLoaders.Count, Is.EqualTo(Math.Min(crew, 8)),
                    $"crew {crew}: the same goods in eight stacks must occupy the whole crew");
                Assert.That(parallel.trips.Count, Is.EqualTo(serialised.trips.Count),
                    $"crew {crew}: whole-Thing reservation costs concurrency, not trips");
            }
        }

        /// <summary>
        /// The consequence of the two mechanisms meeting, pinned rather than hidden. The divisor counts every able
        /// boarding peer, because <see cref="LoadFairShare.CountsAsCoLoader"/>'s "has claimable work" fact is read
        /// from the per-DEF ledger — which still says yes for a peer that the whole-stack reservation has locked out
        /// of the only Thing on the manifest. So near the end of a mega-stack load, the one pawn that CAN act
        /// divides its trip among seven that cannot, and P2 says so.
        ///
        /// <para>→ NOTE: this is a documented consequence, not a regression. The report's §6 sizes the fix (reserve
        /// <c>stackCount: take</c> instead of the whole stack, and make the per-thing claim set count-aware) as its
        /// own item, because it touches Multiplayer determinism and vanilla's own fallback. The cost is bounded and
        /// small — the trips are full while the remainder is large, and the fit-in-one-trip short-circuit ends the
        /// division as soon as the remainder fits a pack — which is why the run below still finishes in 59 trips
        /// against an optimum of 50, rather than decaying. If that fix ever lands, this test flips to
        /// <c>Is.Empty</c>.</para>
        /// </summary>
        [Test]
        public void OneMegaStack_ShrinksTheLoadersTrip_ForPeersThatCannotTouchIt()
        {
            var sim = CrewSim.FromPool(new Stack("steel", 5000, 0.5f));
            var run = RunCrewLoad(sim, new LoadScenario
            {
                crewSize = 8,
                decisionBudgetKg = 50f,
                clampBudgetKg = 50f,
                perThingExclusive = true
            });

            Assert.That(ConservationViolations(run, 5000), Is.Empty, "nothing may be lost to the reservation");
            Assert.That(run.couldTakeNothing, Is.Not.Empty, "the peers must really be locked out of the one stack");

            var fullness = FullnessViolations(run);
            Assert.That(fullness, Is.Not.Empty,
                "a share given up for peers that cannot touch the stack is exactly the shortfall P2 exists to name");
            Assert.That(fullness[0], Does.Contain("which never loaded anything"));

            // Bounded, and that is the whole reason this is a note rather than a bug: the divisor costs a handful of
            // trips at the tail, not a decay into single units.
            Assert.That(run.trips.Count, Is.LessThanOrEqualTo(70), "the shortfall must stay a tail, not a decay");
            Assert.That(run.trips[run.trips.Count - 1].units, Is.GreaterThan(1),
                "the fit-in-one-trip short-circuit must still hand the last remainder over whole");
        }

        /// <summary>
        /// The fair share is not what serialises a mega-stack: turn the clamp off entirely (a crew of one, so
        /// <see cref="LoadFairShare.ShareMassBudget"/> returns its no-clamp sentinel on every call) and the shape is
        /// unchanged — one pawn, 50 trips. Conversely, with the exclusivity removed and the share left in place, the
        /// whole crew loads. The discriminating variable is the Thing, exactly as the report concluded.
        /// </summary>
        [Test]
        public void MegaStackSerialisation_SurvivesRemovingTheShare_AndVanishesWithoutTheThingLock()
        {
            var noShare = CrewSim.FromPool(new Stack("steel", 5000, 0.5f));
            var withoutSharing = RunCrewLoad(noShare, new LoadScenario
            {
                crewSize = 1,
                decisionBudgetKg = 50f,
                clampBudgetKg = 50f,
                perThingExclusive = true
            });
            Assert.That(withoutSharing.trips.Count, Is.EqualTo(50), "5000 units of 0.5kg in 50kg packs");
            Assert.That(withoutSharing.clampedTrips, Is.EqualTo(0), "a lone loader is never clamped — no share here at all");

            var noLock = CrewSim.FromPool(new Stack("steel", 5000, 0.5f));
            var shared = RunCrewLoad(noLock, new LoadScenario
            {
                crewSize = 8,
                decisionBudgetKg = 50f,
                clampBudgetKg = 50f,
                perThingExclusive = false
            });
            var loaders = new HashSet<int>();
            foreach (var t in shared.trips)
                loaders.Add(t.pawn);
            Assert.That(loaders.Count, Is.EqualTo(8), "without the Thing lock the same stack occupies the whole crew");
            Assert.That(ConservationViolations(shared, 5000), Is.Empty);
        }
    }
}
