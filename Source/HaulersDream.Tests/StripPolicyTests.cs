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

        // ---- StaysOnCorpse: the per-piece "HD refuses to take it off" rule ----
        //
        // This is the predicate that keeps vanilla's "is there anything to strip?" answer honest. When it drifts
        // from the drop filter, a manual Strip order deletes its own designation, drops nothing, and can be
        // re-placed forever — the silent no-op these tests exist to prevent.

        /// <summary>
        /// The whole truth table, so a change to <see cref="StripPolicy.ApparelAction"/> that reshuffles which
        /// resolution keeps a piece worn fails here rather than in a save.
        /// </summary>
        [Test]
        public void StaysOnCorpse_IsTrueForExactlyTheLeaveOnCorpseResolution()
        {
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                    foreach (bool tainted in new[] { true, false })
                        foreach (bool smeltable in new[] { true, false })
                        {
                            var action = StripPolicy.ApparelAction(tainted, smeltable, sm, non);
                            Assert.That(StripPolicy.StaysOnCorpse(tainted, smeltable, sm, non),
                                Is.EqualTo(action == TaintedApparelPolicy.LeaveOnCorpse),
                                $"tainted={tainted} smeltable={smeltable} sm={sm} non={non} -> {action}");
                        }
        }

        /// <summary>
        /// ORACLE: the predicate must reject exactly the pieces HD's injected <c>Pawn_ApparelTracker.DropAll</c>
        /// selector rejects. The selector was written inline before this fix, and <see cref="DropAllSelectorOracle"/>
        /// restates that original code verbatim — so this pins the extraction as behaviour-preserving AND keeps
        /// the two definitions in step from here on. Modelled with a "strip everything" original selector, which
        /// is what vanilla <c>Pawn.Strip</c> passes (null).
        /// </summary>
        [Test]
        public void StaysOnCorpse_MatchesTheDropFilterRejection_ForEveryPiece()
        {
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                    foreach (bool tainted in new[] { true, false })
                        foreach (bool smeltable in new[] { true, false })
                        {
                            bool oracleDrops = DropAllSelectorOracle(tainted, smeltable, sm, non);
                            bool filterDrops = !StripPolicy.StaysOnCorpse(tainted, smeltable, sm, non);
                            Assert.That(filterDrops, Is.EqualTo(oracleDrops),
                                $"tainted={tainted} smeltable={smeltable} sm={sm} non={non}");
                        }
        }

        /// <summary>
        /// The ORIGINAL inline <c>DropAll</c> selector body, transcribed from the pre-fix runtime code: a tainted
        /// piece whose resolved action is LeaveOnCorpse is not dropped; everything else falls through to the
        /// caller's own selector (null here, i.e. "drop it").
        /// </summary>
        /// <param name="tainted">Worn by a corpse AND the apparel kind cares.</param>
        /// <param name="smeltable">Whether the instance is smeltable, picking the applicable category.</param>
        /// <param name="smeltablePolicy">Policy for tainted smeltable apparel.</param>
        /// <param name="nonSmeltablePolicy">Policy for tainted non-smeltable apparel.</param>
        /// <returns>True when the selector would let the piece be dropped.</returns>
        private static bool DropAllSelectorOracle(bool tainted, bool smeltable,
            TaintedApparelPolicy smeltablePolicy, TaintedApparelPolicy nonSmeltablePolicy)
        {
            if (tainted)
            {
                var action = StripPolicy.ApparelAction(true, smeltable, smeltablePolicy, nonSmeltablePolicy);
                if (action == TaintedApparelPolicy.LeaveOnCorpse)
                    return false; // keep on the body — don't drop
            }
            return true;
        }

        [Test]
        public void StaysOnCorpse_DefaultTakeTake_NeverKeepsAnything()
        {
            // At the shipped defaults the probe is identical to vanilla's own: every piece is still strippable,
            // so CanBeStrippedByColony is never narrowed and strip orders behave exactly as before the fix.
            foreach (bool tainted in new[] { true, false })
                foreach (bool smeltable in new[] { true, false })
                    Assert.That(StripPolicy.StaysOnCorpse(tainted, smeltable,
                        TaintedApparelPolicy.Take, TaintedApparelPolicy.Take), Is.False);
        }

        [Test]
        public void StaysOnCorpse_DropAndForbidAndDestroy_ComeOffTheBody()
        {
            // Both come OFF first (then forbidden / destroyed on the ground), so a body wearing only these still
            // has something to strip — the distinction from LeaveWhereItIs, which is about a LOOSE piece.
            Assert.That(StripPolicy.StaysOnCorpse(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.DropAndForbid), Is.False);
            Assert.That(StripPolicy.StaysOnCorpse(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.Destroy), Is.False);
            // ...while LeaveWhereItIs treats DropAndForbid as "leave it", so the two must NOT be interchangeable.
            Assert.That(StripPolicy.LeaveWhereItIs(true, false,
                TaintedApparelPolicy.Take, TaintedApparelPolicy.DropAndForbid), Is.True);
        }

        [Test]
        public void StaysOnCorpse_UsesTheApplicableCategoryOnly()
        {
            // Metal armour kept on the body, rags taken: only the smeltable piece stays worn.
            Assert.That(StripPolicy.StaysOnCorpse(true, true,
                TaintedApparelPolicy.LeaveOnCorpse, TaintedApparelPolicy.Take), Is.True);
            Assert.That(StripPolicy.StaysOnCorpse(true, false,
                TaintedApparelPolicy.LeaveOnCorpse, TaintedApparelPolicy.Take), Is.False);
        }

        [Test]
        public void StaysOnCorpse_Untainted_NeverStays_RegardlessOfPolicies()
        {
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                {
                    Assert.That(StripPolicy.StaysOnCorpse(false, true, sm, non), Is.False);
                    Assert.That(StripPolicy.StaysOnCorpse(false, false, sm, non), Is.False);
                }
        }

        [Test]
        public void LeavesAnyTainted_CoversEveryStaysOnCorpseCase()
        {
            // The cheap pre-gate both the DropAll filter and the anything-to-strip probe run first. It may be
            // wider than StaysOnCorpse (it also passes for DropAndForbid) but it must NEVER be narrower: a
            // configuration where a piece stays on the body while the gate says "nothing is ever kept" would skip
            // the filter and the probe together, and the no-op would be back.
            foreach (TaintedApparelPolicy sm in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                foreach (TaintedApparelPolicy non in System.Enum.GetValues(typeof(TaintedApparelPolicy)))
                    foreach (bool smeltable in new[] { true, false })
                        if (StripPolicy.StaysOnCorpse(true, smeltable, sm, non))
                            Assert.That(StripPolicy.LeavesAnyTainted(sm, non), Is.True,
                                $"sm={sm} non={non} smeltable={smeltable}");
        }
    }
}
