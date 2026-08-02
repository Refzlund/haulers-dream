using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The fair-share claim splitting (<see cref="LoadFairShare.ShareMassBudget"/>), the co-loader predicate that
    /// feeds its divisor (<see cref="LoadFairShare.CountsAsCoLoader"/>), and their interaction with the claim
    /// ledger: N pawns splitting one manifest. The simulation mirrors the runtime planner faithfully where it
    /// matters for the CLAIM math: availability comes from <see cref="LoadLedger{TDef,TPawn}.AvailableToClaim"/>,
    /// the divisor counts only CLAIMLESS co-loaders, the mass pre-pass counts pool stacks up to the per-def
    /// claimable units (heaviest counted unit = the no-starvation floor), the share is applied as a MIN against the
    /// asker's own trip budget exactly as <c>TransportLoad.TryGiveBulkJob</c> applies it, and each take is clamped
    /// by <see cref="TransportLoadPlan.DeliverableUnits"/> under <see cref="TransportLoadPlan.UnitsWithinMassBudget"/>.
    /// Per-THING conflicts (a stack queued by another pawn) are a runtime concern (a HashSet exclusion) and are not
    /// modeled; the aggregate per-def ledger bounds are what these oracles pin: no starvation, no over-claim,
    /// evenness, determinism, and invariance to the pool's arrival order (the runtime sorts by thingIDNumber).
    ///
    /// <para>MULTI-TRIP COVERAGE (issue #167, reopened). Every test here used to be SINGLE-ROUND — each simulated
    /// pawn asked exactly once — so a decay ACROSS trips was structurally invisible, which is how a divisor bug that
    /// turned two trips into ten shipped green. One HD job is one TRIP, so <see cref="RunTripsToCompletion"/> now
    /// runs a whole load the way the game does: ask, clamp, sweep, DELIVER (the ledger settles and the ground pool
    /// shrinks), repeat. The trip-by-trip sizes it returns are what the reporter was actually counting.</para>
    /// </summary>
    [TestFixture]
    public class LoadFairShareTests
    {
        private const float Inf = float.PositiveInfinity;

        // ============ ShareMassBudget unit contract ============

        [Test]
        public void LoneLoader_NeverClamped()
        {
            // Divisor 1 (or nonsense below it) is the back-compat pin: a lone loader keeps the full trip budget.
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 1, 50f), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 0, 50f), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, -3, 50f), Is.EqualTo(Inf));
        }

        [Test]
        public void MasslessPool_NeverClamped()
        {
            // Nothing measurable to divide: a 0 budget would wrongly sweep nothing, so the sentinel disables the clamp.
            Assert.That(LoadFairShare.ShareMassBudget(0f, 0f, 4, 50f), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(-1f, 0.5f, 4, 50f), Is.EqualTo(Inf));
        }

        [Test]
        public void EvenSplit()
        {
            // Trip budgets here are deliberately smaller than the pool, so the split is the binding term (a pool
            // that already fits one trip is never divided at all — see RemainderFittingOneTrip_IsNotSplit).
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 4, 50f), Is.EqualTo(50f));
            Assert.That(LoadFairShare.ShareMassBudget(90f, 1f, 3, 30f), Is.EqualTo(30f));
        }

        [Test]
        public void Floor_GuaranteesOneHeaviestUnit()
        {
            // 10kg split 8 ways is 1.25kg, below the 3kg heaviest item: floored so every claimable item still fits
            // inside one share (a share smaller than an item would make that item unclaimable for the whole crew).
            Assert.That(LoadFairShare.ShareMassBudget(10f, 3f, 8, 4f), Is.EqualTo(3f));
            // Inert when the even share already covers the heaviest unit.
            Assert.That(LoadFairShare.ShareMassBudget(100f, 3f, 4, 30f), Is.EqualTo(25f));
            // A non-positive floor (no massive item seen) leaves the raw division.
            Assert.That(LoadFairShare.ShareMassBudget(10f, 0f, 8, 4f), Is.EqualTo(1.25f));
        }

        [Test]
        public void RemainderFittingOneTrip_IsNotSplit()
        {
            // The reopened #167 rule. The caller applies this share as a MIN against the very trip budget passed in,
            // so a share can only ever SHRINK a trip. Dividing a pool the asker could clear in one go therefore buys
            // nothing and costs trips: it comes back for the rest, re-divides the smaller remainder, and each trip
            // carries less than the last.
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, 50f), Is.EqualTo(Inf), "30kg fits in a 50kg trip");
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, 30f), Is.EqualTo(Inf), "exactly one trip's worth still fits");

            // A pool bigger than one trip is a genuine crew job again: split it.
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, 20f), Is.EqualTo(7.5f));

            // The UNBOUNDED sentinels must still split. An uncapped smart-overload ceiling (level 0) reaches the
            // planner as float.MaxValue and an uncapped destination as infinity; an asker with no trip bound could
            // swallow ANY manifest in one trip, so short-circuiting there would hand it the lot and idle its peers —
            // the concentration this clamp exists to stop.
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, float.MaxValue), Is.EqualTo(7.5f));
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, Inf), Is.EqualTo(7.5f));

            // A nonsense budget never short-circuits either (no NaN or non-positive value reaches a claim decision).
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, float.NaN), Is.EqualTo(7.5f));
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, 0f), Is.EqualTo(7.5f));
            Assert.That(LoadFairShare.ShareMassBudget(30f, 5f, 4, -12f), Is.EqualTo(7.5f));
        }

        // ============ CountsAsCoLoader (who may shrink someone else's trip) ============

        /// <summary>
        /// One other pawn the runtime would consider for the divisor, described by the plain facts
        /// <see cref="LoadFairShare.CountsAsCoLoader"/> decides on. The defaults describe a GENUINE co-loader (a
        /// boarding passenger that can haul, still has something claimable, and is able to act right now); each test
        /// flips only the one fact it is about.
        /// </summary>
        private sealed class Bystander
        {
            /// <summary>Bound to THIS loadable by a boarding duty ("load this, then enter it").</summary>
            public bool boardingThisLoadable = true;
            /// <summary>Vanilla would give it the Hauling WORK TYPE (for a colony mech: its mechEnabledWorkTypes).</summary>
            public bool canDoHaulingWorkType = true;
            /// <summary>The ledger still has something for it to claim on this task.</summary>
            public bool hasClaimableWork = true;
            /// <summary>Incapacitated.</summary>
            public bool downed;
            /// <summary>Under player control, so not taking jobs from the duty tree.</summary>
            public bool drafted;
            /// <summary>Berserk / wandering / binging.</summary>
            public bool inMentalState;
            /// <summary>Has working manipulation (vanilla's own gate for handing out a loading job).</summary>
            public bool capableOfManipulation = true;
            /// <summary>Can run HD's bulk-load driver at all (carrier comp + a pawn inventory).</summary>
            public bool hasCarrierComp = true;

            /// <summary>Ask the pure predicate whether these facts add up to a co-loader.</summary>
            public bool Counts() => LoadFairShare.CountsAsCoLoader(
                isBoardingPassengerOfThisLoadable: boardingThisLoadable,
                canDoHaulingWorkType: canDoHaulingWorkType,
                hasClaimableWork: hasClaimableWork,
                downed: downed,
                drafted: drafted,
                inMentalState: inMentalState,
                capableOfManipulation: capableOfManipulation,
                hasCarrierComp: hasCarrierComp);

            /// <summary>A colony mech standing right there, fully able, that vanilla can never give a hauling job to
            /// — a Constructoid (mechEnabledWorkTypes = Construction) or a Cleansweeper (Cleaning). Counting these
            /// two as loaders is what divided the reporter's single hauler mech into ten trips.</summary>
            public static Bystander NonHaulingMech() => new Bystander { canDoHaulingWorkType = false };
        }

        /// <summary>The fair-share divisor these bystanders produce: the asker plus everyone the predicate accepts.</summary>
        private static int Divisor(params Bystander[] bystanders)
        {
            int coLoaders = 0;
            foreach (var b in bystanders)
                if (b.Counts())
                    coLoaders++;
            return 1 + coLoaders;
        }

        /// <summary>A bystander that fails exactly ONE fact (chosen by <paramref name="rng"/>) and so must never be
        /// counted — the oracle's "pawns who will never load" population.</summary>
        private static Bystander RejectedBystander(Random rng)
        {
            var b = new Bystander();
            switch (rng.Next(8))
            {
                case 0: b.boardingThisLoadable = false; break; // a free hauler with a colony of other work
                case 1: b.canDoHaulingWorkType = false; break; // a constructoid / cleansweeper
                case 2: b.hasClaimableWork = false; break;     // already aboard, or can reach nothing claimable
                case 3: b.downed = true; break;
                case 4: b.drafted = true; break;
                case 5: b.inMentalState = true; break;
                case 6: b.capableOfManipulation = false; break;
                default: b.hasCarrierComp = false; break;
            }
            return b;
        }

        [Test]
        public void CoLoader_CountsOnlyAnAbleLoaderBoundToThisManifest()
        {
            Assert.That(new Bystander().Counts(), Is.True,
                "a boarding passenger that can haul and still has claimable work is a real co-loader");

            // Each fact on its own is disqualifying: every pawn counted here shrinks a real hauler's trip, and the
            // clamp only ever removes capacity, so the bar is "committed and able right now", not "might help".
            Assert.That(new Bystander { boardingThisLoadable = false }.Counts(), Is.False,
                "a free hauler is not travelling to this target — it must never shrink someone else's load");
            Assert.That(new Bystander { hasClaimableWork = false }.Counts(), Is.False,
                "already aboard or nothing left to claim: it will never ask again");
            Assert.That(new Bystander { downed = true }.Counts(), Is.False);
            Assert.That(new Bystander { drafted = true }.Counts(), Is.False);
            Assert.That(new Bystander { inMentalState = true }.Counts(), Is.False);
            Assert.That(new Bystander { capableOfManipulation = false }.Counts(), Is.False);
            Assert.That(new Bystander { hasCarrierComp = false }.Counts(), Is.False);
        }

        [Test]
        public void CoLoader_MechThatCannotHaulNeverCounts()
        {
            // Vanilla gives a colony mech only the work types in its mechEnabledWorkTypes: a Lifter hauls, a
            // Constructoid builds, a Cleansweeper cleans, and Pawn.GetDisabledWorkTypes disables everything else.
            // So the mech standing next to the shuttle is not a loader no matter how able it otherwise looks.
            Assert.That(Bystander.NonHaulingMech().Counts(), Is.False);

            // Nothing else can rescue it: an awake, undrafted, manipulation-capable boarding mech that could run the
            // bulk-load driver and still has work waiting is not someone the game will ever hand a hauling job.
            var ableButNotAHauler = Bystander.NonHaulingMech();
            ableButNotAHauler.hasClaimableWork = true;
            ableButNotAHauler.capableOfManipulation = true;
            ableButNotAHauler.hasCarrierComp = true;
            Assert.That(ableButNotAHauler.Counts(), Is.False);

            // The Lifter beside it, which vanilla DOES give hauling work to, is counted.
            Assert.That(new Bystander().Counts(), Is.True);

            // Two non-hauling mechs leave the one able hauler dividing by one — i.e. not dividing at all.
            Assert.That(Divisor(Bystander.NonHaulingMech(), Bystander.NonHaulingMech()), Is.EqualTo(1));
        }

        // ============ N-pawn split simulation (the claim-splitting oracle) ============

        /// <summary>One ground stack in the simulated pool.</summary>
        private sealed class Stack
        {
            /// <summary>Fake def id (the ledger's TDef).</summary>
            public string def;
            /// <summary>Units remaining on the ground.</summary>
            public int count;
            /// <summary>Mass of one unit, kg.</summary>
            public float unitMass;
            /// <summary>Stand-in for thingIDNumber, the runtime's deterministic sort key. Only the pool-order
            /// invariance test assigns it; everywhere else the arrival order is already canonical.</summary>
            public int id;

            public Stack(string def, int count, float unitMass, int id = 0)
            {
                this.def = def;
                this.count = count;
                this.unitMass = unitMass;
                this.id = id;
            }
        }

        /// <summary>Whole simulated task: the three ledger dictionaries plus the ground pool.</summary>
        private sealed class Sim
        {
            public Dictionary<string, int> needed = new Dictionary<string, int>();
            public Dictionary<string, int> claimed = new Dictionary<string, int>();
            public Dictionary<int, Dictionary<string, int>> pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            public List<Stack> pool = new List<Stack>();

            public static Sim FromPool(params Stack[] stacks)
            {
                var sim = new Sim();
                foreach (var s in stacks)
                {
                    sim.pool.Add(s);
                    sim.needed[s.def] = (sim.needed.TryGetValue(s.def, out int cur) ? cur : 0) + s.count;
                }
                return sim;
            }
        }

        // The runtime's fair-share mass pre-pass: pool stacks of claimable defs, counted up to the per-def
        // claimable units (decrementing so over-supply never inflates), heaviest counted unit reported for the floor.
        private static float ClaimableMass(Sim sim, Dictionary<string, int> available, out float heaviest)
        {
            heaviest = 0f;
            float total = 0f;
            var left = new Dictionary<string, int>(available);
            foreach (var s in sim.pool)
            {
                if (s.count <= 0 || !left.TryGetValue(s.def, out int rem) || rem <= 0)
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
        // fairness clamp and the trip budget are the binding terms under test.
        private static Dictionary<string, int> BuildPlan(Sim sim, Dictionary<string, int> available, float massBudget)
        {
            var plan = new Dictionary<string, int>();
            var claimLeft = new Dictionary<string, int>(available);
            float massLeft = massBudget;
            foreach (var s in sim.pool)
            {
                if (massLeft <= 0.0001f)
                    break;
                if (s.count <= 0 || !claimLeft.TryGetValue(s.def, out int avail) || avail <= 0)
                    continue;
                int massAffordable = TransportLoadPlan.UnitsWithinMassBudget(massLeft, s.unitMass, s.count);
                int take = TransportLoadPlan.DeliverableUnits(s.count, avail, avail, massAffordable);
                if (take <= 0)
                    continue;
                plan[s.def] = (plan.TryGetValue(s.def, out int cur) ? cur : 0) + take;
                claimLeft[s.def] = avail - take;
                massLeft -= take * s.unitMass;
            }
            return plan;
        }

        // The runtime's trip budget after the fairness clamp: TryGiveBulkJob starts from the pawn's own trip mass
        // and lowers it to the share ONLY when the share is smaller (`if (share < massLeft) massLeft = share`).
        // Modelling the min is the whole point — a share can shrink a trip, never grow one.
        private static float ClampedTripBudget(float share, float tripBudgetKg)
            => share < tripBudgetKg ? share : tripBudgetKg;

        // One pawn asks and claims: availability from the ledger, divisor = 1 + other CLAIMLESS pawns of the crew,
        // share from ShareMassBudget, plan committed via ApplyClaim. Returns the plan (possibly empty).
        private static Dictionary<string, int> AskAndClaim(Sim sim, int pawn, int[] crew, float tripBudgetKg)
        {
            var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
            int coLoaders = 0;
            foreach (var p in crew)
                if (p != pawn && !sim.pawnClaims.ContainsKey(p))
                    coLoaders++;
            float mass = ClaimableMass(sim, available, out float heaviest);
            float share = LoadFairShare.ShareMassBudget(mass, heaviest, 1 + coLoaders, tripBudgetKg);
            var plan = BuildPlan(sim, available, ClampedTripBudget(share, tripBudgetKg));
            if (plan.Count > 0)
                LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, pawn, plan);
            return plan;
        }

        /// <summary>
        /// Run one pawn's whole load to completion, ONE TRIP PER ROUND, and report the units carried on each trip.
        /// This is the shape the single-round helpers above cannot see: a job is a trip, so the remainder is
        /// re-divided every time the pawn comes back, and an over-large divisor decays the trips geometrically.
        /// </summary>
        /// <param name="sim">The task; mutated as trips are delivered (needed shrinks, the pool empties).</param>
        /// <param name="asker">The only pawn that actually asks for work.</param>
        /// <param name="divisor">The fair-share divisor for every round — 1 + the co-loaders
        /// <see cref="LoadFairShare.CountsAsCoLoader"/> accepts. Held constant because the bystanders it counts
        /// never claim anything, which is exactly the reported situation.</param>
        /// <param name="tripBudgetKg">What the asker can carry in one trip (kg).</param>
        /// <returns>Units carried per trip, in order. Stops when nothing is claimable or a trip would be empty; a
        /// hard round cap keeps a regression from hanging the suite rather than failing it (the caller asserts the
        /// whole order was delivered, so hitting the cap fails).</returns>
        private static List<int> RunTripsToCompletion(Sim sim, int asker, int divisor, float tripBudgetKg)
        {
            var trips = new List<int>();
            for (int round = 0; round < 500; round++)
            {
                var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, asker);
                if (available.Count == 0)
                    break;
                float mass = ClaimableMass(sim, available, out float heaviest);
                float share = LoadFairShare.ShareMassBudget(mass, heaviest, divisor, tripBudgetKg);
                var plan = BuildPlan(sim, available, ClampedTripBudget(share, tripBudgetKg));
                int units = 0;
                foreach (var kv in plan)
                    units += kv.Value;
                if (units == 0)
                    break; // nothing affordable — the load has stalled, the caller's "all delivered" assert catches it
                LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, asker, plan);
                Deliver(sim, asker, plan);
                trips.Add(units);
            }
            return trips;
        }

        // The end of a trip: the goods are physically in the container now, so the ledger SETTLES them (needed,
        // claimed and this pawn's claim all drop — the pawn starts the next trip claimless, as the runtime does
        // after its deposit) and the ground pool loses what was carried away.
        private static void Deliver(Sim sim, int pawn, Dictionary<string, int> plan)
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
        }

        private static float MassOf(Dictionary<string, int> plan, Sim sim)
        {
            float total = 0f;
            foreach (var kv in plan)
            {
                // Unit mass by def from the pool (uniform per def in these scenarios).
                float unit = 0f;
                foreach (var s in sim.pool)
                    if (s.def == kv.Key) { unit = s.unitMass; break; }
                total += kv.Value * unit;
            }
            return total;
        }

        private static void AssertNoOverClaim(Sim sim)
        {
            var recomputed = LoadLedger<string, int>.RecomputeClaimed(sim.pawnClaims);
            foreach (var kv in recomputed)
            {
                int needed = sim.needed.TryGetValue(kv.Key, out int n) ? n : 0;
                Assert.That(kv.Value, Is.LessThanOrEqualTo(needed), $"claims of {kv.Key} exceed needed");
                Assert.That(sim.claimed.TryGetValue(kv.Key, out int c) ? c : 0, Is.EqualTo(kv.Value),
                    "totalClaimed invariant broken");
            }
        }

        // ============ Multi-trip oracles (issue #167, reopened) ============

        [Test]
        public void SingleEffectiveHauler_EveryTripIsFull_UntilOrderMet()
        {
            // The reopened report, to the unit: 33 units to move to an exit, ONE hauler mech that can actually haul,
            // and two other mechs standing beside it that vanilla can never give a hauling job to. The hauler's pack
            // holds 9 units per trip, so the whole order is four trips.
            const float TripBudget = 9f; // 9 units of 1kg
            int divisor = Divisor(Bystander.NonHaulingMech(), Bystander.NonHaulingMech());
            Assert.That(divisor, Is.EqualTo(1), "neither mech can haul, so there is nobody to share with");

            var sim = Sim.FromPool(new Stack("steel", 33, 1f));
            var trips = RunTripsToCompletion(sim, 1, divisor, TripBudget);

            Assert.That(trips, Is.EqualTo(new[] { 9, 9, 9, 6 }), "every trip full until the order runs out");
            Assert.That(trips.Count, Is.EqualTo(4), "ceil(33 / 9) trips, not ten");

            // The same thing stated as the general property, so a future change that merely reshuffles the sizes
            // still has to keep every trip as full as the remaining order allows.
            int remaining = 33;
            foreach (int carried in trips)
            {
                Assert.That(carried, Is.EqualTo(Math.Min(9, remaining)),
                    "a trip carried less than the pack could hold while goods were still waiting");
                remaining -= carried;
            }
            Assert.That(remaining, Is.EqualTo(0), "the whole order was delivered");

            // What an inflated divisor still costs, and what it can no longer cost. Feeding divisor 3 to the FIXED
            // policy gives 9, 8, 5, 3, 8: the middle trips still shrink (that is the divisor doing damage, and why
            // the co-loader count had to be fixed too), but the run no longer tails off into single units — once the
            // remainder fits one pack the short-circuit hands it over whole.
            //
            // So the two halves of this fix are independent, and this pins that: the short-circuit alone is enough
            // to kill the reported one-item tail even if some future change re-inflates the divisor. The reporter's
            // full decay (9, 8, 5, 3, 2, 2, 1, 1, 1, 1) needed BOTH defects present.
            var decaySim = Sim.FromPool(new Stack("steel", 33, 1f));
            var decayTrips = RunTripsToCompletion(decaySim, 1, 3, TripBudget);
            Assert.That(decayTrips.Count, Is.GreaterThan(trips.Count),
                "an inflated divisor must be what costs the extra trips");
            Assert.That(decayTrips[0], Is.EqualTo(9), "the first trip still looked healthy — that is why it hid");
            Assert.That(decayTrips[decayTrips.Count - 1], Is.GreaterThan(1),
                "the short-circuit must protect the tail even when the divisor is wrong");
            Assert.That(decayTrips, Has.None.EqualTo(1),
                "no trip may rattle a single unit around an otherwise empty pack");
        }

        [Test]
        public void OneEffectiveHauler_NeverCarriesLessThanAFullTrip()
        {
            // Oracle over randomised orders, pack sizes and bystander crowds: however many pawns are standing
            // around, if none of them will actually load, the one hauler that does must fill every trip.
            // Masses are exact binary fractions so the sim's mass arithmetic cannot drift a unit at the boundary
            // (a float wobble there would be a test artefact, not the behaviour under test).
            var unitMasses = new[] { 0.25f, 0.5f, 1f, 2f, 4f };
            var rng = new Random(20260802);

            for (int iteration = 0; iteration < 400; iteration++)
            {
                int total = rng.Next(1, 120);
                float unitMass = unitMasses[rng.Next(unitMasses.Length)];
                int perTrip = rng.Next(1, 25);
                float tripBudget = perTrip * unitMass;

                var bystanders = new Bystander[rng.Next(0, 6)];
                for (int i = 0; i < bystanders.Length; i++)
                    bystanders[i] = RejectedBystander(rng);
                int divisor = Divisor(bystanders);
                Assert.That(divisor, Is.EqualTo(1), "no bystander that will never load may enter the divisor");

                // Split the order over one to three ground stacks so the sweep has to fill across stacks.
                int stackCount = rng.Next(1, 4);
                var stacks = new List<Stack>();
                int spread = total;
                for (int i = 0; i < stackCount && spread > 0; i++)
                {
                    int units = (i == stackCount - 1) ? spread : Math.Max(1, spread / (stackCount - i));
                    stacks.Add(new Stack("cargo", units, unitMass));
                    spread -= units;
                }
                var sim = Sim.FromPool(stacks.ToArray());

                var trips = RunTripsToCompletion(sim, 1, divisor, tripBudget);

                int remaining = total;
                foreach (int carried in trips)
                {
                    Assert.That(carried, Is.EqualTo(Math.Min(perTrip, remaining)),
                        $"iteration {iteration}: total {total}, pack {perTrip}, unit {unitMass}kg, " +
                        $"{bystanders.Length} bystanders — a trip came up short");
                    remaining -= carried;
                }
                Assert.That(remaining, Is.EqualTo(0), $"iteration {iteration}: the order was left unfinished");
            }
        }

        // ============ Single-round splits (the crew that really does share) ============

        [Test]
        public void FourPawns_BulkStacks_EvenMassSplit()
        {
            // 4 stacks of 100 x 0.5kg (200kg). Four ready pawns must each claim exactly a quarter (50kg = 100 units),
            // not first-come-take-all. This is the reported bug's shape with stackable loot. The 60kg trip budget is
            // bigger than a share (so the share is what binds) but far smaller than the pool (so it is a real crew
            // job, not a remainder one pawn could clear alone).
            const float TripBudget = 60f;
            var sim = Sim.FromPool(
                new Stack("steel", 100, 0.5f), new Stack("steel", 100, 0.5f),
                new Stack("steel", 100, 0.5f), new Stack("steel", 100, 0.5f));
            var crew = new[] { 1, 2, 3, 4 };

            foreach (var pawn in crew)
            {
                var plan = AskAndClaim(sim, pawn, crew, TripBudget);
                Assert.That(plan.Count, Is.GreaterThan(0), $"pawn {pawn} starved");
                Assert.That(MassOf(plan, sim), Is.EqualTo(50f).Within(0.001f), $"pawn {pawn} share uneven");
            }
            AssertNoOverClaim(sim);
            // The whole manifest is claimed: needed fully covered, nothing left for a fifth asker.
            var extra = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, 5);
            Assert.That(extra.Count, Is.EqualTo(0));
        }

        [Test]
        public void FourPawns_SingletonUniques_SplitByShare()
        {
            // Dungeon-loot shape: 8 DISTINCT one-item defs (2kg each). Per-def quota math cannot bound this (a quota
            // of 1 per def still lets one pawn claim every def); the MASS split must hand each pawn 2 items. The 5kg
            // pack keeps the pool (16kg) well beyond one trip, so the split is the binding term throughout.
            const float TripBudget = 5f;
            var stacks = new Stack[8];
            for (int i = 0; i < 8; i++)
                stacks[i] = new Stack($"relic{i}", 1, 2f);
            var sim = Sim.FromPool(stacks);
            var crew = new[] { 1, 2, 3, 4 };

            foreach (var pawn in crew)
            {
                var plan = AskAndClaim(sim, pawn, crew, TripBudget);
                int items = 0;
                foreach (var kv in plan) items += kv.Value;
                Assert.That(items, Is.EqualTo(2), $"pawn {pawn} took {items} uniques, expected 2");
            }
            AssertNoOverClaim(sim);
            var extra = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, 5);
            Assert.That(extra.Count, Is.EqualTo(0), "everything should be claimed after the crew split");
        }

        [Test]
        public void NoStarvation_TinyRemainderStillClaimable()
        {
            // 2 units of 5kg split across 3 pawns, each able to carry one statue per trip (6kg pack). The raw share
            // (3.33kg) is below one unit, so the floor lifts it and the first asker claims one; the second finds a
            // remainder that fits its own trip and takes it whole; the third finds the manifest genuinely empty
            // (not starved).
            const float TripBudget = 6f;
            var sim = Sim.FromPool(new Stack("statue", 2, 5f));
            var crew = new[] { 1, 2, 3 };

            Assert.That(AskAndClaim(sim, 1, crew, TripBudget).Count, Is.GreaterThan(0), "first asker starved by the raw share");
            Assert.That(AskAndClaim(sim, 2, crew, TripBudget).Count, Is.GreaterThan(0), "second asker starved by the raw share");
            var third = AskAndClaim(sim, 3, crew, TripBudget);
            Assert.That(third.Count, Is.EqualTo(0), "nothing remains for the third asker");
            AssertNoOverClaim(sim);
        }

        [Test]
        public void EveryClaimlessAskerGetsWork_WhileUnclaimedRemains()
        {
            // The no-starvation property at crew scale: mixed-mass loot, 5 pawns; every asker in turn must get a
            // non-empty plan while AvailableToClaim is non-empty for it.
            const float TripBudget = 20f;
            var sim = Sim.FromPool(
                new Stack("gold", 300, 0.008f), new Stack("jelly", 80, 0.03f),
                new Stack("mace", 1, 4f), new Stack("plate", 1, 12f),
                new Stack("steel", 150, 0.5f));
            var crew = new[] { 1, 2, 3, 4, 5 };

            foreach (var pawn in crew)
            {
                var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
                var plan = AskAndClaim(sim, pawn, crew, TripBudget);
                if (available.Count > 0)
                    Assert.That(plan.Count, Is.GreaterThan(0), $"pawn {pawn} starved while goods were unclaimed");
            }
            AssertNoOverClaim(sim);
        }

        [Test]
        public void ClaimHolders_DoNotShrinkOthersShares()
        {
            // A pawn already carrying its slice is excluded from the divisor (its claim already shrank the
            // available map). Crew of 3: pawn 1 pre-claims 40 of 100; pawn 2 then splits the REMAINING 60 with
            // pawn 3 only (30 each), not three ways (20).
            const float TripBudget = 40f;
            var sim = Sim.FromPool(new Stack("steel", 100, 1f));
            var crew = new[] { 1, 2, 3 };
            LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, 1,
                new Dictionary<string, int> { ["steel"] = 40 });

            var plan2 = AskAndClaim(sim, 2, crew, TripBudget);
            Assert.That(plan2["steel"], Is.EqualTo(30), "divisor must count only claimless co-loaders");
            var plan3 = AskAndClaim(sim, 3, crew, TripBudget);
            Assert.That(plan3["steel"], Is.EqualTo(30));
            AssertNoOverClaim(sim);
        }

        [Test]
        public void SplitIsDeterministic()
        {
            // Same inputs, same claims, twice over: the split must be a pure function of the sim state (Multiplayer
            // runs it independently on every client).
            Dictionary<int, Dictionary<string, int>> Run()
            {
                var sim = Sim.FromPool(
                    new Stack("gold", 300, 0.008f), new Stack("jelly", 80, 0.03f),
                    new Stack("mace", 1, 4f), new Stack("steel", 150, 0.5f));
                var crew = new[] { 1, 2, 3, 4 };
                foreach (var pawn in crew)
                    AskAndClaim(sim, pawn, crew, 20f);
                return sim.pawnClaims;
            }

            var a = Run();
            var b = Run();
            Assert.That(a.Count, Is.EqualTo(b.Count));
            foreach (var kv in a)
            {
                Assert.That(b.ContainsKey(kv.Key), $"pawn {kv.Key} claim set diverged");
                var other = b[kv.Key];
                Assert.That(other.Count, Is.EqualTo(kv.Value.Count));
                foreach (var defKv in kv.Value)
                    Assert.That(other.TryGetValue(defKv.Key, out int v) ? v : -1, Is.EqualTo(defKv.Value),
                        $"pawn {kv.Key} def {defKv.Key} diverged");
            }
        }

        [Test]
        public void LoneLoader_SimulationMatchesLegacyFullClaim()
        {
            // Crew of one: the sentinel keeps the old behavior, the single pawn claims the entire manifest in one
            // plan (only its own trip budget and the per-def availability bind).
            var sim = Sim.FromPool(new Stack("steel", 100, 1f), new Stack("gold", 50, 0.008f));
            var plan = AskAndClaim(sim, 1, new[] { 1 }, 200f);
            Assert.That(plan["steel"], Is.EqualTo(100));
            Assert.That(plan["gold"], Is.EqualTo(50));
            AssertNoOverClaim(sim);
        }

        [Test]
        public void HeaviestUnitFloor_KeepsHeavyItemsClaimable()
        {
            // 3 sculptures of 12kg across 4 pawns, each able to carry one per trip (15kg pack): the raw share (9kg)
            // sits below one sculpture, so a lightest-unit floor would leave them unclaimable by the WHOLE crew
            // (every plan empty, the haul stalled into the vanilla one-stack fallback). The heaviest-unit floor
            // lifts every share to 12kg: the first three askers claim one sculpture each and the fourth finds the
            // manifest genuinely empty.
            const float TripBudget = 15f;
            Assert.That(LoadFairShare.ShareMassBudget(36f, 12f, 4, TripBudget), Is.EqualTo(12f),
                "the floor must lift the share to the heaviest claimable unit");

            var sim = Sim.FromPool(
                new Stack("sculpture", 1, 12f), new Stack("sculpture", 1, 12f), new Stack("sculpture", 1, 12f));
            var crew = new[] { 1, 2, 3, 4 };
            Assert.That(AskAndClaim(sim, 1, crew, TripBudget)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 2, crew, TripBudget)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 3, crew, TripBudget)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 4, crew, TripBudget).Count, Is.EqualTo(0), "nothing remains for the fourth asker");
            AssertNoOverClaim(sim);
        }

        [Test]
        public void PoolOrderDoesNotChangeClaims()
        {
            // The float mass sum is order-sensitive in its low bits and the runtime pool arrives in per-client
            // HashSet order, so TransportLoad.TryGiveBulkJob normalizes the pool to thingIDNumber order before the
            // pre-pass (its ByThingId sort). This pins the twin contract at the math level: the same stacks fed in
            // two different arrival orders, normalized by id exactly as the runtime does, must produce identical
            // claims for the whole crew (a low-bit share difference on one Multiplayer client is a desync).
            Stack S(int id, string def, int count, float unitMass) => new Stack(def, count, unitMass, id);

            Dictionary<int, Dictionary<string, int>> Run(params Stack[] arrival)
            {
                var sim = Sim.FromPool(arrival);
                // The runtime's pool.Sort(ByThingId) twin: normalize arrival order before any mass math.
                sim.pool.Sort((a, b) => a.id.CompareTo(b.id));
                var crew = new[] { 1, 2, 3 };
                foreach (var pawn in crew)
                    AskAndClaim(sim, pawn, crew, 20f);
                return sim.pawnClaims;
            }

            var forward = Run(
                S(1, "gold", 300, 0.008f), S(2, "jelly", 80, 0.03f), S(3, "mace", 1, 4f), S(4, "steel", 150, 0.5f));
            var reversed = Run(
                S(4, "steel", 150, 0.5f), S(3, "mace", 1, 4f), S(2, "jelly", 80, 0.03f), S(1, "gold", 300, 0.008f));

            Assert.That(forward.Count, Is.EqualTo(reversed.Count));
            foreach (var kv in forward)
            {
                Assert.That(reversed.ContainsKey(kv.Key), $"pawn {kv.Key} claim set diverged across arrival orders");
                var other = reversed[kv.Key];
                Assert.That(other.Count, Is.EqualTo(kv.Value.Count), $"pawn {kv.Key} def-set diverged across arrival orders");
                foreach (var defKv in kv.Value)
                    Assert.That(other.TryGetValue(defKv.Key, out int v) ? v : -1, Is.EqualTo(defKv.Value),
                        $"pawn {kv.Key} def {defKv.Key} diverged across arrival orders");
            }
        }
    }
}
