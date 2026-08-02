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
    /// Pure decisions about WHO owns the crafting-ingredient gather, so the per-bench control and the crafting
    /// settings can describe themselves honestly instead of promising behaviour HD may have ceded.
    ///
    /// <para>The distinction this pins (issue #243): "Common Sense owns the DoBill driver" is NOT the same fact as
    /// "Common Sense is pocketing ingredients". CS takes over the driver when EITHER of its two options is on, but
    /// its cleaning option alone leaves vanilla's carry-in-hands collect running — nothing is gathered into an
    /// inventory then, and a notice claiming otherwise would be false. Only the haul-all-ingredients option makes
    /// CS a gatherer, so <c>foreignModGathersNow</c> must be fed from that narrower fact.</para>
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
    }
}
