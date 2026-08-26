using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The transporter/shuttle "Bulk unload all" toggle, a Harmony postfix on <see cref="CompTransporter.CompGetGizmosExtra"/>
    /// that splices ONE <see cref="Command_Toggle"/> into vanilla's gizmo row. While toggled on (a scribed flag in
    /// <see cref="HaulersDreamGameComponent.TransporterUnloads"/>, written through the MP-synced
    /// <see cref="MultiplayerCompat.SetBulkUnloadAll"/>), the autonomous <see cref="WorkGiver_BulkUnloadTransporters"/>
    /// keeps handing the hold to hauling pawns, one backpack-filling visit each, repeated across trips and haulers
    /// until nothing pullable remains. The one-visit right-click order (<see
    /// cref="FloatMenuOptionProvider_BulkUnloadTransporter"/>) stays for forcing a single hauler right now.
    ///
    /// <para>POSITION: inserted immediately before the first <c>Command_LoadToTransporter</c> ("Set to load") the
    /// base method yields, so it sits between vanilla's Unload/Cancel-load button and Set to load in BOTH of
    /// CompTransporter's branches (ready-to-launch: Unload, select-prev/all/next, ours, Set to load; idle: ours,
    /// Set to load). If that gizmo type ever disappears from the row, ours is appended at the end instead, a
    /// position change, never a lost toggle.</para>
    ///
    /// <para>ICON: vanilla's Set-to-load art flipped vertically (shipped as
    /// <c>Textures/HaulersDream/Interface/BulkUnloadAll.png</c>), so the arrow points OUT of the pod, the same
    /// art speaking the inverse operation. Falls back to the unflipped vanilla texture if the file is missing.</para>
    ///
    /// <para>MULTISELECT: emitted only while exactly one transporter-family thing is selected (vanilla suppresses
    /// its own loading gizmos under the same count for shuttles); a merged multi-toggle would make "which pod does
    /// this click flip?" ambiguous. Feature-off mid-session is handled by <see cref="Prepare"/>.</para>
    /// </summary>
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CompGetGizmosExtra))]
    // StaticConstructorOnStartup: this type holds a static Texture2D field. RimWorld warns about any type with a
    // static texture field that lacks this attribute (a structural check). The attribute satisfies that check;
    // the field itself stays NULL until the lazy property first builds it — it must NOT be an eager static
    // initializer, because Harmony forces a patch class's cctor during patch application, which runs on the
    // LOADING THREAD where ContentFinder.Get throws "resource from a different thread" (the reported error).
    // First draw happens in OnGUI, always the main thread, so the lazy build is legal.
    [StaticConstructorOnStartup]
    public static class Patch_CompTransporter_Gizmos_BulkUnloadAll
    {
        // The flipped Set-to-load art, resolved once on first draw (the texture is immutable, so a ContentFinder
        // lookup per selected pod per frame is pure waste). Falls back to the unflipped vanilla texture, then to
        // BadTex, so a missing file degrades the icon, never the gizmo row.
        private static Texture2D bulkUnloadAllIcon;
        private static Texture2D BulkUnloadAllIcon
            => bulkUnloadAllIcon ??= ContentFinder<Texture2D>.Get("HaulersDream/Interface/BulkUnloadAll", false)
               ?? CompTransporter.LoadCommandTex
               ?? BaseContent.BadTex;

        static bool Prepare() => HaulersDreamMod.Settings?.enableBulkUnloadTransporters ?? true;

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompTransporter __instance)
        {
            // LIVE feature-off check (Prepare() only runs once at patch-application): with the setting off, this
            // postfix must vanish entirely, no toggle AND no mutual-exclusion graying, or a mid-session settings
            // change would leave a clickable dead switch whose flag nothing acts on.
            if (!(HaulersDreamMod.Settings?.enableBulkUnloadTransporters ?? false))
            {
                foreach (var gizmo in __result)
                    yield return gizmo;
                yield break;
            }
            bool inserted = false;
            // MUTUAL EXCLUSION (unload side): while "Bulk unload all" is flagged on THIS transporter, vanilla's
            // Set-to-load gizmos grey out, starting a new load into a hold haulers are actively emptying used to
            // be possible and produced an invisible load/unload fight. The flag check is read-only synced state.
            bool flagOn = __instance?.parent != null
                          && (HaulersDreamGameComponent.Instance?.BulkUnloadAllActive(__instance.parent.thingIDNumber) ?? false);
            foreach (var gizmo in __result)
            {
                if (!inserted && gizmo is Command_LoadToTransporter)
                {
                    var bulk = MakeBulkUnloadAllToggle(__instance);
                    if (bulk != null)
                    {
                        yield return bulk;
                        inserted = true;
                    }
                    if (flagOn)
                        gizmo.Disable("HaulersDream.Gizmo.BulkUnloadAll.LoadBlocked".Translate());
                }
                yield return gizmo;
            }
            if (!inserted)
            {
                var bulk = MakeBulkUnloadAllToggle(__instance);
                if (bulk != null)
                    yield return bulk;
            }
        }

        private static Gizmo MakeBulkUnloadAllToggle(CompTransporter comp)
        {
            var parent = comp?.parent;
            if (parent == null || !parent.Spawned)
                return null;
            // [UC1-parity] A VF VehiclePawn's cargo is VF's to manage, no HD toggle on it (no-op when VF absent).
            if (VehicleFrameworkCompat.IsVehicle(parent))
                return null;
            // Respect vanilla's own gizmo suppression: a shuttle whose ShowLoadingGizmos is false is in a state
            // the game deliberately keeps UI-free (quest/ship-owned holds). Vanilla bails out of its whole loading
            // row there; our fallback append must not punch through that.
            var shuttle = comp.Shuttle;
            if (shuttle != null && !shuttle.ShowLoadingGizmos)
                return null;
            // Same single-selection rule vanilla applies to its own loading gizmos: with several transporters
            // selected the merged toggle's click target would be ambiguous.
            int transporterSelectionCount = 0;
            foreach (object selected in Find.Selector.SelectedObjects)
                if (selected is ThingWithComps t && t.HasComp<CompTransporter>())
                    transporterSelectionCount++;
            if (transporterSelectionCount > 1)
                return null;

            bool hasPullableContents = BulkUnloadTransporterGate.HasPullableContents(comp);
            var toggle = new Command_Toggle
            {
                defaultLabel = "HaulersDream.Gizmo.BulkUnloadAll.Label".Translate(),
                defaultDesc = "HaulersDream.Gizmo.BulkUnloadAll.Desc".Translate(parent.LabelShort),
                icon = BulkUnloadAllIcon,
                isActive = () => HaulersDreamGameComponent.Instance?.BulkUnloadAllActive(parent.thingIDNumber) ?? false,
                toggleAction = () =>
                    MultiplayerCompat.SetBulkUnloadAll(parent,
                        !(HaulersDreamGameComponent.Instance?.BulkUnloadAllActive(parent.thingIDNumber) ?? false)),
            };
            if (!hasPullableContents)
                toggle.Disable("HaulersDream.Gizmo.BulkUnloadAll.Empty".Translate());
            // MUTUAL EXCLUSION (load side): the toggle greys out while anything is committed INTO the hold — live
            // flows (lord / HD couriers / vanilla haulers), or a recorded load session with an OPEN MANIFEST. The
            // manifest condition carries both event blind spots: a top-up "Set to load" never re-fires
            // InitiateLoading, and draining the manifest (everything loaded out, or removed via the Contents-tab
            // X) releases the hold for the unload side even though no teardown method ran. The InitiateLoading /
            // TryAccept / CancelLoad postfixes keep the flag and session records honest at each transition.
            else if (BulkUnloadTransporterGate.ConflictActive(comp)
                     || BulkUnloadTransporterGate.LoadSessionHasOpenManifest(comp))
                toggle.Disable("HaulersDream.Gizmo.BulkUnloadAll.UnloadBlocked".Translate());
            return toggle;
        }
    }
}
