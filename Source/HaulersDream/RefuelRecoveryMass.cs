using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal static class RefuelRecoveryMass
    {
        internal static readonly Type CeInventory = AccessTools.TypeByName("CombatExtended.CompInventory");
        internal static readonly FieldInfo CeBulk = CeInventory?.Assembly.GetType("CombatExtended.CE_StatDefOf")
            ?.GetField("Bulk", BindingFlags.Public | BindingFlags.Static);

        internal static float Additional(Pawn pawn, StatDef stat)
            => pawn?.GetComp<CompHauledToInventory>()?.RetainedRefuelStat(stat) ?? 0f;
    }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.InventoryMass))]
    internal static class Patch_RefuelRecoveryInventoryMass
    {
        private static void Postfix(Pawn p, ref float __result)
            => __result += RefuelRecoveryMass.Additional(p, StatDefOf.Mass);
    }

    // CE's cached totals enumerate ordinary inventory/equipment only. Its live
    // weight/bulk getters feed native fit, available capacity and penalties.
    // Add current emergency custody at these reads, never to CE's cached fields.
    [HarmonyPatch]
    internal static class Patch_RefuelRecoveryCeMass
    {
        private static bool Prepare() => RefuelRecoveryMass.CeInventory != null;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = RefuelRecoveryMass.CeInventory;
            var weight = AccessTools.PropertyGetter(type, "currentWeight");
            var bulk = AccessTools.PropertyGetter(type, "currentBulk");
            if (weight == null || bulk == null || weight.IsStatic || bulk.IsStatic
                || weight.ReturnType != typeof(float) || bulk.ReturnType != typeof(float)
                || RefuelRecoveryMass.CeBulk?.FieldType != typeof(StatDef))
                throw new InvalidOperationException("CE retained refuel weight/bulk accounting APIs are unavailable.");
            yield return weight;
            yield return bulk;
        }
        private static void Postfix(ThingComp __instance, MethodBase __originalMethod, ref float __result)
        {
            var stat = __originalMethod.Name == "get_currentBulk"
                ? RefuelRecoveryMass.CeBulk.GetValue(null) as StatDef : StatDefOf.Mass;
            __result += RefuelRecoveryMass.Additional(__instance.parent as Pawn, stat);
        }
    }
}
