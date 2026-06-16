using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests.Perf
{
    /// <summary>
    /// 0-alloc guard for the per-storage-building permit/deny decision. It is called once per storage
    /// candidate while a pawn chooses a destination, so it must do only membership tests against
    /// caller-owned sets and the static curated sets — no per-call allocation.
    /// </summary>
    [TestFixture, Category("Perf")]
    public class StorageFilterPolicyPerfTests
    {
        // Player override sets built ONCE outside the measured delegate (the Verse layer reuses its own).
        static readonly HashSet<string> Denied =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "player.denied.mod" };
        static readonly HashSet<string> Allowed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "player.allowed.mod" };

        const string BuildingDefName = "SomeStorageBuilding";

        [Test]
        public void IsAllowed_Opportunistic_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageFilterPolicy.IsAllowed(
                    BuildingDefName, "some.random.mod", StorageFilterContext.Opportunistic,
                    useDefaults: true, denyLwmForOpportunistic: true, Denied, Allowed),
                "opportunistic permit decision must be membership-only (no allocation)");

        [Test]
        public void IsAllowed_BeforeCarry_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageFilterPolicy.IsAllowed(
                    BuildingDefName, "vanillaexpanded.vfecore", StorageFilterContext.BeforeCarry,
                    useDefaults: true, denyLwmForOpportunistic: true, Denied, Allowed),
                "before-carry permit decision must be membership-only (no allocation)");

        [Test]
        public void IsAllowed_Unload_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageFilterPolicy.IsAllowed(
                    BuildingDefName, "lwm.deepstorage", StorageFilterContext.Unload,
                    useDefaults: true, denyLwmForOpportunistic: true, Denied, Allowed),
                "unload permit decision must be membership-only (no allocation)");

        [Test]
        public void IsAllowed_SlowSetDenial_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageFilterPolicy.IsAllowed(
                    BuildingDefName, "lwm.deepstorage", StorageFilterContext.Opportunistic,
                    useDefaults: true, denyLwmForOpportunistic: true, Denied, Allowed),
                "slow-set denial path must be membership-only (no allocation)");

        [Test]
        public void IsAllowed_NullOverrideSets_IsZeroAlloc() =>
            AllocationAssert.AssertZeroAlloc(
                () => StorageFilterPolicy.IsAllowed(
                    BuildingDefName, "some.random.mod", StorageFilterContext.Opportunistic,
                    useDefaults: true, denyLwmForOpportunistic: true, null, null),
                "null override sets must not allocate");
    }
}
