using System.Collections.Generic;

namespace HaulersDream.Core
{
    public enum UnloadDecision
    {
        Skip,         // do nothing
        ClearTracker, // tracker is out of sync with inventory — reset it
        Queue         // queue the single unload pass
    }

    /// <summary>
    /// Decides whether a pawn should run its consolidated unload pass now. Pure;
    /// <c>PawnUnloadChecker</c> gathers the primitives (counts, ticks, flags) and acts on the result.
    /// </summary>
    public static class UnloadPolicy
    {
        public static UnloadDecision Decide(
            bool eligible,
            int carriedCount,
            int inventoryCount,
            bool alreadyUnloading,
            bool forced,
            bool hasPendingWork,
            int ticksSinceLastYield,
            int graceTicks,
            bool anyUnloadable = true)
        {
            if (!eligible || carriedCount <= 0)
                return UnloadDecision.Skip;
            if (alreadyUnloading)
                return UnloadDecision.Skip;
            // Fewer real items than tracked entries => the tracker drifted (rotted / force-dropped / merged).
            // Self-heal even with pending work / within grace so the tracker never stays desynced. This check
            // must come BEFORE any inventory-empty skip: tags with an EMPTY inventory are the worst desync
            // (a stale tag would otherwise be unprunable forever — a permanent phantom "Unload now" gizmo).
            if (inventoryCount < carriedCount)
                return UnloadDecision.ClearTracker;
            if (inventoryCount <= 0)
                return UnloadDecision.Skip;
            // Nothing ABOVE keep-stock to unload right now: every tracked stack is the pawn's personal kit
            // (drug-policy / inventoryStock / packable food / CE loadout). We keep those tags (so a later
            // keep-drop resurfaces the surplus tracked, never stranded), but must NOT queue an automatic
            // unload that would instantly end Incompletable and re-fire every cycle (churn + a misleading
            // permanent "Unload now" gizmo). A FORCED unload still proceeds — the gizmo/recovery must work
            // even when it will no-op. (Mirrors the EndOfRunUnloadAllowed anyUnloadable guard.)
            if (!forced && !anyUnloadable)
                return UnloadDecision.Skip;
            // An automatic (non-forced) unload must NEVER jump in front of queued/enroute work — the unload
            // job is EnqueueFirst'd, so without this it preempts a player's shift-prioritized harvest route
            // (the pawn would break off to unload, far from full, while bushes are still queued). The pawn
            // finishes its run first; the idle backstop / full-trigger / next interval then unload. Forced
            // unloads (the gizmo, the full-when-at-ceiling trigger, debug) bypass this and unload mid-route.
            if (!forced && hasPendingWork)
                return UnloadDecision.Skip;
            // Grace period: don't unload mid-stream right after a pickup (unless forced).
            if (!forced && graceTicks > 0 && ticksSinceLastYield < graceTicks)
                return UnloadDecision.Skip;
            return UnloadDecision.Queue;
        }

        /// <summary>
        /// Whether the hit-the-carry-ceiling trigger may fire a FORCED unload: never in strict mode (the
        /// pawn keeps working and leaves the surplus on the ground for normal hauling), and never with
        /// auto-unload off (the player manages unloading via the gizmo). Pure so the gate — the exact
        /// family behind a past strict-mode livelock — is unit-pinned.
        /// </summary>
        public static bool FullTriggerAllowed(bool strictCarryWeight, bool markForUnload)
            => !strictCarryWeight && markForUnload;

        /// <summary>
        /// Whether the end-of-work-run trigger may issue an unload: the work scan just came up EMPTY for
        /// a pawn carrying tracked goods, so before it drifts off to recreation/idle it makes the
        /// consolidated unload trip. No grace gate on purpose — an empty work scan means the pickup
        /// stream is over by definition, and the freshest scoop is exactly when the trip should start.
        /// <paramref name="anyUnloadable"/> = at least one tracked stack is still in inventory and
        /// reservable; without it the job would end Incompletable instantly and re-issue every think
        /// cycle (the same loop guard the vanilla-unload substitution patch uses). The cooldown bounds
        /// re-issue when an unload starts but fails mid-trip (storage destroyed, target stolen).
        /// </summary>
        public static bool EndOfRunUnloadAllowed(
            bool markForUnload,
            bool eligible,
            bool drafted,
            int trackedCount,
            bool anyUnloadable,
            bool alreadyUnloading,
            int ticksSinceLastIssue,
            int cooldownTicks)
        {
            if (!markForUnload || !eligible || drafted)
                return false;
            if (trackedCount <= 0 || !anyUnloadable || alreadyUnloading)
                return false;
            if (ticksSinceLastIssue < cooldownTicks)
                return false;
            return true;
        }

        /// <summary>
        /// True if any queued job is the pawn's OWN real work — i.e. a queued job whose def is NOT one of
        /// the mod's housekeeping defs (self-pickup / unload). This is the "hasPendingWork" signal fed to
        /// <see cref="Decide"/>: an automatic unload must defer behind real work, but must NOT count the
        /// mod's own housekeeping jobs as work regardless of queueing order (a queued self-pickup next to
        /// the unload check would otherwise skip the unload forever and strand goods in strict mode).
        /// Pure mirror of the game-layer queue scan so that contract is unit-pinned.
        /// </summary>
        public static bool HasPendingRealWork(IEnumerable<string> queuedJobDefNames, params string[] housekeepingDefNames)
        {
            if (queuedJobDefNames == null)
                return false;
            foreach (var name in queuedJobDefNames)
            {
                if (name == null)
                    continue;
                bool housekeeping = false;
                if (housekeepingDefNames != null)
                    for (int i = 0; i < housekeepingDefNames.Length; i++)
                        if (name == housekeepingDefNames[i]) { housekeeping = true; break; }
                if (!housekeeping)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True if a queued job identity is the pawn's OWN real work — i.e. it is NOT either of the two
        /// housekeeping job identities. Compared by reference equality (<typeparamref name="T"/> = a Verse
        /// <c>JobDef</c> at runtime, a stub in tests), so the runtime can iterate <c>pawn.jobs.jobQueue</c> via
        /// its indexer and call this per queued job WITHOUT materialising a <c>List&lt;string&gt;</c> of defNames
        /// (the allocation HD-UNLWORK removes). A null job identity is treated as housekeeping (= "not real
        /// work", matching the string overload's <c>name == null -&gt; continue</c>): a queued job with no def is
        /// never the pawn's pending work.
        /// </summary>
        public static bool IsPendingRealWork<T>(T queuedJobDef, T housekeeping1, T housekeeping2)
            where T : class
        {
            if (queuedJobDef == null)
                return false;
            return !ReferenceEquals(queuedJobDef, housekeeping1)
                && !ReferenceEquals(queuedJobDef, housekeeping2);
        }
    }
}
