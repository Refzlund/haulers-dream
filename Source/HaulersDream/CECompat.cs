using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Combat Extended compatibility bridge — REFLECTION ONLY, no hard assembly reference, so the mod
    /// runs identically with or without CE installed. Everything verified against the CE source clone
    /// (CombatExtended-Continued, v16.x for RimWorld 1.6):
    ///
    /// <list type="bullet">
    /// <item><b>Weight</b>: CE postfixes <c>MassUtility.Capacity</c> to return its CarryWeight stat, so all
    /// of this mod's existing mass math transparently reads CE's capacity — nothing to bridge.</item>
    /// <item><b>Bulk</b>: CE adds a second carry dimension (Bulk per item vs the pawn's CarryBulk) that
    /// vanilla math can't see. <see cref="MaxFitCount"/> exposes CE's own canonical check —
    /// <c>CompInventory.CanFitInInventory(Thing, out int, bool, bool)</c>, which enforces BOTH weight and
    /// bulk — and every pickup-count decision in this mod clamps through it.</item>
    /// <item><b>No overloading under CE</b>: CE's <c>StatWorker_MoveSpeed</c> applies its own encumbrance
    /// penalty (and calls base first, so StatParts would STACK with it). With CE active the smart-overload
    /// feature therefore stands down entirely: <see cref="OverloadGate.NoOverload"/> treats CE as strict
    /// carry weight (never load past CE's caps) and <see cref="StatPart_Overload"/> applies no factor —
    /// CE's encumbrance simulation is the single source of slowdown truth.</item>
    /// <item><b>Loadout auto-drop</b>: CE's <c>JobGiver_UpdateLoadout</c> force-drops inventory items that a
    /// pawn's assigned loadout doesn't cover (<c>GetExcessThing</c>; default-loadout pawns are exempt).
    /// <see cref="Patch_CombatExtended_GetExcessThing"/> suppresses a positive result while the pawn still has any
    /// genuinely unloadable HD-tagged cargo. Whole-load scope is required because a CE generic ceiling is shared
    /// across defs: tagged Beer can make CE select an untagged Wake-Up stack. Cargo remains HD-owned transient
    /// state — it is never written into CE's persistent, player-facing HoldTracker — and CE resumes ordinary
    /// cleanup as soon as no tagged surplus remains.</item>
    /// <item><b>Inventory cache</b>: CE postfixes ThingOwner's NotifyAdded/NotifyRemoved/Take, so this mod's
    /// SplitOff+TryAdd/TryAddOrTransfer flows keep CE's CompInventory cache in sync automatically.</item>
    /// </list>
    /// </summary>
    public static class CECompat
    {
        private static bool initialized;
        private static bool active;

        private static Type compInventoryType;
        private static MethodInfo canFitInInventory;   // instance: (Thing, out int, bool, bool) -> bool
        private static MethodInfo getAvailableBulk;    // instance: (bool) -> float
        private static MethodInfo getLoadout;          // static ext: (Pawn) -> Loadout
        private static MethodInfo getSlotsFor;          // instance: Loadout.GetSlotsFor(Pawn) -> IEnumerable<LoadoutSlot>
        private static FieldInfo loadoutDefaultField;   // instance: Loadout.defaultLoadout -> bool
        private static MethodInfo getStorageByThingDef; // static ext: (Pawn) -> Dictionary<ThingDef,Integer>
        private static FieldInfo integerValueField;     // instance: Integer.value -> int
        private static MethodInfo slotThingDefGetter;  // instance prop get: LoadoutSlot.thingDef -> ThingDef (null for generic slots)
        private static MethodInfo slotCountGetter;     // instance prop get: LoadoutSlot.count -> int
        private static MethodInfo slotGenericDefGetter; // instance prop get: LoadoutSlot.genericDef -> LoadoutGenericDef (null for specific slots)
        private static MethodInfo slotCountTypeGetter; // instance prop get: LoadoutSlot.countType -> LoadoutCountType
        private static MethodInfo lambdaGetter;         // instance prop get: LoadoutGenericDef.lambda -> Predicate<ThingDef>
        private static object dropExcessCountType;      // boxed enum member, resolved by name (never numeric layout)
        private static StatDef bulkStat;               // CE's per-item "Bulk" stat (data, no assembly ref needed)
        private static Type ammoDefType;               // CombatExtended.AmmoDef (a ThingDef subclass)

        // The loadout reader is reached from per-stack surplus probes, often several times in one work scan.
        // Keep every mutable work buffer thread-local: threading mods can fan those probes to worker threads, while
        // a shared scratch collection would race its Clear/Add calls. Each Fill clears before use and again on exit,
        // retaining capacity but not Pawn/ThingDef/delegate references between calls.
        [ThreadStatic] private static Dictionary<ThingDef, int> loadoutQueryCounts;
        [ThreadStatic] private static Dictionary<ThingDef, int> loadoutInventoryCounts;
        [ThreadStatic] private static List<CeLoadoutKeepPolicy.Stock> loadoutStocks;
        [ThreadStatic] private static List<CeLoadoutKeepPolicy.Slot> loadoutSlots;
        [ThreadStatic] private static List<CeLoadoutKeepPolicy.Keep> loadoutKeeps;
        [ThreadStatic] private static object[] loadoutPawnArg;
        [ThreadStatic] private static bool fillingLoadoutCounts;

        /// <summary>Whether Combat Extended is loaded (detected by its CompInventory type being resolvable —
        /// the assembly only loads when the mod is active). Cached after the first call.</summary>
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
            active = false;
            // No try/catch: CE-ABSENT is handled by the precondition below — AccessTools.TypeByName returns null
            // (it does not throw) when CE isn't loaded, and every member resolve is null-guarded. So a throw in
            // here would be a GENUINE reflection/contract fault worth surfacing as a red error, not the optional-
            // dependency case the old catch was downgrading to a warning. Init runs ONCE (lazily on first
            // IsActive), so there is no per-tick cost.
            compInventoryType = AccessTools.TypeByName("CombatExtended.CompInventory");
            if (compInventoryType == null)
                return; // CE not loaded — the real precondition, no catch needed
            // Ammo detection is independent of the inventory-fit feature below (it gates the keep, not loading),
            // so resolve it here while we know CE is present — even if CanFitInInventory later fails to resolve.
            ammoDefType = AccessTools.TypeByName("CombatExtended.AmmoDef");
            canFitInInventory = AccessTools.Method(compInventoryType, "CanFitInInventory",
                new[] { typeof(Thing), typeof(int).MakeByRefType(), typeof(bool), typeof(bool) });
            getAvailableBulk = AccessTools.Method(compInventoryType, "GetAvailableBulk", new[] { typeof(bool) });
            var utilityLoadouts = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
            if (utilityLoadouts != null)
                getLoadout = AccessTools.Method(utilityLoadouts, "GetLoadout", new[] { typeof(Pawn) });
            var loadoutType = AccessTools.TypeByName("CombatExtended.Loadout");
            if (loadoutType != null)
            {
                getSlotsFor = AccessTools.Method(loadoutType, "GetSlotsFor", new[] { typeof(Pawn) });
                loadoutDefaultField = AccessTools.Field(loadoutType, "defaultLoadout");
            }
            var holdTrackerType = AccessTools.TypeByName("CombatExtended.Utility_HoldTracker");
            if (holdTrackerType != null)
                getStorageByThingDef = AccessTools.Method(holdTrackerType, "GetStorageByThingDef", new[] { typeof(Pawn) });
            var integerType = AccessTools.TypeByName("CombatExtended.Integer");
            if (integerType != null)
                integerValueField = AccessTools.Field(integerType, "value");
            var slotType = AccessTools.TypeByName("CombatExtended.LoadoutSlot");
            if (slotType != null)
            {
                slotThingDefGetter = AccessTools.PropertyGetter(slotType, "thingDef");
                slotCountGetter = AccessTools.PropertyGetter(slotType, "count");
                slotGenericDefGetter = AccessTools.PropertyGetter(slotType, "genericDef");
                slotCountTypeGetter = AccessTools.PropertyGetter(slotType, "countType");
            }
            var countType = AccessTools.TypeByName("CombatExtended.LoadoutCountType");
            // A fork may retain the enum type while renaming/removing this member. Treat that exactly like any
            // other unresolved reflection seam: leave it null so the warning below reports a graceful capability
            // loss. Enum.Parse without the IsDefined gate used to turn that ordinary API drift into an Init throw.
            try
            {
                if (countType?.IsEnum == true && Enum.IsDefined(countType, "dropExcess"))
                    dropExcessCountType = Enum.Parse(countType, "dropExcess");
            }
            catch (Exception)
            {
                dropExcessCountType = null;
            }
            var genericDefType = AccessTools.TypeByName("CombatExtended.LoadoutGenericDef");
            if (genericDefType != null)
                lambdaGetter = AccessTools.PropertyGetter(genericDefType, "lambda");
            bulkStat = DefDatabase<StatDef>.GetNamedSilentFail("Bulk");
            // The fit check is the load-bearing piece; without it we must not claim compatibility-managed
            // loading (degrade SAFE — report inactive, the mod then behaves as without CE — vanilla math).
            active = canFitInInventory != null;
            if (active)
            {
                HDLog.Msg("Combat Extended detected — inventory loading defers to CE's "
                            + "weight+bulk capacity, smart overload stands down, shared refill-only loadout allocation on.");
                // Silent-degrade tripwire: `active` gates ONLY on the fit check, while the loadout reader is a
                // separate member. If CE renamed the Loadout API, LoadoutKeepCount returns 0 with no other symptom
                // and HD may ship CE-loadout stock to storage only for CE to fetch it again. Surface that drift
                // loudly (logging only — behaviour already degrades safely member-by-member). The independent
                // GetExcessThing cargo guard owns and reports its own reflection seam in its Harmony Prepare().
                if (getLoadout == null || getSlotsFor == null || loadoutDefaultField == null
                    || getStorageByThingDef == null || integerValueField == null
                    || slotThingDefGetter == null || slotCountGetter == null || slotCountTypeGetter == null
                    || dropExcessCountType == null)
                    HDLog.Warn("Combat Extended present but its loadout-allocation API (Utility_Loadouts.GetLoadout / "
                               + "Loadout.GetSlotsFor / Utility_HoldTracker.GetStorageByThingDef / Integer.value / "
                               + "LoadoutSlot.thingDef|count|countType) did not fully resolve; HD cannot read CE loadouts to keep "
                               + "loadout ammo with the pawn — a CE rename likely. Please report it. HD continues.");
                if (slotGenericDefGetter == null || lambdaGetter == null)
                    HDLog.Warn("Combat Extended present but its generic-loadout API (LoadoutSlot.genericDef / "
                               + "LoadoutGenericDef.lambda) did not fully resolve; HD cannot read CE's generic loadout "
                               + "slots (e.g. GenericMeal) — meals and other generic-slot items may loop between "
                               + "inventory and storage. A CE rename likely. Please report it. HD continues.");
            }
            else
                // CE is present (CompInventory resolved) but its load-bearing weight+bulk fit check did not bind
                // (a CE fork/version renamed the method) — degrade SAFE (report inactive => HD uses vanilla mass
                // math), but surface the drift once so it isn't a silent capability loss.
                HDLog.Warn("Combat Extended present but CompInventory.CanFitInInventory(Thing, out int, bool, bool) "
                           + "did not resolve; CE weight+bulk-aware loading is OFF (falling back to vanilla mass math).");
        }

        // Single-slot per-pawn CompInventory memo (the sweep callers — BulkHaul.BuildPoolInto, TransportLoad,
        // PackAnimalLoad, the OverloadGate gate — probe MaxFitCount per candidate for the SAME pawn, so caching
        // the last-resolved (pawn -> comp) pair collapses the per-candidate AllComps walk to one walk per pawn).
        // [ThreadStatic] per this assembly's hook-reachable-scratch convention (see PawnMassCache / BulkHaul):
        // a work-scan call on a worker thread gets its own slot, so a threading mod can't race it. Keyed by the
        // pawn reference (not thingIDNumber): a pawn's comp set is fixed for its lifetime, and on a different pawn
        // the reference miss re-walks — no staleness risk (a pawn never swaps its CompInventory at runtime).
        [ThreadStatic] private static Pawn lastCompPawn;
        [ThreadStatic] private static ThingComp lastCompInventory;

        private static ThingComp CompInventoryOf(Pawn pawn)
        {
            if (pawn == null || compInventoryType == null)
                return null;
            // Fast path: same pawn as the previous candidate in this sweep — reuse the resolved comp.
            if (ReferenceEquals(pawn, lastCompPawn))
                return lastCompInventory;
            var comps = pawn.AllComps;
            if (comps == null)
                return null;
            ThingComp found = null;
            for (int i = 0; i < comps.Count; i++)
                if (compInventoryType.IsInstanceOfType(comps[i]))
                {
                    found = comps[i];
                    break;
                }
            lastCompPawn = pawn;
            lastCompInventory = found;
            return found;
        }

        // Reused scratch for the CanFitInInventory marshalling so the per-candidate fit check allocates no
        // object[] per call (it's called per candidate in BulkHaul / TransportLoad / PackAnimalLoad / OverloadGate
        // sweeps). [ThreadStatic] + lazy-init matches the assembly's hook-reachable-scratch idiom (a worker-thread
        // work scan gets its own buffer). The two constant bool args (args[2]/args[3], both false) are boxed ONCE
        // at first use; only args[0] (thing) and args[1] (the out-count) are refilled per call. CRITICAL: args[1]
        // is the `out int count` slot CanFitInInventory writes (decompile-verified: CE assigns
        // `count = Mathf.FloorToInt(...)` on EVERY path, so the prior boxed int is fully overwritten) — we still
        // RESET it to a fresh boxed 0 before each Invoke to never hand CE a stale box and to keep the contract
        // byte-identical to the old `new object[]{thing,0,false,false}`. A single Invoke runs to completion before
        // the next reuse on one thread, so no re-entrancy aliasing.
        [ThreadStatic] private static object[] fitArgs;
        // Box `false` once (the two constant CanFitInInventory bool args) — shared across threads (immutable box).
        private static readonly object BoxedFalse = false;

        /// <summary>
        /// How many units of <paramref name="thing"/> CE allows this pawn to load right now (weight AND bulk,
        /// measured against the live inventory; capped by the thing's stackCount — CE's own semantics).
        /// int.MaxValue when CE is off or the pawn has no CompInventory (nothing to defer to).
        /// </summary>
        public static int MaxFitCount(Pawn pawn, Thing thing)
        {
            if (!IsActive || pawn == null || thing == null)
                return int.MaxValue;
            var comp = CompInventoryOf(pawn);
            if (comp == null)
                return int.MaxValue;
            // No try/catch: !IsActive, the resolved member, and comp == null are all checked above, so in here CE
            // is present and CanFitInInventory resolved — a throw is a real CE-integration fault to surface, not
            // silently fail-open to int.MaxValue (which would over-load the pawn past CE's bulk cap).
            var args = fitArgs;
            if (args == null)
            {
                // First use on this thread: allocate once and box the two constant bool args once (they never
                // change — always false). Only args[0] (thing) and args[1] (the out-count) are refilled per call.
                args = fitArgs = new object[4];
                args[2] = BoxedFalse;   // bool ignoreEquipment (constant)
                args[3] = BoxedFalse;   // bool useApparelCalculations (constant)
            }
            args[0] = thing;   // Thing thing
            args[1] = 0;       // out int count — RESET the out-param slot to a fresh boxed 0 before each Invoke
            canFitInInventory.Invoke(comp, args);
            int count = (int)args[1];
            // CE computes the count from availableWeight/availableBulk, which go NEGATIVE for an
            // already-over-capacity pawn — clamp so callers never see a negative pickup count.
            return count < 0 ? 0 : count;
        }

        /// <summary>The pawn's remaining CE bulk room. PositiveInfinity when CE is off / unavailable.</summary>
        public static float AvailableBulk(Pawn pawn)
        {
            if (!IsActive || pawn == null || getAvailableBulk == null)
                return float.PositiveInfinity;
            var comp = CompInventoryOf(pawn);
            if (comp == null)
                return float.PositiveInfinity;
            // No try/catch: CE present + getAvailableBulk resolved + comp != null (all checked above) — a throw
            // is a real fault to surface, not silently fail-open and disable the bulk gate.
            return (float)getAvailableBulk.Invoke(comp, new object[] { true });
        }

        /// <summary>
        /// True if this item is Combat Extended ammo (its def is a CombatExtended.AmmoDef). CE keeps a pawn's
        /// loadout ammo in inventory and re-fetches anything taken out, so HD's surplus unload must leave carried
        /// ammo alone or pawns walk back and forth dropping/re-grabbing bullets (the reported loop). Keeps ALL
        /// carried ammo (CE's own loadout system manages the right amount and drops genuine excess); HD-swept
        /// loose ammo is still unloadable because the caller excludes HD-tagged stacks. Independent of the
        /// inventory-fit feature, so it works even if that part of the bridge fails to resolve.
        /// </summary>
        public static bool IsCarriedAmmo(Thing thing)
        {
            if (thing?.def == null)
                return false;
            if (!initialized)
                Init();
            return ammoDefType != null && ammoDefType.IsInstanceOfType(thing.def);
        }

        /// <summary>CE bulk per unit of <paramref name="thing"/> (0 when CE is off — bulk then never binds).</summary>
        public static float BulkPerUnit(Thing thing)
        {
            if (!IsActive || thing == null || bulkStat == null)
                return 0f;
            // No try/catch: GetStatValue is a vanilla call (bulkStat null-checked above) — surface a throw.
            return thing.GetStatValue(bulkStat);
        }

        /// <summary>CE bulk per unit of <paramref name="def"/>, def-level (planning — no live Thing yet).</summary>
        public static float BulkPerUnitAbstract(ThingDef def)
        {
            if (!IsActive || def == null || bulkStat == null)
                return 0f;
            // No try/catch: GetStatValueAbstract is a vanilla call (bulkStat null-checked above) — surface a throw.
            return def.GetStatValueAbstract(bulkStat);
        }

        /// <summary>
        /// How many MORE units of <paramref name="def"/> the pawn's remaining CE BULK room allows.
        /// Def-level and unclamped by any live stack, so a PLANNER can gate/bound a load that will span
        /// several stacks (<see cref="MaxFitCount"/> is capped at one stack's count, per CE's semantics,
        /// which under-measures a multi-stack gather; the issue #124 lesson). int.MaxValue when CE is off,
        /// the pawn has no CompInventory, or the def carries no bulk (the dimension never binds); 0 when
        /// the room is used up. Weight is NOT considered here: the callers' vanilla mass math already
        /// covers it (CE postfixes <c>MassUtility.Capacity</c> with its CarryWeight).
        /// </summary>
        public static int FitUnitsByBulk(Pawn pawn, ThingDef def)
        {
            if (!IsActive || pawn == null || def == null)
                return int.MaxValue;
            float perUnit = BulkPerUnitAbstract(def);
            if (perUnit <= 0f)
                return int.MaxValue; // no bulk stat resolved, or a zero-bulk item: bulk never binds
            // AvailableBulk reads CE's live CompInventory (PositiveInfinity when unavailable; the pure
            // helper maps that to "never binds" rather than the int.MinValue a raw cast would produce).
            return CarryMath.UnitsThatFitBulk(AvailableBulk(pawn), perUnit);
        }

        /// <summary>
        /// How many INVENTORY units of <paramref name="def"/> the pawn's assigned CE loadout would actively
        /// re-fetch after HD unloaded them. This mirrors CE's pickup calculation, not merely its drop ceiling:
        /// <c>dropExcess</c> slots contribute 0, while <c>pickupDrop</c> generic slots share one count across all
        /// matching defs in CE's storage order. Equipment and loaded magazines satisfy slots too, but are
        /// subtracted back out of the returned inventory keep, so they do not pin spare copies in the pack.
        /// </summary>
        public static int LoadoutKeepCount(Pawn pawn, ThingDef def)
        {
            if (def == null)
                return 0;

            var counts = loadoutQueryCounts
                         ?? (loadoutQueryCounts = new Dictionary<ThingDef, int>());
            FillLoadoutKeepCounts(pawn, counts);
            int keep = counts.TryGetValue(def, out int found) ? found : 0;
            counts.Clear();
            return keep;
        }

        /// <summary>
        /// Fill every positive per-def inventory keep contributed by CE's refill loadout for one live pawn
        /// snapshot. The caller owns <paramref name="output"/>; it is always cleared first and remains empty when
        /// CE is absent, its reflected API is incomplete, the pawn uses the default loadout, or CE throws while
        /// producing the snapshot. This whole-map form lets scan callers reuse one allocation across many defs.
        /// </summary>
        internal static void FillLoadoutKeepCounts(Pawn pawn, Dictionary<ThingDef, int> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (!IsActive || pawn == null
                || getLoadout == null || getSlotsFor == null || loadoutDefaultField == null
                || getStorageByThingDef == null || integerValueField == null
                || slotThingDefGetter == null || slotCountGetter == null || slotCountTypeGetter == null
                || dropExcessCountType == null || fillingLoadoutCounts)
                return;

            var inventoryCounts = loadoutInventoryCounts
                                  ?? (loadoutInventoryCounts = new Dictionary<ThingDef, int>());
            var stocks = loadoutStocks ?? (loadoutStocks = new List<CeLoadoutKeepPolicy.Stock>());
            var slots = loadoutSlots ?? (loadoutSlots = new List<CeLoadoutKeepPolicy.Slot>());
            var keeps = loadoutKeeps ?? (loadoutKeeps = new List<CeLoadoutKeepPolicy.Keep>());
            var pawnArg = loadoutPawnArg ?? (loadoutPawnArg = new object[1]);
            inventoryCounts.Clear();
            stocks.Clear();
            slots.Clear();
            keeps.Clear();
            pawnArg[0] = pawn;
            fillingLoadoutCounts = true;

            try
            {
                var loadout = getLoadout.Invoke(null, pawnArg);
                if (loadout == null || (bool)loadoutDefaultField.GetValue(loadout))
                    return;

                // Preserve CE's dictionary enumeration order. Generic slots consume this exact order; sorting by
                // defName (or reconstructing storage ourselves) can pick a different concrete def for the slot.
                if (!(getStorageByThingDef.Invoke(null, pawnArg) is IDictionary storage))
                    return;
                var owner = pawn.inventory?.innerContainer;
                if (owner != null)
                    foreach (var outer in owner)
                    {
                        var thing = outer?.GetInnerIfMinified();
                        if (thing?.def == null)
                            continue;
                        inventoryCounts.TryGetValue(thing.def, out int current);
                        inventoryCounts[thing.def] = current + thing.stackCount;
                    }

                foreach (DictionaryEntry entry in storage)
                {
                    var stockDef = entry.Key as ThingDef;
                    if (stockDef == null || entry.Value == null)
                        continue;
                    int total = (int)integerValueField.GetValue(entry.Value);
                    inventoryCounts.TryGetValue(stockDef, out int inPack);
                    stocks.Add(new CeLoadoutKeepPolicy.Stock(stockDef, total, inPack));
                }

                // GetSlotsFor includes parent and ad-hoc virtual weapon/ammo slots; Loadout.Slots alone does not.
                if (!(getSlotsFor.Invoke(loadout, pawnArg) is IEnumerable reflectedSlots))
                    return;
                foreach (var slot in reflectedSlots)
                {
                    if (slot == null)
                        continue;
                    int count = (int)slotCountGetter.Invoke(slot, null);
                    var mode = Equals(slotCountTypeGetter.Invoke(slot, null), dropExcessCountType)
                        ? CeLoadoutKeepPolicy.SlotMode.DropExcess
                        : CeLoadoutKeepPolicy.SlotMode.PickupDrop;
                    var exactDef = slotThingDefGetter.Invoke(slot, null) as ThingDef;
                    if (exactDef != null)
                    {
                        slots.Add(CeLoadoutKeepPolicy.Slot.Exact(exactDef, count, mode));
                        continue;
                    }
                    if (slotGenericDefGetter == null || lambdaGetter == null)
                        continue;
                    var genericDef = slotGenericDefGetter.Invoke(slot, null);
                    var matcher = genericDef == null ? null : lambdaGetter.Invoke(genericDef, null) as Delegate;
                    if (matcher != null)
                        slots.Add(CeLoadoutKeepPolicy.Slot.Generic(matcher, count, mode));
                }

                CeLoadoutKeepPolicy.Allocate(stocks, slots, GenericSlotMatches, keeps);
                for (int i = 0; i < keeps.Count; i++)
                    if (keeps[i].Count > 0 && keeps[i].Def is ThingDef keptDef)
                        output[keptDef] = keeps[i].Count;
            }
            catch (Exception ex)
            {
                output.Clear();
                HDLog.ErrOnce("Combat Extended loadout allocation threw; HD is standing down its CE keep-count "
                              + "bridge for this probe (items may be unloaded and re-fetched). Please report it.\n"
                              + HDFault.Render(ex), unchecked((int)0xCE7A0002));
            }
            finally
            {
                // Clear reference-bearing scratch even on an early return/foreign exception; capacity remains
                // available to the next probe on this thread without retaining pawn defs or CE delegates.
                inventoryCounts.Clear();
                stocks.Clear();
                slots.Clear();
                keeps.Clear();
                pawnArg[0] = null;
                fillingLoadoutCounts = false;
            }
        }

        private static bool GenericSlotMatches(object matcher, object def)
        {
            try
            {
                if (matcher is Predicate<ThingDef> predicate && def is ThingDef thingDef)
                    return predicate(thingDef);
                if (matcher is Delegate fallback)
                    return (bool)fallback.DynamicInvoke(def);
            }
            catch (Exception)
            {
                HDLog.ErrOnce("CE LoadoutGenericDef.lambda threw while allocating a shared generic loadout slot — "
                              + "that match was skipped (non-fatal).", unchecked((int)0xCE7A0001));
            }
            return false;
        }
    }
}
