using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Locks the permission rule behind Hauler's Dream's bulk carrier unload: which pawns the player may have
    /// emptied, and — the part that was wrong — which it may not. A Steam reporter found HD offering "Prioritize
    /// bulk unloading" on a downed Hospitality guest, including a Bestower still carrying the psylink neuroformer
    /// vanilla deliberately protects, because the offer reused vanilla's job-time predicate whose host-faction arm
    /// admits every pawn the colony merely HOSTS. The three cases below that must stay separable are a colony pack
    /// animal (yes), a prisoner (yes — the legitimate content of that arm), and a guest (no).
    ///
    /// <para>WHAT THESE TESTS CANNOT SEE, which is most of what can actually break. The rule is one boolean
    /// expression; everything that decides whether it is CONSULTED lives in Verse, where
    /// <c>HaulersDream.Tests</c> cannot reach (it references only <c>HaulersDream.Core</c>). Nothing here can
    /// fail if:</para>
    /// <list type="bullet">
    /// <item><description>an entry point stops calling the rule — the float menu, the work-giver takeover, or
    /// the driver's <c>UnloadEverything</c> write. Three call sites, and the flag write is the widest of them:
    /// raising that scribed flag also opens vanilla's own faction-blind unload work-giver on the victim for every
    /// hauler on the map;</description></item>
    /// <item><description>the live-pawn reads feeding it are wrong — <c>IsPrisoner</c> quietly replaced by "has a
    /// host faction" would re-admit every guest while every assertion below stays green;</description></item>
    /// <item><description>a fourth entry point is added and gates nothing.</description></item>
    /// </list>
    /// <para>That inventory is enforced by <c>scripts/check-non-colony-pawn-gates.ts</c>, not here.</para>
    /// </summary>
    [TestFixture]
    public class BulkUnloadPermissionPolicyTests
    {
        // ══════════════════ Allowed: what the feature was built for ══════════════════

        [Test]
        public void MayBulkUnload_OwnFactionCarrier_Allowed()
        {
            // The whole intended population: a colony pack animal home from a caravan, a colony mech, a slave
            // (whose faction IS the player's). Nothing about the fix may cost this case.
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: true, isPrisonerOfHaulerFaction: false, questRelated: false),
                Is.True);
        }

        [Test]
        public void MayBulkUnload_OurPrisoner_Allowed()
        {
            // The legitimate content of vanilla's host-faction arm, and the reason the branch is narrowed rather
            // than deleted. A prisoner's Faction stays its ORIGINAL faction, so the faction arm cannot see one —
            // yet a caravan arriving with prisoners flags every pawn it brought, and vanilla expects a colonist to
            // unload them. Taking a prisoner's goods is sanctioned twice over in vanilla: the gear tab admits
            // IsPrisonerOfColony, and CanBeStrippedByColony admits a secure prisoner.
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: false, isPrisonerOfHaulerFaction: true, questRelated: false),
                Is.True);
        }

        // ══════════════════ Refused: the reported bug ══════════════════

        [Test]
        public void MayBulkUnload_HostedGuest_Refused()
        {
            // A Hospitality visitor, a rescued wanderer, a downed Bestower. Hosted but neither ours nor our
            // prisoner — exactly the pawns the old host-faction arm admitted and the ones this rule exists for.
            // Both allow-arms are false, which is the whole difference between "hosted" and "held". Note that no
            // quest claim is needed to reach the refusal: an ordinary Hospitality visitor with no quest attached
            // is refused on the arms alone. (The same inputs also describe a total stranger — a downed raider, a
            // trade caravan's mule — which vanilla's own predicate already refused; the rule cannot tell the two
            // apart and need not, since both answers are no.)
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: false, isPrisonerOfHaulerFaction: false, questRelated: false),
                Is.False);
        }

        // ══════════════════ The quest clause vetoes BOTH arms ══════════════════

        [Test]
        public void MayBulkUnload_QuestRelated_VetoesOwnFaction()
        {
            // Mirrors vanilla's strip menu, which refuses a quest-related pawn outright ("Cannot strip: quest
            // related") without first asking whose pawn it is — and matches every sibling HD entry point, all of
            // which pair their faction test with IsQuestLodger(). A quest pawn's belongings leave with the quest.
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: true, isPrisonerOfHaulerFaction: false, questRelated: true),
                Is.False);
        }

        [Test]
        public void MayBulkUnload_QuestRelated_VetoesPrisoner()
        {
            // The other half of the veto: a prisoner a live quest has a claim on (a "capture and hold" target) is
            // no more emptiable than a quest lodger. Asserted separately from the faction case so a rewrite that
            // attaches the quest clause to only one arm fails here rather than in someone's save.
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: false, isPrisonerOfHaulerFaction: true, questRelated: true),
                Is.False);
        }

        [Test]
        public void MayBulkUnload_QuestRelated_VetoesBothArmsAtOnce()
        {
            // Belt and braces on the veto's precedence: no combination of true allow-arms outranks a quest claim.
            Assert.That(
                BulkUnloadPermissionPolicy.MayBulkUnload(
                    sharesHaulerFaction: true, isPrisonerOfHaulerFaction: true, questRelated: true),
                Is.False);
        }

        // ══════════════════ Totality ══════════════════

        [Test]
        public void MayBulkUnload_EveryInputCombination_MatchesTheStatedRule()
        {
            // The rule is small enough to state exhaustively, so state it: permission is (ours OR our prisoner)
            // AND NOT quest-related, over all eight inputs. This is what catches an "improvement" that swaps an
            // operator or drops a clause while every named case above happens to still pass.
            for (int bits = 0; bits < 8; bits++)
            {
                bool ours = (bits & 1) != 0;
                bool prisoner = (bits & 2) != 0;
                bool quest = (bits & 4) != 0;
                Assert.That(
                    BulkUnloadPermissionPolicy.MayBulkUnload(ours, prisoner, quest),
                    Is.EqualTo((ours || prisoner) && !quest),
                    $"ours={ours} prisoner={prisoner} quest={quest}");
            }
        }
    }
}
