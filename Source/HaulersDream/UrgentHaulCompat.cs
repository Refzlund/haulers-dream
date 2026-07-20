using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Soft-dependency bridge to the two "Haul Urgently" mods: Allow Tool (<c>unlimitedhugs.allowtool</c>) and its
    /// performance-friendly reimplementation Keyz' Allow Utilities (<c>keyz182.KeyzAllowUtilities</c>). Both expose
    /// urgent hauling as a plain map <see cref="Designation"/> of a <see cref="DesignationDef"/>: Allow Tool
    /// <c>"HaulUrgentlyDesignation"</c>, Keyz <c>"KAU_HaulUrgentlyDesignation"</c>.
    ///
    /// <para>This is the SINGLE SOURCE OF TRUTH for "is this an urgent-haul designation / item?", consumed by both
    /// <see cref="UrgentHaulBulk"/> (which builds the bulk pickup) and <see cref="ForeignOrderGuard"/> (which must
    /// treat an urgent mark as a haul, not a foreign per-item claim). Keeping the two on one resolver means the Keyz
    /// def can never be whitelisted in one place but missed in the other.</para>
    ///
    /// <para>No compile-time reference to either mod: the defs are resolved by name via
    /// <see cref="DefDatabase{T}.GetNamedSilentFail"/> exactly like <see cref="ForeignOrderGuard"/> resolves the
    /// vanilla "Haul" def, so HD compiles and runs identically with or without either mod. When neither mod is
    /// present both defs are null and the whole urgent-haul feature is inert.</para>
    /// </summary>
    internal static class UrgentHaulCompat
    {
        // Resolved lazily on first actual use, after defs are loaded, mirroring ForeignOrderGuard's lazy resolve.
        // Either may be null (its mod absent); when BOTH are null nothing is ever urgent.
        private static bool resolved;
        private static DesignationDef keyzUrgentDef;      // Keyz' Allow Utilities
        private static DesignationDef allowToolUrgentDef; // Allow Tool

        private static void EnsureResolved()
        {
            if (resolved)
                return;
            resolved = true;
            keyzUrgentDef = DefDatabase<DesignationDef>.GetNamedSilentFail("KAU_HaulUrgentlyDesignation");
            allowToolUrgentDef = DefDatabase<DesignationDef>.GetNamedSilentFail("HaulUrgentlyDesignation");
        }

        /// <summary>True when at least one "Haul Urgently" mod defined its designation, so the urgent bulk pickup
        /// has anything to act on. The builder's cheapest early-out.</summary>
        internal static bool AnyUrgentDefResolved
        {
            get
            {
                EnsureResolved();
                return keyzUrgentDef != null || allowToolUrgentDef != null;
            }
        }

        /// <summary>True when <paramref name="def"/> is one of the resolved "Haul Urgently" designation defs (Allow
        /// Tool or Keyz). <see cref="ForeignOrderGuard"/> uses it to treat an urgent mark as a haul (HD may bulk it),
        /// not a foreign per-item claim. A null / any other def → false.</summary>
        internal static bool IsUrgentDesignationDef(DesignationDef def)
        {
            if (def == null)
                return false;
            EnsureResolved();
            return def == keyzUrgentDef || def == allowToolUrgentDef;
        }

        /// <summary>
        /// Fill <paramref name="into"/> (Clearing it first) with every spawned thing carrying a "Haul Urgently"
        /// designation whose position is within <paramref name="radiusSq"/> (squared cells) of
        /// <paramref name="anchor"/>. Enumerates the designation manager (the urgent WorkGiver's own source) so an
        /// urgent item is found whether or not the haul lister lists it (an urgent stack is moved even with no
        /// strictly-better storage, the point of the feature). A thing marked by BOTH mods can appear twice; the
        /// caller de-dups by thing id. No ordering is imposed here; the pure policy imposes the deterministic one.
        /// </summary>
        internal static void CollectUrgentNear(Map map, IntVec3 anchor, float radiusSq, List<Thing> into)
        {
            into.Clear();
            if (map == null)
                return;
            EnsureResolved();
            AddUrgentOfDef(map, anchor, radiusSq, keyzUrgentDef, into);
            AddUrgentOfDef(map, anchor, radiusSq, allowToolUrgentDef, into);
        }

        // Append the in-range spawned things carrying `def` to `into`. A null def (its mod absent) is a no-op.
        private static void AddUrgentOfDef(Map map, IntVec3 anchor, float radiusSq, DesignationDef def, List<Thing> into)
        {
            if (def == null)
                return;
            foreach (var d in map.designationManager.SpawnedDesignationsOfDef(def))
            {
                if (!d.target.HasThing)
                    continue;
                var t = d.target.Thing;
                if (t == null || !t.Spawned)
                    continue;
                if ((t.Position - anchor).LengthHorizontalSquared > radiusSq)
                    continue;
                into.Add(t);
            }
        }
    }
}
