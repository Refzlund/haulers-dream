using System;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class UnloadFallbackPolicyTests
    {
        private static UnloadPlacement Choose(bool hasStorageDestination, bool onPlayerHomeMap, bool hasNearbyHomeCell)
            => UnloadFallbackPolicy.Choose(hasStorageDestination, onPlayerHomeMap, hasNearbyHomeCell);

        // ---------------- Choose ----------------

        [Test]
        public void RealStorage_AlwaysDelivers_HomeMap()
        {
            // Storage wins over everything: the stack is carried to the stockpile/shelf/container.
            Assert.That(Choose(hasStorageDestination: true, onPlayerHomeMap: true, hasNearbyHomeCell: true),
                Is.EqualTo(UnloadPlacement.Deliver));
        }

        [Test]
        public void RealStorage_AlwaysDelivers_NoHomeCell()
        {
            // The home-area fallback is irrelevant once real storage resolved.
            Assert.That(Choose(hasStorageDestination: true, onPlayerHomeMap: true, hasNearbyHomeCell: false),
                Is.EqualTo(UnloadPlacement.Deliver));
        }

        [Test]
        public void RealStorage_OnCaravanMap_StillDelivers()
        {
            // A genuine stockpile on a temporary map is still used — the keep-in-inventory branch is only
            // reached when NO storage accepted the stack.
            Assert.That(Choose(hasStorageDestination: false, onPlayerHomeMap: false, hasNearbyHomeCell: false),
                Is.EqualTo(UnloadPlacement.KeepInInventory));
            Assert.That(Choose(hasStorageDestination: true, onPlayerHomeMap: false, hasNearbyHomeCell: false),
                Is.EqualTo(UnloadPlacement.Deliver));
        }

        [Test]
        public void NoStorage_OffHomeMap_KeepsInInventory()
        {
            // On a caravan camp / bandit base the load rides home instead of being abandoned on the ground —
            // even when a "home area" cell would nominally qualify (the home grid there is not the colony).
            Assert.That(Choose(hasStorageDestination: false, onPlayerHomeMap: false, hasNearbyHomeCell: true),
                Is.EqualTo(UnloadPlacement.KeepInInventory));
        }

        [Test]
        public void NoStorage_HomeMap_WithNearbyHomeCell_PlacesThere()
        {
            // The #231 replacement for vanilla's desperate search: a reachable home-area floor cell near the
            // carrier, NOT a random spot at the edge of the map.
            Assert.That(Choose(hasStorageDestination: false, onPlayerHomeMap: true, hasNearbyHomeCell: true),
                Is.EqualTo(UnloadPlacement.PlaceOnNearbyHomeCell));
        }

        [Test]
        public void NoStorage_HomeMap_NoHomeCell_DropsAtFeet()
        {
            // Genuinely nowhere: put it down where the pawn stands (the home-preferring drop), never carry it
            // off to the wilderness. Nothing is kept in the pack and nothing is lost.
            Assert.That(Choose(hasStorageDestination: false, onPlayerHomeMap: true, hasNearbyHomeCell: false),
                Is.EqualTo(UnloadPlacement.DropAtFeet));
        }

        [Test]
        public void EveryCombination_ReturnsAKnownPlacement()
        {
            // Total-function pin: all 8 input combinations land on one of the four sanctioned outcomes, so the
            // driver's switch over Choose can never fall through to an unhandled placement.
            //
            // This test alone does NOT stop #231 coming back — a unit test cannot see a driver that stops calling
            // the policy. The actual protection is scripts/check-no-desperate-leg.ts (run by bun run build), which
            // bans the vanilla desperate search in source, pins the driver's dispatch through Choose, and fails if
            // this enum grows a fifth "haul it outside the home area" outcome.
            for (int mask = 0; mask < 8; mask++)
            {
                bool hasStorage = (mask & 1) != 0;
                bool homeMap = (mask & 2) != 0;
                bool homeCell = (mask & 4) != 0;
                var placement = Choose(hasStorage, homeMap, homeCell);
                Assert.That(Enum.IsDefined(typeof(UnloadPlacement), placement), Is.True,
                    $"storage={hasStorage} homeMap={homeMap} homeCell={homeCell} produced an unknown placement");
                Assert.That(placement, Is.AnyOf(UnloadPlacement.Deliver, UnloadPlacement.KeepInInventory,
                        UnloadPlacement.PlaceOnNearbyHomeCell, UnloadPlacement.DropAtFeet),
                    $"storage={hasStorage} homeMap={homeMap} homeCell={homeCell}");
            }
        }

        // ---------------- PreferHomeAreaDrop ----------------

        [Test]
        public void HomeMap_WithPaintedHomeArea_PrefersHome()
        {
            // The only situation where "inside the Home area" is a meaningful constraint on a feet-drop.
            Assert.That(UnloadFallbackPolicy.PreferHomeAreaDrop(onPlayerHomeMap: true, homeAreaHasAnyCells: true),
                Is.True);
        }

        [Test]
        public void CaravanMap_DoesNotPreferHome()
        {
            // Off the settled map the home grid is not the colony; gating on it would reject every cell.
            Assert.That(UnloadFallbackPolicy.PreferHomeAreaDrop(onPlayerHomeMap: false, homeAreaHasAnyCells: true),
                Is.False);
        }

        [Test]
        public void EmptyHomeArea_DoesNotPreferHome()
        {
            // An unpainted home area matches nothing, so the constrained pass would only waste a placement
            // search before the unconstrained fallback ran anyway.
            Assert.That(UnloadFallbackPolicy.PreferHomeAreaDrop(onPlayerHomeMap: true, homeAreaHasAnyCells: false),
                Is.False);
        }

        // ---------------- RemainingToDrop ----------------

        private static int Remaining(int requested, int before, int after, bool gone)
            => UnloadFallbackPolicy.RemainingToDrop(requested, before, after, gone);

        [Test]
        public void NothingPlaced_SecondPassRetriesTheWholeRequest()
        {
            // The ordinary case: the home-area pass found no acceptable cell at all, so the stack is untouched
            // and the unconstrained pass retries exactly what was asked for.
            Assert.That(Remaining(requested: 20, before: 50, after: 50, gone: false), Is.EqualTo(20));
        }

        [Test]
        public void PartialPlacement_SubtractsWhatAlreadyLanded()
        {
            // THE over-drop regression pin. The home pass placed 5 of the 20 requested units and still reported
            // failure. Retrying the full 20 would put 25 units on the ground in total — 5 more than was ever
            // requested, silently shaved off the pawn's keep-stock. Only the 15 still owed may be dropped.
            Assert.That(Remaining(requested: 20, before: 50, after: 45, gone: false), Is.EqualTo(15));
            Assert.That(Remaining(requested: 20, before: 50, after: 45, gone: false), Is.Not.EqualTo(20),
                "re-dropping the full request after a partial placement over-drops by what already landed");
        }

        [Test]
        public void FullStackRequest_PartialPlacement_MatchesWhatRemains()
        {
            // Whole-stack request (count == stackCount): 5 of 20 landed, so 15 are owed AND 15 physically remain.
            // Both clamp terms agree here, which is why this case never exposed the over-drop bug on its own.
            Assert.That(Remaining(requested: 20, before: 20, after: 15, gone: false), Is.EqualTo(15));
        }

        [Test]
        public void OverLargeRequest_ClampedToWhatRemains()
        {
            // THE red-error regression pin, and proof the stackCount term is independently load-bearing: a caller
            // whose requested count exceeds the stack (a surplus figure computed before the stack shrank) must be
            // clamped, or vanilla ThingOwner.TryDrop logs "Tried to drop 99 of X while only having 50".
            Assert.That(Remaining(requested: 99, before: 50, after: 50, gone: false), Is.EqualTo(50));
        }

        [Test]
        public void StackGone_NeedsNoSecondPass()
        {
            // Destroyed, or absorbed whole into a ground pile, or moved out of the container: nothing remains for
            // a second pass to place, whatever the stale count says.
            Assert.That(Remaining(requested: 20, before: 50, after: 45, gone: true), Is.EqualTo(0));
            Assert.That(Remaining(requested: 20, before: 50, after: 0, gone: true), Is.EqualTo(0));
        }

        [Test]
        public void HomePassPlacedTheWholeRequest_NoSecondPass()
        {
            // All 20 requested units landed even though the pass reported failure — the second pass must not run,
            // or it would drop 20 MORE units of keep-stock.
            Assert.That(Remaining(requested: 20, before: 50, after: 30, gone: false), Is.EqualTo(0));
        }

        [Test]
        public void PlacedMoreThanRequested_NeverGoesNegative()
        {
            // Defensive: a foreign placedAction absorbing beyond the request must clamp to 0, not to a negative
            // count that vanilla would treat as an error.
            Assert.That(Remaining(requested: 20, before: 50, after: 20, gone: false), Is.EqualTo(0));
        }

        [Test]
        public void GrownStack_DoesNotWidenTheRequest()
        {
            // A foreign patch on GenPlace.TryPlaceThing (or a placedAction) topped the inventory stack up during
            // the home pass, so `placed` computes NEGATIVE. Without the requested-clamp, `requested - placed`
            // would ask for 30 when only 20 were ever requested — dropping 10 units of keep-stock. Vanilla alone
            // cannot produce after > before, but a 230-mod load is this mod's design point.
            Assert.That(Remaining(requested: 20, before: 50, after: 60, gone: false), Is.EqualTo(20));
            Assert.That(Remaining(requested: 20, before: 50, after: 60, gone: false), Is.Not.EqualTo(30),
                "a stack that grew during the first pass must not widen the second pass beyond the request");
        }

        [Test]
        public void NeverExceedsTheRequestOrWhatRemains()
        {
            // The two invariants the whole clamp exists to hold, over every shape of before/after/request.
            // `after` deliberately sweeps ABOVE `before` so the grown-stack case is structurally reachable:
            //   1. the two passes together never place more than was requested  (result <= requested - placed)
            //   2. the second pass never asks for more than physically remains  (result <= stackCountAfter)
            for (int requested = 0; requested <= 12; requested++)
                for (int before = 0; before <= 12; before++)
                    for (int after = 0; after <= before + 4; after++)
                    {
                        int left = Remaining(requested, before, after, gone: false);
                        int placed = before - after;
                        Assert.That(left, Is.GreaterThanOrEqualTo(0),
                            $"requested={requested} before={before} after={after}");
                        Assert.That(left, Is.LessThanOrEqualTo(requested),
                            $"second pass widened past the request: requested={requested} before={before} after={after}");
                        Assert.That(left, Is.LessThanOrEqualTo(after),
                            $"would trigger the red over-drop error: requested={requested} before={before} after={after}");
                        Assert.That(placed + left, Is.LessThanOrEqualTo(Math.Max(requested, placed)),
                            $"two passes placed more than requested: requested={requested} before={before} after={after}");
                        Assert.That(Remaining(requested, before, after, gone: true), Is.EqualTo(0),
                            $"a gone stack always needs 0: requested={requested} before={before} after={after}");
                    }
        }

        // ---------------- vanilla-parity constants ----------------

        [Test]
        public void RadialCellsToTry_MatchesVanillaLoopBound()
        {
            // Vanilla StoreUtility.TryFindStoreCellNearColonyDesperate (RimWorld 1.6) scans
            // `for (int i = -4; i < 20; i++)` — the upper bound is the sequential radial-cell count.
            Assert.That(UnloadFallbackPolicy.RadialCellsToTry, Is.EqualTo(20));
        }

        [Test]
        public void RandomLeadTries_MatchesVanillaLoopBound()
        {
            // ...and the four leading `i < 0` iterations draw Rand.RangeInclusive(0, 4) instead of `i`.
            Assert.That(UnloadFallbackPolicy.RandomLeadTries, Is.EqualTo(4));
        }
    }
}
