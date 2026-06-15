using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The MANDATORY precision fix for the manifest decrement. When HD deposits a multi-stack load, the ThingOwner
    /// add auto-fires <c>CompTransporter.SubtractFromToLoadList</c> — but vanilla's <c>TransferableMatchingDesperate</c>
    /// is FUZZY and miscounts which manifest entry to subtract (and by how much) across same-def entries. This prefix
    /// — gated on the per-thread <see cref="Global.IsExecutingManagedUnload"/> flag (set only inside HD's deposit
    /// toil), so vanilla single-item loads and OTHER mods' loads keep vanilla accounting — replaces the decrement
    /// with a precise instance-aware match (<see cref="Global.FindBestMatchFor"/>, ranked by the pure
    /// <c>TransferableMatchPolicy</c>) written via the PUBLIC <c>Transferable.ForceTo</c> (no reflection — it keeps
    /// <c>EditBuffer</c> consistent), re-fires the correct finished message (shuttle vs transporters), and returns
    /// the exact count.
    ///
    /// Gated by feature flag at <see cref="Prepare"/> (fail-open: with the feature off the patch isn't even applied,
    /// so vanilla is byte-for-byte unchanged).
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.SubtractFromToLoadList))]
    public static class Patch_SubtractFromToLoadList
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        // No try/catch: a fault inside HD's own precise decrement is a real bug to surface (Harmony propagates),
        // not a silently-downgraded warning. The flag-gate makes this inert for every non-HD caller.
        static bool Prefix(CompTransporter __instance, Thing t, int count, bool sendMessageOnFinished, ref int __result)
        {
            if (!Global.IsExecutingManagedUnload)
                return true; // not HD's deposit -> run vanilla's fuzzy original unchanged

            var leftToLoad = __instance.leftToLoad;
            if (leftToLoad == null) { __result = 0; return false; }

            // Precise, instance-aware match for THIS deposit (def + the count being deposited).
            var best = Global.FindBestMatchFor(t?.def, count, leftToLoad);
            if (best == null) { __result = 0; return false; }

            int before = best.CountToTransfer;
            if (before <= 0) { __result = 0; return false; }
            int moved = count < before ? count : before; // never subtract more than this entry holds
            int after = before - moved;

            // PUBLIC ForceTo (sets CountToTransfer AND keeps EditBuffer consistent). Reflection fallback only if a
            // fork Transferable lacks the public method (RW 1.6 has it).
            if (!Global.ForceTo(best, after))
                Global.ForceToViaReflection(best, after);
            if (best.CountToTransfer <= 0)
                leftToLoad.Remove(best);

            // Re-fire the finished message correctly — shuttle vs transporters — exactly like vanilla, only after
            // the WHOLE group has nothing left to load (AnyInGroupHasAnythingLeftToLoad).
            if (sendMessageOnFinished && !__instance.AnyInGroupHasAnythingLeftToLoad)
            {
                var shuttle = __instance.parent.GetComp<CompShuttle>();
                if (shuttle == null || shuttle.AllRequiredThingsLoaded)
                {
                    if (shuttle != null)
                        Messages.Message("MessageFinishedLoadingShuttle".Translate(__instance.parent.Named("SHUTTLE")),
                            __instance.parent, MessageTypeDefOf.TaskCompletion);
                    else
                        Messages.Message("MessageFinishedLoadingTransporters".Translate(),
                            __instance.parent, MessageTypeDefOf.TaskCompletion);
                }
            }

            __result = moved;
            return false; // skip the fuzzy original
        }
    }
}
