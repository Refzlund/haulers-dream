using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Walks the bulk-haul pickup plan (see <see cref="BulkHaul"/>): visit each planned stack in chain order,
    /// load the planned count into INVENTORY (tagged in <see cref="CompHauledToInventory"/>), and when the
    /// chain is done force the single storage-aware unload pass — one trip to storage for the whole sweep.
    ///
    /// Safety by construction: this job only MOVES things (SplitOff + TryAdd, with a place-back fallback) and
    /// tags them, so anything loaded is reclaimed by the unload pass no matter how the job ends — interrupted
    /// by a threat, a stack sniped mid-walk, whatever. Per-stack validity is re-checked at each step (skip,
    /// never fail the whole job), and the live mass ceiling is re-applied at pickup time so a mid-job change
    /// (gear picked up, settings changed) can't over-load past the worth-it point.
    ///
    /// Stacks are added WITHOUT merging into the pawn's PRE-EXISTING (personal/untagged) stock: tagging a
    /// merged stack would also flag the pawn's own stock (a packed lunch of the same meal def) for unload.
    /// But swept stacks DO consolidate WITH EACH OTHER (and with already-HD-tagged same-def stock) — see
    /// <see cref="DepositSwept"/>: a fresh split is first absorbed into the already-tagged same-def stacks
    /// (merging stays strictly inside the hauled set), and only the remainder becomes a new separate tagged
    /// stack. So a sweep of N same-def stacks ends as ONE inventory stack per def (bounded by the stack limit)
    /// instead of N loose entries — fewer tags, a smaller unload, no churn — while the tag-isolation invariant
    /// (never touch personal stock) is preserved exactly. (The comp's def-level self-heal may still re-tag a
    /// same-def personal stack later — known, accepted; the unload's surplus math keeps personal kept-stock.)
    /// </summary>
    public class JobDriver_BulkHaul : JobDriver
    {
        private const TargetIndex PrimaryInd = TargetIndex.A; // the clicked/assigned haulable (report anchor)
        private const TargetIndex StackInd = TargetIndex.B;   // scratch: the stack currently being walked to

        private int loadIndex;
        private bool loadedAnything;

        // The loop-reentry toil, kept so a pathing failure can jump back to it instead of ending the whole
        // job (see Notify_PatherFailed below). Assigned once in MakeNewToils, same convention as
        // JobDriver_SelfPickup's loop field.
        private Toil loadDecideToil;

        /// <summary>
        /// A mid-walk pathing failure to ONE swept stack must not cost the pawn the whole sweep (issue #160,
        /// same fix as JobDriver_SelfPickup): the chain can include stacks queued minutes earlier, and by the
        /// time an older one is walked to, another pawn or a freshly-placed stack in the same busy work area
        /// can have transiently blocked its only approach, a race no reachability check taken at plan time can
        /// foresee. The vanilla default (JobDriver.Notify_PatherFailed) ends the job as ErroredPather, and
        /// Pawn_JobTracker.EndCurrentJob's response to that condition is a hardcoded, uninterruptible 250-tick
        /// JobDefOf.Wait (decompile-verified) that a freshly queued job can't preempt. Advancing loadIndex and
        /// jumping back to loadDecide mirrors exactly what loadDecide/loadGoto/take already do for every OTHER
        /// invalid-stack case (despawned, forbidden, claimed): skip this one step, keep walking the rest of
        /// the chain, flush whatever loaded at the end regardless.
        /// </summary>
        public override void Notify_PatherFailed()
        {
            loadIndex++;
            JumpToToil(loadDecideToil);
        }

        // MP determinism: reused snapshot of the tagged set for the step-1 fold in DepositSwept, so absorbers are
        // visited in a client-stable thingIDNumber order. [ThreadStatic] + lazy-init matches this assembly's
        // hook-reachable scratch convention; cleared at use, never trusted empty / never aliased into job state.
        [System.ThreadStatic] private static List<Thing> foldScratch;

        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        /// <summary>Still walking the pickup chain (hasn't reached the storage flush), so a newly-ordered nearby
        /// item appended to <see cref="Verse.AI.Job.targetQueueB"/> will still be swept by the loadDecide loop.
        /// Once loadIndex passes the queue end the chain is done and an append would never be picked up — the
        /// takeover then routes the new order elsewhere instead of silently dropping it.</summary>
        internal bool IsStillLoading => job?.targetQueueB != null && loadIndex < job.targetQueueB.Count;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref loadIndex, "hdBulkLoadIndex", 0);
            Scribe_Values.Look(ref loadedAnything, "hdBulkLoadedAnything", false);
        }

        public override string GetReport() => "HaulersDream.BulkHaul.Report".Translate();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // The primary must be ours (it's what the work scan / order assigned); the rest of the sweep is
            // best-effort — a stack another pawn reserved first is simply skipped by the per-step validity.
            var queue = job.GetTargetQueue(StackInd);
            if (queue == null || queue.Count == 0)
                return false;
            if (!pawn.Reserve(queue[0], job, 1, -1, null, errorOnFailed))
                return false;
            pawn.ReserveAsManyAsPossible(queue, job);
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            // Whatever way the job ends — completed, interrupted, target gone — the swept stock is tagged, so
            // flush it to storage now ("when done THEN unload"). With nothing loaded this is a cheap no-op.
            AddFinishAction(delegate
            {
                // behindQueuedWork: a player order interrupting this job (TryTakeOrderedJob EnqueueFirst's
                // the order, then ends us) must not be preempted by the flush — the unload queues BEHIND it.
                if (loadedAnything)
                    PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true, behindQueuedWork: true);
            });

            Toil end = Toils_General.Label();

            Toil loadDecide = ToilMaker.MakeToil("HD_Bulk_LoadDecide");
            loadDecideToil = loadDecide;
            loadDecide.initAction = delegate
            {
                var queue = job.targetQueueB;
                var counts = job.countQueue;
                float ceiling = CeilingKgLive(HaulersDreamMod.Settings);
                bool roomLeft = float.IsPositiveInfinity(ceiling)
                                || MassUtility.GearAndInventoryMass(pawn) < ceiling - 0.0001f;
                // Under CE the live BULK room can fill before weight does — touring the remaining stacks
                // to take 0 from each is pure walking; end the chain and flush what's loaded.
                if (roomLeft && CECompat.IsActive && CECompat.AvailableBulk(pawn) <= 0f)
                    roomLeft = false;
                while (roomLeft && queue != null && loadIndex < queue.Count)
                {
                    var t = queue[loadIndex].Thing;
                    // The playerForced primary may be forbidden (that's what forcing means); swept extras are
                    // never taken while forbidden. Stacks in someone's inventory (claimed mid-walk) are gone.
                    bool forbiddenOk = t != null && !t.IsForbidden(pawn);
                    if (!forbiddenOk && loadIndex == 0 && job.playerForced)
                        forbiddenOk = true;
                    bool valid = t != null && t.Spawned && forbiddenOk
                                 && !(t.ParentHolder is Pawn_InventoryTracker)
                                 && counts != null && loadIndex < counts.Count && counts[loadIndex] > 0
                                 // A swept extra that reached its valid BEST storage between plan and pickup
                                 // (another hauler stored it, or this pawn hand-delivered it as an earlier
                                 // order) must not be pulled back OUT — skip it. Best (not just valid) storage
                                 // so upgrade-sweeps from worse storage still work; the primary keeps vanilla's
                                 // semantics (it's what the work scan / order assigned).
                                 && (loadIndex == 0 || !t.IsInValidBestStorage());
                    // #214 execution-time re-gate (defense-in-depth for the RimIOT terminal loop). BulkHaul's
                    // plan-time gates checked the stack's cell when the job was BUILT, but a foreign patch (RimIOT's
                    // Patch_StartPath_NetworkItemRedirect, priority 200) can rewrite this job's pickup target into a
                    // network-INTERNAL stack AFTER that, during loadGoto's StartPath. So re-check the CURRENT stack's
                    // cell here: on the AUTOMATIC path never pocket a stack now sitting in RimIOT-handled storage
                    // (pocketing it out of the network and force-unloading it back is the net-zero loop). See RegateRimIOT.
                    if (valid && ShouldSkipRimIOTRetarget(t, loadIndex))
                        valid = false;
                    // RESERVE at the walk, not just at job start: start-time ReserveAsManyAsPossible may have
                    // failed for this stack (and the conflict since cleared), and a bare CanReserve leaves it
                    // up for grabs — another pawn could reserve it mid-walk and we'd yank it anyway (vanilla
                    // never steals reserved stacks). CanReserve gates the Reserve call: on a playerForced
                    // sweep, Reserve's ignoreOtherReservations branch would otherwise STEAL a contested extra
                    // (force-ending the holder's job) — forcing covers the primary, not the swept extras.
                    // On failure the entry is skipped like any other invalid one. Reservations taken here are
                    // job-bound and release with the rest at job end (Pawn_JobTracker.CleanupCurrentJob →
                    // ClearReservationsForJob, decompile-verified).
                    if (valid && !pawn.Map.reservationManager.ReservedBy(t, pawn, job)
                        && (!pawn.CanReserve(t) || !pawn.Reserve(t, job, errorOnFailed: false)))
                        valid = false;
                    if (valid)
                        break;
                    loadIndex++;
                }
                if (!roomLeft || queue == null || loadIndex >= queue.Count) { JumpToToil(end); return; }
                job.SetTarget(StackInd, queue[loadIndex].Thing);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_Bulk_LoadGoto");
            loadGoto.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                if (t == null || !t.Spawned) { loadIndex++; JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            // Vanilla-like pickup pause (#121): the wait-with-progress-bar vanilla's JobDriver_TakeInventory
            // shows for a player "Pick up" order, paid once per swept stack (between arrival and the take,
            // inside the decide->goto->take jump loop). Deliberately NO fail conditions: a stack sniped or
            // forbidden mid-pause must SKIP (the take re-validates and advances), never fail the whole sweep.
            //
            // Scope (PickupDelayPolicy.ShouldPause), corrected from playerForced (issue #159/#156): this ONE
            // driver services several player orders with different vanilla equivalents: "Pick up X" mimics
            // vanilla's delayed TakeInventory order, but "Prioritize hauling" (including a shift-queued 2nd
            // order taking over the sweep) and "Haul everything nearby" mimic vanilla's HaulToCell, which is
            // NEVER paced. All of them set job.playerForced = true (it just means "the player ordered this"),
            // so that flag can't tell a carry-into-inventory order from a haul-to-storage one, and using it here
            // wrongly paced the two storage-bound orders. job.takeInventoryDelay is vanilla's OWN field for
            // exactly "this job takes something into inventory with a delay" (BuildPickUpJob is the only
            // builder that sets it, mirroring vanilla's own "Pick up" float-menu write), so it identifies ONLY
            // that order regardless of playerForced. Read once here; the field never changes for the job's life.
            yield return PickupPause.MakeToil(StackInd,
                job.takeInventoryDelay > 0 ? PickupDelayContext.ManualCarry : PickupDelayContext.AutoHaul);

            Toil take = ToilMaker.MakeToil("HD_Bulk_Take");
            take.initAction = delegate
            {
                var t = job.GetTarget(StackInd).Thing;
                var counts = job.countQueue;
                int planned = counts != null && loadIndex < counts.Count ? counts[loadIndex] : 0;
                if (t == null || !t.Spawned || planned <= 0) { loadIndex++; JumpToToil(loadDecide); return; }
                // Forbiddance re-check at pickup time: the player may have forbidden the stack mid-walk
                // (and the unload pass would later erase the forbid flag). Same exemption as loadDecide:
                // the playerForced primary may be forbidden — that's what forcing means.
                if (t.IsForbidden(pawn) && !(loadIndex == 0 && job.playerForced)) { loadIndex++; JumpToToil(loadDecide); return; }
                // Same re-check as loadDecide: a swept extra stored (best) mid-walk stays in storage.
                if (loadIndex != 0 && t.IsInValidBestStorage()) { loadIndex++; JumpToToil(loadDecide); return; }

                // #214 execution-time re-gate (see loadDecide + ShouldSkipRimIOTRetarget). This is the ACTUAL
                // pocket site: RimIOT rewrites targetB during loadGoto's StartPath, so the swap is only visible
                // here. Never pocket a network-handled stack on the automatic path; skip it (nothing loads, so
                // the finish flush queues no unload) and surface + back off the foreign retarget once.
                if (ShouldSkipRimIOTRetarget(t, loadIndex)) { loadIndex++; JumpToToil(loadDecide); return; }

                // Auto-strip-on-haul parity for a corpse pickup ("Pick up X", or "Haul everything nearby"
                // anchored on a corpse — the two corpse entries into this driver; the automatic scan never
                // assigns one): the hand-haul path strips at the
                // Pawn_CarryTracker.TryStartCarry seam, which an inventory pickup never crosses, so mirror it
                // here — same timing (the corpse is still spawned, the pickup is committed to this stack) and
                // the same self-gating call (mode / faction / opt-out / QualifyingHaul — which recognizes this
                // job def under AllHauls only). Runs BEFORE the mass clamp below so the scooped loot's weight
                // is counted before deciding how much of the corpse still fits. A throw here is a real bug and
                // stays visible (the toil error names this driver), matching the seam guard's philosophy.
                if (t is Corpse corpsePickup)
                    CorpseStripper.MaybeStripForHaul(pawn, corpsePickup);

                // Re-clamp the planned count to the LIVE remaining room (mass may have shifted since planning).
                int count = BulkHaulPolicy.CountWithinCeiling(CeilingKgLive(HaulersDreamMod.Settings),
                    MassUtility.GearAndInventoryMass(pawn), t.GetStatValue(StatDefOf.Mass),
                    System.Math.Min(planned, t.stackCount));
                // Under Combat Extended also clamp to CE's live weight+bulk fit (exact — CE's inventory cache
                // updates after every add, so the plan's optimism self-corrects here).
                count = System.Math.Min(count, CECompat.MaxFitCount(pawn, t));
                if (count <= 0) { loadIndex++; JumpToToil(loadDecide); return; }

                // SplitOff with count >= stackCount despawns the thing itself (the full-stack pickup path);
                // a partial split returns a fresh unspawned thing. Either way TryAdd takes the plain-add path.
                var split = t.SplitOff(count);
                if (DepositSwept(split))
                    loadedAnything = true;
                loadIndex++;
                JumpToToil(loadDecide);
            };
            take.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return take;

            yield return end;
        }

        /// <summary>
        /// #214 defense-in-depth: whether the CURRENT pickup stack <paramref name="t"/> must be skipped because a
        /// foreign patch rewrote this AUTOMATIC bulk job's target into RimIOT-handled storage AFTER BulkHaul's
        /// plan-time gates ran (RimIOT's <c>Patch_StartPath_NetworkItemRedirect</c> swaps <c>targetB</c> during
        /// loadGoto's StartPath, pointing the pickup at a network-INTERNAL stack). Pocketing that stack out of the
        /// network and force-unloading it back is the net-zero terminal loop (#177/#184/#192/#214).
        ///
        /// <para>Forced player orders are never re-gated: their one-shot semantics can't infinite-loop and the
        /// player asked for it. When slot 0 (the anchor) is the swapped one (<paramref name="t"/> is no longer the
        /// assigned primary; HD itself never swaps slot 0, only appends), the swap is a FOREIGN retarget:
        /// <see cref="HaulChurnGuard.NoteForeignRetarget"/> surfaces it once and backs the real anchor off the
        /// automatic haul scan so the loop cannot re-arm next tick. Inert without RimIOT
        /// (<see cref="RimIOTCompat.IsPresent"/> is false).</para>
        /// </summary>
        /// <param name="t">The stack the driver is about to reserve/pocket (the job's current StackInd target).</param>
        /// <param name="loadIndex">The chain position being loaded; slot 0 is the assigned anchor.</param>
        private bool ShouldSkipRimIOTRetarget(Thing t, int loadIndex)
        {
            if (t == null || job.playerForced || !RimIOTCompat.IsPresent
                || !RimIOTCompat.IsRimIOTHandledCell(pawn.Map, t.Position))
                return false;
            if (loadIndex == 0)
            {
                var anchor = job.GetTarget(PrimaryInd).Thing;
                if (anchor != null && anchor != t)
                    HaulChurnGuard.NoteForeignRetarget(pawn, anchor, t);
            }
            return true;
        }

        /// <summary>
        /// Put a freshly-split swept stack into inventory, CONSOLIDATING it with the sweep's already-tagged
        /// same-def stock (#2B) while NEVER merging into the pawn's pre-existing personal/untagged stock.
        ///
        /// Tag isolation by construction: candidate absorbers are taken ONLY from the comp's tracked set
        /// (<see cref="CompHauledToInventory.PeekHashSet"/> — the HD-tagged stacks), so a same-def packed lunch
        /// the pawn already carried (which is never in that set unless the def-level self-heal claimed it) is
        /// untouched. We first pour <paramref name="split"/> into those tagged absorbers (respecting the stack
        /// limit, so an absorber never overflows), then add any remainder as a NEW separate tagged stack with
        /// <c>canMergeWithExistingStacks:false</c> — the exact isolation the old unconditional false-add gave,
        /// now only for the part that didn't fold into the hauled set. Net result: one inventory stack per def
        /// for the whole sweep (bounded by the stack limit), with personal stock provably never merged into.
        ///
        /// CE: a merge that GREW a tagged absorber re-notifies CE's HoldTracker of the moved delta (so loadout
        /// enforcement won't dump the growth); a brand-new tagged stack notifies the full count via RegisterHauledItem.
        /// </summary>
        /// <returns>true if any units landed in inventory (the caller marks the job as having loaded something).</returns>
        private bool DepositSwept(Thing split)
        {
            if (split == null || split.Destroyed || split.stackCount <= 0)
                return false;
            var inv = Inv;
            if (inv == null)
            {
                // No inventory owner (shouldn't happen) — never let the split vanish; put it back on the ground.
                if (!split.Spawned)
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                return false;
            }
            var comp = pawn.GetComp<CompHauledToInventory>();
            bool loaded = false;

            // 1) Consolidate into the sweep's already-tagged same-def stock — merging stays strictly inside the
            //    hauled set. Iterate the tracked tags (PeekHashSet has no side effects, but may list stale/foreign
            //    entries, so guard each: live, in THIS inventory, same def, stackable with the split, has room).
            //    Direct iteration is allocation-free and safe: TryAbsorbStack only ever Destroys the SPLIT (the
            //    source, which is NOT in the set), absorbers only GROW, and RegisterHauledItem on an already-tagged
            //    absorber is a no-op on the set (Add returns false → CE-notify only) — so the set never mutates
            //    during the loop. (The for-loop guard re-checks split each pass, so a fully-folded split exits.)
            //    MP determinism: this folds `split` into the pawn's OWN same-def tagged stacks. The per-def TOTAL is
            //    order-independent, BUT TryAbsorbStack fills greedily to the stack limit, so WHICH tagged stack holds
            //    the partial remainder depends on iteration order — and PeekHashSet's order can differ per client
            //    (e.g. a mid-game joiner's set was rebuilt in a different order). A later capacity/manifest-bound
            //    deposit reads InventorySurplus.SurplusOf PER STACK, so a divergent per-stack distribution could
            //    diverge an intermediate deposit. So snapshot + sort by thingIDNumber and fold in that stable order
            //    (matching the sibling deposit/sweep sites). TryAbsorbStack only Destroys the SPLIT (the source, not
            //    in the snapshot) and absorbers only grow, so iterating the snapshot is safe.
            if (comp != null)
            {
                var fold = foldScratch ?? (foldScratch = new List<Thing>());
                fold.Clear();
                // PeekHashSet (no self-heal) may hold null tags; skip nulls so the sort comparator never NPEs.
                foreach (var t in comp.PeekHashSet())
                    if (t != null)
                        fold.Add(t);
                fold.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                for (int fi = 0; fi < fold.Count; fi++)
                {
                    var target = fold[fi];
                    if (split.Destroyed || split.stackCount <= 0)
                        break; // fully folded into the hauled set
                    if (target == split || target.Destroyed || target.def != split.def)
                        continue;
                    if (!inv.Contains(target) || target.stackCount >= target.def.stackLimit || !target.CanStackWith(split))
                        continue;
                    int before = split.stackCount;
                    target.TryAbsorbStack(split, respectStackLimit: true); // moves up to the absorber's room
                    int moved = before - split.stackCount;
                    if (moved > 0)
                    {
                        // Re-notify CE of the growth on the absorber (already tagged; RegisterHauledItem with a
                        // positive mergedCount notifies only the delta — Add is a no-op so the set is unchanged).
                        // The pickup clock is refreshed once below.
                        comp.RegisterHauledItem(target, moved);
                        loaded = true;
                    }
                }
                fold.Clear();
            }

            // 2) Anything left becomes a NEW separate tagged stack — the exact isolation the old false-add gave.
            if (!split.Destroyed && split.stackCount > 0)
            {
                if (inv.TryAdd(split, canMergeWithExistingStacks: false))
                {
                    comp?.RegisterHauledItem(split);
                    // Unspawned splits carry a default (0,0,0) position; the shared-inventory chooser ranks
                    // carried stock by position, so stamp the pawn's cell (a plain field write when unspawned).
                    if (!split.Spawned)
                        split.Position = pawn.Position;
                    loaded = true;
                }
                else if (!split.Destroyed && !split.Spawned)
                {
                    // Add failed (shouldn't happen — pawn inventories are effectively unbounded): put it back
                    // on the ground rather than ever letting an item vanish.
                    GenPlace.TryPlaceThing(split, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
            }

            if (loaded)
                comp?.NotifyYieldPicked();
            return loaded;
        }

        // The live worth-it mass ceiling for THIS pawn (per-pawn base cap × the overload break-even ratio).
        // Pawn-aware gate (NoOverloadFor): only an ANIMAL (non-mech non-humanlike) stands down to the plain
        // carry limit; player humanlikes AND mechs overload to the break-even ceiling and are slowed by
        // StatPart_Overload for it.
        private float CeilingKgLive(HaulersDreamSettings s)
        {
            // Null settings fails STRICT like every other null-settings fallback in the mod
            // (OverloadGate.NoOverload(null) == true): the ceiling is the plain base capacity, never
            // infinite. Unreachable in practice — the plan is only ever built with live settings.
            if (s == null)
                return CarryMath.EffectiveCapacity(CarryCapacity.Of(pawn), CarryMath.MaxFraction);
            float baseCap = CarryMath.EffectiveCapacity(CarryCapacity.Of(pawn), s.carryLimitFraction);
            return BulkHaulPolicy.CeilingKg(s.overloadLevel, OverloadGate.NoOverloadFor(pawn, s), baseCap,
                OverloadGate.MaxCeilingKg(s));
        }
    }
}
