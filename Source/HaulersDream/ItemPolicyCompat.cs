using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "Item Policy" (Steam: per-pawn inventory-stock policy) compatibility bridge — REFLECTION ONLY, no hard
    /// assembly reference. Always-on when Item Policy is present (no setting), like the other keeper shims.
    ///
    /// WHY: Item Policy gives each pawn a per-pawn "keep N of these defs in inventory" policy and re-fetches the
    /// shortfall via its own <c>_ItemPolicy.JobGiver_TakeItemForInventoryStock</c>. HD's surplus-unload would strip
    /// those items back out as "surplus", and Item Policy would immediately re-fetch them — an unload↔refetch loop.
    ///
    /// HOW: feed Item Policy's per-pawn keep-count into HD's EXISTING count-aware keep model
    /// (<see cref="InventorySurplus.KeepCountOf"/>) so HD keeps N and only unloads the genuine surplus, exactly like
    /// the vanilla drug-policy / inventory-stock and CE-loadout keep counts already summed there.
    ///
    /// Item Policy's count lives in the static <c>_ItemPolicy.ItemPolicyUtility.policies</c> dictionary
    /// (<c>Dictionary&lt;Pawn, ItemPolicy&gt;</c>, SCRIBED), queried via the static
    /// <c>GetItemPolicyEntry(Pawn, ThingDef) -&gt; int</c>. Resolution is fail-open: if any required member is
    /// missing, <see cref="IsActive"/> stays false and HD behaves exactly as without Item Policy. Mirrors the
    /// reflection-soft-dep style of <see cref="CECompat"/> / <see cref="StorageNetworkCompat"/>.
    /// </summary>
    public static class ItemPolicyCompat
    {
        private static bool initialized;
        private static bool active;
        // _ItemPolicy.ItemPolicyUtility.policies : static Dictionary<Pawn, ItemPolicy> (SCRIBED).
        private static FieldInfo policiesField;
        // _ItemPolicy.ItemPolicyUtility.GetItemPolicyEntry(Pawn, ThingDef) -> int (static).
        private static MethodInfo getEntryMethod;

        /// <summary>Whether Item Policy is loaded and its policy field + keep-count method resolved. Cached.</summary>
        public static bool IsActive
        {
            get { if (!initialized) Init(); return active; }
        }

        private static void Init()
        {
            initialized = true;
            // TypeByName returns null (never throws) when Item Policy isn't loaded — that is the real precondition.
            var utilityType = AccessTools.TypeByName("_ItemPolicy.ItemPolicyUtility");
            if (utilityType == null)
                return; // Item Policy not loaded — HD's keep count is unchanged.
            policiesField = AccessTools.Field(utilityType, "policies");
            getEntryMethod = AccessTools.Method(utilityType, "GetItemPolicyEntry", new[] { typeof(Pawn), typeof(ThingDef) });
            active = policiesField != null && getEntryMethod != null;
            if (active)
                Log.Message("[Hauler's Dream] Item Policy detected — its per-pawn inventory-stock counts are kept "
                            + "during surplus unload (so HD won't fight its re-fetch).");
            else
                HDLog.Warn("Item Policy present but its policy API did not resolve (a version/rename?); its kept "
                           + "inventory items may be unloaded as surplus. Vanilla keep counts still work.");
        }

        /// <summary>
        /// The count of <paramref name="def"/> this pawn keeps in inventory under its Item Policy. Returns 0 when
        /// Item Policy is absent/inactive or the pawn has NO policy yet.
        ///
        /// CRITICAL GUARD: <c>GetItemPolicyEntry</c> → <c>GetPawnPolicy</c> AUTO-INSERTS an empty policy for any
        /// unseen pawn, and <c>policies</c> is SCRIBED — calling it for a policy-less pawn would bloat Item Policy's
        /// save file. So we only invoke it when the live <c>policies</c> dictionary ALREADY contains this pawn.
        /// </summary>
        public static int KeepCount(Pawn pawn, ThingDef def)
        {
            if (!IsActive || pawn == null || def == null)
                return 0;
            // Read-only: do not touch GetItemPolicyEntry unless the pawn already has a policy (it auto-inserts one).
            if (!(policiesField.GetValue(null) is IDictionary policies) || !policies.Contains(pawn))
                return 0;
            return Math.Max(0, (int)getEntryMethod.Invoke(null, new object[] { pawn, def }));
        }
    }
}
