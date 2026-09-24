using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    // Automatic work is one complete storage trip. A forced order keeps the selected hold until done.
    // Feature adapted from nullpat's GH267; JobOnThing independently revalidates stale scanner answers.
    public sealed class WorkGiver_BulkUnloadTransporters : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Transporter);
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !BulkUnloadTransporterGate.Enabled;
        public override bool HasJobOnThing(Pawn pawn, Thing target, bool forced = false)
            => BulkUnloadTransporterGate.StartBlock(pawn, target?.TryGetComp<CompTransporter>(), forced) == TransporterUnloadBlock.None;
        public override Job JobOnThing(Pawn pawn, Thing target, bool forced = false)
        {
            if (!HasJobOnThing(pawn, target, forced)) return null;
            var job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_UnloadTransporterInBulk, target);
            job.playerForced = forced;
            return job;
        }
    }
}
