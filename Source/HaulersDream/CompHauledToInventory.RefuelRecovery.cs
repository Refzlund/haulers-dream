using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HaulersDream
{
	public partial class CompHauledToInventory : ThingComp
	{
        private RefuelRecoveryHolder refuelRecoveryCustody;
        internal RefuelRecoveryHolder RefuelRecoveryCustody
            => refuelRecoveryCustody ??= new RefuelRecoveryHolder(this);
        internal bool HasRetainedRefuel => refuelRecoveryCustody?.HasLiveContents == true;
        internal bool OwnsRetainedRefuel(Thing item) => refuelRecoveryCustody?.Owns(item) == true;
        internal float RetainedRefuelStat(StatDef stat) => refuelRecoveryCustody?.TotalStat(stat) ?? 0f;

		private RefuelRecoveryPending refuelRecovery;

		private int refuelRecoverySerial;

		private bool refuelRecoveryBlocked;

		private RefuelRecoveryPending lastRefuelRecovery;

		private int lastRefuelDisposition;

		internal bool HasPendingRefuelRecovery
		{
			get
			{
				if (refuelRecovery == null)
				{
					return refuelRecoveryBlocked;
				}
				return true;
			}
		}

		internal RefuelRecoveryPending PendingRefuelRecovery => refuelRecovery;

		internal int PendingRefuelSerial => refuelRecovery?.Serial ?? refuelRecoverySerial;

		internal int PendingRefuelRevision => refuelRecovery?.Revision ?? 0;

		internal void BlockRefuelRecovery()
		{
			checked
			{
				if (!HasPendingRefuelRecovery)
				{
					refuelRecoverySerial++;
				}
				refuelRecoveryBlocked = true;
			}
		}

		internal void SaveRefuelFailure(RefuelRecoveryPending record)
		{
			if (refuelRecovery != null)
			{
				throw new InvalidOperationException("Cannot replace an unresolved refuel failure.");
			}
			if (!refuelRecoveryBlocked)
			{
				BlockRefuelRecovery();
			}
			record.Serial = refuelRecoverySerial;
			refuelRecovery = record;
		}

		private void ExposeRefuelRecovery()
		{
            Scribe_Deep.Look(ref refuelRecoveryCustody, "hdRefuelRecoveryCustody", this);
			Scribe_Deep.Look(ref refuelRecovery, "hdRefuelRecovery");
			Scribe_Values.Look(ref refuelRecoverySerial, "hdRefuelRecoverySerial", 0);
			Scribe_Values.Look(ref refuelRecoveryBlocked, "hdRefuelRecoveryBlocked", defaultValue: false);
			Scribe_Deep.Look(ref lastRefuelRecovery, "hdLastRefuelRecovery");
			Scribe_Values.Look(ref lastRefuelDisposition, "hdLastRefuelDisposition", 0);
		}

		public override string CompInspectStringExtra()
		{
			string text = base.CompInspectStringExtra();
			if (!HasPendingRefuelRecovery)
			{
				if (lastRefuelDisposition == 0)
				{
					return text;
				}
				string text2 = RefuelRecoveryCommand.OutcomeKey(lastRefuelDisposition).Translate();
				if (!string.IsNullOrEmpty(text))
				{
					return text + "\n" + text2;
				}
				return text2;
			}
			string text3 = refuelRecovery?.Describe() ?? ((string)"HD_RefuelRecoveryRecordMissing".Translate());
			if (!string.IsNullOrEmpty(text))
			{
				return text + "\n" + text3;
			}
			return text3;
		}

		internal void CopyRefuelTag(Thing piece, bool tagged, int firstTick)
		{
			lastHealTick = -1;
			if (tagged)
			{
				takenToInventory.Add(piece);
				if (taggedTick == null)
				{
					taggedTick = new Dictionary<Thing, int>();
				}
				taggedTick[piece] = firstTick;
			}
			else
			{
				takenToInventory.Remove(piece);
				taggedTick?.Remove(piece);
			}
		}

		internal void SettleRefuelTag(Thing item)
		{
			ThingOwner<Thing> thingOwner = (parent as Pawn)?.inventory?.innerContainer;
			if (item != null && (!RefuelAttempt.IsMember(thingOwner, item) || item.Destroyed))
			{
				takenToInventory.Remove(item);
				taggedTick?.Remove(item);
			}
			lastHealTick = -1;
			if (thingOwner != null)
			{
				PruneEmptyKeptCounts(thingOwner);
			}
		}

		internal void ReconcileRecoveryItem(Thing item)
		{
			if (item != null)
			{
				Pawn_InventoryTracker pawn_InventoryTracker = (parent as Pawn)?.inventory;
				if (item.Destroyed || !RefuelAttempt.IsMember(pawn_InventoryTracker?.innerContainer, item))
				{
					takenToInventory.Remove(item);
					taggedTick?.Remove(item);
					pawn_InventoryTracker?.itemsNotForSale.Remove(item);
					pawn_InventoryTracker?.unpackedCaravanItems.Remove(item);
				}
				lastHealTick = -1;
			}
		}

		internal bool CloseRefuelRecovery(int serial, int revision, int disposition)
		{
			if (!HasPendingRefuelRecovery || HasRetainedRefuel || PendingRefuelSerial != serial || PendingRefuelRevision != revision)
			{
				return false;
			}
			lastRefuelRecovery = refuelRecovery;
			lastRefuelDisposition = disposition;
			refuelRecovery = null;
			refuelRecoveryBlocked = false;
			return true;
		}
	}
}
