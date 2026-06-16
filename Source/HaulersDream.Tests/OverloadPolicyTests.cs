using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class OverloadPolicyTests
    {
        // Reference pawn: 35 kg capacity, 5 kg of worn gear, 1 kg/unit resource, full carry limit.
        const float Cap = 35f;
        const float Base = 35f;   // carry-limit mass = fraction 1.0 × capacity
        const float Gear = 5f;
        const float Unit = 1f;

        static int Carry(int level, int demand, int available, float current = Gear, float unit = Unit, float baseCap = Base)
            => OverloadPolicy.UnitsToCarry(level, Cap, baseCap, current, unit, demand, available);

        // baseUnits under the carry limit = floor((35-5)/1) = 30.

        [Test]
        public void Off_NeverOverloads()
        {
            Assert.That(Carry(OverloadTuning.OffLevel, demand: 200, available: 200), Is.EqualTo(30));
        }

        [Test]
        public void NoSlowdownLevel_CarriesEverythingDemanded()
        {
            Assert.That(Carry(0, demand: 200, available: 200), Is.EqualTo(200));
        }

        [Test]
        public void NoSlowdownLevel_StillCappedByAvailability()
        {
            Assert.That(Carry(0, demand: 200, available: 47), Is.EqualTo(47));
        }

        [Test]
        public void Fair_OverloadsToTheBreakEvenCeiling()
        {
            // Fair break-even ratio ≈ 2.75 → mass cap ≈ 96.25 kg → room 91.25 → 91 units of 1 kg.
            Assert.That(Carry(5, demand: 200, available: 200), Is.EqualTo(91));
        }

        [Test]
        public void Fair_CappedByDemand()
        {
            // Only 45 units are actually wanted (this job + future plans) → carry 45, not the 91 it could.
            Assert.That(Carry(5, demand: 45, available: 200), Is.EqualTo(45));
        }

        [Test]
        public void Fair_CappedByAvailability()
        {
            Assert.That(Carry(5, demand: 200, available: 40), Is.EqualTo(40));
        }

        [Test]
        public void DemandWithinCarryLimit_NoOverload()
        {
            // Demand (20) is below the no-overload baseline (30) → just take the 20, no overload.
            Assert.That(Carry(5, demand: 20, available: 200), Is.EqualTo(20));
        }

        [Test]
        public void DemandEqualsBaseline_NoOverload()
        {
            Assert.That(Carry(5, demand: 30, available: 200), Is.EqualTo(30));
        }

        [Test]
        public void HigherLevel_CarriesNoMoreThanLowerLevel()
        {
            int l1 = Carry(1, demand: 500, available: 500);
            int l5 = Carry(5, demand: 500, available: 500);
            int l9 = Carry(9, demand: 500, available: 500);
            Assert.That(l1, Is.GreaterThanOrEqualTo(l5));
            Assert.That(l5, Is.GreaterThanOrEqualTo(l9));
            Assert.That(l9, Is.GreaterThanOrEqualTo(30)); // never below the no-overload baseline
        }

        [Test]
        public void AlreadyOverloaded_NoRoom_ReturnsBaseline()
        {
            // Pawn already at 100 kg — past the Fair overload ceiling (~96.25 kg = 2.75 × 35) → take none
            // extra beyond the (zero) baseline room. baseUnits here is 0 (already over the carry limit).
            Assert.That(Carry(5, demand: 200, available: 200, current: 100f), Is.EqualTo(0));
        }

        [Test]
        public void Massless_TakenInFullUpToDemand()
        {
            Assert.That(Carry(5, demand: 123, available: 500, unit: 0f), Is.EqualTo(123));
        }

        [Test]
        public void ZeroDemandOrAvailable_TakesNothing()
        {
            Assert.That(Carry(5, demand: 0, available: 200), Is.EqualTo(0));
            Assert.That(Carry(5, demand: 200, available: 0), Is.EqualTo(0));
        }

        [Test]
        public void LowerCarryLimitFraction_ScalesTheOverloadCeilingToo()
        {
            // Carry limit set to 50% (baseCap 17.5): the overload ceiling scales off the CONFIGURED base cap
            // (~2.75 × 17.5 ≈ 48.1 kg; room from 5 kg current ≈ 43.1 → 43 units), NOT the true capacity — a
            // player-reduced carry limit must not be silently nullified by the overload feature.
            // (Supersedes the old "...StillOverloadsFromTrueCapacity" spec, which encoded exactly that bug.)
            int units = Carry(5, demand: 200, available: 200, baseCap: 17.5f);
            Assert.That(units, Is.EqualTo(43));
            // At the default fraction (base == max) the ceiling matches the headline Fair break-even.
            Assert.That(Carry(5, demand: 200, available: 200), Is.EqualTo(91));
        }

        [Test]
        public void HeavyUnits_FewerUnitsButSameMassCeiling()
        {
            // 10 kg/unit: mass cap ≈ 96.25, room from 5 kg = 91.25 → floor(91.25/10) = 9 units.
            Assert.That(Carry(5, demand: 200, available: 200, unit: 10f), Is.EqualTo(9));
        }

        [Test]
        public void ZeroCapacityPawn_NoOverloadNoCrash()
        {
            // Babies / subhumans / non-tool-user non-pack-animals: MassUtility.Capacity returns 0,
            // which OverloadGate passes as maxCapacityKg. The maxCapacityKg<=0 guard must fall back to
            // the (zero) baseline rather than attempting overload (no divide, no negative room).
            int units = OverloadPolicy.UnitsToCarry(
                OverloadTuning.FairLevel,
                maxCapacityKg: 0f, baseCapKg: 0f, currentMassKg: 0f,
                unitMassKg: 1f, demandUnits: 200, availableUnits: 200);
            Assert.That(units, Is.EqualTo(0));
        }

        [Test]
        public void ZeroCapacityPawn_WithGearMass_StillNoOverload()
        {
            int units = OverloadPolicy.UnitsToCarry(
                OverloadTuning.FairLevel,
                maxCapacityKg: 0f, baseCapKg: 0f, currentMassKg: 3f,
                unitMassKg: 1f, demandUnits: 200, availableUnits: 200);
            Assert.That(units, Is.EqualTo(0));
        }
    }

    /// <summary>
    /// Pins the overload "lockstep" invariant: the set of pawns that MAY load past 100% capacity
    /// (<c>OverloadGate.NoOverloadFor</c> → <see cref="OverloadPolicy.ParticipatesInOverload"/>) must equal
    /// the set that PAYS the move-speed penalty (<c>StatPart_Overload</c> → <see cref="OverloadPolicy.AppliesSpeedPenalty"/>)
    /// — the overload deal is "extra capacity FOR a speed penalty". These two were once hand-coded in two
    /// files with different conditions; consolidating them onto these shared Core predicates is what this
    /// matrix protects. Any future edit that breaks the agreement on the shared off-matrix fails the build.
    /// </summary>
    [TestFixture]
    public class OverloadLockstepTests
    {
        // The four input axes the two sites SHARE. Off-level is at OffLevel; a representative
        // "active" level (Fair) plus level 0 ("Free") are both exercised so the level edges are covered.
        static readonly int[] Levels = { 0, OverloadTuning.FairLevel, OverloadTuning.OffLevel };
        static readonly bool[] Bools = { false, true };

        /// <summary>
        /// THE core lockstep assertion. For the canonical participant (player-faction, undrafted — the
        /// faction/draft asymmetry is off, so the two sites are expected to AGREE EXACTLY), the "may
        /// overload" set and the "is slowed who-set" must be identical across the entire shared matrix of
        /// {strict × level × CE × race(humanlike/mech/animal)}. Note level 0 participates in BOTH (the
        /// consistent "free capacity, no slowdown" deal — the actual SpeedFactor at level 0 is 1.0, so a
        /// participating level-0 pawn pays a zero penalty, which the separate factor test below confirms).
        /// </summary>
        [Test]
        public void MayOverload_Equals_IsSlowedWhoSet_ForPlayerUndrafted()
        {
            foreach (bool strict in Bools)
            foreach (int level in Levels)
            foreach (bool ce in Bools)
            foreach ((bool human, bool mech) in Races())
            {
                bool mayOverload = OverloadPolicy.ParticipatesInOverload(strict, level, ce, human, mech);
                bool isSlowed = OverloadPolicy.AppliesSpeedPenalty(
                    strict, level, ce, human, mech, isPlayerFaction: true, isDrafted: false);
                Assert.That(isSlowed, Is.EqualTo(mayOverload),
                    $"lockstep broken: strict={strict} level={level} ce={ce} human={human} mech={mech} " +
                    $"-> mayOverload={mayOverload} but isSlowed={isSlowed}");
            }
        }

        /// <summary>
        /// The penalty is NEVER a SUPERSET of the capacity grant: if a pawn is slowed it must also be
        /// allowed to overload (you can never pay the penalty without getting the capacity). This holds
        /// across the FULL matrix including the faction/draft axes — those can only REMOVE the penalty
        /// from a may-overload pawn, never add it to a non-participant. (Guards the dangerous direction:
        /// an un-agreed slowdown.)
        /// </summary>
        [Test]
        public void IsSlowed_Implies_MayOverload_AcrossFullMatrix()
        {
            foreach (bool strict in Bools)
            foreach (int level in Levels)
            foreach (bool ce in Bools)
            foreach ((bool human, bool mech) in Races())
            foreach (bool player in Bools)
            foreach (bool drafted in Bools)
            {
                bool mayOverload = OverloadPolicy.ParticipatesInOverload(strict, level, ce, human, mech);
                bool isSlowed = OverloadPolicy.AppliesSpeedPenalty(
                    strict, level, ce, human, mech, player, drafted);
                if (isSlowed)
                    Assert.That(mayOverload, Is.True,
                        $"penalty-without-capacity: strict={strict} level={level} ce={ce} human={human} " +
                        $"mech={mech} player={player} drafted={drafted}");
            }
        }

        /// <summary>
        /// The two documented penalty-only asymmetries are exactly that — a may-overload pawn that is
        /// non-player OR drafted KEEPS the capacity grant but is NOT slowed (it already loaded the weight;
        /// drafted pawns move at full speed). This pins the asymmetry direction so a future change can't
        /// accidentally start slowing them (or stop granting capacity to a slowed pawn).
        /// </summary>
        [Test]
        public void NonPlayerOrDrafted_MayOverloadButNotSlowed()
        {
            // A canonical participant (Fair, not strict, no CE, humanlike) — definitely may overload.
            const int level = OverloadTuning.FairLevel;
            Assert.That(OverloadPolicy.ParticipatesInOverload(false, level, false, true, false), Is.True);

            // player + undrafted -> slowed (the lockstep case)
            Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, level, false, true, false, true, false), Is.True);
            // non-player -> NOT slowed (but still may overload)
            Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, level, false, true, false, false, false), Is.False);
            // drafted -> NOT slowed (but still may overload)
            Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, level, false, true, false, true, true), Is.False);
        }

        /// <summary>
        /// Animals (non-humanlike, non-mechanoid) never participate and are never slowed, regardless of
        /// every other axis — they must not get penalty-free overload capacity.
        /// </summary>
        [Test]
        public void Animals_NeverParticipate_NeverSlowed()
        {
            foreach (bool strict in Bools)
            foreach (int level in Levels)
            foreach (bool ce in Bools)
            foreach (bool player in Bools)
            foreach (bool drafted in Bools)
            {
                Assert.That(OverloadPolicy.ParticipatesInOverload(strict, level, ce, false, false), Is.False);
                Assert.That(OverloadPolicy.AppliesSpeedPenalty(strict, level, ce, false, false, player, drafted), Is.False);
            }
        }

        /// <summary>
        /// strict, CE, and the Off level each independently force BOTH sites off for every pawn — these are
        /// the shared overrides that must always agree.
        /// </summary>
        [Test]
        public void StrictOrCeOrOff_ForcesBothOff()
        {
            const int activeLevel = OverloadTuning.FairLevel;
            foreach ((bool human, bool mech) in Races())
            {
                // strict on
                Assert.That(OverloadPolicy.ParticipatesInOverload(true, activeLevel, false, human, mech), Is.False);
                Assert.That(OverloadPolicy.AppliesSpeedPenalty(true, activeLevel, false, human, mech, true, false), Is.False);
                // CE active
                Assert.That(OverloadPolicy.ParticipatesInOverload(false, activeLevel, true, human, mech), Is.False);
                Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, activeLevel, true, human, mech, true, false), Is.False);
                // slider Off
                Assert.That(OverloadPolicy.ParticipatesInOverload(false, OverloadTuning.OffLevel, false, human, mech), Is.False);
                Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, OverloadTuning.OffLevel, false, human, mech, true, false), Is.False);
            }
        }

        /// <summary>
        /// Confirms the level-0 ("Free") consistency claim numerically: a level-0 pawn PARTICIPATES (may
        /// overload) and its who-set penalty flag is on for a player/undrafted pawn, but the ACTUAL speed
        /// factor at level 0 is 1.0 (no slowdown) — so "free capacity, zero penalty" is a consistent
        /// bargain, not a lockstep violation. (The StatPart's <c>level &lt;= 0</c> fast-path and its final
        /// <c>factor &lt; 1</c> check both yield the same no-penalty outcome.)
        /// </summary>
        [Test]
        public void LevelZero_ParticipatesButSpeedFactorIsOne()
        {
            Assert.That(OverloadPolicy.ParticipatesInOverload(false, 0, false, true, false), Is.True);
            Assert.That(OverloadPolicy.AppliesSpeedPenalty(false, 0, false, true, false, true, false), Is.True);
            // ...but the curve at level 0 never slows: factor is 1.0 at any over-capacity ratio.
            Assert.That(OverloadTuning.SpeedFactor(0, 1.5f), Is.EqualTo(1f));
            Assert.That(OverloadTuning.SpeedFactor(0, 3.0f), Is.EqualTo(1f));
        }

        // Humanlike, Mechanoid, Animal (neither). Both-true is not a real RimWorld race and is omitted.
        static System.Collections.Generic.IEnumerable<(bool human, bool mech)> Races()
        {
            yield return (true, false);   // humanlike
            yield return (false, true);   // mechanoid
            yield return (false, false);  // animal / non-participant
        }
    }
}
