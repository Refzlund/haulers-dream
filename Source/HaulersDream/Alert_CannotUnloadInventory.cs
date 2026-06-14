using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// The black-hole SAFETY NET. A critical (red, pulsing, bottom-right) alert — like vanilla "Fire!" — that
    /// fires when one or more player pawns are carrying items they cannot put away:
    ///   • Condition A (no destination — the literal "no stockpiles available" case): a NON-personal-kit
    ///     surplus stack in the pawn's inventory has NO destination anywhere — no stockpile, no dumping zone,
    ///     and not even a desperate home-area cell. Scanned over ALL inventory (not just HD-tagged items), so
    ///     an item that lost its tag is still surfaced. Safe from mods that legitimately stash items in
    ///     inventory (Simple Sidearms etc.): those items HAVE a destination (a weapon stockpile), so they are
    ///     never flagged.
    ///   • Condition B (stuck): an HD-tagged surplus stack has been held longer than alertStuckHours (per
    ///     ITEM, so a busy hauler refreshing its scoop clock can't mask one stranded stack) — a destination
    ///     exists but unloading isn't happening (storage unreachable, or another mod keeps cancelling the
    ///     haul/unload job, e.g. an autocast mod).
    ///
    /// ONE alert covers ALL affected pawns: the report's culprits are those pawns, so hovering points arrows
    /// at them and clicking cycles the camera through them (vanilla <see cref="Alert.OnClick"/> +
    /// <see cref="AlertReport.CulpritsAre(System.Collections.Generic.List{Pawn})"/>). Auto-discovered by
    /// RimWorld (Alert leaf subclass) — no XML. Guarantees inventories are never SILENT black holes, while
    /// the guards below keep it from nagging on legitimate in-transit loads.
    /// </summary>
    public class Alert_CannotUnloadInventory : Alert_Critical
    {
        private List<Pawn> lastCulprits = new List<Pawn>();
        private bool anyNoStorage;

        // Debounce: a pawn must stay problematic for >= GraceTicks before it's flagged, so a transient
        // no-destination/stuck window (a stockpile momentarily full, a one-frame interrupt gap) can't flash
        // the critical alert. Keyed by pawn, cleared when a pawn stops being problematic.
        private readonly Dictionary<Pawn, int> problemSince = new Dictionary<Pawn, int>();
        private static List<Pawn> tmpDeadKeys = new List<Pawn>();
        private const int GraceTicks = 2500; // ~1 in-game hour

        // Recompute throttle: GetReport is hit from the per-frame render path (Recalculate, OnClick,
        // DrawInfoPane). Recompute at most ~once/second and cache, so the StoreUtility probes and the scan
        // don't run every frame.
        private int lastComputeTick = -99999;
        private AlertReport cachedReport = AlertReport.Inactive;
        private const int RecomputeInterval = 60;

        public Alert_CannotUnloadInventory()
        {
            defaultLabel = "HaulersDream.Alert.CannotUnload".Translate();
            defaultExplanation = "HaulersDream.Alert.CannotUnloadDesc.Stuck".Translate("");
        }

        // The persistent red box (with its arrows + click-cycle) IS the warning; suppress the extra recurring
        // top-left "critical alert" letter Alert_Critical would otherwise post each activation.
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
            if (s == null || !s.alertCannotUnload || Find.Maps == null || Find.TickManager == null)
                return AlertReport.Inactive;

            int now = Find.TickManager.TicksGame;
            if (lastComputeTick >= 0 && now - lastComputeTick < RecomputeInterval)
                return cachedReport; // cheap path for repeated same-second calls (hover/click/redraw)
            lastComputeTick = now;

            try
            {
                cachedReport = ComputeReport(s, now, StuckTicksFor(s));
            }
            catch (System.Exception e)
            {
                // This GetReport is reachable UNPROTECTED on the OnGUI render path — Alert.DrawInfoPane (on
                // mouseover) and Alert.OnClick both call Recalculate()/GetReport(), and vanilla does NOT wrap
                // the per-alert draw/mouseover/click loop in AlertsReadoutOnGUI. An uncaught throw here would
                // abort the rest of the UIRootOnGUI frame BEFORE the window stack draws, blanking the whole HUD
                // ("all UI invisible but still clickable"). Per the no-suppression rule this is the legitimate
                // boundary exception: keep the error LOUD (logged, never swallowed) but return the last good
                // report instead of taking the entire UI down. The 60-tick throttle above also rate-limits this.
                Log.ErrorOnce("[Hauler's Dream] Alert_CannotUnloadInventory.GetReport threw; returning the cached "
                    + "report to keep the HUD alive. This is a bug — please report this stack trace:\n" + e,
                    0x4844A1E7);
            }
            return cachedReport;
        }

        private static int StuckTicksFor(HaulersDreamSettings s)
            => Mathf.Max(2500, Mathf.RoundToInt(s.alertStuckHours * 2500f)); // 2500 ticks/in-game hour

        // The actual scan, extracted so GetReport can guard it (see the try/catch above). Updates lastCulprits /
        // anyNoStorage (read by GetLabel/GetExplanation) and returns the report.
        private AlertReport ComputeReport(HaulersDreamSettings s, int now, int stuckTicks)
        {
            var culprits = new List<Pawn>();
            bool noStore = false;

            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    var fault = Evaluate(p, s, now, stuckTicks, out bool pNoStore);
                    if (fault == UnloadFault.None)
                    {
                        // No fault -> reset the debounce clock. (The ONLY reset path: a momentary in-transit
                        // job does NOT clear it, so a mod that keeps cancelling/re-queuing the unload faster
                        // than the grace window can't keep the timer pinned at zero forever.)
                        problemSince.Remove(p);
                        continue;
                    }

                    // A real fault exists (undeliverable item, or a tagged item stuck past the threshold).
                    // Start/keep the debounce clock running regardless of whether an unload happens to be
                    // running THIS frame — the fault is what's timed, not the gaps between retries.
                    if (!problemSince.TryGetValue(p, out int since))
                    {
                        since = now;
                        problemSince[p] = since;
                    }
                    // InFlight = the fault is real but an HD unload/gather is running or queued right now;
                    // give it a chance to clear, so don't surface the pawn THIS frame — but the clock keeps
                    // ticking, so if it's still faulted on a later frame when nothing is in flight, it flags.
                    if (fault == UnloadFault.Stranded && now - since >= GraceTicks)
                    {
                        culprits.Add(p);
                        if (pNoStore)
                            noStore = true;
                    }
                }
            }

            PruneDeadDebounceKeys();

            lastCulprits = culprits;
            anyNoStorage = noStore;
            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        private void PruneDeadDebounceKeys()
        {
            if (problemSince.Count == 0)
                return;
            tmpDeadKeys.Clear();
            foreach (var kv in problemSince)
                if (kv.Key == null || kv.Key.Destroyed || !kv.Key.Spawned)
                    tmpDeadKeys.Add(kv.Key);
            for (int i = 0; i < tmpDeadKeys.Count; i++)
                problemSince.Remove(tmpDeadKeys[i]);
            tmpDeadKeys.Clear();
        }

        /// <summary>How a pawn relates to the cannot-unload fault this frame.</summary>
        private enum UnloadFault
        {
            None,     // nothing wrong (or deliberately exempt) -> reset the debounce clock
            InFlight, // a real fault, but an HD unload/gather is running/queued -> don't surface yet, KEEP the clock
            Stranded  // a real fault and no in-transit job to clear it -> surface once debounced
        }

        /// <summary>Classify whether this pawn is carrying surplus it genuinely cannot get rid of. The FAULT
        /// (Conditions A/B) is evaluated independently of whether an unload happens to be running right now, so
        /// a mod that keeps cancelling/re-queuing the unload can't keep resetting the debounce clock and hiding
        /// the alert forever (the in-transit state only DEFERS surfacing — InFlight — it never clears the
        /// fault).</summary>
        private static UnloadFault Evaluate(Pawn p, HaulersDreamSettings s, int now, int stuckTicks, out bool noStorage)
        {
            noStorage = false;
            var inner = p?.inventory?.innerContainer;
            if (inner == null || p.Map == null)
                return UnloadFault.None;

            var comp = p.GetComp<CompHauledToInventory>();
            // Only watch pawns Hauler's Dream actually manages: a pawn with no HD tags AND not HD-eligible
            // (a pack animal, a hauling-incapable colonist, a mech the player loaded manually) is carrying
            // cargo HD never scooped — not its responsibility, and flagging it would nag on a deliberate
            // stash. A pawn WITH tags (or HD-eligible) keeps the full lost-tag safety net. (Same eligibility
            // the scoop/unload sides gate on — the strand-symmetry rule.)
            if ((comp == null || comp.PeekHashSet().Count == 0) && !YieldRouter.IsEligible(p))
                return UnloadFault.None;

            // Deliberate, non-transient exemptions — the mod will genuinely not unload these, so they are not
            // a fault at all: a drafted pawn first.
            if (p.Drafted)
                return UnloadFault.None;

            // Caravan / away map: there is no player storage here, so Condition A's storage-only destination
            // probe would mis-report EVERY surplus stack as a "no destination" black hole. Decide the fault by
            // PACK-ANIMAL availability instead. The opportunistic offload requires the auto-unload master
            // (markForUnload) + the caravan toggle (autoDivertToPackAnimal) + the mod being active on non-home
            // maps; with any of those off, the loot legitimately rides home in inventory (not a fault).
            if (!p.Map.IsPlayerHome)
            {
                if (!s.enableOnNonHomeMaps || !s.markForUnload || !s.autoDivertToPackAnimal)
                    return UnloadFault.None;
                if (!PackAnimalLoad.HasDepositableSurplus(p))
                    return UnloadFault.None;            // only personal keep-stock to carry -> nothing to offload
                if (PackAnimalLoad.HasLoadJob(p))
                    return UnloadFault.InFlight;          // a load trip is running/queued -> give it a chance
                if (PackAnimalLoad.FindCarrier(p) == null)
                    return UnloadFault.None;            // no usable pack animal reachable -> loot rides home (intended)
                // A usable carrier exists and there's depositable surplus, but no load is happening. Only a
                // GENUINE fault once a tagged stack has been stuck past the threshold (mirror Condition B's
                // per-item clock) — otherwise it's just the normal accumulate window before the next offload
                // trigger fires. There IS a destination (the animal), so this is the "Stuck" variant, never
                // "NoStorage" (noStorage stays false).
                if (comp != null)
                    foreach (var t in comp.PeekHashSet())
                        if (t != null && !t.Destroyed && inner.Contains(t)
                            && InventorySurplus.SurplusOf(p, t) > 0
                            && now - comp.FirstTaggedTick(t) > stuckTicks)
                            return UnloadFault.Stranded;
                return UnloadFault.None;
            }

            // ---- the real fault (independent of any in-transit job) ----
            bool fault = false;

            // Condition A — any non-kit SURPLUS stack with NO destination anywhere (true undeliverable black
            // hole). Scanned over ALL inventory, so an item that lost its HD tag is still surfaced; safe from
            // mods that deliberately stash items in inventory because those items HAVE a destination.
            for (int i = 0; i < inner.Count; i++)
            {
                var t = inner[i];
                if (t == null || InventorySurplus.SurplusOf(p, t) <= 0)
                    continue; // personal kit (keep-stock) or nothing surplus -> not a black hole
                if (!InventorySurplus.HasUnloadDestination(p, t))
                {
                    noStorage = true;
                    fault = true;
                    break;
                }
            }

            // Condition B — an HD-tagged surplus stack has been stuck too long (per ITEM, so a busy hauler's
            // refreshed scoop clock can't mask one stranded stack). A destination exists but unloading isn't
            // happening: storage unreachable, or a mod keeps cancelling the haul/unload job. Only when an
            // AUTOMATIC unload was actually promised (markForUnload on) — in gizmo-only mode the pawn is meant
            // to hold its load until the player presses the button, so a lingering deliverable load is not a
            // fault to nag about (Condition A still surfaces a true no-destination black hole either way).
            if (!fault && s.markForUnload && comp != null)
            {
                foreach (var t in comp.PeekHashSet())
                {
                    if (t == null || t.Destroyed || !inner.Contains(t))
                        continue;
                    if (InventorySurplus.SurplusOf(p, t) <= 0)
                        continue;
                    if (now - comp.FirstTaggedTick(t) > stuckTicks)
                    {
                        fault = true;
                        break;
                    }
                }
            }

            if (!fault)
                return UnloadFault.None;

            // The fault is real. If the mod's own in-transit job (a deliberate gather/craft/deliver/sweep
            // load, or an unload already running/queued) is on it RIGHT NOW, defer surfacing — but the caller
            // keeps the debounce clock running, so a fault that persists across the in-transit window (the
            // autocast-keeps-cancelling case) still flags.
            var jd = p.CurJobDef;
            if (jd == HaulersDreamDefOf.HaulersDream_UnloadInventory
                || jd == HaulersDreamDefOf.HaulersDream_BillPrepGather
                || jd == HaulersDreamDefOf.HaulersDream_BatchCraft
                || jd == HaulersDreamDefOf.HaulersDream_OverloadConstructDeliver
                || jd == HaulersDreamDefOf.HaulersDream_ConstructDeliverBuild
                || jd == HaulersDreamDefOf.HaulersDream_BulkHaul
                || jd == HaulersDreamDefOf.HaulersDream_SelfPickup
                || PawnUnloadChecker.HasQueuedUnload(p))
                return UnloadFault.InFlight;

            return UnloadFault.Stranded;
        }
    }
}
