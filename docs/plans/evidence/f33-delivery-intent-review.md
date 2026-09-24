# F33 / GH259 — delivery versus construction intent

**Confirmed source-level cause; recommend a one-file correction at job creation.** No product edit, build, harness or native run was performed. GH259's retained complete body says a pawn with Build unassigned is ordered to “Deliver resources,” delivers them, then builds; the expected result is delivery followed by its other work. It reports HD 1.24.0.0 and 194 active mods. This review establishes a matching defect in the current Core/HD path, not a reproduced trace of that particular mod list.

## Exact path and evidence

The installed Core `Defs/WorkGiverDefs/WorkGivers.xml` defines the **same scanner classes under different work types**:

| WorkGiverDef | Lines | Work type / intent |
|---|---:|---|
| ConstructDeliverResourcesToFrames / ConstructDeliverResourcesToBlueprints | 705–734 | Construction; work on the site |
| DeliverResourcesToFrames / DeliverResourcesToBlueprints | 1378–1409 | Hauling; deliver to the site |

Actual native `FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption` calls the scanner with `forced:true` **before** its priority gate (export lines 123, 149). A capable pawn with Construction priority 0 can therefore receive the Hauling option while the Construction option is disabled. The enabled option assigns its real `workGiverDef` only later (line 174), then calls `TryTakeOrderedJobPrioritizedWork`. Consequently reading `__result.workGiverDef` inside the delivery postfix is too early; the scanner `__instance.def.workType` is the available authoritative distinction.

Current product boundaries:

- `Source/HaulersDream/InventoryConstructDelivery.cs:31–57`: the postfix receives `forced` but discards scanner identity. Lines 51–54 treat every forced delivery as haul-and-build when the global tether is enabled and no route says HaulOnly. Both native work types therefore choose the same tether.
- The same file, lines 302–305, selects `HaulersDream_ConstructDeliverBuild` versus `HaulersDream_OverloadConstructDeliver`. The existing JobDef already carries this decision through queueing and serialization.
- `Source/HaulersDream/JobDriver_OverloadConstructDeliver.cs:182–201`: the finish action of a forced ConstructDeliverBuild calls `ConstructTether.QueueNext`.
- `Source/HaulersDream/ConstructTether.cs:25–49`: a completed frame becomes a forced native FinishFrame job. `WorkCapabilityProbe.IsDisabled` checks inability, **not assignment**; `GenConstruct.CanConstruct(...checkSkills:true, forced:true)` checks genuine construction eligibility but does not restore the missing delivery-only intent. A capable unassigned pawn passes these checks.

This is not native prioritized work unexpectedly switching work types. Actual `JobGiver_Work` lines 55–76 restrict its priority continuation to the selected giver's **work type** before checking related givers. Hauling priority work does not scan Construction's FinishFrame giver. HD's explicit queued tether crosses that boundary.

Read-only native exports are retained at `%TEMP%/haulersdream-f33-investigation-20260920`: the two delivery scanner subclasses, base delivery scanner, FloatMenuOptionProvider_WorkGivers, GenConstruct, JobGiver_Work and Pawn_JobTracker. They were decompiled from installed Assembly-CSharp SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`; no target methods ran.

## Smallest complete correction

In `Patch_ResourceDeliverJobFor_Inventory.Postfix`, accept `WorkGiver_ConstructDeliverResources __instance` and replace only the tether decision/comment:

```csharp
var intent = InventoryConstructDelivery.RouteIntent;
bool tether = forced &&
    (intent == ConstructRouteIntent.HaulBuild ||
     (intent == ConstructRouteIntent.None
      && __instance?.def?.workType == WorkTypeDefOf.Construction
      && s.orderedConstructTether));
```

This preserves explicit route intent first: HaulOnly never builds; HaulBuild still means build even with the global plain-order tether disabled. Without route intent, only the Construction work giver can request the configured tether. Hauling remains a forced delivery (same pickup, capacity, enroute and material handling), but gets the nonbuilding JobDef. Unknown/nonconstruction work types do not gain implicit construction permission.

**Do not add a blanket Construction-priority check to QueueNext.** Explicit planned HaulBuild orders may intentionally override an unassigned work type when `planForUnassignedWork` permits the planner. `RouteExecutor.cs:102–149` already carries that explicit choice; lines 245–268 preserve capability/skill checks for ready frames. Native manual Construction options retain their own assignment gate, while programmatic forced construction and explicitly requested routes retain their intended forced semantics. Keep incapable/under-skilled guards in place; assignment, capability and requested action are three different facts.

HD's separate “Prioritize hauling materials to…” provider is already delivery-only: `FloatMenuOptionProvider_HaulToSite.cs:56–70` calls `InventoryConstructDelivery.TryBuildHaulOnlyOrder`, whose lines 387–394 pass `tetherBuild:false`. Do not rewrite that unrelated working entry point. The new predicate also leaves autonomous deliveries untethered and leaves vanilla hand delivery/shared-inventory fallbacks unchanged.

## Focused regression evidence

Use one small isolated native scenario with four owned sites, ordinary Core materials and a capable pawn. Choose a single site/available load that actually converts to the HD inventory job; record the selected WorkGiverDef and resulting JobDef rather than assuming conversion from the recipe name. No construction-specific runnable fixture was found in the current `tools/RuntimeHarness` or durable evidence sources. Reuse the existing `docs/plans/evidence/f34-live-20260920` initialized-map Begin/Update/Dispose host pattern and private controller; its productive native-job observation/finite cleanup pattern is suitable. `f45-menu-20260920` supplies the actual Layout-bound menu-delegate pattern if capturing the offered native choice. Existing `ConstructDeliveryPlanTests` cover load/enroute math, not this command-intent defect; they cannot substitute for the native check.

| Scene | Required discriminating observation |
|---|---|
| Hauling order, Build unassigned | Capture the actual native Hauling option and dispatch its offered job. Baseline selects ConstructDeliverBuild and queues/starts FinishFrame. Candidate selects OverloadConstructDeliver, actually deposits the requested material, releases its claims, never queues/starts FinishFrame and leaves frame work unchanged; observe bounded ordinary follow-up/other assigned work. Include the blueprint-to-frame transition. |
| Construction order, Build assigned | The native Construction giver with tether on still delivers and completes real FinishFrame work. This proves the repair did not globally disable tethering. |
| Explicit HaulBuild, capable but Build unassigned | Use the actual route path with unassigned planning allowed and global tether off. The explicit choice must still deliver and build with existing skill/capability gates. |
| Explicit HaulOnly | Use the route with Build assigned and tether on. It must not enqueue a construction continuation. Distinguish any later independently scheduled normal Construction job from a forbidden forced tether; capture the queue/job provenance at the delivery boundary. |

Within those setups, query both actual blueprint/frame Hauling giver identities and toggle `planForUnassignedWork` for the native delivery selection; it must not affect the delivery-only classification. A direct HaulToSite job-shape check can preserve its already explicit false tether without another full scene. Record exact material movement, native job IDs and frame work/queue transitions; do not manufacture a pass by clearing a queued FinishFrame or disabling the pawn after delivery. No broad construction, mod, route-efficiency or save-age matrix is needed for GH259.

Historical ledger L16 links #176/PR179 and #229/PR241 to related capability/permission work. Those distinguish incapable from unassigned pawns; they do not establish a previous fix for this forced-Hauling-to-build conversion. Classify this as a related authorization mistake, **not proven recurrence of an exact previously fixed GH259 defect**. F33 stays open until the minimal correction and actual baseline/candidate behavior are reviewed.
