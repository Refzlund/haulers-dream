using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal sealed class RefuelAttempt
	{
		internal readonly Pawn Pawn;

		internal readonly Job Job;

		private readonly int jobLoadId;

		internal readonly CompRefuelable Target;

		internal readonly Thing Source;

		internal readonly int Bound;

		internal readonly bool PickupOnly;

		internal readonly CompHauledToInventory Tracking;

		internal readonly RefuelProvenance Provenance;

		private readonly ThingOwner originalInventory;

		private readonly Map originalMap;

		private readonly IntVec3 originalPosition;

		internal Thing Prepared;

		internal Thing DestroyPiece;

		internal RefuelSplit PreparationSplit;

		internal RefuelSplit ConsumptionSplit;

		internal RefuelSplit SplitWindow;

		internal int Selected;

		internal int FloatDepth;

		internal bool InvokingList;

		internal bool ExpectedFloat;

		internal bool CreditCallStarted;

		internal bool CreditObserved;

		internal bool Ambiguous;

		internal bool DestroyAttempted;

		private bool settled;

		private bool registrationAttempted;

		private Exception secondary;

		private RefuelAttempt(RefuelActivation activation, CompRefuelable target, Thing source, int bound, bool pickupOnly)
		{
			Pawn pawn = activation.Pawn;
			Job job = activation.Job;
			Pawn = pawn;
			Job = job;
			Target = target;
			Source = source;
			Bound = bound;
			PickupOnly = pickupOnly;
			jobLoadId = job.loadID;
			Tracking = pawn.GetComp<CompHauledToInventory>();
			originalInventory = pawn.inventory.innerContainer;
			originalMap = pawn.Map;
			originalPosition = pawn.Position;
			Provenance = new RefuelProvenance(activation, Tracking, source);
		}

		internal static RefuelAttempt Begin(RefuelActivation activation, CompRefuelable target, Thing source, int bound, bool pickupOnly = false)
		{
			Pawn pawn = activation.Pawn;
			Job job = activation.Job;
			if (RefuelNativeWitness.Active != null)
			{
				RefuelNativeWitness.Active.Ambiguous = true;
				throw new InvalidOperationException("Reentered an active ordinary refuel attempt.");
			}
			if (!RefuelNativeWitness.Ready || pawn?.inventory?.innerContainer == null || pawn.GetComp<CompHauledToInventory>() == null || pawn.GetComp<CompHauledToInventory>().HasPendingRefuelRecovery || pawn.CurJob != job || target == null || source == null || source.Destroyed || bound <= 0 || bound > source.stackCount || (!pickupOnly && !IsMember(pawn.inventory.innerContainer, source)))
			{
				throw new InvalidOperationException("Ordinary refuel attempt is not authorized by its live context.");
			}
			return RefuelNativeWitness.Active = new RefuelAttempt(activation, target, source, bound, pickupOnly);
		}

		internal Thing RunSplit(RefuelSplit split)
		{
			if (SplitWindow != null)
			{
				Ambiguous = true;
				throw new InvalidOperationException("Nested scoped refuel split.");
			}
			SplitWindow = split;
			try
			{
				Thing thing = split.Source.SplitOff(split.Requested);
				if (thing != split.Piece || !split.HasFundedPiece || thing == null || thing.Destroyed || thing.stackCount != split.Requested)
				{
					Ambiguous = true;
					throw new InvalidOperationException("Native refuel split identity/quantity changed.");
				}
				return thing;
			}
			finally
			{
				SplitWindow = null;
			}
		}

		internal Thing PrepareOwnedPortion()
		{
			if (!PickupOnly && Bound == Source.stackCount)
			{
				Prepared = Source;
				return Prepared;
			}
			PreparationSplit = new RefuelSplit(Source, Bound);
			Thing thing = (Prepared = RunSplit(PreparationSplit));
			if (!IsMember(originalInventory, thing))
			{
				if (thing.Spawned || thing.holdingOwner != null)
				{
					throw new InvalidOperationException("Refuel preparation left the piece in an unexpected owner.");
				}
				if (!originalInventory.TryAdd(thing, canMergeWithExistingStacks: false) || !IsMember(originalInventory, thing))
				{
					throw new InvalidOperationException("Refuel preparation could not establish native inventory custody.");
				}
			}
			if (PickupOnly)
			{
				RegisterPicked(thing);
			}
			else
			{
				Provenance.ApplyToNewOwnedPiece(thing);
			}
			return thing;
		}

		private void RegisterPicked(Thing piece)
		{
			if (!registrationAttempted && IsMember(Pawn.inventory?.innerContainer, piece) && !piece.Destroyed)
			{
				registrationAttempted = true;
				Tracking.RegisterHauledItem(piece);
				Tracking.NotifyYieldPicked();
			}
		}

		internal void InvokeNativeRefuel()
		{
			if (Prepared == null || !IsMember(originalInventory, Prepared) || Ambiguous)
			{
				throw new InvalidOperationException("Refuel portion is not in verified native custody.");
			}
			InvokingList = true;
			try
			{
				Target.Refuel(new List<Thing> { Prepared });
			}
			finally
			{
				InvokingList = false;
			}
			if (!CreditObserved)
			{
				Ambiguous = true;
			}
			if (Ambiguous || !PhysicalCostPaid())
			{
				throw new InvalidOperationException("Native refuel returned without a verified credit and physical cost.");
			}
		}

		internal void DestroyOnce(Thing piece, DestroyMode mode = DestroyMode.Vanish)
		{
			if (DestroyAttempted)
			{
				throw new InvalidOperationException("Refuel destruction was already attempted.");
			}
			if (piece == null || piece.Destroyed || piece.stackCount != Selected)
			{
				throw new InvalidOperationException("Refuel destruction does not match the credited quantity.");
			}
			DestroyPiece = piece;
			DestroyAttempted = true;
			piece.Destroy(mode);
		}

		private bool PhysicalCostPaid()
		{
			if (CreditObserved && !Ambiguous && DestroyAttempted && DestroyPiece != null)
			{
				return DestroyPiece.Destroyed;
			}
			return false;
		}

		private void FinishKnownCost()
		{
			if (!CreditObserved || Ambiguous || DestroyAttempted)
			{
				return;
			}
			if (ConsumptionSplit != null)
			{
				if (ConsumptionSplit.HasFundedPiece && ConsumptionSplit.Piece != null && !ConsumptionSplit.Piece.Destroyed && ConsumptionSplit.Piece.stackCount == Selected)
				{
					DestroyOnce(ConsumptionSplit.Piece);
				}
			}
			else
			{
				if (Prepared == null || Prepared.Destroyed || Selected <= 0 || Prepared.stackCount < Selected)
				{
					return;
				}
				if (Prepared.stackCount == Selected)
				{
					DestroyOnce(Prepared);
					return;
				}
				ConsumptionSplit = new RefuelSplit(Prepared, Selected);
				Thing thing = null;
				try
				{
					thing = RunSplit(ConsumptionSplit);
				}
				catch (Exception error)
				{
					NoteSecondary(error);
				}
				if (thing == null && ConsumptionSplit.HasFundedPiece)
				{
					thing = ConsumptionSplit.Piece;
				}
				if (thing != null && !thing.Destroyed && thing.stackCount == Selected && !Ambiguous)
				{
					DestroyOnce(thing);
				}
			}
		}

		internal void FinishAndRethrow(Exception primary)
		{
			if (settled)
			{
				throw new InvalidOperationException("Ordinary refuel settlement repeated.");
			}
			settled = true;
			try
			{
				TryAction(FinishKnownCost);
				List<Thing> list = ActualPieces();
				bool flag = true;
                bool retainedForReview = false;
				for (int num = 0; num < list.Count; num++)
				{
					Thing piece = list[num];
					if (!piece.Destroyed && !EnsureNativeCustody(piece))
					{
                        // Ordinary native custody failed. Preserve the actual detached parcel
                        // in a saved owner and require review even if this final retention succeeds.
                        retainedForReview = true;
                        TryAction(() => Tracking.RefuelRecoveryCustody.Retain(piece));
                        if (!HasCustody(piece)) flag = false;
					}
					if (PickupOnly && !piece.Destroyed)
					{
						TryAction(delegate
						{
							RegisterPicked(piece);
						});
					}
					else if (!piece.Destroyed)
					{
						if (piece == Source)
						{
							RefuelSplit consumptionSplit = ConsumptionSplit;
							if (consumptionSplit == null || !consumptionSplit.HasFundedPiece || ConsumptionSplit.Piece != piece)
							{
								goto IL_00f0;
							}
						}
						TryAction(delegate
						{
							Provenance.ApplyToNewOwnedPiece(piece, restoreUnload: true);
						});
					}
					goto IL_00f0;
					IL_00f0:
					if (piece.Destroyed)
					{
						TryAction(delegate
						{
							RemoveDestroyedResidue(piece);
						});
					}
					TryAction(delegate
					{
						Provenance.Reconcile(piece);
					});
				}
				TryAction(delegate
				{
					Provenance.Reconcile(Source);
				});
				TryAction(delegate
				{
					if (!RefuelCeCarryBudget.TryRefreshForDriver(Pawn, out var refusal))
					{
						throw new InvalidOperationException(refusal);
					}
				});
				if (Ambiguous || (CreditCallStarted && !PhysicalCostPaid()) || !flag || retainedForReview)
				{
					Tracking.BlockRefuelRecovery();
					if (primary == null)
					{
						primary = new InvalidOperationException("Ordinary refuel recovery remains unresolved.");
					}
					RefuelRecoveryPending record = new RefuelRecoveryPending
					{
						Version = 2,
						KnownUnpaidCost = (CreditObserved && !Ambiguous && !PhysicalCostPaid()),
						CostPaidBeforeResolution = PhysicalCostPaid(),
						JobLoadId = jobLoadId,
						Target = Target.parent,
						TargetId = Target.parent.GetUniqueLoadID(),
						SelectedUnits = Selected,
						CreditObserved = CreditObserved,
						CreditAmbiguous = (Ambiguous || (CreditCallStarted && !CreditObserved)),
						DestroyAttempted = DestroyAttempted,
						CustodyVerified = flag,
						FirstError = RefuelRecoveryPending.ErrorText(primary),
						SecondaryError = RefuelRecoveryPending.ErrorText(secondary)
					};
					object obj = DestroyPiece;
					if (obj == null)
					{
						RefuelSplit consumptionSplit2 = ConsumptionSplit;
						obj = ((consumptionSplit2 != null && consumptionSplit2.HasFundedPiece) ? ConsumptionSplit.Piece : ((ConsumptionSplit == null && Prepared?.stackCount == Selected) ? Prepared : null));
					}
					Thing thing = (Thing)obj;
					if (thing != null)
					{
						record.CostItem = thing;
						record.CostItemId = thing.GetUniqueLoadID();
					}
					for (int num2 = 0; num2 < list.Count; num2++)
					{
						Thing thing2 = list[num2];
						record.Items.Add(new RefuelRecoveryItem
						{
							Item = thing2,
							LoadId = thing2.GetUniqueLoadID(),
							Count = thing2.stackCount,
							Destroyed = thing2.Destroyed,
							Tagged = Provenance.Tagged,
							FirstTick = Provenance.FirstTick,
							NotForSale = Provenance.NotForSale,
							Caravan = Provenance.Caravan,
							WasUnloadEverything = Provenance.WasUnloadEverything
						});
					}
					Tracking.SaveRefuelFailure(record);
					TryAction(delegate
					{
						Messages.Message(record.Describe(), Pawn, MessageTypeDefOf.NegativeEvent, historical: false);
					});
				}
			}
			catch (Exception ex)
			{
				Tracking.BlockRefuelRecovery();
				if (primary == null)
				{
					primary = ex;
				}
				else
				{
					NoteSecondary(ex);
				}
			}
			finally
			{
				if (RefuelNativeWitness.Active == this)
				{
					RefuelNativeWitness.Active = null;
				}
			}
			if (primary != null && secondary != null)
			{
				try
				{
                    primary.Data["HaulersDream.Refuel.SecondarySettlement"] = HDFault.Render(secondary);
					HDLog.Err("Ordinary refuel secondary settlement failure (primary preserved): " + RefuelRecoveryPending.ErrorText(secondary));
				}
				catch
				{
				}
			}
			if (primary != null)
			{
				ExceptionDispatchInfo.Capture(primary).Throw();
			}
			if (secondary != null)
			{
				ExceptionDispatchInfo.Capture(secondary).Throw();
			}
		}

		private List<Thing> ActualPieces()
		{
			List<Thing> list = new List<Thing>(2);
			AddUnique(list, Prepared);
			RefuelSplit preparationSplit = PreparationSplit;
			if (preparationSplit != null && preparationSplit.HasFundedPiece)
			{
				AddUnique(list, PreparationSplit.Piece);
			}
			RefuelSplit consumptionSplit = ConsumptionSplit;
			if (consumptionSplit != null && consumptionSplit.HasFundedPiece)
			{
				AddUnique(list, ConsumptionSplit.Piece);
			}
			if ((PreparationSplit?.Piece != null && !PreparationSplit.HasFundedPiece) || (ConsumptionSplit?.Piece != null && !ConsumptionSplit.HasFundedPiece))
			{
				Ambiguous = true;
			}
			return list;
		}

		private static void AddUnique(List<Thing> list, Thing thing)
		{
			if (thing == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == thing)
				{
					return;
				}
			}
			list.Add(thing);
		}

		private bool EnsureNativeCustody(Thing piece)
		{
			if (HasCustody(piece))
			{
				return true;
			}
			if (piece.holdingOwner != null || piece.Spawned)
			{
				return false;
			}
			TryAction(delegate
			{
				originalInventory.TryAdd(piece, canMergeWithExistingStacks: false);
			});
			if (HasCustody(piece))
			{
				return true;
			}
			ThingOwner<Thing> current = Pawn.inventory?.innerContainer;
			if (current != null && current != originalInventory && piece.holdingOwner == null && !piece.Spawned)
			{
				TryAction(delegate
				{
					current.TryAdd(piece, canMergeWithExistingStacks: false);
				});
			}
			if (HasCustody(piece))
			{
				return true;
			}
			ThingOwner<Thing> carry = Pawn.carryTracker?.innerContainer;
			if (carry != null && carry.Count == 0 && piece.holdingOwner == null && !piece.Spawned)
			{
				TryAction(delegate
				{
					carry.TryAdd(piece, canMergeWithExistingStacks: false);
				});
			}
			if (HasCustody(piece))
			{
				return true;
			}
			if (piece.holdingOwner == null && !piece.Spawned && originalMap != null && piece.def.Size.x == 1 && piece.def.Size.z == 1)
			{
				bool flag = false;
				for (int num = -2; num <= 2; num++)
				{
					if (flag)
					{
						break;
					}
					for (int num2 = -2; num2 <= 2; num2++)
					{
						if (flag)
						{
							break;
						}
						IntVec3 cell = originalPosition + new IntVec3(num, 0, num2);
						if (cell.InBounds(originalMap) && cell.Standable(originalMap) && cell.GetThingList(originalMap).Count == 0)
						{
							flag = true;
							TryAction(delegate
							{
								GenSpawn.Spawn(piece, cell, originalMap, piece.Rotation);
							});
						}
					}
				}
			}
			return HasCustody(piece);
		}

		private void RemoveDestroyedResidue(Thing piece)
		{
			ThingOwner holdingOwner = piece.holdingOwner;
			if (holdingOwner != null && IsMember(holdingOwner, piece))
			{
				holdingOwner.Remove(piece);
			}
		}

		private static bool HasCustody(Thing piece)
		{
			if (piece == null || piece.Destroyed)
			{
				return false;
			}
			if (IsMember(piece.holdingOwner, piece))
			{
				return !piece.Spawned;
			}
			if (!piece.Spawned || piece.Map == null || piece.holdingOwner != null)
			{
				return false;
			}
			List<Thing> thingList = piece.Position.GetThingList(piece.Map);
			for (int i = 0; i < thingList.Count; i++)
			{
				if (thingList[i] == piece)
				{
					return true;
				}
			}
			return false;
		}

		internal static bool IsMember(ThingOwner owner, Thing thing)
		{
			if (owner == null || thing == null || thing.holdingOwner != owner)
			{
				return false;
			}
			int num = 0;
			for (int i = 0; i < owner.Count; i++)
			{
				if (owner[i] == thing)
				{
					num++;
				}
			}
			return num == 1;
		}

		private void TryAction(Action action)
		{
			try
			{
				action();
			}
			catch (Exception error)
			{
				NoteSecondary(error);
			}
		}

		private void NoteSecondary(Exception error)
		{
			if (secondary == null)
			{
				secondary = error;
			}
		}
	}
}
