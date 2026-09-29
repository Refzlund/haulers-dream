using System;
using System.Collections.Generic;
using Verse;

namespace HaulersDream
{
	internal sealed class RefuelRecoveryPending : IExposable
	{
		internal int Serial;

		internal int JobLoadId;

		internal int SelectedUnits;

		internal int Version;

		internal int Revision;

		internal int ResolutionDestroyAttempts;

		internal Thing CostItem;

		internal string CostItemId;

		internal string ResolutionError;

		internal string ResolutionSecondaryError;

		internal bool KnownUnpaidCost;

		internal bool CostPaidBeforeResolution;

		internal bool PaidByResolution;

		internal bool ResolutionMetadataSettled;

		internal bool CreditObserved;

		internal bool CreditAmbiguous;

		internal bool DestroyAttempted;

		internal bool CustodyVerified;

		internal Thing Target;

		internal string TargetId;

		internal string FirstError;

		internal string SecondaryError;

		internal List<RefuelRecoveryItem> Items = new List<RefuelRecoveryItem>();

		public void ExposeData()
		{
			Scribe_Values.Look(ref Version, "version", 0);
			Scribe_Values.Look(ref Revision, "revision", 0);
			Scribe_Values.Look(ref ResolutionDestroyAttempts, "resolutionDestroyAttempts", 0);
			Scribe_References.Look(ref CostItem, "costItem");
			Scribe_Values.Look(ref CostItemId, "costItemId");
			Scribe_Values.Look(ref KnownUnpaidCost, "knownUnpaidCost", defaultValue: false);
			Scribe_Values.Look(ref CostPaidBeforeResolution, "costPaidBeforeResolution", defaultValue: false);
			Scribe_Values.Look(ref PaidByResolution, "paidByResolution", defaultValue: false);
			Scribe_Values.Look(ref ResolutionError, "resolutionError");
			Scribe_Values.Look(ref ResolutionSecondaryError, "resolutionSecondaryError");
			Scribe_Values.Look(ref ResolutionMetadataSettled, "resolutionMetadataSettled", defaultValue: false);
			Scribe_Values.Look(ref Serial, "serial", 0);
			Scribe_Values.Look(ref JobLoadId, "jobLoadId", 0);
			Scribe_Values.Look(ref SelectedUnits, "selectedUnits", 0);
			Scribe_Values.Look(ref CreditObserved, "creditObserved", defaultValue: false);
			Scribe_Values.Look(ref CreditAmbiguous, "creditAmbiguous", defaultValue: false);
			Scribe_Values.Look(ref DestroyAttempted, "destroyAttempted", defaultValue: false);
			Scribe_Values.Look(ref CustodyVerified, "custodyVerified", defaultValue: false);
			Scribe_References.Look(ref Target, "target");
			Scribe_Values.Look(ref TargetId, "targetId");
			Scribe_Values.Look(ref FirstError, "firstError");
			Scribe_Values.Look(ref SecondaryError, "secondaryError");
			Scribe_Collections.Look(ref Items, "items", LookMode.Deep);
			if (Items == null)
			{
				Items = new List<RefuelRecoveryItem>();
			}
		}

		internal string Describe()
		{
			if (PaidByResolution || CostPaidBeforeResolution)
			{
				return "HD_RefuelRecoveryCostPaid".Translate();
			}
			bool flag = false;
			for (int i = 0; i < Items.Count; i++)
			{
				if (Items[i] == null || !Items[i].Matches())
				{
					flag = true;
				}
			}
			return string.Concat("HD_RefuelRecoveryPaused".Translate(), flag ? (" " + "HD_RefuelRecoveryChanged".Translate()) : "", (!CustodyVerified) ? (" " + "HD_RefuelRecoveryCustodyUnknown".Translate()) : "");
		}

		internal static string ErrorText(Exception error)
		{
			if (error == null)
			{
				return null;
			}
			string text = error.GetType().Name + ": " + error.Message;
			if (text.Length > 1200)
			{
				return text.Substring(0, 1200);
			}
			return text;
		}
	}
}
