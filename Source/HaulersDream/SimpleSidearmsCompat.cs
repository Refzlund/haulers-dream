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
        /// Simple Sidearms weapon (precise (def, stuff) match), or — if SS is present but its memory API can't be
        /// resolved (a fork/version rename) — any non-HD-tagged colonist weapon (a safe, loop-free fallback).
        /// EXCLUDES weapons HD itself swept off the ground (they are HD-tagged and must stay unloadable, or they
        /// would become a silent black hole). Returns false when SS is absent (no weapon-keep rule then).
        /// </summary>
        public static bool IsKeptWeapon(Pawn pawn, Thing weapon)
        {
            if (!IsActive || pawn == null || weapon?.def == null)
                return false;
            if (!(weapon.def.IsRangedWeapon || weapon.def.IsMeleeWeapon))
                return false;
            // Never keep a weapon HD itself scooped/swept (it is HD-tagged) — it must remain unloadable, or HD
            // would have put it in the pack and then refuse to take it out (a black hole). A genuine remembered
            // sidearm is never HD-scooped, so this only un-keeps a loose weapon HD swept.
            var hd = pawn.GetComp<CompHauledToInventory>();
            if (hd != null && hd.PeekHashSet().Contains(weapon))
                return false;
            if (memoryApiOk)
            {
                var comp = MemoryOf(pawn);
                // No try/catch: SS present + members resolved (checked above) — a throw is a real contract fault
                // to surface, not silently fail-open. comp == null (pawn has no sidearm memory) degrades cleanly.
                if (comp != null && rememberedField.GetValue(comp) is IEnumerable list)
                    foreach (var pair in list)
                        if ((pairThingField.GetValue(pair) as ThingDef) == weapon.def
                            && (pairStuffField.GetValue(pair) as ThingDef) == weapon.Stuff)
                            return true;
                return false; // memory resolved, not a remembered sidearm -> HD may unload this loose weapon
            }
            // SS present but its memory API didn't resolve (renamed in a fork/version). Conservatively keep any
            // non-HD-tagged colonist weapon so the unload<->refetch loop cannot occur; surface the mismatch once.
            if (!warned)
            {
                warned = true;
                Log.Warning("[Hauler's Dream] Simple Sidearms present but CompSidearmMemory.rememberedWeapons did "
                            + "not resolve; keeping all carried weapons out of surplus unloading as a safe fallback.");
            }
            return true;
        }
    }
}
