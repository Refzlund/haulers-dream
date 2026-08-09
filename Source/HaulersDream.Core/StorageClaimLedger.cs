using System;

namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
              Storage commitment claim ledger
        ──────────────────────────────────────────────
        The pure arithmetic behind "how much of this storage group is already spoken for". Sibling of
        LoadLedger, which does the same job for a transport manifest — and deliberately NOT the same type,
        because the two answer different questions: a manifest ledger tracks what a destination still WANTS,
        this one tracks what several pawns have already promised a destination it will RECEIVE.

        → KEY: a row records the DESTINATION and the INTENT; the pawn's own live state records the AMOUNT.
          Every read therefore clamps a row to the pawn's evidence (what it is really carrying, or really
          queued to fetch), so a row whose cargo evaporated stops blocking the destination WITHOUT anyone
          having to remember to release it. That is what makes the phantom-claim failure inexpressible —
          the one that starves a destination forever (see AClaimNeverReleased_ in the harness tests).
        → KEY: one destination per (pawn, def). Re-targeting a def REPLACES the pawn's earlier row for it
          instead of adding a second, so a pawn's claims for one def can never sum to more than the units it
          actually holds — the property that keeps the group sums honest across re-plans.
        → GOTCHA: rows live in a FLAT ARRAY replaced wholesale on write, never mutated in place. Writes
          happen only at job start on the main thread; a reader takes the array reference into a local once
          and is then immune to a concurrent replacement. A Dictionary here would tear under a threading
          mod's work scan.
        → NOTE: pawn / group / def are plain `object`, reference-compared, exactly as StorageGroupBudget
          keys its defs. Generics would buy nothing — every caller passes a game type this assembly cannot
          name anyway — and would spread three type parameters across every signature.
    */

    /// <summary>
    /// What one pawn has promised to put into one storage group, for one item def.
    ///
    /// <para>Immutable: a claim changes by replacing the row, never by editing it, so a reader holding the
    /// array can never observe a half-written row.</para>
    /// </summary>
    public readonly struct StorageClaimRow
    {
        /// <summary>The committing pawn. Opaque here (the runtime passes the <c>Pawn</c>); only ever
        /// compared by reference.</summary>
        public readonly object Pawn;

        /// <summary>The destination's budget identity — a linked storage group when there is one, else the
        /// slot group. Reference-compared, so every producer must derive it the same way or two rows for
        /// one shelf would never match.</summary>
        public readonly object Group;

        /// <summary>The item def being delivered. Reference-compared.</summary>
        public readonly object Def;

        /// <summary>Units of <see cref="Def"/> this pawn intends to put into <see cref="Group"/>. The
        /// RECORDED figure; what a reader charges the group is this clamped by the pawn's live evidence
        /// (see <see cref="StorageClaimLedger.EffectiveClaim"/>).</summary>
        public readonly int Units;

        /// <summary>Record one commitment.</summary>
        /// <param name="pawn">The committing pawn.</param>
        /// <param name="group">The destination's budget identity.</param>
        /// <param name="def">The item def being delivered.</param>
        /// <param name="units">Units intended. A caller should not pass a negative; every reader clamps
        /// anyway rather than trusting it.</param>
        public StorageClaimRow(object pawn, object group, object def, int units)
        {
            Pawn = pawn;
            Group = group;
            Def = def;
            Units = units;
        }
    }

    /// <summary>
    /// How many units of <paramref name="def"/> <paramref name="pawn"/> can actually be SEEN to be bringing
    /// somewhere right now — its cargo, its tagged inventory surplus, and what it is queued to pick up.
    ///
    /// <para>Supplied by the caller because it is a question about live game state, which this assembly
    /// cannot ask. Returning 0 for a pawn that has nothing is what retires a stale row; returning more than
    /// a row recorded never inflates the claim, since the clamp is a minimum.</para>
    /// </summary>
    /// <param name="pawn">The pawn whose cargo is being weighed.</param>
    /// <param name="def">The def in question.</param>
    /// <returns>Units of that def in that pawn's hands, inventory or pickup queue. Negative reads as zero.</returns>
    public delegate int StorageClaimEvidence(object pawn, object def);

    /// <summary>
    /// The claim ledger's pure arithmetic: add, drop, reconcile, and the sums a decision is taken on. Every
    /// operation is a static function over a rows array, so the runtime owns the one live array and this
    /// owns none of it.
    /// </summary>
    public static class StorageClaimLedger
    {
        /// <summary>The empty ledger. Shared: an empty array is immutable in every way that matters, and a
        /// fresh one per clear would be pure garbage on the hot path.</summary>
        public static readonly StorageClaimRow[] Empty = new StorageClaimRow[0];

        /// <summary>
        /// Book <paramref name="units"/> of <paramref name="def"/> for <paramref name="pawn"/> into
        /// <paramref name="group"/>, returning the NEW rows array.
        ///
        /// <para>Replaces every earlier row this pawn held for this def, whatever group it named — a pawn
        /// delivers a def to ONE place at a time, and leaving the old row behind would count one load
        /// against two destinations. A non-positive <paramref name="units"/> (or a null group) therefore
        /// reads as "this pawn is no longer bringing this def anywhere" and simply drops the rows.</para>
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty. Never mutated.</param>
        /// <param name="pawn">The committing pawn; null is a no-op (an unattributable claim could never be
        /// released and would hold the destination forever).</param>
        /// <param name="group">The destination's budget identity; null drops the pawn's rows for the def.</param>
        /// <param name="def">The def being delivered; null is a no-op — no reader could ever match such a
        /// row, so it would leak.</param>
        /// <param name="units">Units intended.</param>
        /// <returns>A new array, or the same reference when nothing changed.</returns>
        public static StorageClaimRow[] Add(StorageClaimRow[] rows, object pawn, object group, object def, int units)
        {
            var current = rows ?? Empty;
            if (pawn == null || def == null)
                return current;

            bool keeping = group != null && units > 0;

            int survivors = 0;
            for (int i = 0; i < current.Length; i++)
                if (!IsRowOf(current[i], pawn, def))
                    survivors++;

            // Nothing to drop and nothing to add: hand the same array back, so a no-op write allocates
            // nothing and readers holding the old reference stay correct by construction.
            if (survivors == current.Length && !keeping)
                return current;

            var next = new StorageClaimRow[survivors + (keeping ? 1 : 0)];
            int w = 0;
            for (int i = 0; i < current.Length; i++)
                if (!IsRowOf(current[i], pawn, def))
                    next[w++] = current[i];
            if (keeping)
                next[w] = new StorageClaimRow(pawn, group, def, units);
            return next;
        }

        /// <summary>
        /// Drop every row belonging to <paramref name="pawn"/> — it died, despawned, or its cargo is
        /// provably gone. Idempotent.
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty. Never mutated.</param>
        /// <param name="pawn">The pawn to forget.</param>
        /// <returns>A new array, or the same reference when the pawn held nothing.</returns>
        public static StorageClaimRow[] DropPawn(StorageClaimRow[] rows, object pawn)
        {
            var current = rows ?? Empty;
            int survivors = 0;
            for (int i = 0; i < current.Length; i++)
                if (!ReferenceEquals(current[i].Pawn, pawn))
                    survivors++;
            if (survivors == current.Length)
                return current;

            var next = new StorageClaimRow[survivors];
            int w = 0;
            for (int i = 0; i < current.Length; i++)
                if (!ReferenceEquals(current[i].Pawn, pawn))
                    next[w++] = current[i];
            return next;
        }

        /// <summary>
        /// Drop every row the world no longer supports: a pawn with no evidence left, a row whose group has
        /// gone, or a row recorded as nothing. The periodic self-heal — correctness does not depend on it
        /// (every read already clamps to evidence); it stops a dead row occupying the array and keeps
        /// <see cref="AnyRows"/> honest as the hot-path gate.
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty. Never mutated.</param>
        /// <param name="evidence">What each pawn can be seen to be carrying. Null keeps every row — a
        /// reconcile with no way to measure must not delete the ledger.</param>
        /// <param name="groupStillExists">Whether a group is still a live destination; null treats every
        /// group as live.</param>
        /// <returns>A new array, or the same reference when every row survived.</returns>
        public static StorageClaimRow[] Reconcile(
            StorageClaimRow[] rows, StorageClaimEvidence evidence, Predicate<object> groupStillExists)
        {
            var current = rows ?? Empty;
            if (current.Length == 0 || evidence == null)
                return current;

            int survivors = 0;
            for (int i = 0; i < current.Length; i++)
                if (Survives(current[i], evidence, groupStillExists))
                    survivors++;
            if (survivors == current.Length)
                return current;

            var next = new StorageClaimRow[survivors];
            int w = 0;
            for (int i = 0; i < current.Length; i++)
                if (Survives(current[i], evidence, groupStillExists))
                    next[w++] = current[i];
            return next;
        }

        /// <summary>
        /// What a row actually charges its group: the recorded units, capped by what the pawn can be seen
        /// to be carrying, floored at zero.
        ///
        /// <para>This clamp IS the release mechanism. A pawn that deposited its load, was drafted, or had
        /// its stack stolen stops charging the destination on the very next read — no deposit hook, and no
        /// way for a row to outlive its cargo.</para>
        /// </summary>
        /// <param name="row">The row to weigh.</param>
        /// <param name="evidence">The evidence source; null charges the recorded units, because a reader
        /// that cannot measure must not silently zero every claim.</param>
        /// <returns>Units this row currently withholds from other pawns.</returns>
        public static int EffectiveClaim(StorageClaimRow row, StorageClaimEvidence evidence)
        {
            int recorded = row.Units;
            if (recorded <= 0)
                return 0;
            if (evidence == null)
                return recorded;
            int seen = evidence(row.Pawn, row.Def);
            if (seen <= 0)
                return 0;
            return seen < recorded ? seen : recorded;
        }

        /// <summary>
        /// Units of <paramref name="def"/> that pawns OTHER than <paramref name="asker"/> have committed to
        /// <paramref name="group"/> — what any pawn must take off the top before pricing its own load.
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty.</param>
        /// <param name="group">The destination's budget identity, reference-compared.</param>
        /// <param name="def">The def, reference-compared.</param>
        /// <param name="asker">The asking pawn, excluded from its own answer.</param>
        /// <param name="evidence">Live-cargo source for the clamp.</param>
        /// <returns>A saturating sum; never negative, never overflowed.</returns>
        public static int ClaimedByOthers(
            StorageClaimRow[] rows, object group, object def, object asker, StorageClaimEvidence evidence)
        {
            var current = rows ?? Empty;
            int units = 0;
            for (int i = 0; i < current.Length; i++)
            {
                var row = current[i];
                if (ReferenceEquals(row.Pawn, asker) || !Matches(row, group, def))
                    continue;
                units = DestinationEnroutePolicy.SaturatingAdd(units, EffectiveClaim(row, evidence));
            }
            return units < 0 ? 0 : units;
        }

        /// <summary>
        /// Units of <paramref name="def"/> committed to <paramref name="group"/> by EVERY pawn. The figure a
        /// cross-def question needs: another def's claim spends CELLS this def would otherwise open, and
        /// whose claim it is makes no difference to the cell.
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty.</param>
        /// <param name="group">The destination's budget identity, reference-compared.</param>
        /// <param name="def">The def, reference-compared.</param>
        /// <param name="evidence">Live-cargo source for the clamp.</param>
        /// <returns>A saturating sum; never negative, never overflowed.</returns>
        public static int ClaimedTotal(
            StorageClaimRow[] rows, object group, object def, StorageClaimEvidence evidence)
        {
            var current = rows ?? Empty;
            int units = 0;
            for (int i = 0; i < current.Length; i++)
            {
                if (!Matches(current[i], group, def))
                    continue;
                units = DestinationEnroutePolicy.SaturatingAdd(units, EffectiveClaim(current[i], evidence));
            }
            return units < 0 ? 0 : units;
        }

        /// <summary>
        /// One pawn's OWN committed units of <paramref name="def"/> into <paramref name="group"/>. What a
        /// pawn planning a fresh pickup must subtract as well (its in-flight load lands in the very space it
        /// is pricing), and exactly what a pawn already holding cargo must NOT (that space is its own).
        /// </summary>
        /// <param name="rows">The current rows; null reads as empty.</param>
        /// <param name="group">The destination's budget identity, reference-compared.</param>
        /// <param name="def">The def, reference-compared.</param>
        /// <param name="pawn">The pawn whose own claim is wanted.</param>
        /// <param name="evidence">Live-cargo source for the clamp.</param>
        /// <returns>That pawn's effective claim on this group and def.</returns>
        public static int ClaimedByPawn(
            StorageClaimRow[] rows, object group, object def, object pawn, StorageClaimEvidence evidence)
        {
            var current = rows ?? Empty;
            int units = 0;
            for (int i = 0; i < current.Length; i++)
            {
                var row = current[i];
                if (!ReferenceEquals(row.Pawn, pawn) || !Matches(row, group, def))
                    continue;
                units = DestinationEnroutePolicy.SaturatingAdd(units, EffectiveClaim(row, evidence));
            }
            return units < 0 ? 0 : units;
        }

        /// <summary>Whether any claim is recorded at all — the O(1) gate the storage hot path takes before
        /// doing any work. Deliberately does NOT consult evidence: this has to stay a length read, and a
        /// stale row costs one wasted evidence check on the rare path, never a wrong answer.</summary>
        /// <param name="rows">The current rows; null reads as empty.</param>
        /// <returns>True when at least one row exists.</returns>
        public static bool AnyRows(StorageClaimRow[] rows) => rows != null && rows.Length > 0;

        /// <summary>Whether a row belongs to this pawn and this def, whatever group it names.</summary>
        /// <param name="row">The row under test.</param>
        /// <param name="pawn">The pawn, reference-compared.</param>
        /// <param name="def">The def, reference-compared.</param>
        /// <returns>True when the row is this pawn's row for this def.</returns>
        private static bool IsRowOf(StorageClaimRow row, object pawn, object def)
            => ReferenceEquals(row.Pawn, pawn) && ReferenceEquals(row.Def, def);

        /// <summary>Whether a row names this destination and this def.</summary>
        /// <param name="row">The row under test.</param>
        /// <param name="group">The destination's budget identity, reference-compared.</param>
        /// <param name="def">The def, reference-compared.</param>
        /// <returns>True on a match.</returns>
        private static bool Matches(StorageClaimRow row, object group, object def)
            => ReferenceEquals(row.Group, group) && ReferenceEquals(row.Def, def);

        /// <summary>Whether a row still describes something real: a live group, and a pawn that can still
        /// be seen carrying or fetching the def.</summary>
        /// <param name="row">The row under test.</param>
        /// <param name="evidence">Live-cargo source; the caller has already established it is non-null.</param>
        /// <param name="groupStillExists">Group liveness test; null treats every group as live.</param>
        /// <returns>True to keep the row.</returns>
        private static bool Survives(
            StorageClaimRow row, StorageClaimEvidence evidence, Predicate<object> groupStillExists)
        {
            if (row.Units <= 0 || row.Group == null || row.Pawn == null || row.Def == null)
                return false;
            if (groupStillExists != null && !groupStillExists(row.Group))
                return false;
            return evidence(row.Pawn, row.Def) > 0;
        }
    }
}
