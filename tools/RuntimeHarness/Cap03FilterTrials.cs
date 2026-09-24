using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HaulersDream.RuntimeHarness
{
    internal sealed partial class Cap03FilterScenario
    {
        private long PreparationCost(Building_Storage parent) => checked(4L * (result.thingDefs + 1) * (result.categoryDefs + 1) * (result.specialDefs + 1)
            + 3L * parent.def.size.x * parent.def.size.z + parent.AllComps.Count + result.gameMaps);
        private void Prepare(Cap03FilterScene scene, Building_Storage parent, long allowance, string stage)
        {
            var p = new Cap03FilterPreparation { stage = stage, parentId = parent.thingIDNumber, allowance = allowance,
                sourceRequired = PreparationCost(parent), before = Counter() };
            var raw = api.Prepare(parent, allowance, true);
            p.status = api.ResultStatus(raw); p.declaredCost = api.Get<long>(raw, "DeclaredCompleteCost"); p.charged = api.Get<long>(raw, "ChargedWork");
            p.ready = api.Get<bool>(raw, "Ready"); p.indexed = api.Get<int>(raw, "IndexedZoneCells"); p.warmup = api.Get<bool>(raw, "FootprintWarmupRequested"); p.after = Counter();
            scene.preparations.Add(p); Capture("preparation", scene.id, p);
            bool unsupported = scene.unknownWorker && parent.GetType() == api.AsfParentType;
            bool expectedReady = allowance != 0 && !unsupported;
            string reason = allowance == 0 ? "ProviderInitializing" : unsupported ? "UnreviewedPredicate" : "None";
            Check(scene.id + "-prepare-" + parent.ThingID + "-" + stage, p.ready == expectedReady && p.status.reason == reason
                && (expectedReady ? Complete(p.status) : !p.status.usable && p.status.observation == "Deferred" && p.status.capability == (unsupported && allowance != 0 ? "Unsupported" : "Supported"))
                && p.charged == (allowance == 0 ? 0 : p.sourceRequired) && p.declaredCost == allowance && p.indexed == 0 && p.warmup,
                "Actual preparation/readiness and source-derived complete setup reservation; no independent runtime-call-count claim.");
            Check(scene.id + "-prepare-no-Matches-" + parent.ThingID + "-" + stage, p.after.matches == p.before.matches,
                "Custom type containment occurs before Matches; constructor and other virtual calls remain separately recorded.");
        }

        private void Trial(Cap03FilterScene scene, StorageGroup group, Building_Storage parent, Thing subject, string stage, Building_Storage asf)
        {
            var t = new Cap03FilterTrial { id = scene.id + "/" + stage + "/" + parent.ThingID + "/" + subject.ThingID,
                stage = stage, parentId = parent.thingIDNumber, subjectId = subject.thingIDNumber, before = Counter() };
            t.query = "CAP03-A/" + t.id; t.parcelId = t.id + "/parcel"; scene.trials.Add(t);
            Capture("trial-begin", scene.id, t); object scope = null;
            try
            {
                var limits = api.MakeLimits(8192); t.allowance = api.LimitsWork(limits);
                var opened = api.OpenBudget(map, group, t.query, limits); t.openStatus = api.ResultStatus(opened); t.openWork = api.OpenWork(opened);
                scope = api.Scope(opened); t.scopeIdentity = Id(scope); Capture("open", t.id, new Cap03BudgetOpen { status = t.openStatus, query = t.query, scopeIdentity = t.scopeIdentity, work = t.openWork });
                if (!Check(t.id + "-open", Complete(t.openStatus) && scope != null, Json.Stringify(t.openStatus))) return;
                t.afterOpen = api.ScopeUsed(scope);
                Check(t.id + "-open-accounting", Same(t.openWork, t.afterOpen) && Work(t.openWork, "GuardChecks") == result.gameMaps,
                    "Actual open charge and cumulative scope.Used captured separately.");
                var address = parent.OccupiedRect().Cells.First(); var rawCell = api.Observe(scope, address);
                t.cell = api.Cell(rawCell); t.cell.fixtureScene = scene.id; t.cell.fixtureStage = stage; t.afterCell = api.ScopeUsed(scope);
                Capture("cell", t.id, t.cell);
                Check(t.id + "-cell-accounting", Adds(t.afterOpen, t.cell.work, t.afterCell), "Cell.Work added exactly once to cumulative Used.");
                if (!Check(t.id + "-real-cell", Complete(t.cell.status) && t.cell.cell == address.ToString() && t.cell.parentKey == "building:" + parent.thingIDNumber
                    && t.cell.mapId == map.uniqueID && t.cell.tick == Find.TickManager.TicksGame && t.cell.query == t.query && t.cell.session == result.session
                    && t.cell.generation == 1 && t.cell.observationId > 0 && t.cell.stacks.Count == 0 && t.cell.itemCount == 0 && t.cell.gridEntries == 1
                    && t.cell.maximumSlots == (parent == asf ? 6 : 3) && t.cell.vacantSlots == t.cell.maximumSlots,
                    "Actual empty member slot, grid, parent, query and physical resource observation identity.")) return;
                var parcel = api.Parcel(t.parcelId, subject, actor); t.eligibility = api.EligibilityRow(api.Eligibility(scope, parcel, rawCell));
                t.eligibility.fixtureScene = scene.id; t.eligibility.fixtureStage = stage; t.afterEligibility = api.ScopeUsed(scope);
                Capture("eligibility", t.id, t.eligibility);
                Check(t.id + "-eligibility-accounting", Adds(t.afterCell, t.eligibility.work, t.afterEligibility), "Eligibility.Work equals separate actual Used delta, including defer/refusal work.");
                Check(t.id + "-cell-parcel-binding", t.eligibility.observationId == t.cell.observationId && t.eligibility.parcelId == t.parcelId,
                    "Eligibility uses the exact returned native-scope cell and actual physical parcel.");
                Grade(scene, t, parent == asf, subject.def == rawRice, subject.def == ThingDefOf.WoodLog, subject.def.stackLimit);
                t.completed = true;
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
                    Check(t.id + "-disposed", Same(before, t.afterDispose) && !api.HasOpenScope, "Native scope disposed without fabricated work or an open entry.");
                }
                t.after = Counter(); Check(t.id + "-no-custom-Matches", t.after.matches == t.before.matches, "No custom Matches invocation during this exact scope lifetime.");
                Capture("trial-result", scene.id, t);
            }
        }

        private void Grade(Cap03FilterScene scene, Cap03FilterTrial t, bool asf, bool rice, bool wood, int stackLimit)
        {
            bool deferred = t.stage == "unprepared" || (scene.unknownWorker && (asf || scene.asfFirst));
            bool effectiveRefusal = !deferred && scene.asfFirst && rice;
            bool memberRefusal = !deferred && !effectiveRefusal && asf && !wood;
            bool eligible = !deferred && !effectiveRefusal && !memberRefusal;
            var e = t.eligibility;
            var physical = scene.linked.members.Single(x => x.thing.thingId == t.parentId);
            // Native Shelf retains its own fixed special filter. Charge each actual
            // source-selected filter, even when two reads refer to the same object.
            long preflight = 3L + scene.linked.groupEffective.contents.disallowedSpecials.Count
                + scene.linked.groupFixed.contents.disallowedSpecials.Count + physical.declaredFixed.contents.disallowedSpecials.Count;
            t.expectedFilterCharge = deferred ? 0 : preflight + (eligible ? (asf ? 1L + physical.declaredFixed.contents.disallowedSpecials.Count : 0L) + 1L : 0L);
            // A shelf-first unknown group does not visit the other member's fixed filter.
            Check(t.id + "-filter-charge", Work(e.work, "Filters") == t.expectedFilterCharge,
                "Three actual preflight filter contents (including native Shelf specials), optional ASF recheck and successful HD-context charge; source precharges, not instrumented native call counts.");
            Check(t.id + "-typed-outcome", deferred ? !e.status.usable && e.status.observation == "Deferred" && e.status.capability == "Supported"
                && e.status.reason == "ProviderInitializing" && e.state == "NotEvaluated"
                : Complete(e.status) && e.state == (eligible ? "Eligible" : "Refused"), "Explicit expected readiness/actual refusal, independently distinguished from aggregate success.");
            Check(t.id + "-quantity", e.topUps.Count == 0 && e.vacantEligible == eligible && e.unitsPerNewStack == (eligible ? (int?)stackLimit : null),
                "No phantom stack target; only supported actual vacancies have a real native stack unit limit.");
            var names = new[] { "destination-enabled", "destination-faction", "selected-priority", "effective-thing-filter", "concrete-fixed-filter",
                "asf-declared-fixed-filter", "asf-actual-member-capacity", "native-IsGoodStoreCell", "hd-explicit-context-filter" };
            Check(t.id + "-predicate-catalog", e.predicates.Select(x => x.name).SequenceEqual(names) && e.predicates.All(x => x.targetId == null), "Exact native production predicate order, no omitted, duplicate or fabricated target rows.");
            for (int i = 0; i < names.Length; i++)
            {
                string state = "NotEvaluated", reason = "None";
                if (i < 3) state = "Eligible";
                else if (!deferred)
                {
                    if (i == 3) { state = effectiveRefusal ? "Refused" : "Eligible"; reason = effectiveRefusal ? "ThingFilterRefused" : "None"; }
                    else if (!effectiveRefusal && i == 4) { state = memberRefusal ? "Refused" : "Eligible"; reason = memberRefusal ? "MemberFixedFilterRefused" : "None"; }
                    else if (eligible && (i >= 7 || asf)) state = "Eligible";
                }
                Check(t.id + "-predicate-" + names[i], e.predicates.Count(x => x.name == names[i] && x.state == state && x.reason == reason && x.targetId == null) == 1,
                    "Expected source-selected filter/predicate boundary, including later NotEvaluated rows.");
            }
        }
        private void Native(Cap03FilterScene scene, string route, Building_Storage parent, Thing subject, bool expected, Func<bool> invoke, bool visitsCustom)
        {
            var n = new Cap03FilterNative { route = route, parentId = parent.thingIDNumber, subjectId = subject.thingIDNumber, expected = expected, before = Counter() };
            n.actual = invoke(); n.after = Counter(); scene.nativeControls.Add(n); Capture("native-control", scene.id, n);
            Check(scene.id + "-native-" + route + "-" + parent.ThingID + "-" + subject.ThingID, n.actual == expected
                && n.after.matches - n.before.matches == (visitsCustom ? 1 : 0),
                "Explicit native control outside protected interval; exact custom Matches delta is separate from worker construction.");
        }
        private static long Work(List<ProjectionFixtureWork> rows, string kind) => rows.Single(x => x.kind == kind).value;
        private static bool Adds(List<ProjectionFixtureWork> before, List<ProjectionFixtureWork> operation, List<ProjectionFixtureWork> after) =>
            before.Count == 14 && operation.Count == 14 && after.Count == 14 && before.Select(x => x.kind).Distinct().Count() == 14
            && before.All(x => Work(after, x.kind) == checked(x.value + Work(operation, x.kind)));
    }
}
