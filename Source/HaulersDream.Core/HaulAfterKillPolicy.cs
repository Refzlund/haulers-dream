namespace HaulersDream.Core
{
    /// <summary>Which kill path produced the corpse — the wild/tamed classification tag.
    /// Hunt = wild animal (JobDriver_Hunt); Slaughter = tamed colony animal (JobDriver_Slaughter).
    /// Classification-only: deliberately NOT a HaulSourceType / not wired into WorkTypePolicy or
    /// YieldRouter (a kill corpse is hauled to storage, never routed into inventory).</summary>
    public enum HaulKillSource
    {
        Hunt,       // wild animal killed by hunting (JobDriver_Hunt)
        Slaughter   // tamed colony animal killed by slaughter (JobDriver_Slaughter)
    }

    /// <summary>Pure decision for Haul After a Kill: should this finished kill be hauled? The two toggles
    /// are independent — Hunt reads only haulWildKills, Slaughter reads only haulTamedSlaughter. Animal-scope,
    /// faction, forbidden-state and storage-existence are game-typed gates kept in the runtime. Both kinds are
    /// delivered the same way — a finish action on the killer's job driver appends a haul-to-storage job onto
    /// the killer's own queue (Slaughter on a clean finish; Hunt only on a NON-clean finish, where vanilla's
    /// own hunt self-haul did not run). Neither needs a per-scan policy here, only this toggle lookup.</summary>
    public static class HaulAfterKillPolicy
    {
        /// <summary>The two-toggle wild-vs-tamed lookup. No cross-leak.</summary>
        public static bool ShouldHaul(HaulKillSource kind, bool haulWildKills, bool haulTamedSlaughter)
        {
            switch (kind)
            {
                case HaulKillSource.Hunt:      return haulWildKills;
                case HaulKillSource.Slaughter: return haulTamedSlaughter;
                default:                       return false;
            }
        }
    }
}
