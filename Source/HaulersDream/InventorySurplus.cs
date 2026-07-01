using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

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
        public static int SurplusOf(Pawn pawn, Thing thing) => SurplusOf(pawn, thing, null, null);

        /// <summary>
        /// Hoisted form of <see cref="SurplusOf(Pawn,Thing)"/> for a caller that scans every stack of one pawn in a
        /// loop (the "has any surplus" gizmo/alert pass): pass the pawn's <see cref="CompHauledToInventory"/> ONCE
        /// (instead of a per-stack <c>GetComp</c>) and a per-def inventory-count scratch dict ONCE (instead of the
        /// per-stack full-inventory <see cref="YieldRouter.InventoryCountOfDef"/> walk that made the pass O(n²)).
        /// Pass <paramref name="comp"/> = null to look the comp up here, and <paramref name="invCountByDef"/> = null
        /// to fall back to the per-call inventory walk — so the public 2-arg overload is behaviour-identical.
        /// </summary>
        internal static int SurplusOf(Pawn pawn, Thing thing, CompHauledToInventory comp, Dictionary<ThingDef, int> invCountByDef)
        {
            if (pawn?.inventory?.innerContainer == null || thing?.def == null)
                return 0;
            var def = thing.def;
            // comp may be passed in (hoisted) or looked up; either way PeekHashSet is read-only (no self-heal) so
            // this stays safe on the render/alert path.
            if (comp == null)
                comp = pawn.GetComp<CompHauledToInventory>();
            bool hdSwept = comp?.PeekHashSet().Contains(thing) == true;

            // Player "keep in inventory" (the "Keep X in inventory" order): this exact stack is HELD, so it is never
            // surplus and the unload never hauls it away. Checked before the per-item rules / keep-mods so an explicit
            // keep wins even over an UnloadAlways rule — the player deliberately kept THIS stack. PeekKept is a
            // side-effect-free read (a stale ref never matches a live inventory thing), safe on the alert/render path.
            if (comp != null && comp.PeekKept().Contains(thing))
                return 0;

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
                        int haveN = InventoryCountOfDef(pawn, def, invCountByDef);
                        int over = haveN - keepN;
                        return over <= 0 ? 0 : System.Math.Min(thing.stackCount, over);
                }
            }
            else if (SimpleSidearmsCompat.IsActive
                     && (def.IsRangedWeapon || def.IsMeleeWeapon)
                     && SimpleSidearmsCompat.MemoryApiOk)
            {
                // Simple Sidearms: keep exactly as many of this (def, stuff) as the pawn wants in INVENTORY and
                // treat every EXTRA copy as surplus — so a HAULED duplicate weapon (same def+stuff as a kept
                // sidearm) is unloaded while the wanted sidearm itself is kept. Per-(def,stuff), not per-def, so a
                // steel-ikwa sidearm + a hauled plasteel ikwa keeps the steel and unloads the plasteel. Weapons are
                // stackLimit 1, so each Thing is 0 or 1 of the count.
                //
                // pairKeep uses InventoryKeepCount (remembered MINUS the equipped primary), NOT raw RememberedCount:
                // SS records the equipped primary in rememberedWeapons, but the primary lives in equipment (not
                // innerContainer, which is what pairHave counts). Counting it in the keep but not the have made a
                // hauled weapon matching the equipped primary's (def,stuff) read over = 1 - 1 = 0 and never unload —
                // the reported "won't put away / re-stows" bug. (memoryApiOk==false is handled by the
                // IsManagedKeepItem fallback below, not here, so we never compute have - 0 and strip a weapon kit.)
                int pairKeep = SimpleSidearmsCompat.InventoryKeepCount(pawn, def, thing.Stuff);
                int pairHave = YieldRouter.InventoryCountOfPair(pawn.inventory.innerContainer, def, thing.Stuff);
                int over = pairHave - pairKeep;
                int pairSurplus = over <= 0 ? 0 : System.Math.Min(thing.stackCount, over);
                // Diagnostic (gated so the string/equipment read never runs unless verbose logging is on — SurplusOf
                // is a hot path read by the unload driver, the gizmo, and the alert).
                if (settings != null && settings.verboseLogging)
                    HDLog.Dbg($"SurplusOf weapon {def.defName} (stuff={thing.Stuff?.defName ?? "none"}) for {pawn.LabelShort}: "
                              + $"have={pairHave} keep={pairKeep} "
                              + $"(remembered={SimpleSidearmsCompat.RememberedCount(pawn, def, thing.Stuff)}, "
                              + $"primaryMatch={pawn.equipment?.Primary?.def == def && pawn.equipment?.Primary?.Stuff == thing.Stuff}) "
                              + $"-> surplus={pairSurplus}");
                return pairSurplus;
            }
            else if (IsManagedKeepItem(pawn, thing, hdSwept))
            {
                // No explicit rule: auto-detected personal kit another system manages (Simple Sidearms carried
                // weapons via the count-aware branch above when its API resolved — else the keep-all fallback here;
                // Smart Medicine stock-up, Dub's Bad Hygiene water, Combat Extended ammo, or a vanilla
                // addiction/chemical-dependency drug). Keep the WHOLE stack so adoption never tags them (severing
                // the unload<->refetch loop those mods drive) and the unload driver / alert never act on them.
                return 0;
            }

            int keep = KeepCountOf(pawn, def) + FoodKeepCountOf(pawn, thing);
            if (keep <= 0)
                return thing.stackCount;
            int surplus = InventoryCountOfDef(pawn, def, invCountByDef) - keep;
            return System.Math.Min(thing.stackCount, surplus);
        }

        /// <summary>Total units of <paramref name="def"/> in the pawn's inventory — served from the hoisted
        /// per-def scratch dict when present (one pass, shared across every stack of the same def in a
        /// "has any surplus" scan), else the per-call full-inventory walk. Behaviour-identical either way.</summary>
        private static int InventoryCountOfDef(Pawn pawn, ThingDef def, Dictionary<ThingDef, int> invCountByDef)
        {
            if (invCountByDef != null)
                return invCountByDef.TryGetValue(def, out int c) ? c : 0;
            return YieldRouter.InventoryCountOfDef(pawn.inventory.innerContainer, def);
        }

        // Reused scratch for the per-def inventory-count precompute in the HasAny* scans, so the (per-frame, via
        // the cache) pass allocates nothing. [ThreadStatic] to match this assembly's hook-reachable scratch
        // convention (CompHauledToInventory's tmpScoopedDefs, PawnMassCache's per-thread memo).
        [System.ThreadStatic] private static Dictionary<ThingDef, int> tmpInvCountByDef;

        /// <summary>True if the pawn holds ANY inventory stack with surplus above its keep-stock — i.e. the
        /// "unload all surplus" option would have something to put away (tag-independent: counts foreign stock
        /// HD never scooped). Read-only — safe on the render/gizmo path (no tagging, no Rand, no CE notify).
        ///
        /// Hoists the <see cref="CompHauledToInventory"/> lookup and the per-def inventory counts OUT of the
        /// per-stack <see cref="SurplusOf(Pawn,Thing)"/> so the pass is O(n) instead of O(n²) (the inner
        /// <c>SurplusOf</c> otherwise re-walked the whole inventory to count each def, once per stack).</summary>
        public static bool HasAnySurplus(Pawn pawn)
        {
            var inner = pawn?.inventory?.innerContainer;
            if (inner == null)
                return false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            var counts = BuildInvCountByDef(inner);
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t != null && !t.Destroyed && SurplusOf(pawn, t, comp, counts) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>True if the pawn holds any stack whose def has an explicit surplus-producing rule
        /// (keep-at-most / always-unload) AND is actually over that rule's keep — i.e. the stock a forced unload
        /// would adopt + move when the global "unload all surplus" toggle is OFF. Mirrors the toggle-off branch of
        /// <see cref="PawnUnloadChecker.AdoptSurplusInventory"/> so the gizmo's visibility matches what the button
        /// does. Read-only (no tagging) — safe on the render/gizmo path. Hoists comp + per-def counts like
        /// <see cref="HasAnySurplus"/> (O(n), not O(n²)).</summary>
        public static bool HasAnyRuledSurplus(Pawn pawn)
        {
            var inner = pawn?.inventory?.innerContainer;
            var settings = HaulersDreamMod.Settings;
            if (inner == null || settings == null)
                return false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            var counts = BuildInvCountByDef(inner);
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t != null && !t.Destroyed && settings.RuleProducesSurplus(t.def) && SurplusOf(pawn, t, comp, counts) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>Fill (and return) the reused <see cref="tmpInvCountByDef"/> scratch with total units per def
        /// across the owner's stacks — one O(n) pass, so the surplus scan can answer "how many of this def?" with
        /// a dict lookup instead of re-walking the inventory per stack.</summary>
        private static Dictionary<ThingDef, int> BuildInvCountByDef(ThingOwner inner)
        {
            var counts = tmpInvCountByDef ?? (tmpInvCountByDef = new Dictionary<ThingDef, int>());
            counts.Clear();
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t?.def == null)
                    continue;
                counts.TryGetValue(t.def, out int c);
                counts[t.def] = c + t.stackCount;
            }
            return counts;
        }

        /// <summary>Can the unload place this anywhere — a real stockpile/container, OR (failing that) a
        /// desperate home-area floor cell? Mirrors the driver's real storage probe
        /// (<see cref="StoreUtility.TryFindBestBetterStorageFor"/>) plus a SAFE re-implementation of vanilla's
        /// home-area radial-cell fallback (see <see cref="HasDesperateHomeAreaCell"/>). It deliberately does NOT
        /// call <c>StoreUtility.TryFindStoreCellNearColonyDesperate</c>, whose final
        /// <c>RCellFinder.TryFindRandomSpotJustOutsideColony</c> leg throws a vanilla NullReferenceException for a
        /// degenerate single-pawn colony with the pawn far from home (issue #76); since this method is re-evaluated
        /// ~once/second from the cannot-unload alert, that caught-NRE's Mono stack capture WAS the periodic hitch.
        /// Wrapped in Rand.PushState/PopState so it is safe to call from the per-frame alert/render path: the
        /// probes consume the global Rand stream, which would otherwise desync seeded RNG (multiplayer) and flicker
        /// the result between alert recalculations.</summary>
        public static bool HasUnloadDestination(Pawn pawn, Thing thing)
        {
            if (pawn?.Map == null || thing == null)
                return false;
            Rand.PushState();
            try
            {
                // "Does this carried item have anywhere to be stored?" must be answered ALLOW-ALL, even if an
                // en-route/before-carry path (which pushes Opportunistic/BeforeCarry) is on the call stack:
                // this is the UNLOAD destination probe (plan G4 — InventorySurplus.HasUnloadDestination ⇒ Unload).
                // If the building filter narrowed this query, it could wrongly report "no destination" and the
                // pawn would think it cannot unload (a black hole) — so push Unload to neutralize any inherited
                // context for the duration of the storage search.
                using (StorageBuildingFilter.PushContext(StorageFilterContext.Unload))
                {
                    return StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map, StoragePriority.Unstored,
                               pawn.Faction, out _, out _)
                           || HasDesperateHomeAreaCell(pawn, thing);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// SAFE re-implementation of ONLY the home-area radial-cell leg of vanilla
        /// <c>StoreUtility.TryFindStoreCellNearColonyDesperate</c> (RimWorld 1.6): scan the cells around the
        /// carrier, accepting the first reachable, in-home-area, non-slot-group cell that
        /// <see cref="StoreUtility.IsGoodStoreCell"/> approves. Identical loop bounds / order / predicates to
        /// vanilla so a "desperate" destination this reports is one the unload driver would actually use.
        ///
        /// It deliberately OMITS vanilla's final <c>RCellFinder.TryFindRandomSpotJustOutsideColony</c> leg: that
        /// inner <c>FinalValidator</c> dereferences <c>c.GetDistrict(map).Room</c> on random map cells, which is
        /// null for a degenerate single-pawn "Adventure Mode" colony with the pawn far outside the home area —
        /// throwing a vanilla NullReferenceException attributed (in release Mono) to
        /// <c>TryFindStoreCellNearColonyDesperate</c> (issue #76). Because this destination probe is a read-only
        /// "can it be put away?" question — NOT the driver actually carrying an item to a drop cell — the
        /// just-outside-colony random spot adds nothing here (a pawn far in the wilderness genuinely has no
        /// home-area destination, which is exactly the black-hole the alert should surface), so dropping that leg
        /// is behaviour-correct, not merely a workaround.
        ///
        /// Cannot throw for these inputs: the caller guarantees a non-null spawned carrier + map (the entry guard
        /// in <see cref="HasUnloadDestination"/>), every call below is non-throwing for a non-null carrier/map, and
        /// the <c>Rand.RangeInclusive</c> runs inside the caller's Rand.PushState/PopState scope.
        /// </summary>
        private static bool HasDesperateHomeAreaCell(Pawn carrier, Thing item)
        {
            var map = carrier.Map;
            for (int i = -4; i < 20; i++)
            {
                int num = (i < 0) ? Rand.RangeInclusive(0, 4) : i;
                IntVec3 cell = carrier.Position + GenRadial.RadialPattern[num];
                if (cell.InBounds(map)
                    && map.areaManager.Home[cell]
                    && carrier.CanReach(cell, PathEndMode.ClosestTouch, Danger.Deadly)
                    && cell.GetSlotGroup(map) == null
                    && StoreUtility.IsGoodStoreCell(cell, map, item, carrier, carrier.Faction))
                    return true;
            }
            return false;
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
        /// <item>Combat Extended / Yayo's Combat 3: carried ammo the mod re-fetches
        /// (<see cref="CECompat.IsCarriedAmmo"/> / <see cref="YayoCombatCompat.IsCarriedAmmo"/>).</item>
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
                // Yayo's Combat 3 ammo: YC3 likewise keeps a pawn's ammo in inventory and re-fetches it; shipping
                // it to storage fights YC3 and can stall the unload job on a caravan-return pawn (the reported
                // freeze). Defer YC3 ammo management entirely to YC3, exactly like CE ammo above.
                if (YayoCombatCompat.IsCarriedAmmo(thing))
                    return true;
            }
            // Simple Sidearms carried weapon: when the precise rememberedWeapons API resolved, SurplusOf handles
            // weapons via its count-aware (def,stuff) branch BEFORE reaching here, so this governs ONLY the
            // fallback (API unresolved, a fork/rename) — keep all non-HD-tagged colonist weapons. IsKeptWeapon
            // applies the same HD-swept exclusion internally, so a genuinely-swept loose weapon stays unloadable.
            if (!SimpleSidearmsCompat.MemoryApiOk && SimpleSidearmsCompat.IsKeptWeapon(pawn, thing))
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
            // Item Policy's per-pawn inventory-stock count: keep it too, or HD's unload fights its re-fetch loop.
            keep += ItemPolicyCompat.KeepCount(pawn, def);
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
            // Closed-form of the old k-loop (see FoodKeepMath.KeepCount), O(1) instead of O(stackCount): the
            // two early-outs (perUnit <= 0; over cap even without the whole stack) and the ceil(over/perUnit)
            // keep-count are all folded in, behaviour-identical for every input.
            return FoodKeepMath.KeepCount(total, maxLevel, perUnit, thing.stackCount);
        }
    }
}
