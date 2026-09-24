using Verse;

namespace HaulersDream
{
    public partial class CompHauledToInventory
    {
        internal readonly struct ExplicitDropTag
        {
            internal readonly bool tagged;
            internal readonly int? age;
            internal ExplicitDropTag(bool tagged, int? age) { this.tagged = tagged; this.age = age; }
        }

        // Observe without running self-heal or renewing the source's tag/CE hold.
        internal ExplicitDropTag CaptureExplicitDropTag(Thing source) => new ExplicitDropTag(
            takenToInventory.Contains(source),
            taggedTick != null && taggedTick.TryGetValue(source, out int tick) ? tick : (int?)null);

        internal void RestoreExplicitDropFragment(Thing piece, ExplicitDropTag original)
        {
            var owner = (parent as Pawn)?.inventory?.innerContainer;
            if (!original.tagged || piece == null || piece.Destroyed || piece.Spawned
                || owner == null || !ReferenceEquals(piece.holdingOwner, owner) || !owner.Contains(piece))
                return;
            takenToInventory.Add(piece);
            if (original.age.HasValue)
            {
                if (taggedTick == null) taggedTick = new System.Collections.Generic.Dictionary<Thing, int>();
                taggedTick[piece] = original.age.Value;
            }
            lastHealTick = -1;
        }
    }
}
