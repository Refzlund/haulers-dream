using System;
using System.Runtime.ExceptionServices;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal enum InventoryDropUiPolicy
    {
        NativeInventory,
        CombatExtendedButton,
        CombatExtendedInventoryButton,
        CombatExtendedMenu
    }

    /// <summary>One explicitly confirmed inventory transfer. Contains no UI selection or MP API types.</summary>
    internal static class InventoryDropCommand
    {
        internal static bool CanControl(Pawn pawn)
        {
            if (pawn == null || pawn.Downed || pawn.InMentalState || pawn.CarriedBy != null)
                return false;
            if (pawn.Faction != Faction.OfPlayer && !pawn.IsPrisonerOfColony)
                return false;
            if (!pawn.IsPrisonerOfColony)
                return true;
            return (!pawn.Spawned || pawn.Map.mapPawns.AnyFreeColonistSpawned)
                && !PrisonBreakUtility.IsPrisonBreaking(pawn)
                && !(pawn.CurJob?.exitMapOnArrival ?? false);
        }

        internal static bool Eligible(Pawn pawn, Thing thing, InventoryDropUiPolicy policy)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null
                || !pawn.Position.InBounds(pawn.Map) || thing?.def == null || thing.Destroyed
                || thing.Spawned || thing.def.destroyOnDrop || thing.def.stackLimit <= 1
                || pawn.inventory?.innerContainer == null
                || !ReferenceEquals(thing.holdingOwner, pawn.inventory.innerContainer))
                return false;

            if (policy == InventoryDropUiPolicy.NativeInventory)
                return CanControl(pawn) && !pawn.IsQuestLodger()
                    && !(thing is Apparel apparel && pawn.apparel != null && pawn.apparel.IsLocked(apparel));

            bool control;
            switch (policy)
            {
                case InventoryDropUiPolicy.CombatExtendedButton:
                case InventoryDropUiPolicy.CombatExtendedInventoryButton:
                    control = CanControl(pawn) || (pawn.Faction == Faction.OfPlayer && pawn.RaceProps.packAnimal)
                        || (policy == InventoryDropUiPolicy.CombatExtendedInventoryButton && pawn.IsPrisonerOfColony);
                    break;
                case InventoryDropUiPolicy.CombatExtendedMenu:
                    control = CanControl(pawn);
                    break;
                default:
                    return false;
            }
            return control && InventoryDropCompat.CanUseCombatExtendedDrop(pawn, thing);
        }

        internal static Thing Resolve(Pawn pawn, int thingId, int count, int openingCount, int mapId,
            InventoryDropUiPolicy policy)
        {
            if (count < 1 || openingCount < 2 || count > openingCount || pawn?.Map == null
                || pawn.Map.uniqueID != mapId || pawn.inventory?.innerContainer == null)
                return null;
            var owner = pawn.inventory.innerContainer;
            for (int i = 0; i < owner.Count; i++)
            {
                var thing = owner[i];
                if (thing.thingIDNumber == thingId)
                    return thing.stackCount == openingCount && Eligible(pawn, thing, policy) ? thing : null;
            }
            return null;
        }

        // Register by name inside MultiplayerCompat.MpHooks. A SyncMethod attribute would break non-MP loading.
        public static void DropInventoryCountSynced(Pawn pawn, int inventoryThingId, int requestedCount,
            int expectedStackCount, int expectedMapId, int uiPolicy)
        {
            if (!MultiplayerCompat.InventoryQuantityDropExecuting)
                return;
            if (!MultiplayerCompat.InventoryQuantityDropAvailable)
            {
                Feedback("HD_DropAmountUnavailable");
                return;
            }
            var thing = Resolve(pawn, inventoryThingId, requestedCount, expectedStackCount, expectedMapId,
                (InventoryDropUiPolicy)uiPolicy);
            if (thing == null)
            {
                Feedback("HD_DropAmountChanged");
                return;
            }
            if (!InventoryDropCompat.TrySnapshot(pawn, thing, out var compatibility))
            {
                Feedback("HD_DropAmountUnavailable");
                return;
            }

            var owner = pawn.inventory.innerContainer;
            var map = pawn.Map;
            long placed = 0;
            bool invalidPlacement = false;
            bool nativeResult = false;
            Exception failure = null;
            var recovery = new InventoryDropRecovery(pawn, thing, requestedCount);
            HaulChurnGuard.EnterPlacementWrapper();
            try
            {
                nativeResult = owner.TryDrop(thing, pawn.Position, map, ThingPlaceMode.Near, requestedCount,
                    out _, (destination, count) =>
                    {
                        if (count <= 0 || destination == null || destination.Destroyed || !destination.Spawned
                            || destination.Map != map || destination.def != thing.def)
                            invalidPlacement = true;
                        else
                            placed += count;
                    });
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                recovery.Dispose();
                HaulChurnGuard.ExitPlacementWrapper();
            }

            recovery.RetainDetached(ref failure);
            bool retained = !thing.Destroyed && !thing.Spawned && ReferenceEquals(thing.holdingOwner, owner);
            long heldAfter = recovery.HeldUnits;
            long removed = (long)expectedStackCount - heldAfter;
            bool changed = !retained || heldAfter != expectedStackCount || placed != 0 || recovery.Recovered;
            if (changed)
            {
                try
                {
                    // Clear only inventory read caches; never reset cargo histories or other session state.
                    PawnMassCache.Clear();
                    TrackedMassCache.Clear();
                    InventoryShare.Clear();
                    OrganicInventoryShare.Clear();
                    CarriedHaulShare.Clear();
                    SurplusCache.Clear();
                    pawn.GetComp<CompHauledToInventory>()?.NotifyExplicitDropSettled(thing, retained);
                    InventoryDropCompat.SettleSidearms(pawn, thing, retained, compatibility);
                }
                catch (Exception e)
                {
                    if (failure == null) failure = e;
                    else Log.Error("[Hauler's Dream] Inventory-drop bookkeeping also failed: " + e);
                }
                finally
                {
                    // The partial native overload bypasses CE's Take/NotifyRemoved cache hooks.
                    try { InventoryDropCompat.RefreshCombatExtended(pawn); }
                    catch (Exception e)
                    {
                        if (failure == null) failure = e;
                        else Log.Error("[Hauler's Dream] Inventory-drop CE refresh also failed: " + e);
                    }
                }
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();

            if (invalidPlacement || removed < 0 || removed > requestedCount || placed != removed
                || (nativeResult && removed != requestedCount))
            {
                Log.Error($"[Hauler's Dream] Inventory drop observation mismatch: pawn={pawn.thingIDNumber}, "
                    + $"source={inventoryThingId}, requested={requestedCount}, before={expectedStackCount}, "
                    + $"heldAfter={heldAfter}, retained={retained}, placed={placed}, native={nativeResult}.");
                Feedback("HD_DropAmountUnconfirmed");
            }
            else if (removed == 0)
                Feedback("HD_DropAmountBlocked");
            else if (removed < requestedCount && LocalFeedback)
                Messages.Message("HD_DropAmountPartial".Translate(removed, requestedCount),
                    MessageTypeDefOf.RejectInput, historical: false);
        }

        private static bool LocalFeedback => !MultiplayerCompat.InMultiplayerGame
            || MultiplayerCompat.ShouldShowLocalFeedback;

        internal static void Feedback(string key)
        {
            if (LocalFeedback)
                Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
