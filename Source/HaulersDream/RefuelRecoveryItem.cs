using Verse;

namespace HaulersDream
{
	internal sealed class RefuelRecoveryItem : IExposable
	{
		internal Thing Item;

		internal string LoadId;

		internal int Count;

		internal int FirstTick;

		internal bool Destroyed;

		internal bool Tagged;

		internal bool NotForSale;

		internal bool Caravan;

		internal bool WasUnloadEverything;

		internal bool Matches()
		{
			if (Item != null && Item.GetUniqueLoadID() == LoadId && Item.Destroyed == Destroyed)
			{
				if (!Destroyed)
				{
					return Item.stackCount == Count;
				}
				return true;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_References.Look(ref Item, "item");
			Scribe_Values.Look(ref LoadId, "loadId");
			Scribe_Values.Look(ref Count, "count", 0);
			Scribe_Values.Look(ref FirstTick, "firstTick", 0);
			Scribe_Values.Look(ref Destroyed, "destroyed", defaultValue: false);
			Scribe_Values.Look(ref Tagged, "tagged", defaultValue: false);
			Scribe_Values.Look(ref NotForSale, "notForSale", defaultValue: false);
			Scribe_Values.Look(ref Caravan, "caravan", defaultValue: false);
			Scribe_Values.Look(ref WasUnloadEverything, "unloadEverything", defaultValue: false);
		}
	}
}
