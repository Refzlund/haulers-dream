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
            int graceTicks)
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
    }
}
