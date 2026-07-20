using System.Collections.Generic;
using System.Text;
using HaulersDream.Core;
using NUnit.Framework;
using static HaulersDream.Core.UrgentVicinityPolicy;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the pure "Haul Urgently" vicinity selection (<see cref="UrgentVicinityPolicy"/>): the inclusive radius
    /// filter, the urgent-first then nearest-first then id-tiebreak ordering, the include-non-urgent gate, the
    /// carry-mass ceiling accumulation (partial takes, skip-too-heavy, max-stacks), and multiplayer determinism
    /// across a shuffled input. Mirrors the style of <see cref="BulkHaulPolicyTests"/> / EnRoutePickupPolicyTests.
    /// </summary>
    [TestFixture]
    public class UrgentVicinityPolicyTests
    {
        // A candidate. The second arg is the SQUARED distance to the anchor (as the policy compares it), so a
        // "dist 5" neighbour is C(id, 25f, ...). Default unit mass 1 kg and stack 10 keep the ceiling math simple.
        static Candidate C(int id, float distSq, bool urgent, float unitMass = 1f, int stack = 10)
            => new Candidate(id, distSq, urgent, unitMass, stack);

        static List<Candidate> Cands(params Candidate[] cs) => new List<Candidate>(cs);

        // A stable signature of the selection (id:take pairs, in order) for order-and-value equality assertions.
        static string Sig(List<UrgentTake> r)
        {
            var sb = new StringBuilder();
            foreach (var t in r)
                sb.Append(t.ThingId).Append(':').Append(t.Take).Append(' ');
            return sb.ToString();
        }

        const float Inf = float.PositiveInfinity; // a ceiling that never binds (isolate ordering / filtering)

        // ── Empty / lone-primary ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Empty_Or_AllFiltered_SelectsNothing()
        {
            // No candidates (the "lone primary, no vicinity cluster" case the Verse layer maps to a null job).
            Assert.That(Select(new List<Candidate>(), 100f, includeNonUrgent: true, Inf, 0f, 24), Is.Empty);
            Assert.That(Select(null, 100f, includeNonUrgent: true, Inf, 0f, 24), Is.Empty);
            // A single candidate out of range → still empty.
            Assert.That(Select(Cands(C(1, 400f, urgent: true)), 100f, true, Inf, 0f, 24), Is.Empty);
            // maxStacks 0 → empty regardless.
            Assert.That(Select(Cands(C(1, 1f, urgent: true)), 100f, true, Inf, 0f, maxStacks: 0), Is.Empty);
        }

        // ── Radius filter (inclusive boundary) ─────────────────────────────────────────────────────────

        [Test]
        public void RadiusBoundary_IsInclusive()
        {
            // radiusSq 9 (radius 3): distSq exactly 9 is IN range; 10 is out.
            var atBoundary = Select(Cands(C(1, 9f, urgent: true)), 9f, true, Inf, 0f, 24);
            Assert.That(atBoundary.Count, Is.EqualTo(1));
            Assert.That(atBoundary[0].ThingId, Is.EqualTo(1));

            Assert.That(Select(Cands(C(2, 10f, urgent: true)), 9f, true, Inf, 0f, 24), Is.Empty);
        }

        // ── include-non-urgent gate ────────────────────────────────────────────────────────────────────

        [Test]
        public void IncludeNonUrgentFalse_DropsAllNonUrgent()
        {
            var cands = Cands(C(1, 1f, urgent: false), C(2, 4f, urgent: true));
            var onlyUrgent = Select(cands, 100f, includeNonUrgent: false, Inf, 0f, 24);
            Assert.That(onlyUrgent.Count, Is.EqualTo(1));
            Assert.That(onlyUrgent[0].ThingId, Is.EqualTo(2)); // the urgent one only

            var both = Select(cands, 100f, includeNonUrgent: true, Inf, 0f, 24);
            Assert.That(both.Count, Is.EqualTo(2));
        }

        // ── ordering ───────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void UrgentTier_RanksBeforeNonUrgent_EvenWhenFarther()
        {
            // Urgent at dist 5 (distSq 25) still leads a non-urgent at dist 1 (distSq 1) when both are opted in.
            var cands = Cands(C(1, 1f, urgent: false), C(2, 25f, urgent: true));
            var r = Select(cands, 100f, includeNonUrgent: true, Inf, 0f, 24);
            Assert.That(r.Count, Is.EqualTo(2));
            Assert.That(r[0].ThingId, Is.EqualTo(2)); // urgent first, despite being farther
            Assert.That(r[1].ThingId, Is.EqualTo(1));
        }

        [Test]
        public void WithinTier_NearestFirst_ThenLowerIdTiebreak()
        {
            // Three urgent: two tied at distSq 1 (ids 30, 20) and one at distSq 4 (id 10). Nearest first, and the
            // equal-distance pair breaks to the LOWER id (20 before 30) so the order is deterministic.
            var cands = Cands(C(10, 4f, urgent: true), C(30, 1f, urgent: true), C(20, 1f, urgent: true));
            var r = Select(cands, 100f, includeNonUrgent: true, Inf, 0f, 24);
            Assert.That(r[0].ThingId, Is.EqualTo(20));
            Assert.That(r[1].ThingId, Is.EqualTo(30));
            Assert.That(r[2].ThingId, Is.EqualTo(10));
        }

        // ── carry-mass ceiling ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void CapacityCeiling_PartialThirdTake_NeverExceeds()
        {
            // Ceiling 25 kg, running 0, unit 1 kg, stacks of 10 → 2.5 stacks fit: 10, 10, then a PARTIAL 5.
            var cands = Cands(
                C(1, 1f, urgent: true, unitMass: 1f, stack: 10),
                C(2, 2f, urgent: true, unitMass: 1f, stack: 10),
                C(3, 3f, urgent: true, unitMass: 1f, stack: 10),
                C(4, 4f, urgent: true, unitMass: 1f, stack: 10));
            var r = Select(cands, 100f, includeNonUrgent: true, ceilingKg: 25f, runningMassKg: 0f, maxStacks: 24);
            Assert.That(r.Count, Is.EqualTo(3));
            Assert.That(r[0].Take, Is.EqualTo(10));
            Assert.That(r[1].Take, Is.EqualTo(10));
            Assert.That(r[2].Take, Is.EqualTo(5)); // partial third, the ceiling binds mid-stack

            int total = 0;
            foreach (var t in r)
                total += t.Take;
            Assert.That(total, Is.EqualTo(25)); // 25 units × 1 kg = the 25 kg ceiling exactly, never over
        }

        [Test]
        public void MaxStacks_Respected()
        {
            var cands = Cands(
                C(1, 1f, urgent: true, unitMass: 1f, stack: 10),
                C(2, 2f, urgent: true, unitMass: 1f, stack: 10),
                C(3, 3f, urgent: true, unitMass: 1f, stack: 10));
            var r = Select(cands, 100f, includeNonUrgent: true, Inf, 0f, maxStacks: 2);
            Assert.That(r.Count, Is.EqualTo(2)); // capped, even though a third would fit under the (infinite) ceiling
            Assert.That(r[0].ThingId, Is.EqualTo(1));
            Assert.That(r[1].ThingId, Is.EqualTo(2));
        }

        [Test]
        public void TooHeavyForRoom_IsSkipped_LighterLaterStillFits()
        {
            // 5 kg of room (running 20, ceiling 25). The nearer candidate weighs 10 kg/unit (fits 0) and is skipped
            // (not a hard stop) so the lighter farther one (1 kg/unit, 3 units) still gets pocketed.
            var cands = Cands(
                C(1, 1f, urgent: true, unitMass: 10f, stack: 1),
                C(2, 2f, urgent: true, unitMass: 1f, stack: 3));
            var r = Select(cands, 100f, includeNonUrgent: true, ceilingKg: 25f, runningMassKg: 20f, maxStacks: 24);
            Assert.That(r.Count, Is.EqualTo(1));
            Assert.That(r[0].ThingId, Is.EqualTo(2));
            Assert.That(r[0].Take, Is.EqualTo(3));
        }

        // ── determinism ────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Deterministic_AcrossShuffledInput()
        {
            // Same set, two input orders → identical selection (ids AND takes, in the same order). Urgent (id3
            // dist2, id2 dist5) lead the non-urgent (id1 dist1, id4 dist3); ceiling 30 kg / 1 kg-unit / 10-stacks
            // fills the first three (10+10+10) and leaves id4 nothing.
            var a = new List<Candidate>
            {
                C(1, 1f, urgent: false), C(2, 25f, urgent: true), C(3, 4f, urgent: true), C(4, 9f, urgent: false)
            };
            var b = new List<Candidate>
            {
                C(4, 9f, urgent: false), C(2, 25f, urgent: true), C(1, 1f, urgent: false), C(3, 4f, urgent: true)
            };
            var ra = Select(a, 100f, includeNonUrgent: true, ceilingKg: 30f, runningMassKg: 0f, maxStacks: 24);
            var rb = Select(b, 100f, includeNonUrgent: true, ceilingKg: 30f, runningMassKg: 0f, maxStacks: 24);

            Assert.That(Sig(ra), Is.EqualTo(Sig(rb)));
            // And the concrete order: urgent nearest-first (id3, id2), then non-urgent nearest-first (id1), then the
            // ceiling is full so id4 is dropped.
            Assert.That(ra.Count, Is.EqualTo(3));
            Assert.That(ra[0].ThingId, Is.EqualTo(3));
            Assert.That(ra[1].ThingId, Is.EqualTo(2));
            Assert.That(ra[2].ThingId, Is.EqualTo(1));
        }

        [Test]
        public void MasslessCandidates_AllTaken_UpToMaxStacks()
        {
            // Unit mass 0 → the ceiling never advances, so every massless neighbour is taken until the stack cap.
            var cands = Cands(
                C(1, 1f, urgent: true, unitMass: 0f, stack: 5),
                C(2, 2f, urgent: true, unitMass: 0f, stack: 5),
                C(3, 3f, urgent: true, unitMass: 0f, stack: 5));
            var r = Select(cands, 100f, includeNonUrgent: true, ceilingKg: 1f, runningMassKg: 5f, maxStacks: 24);
            Assert.That(r.Count, Is.EqualTo(3));
            foreach (var t in r)
                Assert.That(t.Take, Is.EqualTo(5)); // whole massless stacks
        }
    }
}
