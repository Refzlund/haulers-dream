namespace HaulersDream.Core
{
    /*
        ──────────────────────────────────────────────
                    Haul simulation script
        ──────────────────────────────────────────────
        The vocabulary a test writes a multi-hauler scenario in. One command = one discrete thing that
        happens to one hauler, in a fixed order.

        → KEY: a hauler PICKS UP in one job and DEPOSITS in a later one, so its commitment is live across a
          gap during which other haulers plan. Decide and Deposit are therefore separate commands, and a
          scenario that cannot put another pawn's Decide between them cannot express the bug at all.
        → GOTCHA: ReleaseCommitment and DropCargo are not opposites and neither is a tidy "cancel". One
          forgets the ACCOUNTING while the goods keep coming; the other loses the GOODS while the
          accounting keeps blocking. They are the two ways a claim ledger fails, and a design has to
          survive both.
    */

    /// <summary>
    /// One discrete event in a haul scenario. Ordering is the script's; nothing here happens on a timer.
    /// </summary>
    public enum HaulSimAction
    {
        /// <summary>A hauler reads the destination and commits. The rule's answer becomes both a live
        /// commitment (visible to whoever plans next) and cargo in the pawn's hands — pickup is treated as
        /// instantaneous, because the interval that matters for this bug is the one before the DEPOSIT.
        /// A decision of zero or less means no job was created, so the pawn never sets off.</summary>
        Decide,

        /// <summary>A hauler arrives and puts down as much of its cargo as still fits. Whatever does not
        /// fit rides back — that remainder is the reported symptom. Clears the pawn's cargo and its
        /// commitment either way.</summary>
        Deposit,

        /// <summary>The accounting forgets this hauler's commitment while its cargo is untouched and still
        /// on its way. Models a claim released too early (job ended, pawn re-tasked, a snapshot that stopped
        /// counting it) and lets the next planner see space that is not actually free.</summary>
        ReleaseCommitment,

        /// <summary>The hauler's cargo vanishes without ever landing, while its commitment stays live.
        /// Models a load that never arrives (drafted, downed, dumped elsewhere) against accounting that
        /// never hears about it — the phantom claim that can starve a destination indefinitely.</summary>
        DropCargo,

        /// <summary>Advance the clock. Only matters where in-flight visibility is snapshotted per tick, in
        /// which case this is what lets the next decision see commitments made during the tick just ended.</summary>
        Tick
    }

    /// <summary>
    /// One entry of a haul scenario: an action, who it happens to, and (for a decision) how much that
    /// hauler wants. Built through the named factories rather than the constructor so a reader of the
    /// scenario sees which fields each action actually uses.
    /// </summary>
    public readonly struct HaulSimCommand
    {
        /// <summary>What happens.</summary>
        public readonly HaulSimAction Action;

        /// <summary>Which hauler it happens to. Unused (and zero) for <see cref="HaulSimAction.Tick"/>.</summary>
        public readonly int PawnId;

        /// <summary>For <see cref="HaulSimAction.Decide"/> only: what this hauler would take against an
        /// unlimited destination. Per-command rather than per-hauler so a scenario can vary appetite over a
        /// run — a second trip for the same pawn is usually a different size.</summary>
        public readonly int Desire;

        /// <summary>Direct construction. Private to the factories below, which is what keeps an unused
        /// field from being filled with a number that reads as meaningful.</summary>
        /// <param name="action">The event.</param>
        /// <param name="pawnId">Hauler it applies to.</param>
        /// <param name="desire">Appetite, for a decision only.</param>
        private HaulSimCommand(HaulSimAction action, int pawnId, int desire)
        {
            Action = action;
            PawnId = pawnId;
            Desire = desire;
        }

        /// <summary>A hauler plans and picks up.</summary>
        /// <param name="pawnId">The planning hauler.</param>
        /// <param name="desire">What it would take against an unlimited destination.</param>
        public static HaulSimCommand Decide(int pawnId, int desire) =>
            new HaulSimCommand(HaulSimAction.Decide, pawnId, desire);

        /// <summary>A hauler arrives and unloads what fits. Harmless for a hauler holding nothing: it is
        /// recorded, counts as no trip, and shows in the trace as the pawn that stood down.</summary>
        /// <param name="pawnId">The arriving hauler.</param>
        public static HaulSimCommand Deposit(int pawnId) =>
            new HaulSimCommand(HaulSimAction.Deposit, pawnId, 0);

        /// <summary>Drop this hauler's commitment from the accounting, leaving its cargo in flight.</summary>
        /// <param name="pawnId">The hauler whose claim is forgotten.</param>
        public static HaulSimCommand ReleaseCommitment(int pawnId) =>
            new HaulSimCommand(HaulSimAction.ReleaseCommitment, pawnId, 0);

        /// <summary>Destroy this hauler's cargo without delivering it, leaving its commitment live.</summary>
        /// <param name="pawnId">The hauler whose load never arrives.</param>
        public static HaulSimCommand DropCargo(int pawnId) =>
            new HaulSimCommand(HaulSimAction.DropCargo, pawnId, 0);

        /// <summary>Advance the clock by one tick.</summary>
        public static HaulSimCommand Tick() =>
            new HaulSimCommand(HaulSimAction.Tick, 0, 0);
    }
}
