using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;

namespace HaulersDream
{
    // Always installed, even when unloading starts disabled: enabling the feature later needs actual history.
    // Lifecycle seams adapted from nullpat's GH267; only successful acceptance can acquire a load session.
    [HarmonyPatch(typeof(TransporterUtility), nameof(TransporterUtility.InitiateLoading))]
    internal static class Patch_TransporterUtility_InitiateLoading_RecordSession
    {
        static void Postfix(IEnumerable<CompTransporter> transporters, int __result)
        { if (__result >= 0) HaulersDreamGameComponent.Instance?.TransporterLoadAccepted(transporters, true); }
    }

    [HarmonyPatch(typeof(Dialog_LoadTransporters), "TryAccept")]
    internal static class Patch_Dialog_LoadTransporters_TryAccept_RecordSession
    {
        static void Postfix(bool __result, List<CompTransporter> ___transporters)
            => HaulersDreamGameComponent.Instance?.TransporterLoadAccepted(___transporters, __result);
    }

    // TryRemoveLord neither clears a manifest nor proves that a lord existed. Only actual cleanup
    // ends recorded provenance; live manifests/couriers still independently guard custody.
    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CleanUpLoadingVars))]
    internal static class Patch_CompTransporter_CleanUpLoadingVars_EndSession
    {
        static void Prefix(CompTransporter __instance, out int __state) => __state = __instance.groupID;
        static void Postfix(int __state) => HaulersDreamGameComponent.Instance?.TransporterLoadEnded(__state);
    }

    [HarmonyPatch(typeof(CompTransporter), nameof(CompTransporter.CancelLoad), new Type[0])]
    internal static class Patch_CompTransporter_CancelLoad_ClearUnloadFlag
    {
        static void Postfix(CompTransporter __instance)
            => HaulersDreamGameComponent.Instance?.TrySetBulkUnloadAll(__instance.parent, false);
    }

    [HarmonyPatch(typeof(ITab_ContentsTransporter), "OnDropThing")]
    internal static class Patch_ITab_ContentsTransporter_OnDropThing_ClearFlag
    {
        static void Postfix(ITab_ContentsTransporter __instance)
            => HaulersDreamGameComponent.Instance?.BulkUnloadAllClearIfNothingPullable(__instance.Transporter);
    }
}
