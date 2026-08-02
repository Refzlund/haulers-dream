using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The corpse bulk-haul opt-in (Steam: corpse hauls never swept, and bodies were fetched one per trip),
    /// plus the two REGRESSION guards for the paths that fix opens up: a grave destination reaching the
    /// storage-budget arithmetic, and a corpse riding in a pawn's inventory past vanilla's raw-food drop loop.
    /// </summary>
    [TestFixture]
    public class CorpseSweepPolicyTests
    {
        // ---- off => byte-identical to the pre-fix behaviour ----

        [Test]
        public void Off_NeitherAnchorsNorSweeps()
        {
            // The whole point of the opt-in: switched off, a corpse is exactly as invisible to the sweep as it
            // was before the WorkGiver_HaulCorpses hook existed.
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, false, false, false), Is.False);
            Assert.That(CorpseSweepPolicy.CanSweepAsNeighbor(true, false, false, false), Is.False);
        }

        [Test]
        public void MasterBulkHaulOff_Dominates()
        {
            // The corpse opt-in is a sub-option: it can never revive a sweep the master switch turned off, in
            // either role. (The settings window greys it out for the same reason.)
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(false, true, false, false), Is.False);
            Assert.That(CorpseSweepPolicy.CanSweepAsNeighbor(false, true, false, false), Is.False);
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(false, false, false, false), Is.False);
            Assert.That(CorpseSweepPolicy.CanSweepAsNeighbor(false, false, false, false), Is.False);
        }

        // ---- on => both roles open ----

        [Test]
        public void On_AllowsBothRoles()
        {
            // Anchor = a haul ordered (or scanned) on the body itself, which now sweeps the loose loot around it.
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, true, false, false), Is.True);
            // Neighbour = a body lying beside some other haul, now picked up on the way past.
            Assert.That(CorpseSweepPolicy.CanSweepAsNeighbor(true, true, false, false), Is.True);
        }

        [Test]
        public void TheTwoRolesNeverDisagree()
        {
            // Kept as separate methods because they fail differently at the call sites (a primary that will not
            // fit falls back to vanilla's hand-haul; a neighbour that will not fit is simply skipped), but a
            // configuration that allowed one and not the other would be the confusing half of the feature.
            foreach (bool bulk in new[] { true, false })
                foreach (bool corpses in new[] { true, false })
                    foreach (bool disposalOnly in new[] { true, false })
                        foreach (bool ordered in new[] { true, false })
                            Assert.That(CorpseSweepPolicy.CanSweepAsNeighbor(bulk, corpses, disposalOnly, ordered),
                                Is.EqualTo(CorpseSweepPolicy.CanAnchorSweep(bulk, corpses, disposalOnly, ordered)),
                                $"bulkHaul={bulk} corpses={corpses} disposalOnly={disposalOnly} ordered={ordered}");
        }

        // ---- the disposal-only carve-out ----

        [Test]
        public void DisposalOnlyStripping_AutomaticCorpseAnchorStandsDown()
        {
            // Auto-strip set to "disposal hauls only" recognises a burial by the JOB, and a bulk sweep is not one
            // (the destination is unknown at pickup). Before the corpse opt-in that cost nothing, because the
            // automatic scan never anchored on a body — every automatic grave run was vanilla's haul-to-container,
            // which strips. Letting the scan anchor would bury bodies dressed for a player who changed nothing.
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, true, autoStripOnDisposalOnly: true,
                playerOrdered: false), Is.False, "the automatic scan must not anchor on a body in this mode");

            // An explicit order still sweeps: that already worked before this change for "Pick up X" and "Haul
            // everything nearby", and a player pointing at a body is asking for that trip specifically.
            Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, true, autoStripOnDisposalOnly: true,
                playerOrdered: true), Is.True, "an explicit order must keep working");
        }

        [Test]
        public void OtherStripModes_AreUnaffectedByTheCarveOut()
        {
            // "Every haul" strips at pickup whichever job carries the body, and with auto-strip off there is no
            // stripping expectation to break — so neither mode has anything to protect and the anchor stays open.
            foreach (bool ordered in new[] { true, false })
                Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, true, autoStripOnDisposalOnly: false, ordered),
                    Is.True, $"playerOrdered={ordered}");
        }

        [Test]
        public void TheCarveOutNeverRevivesASweepTheOptInTurnedOff()
        {
            // playerOrdered is a permission to try, never an override of the two switches — otherwise a player who
            // turned corpse sweeping off would still get it whenever they clicked a body.
            foreach (bool disposalOnly in new[] { true, false })
                foreach (bool ordered in new[] { true, false })
                {
                    Assert.That(CorpseSweepPolicy.CanAnchorSweep(true, false, disposalOnly, ordered), Is.False);
                    Assert.That(CorpseSweepPolicy.CanAnchorSweep(false, true, disposalOnly, ordered), Is.False);
                }
        }

        // ---- REGRESSION: a grave destination must not disturb the storage-budget arithmetic ----

        /// <summary>
        /// A grave-bound corpse is the first thing the sweep routes to a NON-cell destination in numbers.
        /// <c>TryFindBestBetterStorageFor</c> hands back an invalid cell for one, the caller's group lookup then
        /// yields no slot group, and the budget stays null — so the plan applies no clamp and the deposit re-gate
        /// remains the authority. Modelled here as the unbounded budget that path produces, to pin that an
        /// unbounded budget answers "no limit" and swallows commitments instead of throwing or going negative.
        /// </summary>
        [Test]
        public void UnboundedBudget_ForAContainerDestination_ClampsNothingAndCannotThrow()
        {
            var def = new object();
            var budget = new StorageGroupBudget(int.MaxValue);

            Assert.That(budget.Unbounded, Is.True);
            Assert.That(budget.AvailableFor(def), Is.EqualTo(int.MaxValue));
            // Pricing and consuming are no-ops rather than errors, so the caller needs no special case.
            budget.PriceDef(def, partialSpace: 4, perCellCapacity: 10);
            budget.Consume(def, 1);
            budget.Consume(def, int.MaxValue);
            Assert.That(budget.Unbounded, Is.True);
            Assert.That(budget.AvailableFor(def), Is.EqualTo(int.MaxValue));
        }

        /// <summary>
        /// The same shape one step earlier: an UNPRICED def reports no limit rather than zero. A corpse whose
        /// destination produced no budget at all must never be clamped to nothing — that would silently decline
        /// every grave-bound sweep.
        /// </summary>
        [Test]
        public void UnpricedDef_FailsOpen_RatherThanClampingToZero()
        {
            var budget = new StorageGroupBudget(3);
            Assert.That(budget.Unbounded, Is.False);
            Assert.That(budget.IsPriced(new object()), Is.False);
            Assert.That(budget.AvailableFor(new object()), Is.EqualTo(int.MaxValue));
        }

        // ---- REGRESSION: a corpse in inventory still trips the raw-food drop protection ----

        /// <summary>
        /// A flesh corpse's generated def carries <c>ingestible.preferability = DesperateOnly</c> (2), which is
        /// at or under vanilla's drop threshold of 5 — so vanilla's <c>JobGiver_DropUnusedInventory</c> raw-food
        /// loop WOULD dump a swept body at the hauler's feet. Sweeping corpses makes that a routine situation
        /// rather than a manual-order curiosity, so the drop protection's own predicate is pinned here: if this
        /// ever stops classifying a corpse as a raw-food candidate, HD would stop re-arming the food clock for a
        /// pawn carrying one, and bodies would start hitting the floor mid-haul.
        /// </summary>
        [Test]
        public void FleshCorpseInInventory_IsARawFoodDropCandidate()
        {
            const int desperateOnly = 2; // FoodPreferability.DesperateOnly
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(
                isIngestible: true, isDrug: false, preferabilityInt: desperateOnly), Is.True);
            // A mechanoid corpse is generated with NeverForNutrition (1) — also under the threshold, so the same
            // protection has to cover it.
            const int neverForNutrition = 1;
            Assert.That(DropUnusedFoodPolicy.IsRawFoodDropCandidate(
                isIngestible: true, isDrug: false, preferabilityInt: neverForNutrition), Is.True);
        }

        /// <summary>
        /// And the other half of the protection: re-arming the food clock to "now" closes the loop's gate, so a
        /// pawn carrying a swept corpse is never asked to drop it.
        /// </summary>
        [Test]
        public void ReArmingTheFoodClock_ClosesTheDropLoopForACarriedCorpse()
        {
            const int now = 5_000_000;
            Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(now, lastInventoryRawFoodUseTick: now), Is.False);
            // Without the re-arm the loop is wide open at this age, which is exactly what would drop the body.
            Assert.That(DropUnusedFoodPolicy.FoodLoopWouldRun(now,
                lastInventoryRawFoodUseTick: now - DropUnusedFoodPolicy.RawFoodDropDelay - 1), Is.True);
        }
    }
}
