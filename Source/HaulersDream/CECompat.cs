using System;
using System.Reflection;
using HarmonyLib;
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
    /// Scooped/swept goods waiting for the unload trip would be dumped on the floor. <see cref="NotifyHeld"/>
    /// registers them with CE's HoldTracker (<c>Utility_HoldTracker.Notify_HoldTrackerItem</c>) so CE leaves
    /// them alone. Caveat: CE's cleanup loop iterates <c>i &gt; 0</c> and never prunes its FIRST record, so the
    /// first-scooped def's record can outlive the goods and its count inflate on re-notify (a CE quirk;
    /// mod-side mitigation is a documented follow-up, not implemented here).</item>
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
        private static MethodInfo notifyHoldTracker;   // static ext: (Pawn, Thing, int) -> void
        private static MethodInfo getLoadout;          // static ext: (Pawn) -> Loadout
        private static MethodInfo loadoutSlotsGetter;  // instance prop get: Loadout.Slots -> List<LoadoutSlot>
        private static MethodInfo slotThingDefGetter;  // instance prop get: LoadoutSlot.thingDef -> ThingDef (null for generic slots)
        private static MethodInfo slotCountGetter;     // instance prop get: LoadoutSlot.count -> int
        private static StatDef bulkStat;               // CE's per-item "Bulk" stat (data, no assembly ref needed)
        private static Type ammoDefType;               // CombatExtended.AmmoDef (a ThingDef subclass)

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
            var holdTracker = AccessTools.TypeByName("CombatExtended.Utility_HoldTracker");
            if (holdTracker != null)
                notifyHoldTracker = AccessTools.Method(holdTracker, "Notify_HoldTrackerItem",
                    new[] { typeof(Pawn), typeof(Thing), typeof(int) });
            var utilityLoadouts = AccessTools.TypeByName("CombatExtended.Utility_Loadouts");
            if (utilityLoadouts != null)
                getLoadout = AccessTools.Method(utilityLoadouts, "GetLoadout", new[] { typeof(Pawn) });
            var loadoutType = AccessTools.TypeByName("CombatExtended.Loadout");
            if (loadoutType != null)
                loadoutSlotsGetter = AccessTools.PropertyGetter(loadoutType, "Slots");
            var slotType = AccessTools.TypeByName("CombatExtended.LoadoutSlot");
            if (slotType != null)
            {
                slotThingDefGetter = AccessTools.PropertyGetter(slotType, "thingDef");
                slotCountGetter = AccessTools.PropertyGetter(slotType, "count");
            }
            bulkStat = DefDatabase<StatDef>.GetNamedSilentFail("Bulk");
            // The fit check is the load-bearing piece; without it we must not claim compatibility-managed
            // loading (degrade SAFE — report inactive, the mod then behaves as without CE — vanilla math).
            active = canFitInInventory != null;
            if (active)
                Log.Message("[Hauler's Dream] Combat Extended detected — inventory loading defers to CE's "
                            + "weight+bulk capacity, smart overload stands down, HoldTracker integration on.");
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
        /// Tell CE's HoldTracker the pawn means to HOLD this item (a scooped/swept stack waiting for the
        /// unload trip), so CE's loadout enforcement doesn't dump it on the floor. No-ops without CE, for
        /// default-loadout pawns (CE checks internally — they're never drop-enforced anyway), and on any
        /// reflection failure (worst case: CE drops the item near the pawn; it stays haulable — no loss).
        /// </summary>
        public static void NotifyHeld(Pawn pawn, Thing item, int count)
        {
            if (!IsActive || notifyHoldTracker == null || pawn == null || item == null || count <= 0)
                return;
            // No try/catch: CE present + notifyHoldTracker resolved (checked above) — surface a real fault rather
            // than silently let CE drop the carried goods on the floor.
            notifyHoldTracker.Invoke(null, new object[] { pawn, item, count });
        }

        /// <summary>
        /// How many units of <paramref name="def"/> the pawn's assigned CE loadout wants it to CARRY —
        /// the pawn's own ammo/sidearm reserve. The unload pass must not ship it to storage: CE's
        /// JobGiver_UpdateLoadout would just re-fetch it (one churn cycle per sweep, the pawn temporarily
        /// disarmed of ammo in between). Conservative: EXACT def matches only — generic-def slots ("any
        /// AP ammo") are ignored, so at worst CE re-fetches once; guessing a generic match could instead
        /// hoard swept goods in inventory. 0 when CE is absent or anything fails (fail-open, like the
        /// other bridge members — the mod then behaves as without CE).
        /// </summary>
        public static int LoadoutKeepCount(Pawn pawn, ThingDef def)
        {
            if (!IsActive || pawn == null || def == null
                || getLoadout == null || loadoutSlotsGetter == null
                || slotThingDefGetter == null || slotCountGetter == null)
                return 0;
            // No try/catch: CE present + all loadout members resolved (checked above) — surface a real fault
            // instead of silently shipping the pawn's loadout ammo/sidearms to storage. The loadout == null
            // value-check below still degrades cleanly (a pawn with no assigned loadout keeps nothing extra).
            var loadout = getLoadout.Invoke(null, new object[] { pawn });
            if (loadout == null)
                return 0;
            int keep = 0;
            if (loadoutSlotsGetter.Invoke(loadout, null) is System.Collections.IEnumerable slots)
                foreach (var slot in slots)
                    if (slot != null && (slotThingDefGetter.Invoke(slot, null) as ThingDef) == def)
                        keep += (int)slotCountGetter.Invoke(slot, null);
            return keep;
        }
    }
}
