using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class MapEligibilityPolicyTests
    {
        private static bool Active(bool isPlayerHome, bool isPlayerControlled, bool enableOnNonHomeMaps,
            bool playerControlledOnly)
            => MapEligibilityPolicy.HdActiveOnMap(isPlayerHome, isPlayerControlled, enableOnNonHomeMaps,
                playerControlledOnly);

        [Test]
        public void PlayerHome_AlwaysActive_EvenWithMasterOff()
        {
            // A player-home colony short-circuits to active regardless of the off-home settings.
            Assert.That(Active(isPlayerHome: true, isPlayerControlled: true, enableOnNonHomeMaps: false,
                playerControlledOnly: false), Is.True);
        }

        [Test]
        public void PlayerHome_IgnoresControlledOnlyScope()
        {
            // Home never consults isPlayerControlled, so an uncontrolled-looking home map still runs.
            Assert.That(Active(isPlayerHome: true, isPlayerControlled: false, enableOnNonHomeMaps: true,
                playerControlledOnly: true), Is.True);
        }

        [Test]
        public void NonHome_MasterOff_Inert()
        {
            // enableOnNonHomeMaps off = HD is fully inert off-home (the nomad "no home" inert trap): even a
            // player-controlled camp is skipped until the master toggle is on.
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: true, enableOnNonHomeMaps: false,
                playerControlledOnly: false), Is.False);
        }

        [Test]
        public void NonHome_PlayerMap_AllMaps_Active()
        {
            // Master on, controlled-only off -> the default "work on every non-home map" behaviour.
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: true, enableOnNonHomeMaps: true,
                playerControlledOnly: false), Is.True);
        }

        [Test]
        public void NonHome_PlayerMap_ControlledOnly_Active()
        {
            // A player-controlled camp passes the controlled-only scope.
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: true, enableOnNonHomeMaps: true,
                playerControlledOnly: true), Is.True);
        }

        [Test]
        public void NonHome_EnemyMap_AllMaps_Active()
        {
            // Controlled-only off: HD still works on an uncontrolled map (the shipped raider-default behaviour).
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: false, enableOnNonHomeMaps: true,
                playerControlledOnly: false), Is.True);
        }

        [Test]
        public void NonHome_EnemyMap_ControlledOnly_Blocked()
        {
            // The feature's whole point: an uncontrolled ambush / enemy map is stood down when scoped.
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: false, enableOnNonHomeMaps: true,
                playerControlledOnly: true), Is.False);
        }

        [Test]
        public void NonHome_MasterOff_ControlledOnlyIrrelevant_Inert()
        {
            // Master off takes precedence over the scope flag (uncontrolled map, both off-home gates against it).
            Assert.That(Active(isPlayerHome: false, isPlayerControlled: false, enableOnNonHomeMaps: false,
                playerControlledOnly: true), Is.False);
        }
    }
}
