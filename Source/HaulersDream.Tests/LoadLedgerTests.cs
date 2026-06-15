using System.Collections.Generic;
using System.Linq;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    /// <summary>
    /// The claim-ledger arithmetic (<see cref="LoadLedger{TDef,TPawn}"/>) — the multi-pawn split-load spine.
    /// Generic over <c>string</c> def ids and <c>int</c> pawn ids so it runs headlessly. Every test that mutates
    /// state re-asserts the pinned invariant <c>totalClaimed[def] == Σ_pawn pawnClaims[pawn][def]</c>.
    /// </summary>
    [TestFixture]
    public class LoadLedgerTests
    {
        private static Dictionary<string, int> Need(params (string def, int n)[] items)
        {
            var d = new Dictionary<string, int>();
            foreach (var (def, n) in items) d[def] = n;
            return d;
        }

        // Assert totalClaimed == Σ pawnClaims for every def present in either side.
        private static void AssertInvariant(Dictionary<string, int> totalClaimed,
            Dictionary<int, Dictionary<string, int>> pawnClaims)
        {
            var recomputed = LoadLedger<string, int>.RecomputeClaimed(pawnClaims);
            // Both directions: every claimed key matches the sum, and no extra positive key lingers.
            foreach (var kv in totalClaimed)
                Assert.That(recomputed.TryGetValue(kv.Key, out int r) ? r : 0, Is.EqualTo(kv.Value),
                    $"totalClaimed[{kv.Key}] != Σ pawnClaims");
            foreach (var kv in recomputed)
                Assert.That(totalClaimed.TryGetValue(kv.Key, out int t) ? t : 0, Is.EqualTo(kv.Value),
                    $"Σ pawnClaims[{kv.Key}] not reflected in totalClaimed");
        }

        // --- AvailableToClaim ---

        [Test]
        public void Available_ExcludesOwnClaims()
        {
            // needed steel=100, A already claims 60. A re-asking sees its own 60 as still available → 100, not 40.
            var needed = Need(("steel", 100));
            var claimed = Need(("steel", 60));
            var pawnClaims = new Dictionary<int, Dictionary<string, int>> { [1] = Need(("steel", 60)) };
            var availA = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, 1);
            Assert.That(availA["steel"], Is.EqualTo(100));
            // A THIRD pawn (id 2) with no claim sees needed − claimedByOthers(60) = 40.
            var availC = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, 2);
            Assert.That(availC["steel"], Is.EqualTo(40));
        }

        [Test]
        public void Available_NeverNegative()
        {
            // Over-claimed (claimed 120 > needed 100) → others' claim exceeds need → clamp to 0 (dropped key).
            var needed = Need(("steel", 100));
            var claimed = Need(("steel", 120));
            var pawnClaims = new Dictionary<int, Dictionary<string, int>> { [1] = Need(("steel", 120)) };
            var avail = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, 2);
            Assert.That(avail.ContainsKey("steel"), Is.False);
        }

        [Test]
        public void Available_DropsZeroKeys()
        {
            // Exactly fully claimed → 0 available → key dropped, not present as 0.
            var needed = Need(("steel", 50), ("wood", 30));
            var claimed = Need(("steel", 50));
            var pawnClaims = new Dictionary<int, Dictionary<string, int>> { [1] = Need(("steel", 50)) };
            var avail = LoadLedger<string, int>.AvailableToClaim(needed, claimed, pawnClaims, 2);
            Assert.That(avail.ContainsKey("steel"), Is.False);
            Assert.That(avail["wood"], Is.EqualTo(30));
        }

        // --- ApplyClaim ---

        [Test]
        public void ApplyClaim_DeltaUpdatesTotal()
        {
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 60)));
            Assert.That(totalClaimed["steel"], Is.EqualTo(60));
            AssertInvariant(totalClaimed, pawnClaims);

            // Re-plan the same pawn DOWN to 25 — delta -35 → total = 25 (not 85).
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 25)));
            Assert.That(totalClaimed["steel"], Is.EqualTo(25));
            Assert.That(pawnClaims[1]["steel"], Is.EqualTo(25));
            AssertInvariant(totalClaimed, pawnClaims);
        }

        [Test]
        public void ApplyClaim_EmptyPlanDropsPawn()
        {
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 40)));
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, new Dictionary<string, int>());
            Assert.That(pawnClaims.ContainsKey(1), Is.False);
            Assert.That(totalClaimed.ContainsKey("steel"), Is.False);
            AssertInvariant(totalClaimed, pawnClaims);
        }

        // --- Settle ---

        [Test]
        public void Settle_ShrinksNeededClaimedAndPawn()
        {
            var needed = Need(("steel", 100));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 60)));

            // Deposit the full 60.
            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 1, "steel", 60);
            Assert.That(needed["steel"], Is.EqualTo(40));        // needed shrank
            Assert.That(totalClaimed.ContainsKey("steel"), Is.False); // claimed fully settled
            Assert.That(pawnClaims.ContainsKey(1), Is.False);    // pawn's claim emptied → pawn dropped
            AssertInvariant(totalClaimed, pawnClaims);
        }

        [Test]
        public void Settle_PartialDeposit()
        {
            var needed = Need(("steel", 100));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 60)));

            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 1, "steel", 25);
            Assert.That(needed["steel"], Is.EqualTo(75));      // 100 − 25
            Assert.That(totalClaimed["steel"], Is.EqualTo(35)); // 60 − 25
            Assert.That(pawnClaims[1]["steel"], Is.EqualTo(35));
            AssertInvariant(totalClaimed, pawnClaims);
        }

        [Test]
        public void Settle_OverDeposit_ClampsCachesShrinksNeededFully()
        {
            // Pawn claimed 20 but deposits 30 (an opportunistic top-up). claimed/pawnClaim clamp to 0; needed
            // drops by the full 30.
            var needed = Need(("steel", 100));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 20)));

            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 1, "steel", 30);
            Assert.That(needed["steel"], Is.EqualTo(70));        // 100 − 30 (the full deposit counts toward need)
            Assert.That(totalClaimed.ContainsKey("steel"), Is.False); // claimed clamped to 0
            Assert.That(pawnClaims.ContainsKey(1), Is.False);    // pawn's claim clamped to 0 → dropped
            AssertInvariant(totalClaimed, pawnClaims);
        }

        // --- Release ---

        [Test]
        public void Release_ReturnsClaimsButNotNeeded()
        {
            var needed = Need(("steel", 100));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 60)));

            LoadLedger<string, int>.Release(totalClaimed, pawnClaims, 1);
            Assert.That(needed["steel"], Is.EqualTo(100));       // needed UNTOUCHED (interrupt ≠ progress)
            Assert.That(totalClaimed.ContainsKey("steel"), Is.False); // claim returned to the pool
            Assert.That(pawnClaims.ContainsKey(1), Is.False);
            AssertInvariant(totalClaimed, pawnClaims);

            // Double-release is a harmless no-op.
            LoadLedger<string, int>.Release(totalClaimed, pawnClaims, 1);
            Assert.That(totalClaimed.Count, Is.EqualTo(0));
        }

        // --- RecomputeClaimed (the quota-leak fix) ---

        [Test]
        public void Recompute_FromPawnClaims_DropsOrphan()
        {
            // Two pawns claimed; the scribed totalClaimed (steel=90) over-counts because a downed pawn's claim
            // was orphaned. After dropping that pawn from pawnClaims, recompute derives the TRUE total (60).
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>
            {
                [1] = Need(("steel", 60)),
                // pawn 2's claim was pruned (null pawn on load) — only pawn 1 survives
            };
            var recomputed = LoadLedger<string, int>.RecomputeClaimed(pawnClaims);
            Assert.That(recomputed["steel"], Is.EqualTo(60));
            Assert.That(recomputed.Count, Is.EqualTo(1));
        }

        [Test]
        public void Invariant_TotalEqualsSumOfPawns()
        {
            // A sequence of mixed ops keeps totalClaimed == Σ pawnClaims throughout.
            var needed = Need(("steel", 200), ("wood", 50));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();

            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 80), ("wood", 20)));
            AssertInvariant(totalClaimed, pawnClaims);
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 2, Need(("steel", 60)));
            AssertInvariant(totalClaimed, pawnClaims);
            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 1, "steel", 30);
            AssertInvariant(totalClaimed, pawnClaims);
            LoadLedger<string, int>.Release(totalClaimed, pawnClaims, 2);
            AssertInvariant(totalClaimed, pawnClaims);
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("wood", 20))); // drop A's steel
            AssertInvariant(totalClaimed, pawnClaims);
        }

        // --- the flagship: split with no double-haul ---

        [Test]
        public void MultiPawn_SplitNoDoubleHaul()
        {
            // needed steel=100. A claims 60 → B's available = 40 → B claims 40 → a third pawn sees 0 available;
            // A re-planning still sees its OWN 60 as available to itself.
            var needed = Need(("steel", 100));
            var totalClaimed = new Dictionary<string, int>();
            var pawnClaims = new Dictionary<int, Dictionary<string, int>>();

            // A claims 60.
            var availA = LoadLedger<string, int>.AvailableToClaim(needed, totalClaimed, pawnClaims, 1);
            Assert.That(availA["steel"], Is.EqualTo(100));
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 1, Need(("steel", 60)));
            AssertInvariant(totalClaimed, pawnClaims);

            // B sees only 40 available.
            var availB = LoadLedger<string, int>.AvailableToClaim(needed, totalClaimed, pawnClaims, 2);
            Assert.That(availB["steel"], Is.EqualTo(40));
            LoadLedger<string, int>.ApplyClaim(totalClaimed, pawnClaims, 2, Need(("steel", 40)));
            AssertInvariant(totalClaimed, pawnClaims);

            // A THIRD pawn sees nothing available (no double-haul).
            var availC = LoadLedger<string, int>.AvailableToClaim(needed, totalClaimed, pawnClaims, 3);
            Assert.That(availC.Values.Any(v => v > 0), Is.False);
            Assert.That(LoadLedger<string, int>.HasWork(needed, totalClaimed, pawnClaims, 3), Is.False);

            // A re-planning still sees its own 60 (idempotent re-plan).
            var replanA = LoadLedger<string, int>.AvailableToClaim(needed, totalClaimed, pawnClaims, 1);
            Assert.That(replanA["steel"], Is.EqualTo(60));

            // Both deposit fully → needed reaches exactly 0 (never over/under).
            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 1, "steel", 60);
            LoadLedger<string, int>.Settle(needed, totalClaimed, pawnClaims, 2, "steel", 40);
            Assert.That(needed.ContainsKey("steel"), Is.False);   // exactly 100 deposited
            Assert.That(LoadLedger<string, int>.AnyClaimed(totalClaimed), Is.False);
            Assert.That(pawnClaims.Count, Is.EqualTo(0));
            AssertInvariant(totalClaimed, pawnClaims);
        }
    }
}
