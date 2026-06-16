using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
    /// <summary>
    /// Vehicle Framework (VF, <c>SmashPhil.VehicleFramework</c>, assembly <c>Vehicles</c>) compatibility bridge —
    /// REFLECTION ONLY, no hard assembly reference, so HD compiles + runs byte-identically with or without VF.
    /// This is the SOLE VF-coupled surface in the mod: nothing else references any <c>Vehicles.*</c> /
    /// <c>SmashTools.*</c> type, and no method signature here exposes one (every VF object is a <see cref="Thing"/>
    /// or <c>object</c>). Mirrors <see cref="CECompat"/>: lazy <see cref="Init"/> on first use, a cached
    /// <see cref="IsActive"/>, no try/catch around the absent precondition (<c>AccessTools.TypeByName</c> returns
    /// null, never throws), and each live member null-guards its handle and fails OPEN.
    ///
    /// All bindings verified against the cloned VF 1.6 source
    /// (<c>.refmods/mods/vehicle-framework/Source/Vehicles</c>):
    /// <list type="bullet">
    /// <item><b>Cargo lives on the inherited Pawn inventory.</b> <c>VehiclePawn : Pawn</c> does NOT override
    /// <c>inventory</c> / <c>IThingHolder</c>, so <c>((Pawn)vehicle).inventory.innerContainer</c>,
    /// <c>MassUtility.*</c>, and <c>inventory.Count(def)</c> need NO reflection. Only the type checks,
    /// <c>AddOrTransfer</c>, the <c>cargoToLoad</c> field, the <c>CargoCapacity</c> VehicleStatDef +
    /// per-vehicle <c>GetStatValue(VehicleStatDef)</c>, the VRM claim/release, and <c>InVehicle</c> are reflected.</item>
    /// <item><b>Degrade-SAFE detection.</b> <see cref="IsActive"/> is true only when the load-bearing members all
    /// bound — a partial VF/API drift reports inactive rather than fail-opening to a broken capacity-0 deposit.</item>
    /// </list>
    /// </summary>
    public static class VehicleFrameworkCompat
    {
        private static bool initialized;
        private static bool active;

        private static Type vehiclePawnType;                 // Vehicles.VehiclePawn : Pawn  (the presence probe)
        private static Type vehicleDefType;                  // Vehicles.VehicleDef : ThingDef
        private static MethodInfo inVehicleThing;            // static ext Vehicles.Ext_Vehicles.InVehicle(Thing) -> bool
        private static Func<Thing, bool> inVehicleDelegate;  // open delegate bound to inVehicleThing (no Invoke / no object[] per call)
        private static MethodInfo addOrTransfer;             // instance VehiclePawn.AddOrTransfer(Thing,int) -> int
        private static FieldInfo cargoToLoadField;           // instance VehiclePawn.cargoToLoad : List<TransferableOneWay>
        private static MethodInfo getStatValue;              // instance VehiclePawn.GetStatValue(VehicleStatDef) -> float
        private static object cargoCapacityStatDef;          // Vehicles.VehicleStatDefOf.CargoCapacity (a VehicleStatDef)

        private static Type vrmType;                         // Vehicles.VehicleReservationManager : MapComponent
        private static MethodInfo reserveVehicleGeneric;     // VRM.Reserve<LocalTargetInfo,VehicleTargetReservation>(VehiclePawn,Pawn,Job,LocalTargetInfo) -> bool
        private static MethodInfo releaseAllClaimedBy;       // VRM.ReleaseAllClaimedBy(Pawn) -> void

        /// <summary>Whether Vehicle Framework is loaded AND its load-bearing members all bound. Detected by
        /// <c>Vehicles.VehiclePawn</c> being resolvable (the <c>Vehicles</c> assembly only loads when VF is active);
        /// gated further on the deposit/capacity/manifest members so a partial API drift reports inactive (degrade
        /// SAFE — HD then behaves as without VF) instead of fail-opening to a broken capacity-0 load. Cached after
        /// the first call.</summary>
        public static bool IsActive
        {
            get
            {
                if (!initialized)
                    Init();
                return active;
            }
        }

        private static void Init()
        {
            initialized = true;
            active = false;
            // No try/catch: VF-ABSENT is the precondition below — AccessTools.TypeByName returns null (it does not
            // throw) when VF isn't loaded, and every member resolve is null-guarded. So a throw in here would be a
            // GENUINE reflection/contract fault worth surfacing as a red error, not the optional-dependency case.
            // Init runs ONCE (lazily on first IsActive), so there is no per-tick cost.
            vehiclePawnType = AccessTools.TypeByName("Vehicles.VehiclePawn");
            if (vehiclePawnType == null)
                return; // VF not loaded — the real precondition, no catch needed

            vehicleDefType = AccessTools.TypeByName("Vehicles.VehicleDef");

            // InVehicle(Thing) — the (Thing) overload at Ext_Vehicles.cs:1017 (forward-compat durable; the (Pawn)
            // overload at :1006 is annotated "// TODO 1.7 - Remove"). A static extension method on Verse.Thing.
            var extVehiclesType = AccessTools.TypeByName("Vehicles.Ext_Vehicles");
            if (extVehiclesType != null)
            {
                inVehicleThing = AccessTools.Method(extVehiclesType, "InVehicle", new[] { typeof(Thing) });
                // Bind an open delegate so the per-holder InVehicle check (called per holder in the colony-wide
                // EatFromInventory / OrganicInventoryShare loops) costs no `new object[1]` + boxed-bool Invoke per
                // call. The signature matches exactly — a static `(Thing) -> bool` with no by-ref args binds cleanly
                // to Func<Thing, bool>. Fail-safe: if CreateDelegate throws (a VF fork with an odd signature), the
                // delegate stays null and InVehicle falls back to the reflective Invoke path (behaviour-identical).
                if (inVehicleThing != null)
                {
                    try
                    {
                        inVehicleDelegate = (Func<Thing, bool>)Delegate.CreateDelegate(typeof(Func<Thing, bool>), inVehicleThing);
                    }
                    catch (Exception)
                    {
                        inVehicleDelegate = null; // fall back to Invoke in InVehicle (no behaviour change)
                    }
                }
            }

            // AddOrTransfer(Thing,int) -> int (the event-correct deposit; does its OWN cargoToLoad decrement).
            addOrTransfer = AccessTools.Method(vehiclePawnType, "AddOrTransfer", new[] { typeof(Thing), typeof(int) });

            // cargoToLoad : List<TransferableOneWay> (the pending-load manifest field).
            cargoToLoadField = AccessTools.Field(vehiclePawnType, "cargoToLoad");

            // Cargo capacity: the per-vehicle GetStatValue(VehicleStatDef) + the CargoCapacity VehicleStatDef.
            // NOTE: CargoCapacity is a Vehicles.VehicleStatDef, NOT a vanilla StatDef — there is NO
            // DefDatabase<StatDef>.GetNamedSilentFail("CargoCapacity") path (it would return null -> capacity 0 ->
            // deposits nothing). Bind the typed accessor + the static field instead.
            var vehicleStatDefType = AccessTools.TypeByName("Vehicles.VehicleStatDef");
            if (vehicleStatDefType != null)
            {
                getStatValue = AccessTools.Method(vehiclePawnType, "GetStatValue", new[] { vehicleStatDefType });
                var statDefOf = AccessTools.TypeByName("Vehicles.VehicleStatDefOf");
                var cargoCapField = statDefOf != null ? AccessTools.Field(statDefOf, "CargoCapacity") : null;
                if (cargoCapField != null)
                    cargoCapacityStatDef = cargoCapField.GetValue(null);
            }

            // VehicleReservationManager (VF's OWN MapComponent — NOT Map.reservationManager). Belt-and-suspenders
            // claim alongside the authoritative WorkGiver suppression; never load-bearing for correctness.
            vrmType = AccessTools.TypeByName("Vehicles.VehicleReservationManager");
            var vtrType = AccessTools.TypeByName("Vehicles.VehicleTargetReservation");
            if (vrmType != null)
            {
                releaseAllClaimedBy = AccessTools.Method(vrmType, "ReleaseAllClaimedBy", new[] { typeof(Pawn) });
                if (vtrType != null)
                {
                    var reserveOpen = AccessTools.Method(vrmType, "Reserve");
                    if (reserveOpen != null && reserveOpen.IsGenericMethodDefinition)
                        reserveVehicleGeneric = reserveOpen.MakeGenericMethod(typeof(LocalTargetInfo), vtrType);
                }
            }

            // Degrade SAFE: only claim active when EVERY load-bearing member bound. The VRM claim is optional
            // (belt-and-suspenders) so it does NOT gate IsActive — but the deposit, the capacity, and the manifest
            // ARE load-bearing for the bulk-load feature, so a drift in any of them reports inactive.
            active = addOrTransfer != null
                     && getStatValue != null
                     && cargoCapacityStatDef != null
                     && cargoToLoadField != null;
            if (active)
                Log.Message("[Hauler's Dream] Vehicle Framework detected — bulk-load-into-vehicle + event-correct "
                            + "pack-animal deposit on, eat-from / build-from a parked vehicle's cargo enabled.");
            else
                // VF is present (VehiclePawn resolved) but one or more load-bearing deposit/capacity/manifest
                // members did not bind (a VF version/API drift) — degrade SAFE (report inactive => HD behaves as
                // without VF), but name which member(s) are missing so the lost integration isn't silent.
                HDLog.Warn("Vehicle Framework present but a load-bearing member did not resolve "
                           + "(VehiclePawn.AddOrTransfer(Thing,int)=" + (addOrTransfer != null)
                           + ", VehiclePawn.GetStatValue(VehicleStatDef)=" + (getStatValue != null)
                           + ", VehicleStatDefOf.CargoCapacity=" + (cargoCapacityStatDef != null)
                           + ", VehiclePawn.cargoToLoad=" + (cargoToLoadField != null)
                           + "); vehicle bulk-load / cargo integration is OFF (HD behaves as without VF).");
        }

        /// <summary>True if <paramref name="t"/> is a VF <c>VehiclePawn</c> (subclass-safe). False when VF absent.</summary>
        public static bool IsVehicle(Thing t)
        {
            if (!IsActive || t == null)
                return false;
            return vehiclePawnType.IsInstanceOfType(t);
        }

        /// <summary>True if <paramref name="d"/> is a VF <c>VehicleDef</c> (subclass-safe). False when VF absent.</summary>
        public static bool IsVehicleDef(ThingDef d)
        {
            if (!IsActive || d == null || vehicleDefType == null)
                return false;
            return vehicleDefType.IsInstanceOfType(d);
        }

        /// <summary>True if <paramref name="t"/> is currently inside a vehicle (riding a seat or stored in cargo) —
        /// its parent holder is a <c>VehicleRoleHandler</c> or a vehicle's inventory tracker, so its inventory is
        /// unreachable. Used by the MOW/ORG holder guard to skip an embarked holder. False when VF absent or the
        /// member is unbound.</summary>
        public static bool InVehicle(Thing t)
        {
            if (!IsActive || t == null || inVehicleThing == null)
                return false;
            // Fast path: the open delegate bound in Init (no per-call object[] / boxing, no MethodInfo.Invoke).
            var del = inVehicleDelegate;
            if (del != null)
                return del(t);
            // Fallback (delegate creation failed at Init — a VF fork with an odd signature): the reflective path.
            // No try/catch: IsActive + the bound member + t != null are checked — a throw here is a real
            // VF-integration fault to surface, not a silent fail-open (which would only cost a wasted pathing guard).
            return (bool)inVehicleThing.Invoke(null, new object[] { t });
        }

        /// <summary>
        /// Deposit up to <paramref name="count"/> units of <paramref name="item"/> into <paramref name="vehicle"/>'s
        /// cargo via VF's <c>VehiclePawn.AddOrTransfer(Thing,int)</c> — which moves the items into
        /// <c>inventory.innerContainer</c>, fires the <c>CargoAdded</c> event, AND decrements the matching
        /// <c>cargoToLoad</c> entry by the PASSED count (so the caller MUST clamp <paramref name="count"/> to the
        /// remaining demand — see <see cref="RemainingDemandForThing"/> — or the entry is driven negative→removed
        /// and the def over-loaded). Returns the units actually moved, or <b>-1</b> when VF is absent / the member is
        /// unbound (the caller MUST treat -1 as "fall back to a raw container transfer").
        /// </summary>
        public static int AddOrTransfer(Thing vehicle, Thing item, int count)
        {
            if (!IsActive || addOrTransfer == null || vehicle == null || item == null || count <= 0)
                return -1; // -1 sentinel: caller does the raw transfer
            // No try/catch: IsActive + the bound member + non-null args are checked above — a throw here is a real
            // VF-integration fault to surface (the deposit is the load-bearing path), not a silent degrade.
            return (int)addOrTransfer.Invoke(vehicle, new object[] { item, count });
        }

        /// <summary>The vehicle's live <c>cargoToLoad</c> manifest (<c>List&lt;TransferableOneWay&gt;</c>, returned
        /// as the non-generic <see cref="IList"/> so no <c>Vehicles.*</c> type appears in the signature — the
        /// entries are vanilla <see cref="TransferableOneWay"/> the caller reads directly). Null when VF absent / the
        /// field is unbound / the vehicle has no manifest.</summary>
        public static IList CargoToLoad(Thing vehicle)
        {
            if (!IsActive || cargoToLoadField == null || vehicle == null)
                return null;
            // No try/catch: a field read on a bound FieldInfo for a present vehicle is a real fault if it throws.
            return cargoToLoadField.GetValue(vehicle) as IList;
        }

        /// <summary>
        /// How many units of <paramref name="item"/>'s exact transferable identity the vehicle still wants — the
        /// CLAMP source for <see cref="AddOrTransfer"/>. Computed in C# with the SAME vanilla matcher VF's
        /// <c>AddOrTransfer</c> uses to find the entry it decrements
        /// (<c>TransferableUtility.TransferableMatchingDesperate(item, cargoToLoad, PodsOrCaravanPacking)</c>, which
        /// matches by def+stuff+quality), so the clamp lines up exactly with the decrement. Returns 0 when VF absent
        /// / no matching entry.
        /// </summary>
        public static int RemainingDemandForThing(Thing vehicle, Thing item)
        {
            if (!IsActive || vehicle == null || item == null)
                return 0;
            var ltl = CargoToLoad(vehicle);
            if (ltl == null || ltl.Count == 0)
                return 0;
            // CargoToLoad is the live List<TransferableOneWay>; the matcher takes List<TransferableOneWay>. Build a
            // typed list once (the entries are already TransferableOneWay) so the vanilla matcher binds.
            var typed = new List<TransferableOneWay>(ltl.Count);
            for (int i = 0; i < ltl.Count; i++)
                if (ltl[i] is TransferableOneWay tr)
                    typed.Add(tr);
            var match = TransferableUtility.TransferableMatchingDesperate(item, typed, TransferAsOneMode.PodsOrCaravanPacking);
            return match?.CountToTransfer ?? 0;
        }

        /// <summary>The vehicle's cargo mass capacity — the <c>CargoCapacity</c> VehicleStatDef value via
        /// <c>VehiclePawn.GetStatValue(VehicleStatDef)</c> (this is a soft mass budget, not a slot limit; the inner
        /// container itself is uncapped). Returns 0 when VF absent / the member is unbound (a 0 capacity makes the
        /// trip-mass budget take no group headroom — the sweep falls back to the pawn's own free space only).</summary>
        public static float CargoCapacity(Thing vehicle)
        {
            if (!IsActive || getStatValue == null || cargoCapacityStatDef == null || vehicle == null)
                return 0f;
            // No try/catch: a bound GetStatValue on a present vehicle with a resolved VehicleStatDef — surface a throw.
            return (float)getStatValue.Invoke(vehicle, new[] { cargoCapacityStatDef });
        }

        /// <summary>
        /// Belt-and-suspenders: claim <paramref name="vehicle"/> in VF's own <c>VehicleReservationManager</c> (a
        /// <c>MapComponent</c>) so VF's single-stack <c>WorkGiver_PackVehicle</c> sees it reserved and stands down
        /// alongside HD's authoritative <c>JobOnThing</c> bulk-load redirect (which replaces VF's single-stack job for
        /// HD-eligible scanning pawns; this VRM claim only covers the fail-open / non-HD-eligible case). Reflects
        /// <c>VRM.Reserve&lt;LocalTargetInfo, VehicleTargetReservation&gt;(vehicle, pawn, job, (LocalTargetInfo)vehicle)</c>.
        /// Fail-OPEN: returns true (the claim is best-effort, never blocks the job) when VF absent / unbound. This is
        /// the ONE place a guarded reflective call may degrade silently — it is pure redundancy (addendum §0.1), so
        /// a reflection throw is swallowed to fail-open rather than crash the driver.
        /// </summary>
        public static bool ReserveVehicle(Thing vehicle, Pawn pawn, Job job)
        {
            if (!IsActive || reserveVehicleGeneric == null || vrmType == null
                || vehicle == null || pawn == null || job == null)
                return true; // fail-open: the WorkGiver-suppress prefix is the authoritative stand-down
            var map = vehicle.MapHeld;
            if (map == null)
                return true;
            var vrm = map.GetComponent(vrmType);
            if (vrm == null)
                return true;
            // The ONLY permitted fail-open swallow in the mod (addendum §0.1): this VRM claim is pure redundancy on
            // top of the authoritative WorkGiver suppression, so a reflection fault here must NOT crash the load job
            // — degrade to "claimed best-effort" (true). (LocalTargetInfo) vehicle is the implicit Thing conversion.
            try
            {
                return (bool)reserveVehicleGeneric.Invoke(vrm,
                    new object[] { vehicle, pawn, job, (LocalTargetInfo)vehicle });
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Release every VRM claim <paramref name="pawn"/> holds (the finish-action counterpart to
        /// <see cref="ReserveVehicle"/>). Reflects <c>VRM.ReleaseAllClaimedBy(Pawn)</c>. No-ops when VF absent /
        /// unbound / the pawn has no map. (Fail-open: a missed release is harmless — VF's own
        /// <c>VerifyAndValidateClaimants</c> drops a stale claim whose claimant's CurJob no longer matches.)</summary>
        public static void ReleaseVehicleClaims(Pawn pawn)
        {
            if (!IsActive || releaseAllClaimedBy == null || vrmType == null || pawn == null)
                return;
            var map = pawn.MapHeld;
            if (map == null)
                return;
            var vrm = map.GetComponent(vrmType);
            if (vrm == null)
                return;
            // No try/catch here: unlike Reserve (which lives inside the running deposit and must never crash it),
            // Release runs in the finish action where a fault is a real bug to surface; a stale claim is self-healed
            // by VF's VerifyAndValidateClaimants regardless.
            releaseAllClaimedBy.Invoke(vrm, new object[] { pawn });
        }
    }
}
