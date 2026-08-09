using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /*
        ──────────────────────────────────────────────
             Storage commitment claims (the ledger)
        ──────────────────────────────────────────────
        Where the one authoritative answer to "who has already promised this shelf a load" is kept, and the
        periodic reconcile that keeps it honest. The arithmetic is StorageClaimLedger's; the Verse glue that
        reads and writes it is StorageCommitments'. This file owns only the storage.

        → KEY: NOT A PER-TICK SNAPSHOT, and that is the entire fix. The previous design memoised the
          colony's in-flight loads at the first query of each tick, so two haulers planning in the SAME tick
          each read a destination nobody had committed to yet — issue #248, and the reason the #114 rule was
          correct and its answer still wrong. A commitment written here is visible to the very next reader,
          in the same tick. scripts/check-storage-commit-seam.ts fails the build if this field ever becomes
          a TickKeyedMemo or [ThreadStatic] again.
        → KEY: NOT SCRIBED, on purpose. A storage commitment is fully reconstructible from pawn state (what
          a pawn carries, what its job queue names), so a saved claim could only ever be a phantom. HD's
          transport LoadLedger IS scribed because a manifest is not reconstructible — and it still needed
          RecomputeClaimed written to repair a real permanent-over-reservation leak. Deriving instead makes
          that whole leak class inexpressible here.
        → GOTCHA: STATIC, unlike the scribed loadTasks on the same component. IsGoodStoreCell is the hottest
          method in the haul system and its 99% path is "is anything in flight at all?"; routing that
          through Current.Game.GetComponent (a component-list walk) every call would cost more than the
          feature. A static field is a single load. It is process-wide, which is correct because only one
          game is ever loaded, and FinalizeInit clears it so a quickload cannot inherit the previous
          session's Pawn/SlotGroup references.
    */

    public partial class HaulersDreamGameComponent
    {
        /// <summary>
        /// How often the claim ledger reconciles itself against live pawn state, in ticks. Two seconds at
        /// normal speed — short enough that an unforeseen leak clears before a player notices a stalled
        /// stockpile, long enough that the per-pawn evidence walk is noise. Evaluated on a
        /// <c>ticksGame % this == 0</c> boundary so every multiplayer client reconciles on the same tick.
        /// </summary>
        internal const int StorageClaimJanitorTicks = 120;

        /// <summary>
        /// Every live storage commitment, as a flat array replaced wholesale on write. Read directly (into
        /// a local) by <see cref="StorageCommitments"/>; written only through it, only on the main thread,
        /// only at job start.
        /// </summary>
        internal static StorageClaimRow[] storageClaims = StorageClaimLedger.Empty;

        /// <summary>Bumped on every write to <see cref="storageClaims"/>. Derived per-tick memos elsewhere
        /// key on it so a commitment made mid-tick invalidates any evidence figure taken before it — the
        /// one place a cache could otherwise reintroduce the same-tick blindness this ledger exists to
        /// remove.</summary>
        internal static int storageClaimGeneration;

        /// <summary>
        /// Replace the ledger. The single write path, so the generation counter can never be forgotten.
        /// </summary>
        /// <param name="rows">The new rows; null reads as empty.</param>
        internal static void SetStorageClaims(StorageClaimRow[] rows)
        {
            storageClaims = rows ?? StorageClaimLedger.Empty;
            storageClaimGeneration++;
        }

        /// <summary>Drop every claim — game load hygiene. The array holds live <c>Pawn</c> and
        /// <c>SlotGroup</c> references, so a quickload must not inherit the previous session's.</summary>
        internal static void ClearStorageClaims() => SetStorageClaims(StorageClaimLedger.Empty);
    }
}
