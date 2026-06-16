using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Boarding inventory sync (shuttle parity, BLFT gap #5). When a pawn carrying HD-tagged manifest cargo BOARDS a
    /// shuttle, that cargo should ride the shuttle's container — not fly away inside the pawn's backpack. Vanilla
    /// <c>CompTransporter.Notify_ThingAdded(Thing t)</c> fires once for the boarding pawn (the pawn arrives as the
    /// <c>t</c> arg — confirmed by vanilla's own <c>t is Pawn pawn &amp;&amp; pawn.IsFormingCaravan()</c> branch in the
    /// same method), so this POSTFIX then drains the pawn's HD-tagged inventory into the shuttle's
    /// <c>innerContainer</c>, decrementing the manifest exactly.
    ///
    /// CompShuttle-only (parity with BLFT — a non-shuttle transporter loads via HD's normal bulk-load driver).
    ///
    /// The move is wrapped in <see cref="Global.IsExecutingManagedUnload"/> = true (try/finally) so the
    /// <c>SubtractFromToLoadList</c> auto-fired by <c>TryTransferToContainer</c> takes HD's PRECISE-decrement path
    /// (<see cref="Patch_SubtractFromToLoadList"/>) — the same instance-aware match the HD bulk-load deposit toil uses.
    /// Without the flag, that auto-fired subtract would run vanilla's fuzzy original AND this postfix's own move would
    /// not be in lock-step with the clamp, double-counting the manifest. With it, each transfer subtracts exactly the
    /// matched entry's remaining.
    ///
    /// Gated by feature flag at <see cref="Prepare"/> (fail-open on null Settings, matching HD idiom): with
    /// <c>enableBulkLoadTransporters</c> off the patch isn't even applied, so vanilla is byte-for-byte unchanged.
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.Notify_ThingAdded))]
    public static class Patch_CompTransporter_NotifyThingAdded
    {
        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkLoadTransporters ?? true;

        // Reused scratch so the per-board scan allocates nothing. [ThreadStatic] to match this assembly's convention
        // for hook-reachable scratch state (a threading mod can't race a per-thread buffer); cleared at use.
        [System.ThreadStatic] private static List<Thing> scratchItems;

        // No try/catch around the correctness logic (HD idiom: a fault here is a real bug to surface). NOTE: vanilla
        // Notify_ThingAdded wraps its whole body (this postfix included) in a try/catch that logs + swallows, so a
        // fault would be LOGGED rather than crash the game — but the IsExecutingManagedUnload try/finally below still
        // resets the flag on any throw, so it is never left stuck.
        [HarmonyPostfix]
        static void Postfix(CompTransporter __instance, Thing t)
        {
            // Explicit runtime gate (in addition to Prepare's apply-time gate) — defensive, and matches the spec.
            if (HaulersDreamMod.Settings?.enableBulkLoadTransporters != true)
                return;

            // Shuttle-only (parity with BLFT). A non-shuttle transporter is serviced by HD's normal bulk-load driver.
            if (__instance.parent?.GetComp<CompShuttle>() == null)
                return;

            // Only a boarding pawn carrying something is interesting.
            if (!(t is Pawn pawn))
                return;
            var pawnInventory = pawn.inventory?.innerContainer;
            if (pawnInventory == null || pawnInventory.Count == 0)
                return;

            // Nothing left to load -> nothing to sync.
            var leftToLoad = __instance.leftToLoad;
            if (leftToLoad.NullOrEmpty())
                return;

            // The boarding pawn's HD-tagged inventory is the source of truth for "this is manifest cargo HD scooped".
            var hcomp = pawn.GetComp<CompHauledToInventory>();
            if (hcomp == null)
                return;
            var tagged = hcomp.GetHashSet();
            if (tagged.Count == 0)
                return;

            var transporterContainer = __instance.innerContainer;
            if (transporterContainer == null)
                return;

            // Snapshot the tagged items present in the backpack (TryTransferToContainer mutates the live inventory and
            // can deregister tags, so we must not enumerate hcomp's set / the inventory directly while moving).
            var items = scratchItems ?? (scratchItems = new List<Thing>());
            items.Clear();
            for (int i = 0; i < pawnInventory.Count; i++)
            {
                var item = pawnInventory[i];
                if (item != null && tagged.Contains(item))
                    items.Add(item);
            }

            for (int i = 0; i < items.Count; i++)
            {
                var thing = items[i];
                // The thing may have been merged/moved by an earlier iteration; re-check it's still in the backpack.
                if (thing == null || thing.Destroyed || !pawnInventory.Contains(thing))
                    continue;

                // The leftToLoad entry vanilla would decrement for this stack (the SAME instance-aware matcher the
                // SubtractFromToLoadList intercept and deposit clamp use), and how much it still wants.
                var bestMatch = Global.FindBestMatchFor(thing, leftToLoad);
                if (bestMatch == null)
                    continue;
                int remaining = bestMatch.CountToTransfer;
                if (remaining <= 0)
                    continue;
                int count = thing.stackCount < remaining ? thing.stackCount : remaining;
                if (count <= 0)
                    continue;

                int moved;
                // Set the per-thread flag so the SubtractFromToLoadList auto-fired by the transfer does the PRECISE
                // decrement (clamped to the matched entry) instead of vanilla's fuzzy math. try/finally resets it even
                // on throw (no suppression). canMergeWithExistingStacks:false mirrors the HD bulk-load deposit toil.
                Global.IsExecutingManagedUnload = true;
                try
                {
                    moved = pawnInventory.TryTransferToContainer(thing, transporterContainer, count, out Thing _, canMergeWithExistingStacks: false);
                }
                finally
                {
                    Global.IsExecutingManagedUnload = false;
                }

                // Fully moved out of the backpack -> drop the tag (a partial leaves the remainder tagged for HD's
                // normal unload), mirroring the bulk-load driver's deregister.
                if (moved > 0 && !pawnInventory.Contains(thing))
                    hcomp.Deregister(thing);
            }

            items.Clear();
        }
    }
}
