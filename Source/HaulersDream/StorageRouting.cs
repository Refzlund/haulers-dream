using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// STORAGE ROUTING (While You're Up "haul before carry") — the standalone, consumer-aware relocation engine.
    /// Before a pawn carries a resource to a build site / crafting bill, RELOCATE the largest nearby stack of that
    /// material to storage CLOSER to the consuming job (so future fetches are short), and grab same-/equal-priority
    /// extras. Faithful to WYU's <c>BeforeCarryDetour.cs</c> (the before-carry redirect + worthwhileness gate) and
    /// <c>StoreUtility.cs</c> <c>TryFindBestBetterStoreCellFor_MidwayToTarget</c> (the relaxed-priority,
    /// same-group-skipping, midway-ranked store scan).
    ///
    /// <para><b>RE-SCOPED to STANDALONE scans</b> (per the parity plan): NOT a postfix extension of HaulToStack.
    /// The pure decisions live in <see cref="StorageRoutingPolicy"/> (priority eligibility, worthwhileness,
    /// midway/destination ranking — all reused, not duplicated); this Verse layer enumerates slot groups / cells,
    /// supplies their priorities and squared distances, and emits the relocation job.</para>
    ///
    /// <para><b>The relocation job is a vanilla <c>HaulAIUtility.HaulToCellStorageJob</c></b> (a normal
    /// <c>HaulToCell</c> to the chosen closer store cell), exactly as WYU emits — so a separate hand-haul runs
    /// BEFORE the carry, then the pawn's next work scan re-issues the original construct/bill job and fetches from
    /// the now-closer storage (no toil transpile, no fragile mid-job surgery).</para>
    ///
    /// <para><b>Conflict guards (plan G2/G5/G6/G7/G8):</b>
    /// <list type="bullet">
    ///   <item><b>G2 anti-double-haul</b> — a candidate stack is rejected if another pawn already targets it
    ///   (<see cref="RouteSelection.ClaimedByOtherPawns"/>, which skips self) OR this pawn's own current/queued job
    ///   already targets it. (The seam patches also re-confirm with the live job before emitting.)</item>
    ///   <item><b>G5 no double-act with HaulToStack</b> — the relocation IS a normal <c>HaulToCellStorageJob</c>;
    ///   HaulToStack's postfix may refine its cell on a later work-pipeline re-scan (accepted top-up). The
    ///   <see cref="StorageBuildingFilter.PushContext"/>(<see cref="StorageFilterContext.BeforeCarry"/>) is scoped
    ///   TIGHTLY around ONLY this engine's own slot-group scan (a synchronous <c>using</c> block), so BeforeCarry is
    ///   provably inactive by the time the job is emitted / started — never leaking into the
    ///   <c>needAccurateResult:false</c> store probes elsewhere.</item>
    ///   <item><b>G6 no double-act with InventoryConstructDelivery / BillPrepGather</b> — enforced at PLAN time in
    ///   the seam patches: when ICD (supplies) or BillPrepGather (ingredients) is ON and would convert this very
    ///   job, the routing stands down entirely (those features deliver fewer trips their own way).</item>
    ///   <item><b>G7 one shared building filter</b> — the midway scan runs inside the BeforeCarry context and
    ///   applies the shared <see cref="StorageBuildingFilter"/> per group/cell, so a building the player denied for
    ///   before-carry is excluded (byte-inert when the filter feature is off — every query is allow-all).</item>
    ///   <item><b>G8 no recursion</b> — the emitted relocation is MARKED (<see cref="MarkRelocation"/>) and
    ///   <see cref="Patch_Pawn_JobTracker_TryOpportunisticJob_NoRouteRecurse"/> suppresses vanilla's opportunistic
    ///   prefix for it, so a routing relocation never recursively spawns another opportunistic haul through
    ///   <c>TryOpportunisticJob</c>.</item>
    /// </list></para>
    ///
    /// <para>Allocation-light: the per-thread scratch sets are reused (Cleared at the point of use); the slot-group
    /// scan runs no per-cell LINQ/closures. BYTE-INERT when <see cref="HaulersDreamSettings.storageRouting"/> is
    /// off (every seam's first line returns).</para>
    /// </summary>
    public static class StorageRouting
    {
        // ---- G8 relocation marker -------------------------------------------------------------------------

        // The jobs THIS engine emitted, so TryOpportunisticJob can recognize them and suppress the opportunistic
        // prefix (G8). A ConditionalWeakTable holds no strong ref (GC-safe) and only ever has the live relocation
        // job(s) in flight — the table never grows unbounded. A new (HaulToCell) Job has allowOpportunisticPrefix
        // == true on its DEF, which we cannot change from this wave's file set (a dedicated def lives outside it),
        // so we mark the INSTANCE instead and gate on the mark in the patch below.
        //
        // The stored value is the RelocationInfo for the job — carrying the consumer cell it is being relocated
        // CLOSER TO, used purely cosmetically by the W6 inspector report rewrite ("X (closer to {DESTINATION})")
        // and the dev detour overlay. Cosmetic only: never read on a gameplay path.
        private static readonly ConditionalWeakTable<Job, RelocationInfo> relocationJobs =
            new ConditionalWeakTable<Job, RelocationInfo>();

        /// <summary>Cosmetic side-data for a routing relocation: the consumer cell it is being moved closer to
        /// (the build site / bench), so the inspector report and dev overlay can name/draw it. Reference-typed so
        /// the weak table can hold it without boxing an IntVec3 per relocation.</summary>
        internal sealed class RelocationInfo
        {
            internal readonly IntVec3 consumeCell;
            internal RelocationInfo(IntVec3 consumeCell) { this.consumeCell = consumeCell; }
        }

        /// <summary>Record <paramref name="job"/> as a routing relocation (G8 gate) carrying the consumer cell it is
        /// being moved closer to (cosmetic — for the report rewrite / overlay). Idempotent.</summary>
        internal static void MarkRelocation(Job job, IntVec3 consumeCell)
        {
            if (job != null)
                relocationJobs.GetValue(job, _ => new RelocationInfo(consumeCell));
        }

        /// <summary>True if <paramref name="job"/> is a routing relocation this engine emitted (G8 gate).</summary>
        internal static bool IsRelocation(Job job)
            => job != null && relocationJobs.TryGetValue(job, out _);

        /// <summary>The cosmetic side-data for <paramref name="job"/> if it is a routing relocation, else null.</summary>
        internal static RelocationInfo RelocationData(Job job)
            => job != null && relocationJobs.TryGetValue(job, out var info) ? info : null;

        // ---- the public entry: build the relocation job for a consuming job, or null --------------------

        /// <summary>
        /// Build the relocation haul for a pawn about to carry <paramref name="material"/> to
        /// <paramref name="consumeCell"/>, choosing the LARGEST eligible stack among <paramref name="plannedStacks"/>
        /// and relocating it to storage strictly closer to the consumer (WYU before-carry). Returns null when no
        /// relocation is worthwhile (vanilla's carry then stands).
        ///
        /// PURE planning: no reservations, no world mutation — the emitted job makes its own reservations.
        /// </summary>
        /// <param name="pawn">The carrier about to do the consuming job.</param>
        /// <param name="material">The resource def being carried (supplies) — informational; the chosen stack's def
        /// governs the store search. May be null (the chosen stack's def is authoritative).</param>
        /// <param name="consumeCell">The build site / bench cell the material is headed to (WYU carryTarget).</param>
        /// <param name="plannedStacks">The floor stacks vanilla already queued to fetch (construct: targetA +
        /// targetQueueA; bill: targetQueueB). The C3-mf2 contract: read what vanilla QUEUED, not the consumed
        /// resourcesAvailable.</param>
        /// <param name="allowEqualPriority">routeToEqualPriority — equal-priority relocation allowed (before-carry).</param>
        /// <param name="allowStockpiles">routeToStockpiles — plain stockpiles are eligible relocation targets.</param>
        internal static Job TryRouteToConsumer(Pawn pawn, ThingDef material, IntVec3 consumeCell,
            IReadOnlyList<LocalTargetInfo> plannedStacks, bool allowEqualPriority, bool allowStockpiles)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.storageRouting)
                return null; // BYTE-INERT when off
            // Master kill switch: routing is an AUTOMATIC behavior -> master-OFF suppresses it. It only ever
            // ADDS a relocation job (never an unload), so suppressing it strands nothing (G1).
            if (!MasterEnable.Active)
                return null;
            if (pawn?.Map == null || !pawn.Spawned || !consumeCell.IsValid)
                return null;

            // The biggest reservable, deliverable, unclaimed floor stack vanilla planned to fetch (WYU picks the
            // single largest stack to relocate — `resourcesAvailable.MaxBy(stackCount)` — but faithful to the
            // C3-mf2 re-scope we choose from what vanilla actually QUEUED, not the consumed pool).
            var thing = PickLargestRoutableStack(pawn, plannedStacks);
            if (thing == null)
                return null;

            // WYU: never relocate a stack already in someone's pockets (ParentHolder is Pawn_InventoryTracker —
            // BeforeCarryDetour.cs:91) and don't relocate when picking it up would over-encumber the pawn for the
            // following carry (BeforeCarryDetour.cs:95: "already going for 1, so 2 to check for another").
            if (thing.ParentHolder is Pawn_InventoryTracker)
                return null;
            if (MassUtility.WillBeOverEncumberedAfterPickingUp(pawn, thing, 2))
                return null;

            // The midway store search (BeforeCarry context). The chosen cell is STRICTLY-priority-relaxed and
            // closest to the thing<->consumer midway, same-group-skipped, building-filtered (G7).
            IntVec3 storeCell = FindBeforeCarryStoreCell(pawn, thing, consumeCell, allowEqualPriority, allowStockpiles);
            if (!storeCell.IsValid)
                return null;

            // WORTHWHILENESS (BeforeCarryDetour.cs:100-104 via StorageRoutingPolicy.WorthRelocating): only relocate
            // when the candidate store is STRICTLY closer to the consumer than the stack's current position is.
            int fromHereSq = (thing.Position - consumeCell).LengthHorizontalSquared;
            int fromStoreSq = (storeCell - consumeCell).LengthHorizontalSquared;
            if (!StorageRoutingPolicy.WorthRelocating(fromHereSq, fromStoreSq))
                return null;

            // EMIT the relocation as a normal HaulToCellStorageJob (G5) and MARK it so it can't recursively spawn an
            // opportunistic prefix (G8).
            var job = HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
            if (job == null)
                return null;
            MarkRelocation(job, consumeCell);
            if (s.verboseLogging)
                HDLog.Dbg($"Routing: {pawn} relocates {thing.def?.label} (x{thing.stackCount}) closer to {consumeCell} " +
                          $"before carrying (from {fromHereSq} -> {fromStoreSq} sq to target).");
            return job;
        }

        // ---- candidate selection (G2) ---------------------------------------------------------------------

        /// <summary>The LARGEST stack among <paramref name="planned"/> that is a valid relocation candidate: a
        /// spawned, non-forbidden, non-corpse floor stack this pawn can auto-haul + reserve, not already claimed by
        /// another pawn or targeted by this pawn's own job queue (G2). Allocation-light (one reused scratch list is
        /// not even needed — a single linear max-scan).</summary>
        private static Thing PickLargestRoutableStack(Pawn pawn, IReadOnlyList<LocalTargetInfo> planned)
        {
            if (planned == null || planned.Count == 0)
                return null;
            var claimed = RouteSelection.ClaimedByOtherPawns(pawn); // skips self (G2 "others" half)

            Thing best = null;
            int bestCount = 0;
            for (int i = 0; i < planned.Count; i++)
            {
                var t = planned[i].Thing;
                if (t == null || !t.Spawned || t.Map != pawn.Map)
                    continue;
                if (t.def == null || !t.def.EverHaulable || t is Corpse)
                    continue;
                if (t.stackCount <= bestCount)
                    continue; // can't beat the running best — skip the (cheaper-than-validation) common case early
                if (t.IsForbidden(pawn))
                    continue;
                // G2: another pawn already targets it, or this pawn's own current/queued job does (the consuming
                // job we're prefixing already targets these very stacks — but that's the SAME job being replaced,
                // so OwnJobTargets is checked against the LIVE job in the seam patch, not here; here we only reject
                // OTHER pawns' claims to avoid a double-haul).
                if (claimed.Contains(t))
                    continue;
                if (!pawn.CanReserve(t))
                    continue;
                if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, t, forced: false))
                    continue;
                best = t;
                bestCount = t.stackCount;
            }
            return best;
        }

        // ---- the before-carry midway store search (WYU TryFindBestBetterStoreCellFor_MidwayToTarget) ------

        /// <summary>
        /// Find the store cell for <paramref name="thing"/> CLOSEST to the thing<->consumer midway, walking slot
        /// groups in priority order with WYU's before-carry relaxation: strictly-higher priority always, equal
        /// priority only when <paramref name="allowEqualPriority"/>, never the stack's own group, never Unstored,
        /// stockpiles only when <paramref name="allowStockpiles"/> (and Building_Storage only when the shared filter
        /// allows — G7). A faithful re-expression of <c>StoreUtility.cs:214-272</c> with a valid beforeCarryTarget
        /// (no opportunityTarget). Ranking via the pure <see cref="StorageRoutingPolicy.MidwayBetter"/> /
        /// <see cref="EnRoutePickupPolicy.MidwayDistanceSquared"/> (shared midway math).
        /// </summary>
        private static IntVec3 FindBeforeCarryStoreCell(Pawn pawn, Thing thing, IntVec3 consumeCell,
            bool allowEqualPriority, bool allowStockpiles)
        {
            var map = pawn.Map;
            var currentPriority = StoreUtility.CurrentStoragePriorityOf(thing);
            int currentPriorityInt = (int)currentPriority;

            // The thing<->consumer midway (WYU StoreUtility.cs:247-249). thingPos = where the stack physically is.
            IntVec3 thingPos = thing.SpawnedOrAnyParentSpawned ? thing.PositionHeld : pawn.PositionHeld;
            EnRoutePickupPolicy.Midway(thingPos.x, thingPos.y, thingPos.z,
                consumeCell.x, consumeCell.y, consumeCell.z, out int midX, out int _, out int midZ);

            // The stack's OWN slot group (WYU skips it — StoreUtility.cs:233). Resolved once.
            var ownGroup = thingPos.IsValid ? map.haulDestinationManager.SlotGroupAt(thingPos) : null;

            // G7: declare the BEFORE-CARRY purpose so the shared filter applies its before-carry curated/deny set.
            // Scoped TIGHTLY around ONLY this scan (G5): popped before the job is emitted / started, so it can never
            // be active at the needAccurateResult:false store probes elsewhere.
            using (StorageBuildingFilter.PushContext(StorageFilterContext.BeforeCarry))
            {
                bool filterActive = StorageBuildingFilter.Enabled
                    && StorageBuildingFilter.CurrentContext != StorageFilterContext.Unload;
                var filter = filterActive ? HaulersDreamMod.Settings?.storageBuildingFilter : null;

                IntVec3 closestSlot = IntVec3.Invalid;
                int closestDistSq = int.MaxValue;
                var groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
                for (int g = 0; g < groups.Count; g++)
                {
                    var slotGroup = groups[g];
                    var pr = slotGroup.Settings.Priority;
                    if (pr == StoragePriority.Unstored)
                        break; // priority-sorted; nothing below Unstored is a real destination

                    int prInt = (int)pr;
                    // List is priority-DESCENDING. Stop entirely once nothing further can be eligible:
                    //  - strictly below current is never a destination (WYU StoreUtility.cs:218);
                    //  - equal-to-current is never eligible when routeToEqualPriority is off (WYU :232 break).
                    if (prInt < currentPriorityInt || (prInt == currentPriorityInt && !allowEqualPriority))
                        break;

                    // WYU priority-eligibility (StorageRoutingPolicy.PriorityEligibleForRoute, beforeCarryActive=true):
                    // strictly-higher always; equal only when routeToEqualPriority; never the own group (WYU :233).
                    bool isOwnGroup = ownGroup != null && slotGroup == ownGroup;
                    if (!StorageRoutingPolicy.PriorityEligibleForRoute(
                            prInt, currentPriorityInt, beforeCarryActive: true,
                            allowEqualPriority: allowEqualPriority, isOwnGroup: isOwnGroup))
                        continue; // own group at an otherwise-eligible priority — skip, keep scanning equal/higher

                    // Stockpile / building gating (WYU StoreUtility.cs:222-241, before-carry branch).
                    var stockpile = slotGroup.parent as Zone_Stockpile;
                    var buildingStorage = slotGroup.parent as Building_Storage;
                    if (stockpile != null && !allowStockpiles)
                        continue; // routeToStockpiles off -> never relocate into a plain stockpile
                    if (buildingStorage != null && filter != null && !filter.IsGroupAllowed(slotGroup))
                        continue; // G7: building denied for before-carry

                    if (!slotGroup.parent.Accepts(thing))
                        continue; // WYU StoreUtility.cs:244

                    var cells = slotGroup.CellsList;
                    if (cells == null)
                        continue;
                    for (int c = 0; c < cells.Count; c++)
                    {
                        var cell = cells[c];
                        int distSq = EnRoutePickupPolicy.MidwayDistanceSquared(cell.x, cell.z, midX, midZ);
                        if (!StorageRoutingPolicy.MidwayBetter(distSq, closestDistSq))
                            continue;
                        if (!StoreUtility.IsGoodStoreCell(cell, map, thing, pawn, pawn.Faction))
                            continue;
                        // A linked StorageGroup pools cells from MULTIPLE buildings, so a denied building's cell must
                        // be dropped individually even when its group was allowed (mirrors BulkHaul / EnRoutePickup).
                        if (filter != null && !filter.IsCellAllowed(cell, map))
                            continue;
                        closestSlot = cell;
                        closestDistSq = distSq;
                    }
                }
                return closestSlot;
            }
        }

        // ---- shared eligibility for the seam patches -----------------------------------------------------

        /// <summary>
        /// Is <paramref name="pawn"/> a pawn HD may make an autonomous routing relocation for? A valid player-faction
        /// HD hauler with the comp, the auto-haul opt-out honored. NOT gated on the bleeding fitness check: WYU's
        /// <c>:Bleeding</c> exception (StoreUtility.cs:178) explicitly ALLOWS grabbing extra supplies (defense-
        /// critical) / ingredients (medicine) even while bleeding, and a relocation is a closer-storage MOVE, not an
        /// inventory intake — so the C1 bleeding intake-gate does not apply here (matching WYU intent). The per-pawn
        /// "Auto-haul yields" opt-out IS honored (a toggled-off pawn gets no HD-initiated detours).
        /// </summary>
        internal static bool MayRoute(Pawn pawn)
        {
            if (pawn == null || pawn.Faction != Faction.OfPlayerSilentFail || pawn.IsQuestLodger())
                return false;
            if (!YieldRouter.IsEligible(pawn))
                return false;
            if (pawn.IsFormingCaravan())
                return false;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            // Per-pawn "Auto-haul yields" opt-out: a toggled-off pawn never gets an HD-initiated routing detour
            // (matches what the gizmo tooltip promises and the scoop / sweep / en-route intake gates).
            return comp.autoHaulYields;
        }
    }

    /// <summary>
    /// G8 NO-RECURSION for storage routing: a Harmony prefix on <see cref="Pawn_JobTracker.TryOpportunisticJob"/>
    /// that suppresses the opportunistic prefix when the job being started is one of <see cref="StorageRouting"/>'s
    /// relocation hauls (a vanilla <c>HaulToCell</c> whose DEF allows the prefix, but which must never recursively
    /// spawn another opportunistic haul). Returns the finalizer if present (vanilla's own contract) else null.
    /// Byte-inert when storage routing is off (no relocation job is ever marked, so this never fires).
    ///
    /// <para>NOTE (scoped honesty): this stops VANILLA's opportunistic prefix on the relocation — the recursion G8
    /// names. HD's own en-route pickup (C2) is a POSTFIX that re-checks <c>job.def.allowOpportunisticPrefix</c>
    /// (true for HaulToCell), so it can still add ONE en-route pickup onto a relocation when en-route is enabled;
    /// that is bounded (the pickup is a HaulersDream_BulkHaul whose def forbids a further prefix) and benign — the
    /// relocation trip simply also sweeps a loose item on the way. A strictly non-opportunistic relocation def lives
    /// outside this wave's editable file set; this prefix is the strongest in-scope enforcement.</para>
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryOpportunisticJob))]
    public static class Patch_Pawn_JobTracker_TryOpportunisticJob_NoRouteRecurse
    {
        static bool Prefix(Job finalizerJob, Job job, ref Job __result)
        {
            // Only act on OUR marked relocation jobs; everything else runs vanilla (and HD's en-route postfix).
            if (!StorageRouting.IsRelocation(job))
                return true;
            // Mirror vanilla's own contract: a finalizer wins; otherwise no opportunistic prefix for a relocation.
            __result = finalizerJob;
            return false; // Halt — skip vanilla's opportunistic search for this relocation.
        }
    }
}
