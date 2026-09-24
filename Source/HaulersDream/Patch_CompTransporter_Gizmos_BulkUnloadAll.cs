using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    // Toggle and icon adapted from nullpat's GH267. Always installed so runtime enablement is supported.
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CompGetGizmosExtra))]
    [StaticConstructorOnStartup]
    public static class Patch_CompTransporter_Gizmos_BulkUnloadAll
    {
        private static Texture2D icon;
        private static Texture2D Icon => icon ??= ContentFinder<Texture2D>.Get("HaulersDream/Interface/BulkUnloadAll", false)
            ?? CompTransporter.CancelLoadCommandTex;

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, CompTransporter __instance)
        {
            var toggle = MakeToggle(__instance);
            bool inserted = false;
            bool blockLoad = BulkUnloadTransporterGate.AnyUnloadFlagInGroup(__instance);
            foreach (var gizmo in __result)
            {
                if (gizmo is Command_LoadToTransporter)
                {
                    if (!inserted && toggle != null) { yield return toggle; inserted = true; }
                    // Every loading command, including commands for a flagged non-primary group member.
                    if (blockLoad) gizmo.Disable("HaulersDream.Gizmo.BulkUnloadAll.LoadBlocked".Translate());
                }
                yield return gizmo;
            }
            if (!inserted && toggle != null) yield return toggle;
        }

        private static Command_Toggle MakeToggle(CompTransporter comp)
        {
            var parent = comp?.parent;
            if (parent == null || !parent.Spawned || !MultiplayerCompat.TransporterLocalUi) return null;
            bool flagged = HaulersDreamGameComponent.Instance?.BulkUnloadAllFlagged(parent.thingIDNumber) == true;
            if (!flagged && (!BulkUnloadTransporterGate.Enabled || !BulkUnloadTransporterGate.IsSupported(comp))) return null;
            int selected = 0;
            foreach (object item in Find.Selector.SelectedObjects)
                if (item is ThingWithComps thing && thing.HasComp<CompTransporter>()) selected++;
            if (selected > 1) return null;
            int mapId = parent.Map.uniqueID;
            var toggle = new Command_Toggle
            {
                defaultLabel = "HaulersDream.Gizmo.BulkUnloadAll.Label".Translate(),
                defaultDesc = "HaulersDream.Gizmo.BulkUnloadAll.Desc".Translate(parent.LabelShort),
                icon = Icon,
                isActive = () => HaulersDreamGameComponent.Instance?.BulkUnloadAllFlagged(parent.thingIDNumber) == true,
                toggleAction = () => TransporterCommand.SetUnloadSynced(parent, mapId,
                    !(HaulersDreamGameComponent.Instance?.BulkUnloadAllFlagged(parent.thingIDNumber) ?? false))
            };
            if (!MultiplayerCompat.TransporterAvailable) toggle.Disable("HaulersDream.Transporter.SyncUnavailable".Translate());
            else if (!flagged)
            {
                var blocked = BulkUnloadTransporterGate.TargetBlock(comp, false);
                if (blocked != TransporterUnloadBlock.None) toggle.Disable(TransporterCommand.UnloadReason(blocked));
                else if (!BulkUnloadTransporterGate.HasPullableContents(comp)) toggle.Disable("HaulersDream.Gizmo.BulkUnloadAll.Empty".Translate());
            }
            return toggle;
        }
    }
}
