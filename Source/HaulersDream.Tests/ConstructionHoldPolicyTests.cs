using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the guard that stops a builder walking its leftover material to the stockpile between two frames — and,
    /// just as importantly, pins the three ways that hold ALWAYS releases, so material can never be stranded: a
    /// forced unload, the never-strand time ceiling, and construction no longer wanting what is held.
    /// </summary>
    [TestFixture]
    public class ConstructionHoldPolicyTests
    {
        private static bool Hold(bool forced = false, bool wanted = true, int sinceIntake = 0,
            int maxHold = ConstructionHoldPolicy.MaxHoldTicks)
            => ConstructionHoldPolicy.ShouldHoldMaterial(forced, wanted, sinceIntake, maxHold);

        [Test]
        public void BuilderMidRun_HoldsItsMaterial()
        {
            // Just delivered to a frame, more construction still wants the leftover: keep it.
            Assert.That(Hold(), Is.True);
        }

        [Test]
        public void Forced_NeverHolds()
        {
            // The "Unload now" gizmo, a finish flush, the mech shed-before-charge: the caller asked, so it goes.
            Assert.That(Hold(forced: true), Is.False);
            Assert.That(Hold(forced: true, sinceIntake: 0), Is.False);
            Assert.That(ConstructionHoldPolicy.MayHoldAtAll(forced: true, ticksSinceLastIntake: 0), Is.False);
        }

        [Test]
        public void NothingNearbyWantsIt_ReleasesImmediately()
        {
            // The build finished / was cancelled / the pawn walked away: nothing to hold for.
            Assert.That(Hold(wanted: false), Is.False);
        }

        [Test]
        public void MaxHoldCeiling_ReleasesEvenWhileStillWanted()
        {
            // The never-strand escape. A pawn that has stopped picking anything up for the whole window releases,
            // however much a nearby site would still like the material.
            Assert.That(Hold(wanted: true, sinceIntake: ConstructionHoldPolicy.MaxHoldTicks - 1), Is.True);
            Assert.That(Hold(wanted: true, sinceIntake: ConstructionHoldPolicy.MaxHoldTicks), Is.False);
            Assert.That(Hold(wanted: true, sinceIntake: ConstructionHoldPolicy.MaxHoldTicks * 10), Is.False);
        }

        [Test]
        public void CheapPreGate_OnlyEverShortCircuits_NeverAdmits()
        {
            // MayHoldAtAll exists so the Verse layer can skip the (expensive) nearby-site search. It must never say
            // "may hold" where the full decision would refuse for the same cheap reasons, in either direction.
            foreach (bool forced in new[] { false, true })
                foreach (int since in new[] { 0, 1, ConstructionHoldPolicy.MaxHoldTicks - 1,
                             ConstructionHoldPolicy.MaxHoldTicks, ConstructionHoldPolicy.MaxHoldTicks + 1 })
                {
                    bool cheap = ConstructionHoldPolicy.MayHoldAtAll(forced, since);
                    Assert.That(ConstructionHoldPolicy.ShouldHoldMaterial(forced, true, since), Is.EqualTo(cheap),
                        $"forced={forced} since={since}: with the material wanted, the full decision IS the pre-gate");
                    if (!cheap)
                        Assert.That(ConstructionHoldPolicy.ShouldHoldMaterial(forced, false, since), Is.False);
                }
        }

        [Test]
        public void CeilingIsGenerous_ButFinite()
        {
            // A backstop, not the normal release: comfortably longer than the default settle window (2500 ticks,
            // one in-game hour) so an actively-working builder never hits it, and finite so a hold cannot last
            // forever. A regression that made it zero (or negative) would disable the fix outright.
            Assert.That(ConstructionHoldPolicy.MaxHoldTicks, Is.GreaterThan(2500));
            Assert.That(ConstructionHoldPolicy.MaxHoldTicks, Is.LessThan(60000));
        }

        [Test]
        public void PawnThatNeverPickedAnythingUp_DoesNotHold()
        {
            // The Verse layer feeds (now - lastYieldTick), and an unstamped comp's stamp is -99999 — so a pawn that
            // has never scooped anything reads as ancient and releases. That is right: it is not mid-run.
            Assert.That(Hold(sinceIntake: 99999), Is.False);
        }

        [Test]
        public void NegativeElapsed_IsTreatedAsFresh_NotExpired()
        {
            // Defensive: with no tick manager the Verse layer reads "now" as 0, so a stamp restored from a save can
            // make the delta negative. That must not read as "the ceiling expired" — it degrades to holding, which
            // at worst skips one automatic unload.
            Assert.That(Hold(sinceIntake: -50), Is.True);
        }
    }
}
