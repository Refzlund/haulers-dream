using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Adds a "Plan prioritized {work}…" option to the right-click float menu, next to the vanilla
    /// "Prioritize {work}" option, whenever the clicked thing is routable work (a designated plant to
    /// harvest/cut, a vein to mine, a blueprint to build, …). Choosing it opens <see cref="Dialog_PlanRoute"/>.
    /// This is a vanilla auto-discovered provider (all FloatMenuOptionProvider subclasses are found by
    /// reflection), so it needs zero Harmony patching.
    ///
    /// We emit options from the per-MENU <see cref="GetOptions"/> (not the per-thing GetSingleOptionFor) so we can
    /// DEDUPE: a single cell often has several things that map to the SAME work kind (two filth tiles → one
    /// "cleaning", several blueprints → one "constructing"). The per-thing path would add a duplicate option for
    /// each; instead we walk every clicked thing once and emit one option per distinct kind (keyed by label,
    /// mirroring vanilla's own tmpUsedLabels dedup), anchored on the first thing of that kind.
    /// </summary>
    public class FloatMenuOptionProvider_PlanRoute : FloatMenuOptionProvider
    {
        public override bool Drafted => false;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => false;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            var things = context?.ClickedThings;
            if (pawn == null || things == null)
                yield break;
            if (HaulersDreamMod.Settings == null || !HaulersDreamMod.Settings.planRoutes)
                yield break; // route planner disabled in mod options

            var seenLabels = new HashSet<string>();
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null)
                    continue;

                RouteWorkKind kind;
                try { kind = WorkKindResolver.Resolve(pawn, thing); }
                catch { kind = null; }
                if (kind == null)
                    continue;

                string label = "HaulersDream.PlanRoute.Option".Translate(kind.gerund);
                if (!seenLabels.Add(label))
                    continue; // one "Plan prioritized X…" per distinct work kind, however many targets share it

                Thing anchor = thing;
                RouteWorkKind capturedKind = kind;

                // A construction route is ALWAYS offered — even when this instant has no deliverable materials.
                // This matches WorkKindResolver's documented design ("a route over [blueprints] should be plannable
                // even when materials aren't deliverable this instant"): a build run is worth queuing regardless,
                // and the route delivers to whichever stops have free materials (in optimal order) while the rest
                // build as materials become available. The OLD gate disabled the WHOLE option whenever just the
                // ANCHOR lacked free materials right now — wrong even when other stops are fully stocked.
                if (HaulersDreamMod.Settings != null && HaulersDreamMod.Settings.verboseLogging
                    && kind.scanner is WorkGiver_ConstructDeliverResourcesToBlueprints)
                {
                    bool hadJob;
                    try { hadJob = RouteExecutor.BuildJobForStop(pawn, anchor, kind) != null; }
                    catch { hadJob = false; }
                    HDLog.Dbg($"construct route offered for {anchor.LabelShort} (def {anchor.def?.defName}, " +
                              $"blueprint={anchor.def?.IsBlueprint}, frame={anchor.def?.IsFrame}, deliverableNow={hadJob}).");
                }

                var option = new FloatMenuOption(label, () =>
                    Find.WindowStack.Add(new Dialog_PlanRoute(pawn, anchor, capturedKind)))
                {
                    iconThing = anchor,
                };
                yield return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, anchor);
            }
        }
    }
}
