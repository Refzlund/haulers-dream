using System.Text;
using HaulersDream.Core;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// In-game dev harness (Dev Mode → Debug actions → "Hauler's Dream"). See .docs/02. Behavioural
    /// sign-off must happen in RimWorld — the unit tests only cover the pure carry math.
    /// </summary>
    public static class HaulersDreamDebugActions
    {
        // Bump this whenever the diagnostic output changes — if the log shows an OLDER tag than the build
        // you just deployed, RimWorld is still running the previously-loaded mod DLL (restart required).
        private const string DiagVersion = "diag-v2";

        [DebugAction("Hauler's Dream", "Log carry state (selected pawn)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogCarryState()
        {
            if (!(Find.Selector.SingleSelectedThing is Pawn pawn))
            {
                Messages.Message("Hauler's Dream: select a single pawn.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            var comp = pawn.GetComp<CompHauledToInventory>();
            var s = HaulersDreamMod.Settings;
            float max = MassUtility.Capacity(pawn);
            float cur = MassUtility.GearAndInventoryMass(pawn);
            float cap = CarryMath.EffectiveCapacity(max, s.carryLimitFraction);

            var tracked = comp?.GetHashSet();
            var inv = pawn.inventory?.innerContainer;

            var sb = new StringBuilder();
            sb.AppendLine($"[Hauler's Dream] {DiagVersion} {pawn}: max={max:0.#}kg current={cur:0.#}kg limit={cap:0.#}kg " +
                          $"encumbrance={CarryMath.EncumbranceFraction(cur, max):P0} eligible={YieldRouter.IsEligible(pawn)}");
            sb.AppendLine($"  settings: shareForCrafting={s.shareForCrafting} shareForBuilding={s.shareForBuilding} " +
                          $"pickupMode={s.pickupMode} drafted={pawn.Drafted}");
            sb.AppendLine($"  TRACKED (tagged / shareable) x{tracked?.Count ?? 0}:");
            if (tracked != null)
                foreach (var t in tracked)
                    sb.AppendLine($"    - {t?.def?.defName} x{t?.stackCount} (stillInInventory={inv?.Contains(t)}, reservable={(t != null && pawn.CanReserve(t))})");
            sb.AppendLine($"  FULL INVENTORY x{inv?.Count ?? 0} (tagged? = counted for sharing):");
            if (inv != null)
                for (int i = 0; i < inv.Count; i++)
                    sb.AppendLine($"    - {inv[i]?.def?.defName} x{inv[i]?.stackCount} (tagged={tracked?.Contains(inv[i])})");
            Log.Message(sb.ToString().TrimEnd());
        }

        [DebugAction("Hauler's Dream", "Force unload (selected pawn)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceUnload()
        {
            if (Find.Selector.SingleSelectedThing is Pawn pawn)
                PawnUnloadChecker.CheckIfShouldUnload(pawn, true);
            else
                Messages.Message("Hauler's Dream: select a single pawn.", MessageTypeDefOf.RejectInput, false);
        }

        [DebugAction("Hauler's Dream", "Prepare for safe removal (clear HD jobs)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PrepareForSafeRemoval()
        {
            string result = SafeRemoval.PrepareForSafeRemoval();
            Log.Message("[Hauler's Dream] " + result);
            Messages.Message(result, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
