using System.Collections.Generic;
using HaulersDream.Core;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// One per-task claim-ledger entry (keyed in the GameComponent by the task's save-unique load id): the three
    /// dictionaries the pure <see cref="LoadLedger{TDef,TPawn}"/> math operates on, plus the <see cref="Map"/> the
    /// task lives on (so map removal can drop it in one pass). All arithmetic delegates to <c>LoadLedger</c>; this
    /// class only owns the live state + its serialization.
    ///
    /// <c>pawnClaims</c> can't be scribed directly (a <c>Dictionary&lt;Pawn, Dictionary&lt;ThingDef,int&gt;&gt;</c> has
    /// no native LookMode), so it round-trips through a list of <see cref="PawnClaimData"/> DTOs — the canonical
    /// workaround. On load: rebuild the dict, drop any null-pawn entry, then <c>RecomputeClaimed</c> so
    /// <c>totalClaimed</c> is derived from the surviving pawns (the ref-mod quota-leak fix — see RecomputeClaimed).
    /// </summary>
    public class LoadLedgerEntry : IExposable
    {
        public Map map;
        public Dictionary<ThingDef, int> totalNeeded = new Dictionary<ThingDef, int>();
        public Dictionary<ThingDef, int> totalClaimed = new Dictionary<ThingDef, int>();
        public Dictionary<Pawn, Dictionary<ThingDef, int>> pawnClaims = new Dictionary<Pawn, Dictionary<ThingDef, int>>();

        // Scribe scratch for the pawnClaims DTO round-trip.
        private List<PawnClaimData> pawnClaimScribe;

        public LoadLedgerEntry() { }

        public LoadLedgerEntry(Map map)
        {
            this.map = map;
        }

        /// <summary>True once this entry holds nothing live (no needed AND no claimed) — the GameComponent self-prunes
        /// such inert entries, same tolerance as the batch-bills map.</summary>
        public bool IsInert
            => (totalNeeded == null || totalNeeded.Count == 0)
               && (totalClaimed == null || totalClaimed.Count == 0);

        // --- thin delegates to the pure math (the GameComponent calls these) ---

        public Dictionary<ThingDef, int> AvailableToClaim(Pawn asker)
            => LoadLedger<ThingDef, Pawn>.AvailableToClaim(totalNeeded, totalClaimed, pawnClaims, asker);

        public bool HasWork(Pawn asker)
            => LoadLedger<ThingDef, Pawn>.HasWork(totalNeeded, totalClaimed, pawnClaims, asker);

        public bool AnyClaimed()
            => LoadLedger<ThingDef, Pawn>.AnyClaimed(totalClaimed);

        public void ApplyClaim(Pawn pawn, IReadOnlyDictionary<ThingDef, int> newPlan)
            => LoadLedger<ThingDef, Pawn>.ApplyClaim(totalClaimed, pawnClaims, pawn, newPlan);

        public void Settle(Pawn pawn, ThingDef def, int deposited)
            => LoadLedger<ThingDef, Pawn>.Settle(totalNeeded, totalClaimed, pawnClaims, pawn, def, deposited);

        public void Release(Pawn pawn)
            => LoadLedger<ThingDef, Pawn>.Release(totalClaimed, pawnClaims, pawn);

        /// <summary>Replace <c>totalNeeded</c> from a freshly-read manifest (def → remaining CountToTransfer).</summary>
        public void SetNeeded(Dictionary<ThingDef, int> needed)
            => totalNeeded = needed ?? new Dictionary<ThingDef, int>();

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Collections.Look(ref totalNeeded, "totalNeeded", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref totalClaimed, "totalClaimed", LookMode.Def, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                pawnClaimScribe = new List<PawnClaimData>();
                foreach (var kv in pawnClaims)
                    if (kv.Key != null && kv.Value != null && kv.Value.Count > 0)
                        pawnClaimScribe.Add(new PawnClaimData(kv.Key, kv.Value));
            }
            Scribe_Collections.Look(ref pawnClaimScribe, "pawnClaims", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (totalNeeded == null) totalNeeded = new Dictionary<ThingDef, int>();
                if (totalClaimed == null) totalClaimed = new Dictionary<ThingDef, int>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Rebuild pawnClaims from the DTOs, dropping any null-pawn (a hauler dead/removed since save) or
                // null-claim entry, then SELF-HEAL totalClaimed = Σ surviving pawnClaims (the quota-leak fix —
                // never trust the scribed totalClaimed, which could over-count an orphaned downed hauler's units).
                pawnClaims = new Dictionary<Pawn, Dictionary<ThingDef, int>>();
                if (pawnClaimScribe != null)
                    foreach (var dto in pawnClaimScribe)
                        if (dto?.pawn != null && dto.claims != null && dto.claims.Count > 0)
                            pawnClaims[dto.pawn] = dto.claims;
                pawnClaimScribe = null;
                totalClaimed = LoadLedger<ThingDef, Pawn>.RecomputeClaimed(pawnClaims);
            }
        }

        /// <summary>Serialization DTO for one pawn's claim slice (a <c>Dictionary&lt;Pawn, Dictionary&lt;ThingDef,int&gt;&gt;</c>
        /// can't be scribed directly). <c>pawn</c> is a reference; the claims dict uses the Def/Value look modes.</summary>
        public class PawnClaimData : IExposable
        {
            public Pawn pawn;
            public Dictionary<ThingDef, int> claims = new Dictionary<ThingDef, int>();

            public PawnClaimData() { }

            public PawnClaimData(Pawn pawn, Dictionary<ThingDef, int> claims)
            {
                this.pawn = pawn;
                this.claims = claims;
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_Collections.Look(ref claims, "claims", LookMode.Def, LookMode.Value);
                if (Scribe.mode == LoadSaveMode.LoadingVars && claims == null)
                    claims = new Dictionary<ThingDef, int>();
            }
        }
    }
}
