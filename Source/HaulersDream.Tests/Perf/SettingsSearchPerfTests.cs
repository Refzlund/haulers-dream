using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// Allocation cap for the settings-search scorer (issue #138). The settings window scores its full
    /// registry (~135 controls × name/description/category) on EVERY keystroke; the typo path slides a bounded
    /// Levenshtein window across each field, which USED to allocate two <c>int[]</c> per window position — tens
    /// of thousands of gen0 allocations per keystroke, tanking FPS while typing. The fix reuses two
    /// <c>[ThreadStatic]</c> DP rows and precomputes each control's lower-cased name/description, so a per-call
    /// score now allocates only a handful of short query-token substrings (bounded), never the int[] storm.
    ///
    /// <para>These are a before/after regression net: the pre-fix scorer allocated tens of KB per call for this
    /// input (the window slide over the long description), so the sub-KB caps below would fail loudly if the
    /// per-call int[] allocation ever returns.</para>
    /// </summary>
    [TestFixture, Category("Perf")]
    public class SettingsSearchPerfTests
    {
        // A realistic option shape: a short name + a long description + a category, mirroring one registry row.
        private const string Name = "Strict carry weight";
        private const string Desc =
            "Pawns refuse to overload past their carry capacity, so a hauler never picks up more than it can actually carry in a single trip.";
        private const string Cat = "Loading & carriers";

        // "strci" is a transposition typo (as in the behaviour tests) that misses the substring path on the long
        // description and drops to the bounded-Levenshtein window slide — the exact path the fix de-allocated; the
        // clean "carry" token exercises the cheap substring path alongside it.
        private const string Query = "strci carry";

        // The pre-lower-cased inputs the UI hot path (OptionScoreLower) actually receives: the query lower-cased once
        // per keystroke, plus each control's precomputed NameLower/DescLower and the lower-cased category text.
        private static readonly string QueryLower = Query.ToLowerInvariant();
        private static readonly string NameLower = Name.ToLowerInvariant();
        private static readonly string DescLower = Desc.ToLowerInvariant();
        private static readonly string CatLower = Cat.ToLowerInvariant();

        [Test]
        public void OptionScoreLower_TypoQuery_AllocationIsBounded() =>
            AllocationAssert.AssertAllocAtMost(
                () => SettingsSearch.OptionScoreLower(QueryLower, NameLower, DescLower, CatLower),
                512,
                "the per-keystroke UI scorer must not reallocate the bounded-Levenshtein DP rows per window " +
                "(pre-fix this allocated tens of KB for the window slide over the description)");

        [Test]
        public void OptionScore_TypoQuery_AllocationIsBounded() =>
            AllocationAssert.AssertAllocAtMost(
                () => SettingsSearch.OptionScore(Query, Name, Desc, Cat),
                2048,
                "even the raw scorer (which lower-cases its four inputs) must stay far below the pre-fix per-call storm");
    }
}
