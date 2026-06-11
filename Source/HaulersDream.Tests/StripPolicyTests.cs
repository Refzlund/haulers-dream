using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class StripPolicyTests
    {
        [Test]
        public void Untainted_IsAlwaysTaken_RegardlessOfPolicies()
        {
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                {
                    Assert.That(StripPolicy.ApparelAction(false, true, sm, non), Is.EqualTo(TaintedApparelPolicy.Take));
                    Assert.That(StripPolicy.ApparelAction(false, false, sm, non), Is.EqualTo(TaintedApparelPolicy.Take));
                }
        }

        [Test]
        public void TaintedSmeltable_FollowsTheSmeltablePolicy()
        {
            Assert.That(StripPolicy.ApparelAction(true, true,
                    TaintedApparelPolicy.DropAndForbid, TaintedApparelPolicy.Destroy),
                Is.EqualTo(TaintedApparelPolicy.DropAndForbid));
        }

        [Test]
        public void TaintedNonSmeltable_FollowsTheNonSmeltablePolicy()
        {
            Assert.That(StripPolicy.ApparelAction(true, false,
                    TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse),
                Is.EqualTo(TaintedApparelPolicy.LeaveOnCorpse));
        }

        [Test]
        public void TaintedCategories_AreIndependent()
        {
            // Keep the smeltable armor, burn the rags with the body.
            Assert.That(StripPolicy.ApparelAction(true, true,
                    TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse),
                Is.EqualTo(TaintedApparelPolicy.Take));
            Assert.That(StripPolicy.ApparelAction(true, false,
                    TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse),
                Is.EqualTo(TaintedApparelPolicy.LeaveOnCorpse));
        }
    }
}
