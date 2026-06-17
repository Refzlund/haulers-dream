using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class CommonSenseCedePolicyTests
    {
        // Defaulted wrapper: each test states only what it varies.
        private static bool Cede(bool csPresent = true, bool fieldsReadable = true,
                                 bool advCleaning = false, bool advHaulAll = false)
            => CommonSenseCedePolicy.ShouldCedeDoBillFlow(csPresent, fieldsReadable, advCleaning, advHaulAll);

        // --- CEDE truth table (Fix #1) ---

        [Test] // fail-open: CS absent => never cede, even with toggles/unreadable that would otherwise cede.
        public void Absent_NeverCedes()
        {
            Assert.That(Cede(csPresent: false, advCleaning: true, advHaulAll: true), Is.False);
            Assert.That(Cede(csPresent: false, fieldsReadable: false), Is.False);
        }

        [Test] // do-NOT-over-cede: CS present, both toggles off, readable => CS runs vanilla => HD still operates.
        public void PresentBothToggleOff_DoesNotCede()
            => Assert.That(Cede(advCleaning: false, advHaulAll: false), Is.False);

        [Test] // adv_cleaning is CS's default-true real-world case.
        public void PresentCleaningOnly_Cedes()
            => Assert.That(Cede(advCleaning: true, advHaulAll: false), Is.True);

        [Test]
        public void PresentHaulAllOnly_Cedes()
            => Assert.That(Cede(advCleaning: false, advHaulAll: true), Is.True);

        [Test]
        public void PresentBothOn_Cedes()
            => Assert.That(Cede(advCleaning: true, advHaulAll: true), Is.True);

        [TestCase(false, false)] // present-as-owning: unreadable fields => cede regardless of (meaningless) toggles.
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PresentUnreadableFields_CedesRegardlessOfToggles(bool advCleaning, bool advHaulAll)
            => Assert.That(Cede(fieldsReadable: false, advCleaning: advCleaning, advHaulAll: advHaulAll), Is.True);

        // --- UNLOAD-DEFER predicate (Fix #2) ---

        [Test]
        public void DeferUnload_TrueWhenActiveBillMatches()
            => Assert.That(CommonSenseCedePolicy.ShouldDeferUnloadForActiveBill(true), Is.True);

        [Test]
        public void DeferUnload_FalseWhenNoMatchingBill()
            => Assert.That(CommonSenseCedePolicy.ShouldDeferUnloadForActiveBill(false), Is.False);
    }
}
