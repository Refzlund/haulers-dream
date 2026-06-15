using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// "Build from inventory" source reader — the ORGANIC sibling to <see cref="InventoryShare"/>. Where
    /// <see cref="InventoryShare"/> reads only HD-TAGGED scooped stock (a <c>CompHauledToInventory</c> hash
    /// set), this reads the WHOLE <c>carrier.inventory.innerContainer</c> of a needed material def — so
    /// pre-existing organic inventory a pawn/animal was already carrying (player-loaded caravan cargo, a
    /// colonist's traded/manually-loaded steel) is a valid construction material source. Organic is a
    /// SUPERSET of tagged (tagged items live in the same innerContainer), so the availability postfix uses
    /// the organic count INSTEAD of the tagged count — never both — to avoid double-counting one stack.
    ///
    /// Classifies each holder as <see cref="BuildMaterialSource.Own"/> / <see cref="BuildMaterialSource.Other"/>
    /// / <see cref="BuildMaterialSource.PackAnimal"/> (the worker itself / a colonist / a player-faction
    /// animal) and picks the best stack via <see cref="BuildFromInventorySource.Compare"/> (own → other →
    /// pack, then nearest, then stable index). Reuses <see cref="InventoryShare.IsEligibleCarrier"/> (excludes
    /// self/unspawned/dead/downed/drafted/mental/mid-HD-job) and <see cref="SharePolicy.ShouldIncludeStack"/>
    /// (self-bypass / reachable / reservable) verbatim, plus two guards the tagged path doesn't need: never
    /// source from an <c>IsFormingCaravan</c> holder (don't cannibalize earmarked caravan cargo) nor from a
    /// holder standing on a cell forbidden to the worker. The pack-animal branch additionally respects
    /// <c>enableOnNonHomeMaps</c> off a home map; own/other sourcing stays available everywhere.
    /// </summary>
    public static class OrganicInventoryShare
    {
        /// <summary>Whether this map allows sourcing from OTHER pawns (other colonists + pack animals) — the
        /// "walk to another pawn and fetch" sources. On the HOME map they're always allowed; on a non-home
        /// (caravan/raid) map they're gated behind <c>enableOnNonHomeMaps</c> (the HD-doesn't-work-on-away-maps
        /// contract). The worker's OWN inventory is always sourced regardless of map (it's already in hand,
        /// distance 0 — the least intrusive source, and the most direct read of "build from what I carry").</summary>
        private static bool OtherPawnSourcesAllowedHere(Map map, HaulersDreamSettings s)
            => s.enableOnNonHomeMaps || (map != null && map.IsPlayerHome);

        /// <summary>Classify a holder as the worker's OWN inventory, another colonist (Other), or a player-faction
        /// PACK animal — the source-priority rank (own &lt; other &lt; pack) fed to <see cref="BuildFromInventorySource.Compare"/>.</summary>
        private static BuildMaterialSource ClassifyHolder(Pawn carrier, Pawn worker)
        {
            if (carrier == worker)
                return BuildMaterialSource.Own;
            return carrier.RaceProps != null && carrier.RaceProps.Animal
                ? BuildMaterialSource.PackAnimal
                : BuildMaterialSource.Other;
        }

        /// <summary>A holder this worker may organically source build material from — on top of
        /// <see cref="InventoryShare.IsEligibleCarrier"/>, also reject forming-caravan holders (earmarked cargo)
        /// and holders standing on a cell forbidden to the worker. (Self is handled by the caller, not here.)</summary>
        private static bool IsEligibleOrganicHolder(Pawn carrier, Pawn worker)
        {
            if (!InventoryShare.IsEligibleCarrier(carrier, worker))
                return false;
            // [ORG] Skip a holder riding INSIDE a vehicle (seat/cargo) — its inventory is unreachable, so pathing to
            // it is wasted (mirrors EatFromInventory's MOW guard). Sourcing from a PARKED vehicle's cargo stays ON (a
            // parked vehicle is not InVehicle). Both organic loops (CountOrganic + FindOrganicStack) gate through this
            // helper. Gated on InVehicle ONLY (a safety fix, not a feature): InVehicle returns false when VF is absent.
            if (VehicleFrameworkCompat.InVehicle(carrier))
                return false;
            if (carrier.IsFormingCaravan())
                return false; // never disturb cargo earmarked for a departing caravan
            if (carrier.Position.IsForbidden(worker))
                return false;
            return true;
        }

        // Per-tick result cache for CountOrganic: the construction availability scan calls it once per
        // missing-material blueprint per def — a colony-wide pawn × inventory walk each time. Within one tick
        // the answer for a given (worker, def) cannot change, so cache it (cleared whenever the tick advances).
        // Mirrors InventoryShare.CountSharable's countCache pattern (and vanilla ItemAvailability's per-tick cache).
        private static int countCacheTick = -1;
        private static readonly Dictionary<long, int> countCache = new Dictionary<long, int>();

        /// <summary>Total ORGANIC units of <paramref name="def"/> held by the worker itself plus eligible
        /// carriers (other colonists, and — when allowed — pack animals) — for the construction availability
        /// gate, so a job is OFFERED when material lives only in inventory. Counts the whole innerContainer of
        /// the def (a superset of the tagged count). Per-tick cached.</summary>
        public static int CountOrganic(Map map, Pawn worker, ThingDef def)
        {
            if (map == null || worker == null || def == null)
                return 0;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return 0;

            int tick = Find.TickManager?.TicksGame ?? -1;
            if (tick != countCacheTick)
            {
                countCacheTick = tick;
                countCache.Clear();
            }
            long key = ((long)worker.thingIDNumber << 32) | (uint)def.shortHash;
            if (countCache.TryGetValue(key, out int cached))
                return cached;

            int total = CountOrganicOfDef(worker, def); // the worker's own organic stock always counts (any map)

            // Other colonists + pack animals are "fetch from another pawn" sources, gated off a non-home map.
            if (OtherPawnSourcesAllowedHere(map, s))
            {
                var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    var carrier = pawns[i];
                    if (!IsEligibleOrganicHolder(carrier, worker)) // excludes worker -> no double-count
                        continue;
                    total += CountOrganicOfDef(carrier, def);
                }
            }
            countCache[key] = total;
            return total;
        }

        /// <summary>Sum every organic stack of <paramref name="def"/> in <paramref name="carrier"/>'s inventory.</summary>
        private static int CountOrganicOfDef(Pawn carrier, ThingDef def)
        {
            var owner = carrier?.inventory?.innerContainer;
            if (owner == null)
                return 0;
            int total = 0;
            for (int i = 0; i < owner.Count; i++)
            {
                var t = owner[i];
                if (t != null && t.def == def)
                    total += t.stackCount;
            }
            return total;
        }

        /// <summary>The best reachable, reservable ORGANIC inventory stack of <paramref name="def"/> for the
        /// worker to fetch — own first (distance 0, no reach/reserve), then other colonists, then pack animals,
        /// each nearest-first (<see cref="BuildFromInventorySource.Compare"/>). Null when none qualifies. This is
        /// the F3b deliver-job source: the chosen stack stays IN the holder's inventory and the vanilla
        /// HaulToContainer driver pulls it out via <c>canGotoSpawnedParent</c> + <c>canTakeFromInventory</c>.</summary>
        public static Thing FindOrganicStack(Map map, Pawn worker, ThingDef def)
        {
            if (map == null || worker == null || def == null)
                return null;
            var s = HaulersDreamMod.Settings;
            if (s == null)
                return null;

            Thing best = null;
            BuildMaterialSource bestSource = BuildMaterialSource.PackAnimal;
            int bestDist = int.MaxValue;
            int bestIndex = int.MaxValue;
            int idx = 0;

            // The worker's own inventory first — already in hand at the site (distance 0, self-bypass; any map).
            ConsiderHolder(worker, worker, def, BuildMaterialSource.Own,
                ref best, ref bestSource, ref bestDist, ref bestIndex, ref idx);

            // Other colonists + pack animals are "fetch from another pawn" sources, gated off a non-home map.
            if (OtherPawnSourcesAllowedHere(map, s))
            {
                var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    var carrier = pawns[i];
                    if (!IsEligibleOrganicHolder(carrier, worker))
                        continue;
                    ConsiderHolder(carrier, worker, def, ClassifyHolder(carrier, worker),
                        ref best, ref bestSource, ref bestDist, ref bestIndex, ref idx);
                }
            }
            return best;
        }

        /// <summary>Rank <paramref name="carrier"/>'s organic stacks of <paramref name="def"/> into the running
        /// best by <see cref="BuildFromInventorySource.Compare"/>. The worker's own stock (self) bypasses the
        /// walk-to-carrier reach gate and ranks at distance 0; others require reach + reservation.</summary>
        private static void ConsiderHolder(Pawn carrier, Pawn worker, ThingDef def, BuildMaterialSource source,
            ref Thing best, ref BuildMaterialSource bestSource, ref int bestDist, ref int bestIndex, ref int idx)
        {
            var owner = carrier.inventory?.innerContainer;
            if (owner == null)
                return;
            bool isSelf = carrier == worker;
            bool reachable = isSelf, reachChecked = isSelf;
            int dist = isSelf ? 0 : IntVec3Utility.ManhattanDistanceFlat(worker.Position, carrier.Position);
            for (int i = 0; i < owner.Count; i++)
            {
                var stack = owner[i];
                if (stack == null || stack.def != def)
                    continue;
                bool canReserve = worker.CanReserve(stack);
                if (!isSelf && canReserve && !reachChecked)
                {
                    reachable = worker.CanReach(carrier, PathEndMode.Touch, Danger.Some);
                    reachChecked = true;
                }
                if (!isSelf && reachChecked && !reachable)
                    return; // remote carrier unreachable -> none of its stacks qualify
                if (!SharePolicy.ShouldIncludeStack(isSelf, reachable, canReserve, isUsable: true, withinRadius: true))
                    continue;
                if (best == null || BuildFromInventorySource.Compare(
                        source, dist, idx, bestSource, bestDist, bestIndex) < 0)
                {
                    best = stack;
                    bestSource = source;
                    bestDist = dist;
                    bestIndex = idx;
                }
                idx++;
            }
        }
    }
}
