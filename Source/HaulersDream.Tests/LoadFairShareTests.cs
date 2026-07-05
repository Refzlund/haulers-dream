using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The fair-share claim splitting (<see cref="LoadFairShare.ShareMassBudget"/>) and its interaction with the
    /// claim ledger: N pawns splitting one manifest. The simulation mirrors the runtime planner faithfully where it
    /// matters for the CLAIM math: availability comes from <see cref="LoadLedger{TDef,TPawn}.AvailableToClaim"/>,
    /// the divisor counts only CLAIMLESS co-loaders, the mass pre-pass counts pool stacks up to the per-def
    /// claimable units (heaviest counted unit = the no-starvation floor), and each take is clamped by
    /// <see cref="TransportLoadPlan.DeliverableUnits"/> under <see cref="TransportLoadPlan.UnitsWithinMassBudget"/>.
    /// Per-THING conflicts (a stack queued by another pawn) are a runtime concern (a HashSet exclusion) and are not
    /// modeled; the aggregate per-def ledger bounds are what these oracles pin: no starvation, no over-claim,
    /// evenness, determinism, and invariance to the pool's arrival order (the runtime sorts by thingIDNumber).
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
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 1), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 0), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, -3), Is.EqualTo(Inf));
        }

        [Test]
        public void MasslessPool_NeverClamped()
        {
            // Nothing measurable to divide: a 0 budget would wrongly sweep nothing, so the sentinel disables the clamp.
            Assert.That(LoadFairShare.ShareMassBudget(0f, 0f, 4), Is.EqualTo(Inf));
            Assert.That(LoadFairShare.ShareMassBudget(-1f, 0.5f, 4), Is.EqualTo(Inf));
        }

        [Test]
        public void EvenSplit()
        {
            Assert.That(LoadFairShare.ShareMassBudget(200f, 0.5f, 4), Is.EqualTo(50f));
            Assert.That(LoadFairShare.ShareMassBudget(90f, 1f, 3), Is.EqualTo(30f));
        }

        [Test]
        public void Floor_GuaranteesOneHeaviestUnit()
        {
            // 10kg split 8 ways is 1.25kg, below the 3kg heaviest item: floored so every claimable item still fits
            // inside one share (a share smaller than an item would make that item unclaimable for the whole crew).
            Assert.That(LoadFairShare.ShareMassBudget(10f, 3f, 8), Is.EqualTo(3f));
            // Inert when the even share already covers the heaviest unit.
            Assert.That(LoadFairShare.ShareMassBudget(100f, 3f, 4), Is.EqualTo(25f));
            // A non-positive floor (no massive item seen) leaves the raw division.
            Assert.That(LoadFairShare.ShareMassBudget(10f, 0f, 8), Is.EqualTo(1.25f));
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
        // fairness clamp is the binding term under test.
        private static Dictionary<string, int> BuildPlan(Sim sim, Dictionary<string, int> available, float shareMass)
        {
            var plan = new Dictionary<string, int>();
            var claimLeft = new Dictionary<string, int>(available);
            float massLeft = shareMass;
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

        // One pawn asks and claims: availability from the ledger, divisor = 1 + other CLAIMLESS pawns of the crew,
        // share from ShareMassBudget, plan committed via ApplyClaim. Returns the plan (possibly empty).
        private static Dictionary<string, int> AskAndClaim(Sim sim, int pawn, int[] crew)
        {
            var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
            int coLoaders = 0;
            foreach (var p in crew)
                if (p != pawn && !sim.pawnClaims.ContainsKey(p))
                    coLoaders++;
            float mass = ClaimableMass(sim, available, out float heaviest);
            float share = LoadFairShare.ShareMassBudget(mass, heaviest, 1 + coLoaders);
            var plan = BuildPlan(sim, available, share);
            if (plan.Count > 0)
                LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, pawn, plan);
            return plan;
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

        [Test]
        public void FourPawns_BulkStacks_EvenMassSplit()
        {
            // 4 stacks of 100 x 0.5kg (200kg). Four ready pawns must each claim exactly a quarter (50kg = 100 units),
            // not first-come-take-all. This is the reported bug's shape with stackable loot.
            var sim = Sim.FromPool(
                new Stack("steel", 100, 0.5f), new Stack("steel", 100, 0.5f),
                new Stack("steel", 100, 0.5f), new Stack("steel", 100, 0.5f));
            var crew = new[] { 1, 2, 3, 4 };

            foreach (var pawn in crew)
            {
                var plan = AskAndClaim(sim, pawn, crew);
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
            // of 1 per def still lets one pawn claim every def); the MASS split must hand each pawn 2 items.
            var stacks = new Stack[8];
            for (int i = 0; i < 8; i++)
                stacks[i] = new Stack($"relic{i}", 1, 2f);
            var sim = Sim.FromPool(stacks);
            var crew = new[] { 1, 2, 3, 4 };

            foreach (var pawn in crew)
            {
                var plan = AskAndClaim(sim, pawn, crew);
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
            // 2 units of 5kg split across 3 pawns: the raw share (3.33kg) is below one unit, the floor lifts it so
            // the first two askers claim one unit each; the third finds the manifest genuinely empty (not starved).
            var sim = Sim.FromPool(new Stack("statue", 2, 5f));
            var crew = new[] { 1, 2, 3 };

            Assert.That(AskAndClaim(sim, 1, crew).Count, Is.GreaterThan(0), "first asker starved by the raw share");
            Assert.That(AskAndClaim(sim, 2, crew).Count, Is.GreaterThan(0), "second asker starved by the raw share");
            var third = AskAndClaim(sim, 3, crew);
            Assert.That(third.Count, Is.EqualTo(0), "nothing remains for the third asker");
            AssertNoOverClaim(sim);
        }

        [Test]
        public void EveryClaimlessAskerGetsWork_WhileUnclaimedRemains()
        {
            // The no-starvation property at crew scale: mixed-mass loot, 5 pawns; every asker in turn must get a
            // non-empty plan while AvailableToClaim is non-empty for it.
            var sim = Sim.FromPool(
                new Stack("gold", 300, 0.008f), new Stack("jelly", 80, 0.03f),
                new Stack("mace", 1, 4f), new Stack("plate", 1, 12f),
                new Stack("steel", 150, 0.5f));
            var crew = new[] { 1, 2, 3, 4, 5 };

            foreach (var pawn in crew)
            {
                var available = LoadLedger<string, int>.AvailableToClaim(sim.needed, sim.claimed, sim.pawnClaims, pawn);
                var plan = AskAndClaim(sim, pawn, crew);
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
            var sim = Sim.FromPool(new Stack("steel", 100, 1f));
            var crew = new[] { 1, 2, 3 };
            LoadLedger<string, int>.ApplyClaim(sim.claimed, sim.pawnClaims, 1,
                new Dictionary<string, int> { ["steel"] = 40 });

            var plan2 = AskAndClaim(sim, 2, crew);
            Assert.That(plan2["steel"], Is.EqualTo(30), "divisor must count only claimless co-loaders");
            var plan3 = AskAndClaim(sim, 3, crew);
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
                    AskAndClaim(sim, pawn, crew);
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
            // plan (mass budget infinite, only the per-def availability binds).
            var sim = Sim.FromPool(new Stack("steel", 100, 1f), new Stack("gold", 50, 0.008f));
            var plan = AskAndClaim(sim, 1, new[] { 1 });
            Assert.That(plan["steel"], Is.EqualTo(100));
            Assert.That(plan["gold"], Is.EqualTo(50));
            AssertNoOverClaim(sim);
        }

        [Test]
        public void HeaviestUnitFloor_KeepsHeavyItemsClaimable()
        {
            // 3 sculptures of 12kg across 4 pawns: the raw share (9kg) sits below one sculpture, so a lightest-unit
            // floor would leave them unclaimable by the WHOLE crew (every plan empty, the haul stalled into the
            // vanilla one-stack fallback). The heaviest-unit floor lifts every share to 12kg: the first three askers
            // claim one sculpture each and the fourth finds the manifest genuinely empty.
            Assert.That(LoadFairShare.ShareMassBudget(36f, 12f, 4), Is.EqualTo(12f),
                "the floor must lift the share to the heaviest claimable unit");

            var sim = Sim.FromPool(
                new Stack("sculpture", 1, 12f), new Stack("sculpture", 1, 12f), new Stack("sculpture", 1, 12f));
            var crew = new[] { 1, 2, 3, 4 };
            Assert.That(AskAndClaim(sim, 1, crew)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 2, crew)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 3, crew)["sculpture"], Is.EqualTo(1));
            Assert.That(AskAndClaim(sim, 4, crew).Count, Is.EqualTo(0), "nothing remains for the fourth asker");
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
                    AskAndClaim(sim, pawn, crew);
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
