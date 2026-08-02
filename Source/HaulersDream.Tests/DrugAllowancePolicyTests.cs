using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins vanilla's "the drug policy refuses this drug for an addiction" clause, and — the reason this policy
    /// exists at all (issue #232) — the answer it gives for a drug the policy has NO ENTRY for, where vanilla's
    /// own per-<c>ThingDef</c> indexer throws a message-less <c>ArgumentException</c> instead of answering.
    /// </summary>
    [TestFixture]
    public class DrugAllowancePolicyTests
    {
        /// <summary>
        /// The fully-refusing case — a policy that holds an entry marked NOT allowed for addiction, on a pawn
        /// with neither the Drug Desire trait nor a policy-ignoring mental state. Every test below perturbs one
        /// field of it.
        /// </summary>
        private static bool Blocks(
            bool hasPolicy = true, bool entryPresent = true, bool entryAllowedForAddiction = false,
            bool hasStory = true, int drugDesireDegree = 0, bool mentalStateIgnoresPolicy = false)
            => DrugAllowancePolicy.BlocksAddictionUse(hasPolicy, entryPresent, entryAllowedForAddiction,
                hasStory, drugDesireDegree, mentalStateIgnoresPolicy);

        // ── the refusal ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryClauseHolds_Blocks()
        {
            // The player's rehab lever, working: the drug is marked not allowed for addiction and nothing exempts
            // the pawn from its own policy.
            Assert.That(Blocks(), Is.True);
        }

        [Test]
        public void NoPolicy_DoesNotBlock()
        {
            // Vanilla's first clause. A pawn with no current drug policy is never refused.
            Assert.That(Blocks(hasPolicy: false), Is.False);
        }

        [Test]
        public void EntryAllowedForAddiction_DoesNotBlock()
        {
            Assert.That(Blocks(entryAllowedForAddiction: true), Is.False);
        }

        [Test]
        public void NoStory_DoesNotBlock()
        {
            // Vanilla reads traits only behind `pawn.story != null`, so a pawn without one is never refused —
            // and the trait degree it is handed is then a don't-care, which this asserts by passing the value
            // that WOULD refuse.
            Assert.That(Blocks(hasStory: false, drugDesireDegree: 0), Is.False);
        }

        [Test]
        public void MentalStateIgnoresPolicy_DoesNotBlock()
        {
            // Vanilla's own exemption: a binge and friends set MentalStateDef.ignoreDrugPolicy.
            Assert.That(Blocks(mentalStateIgnoresPolicy: true), Is.False);
        }

        // ── the #232 pin: a def the policy has no entry for ──────────────────────────────────────

        [Test]
        public void MissingEntry_DoesNotBlock_WhateverAllowedFlagIsPassed()
        {
            // THE issue-#232 case. Vanilla's DrugPolicy[ThingDef] indexer ends in a bare
            // `throw new ArgumentException();` for a def it holds no entry for, so there is no vanilla answer to
            // copy — this policy supplies the value InitializeIfNeeded WOULD have created (allowedForAddiction =
            // true), which is also the only value the player cannot have overridden: a def with no entry has no
            // row in the drug-policy dialog. Refusing instead would silently arm the rehab lever for a drug
            // nobody ever marked.
            //
            // Both flag values are asserted because the flag must be IGNORED, not merely happen to agree: a
            // caller with no entry has nothing meaningful to pass there.
            Assert.That(Blocks(entryPresent: false, entryAllowedForAddiction: false), Is.False);
            Assert.That(Blocks(entryPresent: false, entryAllowedForAddiction: true), Is.False);
        }

        [Test]
        public void MissingEntryAllowedForAddiction_IsTrue()
        {
            // Asserted BY NAME so that flipping the constant fails here instead of quietly changing whether a
            // colonist in withdrawal may reach a drug whose policy entry was never built (#229's lever, #232's
            // data). If this ever has to change, it must be a deliberate edit to a failing test.
            Assert.That(DrugAllowancePolicy.MissingEntryAllowedForAddiction, Is.True);
        }

        // ── the #232 gate: permitted is NOT the same as routable ─────────────────────────────────

        [Test]
        public void MissingEntry_IsNotRoutable_EvenThoughThePolicyDoesNotRefuseIt()
        {
            // The two questions, side by side, in the one case where they DISAGREE — which is why they are two
            // members and must never be collapsed into one. The policy does not refuse a drug it has no entry for
            // (above), but Hauler's Dream still must not route a pawn to it: the take moves a dose into the
            // seeker's own inventory, where vanilla's own DrugValidator re-checks it next think through the
            // unguarded `drugPolicy[drug.def]` that has no entry to find. The addict would be left holding a dose
            // it can never ingest and never shed, losing its whole drug-satisfaction node every scan.
            bool policyBlocks = Blocks(entryPresent: false);
            Assert.That(policyBlocks, Is.False, "a drug with no policy entry is not REFUSED by the policy");
            Assert.That(DrugAllowancePolicy.MayRouteToDrug(entryPresent: false, policyBlocks: policyBlocks),
                Is.False, "...but it is still not ROUTABLE, because vanilla cannot evaluate it");
        }

        [Test]
        public void MayRouteToDrug_TruthTable()
        {
            // Routable requires BOTH: an entry vanilla can evaluate, and a policy that permits it.
            Assert.That(DrugAllowancePolicy.MayRouteToDrug(entryPresent: true, policyBlocks: false), Is.True);
            Assert.That(DrugAllowancePolicy.MayRouteToDrug(entryPresent: true, policyBlocks: true), Is.False);
            Assert.That(DrugAllowancePolicy.MayRouteToDrug(entryPresent: false, policyBlocks: false), Is.False);
            Assert.That(DrugAllowancePolicy.MayRouteToDrug(entryPresent: false, policyBlocks: true), Is.False);
        }

        // ── the Drug Desire boundary ─────────────────────────────────────────────────────────────

        [Test]
        public void PositiveDrugDesire_DoesNotBlock()
        {
            // Vanilla's clause is `DegreeOfTrait(DrugDesire) <= 0`, so only a POSITIVE degree (chemical
            // interest / fascination) exempts the pawn from its own policy.
            Assert.That(Blocks(drugDesireDegree: 1), Is.False);
        }

        [Test]
        public void NegativeDrugDesire_StillBlocks()
        {
            // A teetotaler (degree -1) is not exempt: the boundary is at 0, not at "has the trait".
            Assert.That(Blocks(drugDesireDegree: -1), Is.True);
            Assert.That(Blocks(drugDesireDegree: 0), Is.True);
        }

        // ── the allowance itself ─────────────────────────────────────────────────────────────────

        [Test]
        public void AllowedForAddiction_TruthTable()
        {
            // With an entry the entry decides; without one the constant does, whatever flag is passed.
            Assert.That(DrugAllowancePolicy.AllowedForAddiction(entryPresent: true, entryAllowedForAddiction: true),
                Is.True);
            Assert.That(DrugAllowancePolicy.AllowedForAddiction(entryPresent: true, entryAllowedForAddiction: false),
                Is.False);
            Assert.That(DrugAllowancePolicy.AllowedForAddiction(entryPresent: false, entryAllowedForAddiction: true),
                Is.EqualTo(DrugAllowancePolicy.MissingEntryAllowedForAddiction));
            Assert.That(DrugAllowancePolicy.AllowedForAddiction(entryPresent: false, entryAllowedForAddiction: false),
                Is.EqualTo(DrugAllowancePolicy.MissingEntryAllowedForAddiction));
        }
    }
}
