using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace HaulersDream
{
	[StaticConstructorOnStartup]
	internal static class RefuelRecoveryCommand
	{
		private const int Restore = 1;

		private const int PayCost = 2;

		private const int AcceptCurrent = 3;

		private const int Cleanup = 4;

		private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Buttons/Drop", reportFailure: false) ?? BaseContent.BadTex;

		internal static string OutcomeKey(int disposition)
		{
			switch (disposition)
			{
			default:
				return "HD_RefuelRecoveryUncertainOutcome";
			case 2:
				return "HD_RefuelRecoveryUncreditedOutcome";
			case 1:
				return "HD_RefuelRecoveryPaidOutcome";
			}
		}

		private static bool CanControl(Pawn pawn)
		{
			if (pawn == null || pawn.Faction != Faction.OfPlayer || pawn.MapHeld == null)
			{
				return false;
			}
			if (!pawn.Dead)
			{
				if (pawn.Spawned)
				{
					return !pawn.InMentalState;
				}
				return false;
			}
			if (pawn.ParentHolder is Corpse corpse && corpse.Spawned)
			{
				return corpse.InnerPawn == pawn;
			}
			return false;
		}

		internal static Command_Action Gizmo(Pawn pawn)
		{
			if (CanControl(pawn))
			{
				CompHauledToInventory comp = pawn.GetComp<CompHauledToInventory>();
				if (comp != null && comp.HasPendingRefuelRecovery)
				{
					return new Command_Action
					{
						defaultLabel = "HD_RefuelRecoveryReview".Translate(),
						defaultDesc = "HD_RefuelRecoveryReviewDesc".Translate(),
						icon = Icon,
						Order = float.MaxValue,
						action = delegate
						{
							Open(pawn);
						}
					};
				}
			}
			return null;
		}

		private static void Open(Pawn pawn)
		{
			if (!MultiplayerCompat.RefuelRecoveryLocalUi || !CanControl(pawn))
			{
				return;
			}
			CompHauledToInventory comp = pawn.GetComp<CompHauledToInventory>();
			if (comp != null && comp.HasPendingRefuelRecovery)
			{
				RefuelRecoveryPending pendingRefuelRecovery = comp.PendingRefuelRecovery;
				string details = Details(pawn, pendingRefuelRecovery);
				List<FloatMenuOption> options = new List<FloatMenuOption>
				{
					new FloatMenuOption("HD_RefuelRecoveryDetails".Translate(), delegate
					{
						Find.WindowStack.Add(new Dialog_MessageBox(details));
					})
				};
				AddOption(options, pawn, comp, "HD_RefuelRecoveryPay", 2, CanPay(pawn, pendingRefuelRecovery));
				AddOption(options, pawn, comp, "HD_RefuelRecoveryRestore", 1, CanRestore(pawn, pendingRefuelRecovery));
				AddOption(options, pawn, comp, "HD_RefuelRecoveryCleanup", 4, KnownConsumed(pendingRefuelRecovery));
				AddOption(options, pawn, comp, "HD_RefuelRecoveryAccept", 3, eligible: true);
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		private static void AddOption(List<FloatMenuOption> options, Pawn pawn, CompHauledToInventory comp, string label, int decision, bool eligible)
		{
			if (!eligible)
			{
				return;
			}
			if (!MultiplayerCompat.RefuelRecoveryAvailable)
			{
				options.Add(new FloatMenuOption(label.Translate() + " (" + "HD_RefuelRecoverySyncUnavailable".Translate() + ")", null));
				return;
			}
			int serial = comp.PendingRefuelSerial;
			int revision = comp.PendingRefuelRevision;
			string opening = Snapshot(pawn, comp.PendingRefuelRecovery);
			options.Add(new FloatMenuOption(label.Translate(), delegate
			{
				if (!comp.HasPendingRefuelRecovery || comp.PendingRefuelSerial != serial || comp.PendingRefuelRevision != revision || !string.Equals(opening, Snapshot(pawn, comp.PendingRefuelRecovery), StringComparison.Ordinal))
				{
					Feedback("HD_RefuelRecoverySelectionChanged");
				}
				else
				{
					string key = ((decision == 2) ? "HD_RefuelRecoveryPayConfirm" : ((decision == 1) ? "HD_RefuelRecoveryRestoreConfirm" : ((decision == 4) ? "HD_RefuelRecoveryCleanupConfirm" : "HD_RefuelRecoveryAcceptConfirm")));
					string text = Details(pawn, comp.PendingRefuelRecovery) + "\n\n" + key.Translate();
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
					{
						Dispatch(pawn, serial, revision, decision, opening);
					}, decision == 2));
				}
			}));
		}

		private static void Dispatch(Pawn pawn, int serial, int revision, int decision, string opening)
		{
			if (MultiplayerCompat.RefuelRecoveryLocalUi)
			{
				CompHauledToInventory compHauledToInventory = pawn?.GetComp<CompHauledToInventory>();
				if (!MultiplayerCompat.RefuelRecoveryAvailable || !CanControl(pawn) || pawn.MapHeld != Find.CurrentMap || compHauledToInventory == null || !compHauledToInventory.HasPendingRefuelRecovery || compHauledToInventory.PendingRefuelSerial != serial || compHauledToInventory.PendingRefuelRevision != revision || !string.Equals(opening, Snapshot(pawn, compHauledToInventory.PendingRefuelRecovery), StringComparison.Ordinal))
				{
					Messages.Message("HD_RefuelRecoverySelectionChanged".Translate(), MessageTypeDefOf.RejectInput, historical: false);
				}
				else
				{
					ResolveSynced(pawn, serial, revision, decision, opening);
				}
			}
		}

		public static void ResolveSynced(Pawn pawn, int serial, int revision, int decision, string opening)
		{
			if (!MultiplayerCompat.RefuelRecoveryExecuting || !MultiplayerCompat.RefuelRecoveryAvailable || !CanControl(pawn))
			{
				return;
			}
			CompHauledToInventory comp = pawn.GetComp<CompHauledToInventory>();
			if (comp == null || !comp.HasPendingRefuelRecovery || comp.PendingRefuelSerial != serial || comp.PendingRefuelRevision != revision || decision < 1 || decision > 4 || !string.Equals(opening, Snapshot(pawn, comp.PendingRefuelRecovery), StringComparison.Ordinal))
			{
				Feedback("HD_RefuelRecoverySelectionChanged");
				return;
			}
			RefuelRecoveryPending pendingRefuelRecovery = comp.PendingRefuelRecovery;
			if (decision == 3)
			{
				int disposition = ((KnownConsumed(pendingRefuelRecovery) && pendingRefuelRecovery.ResolutionMetadataSettled && AllKnownCustody(pawn, pendingRefuelRecovery)) ? 1 : ((DefinitelyUncredited(pendingRefuelRecovery) && AllKnownCustody(pawn, pendingRefuelRecovery)) ? 2 : 3));
				if (comp.CloseRefuelRecovery(serial, revision, disposition))
				{
					Feedback(OutcomeKey(disposition));
				}
				return;
			}
			if (pendingRefuelRecovery == null || (decision == 2 && !CanPay(pawn, pendingRefuelRecovery)) || (decision == 1 && !CanRestore(pawn, pendingRefuelRecovery)) || (decision == 4 && (!KnownConsumed(pendingRefuelRecovery) || pendingRefuelRecovery.Items == null || pendingRefuelRecovery.Items.Count > 2)))
			{
				Feedback("HD_RefuelRecoverySelectionChanged");
				return;
			}
			Exception ex;
			bool resolutionMetadataSettled;
			checked
			{
				pendingRefuelRecovery.Revision++;
				ex = null;
				switch (decision)
				{
				case 2:
				{
					Thing costItem = pendingRefuelRecovery.CostItem;
					pendingRefuelRecovery.ResolutionDestroyAttempts++;
					try
					{
						costItem.Destroy();
					}
					catch (Exception ex4)
					{
						ex = ex4;
					}
					pendingRefuelRecovery.PaidByResolution = costItem.Destroyed;
					if (!pendingRefuelRecovery.PaidByResolution && ex == null)
					{
						ex = new InvalidOperationException("The confirmed fuel-cost destruction left its item alive.");
					}
					break;
				}
				case 1:
				{
					for (int i = 0; i < pendingRefuelRecovery.Items.Count; i = unchecked(i + 1))
					{
						RefuelRecoveryItem refuelRecoveryItem = pendingRefuelRecovery.Items[i];
						if (!refuelRecoveryItem.Matches())
						{
							ex = ex ?? new InvalidOperationException("A recovery item changed during restoration.");
							break;
						}
						if (OwnerToken(pawn, refuelRecoveryItem.Item) != "unowned")
						{
							continue;
						}
						try
						{
							pawn.inventory.innerContainer.TryAdd(refuelRecoveryItem.Item, canMergeWithExistingStacks: false);
						}
						catch (Exception ex2)
						{
							if (ex == null)
							{
								ex = ex2;
							}
							else
							{
								Secondary(pendingRefuelRecovery, ex2);
							}
						}
						if (RefuelAttempt.IsMember(pawn.inventory.innerContainer, refuelRecoveryItem.Item))
						{
							try
							{
								if (refuelRecoveryItem.Tagged && !comp.PeekHashSet().Contains(refuelRecoveryItem.Item))
								{
									comp.CopyRefuelTag(refuelRecoveryItem.Item, tagged: true, refuelRecoveryItem.FirstTick);
								}
								if (refuelRecoveryItem.NotForSale && !pawn.inventory.itemsNotForSale.Contains(refuelRecoveryItem.Item))
								{
									pawn.inventory.itemsNotForSale.Add(refuelRecoveryItem.Item);
								}
								if (refuelRecoveryItem.Caravan && !pawn.inventory.unpackedCaravanItems.Contains(refuelRecoveryItem.Item))
								{
									pawn.inventory.unpackedCaravanItems.Add(refuelRecoveryItem.Item);
								}
							}
							catch (Exception ex3)
							{
								if (ex == null)
								{
									ex = ex3;
								}
								else
								{
									Secondary(pendingRefuelRecovery, ex3);
								}
							}
						}
						else if (ex == null)
						{
							ex = new InvalidOperationException("Native inventory did not accept the recovery item.");
						}
					}
					break;
				}
				}
				resolutionMetadataSettled = true;
			}
			try
			{
				for (int j = 0; j < pendingRefuelRecovery.Items.Count; j++)
				{
					Thing thing = pendingRefuelRecovery.Items[j]?.Item;
					if (thing != null)
					{
						if (thing.Destroyed && (thing.holdingOwner == pawn.inventory?.innerContainer || thing.holdingOwner == pawn.carryTracker?.innerContainer) && RefuelAttempt.IsMember(thing.holdingOwner, thing))
						{
							thing.holdingOwner.Remove(thing);
						}
						comp.ReconcileRecoveryItem(thing);
					}
				}
				PawnMassCache.Clear();
				TrackedMassCache.Clear();
				InventoryShare.Clear();
				OrganicInventoryShare.Clear();
				CarriedHaulShare.Clear();
				SurplusCache.Clear();
				if (!pawn.Dead && !RefuelCeCarryBudget.TryRefreshForDriver(pawn, out var refusal))
				{
					throw new InvalidOperationException(refusal);
				}
			}
			catch (Exception ex5)
			{
				resolutionMetadataSettled = false;
				if (ex == null)
				{
					ex = ex5;
				}
				else
				{
					Secondary(pendingRefuelRecovery, ex5);
				}
			}
			pendingRefuelRecovery.ResolutionMetadataSettled = resolutionMetadataSettled;
			pendingRefuelRecovery.CustodyVerified = AllKnownCustody(pawn, pendingRefuelRecovery);
			if (ex != null)
			{
				pendingRefuelRecovery.ResolutionError = RefuelRecoveryPending.ErrorText(ex);
				ExceptionDispatchInfo.Capture(ex).Throw();
			}
			Feedback(KnownConsumed(pendingRefuelRecovery) ? "HD_RefuelRecoveryCostPaid" : "HD_RefuelRecoveryRestored");
		}

		private static void Secondary(RefuelRecoveryPending record, Exception error)
		{
			if (record.ResolutionSecondaryError == null)
			{
				record.ResolutionSecondaryError = RefuelRecoveryPending.ErrorText(error);
			}
		}

		private static bool DefinitelyUncredited(RefuelRecoveryPending record)
		{
			if (record != null && record.Version >= 2 && !record.CreditObserved)
			{
				return !record.CreditAmbiguous;
			}
			return false;
		}

		private static bool KnownConsumed(RefuelRecoveryPending record)
		{
			if (record != null && record.Version >= 2)
			{
				if (!record.CostPaidBeforeResolution)
				{
					return record.PaidByResolution;
				}
				return true;
			}
			return false;
		}

		private static bool CanPay(Pawn pawn, RefuelRecoveryPending record)
		{
			if (record == null || record.Version < 2 || !record.KnownUnpaidCost || record.PaidByResolution || record.CreditAmbiguous || !record.CreditObserved || record.SelectedUnits <= 0 || record.CostItem == null || record.CostItem.Destroyed || record.Items == null || record.Items.Count > 2 || record.CostItem.GetUniqueLoadID() != record.CostItemId || record.CostItem.stackCount != record.SelectedUnits)
			{
				return false;
			}
			if (!ControllableOwner(OwnerToken(pawn, record.CostItem)))
			{
				return false;
			}
			for (int i = 0; i < record.Items.Count; i++)
			{
				if (record.Items[i]?.Item == record.CostItem && record.Items[i].Matches())
				{
					return true;
				}
			}
			return false;
		}

		private static bool CanRestore(Pawn pawn, RefuelRecoveryPending record)
		{
			if (!DefinitelyUncredited(record) || pawn.inventory?.innerContainer == null || record.Items == null || record.Items.Count > 2)
			{
				return false;
			}
			bool result = false;
			for (int i = 0; i < record.Items.Count; i++)
			{
				RefuelRecoveryItem refuelRecoveryItem = record.Items[i];
				if (refuelRecoveryItem == null || !refuelRecoveryItem.Matches())
				{
					return false;
				}
				if (!refuelRecoveryItem.Destroyed && OwnerToken(pawn, refuelRecoveryItem.Item) == "unowned")
				{
					result = true;
				}
			}
			return result;
		}

		private static bool AllKnownCustody(Pawn pawn, RefuelRecoveryPending record)
		{
			if (record?.Items == null || record.Items.Count > 2)
			{
				return false;
			}
			for (int i = 0; i < record.Items.Count; i++)
			{
				RefuelRecoveryItem refuelRecoveryItem = record.Items[i];
				if (refuelRecoveryItem?.Item == null)
				{
					if (!KnownConsumed(record) || record.CostItemId == null || !(record.CostItemId == refuelRecoveryItem?.LoadId))
					{
						return false;
					}
				}
				else if (refuelRecoveryItem.Item.Destroyed)
				{
					if (!KnownConsumed(record) || !(refuelRecoveryItem.LoadId == record.CostItemId))
					{
						return false;
					}
				}
				else if (!refuelRecoveryItem.Matches() || !ControllableOwner(OwnerToken(pawn, refuelRecoveryItem.Item)) || OwnerToken(pawn, refuelRecoveryItem.Item) == "unowned")
				{
					return false;
				}
			}
			return true;
		}

		private static bool ControllableOwner(string owner)
		{
			switch (owner)
			{
			default:
				return owner.StartsWith("ground:", StringComparison.Ordinal);
			case "inventory":
			case "carry":
			case "unowned":
				return true;
			}
		}

		private static string OwnerToken(Pawn pawn, Thing item)
		{
			if (item == null)
			{
				return "missing";
			}
			if (item.Destroyed)
			{
				return "destroyed";
			}
			if (item.Spawned)
			{
				if (item.holdingOwner != null || item.Map == null)
				{
					return "contradictory";
				}
				int num = 0;
				List<Thing> thingList = item.Position.GetThingList(item.Map);
				for (int i = 0; i < thingList.Count; i++)
				{
					if (thingList[i] == item)
					{
						num++;
					}
				}
				if (num != 1)
				{
					return "contradictory";
				}
				string text = N(item.Map.uniqueID) + ":" + N(item.Position.x) + ":" + N(item.Position.z);
				return ((item.Map == pawn.MapHeld) ? "ground:" : "other-map:") + text;
			}
			ThingOwner holdingOwner = item.holdingOwner;
			if (holdingOwner == null)
			{
				return "unowned";
			}
			if (!RefuelAttempt.IsMember(holdingOwner, item))
			{
				return "contradictory";
			}
			if (holdingOwner == pawn.inventory?.innerContainer && holdingOwner.Owner == pawn.inventory && pawn.inventory.pawn == pawn)
			{
				return "inventory";
			}
			if (holdingOwner == pawn.carryTracker?.innerContainer && holdingOwner.Owner == pawn.carryTracker && pawn.carryTracker.pawn == pawn)
			{
				return "carry";
			}
			if (holdingOwner.Owner is Pawn_InventoryTracker pawn_InventoryTracker)
			{
				return "other-inventory:" + N(pawn_InventoryTracker.pawn.thingIDNumber);
			}
			if (holdingOwner.Owner is Pawn_CarryTracker pawn_CarryTracker)
			{
				return "other-carry:" + N(pawn_CarryTracker.pawn.thingIDNumber);
			}
			return "other-holder:" + holdingOwner.Owner?.GetType().FullName;
		}

		private static string Snapshot(Pawn pawn, RefuelRecoveryPending record)
		{
			string text = N(pawn.MapHeld?.uniqueID ?? (-1));
			if (record == null)
			{
				return text + "|missing-record";
			}
			StringBuilder stringBuilder = new StringBuilder(text).Append('|');
			stringBuilder.Append(N(record.Version)).Append('|').Append(N(record.SelectedUnits))
				.Append('|')
				.Append(record.CreditObserved)
				.Append('|')
				.Append(record.CreditAmbiguous)
				.Append('|')
				.Append(record.PaidByResolution)
				.Append('|')
				.Append(record.CostPaidBeforeResolution)
				.Append('|')
				.Append(record.ResolutionMetadataSettled)
				.Append('|')
				.Append(N(record.CostItem?.thingIDNumber ?? (-1)));
			int num = record.Items?.Count ?? (-1);
			stringBuilder.Append('|').Append(N(num));
			if (num >= 0 && num <= 2)
			{
				for (int i = 0; i < num; i++)
				{
					Thing thing = record.Items[i]?.Item;
					stringBuilder.Append('|').Append(N(thing?.thingIDNumber ?? (-1))).Append(':')
						.Append(N(thing?.stackCount ?? (-1)))
						.Append(':')
						.Append(record.Items[i]?.Matches() ?? false)
						.Append(':')
						.Append(OwnerToken(pawn, thing));
				}
			}
			return stringBuilder.ToString();
		}

		private static string N(int value)
		{
			return value.ToString(CultureInfo.InvariantCulture);
		}

		private static string Details(Pawn pawn, RefuelRecoveryPending record)
		{
			if (record == null)
			{
				return "HD_RefuelRecoveryRecordMissing".Translate();
			}
			string key = (KnownConsumed(record) ? "HD_RefuelRecoveryCostPaid" : (DefinitelyUncredited(record) ? "HD_RefuelRecoveryNoCredit" : ((record.Version >= 2 && record.KnownUnpaidCost && !record.CreditAmbiguous) ? "HD_RefuelRecoveryKnownDebt" : "HD_RefuelRecoveryUncertain")));
			string text = ((record.Target != null) ? record.Target.LabelShort : ((string)"HD_RefuelRecoveryTargetMissing".Translate()));
			StringBuilder stringBuilder = new StringBuilder("HD_RefuelRecoveryDetailHeader".Translate(pawn.LabelShort, text, record.SelectedUnits));
			stringBuilder.Append("\n\n").Append(key.Translate());
			if (record.DestroyAttempted || record.ResolutionDestroyAttempts > 0)
			{
				stringBuilder.Append("\n").Append("HD_RefuelRecoveryPriorDestroy".Translate());
			}
			if (!CanPay(pawn, record) && record.KnownUnpaidCost && !record.PaidByResolution)
			{
				stringBuilder.Append("\n").Append("HD_RefuelRecoveryNoExactCost".Translate());
			}
			if (record.Items != null)
			{
				for (int i = 0; i < record.Items.Count && i < 2; i++)
				{
					RefuelRecoveryItem refuelRecoveryItem = record.Items[i];
					string value = ((refuelRecoveryItem?.Item == null) ? "HD_RefuelRecoveryItemMissing".Translate() : (refuelRecoveryItem.Item.Destroyed ? "HD_RefuelRecoveryItemDestroyed".Translate() : "HD_RefuelRecoveryItemCurrent".Translate(refuelRecoveryItem.Item.LabelShort, refuelRecoveryItem.Item.stackCount)));
					string text2 = OwnerToken(pawn, refuelRecoveryItem?.Item);
					object obj;
					switch (text2)
					{
					default:
						obj = (text2.StartsWith("ground:", StringComparison.Ordinal) ? "HD_RefuelRecoveryOnGround" : "HD_RefuelRecoveryElsewhere");
						break;
					case "unowned":
						obj = "HD_RefuelRecoveryNoOwner";
						break;
					case "carry":
						obj = "HD_RefuelRecoveryInCarry";
						break;
					case "inventory":
						obj = "HD_RefuelRecoveryInInventory";
						break;
					}
					string key2 = (string)obj;
					stringBuilder.Append("\n\n").Append(value).Append(" ")
						.Append(key2.Translate());
					if (refuelRecoveryItem == null || !refuelRecoveryItem.Matches())
					{
						stringBuilder.Append(" ").Append("HD_RefuelRecoveryChanged".Translate());
					}
				}
			}
			if (!string.IsNullOrEmpty(record.ResolutionError))
			{
				stringBuilder.Append("\n\n").Append("HD_RefuelRecoveryLastActionFailed".Translate());
			}
			return stringBuilder.ToString();
		}

		private static void Feedback(string key)
		{
			if (!MultiplayerCompat.InMultiplayerGame || MultiplayerCompat.ShouldShowLocalFeedback)
			{
				Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, historical: false);
			}
		}
	}
}
