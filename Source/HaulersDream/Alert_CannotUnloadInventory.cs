using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The black-hole SAFETY NET. A critical (red, pulsing, bottom-right) alert — like vanilla "Fire!" —
    /// that fires when one or more player pawns are carrying scooped haul items they cannot put away:
    ///   • Condition A (no destination): nothing on the map can store the items — no stockpile, no dumping
    ///     zone, and not even a reachable home-area cell. This is the literal "no stockpiles available" case.
    ///   • Condition B (stuck): a destination DOES exist, but the pawn has held tagged items far longer than
    ///     any normal unload should take — storage is unreachable from where it is, or another mod keeps
    ///     cancelling the haul/unload job (e.g. an autocast mod), so the load is going nowhere.
    ///
    /// ONE alert covers ALL affected pawns (never one per pawn): the report's culprits are those pawns, so
    /// hovering the alert points arrows at them and clicking cycles the camera through them — the same
    /// behaviour as the "Fire!" alert / Geothermal placement (vanilla <see cref="Alert.OnClick"/> +
    /// <see cref="AlertReport.CulpritsAre(System.Collections.Generic.List{Pawn})"/>).
    ///
    /// This guarantees inventories never become SILENT black holes: even if some path fails to unload an
    /// item, the player is told and the pawn is flagged. RimWorld auto-discovers every non-abstract Alert
    /// subclass (AllLeafSubclasses + Activator.CreateInstance), so no XML/registration is needed.
    /// </summary>
    public class Alert_CannotUnloadInventory : Alert_Critical
    {
        private List<Pawn> lastCulprits = new List<Pawn>();
        private bool anyNoStorage; // at least one culprit has NOWHERE to store its items (vs merely stuck)

        public Alert_CannotUnloadInventory()
        {
            defaultLabel = "HaulersDream.Alert.CannotUnload".Translate();
            defaultExplanation = "HaulersDream.Alert.CannotUnloadDesc.Stuck".Translate("");
        }

        // The persistent red box (plus its arrows/click-cycle) IS the warning the player asked for; suppress
        // the extra recurring top-left "critical alert" letter Alert_Critical would also post each activation.
        public override bool DoMessage => false;

        public override string GetLabel()
            => "HaulersDream.Alert.CannotUnload".Translate(lastCulprits.Count);

        public override TaggedString GetExplanation()
        {
            if (lastCulprits.Count == 0)
                return "";
            var names = new List<string>();
            for (int i = 0; i < lastCulprits.Count; i++)
                names.Add(lastCulprits[i].LabelShortCap);
            string key = anyNoStorage
                ? "HaulersDream.Alert.CannotUnloadDesc.NoStorage"
                : "HaulersDream.Alert.CannotUnloadDesc.Stuck";
            return key.Translate(names.ToLineList("  - "));
        }

        public override AlertReport GetReport()
        {
            var s = HaulersDreamMod.Settings;
            if (s == null || !s.alertCannotUnload || Find.Maps == null)
                return AlertReport.Inactive;

            var culprits = new List<Pawn>();
            bool noStore = false;
            int now = Find.TickManager?.TicksGame ?? 0;
            int stuckTicks = Mathf.Max(2500, Mathf.RoundToInt(s.alertStuckHours * 2500f)); // 2500 ticks/in-game hour

            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (IsStuck(pawns[i], now, stuckTicks, out bool pawnNoStore))
                    {
                        culprits.Add(pawns[i]);
                        if (pawnNoStore)
                            noStore = true;
                    }
                }
            }

            lastCulprits = culprits;
            anyNoStorage = noStore;
            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        /// <summary>Is this pawn carrying tagged haul items it genuinely cannot get rid of?</summary>
        private static bool IsStuck(Pawn p, int now, int stuckTicks, out bool noStorage)
        {
            noStorage = false;
            if (p?.inventory?.innerContainer == null || p.Map == null)
                return false;
            var comp = p.GetComp<CompHauledToInventory>();
            if (comp == null)
                return false;
            var carried = comp.GetHashSet();
            if (carried.Count == 0)
                return false;
            // Actively resolving it (an unload is running or queued) -> not stuck; give it a chance to finish.
            if (p.CurJobDef == HaulersDreamDefOf.HaulersDream_UnloadInventory || PawnUnloadChecker.HasQueuedUnload(p))
                return false;

            // Condition A — NO destination anywhere for the carried items: no stockpile, no dumping zone,
            // not even a desperate home-area cell. The exact "carries items, no stockpile available" case.
            // (Break on the first item that DOES have a home, so the common case is one cheap storage probe.)
            bool anyDestination = false;
            var inner = p.inventory.innerContainer;
            foreach (var t in carried)
            {
                if (t == null || t.Destroyed || !inner.Contains(t))
                    continue;
                if (StoreUtility.TryFindBestBetterStorageFor(t, p, p.Map, StoragePriority.Unstored, p.Faction, out _, out _)
                    || StoreUtility.TryFindStoreCellNearColonyDesperate(t, p, out _))
                {
                    anyDestination = true;
                    break;
                }
            }
            if (!anyDestination)
            {
                noStorage = true;
                return true;
            }

            // Condition B — a destination exists but the load has been stranded too long. lastYieldTick is the
            // last scoop; an actively-hauling pawn refreshes it constantly, so only a genuinely stuck load
            // (storage unreachable, or a mod repeatedly cancelling the unload job) ages past the threshold.
            return now - comp.lastYieldTick > stuckTicks;
        }
    }
}
