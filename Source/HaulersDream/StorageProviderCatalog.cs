using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HaulersDream.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HaulersDream
{
    internal sealed class StorageProjectionPreparation
    {
        internal ProjectionStatus Status { get; }
        internal long DeclaredCompleteCost { get; }
        internal long ChargedWork { get; }
        internal bool Ready { get; }
        internal int IndexedZoneCells { get; }
        internal bool FootprintWarmupRequested { get; }
        internal StorageProjectionPreparation(ProjectionStatus status, long allowance, long work, bool ready, int indexedCells = 0, bool warmup = false)
        { Status = status; DeclaredCompleteCost = allowance; ChargedWork = work; Ready = ready; IndexedZoneCells = indexedCells; FootprintWarmupRequested = warmup; }
    }

    internal sealed class StorageProviderCatalog
    {
        private static readonly FieldInfo EverFixed = typeof(StorageSettings).GetField("cachedEverStorableFixedSettings", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo DisallowedSpecial = typeof(ThingFilter).GetField("disallowedSpecialFilters", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo SpecialWorker = typeof(SpecialThingFilterDef).GetField("workerInt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private readonly StorageProjectionEnvironment environment;
        private readonly Game game;
        private readonly Guid session;
        private readonly Dictionary<ISlotGroupParent, PreparedMember> prepared = new Dictionary<ISlotGroupParent, PreparedMember>();
        private readonly HashSet<Type> stackTypes = new HashSet<Type>(), compTypes = new HashSet<Type>(), specialTypes = new HashSet<Type>();
        private readonly List<MethodBase> unclassifiedGlobal = new List<MethodBase>();
        private readonly List<Tuple<MethodBase, Type>> scopedUnclassified = new List<Tuple<MethodBase, Type>>();
        internal long Generation { get; }
        internal StorageProjectionAsfBinding Asf { get; }
        internal ProjectionReason AsfBindingReason { get; }
        internal IReadOnlyList<string> PatchInventory { get; }
        internal IReadOnlyList<string> ReviewEvidence { get; }
        internal int SetupDefinitionsVisited { get; }
        internal string NativeIdentity { get; }
        internal bool NativeSemanticsReviewed { get; }

        private StorageProviderCatalog(StorageProjectionEnvironment environment)
        {
            this.environment = environment; game = environment.Game; session = environment.SessionId; Generation = environment.CatalogGeneration;
            NativeIdentity = VersionControl.CurrentVersionStringWithRev + ";" + typeof(Map).Assembly.FullName + ";mvid=" + typeof(Map).Module.ModuleVersionId;
            NativeSemanticsReviewed = VersionControl.CurrentMajor == 1 && VersionControl.CurrentMinor == 6
                && VersionControl.CurrentBuild == 4871 && VersionControl.CurrentRevision == 591;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "AdaptiveStorageFramework").ToList();
            AsfBindingReason = ProjectionReason.None;
            if (assemblies.Count > 1) AsfBindingReason = ProjectionReason.BindingFault;
            else if (assemblies.Count == 1)
            {
                try { Asf = new StorageProjectionAsfBinding(assemblies[0]); }
                catch (NotSupportedException) { AsfBindingReason = ProjectionReason.UnsupportedProvider; }
                catch (Exception) { AsfBindingReason = ProjectionReason.BindingFault; }
            }
            var targets = new HashSet<MethodBase>();
            void Add(Type type, string name, params Type[] args)
            {
                var method = AccessTools.Method(type, name, args);
                if (method == null) throw new MissingMethodException(type.FullName, name);
                targets.Add(method);
            }
            Add(typeof(GridsUtility), "GetMaxItemsAllowedInCell", typeof(IntVec3), typeof(Map));
            Add(typeof(StoreUtility), "NoStorageBlockersIn", typeof(IntVec3), typeof(Map), typeof(Thing));
            Add(typeof(StoreUtility), "IsGoodStoreCell", typeof(IntVec3), typeof(Map), typeof(Thing), typeof(Pawn), typeof(Faction));
            targets.Add(AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker"));
            Add(typeof(StorageSettings), "AllowedToAccept", typeof(Thing));
            Add(typeof(StorageSettings), "EverStorableFixedSettings");
            Add(typeof(ThingFilter), "Allows", typeof(Thing));
            Add(typeof(ThingFilter), "CreateOnlyEverStorableThingFilter");
            Add(typeof(Thing), "CanStackWith", typeof(Thing));
            Add(typeof(ThingWithComps), "CanStackWith", typeof(Thing));
            void Register(Type type, string methodName, HashSet<Type> supported, bool requireConcreteReview = false)
            {
                if (type == null) return;
                var method = type.GetMethod(methodName, new[] { typeof(Thing) });
                if (method == null) return;
                targets.Add(method);
                if (type.Assembly == typeof(Thing).Assembly || (!requireConcreteReview && method.DeclaringType.Assembly == typeof(Thing).Assembly)) supported.Add(type);
            }
            // Setup-only binding of actual def/comp/worker types. Query code never
            // resolves MethodInfo or scans assemblies for an encountered Thing.
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                Register(def.thingClass, "CanStackWith", stackTypes);
                if (def.comps != null) foreach (var comp in def.comps) Register(comp.compClass, "AllowStackWith", compTypes, true);
            }
            foreach (var def in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
            {
                Register(def.workerClass, "Matches", specialTypes, true);
                if (def.workerClass != null)
                {
                    var always = def.workerClass.GetMethod("AlwaysMatches", new[] { typeof(ThingDef) });
                    if (always != null) targets.Add(always);
                }
            }
            SetupDefinitionsVisited = DefDatabase<ThingDef>.AllDefsListForReading.Count + DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count;
            targets.Add(AccessTools.PropertyGetter(typeof(Building), "MaxItemsInCell"));
            foreach (var t in new[] { typeof(Building_Storage), typeof(Zone_Stockpile), typeof(StorageGroup) })
            {
                Add(t, "GetParentStoreSettings");
                Add(t, "GetStoreSettings");
                if (t != typeof(StorageGroup)) { Add(t, "AllSlotCells"); Add(t, "AllSlotCellsList"); Add(t, "Accepts", typeof(Thing)); }
            }
            if (Asf != null) foreach (var method in Asf.Methods) targets.Add(method);
            // Linked StorageSettings dispatches through IStorageGroupMember, not
            // ISlotGroupParent. ASF inherits this explicit base implementation
            // even though it reimplements the latter interface's fixed getter.
            var groupFixedGetter = typeof(IStorageGroupMember).GetProperty("ParentStoreSettings")?.GetGetMethod();
            if (groupFixedGetter == null) throw new MissingMemberException("Storage group fixed-filter getter.");
            foreach (var type in Asf == null ? new[] { typeof(Building_Storage) } : new[] { typeof(Building_Storage), Asf.ParentType })
            {
                var map = type.GetInterfaceMap(typeof(IStorageGroupMember));
                int index = Array.IndexOf(map.InterfaceMethods, groupFixedGetter);
                if (index < 0 || map.TargetMethods[index].DeclaringType != typeof(Building_Storage))
                    throw new NotSupportedException("Unreviewed storage group fixed-filter dispatch.");
                targets.Add(map.TargetMethods[index]);
            }
            if (targets.Contains(null)) throw new MissingMethodException("Required storage extension point missing.");
            var inventory = new List<string>();
            var unreviewedCalls = new List<MethodBase>();
            foreach (var target in targets)
            {
                var info = Harmony.GetPatchInfo(target);
                if (info == null) continue;
                foreach (var patchKind in new[] { HarmonyPatchType.Prefix, HarmonyPatchType.Postfix, HarmonyPatchType.Transpiler, HarmonyPatchType.Finalizer })
                {
                    var patches = patchKind == HarmonyPatchType.Prefix ? info.Prefixes
                        : patchKind == HarmonyPatchType.Postfix ? info.Postfixes
                        : patchKind == HarmonyPatchType.Transpiler ? info.Transpilers : info.Finalizers;
                    foreach (var patch in patches)
                    {
                        inventory.Add(target.DeclaringType.FullName + "." + target.Name + " <- " + patch.owner + ":" + patch.PatchMethod.DeclaringType.FullName
                            + "." + patch.PatchMethod.Name + ";kind=" + patchKind + ";mvid=" + patch.PatchMethod.Module.ModuleVersionId + ";token=" + patch.PatchMethod.MetadataToken);
                        if (KnownPatch(target, patch, patchKind)) continue;
                        if (target.Name == "CanStackWith" || target.Name == "AllowStackWith" || target.Name == "Matches" || target.Name == "AlwaysMatches")
                        { unreviewedCalls.Add(target); continue; }
                        // An unreviewed extension has no proven applicability bound.
                        // Scope only when the target itself is an ASF-only member.
                        if (Asf != null && target.DeclaringType.Assembly == Asf.ParentType.Assembly)
                            scopedUnclassified.Add(Tuple.Create(target, Asf.ParentType));
                        else unclassifiedGlobal.Add(target);
                    }
                }
            }
            foreach (var method in unreviewedCalls)
            {
                var set = method.Name == "CanStackWith" ? stackTypes : method.Name == "AllowStackWith" ? compTypes : specialTypes;
                set.RemoveWhere(t => method.DeclaringType.IsAssignableFrom(t));
            }
            PatchInventory = new ReadOnlyCollection<string>(inventory);
            ReviewEvidence = new ReadOnlyCollection<string>(new List<string>
            {
                "Native 1.6 physical slot/filter/stack semantics inspected in storage-resource-projection-contract.md and its independent review.",
                "Only exact HD recursive-gate/exception-observer registrations and inspected ASF storage patch identities are recognized; no caller Boolean grants unknown quantitative support.",
                "Unmodeled overrides, comp quantity rules, custom predicate costs and native detours require a separate inspected binding; Harmony inventory is not proof that detours do not exist."
            });
            if (EverFixed == null || EverFixed.FieldType != typeof(StorageSettings) || DisallowedSpecial == null || SpecialWorker == null)
                throw new MissingFieldException("Native filter readiness binding.");
        }

        internal static ProjectionStatus Create(StorageProjectionEnvironment environment, out StorageProviderCatalog catalog)
        {
            catalog = null;
            if (environment == null) return ProjectionStatus.Failure(ProjectionReason.InvalidRequest);
            var reason = environment.CheckThread();
            if (reason != ProjectionReason.None) return ProjectionStatus.Failure(reason);
            if (StorageResourceProjector.HasOpenScope) return ProjectionStatus.Failure(ProjectionReason.NestedProjection);
            if (environment.Game == null || !ReferenceEquals(Current.Game, environment.Game) || environment.SessionId == Guid.Empty || environment.CatalogGeneration <= 0)
                return ProjectionStatus.Failure(ProjectionReason.InvalidRequest);
            try { catalog = new StorageProviderCatalog(environment); return ProjectionStatus.Complete; }
            catch (ArgumentException) { return ProjectionStatus.Failure(ProjectionReason.InvalidRequest); }
            catch (Exception) { return ProjectionStatus.Failure(ProjectionReason.BindingFault); }
        }

        private bool KnownPatch(MethodBase target, Patch registration, HarmonyPatchType kind)
        {
            var patch = registration.PatchMethod;
            if (registration.owner == HaulersDreamMod.HarmonyId && target.DeclaringType == typeof(StoreUtility) && target.Name == "IsGoodStoreCell")
            {
                if (kind == HarmonyPatchType.Postfix && patch == AccessTools.Method(typeof(Patch_IsGoodStoreCell_HonourCommitments), "Postfix")) return true;
                // The actual HD startup adds this void observer after patching.
                // On success it returns before any work; on failure it logs the
                // exception without changing it or the result. Eligibility's
                // catch still returns BindingFault, never quantitative evidence.
                // Recognize only this owner, target, method and finalizer kind.
                if (kind == HarmonyPatchType.Finalizer
                    && patch == AccessTools.Method(typeof(HDLog), nameof(HDLog.UniversalExceptionFinalizer), new[] { typeof(Exception), typeof(MethodBase) })) return true;
            }
            if (Asf == null) return false;
            return (kind == HarmonyPatchType.Transpiler && target.DeclaringType == typeof(GridsUtility) && target.Name == "GetMaxItemsAllowedInCell" && patch == Asf.LimitPatch)
                || (kind == HarmonyPatchType.Transpiler && target.DeclaringType == typeof(StoreUtility) && target.Name == "NoStorageBlockersIn" && patch == Asf.ValidityPatch)
                || (kind == HarmonyPatchType.Prefix && target.DeclaringType == typeof(StoreUtility) && target.Name == "TryFindBestBetterStoreCellForWorker" && patch == Asf.WorkerPatch);
        }

        internal ProjectionReason CheckLifetime(StorageProjectionEnvironment expected)
        {
            if (!ReferenceEquals(environment, expected) || environment.CheckThread() != ProjectionReason.None) return ProjectionReason.NeedsMainThread;
            if (!ReferenceEquals(Current.Game, game) || !ReferenceEquals(environment.Game, game) || session != environment.SessionId) return ProjectionReason.SessionChanged;
            return Generation == environment.CatalogGeneration ? ProjectionReason.None : ProjectionReason.CatalogChanged;
        }
        internal ProjectionReason Classify(ISlotGroupParent parent, out bool asf)
        {
            var shape = ShapeReason(parent);
            asf = Asf != null && parent != null && parent.GetType() == Asf.ParentType;
            if (shape != ProjectionReason.None) return shape;
            if (!NativeSemanticsReviewed) return ProjectionReason.UnsupportedProvider;
            if (unclassifiedGlobal.Count != 0) return ProjectionReason.UnreviewedPatch;
            foreach (var pair in scopedUnclassified) if (pair.Item2.IsInstanceOfType(parent)) return ProjectionReason.UnreviewedPatch;
            if (parent is Building_Storage building)
            {
                // Unknown comps may implement quantity limits without overriding the
                // parent. A future inspected binding must establish any custom
                // comp's quantity semantics and call bounds; no package blanket is used.
                foreach (var comp in building.AllComps)
                    if (!compTypes.Contains(comp.GetType()))
                        return ProjectionReason.UnreviewedPredicate;
            }
            return ProjectionReason.None;
        }
        internal ProjectionReason ShapeReason(ISlotGroupParent parent)
        {
            if (parent == null) return ProjectionReason.InvalidRequest;
            Type type = parent.GetType();
            if (type == typeof(Zone_Stockpile) || type == typeof(Building_Storage) || (Asf != null && type == Asf.ParentType)) return ProjectionReason.None;
            if (type.Assembly.GetName().Name == "AdaptiveStorageFramework" && AsfBindingReason != ProjectionReason.None) return AsfBindingReason;
            return ProjectionReason.UnsupportedProvider;
        }
        internal bool StackPredicateReviewed(Thing target)
        {
            // Native minified wrappers delegate both filter and stack behavior
            // to an inner Thing. Its identity, holder and nested predicate cost
            // require their own binding; do not execute it under the outer type.
            if (target == null || target is MinifiedThing) return false;
            if (!stackTypes.Contains(target.GetType())) return false;
            if (target is ThingWithComps withComps)
                foreach (var comp in withComps.AllComps)
                {
                    if (!compTypes.Contains(comp.GetType())) return false;
                }
            return true;
        }

        internal StorageProjectionPreparation PrepareMember(ISlotGroupParent parent, long declaredCompleteCost, int maximumZoneCells = 65536, bool warmFootprintCache = false)
        {
            var thread = environment.CheckThread();
            if (thread != ProjectionReason.None) return Prep(thread, 0);
            var lifetime = CheckLifetime(environment);
            if (lifetime != ProjectionReason.None) return Prep(lifetime, 0);
            if (StorageResourceProjector.HasOpenScope) return Prep(ProjectionReason.NestedProjection, 0);
            if (environment.TransferInProgress) return Prep(ProjectionReason.TransferInProgress, 0);
            var parentShape = ShapeReason(parent);
            if (parentShape != ProjectionReason.None) return Prep(parentShape, 0);
            if (parent == null || parent.Map == null || declaredCompleteCost < 0 || maximumZoneCells <= 0) return Prep(ProjectionReason.InvalidRequest, 0);
            if (game.Maps.Count > declaredCompleteCost) return Prep(ProjectionReason.ProviderInitializing, 0);
            if (!game.Maps.Contains(parent.Map)) return Prep(ProjectionReason.InvalidRequest, game.Maps.Count);
            long charged = 0;
            try
            {
                // Cold filter creation can walk category/def/special-filter sets.
                // Reserve a complete conservative cross-product before invoking it;
                // setup has its own explicit allowance and never runs inside a page.
                long cost = checked(4L * (DefDatabase<ThingDef>.AllDefsListForReading.Count + 1)
                    * (DefDatabase<ThingCategoryDef>.AllDefsListForReading.Count + 1)
                    * (DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count + 1));
                int zoneCount = parent is Zone_Stockpile zone ? zone.cells.Count : 0;
                if (zoneCount > maximumZoneCells) return Prep(ProjectionReason.ProviderScanRequired, 0);
                long footprint = parent is Building_Storage b ? checked((long)b.def.size.x * b.def.size.z) : 0;
                if (footprint < 0 || footprint > int.MaxValue) return Prep(ProjectionReason.UnsupportedFootprint, 0);
                cost = checked(cost + 3L * zoneCount + (warmFootprintCache ? 3L * footprint : 0)
                    + (parent is Building_Storage withComps ? withComps.AllComps.Count : 0) + game.Maps.Count);
                if (declaredCompleteCost < cost) return Prep(ProjectionReason.ProviderInitializing, 0);
                charged = cost;
                var shape = Classify(parent, out bool asf);
                if (shape != ProjectionReason.None) return Prep(shape, charged);
                using (StorageResourceProjector.EnterPreparation())
                using (StorageCommitments.SuppressOwnGateForProjection())
                {
                    bool mayCreate = EverFixed.GetValue(null) == null || (asf && Asf.ReadyFixed((Building_Storage)parent) == null);
                    if (mayCreate)
                    {
                        foreach (var special in DefDatabase<SpecialThingFilterDef>.AllDefsListForReading)
                        {
                            if (special.allowedByDefault) continue;
                            if (!specialTypes.Contains(special.workerClass)) return Prep(ProjectionReason.UnreviewedPredicate, charged);
                            if (special.Worker == null) return Prep(ProjectionReason.BindingFault, charged);
                        }
                    }
                    var native = StorageSettings.EverStorableFixedSettings();
                    var concrete = asf ? Asf.PrepareFixed((Building_Storage)parent) : parent.GetParentStoreSettings();
                    var baseFixed = GroupMemberFixed(parent);
                    if (native == null || native.owner != null || concrete == null || concrete.owner != null || baseFixed == null || baseFixed.owner != null)
                        return Prep(ProjectionReason.UnsupportedProvider, cost);
                    foreach (var filter in new[] { parent.GetStoreSettings().filter, concrete.filter, baseFixed.filter })
                    {
                        if (filter == null || filter.GetType() != typeof(ThingFilter)) return Prep(ProjectionReason.UnreviewedPredicate, charged);
                        var specials = (List<SpecialThingFilterDef>)DisallowedSpecial.GetValue(filter);
                        if (specials == null) return Prep(ProjectionReason.BindingFault, charged);
                        // Bound even a malformed duplicate-filled live filter list.
                        if (specials.Count > DefDatabase<SpecialThingFilterDef>.AllDefsListForReading.Count) return Prep(ProjectionReason.ProviderScanRequired, charged);
                        foreach (var special in specials)
                        {
                            if (special == null || !specialTypes.Contains(special.workerClass)) return Prep(ProjectionReason.UnreviewedPredicate, charged);
                            if (special.Worker == null) return Prep(ProjectionReason.BindingFault, charged);
                        }
                    }
                    PreparedProjectionZoneCells zoneIndex = parent is Zone_Stockpile stockpile ? new PreparedProjectionZoneCells(stockpile) : null;
                    if (warmFootprintCache && parent is Building_Storage building)
                    {
                        // Explicit slow preparation only, after the entire inspected
                        // footprint was precharged and the concrete type classified.
                        // Never called by Open/ObserveCell/ObserveGroupPage.
                        var warmed = building.AllSlotCellsList();
                        var rect = building.OccupiedRect();
                        if (warmed.Count != footprint) return Prep(ProjectionReason.GroupChanged, charged);
                        for (int i = 0; i < warmed.Count; i++)
                            if (warmed[i] != new IntVec3(rect.minX + i / rect.Height, 0, rect.minZ + i % rect.Height))
                                return Prep(ProjectionReason.GroupChanged, charged);
                    }
                    var state = new PreparedMember(parent, native, concrete, baseFixed, zoneIndex);
                    var after = CheckLifetime(environment);
                    if (after != ProjectionReason.None) return Prep(after, charged);
                    if (environment.TransferInProgress) return Prep(ProjectionReason.TransferInProgress, charged);
                    prepared[parent] = state;
                    return new StorageProjectionPreparation(ProjectionStatus.Complete, declaredCompleteCost, cost, true, zoneIndex?.Count ?? 0, warmFootprintCache);
                }
            }
            catch (ProjectionAbort abort) { return Prep(abort.Reason, charged); }
            catch (Exception) { return Prep(ProjectionReason.BindingFault, charged); }
            StorageProjectionPreparation Prep(ProjectionReason reason, long work) => new StorageProjectionPreparation(ProjectionStatus.Failure(reason), declaredCompleteCost, work, false, warmup: warmFootprintCache);
        }

        internal bool TryReady(ISlotGroupParent parent, out StorageSettings settings)
        {
            settings = null;
            if (!prepared.TryGetValue(parent, out var ready)) return false;
            var native = (StorageSettings)EverFixed.GetValue(null);
            if (!ready.Matches(parent, native)) return false;
            bool asf = Asf != null && parent.GetType() == Asf.ParentType;
            // All lazy sources were explicitly prepared. Reading the ASF backing
            // field establishes readiness before calling its actual declared getter.
            var actual = asf ? Asf.ReadyFixed((Building_Storage)parent) : parent.GetParentStoreSettings();
            if (!ReferenceEquals(actual, ready.Concrete) || actual == null || !ReferenceEquals(actual.filter, ready.Filter) || actual.owner != null) return false;
            var baseFixed = GroupMemberFixed(parent);
            if (!ReferenceEquals(baseFixed, ready.BaseFixed) || baseFixed.owner != null || !ReferenceEquals(baseFixed.filter, ready.BaseFilter)) return false;
            settings = actual; return true;
        }
        internal ProjectionReason CheckFilter(StorageSettings settings, ProjectionWorkBudget work)
        {
            if (settings?.filter == null || settings.filter.GetType() != typeof(ThingFilter)) return ProjectionReason.UnreviewedPredicate;
            var specials = (List<SpecialThingFilterDef>)DisallowedSpecial.GetValue(settings.filter);
            if (specials == null) return ProjectionReason.BindingFault;
            if (!work.TryCharge(ProjectionWork.Cost(ProjectionWorkKind.Filters, specials.Count + 1L))) return ProjectionReason.BudgetExhausted;
            foreach (var special in specials)
                if (special == null || !specialTypes.Contains(special.workerClass)) return ProjectionReason.UnreviewedPredicate;
                else if (SpecialWorker.GetValue(special) == null) return ProjectionReason.ProviderInitializing;
            return ProjectionReason.None;
        }
        internal StorageSettings BaseFixed(ISlotGroupParent parent) => prepared.TryGetValue(parent, out var ready) ? ready.BaseFixed : null;
        private static StorageSettings GroupMemberFixed(ISlotGroupParent parent) =>
            parent is IStorageGroupMember member ? member.ParentStoreSettings : parent.GetParentStoreSettings();
        internal ProjectionReason ZoneCells(Zone_Stockpile zone, out PreparedProjectionZoneCells index)
        {
            index = null;
            if (!prepared.TryGetValue(zone, out var ready) || ready.ZoneCells == null) return ProjectionReason.ProviderInitializing;
            if (!ready.ZoneCells.Matches(zone)) return ProjectionReason.GroupChanged;
            index = ready.ZoneCells; return ProjectionReason.None;
        }
        private sealed class PreparedMember
        {
            internal StorageSettings Concrete { get; }
            internal ThingFilter Filter { get; }
            internal StorageSettings BaseFixed { get; }
            internal ThingFilter BaseFilter { get; }
            internal PreparedProjectionZoneCells ZoneCells { get; }
            private readonly StorageSettings native;
            private readonly ThingDef def, stuff;
            internal PreparedMember(ISlotGroupParent parent, StorageSettings native, StorageSettings concrete, StorageSettings baseFixed, PreparedProjectionZoneCells zoneCells)
            { this.native = native; Concrete = concrete; Filter = concrete.filter; BaseFixed = baseFixed; BaseFilter = baseFixed.filter; ZoneCells = zoneCells; def = (parent as Thing)?.def; stuff = (parent as Thing)?.Stuff; }
            internal bool Matches(ISlotGroupParent parent, StorageSettings currentNative) => ReferenceEquals(native, currentNative)
                && ReferenceEquals(def, (parent as Thing)?.def) && ReferenceEquals(stuff, (parent as Thing)?.Stuff);
        }
    }
}
