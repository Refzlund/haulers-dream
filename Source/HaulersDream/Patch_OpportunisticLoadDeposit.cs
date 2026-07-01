using System.Collections.Generic;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// B1 — OPPORTUNISTIC UNLOAD-FIRST for the load path (BLFT parity gap #2; default OFF via
    /// <c>enableOpportunisticLoad</c>). When a pawn is ALREADY carrying HD-tagged inventory cargo and the
    /// think-tree hands it some unrelated, non-forced job, this postfix checks whether a needy load target
    /// (transporter group / portal / vehicle) within <c>loadOpportunityScanRadius</c> still wants some of that
    /// exact carried cargo. If so it DIVERTS the pawn to a DEPOSIT-ONLY bulk-load job into the nearest such
    /// target — shedding what it carries on the way — instead of walking past and later making a separate
    /// pickup+load trip. After depositing, the pawn re-picks work normally.
    ///
    /// Mirrors the cleanest existing carrying-pawn intercept, <see cref="Patch_JobGiver_Work_OpportunisticUnload"/>
    /// (a postfix on the same <see cref="JobGiver_Work.TryIssueJobPackage"/>), rather than inventing a new hook.
    /// It runs as a SECOND postfix on the same method; the two never both fire on one result because each diverts
    /// only when its own gate passes and they target disjoint outcomes (this one starts an HD deposit job; the
    /// other an HD unload-to-storage job). Both are gated on the pawn carrying tagged cargo.
    ///
    /// BYTE-INERT when OFF: the very first check is <c>enableOpportunisticLoad</c>; with it false the postfix
    /// returns immediately and no opportunistic job is ever built, so HD behaves exactly as before.
    ///
    /// DEPOSIT-ONLY (no fresh sweep): the built job has an EMPTY <c>targetQueueB</c>/<c>countQueue</c>, so the
    /// reused bulk-load driver short-circuits its fill phase and only deposits the carried tagged surplus. The
    /// existing transporter/portal/vehicle JobDefs + drivers already handle an empty sweep queue cleanly (their
    /// <c>TryMakePreToilReservations</c> returns true for an empty queue and the fill toils jump straight to the
    /// deposit loop), so NO new JobDef or driver is needed — see <see cref="BuildDepositOnlyJob"/>.
    ///
    /// CLAIM BALANCE (no double-claim): the deposit-only job records NO ledger claim. Its driver's
    /// <c>Notify_Starting → HaulersDreamGameComponent.LoadClaim</c> computes the plan from the (empty) sweep
    /// queue, so <c>ApplyClaim</c> gets an empty plan: it releases any prior claim this pawn held and records
    /// nothing. The deposit then settles via the over-deposit branch of <c>LoadLedger.Settle</c> (credit the
    /// unclaimed units into <c>totalClaimed</c>, then decrement by the deposited count) so <c>totalNeeded</c> and
    /// <c>totalClaimed</c> stay balanced exactly as for any deposit. On every non-Success end the driver's finish
    /// action calls <c>LoadReleaseClaimsForPawn</c> (idempotent; a no-op here since this job claimed nothing). The
    /// policy reads the ledger's <c>AvailableToClaim</c> ONLY to decide whether a divert is worthwhile — it never
    /// reserves against it.
    /// </summary>
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class Patch_OpportunisticLoadDeposit
    {
        // Run LAST among postfixes on TryIssueJobPackage so HD reacts to the FINAL chosen job — after a
        // job-substituting mod (e.g. "While You Are Nearby", which postfixes this same method to swap in a nearby
        // equivalent job) has had its say. Otherwise HD might divert off a job that mod is about to replace.
        [HarmonyPriority(Priority.Last)]
        static void Postfix(ref ThinkResult __result, Pawn pawn, JobGiver_Work __instance)
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.enableOpportunisticLoad)
                return; // feature OFF -> byte-inert
            // Only act on a freshly-issued, real, non-forced job for an eligible carrying pawn.
            if (!__result.IsValid || __result.Job == null)
                return;
            if (pawn?.Map == null || pawn.Drafted || pawn.inventory == null)
                return;
            var assigned = __result.Job;
            if (assigned.playerForced)
                return; // never pre-empt a player-prioritized job
            // Never divert off EMERGENCY / medical / rescue / firefighting work (see ProtectedWork) — the same
            // guard as the opportunistic-unload postfix. This feature is opt-in (enableOpportunisticLoad, default
            // OFF), but the medical protection applies identically.
            if (ProtectedWork.IsProtected(assigned, __instance.emergency))
                return;

            // Never divert off an HD job (it already owns/loads/uses the carried cargo) or vanilla's unload.
            // Identified by the driver living in THIS assembly (covers every HD job without enumerating defs) —
            // the same predicate OpportunisticUnload.ShouldDivert uses.
            if (assigned.def?.driverClass != null
                && assigned.def.driverClass.Assembly == typeof(Patch_OpportunisticLoadDeposit).Assembly)
                return;
            if (pawn.CurJobDef != null && pawn.CurJobDef.driverClass != null
                && pawn.CurJobDef.driverClass.Assembly == typeof(Patch_OpportunisticLoadDeposit).Assembly)
                return; // currently mid an HD job that owns the cargo

            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return;
            // Cheap read-only pre-gate: any tagged surplus carried at all? (PeekHashSet — no self-heal / mutation
            // on this hot scan path.) The total-surplus count for the policy gate is computed lazily below only if
            // a candidate target is found.
            var tagged = comp.PeekHashSet();
            if (tagged.Count == 0)
                return;

            float radius = Mathf.Max(0f, s.loadOpportunityScanRadius);
            if (radius <= 0f)
                return;

            // Find the best (nearest) needy target within radius that wants some carried surplus, plus the chosen
            // deposit count — the policy clamps to the smaller of carried-surplus and the target's available-to-claim.
            var loadable = FindBestOpportunity(pawn, comp, radius);
            if (loadable == null)
                return;

            var job = BuildDepositOnlyJob(pawn, loadable);
            if (job == null)
                return;
            // Issue as the work node's own think result (after work, like the sibling unload diverts). NOT
            // playerForced: this is an autonomous opportunistic divert, so urgent needs still outrank it and a
            // genuine player order is never overridden.
            __result = new ThinkResult(job, __result.SourceNode, __result.Tag, __result.FromQueue);
            HDLog.Dbg($"{pawn} opportunistically diverting to deposit carried cargo into "
                      + $"{loadable.GetParentThing()?.LabelShort} (kind {loadable.Kind}).");
        }

        /// <summary>
        /// Scan distinct things within <paramref name="radius"/> of the pawn, build the right adapter for each
        /// transporter / portal / vehicle, and return the NEAREST one whose ledger still has available-to-claim
        /// headroom for a def the pawn carries as tagged surplus (per <see cref="OpportunisticLoadPolicy"/>), or
        /// null. Read-only: registers/refreshes each candidate's ledger entry (idempotent) but records NO claim.
        /// </summary>
        private static IManagedLoadable FindBestOpportunity(Pawn pawn, CompHauledToInventory comp, float radius)
        {
            var map = pawn.Map;
            var ledger = HaulersDreamGameComponent.Instance;
            if (map == null || ledger == null)
                return null;

            // The pawn's carried tagged SURPLUS per def (the only thing a deposit-only divert can shed). Built once
            // and reused across every candidate. Empty -> nothing to divert for.
            var carried = CarriedSurplusByDef(pawn, comp);
            if (carried.Count == 0)
                return null;

            var s = HaulersDreamMod.Settings;
            IManagedLoadable best = null;
            float bestDistSq = float.MaxValue;
            var origin = pawn.Position;

            // RadialDistinctThingsAround returns each Thing once within the radius (verified Verse API). We classify
            // each into the matching adapter; non-load things are skipped cheaply.
            foreach (var thing in GenRadial.RadialDistinctThingsAround(origin, map, radius, useCenter: true))
            {
                if (thing == null || thing == pawn)
                    continue;
                var loadable = TryAdaptNeedy(thing, s);
                if (loadable == null)
                    continue;
                // Distance gate (straight-line; the policy's radius test is exact, this prunes the candidate set).
                float distSq = (thing.Position - origin).LengthHorizontalSquared;
                if (distSq >= bestDistSq)
                    continue; // a nearer candidate already found
                // Worthwhile? At least one carried def the target can still usefully receive (available-to-claim).
                if (!WantsAnyCarried(ledger, loadable, pawn, carried, radius, distSq))
                    continue;
                best = loadable;
                bestDistSq = distSq;
            }
            return best;
        }

        /// <summary>Build the loadable adapter for a clicked thing IF it is a needy load target whose family
        /// feature is enabled (transporter group with leftToLoad / portal mid-load / VF vehicle with cargo), else
        /// null. Mirrors the feature gating in <c>TransportLoad.FeatureEnabled</c> so an opportunistic divert
        /// honours the same per-family toggles as the normal load path.</summary>
        private static IManagedLoadable TryAdaptNeedy(Thing thing, HaulersDreamSettings s)
        {
            if (s == null)
                return null;
            // Transporter / shuttle group.
            var transporter = thing.TryGetComp<CompTransporter>();
            if (transporter != null)
            {
                if (!s.enableBulkLoadTransporters || !transporter.AnyInGroupHasAnythingLeftToLoad)
                    return null;
                return LoadTransportersAdapter.TryCreate(transporter);
            }
            // Map portal (pit gate / cave or vault exit).
            if (thing is MapPortal portal)
            {
                if (!s.enableBulkLoadPortal || !portal.LoadInProgress)
                    return null;
                return MapPortalBulkTarget.TryCreate(portal);
            }
            // Vehicle Framework vehicle (reflection-only; IsVehicle is false when VF is absent).
            if (s.enableVehicleFramework && s.enableBulkLoadVehicles
                && VehicleFrameworkCompat.IsActive && VehicleFrameworkCompat.IsVehicle(thing))
            {
                var cargo = VehicleFrameworkCompat.CargoToLoad(thing);
                if (cargo == null || cargo.Count == 0)
                    return null;
                return VehicleLoadTarget.TryCreate(thing);
            }
            return null;
        }

        /// <summary>True if, per <see cref="OpportunisticLoadPolicy"/>, the target (within radius) can still
        /// usefully receive at least one carried def — i.e. the ledger's available-to-claim for some carried def is
        /// positive and the policy yields a positive deposit count. Registers/refreshes the ledger entry
        /// (idempotent) without claiming.</summary>
        private static bool WantsAnyCarried(HaulersDreamGameComponent ledger, IManagedLoadable loadable, Pawn pawn,
            Dictionary<ThingDef, int> carried, float radius, float distSq)
        {
            ledger.LoadRegisterOrUpdate(loadable);
            // available-to-claim excludes OTHER pawns' in-flight claims AND this pawn's own (a re-plan is stable);
            // since this pawn holds no claim on the target yet, this is "manifest remaining minus others' claims".
            var avail = ledger.LoadAvailableToClaim(loadable, pawn);
            if (avail.Count == 0)
                return false;
            float dist = Mathf.Sqrt(distSq);
            bool busy = false; // gate's busy-check already applied by the caller before scanning
            foreach (var kv in carried)
            {
                int availForDef = avail.TryGetValue(kv.Key, out int a) ? a : 0;
                int n = OpportunisticLoadPolicy.ShouldDivertTo(
                    featureEnabled: true, carriedSurplusOfDef: kv.Value, targetAvailableForDef: availForDef,
                    distanceToTarget: dist, scanRadius: radius, alreadyOnHigherPriorityHdJob: busy);
                if (n > 0)
                    return true;
            }
            return false;
        }

        /// <summary>The pawn's carried tagged SURPLUS per def (units above its personal keep-stock, summed across
        /// stacks of the same def) — the only cargo a deposit-only divert can shed. Read-only (PeekHashSet); a
        /// stack still in inventory with positive <see cref="InventorySurplus.SurplusOf"/> contributes.</summary>
        private static Dictionary<ThingDef, int> CarriedSurplusByDef(Pawn pawn, CompHauledToInventory comp)
        {
            var result = new Dictionary<ThingDef, int>();
            var inner = pawn.inventory?.innerContainer;
            if (inner == null)
                return result;
            foreach (var t in comp.PeekHashSet())
            {
                if (t == null || t.Destroyed || t.def == null || !inner.Contains(t))
                    continue;
                int surplus = InventorySurplus.SurplusOf(pawn, t);
                if (surplus <= 0)
                    continue;
                result[t.def] = (result.TryGetValue(t.def, out int cur) ? cur : 0) + surplus;
            }
            return result;
        }

        /// <summary>
        /// Build a DEPOSIT-ONLY HD bulk-load job for <paramref name="loadable"/>: the correct family JobDef
        /// (transporter / portal / vehicle) with <c>targetA</c> = the load target and an EMPTY sweep queue. The
        /// reused driver skips its fill phase and only deposits the pawn's carried tagged surplus the target still
        /// wants. Returns null if the job can't make its (trivial, empty-queue) reservations.
        /// </summary>
        internal static Job BuildDepositOnlyJob(Pawn pawn, IManagedLoadable loadable)
        {
            var target = loadable.GetParentThing();
            if (target == null)
                return null;
            var job = JobMaker.MakeJob(JobDefForKind(loadable.Kind), target);
            // Empty sweep queue => deposit-only. The driver's TryMakePreToilReservations returns true for an empty
            // targetQueueB (reserves nothing), and its fill toils jump straight to the deposit loop. count=1 sentinel
            // (a -1 Job.count reads as "broken" in some vanilla checks), matching TransportLoad.TryGiveBulkJob.
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();
            job.count = 1;
            if (!job.TryMakePreToilReservations(pawn, false))
                return null;
            return job;
        }

        /// <summary>The bulk-load JobDef for a loadable kind — the same 3-way <c>TransportLoad.JobDefFor</c> uses.</summary>
        private static JobDef JobDefForKind(LoadableKind kind)
        {
            switch (kind)
            {
                case LoadableKind.Portal: return HaulersDreamDefOf.HaulersDream_LoadPortalInBulk;
                case LoadableKind.Vehicle: return HaulersDreamDefOf.HaulersDream_LoadVehicleInBulk;
                default: return HaulersDreamDefOf.HaulersDream_LoadTransportersInBulk;
            }
        }
    }
}
