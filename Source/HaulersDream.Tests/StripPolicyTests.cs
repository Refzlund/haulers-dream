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

        // ---- LeaveWhereItIs: the shared loose-piece intake guard (issue #187a) ----

        [Test]
        public void LeaveWhereItIs_TaintedNonSmeltable_LeaveOnCorpse_Leaves()
        {
            // The reporter's exact case: a tainted cloth rag with the non-smeltable category set to LeaveOnCorpse.
            Assert.That(StripPolicy.LeaveWhereItIs(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse), Is.True);
        }

        [Test]
        public void LeaveWhereItIs_DropAndForbid_Leaves()
        {
            Assert.That(StripPolicy.LeaveWhereItIs(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.DropAndForbid), Is.True);
            Assert.That(StripPolicy.LeaveWhereItIs(true, true,
                TaintedApparelPolicy.DropAndForbid, TaintedApparelPolicy.Take), Is.True);
        }

        [Test]
        public void LeaveWhereItIs_TaintedSmeltable_Take_DoesNotLeave()
        {
            // A tainted smeltable piece under Take is hauled home like ordinary loot — not left.
            Assert.That(StripPolicy.LeaveWhereItIs(true, true,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse), Is.False);
        }

        [Test]
        public void LeaveWhereItIs_Destroy_DoesNotLeave()
        {
            // A still-loose Destroy piece fell through the strip loop's quest/relic/merged guard and is treated as
            // loot (Take) there, so the intake guard must NOT strand it — Destroy resolves "don't leave".
            Assert.That(StripPolicy.LeaveWhereItIs(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.Destroy), Is.False);
            Assert.That(StripPolicy.LeaveWhereItIs(true, true,
                TaintedApparelPolicy.Destroy, TaintedApparelPolicy.Take), Is.False);
        }

        [Test]
        public void LeaveWhereItIs_Untainted_NeverLeaves_RegardlessOfPolicies()
        {
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                {
                    Assert.That(StripPolicy.LeaveWhereItIs(false, true, sm, non), Is.False);
                    Assert.That(StripPolicy.LeaveWhereItIs(false, false, sm, non), Is.False);
                }
        }

        [Test]
        public void LeaveWhereItIs_UsesTheApplicableCategoryOnly()
        {
            // Smeltable=DropAndForbid, non-smeltable=Take: only the smeltable piece is left.
            Assert.That(StripPolicy.LeaveWhereItIs(true, true,
                TaintedApparelPolicy.DropAndForbid, TaintedApparelPolicy.Take), Is.True);
            Assert.That(StripPolicy.LeaveWhereItIs(true, false,
                TaintedApparelPolicy.DropAndForbid, TaintedApparelPolicy.Take), Is.False);
        }

        // ---- LeavesAnyTainted: the cheap default-config pre-gate ----

        [Test]
        public void LeavesAnyTainted_DefaultTakeTake_IsFalse()
        {
            // The Take/Smelt defaults keep nothing out of storage -> the per-candidate apparel test is skippable.
            Assert.That(StripPolicy.LeavesAnyTainted(
                TaintedApparelPolicy.Take, TaintedApparelPolicy.Take), Is.False);
        }

        [Test]
        public void LeavesAnyTainted_DestroyIsNotALeavePolicy()
        {
            Assert.That(StripPolicy.LeavesAnyTainted(
                TaintedApparelPolicy.Destroy, TaintedApparelPolicy.Take), Is.False);
        }

        [Test]
        public void LeavesAnyTainted_TrueWhenEitherCategoryLeaves()
        {
            Assert.That(StripPolicy.LeavesAnyTainted(
                TaintedApparelPolicy.Take, TaintedApparelPolicy.LeaveOnCorpse), Is.True);
            Assert.That(StripPolicy.LeavesAnyTainted(
                TaintedApparelPolicy.DropAndForbid, TaintedApparelPolicy.Take), Is.True);
            Assert.That(StripPolicy.LeavesAnyTainted(
                TaintedApparelPolicy.LeaveOnCorpse, TaintedApparelPolicy.DropAndForbid), Is.True);
        }
    }
}
