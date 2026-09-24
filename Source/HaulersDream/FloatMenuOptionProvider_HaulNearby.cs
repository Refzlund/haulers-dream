using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// "Haul everything nearby": a one-click order that starts the bulk sweep directly (pick up the clicked
    /// haulable AND everything haulable around it into inventory, then one storage trip), so the player needn't
    /// right-click → "Prioritize hauling" twice to trigger bulk hauling. The explicit counterpart to the
    /// automatic SecondTasked behavior — it always sweeps regardless of the trigger setting. Auto-discovered
    /// FloatMenuOptionProvider, shown alongside vanilla "Prioritize hauling". The exact forced WorkGiver
    /// definition supplies live command preferences; its native duplicate is suppressed by a narrow bridge.
    /// Falls back to a normal forced haul if there's nothing worth sweeping (single stack that fits in hands).
    /// </summary>
    public class FloatMenuOptionProvider_HaulNearby : FloatMenuOptionProvider
    {
        public override bool Drafted => true;
        public override bool Undrafted => true;
        public override bool Multiselect => false;
        public override bool MechanoidCanDo => true;
        public override bool CanSelfTarget => false;

        public override IEnumerable<FloatMenuOption> GetOptions(FloatMenuContext context)
        {
            var pawn = context?.FirstSelectedPawn;
            var things = context?.ClickedThings;
            if (pawn == null || things == null || pawn.Map == null)
                yield break;
            if (!NearbyHaulCommand.Enabled || !MultiplayerCompat.NearbyHaulLocalUi)
                yield break;

            if (pawn.Drafted && NearbyHaulCommand.Definition?.canBeDoneWhileDrafted != true)
                yield break;

            string disabledReason = null;
            for (int i = 0; i < things.Count; i++)
            {
                var clicked = things[i];
                if (!NearbyHaulCommand.IsGroundTarget(pawn, clicked))
                    continue;
                bool allowed = NearbyHaulCommand.CanOffer(pawn, clicked, out var reason);
                if (!allowed)
                {
                    // An overlapping later item may still be usable. Keep one reason only if no
                    // candidate succeeds; one bad stack must not hide an otherwise valid order.
                    if (disabledReason == null)
                        disabledReason = reason;
                    continue;
                }
                var clickedLocal = clicked;
                int mapId = pawn.Map.uniqueID;
                var option = new FloatMenuOption("HaulersDream.HaulNearby.Option".Translate(), () =>
                {
                    NearbyHaulCommand.Dispatch(pawn, clickedLocal, mapId, KeyBindingDefOf.QueueOrder.IsDownEvent);
                })
                {
                    iconThing = clicked,
                };
                var decorated = FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clicked);
                int draftedPriority = NearbyHaulCommand.Definition.autoTakeablePriorityDrafted;
                if (pawn.Drafted && draftedPriority != -1)
                {
                    decorated.autoTakeable = true;
                    decorated.autoTakeablePriority = draftedPriority;
                }
                yield return decorated;
                yield break; // one bulk option per click; vanilla's single "Prioritize hauling" still appears alongside
            }
            if (disabledReason != null)
                yield return new FloatMenuOption("HaulersDream.HaulNearby.Option".Translate() + ": " + disabledReason, null);
        }
    }
}
