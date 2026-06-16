using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The ONE shared storage permit/deny filter (plan G4/G7) — the single serialized object that the
    /// en-route midway-store check (C2), before-carry routing (C3), and the permit/deny dialog (C4) all
    /// read. There is exactly one instance (<see cref="HaulersDreamSettings.storageBuildingFilter"/>, a
    /// single <c>Scribe_Deep</c> field) and one dialog (<see cref="Dialog_StorageBuildingFilter"/>); never
    /// create a second serialized <see cref="ThingFilter"/> for storage filtering.
    ///
    /// <para>This is INFRA ONLY in this wave: it is inert until W3 wires the funnel + call-site contexts,
    /// and the whole feature defaults OFF (<see cref="Enabled"/> short-circuits every query to allow-all).</para>
    ///
    /// <para>The object holds only the player's explicit overrides — two id sets (<see cref="denied"/> /
    /// <see cref="allowed"/>), keyed by storage-building <c>defName</c> OR owning <c>packageId</c>
    /// (case-insensitive). The pure decision (curated defaults, context selection, slow-set handling) lives
    /// in <see cref="StorageFilterPolicy.IsAllowed"/>; this Verse layer only resolves the building def +
    /// owning packageId from a cell/group and supplies the sets + current context + settings.</para>
    /// </summary>
    public class StorageBuildingFilter : IExposable
    {
        // Player explicit overrides. A key is EITHER a storage-building defName OR an owning mod packageId —
        // the policy tests both against the resolved candidate, so the dialog can offer per-building OR
        // whole-mod toggles into the same two sets. Case-insensitive (RimWorld treats packageIds so, and
        // defNames are unique regardless of case). Empty = allow-all (the parameterless-ctor default).
        public HashSet<string> denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- context push/pop stack (plan G4) -------------------------------------------------------------
        // W3 declares WHY storage is being chosen by pushing a context around the storage-search call. The
        // stack is [ThreadStatic] so a nested/recursive search (or a background thread that never pushes)
        // can't read another path's context. With nothing pushed, CurrentContext is the SAFE sentinel
        // Unload = allow-all, so any unguarded storage query the funnel reaches defaults to "permit" and the
        // filter can never wrongly block storage it wasn't told the purpose of.
        [ThreadStatic] private static List<StorageFilterContext> contextStack;

        /// <summary>The context the innermost active <see cref="PushContext"/> declared, or the safe
        /// allow-all sentinel (<see cref="StorageFilterContext.Unload"/>) when nothing is pushed.</summary>
        public static StorageFilterContext CurrentContext
        {
            get
            {
                var stack = contextStack;
                return stack != null && stack.Count > 0 ? stack[stack.Count - 1] : StorageFilterContext.Unload;
            }
        }

        /// <summary>Push <paramref name="ctx"/> as the current storage-choosing context for the duration of
        /// the returned scope; dispose (a <c>using</c> block) to pop it. Reentrant — pushes nest, and the
        /// matching pop restores the prior context. Used by W3 at every HD storage-choosing call site.</summary>
        public static IDisposable PushContext(StorageFilterContext ctx)
        {
            (contextStack ?? (contextStack = new List<StorageFilterContext>())).Add(ctx);
            return new ContextScope();
        }

        // A struct would box on the IDisposable return (an allocation either way); a tiny class scope pops
        // the innermost entry off this thread's stack. Idempotent-safe: only pops when the stack is non-empty.
        private sealed class ContextScope : IDisposable
        {
            public void Dispose()
            {
                var stack = contextStack;
                if (stack != null && stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
        }

        // ---- feature gate + queries -----------------------------------------------------------------------

        /// <summary>True only while the storage-filter feature master toggle is on. When false, every
        /// <see cref="IsCellAllowed"/>/<see cref="IsGroupAllowed"/> query short-circuits to allow-all, so the
        /// whole feature is byte-inert (W3's funnel postfix also early-returns on this before any work).</summary>
        public static bool Enabled =>
            HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.storageFiltersEnabled;

        /// <summary>True if any non-vanilla storage building def exists in this game's def database (the
        /// cached early-out for W3's funnel: with no modded storage at all, the curated/override sets can
        /// never matter and the funnel can skip entirely). Computed once, lazily — the def database is fixed
        /// after startup. Vanilla-only games return false; the presence of even one modded
        /// <see cref="Building_Storage"/> def returns true.</summary>
        public static bool AnyStorageModPresent
        {
            get
            {
                if (anyStorageModPresentCached.HasValue)
                    return anyStorageModPresentCached.Value;
                bool any = false;
                var defs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    var def = defs[i];
                    if (!IsStorageBuildingDef(def))
                        continue;
                    // Vanilla Core's own storage (shelves) doesn't count as "a storage mod present".
                    var mcp = def.modContentPack;
                    if (mcp == null || mcp.IsCoreMod)
                        continue;
                    any = true;
                    break;
                }
                anyStorageModPresentCached = any;
                return any;
            }
        }

        [System.NonSerialized] private static bool? anyStorageModPresentCached;

        /// <summary>Whether the storage building covering <paramref name="cell"/> is permitted in the current
        /// context. A cell that is a plain stockpile ZONE (or empty/unhandled) has NO storage building, so it
        /// is ALWAYS permitted — HD never blocks a vanilla stockpile. Feature-off ⇒ true.</summary>
        public bool IsCellAllowed(IntVec3 cell, Map map)
        {
            if (!Enabled)
                return true; // byte-inert when the feature master is off
            if (map == null || !cell.IsValid)
                return true; // nothing to resolve -> permit
            return IsGroupAllowed(cell.GetSlotGroup(map));
        }

        /// <summary>Whether <paramref name="group"/>'s storage building is permitted in the current context.
        /// A null group, or a group whose parent is a stockpile ZONE (not a building), has no building def to
        /// filter and is ALWAYS permitted. Feature-off ⇒ true. Allocation-light: resolves the def +
        /// packageId and delegates to the pure <see cref="StorageFilterPolicy.IsAllowed"/> (no per-call LINQ
        /// or allocation; the override sets are the live fields, the curated sets are static).</summary>
        public bool IsGroupAllowed(SlotGroup group)
        {
            if (!Enabled)
                return true; // byte-inert when the feature master is off
            if (group == null)
                return true; // no slot group here (empty cell / non-storage) -> permit

            // Resolve the owning BUILDING def. Only a Building_Storage parent carries a building def to
            // filter; a Zone_Stockpile parent (a plain stockpile) is not a building -> always permitted.
            // Null-carrier / null-parent guard mirrors HaulToStack.cs ~line 57's defensive early-outs.
            var parent = group.parent;
            if (!(parent is Building_Storage storage))
                return true; // stockpile zone or unknown parent -> permit (never block a plain stockpile)
            var def = storage.def;
            if (def == null)
                return true; // a Building with no def is malformed; permit rather than wrongly block

            return IsBuildingDefAllowed(def);
        }

        /// <summary>Whether the non-slot-group haul destination <paramref name="dest"/> (the
        /// <c>haulDestination</c> out-param of <see cref="StoreUtility.TryFindBestBetterStorageFor"/> when its
        /// <c>foundCell</c> is invalid — e.g. a grave or a modded container building) is permitted in the
        /// current context. A slot-group parent never reaches here (the cell path filters those); a
        /// non-Building destination (or one with no def) has no building def to filter and is ALWAYS
        /// permitted, mirroring the "never block a plain stockpile" rule. Feature-off ⇒ true.</summary>
        public bool IsHaulDestinationAllowed(IHaulDestination dest)
        {
            if (!Enabled)
                return true; // byte-inert when the feature master is off
            // Slot-group destinations are filtered via the cell/group path; this overload is only for the
            // non-slot-group container path. Resolve the owning BUILDING def; anything that isn't a Building
            // (a zone, a non-building container) has no building def to filter -> always permitted.
            if (!(dest is Building building))
                return true;
            var def = building.def;
            if (def == null)
                return true; // malformed building; permit rather than wrongly block
            return IsBuildingDefAllowed(def);
        }

        /// <summary>Shared resolution of a storage/haul-destination building <paramref name="def"/> to its
        /// defName + owning packageId and the pure <see cref="StorageFilterPolicy.IsAllowed"/> decision. Both
        /// <see cref="IsGroupAllowed"/> (slot-group / cell path) and <see cref="IsHaulDestinationAllowed"/>
        /// (non-slot-group container path) funnel through here so they agree exactly. Callers have already
        /// handled the feature-off + null-def early-outs.</summary>
        private bool IsBuildingDefAllowed(ThingDef def)
        {
            string defName = def.defName;
            string packageId = def.modContentPack?.PackageId; // already lowercased by ModContentPack; null = unknown owner
            return StorageFilterPolicy.IsAllowed(
                defName, packageId, CurrentContext,
                HaulersDreamMod.Settings.storageFilterUseDefaults,
                HaulersDreamMod.Settings.storageFilterDenyLwmForOpportunistic,
                denied, allowed);
        }

        /// <summary>True if <paramref name="def"/> is a storage building — i.e. a building whose thingClass
        /// is (or derives from) <see cref="Building_Storage"/> (vanilla shelves + every modded storage unit
        /// built on it). Plain stockpile zones are not ThingDefs and are handled separately as always-allowed.
        /// Shared by <see cref="AnyStorageModPresent"/> and the dialog's enumeration so both agree on what a
        /// "storage building" is.</summary>
        public static bool IsStorageBuildingDef(ThingDef def)
        {
            if (def?.thingClass == null)
                return false;
            return typeof(Building_Storage).IsAssignableFrom(def.thingClass);
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref denied, "denied", LookMode.Value);
            Scribe_Collections.Look(ref allowed, "allowed", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // Scribe reconstructs a HashSet with the DEFAULT (case-sensitive) comparer, dropping our
                // OrdinalIgnoreCase one; an absent/cleared node loads as null. Rebuild both as
                // case-insensitive sets (copying any loaded members) so the comparer is never lost and the
                // empty default is allow-all.
                denied = ToCaseInsensitive(denied);
                allowed = ToCaseInsensitive(allowed);
            }
        }

        private static HashSet<string> ToCaseInsensitive(HashSet<string> loaded)
            => loaded == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
    }
}
