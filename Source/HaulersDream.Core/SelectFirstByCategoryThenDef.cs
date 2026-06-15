using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure single-pass selection of the FIRST element in <c>(categoryIndex asc, then defName ordinal asc)</c>
    /// order — the allocation-free replacement for the LINQ
    /// <c>OrderBy(t =&gt; t.def.FirstThingCategory?.index).ThenBy(t =&gt; t.def.defName).First()</c> the unload driver
    /// (HD-ORDERBY) re-ran per item unloaded. A full sort is wasted when only the first element is needed; this
    /// min-scan reproduces that element exactly, with 0 allocations and 0 closures.
    ///
    /// Tiebreak fidelity (matches the LINQ exactly):
    ///   • A NULL category index sorts LAST — LINQ's <c>OrderBy</c> on a <c>Nullable&lt;int&gt;</c> orders <c>null</c>
    ///     after every value. Callers pass <see cref="NoCategory"/> (= <see cref="int.MaxValue"/>) for null; a real
    ///     <c>int.MaxValue</c> category would be indistinguishable from null, which is harmless (it would sort to the
    ///     same place either way).
    ///   • The defName tiebreak uses <see cref="StringComparer.Ordinal"/> — vanilla <c>OrderBy(string)</c> uses the
    ///     default string comparer, which is culture-sensitive; defNames are invariant ASCII identifiers, so ordinal
    ///     matches in practice and is the stable, culture-independent choice (the oracle test pins parity over
    ///     randomized inputs).
    ///   • STABILITY: LINQ <c>OrderBy</c>/<c>ThenBy</c> is a STABLE sort, so among elements equal on BOTH keys it
    ///     returns the one that appeared FIRST in the source. This scan only REPLACES the running best on a strictly
    ///     smaller key (never on a tie), so it likewise keeps the first-seen element among equals.
    ///
    /// No game types — unit-tested headlessly. The Verse-side caller iterates its own collection and feeds the two
    /// keys per element via <see cref="Begin"/>/<see cref="Consider"/>.
    /// </summary>
    public static class SelectFirstByCategoryThenDef
    {
        /// <summary>The sentinel category index a caller passes for a NULL <c>FirstThingCategory</c> — sorts LAST,
        /// matching LINQ's <c>OrderBy</c> on a <c>Nullable&lt;int&gt;</c>.</summary>
        public const int NoCategory = int.MaxValue;

        /// <summary>
        /// A running "best so far" accumulator for a single min-scan. Construct via <see cref="Begin"/>, feed every
        /// candidate's keys through <see cref="Consider"/>, then read <see cref="HasBest"/> /
        /// <see cref="BestIndex"/>. A value type — no allocation; the caller maps <see cref="BestIndex"/> back to the
        /// element it fed at that index (so the comparison stays game-type-free).
        /// </summary>
        public struct Selector
        {
            private int bestCategory;
            private string bestDefName;
            private int bestIndex;
            private int considered;

            /// <summary>True once at least one candidate has been considered (i.e. a best exists).</summary>
            public bool HasBest => bestIndex >= 0;

            /// <summary>The 0-based ordinal of the winning candidate among the ones fed to <see cref="Consider"/>
            /// (counting every call, in call order), or -1 when none were fed.</summary>
            public int BestIndex => bestIndex;

            internal static Selector Create() => new Selector
            {
                bestCategory = 0,
                bestDefName = null,
                bestIndex = -1,
                considered = 0,
            };

            /// <summary>
            /// Offer one candidate's keys. Replaces the running best only when this candidate sorts STRICTLY before
            /// it in <c>(categoryIndex asc, then defName ordinal asc)</c> order — so ties keep the FIRST-fed element
            /// (stable, matching LINQ). Returns the candidate's 0-based ordinal (so a caller can record it if needed);
            /// the candidate at <see cref="BestIndex"/> after the scan is the winner.
            /// </summary>
            public int Consider(int categoryIndex, string defName)
            {
                int idx = considered++;
                if (bestIndex < 0 || LessThan(categoryIndex, defName, bestCategory, bestDefName))
                {
                    bestCategory = categoryIndex;
                    bestDefName = defName;
                    bestIndex = idx;
                }
                return idx;
            }
        }

        /// <summary>Start a fresh selection.</summary>
        public static Selector Begin() => Selector.Create();

        /// <summary>
        /// True iff <c>(catA, defA)</c> sorts STRICTLY before <c>(catB, defB)</c> in
        /// <c>(categoryIndex asc, then defName ordinal asc)</c>. Exposed for unit tests / direct use. A null defName
        /// sorts before any non-null (mirrors the default string comparer's null handling), and two nulls are equal.
        /// </summary>
        public static bool LessThan(int catA, string defA, int catB, string defB)
        {
            if (catA != catB)
                return catA < catB;
            return string.CompareOrdinal(defA, defB) < 0;
        }
    }
}
