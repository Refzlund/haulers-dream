using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The mod-added move-speed penalty for overloaded colonists. Vanilla 1.6 has NO local move-speed
    /// penalty for inventory mass (the MoveSpeed stat has no mass StatPart), so this provides the
    /// "slowed down past 100% capacity" that the smart-overload feature trades against. It uses the
    /// SAME model (<see cref="OverloadTuning"/>) the pickup decision uses, so the slowdown a pawn
    /// accepts when it chooses to overload is exactly the slowdown it then experiences.
    ///
    /// Added to the vanilla MoveSpeed StatDef via Patches/MoveSpeed_Overload.xml. Only player-faction
    /// humanlikes carrying past 100% capacity are affected; at/under capacity it is a no-op (matching
    /// vanilla), and it is inert whenever the overload feature stands down — strict carry weight, the
    /// slider at "no slowdown" (0) or "off", or Combat Extended active (the same matrix as
    /// <see cref="OverloadGate.NoOverload"/>).
    /// </summary>
    public class StatPart_Overload : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetFactor(req, out float factor))
                val *= factor;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (TryGetFactor(req, out float factor))
                return "HaulersDream.Stat.Overload".Translate() + ": x" + factor.ToStringPercent();
            return null;
        }

        private static bool TryGetFactor(StatRequest req, out float factor)
        {
            factor = 1f;
            var s = HaulersDreamMod.Settings;
            if (s == null || s.strictCarryWeight || s.overloadLevel <= 0 || OverloadTuning.IsOff(s.overloadLevel))
                return false; // strict carry weight, "no slowdown" or "off" -> no penalty (mirrors OverloadGate.NoOverload)
            // Combat Extended's StatWorker_MoveSpeed applies its OWN encumbrance penalty inside
            // GetValueUnfinalized; vanilla then runs StatParts in StatWorker.FinalizeValue AFTER that — so this
            // factor would stack with CE's and double-punish. Under CE the whole overload feature stands down
            // (OverloadGate.NoOverload), and CE's encumbrance simulation is the single source of slowdown truth.
            if (CECompat.IsActive)
                return false;
            if (!(req.Thing is Pawn pawn) || pawn.Dead || pawn.RaceProps == null)
                return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
                return false;
            // Slow player-faction HUMANLIKES and player-faction MECHANOIDS (the same race classes
            // OverloadGate.NoOverloadFor lets overload — the two stay in lockstep so the capacity a pawn
            // gains is exactly what it pays for in speed). Animals / non-player pawns are never slowed.
            if (!(pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid))
                return false;
            // The slowdown is a hauling-economics mechanic; drafted pawns stand to orders at full speed,
            // vanilla-style (and with the draft gate on unloads they couldn't even shed the load until undrafted).
            if (pawn.Drafted)
                return false;
            float cap = MassUtility.Capacity(pawn);
            if (cap <= 0f)
                return false;
            float ratio = MassUtility.GearAndInventoryMass(pawn) / cap;
            if (ratio <= 1f)
                return false; // at/under capacity -> no penalty (vanilla behaviour)
            factor = OverloadTuning.SpeedFactor(s.overloadLevel, ratio);
            return factor < 1f;
        }
    }
}
