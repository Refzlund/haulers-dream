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
            try
            {
                compInventoryType = AccessTools.TypeByName("CombatExtended.CompInventory");
                if (compInventoryType == null)
                    return; // CE not loaded
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
                // loading (fail SAFE: report inactive, the mod then behaves as without CE — vanilla math).
                active = canFitInInventory != null;
                if (active)
                    Log.Message("[Hauler's Dream] Combat Extended detected — inventory loading defers to CE's "
                                + "weight+bulk capacity, smart overload stands down, HoldTracker integration on.");
            }
            catch (Exception e)
            {
                Log.Warning("[Hauler's Dream] Combat Extended detection failed (running without CE integration): " + e);
                active = false;
            }
        }

        private static ThingComp CompInventoryOf(Pawn pawn)
        {
            if (pawn == null || compInventoryType == null)
                return null;
            var comps = pawn.AllComps;
            if (comps == null)
                return null;
            for (int i = 0; i < comps.Count; i++)
                if (compInventoryType.IsInstanceOfType(comps[i]))
                    return comps[i];
            return null;
        }

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
            try
            {
                var args = new object[] { thing, 0, false, false };
                canFitInInventory.Invoke(comp, args);
                int count = (int)args[1];
                // CE computes the count from availableWeight/availableBulk, which go NEGATIVE for an
                // already-over-capacity pawn — clamp so callers never see a negative pickup count.
                return count < 0 ? 0 : count;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Hauler's Dream] CE CanFitInInventory call failed; not clamping: " + e, 0x43464E);
                return int.MaxValue;
            }
        }

        /// <summary>The pawn's remaining CE bulk room. PositiveInfinity when CE is off / unavailable.</summary>
        public static float AvailableBulk(Pawn pawn)
        {
            if (!IsActive || pawn == null || getAvailableBulk == null)
                return float.PositiveInfinity;
            var comp = CompInventoryOf(pawn);
            if (comp == null)
                return float.PositiveInfinity;
            try
            {
                return (float)getAvailableBulk.Invoke(comp, new object[] { true });
            }
            catch
            {
                return float.PositiveInfinity;
            }
        }

        /// <summary>CE bulk per unit of <paramref name="thing"/> (0 when CE is off — bulk then never binds).</summary>
        public static float BulkPerUnit(Thing thing)
        {
            if (!IsActive || thing == null || bulkStat == null)
                return 0f;
            try
            {
                return thing.GetStatValue(bulkStat);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>CE bulk per unit of <paramref name="def"/>, def-level (planning — no live Thing yet).</summary>
        public static float BulkPerUnitAbstract(ThingDef def)
        {
            if (!IsActive || def == null || bulkStat == null)
                return 0f;
            try
            {
                return def.GetStatValueAbstract(bulkStat);
            }
            catch
            {
                return 0f;
            }
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
            try
            {
                notifyHoldTracker.Invoke(null, new object[] { pawn, item, count });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Hauler's Dream] CE HoldTracker notify failed (CE may drop carried goods early): " + e, 0x484C44);
            }
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
            try
            {
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
            catch (Exception e)
            {
                Log.WarningOnce("[Hauler's Dream] CE loadout read failed (not protecting loadout stock from unload): " + e, 0x4C4F44);
                return 0;
            }
        }
    }
}
