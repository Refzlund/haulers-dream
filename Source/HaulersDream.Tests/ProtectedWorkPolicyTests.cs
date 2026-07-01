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
    }
}
