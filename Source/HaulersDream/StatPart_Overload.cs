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
            if (s == null)
                return false;
            // "No slowdown" (level 0) -> no penalty. Redundant with the SpeedFactor(0,..)==1 result below
            // but a cheap fast-path that also skips the per-tick mass-cache read. (Level 0 still PARTICIPATES
            // in overload per the shared predicate — that is the consistent "free capacity, no slowdown" deal.)
            if (s.overloadLevel <= 0)
                return false;
            if (!(req.Thing is Pawn pawn) || pawn.Dead || pawn.RaceProps == null)
                return false;
            // The strict/CE/level/race off-matrix AND the player-faction + undrafted asymmetry come from the
            // SINGLE pure predicate OverloadPolicy.AppliesSpeedPenalty — the SAME shared off-matrix
            // OverloadGate.NoOverloadFor derives the who-MAY-overload set from. This is what keeps the two in
            // lockstep (the capacity granted == the speed paid); the matrix test in OverloadPolicyTests fails
            // the build if a future edit breaks it. Drafted pawns stand to orders at full speed (vanilla-style,
            // and they couldn't shed the load until undrafted anyway); animals / non-player pawns are never
            // slowed. Under Combat Extended the whole feature stands down (CE's StatWorker_MoveSpeed already
            // applies its own encumbrance penalty inside GetValueUnfinalized, which vanilla runs StatParts
            // AFTER — so this factor would otherwise double-punish; CE is the single slowdown truth there).
            if (!OverloadPolicy.AppliesSpeedPenalty(
                    s.strictCarryWeight, s.overloadLevel, CECompat.IsActive,
                    pawn.RaceProps.Humanlike, pawn.RaceProps.IsMechanoid,
                    pawn.Faction != null && pawn.Faction.IsPlayer, pawn.Drafted))
                return false;
            // PERF (HD-MASS): the vanilla MoveSpeed StatDef is UNCACHED, so this runs once per CELL a moving
            // pawn enters. Read the two mass numbers through the per-(pawn,tick) memo so the per-cell re-walk
            // collapses to one apparel+equipment+inventory walk per tick, shared with the same-tick capacity
            // gates. Pure read (decompile-verified) — no decision changes, only recomputation is avoided.
            var mass = PawnMassCache.MassInfo(pawn);
            float cap = mass.Capacity;
            if (cap <= 0f)
                return false;
            float ratio = mass.CurrentMass / cap;
            if (ratio <= 1f)
                return false; // at/under capacity -> no penalty (vanilla behaviour)
            factor = OverloadTuning.SpeedFactor(s.overloadLevel, ratio);
            return factor < 1f;
        }
    }
}
