using System;
using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// Pins how Hauler's Dream classifies its own involvement in an exception observed at a method it patches,
    /// and what it then PRINTS about it (issue #236).
    ///
    /// <para>Three things are under test: the strongest-evidence-first ordering of the three independent facts
    /// a finalizer can establish; the SHAPE of the verdict set, so no member meaning "definitely not HD" can be
    /// added unnoticed; and the exact wording of every sentence, held as golden text. The wording is the actual
    /// deliverable — the defect was a categorical "This is NOT a Hauler's Dream bug" printed from evidence that
    /// could not have shown involvement — so it is pinned by exact match rather than by keyword scanning, which
    /// an early version of this fixture proved a paraphrase walks straight through. See the note above the
    /// golden constants for that evidence and for why a build-time guard script was rejected.</para>
    /// </summary>
    [TestFixture]
    public class BlamePolicyTests
    {
        // --- classification: strongest evidence first -----------------------------------------------------

        [Test]
        public void HdFrameInTrace_IsFrameInTrace()
        {
            // A HaulersDream. frame in the rendered exception is positive, self-contained evidence.
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: true, hdTranspilesMethod: false, hdWrapperIsAncestor: false),
                Is.EqualTo(HdInvolvement.FrameInTrace));
        }

        [Test]
        public void HdFrameInTrace_WinsOverTheWeakerFacts()
        {
            // All three facts true: the frame is the strongest and is what gets reported.
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: true, hdTranspilesMethod: true, hdWrapperIsAncestor: true),
                Is.EqualTo(HdInvolvement.FrameInTrace));
        }

        [Test]
        public void NoFrameButTranspiled_IsTranspiled()
        {
            // HD rewrote this method's IL, so its edits live inside the original method's own frames and can
            // never appear separately (QA #197).
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: false, hdTranspilesMethod: true, hdWrapperIsAncestor: false),
                Is.EqualTo(HdInvolvement.Transpiled));
        }

        [Test]
        public void TranspiledWinsOverAncestorWrapper()
        {
            // An IL edit inside the throwing method is closer to the fault than a wrapper further up the stack.
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: false, hdTranspilesMethod: true, hdWrapperIsAncestor: true),
                Is.EqualTo(HdInvolvement.Transpiled));
        }

        /// <summary>
        /// THE #236 SHAPE. No HaulersDream. frame (a captured trace spans the throw site down to the observing
        /// method, so it cannot contain a caller), no transpiler on the method — yet HD's haul-placement wrapper
        /// was a live ancestor six frames up. This is precisely the case where the old code printed a categorical
        /// "This is NOT a Hauler's Dream bug".
        /// </summary>
        [Test]
        public void NoFrameNoTranspilerButWrapperAbove_IsAncestorWrapper()
        {
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: false, hdTranspilesMethod: false, hdWrapperIsAncestor: true),
                Is.EqualTo(HdInvolvement.AncestorWrapper));
        }

        [Test]
        public void NothingObserved_IsNotObserved()
        {
            // The honest fallthrough: nothing was detected. Not "HD is innocent" — see the wording-contract pin.
            Assert.That(
                BlamePolicy.Classify(hdFrameInTrace: false, hdTranspilesMethod: false, hdWrapperIsAncestor: false),
                Is.EqualTo(HdInvolvement.NotObserved));
        }

        // --- the wording contract -------------------------------------------------------------------------

        /// <summary>
        /// EXHAUSTIVENESS PIN on the verdict set. Every input combination classifies into exactly these four
        /// members, and the enum declares no others — so adding a member that means "definitely not Hauler's
        /// Dream" fails here and forces the author to confront why.
        ///
        /// <para>Why this is a test and not a comment (#236): the finalizer used to print a categorical
        /// "This is NOT a Hauler's Dream bug" derived from a stack trace that structurally could not contain the
        /// evidence — a trace spans the throw site down to the observing method, so it can never show HD's
        /// prefix (already returned), HD's postfix (not yet run), or any HD frame above it. The absence of an HD
        /// frame is therefore not evidence of HD's innocence, and no verdict may assert that it is. The
        /// classification is the only place that could smuggle such a claim back in.</para>
        /// </summary>
        [Test]
        public void VerdictSet_HasExactlyTheFourHonestOutcomes_AndNoDefinitelyNotHdVerdict()
        {
            var declared = Enum.GetValues(typeof(HdInvolvement)).Cast<HdInvolvement>().ToArray();
            Assert.That(declared, Is.EquivalentTo(new[]
            {
                HdInvolvement.FrameInTrace,
                HdInvolvement.Transpiled,
                HdInvolvement.AncestorWrapper,
                HdInvolvement.NotObserved
            }), "a new HdInvolvement member must be reviewed against the #236 wording contract: no verdict may "
              + "claim Hauler's Dream is uninvolved from evidence that could not have shown involvement");
        }

        [Test]
        public void EveryInputCombination_ClassifiesIntoTheDeclaredSet()
        {
            // Drive all eight combinations: the classifier is total, and never invents a verdict outside the set.
            foreach (bool frame in new[] { false, true })
            foreach (bool transpiled in new[] { false, true })
            foreach (bool ancestor in new[] { false, true })
            {
                var verdict = BlamePolicy.Classify(frame, transpiled, ancestor);
                Assert.That(Enum.IsDefined(typeof(HdInvolvement), verdict), Is.True,
                    $"({frame}, {transpiled}, {ancestor}) produced an undeclared verdict");
            }
        }

        /// <summary>
        /// The one direction that must never invert: a missing HD frame, on its own, must not upgrade the
        /// verdict's confidence. Whatever the other two facts say, "no frame" can only ever yield one of the
        /// weaker outcomes — never <see cref="HdInvolvement.FrameInTrace"/>, and never a claim of innocence.
        /// </summary>
        [Test]
        public void MissingHdFrame_NeverProducesAPositiveOrExculpatoryVerdict()
        {
            foreach (bool transpiled in new[] { false, true })
            foreach (bool ancestor in new[] { false, true })
            {
                var verdict = BlamePolicy.Classify(hdFrameInTrace: false, hdTranspilesMethod: transpiled,
                    hdWrapperIsAncestor: ancestor);
                Assert.That(verdict, Is.Not.EqualTo(HdInvolvement.FrameInTrace),
                    "a missing frame can never be reported as a frame in the trace");
                Assert.That(verdict, Is.AnyOf(HdInvolvement.Transpiled, HdInvolvement.AncestorWrapper,
                    HdInvolvement.NotObserved));
            }
        }

        // --- the PRINTED wording, pinned as GOLDEN TEXT ----------------------------------------------------
        //  These sentences ARE the deliverable of #236, which is why they live in Core beside the enum rather
        //  than in the Verse glue that prints them: a contract nothing can test is just a comment.
        //
        //  Why golden text and not a keyword scan. The first version of this fixture asserted five literal
        //  substrings of the OLD sentence, and review showed a rewritten `default:` arm — "so you can rule it
        //  out", "in practice this is another mod's problem" — passing every single test in the file: it kept
        //  the scoping phrase, kept "NOT proof", and matched none of the blacklisted phrasings. A blacklist
        //  cannot enumerate the ways English says "not us" ("HD played no part", "merely a bystander", or the
        //  same words with the apostrophe dropped), and tightening it risks the opposite error, since the
        //  honest NotObserved text legitimately CONTAINS "Hauler's Dream is uninvolved" inside "That is NOT
        //  proof Hauler's Dream is uninvolved". Exact-match instead: rewording a verdict now requires editing
        //  this file, which puts the new wording in front of a reviewer. The two scans below stay as a second
        //  net for anything ADDED to the enum later, which golden text cannot cover by construction.
        //
        //  A build-time `scripts/check-blame-wording.ts` guard was considered and deliberately REJECTED:
        //  `bun run test` runs in the same workflow as the build guards, so a duplicate guard would add a
        //  seventh script asserting exactly what these tests already assert.

        private const string FrameInTraceText =
            "Hauler's Dream's own code IS in this exception's stack, so it may be involved, though the original "
            + "method or another mod's patch on it could still be the real cause.";

        private const string TranspiledText =
            "Hauler's Dream TRANSPILES this method (it edits the method's IL), so even though its own code is "
            + "not a separate frame in the stack it could still be involved; the original method or another "
            + "mod's patch on it may also be the cause.";

        // Two phrasing traps are baked into this one, both shipped and caught in review: (a) "further out on
        // the call stack" rather than "above this method", because a PRINTED trace puts callers BELOW the throw
        // site, so "above" reads backwards to anyone looking at the log; (b) the parenthesis about Harmony's
        // annotation lines, without which the sentence appears to contradict the trace printed underneath it
        // (that trace names HD on every HD-patched frame — as a patch owner, not as code that ran).
        private const string AncestorWrapperText =
            "Hauler's Dream is further out on the call stack than this method — one of its marked item-placement "
            + "paths called into the code that threw — and it does not appear as a frame in the trace printed "
            + "below, because a stack records what a method called, never who called it. (Lines starting with "
            + "'- PREFIX', '- POSTFIX', '- TRANSPILER' or '- FINALIZER' are Harmony listing which mods PATCH "
            + "that frame, not which code ran, so Hauler's Dream's name can appear there without any of its "
            + "code having run.) That wrapper only invokes the original action unchanged, so this is not by "
            + "itself blame; it means Hauler's Dream was involved in getting here.";

        // The verdict #236 was actually about. Two obligations beyond "no denial": it must say out loud that
        // finding nothing is NOT proof, and it must scope its no-ancestor claim to the placement paths HD
        // actually marks — HD's bulk-haul, bill-gather, refuel and transporter/portal/vehicle load drivers all
        // reach a placement UNMARKED, so a wider claim would be the same over-reach in a new place.
        private const string NotObservedText =
            "Nothing here points at Hauler's Dream: no Hauler's Dream frame was found in this exception's "
            + "stack, Hauler's Dream does not rewrite this method's code, and none of the item-placement paths "
            + "it marks — the only ancestors it tracks — is further out on the call stack. That is NOT proof "
            + "Hauler's Dream is uninvolved. A stack records what a method called, never who called it, so the "
            + "trace printed "
            + "below cannot show a Hauler's Dream prefix that already returned, a postfix that has not run yet, "
            + "or any Hauler's Dream code further out — including Hauler's Dream's own jobs, which are not "
            + "tracked. (Lines starting with '- PREFIX', '- POSTFIX', '- TRANSPILER' or '- FINALIZER' are "
            + "Harmony listing which mods PATCH that frame, not which code ran, so Hauler's Dream's name "
            + "appearing there is not a frame.) On the evidence available here the cause is most likely the "
            + "original method itself or another mod patching it.";

        // The out-of-range arm. It must NOT reuse NotObservedText: that sentence asserts three specific
        // negative findings (no frame, no transpiler, no tracked ancestor) which were never established when
        // the value could not be classified at all.
        private const string UnclassifiedText =
            "Hauler's Dream could not classify its involvement in this fault, so nothing has been established "
            + "either way — it may or may not be involved. The trace below is the only evidence here.";

        [Test]
        public void EachVerdict_PrintsItsExactAgreedSentence()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BlamePolicy.Describe(HdInvolvement.FrameInTrace), Is.EqualTo(FrameInTraceText));
                Assert.That(BlamePolicy.Describe(HdInvolvement.Transpiled), Is.EqualTo(TranspiledText));
                Assert.That(BlamePolicy.Describe(HdInvolvement.AncestorWrapper), Is.EqualTo(AncestorWrapperText));
                Assert.That(BlamePolicy.Describe(HdInvolvement.NotObserved), Is.EqualTo(NotObservedText));
            });
        }

        /// <summary>
        /// Totality. The finalizer is an exception handler: a throw there is swallowed by Harmony and the whole
        /// breadcrumb vanishes, so an unknown enum value must degrade to an honest sentence rather than throw.
        /// It gets its OWN wording — deliberately not an alias of any real verdict, so the "which facts were
        /// established" claim stays true.
        /// </summary>
        [Test]
        public void OutOfRangeValue_GetsItsOwnHonestSentence_NotAnotherVerdictsClaims()
        {
            string text = BlamePolicy.Describe((HdInvolvement)999);
            Assert.That(text, Is.EqualTo(UnclassifiedText));
            Assert.That(text, Is.Not.EqualTo(BlamePolicy.Describe(HdInvolvement.NotObserved)),
                "the unclassified arm must not borrow findings it never made");
        }

        // --- the second net: obligations every verdict must meet, including ones added later ----------------

        /// <summary>Every declared verdict plus an out-of-range value, so a member added later is covered by
        /// the scans below even though golden text cannot know about it.</summary>
        private static IEnumerable<HdInvolvement> AllVerdictsAndAnUnknown()
        {
            foreach (HdInvolvement verdict in Enum.GetValues(typeof(HdInvolvement)))
                yield return verdict;
            yield return (HdInvolvement)999;
        }

        /// <summary>Phrasings that would reintroduce the #236 defect outright. Not a sufficient guard on its
        /// own (see the golden-text note above) — a backstop for verdicts golden text does not cover.</summary>
        private static readonly string[] CategoricalDenials =
        {
            "is NOT a Hauler's Dream bug",
            "not a Hauler's Dream bug",
            "not a HaulersDream bug",
            "Hauler's Dream played no part",
            "HD played no part",
            "merely a bystander",
            "only a bystander",
            "rule it out",
            "you can rule out"
        };

        /// <summary>The hedges that separate a DESCRIPTION of evidence from a VERDICT on blame, in two
        /// families: an epistemic modal ("may", "could", "most likely"), or an explicit statement that the
        /// finding is not conclusive ("NOT proof", "not by itself blame"). Every sentence must carry at least
        /// one; a sentence with none is stating a conclusion, which is the #236 defect however it is worded.
        /// (The second family exists because AncestorWrapper reports a fact HD is certain of — its wrapper IS
        /// on the stack — so it cannot hedge the observation; it hedges what the observation MEANS.)</summary>
        private static readonly string[] RequiredHedges =
            { "may", "could", "most likely", "NOT proof", "not by itself blame" };

        [Test]
        public void EveryVerdict_HasANonEmptyDescription()
        {
            foreach (var verdict in AllVerdictsAndAnUnknown())
            {
                string text = BlamePolicy.Describe(verdict);
                Assert.That(text, Is.Not.Null.And.Not.Empty, $"{verdict} must print something");
                Assert.That(text.Trim(), Is.Not.Empty, $"{verdict} must print more than whitespace");
            }
        }

        /// <summary>
        /// THE #236 CONTRACT. No verdict may assert that Hauler's Dream is uninvolved. The absence of an HD
        /// frame is not evidence of innocence — a captured stack records what a method called, never who called
        /// it, so it can never contain HD's prefix (already returned), HD's postfix (not yet run), or any HD
        /// frame further out. The old wording claimed exactly that, and claimed it in the one report where HD's
        /// own haul-placement wrapper was the caller.
        /// </summary>
        [Test]
        public void NoVerdict_AssertsHaulersDreamIsUninvolved()
        {
            foreach (var verdict in AllVerdictsAndAnUnknown())
            {
                string text = BlamePolicy.Describe(verdict);
                foreach (string denial in CategoricalDenials)
                {
                    Assert.That(text.IndexOf(denial, StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                        $"{verdict} must never claim Hauler's Dream is uninvolved — found \"{denial}\"");
                }
            }
        }

        /// <summary>
        /// The POSITIVE half of the contract, and the one a blacklist cannot express: it is not enough to avoid
        /// the banned phrasings, the sentence has to actually hedge. A verdict that reads as a flat conclusion
        /// is the #236 defect however it is worded, so each must carry at least one explicit hedge.
        /// </summary>
        [Test]
        public void EveryVerdict_HedgesRatherThanConcludes()
        {
            foreach (var verdict in AllVerdictsAndAnUnknown())
            {
                string text = BlamePolicy.Describe(verdict);
                bool hedged = false;
                foreach (string hedge in RequiredHedges)
                {
                    if (text.IndexOf(hedge, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hedged = true;
                        break;
                    }
                }
                Assert.That(hedged, Is.True,
                    $"{verdict} states a conclusion with no hedge — expected one of "
                    + string.Join(", ", RequiredHedges));
            }
        }
    }
}
