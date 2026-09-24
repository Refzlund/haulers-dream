using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class LoadMenuProjectionTests
    {
        [Test]
        public void RepeatedProjectionKeepsManifestAndEveryClaimUnchanged()
        {
            var needed = new Dictionary<string, int> { ["steel"] = 80, ["wood"] = 20 };
            var total = new Dictionary<string, int> { ["steel"] = 70, ["wood"] = 10 };
            var own = new Dictionary<string, int> { ["steel"] = 30 };
            var other = new Dictionary<string, int> { ["steel"] = 40, ["wood"] = 10 };
            var claims = new Dictionary<int, Dictionary<string, int>> { [1] = own, [2] = other };
            for (int i = 0; i < 20; i++)
            {
                var projection = LoadLedger<string, int>.AvailableToClaim(needed, total, claims, 1);
                Assert.That(projection, Is.EquivalentTo(new Dictionary<string, int> { ["steel"] = 40, ["wood"] = 10 }));
                projection.Clear(); // A planner owns its returned view; it must not alias the ledger.
            }
            Assert.That(needed, Is.EquivalentTo(new Dictionary<string, int> { ["steel"] = 80, ["wood"] = 20 }));
            Assert.That(total, Is.EquivalentTo(new Dictionary<string, int> { ["steel"] = 70, ["wood"] = 10 }));
            Assert.That(own, Is.EquivalentTo(new Dictionary<string, int> { ["steel"] = 30 }));
            Assert.That(other, Is.EquivalentTo(new Dictionary<string, int> { ["steel"] = 40, ["wood"] = 10 }));
            Assert.That(claims.Count, Is.EqualTo(2));
            Assert.That(claims[1], Is.SameAs(own));
            Assert.That(claims[2], Is.SameAs(other));
        }

        [Test]
        public void MissingSavedEntryNeedsNoRegistrationAndDoesNotAliasManifest()
        {
            var needed = new Dictionary<string, int> { ["steel"] = 12 };
            var projection = LoadLedger<string, int>.AvailableToClaim(needed, null, null, 1);
            Assert.That(projection["steel"], Is.EqualTo(12));
            projection["steel"] = 0;
            Assert.That(needed["steel"], Is.EqualTo(12));
        }
    }
}
