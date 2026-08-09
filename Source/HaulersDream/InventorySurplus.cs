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

            // Player "keep in inventory" (#197: the "Keep N in inventory" order slider + the Gear-tab keep button):
            // this pawn is pinned to hold up to N units of the def, so only what it holds ABOVE N is surplus. Checked
            // before the per-item rules / keep-mods so an explicit per-pawn keep wins even over an UnloadAlways rule —
            // the player deliberately kept this def on this pawn. KeptCountOf is a side-effect-free read (safe on the
            // alert/render path); the surplus arithmetic is the unit-tested Core policy. It can never black-hole: it
            // only pins the first N, so any HD-swept excess above N still unloads normally.
            if (comp != null)
            {
                int keptN = comp.KeptCountOf(def);
                if (keptN > 0)
                {
                    int heldN = InventoryCountOfDef(pawn, def, invCountByDef);
                    return KeepCountPolicy.SurplusForKeptDef(keptN, heldN, thing.stackCount);
                }
            }

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
            else if (!hdSwept && GrabYourToolCompat.IsCarriedTool(pawn, thing)
                     && !SimpleSidearmsCompat.IsRememberedSidearm(pawn, thing))
            {
                // Grab Your Tool carries this weapon-tool for the pawn's work jobs and swaps it in/out of the
                // equipment slot, keeping it on the pawn; if HD unloaded it to storage GYT would just re-fetch it
                // (an unload<->pickup loop). Keep the whole stack. Placed ABOVE the Simple Sidearms branch on
                // purpose: SS's (def,stuff) remembered-count reads 0 for a GYT tool SS never remembered, so the SS
                // branch would otherwise report full surplus and unload it (and the SS branch intercepts every
                // weapon first when SS is active). The !hdSwept guard keeps a genuinely HD-swept loose weapon
                // unloadable, so this can never create a black hole (same discipline as the SS keep). Inert when
                // GYT is absent (IsCarriedTool short-circuits on !IsActive), and an explicit per-def "Unload
                // always" rule still wins — it is handled by the TryGetItemRule branch above, before this.
                //
                // The !IsRememberedSidearm gate defers a tool SS ALSO precisely remembers to the SS branch below,
                // whose count-aware (def,stuff) keep unloads a HAULED DUPLICATE of that pair while keeping the one
                // wanted copy — so a GYT+SS user still gets SS's dedup. A GYT tool SS does NOT remember (or SS
                // absent — IsRememberedSidearm is false then) is kept here, which is the whole point of the fix.
                return 0;
            }
            else if (!hdSwept && SurvivalToolsCompat.IsCarriedTool(pawn, thing))
            {
                // Survival Tools keeps this pickaxe/axe/sickle in the pawn's inventory for its gated work jobs and
                // re-fetches it FROM STORAGE on its own schedule (its optimizer's acquisition policy accepts any
                // tool.IsInAnyStorage()), so unloading it just starts an unload<->pickup loop. Keep the whole stack.
                //
                // ABOVE the Simple Sidearms branch for the same reason the Grab Your Tool branch is: the tool defs
                // carry melee <tools>, so they read as IsMeleeWeapon and SS would otherwise intercept them first with
                // a remembered count of 0 — i.e. FULL surplus. The GYT branch cannot cover them either, because it
                // requires def.equippedStatOffsets and every survival-tool def leaves that empty (the work-stat
                // factors flow through the mod's own StatPart, not vanilla equipped offsets).
                //
                // Deliberately does NOT defer to a matching SS remembered weapon the way the GYT branch above does. A
                // survival tool CAN be equipped, so SS can remember one, and SS's count-aware keep would then treat
                // every copy above the remembered count as surplus — but Survival Tools carries SEVERAL tools on
                // purpose, up to the pawn's SurvivalToolCarryCapacity, and fetches each one back, so deferring would
                // re-open precisely the loop this branch severs. The mod trims its own excess (an idle drop over the
                // carry limit, plus the optimizer's dedup/downgrade drops), so nothing accumulates.
                //
                // The !hdSwept guard keeps a genuinely HD-swept loose tool unloadable, so this can never create a
                // black hole (same discipline as the SS and GYT keeps) — that is what makes the keep-ALL contract
                // safe here. Inert when the mod is absent (IsCarriedTool short-circuits on !IsActive), and an
                // explicit per-def "Unload always" rule still wins — it is handled by the TryGetItemRule branch
                // above, before this.
                return 0;
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
                // The keep is the remembered count MINUS the equipped primary, NOT raw RememberedCount: SS records
                // the equipped primary in rememberedWeapons, but the primary lives in equipment (not innerContainer,
                // which is what pairHave counts). Counting it in the keep but not the have made a hauled weapon
                // matching the equipped primary's (def,stuff) read over = 1 - 1 = 0 and never unload (the reported
                // "won't put away / re-stows" bug). That subtraction and the surplus clamp are the unit-tested Core
                // policy (SidearmKeepMath.SurplusForPair), so the shipped math IS the tested representation.
                // (memoryApiOk==false is handled by the IsManagedKeepItem fallback below, not here, so we never
                // compute have - 0 and strip a weapon kit.)
                int rememberedCount = SimpleSidearmsCompat.RememberedCount(pawn, def, thing.Stuff);
                var primary = pawn.equipment?.Primary;
                bool primaryMatchesPair = primary != null && primary.def == def && primary.Stuff == thing.Stuff;
                int pairHave = YieldRouter.InventoryCountOfPair(pawn.inventory.innerContainer, def, thing.Stuff);
                int pairSurplus = SidearmKeepMath.SurplusForPair(rememberedCount, primaryMatchesPair, pairHave, thing.stackCount);
                // Diagnostic (gated so the string/equipment read never runs unless verbose logging is on: SurplusOf
                // is a hot path read by the unload driver, the gizmo, and the alert). keep is re-derived through the
                // same InventoryKeepCount -> KeepForPair policy for display parity with the shipped surplus.
                if (settings != null && settings.verboseLogging)
                    HDLog.Dbg($"SurplusOf weapon {def.defName} (stuff={thing.Stuff?.defName ?? "none"}) for {pawn.LabelShort}: "
                              + $"have={pairHave} keep={SimpleSidearmsCompat.InventoryKeepCount(pawn, def, thing.Stuff)} "
                              + $"(remembered={rememberedCount}, primaryMatch={primaryMatchesPair}) "
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

            // The third keep term (animal-interaction food) deliberately does NOT carry the `!hdSwept` guard the
            // IsManagedKeepItem branch above uses, and that inversion is the whole fix. That guard exists because
            // those keeps are UNCONDITIONAL — keeping an HD-swept stack forever would black-hole it. This keep is
            // bounded by a JOB LIFETIME instead: it releases the moment no interaction job remains. And because
            // CompHauledToInventory's tag self-heal re-tags by DEF, the kibble in the reported case IS swept — so
            // a `!hdSwept` guard here would defeat the fix entirely rather than protect anything.
            //
            // Placement is the contract the sibling keeps follow: this sum sits BELOW the per-item-rule branch
            // above, so an explicit player "Unload always" rule still returns the whole stack as surplus before any
            // of these keeps is consulted.
            int keep = KeepCountOf(pawn, def) + FoodKeepCountOf(pawn, thing)
                       + AnimalInteractFoodKeepCountOf(pawn, thing);
            if (keep <= 0)
                return thing.stackCount;
            int surplus = InventoryCountOfDef(pawn, def, invCountByDef) - keep;
            return System.Math.Min(thing.stackCount, surplus);
        }

        /// <summary>
        /// The pawn's HD-tagged carried SURPLUS per def — units of each tagged inventory stack above the pawn's
        /// personal keep-stock (<see cref="SurplusOf(Pawn,Thing)"/>), summed across stacks of the same def. The
        /// only cargo a deposit-only opportunistic load can shed, and the amount the ledger records as that
        /// divert's incoming claim (#188). Read-only: iterates <see cref="CompHauledToInventory.PeekHashSet"/> (no
        /// self-heal / mutation — safe on the scan path) and counts only a tagged stack still physically in the
        /// pawn's inventory. Per-def integer SUMS with no ordering dependence, so the result is
        /// multiplayer-deterministic. The single choke point BOTH the opportunistic-divert scan and the ledger's
        /// carried-surplus claim read, so the claim and the scan use identical surplus math.
        /// </summary>
        /// <param name="pawn">The carrying pawn; null (or a pawn without inventory / the HD carry comp) → empty.</param>
        public static Dictionary<ThingDef, int> SurplusByDef(Pawn pawn)
        {
            var result = new Dictionary<ThingDef, int>();
            var inner = pawn?.inventory?.innerContainer;
            var comp = pawn?.GetComp<CompHauledToInventory>();
            if (inner == null || comp == null)
                return result;
            foreach (var t in comp.PeekHashSet())
            {
                if (t == null || t.Destroyed || t.def == null || !inner.Contains(t))
                    continue;
                int surplus = SurplusOf(pawn, t);
                if (surplus <= 0)
                    continue;
                result[t.def] = (result.TryGetValue(t.def, out int cur) ? cur : 0) + surplus;
            }
            return result;
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
        /// (<see cref="StoreUtility.TryFindBestBetterStorageFor"/>) plus the SAME home-area radial-cell fallback
        /// the driver itself now uses (<see cref="TryFindDesperateHomeAreaCell"/>), so the alert and the driver
        /// cannot disagree. Neither calls <c>StoreUtility.TryFindStoreCellNearColonyDesperate</c> — see that
        /// method for why its third leg is dropped for EVERYONE (issues #231 and #76).
        /// Wrapped in Rand.PushState/PopState so it is safe to call from the per-frame alert/render path: the
        /// probes consume the global Rand stream, which would otherwise desync seeded RNG (multiplayer) and flicker
        /// the result between alert recalculations. (This is the ALERT path's concern only — the driver calls
        /// <see cref="TryFindDesperateHomeAreaCell"/> directly, once per unload step, exactly as vanilla's own
        /// desperate search consumes the stream from a job.)</summary>
        public static bool HasUnloadDestination(Pawn pawn, Thing thing)
        {
            if (pawn?.Map == null || thing == null || thing.Destroyed || thing.def == null)
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
                           || TryFindDesperateHomeAreaCell(pawn, thing, out _);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// The ONE home-area "desperate" cell search Hauler's Dream uses — shared by the cannot-unload ALERT
        /// (via <see cref="HasUnloadDestination"/>) and by the unload DRIVER, so the alert can never claim a
        /// destination the driver would not actually use. A faithful re-implementation of ONLY the home-area
        /// radial-cell leg of vanilla <c>StoreUtility.TryFindStoreCellNearColonyDesperate</c> (RimWorld 1.6,
        /// <c>StoreUtility.cs:378-386</c>): scan the cells around the carrier, returning the first reachable,
        /// in-home-area, non-slot-group cell that <see cref="StoreUtility.IsGoodStoreCell"/> approves. Identical
        /// loop bounds / order / predicates to vanilla, including the leading random-index draws.
        ///
        /// It deliberately OMITS vanilla's final <c>RCellFinder.TryFindRandomSpotJustOutsideColony</c> leg
        /// (<c>StoreUtility.cs:388</c>) for BOTH callers, for two independent reasons:
        /// <list type="number">
        /// <item>It is NOT HOME-CONSTRAINED (issue #231). Its <c>FinalValidator</c> requires an OUTDOOR district
        /// that TOUCHES THE MAP EDGE, and its last pass rolls a random cell over the whole map
        /// (<c>RCellFinder.cs:773</c>) — the home area is never consulted. Vanilla only ever reaches it behind
        /// the rare event-driven <c>UnloadEverything</c> flag, once per job; this mod's unload runs per tagged
        /// stack, in a loop, for every hauling pawn, re-rolling a fresh random cell from each new position. That
        /// is exactly the reported "items placed completely outside the Home area".</item>
        /// <item>It THROWS for a degenerate colony (issue #76). That same <c>FinalValidator</c> dereferences
        /// <c>c.GetDistrict(map).Room</c> on random map cells (<c>RCellFinder.cs:783-791</c>), which is null for a
        /// single-pawn "Adventure Mode" colony with the pawn far outside the home area — a vanilla
        /// NullReferenceException attributed (in release Mono) to <c>TryFindStoreCellNearColonyDesperate</c>. On
        /// the alert path, re-evaluated ~once/second, that caught-NRE's Mono stack capture WAS the periodic hitch.</item>
        /// </list>
        /// When this finds nothing there is genuinely no home-area destination — which the driver answers with a
        /// home-preferring drop where the pawn stands (<see cref="InventoryDrop.TryDropPreferHome"/>) and the alert
        /// surfaces to the player.
        ///
        /// Cannot throw for these inputs: the callers guarantee a non-null spawned carrier + map, and every call
        /// below is non-throwing for a non-null carrier/map.
        /// </summary>
        /// <param name="carrier">The pawn holding the stack; the scan is centred on its position.</param>
        /// <param name="item">The stack to be placed — passed to <see cref="StoreUtility.IsGoodStoreCell"/> so a
        /// cell that could not actually accept it is rejected.</param>
        /// <param name="cell">The accepted home-area cell, or <see cref="IntVec3.Invalid"/> when none was found.</param>
        /// <returns>True when a usable home-area cell was found.</returns>
        internal static bool TryFindDesperateHomeAreaCell(Pawn carrier, Thing item, out IntVec3 cell)
        {
            var map = carrier.Map;
            // Vanilla parity, verbatim: the leading RandomLeadTries iterations draw a RANDOM radial index (so two
            // pawns unloading side by side don't always aim at the same cell), the rest walk outward in order.
            // The Rand draw MUST stay inside the ternary — hoisting it would consume the global Rand stream on
            // every iteration and desync multiplayer clients from vanilla's sequence.
            for (int i = -UnloadFallbackPolicy.RandomLeadTries; i < UnloadFallbackPolicy.RadialCellsToTry; i++)
            {
                int num = (i < 0) ? Rand.RangeInclusive(0, UnloadFallbackPolicy.RandomLeadTries) : i;
                IntVec3 candidate = carrier.Position + GenRadial.RadialPattern[num];
                if (candidate.InBounds(map)
                    && map.areaManager.Home[candidate]
                    && carrier.CanReach(candidate, PathEndMode.ClosestTouch, Danger.Deadly)
                    && candidate.GetSlotGroup(map) == null
                    && StoreUtility.IsGoodStoreCell(candidate, map, item, carrier, carrier.Faction))
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = IntVec3.Invalid;
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
            // Routed through the shared accessor (#232) rather than open-coded here — same integer-indexer walk,
            // same SUM over duplicate entries, so the keep count is unchanged. What the accessor buys is that HD
            // now has exactly one drug-policy read to audit, and it is one that cannot reach DrugPolicy's
            // per-ThingDef indexer, whose no-entry path is a message-less throw.
            keep += DrugPolicyLookup.TakeToInventoryTotal(pawn.drugs?.CurrentPolicy, def);
            var stockEntries = pawn.inventoryStock?.stockEntries;
            if (stockEntries != null)
                foreach (var entry in stockEntries.Values)
                    if (entry != null && entry.thingDef == def)
                        keep += entry.count;
            // Under CE the pawn's assigned loadout (ammo/sidearm reserve) is personal stock too — keep it.
            keep += CECompat.LoadoutKeepCount(pawn, def);
            // Item Policy's per-pawn inventory-stock count: keep it too, or HD's unload fights its re-fetch loop.
            keep += ItemPolicyCompat.KeepCount(pawn, def);
            // Compositable Loadouts' per-pawn loadout desired count (#200): same re-fetch-loop family as Item Policy.
            keep += CompositableLoadoutsCompat.KeepCount(pawn, def);
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

        /// <summary>
        /// The FOURTH keep source, which vanilla has and HD did not model: food the pawn is carrying to hand to an
        /// animal it is taming or training. Reported twice ("the pawn always tries to drop the kibble used for
        /// training if you manually try to tame an animal") — with no keep covering it, interaction food read as
        /// 100% surplus and the unload shipped it to storage mid-job.
        ///
        /// <para>The gap was structural, not an oversight of degree: <see cref="FoodKeepCountOf"/> is gated on
        /// <c>JobGiver_PackFood.IsGoodPackableFoodFor</c> (preferability &gt;= 7) while
        /// <c>WorkGiver_InteractAnimal.HasFoodToInteractAnimal</c> only accepts preferability &lt;= 5, so the
        /// packed-lunch keep could NEVER have protected it. See <see cref="AnimalInteractFoodKeepMath"/>.</para>
        ///
        /// <para>SELF-RELEASING and BOUNDED, the two properties that keep it from becoming a black hole: it is
        /// keyed on a live current/queued interaction job (<see cref="AnimalInteractFood.ReserveNutritionFor"/>
        /// returns 0 once none remains, and the ordinary unload then ships the whole stack), and it never pins more
        /// than the ~2.4 nutrition vanilla itself fetches — so a pawn that swept a 200-unit kibble stack for
        /// hauling keeps a handful of it for the animal and unloads the rest.</para>
        /// </summary>
        /// <param name="pawn">The carrying pawn — the source of the live-job signal.</param>
        /// <param name="thing">The inventory stack being assessed.</param>
        /// <returns>Units of this stack to keep, in <c>[0, stackCount]</c>; 0 for any stack that is not
        /// interaction food, or for a pawn with no live interaction job.</returns>
        public static int AnimalInteractFoodKeepCountOf(Pawn pawn, Thing thing)
        {
            // Def check first: it is a couple of field reads, so a non-food stack never pays for the job-queue walk.
            if (!AnimalInteractFood.IsInteractFood(thing.def))
                return 0;
            float reserve = AnimalInteractFood.ReserveNutritionFor(pawn);
            if (reserve <= 0f)
                return 0;
            float perUnit = thing.GetStatValue(StatDefOf.Nutrition);
            return AnimalInteractFoodKeepMath.KeepCount(reserve, perUnit, thing.stackCount);
        }
    }
}
