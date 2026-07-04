using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Combat Extended compatibility for issue #54: make a player MECHANOID's CE carry weight equal its
    /// <see cref="StatDefOf.CarryingCapacity"/> (the value on the mech's UI panel), so a hauler mech under CE
    /// loads per its carrying capacity — e.g. 52 for a vanilla lifter, 158 for a modded advanced loader — instead
    /// of CE's body-size default CarryWeight (base 40 × body size ≈ 42 for any 0.7-body mech, which ignores the
    /// mech's hauling stat entirely, so a 52-cap lifter and a 158-cap loader were both stuck near ~42).
    ///
    /// <para>WHY a StatPart on CE's CarryWeight, not just HD's own ceiling: under CE a mech's whole inventory model
    /// derives from CarryWeight. CE adds its <c>CompInventory</c> to <c>BasePawn</c>, so mechs inherit it, and
    /// <c>CompInventory.capacityWeight = GetStatValue(CarryWeight)</c> drives BOTH the per-pickup fit check (which
    /// HD clamps every pickup through, via <see cref="CECompat.MaxFitCount"/>) AND CE's move-speed encumbrance; CE
    /// also postfixes <c>MassUtility.Capacity</c> to return CarryWeight, which is what <see cref="CarryCapacity"/>
    /// reads under CE. Raising only HD's ceiling would be clamped straight back by CE's fit check, and forcing the
    /// load past CarryWeight would make the mech crawl under CE's encumbrance. Setting CarryWeight at the source
    /// makes the fit check, the encumbrance curve, and HD's ceiling all become carrying-capacity-based and
    /// consistent: the mech loads up to its carrying capacity WITHOUT CE's over-capacity penalty (CE still applies
    /// its normal sub-capacity move-speed curve, which tops out near 0.75 at a full load — so this is "no longer
    /// crawling," not literally full speed). (The non-CE path is handled directly by
    /// <see cref="CarryCapacity"/>; there is no CarryWeight stat without CE.)</para>
    ///
    /// <para>Added to CE's CarryWeight StatDef via <c>Patches/MechCarryWeight_CE.xml</c>, gated on the stat
    /// existing (i.e. CE loaded). Affects ONLY player-faction mechanoids; every other pawn keeps CE's own
    /// CarryWeight, so CE's humanlike/animal weight model is untouched.</para>
    ///
    /// <para>OPT-IN under CE (issue #118): this part fires ONLY when the "Mechanoid carrying capacity" slider
    /// (<c>mechHaulMultiplier</c>) is raised ABOVE the default ×1.0. At ×1.0 it stands down and CE keeps its own
    /// carry weight, matching what every language's setting description promises ("no effect while Combat Extended
    /// is installed"). #54 originally applied this override unconditionally under CE, which contradicted that
    /// description and overrode CE's mech encumbrance model even at the default; gating it on an explicit slider
    /// opt-in restores the documented default while keeping the boost available. When it does fire, the multiplier
    /// scales the carrying-capacity base (e.g. a 158-capacity loader at ×1.5 gets a 237 CE carry weight).</para>
    /// </summary>
    public class StatPart_MechCarryWeightCE : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetMechCarryWeight(req, out float w))
                val = w; // override CE's body-size CarryWeight with the mech's carrying capacity
        }

        // The line shown in the CE CarryWeight stat tooltip's breakdown when this part fires, so a player
        // inspecting a hauler mech sees WHY its carry weight equals its carrying capacity (issue #54: the user
        // saw a mech with carrying capacity 52/158 but a ~24.5/42 carry weight and had no explanation). Same
        // label-plus-value shape as StatPart_Overload; null when the part does not apply (every non-player /
        // non-mech pawn, where CE's own CarryWeight stands).
        public override string ExplanationPart(StatRequest req)
        {
            if (TryGetMechCarryWeight(req, out float w))
                return "HaulersDream.Stat.MechCarryCapacity".Translate() + ": " + w.ToString("0.##");
            return null;
        }

        private static bool TryGetMechCarryWeight(StatRequest req, out float weight)
        {
            weight = 0f;
            if (!(req.Thing is Pawn pawn) || pawn.Dead)
                return false;
            var race = pawn.RaceProps;
            if (race == null || !race.IsMechanoid)
                return false; // humanlikes / animals keep CE's own CarryWeight
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
                return false; // only the player's own mechs
            // OPT-IN under Combat Extended (issue #118): at the default multiplier ×1.0 this part does NOT fire, so
            // Combat Extended owns the mech's carry weight exactly as every language's setting description promises
            // ("no effect while Combat Extended is installed"). Only when the player RAISES the "Mechanoid carrying
            // capacity" slider above ×1.0 — an explicit opt-in to have HD manage mech hauling under CE — does HD set
            // the mech's CE carry weight to its carrying-capacity stat × the multiplier (the issue #54 behavior, now
            // gated). This keeps HD out of CE's encumbrance model by default while preserving the boost for those who
            // want it. (The non-CE mech path in CarryCapacity.Of is unchanged — without CE there is no CarryWeight
            // stat to defer to, so HD sizes mech hauling by carrying capacity there as before.)
            float mult = HaulersDreamMod.Settings?.mechHaulMultiplier ?? 1f;
            if (!Core.CarryCapacityPolicy.CeMechCarryOverrideActive(mult))
                return false; // ×1.0 (default) or lower → CE keeps its own carry weight
            // The mech's hauling stat (the UI "carrying capacity" panel value) × the multiplier becomes its CE carry
            // weight, so CE's fit check + encumbrance + HD's ceiling all key off carrying capacity.
            float statCap = pawn.GetStatValue(StatDefOf.CarryingCapacity);
            weight = statCap * mult;
            return weight > 0f;
        }
    }
}
