namespace HaulersDream.Core
{
    /// <summary>
    /// Which caveat the per-bench "Gather ingredients" control has to admit to before its own state description
    /// can be believed. The control governs HD's gather; it cannot govern gathering HD is not doing.
    /// </summary>
    public enum BenchGatherNotice
    {
        /// <summary>No caveat: HD owns the plain one-sweep gather, so the switch means exactly what it says.</summary>
        None,

        /// <summary>
        /// Another mod is doing the ingredient gathering (Common Sense with its haul-all-ingredients option on).
        /// HD stands aside entirely while that is true, so neither this switch nor HD's own gather settings can
        /// stop the collecting the player is watching — only that mod's own option can. This is the case behind
        /// issue #243.
        /// </summary>
        ForeignModGathers,

        /// <summary>
        /// HD would own the gather, but the player has switched HD's plain one-sweep gather off in mod options.
        /// The bench switch is not inert — batch crafting and the move-ingredients-closer detour still run through
        /// it — but it no longer governs the one-sweep gather its description talks about.
        /// </summary>
        GlobalGatherOff
    }

    /// <summary>
    /// Who actually collects a crafting bill's ingredients right now. The ground truth the notice is graded
    /// against: every <see cref="BenchGatherNotice"/> must be the honest sentence for exactly one of these.
    /// </summary>
    public enum BillGatherer
    {
        /// <summary>Nobody sweeps ingredients into an inventory: the pawn makes RimWorld's own one-stack-per-trip
        /// carry runs. Not a fault — it is what the player asked for by switching HD's gather off — but it is a
        /// state the UI has to be able to admit to, because it is the one where a control describes something that
        /// is not happening.</summary>
        Nobody,

        /// <summary>Hauler's Dream's one-sweep gather. The only state in which the bench switch and the crafting
        /// settings govern what the player is watching.</summary>
        HaulersDream,

        /// <summary>Another mod (Common Sense with its haul-all-ingredients option on). HD stands aside entirely,
        /// so none of HD's controls can change it.</summary>
        ForeignMod
    }

    /// <summary>
    /// Pure decisions about WHO owns the crafting-ingredient gather, so the per-bench control and the crafting
    /// settings can describe themselves honestly instead of promising behaviour HD may have ceded.
    ///
    /// <para>The distinction this pins (issue #243): "Common Sense owns the DoBill driver" is NOT the same fact as
    /// "Common Sense is pocketing ingredients". CS takes over the driver when EITHER of its two options is on, but
    /// its cleaning option alone leaves vanilla's carry-in-hands collect running — nothing is gathered into an
    /// inventory then, and a notice claiming otherwise would be false. Only the haul-all-ingredients option makes
    /// CS a gatherer, so <c>foreignModGathersNow</c> must be fed from that narrower fact — see
    /// <see cref="CommonSenseCedePolicy.CommonSenseGathersIngredients"/>, which is the same fact HD's own cede is
    /// keyed on, so the notice and the behaviour cannot disagree.</para>
    /// </summary>
    public static class GatherOwnershipPolicy
    {
        /// <summary>
        /// Which caveat, if any, the bench control and the crafting settings must show.
        /// </summary>
        /// <param name="foreignModGathersNow">Is another mod pocketing bill ingredients right now? Narrow sense —
        /// actually gathering, not merely holding the DoBill driver.</param>
        /// <param name="plainGatherEnabled">Is HD's own one-sweep gather turned on in mod options
        /// (<c>inventoryCraftDeliver</c>)?</param>
        /// <returns>The caveat to surface; <see cref="BenchGatherNotice.None"/> when the control can be taken at
        /// its word.</returns>
        /// <remarks>A foreign gatherer WINS over the global-off caveat: when another mod is doing the collecting,
        /// "HD's own gather is switched off" is true but useless — it explains nothing about the pawns the player
        /// is actually watching, and points at the wrong option to change.</remarks>
        public static BenchGatherNotice ResolveNotice(bool foreignModGathersNow, bool plainGatherEnabled)
        {
            if (foreignModGathersNow)
                return BenchGatherNotice.ForeignModGathers;
            return plainGatherEnabled ? BenchGatherNotice.None : BenchGatherNotice.GlobalGatherOff;
        }

        /// <summary>
        /// Does the per-bench switch actually govern the PLAIN one-sweep gather right now — the behaviour its own
        /// description talks about? The boolean view of <see cref="ResolveNotice"/> returning
        /// <see cref="BenchGatherNotice.None"/>, kept separate because the caller wants a gate here and a reason
        /// there.
        /// </summary>
        /// <param name="foreignOwnsGatherFlow">Is another mod pocketing bill ingredients right now?</param>
        /// <param name="plainGatherEnabled">Is HD's own one-sweep gather turned on in mod options?</param>
        /// <returns>True only when HD is the one gathering and its gather is enabled.</returns>
        /// <remarks>False does NOT mean the switch is inert: it still vetoes batch crafting and the
        /// move-ingredients-closer detour at that bench, neither of which reads
        /// <paramref name="plainGatherEnabled"/>. It means only that the one-sweep gather the description
        /// describes is not HD's to grant or withhold at that moment.</remarks>
        public static bool BenchSwitchGovernsPlainGather(bool foreignOwnsGatherFlow, bool plainGatherEnabled)
            => !foreignOwnsGatherFlow && plainGatherEnabled;

        /// <summary>
        /// Is HD's own one-sweep gather switched on in mod options? All THREE crafting checkboxes, because the
        /// route needs all three and any one of them off silences it.
        /// </summary>
        /// <param name="inventoryCraftDeliver">"Carry crafting ingredients in inventory" — the checkbox a player
        /// looking for this feature would find first.</param>
        /// <param name="shareForCrafting">"Use carried materials for crafting bills" — the relay depends on it,
        /// because the sweep only pays off if the next work scan can source the bill from what the pawn carries.</param>
        /// <param name="markForUnload">Automatic unloading — the relay's safety net: it is what reclaims swept
        /// stock the craft never consumes, so the route refuses to run without it.</param>
        /// <returns>True only when the one-sweep gather can actually engage.</returns>
        /// <remarks>
        /// → GOTCHA: this exists so the ROUTE and the NOTICE read one expression instead of two. The recurring bug
        /// in this corner is a description gated on a NARROWER condition than the behaviour it describes: the
        /// notice once read <c>inventoryCraftDeliver</c> alone, so a player who switched the gather off via either
        /// of the other two was still told by the bench button that pawns gather there. Both sides now call this.
        /// </remarks>
        public static bool PlainGatherEnabled(bool inventoryCraftDeliver, bool shareForCrafting, bool markForUnload)
            => inventoryCraftDeliver && shareForCrafting && markForUnload;

        /// <summary>
        /// Who is actually collecting a bill's ingredients — the ground truth <see cref="ResolveNotice"/> has to
        /// stay honest about.
        /// </summary>
        /// <param name="foreignModGathersNow">Is another mod pocketing bill ingredients right now? Narrow sense —
        /// actually gathering, not merely holding the DoBill driver.</param>
        /// <param name="plainGatherEnabled">Is HD's own one-sweep gather turned on (see
        /// <see cref="PlainGatherEnabled"/>)?</param>
        /// <returns>The gatherer, or <see cref="BillGatherer.Nobody"/> when neither is sweeping.</returns>
        /// <remarks>
        /// The foreign mod WINS when both could apply, because HD's cede is keyed on the same fact: while another
        /// mod gathers, HD has already stood down whatever its own checkboxes say.
        /// <para>Stated as its own function rather than derived from the notice so the two can be cross-checked:
        /// the notice is graded against this, one case each, which is what makes "the description tells the truth
        /// in every combination" an executable claim instead of a review opinion.</para>
        /// </remarks>
        public static BillGatherer ResolveGatherer(bool foreignModGathersNow, bool plainGatherEnabled)
        {
            if (foreignModGathersNow)
                return BillGatherer.ForeignMod;
            return plainGatherEnabled ? BillGatherer.HaulersDream : BillGatherer.Nobody;
        }
    }
}
