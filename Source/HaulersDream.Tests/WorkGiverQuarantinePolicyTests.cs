using System;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the one decision in Hauler's Dream that can permanently remove a kind of work from a save (issue
    /// #235): whether a fault at the work-selection seam may switch the responsible work giver off for the
    /// session. It is destructive twice over — the work stops happening AND the player's right-click
    /// "prioritise" for it silently does nothing, because vanilla gates that path on the same
    /// <c>PawnCanUseWorkGiver</c>.
    ///
    /// <para><b>Both directions are failure, and both were caught in review.</b> Too permissive and HD switches
    /// off an innocent (usually vanilla) work giver, reproducing #235's symptom with HD as the cause. Too strict
    /// and it refuses the reported shape itself — the mod hooked into that giver had patched a VANILLA giver
    /// type, so any rule asking "does this giver belong to a mod?" locks out the case the feature exists for.
    /// The rule that satisfies both asks whether a MOD is demonstrably implicated in this giver, on every
    /// attribution route alike. Each test below is one wrong outcome, refused or admitted deliberately.</para>
    ///
    /// <para><b>These are the SWITCH-OFF rules only.</b> Whether the player is told a mod's NAME is a separate,
    /// stricter question with its own tests — see <see cref="WorkGiverNamingPolicyTests"/>. Clearing the bar
    /// here means a mod is demonstrably mixed into this giver; it never means that mod threw.</para>
    /// </summary>
    [TestFixture]
    public class WorkGiverQuarantinePolicyTests
    {
        // The evidence shape of a mod-caused seam fault, used as the baseline each test perturbs by ONE fact.
        private static QuarantineVerdict Decide(
            GiverAttribution attribution = GiverAttribution.FrameWalk,
            bool giverIsHaulersDream = false,
            bool giverIsPatchedByAMod = true,
            bool originIsModOwned = true,
            int faultCount = WorkGiverQuarantinePolicy.FaultsBeforeQuarantine)
            => WorkGiverQuarantinePolicy.Decide(attribution, giverIsHaulersDream, giverIsPatchedByAMod,
                originIsModOwned, faultCount);

        // --- the case the feature exists for ---------------------------------------------------------------

        [Test]
        public void ModPatchedVanillaGiver_NamedOnlyByScanContext_IsQuarantined()
        {
            // THE REPORTED SHAPE. A mod postfixes RimWorld.WorkGiver_ConstructFinishFrames, so the giver type is
            // VANILLA'S, and Harmony's replacement is a DynamicMethod whose GetMethod() returns null — the frame
            // walk names nothing and the route is ScanContext. An earlier revision required the giver's own
            // assembly to be a mod's, which refused this at every fault count forever: the fix was inert for the
            // shape it exists for. What links the mod to the giver is the PATCH — which is enough to switch the
            // work off and, on its own, never enough to name anyone.
            Assert.That(
                Decide(attribution: GiverAttribution.ScanContext, giverIsPatchedByAMod: true, originIsModOwned: true),
                Is.EqualTo(QuarantineVerdict.Quarantine));
        }

        [Test]
        public void ModPatchesTheGiver_IsEnoughOnItsOwn_EvenWithNoModFrameInTheTrace()
        {
            // A mod's TRANSPILER on a vanilla giver leaves only vanilla frames behind, so the trace implicates
            // nobody — but the mod's edits are demonstrably running inside that giver's job call.
            Assert.That(
                Decide(attribution: GiverAttribution.ScanContext, giverIsPatchedByAMod: true, originIsModOwned: false),
                Is.EqualTo(QuarantineVerdict.Quarantine));
        }

        [Test]
        public void ModFrameInTheTrace_IsEnoughOnItsOwn_EvenIfNoModPatchesTheGiver()
        {
            // The mirror: a mod's own code is visibly in the stack, so it is implicated whether or not it
            // reached the giver through a Harmony patch.
            Assert.That(Decide(giverIsPatchedByAMod: false, originIsModOwned: true),
                Is.EqualTo(QuarantineVerdict.Quarantine));
        }

        // --- the refusals ----------------------------------------------------------------------------------

        [Test]
        public void ModdedDataPoisoningAnUnpatchedVanillaGiver_IsNeverQuarantined()
        {
            // THE OTHER BLOCKING CASE. A modded ThingDef makes an unpatched vanilla giver throw: a real
            // RimWorld.WorkGiver_* frame resolves, so this arrives by the STRONG route — and must still be
            // refused. No mod patches the giver and no mod frame is in the trace, so switching that work type off
            // would tell the player RimWorld itself is broken. Containment alone already keeps the pawn working.
            Assert.That(Decide(giverIsPatchedByAMod: false, originIsModOwned: false),
                Is.EqualTo(QuarantineVerdict.NoModImplicated));
        }

        [Test]
        public void StaleScanContextNamingAnUnpatchedVanillaGiver_IsNeverQuarantined()
        {
            // A mod postfixes TryIssueJobPackage and throws after the scan finished, so no giver frame exists and
            // the scan context names whichever giver passed the gate last. Nothing links a mod to THAT giver.
            Assert.That(
                Decide(attribution: GiverAttribution.ScanContext, giverIsPatchedByAMod: false,
                    originIsModOwned: false),
                Is.EqualTo(QuarantineVerdict.NoModImplicated));
        }

        [Test]
        public void EveryRouteFacesTheSameBar_TheFrameWalkIsNotExempt()
        {
            // An earlier revision applied the corroboration only to the weak route, which let the frame walk
            // blame vanilla — the blocked outcome by the other door. Identical evidence must give an identical
            // verdict whichever route named the giver.
            Assert.That(Decide(attribution: GiverAttribution.FrameWalk, giverIsPatchedByAMod: false,
                    originIsModOwned: false),
                Is.EqualTo(Decide(attribution: GiverAttribution.ScanContext, giverIsPatchedByAMod: false,
                    originIsModOwned: false)));
            Assert.That(Decide(attribution: GiverAttribution.FrameWalk, giverIsPatchedByAMod: true,
                    originIsModOwned: false),
                Is.EqualTo(Decide(attribution: GiverAttribution.ScanContext, giverIsPatchedByAMod: true,
                    originIsModOwned: false)));
        }

        [Test]
        public void PostfixThrew_NoGiverContextAtAll_IsNeverQuarantined()
        {
            // Nothing named a giver by either route: there is nothing to switch off, and a guess is not allowed
            // to become one.
            Assert.That(Decide(attribution: GiverAttribution.None), Is.EqualTo(QuarantineVerdict.NoGiverAttributed));
        }

        [Test]
        public void NoGiverAttributed_IsRefusedEvenWithEveryOtherFactFavourable()
        {
            // The refusal is structural, not a tie-break: no amount of corroboration invents a culprit.
            Assert.That(
                Decide(attribution: GiverAttribution.None, giverIsPatchedByAMod: true, originIsModOwned: true,
                    faultCount: 999),
                Is.EqualTo(QuarantineVerdict.NoGiverAttributed));
        }

        // --- HD's own bugs stay loud -----------------------------------------------------------------------

        [Test]
        public void HaulersDreamsOwnGiver_IsNeverQuarantined()
        {
            // Mirrors Patch_JobGiver_Work_WorkGiverResilient (issue #7): hiding an HD bug behind a switched-off
            // feature is the one outcome the no-swallow rule forbids outright.
            Assert.That(Decide(giverIsHaulersDream: true), Is.EqualTo(QuarantineVerdict.OwnGiver));
        }

        [Test]
        public void HaulersDreamsOwnGiver_OutranksEveryQuarantineFavourableFact()
        {
            Assert.That(
                Decide(giverIsHaulersDream: true, giverIsPatchedByAMod: true, originIsModOwned: true,
                    faultCount: 999),
                Is.EqualTo(QuarantineVerdict.OwnGiver));
        }

        // --- the threshold ---------------------------------------------------------------------------------

        [Test]
        public void BelowTheThreshold_IsNotQuarantined()
        {
            // A single transient fault (a malformed thing, a mid-map-generation null) must not cost a work type.
            for (int count = 1; count < WorkGiverQuarantinePolicy.FaultsBeforeQuarantine; count++)
                Assert.That(Decide(faultCount: count), Is.EqualTo(QuarantineVerdict.BelowThreshold),
                    "fault #" + count + " is below the threshold and must not quarantine");
        }

        [Test]
        public void PastTheThreshold_StillQuarantines_SoWeakEarlyFaultsCannotExemptAGiverForever()
        {
            // The threshold is AT-OR-ABOVE, not exact equality. The evidence facts vary per fault, so a giver
            // whose first three faults were refused (no mod implicated yet) must still be switchable off when a
            // later fault finally carries the evidence — under exact equality it never could be again.
            Assert.That(Decide(faultCount: WorkGiverQuarantinePolicy.FaultsBeforeQuarantine + 1),
                Is.EqualTo(QuarantineVerdict.Quarantine));
            Assert.That(Decide(faultCount: 999), Is.EqualTo(QuarantineVerdict.Quarantine));
        }

        [Test]
        public void ThresholdIsAboveOne_SoASingleTransientFaultCanNeverDisableWork()
        {
            Assert.That(WorkGiverQuarantinePolicy.FaultsBeforeQuarantine, Is.GreaterThan(1));
        }

        // --- verdict-set shape -----------------------------------------------------------------------------

        [Test]
        public void ExactlyOneVerdictQuarantines()
        {
            // The refusal reasons exist to be reported, so a future edit must not collapse them into a bool —
            // and only ONE member may mean "switch it off".
            var quarantining = Enum.GetValues(typeof(QuarantineVerdict)).Cast<QuarantineVerdict>()
                .Where(v => v == QuarantineVerdict.Quarantine).ToList();
            Assert.That(quarantining.Count, Is.EqualTo(1));
            Assert.That(Enum.GetValues(typeof(QuarantineVerdict)).Length, Is.GreaterThan(1),
                "the refusal reasons must stay distinguishable in the log");
        }
    }
}
