using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    // Adapted from flixzf's PR #148 (6430509acf2f759ca1849efd86062459ab45cf01).
    // Bill.billStack is unsaved. A bill serialized outside a BillStack can retain its UFT reference
    // after loading with no stack; vanilla BoundBill and ExposeData then dereference that null stack.
    [HarmonyPatch(typeof(UnfinishedThing), nameof(UnfinishedThing.ExposeData))]
    public static class Patch_UnfinishedThing_ExposeData_OrphanBillGuard
    {
        static bool Prepare()
        {
            if (UftOrphanBillGuard.FieldBound)
                return true;
            HDLog.Warn("UnfinishedThing.boundBillInt could not be bound; orphan unfinished-item repair is disabled.");
            return false;
        }

        static void Prefix(UnfinishedThing __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (UftOrphanBillGuard.TryUnbind(__instance))
                    UftOrphanBillGuard.NoteLoadRepair();
            }
            else if (Scribe.mode == LoadSaveMode.Saving && UftOrphanBillGuard.TryUnbind(__instance))
            {
                UftOrphanBillGuard.ReportSaveRepair(__instance);
            }
        }
    }

    // A separately deep-saved bill may be serialized before the map's unfinished item. Repair the
    // reciprocal link before that bill writes boundUft; the item prefix covers the reverse order.
    [HarmonyPatch(typeof(Bill_ProductionWithUft), nameof(Bill_ProductionWithUft.ExposeData))]
    public static class Patch_Bill_ProductionWithUft_ExposeData_OrphanBillGuard
    {
        static bool Prepare() => UftOrphanBillGuard.FieldBound;

        static void Prefix(Bill_ProductionWithUft __instance)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var uft = UftOrphanBillGuard.ReciprocalOrphanItem(__instance);
                if (uft != null && UftOrphanBillGuard.TryUnbind(uft))
                    UftOrphanBillGuard.ReportSaveRepair(uft);
            }
        }
    }

    internal static class UftOrphanBillGuard
    {
        // Probe without generating a field-access delegate in a static initializer. A missing/changed field
        // disables this repair before patching, rather than first throwing partway through saving.
        private static readonly FieldInfo BoundBillField = AccessTools.Field(typeof(UnfinishedThing), "boundBillInt");
        internal static bool FieldBound => BoundBillField != null && !BoundBillField.IsStatic
            && BoundBillField.FieldType == typeof(Bill_ProductionWithUft);
        private static int repairedDuringLoad;

        // The game constructor also runs for loads that later abort before FinalizeInit.
        internal static void ResetLoadCount() => repairedDuringLoad = 0;
        internal static void NoteLoadRepair() => repairedDuringLoad++;

        internal static UnfinishedThing ReciprocalOrphanItem(Bill_ProductionWithUft bill)
        {
            if (!FieldBound || bill == null || bill.billStack != null)
                return null;
            var uft = bill.BoundUft; // Native getter only returns boundUftInt; unlike UFT.BoundBill it is safe.
            return uft != null && ReferenceEquals(BoundBillField.GetValue(uft), bill) ? uft : null;
        }

        internal static void ReportSaveRepair(UnfinishedThing uft) =>
            HDLog.Warn($"Save repair: unbound unfinished item {uft.ThingID} from a bill with no "
                + "bill stack before saving; its recipe, creator, work and ingredients were preserved.");

        internal static bool TryUnbind(UnfinishedThing uft)
        {
            if (!FieldBound || uft == null)
                return false;
            var bill = (Bill_ProductionWithUft)BoundBillField.GetValue(uft);
            if (bill == null || bill.billStack != null)
                return false;

            // Never read the unsafe BoundBill getter or recreate/rebind a bill. Clear only these links;
            // a loose UFT keeps all its data and native recipe/creator eligibility rules still apply.
            BoundBillField.SetValue(uft, null);
            if (bill.BoundUft == uft)
                bill.ClearBoundUft();
            return true;
        }

        internal static void RepairAfterLoadAndReport()
        {
            int repaired = repairedDuringLoad;
            ResetLoadCount();
            if (!FieldBound)
                return;

            // Once per load, cover spawned haulable subclasses that do not call base.ExposeData.
            // Scribed held UFTs are covered by the PostLoadInit prefix, not by this map sweep.
            var maps = Find.Maps;
            if (maps != null)
                for (int m = 0; m < maps.Count; m++)
                {
                    var things = maps[m]?.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver);
                    if (things == null)
                        continue;
                    for (int i = 0; i < things.Count; i++)
                        if (things[i] is UnfinishedThing uft && TryUnbind(uft))
                            repaired++;
                }

            if (repaired > 0)
                HDLog.Msg($"Save repair: unbound {repaired} unfinished item(s) from bills with no bill stack. "
                    + "Their recipes, creators, work and ingredients were preserved.");
        }
    }
}
