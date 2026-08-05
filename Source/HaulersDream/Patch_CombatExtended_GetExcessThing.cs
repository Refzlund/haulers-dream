using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Keeps CE's loadout cleanup from floor-dropping cargo that Hauler's Dream owns temporarily.
    ///
    /// CE's <c>JobGiver_UpdateLoadout</c> calls <c>Utility_HoldTracker.GetExcessThing</c> and immediately
    /// <c>TryDrop</c>s the returned inventory stack before it creates a storage-haul job. HD cargo can remain in a
    /// pawn's inventory across ordinary work and the unload grace period, so task ordering alone cannot prevent
    /// that direct drop. The old bridge registered every scooped stack in CE's HoldTracker, but that tracker is a
    /// persistent, player-facing per-ThingDef "forced carry" list with no source-aware release operation. If the
    /// pawn retained one policy drug of the same def, CE never saw a zero inventory count and the forced record
    /// survived after HD unloaded its cargo.
    ///
    /// This POSTFIX leaves CE's calculation untouched, then vetoes while the pawn still carries any real,
    /// unloadable HD cargo. The scope must be the whole temporary load rather than CE's selected stack: a generic
    /// limit is shared across defs, so tagged Beer can make CE select and drop an untagged Wake-Up stack. An explicit
    /// vanilla/CE <c>UnloadEverything</c> request is left untouched: that path is already meant to empty the pack and must be
    /// allowed to finish and clear its flag. Once HD's ordinary unload consumes all tagged surplus, CE resumes
    /// normal excess cleanup. Missing-loadout pickup still runs because
    /// only <c>GetExcessThing</c>'s result is suppressed, not the surrounding <c>JobGiver_UpdateLoadout</c>.
    ///
    /// Reflection-only soft dependency: CE absent means <see cref="Prepare"/> returns false and Harmony skips the
    /// patch. Because CE's work scan may run off the main thread, the postfix copies ownership from a transient
    /// ConcurrentDictionary mirror and never reads or heals HD's live HashSet. It creates no CE records and no
    /// additional saved state. Any HD-side failure degrades to CE's original result and is reported once.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CombatExtended_GetExcessThing
    {
        private static MethodBase targetMethod;
        [ThreadStatic] private static List<Thing> trackedScratch;

        static bool Prepare()
        {
            var holdTrackerType = AccessTools.TypeByName("CombatExtended.Utility_HoldTracker");
            if (holdTrackerType == null)
                return false;

            targetMethod = AccessTools.Method(holdTrackerType, "GetExcessThing", new[]
            {
                typeof(Pawn),
                typeof(Thing).MakeByRefType(),
                typeof(int).MakeByRefType(),
            });
            if (targetMethod == null)
                HDLog.Warn("Combat Extended present but Utility_HoldTracker.GetExcessThing(Pawn, out Thing, out int) "
                           + "did not resolve; HD cannot temporarily shield scooped cargo from CE's loadout drop. "
                           + "A CE rename likely. Please report it; HD continues with CE's original behaviour.");
            return targetMethod != null;
        }

        static MethodBase TargetMethod() => targetMethod;

        // Positional argument names avoid depending on CE's source parameter names:
        // __0 = pawn, __1 = dropThing, __2 = dropCount.
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn __0, ref Thing __1, ref int __2, ref bool __result)
        {
            if (!__result || __0 == null || (__0.inventory?.UnloadEverything ?? false))
                return;
            List<Thing> tracked = null;
            try
            {
                var comp = __0.GetComp<CompHauledToInventory>();
                var owner = __0.inventory?.innerContainer;
                if (comp == null || __1 == null || owner == null || !owner.Contains(__1))
                    return;

                tracked = trackedScratch ?? (trackedScratch = new List<Thing>());
                comp.CopyTrackedNoHeal(tracked);
                // The early-return gate above already established that CE selected a live inventory excess and
                // this is not an explicit UnloadEverything pass. Whole-load scope is intentional: tagged Beer can
                // consume a shared GenericDrugs ceiling and make CE select an untagged Wake-Up stack instead.
                if (!PawnUnloadChecker.AnyUnloadable(__0, tracked))
                    return;

                __1 = null;
                __2 = 0;
                __result = false;
            }
            catch (Exception ex)
            {
                HDGuard.SeamDegraded(ex, "CombatExtended.Utility_HoldTracker.GetExcessThing (HD cargo guard)", __0,
                    "kept CE's selected excess drop, so its loadout management continues.");
            }
            finally
            {
                // ThreadStatic scratch must not retain destroyed pawns/items for the lifetime of an optimizer
                // worker thread, and the next invocation must always start from a fresh mirror copy.
                tracked?.Clear();
            }
        }
    }
}
