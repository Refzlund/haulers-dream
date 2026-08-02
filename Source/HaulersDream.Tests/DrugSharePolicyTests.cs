using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the clause set — and the clause ORDER — behind "a colonist in withdrawal may take a kept drug from
    /// another colonist" (issue #229). The order is load-bearing: the feature/vanilla gates must short-circuit
    /// before any scan, and the HD-pinned check must refuse a vanilla-held stack before its count is ever read.
    /// </summary>
    [TestFixture]
    public class DrugSharePolicyTests
    {
        /// <summary>Every clause satisfied — the happy path all the rejection cases perturb one field of.</summary>
        private static DrugShareVerdict Evaluate(
            bool featureEnabled = true, bool vanillaFoundDrug = false, bool seekerHasChemicalNeed = true,
            bool holderEligible = true, bool stackPinnedByHaulersDream = true, int heldUnits = 5)
            => DrugSharePolicy.Evaluate(featureEnabled, vanillaFoundDrug, seekerHasChemicalNeed,
                holderEligible, stackPinnedByHaulersDream, heldUnits);

        // ── the verdicts ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void AllClausesSatisfied_Allows()
        {
            Assert.That(Evaluate(), Is.EqualTo(DrugShareVerdict.Allow));
        }

        [Test]
        public void FeatureOff_Refuses()
        {
            Assert.That(Evaluate(featureEnabled: false), Is.EqualTo(DrugShareVerdict.FeatureOff));
        }

        [Test]
        public void VanillaFoundDrug_Refuses()
        {
            // HD only fills the gap vanilla cannot see into; it must never override a source vanilla found.
            Assert.That(Evaluate(vanillaFoundDrug: true), Is.EqualTo(DrugShareVerdict.VanillaFoundDrug));
        }

        [Test]
        public void NoChemicalNeed_Refuses()
        {
            Assert.That(Evaluate(seekerHasChemicalNeed: false), Is.EqualTo(DrugShareVerdict.NoChemicalNeed));
        }

        [Test]
        public void HolderNotEligible_Refuses()
        {
            Assert.That(Evaluate(holderEligible: false), Is.EqualTo(DrugShareVerdict.HolderNotEligible));
        }

        [Test]
        public void StackNotPinnedByHaulersDream_Refuses()
        {
            // THE scoping rule: a drug vanilla put in that inventory stays as invisible as it is in vanilla.
            Assert.That(Evaluate(stackPinnedByHaulersDream: false),
                Is.EqualTo(DrugShareVerdict.NotPinnedByHaulersDream));
        }

        [Test]
        public void TaggedButNoSurplus_Refuses_SoAPolicyStashIsNeverDrained()
        {
            // The leak this pins: HD's tag self-heal adopts any untagged stack whose DEF matches something the
            // pawn hauled, so the 2 go-juice a colonist's DRUG POLICY keeps in the same pack end up tagged too.
            // Tag membership is therefore not proof HD is why the stack is held. The game layer resolves that by
            // reporting stackPinnedByHaulersDream only when the stack is ALSO still surplus above the holder's own
            // keep-stock; a stash sitting exactly at its policy count has zero surplus and must be refused here.
            Assert.That(Evaluate(stackPinnedByHaulersDream: false, heldUnits: 2),
                Is.EqualTo(DrugShareVerdict.NotPinnedByHaulersDream));
        }

        [Test]
        public void NothingHeld_Refuses()
        {
            Assert.That(Evaluate(heldUnits: 0), Is.EqualTo(DrugShareVerdict.NothingHeld));
        }

        // ── clause ORDER ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void FeatureOff_ShortCircuitsEverythingElse()
        {
            // With the feature off, nothing downstream may be consulted — including a scan the player disabled.
            Assert.That(
                DrugSharePolicy.Evaluate(featureEnabled: false, vanillaFoundDrug: true,
                    seekerHasChemicalNeed: false, holderEligible: false,
                    stackPinnedByHaulersDream: false, heldUnits: 0),
                Is.EqualTo(DrugShareVerdict.FeatureOff));
        }

        [Test]
        public void ClauseOrder_IsStable()
        {
            // Each case fails EVERY clause from its own position onward; the reported verdict must be the FIRST
            // one, which is what lets the game layer hoist the cheap gates ahead of the expensive scan.
            Assert.That(
                DrugSharePolicy.Evaluate(true, true, false, false, false, 0),
                Is.EqualTo(DrugShareVerdict.VanillaFoundDrug));
            Assert.That(
                DrugSharePolicy.Evaluate(true, false, false, false, false, 0),
                Is.EqualTo(DrugShareVerdict.NoChemicalNeed));
            Assert.That(
                DrugSharePolicy.Evaluate(true, false, true, false, false, 0),
                Is.EqualTo(DrugShareVerdict.HolderNotEligible));
            Assert.That(
                DrugSharePolicy.Evaluate(true, false, true, true, false, 0),
                Is.EqualTo(DrugShareVerdict.NotPinnedByHaulersDream));
            Assert.That(
                DrugSharePolicy.Evaluate(true, false, true, true, true, 0),
                Is.EqualTo(DrugShareVerdict.NothingHeld));
        }

        // ── the dose ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void UnitsToTake_IsOneDosePerTrip()
        {
            // Vanilla's own two TakeFromOtherInventory sites both set count = 1, so a take never drains a holder.
            Assert.That(DrugSharePolicy.UnitsToTake(0), Is.EqualTo(0));
            Assert.That(DrugSharePolicy.UnitsToTake(1), Is.EqualTo(1));
            Assert.That(DrugSharePolicy.UnitsToTake(99), Is.EqualTo(1));
            Assert.That(DrugSharePolicy.UnitsToTake(-4), Is.EqualTo(0));
        }
    }
}
