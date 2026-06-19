using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Smart Medicine compatibility bridge — REFLECTION ONLY, no hard assembly reference. Verified against the
    /// Smart Medicine source (alextd/RimWorld-SmartMedicine): a pawn's "stock up" medicine/drugs are kept in
    /// inventory and tested by the static extension <c>SmartMedicine.StockUpUtility.StockingUpOn(Pawn,
    /// ThingDef)</c>.
    ///
    /// Why this exists: HD's "unload all surplus" would otherwise ship a doctor's stocked medicine to storage,
    /// and Smart Medicine's stock-up job re-fetches it — an unload↔pickup LOOP. <see cref="IsStockedUp"/> reports
    /// stocked items as keep-stock so <see cref="InventorySurplus.SurplusOf"/> returns 0 and they are never
    /// adopted/unloaded (mirrors Smart Medicine's own DontDropStockedDrugs patch).
    /// </summary>
    public static class SmartMedicineCompat
    {
        private static bool initialized;
        private static bool active;
        private static MethodInfo stockingUpOn; // static SmartMedicine.StockUpUtility.StockingUpOn(Pawn, ThingDef)

        /// <summary>Whether Smart Medicine is loaded (its StockUpUtility resolves and StockingUpOn is usable).</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        private static void Init()
        {
            initialized = true;
            // No try/catch: SM-ABSENT is the TypeByName == null precondition; members are null-guarded. Lazy, once.
            var utility = AccessTools.TypeByName("SmartMedicine.StockUpUtility");
            if (utility == null)
                return; // Smart Medicine not loaded
            // The extension method is a plain static method taking (Pawn, ThingDef) — pick that overload
            // (a (Pawn, Thing) overload also exists).
            stockingUpOn = AccessTools.Method(utility, "StockingUpOn", new[] { typeof(Pawn), typeof(ThingDef) });
            active = stockingUpOn != null;
            if (active)
                HDLog.Msg("Smart Medicine detected — stocked-up medicine is excluded from surplus unloading.");
            else
                HDLog.Warn("Smart Medicine present but StockUpUtility.StockingUpOn(Pawn, ThingDef) "
                           + "did not resolve; stocked medicine is NOT specially protected (turn off 'unload foreign surplus' if it loops).");
        }

        /// <summary>True if the pawn is configured to stock up on this def (so the whole carried stack is kept).</summary>
        public static bool IsStockedUp(Pawn pawn, ThingDef def)
        {
            if (!IsActive || pawn == null || def == null)
                return false;
            // No try/catch: SM present + stockingUpOn resolved (checked above) — surface a real fault rather than
            // silently fail-open (which would re-expose the medicine unload loop).
            return (bool)stockingUpOn.Invoke(null, new object[] { pawn, def });
        }
    }
}
