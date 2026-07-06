using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
    /// <summary>
    /// Runtime side of the quest-pawn revert drop (issue #123): maps live Verse state onto the pure
    /// <see cref="QuestPawnDropPolicy"/> and performs the drops. Driven by <see cref="Patch_Pawn_SetFaction"/>
    /// (both directions of the faction transition) and <see cref="Patch_RecruitUtility_Recruit"/> (permanent
    /// recruitment discards the snapshot). All of it runs inside the synced simulation (quest parts /
    /// job drivers call SetFaction), so MP determinism only needs the thingIDNumber-ordered iteration below
    /// (no Rand, no render-path state).
    /// </summary>
    internal static class QuestPawnReversion
    {
        /// <summary>
        /// The single faction-transition hook. GAIN (someone else's pawn joins the player): if it is a
        /// temporary quest pawn (<c>IsQuestLodger()</c>, true for lodgers AND helpers, both carry a
        /// QuestPart_ExtraFaction), snapshot what it arrived with. LOSS (a player pawn leaves the faction):
        /// if a snapshot exists, consume it and drop everything above it at the pawn's feet.
        ///
        /// The LOSS side deliberately keys on "a snapshot exists" instead of re-querying IsQuestLodger():
        /// the main revert path (QuestPart_Leave.Cleanup -> LeaveQuestPartUtility.MakePawnLeave) runs after
        /// the quest left the Ongoing state, where GetExtraFactionsFromQuestParts already reports the pawn as
        /// NOT a lodger (decompile-verified state filter), a re-query would miss the exact case the feature
        /// exists for.
        /// </summary>
        /// <param name="pawn">The pawn whose faction changed.</param>
        /// <param name="oldFaction">The faction before the change (captured by the patch prefix).</param>
        /// <param name="newFaction">The faction after the change.</param>
        internal static void OnFactionChanged(Pawn pawn, Faction oldFaction, Faction newFaction)
        {
            // Mirror vanilla: SetFaction warns and early-returns on a same-faction call, but the Harmony pair
            // still runs, treat it as the no-op it was.
            if (pawn == null || oldFaction == newFaction || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
                return;

            var player = Faction.OfPlayerSilentFail;
            if (player == null)
                return;
            var component = HaulersDreamGameComponent.Instance;
            if (component == null)
                return;

            if (newFaction == player && oldFaction != player)
            {
                // GAIN. IsQuestLodger() is reliable HERE (unlike at revert): the QuestPart_ExtraFaction is
                // created at quest generation, before any join signal fires, and the quest is Ongoing, vanilla
                // itself gates ChangeKind on !IsQuestLodger() inside this very SetFaction call. The pawn is
                // often still UNSPAWNED at this moment (QuestPart_PawnsArrive flips the faction before
                // spawning), which is fine: its inventory tracker is fully readable.
                if (pawn.IsQuestLodger())
                    RecordGainSnapshot(pawn, component);
                else
                    // A PERMANENT join (prisoner recruit, rescue, wanderer). No snapshot should exist, but if a
                    // stale one somehow does, a permanent colonist must never carry a pending revert diff.
                    component.ClearQuestPawnSnapshot(pawn);
                return;
            }

            if (oldFaction == player && newFaction != player)
            {
                if (!component.TryGetQuestPawnSnapshot(pawn, out var snapshotItems))
                    return;
                // One-shot: consume the snapshot on ANY loss-of-control, even when the drop below is skipped.
                // A stale snapshot must never fire on some later, unrelated transition.
                component.ClearQuestPawnSnapshot(pawn);

                if (HaulersDreamMod.Settings?.enableQuestPawnDrop != true)
                    return;
                // Dead pawns: clear-only, never drop. For a SPAWNED death, vanilla Kill already ran
                // DropAndForbidEverything (its inventory drop is gated on the pawn being spawned) before
                // QuestPart_ExtraFaction.Notify_PawnKilled reverts the faction, so the goods are already on
                // the ground and dropping again would fight vanilla. For an UNSPAWNED death (e.g. dying in a
                // caravan) that vanilla drop never runs and the corpse keeps the inventory, but there is no
                // ground to drop onto here either, so leaving the corpse's inventory alone is exactly the
                // vanilla outcome in both cases.
                if (pawn.Dead)
                    return;
                // No held map: the pawn is in a caravan (vanilla MakePawnLeave already moved its ENTIRE
                // inventory to the other caravan members and removed it from the caravan BEFORE flipping the
                // faction, so the goods already stayed with the player), in a travelling transporter, or a pure
                // world pawn, there is no ground to drop onto. Skip; never error like DropAllNearPawn would.
                var map = pawn.MapHeld;
                if (map == null)
                    return;

                DropExcessAtRevert(pawn, snapshotItems, map);
            }
        }

        /// <summary>A pawn was permanently recruited into the player faction. A lodger accepting its join
        /// offer runs QuestPart_JoinPlayer -> RecruitUtility.Recruit while ALREADY player faction, so no
        /// SetFaction fires and the transition seam never sees it, without this, the new colonist's stale
        /// arrival snapshot would linger and a far-future faction departure would dump a bogus diff.</summary>
        internal static void NotifyRecruited(Pawn pawn)
        {
            if (pawn == null)
                return;
            HaulersDreamGameComponent.Instance?.ClearQuestPawnSnapshot(pawn);
        }

        /// <summary>Snapshot what the joining pawn arrived with, as aggregated (def, stuff) -> count lines.
        /// A missing inventory tracker records an EMPTY snapshot on purpose: "arrived with nothing" is the
        /// safe-direction fallback (everything later found on the pawn is colony property), whereas skipping
        /// would silently disable the revert drop for this pawn.</summary>
        private static void RecordGainSnapshot(Pawn pawn, HaulersDreamGameComponent component)
        {
            var stacks = new List<QuestPawnDropPolicy.InventoryStack>();
            var owner = pawn.inventory?.innerContainer;
            if (owner != null)
            {
                // The healed set (decision path). A fresh guest has no tags; a RE-joining one whose previous
                // revert drop failed can still carry HD tags, excluded from the snapshot by the policy so HD
                // cargo never counts as arrival kit.
                var tagged = pawn.TryGetComp<CompHauledToInventory>()?.GetHashSet();
                CollectStacks(owner, tagged, stacks, null);
                // MP determinism: aggregation output order is first-seen input order, fix the input order.
                stacks.Sort((a, b) => a.Id.CompareTo(b.Id));
            }

            var entries = new List<QuestPawnDropPolicy.SnapshotEntry>();
            QuestPawnDropPolicy.BuildSnapshot(stacks, entries);

            var items = new List<QuestPawnSnapshotItem>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
                items.Add(new QuestPawnSnapshotItem((ThingDef)entries[i].Def, (ThingDef)entries[i].Stuff, entries[i].Count));
            component.RecordQuestPawnSnapshot(pawn, items);
        }

        /// <summary>Drop everything above the arrival snapshot at the pawn's held position. Equipped weapons
        /// and worn apparel are untouched by construction (they live in the equipment/apparel trackers, not in
        /// <c>inventory.innerContainer</c>).</summary>
        private static void DropExcessAtRevert(Pawn pawn, List<QuestPawnSnapshotItem> snapshotItems, Map map)
        {
            var owner = pawn.inventory?.innerContainer;
            if (owner == null || owner.Count == 0)
                return;

            var comp = pawn.TryGetComp<CompHauledToInventory>();
            // GetHashSet (the healed set) on this decision path: the heal re-tags stacks that merged/split
            // while the guest worked, so HD cargo can't hide from the "tagged always drops" rule.
            var tagged = comp?.GetHashSet();

            var stacks = new List<QuestPawnDropPolicy.InventoryStack>(owner.Count);
            var byId = new Dictionary<int, Thing>(owner.Count);
            CollectStacks(owner, tagged, stacks, byId);
            if (stacks.Count == 0)
                return;
            // MP determinism: allowance is consumed in stack order, thingIDNumber order makes every client
            // keep/drop the same units (HD convention).
            stacks.Sort((a, b) => a.Id.CompareTo(b.Id));

            var snapshot = new List<QuestPawnDropPolicy.SnapshotEntry>(snapshotItems.Count);
            for (int i = 0; i < snapshotItems.Count; i++)
            {
                var line = snapshotItems[i];
                if (line?.def == null || line.count <= 0)
                    continue;
                snapshot.Add(new QuestPawnDropPolicy.SnapshotEntry(line.def, line.stuff, line.count));
            }

            var orders = new List<QuestPawnDropPolicy.DropOrder>();
            QuestPawnDropPolicy.SelectDrops(snapshot, stacks, orders);

            // PositionHeld == Position for the normal spawned revert (MakePawnLeave fires BEFORE the exit-map
            // lord forms, so the pawn still stands inside the colony); for a pawn held in an on-map transporter
            // it is the holder's cell (vanilla DropAndForbidEverything precedent).
            var pos = pawn.PositionHeld;
            for (int i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (!byId.TryGetValue(order.Id, out var thing) || thing == null || thing.Destroyed || !owner.Contains(thing))
                    continue;
                int count = order.Count < thing.stackCount ? order.Count : thing.stackCount;
                if (count <= 0)
                    continue;

                // TryDrop reassigns the out param to the (possibly merged) ground stack; hold the ORIGINAL
                // reference for the untag below. On failure (saturated / boxed-in area) keep trying the
                // remaining orders: this is a ONE-SHOT revert with no later retry cycle, so one unplaceable
                // stack must not strand the rest. No try/catch, a genuine drop fault must surface red.
                if (!owner.TryDrop(thing, pos, map, ThingPlaceMode.Near, count, out Thing dropped))
                    continue;

                // Colonists should haul the pile back to storage: clear any forbidden flag the item kept from
                // before it was picked up (the TryDrop -> GenDrop -> GenPlace chain itself sets none, verified).
                dropped?.SetForbidden(false, false);
                // A stack that fully left the inventory releases its HD tag (no-op for untagged things). A
                // partial drop only happens on untagged stacks, tagged ones always drop in full.
                if (!owner.Contains(thing))
                    comp?.Deregister(thing);
            }
        }

        /// <summary>Flatten live inventory into policy stacks (and optionally an id -> Thing map for executing
        /// drop orders). Skips destroyed/empty stacks and QUEST-TAGGED things: an item carrying questTags is
        /// quest-managed state (a delivery target etc.) that HD never relocates (CorpseStripper precedent);
        /// skipped at BOTH snapshot and revert time, so it neither grants allowance nor gets dropped.</summary>
        private static void CollectStacks(
            ThingOwner<Thing> owner,
            HashSet<Thing> tagged,
            List<QuestPawnDropPolicy.InventoryStack> outStacks,
            Dictionary<int, Thing> outById)
        {
            for (int i = 0; i < owner.Count; i++)
            {
                var thing = owner[i];
                if (thing == null || thing.Destroyed || thing.stackCount <= 0)
                    continue;
                if (!thing.questTags.NullOrEmpty())
                    continue;

                bool isTagged = tagged != null && tagged.Contains(thing);
                outStacks.Add(new QuestPawnDropPolicy.InventoryStack(
                    thing.thingIDNumber, thing.def, thing.Stuff, thing.stackCount, isTagged));
                if (outById != null)
                    outById[thing.thingIDNumber] = thing;
            }
        }
    }
}
