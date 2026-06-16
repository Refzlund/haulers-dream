namespace HaulersDream.Core
{
    /// <summary>
    /// The state of a mechanoid that makes it unable to ever haul — so HD-tagged cargo it holds would be
    /// stranded forever. The runtime maps the live Verse state onto one of these (see
    /// <c>HaulersDreamGameComponent</c>):
    /// <list type="bullet">
    /// <item><see cref="None"/> — not a mech, or a mech that is awake and capable (current job is not charging /
    /// self-shutdown and its dormancy comp, if any, reports Awake).</item>
    /// <item><see cref="Charging"/> — current job is <c>JobDefOf.MechCharge</c> (parked on a recharger).</item>
    /// <item><see cref="SelfShutdown"/> — current job is <c>JobDefOf.SelfShutdown</c>, or the energy need reports
    /// self-shutdown (<c>Pawn.IsSelfShutdown()</c>): out of power, will not act.</item>
    /// <item><see cref="Dormant"/> — a <c>CompCanBeDormant</c> reports <c>Awake == false</c> (hibernating).</item>
    /// </list>
    /// </summary>
    public enum MechState
    {
        None,
        Charging,
        SelfShutdown,
        Dormant
    }

    /// <summary>
    /// Pure decision for the anti-softlock auto-drop (BLFT parity, gap #3 / plan A2): a pawn holding
    /// HD-tagged inventory cargo that can NO LONGER haul will never unload it on its own — nothing will ever
    /// service that cargo, so it is trapped forever. This decides when to force-drop ONLY the tagged items so
    /// other haulers can reclaim them off the ground. No Verse types — the runtime gathers the primitives and
    /// performs the drop.
    /// </summary>
    public static class SoftlockDropPolicy
    {
        /// <summary>
        /// Should this pawn's HD-tagged cargo be force-dropped?
        ///
        /// Decision order (first match wins):
        /// <list type="number">
        /// <item><paramref name="taggedCount"/> &lt;= 0 → <c>false</c> (nothing tagged to free).</item>
        /// <item><paramref name="runningHdJob"/> → <c>false</c> (the pawn is actively running an HD load /
        /// unload / cleanup job; it will resolve the cargo itself — never yank items out from under a live
        /// job, even if the pawn is otherwise "incapable").</item>
        /// <item>incapable of hauling (any of: <paramref name="haulingDisabled"/>,
        /// <paramref name="haulingPriorityZero"/>, or a mech in a stuck <paramref name="mechState"/>) →
        /// <c>true</c>.</item>
        /// <item>otherwise → <c>false</c> (a capable hauler — it will unload normally).</item>
        /// </list>
        /// </summary>
        /// <param name="haulingDisabled">The Hauling work tag is disabled (incapable type / forced off).</param>
        /// <param name="haulingPriorityZero">Hauling work type priority is 0 (player set it to never).</param>
        /// <param name="isMech">The pawn is a mechanoid (only then is <paramref name="mechState"/> consulted).</param>
        /// <param name="mechState">The mech's stuck-state classification (ignored when <paramref name="isMech"/> is false).</param>
        /// <param name="taggedCount">How many HD-tagged stacks the pawn currently holds.</param>
        /// <param name="runningHdJob">The pawn's current job is an HD load / unload / cleanup job.</param>
        public static bool ShouldDrop(
            bool haulingDisabled,
            bool haulingPriorityZero,
            bool isMech,
            MechState mechState,
            int taggedCount,
            bool runningHdJob)
        {
            // Nothing tagged → nothing to free.
            if (taggedCount <= 0)
                return false;
            // Actively running an HD job → the job owns this cargo; let it finish (or fail and retag).
            if (runningHdJob)
                return false;
            // Incapable of ever hauling → the cargo is stranded; drop it for others to reclaim.
            return IsHaulIncapable(haulingDisabled, haulingPriorityZero, isMech, mechState);
        }

        /// <summary>
        /// True if the pawn can no longer take a hauling job: the Hauling work tag is disabled, its Hauling
        /// priority is 0, or it is a mech in a stuck state (<see cref="MechState.None"/> for a non-mech or an
        /// awake-and-capable mech is NOT incapable). Split out so the "incapable" classification is unit-pinned
        /// independently of the tagged-count / running-job guards.
        /// </summary>
        public static bool IsHaulIncapable(
            bool haulingDisabled,
            bool haulingPriorityZero,
            bool isMech,
            MechState mechState)
        {
            if (haulingDisabled)
                return true;
            if (haulingPriorityZero)
                return true;
            if (isMech && mechState != MechState.None)
                return true;
            return false;
        }
    }
}
