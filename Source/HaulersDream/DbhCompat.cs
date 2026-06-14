using System;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Dub's Bad Hygiene compatibility bridge — REFLECTION ONLY, no hard assembly reference. Verified against the
    /// DBH defs (Dubwise56/Dubs-Bad-Hygiene): carried drinks (e.g. <c>DBH_WaterBottle</c>, and items the More
    /// Drinkables addon tags) carry the <c>DubsBadHygiene.WaterExt</c> DefModExtension. Only that type is
    /// reflected; <c>Def.modExtensions</c> is a public vanilla list.
    ///
    /// Why this exists: HD's "unload all surplus" would otherwise ship a pawn's carried water to storage, and
    /// DBH's drink-from-pack need re-fetches it — an unload↔pickup LOOP. <see cref="IsKeptDrink"/> reports a
    /// WaterExt item as keep-stock so <see cref="InventorySurplus.SurplusOf"/> returns 0 and it is never
    /// adopted/unloaded (mirrors DBH's own drop-protection).
    /// </summary>
    public static class DbhCompat
    {
        private static bool initialized;
        private static bool active;
        private static Type waterExtType; // DubsBadHygiene.WaterExt (a DefModExtension)

        /// <summary>Whether Dub's Bad Hygiene is loaded (its WaterExt type resolves). Cached.</summary>
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
            // No try/catch: DBH-ABSENT is the TypeByName == null precondition. Lazy, once.
            waterExtType = AccessTools.TypeByName("DubsBadHygiene.WaterExt");
            active = waterExtType != null;
            if (active)
                Log.Message("[Hauler's Dream] Dub's Bad Hygiene detected — carried water is excluded from surplus unloading.");
        }

        /// <summary>True if this item is a DBH-managed carried drink (has the WaterExt mod extension).</summary>
        public static bool IsKeptDrink(Thing thing)
        {
            if (!IsActive || thing?.def == null)
                return false;
            var exts = thing.def.modExtensions;
            if (exts == null)
                return false;
            for (int i = 0; i < exts.Count; i++)
                if (exts[i] != null && waterExtType.IsInstanceOfType(exts[i]))
                    return true;
            return false;
        }
    }
}
