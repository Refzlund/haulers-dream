using System;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure, game-independent fuzzy scorer for the settings search box (no RimWorld/Unity references — unit-tested
    /// headlessly). Given a user query and the text of a setting (its name, description, and category/header), it
    /// returns a [0,1] relevance score; the UI ranks every option by <see cref="OptionScore"/> and shows the matches
    /// in descending order.
    ///
    /// Matching is case-insensitive, multi-token (the query is split on whitespace and EACH token must find a home in
    /// the text — the field score is the MEAN of every token's best match, so an extra noise token drags the whole
    /// query down), and typo-tolerant. A single token is scored by the strongest of three strategies, in descending
    /// strength:
    ///   1. <b>substring</b> — the token appears verbatim in the text (1.0, with a small bonus when it begins at a word
    ///      boundary so "carry" prefers "carry weight" over "miscarry");
    ///   2. <b>in-order subsequence</b> — the token's characters appear in order but not contiguously (~0.5..0.7, higher
    ///      the more consecutive the run, so "crywt" still finds "carry weight" but weaker than a clean substring);
    ///   3. <b>typo fallback</b> — a bounded-window Levenshtein of the token against any same-length window of the text,
    ///      scored (1 - dist/len) and multiplied by a typo penalty; a token whose best edit distance exceeds
    ///      ceil(len/3) scores 0, which keeps a genuinely unrelated token (e.g. "zzzxqq") from matching anything.
    /// All methods are deterministic and null-safe (null is treated as empty -> 0).
    /// </summary>
    public static class SettingsSearch
    {
        // Field weights for the weighted total in OptionScore. A name hit dominates; a category-only hit is weak.
        public const float WeightName = 1.0f, WeightDesc = 0.45f, WeightCategory = 0.2f;

        // ----- tuning constants (exposed so the UI side / QA can see the exact thresholds) -----

        /// <summary>Bonus added to a 1.0 substring hit when the match starts at a word boundary (clamped back to 1.0,
        /// so it only breaks ties between equally-strong substring hits — a boundary hit edges out a mid-word hit).</summary>
        public const float WordBoundaryBonus = 0.10f;

        /// <summary>Floor of the in-order-subsequence band (all chars present in order but maximally scattered).</summary>
        public const float SubsequenceBase = 0.50f;

        /// <summary>Span of the subsequence band added on top of <see cref="SubsequenceBase"/> as the matched chars
        /// get more consecutive (a fully-consecutive run that somehow wasn't a substring would approach this top).</summary>
        public const float SubsequenceSpan = 0.20f;

        /// <summary>Multiplier applied to a Levenshtein typo match — a typo can never out-score a clean subsequence
        /// or substring, and even a single-edit typo on a short token tops out around 0.6.</summary>
        public const float TypoPenalty = 0.60f;

        /// <summary>OptionScore only counts as a match when the best of name/desc reaches this, OR category reaches
        /// <see cref="CategoryFloor"/>. Keeps weak typo noise (which can leak a few hundredths into a field) out.</summary>
        public const float NameDescFloor = 0.20f;

        /// <summary>A category/header-only hit must be strong (essentially a substring or near-exact) to count, since
        /// the category text is short and generic — a fuzzy category brush must not surface unrelated options.</summary>
        public const float CategoryFloor = 0.50f;

        /// <summary>
        /// [0,1] how well <paramref name="query"/> matches <paramref name="text"/>. Case-insensitive, multi-token
        /// (split on whitespace; the result is the MEAN of each query token's best single-token match against the
        /// whole text), typo-tolerant. Returns 0 if either argument is null/empty or the query is all whitespace.
        /// </summary>
        public static float FieldScore(string query, string text)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
                return 0f;

            string t = text.ToLowerInvariant();

            // Split the query into tokens on whitespace. We sum each token's best score and divide by the token count
            // so a query of N tokens needs all N to land well to score high (one stray token halves a 2-token query).
            float sum = 0f;
            int count = 0;
            int i = 0, n = query.Length;
            while (i < n)
            {
                // skip whitespace
                while (i < n && char.IsWhiteSpace(query[i])) i++;
                if (i >= n) break;
                int start = i;
                while (i < n && !char.IsWhiteSpace(query[i])) i++;
                string token = query.Substring(start, i - start).ToLowerInvariant();
                if (token.Length == 0) continue;
                sum += TokenScore(token, t);
                count++;
            }

            if (count == 0) return 0f; // query was all whitespace
            return sum / count;
        }

        /// <summary>
        /// Weighted total = WeightName*FieldScore(name) + WeightDesc*FieldScore(desc) + WeightCategory*FieldScore(category).
        /// Returns 0 (NOT a match) unless the result is meaningful: max(nameScore, descScore) &gt;= <see cref="NameDescFloor"/>
        /// OR categoryScore &gt;= <see cref="CategoryFloor"/>. Null-safe (null is treated as empty).
        /// </summary>
        public static float OptionScore(string query, string name, string description, string category)
        {
            float nameScore = FieldScore(query, name);
            float descScore = FieldScore(query, description);
            float catScore = FieldScore(query, category);

            // Gate: a result must be backed by a real hit in the name/description, or a strong category hit. Without
            // this a stray fuzzy brush against one field (or a generic category word) would surface unrelated options.
            bool meaningful = Math.Max(nameScore, descScore) >= NameDescFloor || catScore >= CategoryFloor;
            if (!meaningful) return 0f;

            return WeightName * nameScore + WeightDesc * descScore + WeightCategory * catScore;
        }

        // ----- per-token scoring (text is already lower-cased; token is already lower-cased & non-empty) -----

        private static float TokenScore(string token, string text)
        {
            // 1) substring — strongest. 1.0, plus a small word-boundary bonus (clamped to 1.0) so a boundary hit
            //    edges out a mid-word hit of the same token.
            int idx = text.IndexOf(token, StringComparison.Ordinal);
            if (idx >= 0)
            {
                bool atBoundary = idx == 0 || !IsWordChar(text[idx - 1]);
                float s = 1.0f + (atBoundary ? WordBoundaryBonus : 0f);
                return s > 1.0f ? 1.0f : s;
            }

            // 2) in-order subsequence — all of the token's chars appear in order. Score by how consecutive the matched
            //    run was (the more adjacencies, the closer to a real substring).
            float subseq = SubsequenceScore(token, text);
            // 3) typo fallback — bounded-window Levenshtein. Computed independently; we take the best of the two.
            float typo = TypoScore(token, text);

            float best = subseq > typo ? subseq : typo;
            return best;
        }

        /// <summary>
        /// If every character of <paramref name="token"/> appears in <paramref name="text"/> in order, returns a score
        /// in [<see cref="SubsequenceBase"/>, <see cref="SubsequenceBase"/>+<see cref="SubsequenceSpan"/>] that rises
        /// with the fraction of matched chars that were consecutive in the text; otherwise 0. (A single-char token that
        /// isn't a substring can't reach here — IndexOf would have found it — so token.Length is effectively &gt;= 1
        /// and the "consecutive" measure uses token.Length-1 gaps.) Greedy earliest-match, which is the standard cheap
        /// subsequence test and deterministic.
        /// </summary>
        private static float SubsequenceScore(string token, string text)
        {
            if (token.Length == 0) return 0f;
            if (token.Length == 1)
            {
                // A single char that wasn't a substring simply isn't present at all -> no subsequence.
                return text.IndexOf(token[0]) >= 0 ? SubsequenceBase : 0f;
            }

            int ti = 0;            // index into text
            int matched = 0;       // chars of token matched so far
            int consecutive = 0;   // number of token-adjacent pairs that landed on text-adjacent positions
            int lastPos = -2;      // text position of the previously matched char
            for (int k = 0; k < token.Length; k++)
            {
                char c = token[k];
                int found = text.IndexOf(c, ti);
                if (found < 0) return 0f; // a char is missing -> not a subsequence
                if (matched > 0 && found == lastPos + 1) consecutive++;
                lastPos = found;
                ti = found + 1;
                matched++;
            }

            // matched == token.Length here. Consecutiveness fraction over the (token.Length-1) adjacencies.
            float frac = (float)consecutive / (token.Length - 1);
            return SubsequenceBase + SubsequenceSpan * frac;
        }

        /// <summary>
        /// Typo fallback: the minimum Levenshtein distance of <paramref name="token"/> against any window of
        /// <paramref name="text"/> whose length is within ±1 of the token length, converted to a score
        /// (1 - dist/len) * <see cref="TypoPenalty"/>. A token whose best distance exceeds ceil(len/3) scores 0 — so a
        /// genuinely unrelated token (its nearest window still many edits away) never matches. Tokens shorter than 3
        /// chars are not given typo tolerance (too short to tell a typo from a different word) and score 0 here.
        /// </summary>
        private static float TypoScore(string token, string text)
        {
            int len = token.Length;
            if (len < 3) return 0f; // a 1-2 char token typo is indistinguishable from a different word -> no fuzzy match.

            int maxDist = (len + 2) / 3; // ceil(len/3)
            int best = int.MaxValue;

            // Slide windows of length len-1, len, len+1 across the text; the best (lowest) edit distance over all of
            // them is the token's typo distance. Bounded windows keep this O(text * len) rather than full-text DP.
            for (int wlen = len - 1; wlen <= len + 1; wlen++)
            {
                if (wlen <= 0) continue;
                int limit = text.Length - wlen;
                for (int s = 0; s <= limit; s++)
                {
                    int d = BoundedLevenshtein(token, text, s, wlen, best <= maxDist ? best : maxDist);
                    if (d < best)
                    {
                        best = d;
                        if (best == 0) break; // can't do better than an exact window (shouldn't happen — that'd be a substring)
                    }
                }
                if (best == 0) break;
            }

            if (best > maxDist) return 0f;
            float score = (1f - (float)best / len) * TypoPenalty;
            return score < 0f ? 0f : score;
        }

        /// <summary>
        /// Levenshtein distance between <paramref name="token"/> and the window of <paramref name="text"/> starting at
        /// <paramref name="winStart"/> of length <paramref name="winLen"/>, with early-exit once the running minimum of
        /// a row exceeds <paramref name="cutoff"/> (then returns cutoff+1, a value the caller treats as "too far").
        /// Two rolling rows -> O(len * winLen) time, O(winLen) space, no per-char heap allocation beyond the two rows.
        /// </summary>
        private static int BoundedLevenshtein(string token, string text, int winStart, int winLen, int cutoff)
        {
            int n = token.Length;
            // prev[j] = distance of token[0..0] (empty) prefix... we use the classic two-row DP.
            int[] prev = new int[winLen + 1];
            int[] cur = new int[winLen + 1];
            for (int j = 0; j <= winLen; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                char tc = token[i - 1];
                int rowMin = cur[0];
                for (int j = 1; j <= winLen; j++)
                {
                    char wc = text[winStart + j - 1];
                    int cost = tc == wc ? 0 : 1;
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    int v = del < ins ? del : ins;
                    if (sub < v) v = sub;
                    cur[j] = v;
                    if (v < rowMin) rowMin = v;
                }
                // Early exit: if the whole row is already past the cutoff, the final distance can only be >= rowMin.
                if (rowMin > cutoff) return cutoff + 1;
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[winLen];
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
    }
}
