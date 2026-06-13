using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Shared "what in a pawn's inventory is SURPLUS the unload should move" math, used by BOTH the unload
    /// driver and the cannot-unload alert so the two agree EXACTLY (alert-says-no-destination must mean
    /// driver-genuinely-cannot-place-it; alert-says-stuck must mean there is real surplus to move).
    ///
    /// "Keep" = the pawn's personal kit the unload must never strip — the three vanilla
    /// Pawn_InventoryTracker.FirstUnloadableThing sources (drug-policy takeToInventory, inventoryStock,
    /// packable food) plus the CE loadout. Surplus = units of a stack above that keep, clamped to the stack.
    /// </summary>
    public static class InventorySurplus
    {
        /// <summary>Units of this stack that are genuinely surplus (above the pawn's keep for the def). 0 = all
        /// personal kit. Mirrors the old JobDriver_UnloadHauledInventory.UnloadableCountOf.</summary>
        public static int SurplusOf(Pawn pawn, Thing thing)
        {
            if (pawn?.inventory?.innerContainer == null || thing?.def == null)
                return 0;
            int keep = KeepCountOf(pawn, thing.def) + FoodKeepCountOf(pawn, thing);
            if (keep <= 0)
                return thing.stackCount;
            int surplus = YieldRouter.InventoryCountOfDef(pawn.inventory.innerContainer, thing.def) - keep;
            return System.Math.Min(thing.stackCount, surplus);
        }

        /// <summary>True if the pawn holds ANY inventory stack with surplus above its keep-stock — i.e. the
        /// "unload all surplus" option would have something to put away (tag-independent: counts foreign stock
        /// HD never scooped). Read-only — safe on the render/gizmo path (no tagging, no Rand, no CE notify).</summary>
        public static bool HasAnySurplus(Pawn pawn)
        {
            var inner = pawn?.inventory?.innerContainer;
            if (inner == null)
                return false;
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t != null && !t.Destroyed && SurplusOf(pawn, t) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Can the unload place this anywhere — a real stockpile/container, OR (failing that) a
        /// desperate home-area cell (exactly the two destinations JobDriver_UnloadHauledInventory tries)?
        /// Wrapped in Rand.PushState/PopState so it is safe to call from the per-frame alert/render path: both
        /// StoreUtility probes consume the global Rand stream, which would otherwise desync seeded RNG
        /// (multiplayer) and flicker the result between alert recalculations.</summary>
        public static bool HasUnloadDestination(Pawn pawn, Thing thing)
        {
            if (pawn?.Map == null || thing == null)
                return false;
            Rand.PushState();
            try
            {
                return StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map, StoragePriority.Unstored,
                           pawn.Faction, out _, out _)
                       || StoreUtility.TryFindStoreCellNearColonyDesperate(thing, pawn, out _);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>Vanilla parity: the count of this def the pawn wants to KEEP in inventory — drug policy
        /// entries with takeToInventory &gt; 0 plus inventoryStock entries (two of the three tmpItemsToKeep
        /// sources in Pawn_InventoryTracker.FirstUnloadableThing; the third, packable food, is per-stack
        /// nutrition math — see <see cref="FoodKeepCountOf"/>), plus the CE loadout reserve.</summary>
        public static int KeepCountOf(Pawn pawn, ThingDef def)
        {
            int keep = 0;
            var policy = pawn.drugs?.CurrentPolicy;
            if (policy != null)
                for (int i = 0; i < policy.Count; i++)
                    if (policy[i].drug == def && policy[i].takeToInventory > 0)
                        keep += policy[i].takeToInventory;
            var stockEntries = pawn.inventoryStock?.stockEntries;
            if (stockEntries != null)
                foreach (var entry in stockEntries.Values)
                    if (entry != null && entry.thingDef == def)
                        keep += entry.count;
            // Under CE the pawn's assigned loadout (ammo/sidearm reserve) is personal stock too — keep it.
            keep += CECompat.LoadoutKeepCount(pawn, def);
            return keep;
        }

        /// <summary>
        /// Vanilla parity, the THIRD tmpItemsToKeep source in Pawn_InventoryTracker.FirstUnloadableThing: a
        /// colonist keeps packable food up to its food need's MaxLevel of nutrition (JobGiver_PackFood), so the
        /// unload must not strip a packed lunch a harvested yield merged into. Mirrors vanilla's math: keep =
        /// stackCount − k, k = the fewest units whose removal brings the pawn's total packable nutrition within
        /// MaxLevel; 0 when the whole stack is surplus.
        /// </summary>
        public static int FoodKeepCountOf(Pawn pawn, Thing thing)
        {
            if (!pawn.IsColonist || pawn.needs?.food == null)
                return 0;
            var def = thing.def;
            if (!def.IsNutritionGivingIngestible || def.IsDrug
                || !JobGiver_PackFood.IsGoodPackableFoodFor(thing, pawn, checkMass: false))
                return 0;
            float total = JobGiver_PackFood.GetInventoryPackableFoodNutrition(pawn);
            float maxLevel = pawn.needs.food.MaxLevel;
            float perUnit = thing.GetStatValue(StatDefOf.Nutrition);
            if (perUnit <= 0f || total - perUnit * thing.stackCount > maxLevel)
                return 0; // even without this entire stack the pawn is over its cap — all surplus
            int k = 0;
            while (total - perUnit * k > maxLevel)
                k++;
            return thing.stackCount - k;
        }
    }
}
