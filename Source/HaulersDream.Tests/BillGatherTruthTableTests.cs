using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Issue #243, end to end: for EVERY setting a player can reach — Common Sense's two options (plus the mod
    /// being absent, plus its fields being unreadable) crossed with Hauler's Dream's three crafting checkboxes —
    /// who actually gathers a bill's ingredients, and does the per-bench button tell the truth about it?
    ///
    /// <para>WHY a whole-table fixture rather than more single-rule cases: the shipped bug was not a wrong rule.
    /// Both rules were right on their own. It was the COMPOSITION — the cede read one fact and the notice read
    /// another — and a per-rule test cannot see a composition. This fixture reproduces the composition the Verse
    /// glue performs (<c>CommonSenseCompat</c> → <c>GatherNotice</c> → the bench gizmo) and grades the whole thing
    /// against a single property: <b>the sentence the player reads must match who is really gathering</b>.</para>
    /// </summary>
    [TestFixture]
    public class BillGatherTruthTableTests
    {
        /// <summary>
        /// One reachable Common Sense configuration. <see cref="Present"/> false is "mod not installed";
        /// <see cref="FieldsReadable"/> false is a fork or rename that moved the option fields, where HD cannot
        /// prove what the player chose.
        /// </summary>
        private struct CsState
        {
            /// <summary>Did CommonSense.Settings resolve?</summary>
            public bool Present;

            /// <summary>Did both option fields bind by reflection?</summary>
            public bool FieldsReadable;

            /// <summary>CS's adv_cleaning — clean the room between bills. Ships ON.</summary>
            public bool Cleaning;

            /// <summary>CS's adv_haul_all_ings — pocket every ingredient first. Ships ON.</summary>
            public bool HaulAll;

            /// <summary>A label naming this configuration, so a failing row identifies itself.</summary>
            public string Label;
        }

        /// <summary>Every Common Sense configuration a player (or a fork) can put HD in.</summary>
        private static IEnumerable<CsState> CsStates()
        {
            yield return new CsState { Present = false, Label = "CS absent" };
            yield return new CsState { Present = true, FieldsReadable = false, Label = "CS present, fields unreadable" };
            yield return new CsState { Present = true, FieldsReadable = true, Cleaning = false, HaulAll = false, Label = "CS: cleaning off, haul-all off" };
            yield return new CsState { Present = true, FieldsReadable = true, Cleaning = true, HaulAll = false, Label = "CS: cleaning ON, haul-all off (the #243 case)" };
            yield return new CsState { Present = true, FieldsReadable = true, Cleaning = false, HaulAll = true, Label = "CS: cleaning off, haul-all ON" };
            yield return new CsState { Present = true, FieldsReadable = true, Cleaning = true, HaulAll = true, Label = "CS: cleaning ON, haul-all ON (CS defaults)" };
        }

        /// <summary>
        /// The sentence that is honest for a given gatherer. This mapping is the whole contract: a notice is
        /// correct if and only if it is the one line that describes what is really happening.
        /// </summary>
        /// <param name="gatherer">Who is actually sweeping ingredients.</param>
        /// <returns>The only <see cref="BenchGatherNotice"/> that is true of that state.</returns>
        private static BenchGatherNotice HonestNoticeFor(BillGatherer gatherer)
        {
            switch (gatherer)
            {
                // Another mod is doing it, so HD's controls cannot change what the player is watching.
                case BillGatherer.ForeignMod:
                    return BenchGatherNotice.ForeignModGathers;
                // HD is doing it, so the button's own state description is the truth and needs no caveat.
                case BillGatherer.HaulersDream:
                    return BenchGatherNotice.None;
                // Nobody is: the player switched HD's gather off and nothing took over. Say which box did it.
                default:
                    return BenchGatherNotice.GlobalGatherOff;
            }
        }

        [Test]
        public void EverySettingCombination_NoticeMatchesWhoActuallyGathers()
        {
            int rows = 0;

            foreach (var cs in CsStates())
            for (int hd = 0; hd < 8; hd++)
            {
                bool inventoryCraftDeliver = (hd & 1) != 0;
                bool shareForCrafting = (hd & 2) != 0;
                bool markForUnload = (hd & 4) != 0;

                // --- the composition the Verse glue performs, in the same order ---
                // CommonSenseCompat.GathersIngredients: the cede AND the notice's foreign-gatherer fact, one read.
                bool csGathers = CommonSenseCedePolicy.CommonSenseGathersIngredients(cs.Present, cs.FieldsReadable, cs.HaulAll);
                // GatherNotice.PlainGatherEnabled → the route's own entry condition.
                bool hdGatherOn = GatherOwnershipPolicy.PlainGatherEnabled(inventoryCraftDeliver, shareForCrafting, markForUnload);
                var gatherer = GatherOwnershipPolicy.ResolveGatherer(csGathers, hdGatherOn);
                var notice = GatherOwnershipPolicy.ResolveNotice(csGathers, hdGatherOn);
                bool governs = GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(csGathers, hdGatherOn);

                string where = cs.Label
                    + $" | HD: carry-in-inventory={inventoryCraftDeliver}, share-for-crafting={shareForCrafting}, auto-unload={markForUnload}";

                // 1. HD stands down exactly when Common Sense will really gather — never merely because it holds
                //    the driver. This is the fix.
                Assert.That(csGathers, Is.EqualTo(cs.Present && (!cs.FieldsReadable || cs.HaulAll)),
                    "cede rule — " + where);

                // 1b. HD's own gather needs all three checkboxes. Spelled out independently rather than taken from
                //     the rule under test, or every row below would agree with a broken gate by construction.
                Assert.That(hdGatherOn, Is.EqualTo(inventoryCraftDeliver && shareForCrafting && markForUnload),
                    "HD's own gather gate — " + where);

                // 2. Somebody gathers unless the player switched HD's own gather off. In particular the #243 row
                //    (CS cleaning on, haul-all off, HD's boxes ticked) must land on HaulersDream, not Nobody.
                var expectedGatherer = csGathers ? BillGatherer.ForeignMod
                    : hdGatherOn ? BillGatherer.HaulersDream
                    : BillGatherer.Nobody;
                Assert.That(gatherer, Is.EqualTo(expectedGatherer), "who gathers — " + where);

                // 3. The player-facing sentence is the honest one for that gatherer, in every row.
                Assert.That(notice, Is.EqualTo(HonestNoticeFor(gatherer)), "notice honesty — " + where);

                // 4. The bench button governs the plain gather exactly when HD is the gatherer.
                Assert.That(governs, Is.EqualTo(gatherer == BillGatherer.HaulersDream), "bench switch scope — " + where);

                rows++;
            }

            // A sweep that silently enumerated nothing would pass every assertion above.
            Assert.That(rows, Is.EqualTo(48), "the table must cover 6 Common Sense states × 8 Hauler's Dream states.");
        }

        [Test] // The reporter's own configuration, spelled out rather than left implicit in the loop.
        public void Issue243_CleaningOnHaulAllOff_HdGathersAndTheButtonMeansWhatItSays()
        {
            bool csGathers = CommonSenseCedePolicy.CommonSenseGathersIngredients(csPresent: true, fieldsReadable: true, advHaulAll: false);
            bool hdGatherOn = GatherOwnershipPolicy.PlainGatherEnabled(true, true, true);

            Assert.That(GatherOwnershipPolicy.ResolveGatherer(csGathers, hdGatherOn), Is.EqualTo(BillGatherer.HaulersDream));
            Assert.That(GatherOwnershipPolicy.ResolveNotice(csGathers, hdGatherOn), Is.EqualTo(BenchGatherNotice.None));
            Assert.That(GatherOwnershipPolicy.BenchSwitchGovernsPlainGather(csGathers, hdGatherOn), Is.True);
        }

        [Test] // The regression, as a witness rather than as a comment: run the SHIPPED (pre-1.24) cede expression
               // — cede whenever Common Sense holds the driver — through the same composition and watch the notice
               // come out clean while nobody gathers. If a future change reinstates that expression, the row above
               // fails and this one explains why.
        public void OldCedeOnDriverOwnership_LeftNobodyGatheringWhileTheButtonSaidNothingWasWrong()
        {
            const bool cleaning = true, haulAll = false;
            bool shippedCede = CommonSenseCedePolicy.CommonSenseOwnsDoBillDriver(
                csPresent: true, fieldsReadable: true, advCleaning: cleaning, advHaulAll: haulAll);
            bool hdGatherOn = GatherOwnershipPolicy.PlainGatherEnabled(true, true, true);

            // HD ceded, so HD did not gather; Common Sense's cleaning-only chain runs vanilla's collect, so it did
            // not gather either. Nobody did.
            Assert.That(shippedCede, Is.True, "the shipped rule ceded on driver ownership.");
            Assert.That(hdGatherOn && shippedCede, Is.True, "HD's own gather was on and overruled by the cede.");

            // And the notice was driven off the NARROWER fact, so it reported no caveat at all.
            bool noticeInput = CommonSenseCedePolicy.CommonSenseGathersIngredients(
                csPresent: true, fieldsReadable: true, advHaulAll: haulAll);
            Assert.That(GatherOwnershipPolicy.ResolveNotice(noticeInput, hdGatherOn), Is.EqualTo(BenchGatherNotice.None),
                "the button claimed pawns gather here while the cede had already stopped them.");
        }
    }
}
