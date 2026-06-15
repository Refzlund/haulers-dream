using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Delivers construction material to a single big needer (a frame/blueprint that wants more than one
    /// hand-stack of one material) by carrying it in the pawn's INVENTORY instead of its hands. The hands
    /// are stack-limited (~75 steel), so vanilla shuttles a generator's 340 steel in ~5 trips; the
    /// inventory is mass-limited, so the pawn loads a full capacity-load (more, with smart overload) and
    /// makes far fewer trips. Triggered by <see cref="Patch_ResourceDeliverJobFor_Inventory"/> only when
    /// the math says inventory beats hands; otherwise the vanilla hand-carry runs unchanged.
    ///
    /// Phase 1 (LOAD): walk to nearby floor stacks and <c>TakeToInventory</c> up to the smart ceiling /
    /// needer need. Phase 2 (DELIVER): walk to the needer once, then repeatedly pull a hand-sized chunk
    /// from inventory into the hands and deposit it, until the needer is full or the inventory runs out.
    /// Anything left over (the needer filled up first, or the job aborted) is registered for the normal
    /// unload pass — never carried forever, never lost.
    ///
    /// Item safety: every move uses vanilla toils (<c>TakeToInventory</c> = SplitOff+TryAdd;
    /// <c>StartCarryThing</c>/<c>DepositHauledThingInContainer</c>). On any job end, the job tracker drops
    /// a half-carried hands stack on the ground (decompile-verified <c>CleanupCurrentJob</c>) and the
    /// finish action releases enroute + registers inventory leftovers, so no unit is ever destroyed.
    /// </summary>
    public class JobDriver_OverloadConstructDeliver : JobDriver
    {
        private const TargetIndex ResourceInd = TargetIndex.A;       // transient: current load stack / carried thing
        private const TargetIndex NeederInd = TargetIndex.B;         // the frame/blueprint (container + needer)
        private const TargetIndex PrimaryNeederInd = TargetIndex.C;  // same needer (reserve-for cross-check)

        private ThingDef resourceDef;

        // How many units to LOAD this trip. Defaults to the job's creation-time count (one needer's worth); a
        // haul+build ROUTE sets job.count to the WHOLE remaining route's demand so later stops deliver from the
        // kept inventory without re-fetching. Captured into a field because the deliver loop reuses job.count
        // for its per-chunk hand size.
        private int loadTargetUnits;

        // Did this job actually move any units (loaded into inventory or deposited)? A 0-progress delivery must
        // NOT re-tether another delivery — an over-ceiling pawn would otherwise walk stockpile↔site forever
        // (load 0, deliver 0, re-order, repeat). The FinishFrame tether stays unconditional: it's gated by
        // Frame.IsCompleted(), so a 0-delivery arriving at an already-complete frame should still build it.
        private bool madeProgress;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref resourceDef, "hdResourceDef");
            Scribe_Values.Look(ref loadTargetUnits, "hdLoadTargetUnits", 0);
            Scribe_Values.Look(ref madeProgress, "hdMadeProgress", false);
        }

        private Thing Needer => job.GetTarget(NeederInd).Thing;
        private ThingOwner Inv => pawn.inventory?.GetDirectlyHeldThings();

        public override string GetReport()
        {
            var needer = Needer;
            if (resourceDef == null || needer == null)
                return "ReportHaulingUnknown".Translate();
            return "ReportHaulingTo".Translate(resourceDef.label, needer.LabelShort.Named("DESTINATION"), resourceDef.Named("THING"));
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (resourceDef == null)
                resourceDef = job.targetA.Thing?.def
                    ?? (job.targetQueueA != null && job.targetQueueA.Count > 0 ? job.targetQueueA[0].Thing?.def : null);
            if (loadTargetUnits <= 0)
                loadTargetUnits = job.count; // creation-time count, before the deliver loop reuses job.count

            // Reserve the floor resource stacks so two delivery pawns don't grab the same steel. The needer
            // is IHaulEnroute (no hard reservation) — we register enroute instead, exactly like vanilla.
            // EXCEPTION: a later ROUTE stop's primary stack may be gone (destroyed by a merge in an earlier
            // stop) while the pawn already CARRIES the gathered material — the load loop only needs the
            // inventory, so an unreservable/destroyed targetA must not silently kill a stop it can serve.
            if (job.targetA.HasThing && !pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)
                && (resourceDef == null || InventoryCountOfDef() <= 0))
                return false;
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(ResourceInd), job);

            // Declare intent to the enroute system so other pawns don't over-deliver to this needer.
            // GetSpaceRemainingWithEnroute excludes our own claim, so registering never blocks ourselves.
            if (Needer is IHaulEnroute enroute && resourceDef != null && !Needer.DestroyedOrNull() && pawn.Map != null)
            {
                int want = Mathf.Min(job.count, enroute.GetSpaceRemainingWithEnroute(resourceDef, pawn));
                if (want > 0)
                    pawn.Map.enrouteManager.AddEnroute(enroute, pawn, resourceDef, want);
            }
            return true;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            // Capture the site's CELL up front: the delivery itself can REPLACE the needer Thing
            // (blueprint→frame), so the tether must re-find the constructible by position, not reference.
            IntVec3 neederCell = Needer?.Position ?? IntVec3.Invalid;

            // Whatever the outcome, release OUR enroute claim on THIS needer (mirrors vanilla's
            // per-container ReleaseFor — never touches our claims on other needers) and hand any still-held
            // material to the unload pass. The job tracker itself drops a half-carried hands stack on cleanup.
            AddFinishAction(delegate
            {
                if (Needer is IHaulEnroute he)
                    pawn.Map?.enrouteManager?.ReleaseFor(he, pawn);
                // The HAUL+BUILD variant tethers the next step on this site — another material type's
                // delivery, or vanilla's FinishFrame once buildable — EnqueueFirst'd, so an ordered
                // construction is one continuous task (and in a route, the build runs BEFORE the next stop).
                if (job.def == HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild
                    && job.playerForced && neederCell.IsValid)
                    ConstructTether.QueueNext(pawn, neederCell, allowDeliverTether: madeProgress);
                RegisterLeftoverForUnload();
            });

            this.FailOn(() =>
            {
                var n = Needer;
                return n == null || n.Destroyed || !n.Spawned;
            });
            this.FailOnForbidden(NeederInd);

            // ---- PHASE 1: LOAD floor resource into inventory up to the smart ceiling / needer need ----

            Toil deliverGoto = (Needer is Blueprint || Needer is Frame)
                ? Toils_Goto.GotoBuild(NeederInd)
                : Toils_Goto.GotoThing(NeederInd, PathEndMode.Touch);

            // One-time ENTRY gate (runs once, before the fill loop): if the pawn already carries enough of
            // this material for the IMMEDIATE needer, skip the stockpile trip entirely and deliver from
            // inventory. In a haul+build route the pawn keeps a big batch from an earlier stop; without this
            // it walked back to the stockpile after every wall (its load target is the WHOLE route's demand,
            // which a single carry can never reach, and the mass headroom reopened on each deposit). Only the
            // ENTRY decision uses the immediate need; once it DOES enter, loadDecide's loop still fills toward
            // the whole-route ceiling, so a genuine re-load is a full batch (once per ceiling-worth), not a
            // per-frame top-off. The loop jumps back to loadDecide (not here), so this fires exactly once.
            Toil loadEntry = ToilMaker.MakeToil("HD_LoadEntry");
            loadEntry.initAction = () =>
            {
                if (resourceDef == null) { JumpToToil(deliverGoto); return; }
                if (!ConstructDeliveryPlan.ShouldLoadBeforeDeliver(InventoryCountOfDef(), SpaceInNeeder()))
                    JumpToToil(deliverGoto); // already carry enough for this frame (or it needs nothing) -> deliver
            };
            loadEntry.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadEntry;

            Toil loadDecide = ToilMaker.MakeToil("HD_LoadDecide");
            loadDecide.initAction = () =>
            {
                if (resourceDef == null) { JumpToToil(deliverGoto); return; }
                if (LoadTargetUnits() - InventoryCountOfDef() <= 0) { JumpToToil(deliverGoto); return; } // enough loaded
                if (OverloadHeadroomUnits() <= 0) { JumpToToil(deliverGoto); return; }                   // at ceiling
                Thing next = NextResourceStack();
                if (next == null) { JumpToToil(deliverGoto); return; }                                   // no more stock
                job.SetTarget(ResourceInd, next);
            };
            loadDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return loadDecide;

            Toil loadGoto = ToilMaker.MakeToil("HD_LoadGoto");
            loadGoto.initAction = () =>
            {
                var t = job.GetTarget(ResourceInd).Thing;
                if (t == null || !t.Spawned) { JumpToToil(loadDecide); return; }
                pawn.pather.StartPath(t, PathEndMode.ClosestTouch);
            };
            loadGoto.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return loadGoto;

            // No checkEncumbrance on the job, so TakeToInventory does NOT cap at over-encumbered (100%);
            // our own getter applies the smart overload ceiling instead.
            yield return Toils_Haul.TakeToInventory(ResourceInd, () =>
            {
                var st = job.GetTarget(ResourceInd).Thing;
                if (st == null || !st.Spawned) return 0;
                int more = LoadTargetUnits() - InventoryCountOfDef();
                if (more <= 0) return 0;
                int head = OverloadGate.CountToPickUp(pawn, st, HaulersDreamMod.Settings);
                int take = Mathf.Min(Mathf.Min(more, head), st.stackCount);
                if (take > 0)
                    madeProgress = true;
                return take;
            });

            yield return Toils_Jump.Jump(loadDecide);

            // ---- PHASE 2: walk to the needer once, then deposit from inventory in hand-sized chunks ----

            yield return deliverGoto;

            Toil done = ToilMaker.MakeToil("HD_Done");
            done.initAction = () => { };
            done.defaultCompleteMode = ToilCompleteMode.Instant;

            Toil deliverDecide = ToilMaker.MakeToil("HD_DeliverDecide");
            deliverDecide.initAction = () =>
            {
                var n = Needer;
                if (resourceDef == null || n == null || n.Destroyed) { JumpToToil(done); return; }
                int space = SpaceInNeeder();
                Thing inv = InventoryStackOfDef();
                if (space <= 0 || inv == null) { JumpToToil(done); return; }
                int chunk = Mathf.Min(Mathf.Min(space, inv.stackCount), pawn.carryTracker.MaxStackSpaceEver(resourceDef));
                if (chunk <= 0) { JumpToToil(done); return; }
                madeProgress = true; // a deposit chunk is about to move — this job genuinely advanced the site
                job.count = chunk;
                job.SetTarget(ResourceInd, inv);
            };
            deliverDecide.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return deliverDecide;

            // Pull the chunk from our OWN inventory into the hands, then deposit it into the needer.
            yield return Toils_Haul.StartCarryThing(ResourceInd, putRemainderInQueue: false,
                subtractNumTakenFromJobCount: false, failIfStackCountLessThanJobCount: false,
                reserve: false, canTakeFromInventory: true);
            // A BLUEPRINT needer is not a container (only Frame has the resource ThingOwner) — vanilla's
            // deliver driver always converts blueprint→frame immediately before its deposit (decompile-
            // verified JobDriver_HaulToContainer toil order). Without it the deposit errors and transfers
            // nothing, and the next StartCarryThing throws with the hands already full. The toil re-points
            // B/C at the created frame and self-ends the job when construction is blocked.
            yield return Toils_Construct.MakeSolidThingFromBlueprintIfNecessary(NeederInd, PrimaryNeederInd);
            yield return Toils_Haul.DepositHauledThingInContainer(NeederInd, PrimaryNeederInd);
            yield return Toils_Jump.Jump(deliverDecide);

            yield return done; // finish action releases enroute + registers any leftover for unload
        }

        // ---- helpers ----

        private int SpaceInNeeder()
        {
            var n = Needer;
            if (n == null || resourceDef == null)
                return 0;
            if (n is IHaulEnroute he)
                return Mathf.Max(0, he.GetSpaceRemainingWithEnroute(resourceDef, pawn));
            if (n is IConstructible ic)
                return Mathf.Max(0, ic.ThingCountNeeded(resourceDef));
            return 0;
        }

        /// <summary>Units worth LOADING this trip: at least this needer's need, or the whole-route demand a
        /// haul+build route stamped into job.count at creation (whichever is larger).</summary>
        private int LoadTargetUnits() => Mathf.Max(SpaceInNeeder(), loadTargetUnits);

        /// <summary>More construction work queued after this job (route stops / a tethered build)? Then the
        /// carried surplus is the NEXT stop's material, not a leftover.</summary>
        private bool MoreConstructWorkQueued()
        {
            var q = pawn.jobs?.jobQueue;
            if (q == null)
                return false;
            for (int i = 0; i < q.Count; i++)
            {
                var def = q[i]?.job?.def;
                if (def == HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver
                    || def == HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild
                    || def == JobDefOf.FinishFrame)
                    return true;
            }
            return false;
        }

        private Thing InventoryStackOfDef() => YieldRouter.InventoryStackOfDef(Inv, resourceDef);

        private int InventoryCountOfDef()
        {
            var owner = Inv;
            if (owner == null || resourceDef == null)
                return 0;
            int total = 0;
            for (int i = 0; i < owner.Count; i++)
                if (owner[i]?.def == resourceDef)
                    total += owner[i].stackCount;
            return total;
        }

        /// <summary>How many more units of <see cref="resourceDef"/> fit before hitting the smart ceiling now.</summary>
        private int OverloadHeadroomUnits()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || resourceDef == null)
                return 0;
            // Combat Extended: a bulk-full pawn has no headroom even when weight says otherwise — without this
            // the load loop would walk every queued stack taking 0 (the take getter is bulk-clamped) for nothing.
            if (CECompat.IsActive && CECompat.AvailableBulk(pawn) <= 0f)
                return 0;
            float maxCap = MassUtility.Capacity(pawn);
            float baseCap = CarryMath.EffectiveCapacity(maxCap, s.carryLimitFraction);
            float cur = MassUtility.GearAndInventoryMass(pawn);
            float unit = resourceDef.GetStatValueAbstract(StatDefOf.Mass);
            // Pawn-aware (NoOverloadFor): strict / slider-Off / CE — and an ANIMAL (non-mech non-humanlike,
            // never slowed by StatPart) must not get penalty-free overload headroom. Player mechs DO overload
            // here and are slowed for it, like colonists.
            int level = OverloadGate.NoOverloadFor(pawn, s) ? OverloadTuning.OffLevel : s.overloadLevel;
            return OverloadPolicy.UnitsToCarry(level, maxCap, baseCap, cur, unit,
                demandUnits: int.MaxValue, availableUnits: int.MaxValue);
        }

        private Thing NextResourceStack()
        {
            var queue = job.GetTargetQueue(ResourceInd);
            while (queue != null && queue.Count > 0)
            {
                Thing t = queue[0].Thing;
                queue.RemoveAt(0);
                if (t != null && t.Spawned && !t.Destroyed && !t.IsForbidden(pawn)
                    && (pawn.CanReserve(t) || pawn.Map.reservationManager.ReservedBy(t, pawn, job)))
                    return t;
            }
            return null;
        }

        private void RegisterLeftoverForUnload()
        {
            // Always track speculatively-loaded material that didn't make it into the needer, regardless of
            // the auto-unload setting — this is a feature that pre-fills inventory, so untracked leftovers
            // would otherwise accumulate. NOTE: with markForUnload ON the idle/interval/full triggers reclaim
            // the tagged stock; with it OFF those triggers are all gated away, so we must queue the unload
            // ourselves below or the leftovers would strand (recoverable only via the manual gizmo).
            if (resourceDef == null)
                return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            var owner = Inv;
            if (comp == null || owner == null)
                return;
            bool registeredAny = false;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t != null && t.def == resourceDef)
                {
                    comp.RegisterHauledItem(t);
                    registeredAny = true;
                }
            }
            var s = HaulersDreamMod.Settings;
            // SUSPENSION keeps the job queued for resume with the inventory intact — flushing the load to
            // storage now would force a full re-gather on resume. (SuspendCurrentJob enqueues the job BEFORE
            // cleanup runs, so "this job is in my own queue" identifies a suspend at finish-action time.)
            // The same applies MID-ROUTE: queued construct/build jobs will consume the carried surplus, so
            // flushing between stops would defeat the haul+build route's "keep wood in inventory".
            bool suspended = false;
            var q = pawn.jobs?.jobQueue;
            if (q != null)
                for (int i = 0; i < q.Count; i++)
                    if (q[i]?.job == job) { suspended = true; break; }
            if (registeredAny && !suspended && !MoreConstructWorkQueued() && s != null && !s.markForUnload)
                PawnUnloadChecker.CheckIfShouldUnload(pawn, forced: true);
        }
    }
}
