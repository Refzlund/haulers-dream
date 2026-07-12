using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        // --- Deep-drill yield leash (#187b) ---
        // A deep drill is a ToilCompleteMode.Never job (JobDriver_OperateDeepDrill, decompile-verified) that
        // never ends, so the producer's front-queued self-pickup — enqueued when each portion drops
        // (YieldRouter.RecordSelfPickup) — can NEVER start: the running drill job never ends, so the queued job
        // just waits, and the "Drop & haul" portions pile on the ground beside the drill indefinitely (#187b).
        // Every DrillTickInterval ticks we scan drillers and, for one whose pending pile has grown past
        // DrillSection (and whose per-pawn cooldown has elapsed), ensure the self-pickup is queued and briefly
        // interrupt the drill so it runs. The pawn scoops the pile (~1 tile away) and the work scan re-issues the
        // drill; portion progress lives on the building's CompDeepDrill, so nothing is lost.
        private const int DrillTickInterval = 120;            // scan drillers ~every 2s
        private const int DrillSection = 8;                   // pending pile size that triggers a collection break
        private const int DrillInterruptCooldownTicks = 300;  // min spacing between one pawn's drill interrupts (~5s)

        // Per-pawn tick of the last drill interrupt, so a driller isn't churned every scan (it must get back to
        // drilling for DrillInterruptCooldownTicks between collections). Transient in-flight timing, not scribed:
        // constructed fresh with the component on every load, and the scan only ever reads LIVE drillers, so a
        // despawned/dead pawn's stale entry is simply never read again (a handful of driller entries per game —
        // negligible). Pawn keys use reference identity, so a reloaded pawn (a new instance) never collides.
        private readonly Dictionary<Pawn, int> lastDrillInterruptTick = new Dictionary<Pawn, int>();

        // The deep-drill collection backstop: on the interval, interrupt a driller with a big enough pending pile
        // so its front-queued self-pickup runs. Self-gates on the interval (byte-inert on off-interval ticks);
        // called unconditionally from GameComponentTick (mirrors RunSoftlockDropDriver).
        private void RunYieldLeash(int tick)
        {
            if (tick % DrillTickInterval != 0)
                return;
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                    MaybeInterruptDrillForCollection(pawns[i], tick);
            }
        }

        // One pawn: if it is operating a deep drill, is a scoop-eligible producer, and its pending pile has grown
        // past DrillSection with the per-pawn cooldown elapsed, queue the self-pickup and end the drill so it
        // runs. Only ever targets drillers — every other pawn returns at the first gate.
        private void MaybeInterruptDrillForCollection(Pawn pawn, int tick)
        {
            if (pawn?.jobs == null || !(pawn.jobs.curDriver is JobDriver_OperateDeepDrill))
                return; // the interrupt is scoped strictly to a pawn actively operating a deep drill
            // Same eligibility gate as scoop time (master switch, map, race, opt-out, bleeding): a pawn that
            // isn't currently a scoop candidate must not have its drill interrupted for a collection it won't run.
            if (!YieldRouter.IsCandidate(pawn))
                return;
            var comp = pawn.GetComp<CompHauledToInventory>();
            if (comp == null)
                return;
            // A DropThenHaul drill's portions are what fill pendingSelfPickups; a DirectToInventory drill pockets
            // them in the prefix (pile stays 0) and a Disabled drill is plain vanilla (never routed) — so the
            // pile-size gate below is implicitly the DropThenHaul case, and Direct/Disabled drills never trip it.
            int sinceLast = lastDrillInterruptTick.TryGetValue(pawn, out int last) ? tick - last : int.MaxValue;
            if (!YieldLeashPolicy.ShouldCollectNow(comp.pendingSelfPickups.Count, DrillSection, sinceLast,
                    DrillInterruptCooldownTicks))
                return;

            int pile = comp.pendingSelfPickups.Count;
            // Ensure the self-pickup is queued despite the master/bleed INTAKE gates RecordSelfPickup applies when
            // each portion drops — so an interrupt is never wasted on an empty queue. Idempotent when one is
            // already queued (the usual case: the never-ending drill kept the front-queued job from ever starting;
            // this interrupt is what releases it).
            YieldRouter.EnsureSelfPickupJob(pawn, comp);
            lastDrillInterruptTick[pawn] = tick;
            // End the drill so the queued self-pickup runs now. InterruptForced — NOT Errored/ErroredPather, which
            // Pawn_JobTracker.EndCurrentJob answers with a hardcoded, uninterruptible 250-tick Wait
            // (decompile-verified); InterruptForced falls through to TryFindAndStartJob, which pulls the
            // front-queued self-pickup first. CleanupCurrentJob releases the drill reservation, and the work scan
            // re-issues the drill afterward — portion progress lives on the CompDeepDrill, so nothing is lost.
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            HDLog.Dbg($"{pawn} drill-leash: interrupted drilling to collect {pile} pending drop(s).");
        }
    }
}
