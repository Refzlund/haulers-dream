using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The MANDATORY precision fix for the manifest decrement. When HD deposits a multi-stack load, the ThingOwner
    /// add auto-fires <c>CompTransporter.SubtractFromToLoadList</c>. Vanilla picks the entry to subtract via
    /// <c>TransferableMatchingDesperate</c> (a 3-tier identity→TransferAsOne→def-only matcher) and decrements
    /// <c>min(count, remaining)</c>; what makes it imprecise for HD is that vanilla also re-fires the finished message
    /// and HD needs the moved count clamped to the matched entry. This prefix — gated on the per-thread
    /// <see cref="Global.IsExecutingManagedUnload"/> flag (set only inside HD's deposit toil), so vanilla single-item
    /// loads and OTHER mods' loads keep vanilla accounting — reproduces vanilla's decrement EXACTLY: it selects the
    /// SAME entry via <see cref="Global.FindBestMatchFor"/> (which delegates straight to
    /// <c>TransferableUtility.TransferableMatchingDesperate</c>, the identical call the deposit CLAMP makes, so clamp
    /// and decrement can never disagree), writes the result via the PUBLIC <c>Transferable.ForceTo</c> (keeps
    /// <c>EditBuffer</c> consistent), re-fires the correct finished message (shuttle vs transporters), and returns the
    /// exact count.
    ///
    /// Gated by feature flag at <see cref="Prepare"/> (fail-open: with the feature off the patch isn't even applied,
    /// so vanilla is byte-for-byte unchanged).
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.SubtractFromToLoadList))]
    public static class Patch_SubtractFromToLoadList
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        // No try/catch in HD: a fault inside HD's own precise decrement is a real bug to surface, not a
        // silently-downgraded warning. NOTE (BL-03): on the transporter path the throw does NOT propagate to the
        // deposit toil — vanilla CompTransporter.Notify_ThingAdded (the caller of SubtractFromToLoadList) WRAPS it in
        // try/catch that swallows + Debug.LogError("Exception in Notify_ThingAdded: ..."), so a fault here is LOGGED
        // and that single decrement is SKIPPED (the manifest entry stays un-subtracted), not rethrown. The driver's
        // try/finally still resets IsExecutingManagedUnload regardless, so the flag is never stuck. (The portal path
        // genuinely propagates — MapPortal.Notify_ThingAdded has no try/catch.) The flag-gate makes this inert for
        // every non-HD caller either way.
        static bool Prefix(CompTransporter __instance, Thing t, int count, bool sendMessageOnFinished, ref int __result)
        {
            if (!Global.IsExecutingManagedUnload)
                return true; // not HD's deposit -> run vanilla's fuzzy original unchanged

            var leftToLoad = __instance.leftToLoad;
            if (leftToLoad == null) { __result = 0; return false; }

            // Precise, instance-aware match for THIS deposit (def + stuff + quality via TransferAsOne, then the count
            // being deposited) — mirrors vanilla TransferableMatchingDesperate's Tier-2 match so a mixed-stuff/quality
            // manifest decrements the entry that actually matches the deposited stack.
            var best = Global.FindBestMatchFor(t, count, leftToLoad);
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

    /// <summary>
    /// The portal counterpart of the manifest-decrement precision fix. <c>MapPortal.SubtractFromToLoadList(Thing,
    /// int)</c> is the 2-arg variant (no message bool — a portal has no shuttle/transporters finished-message split,
    /// and vanilla's own "MessageCantLoadMoreIntoPortal" is a per-tick stall notice, NOT a finished message, so the
    /// HD intercept fires NONE). When HD deposits into a portal's <c>PortalContainerProxy</c>, the proxy's
    /// <c>TryAdd</c> auto-fires <c>MapPortal.Notify_ThingAdded → SubtractFromToLoadList(t, t.stackCount)</c>. Gated on
    /// the per-thread <see cref="Global.IsExecutingManagedPortalUnload"/> flag (set only inside HD's portal deposit
    /// toil) so vanilla single-item portal loads and OTHER mods keep vanilla's fuzzy accounting, this prefix replaces
    /// the decrement with a precise instance-aware match written via the PUBLIC <c>Transferable.ForceTo</c>.
    ///
    /// Gated by feature flag at <see cref="Prepare"/> (fail-open: feature off → not applied → vanilla unchanged).
    /// </summary>
    [HarmonyPatch(typeof(MapPortal), nameof(MapPortal.SubtractFromToLoadList))]
    public static class Patch_MapPortal_SubtractFromToLoadList
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadPortal ?? true;

        // No try/catch: a fault inside HD's own precise decrement is a real bug to surface. UNLIKE the transporter
        // path (BL-03), this one genuinely propagates — vanilla MapPortal.Notify_ThingAdded calls
        // SubtractFromToLoadList WITHOUT a try/catch, so a throw here unwinds to the deposit toil. The driver's
        // try/finally still resets IsExecutingManagedPortalUnload on the way out, so the flag is never stuck. The
        // flag-gate makes this inert for every non-HD caller.
        static bool Prefix(MapPortal __instance, Thing t, int count, ref int __result)
        {
            if (!Global.IsExecutingManagedPortalUnload)
                return true; // not HD's deposit -> run vanilla's fuzzy original unchanged

            var leftToLoad = __instance.leftToLoad;
            if (leftToLoad == null) { __result = 0; return false; }

            // Precise, instance-aware match for THIS deposit (def + stuff + quality via TransferAsOne, then the count
            // being deposited) — mirrors vanilla TransferableMatchingDesperate's Tier-2 match so a mixed-stuff/quality
            // manifest decrements the entry that actually matches the deposited stack.
            var best = Global.FindBestMatchFor(t, count, leftToLoad);
            if (best == null) { __result = 0; return false; }

            int before = best.CountToTransfer;
            if (before <= 0) { __result = 0; return false; }
            int moved = count < before ? count : before; // never subtract more than this entry holds
            int after = before - moved;

            // PUBLIC ForceTo (sets CountToTransfer AND keeps EditBuffer consistent). Reflection fallback only if a
            // fork Transferable lacks the public method (RW 1.6 has it).
            if (!Global.ForceTo(best, after))
                Global.ForceToViaReflection(best, after);
            // Faithful to vanilla MapPortal.SubtractFromToLoadList: drop the deposited Thing instance from the entry's
            // candidate-things list (a no-op when the HD-deposited split was never a manifest candidate, but keeps the
            // manifest entry's things list free of stale refs exactly as vanilla does).
            best.things?.Remove(t);
            if (best.CountToTransfer <= 0)
                leftToLoad.Remove(best);

            __result = moved;
            return false; // skip the fuzzy original
        }
    }
}
