using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Shared inventories": resources a colonist auto-scooped (tagged in CompHauledToInventory) are
    /// available to other colonists for building/crafting. Only TAGGED items are shareable — never a
    /// pawn's organic kit (drugs, food, weapons) — which makes the "actively-using opt-out" airtight.
    /// Drafted/downed/mental carriers are excluded.
    /// </summary>
    public static class InventoryShare
    {
        /// <summary>A reachable carrier's tagged inventory stack of <paramref name="def"/>, closest first, or null.
        /// The worker's OWN scooped stock is considered first (distance 0), so it's used before fetching from others.</summary>
        public static Thing FindSharableStack(Map map, Pawn worker, ThingDef def)
        {
            if (map == null || worker == null || def == null)
                return null;

            // Short-circuit: no tagged stock of this def held by the worker or any eligible carrier (per-tick
            // cached) -> the find cannot succeed, so skip the colony walk + per-carrier reach/reserve checks.
            // CountSharable applies the same self + IsEligibleCarrier eligibility, so a 0 here is a true negative.
            if (CountSharable(map, worker, def) <= 0)
                return null;

            Thing best = null;
            int bestDist = int.MaxValue;
            // The worker's own inventory first — it's already in hand at the site (distance 0).
            ConsiderCarrierStack(worker, worker, def, ref best, ref bestDist);

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var carrier = pawns[i];
                if (IsEligibleCarrier(carrier, worker))
                    ConsiderCarrierStack(carrier, worker, def, ref best, ref bestDist);
            }
            return best;
        }

        /// <summary>Rank <paramref name="carrier"/>'s tagged stacks of <paramref name="def"/> into best/bestDist.
        /// The worker's own stock (self) bypasses the walk-to-carrier reach gate and ranks at distance 0.</summary>
        private static void ConsiderCarrierStack(Pawn carrier, Pawn worker, ThingDef def, ref Thing best, ref int bestDist)
        {
            var comp = carrier.GetComp<CompHauledToInventory>();
            var owner = carrier.inventory?.innerContainer;
            if (comp == null || owner == null)
                return;
            bool isSelf = carrier == worker;
            bool reachable = isSelf, reachChecked = isSelf;
            foreach (var tagged in comp.GetHashSet())
            {
                if (tagged == null || tagged.def != def || !owner.Contains(tagged))
                    continue;
                bool canReserve = worker.CanReserve(tagged);
                if (!isSelf && canReserve && !reachChecked)
                {
                    reachable = worker.CanReach(carrier, PathEndMode.Touch, Danger.Some);
                    reachChecked = true;
                }
                if (!isSelf && reachChecked && !reachable)
                    break; // remote carrier unreachable -> none of its stacks qualify
                if (!SharePolicy.ShouldIncludeStack(isSelf, reachable, canReserve, isUsable: true, withinRadius: true))
                    continue;
                int d = isSelf ? 0 : IntVec3Utility.ManhattanDistanceFlat(worker.Position, carrier.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = tagged;
                }
            }
        }

        /// <summary>
        /// Append every reachable, reservable, tagged inventory stack that this <paramref name="bill"/> can use
        /// (matching the recipe + bill filters, exactly like vanilla <c>IsUsableIngredient</c>) to
        /// <paramref name="outList"/>, deduped. Used to make carried materials count as bill ingredients.
        /// </summary>
        public static void AddSharableStacksForBill(Pawn worker, Bill bill, List<Thing> outList)
        {
            var map = worker?.Map;
            if (map == null || bill?.recipe == null || outList == null)
                return;

            // Respect the bill's player-set ingredient search radius exactly like vanilla
            // WorkGiver_DoBill, which bounds floor candidates by
            // (t.Position - billGiver.Position).LengthHorizontalSquared < radiusSq. 999f means
            // "Unlimited" -> no bound, so the fast path leaves the common case unchanged.
            float radius = bill.ingredientSearchRadius;
            bool bounded = radius < 999f;
            IntVec3 billGiverPos = IntVec3.Invalid;
            if (bounded)
            {
                if (bill.billStack?.billGiver is Thing giver && giver.Spawned)
                    billGiverPos = giver.Position;
                else
                    bounded = false; // can't locate the giver -> fall back to unbounded
            }
            float radiusSq = radius * radius;

            // The worker is already AT the bench: its own scooped stock is the closest possible candidate
            // (no fetch, no walk), so it bypasses the radius + reach gates that only bound fetching from others.
            // This is the fix for "a cook holding raw food still reads 'missing ingredients'".
            int beforeSelf = outList.Count;
            AddCarrierStacks(worker, worker, bill, outList);
            if (HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.verboseLogging)
            {
                var wc = worker.GetComp<CompHauledToInventory>();
                HDLog.Dbg($"DoBill ingredient-share for {worker} / {bill.recipe?.defName ?? "?"}: " +
                          $"worker tagged={wc?.GetHashSet().Count ?? 0}, self-pass added {outList.Count - beforeSelf} stack(s).");
            }

            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var carrier = pawns[i];
                if (!IsEligibleCarrier(carrier, worker)) // excludes carrier == worker -> no double-add of self
                    continue;
                if (bounded && (carrier.Position - billGiverPos).LengthHorizontalSquared >= radiusSq)
                    continue; // carrier is outside the bill's ingredient radius
                AddCarrierStacks(carrier, worker, bill, outList);
            }
        }

        /// <summary>Append <paramref name="carrier"/>'s tagged, bill-usable, reservable inventory stacks to
        /// <paramref name="outList"/>. The worker's OWN stock (carrier == worker) skips the walk-to-carrier
        /// reach gate (it's already in hand). Radius is enforced by the caller for non-self carriers.</summary>
        private static void AddCarrierStacks(Pawn carrier, Pawn worker, Bill bill, List<Thing> outList)
        {
            var comp = carrier.GetComp<CompHauledToInventory>();
            var owner = carrier.inventory?.innerContainer;
            if (comp == null || owner == null)
                return;
            bool isSelf = carrier == worker;
            bool reachable = isSelf, reachChecked = isSelf;
            foreach (var tagged in comp.GetHashSet())
            {
                if (tagged == null || !owner.Contains(tagged) || !IsUsableForBill(tagged, bill))
                    continue;
                bool canReserve = worker.CanReserve(tagged); // a stack reserved for the carrier's own job -> opt-out
                if (!isSelf && canReserve && !reachChecked)
                {
                    reachable = worker.CanReach(carrier, PathEndMode.Touch, Danger.Some);
                    reachChecked = true;
                }
                if (!isSelf && reachChecked && !reachable)
                    break;
                if (SharePolicy.ShouldIncludeStack(isSelf, reachable, canReserve, isUsable: true, withinRadius: true)
                    && !outList.Contains(tagged))
                    outList.Add(tagged);
            }
        }

        /// <summary>Mirror of vanilla <c>WorkGiver_DoBill.IsUsableIngredient</c>: allowed by the bill and by some recipe
        /// ingredient — PLUS the medical-care gate vanilla applies to every medicine candidate when the bill-giver is a
        /// Pawn (its region scan excludes medicine for pawn bill-givers entirely and re-adds it via
        /// AddEveryMedicineToRelevantThings, filtered by GetMedicalCareCategory; decompile-verified). Injected carried
        /// stock must not bypass the patient's medical-care restriction.</summary>
        internal static bool IsUsableForBill(Thing t, Bill bill)
        {
            if (!bill.IsFixedOrAllowedIngredient(t))
                return false;
            if (t.def.IsMedicine && bill.billStack?.billGiver is Pawn patient
                && !WorkGiver_DoBill.GetMedicalCareCategory(patient).AllowsMedicine(t.def))
                return false;
            var ings = bill.recipe.ingredients;
            for (int i = 0; i < ings.Count; i++)
                if (ings[i].filter.Allows(t))
                    return true;
            return false;
        }

        // Per-tick result cache for CountSharable: the construction work scan calls it once per missing-material
        // blueprint per def — a colony-wide pawn × inventory walk each time. Within one tick the answer for a
        // given (worker, def) cannot change, so cache it (cleared whenever the tick advances).
        //
        // [ThreadStatic] per the assembly's hook-reachable-scratch convention (PawnMassCache/CommonSenseCompat/
        // InventorySurplus): CountSharable is reachable from the construction work-giver scan postfixes, so an
        // off-thread work scan gets its own per-tick slot instead of racing a torn read + Clear() + insert on a
        // shared Dictionary. A [ThreadStatic] field can't have an initializer, so the dict is null-coalesced at
        // the read site.
        [ThreadStatic] private static int countCacheTick;
        [ThreadStatic] private static Dictionary<long, int> countCache;

        // Self-register the per-session count-memo clear with the game-load hygiene sweep (see CacheRegistry), so it
        // can never be forgotten. The static ctor runs once, the first time any member is touched (the only way the
        // memo can hold cross-session data); Clear resets the FinalizeInit (main) thread's slot — the `tick != -1`
        // populate guard in CountSharable is the actual cross-session safeguard.
        static InventoryShare() => CacheRegistry.Register(Clear);

        /// <summary>Total count of <paramref name="def"/> held (tagged) by the worker itself plus eligible
        /// carriers — for the construction availability gate (so a builder's OWN scooped stock counts too).</summary>
        public static int CountSharable(Map map, Pawn worker, ThingDef def)
        {
            if (map == null || def == null || worker == null)
                return 0;

            int tick = Find.TickManager?.TicksGame ?? -1;
            // Lazy per-thread dict init (a [ThreadStatic] field can't carry an initializer).
            var cache = countCache ?? (countCache = new Dictionary<long, int>());
            // tick == -1 (TickManager briefly null across a load): don't trust or populate the memo — a
            // cross-session quickload can land on the same tick number with colliding thingIDNumber keys.
            // Guard the stamp update on `tick != -1` (mirrors CompHauledToInventory.lastHealTick); when -1 we
            // recompute live and never cache. This is the load-bearing cross-session fix.
            if (tick != -1 && tick != countCacheTick)
            {
                countCacheTick = tick;
                cache.Clear();
            }
            long key = ((long)worker.thingIDNumber << 32) | (uint)def.shortHash;
            if (tick != -1 && cache.TryGetValue(key, out int cached))
                return cached;

            int total = CountTaggedOfDef(worker, def); // the worker's own scooped stock counts
            var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < pawns.Count; i++)
            {
                var carrier = pawns[i];
                if (IsEligibleCarrier(carrier, worker)) // excludes worker -> no double-count
                    total += CountTaggedOfDef(carrier, def);
            }
            if (tick != -1)
                cache[key] = total; // only memoize a real tick (see the -1 guard above)
            return total;
        }

        /// <summary>Drop the main thread's per-tick sharable-count memo and reset the tick stamp — called on game
        /// load (FinalizeInit) so an equal tick number across a quickload cannot serve a stale cross-session
        /// entry. Mirrors <see cref="PawnMassCache.Clear"/> (main-thread slot only; the `tick != -1` populate
        /// guard above is the actual cross-session safeguard).</summary>
        internal static void Clear()
        {
            countCacheTick = 0;
            countCache?.Clear();
        }

        private static int CountTaggedOfDef(Pawn carrier, ThingDef def)
        {
            var comp = carrier?.GetComp<CompHauledToInventory>();
            var owner = carrier?.inventory?.innerContainer;
            if (comp == null || owner == null)
                return 0;
            int total = 0;
            foreach (var tagged in comp.GetHashSet())
                if (tagged != null && tagged.def == def && owner.Contains(tagged))
                    total += tagged.stackCount;
            return total;
        }

        /// <summary>
        /// The pure CARRIER-LIVENESS sub-check shared by every "may colonist X draw from carrier Y" gate:
        /// the carrier is a distinct, spawned, alive, capable pawn — not self, unspawned, dead, downed,
        /// drafted, or in a mental state. This is the common core of <see cref="IsEligibleCarrier"/> (which
        /// layers the HD-preloaded-stock job-def guard on top) and <see cref="CarriedHaulShare"/>'s
        /// carried-stack check (which layers carry-tracker / haul-job guards on top); both call this so the
        /// liveness clauses can never drift between them. Returns FALSE (not a live carrier) on null.
        /// </summary>
        internal static bool IsLiveShareCarrier(Pawn carrier, Pawn worker)
        {
            if (carrier == null || carrier == worker)
                return false;
            if (!carrier.Spawned || carrier.Dead || carrier.Downed)
                return false;
            if (carrier.Drafted) // drafted pawns never share
                return false;
            if (carrier.InMentalState)
                return false;
            return true;
        }

        /// <summary>A carrier whose inventory another colonist may draw from: excludes self, unspawned,
        /// dead, downed, drafted, mental, and mid-HD-batch holders. Shared with the Meals On Wheels food
        /// postfix (<see cref="Patch_TryFindBestFoodSourceFor"/>), which layers food-specific guards
        /// (baby-feed, forbidden, allowed-area, reach, stack reservation) on top.</summary>
        internal static bool IsEligibleCarrier(Pawn carrier, Pawn worker)
        {
            // Liveness core (self/spawned/dead/downed/drafted/mental) — shared with CarriedHaulShare so the
            // two can never drift; this gate then adds the HD-preloaded-stock job-def guard below.
            if (!IsLiveShareCarrier(carrier, worker))
                return false;
            // A pawn mid batch-craft is actively holding the ingredients it pre-loaded for its own recipe runs
            // (deliberately untagged so they're not shared) — but the self-heal could re-tag them if it also carries
            // scooped stock of the same def. Never let another pawn pull from such a holder, so its run can't be
            // starved of its own pre-loaded ingredients mid-execution. (Membership: HdJobDefSets — the single
            // source of truth, so a newly-added preloaded-stock driver is covered everywhere it must be.)
            var cjd = carrier.CurJobDef;
            if (cjd != null && HdJobDefSets.HoldsUnshareablePreloadedStock.Contains(cjd))
                return false;
            return true;
        }
    }
}
