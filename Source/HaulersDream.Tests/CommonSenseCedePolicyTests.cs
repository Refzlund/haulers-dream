using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The two Common Sense facts and the gap between them (issue #243). "Common Sense holds the DoBill driver"
    /// and "Common Sense gathers the ingredients" are different questions with different answers, and ceding on
    /// the first is what left nobody gathering.
    /// </summary>
    [TestFixture]
    public class CommonSenseCedePolicyTests
    {
        // Defaulted wrappers: each test states only what it varies.
        private static bool Cede(bool csPresent = true, bool fieldsReadable = true, bool advHaulAll = false)
            => CommonSenseCedePolicy.CommonSenseGathersIngredients(csPresent, fieldsReadable, advHaulAll);

        private static bool OwnsDriver(bool csPresent = true, bool fieldsReadable = true,
                                       bool advCleaning = false, bool advHaulAll = false)
            => CommonSenseCedePolicy.CommonSenseOwnsDoBillDriver(csPresent, fieldsReadable, advCleaning, advHaulAll);

        // --- THE REPORTED BUG (Lensrub, 2026-08-03) ---

        [Test] // Cleaning on, haul-all off: CS takes the driver but hands collecting back to vanilla's own
               // CollectIngredientsToils, so it gathers nothing. HD must NOT cede — ceding here is the bug, and
               // it made the per-bench button able to stop the gather and unable to start it.
        public void CleaningOnly_OwnsTheDriverButDoesNotGather_SoHdKeepsGathering()
        {
            Assert.That(OwnsDriver(advCleaning: true, advHaulAll: false), Is.True,
                "Common Sense's Prefix replaces the toil chain when either option is on.");
            Assert.That(Cede(advHaulAll: false), Is.False,
                "#243: HD must not stand down for a gather that will not happen.");
        }

        // --- CEDE truth table: keyed on adv_haul_all_ings alone ---

        [Test] // fail-open: CS absent => never cede, even on the unreadable path that would otherwise cede.
        public void Absent_NeverCedes()
        {
            Assert.That(Cede(csPresent: false, advHaulAll: true), Is.False);
            Assert.That(Cede(csPresent: false, fieldsReadable: false), Is.False);
        }

        [Test] // CS gathering is the one case HD stands aside for.
        public void PresentHaulAllOn_Cedes()
            => Assert.That(Cede(advHaulAll: true), Is.True);

        [Test] // do-NOT-over-cede: no haul-all => nothing foreign gathers => HD keeps operating.
        public void PresentHaulAllOff_DoesNotCede()
            => Assert.That(Cede(advHaulAll: false), Is.False);

        [TestCase(false)] // present-as-owning: unreadable fields => cede regardless of the (meaningless) toggle.
        [TestCase(true)]
        public void PresentUnreadableFields_CedesRegardlessOfToggle(bool advHaulAll)
            => Assert.That(Cede(fieldsReadable: false, advHaulAll: advHaulAll), Is.True);

        [Test] // The cleaning option is not an input to the cede at all — pinned so a future "shouldn't we OR this
               // back in?" has to delete a test rather than pass one.
        public void CleaningOptionCannotAffectTheCede()
        {
            // CommonSenseGathersIngredients has no adv_cleaning parameter, so the only way to observe the claim is
            // that the driver-ownership rule DOES move with it while the cede rule stays put.
            Assert.That(OwnsDriver(advCleaning: false, advHaulAll: false), Is.False);
            Assert.That(OwnsDriver(advCleaning: true, advHaulAll: false), Is.True);
            Assert.That(Cede(advHaulAll: false), Is.False);
        }

        // --- DRIVER-OWNERSHIP truth table: either option (the allowBatchUnderCommonSense opt-in's fact) ---

        [Test]
        public void Absent_NeverOwnsTheDriver()
        {
            Assert.That(OwnsDriver(csPresent: false, advCleaning: true, advHaulAll: true), Is.False);
            Assert.That(OwnsDriver(csPresent: false, fieldsReadable: false), Is.False);
        }

        [Test] // CS's own Prefix returns true (vanilla runs) only when BOTH its options are off.
        public void PresentBothTogglesOff_DoesNotOwnTheDriver()
            => Assert.That(OwnsDriver(advCleaning: false, advHaulAll: false), Is.False);

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PresentEitherToggleOn_OwnsTheDriver(bool advCleaning, bool advHaulAll)
            => Assert.That(OwnsDriver(advCleaning: advCleaning, advHaulAll: advHaulAll), Is.True);

        [TestCase(false, false)] // present-as-owning: unreadable fields => own regardless of (meaningless) toggles.
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PresentUnreadableFields_OwnsTheDriverRegardlessOfToggles(bool advCleaning, bool advHaulAll)
            => Assert.That(OwnsDriver(fieldsReadable: false, advCleaning: advCleaning, advHaulAll: advHaulAll), Is.True);

        // --- the relationship between the two facts ---

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GatheringImpliesOwningTheDriver(bool advCleaning, bool advHaulAll)
        {
            // One direction only, and that asymmetry IS issue #243: CS cannot gather without holding the driver,
            // but it very much can hold the driver without gathering.
            if (Cede(advHaulAll: advHaulAll))
                Assert.That(OwnsDriver(advCleaning: advCleaning, advHaulAll: advHaulAll), Is.True);
        }

        // --- UNLOAD-DEFER predicate (Fix #2) ---

        [Test]
        public void DeferUnload_TrueWhenActiveBillMatches()
            => Assert.That(CommonSenseCedePolicy.ShouldDeferUnloadForActiveBill(true), Is.True);

        [Test]
        public void DeferUnload_FalseWhenNoMatchingBill()
            => Assert.That(CommonSenseCedePolicy.ShouldDeferUnloadForActiveBill(false), Is.False);
    }
}
