using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Perfect Pathfinding compatibility probe — REFLECTION ONLY, no hard assembly reference, so the mod runs
    /// identically with or without Perfect Pathfinding installed. Mirrors the other <c>*Compat</c> reflection
    /// bridges (e.g. <see cref="SimpleSidearmsCompat"/>): a single lazily-cached detection by type name, no
    /// behaviour when absent.
    ///
    /// <para>WHY a probe and not a bridge: Perfect Pathfinding works by PATCHING the vanilla pathfinder itself —
    /// it replaces vanilla's region-based heuristic so an A* search over a huge map stays accurate. The en-route
    /// pickup's accurate path-checker stages (<see cref="HaulersDream.Core.EnRoutePathChecker.Default"/> /
    /// <see cref="HaulersDream.Core.EnRoutePathChecker.Pathfinding"/>) compute leg costs through the SAME
    /// <c>map.pathFinder.FindPathNow</c> entry point, so they automatically inherit Perfect Pathfinding's
    /// accuracy when it is present — there is no separate API to call (this is exactly how While You're Up's
    /// docs note the two mods "should be compatible": both go through the one pathfinder). So this type exists
    /// only to (a) DETECT presence for an informational log line and (b) give the en-route feature a single
    /// place to reason about the dependency. The accurate path stages fall back to the (Perfect-Pathfinding-less)
    /// vanilla pathfinder automatically when it is absent — still correct, just vanilla's region heuristic.</para>
    /// </summary>
    public static class PerfectPathfindingCompat
    {
        private static bool initialized;
        private static bool active;

        /// <summary>Whether Perfect Pathfinding is loaded (its mod type resolves). Cached. When false the
        /// accurate en-route path stages use the plain vanilla pathfinder — still correct.</summary>
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
            // No try/catch: TypeByName returns null (never throws) when Perfect Pathfinding is absent — the real
            // optional-dependency precondition. A throw in here would be a genuine reflection fault worth
            // surfacing. Runs once, lazily on first IsActive. Perfect Pathfinding's root mod class is
            // PerformanceFish-style: probe its known type name (the public Mod entry the package ships).
            var modType = AccessTools.TypeByName("RimThreaded.PerfectPathfinding")  // older fork namespace
                          ?? AccessTools.TypeByName("PerfectPathfinding.PerfectPathfindingMod")
                          ?? AccessTools.TypeByName("PerfectPathfinding.Mod_PerfectPathfinding")
                          ?? AccessTools.TypeByName("PerfectPathfinding.PerfectPathfinding");
            // Fallback to the loaded-mods metadata check (robust to a class rename across versions): match the
            // workshop package by name. ModLister enumerates active mods; a name match means it's loaded.
            if (modType == null)
            {
                var mods = ModLister.AllInstalledMods;
                if (mods != null)
                    foreach (var m in mods)
                        if (m != null && m.Active && m.Name != null
                            && m.Name.IndexOf("Perfect Pathfinding", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            active = true;
                            break;
                        }
            }
            else
            {
                active = true;
            }
            if (active)
                Log.Message("[Hauler's Dream] Perfect Pathfinding detected — en-route accurate path checks "
                            + "inherit its accuracy automatically (same vanilla pathfinder entry point).");
        }
    }
}
