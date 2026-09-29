using Verse;

namespace HaulersDream
{
	internal sealed class RefuelProvenance
	{
		internal readonly bool Tagged;

		internal readonly bool NotForSale;

		internal readonly bool Caravan;

		internal readonly bool WasUnloadEverything;

		internal readonly int FirstTick;

		private readonly Pawn_InventoryTracker tracker;

		private readonly CompHauledToInventory tracking;

		private readonly RefuelActivation activation;

		internal RefuelProvenance(RefuelActivation activation, CompHauledToInventory tracking, Thing source)
		{
			this.activation = activation;
			Pawn pawn = activation.Pawn;
			tracker = pawn.inventory;
			this.tracking = tracking;
			Tagged = tracking.PeekHashSet().Contains(source);
			FirstTick = tracking.FirstTaggedTick(source);
			NotForSale = tracker.itemsNotForSale.Contains(source);
			Caravan = tracker.unpackedCaravanItems.Contains(source);
			WasUnloadEverything = tracker.unloadEverything;
		}

		internal void ApplyToNewOwnedPiece(Thing piece, bool restoreUnload = false)
		{
			if (RefuelAttempt.IsMember(tracker.innerContainer, piece) && !piece.Destroyed)
			{
				tracking.CopyRefuelTag(piece, Tagged, FirstTick);
				if (NotForSale && !tracker.itemsNotForSale.Contains(piece))
				{
					tracker.itemsNotForSale.Add(piece);
				}
				if (Caravan && !tracker.unpackedCaravanItems.Contains(piece))
				{
					tracker.unpackedCaravanItems.Add(piece);
				}
				if (restoreUnload)
					BulkUnloadGate.RestoreRefuelUnloadIntent(activation, tracker, WasUnloadEverything);
			}
		}

		internal void Reconcile(Thing thing)
		{
			if (thing != null)
			{
				if (thing.Destroyed || !RefuelAttempt.IsMember(tracker.innerContainer, thing))
				{
					tracker.itemsNotForSale.Remove(thing);
					tracker.unpackedCaravanItems.Remove(thing);
				}
				tracking.SettleRefuelTag(thing);
			}
		}
	}
}
