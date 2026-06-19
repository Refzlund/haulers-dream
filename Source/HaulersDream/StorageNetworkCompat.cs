using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Storage Network" (BlackMouse, Steam id 3729793040) compatibility bridge — REFLECTION ONLY, no hard assembly
    /// reference. EXPERIMENTAL and OPT-IN (<see cref="HaulersDreamSettings.enableStorageNetworkBulkLoad"/>, default
    /// OFF); fully inert when the setting is off, when Storage Network (SN) is absent, or if its API shape differs.
    ///
    /// WHY: SN is a VIRTUAL / digital storage (Applied-Energistics style). Stored items are DESPAWNED, held inside the
    /// ThingOwners of its server/terminal buildings — never on the map floor. HD's bulk-load sweep draws only from the
    /// loose-haulables lister plus the spawned in-storage supplement (<c>TransportLoad.AddStoredClaimables</c> →
    /// <c>ListerThings.ThingsOfDef</c>, both Spawned-only), so it cannot see SN's network contents. Without this
    /// bridge a transporter/portal/vehicle whose manifest lives in the network finds nothing to bulk-load and the pawn
    /// degrades to vanilla's one-stack-per-trip loading (the reported "takes 1 pack instead of everything" behaviour).
    ///
    /// HOW: SN already materialises a despawned thing on demand — a prefix on <c>Pawn_JobTracker.StartJob</c> replaces
    /// any DESPAWNED job target / queue entry that is held in a terminal/server with a freshly-spawned stack at the
    /// nearest usable terminal (it handles <c>targetQueueB</c>). HD's bulk-load drivers stage their pickup stacks in
    /// <c>job.targetQueueB</c> — exactly what SN's auto-spawn rewrites — so HD only needs to ADD the network's
    /// despawned stacks to the load plan. When the job starts, SN materialises them at the terminal and HD's driver
    /// picks them up like any spawned stack. HD's sweep toil already skips a queue entry that is not <c>Spawned</c>
    /// (<c>JobDriver_LoadInBulkBase</c>), so a stack SN cannot materialise is a safe skip, not a stall.
    ///
    /// This bridge performs ONLY READ-ONLY queries during HD's (pure, speculatively-probed) load planning — it never
    /// calls SN's side-effecting withdraw/spawn API; the actual materialisation is SN's own StartJob hook. Resolution
    /// is fail-open: if any required member is missing, <see cref="IsActive"/> stays false and HD behaves exactly as
    /// without SN. Mirrors the reflection-soft-dep style of <see cref="CECompat"/> / <see cref="VehicleFrameworkCompat"/>.
    /// </summary>
    public static class StorageNetworkCompat
    {
        private static bool initialized;
        private static bool active;
        private static MethodInfo getTerminalsMethod;       // StorageNetwork.TerminalRegistry.GetTerminals(Map) -> List<Building_StorageTerminal>
        private static MethodInfo canPawnUseTerminalMethod;  // StorageNetwork.StorageNetworkMod.CanPawnUseTerminal(Building_StorageTerminal, Pawn) -> bool
        private static MethodInfo getStacksByDefMethod;      // StorageNetwork.Building_StorageTerminal.GetStacksByDef(ThingDef) -> List<Thing>

        /// <summary>Whether Storage Network is loaded and its read-only terminal API resolved. Cached.</summary>
        public static bool IsActive
        {
            get { if (!initialized) Init(); return active; }
        }

        private static void Init()
        {
            initialized = true;
            // TypeByName returns null (never throws) when SN isn't loaded — that is the real precondition.
            var registryType = AccessTools.TypeByName("StorageNetwork.TerminalRegistry");
            var modType = AccessTools.TypeByName("StorageNetwork.StorageNetworkMod");
            var terminalType = AccessTools.TypeByName("StorageNetwork.Building_StorageTerminal");
            if (registryType == null || modType == null || terminalType == null)
                return; // SN not loaded — HD behaves exactly as without it.
            getTerminalsMethod = AccessTools.Method(registryType, "GetTerminals", new[] { typeof(Map) });
            canPawnUseTerminalMethod = AccessTools.Method(modType, "CanPawnUseTerminal", new[] { terminalType, typeof(Pawn) });
            getStacksByDefMethod = AccessTools.Method(terminalType, "GetStacksByDef", new[] { typeof(ThingDef) });
            active = getTerminalsMethod != null && canPawnUseTerminalMethod != null && getStacksByDefMethod != null;
            if (active)
                Log.Message("[Hauler's Dream] Storage Network detected — opt-in bulk-load from its network servers is "
                            + "available (enable it under Advanced loading in the settings; default off).");
            else
                HDLog.Warn("Storage Network present but its terminal API did not resolve (a version/rename?); "
                           + "bulk-loading from the network is unavailable. Vanilla one-stack loading still works.");
        }

        /// <summary>
        /// The map's SN terminals this pawn may use for loading: spawned, not forbidden, reachable (at the pawn's
        /// normal danger), and accepted by SN's own <c>CanPawnUseTerminal</c> (player faction / per-terminal animal
        /// rule). Returns an empty list when SN is absent/inactive. READ-ONLY. Each element is a terminal Building
        /// (typed as <see cref="Thing"/> so callers need no SN reference).
        /// </summary>
        public static List<Thing> UsableTerminals(Pawn pawn, Map map)
        {
            var result = new List<Thing>();
            if (!IsActive || pawn == null || map == null)
                return result;
            var raw = getTerminalsMethod.Invoke(null, new object[] { map }) as IList;
            if (raw == null)
                return result;
            for (int i = 0; i < raw.Count; i++)
            {
                if (!(raw[i] is Thing terminal) || !terminal.Spawned || terminal.IsForbidden(pawn))
                    continue;
                if (!(bool)canPawnUseTerminalMethod.Invoke(null, new object[] { terminal, pawn }))
                    continue;
                if (!pawn.CanReach(terminal, PathEndMode.Touch, pawn.NormalMaxDanger()))
                    continue;
                result.Add(terminal);
            }
            return result;
        }

        /// <summary>
        /// The DESPAWNED network stacks of <paramref name="def"/> accessible through <paramref name="terminal"/> (SN
        /// returns the whole network's stacks of that def). READ-ONLY — these are added to the load plan and
        /// materialised by SN's own StartJob auto-spawn when the job runs; this never withdraws or spawns. Empty when
        /// inactive. Returned as <see cref="Thing"/>s so callers need no SN reference.
        /// </summary>
        public static List<Thing> NetworkStacksOfDef(Thing terminal, ThingDef def)
        {
            var result = new List<Thing>();
            if (!IsActive || terminal == null || def == null)
                return result;
            if (getStacksByDefMethod.Invoke(terminal, new object[] { def }) is IList raw)
                for (int i = 0; i < raw.Count; i++)
                    if (raw[i] is Thing t)
                        result.Add(t);
            return result;
        }
    }
}
