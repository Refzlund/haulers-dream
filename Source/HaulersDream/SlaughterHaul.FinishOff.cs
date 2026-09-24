using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>Finish-off providers do not run the vanilla slaughter or hunt drivers.</summary>
    [HarmonyPatch]
    internal static class Patch_FinishOff_HaulAfter
    {
        private static readonly string[] DriverTypes =
        {
            "AllowTool.JobDriver_FinishOff",
            "KeyzAllowUtilities.JobDriver_FinishOff"
        };

        static bool Prepare() => TargetMethods().Any();

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in DriverTypes)
            {
                var type = AccessTools.TypeByName(name);
                if (type == null || !typeof(JobDriver).IsAssignableFrom(type))
                    continue;
                var method = AccessTools.DeclaredMethod(type, "DoExecution", new[] { typeof(Pawn), typeof(Pawn) });
                if (method != null && !method.IsStatic && method.ReturnType == typeof(void))
                    yield return method;
            }
        }

        // Observe the provider's actual execution, after its corpse/unforbid behavior. A successful kill
        // can end the job Incompletable when the melee stance delays its final instant toil and the
        // provider's dead-target fail condition runs. JobCondition.Succeeded alone would miss that kill.
        // Keyz' strip-and-finish driver calls this same base method, so it receives exactly one hook.
        static void Postfix(JobDriver __instance, Pawn __0, Pawn __1)
        {
            Pawn killer = __0, victim = __1;
            if (killer == null || victim?.RaceProps?.Animal != true || !victim.Dead
                || __instance.pawn != killer || killer.jobs?.curDriver != __instance
                || __instance.job == null || killer.CurJob != __instance.job
                || __instance.job.targetA.Thing != victim)
                return;

            // Colony animals use the tamed toggle; factionless animals use the wild toggle. Another
            // faction's owned animal is neither a wild animal nor colony slaughter.
            var player = Faction.OfPlayerSilentFail;
            if (player == null || (victim.Faction != null && victim.Faction != player))
                return;
            var kind = victim.Faction == player ? HaulKillSource.Slaughter : HaulKillSource.Hunt;

            // Preserve the existing eligibility, map, forbidden, storage and duplicate-job gates.
            // Append behind player/provider orders; never clear the queue or un-forbid a corpse.
            SlaughterHaul.TryAppendHaul(killer, victim, kind);
        }
    }
}
