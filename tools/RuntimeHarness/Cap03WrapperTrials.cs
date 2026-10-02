using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03WrapperScenario
    {
        private long PreparationCost(ISlotGroupParent parent) => checked(4L * (result.thingDefs + 1) * (result.categoryDefs + 1) * (result.specialDefs + 1)
            + 3L * (parent is Zone_Stockpile zone ? zone.cells.Count : ((Building_Storage)parent).def.size.x * ((Building_Storage)parent).def.size.z)
            + (parent is Building_Storage building ? building.AllComps.Count : 0) + result.gameMaps);
        private void Prepare(Scene s, ISlotGroupParent parent, bool full)
        {
            var p = new Cap03WrapperPreparation { parentKey = Key(parent), stage = full ? "complete-allowance" : "zero-allowance", sourceRequired = PreparationCost(parent), before = Counters(s) };
            p.allowance = full ? p.sourceRequired : 0;
            var value = api.Prepare(parent, p.allowance, true);
            p.status = api.ResultStatus(value); p.declaredCost = api.Get<long>(value, "DeclaredCompleteCost"); p.charged = api.Get<long>(value, "ChargedWork");
            p.ready = api.Get<bool>(value, "Ready"); p.indexed = api.Get<int>(value, "IndexedZoneCells"); p.warmup = api.Get<bool>(value, "FootprintWarmupRequested"); p.after = Counters(s);
            s.row.preparations.Add(p); Capture("preparation", s.row.id, p);
            Require(s.row.id + "-prepared-" + p.parentKey + "-" + p.stage, p.ready == full && p.declaredCost == p.allowance && p.charged == p.allowance && p.warmup
                && p.indexed == (full && parent is Zone_Stockpile z ? z.cells.Count : 0)
                && (full ? Complete(p.status) : !p.status.usable && p.status.observation == "Deferred" && p.status.capability == "Supported" && p.status.reason == "ProviderInitializing"),
                "Exact source-derived preparation reservation/readiness; an earlier unsupported or budget route cannot count as the wrapper boundary.");
            Check(s.row.id + "-prepare-counters-" + p.parentKey + "-" + p.stage, Same(p.before, p.after), "Preparation does not invoke either inner callback.");
        }
        private void Trial(Scene s, ISlotGroupParent parent, IntVec3 address, Thing subject, string kind)
        {
            var t = new Cap03WrapperTrial { id = s.row.id + "/" + kind, kind = kind, parentKey = Key(parent), cellAddress = address.ToString(), subjectId = subject.thingIDNumber, before = Counters(s) };
            t.query = "CAP03-C/" + t.id; t.parcelId = t.id + "/parcel"; s.row.trials.Add(t); Capture("trial-begin", s.row.id, t); object scope = null;
            try
            {
                var limits = api.MakeLimits(8192); t.allowance = api.LimitsWork(limits);
                var opened = api.OpenBudget(map, parent.GetSlotGroup(), t.query, limits); t.openStatus = api.ResultStatus(opened); t.openWork = api.OpenWork(opened);
                scope = api.Scope(opened); t.scopeIdentity = Id(scope); Capture("open", t.id, new Cap03BudgetOpen { status = t.openStatus, query = t.query, scopeIdentity = t.scopeIdentity, work = t.openWork });
                Require(t.id + "-open", Complete(t.openStatus) && scope != null, Json.Stringify(t.openStatus));
                t.afterOpen = api.ScopeUsed(scope);
                Check(t.id + "-open-work", Same(t.openWork, t.afterOpen) && Work(t.openWork, "GuardChecks") == result.gameMaps, "Open work and cumulative scope usage agree.");
                var rawCell = api.Observe(scope, address); t.cell = api.Cell(rawCell); t.cell.fixtureScene = s.row.id; t.cell.fixtureStage = kind; t.afterCell = api.ScopeUsed(scope); Capture("cell", t.id, t.cell);
                Check(t.id + "-cell-work", Adds(t.afterOpen, t.cell.work, t.afterCell), "Separate operation work reconciles to cumulative usage.");
                int occupants = kind == "resident-boundary" ? 1 : 0;
                Require(t.id + "-cell-identity", Complete(t.cell.status) && t.cell.parentKey == t.parentKey && t.cell.cell == address.ToString() && t.cell.mapId == map.uniqueID
                    && t.cell.tick == Find.TickManager.TicksGame && t.cell.query == t.query && t.cell.session == result.session && t.cell.generation == 1 && t.cell.observationId > 0
                    && t.cell.itemCount == occupants && t.cell.stacks.Count == occupants && t.cell.gridEntries == occupants + (parent is Thing ? 1 : 0)
                    && t.cell.maximumSlots == (s.row.asf ? 6 : parent is Zone_Stockpile ? 1 : 3) && t.cell.vacantSlots == t.cell.maximumSlots - occupants,
                    "Current scope returns the exact actual destination/occupancy before any eligibility call.");
                if (occupants != 0)
                    Require(t.id + "-resident-resource", t.cell.stacks[0].thingId == s.wrappers[0].thingIDNumber && t.cell.stacks[0].count == 1 && t.cell.stacks[0].stackLimit == 1 && t.cell.stacks[0].deficit == 0
                        && t.cell.stacks[0].providerTargetValid == (s.row.asf ? (bool?)true : null), "Full real wrapper, not a top-up candidate, remains in the native preflight grid.");
                var parcel = api.Parcel(t.parcelId, subject, actor); t.eligibility = api.EligibilityRow(api.Eligibility(scope, parcel, rawCell));
                t.eligibility.fixtureScene = s.row.id; t.eligibility.fixtureStage = kind; t.afterEligibility = api.ScopeUsed(scope); Capture("eligibility", t.id, t.eligibility);
                Check(t.id + "-eligibility-work", Adds(t.afterCell, t.eligibility.work, t.afterEligibility), "Eligibility's operation delta is distinguished from earlier cell work.");
                Check(t.id + "-parcel-identity", t.eligibility.observationId == t.cell.observationId && t.eligibility.parcelId == t.parcelId, "Exact parcel and returned-cell binding.");
                Grade(s, t, parent, subject); t.completed = true;
            }
            catch (Exception error) { t.error = error.ToString(); Capture("trial-exception", t.id, t.error); throw; }
            finally
            {
                if (scope != null)
                {
                    List<ProjectionFixtureWork> before = null;
                    try { before = api.ScopeUsed(scope); }
                    finally { ((IDisposable)scope).Dispose(); t.disposed = true; }
                    t.afterDispose = api.ScopeUsed(scope);
                    Check(t.id + "-disposed", Same(before, t.afterDispose) && !api.HasOpenScope, "Scope closure preserves accounting and releases the active entry.");
                }
                t.after = Counters(s); Check(t.id + "-callback-boundary", Same(t.before, t.after), "No inner virtual callback across this exact scope lifetime.");
                Capture("trial-result", s.row.id, t);
            }
        }
        private void Grade(Scene s, Cap03WrapperTrial t, ISlotGroupParent parent, Thing subject)
        {
            var e = t.eligibility; bool incoming = t.kind == "incoming-boundary", positive = t.kind == "ordinary-positive";
            Check(t.id + "-typed-outcome", positive ? Complete(e.status) && e.state == "Eligible"
                : !e.status.usable && e.status.observation == "Deferred" && e.status.capability == "Unsupported" && e.status.reason == "UnreviewedPredicate" && e.state == "NotEvaluated",
                "Required wrapper boundary differs from earlier filter/budget/provider failures and preserves an actual ordinary supported path.");
            Check(t.id + "-quantity", e.topUps.Count == 0 && e.vacantEligible == positive && e.unitsPerNewStack == (positive ? (int?)subject.def.stackLimit : null), "Containment publishes no quantity entitlement; empty positive cell uses real Steel stack limit.");
            var d = s.row.beforeProtected.destinations.Single(x => x.key == Key(parent));
            t.expectedFilterCharge = incoming ? 0 : 3L + d.effective.contents.disallowedSpecials.Count + 2L * d.declaredFixed.contents.disallowedSpecials.Count
                + (s.row.asf ? 1L + d.declaredFixed.contents.disallowedSpecials.Count : 0) + (positive ? 1L : 0);
            Check(t.id + "-filter-work", Work(e.work, "Filters") == t.expectedFilterCharge, "Source-derived filter precharges, separate from measured inner callback counts.");
            var names = new[] { "destination-enabled", "destination-faction", "selected-priority", "effective-thing-filter", "concrete-fixed-filter", "asf-declared-fixed-filter", "asf-actual-member-capacity", "native-IsGoodStoreCell", "hd-explicit-context-filter" };
            Check(t.id + "-predicate-shape", incoming ? e.predicates.Count == 0 : e.predicates.Select(x => x.name).SequenceEqual(names) && e.predicates.All(x => x.targetId == null), "Incoming guard precedes predicate construction; resident guard retains exact evaluated prefix.");
            if (incoming) return;
            for (int i = 0; i < names.Length; i++)
            {
                bool reached = i < 5 || (s.row.asf && i < 7) || positive && i >= 7;
                if (i == 1 && !(parent is Thing)) reached = false;
                string state = reached ? "Eligible" : "NotEvaluated";
                Check(t.id + "-predicate-" + names[i], e.predicates.Count(x => x.name == names[i] && x.state == state && x.reason == "None" && x.targetId == null) == 1,
                    "Exact allowed filter/ASF capacity boundary, then native and HD predicates remain unevaluated for resident containment.");
            }
        }
        private void NativeControls(Scene s)
        {
            var first = s.wrappers[0]; var second = s.wrappers[1]; var filter = s.parent.GetStoreSettings().filter;
            Native(s, "wrapper-versus-steel", first, s.steel, false, 0, false, () => first.CanStackWith(s.steel));
            Native(s, "wrapper-versus-peer", first, second, false, 1, false, () => first.CanStackWith(second));
            Native(s, "effective-filter-inner-hp", first, first, true, 0, true, () => filter.Allows(first), Id(filter));
            Native(s, "effective-recursive-inner-hp", first, first, s.row.asf || !s.row.resident, 0, true, () => s.parent.GetStoreSettings().AllowedToAccept(first), Id(filter));
            if (s.row.resident)
                Native(s, "full-resident-native-cell", first, second, true, 1, false, () => StoreUtility.IsGoodStoreCell(s.cell, map, second, actor, actor.Faction));
        }
        private void Native(Scene s, string route, Thing receiver, Thing argument, bool expected, long stackDelta, bool hpPositive, Func<bool> invoke, string filterIdentity = null)
        {
            var n = new Cap03WrapperNative { route = route, receiverId = receiver.thingIDNumber, argumentId = argument.thingIDNumber, expected = expected,
                filterIdentity = filterIdentity, cell = s.cell.ToString(), before = Counters(s) };
            n.actual = invoke(); n.after = Counters(s); s.row.nativeControls.Add(n); Capture("native-control", s.row.id, n);
            Check(s.row.id + "-native-" + route, n.actual == expected && n.after[0].stackCalls - n.before[0].stackCalls == stackDelta
                && (hpPositive ? n.after[0].hitPointReads > n.before[0].hitPointReads : n.after[0].hitPointReads == n.before[0].hitPointReads)
                && Same(n.before[1], n.after[1]), "Post-protection actual native result and directional per-inner Stack/HP deltas; HP stat plumbing is not assumed to have one read.");
        }
        private static long Work(List<ProjectionFixtureWork> rows, string kind) => rows.Single(x => x.kind == kind).value;
        private static bool Adds(List<ProjectionFixtureWork> before, List<ProjectionFixtureWork> operation, List<ProjectionFixtureWork> after) =>
            before.Count == 14 && operation.Count == 14 && after.Count == 14 && before.Select(x => x.kind).Distinct().Count() == 14
            && before.All(x => Work(after, x.kind) == checked(x.value + Work(operation, x.kind)));
    }
}
