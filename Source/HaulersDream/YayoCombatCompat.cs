using System;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Yayo's Combat 3 compatibility — DEF-BASED detection, no hard assembly reference, so Hauler's Dream runs
    /// identically with or without YC3 installed. YC3 adds a consumable-ammo system: a pawn carries ammo Things
    /// in its inventory and YC3 re-stocks / reloads from them. If HD shipped that carried ammo off to storage it
    /// would fight YC3 (the pawn re-fetches it), and a caravan-return pawn could get stuck in HD's "unloading
    /// inventory" job churning on the ammo (the reported bug). So HD treats a pawn's own YC3 ammo as personal kit
    /// it never unloads — exactly the policy already applied to Combat Extended ammo (<see cref="CECompat.IsCarriedAmmo"/>).
    ///
    /// Detected by YC3's DATA, not its packageId: there are several YC3 forks (Mlie / emipa606 / the original)
    /// that all ship the same defs — the shared <c>ThingCategoryDef</c> "yy_ammo_category" and the "yy_ammo"
    /// defName prefix YC3's ammo defs use. A pure DefDatabase lookup means no YC3 assembly reference is needed and
    /// every fork is covered; when YC3 is absent the category resolves to null and no def carries the prefix, so
    /// it is inert (fail-open, like the other compat bridges).
    /// </summary>
    public static class YayoCombatCompat
    {
        private static bool initialized;
        private static ThingCategoryDef ammoCategory;

        private static void Init()
        {
            initialized = true;
            // GetNamedSilentFail returns null when YC3 isn't loaded — the real precondition, no try/catch needed.
            ammoCategory = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("yy_ammo_category");
            if (ammoCategory != null)
                Log.Message("[Hauler's Dream] Yayo's Combat 3 detected — carried ammo excluded from surplus unloading.");
        }

        /// <summary>
        /// True if this item is Yayo's Combat 3 ammo. YC3 keeps a pawn's ammo in inventory and re-fetches anything
        /// taken out, so HD's surplus unload must leave carried ammo alone or the pawn fights YC3 — including a
        /// caravan-return pawn stalling in the unload job (the reported freeze). Keeps ALL carried YC3 ammo (YC3's
        /// own system manages the right amount); an HD-swept LOOSE ammo stack is still unloadable because the
        /// caller (<see cref="InventorySurplus.IsManagedKeepItem"/>) excludes HD-tagged stacks. Matched by def
        /// (category membership, or the "yy_ammo" defName prefix as a fork-proof fallback), so it works with no
        /// YC3 assembly reference and a player can still force-unload it via a per-item "always unload" rule.
        /// </summary>
        public static bool IsCarriedAmmo(Thing thing)
        {
            var def = thing?.def;
            if (def == null)
                return false;
            if (!initialized)
                Init();
            if (ammoCategory != null && def.thingCategories != null && def.thingCategories.Contains(ammoCategory))
                return true;
            return def.defName != null && def.defName.StartsWith("yy_ammo", StringComparison.Ordinal);
        }
    }
}
