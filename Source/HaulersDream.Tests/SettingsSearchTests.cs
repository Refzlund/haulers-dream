using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class SettingsSearchTests
    {
        // A small fixed option set used by the ranking tests. Each is (name, desc, category).
        private static readonly (string name, string desc, string cat) StrictCarry =
            ("Strict carry weight", "Pawns refuse to overload past their carry capacity.", "Loading & carriers");
        private static readonly (string name, string desc, string cat) BulkHaul =
            ("Bulk hauling", "Combine many nearby stacks into one trip.", "Hauling");
        private static readonly (string name, string desc, string cat) SpoilFirst =
            ("Cook spoiling food first", "Prefer ingredients that will rot soonest.", "Bills");

        private static float Score((string name, string desc, string cat) o, string q)
            => SettingsSearch.OptionScore(q, o.name, o.desc, o.cat);

        // ---------- field-strength ordering: name > description > category ----------

        [Test]
        public void FieldScore_ExactSubstring_IsFull()
        {
            // A clean word-boundary substring is a full 1.0 match.
            Assert.That(SettingsSearch.FieldScore("carry", "Strict carry weight"), Is.EqualTo(1.0f).Within(1e-4));
        }

        [Test]
        public void OptionScore_NameHit_OutranksDescOnlyHit_OutranksCategoryOnlyHit()
        {
            // "carry" hits the NAME of StrictCarry, the DESCRIPTION of nothing here, and the CATEGORY ("carriers").
            float nameHit = Score(StrictCarry, "carry");           // name substring -> dominant
            // "overload" appears only in the DESCRIPTION of StrictCarry (not the name, not the category).
            float descHit = Score(StrictCarry, "overload");
            // "bills" appears only in the CATEGORY of SpoilFirst (not its name/desc).
            float catHit = Score(SpoilFirst, "bills");

            Assert.That(nameHit, Is.GreaterThan(descHit), "a name hit must outrank a description-only hit");
            Assert.That(descHit, Is.GreaterThan(catHit), "a description hit must outrank a category-only hit");
            Assert.That(catHit, Is.GreaterThan(0f), "a clean category substring still counts as a (weak) match");
        }

        [Test]
        public void WeightConstants_AreOrderedNameDescCategory()
        {
            Assert.That(SettingsSearch.WeightName, Is.GreaterThan(SettingsSearch.WeightDesc));
            Assert.That(SettingsSearch.WeightDesc, Is.GreaterThan(SettingsSearch.WeightCategory));
        }

        // ---------- typo tolerance: matches, but below the clean match ----------

        [Test]
        public void FieldScore_Typo_StillMatches_ButBelowExact()
        {
            float exact = SettingsSearch.FieldScore("strict carry", "Strict carry weight");
            float typo = SettingsSearch.FieldScore("strci carry", "Strict carry weight"); // "strci" = transposed "stric"
            Assert.That(typo, Is.GreaterThan(0f), "a single-transposition typo must still match");
            Assert.That(typo, Is.LessThan(exact), "a typo'd query must score strictly below the exact query");
        }

        [Test]
        public void OptionScore_Typo_RanksTheRightOption_BelowItsExactScore()
        {
            float exact = Score(StrictCarry, "strict carry");
            float typo = Score(StrictCarry, "strci carry");
            Assert.That(typo, Is.GreaterThan(0f));
            Assert.That(typo, Is.LessThan(exact));
            // The typo'd query must still pick StrictCarry over the unrelated options.
            Assert.That(typo, Is.GreaterThan(Score(BulkHaul, "strci carry")));
            Assert.That(typo, Is.GreaterThan(Score(SpoilFirst, "strci carry")));
        }

        // ---------- case-insensitivity ----------

        [Test]
        public void FieldScore_IsCaseInsensitive()
        {
            float lower = SettingsSearch.FieldScore("carry weight", "Strict carry weight");
            float upper = SettingsSearch.FieldScore("CARRY WEIGHT", "Strict CARRY weight");
            float mixed = SettingsSearch.FieldScore("CaRrY wEiGhT", "Strict carry weight");
            Assert.That(upper, Is.EqualTo(lower).Within(1e-4));
            Assert.That(mixed, Is.EqualTo(lower).Within(1e-4));
        }

        // ---------- multi-token ----------

        [Test]
        public void FieldScore_MultiToken_AllTokensSubstring_IsFull()
        {
            Assert.That(SettingsSearch.FieldScore("carry weight", "Strict carry weight"), Is.EqualTo(1.0f).Within(1e-4));
        }

        [Test]
        public void FieldScore_MultiToken_OneNoiseToken_DragsScoreDown()
        {
            // "carry" matches fully; "zzzzzz" matches nothing -> mean of (1.0, 0.0) = 0.5.
            float oneGood = SettingsSearch.FieldScore("carry", "Strict carry weight");
            float withNoise = SettingsSearch.FieldScore("carry zzzzzz", "Strict carry weight");
            Assert.That(withNoise, Is.LessThan(oneGood), "an extra unmatched token must lower the field score");
            Assert.That(withNoise, Is.EqualTo(0.5f).Within(1e-4), "mean of one full + one zero token");
        }

        [Test]
        public void FieldScore_TokenOrderDoesNotMatter()
        {
            // Each token is matched against the whole text independently, so order is irrelevant.
            float ab = SettingsSearch.FieldScore("carry weight", "Strict carry weight");
            float ba = SettingsSearch.FieldScore("weight carry", "Strict carry weight");
            Assert.That(ba, Is.EqualTo(ab).Within(1e-4));
        }

        // ---------- ranking of a fixed option set ----------

        [Test]
        public void OptionScore_RanksFixedSetAsExpected_ForCarryWeight()
        {
            // "carry weight" is the literal name of StrictCarry; the other two options share none of those words
            // in their name, so StrictCarry must rank first and the others far behind (or zero).
            float strict = Score(StrictCarry, "carry weight");
            float bulk = Score(BulkHaul, "carry weight");
            float spoil = Score(SpoilFirst, "carry weight");
            Assert.That(strict, Is.GreaterThan(bulk));
            Assert.That(strict, Is.GreaterThan(spoil));
        }

        [Test]
        public void OptionScore_RanksFixedSetAsExpected_ForSpoil()
        {
            // "spoiling" is in the name of SpoilFirst only.
            float spoil = Score(SpoilFirst, "spoiling");
            float strict = Score(StrictCarry, "spoiling");
            float bulk = Score(BulkHaul, "spoiling");
            Assert.That(spoil, Is.GreaterThan(strict));
            Assert.That(spoil, Is.GreaterThan(bulk));
            Assert.That(spoil, Is.GreaterThan(0f));
        }

        // ---------- noise queries return 0 (NOT a match) ----------

        [Test]
        public void OptionScore_PureNoise_IsZero()
        {
            Assert.That(Score(StrictCarry, "zzzxqq"), Is.EqualTo(0f));
            Assert.That(Score(BulkHaul, "zzzxqq"), Is.EqualTo(0f));
            Assert.That(Score(SpoilFirst, "qwxzkj"), Is.EqualTo(0f));
        }

        [Test]
        public void FieldScore_ShortTypo_DoesNotOverMatch()
        {
            // A 1-2 char token gets NO typo tolerance (too short to distinguish a typo from a different word),
            // so "xy" must not fuzzily match an unrelated word.
            Assert.That(SettingsSearch.FieldScore("xy", "Strict carry weight"), Is.EqualTo(0f).Within(1e-4));
        }

        [Test]
        public void OptionScore_WeakFuzzyCategoryOnly_IsGatedOut()
        {
            // "lodaing" (a transposition typo of "loading", which appears ONLY in StrictCarry's category) hits the
            // category via the weak Levenshtein path (~0.43, below the strong CategoryFloor) and misses name+desc
            // entirely, so the gate must reject it: a fuzzy category-only brush must not surface unrelated options.
            float s = Score(StrictCarry, "lodaing");
            Assert.That(s, Is.EqualTo(0f), "a fuzzy (typo) category-only hit must be gated out (category needs a strong hit)");
        }

        // ---------- null / empty safety ----------

        [Test]
        public void FieldScore_NullOrEmpty_IsZero()
        {
            Assert.That(SettingsSearch.FieldScore(null, "Strict carry weight"), Is.EqualTo(0f));
            Assert.That(SettingsSearch.FieldScore("carry", null), Is.EqualTo(0f));
            Assert.That(SettingsSearch.FieldScore("", "Strict carry weight"), Is.EqualTo(0f));
            Assert.That(SettingsSearch.FieldScore("carry", ""), Is.EqualTo(0f));
            Assert.That(SettingsSearch.FieldScore("   ", "Strict carry weight"), Is.EqualTo(0f), "all-whitespace query -> 0");
        }

        [Test]
        public void OptionScore_NullFields_AreTreatedAsEmpty()
        {
            // Null name/desc/category must not throw; a hit on the one non-null field still scores.
            Assert.That(SettingsSearch.OptionScore("carry", null, null, null), Is.EqualTo(0f));
            float descOnly = SettingsSearch.OptionScore("overload", null, "Pawns refuse to overload.", null);
            Assert.That(descOnly, Is.GreaterThan(0f), "a description hit with null name/category still matches");
        }

        [Test]
        public void OptionScore_NullQuery_IsZero()
        {
            Assert.That(SettingsSearch.OptionScore(null, "Strict carry weight", "desc", "cat"), Is.EqualTo(0f));
        }

        // ---------- word-boundary tie-break ----------

        [Test]
        public void FieldScore_WordBoundaryHit_NotBelowMidWordHit()
        {
            // Both are full substring matches; the boundary bonus is clamped to 1.0, so a boundary hit is never worse
            // than a mid-word hit. (Equal here because both clamp to 1.0 — the bonus only breaks sub-1.0 ties.)
            float boundary = SettingsSearch.FieldScore("carry", "carry weight");      // at idx 0 -> boundary
            float midword = SettingsSearch.FieldScore("carry", "miscarry of goods");  // inside "miscarry"
            Assert.That(boundary, Is.GreaterThanOrEqualTo(midword));
            Assert.That(boundary, Is.EqualTo(1.0f).Within(1e-4));
        }
    }
}
