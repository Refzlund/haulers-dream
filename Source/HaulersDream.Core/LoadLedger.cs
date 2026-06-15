using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// The pure concurrency CLAIM-LEDGER arithmetic — the non-negotiable spine that lets several pawns SPLIT a
    /// transport-group / portal manifest without double-hauling. No game types: generic over fake def/pawn ids so
    /// the math is unit-tested headlessly (the runtime <c>LoadLedgerEntry</c> feeds it live <c>ThingDef</c>/<c>Pawn</c>).
    ///
    /// State, per task (a transporter group or a portal), is three plain dictionaries:
    ///   • <c>totalNeeded[def]</c>   — what the manifest still wants loaded (shrinks only as units physically arrive).
    ///   • <c>totalClaimed[def]</c>  — the sum of every pawn's in-flight reservation (so a new asker sees only the
    ///                                 leftover). <b>Invariant: <c>totalClaimed[def] == Σ_pawn pawnClaims[pawn][def]</c></b>.
    ///   • <c>pawnClaims[pawn][def]</c> — each pawn's own reservation slice.
    ///
    /// Every operation is a static pure function returning the new value(s); the caller writes them back. This is a
    /// faithful re-expression of the reference mod's <c>LoadTaskState</c> math with the quota-leak fixed
    /// (<see cref="RecomputeClaimed"/>: derive <c>totalClaimed</c> from the authoritative <c>pawnClaims</c> after the
    /// null-pawn prune, rather than scribing it independently and orphaning a downed hauler's units forever).
    /// </summary>
    public static class LoadLedger<TDef, TPawn>
    {
        /// <summary>
        /// What an asker may newly claim: <c>needed − (totalClaimed − askerOwnClaims)</c> per def, clamped ≥0,
        /// dropping ≤0 keys. The asker's OWN existing claim is excluded so a re-plan is idempotent/stable — a pawn
        /// re-planning sees its own previously-claimed units as still available to itself, not double-counted
        /// against the leftover (otherwise a re-plan would shrink its own allowance every cycle).
        /// </summary>
        public static Dictionary<TDef, int> AvailableToClaim(
            IReadOnlyDictionary<TDef, int> totalNeeded,
            IReadOnlyDictionary<TDef, int> totalClaimed,
            IReadOnlyDictionary<TPawn, Dictionary<TDef, int>> pawnClaims,
            TPawn asker)
        {
            var result = new Dictionary<TDef, int>();
            if (totalNeeded == null)
                return result;
            Dictionary<TDef, int> askerOwn = null;
            pawnClaims?.TryGetValue(asker, out askerOwn);
            foreach (var kv in totalNeeded)
            {
                int needed = kv.Value;
                int claimedByOthers = 0;
                if (totalClaimed != null && totalClaimed.TryGetValue(kv.Key, out int tc))
                    claimedByOthers = tc;
                if (askerOwn != null && askerOwn.TryGetValue(kv.Key, out int own))
                    claimedByOthers -= own; // exclude the asker's own claim → stable re-plan
                int avail = needed - claimedByOthers;
                if (avail > 0)
                    result[kv.Key] = avail;
            }
            return result;
        }

        /// <summary>
        /// Record a pawn's NEW plan, replacing its old claim wholesale (DELTA-based per def): for each def in the
        /// union of the old claim and the new plan, <c>totalClaimed += newPlan[def] − oldPawnClaim[def]</c> (a key
        /// dropping to ≤0 is removed from <c>totalClaimed</c>); the pawn's whole claim becomes <c>newPlan</c>, and
        /// the pawn is dropped from <c>pawnClaims</c> when <c>newPlan</c> is empty. The passed-in dictionaries are
        /// mutated in place (the runtime owns them). Mirrors the ref mod's <c>AddClaim</c>.
        /// </summary>
        public static void ApplyClaim(
            Dictionary<TDef, int> totalClaimed,
            Dictionary<TPawn, Dictionary<TDef, int>> pawnClaims,
            TPawn pawn,
            IReadOnlyDictionary<TDef, int> newPlan)
        {
            if (totalClaimed == null || pawnClaims == null)
                return;
            pawnClaims.TryGetValue(pawn, out var oldClaim);

            // Union of every def touched (old claim ∪ new plan) so a def the pawn DROPS is decremented too.
            var defs = new HashSet<TDef>();
            if (oldClaim != null)
                foreach (var k in oldClaim.Keys) defs.Add(k);
            if (newPlan != null)
                foreach (var k in newPlan.Keys) defs.Add(k);

            foreach (var def in defs)
            {
                int oldUnits = (oldClaim != null && oldClaim.TryGetValue(def, out int o)) ? o : 0;
                int newUnits = (newPlan != null && newPlan.TryGetValue(def, out int n)) ? n : 0;
                int delta = newUnits - oldUnits;
                if (delta == 0)
                    continue;
                int tc = (totalClaimed.TryGetValue(def, out int cur) ? cur : 0) + delta;
                if (tc > 0)
                    totalClaimed[def] = tc;
                else
                    totalClaimed.Remove(def);
            }

            // Replace the pawn's whole claim with a private copy of newPlan (drop ≤0 entries), or drop the pawn.
            var rebuilt = new Dictionary<TDef, int>();
            if (newPlan != null)
                foreach (var kv in newPlan)
                    if (kv.Value > 0)
                        rebuilt[kv.Key] = kv.Value;
            if (rebuilt.Count > 0)
                pawnClaims[pawn] = rebuilt;
            else
                pawnClaims.Remove(pawn);
        }

        /// <summary>
        /// A deposit of <paramref name="deposited"/> units of <paramref name="def"/> by <paramref name="pawn"/>:
        /// it shrinks <b>needed AND claimed AND the pawn's claim</b> (the units are now physically in the container).
        /// Over-deposit handling (an unplanned top-up where <c>deposited &gt; this pawn's claim</c>): the excess is
        /// first CREDITED into claimed (<c>delta = deposited − pawnClaim; if &gt;0 claimed += delta</c>) so the
        /// subsequent subtraction of <c>deposited</c> from claimed (clamped ≥0) nets correctly; <c>needed</c> drops
        /// by <c>deposited</c> (clamped ≥0); the pawn's own claim drops by <c>deposited</c> (clamped ≥0, key removed
        /// at ≤0; pawn dropped when its claim empties). All dictionaries mutated in place. Mirrors
        /// <c>SettleTransaction</c>. <b>Settle DECREMENTS needed</b> (distinguishing it from <see cref="Release"/>).
        /// </summary>
        public static void Settle(
            Dictionary<TDef, int> totalNeeded,
            Dictionary<TDef, int> totalClaimed,
            Dictionary<TPawn, Dictionary<TDef, int>> pawnClaims,
            TPawn pawn,
            TDef def,
            int deposited)
        {
            if (deposited <= 0 || totalNeeded == null || totalClaimed == null || pawnClaims == null)
                return;

            pawnClaims.TryGetValue(pawn, out var pawnClaim);
            int claimedByPawn = (pawnClaim != null && pawnClaim.TryGetValue(def, out int pc)) ? pc : 0;

            // Over-deposit (more than this pawn reserved — an opportunistic top-up): credit the excess into
            // claimed FIRST so the unconditional `claimed -= deposited` below nets to the right place.
            int overDelta = deposited - claimedByPawn;
            if (overDelta > 0)
            {
                int cur = totalClaimed.TryGetValue(def, out int c) ? c : 0;
                totalClaimed[def] = cur + overDelta;
            }

            // claimed -= deposited (clamped ≥0; key removed at ≤0).
            {
                int cur = totalClaimed.TryGetValue(def, out int c) ? c : 0;
                int next = cur - deposited;
                if (next > 0)
                    totalClaimed[def] = next;
                else
                    totalClaimed.Remove(def);
            }

            // needed -= deposited (clamped ≥0; key removed at ≤0).
            {
                int cur = totalNeeded.TryGetValue(def, out int c) ? c : 0;
                int next = cur - deposited;
                if (next > 0)
                    totalNeeded[def] = next;
                else
                    totalNeeded.Remove(def);
            }

            // pawnClaim -= deposited (clamped ≥0; key removed at ≤0; pawn dropped when empty).
            if (pawnClaim != null)
            {
                int next = claimedByPawn - deposited;
                if (next > 0)
                    pawnClaim[def] = next;
                else
                    pawnClaim.Remove(def);
                if (pawnClaim.Count == 0)
                    pawnClaims.Remove(pawn);
            }
        }

        /// <summary>
        /// Return a pawn's whole claim to the pool (an interrupt — cancel/draft/downed/despawn): subtract each of
        /// the pawn's claimed units from <c>totalClaimed</c> (clamped ≥0, key removed at ≤0) and drop the pawn from
        /// <c>pawnClaims</c>. <b>Release does NOT touch needed</b> — an interrupt is not progress; the units never
        /// reached the container, so they remain owed. Idempotent (a pawn with no claim is a no-op). Mirrors
        /// <c>ReleasePawn</c>.
        /// </summary>
        public static void Release(
            Dictionary<TDef, int> totalClaimed,
            Dictionary<TPawn, Dictionary<TDef, int>> pawnClaims,
            TPawn pawn)
        {
            if (totalClaimed == null || pawnClaims == null)
                return;
            if (!pawnClaims.TryGetValue(pawn, out var claim) || claim == null)
                return;
            foreach (var kv in claim)
            {
                int cur = totalClaimed.TryGetValue(kv.Key, out int c) ? c : 0;
                int next = cur - kv.Value;
                if (next > 0)
                    totalClaimed[kv.Key] = next;
                else
                    totalClaimed.Remove(kv.Key);
            }
            pawnClaims.Remove(pawn);
        }

        /// <summary>
        /// Rebuild <c>totalClaimed</c> as <c>Σ_pawn pawnClaims[pawn][def]</c> — the post-load self-heal that derives
        /// the claimed totals from the authoritative per-pawn claims (run AFTER the null-pawn prune). Fixes the ref
        /// mod's quota-leak: it scribed <c>totalClaimed</c> independently and, on load, only dropped the null pawn
        /// from <c>pawnClaims</c>, leaving the orphaned units counted in <c>totalClaimed</c> forever (a permanent
        /// over-reservation). Recomputing from the surviving pawns can never over-count.
        /// </summary>
        public static Dictionary<TDef, int> RecomputeClaimed(
            IReadOnlyDictionary<TPawn, Dictionary<TDef, int>> pawnClaims)
        {
            var result = new Dictionary<TDef, int>();
            if (pawnClaims == null)
                return result;
            foreach (var pc in pawnClaims)
            {
                if (pc.Value == null)
                    continue;
                foreach (var kv in pc.Value)
                {
                    if (kv.Value <= 0)
                        continue;
                    result[kv.Key] = (result.TryGetValue(kv.Key, out int cur) ? cur : 0) + kv.Value;
                }
            }
            return result;
        }

        /// <summary>True if the asker can newly claim anything (<see cref="AvailableToClaim"/> has a positive entry).</summary>
        public static bool HasWork(
            IReadOnlyDictionary<TDef, int> totalNeeded,
            IReadOnlyDictionary<TDef, int> totalClaimed,
            IReadOnlyDictionary<TPawn, Dictionary<TDef, int>> pawnClaims,
            TPawn asker)
        {
            foreach (var kv in AvailableToClaim(totalNeeded, totalClaimed, pawnClaims, asker))
                if (kv.Value > 0)
                    return true;
            return false;
        }

        /// <summary>True if ANY claim is live (some pawn still has units reserved on this task).</summary>
        public static bool AnyClaimed(IReadOnlyDictionary<TDef, int> totalClaimed)
        {
            if (totalClaimed == null)
                return false;
            foreach (var kv in totalClaimed)
                if (kv.Value > 0)
                    return true;
            return false;
        }
    }
}
