namespace HaulersDream.Core
{
    /// <summary>
    /// The per-bench "gather ingredients into inventory before crafting" opt-out, as a pure decision (issue #230).
    ///
    /// A player who keeps a bench's ingredients stacked right beside it loses time to HD's one-sweep gather: the
    /// gather is a SEPARATE job that ends at the bench, so the bill itself only starts on the pawn's NEXT work scan.
    /// When the walk it saves is a step or two, that extra job boundary costs more than it saves — hence a per-bench
    /// switch that hands that one bench back to RimWorld's own one-stack-at-a-time flow.
    ///
    /// This type exists to pin down the ONE invariant the runtime must never get wrong: the per-bench flag is a
    /// VETO, never an override. It can only ever take gathering AWAY at a bench that has the component; it can never
    /// turn gathering on somewhere a global setting has turned it off, and a bench with no component at all reads as
    /// "allowed" so an un-patched bench def behaves exactly as it did before the feature existed.
    /// </summary>
    public static class BenchGatherPolicy
    {
        /// <summary>
        /// May HD gather this bench's bill ingredients into a pawn's inventory, as far as the PER-BENCH switch is
        /// concerned? Global settings are decided separately by the caller and ANDed with this — so a true result
        /// means "this bench does not object", never "gather regardless".
        /// </summary>
        /// <param name="hasComp">
        /// Whether the bench actually carries the per-bench switch. False for any bill giver the XML patch did not
        /// reach (a def with no <c>ITab_Bills</c>, a bill giver that is not a building at all) — such a bench has no
        /// player choice recorded, so it must fall through as allowed.
        /// </param>
        /// <param name="benchFlag">
        /// The switch's own value, meaningful only when <paramref name="hasComp"/> is true. True = the player left
        /// this bench gathering (the default); false = the player switched this bench back to vanilla behaviour.
        /// </param>
        /// <returns>False only for a bench that HAS the switch and has it turned OFF; true in every other case.</returns>
        public static bool BenchAllowsGather(bool hasComp, bool benchFlag) => !hasComp || benchFlag;
    }
}
