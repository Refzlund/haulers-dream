using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
    /// <summary>One observational inventory pass. Neither claims nor manifest quantities are changed.</summary>
    public sealed class DirectedLoadCargoBudget<TDef, TManifest> where TDef : class where TManifest : class
    {
        private readonly List<(TDef def, int units, long other)> claims = new List<(TDef, int, long)>();
        private readonly List<(TManifest manifest, int units)> remaining = new List<(TManifest, int)>();

        public DirectedLoadCargoBudget(IReadOnlyDictionary<TDef, int> claims,
            IReadOnlyDictionary<TDef, int> needed, IReadOnlyDictionary<TDef, long> otherClaims)
        {
            if (claims == null) throw new ArgumentNullException(nameof(claims));
            if (needed == null) throw new ArgumentNullException(nameof(needed));
            if (otherClaims == null) throw new ArgumentNullException(nameof(otherClaims));
            foreach (var claim in claims)
            {
                needed.TryGetValue(claim.Key, out int demand);
                otherClaims.TryGetValue(claim.Key, out long other);
                other = Math.Max(0, other);
                // No claim is revoked or reassigned. Only quantities guaranteed even if all
                // other couriers deliver their existing claims may lose storage responsibility.
                int guaranteed = (int)Math.Max(0L, (long)demand - other);
                this.claims.Add((claim.Key, Math.Min(Math.Max(0, claim.Value), guaranteed), other));
            }
        }

        // The adapter supplies a positively matched actual parcel and the exact manifest entry.
        // Multiple compatible parcels share BOTH budgets; a def-wide claim alone proves nothing.
        public int Take(TDef def, TManifest matchedManifest, int manifestUnits, int actualSurplus)
        {
            if (def == null || matchedManifest == null || manifestUnits <= 0 || actualSurplus <= 0) return 0;
            int claimIndex = claims.FindIndex(c => ReferenceEquals(c.def, def));
            if (claimIndex < 0 || claims[claimIndex].units <= 0) return 0;
            int claimed = claims[claimIndex].units;
            int manifestIndex = remaining.FindIndex(m => ReferenceEquals(m.manifest, matchedManifest));
            int wanted = manifestIndex < 0 ? (int)Math.Max(0L, (long)manifestUnits - claims[claimIndex].other)
                : remaining[manifestIndex].units;
            int units = Math.Min(actualSurplus, Math.Min(claimed, Math.Min(wanted, manifestUnits)));
            claims[claimIndex] = (def, claimed - units, claims[claimIndex].other);
            if (manifestIndex < 0) remaining.Add((matchedManifest, wanted - units));
            else remaining[manifestIndex] = (matchedManifest, wanted - units);
            return units;
        }
    }
}
