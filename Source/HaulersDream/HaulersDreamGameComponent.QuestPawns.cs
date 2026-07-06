using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
    public partial class HaulersDreamGameComponent
    {
        // --- Quest-pawn arrival-inventory snapshots (issue #123) ---
        // What each temporary quest pawn (lodger / helper borrowed from another faction) was carrying at the
        // moment it JOINED the player faction, keyed by the pawn. Consumed one-shot when the pawn leaves the
        // player faction: everything above the snapshot drops at its feet (QuestPawnReversion). Keyed by Pawn
        // (pawns never merge, unlike item stacks, so the reference is a stable key); the VALUES are def+stuff
        // counts, never Thing refs, inventory stacks merge/split freely while the pawn works, which is the
        // recurring HD trap this shape avoids.
        //
        // Recorded UNCONDITIONALLY (not gated on the enableQuestPawnDrop toggle) so turning the setting on
        // mid-quest still covers guests already present; the toggle gates only the drop. Bounded: one entry per
        // live temporary quest pawn, removed at revert / recruitment / (for stragglers) the PostLoadInit prune.
        private Dictionary<Pawn, List<QuestPawnSnapshotItem>> questPawnSnapshots =
            new Dictionary<Pawn, List<QuestPawnSnapshotItem>>();

        // Scribe staging for questPawnSnapshots (a Dictionary<Pawn, List<...>> can't be scribed directly);
        // same DTO round-trip as LoadLedgerEntry.pawnClaims.
        private List<QuestPawnSnapshotData> questPawnSnapshotScribe;

        /// <summary>Record (or overwrite) the arrival snapshot for a pawn that just joined the player faction.
        /// Overwriting is the re-join semantics: a pawn gaining control a second time gets a FRESH snapshot,
        /// never a stacked one.</summary>
        /// <param name="pawn">The temporary quest pawn; ignored when null.</param>
        /// <param name="items">Aggregated (def, stuff, count) lines; an empty list is meaningful ("arrived
        /// with nothing": everything it later holds is colony property).</param>
        internal void RecordQuestPawnSnapshot(Pawn pawn, List<QuestPawnSnapshotItem> items)
        {
            if (pawn == null || items == null)
                return;
            questPawnSnapshots[pawn] = items;
        }

        /// <summary>The arrival snapshot recorded for <paramref name="pawn"/>, if any. Read-only lookup;
        /// callers consume via <see cref="ClearQuestPawnSnapshot"/>.</summary>
        internal bool TryGetQuestPawnSnapshot(Pawn pawn, out List<QuestPawnSnapshotItem> items)
        {
            items = null;
            return pawn != null && questPawnSnapshots.TryGetValue(pawn, out items);
        }

        /// <summary>Remove the pawn's snapshot (consumed at revert, or discarded on permanent recruitment).</summary>
        internal void ClearQuestPawnSnapshot(Pawn pawn)
        {
            if (pawn != null)
                questPawnSnapshots.Remove(pawn);
        }

        // The quest-pawn snapshot scribing (additive to base.ExposeData via ExposeData() -> ExposeQuestPawns()).
        // Separate, independently-scribed label so old saves without it load fine, and a mid-quest save/load
        // keeps the snapshot alive (the whole point: the revert can fire many days after the join).
        private void ExposeQuestPawns()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                questPawnSnapshotScribe = new List<QuestPawnSnapshotData>();
                foreach (var kv in questPawnSnapshots)
                    if (kv.Key != null && !kv.Key.Destroyed && !kv.Key.Dead && kv.Value != null)
                        questPawnSnapshotScribe.Add(new QuestPawnSnapshotData(kv.Key, kv.Value));
            }
            Scribe_Collections.Look(ref questPawnSnapshotScribe, "haulersDreamQuestPawnSnapshots", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Rebuild the dictionary from the DTOs, pruning entries whose pawn is gone (null after a world-
                // pawn GC / destroyed) or dead (vanilla Kill already dropped a dead pawn's inventory next to the
                // corpse, the snapshot's job is moot) and items whose def no longer resolves (mod removed).
                questPawnSnapshots = new Dictionary<Pawn, List<QuestPawnSnapshotItem>>();
                if (questPawnSnapshotScribe != null)
                {
                    foreach (var dto in questPawnSnapshotScribe)
                    {
                        if (dto?.pawn == null || dto.pawn.Destroyed || dto.pawn.Dead || dto.items == null)
                            continue;
                        dto.items.RemoveAll(it => it == null || it.def == null || it.count <= 0);
                        questPawnSnapshots[dto.pawn] = dto.items;
                    }
                }
                questPawnSnapshotScribe = null;
            }
        }
    }

    /// <summary>Serialization DTO pairing one quest pawn with its arrival-snapshot lines (a
    /// <c>Dictionary&lt;Pawn, List&lt;...&gt;&gt;</c> can't be scribed directly, same round-trip as
    /// <c>LoadLedgerEntry.PawnClaimData</c>). <c>pawn</c> is a reference; the items are Deep.</summary>
    public class QuestPawnSnapshotData : IExposable
    {
        public Pawn pawn;
        public List<QuestPawnSnapshotItem> items = new List<QuestPawnSnapshotItem>();

        public QuestPawnSnapshotData() { }

        public QuestPawnSnapshotData(Pawn pawn, List<QuestPawnSnapshotItem> items)
        {
            this.pawn = pawn;
            this.items = items;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars && items == null)
                items = new List<QuestPawnSnapshotItem>();
        }
    }

    /// <summary>One arrival-snapshot line: the pawn arrived holding <see cref="count"/> of
    /// (<see cref="def"/>, <see cref="stuff"/>). Def-based on purpose, NEVER a Thing reference or a bare
    /// thingIDNumber, which stack merges/splits would invalidate (the recurring HD ownership trap).
    /// <see cref="stuff"/> is null for unstuffed items; <see cref="Scribe_Defs"/> round-trips null cleanly
    /// (writes the literal "null").</summary>
    public class QuestPawnSnapshotItem : IExposable
    {
        public ThingDef def;
        public ThingDef stuff;
        public int count;

        public QuestPawnSnapshotItem() { }

        public QuestPawnSnapshotItem(ThingDef def, ThingDef stuff, int count)
        {
            this.def = def;
            this.stuff = stuff;
            this.count = count;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref count, "count", 0);
        }
    }
}
