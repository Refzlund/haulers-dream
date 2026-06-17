using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>
    /// Pure permit/deny decision for a single storage-building candidate (the SHARED building filter,
    /// plan G4/G7). Faithful port of While You're Up's two default filters (<c>Settings.cs</c>
    /// <c>ResetFilters()</c>) folded into one context-selected policy. No game types — fully unit-tested.
    ///
    /// <para>Decision order in <see cref="IsAllowed"/> (matches WYU intent):</para>
    /// <list type="number">
    ///   <item>A PLAYER explicit override wins — deny first, then allow (an explicit list the player set
    ///   in the dialog beats any default).</item>
    ///   <item>Else, when <c>useDefaults</c>, the curated default set for the context decides:
    ///     <c>Opportunistic</c> = allow-all minus the slow set; <c>BeforeCarry</c> = deny-all except the
    ///     curated container allow-list; <c>Unload</c> = always allow (a carrying pawn must be able to
    ///     unload — plan G4).</item>
    ///   <item>The slow set (LWM Deep Storage) is denied ONLY in <c>Opportunistic</c>/<c>BeforeCarry</c>
    ///   and ONLY while <c>denyLwmForOpportunistic</c> is on — NEVER in <c>Unload</c>.</item>
    ///   <item>Otherwise ALLOWED — the default for an unknown building (or unknown owning mod) is
    ///   "allowed" so the policy can never wrongly block a vanilla stockpile / unrecognized container.</item>
    /// </list>
    ///
    /// <para>Allocation-light: every collection is supplied by the caller; <see cref="IsAllowed"/> does
    /// only membership tests against the passed-in sets and the <c>static readonly</c> curated sets, with
    /// no per-call allocation (see <c>StorageFilterPolicyPerfTests</c>).</para>
    /// </summary>
    public static class StorageFilterPolicy
    {
        /// <summary>
        /// The "slow" storage set — denied for non-unload hauls (a storing DELAY means a stop there is
        /// not actually opportune). Faithful to WYU: the slow set is <c>{lwm.deepstorage}</c> ONLY.
        /// Deep Storage Plus (<c>im.skye.rimworld.deepstorageplus</c>) and every other storage mod are
        /// NOT slow and stay allowed. PackageId comparison is case-insensitive (RimWorld treats
        /// packageIds case-insensitively).
        /// </summary>
        public static readonly HashSet<string> SlowStoragePackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lwm.deepstorage", // LWM's Deep Storage — store delay; not "opportunistic"
            };

        /// <summary>
        /// The curated BEFORE-CARRY allow-list: the only mods whose storage buildings WYU permits as a
        /// before-carry detour destination by default (everything else is denied). These were hand-curated
        /// in WYU as "actual containers for storage" (not just classes reusing <c>Building_Storage</c>).
        /// Ported verbatim from <c>Settings.cs ResetFilters()</c> (the <c>hbcDefaultBuildingFilter</c>
        /// allow switch). Includes Core (<c>ludeon.rimworld</c>) and LWM (<c>lwm.deepstorage</c>) — note
        /// LWM IS allowed before-carry even though it is slow for opportunistic. Case-insensitive.
        /// </summary>
        public static readonly HashSet<string> BeforeCarryAllowPackageIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "buddy1913.expandedstorageboxes",      // Buddy's Expanded Storage Boxes
                "im.skye.rimworld.deepstorageplus",    // Deep Storage Plus
                "jangodsoul.simplestorage",            // [JDS] Simple Storage
                "jangodsoul.simplestorage.ref",        // [JDS] Simple Storage - Refrigeration
                "ludeon.rimworld",                     // Core
                "lwm.deepstorage",                     // LWM's Deep Storage
                "mlie.displaycases",                   // Display Cases (Continued)
                "mlie.eggincubator",                   // Egg Incubator
                "mlie.extendedstorage",                // Extended Storage (Continued)
                "mlie.fireextinguisher",               // Fire Extinguisher (Continued)
                "mlie.functionalvanillaexpandedprops", // Functional Vanilla Expanded Props (Continued)
                "mlie.tobesdiningroom",                // Tobe's Dining Room (Continued)
                "ogliss.thewhitecrayon.quarry",        // Quarry
                "primitivestorage.velcroboy333",       // Primitive Storage
                "proxyer.smallshelf",                  // Small Shelf
                "rimfridge.kv.rw",                     // [KV] RimFridge
                "sixdd.littlestorage2",                // Little Storage 2
                "skullywag.extendedstorage",           // Extended Storage
                "solaris.furniturebase",               // GloomyFurniture
                "vanillaexpanded.vfecore",             // Vanilla Furniture Expanded
                "vanillaexpanded.vfeart",              // Vanilla Furniture Expanded - Art
                "vanillaexpanded.vfefarming",          // Vanilla Furniture Expanded - Farming
                "vanillaexpanded.vfespacer",           // Vanilla Furniture Expanded - Spacer Module
                "vanillaexpanded.vfesecurity",         // Vanilla Furniture Expanded - Security
            };

        /// <summary>
        /// Decide whether a single storage building is permitted in the given hauling context.
        /// Pure and allocation-free (callers own all collections; the curated sets are static).
        /// </summary>
        /// <param name="buildingDefName">The candidate building's <c>defName</c> — used for the player
        /// per-building overrides. May be null/empty (then no player override can match; treated as
        /// "no explicit override").</param>
        /// <param name="packageId">The owning mod's <c>packageId</c> (case-insensitive). May be
        /// null/empty for an unrecognized owner — then no curated default matches and the building is
        /// ALLOWED (the safe fallback that never blocks a vanilla/unknown stockpile).</param>
        /// <param name="ctx">Which hauling situation is choosing storage (selects the default set).</param>
        /// <param name="useDefaults">When true, the curated per-context default sets apply (WYU
        /// "auto-manage"); when false, only player overrides decide and everything else is allowed.</param>
        /// <param name="denyLwmForOpportunistic">When true, the slow set is denied in
        /// <see cref="StorageFilterContext.Opportunistic"/> / <see cref="StorageFilterContext.BeforeCarry"/>
        /// (never in <see cref="StorageFilterContext.Unload"/>). When false, the slow set is treated like
        /// any other (allowed unless a default/override says otherwise).</param>
        /// <param name="playerDenied">PackageIds the player explicitly DENIED (highest precedence). May be
        /// null. Membership is decided by this collection's own comparer.</param>
        /// <param name="playerAllowed">PackageIds the player explicitly ALLOWED (beats a default-deny, but
        /// not an explicit deny). May be null.</param>
        /// <returns>True if the building is permitted as a storage destination in this context.</returns>
        public static bool IsAllowed(
            string buildingDefName,
            string packageId,
            StorageFilterContext ctx,
            bool useDefaults,
            bool denyLwmForOpportunistic,
            ICollection<string> playerDenied,
            ICollection<string> playerAllowed)
        {
            // 1) Player explicit overrides win, deny before allow. WYU's tree lets the player flip an
            //    individual building def OR a whole mod category; the Verse layer resolves which key to
            //    check and passes the matching id, so we test both the building defName and the packageId.
            if (Contains(playerDenied, buildingDefName) || Contains(playerDenied, packageId))
                return false;
            if (Contains(playerAllowed, buildingDefName) || Contains(playerAllowed, packageId))
                return true;

            // 2) Unload is ALWAYS allow-all (a carrying pawn must be able to put its load down — G4).
            //    No slow-set denial here even with denyLwmForOpportunistic on.
            if (ctx == StorageFilterContext.Unload)
                return true;

            // 3) The slow set (LWM Deep Storage) is denied for the two non-unload contexts when enabled.
            if (denyLwmForOpportunistic && SlowStoragePackageIds.Contains(packageId ?? string.Empty))
                return false;

            // 4) Curated defaults (auto-manage). When defaults are off, nothing else restricts -> allowed.
            if (useDefaults)
            {
                switch (ctx)
                {
                    case StorageFilterContext.Opportunistic:
                        // Allow everything (the slow-set denial above already removed LWM when enabled).
                        return true;

                    case StorageFilterContext.BeforeCarry:
                        // Deny everything EXCEPT the curated container allow-list. An unknown owning mod
                        // (null/empty packageId, or one not in the list) is denied as a before-carry
                        // detour — matching WYU's deny-by-default for this context.
                        return BeforeCarryAllowPackageIds.Contains(packageId ?? string.Empty);
                }
            }

            // 5) Default: allowed (never wrongly block a vanilla stockpile / unrecognized building).
            return true;
        }

        private static bool Contains(ICollection<string> set, string value)
        {
            // Null/empty value can't be an explicit override; a null set has no members.
            if (set == null || string.IsNullOrEmpty(value))
                return false;
            return set.Contains(value);
        }
    }
}
