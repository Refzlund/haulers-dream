using System;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins WHO Hauler's Dream is allowed to name when it switches a work type off (issue #235). The shipped
    /// version answered that from Harmony patch ownership with Hauler's Dream's own id filtered out of the
    /// candidate list — so whichever third party was also hooked into the method got printed to every player as
    /// "the mod responsible", and would have been printed even if Hauler's Dream's own postfix had thrown.
    ///
    /// <para><b>The tests that matter here are the REFUSALS.</b> One test states the only fact that may name
    /// anyone; the rest state, one at a time and then together, that the involvement facts may not — which is
    /// precisely what a future "we already know a mod is mixed in, just print it" edit would undo. They are only
    /// expressible because <see cref="WorkGiverNamingPolicy.Decide"/> takes those facts as parameters instead of
    /// leaving them out.</para>
    /// </summary>
    [TestFixture]
    public class WorkGiverNamingPolicyTests
    {
        // The evidence shape of a fault whose stack names a mod, used as the baseline each test perturbs by ONE
        // fact. Every parameter is a positive observation, so a false is "not observed", never "ruled out".
        private static QuarantineNaming Decide(
            bool exceptionCarriesModOwnedFrame = true,
            bool aModPatchesTheGiver = true,
            bool theGiverTypeBelongsToAMod = false)
            => WorkGiverNamingPolicy.Decide(exceptionCarriesModOwnedFrame, aModPatchesTheGiver,
                theGiverTypeBelongsToAMod);

        // --- the one fact that names anyone ----------------------------------------------------------------

        [Test]
        public void AModOwnedFrameInTheTrace_IsTheOneFactThatNamesAMod()
        {
            // A mod's own compiled method was on the path between the throw site and the observing finalizer.
            // That is the only observation which distinguishes "this mod's code ran here" from "this mod is
            // installed"; it names the mod as the most likely source, never as proven.
            Assert.That(Decide(exceptionCarriesModOwnedFrame: true), Is.EqualTo(QuarantineNaming.NameTheMod));
        }

        [Test]
        public void AModOwnedFrame_NamesTheMod_EvenWithNoOtherEvidenceAtAll()
        {
            // The frame stands on its own: nothing else has to corroborate it, because nothing else can.
            Assert.That(
                Decide(exceptionCarriesModOwnedFrame: true, aModPatchesTheGiver: false,
                    theGiverTypeBelongsToAMod: false),
                Is.EqualTo(QuarantineNaming.NameTheMod));
        }

        // --- the refusals ----------------------------------------------------------------------------------

        [Test]
        public void PatchOwnershipAlone_NamesNobody()
        {
            // THE BUG THIS RULE EXISTS TO REMOVE. A mod is hooked into the giver's job call and the trace resolves
            // nothing (a patched method runs as a DynamicMethod whose frame has no declaring type). Ownership says
            // who is hooked, never who threw — and Hauler's Dream was hooked into the very same method, so the old
            // "skip our own id and print the rest" shortcut would have named a third party for our own fault.
            Assert.That(
                Decide(exceptionCarriesModOwnedFrame: false, aModPatchesTheGiver: true),
                Is.EqualTo(QuarantineNaming.SourceUnknown));
        }

        [Test]
        public void AModShippingTheGiverType_NamesNobody()
        {
            // Declaring the type is not running the code. A modded giver whose call several mods have patched
            // tells us nothing about which of them threw.
            Assert.That(
                Decide(exceptionCarriesModOwnedFrame: false, aModPatchesTheGiver: false,
                    theGiverTypeBelongsToAMod: true),
                Is.EqualTo(QuarantineNaming.SourceUnknown));
        }

        [Test]
        public void EveryInvolvementFactAtOnce_StillNamesNobodyWithoutAFrame()
        {
            // Involvement does not accumulate into attribution. Two facts that each prove "a mod is mixed into
            // this giver" still prove nothing about who threw, so piling them up may not cross the bar.
            Assert.That(
                Decide(exceptionCarriesModOwnedFrame: false, aModPatchesTheGiver: true,
                    theGiverTypeBelongsToAMod: true),
                Is.EqualTo(QuarantineNaming.SourceUnknown));
        }

        [Test]
        public void TheFrameDecidesAlone_NeitherInvolvementFactCanChangeTheAnswer()
        {
            // Exhaustive over the two ignored facts: for a fixed frame answer, every combination agrees. This is
            // the property, stated directly — the individual refusals above name the cases it is made of.
            foreach (bool patched in new[] { false, true })
            {
                foreach (bool modGiver in new[] { false, true })
                {
                    Assert.That(
                        Decide(exceptionCarriesModOwnedFrame: true, aModPatchesTheGiver: patched,
                            theGiverTypeBelongsToAMod: modGiver),
                        Is.EqualTo(QuarantineNaming.NameTheMod),
                        "a frame names the mod regardless of involvement facts (patched=" + patched
                            + ", modGiver=" + modGiver + ")");
                    Assert.That(
                        Decide(exceptionCarriesModOwnedFrame: false, aModPatchesTheGiver: patched,
                            theGiverTypeBelongsToAMod: modGiver),
                        Is.EqualTo(QuarantineNaming.SourceUnknown),
                        "without a frame nobody is named regardless of involvement facts (patched=" + patched
                            + ", modGiver=" + modGiver + ")");
                }
            }
        }

        // --- the honest end state: switched off, source unknown ---------------------------------------------

        [Test]
        public void AGiverQuarantinedOnPatchEvidenceAlone_IsSwitchedOffWithoutNamingAnyone()
        {
            // The deliberate asymmetry between the two policies, stated as one case so it cannot drift apart.
            // The reported shape — a mod patches a VANILLA giver's JobOnThing, the trace resolves nothing —
            // clears the quarantine bar (a mod's code demonstrably runs inside that call) and fails the naming
            // bar (nothing places that mod, or any other, at the throw). Switched off, source unknown, log
            // pointed at. Any change that makes these two agree has re-added the false blame.
            Assert.That(
                WorkGiverQuarantinePolicy.Decide(GiverAttribution.ScanContext, giverIsHaulersDream: false,
                    giverIsPatchedByAMod: true, originIsModOwned: false,
                    faultCount: WorkGiverQuarantinePolicy.FaultsBeforeQuarantine),
                Is.EqualTo(QuarantineVerdict.Quarantine));
            Assert.That(
                WorkGiverNamingPolicy.Decide(exceptionCarriesModOwnedFrame: false, aModPatchesTheGiver: true,
                    theGiverTypeBelongsToAMod: false),
                Is.EqualTo(QuarantineNaming.SourceUnknown));
        }

        [Test]
        public void TheQuarantineDecisionStillReadsThePatchFact_SoRemovingItFromNamingDidNotDisarmTheFeature()
        {
            // Guards the other direction of the same edit: the patch fact was demoted from naming, NOT deleted.
            // If it stopped carrying the decision too, the reported bug would become unquarantinable again and
            // the whole containment feature would be inert for the case it exists for.
            Assert.That(
                WorkGiverQuarantinePolicy.Decide(GiverAttribution.ScanContext, giverIsHaulersDream: false,
                    giverIsPatchedByAMod: false, originIsModOwned: false,
                    faultCount: WorkGiverQuarantinePolicy.FaultsBeforeQuarantine),
                Is.EqualTo(QuarantineVerdict.NoModImplicated));
        }

        // --- verdict-set shape -----------------------------------------------------------------------------

        [Test]
        public void ExactlyOneOutcomeNamesAMod_AndTheOtherIsUnknownRatherThanADenial()
        {
            // Two members, one of which names. A third "probably mod X" tier is the shape this change removed,
            // and "not Hauler's Dream" is a claim no stack can support (a trace records what a method called,
            // never who called it), so neither may be added back without revisiting this test.
            var outcomes = Enum.GetValues(typeof(QuarantineNaming)).Cast<QuarantineNaming>().ToList();
            Assert.That(outcomes.Count, Is.EqualTo(2));
            Assert.That(outcomes.Count(v => v == QuarantineNaming.NameTheMod), Is.EqualTo(1));
            Assert.That(outcomes, Contains.Item(QuarantineNaming.SourceUnknown));
        }
    }
}
