using System;
using System.Collections.Generic;
using HaulersDream.Core;
using NUnit.Framework;

namespace HaulersDream.Tests
{
    [TestFixture]
    public class StorageFilterPolicyTests
    {
        // A case-insensitive set, as the Verse layer would build for player overrides (packageIds are
        // case-insensitive in RimWorld). Null when no override.
        static HashSet<string> Set(params string[] ids) =>
            ids.Length == 0 ? null : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);

        static bool Allowed(
            string packageId,
            StorageFilterContext ctx,
            bool useDefaults = true,
            bool denyLwm = true,
            string buildingDefName = "SomeBuilding",
            HashSet<string> denied = null,
            HashSet<string> allowed = null)
            => StorageFilterPolicy.IsAllowed(buildingDefName, packageId, ctx, useDefaults, denyLwm, denied, allowed);

        // ---- Curated set extraction (pin the exact WYU lists verbatim) ----

        [Test]
        public void SlowSet_IsExactlyLwmDeepStorage()
        {
            // Faithful to WYU: only LWM Deep Storage is "slow" (has a store delay). Everything else,
            // including Deep Storage Plus, is NOT slow.
            Assert.That(StorageFilterPolicy.SlowStoragePackageIds, Has.Count.EqualTo(1));
            Assert.That(StorageFilterPolicy.SlowStoragePackageIds.Contains("lwm.deepstorage"), Is.True);
            Assert.That(StorageFilterPolicy.SlowStoragePackageIds.Contains("im.skye.rimworld.deepstorageplus"), Is.False);
        }

        [Test]
        public void BeforeCarryAllowList_HasExactCuratedMembership()
        {
            var expected = new[]
            {
                "buddy1913.expandedstorageboxes",
                "im.skye.rimworld.deepstorageplus",
                "jangodsoul.simplestorage",
                "jangodsoul.simplestorage.ref",
                "ludeon.rimworld",
                "lwm.deepstorage",
                "mlie.displaycases",
                "mlie.eggincubator",
                "mlie.extendedstorage",
                "mlie.fireextinguisher",
                "mlie.functionalvanillaexpandedprops",
                "mlie.tobesdiningroom",
                "ogliss.thewhitecrayon.quarry",
                "primitivestorage.velcroboy333",
                "proxyer.smallshelf",
                "rimfridge.kv.rw",
                "sixdd.littlestorage2",
                "skullywag.extendedstorage",
                "solaris.furniturebase",
                "vanillaexpanded.vfecore",
                "vanillaexpanded.vfeart",
                "vanillaexpanded.vfefarming",
                "vanillaexpanded.vfespacer",
                "vanillaexpanded.vfesecurity",
            };
            // Exact set equality (no extra, none missing) — the curated list must stay verbatim WYU.
            Assert.That(StorageFilterPolicy.BeforeCarryAllowPackageIds, Has.Count.EqualTo(expected.Length));
            Assert.That(StorageFilterPolicy.BeforeCarryAllowPackageIds, Is.EquivalentTo(expected));
        }

        [Test]
        public void BeforeCarryAllowList_IncludesCoreAndLwm()
        {
            // Subtle WYU detail: Core and LWM ARE in the before-carry allow-list (LWM is slow for
            // opportunistic but a valid before-carry destination when denyLwm is off).
            Assert.That(StorageFilterPolicy.BeforeCarryAllowPackageIds.Contains("ludeon.rimworld"), Is.True);
            Assert.That(StorageFilterPolicy.BeforeCarryAllowPackageIds.Contains("lwm.deepstorage"), Is.True);
        }

        [Test]
        public void CuratedSets_AreCaseInsensitive()
        {
            Assert.That(StorageFilterPolicy.SlowStoragePackageIds.Contains("LWM.DeepStorage"), Is.True);
            Assert.That(StorageFilterPolicy.BeforeCarryAllowPackageIds.Contains("Ludeon.RimWorld"), Is.True);
        }

        // ---- Opportunistic context defaults ----

        [Test]
        public void Opportunistic_AllowsArbitraryStorageByDefault()
        {
            // Allow-all minus the slow set: a random mod's storage is allowed.
            Assert.That(Allowed("some.random.storagemod", StorageFilterContext.Opportunistic), Is.True);
            Assert.That(Allowed("ludeon.rimworld", StorageFilterContext.Opportunistic), Is.True);
        }

        [Test]
        public void Opportunistic_DeniesSlowSetWhenDenyLwmOn()
        {
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.Opportunistic, denyLwm: true), Is.False);
        }

        [Test]
        public void Opportunistic_AllowsSlowSetWhenDenyLwmOff()
        {
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.Opportunistic, denyLwm: false), Is.True);
        }

        [Test]
        public void Opportunistic_DeepStoragePlusStaysAllowed_NotSlow()
        {
            // Deep Storage Plus is in the before-carry allow list but is NOT slow — opportunistic allows it.
            Assert.That(Allowed("im.skye.rimworld.deepstorageplus", StorageFilterContext.Opportunistic, denyLwm: true), Is.True);
        }

        // ---- BeforeCarry context defaults ----

        [Test]
        public void BeforeCarry_DeniesUncuratedModByDefault()
        {
            Assert.That(Allowed("some.random.storagemod", StorageFilterContext.BeforeCarry), Is.False);
        }

        [Test]
        public void BeforeCarry_AllowsCuratedContainerMod()
        {
            Assert.That(Allowed("vanillaexpanded.vfecore", StorageFilterContext.BeforeCarry), Is.True);
            Assert.That(Allowed("rimfridge.kv.rw", StorageFilterContext.BeforeCarry), Is.True);
        }

        [Test]
        public void BeforeCarry_DeniesLwmWhenDenyLwmOn_EvenThoughCurated()
        {
            // LWM is in the allow-list, but the slow-set denial (checked before the curated default) wins
            // in the before-carry context when denyLwm is on.
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.BeforeCarry, denyLwm: true), Is.False);
        }

        [Test]
        public void BeforeCarry_AllowsLwmWhenDenyLwmOff_BecauseCurated()
        {
            // With the slow-set denial off, LWM is permitted before-carry because it IS in the allow-list.
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.BeforeCarry, denyLwm: false), Is.True);
        }

        // ---- Unload context (G4: never deny) ----

        [Test]
        public void Unload_NeverDeniesLwm_EvenWithDenyLwmOn()
        {
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.Unload, denyLwm: true), Is.True);
        }

        [Test]
        public void Unload_AllowsEverything()
        {
            Assert.That(Allowed("some.random.storagemod", StorageFilterContext.Unload), Is.True);
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.Unload), Is.True);
            // Even an uncurated mod that BeforeCarry would deny is allowed when unloading.
            Assert.That(Allowed("uncurated.mod", StorageFilterContext.Unload), Is.True);
        }

        [Test]
        public void Unload_PlayerDenyStillWins()
        {
            // A player explicit deny is honored even in Unload (it precedes the unload allow-all). The
            // Verse layer never pushes a player-denied building as the Unload destination, but the policy
            // is faithful to "player deny wins".
            Assert.That(
                Allowed("uncurated.mod", StorageFilterContext.Unload, denied: Set("uncurated.mod")),
                Is.False);
        }

        // ---- Player overrides ----

        [Test]
        public void PlayerDeny_OverridesDefaultAllow()
        {
            // Opportunistic would allow this mod by default; an explicit player deny flips it.
            Assert.That(
                Allowed("some.storagemod", StorageFilterContext.Opportunistic, denied: Set("some.storagemod")),
                Is.False);
        }

        [Test]
        public void PlayerAllow_OverridesDefaultDeny()
        {
            // BeforeCarry would deny an uncurated mod; an explicit player allow flips it.
            Assert.That(
                Allowed("uncurated.mod", StorageFilterContext.BeforeCarry, allowed: Set("uncurated.mod")),
                Is.True);
        }

        [Test]
        public void PlayerAllow_OverridesSlowSetDenial()
        {
            // An explicit player allow of LWM beats the slow-set denial (deny/allow are checked first).
            Assert.That(
                Allowed("lwm.deepstorage", StorageFilterContext.Opportunistic, denyLwm: true, allowed: Set("lwm.deepstorage")),
                Is.True);
        }

        [Test]
        public void PlayerDeny_BeatsPlayerAllow()
        {
            // Deny is checked before allow — a building in both lists is denied.
            Assert.That(
                Allowed("both.mod", StorageFilterContext.Opportunistic, denied: Set("both.mod"), allowed: Set("both.mod")),
                Is.False);
        }

        [Test]
        public void PlayerOverride_MatchesOnBuildingDefName()
        {
            // The override can key on the individual building defName (WYU's per-def tree leaf), not only
            // the owning mod's packageId.
            Assert.That(
                Allowed("ludeon.rimworld", StorageFilterContext.Opportunistic, buildingDefName: "Shelf", denied: Set("Shelf")),
                Is.False);
            Assert.That(
                Allowed("uncurated.mod", StorageFilterContext.BeforeCarry, buildingDefName: "MyCrate", allowed: Set("MyCrate")),
                Is.True);
        }

        // ---- useDefaults off ----

        [Test]
        public void DefaultsOff_BeforeCarryAllowsUncuratedMod()
        {
            // With auto-manage off and no player override, nothing restricts -> allowed (even before-carry).
            Assert.That(Allowed("uncurated.mod", StorageFilterContext.BeforeCarry, useDefaults: false), Is.True);
        }

        [Test]
        public void DefaultsOff_SlowSetStillDeniedWhenDenyLwmOn()
        {
            // The slow-set denial is independent of useDefaults (it's a separate toggle); LWM is still
            // denied for opportunistic when denyLwm is on even with auto-manage off.
            Assert.That(Allowed("lwm.deepstorage", StorageFilterContext.Opportunistic, useDefaults: false, denyLwm: true), Is.False);
        }

        [Test]
        public void DefaultsOff_PlayerOverridesStillApply()
        {
            Assert.That(
                Allowed("any.mod", StorageFilterContext.Opportunistic, useDefaults: false, denied: Set("any.mod")),
                Is.False);
        }

        // ---- Unknown / null building (never wrongly block) ----

        [Test]
        public void UnknownBuilding_NullPackageId_IsAllowedOpportunisticAndUnload()
        {
            Assert.That(Allowed(null, StorageFilterContext.Opportunistic), Is.True);
            Assert.That(Allowed(null, StorageFilterContext.Unload), Is.True);
        }

        [Test]
        public void UnknownBuilding_NullPackageId_IsDeniedBeforeCarryByDefault()
        {
            // BeforeCarry deny-by-default: an unknown owning mod isn't in the curated container list.
            Assert.That(Allowed(null, StorageFilterContext.BeforeCarry), Is.False);
            // ...but allowed before-carry when defaults are off.
            Assert.That(Allowed(null, StorageFilterContext.BeforeCarry, useDefaults: false), Is.True);
        }

        [Test]
        public void EmptyPackageId_BehavesLikeNull()
        {
            Assert.That(Allowed(string.Empty, StorageFilterContext.Opportunistic), Is.True);
            Assert.That(Allowed(string.Empty, StorageFilterContext.BeforeCarry), Is.False);
        }

        [Test]
        public void NullPlayerSets_AreHandled()
        {
            // Passing null for both override collections must not throw.
            Assert.That(
                StorageFilterPolicy.IsAllowed("B", "some.mod", StorageFilterContext.Opportunistic, true, true, null, null),
                Is.True);
        }

        [Test]
        public void PackageIdMatch_IsCaseInsensitiveViaCuratedSets()
        {
            // The curated sets are OrdinalIgnoreCase, so a differently-cased packageId still matches.
            Assert.That(Allowed("LWM.DeepStorage", StorageFilterContext.Opportunistic, denyLwm: true), Is.False);
            Assert.That(Allowed("VanillaExpanded.VFECore", StorageFilterContext.BeforeCarry), Is.True);
        }
    }
}
