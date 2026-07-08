using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Cross-pawn coordination for the self-pickup pending queue (<see cref="CompHauledToInventory.pendingSelfPickups"/>):
    /// which pawn currently has a given loose stack queued for its own scoop-up. <see cref="RouteSelection.ClaimedByOtherPawns"/>
    /// already keeps two pawns from targeting the SAME item once one of them is actively walking to it (a real job
    /// target), but a stack sitting in another pawn's PENDING queue is invisible to that check: it is only this
    /// mod's own bookkeeping, not a job target, so several pawns harvesting the same dense field could each queue
    /// the SAME nearby stack for themselves, and whichever pawn's own queue happens to reach it first wins,
    /// regardless of which pawn is actually closer (the reported "five rows of pawns, five rows of stacks: the top
    /// pawn walks all the way down to the bottom row" behavior).
    ///
    /// This makes that ownership distance-aware: claiming a stack another pawn already has pending TRANSFERS it
    /// when the new claimant is closer, so whichever pawn ends up nearest at claim time keeps it. It self-corrects
    /// over the course of a work session: a farther pawn's early over-claim (its own sweep radius reached a stack
    /// nobody else had noticed yet) gets reclaimed the moment the truly closer pawn's own sweep considers the same
    /// stack, without either pawn knowing about the other beyond this shared registry.
    /// </summary>
    internal static class SelfPickupClaims
    {
        // Main-thread only, like every other cross-pawn scan in this assembly (e.g. OpportunisticUnload's
        // per-JobDef memo): every caller (RecordSelfPickup, the area sweep, CorpseStripper's tainted-policy pass)
        // runs on the think loop, so a plain Dictionary needs no locking.
        private static readonly Dictionary<Thing, Pawn> owners = new Dictionary<Thing, Pawn>();

        // Self-register with the game-load hygiene sweep (see CacheRegistry) so a stale cross-session claim can
        // never survive a load. The static ctor runs once, the first time any member here is touched.
        static SelfPickupClaims() => CacheRegistry.Register(Clear);

        /// <summary>Drop every claim on game load. Hygiene only (a deserialized Thing/Pawn is a fresh instance
        /// that could never match a stale entry anyway); release the references promptly.</summary>
        internal static void Clear() => owners.Clear();

        /// <summary>
        /// Claim <paramref name="t"/> into <paramref name="pawn"/>'s pendingSelfPickups, returning whether
        /// <paramref name="pawn"/> ends up owning it. If another pawn already has it pending and is at least as
        /// close to it right now, this is a no-op that returns false: the closer (or equally close) claimant
        /// keeps it. Otherwise the claim, and the item's spot in that pawn's own pending list, transfers to
        /// <paramref name="pawn"/> and this returns true. Idempotent and self-healing: safe to call for an item
        /// already owned by <paramref name="pawn"/> (no-op, returns true), or one whose registry entry has
        /// drifted from its actual list membership (the claim is simply re-asserted).
        /// </summary>
        internal static bool Claim(Thing t, Pawn pawn)
        {
            if (t == null || pawn == null)
                return false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            if (owners.TryGetValue(t, out var owner))
            {
                if (owner == pawn)
                    return true; // already ours
                if (IsUsableOwner(owner) && !IsCloser(pawn, owner, t))
                    return false; // the existing claimant is at least as close, leave it with them
                owner.GetComp<CompHauledToInventory>()?.pendingSelfPickups.Remove(t);
            }
            owners[t] = pawn;
            if (!comp.pendingSelfPickups.Contains(t))
                comp.pendingSelfPickups.Add(t);
            return true;
        }

        /// <summary>Release <paramref name="pawn"/>'s claim on <paramref name="t"/>. Call whenever it leaves
        /// that pawn's pendingSelfPickups for any reason (picked up, discarded as invalid/unreachable/forbidden,
        /// dropped by a corpse-stripping policy), so a stale entry can never shadow a stack that is actually free.
        /// A no-op if the registry already points elsewhere (e.g. another pawn already stole the claim); this
        /// never releases someone else's ownership by mistake.</summary>
        internal static void Release(Thing t, Pawn pawn)
        {
            if (t != null && owners.TryGetValue(t, out var owner) && owner == pawn)
                owners.Remove(t);
        }

        private static bool IsUsableOwner(Pawn owner) => owner != null && owner.Spawned && !owner.Dead;

        private static bool IsCloser(Pawn a, Pawn b, Thing t)
            => (a.Position - t.Position).LengthHorizontalSquared < (b.Position - t.Position).LengthHorizontalSquared;
    }
}
