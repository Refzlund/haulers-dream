using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Simple Sidearms compatibility bridge — REFLECTION ONLY, no hard assembly reference, so the mod runs
    /// identically with or without Simple Sidearms installed. Verified against the SS source
    /// (PeteTimesSix/SimpleSidearms): a pawn's carried sidearms live in <c>inventory.innerContainer</c> and are
    /// tracked by <c>SimpleSidearms.rimworld.CompSidearmMemory.rememberedWeapons</c>
    /// (<c>List&lt;ThingDefStuffDefPair&gt;</c>, the struct having public <c>ThingDef thing</c> / <c>ThingDef
    /// stuff</c> fields).
    ///
    /// Why this exists: HD's "unload all surplus" would otherwise tag a remembered sidearm as surplus and ship
    /// it to storage; SS's retrieve-weapon think node then re-fetches it into inventory, and HD re-adopts it next
    /// check — an unload↔pickup LOOP. <see cref="IsKeptWeapon"/> reports a remembered sidearm as keep-stock so
    /// <see cref="InventorySurplus.SurplusOf"/> returns 0 and it is never adopted/unloaded, severing the loop.
    /// </summary>
    public static class SimpleSidearmsCompat
    {
        private static bool initialized;
        private static bool active;            // Simple Sidearms loaded (CompSidearmMemory resolvable)
        private static bool memoryApiOk;       // the precise rememberedWeapons query is usable
        private static bool warned;

        private static Type compType;          // SimpleSidearms.rimworld.CompSidearmMemory
        private static FieldInfo rememberedField; // List<ThingDefStuffDefPair> rememberedWeapons
        private static FieldInfo pairThingField;  // ThingDefStuffDefPair.thing
        private static FieldInfo pairStuffField;  // ThingDefStuffDefPair.stuff

        /// <summary>Whether Simple Sidearms is loaded (its CompSidearmMemory type resolves). Cached.</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        /// <summary>True when SS is present AND its rememberedWeapons (def,stuff) query resolved — i.e. the
        /// count-aware keep (<see cref="RememberedCount"/>) is usable. False => callers fall back to the keep-all
        /// <see cref="IsKeptWeapon"/> path instead of treating an unknown count as 0 (which would unload kit).</summary>
        public static bool MemoryApiOk
        {
            get
            {
                if (!initialized)
                    Init();
                return memoryApiOk;
            }
        }

        private static void Init()
        {
            initialized = true;
            // No try/catch: SS-ABSENT is the AccessTools.TypeByName == null precondition (it returns null, never
            // throws), and every member is null-guarded. A throw in here would be a genuine reflection fault worth
            // surfacing, not the optional-dependency case. Runs once (lazily on first IsActive).
            compType = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory");
            if (compType == null)
                return; // Simple Sidearms not loaded — the real precondition
            active = true;
            rememberedField = AccessTools.Field(compType, "rememberedWeapons");
            var pairType = AccessTools.TypeByName("SimpleSidearms.rimworld.ThingDefStuffDefPair");
            if (pairType != null)
            {
                pairThingField = AccessTools.Field(pairType, "thing");
                pairStuffField = AccessTools.Field(pairType, "stuff");
            }
            memoryApiOk = rememberedField != null && pairThingField != null && pairStuffField != null;
            Log.Message("[Hauler's Dream] Simple Sidearms detected — carried sidearms are excluded from surplus "
                        + "unloading" + (memoryApiOk ? "." : " (memory API unresolved — keeping all carried weapons as a safe fallback)."));
        }

        private static ThingComp MemoryOf(Pawn pawn)
        {
            var comps = pawn?.AllComps;
            if (comps == null || compType == null)
                return null;
            for (int i = 0; i < comps.Count; i++)
                if (compType.IsInstanceOfType(comps[i]))
                    return comps[i];
            return null;
        }

        /// <summary>
        /// True if this inventory weapon is the pawn's carried kit that the unload must NOT strip — a remembered
        /// Simple Sidearms weapon (precise (def, stuff) match, via <see cref="IsRememberedSidearm"/>), or — if SS
        /// is present but its memory API can't be resolved (a fork/version rename) — any non-HD-tagged colonist
        /// weapon (a safe, loop-free fallback). A genuine remembered sidearm is kept EVEN IF HD has tagged it (the
        /// tag is a def-overlap false positive); only a NON-remembered weapon HD swept off the ground is left
        /// unloadable by the HD-tagged exclusion (else it would become a silent black hole). Returns false when SS
        /// is absent (no weapon-keep rule then).
        /// </summary>
        public static bool IsKeptWeapon(Pawn pawn, Thing weapon)
        {
            if (!IsActive || pawn == null || weapon?.def == null)
                return false;
            if (!(weapon.def.IsRangedWeapon || weapon.def.IsMeleeWeapon))
                return false;
            // A GENUINE remembered sidearm is ALWAYS kept — even if HD's def-overlap self-heal or its
            // first-same-def-stack pick falsely TAGGED it. HD never scoops the specific equipped/remembered Thing
            // off the ground, so an HD tag on a remembered sidearm is always a false positive; this must win over
            // the HD-tagged exclusion below, or the pawn ships its own sidearm to storage and SS re-fetches it (the
            // "occasionally stops what it's doing to unload its own sidearm" bug).
            if (IsRememberedSidearm(pawn, weapon))
                return true;
            // Not a (precisely) remembered sidearm: a weapon HD itself scooped/swept off the ground (HD-tagged)
            // must remain unloadable, or HD would have put it in the pack and then refuse to take it out (a black
            // hole). A genuine remembered sidearm is never HD-scooped, so this only un-keeps a loose swept weapon.
            var hd = pawn.GetComp<CompHauledToInventory>();
            if (hd != null && hd.PeekHashSet().Contains(weapon))
                return false;
            if (memoryApiOk)
                return false; // memory resolved, not a remembered sidearm -> HD may unload this loose weapon
            // SS present but its memory API didn't resolve (renamed in a fork/version). Conservatively keep any
            // non-HD-tagged colonist weapon so the unload<->refetch loop cannot occur; surface the mismatch once.
            if (!warned)
            {
                warned = true;
                HDLog.Warn("Simple Sidearms present but CompSidearmMemory.rememberedWeapons did "
                           + "not resolve; keeping all carried weapons out of surplus unloading as a safe fallback.");
            }
            return true;
        }

        /// <summary>
        /// True if this weapon is a GENUINE Simple Sidearms remembered sidearm — a precise (def, stuff) match
        /// against the pawn's CompSidearmMemory.rememberedWeapons — IGNORING whether HD has tagged it. Used both
        /// to let a genuine sidearm WIN over a false-positive HD tag (see <see cref="IsKeptWeapon"/>) and to stop
        /// HD's def-keyed tagging from ever auto-tagging it (the <see cref="CompHauledToInventory"/> self-heal and
        /// <see cref="YieldRouter.InventoryStackOfDef"/> first-same-def pick). Returns false when SS is absent, AND
        /// when SS's memory API did not resolve (a fork/rename) — that conservative "keep all carried weapons"
        /// fallback stays inside <see cref="IsKeptWeapon"/> so it never widens the tagging guards, which must only
        /// ever skip a precisely-known sidearm (else a genuinely-swept loose weapon could become a black hole).
        /// </summary>
        public static bool IsRememberedSidearm(Pawn pawn, Thing weapon)
        {
            if (!IsActive || !memoryApiOk || pawn == null || weapon?.def == null)
                return false;
            if (!(weapon.def.IsRangedWeapon || weapon.def.IsMeleeWeapon))
                return false;
            var comp = MemoryOf(pawn);
            // No try/catch: SS present + members resolved (checked above) — a throw is a real contract fault to
            // surface, not silently fail-open. comp == null (pawn has no sidearm memory) degrades cleanly to false.
            if (comp != null && rememberedField.GetValue(comp) is IEnumerable list)
                foreach (var pair in list)
                    if ((pairThingField.GetValue(pair) as ThingDef) == weapon.def
                        && (pairStuffField.GetValue(pair) as ThingDef) == weapon.Stuff)
                        return true;
            return false;
        }

        /// <summary>
        /// The number of Simple Sidearms remembered-weapon entries matching this exact (def, stuff) — i.e. how
        /// many of that pair the pawn wants to keep as its kit. SS adds one rememberedWeapons entry per equipped
        /// primary/sidearm (duplicates allowed), so the matching count IS the keep-target. Returns 0 when SS is
        /// absent, when its memory API didn't resolve (<see cref="MemoryApiOk"/> false — callers must then fall
        /// back to keep-all, NOT keep-0), when the pawn has no sidearm memory, or for a non-weapon. A HAULED loose
        /// weapon is never routed through SS's InformOfAddedSidearm, so it never inflates this count — the excess
        /// over it is genuinely surplus the unload should put away.
        /// </summary>
        public static int RememberedCount(Pawn pawn, ThingDef def, ThingDef stuff)
        {
            if (!IsActive || !memoryApiOk || pawn == null || def == null)
                return 0;
            if (!(def.IsRangedWeapon || def.IsMeleeWeapon))
                return 0;
            var comp = MemoryOf(pawn);
            int count = 0;
            if (comp != null && rememberedField.GetValue(comp) is IEnumerable list)
                foreach (var pair in list)
                    if ((pairThingField.GetValue(pair) as ThingDef) == def
                        && (pairStuffField.GetValue(pair) as ThingDef) == stuff)
                        count++;
            return count;
        }

        /// <summary>
        /// How many of this exact (def, stuff) Simple Sidearms wants the pawn to keep IN INVENTORY — i.e. its
        /// remembered count (<see cref="RememberedCount"/>) MINUS the equipped primary if the primary is this pair.
        /// <para>
        /// SS records the equipped PRIMARY in rememberedWeapons (InformOfAddedPrimary → InformOfAddedSidearm), but
        /// the primary physically lives in <c>equipment.Primary</c>, NOT <c>inventory.innerContainer</c>. So a raw
        /// RememberedCount over-keeps by one when compared against an inventory-only have-count: a hauled duplicate
        /// of the primary's (def,stuff) computes surplus = have(1) − keep(1) = 0 and is NEVER put away (the reported
        /// "won't unload / re-stows into inventory" bug when the equipped weapon matches the hauled one). This
        /// mirrors Simple Sidearms' OWN <c>Pawn_InventoryTracker.FirstUnloadableThing</c> postfix, which removes the
        /// equipped primary's pair from the kept-sidearm set before counting inventory copies. Sidearms (which DO
        /// live in innerContainer) are unaffected. Returns 0 when SS is absent / its memory API didn't resolve.
        /// </para>
        /// </summary>
        public static int InventoryKeepCount(Pawn pawn, ThingDef def, ThingDef stuff)
        {
            int keep = RememberedCount(pawn, def, stuff);
            if (keep <= 0)
                return 0;
            // The equipped primary satisfies one remembered entry of its pair from EQUIPMENT, so it must not pin an
            // inventory copy. Subtract at most one (keep >= 1 here, so the result stays >= 0). Mirrors SS removing
            // exactly the primary from its desiredSidearms before scanning inventory.
            var primary = pawn?.equipment?.Primary;
            if (primary != null && primary.def == def && primary.Stuff == stuff)
                keep -= 1;
            return keep;
        }
    }
}
