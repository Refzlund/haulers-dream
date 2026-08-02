using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins the work-run taxonomy that decides whether a pawn carrying scooped goods is still MID-RUN (keep the
    /// load, apply the strict "storage is genuinely on the way" bar) or has FINISHED (apply the relaxed run-end bar,
    /// which deliberately has no minimum-trip floor).
    ///
    /// <para>The load-bearing case is CONSTRUCTION. It used to fall through to <see cref="WorkRunKind.Other"/>, so a
    /// builder finishing a frame counted as having ended its run and took the floor-less run-end bar between every
    /// wall tile — the reported "runs back to the stockpile after every single wall tile constructed". Vanilla's
    /// construct DELIVERY is a haul-to-container and so already took the strict bar; that asymmetry was the bug.</para>
    /// </summary>
    [TestFixture]
    public class WorkRunPolicyTests
    {
        // ---- Classify -------------------------------------------------------------------------------

        [Test]
        public void Construction_IsItsOwnKind_AndContinuesTheRun()
        {
            var kind = WorkRunPolicy.Classify(isConstructionDriver: true, isYieldDriver: false, isHaulDriver: false);
            Assert.That(kind, Is.EqualTo(WorkRunKind.Construction));
            Assert.That(WorkRunPolicy.ContinuesRun(kind), Is.True);
            Assert.That(WorkRunPolicy.IsRunOver(kind), Is.False, "a builder finishing a frame is mid-run");
        }

        [Test]
        public void YieldWork_ContinuesTheRun()
        {
            var kind = WorkRunPolicy.Classify(false, isYieldDriver: true, isHaulDriver: false);
            Assert.That(kind, Is.EqualTo(WorkRunKind.Yield));
            Assert.That(WorkRunPolicy.IsRunOver(kind), Is.False);
        }

        [Test]
        public void StorageBoundHaul_ContinuesTheRun()
        {
            var kind = WorkRunPolicy.Classify(false, false, isHaulDriver: true);
            Assert.That(kind, Is.EqualTo(WorkRunKind.Haul));
            Assert.That(WorkRunPolicy.IsRunOver(kind), Is.False);
        }

        [Test]
        public void EverythingElse_EndsTheRun()
        {
            // Cleaning, cooking, doctoring, research: unrelated work, so shed the load first.
            var kind = WorkRunPolicy.Classify(false, false, false);
            Assert.That(kind, Is.EqualTo(WorkRunKind.Other));
            Assert.That(WorkRunPolicy.IsRunOver(kind), Is.True);
            Assert.That(WorkRunPolicy.ContinuesRun(kind), Is.False);
        }

        [Test]
        public void ConstructionWins_WhenADriverIsAlsoAHaulOrAYield()
        {
            // Vanilla's construct delivery IS a haul-to-container, and a modded driver may subclass anything —
            // the Construction label has to win so the material-hold guard recognises it.
            Assert.That(WorkRunPolicy.Classify(true, false, true), Is.EqualTo(WorkRunKind.Construction));
            Assert.That(WorkRunPolicy.Classify(true, true, true), Is.EqualTo(WorkRunKind.Construction));
            Assert.That(WorkRunPolicy.Classify(false, true, true), Is.EqualTo(WorkRunKind.Yield));
        }

        // ---- Oracle: the driver families the Verse layer maps in ------------------------------------

        /// <summary>
        /// The mapping the Verse adapter (<c>OpportunisticUnload.ClassifyJobDef</c>) performs, restated as data:
        /// which driver family answers which of the three questions. Kept here so the SHAPE of that mapping —
        /// notably that all three families continue the run, and that nothing else does — is pinned headlessly.
        /// </summary>
        private static readonly (string driver, bool construction, bool yield, bool haul, WorkRunKind expected)[] DriverFamilies =
        {
            // Construction (the fix): finishing a frame, placing a no-cost frame, HD's inventory construct-delivery.
            ("JobDriver_ConstructFinishFrame",       true,  false, false, WorkRunKind.Construction),
            ("JobDriver_PlaceNoCostFrame",           true,  false, false, WorkRunKind.Construction),
            ("JobDriver_OverloadConstructDeliver",   true,  false, false, WorkRunKind.Construction),
            // The six yield producers.
            ("JobDriver_PlantWork",                  false, true,  false, WorkRunKind.Yield),
            ("JobDriver_Mine",                       false, true,  false, WorkRunKind.Yield),
            ("JobDriver_OperateDeepDrill",           false, true,  false, WorkRunKind.Yield),
            ("JobDriver_GatherAnimalBodyResources",  false, true,  false, WorkRunKind.Yield),
            ("JobDriver_Strip",                      false, true,  false, WorkRunKind.Yield),
            ("JobDriver_Deconstruct",                false, true,  false, WorkRunKind.Yield),
            // The three storage-bound hauls.
            ("JobDriver_HaulToCell",                 false, false, true,  WorkRunKind.Haul),
            ("JobDriver_HaulToContainer",            false, false, true,  WorkRunKind.Haul),
            ("JobDriver_BulkHaul",                   false, false, true,  WorkRunKind.Haul),
            // Anything the three questions all answer "no" for.
            ("JobDriver_CleanFilth (and every other)", false, false, false, WorkRunKind.Other)
        };

        [Test]
        public void EveryMappedDriverFamily_ClassifiesAsExpected()
        {
            foreach (var row in DriverFamilies)
                Assert.That(WorkRunPolicy.Classify(row.construction, row.yield, row.haul), Is.EqualTo(row.expected),
                    $"{row.driver} should classify as {row.expected}");
        }

        [Test]
        public void OnlyOther_EndsTheRun_AcrossEveryMappedFamily()
        {
            foreach (var row in DriverFamilies)
                Assert.That(WorkRunPolicy.IsRunOver(row.expected), Is.EqualTo(row.expected == WorkRunKind.Other),
                    $"{row.driver}");
        }

        [Test]
        public void EveryInputCombination_ReturnsAKnownKind()
        {
            var seen = new HashSet<WorkRunKind>();
            for (int mask = 0; mask < 8; mask++)
            {
                var kind = WorkRunPolicy.Classify((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
                Assert.That(Enum.IsDefined(typeof(WorkRunKind), kind), Is.True);
                // Run-over is exactly "none of the three applied" — no combination may disagree.
                Assert.That(WorkRunPolicy.IsRunOver(kind), Is.EqualTo(mask == 0), $"mask {mask}");
                seen.Add(kind);
            }
            Assert.That(seen, Is.EquivalentTo(new[]
            {
                WorkRunKind.Construction, WorkRunKind.Yield, WorkRunKind.Haul, WorkRunKind.Other
            }), "every kind must be reachable from some combination");
        }
    }
}
