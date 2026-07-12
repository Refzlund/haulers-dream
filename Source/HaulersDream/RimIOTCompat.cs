using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
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
    /// <para>THE FIX (issue #184, the interface-terminal apron). When a RimIOT network is FULL it drops the carried
    /// stack on a PLAIN GROUND tile at the depositing pawn's feet next to a powered interface terminal (RimIOT's own
    /// <c>TryDropCarriedThing</c> / <c>TryStartCarry</c> overflow -> <c>GenPlace.TryPlaceThing(..., pawn.Position,
    /// ThingPlaceMode.Near)</c>, decompile-verified). That tile is NOT a network-managed storage cell, so the #177
    /// cell gate above misses it, and HD's bulk sweep AND its (previously un-gated) en-route grab re-pocket the drop
    /// and force-unload it back into the still-full network, forever. HD now also leaves the small deposit APRON
    /// around a spawned, powered interface to RimIOT on EVERY pickup path (see <see cref="IsNearPoweredInterface"/>
    /// and the shared <see cref="IsRimIOTHandledCell"/>). This gate needs NO RimIOT reflection (the interface is
    /// found by its Verse <see cref="ThingDef"/> and its power read via vanilla <c>CompPowerTrader</c>), so it gates
    /// on <see cref="IsPresent"/> alone and keeps working even if a RimIOT refactor renames the network query.</para>
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
        // #184: RimIOT is LOADED (the mod is in the active list), independent of whether its network-query reflection
        // resolved. The interface-apron gate keys on this (not on `active`) because it needs no RimIOT reflection, so
        // it survives a rename of the network API that would flip `active` false.
        private static bool present;

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

        // #184/#192: EVERY RimIOT interface-terminal def, resolved ROBUSTLY. The overflow-drop loop sits by the
        // "interaction terminal", whose def was RimIOT_Interface / thingClass RimIOT.Building_Interface when #184 was
        // written — but a RimIOT update can rename the def or add interface variants (a large / wireless terminal),
        // and a single hardcoded defName silently misses those, leaving the apron gate off and the loop unbroken
        // (issue #192, still-looping on 1.18.0). We instead collect ALL defs whose thingClass IS Building_Interface
        // or a SUBCLASS of it (version-proof), keeping the known defName as a fallback. NO RimIOT type is referenced:
        // the base type is held as a plain System.Type and only IsAssignableFrom is called, so JIT-isolation holds
        // (a non-RimIOT game never reaches Init past the presence gate). Empty -> the apron gate degrades to false
        // (HD behaves as without RimIOT) and Init WARNs once.
        private static List<ThingDef> interfaceDefs;

        // #184/#192: how far from a powered interface a loose stack is still inside the terminal's deposit apron.
        // The full-network overflow drop is GenPlace.TryPlaceThing(pawn.Position, ThingPlaceMode.Near): the pawn
        // stands adjacent to the interface (~1 tile), and Near then scans an EXPANDING radial pattern for a free
        // cell — around a FULL, overflowing terminal the near cells are packed with prior drops, so the placement
        // is pushed several tiles out. #184's Chebyshev-2 apron assumed the pawn's own free cell; #192 shows the
        // scatter reaches farther, so widen to 4 (a 9x9 apron). This only makes HD LEAVE loose stacks by a RimIOT
        // terminal to RimIOT / vanilla hauling (never lost), so a generous apron is the safe direction.
        private const int InterfaceApronRadius = 4;

        // #184: per-(map, tick) snapshot of every SPAWNED, POWERED interface's cell, so the per-candidate proximity
        // test on the hot bulk-haul / en-route scan pays a single dictionary hit + a short cell loop, and the
        // listerThings walk + power reads run once per tick per map. [ThreadStatic] because the work scan may be
        // fanned to worker threads by a threading mod (mirrors apiArgs): each thread keeps its own per-tick snapshot;
        // the memo self-clears on tick change and holds only IntVec3 coords (no Thing/Map refs), so nothing leaks
        // across a save/load and no CacheRegistry hookup is needed.
        [ThreadStatic] private static TickKeyedMemo<List<IntVec3>> interfaceCellsMemo;

        // Sibling-parity with PawnMassCache/SurplusCache: register the per-session memo clear with the game-load
        // hygiene sweep so a stale (tick, mapId) snapshot can never survive a load on the main (FinalizeInit) thread.
        // Belt-and-braces only (the memo already self-clears on tick change and holds just IntVec3 coords); worker
        // threads' memos are per-tick self-clearing. The static ctor runs once on first access to any member.
        static RimIOTCompat() => CacheRegistry.Register(ClearInterfaceCellsCache);

        /// <summary>Drop the per-(map, tick) powered-interface snapshot on this (main) thread, for cross-session
        /// hygiene on game load. Registered with <see cref="CacheRegistry"/>.</summary>
        private static void ClearInterfaceCellsCache() => interfaceCellsMemo.Clear();

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

        /// <summary>Whether RimIOT (Logistic Matrix) is LOADED, regardless of whether its network-query reflection
        /// resolved. Cached after the first call. The #184 interface-apron gate keys on this (not on
        /// <see cref="IsActive"/>) so it keeps working even if a RimIOT refactor renames the network API; every HD
        /// pickup path short-circuits on it before the per-cell checks, so a non-RimIOT game pays nothing.</summary>
        public static bool IsPresent
        {
            get
            {
                if (!initialized)
                    Init();
                return present;
            }
        }

        private static void Init()
        {
            initialized = true;
            active = false;
            present = false;
            // Presence gate first, purely from Verse's ModLister (never touches a RimIOT type), so a game without
            // RimIOT never resolves the absent assembly. ignorePostfix matches the _steam/_copy packageId variants.
            // No try/catch: RimIOT-ABSENT is exactly this null return, and every member resolve below is
            // null-guarded, so a throw in here would be a genuine reflection fault worth surfacing, not the
            // optional-dependency case. Runs once (lazily on first IsActive/IsPresent).
            if (ModLister.GetActiveModWithIdentifier("CN.RimIOT", ignorePostfix: true) == null)
                return;
            present = true;
            // #184/#192: resolve EVERY interface-terminal def up front (plain Verse ThingDefs, no RimIOT type held).
            // Independent of the network-query reflection below, so the interface-apron gate survives a RimIOT
            // network-API rename. Match by the Building_Interface base type (IsAssignableFrom) so a renamed def or an
            // added interface variant is still caught; fall back to the known defName if that type ever fails to
            // resolve. Init runs lazily on the first in-game IsActive/IsPresent hit, so DefDatabase is fully loaded.
            interfaceDefs = new List<ThingDef>();
            var interfaceBaseType = AccessTools.TypeByName("RimIOT.Building_Interface");
            var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                var def = allDefs[i];
                if (def?.thingClass == null)
                    continue;
                bool isInterface = interfaceBaseType != null
                    ? interfaceBaseType.IsAssignableFrom(def.thingClass)
                    : def.defName == "RimIOT_Interface";
                if (isInterface)
                    interfaceDefs.Add(def);
            }
            // Belt-and-braces: if the base type resolved but matched nothing (e.g. thingClass swapped to a wrapper),
            // still honour the known defName so a partial rename doesn't blank the gate.
            if (interfaceDefs.Count == 0)
            {
                var byName = DefDatabase<ThingDef>.GetNamedSilentFail("RimIOT_Interface");
                if (byName != null)
                    interfaceDefs.Add(byName);
            }
            if (interfaceDefs.Count == 0)
                HDLog.Warn("RimIOT present but no interface-terminal def resolved (RimIOT.Building_Interface / "
                           + "RimIOT_Interface); HD's interface-apron overflow-drop gate is OFF (a RimIOT rename "
                           + "likely). Please report it. HD continues.");
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
                // renamed both). Degrade SAFE for the #177 network-cell gate (report inactive => that gate behaves
                // exactly as without RimIOT), but surface the drift once so the compat loss is not silent. The #184
                // interface-apron gate is UNAFFECTED (it keys on IsPresent + the interface def, not on this
                // reflection), so it keeps breaking the terminal-overflow loop even here.
                HDLog.Warn("RimIOT (Logistic Matrix) present but neither RimIOTApi.GetNetworkForContainer(ISlotGroupParent, Map) "
                           + "nor MapComponent_NetworkManager.GetNetworkForContainer(ISlotGroupParent) resolved; HD's RimIOT "
                           + "network-cell gating is OFF (a RimIOT rename likely). Please report it. HD continues.");
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

        /// <summary>
        /// The single cell-based "leave this stack to RimIOT" test for every HD pickup path (bulk sweep, en-route
        /// grab, work-spot sweep). True when <paramref name="cell"/> is inside a RimIOT-network-managed storage group
        /// (issue #177, the partial-consolidation loop) OR in the deposit apron of a powered interface terminal
        /// (issue #184, the full-network overflow-drop loop). Callers hoist <see cref="IsPresent"/> as the outer
        /// short-circuit, so this is never reached when RimIOT is absent; each sub-check also fast-exits internally.
        /// </summary>
        /// <param name="map">The map the cell is on.</param>
        /// <param name="cell">The cell to test (a haulable stack's Position).</param>
        public static bool IsRimIOTHandledCell(Map map, IntVec3 cell)
        {
            return IsNetworkManagedCell(map, cell) || IsNearPoweredInterface(map, cell);
        }

        /// <summary>
        /// True when <paramref name="cell"/> is within <see cref="InterfaceApronRadius"/> tiles (Chebyshev) of a
        /// spawned, powered RimIOT interface terminal. This is the #184 gate: a FULL RimIOT network drops the carried
        /// stack on a PLAIN GROUND tile at the depositing pawn's feet by the interface (RimIOT's own overflow), which
        /// HD would otherwise re-pocket and re-unload forever. Uses NO RimIOT type (the interface is found by its
        /// Verse <see cref="ThingDef"/> and its power read via vanilla <c>CompPowerTrader</c>), so it is JIT-safe and
        /// false-fast when RimIOT is absent (or the def was renamed).
        /// </summary>
        /// <param name="map">The map the cell is on.</param>
        /// <param name="cell">The cell to test (a haulable stack's Position).</param>
        public static bool IsNearPoweredInterface(Map map, IntVec3 cell)
        {
            if (!IsPresent || interfaceDefs == null || interfaceDefs.Count == 0 || map == null)
                return false;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (!interfaceCellsMemo.TryGet(tick, map.uniqueID, out var cells))
            {
                cells = BuildPoweredInterfaceCells(map);
                interfaceCellsMemo.Store(tick, map.uniqueID, cells);
            }
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (Math.Abs(c.x - cell.x) <= InterfaceApronRadius && Math.Abs(c.z - cell.z) <= InterfaceApronRadius)
                    return true;
            }
            return false;
        }

        /// <summary>Snapshot the cells of every spawned, POWERED RimIOT interface on <paramref name="map"/>, built
        /// once per (map, tick) by <see cref="interfaceCellsMemo"/>. Power is read via vanilla
        /// <c>CompPowerTrader.PowerOn</c> (an unpowered interface can't relay a deposit, so its apron is no loop
        /// risk), defaulting to powered when a terminal has no power comp, exactly RimIOT's own
        /// <c>Building_Interface.IsPowered</c> rule (decompile-verified). <c>listerThings.ThingsOfDef</c> is the
        /// pre-indexed per-def list (O(1) lookup + a handful of interfaces), so this is cheap even on the hot
        /// scan even across the (typically one or two) interface defs.</summary>
        /// <param name="map">The map to snapshot.</param>
        private static List<IntVec3> BuildPoweredInterfaceCells(Map map)
        {
            var list = new List<IntVec3>();
            for (int d = 0; d < interfaceDefs.Count; d++)
            {
                var things = map.listerThings.ThingsOfDef(interfaceDefs[d]);
                for (int i = 0; i < things.Count; i++)
                {
                    var t = things[i];
                    if (t == null || !t.Spawned)
                        continue;
                    var power = t.TryGetComp<CompPowerTrader>();
                    if (power == null || power.PowerOn)
                        list.Add(t.Position);
                }
            }
            return list;
        }
    }
}
