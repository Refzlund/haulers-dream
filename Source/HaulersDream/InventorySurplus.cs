using HaulersDream.Core;
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
            var def = thing.def;
            bool hdSwept = pawn.GetComp<CompHauledToInventory>()?.PeekHashSet().Contains(thing) == true;

            // An explicit per-item rule (mod options -> Individual Item Unload Settings) OVERRIDES both HD's
            // auto-detected keep-mods and the global keep-stock for that def. Keyed by defName, so it is
            // fallback-safe (a missing-mod rule simply never matches). This is the single shared choke point that
            // the unload driver, the "has surplus" gizmo check, and the cannot-unload alert all read.
            var settings = HaulersDreamMod.Settings;
            if (settings != null && settings.TryGetItemRule(def, out var rule))
            {
                switch (rule.mode)
                {
                    case ItemUnloadMode.UnloadAlways:
                        // Force the whole stack to be surplus, even units SS/SM/DBH/CE/addiction would keep.
                        return thing.stackCount;
                    case ItemUnloadMode.KeepAll:
                        // Keep the whole stack as personal kit — UNLESS HD itself swept it, in which case it must
                        // stay unloadable or it becomes a black hole (HD put it there, and the alert also skips
                        // kept items). A swept stack falls through to the ordinary keep-count path below.
                        if (!hdSwept)
                            return 0;
                        break;
                    case ItemUnloadMode.KeepAtMost:
                        // Carry at most N units of the def across the whole inventory; unload the excess. Applies
                        // even to swept stacks — it only ever pins up to N units, so it is bounded (no black hole).
                        int keepN = rule.amount < 0 ? 0 : rule.amount;
                        int haveN = YieldRouter.InventoryCountOfDef(pawn.inventory.innerContainer, def);
                        int over = haveN - keepN;
                        return over <= 0 ? 0 : System.Math.Min(thing.stackCount, over);
                }
            }
            else if (IsManagedKeepItem(pawn, thing, hdSwept))
            {
                // No explicit rule: auto-detected personal kit another system manages (Simple Sidearms carried
                // weapons, Smart Medicine stock-up, Dub's Bad Hygiene water, Combat Extended ammo, or a vanilla
                // addiction/chemical-dependency drug). Keep the WHOLE stack so adoption never tags them (severing
                // the unload<->refetch loop those mods drive) and the unload driver / alert never act on them.
                return 0;
            }

            int keep = KeepCountOf(pawn, def) + FoodKeepCountOf(pawn, thing);
            if (keep <= 0)
                return thing.stackCount;
            int surplus = YieldRouter.InventoryCountOfDef(pawn.inventory.innerContainer, def) - keep;
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

        /// <summary>True if the pawn holds any stack whose def has an explicit surplus-producing rule
        /// (keep-at-most / always-unload) AND is actually over that rule's keep — i.e. the stock a forced unload
        /// would adopt + move when the global "unload all surplus" toggle is OFF. Mirrors the toggle-off branch of
        /// <see cref="PawnUnloadChecker.AdoptSurplusInventory"/> so the gizmo's visibility matches what the button
        /// does. Read-only (no tagging) — safe on the render/gizmo path.</summary>
        public static bool HasAnyRuledSurplus(Pawn pawn)
        {
            var inner = pawn?.inventory?.innerContainer;
            var settings = HaulersDreamMod.Settings;
            if (inner == null || settings == null)
                return false;
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t != null && !t.Destroyed && settings.RuleProducesSurplus(t.def) && SurplusOf(pawn, t) > 0)
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

        /// <summary>
        /// True if this inventory stack is personal kit another system (mod or vanilla) actively keeps in the
        /// pawn's inventory, so the unload must leave the WHOLE stack alone. Each mod check is reflection-based
        /// and fail-open (mod absent → false), so this compiles and runs with any subset of the mods installed.
        ///
        /// <list type="bullet">
        /// <item>Vanilla addiction / chemical dependency: a drug the pawn is addicted to or chem-dependent on,
        /// matching <c>JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory</c> (the policy <c>takeToInventory</c>
        /// and inventoryStock cases are already covered, count-wise, by <see cref="KeepCountOf"/>).</item>
        /// <item>Simple Sidearms: a carried/remembered sidearm (<see cref="SimpleSidearmsCompat.IsKeptWeapon"/>).</item>
        /// <item>Smart Medicine: a stocked-up medicine/drug (<see cref="SmartMedicineCompat.IsStockedUp"/>).</item>
        /// <item>Dub's Bad Hygiene: carried water (<see cref="DbhCompat.IsKeptDrink"/>).</item>
        /// </list>
        /// </summary>
        internal static bool IsManagedKeepItem(Pawn pawn, Thing thing, bool hdSwept)
        {
            var def = thing.def;
            // The "keep the whole stack" branches apply ONLY to a stack the pawn holds as its OWN kit, i.e. NOT one
            // HD scooped/swept. An HD-tagged stack must ALWAYS stay unloadable, or it becomes a silent black hole:
            // HD put it there and would then refuse to take it out, and the cannot-unload alert (which also keys
            // off SurplusOf) would skip it too. The nearby-sweep (default on) can scoop loose medicine/water of a
            // stocked def off the ground, so without this an HD-swept stack of a stocked-medicine / carried-water /
            // addictive-drug def would be pinned in the pack forever. A genuine sidearm / stock-up / carried water /
            // addiction stash is never HD-tagged, so it is still kept and the unload<->refetch loop stays severed.
            if (!hdSwept)
            {
                // Vanilla parity gap: FirstUnloadableThing (HD's count-keep model) does not consult the addiction /
                // chemical-dependency case that JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory does. Keep
                // the whole stack for an addicted / chem-dependent pawn (flesh only; AddictionUtility is
                // meaningless for mechs). NOT the policy/schedule cases — those are KeepCountOf's count-based job.
                if (def.IsDrug && pawn.RaceProps != null && pawn.RaceProps.IsFlesh
                    && (AddictionUtility.IsAddicted(pawn, thing) || AddictionUtility.HasChemicalDependency(pawn, thing)))
                    return true;
                if (SmartMedicineCompat.IsStockedUp(pawn, def))
                    return true;
                if (DbhCompat.IsKeptDrink(thing))
                    return true;
                // Combat Extended ammo: CE keeps a pawn's loadout ammo and re-fetches it if removed (the reported
                // back-and-forth "drop ammo / pick it back up" loop). Defer ammo management entirely to CE.
                if (CECompat.IsCarriedAmmo(thing))
                    return true;
            }
            // Simple Sidearms carried weapon — IsKeptWeapon applies the SAME HD-swept exclusion internally
            // (SimpleSidearmsCompat.cs), so an HD-swept loose weapon stays unloadable.
            if (SimpleSidearmsCompat.IsKeptWeapon(pawn, thing))
                return true;
            return false;
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
