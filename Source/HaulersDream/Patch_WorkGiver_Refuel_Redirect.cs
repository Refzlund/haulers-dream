using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Autonomous bulk-refuel redirect: upgrade vanilla's single-stack <c>WorkGiver_Refuel</c> job to HD's one-trip
    /// BULK refuel, so colonists fill a refuelable (a shuttle's chemfuel, a deep drill, a generator) the SAME way HD
    /// already bulk-loads transporters and vehicles — sweep enough fuel into inventory and deposit it all in one trip
    /// — instead of vanilla's one-stack-in-hands per walk. This is what makes the feature fire on the PRIMARY path
    /// (a refuelable set to load, colonists fuelling it autonomously), not only on an explicit right-click.
    ///
    /// EXACT-TYPE guard: vanilla's <see cref="WorkGiver_Refuel"/> has subclasses
    /// (<c>WorkGiver_RefuelTurret</c> and modded/CE/VF refuel workgivers) that derive their own
    /// <c>JobStandard</c>/<c>JobAtomic</c> / eligibility. A POSTFIX on <c>JobOnThing</c> would also fire for those
    /// (it is a virtual the subclasses override-then-base-call only if they choose to). We therefore proceed ONLY
    /// when <c>__instance.GetType() == typeof(WorkGiver_Refuel)</c> — the exact vanilla base — so a turret/CE/VF
    /// refuel job is NEVER hijacked into a chemfuel bulk-load. (HasJobOnThing/JobOnThing both route through this
    /// prefix on the base type; the cheap HasPotentialBulkRefuel pre-gate keeps the HasJobOnThing probe light.)
    ///
    /// FAIL-OPEN: feature off, the thing isn't a non-atomic refuelable, no deficit, or HD can't build a bulk job →
    /// return true and let vanilla's single-stack refuel stand. HD only ever ACCELERATES refuelling, never starves it.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Refuel), nameof(WorkGiver_Refuel.JobOnThing))]
    public static class Patch_WorkGiver_Refuel_Redirect
    {
        // `pawn`, `t`, `forced` bind by name to JobOnThing(Pawn pawn, Thing t, bool forced). Prefix: when HD builds a
        // bulk job, set __result and skip the original (return false); otherwise leave vanilla's path (return true).
        static bool Prefix(object __instance, Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            // EXACT base only — never a WorkGiver_Refuel subclass (turret / CE / VF), so their refuel jobs are
            // untouched (the same generic/inheritance hijack guard the VF pack-vehicle redirect uses).
            if (__instance == null || __instance.GetType() != typeof(WorkGiver_Refuel))
                return true;
            if (!BulkRefuel.FeatureEnabled)
                return true;
            // Cheap pre-gate (this prefix runs on BOTH the HasJobOnThing probe and the real JobOnThing): skip the
            // expensive fuel sweep when there's plainly no bulk-refuel work (no comp / full / atomic / no deficit).
            if (!BulkRefuel.HasPotentialBulkRefuel(pawn, t))
                return true;
            var job = BulkRefuel.TryGiveBulkRefuelJob(pawn, t, playerOrder: forced);
            if (job != null)
            {
                __result = job; // upgrade to a one-trip bulk refuel
                return false;   // skip vanilla's single-stack RefuelJob
            }
            return true; // HD built none (single stack / nothing reachable) -> vanilla's single-stack stands
        }
    }
}
