using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the classifier that keeps Hauler's Dream from diverting a pawn off doctoring / rescue / firefighting
    /// work to unload first (the reported "no one tends the bleeding after a fight / rescue never happens / fires
    /// ignored"). The load-bearing case is that RESCUE is a non-emergency Doctor-worktype giver, so the worktype
    /// set — not just the emergency flags — must catch it; and that ordinary work is NOT protected, so the
    /// opportunistic-unload feature still works.
    /// </summary>
    [TestFixture]
    public class ProtectedWorkPolicyTests
    {
        [Test]
        public void EmergencyNode_IsProtected_RegardlessOfWorkType()
        {
            // The JobGiver_Work emergency node issues tend/rescue/firefight/take-to-bed; its flag alone protects.
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(nodeIsEmergency: true, workGiverEmergency: false, workTypeDefName: "Hauling"), Is.True);
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(true, false, null), Is.True);
        }

        [Test]
        public void WorkGiverEmergency_IsProtected()
        {
            // FightFires is Firefighter + emergency; catches an emergency giver even via the normal node.
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, workGiverEmergency: true, "Firefighter"), Is.True);
        }

        [Test]
        public void DoctorWorkType_IsProtected_EvenWhenNotEmergency()
        {
            // Rescue (WorkGiver_RescueDowned, def DoctorRescue) and take-to-bed-to-operate are Doctor worktype but
            // NOT flagged emergency in vanilla — so the worktype gate is what saves rescue/tend from the divert.
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Doctor"), Is.True);
        }

        [Test]
        public void FirefighterAndWarden_WorkTypes_AreProtected()
        {
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Firefighter"), Is.True);
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Warden"), Is.True);
        }

        [Test]
        public void OrdinaryWork_IsNotProtected_SoTheFeatureStillWorks()
        {
            // A hauler/miner/cleaner carrying scooped goods must STILL be divertible to an opportunistic unload —
            // the fix is additive and must not disable the feature for ordinary work.
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Hauling"), Is.False);
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Mining"), Is.False);
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Cleaning"), Is.False);
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, "Cooking"), Is.False);
        }

        [Test]
        public void NullWorkType_AndNotEmergency_IsNotProtected()
        {
            Assert.That(ProtectedWorkPolicy.IsProtectedWork(false, false, null), Is.False);
        }

        [Test]
        public void IsProtectedWorkType_MatchesTheSet()
        {
            Assert.That(ProtectedWorkPolicy.IsProtectedWorkType("Doctor"), Is.True);
            Assert.That(ProtectedWorkPolicy.IsProtectedWorkType("Firefighter"), Is.True);
            Assert.That(ProtectedWorkPolicy.IsProtectedWorkType("Warden"), Is.True);
            Assert.That(ProtectedWorkPolicy.IsProtectedWorkType("Hauling"), Is.False);
            Assert.That(ProtectedWorkPolicy.IsProtectedWorkType(null), Is.False);
        }

        // --- the #107 divert-gate split (issue: a doctor carries scooped organs through an elective-surgery queue,
        // never unloading). A protected job is now sorted into exactly one of three buckets: HARD-BLOCK (a true
        // emergency, never touched), ZERO-DETOUR-ELIGIBLE (a non-emergency surgery, may shed a load that is already
        // on the way), or carry-on (rescue/warden, still never diverted but not hard-blocked). These two predicates
        // are the safety boundary, so the emergency-vs-non-emergency line is pinned here, not left in the postfix. ---

        [Test]
        public void HardBlock_OnlyWhenProtectedAndEmergency()
        {
            // A tend-the-bleeding / firefight / emergency take-to-bed job: never diverted for any reason (#107).
            Assert.That(ProtectedWorkPolicy.MustHardBlockDivert(isProtected: true, isEmergency: true), Is.True);
            // Non-emergency protected work (elective surgery, rescue, warden) is NOT hard-blocked: it may still be
            // considered for the zero-detour pass-by below.
            Assert.That(ProtectedWorkPolicy.MustHardBlockDivert(isProtected: true, isEmergency: false), Is.False);
            // Ordinary work is never hard-blocked here (the normal divert path governs it).
            Assert.That(ProtectedWorkPolicy.MustHardBlockDivert(isProtected: false, isEmergency: false), Is.False);
            // Defensive: an emergency flag implies protected in practice (emergency => protected), so this row is
            // unreachable from the live inputs, but the pure predicate still must not hard-block unprotected work.
            Assert.That(ProtectedWorkPolicy.MustHardBlockDivert(isProtected: false, isEmergency: true), Is.False);
        }

        [Test]
        public void ZeroDetour_OnlyForNonEmergencySurgery()
        {
            // The reported case: a non-emergency DoBill (surgery) may shed a load that sits on the path. This is the
            // ONLY protected bucket that opens the zero-detour door.
            Assert.That(ProtectedWorkPolicy.MayZeroDetourUnload(isProtected: true, isEmergency: false, isDoBill: true), Is.True);
        }

        [Test]
        public void ZeroDetour_NeverForAnEmergency()
        {
            // A surgery flagged emergency (patient bleeding out on the table) keeps the hard block: no drop, however
            // free, may sit between the doctor and the operation.
            Assert.That(ProtectedWorkPolicy.MayZeroDetourUnload(isProtected: true, isEmergency: true, isDoBill: true), Is.False);
        }

        [Test]
        public void ZeroDetour_NeverForRescueOrWarden()
        {
            // Rescue / warden work is protected and non-emergency, but it is NOT a DoBill, so it stays carry-on:
            // its urgency should not be delayed even by a free drop. Only surgery (DoBill) qualifies.
            Assert.That(ProtectedWorkPolicy.MayZeroDetourUnload(isProtected: true, isEmergency: false, isDoBill: false), Is.False);
        }

        [Test]
        public void ZeroDetour_NeverForOrdinaryWork()
        {
            // Unprotected work never routes through the zero-detour path (the normal, richer divert gate handles it),
            // even for a DoBill that is not protected (a crafting bill at a workbench).
            Assert.That(ProtectedWorkPolicy.MayZeroDetourUnload(isProtected: false, isEmergency: false, isDoBill: true), Is.False);
        }
    }
}
