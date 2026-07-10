using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// RimIOT (Logistic Matrix, packageId <c>CN.RimIOT</c>) compatibility bridge: REFLECTION ONLY, no hard
    /// assembly reference, so Hauler's Dream runs identically with or without RimIOT installed. RimIOT is a
    /// Steam-only logistics-network storage mod with no NuGet package, so its types are resolved BY NAME and
    /// JIT-isolated: NO RimIOT type ever appears in a signature or a typed local here (the network handle is
    /// captured as a plain <c>object</c> and only null-checked), so the CLR never loads a RimIOT type unless
    /// RimIOT is active. Mirrors the other <c>*Compat</c> shims (detect once, do nothing when absent).
    ///
    /// <para>WHY this exists (issue #177, the RimIOT infinite haul loop). With a stack-size mod raising
    /// <c>stackLimit</c> to ~1000, a def can sit in a RimIOT-network cell as two partials whose sum exceeds the
    /// limit (e.g. 400 + 700 is over 1000), so any merge always leaves a partial and vanilla's
    /// <c>ShouldBeMergeable</c> stays true forever. HD's two signature features then form a success-loop that NO
    /// failure-keyed churn guard catches (every cycle reports Succeeded): the bulk-haul sweep (see
    /// <see cref="BulkHaul"/>) pockets the network partials into inventory, the forced unload re-deposits them,
    /// and HaulToStack (see <see cref="HaulToStack"/>) steers each deposit back toward a partial cell, so the def
    /// re-presents as two partials, the scan re-lists them, and the pawn re-sweeps them, roughly every 6 seconds,
    /// forever, never eating or resting.</para>
    ///
    /// <para>THE FIX (Part A): gate HD's two interfering behaviors OFF for RimIOT-network-managed cells, so the
    /// network's OWN logic owns consolidation. RimIOT does not inflate priority; it has its own anti-loop guard
    /// plus a <c>TickRebalance</c> that converges the partials to at most one once HD stops re-picking them up,
    /// and it never touches HD's inventory (its re-scatter matches PickUpAndHaul / FiannsHauling comps by exact
    /// type name, not HD's <see cref="CompHauledToInventory"/>), so HD is the sole active driver of the loop.
    /// Removing HD's bulk-sweep of network stacks and HD's deposit-steering inside a network breaks it; a pawn
    /// can still do an ordinary vanilla haul-out that RimIOT itself deemed worthwhile.</para>
    ///
    /// <para>Detection: a cell is network-managed when RimIOT returns a non-null <c>StorageNetwork</c> for the
    /// cell's <see cref="SlotGroup"/> parent. The preferred probe is RimIOT's public API
    /// <c>RimIOTApi.GetNetworkForContainer(ISlotGroupParent, Map)</c> (a purpose-built integration surface that
    /// null-guards internally and needs no MapComponent lookup, verified in the RimIOT decompile); a fallback
    /// resolves the same query on <c>MapComponent_NetworkManager</c> directly, so a rename of EITHER symbol still
    /// leaves detection working. Every method returns false fast when RimIOT is absent (byte-identical behavior).</para>
    /// </summary>
    public static class RimIOTCompat
    {
        private static bool initialized;
        private static bool active;

        // Preferred: RimIOTApi.GetNetworkForContainer(ISlotGroupParent, Map) -> StorageNetwork (static; null-guards
        // container/map internally, returns the managing network or null). Its params are both Verse types, so the
        // resolve and the invoke reference NO RimIOT type; the returned network is handled as object (null-checked).
        private static MethodInfo apiGetNetwork;
        // Fallback: MapComponent_NetworkManager.GetNetworkForContainer(ISlotGroupParent) -> StorageNetwork (instance),
        // reached via Map.GetComponent(mgrType). The exact path issue #177's investigation named; kept as a
        // belt-and-braces alternative in case a RimIOT refactor renames the public API class.
        private static Type mgrType;
        private static MethodInfo mgrGetNetwork;

        // Reused per-thread marshalling buffer for the preferred (RimIOTApi, 2-arg) Invoke, so the per-candidate
        // network probe on the bulk-haul work scan allocates no object[] per call (mirrors CECompat.fitArgs).
        // [ThreadStatic] because the work scan may be fanned to worker threads by a threading mod; a single Invoke
        // completes before the next reuse on one thread, and both slots are refilled every call, so there is no
        // stale-value or re-entrancy aliasing. The rare fallback path (only if RimIOTApi did not resolve) keeps a
        // per-call array, since it is a degraded, cold path.
        [ThreadStatic] private static object[] apiArgs;

        /// <summary>Whether RimIOT (Logistic Matrix) is loaded AND at least one network-query member resolved.
        /// Cached after the first call. False (fast) when RimIOT is absent, which makes every gate below inert.
        /// Callers MUST short-circuit on this before the per-cell/per-group checks so a non-RimIOT game pays
        /// nothing (and never resolves the absent assembly).</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        private static void Init()
        {
            initialized = true;
            active = false;
            // Presence gate first, purely from Verse's ModLister (never touches a RimIOT type), so a game without
            // RimIOT never resolves the absent assembly. ignorePostfix matches the _steam/_copy packageId variants.
            // No try/catch: RimIOT-ABSENT is exactly this null return, and every member resolve below is
            // null-guarded, so a throw in here would be a genuine reflection fault worth surfacing, not the
            // optional-dependency case. Runs once (lazily on first IsActive).
            if (ModLister.GetActiveModWithIdentifier("CN.RimIOT", ignorePostfix: true) == null)
                return;
            var apiType = AccessTools.TypeByName("RimIOT.RimIOTApi");
            if (apiType != null)
                apiGetNetwork = AccessTools.Method(apiType, "GetNetworkForContainer",
                    new[] { typeof(ISlotGroupParent), typeof(Map) });
            mgrType = AccessTools.TypeByName("RimIOT.MapComponent_NetworkManager");
            if (mgrType != null)
                mgrGetNetwork = AccessTools.Method(mgrType, "GetNetworkForContainer",
                    new[] { typeof(ISlotGroupParent) });
            active = apiGetNetwork != null || mgrGetNetwork != null;
            if (active)
                HDLog.Msg("RimIOT (Logistic Matrix) detected: HD leaves logistic-network storage to RimIOT "
                          + "(no bulk-sweep of network stacks, no deposit-steering inside a network).");
            else
                // RimIOT is present (ModLister) but NEITHER network-query member resolved (a RimIOT refactor
                // renamed both). Degrade SAFE (report inactive => HD behaves exactly as without RimIOT), but
                // surface the drift once so the compat loss is not silent. Part B (the general success-loop
                // backoff) still nets the loop even here, so this is a graceful, self-announcing degrade.
                HDLog.Warn("RimIOT (Logistic Matrix) present but neither RimIOTApi.GetNetworkForContainer(ISlotGroupParent, Map) "
                           + "nor MapComponent_NetworkManager.GetNetworkForContainer(ISlotGroupParent) resolved; HD's RimIOT "
                           + "network gating is OFF (a RimIOT rename likely). Please report it. HD continues.");
        }

        /// <summary>
        /// True when <paramref name="cell"/> belongs to a RimIOT logistic network (its <see cref="SlotGroup"/>
        /// parent maps to a non-null <c>StorageNetwork</c>). False fast when RimIOT is inactive, the map/cell is
        /// null/out-of-bounds, or there is no storage there. Used to exclude a stack from HD's bulk sweep (so HD
        /// stops re-picking up network stacks).
        /// </summary>
        /// <param name="map">The map the cell is on.</param>
        /// <param name="cell">The cell to test (typically a haulable stack's Position).</param>
        public static bool IsNetworkManagedCell(Map map, IntVec3 cell)
        {
            if (!IsActive || map == null || !cell.InBounds(map))
                return false;
            return IsNetworkManagedParent(map, map.haulDestinationManager?.SlotGroupAt(cell)?.parent);
        }

        /// <summary>
        /// True when <paramref name="group"/> is a RimIOT-network-managed <see cref="SlotGroup"/>. Network
        /// membership is a per-group property (all a group's cells share one <c>parent</c>), so this is the cheaper
        /// overload when the caller already resolved the group (HaulToStack's chosen-group and per-group scan).
        /// False fast when RimIOT is inactive or the group is null. Used to stop HD steering a deposit into the
        /// network (let RimIOT own consolidation within its network).
        /// </summary>
        /// <param name="map">The map the group is on (needed for the network lookup).</param>
        /// <param name="group">The storage group to test.</param>
        public static bool IsNetworkManagedGroup(Map map, SlotGroup group)
        {
            if (!IsActive || map == null || group == null)
                return false;
            return IsNetworkManagedParent(map, group.parent);
        }

        /// <summary>The shared reflection probe: does RimIOT report a managing network for
        /// <paramref name="parent"/>? Prefers the public <c>RimIOTApi</c> static query, falls back to the
        /// <c>MapComponent_NetworkManager</c> instance query. Returns false for a null parent (no storage building
        /// there). No try/catch: the callers already checked <see cref="IsActive"/> (so a member is resolved), so
        /// a throw here is a real RimIOT-integration fault to surface, not to silently fail open (failing open
        /// would leave the loop unbroken with no signal).</summary>
        /// <param name="map">The map for the network lookup.</param>
        /// <param name="parent">The slot-group parent (a storage building/zone), or null for no storage.</param>
        private static bool IsNetworkManagedParent(Map map, ISlotGroupParent parent)
        {
            if (parent == null)
                return false;
            if (apiGetNetwork != null)
            {
                // Reused per-thread args (refilled each call) so this per-candidate probe allocates no object[].
                var args = apiArgs ?? (apiArgs = new object[2]);
                args[0] = parent;
                args[1] = map;
                return apiGetNetwork.Invoke(null, args) != null;
            }
            if (mgrGetNetwork != null)
            {
                var mgr = map.GetComponent(mgrType);
                if (mgr == null)
                    return false;
                return mgrGetNetwork.Invoke(mgr, new object[] { parent }) != null;
            }
            return false;
        }
    }
}
