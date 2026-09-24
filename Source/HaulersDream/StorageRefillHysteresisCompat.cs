using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The provider gates vanilla's per-group search, not ordinary IsGoodStoreCell calls.
    /// HD's own destination searches must ask its controller before selecting a group.
    /// This is a new-refill gate, never a deposit/count limit: the provider intentionally
    /// lets in-flight deliveries finish and owns its linked-group usage, cache and latch.
    /// </summary>
    internal static class StorageRefillHysteresisCompat
    {
        internal static readonly bool IsPresent = ModLister.GetActiveModWithIdentifier(
            "PureMJ.MjRimMods.StorageRefillHysteresis", ignorePostfix: true) != null;
        private static readonly MethodInfo getHysteresis;
        private static readonly MethodInfo allowsRefill;
        [ThreadStatic] private static object[] settingsArgument;

        static StorageRefillHysteresisCompat()
        {
            if (!IsPresent) return;
            var manager = AccessTools.TypeByName("MjRimMods.StorageRefillHysteresis.HysteresisManager");
            var controller = AccessTools.TypeByName("MjRimMods.StorageRefillHysteresis.Hysteresis");
            getHysteresis = manager?.GetMethod("GetHysteresis", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(StorageSettings) }, null);
            allowsRefill = controller?.GetMethod("AllowsRefill", BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (controller == null || getHysteresis?.ReturnType != controller || allowsRefill?.ReturnType != typeof(bool))
            {
                getHysteresis = null;
                allowsRefill = null;
                HDLog.Warn("Storage Refill Hysteresis API could not be bound. HD stack refinement and midway "
                    + "storage shortcuts are disabled; ordinary native storage selection remains available.");
            }
        }

        internal static bool AllowsRefill(ISlotGroup group)
        {
            if (!IsPresent) return true;
            if (getHysteresis == null || group?.Settings == null) return false;
            // Reuse only the invocation buffer, never the controller's verdict or owner.
            // A manual toggle can change the answer within one paused game tick.
            var args = settingsArgument ?? (settingsArgument = new object[1]);
            object controller;
            try
            {
                args[0] = group.Settings;
                controller = getHysteresis.Invoke(null, args);
            }
            finally { args[0] = null; }
            return controller == null || (bool)allowsRefill.Invoke(controller, null);
        }
    }
}
