# Haul coordination and the open report backlog

Working plan, 2026-08-09. Covers every open item on the tracker and in Steam comments as of today.

This file lives under `/docs`, which is gitignored. It is working material, not shipped content.

---

## How to use this document

Work the phases in order. Phase 1 and Phase 2 are the load-bearing ones and everything else is smaller; do not
start the small items first because they feel achievable. Each phase names its own deep investigation, and that
investigation gates the implementation. If an investigation contradicts this plan, the investigation wins and this
file gets corrected.

---

## Standing rules for every phase

These apply to all work below without restating them per item.

**Branch and PR.** Stay on `fix/issue-243-bench-gather` and keep updating **PR #244**. Do not open new PRs and do
not add PR comments; fold every revision into the description. If #244 grows past the point where a human can
review it, say so and propose a split rather than silently continuing.

**Do not fix other mods' bugs.** Where a fault belongs to vanilla or another mod, Hauler's Dream stops *provoking*
it, contains it so the colony keeps running, and reports it honestly. It never patches, mutates or papers over
foreign state. If the honest answer to a report is "that is Common Sense's feature" or "that is vanilla", say so in
the UI and in the reply, and fix only our side.

**Attribution is a claim, not a hint.** #235 is on this list precisely because we named another mod from evidence
that could not support it. Before any code or copy names a mod, the evidence must distinguish that mod from *us*.
Harmony's `- PREFIX/POSTFIX/TRANSPILER` lines under a stack frame list who **patches** the method, not who threw.

**Translation keys.** Never avoid a key to dodge translation work. New user-visible strings get keys in
`Languages/English/Keyed/` **and all 15 other locales**, properly translated, matching each file's terminology.
Reworded English strings must be updated in every locale by hand: `check-translations.ts` compares key sets and
placeholder multisets, never value freshness, so a stale translation stays green.

**Refactors are in scope, at any size.** Two of the three items below have been patched three times each and still
reproduce. That is the signature of a missing abstraction, not a missing guard. Prefer changing the shared thing
over adding a fourth per-path clamp. A large, well-tested refactor that makes the bug inexpressible beats another
narrow fix that passes review.

**Agent team per item.**

| Role | Job |
| --- | --- |
| Relevance | Find every file, path and prior PR the item touches. Read the reporter's attached logs before anything else. |
| Investigator / planner | Root cause with `file:line` evidence and decompiled vanilla. Prescribes the mechanism. |
| Implementer | Builds it. Never runs the build or tests; the orchestrator is the sole builder. |
| QA | Fresh context, did not write the code. Feeds findings back to the planner, who re-plans. Loop until clean. |

**Recurrence check is mandatory, per item.** Before designing anything, answer in writing: has this been "fixed"
before? Search closed issues, merged PR descriptions and `CHANGELOG.md` for the same symptom. If it has, the
investigation's first duty is to explain **why the previous fix did not hold**, and the design must make that class
of failure impossible rather than patching the newest instance. A fix that cannot explain its predecessor's failure
is not ready.

**Never launch the game.** The user performs all in-game verification. Never run `-quicktest` or any smoke test.

**Verify claims yourself.** Do not trust an agent's report of a build, a test count, or a vanilla behaviour. Check
the committed bytes, not the working tree.

---

## The meta-problem, stated plainly

Two symptom families have now been shipped as "fixed" three times each and are still being reported.

**Family A: too many haulers commit to the same destination space.**

| Attempt | Shipped | What it did | Why it did not hold |
| --- | --- | --- | --- |
| PR #139 | 2026-07-06 | bulk-haul over-hauling | superseded within days |
| PR #116 | 2026-07-03 | clamped the primary pickup to destination space | per-pawn only; no cross-pawn part at all |
| PR #241 | 2026-08-02 | added in-flight subtraction (`StorageEnroute`) | reported again within a week (#248, Eversset) |

**Family B: each trip carries far less than a pack.**

| Attempt | Shipped | What it did | Why it did not hold |
| --- | --- | --- | --- |
| PR #169 | 2026-07-08 | blamed leftover cargo from a forced redirect | wrong cause; reporter's pack was empty |
| PR #241 | 2026-08-02 | fixed the fair-share divisor | fix was unreachable when carry weight is unlimited |
| PR #244 | current | substituted a real trip size for the unbounded sentinel | #247 filed against 1.19.0; needs re-test on current |

The pattern in both is identical and worth naming, because it predicts the next failure: **the rule was correct
each time and the arguments reaching it were wrong.** Every fix was verified against the rule in isolation. None
was verified against the set of callers. So each fix was genuinely correct, genuinely tested, and genuinely
irrelevant to the player.

Three consequences shape this plan:

1. A fix that lives in one call site is a liability. Both families need the decision to be **unavoidable** rather
   than correctly called.
2. The unit tests reference only `HaulersDream.Core`, so they can observe a rule and never the arguments the Verse
   glue passes it. This is a structural blind spot, not an oversight, and it is exactly where both families hid.
3. We have no in-game verification. PR #241 said so honestly, and Eversset quoted it back at us. Concurrency
   between many haulers is precisely what unit tests cannot reach.

---

## Phase 0: make the invisible visible

Nothing else in this plan is trustworthy until we can observe the thing we keep failing to fix. Do this first.

**0.1 A destination-commitment trace.** Add a debug-log channel (behind the existing verbose/debug switch, off by
default) that records, for every hauling job HD creates or declines: the pawn, the def, the units taken, the
destination group, the free space it computed, the in-flight units it subtracted, and which code path decided. One
line per decision. Route it through the existing `HDLog.Dbg` disk trail so a reporter's log carries it.

This is the instrument that makes #248 and Eversset's report diagnosable from an attachment instead of a guess. It
is also the thing that would have told us, a month ago, that `StorageEnroute` was consulted on one path out of
several.

**0.2 An enumeration of every path that commits cargo to a destination.** Produce a written table: entry point,
`file:line`, does it consult destination capacity, does it consult in-flight commitments, does it take a vanilla
cell reservation. Known starting points, not exhaustive: `BulkHaul` (the automatic planner), `HaulToStack`,
`EnRoutePickup`, `YieldRouter`, the unload driver's re-store, the float-menu orders, and **vanilla's own
`HaulToCell` when HD declines**. That last one matters and is easy to forget: when HD returns null the vanilla job
still runs, and it is not covered by any HD accounting.

**0.3 A concurrency harness in Core.** A deterministic simulation of N haulers, one destination with K free units,
and a pluggable decision function. Assert the invariant directly: **the sum of units committed by all pawns never
exceeds the destination's free capacity.** This is the test that both families needed and neither had. It belongs
in `HaulersDream.Core` so it can run headless.

**Exit criteria.** The table in 0.2 is complete and reviewed; 0.3 fails against today's code for the #248 scenario;
0.1 produces a readable trace for a simulated multi-hauler run.

---

## Phase 1: destination capacity, one ledger (Family A)

**Covers:** #114 (reopened in effect), #248, Eversset's Steam report, and the "moving things back and forth" half
of Richard Ramirez's report.

### What Phase 0.2 established (2026-08-09, verified against decompiled vanilla)

The enumeration is done and it changes this phase materially. Findings, in order of importance:

**1. The mechanism, finally.** HD strips vanilla's destination reservation for stackables
(`HaulToStack.cs:68-93`, `Patch_JobDriver_HaulToCell_NoCellReservation`, default ON). That reservation does
**double duty** in vanilla, and the second duty is the one everyone missed: it does not only hide the cell, it
**shrinks every other pawn's `job.count`**. `HaulAIUtility.HaulToCellStorageJob` sums `GetItemStackSpaceLeftFor`
across the storage group *only over cells that pass `IsGoodStoreCell`*, then clamps `job.count` to that sum.
Reserved cells drop out of the sum. Remove the reservation and every concurrent hauler prices the same free cells
into its own count. **That is the reported bug in one sentence**, and it is why three fixes aimed at HD's planner
never landed: the over-commitment is happening in vanilla's arithmetic, not ours.

**2. This degrades vanilla's own hauls, not just HD's.** The stripping is global for stackable
`ToCellStorage` hauls, so the plain `HaulToCell` job HD leaves standing whenever its planner declines is also
mispriced. Any fix must repair that too, or HD keeps making the base game worse than it is without us.

**3. `HaulToStack.cs:44-65` is the amplifier.** It actively steers haulers onto the same partial-stack cell, with
no unit arithmetic at all.

**4. Vanilla already ships the ledger we want, and we cannot reuse it as-is.** `Verse.AI.EnrouteManager` is a
per-`(IHaulEnroute, pawn, def)` unit ledger, and vanilla uses it *precisely* as the substitute for cell
exclusivity in `JobDriver_HaulToContainer`. Two blockers: slot groups are not `IHaulEnroute`, and
`Pawn.ClearReservationsForJob` calls `enrouteManager.ReleaseAllClaimedBy(pawn)` **pawn-wide**, wiping every claim
at every job end.

**5. Therefore a destination claim must be keyed on pawn + cargo, never on Job.** HD's haul spans two jobs
(`JobDriver_BulkHaul` pickup, then `JobDriver_UnloadHauledInventory` deposit) and job 1's reservations die at its
end. `CompHauledToInventory`'s tag set is the existing precedent for pawn+cargo keying.

**6. `StorageEnroute` is narrower than "one call site" suggests.** Within that one plan, only the *primary* stack
consults it; swept extras price against a `[ThreadStatic]` scratch dictionary cleared on the next build and
invisible to every other pawn.

**7. Currencies disagree** across the codebase: items-per-def, kilograms, cells, CE bulk. The seam needs one.

**8. Zero test or guard coverage** exists for `Patch_JobDriver_HaulToCell_NoCellReservation`, and its
unstackable carve-out is duplicated at four sites with no shared helper (`HaulToStack.cs:59`, `:87`,
`JobDriver_UnloadHauledInventory.cs:374`, `:481`). Those have already drifted apart once, as issue #162.

### The design candidate

Because the over-commitment happens inside vanilla's own group-sum, the cheapest correct fix may not be a new
ledger at all: **postfix the vanilla "how much room is left here for this def" function so HD subtracts its own
in-flight commitments.** If `GetItemStackSpaceLeftFor` is consulted by vanilla's `HaulToCellStorageJob`, HD's
`ScanGroup`, and `HaulToStack`'s partial-stack search alike, then every path shrinks automatically and no future
path can forget. A design agent is evaluating exactly this, including the exclude-self problem, hot-loop cost, and
whether non-haul callers would be wrongly affected. If it does not hold, the fallback is an HD-owned pawn+cargo
ledger.

### Deep investigation

1. Confirm or refute the Haul to Stack hypothesis with the 0.1 trace and by reading `HaulToStack.cs` against the
   0.2 table. Does the stacking path register anything at the destination? Does it suppress the vanilla
   reservation, and if so where?
2. Decompile `ReservationManager`, `StoreUtility.IsGoodStoreCell`, `HaulAIUtility.HaulToCellStorageJob` and
   `Verse.AI.EnrouteManager` / `IHaulEnroute`. Vanilla's `EnrouteManager` is a genuine quantitative destination
   claim, but it is `Thing`-keyed and wired only into `JobDriver_HaulToContainer`. Decide whether to mirror it at
   slot-group granularity or to adopt it where the destination is a container.
3. Establish what vanilla itself does wrong here, so we do not chase it. Vanilla over-hauls too when free space is
   spread across many cells. **That is vanilla's, and out of scope.** Our obligation is to stop amplifying it.
4. Determine whether a ledger must be persisted or can be derived live each tick. Persisted state spans two jobs
   (pickup then deposit), so a claim cannot be released on job end, which is the phantom-claim class of bug that
   `ValidateLoadLedgerAfterLoad` already had to be written to repair. Derived-live self-heals on death, drafting,
   cancellation and save/load. Prefer derived unless the investigation proves it too costly.

### The refactor

Design toward a single seam every path must pass through to commit cargo to a destination. Something with the
shape of "reserve N units of def D at destination G for pawn P", returning what was actually granted. The point is
not the API; the point is that **no path can commit without going through it**, so a future path cannot forget.

Then delete the per-path clamps that the seam subsumes, rather than leaving them as a second source of truth.

Guard it: a build guard in the `scripts/check-*.ts` family asserting that every job-creation site that targets
storage routes through the seam. This is the same instrument that caught the plan-craft bypass, and it is the only
thing that can see what the Core tests structurally cannot.

### Finding from 0.3: freshness is part of the seam, not a footnote

The harness graded the **shipped** rule, `DestinationEnroutePolicy.FreeAfterEnroute`, over 864 runs (crews 1..8 ×
12 capacities × 3 appetites). Under *immediate* visibility of other pawns' commitments it is **perfect**:
invariant held, nothing carried back, no empty trips, and it never over-corrects into "nobody hauls".

Under **tick-snapshot** visibility it breaks. And tick-snapshot is what `StorageEnroute` actually provides: the
memo is taken at the tick's first query and invalidated only by the clock (`StorageEnroute.cs:79`). Two haulers
planning in the **same tick** each commit the same 10 units into 10 units of room.

**So the rule we shipped for #114 was correct and its visibility was not** — a fourth distinct root cause, and the
reason the fix did not hold even on the single path it covered. Our own changelog calls the residue "one extra
trip"; the harness shows it is **unbounded in crew size**.

Any Phase 1 design must therefore answer: is a commitment registered at tick T visible to a pawn planning later in
tick T? A per-tick memo anywhere behind the seam reintroduces this exact bug.

Two more gaps the harness named, both to be closed by the design rather than assumed away:

- **Unknown capacity is a real state.** `StorageGroupBudget.AvailableFor` returns `int.MaxValue` for an unpriced
  group and `BulkHaul.cs:859` then skips the clamp entirely. Define what the invariant means there; "skip the
  clamp" is what shipped and it is a hole.
- **Cross-def contention is unmodelled.** One storage group's budget is shared across defs (#138), and `Consume`
  runs for the primary and the swept extras. The currency must cover two defs contending for one shelf, not only
  two pawns contending for one def.

### Exit criteria

The 0.3 concurrency harness passes for N haulers against K free units across every path in the 0.2 table,
**including under tick-snapshot visibility**. `Issue248_FiveHaulersForThreeUnitsOfRoom_OverCommitUnderTodaysRule`
is currently `[Ignore]`d so it cannot destroy the green signal other phases depend on; **removing that attribute
and watching it pass is part of this phase's definition of done**, and the assertion must not be weakened to get
there. The build guard fails when a new commit site skips the seam. The 0.1 trace shows a second pawn declining
because the first pawn's commitment was visible.

---

## Phase 2: per-trip quantity (Family B)

**Covers:** #247, SleepingPigNeverSleep's Steam report, kousaka4656's SRTS report.

### What we already know

The per-trip count is `min(stackCount, claimable, carryAffordable, massAffordable)`, and `massAffordable` is
`floor(massLeft / unitMass)` where `massLeft` starts at the trip budget and is then clamped by a fair share. The
clamp is applied as a `min`, so **it can only ever make a trip smaller, never reduce the number of trips.**

Three reports describe the same curve from different angles:

- **#247**: 75 units, one hauler mech, trips of 18, 14, 10. Decaying, and the pack is not full.
- **SleepingPigNeverSleep**: "take lots of item (seems maximum) at first, and then take only one item if there are
  only few left. Maybe some calculation is portional?" That is a precise description of a proportional share with
  a one-unit floor. The reporter has effectively diagnosed it.
- **kousaka4656 (SRTS)**: only a single pawn loads at all, with 5000-per-stack steel. Different symptom, likely
  the same subsystem: either the fair share starves the others, or the claim ledger hands the whole manifest to
  the first asker.

### Deep investigation

1. Re-test #247 against the current build first. It was filed on **1.19.0** and two fixes have shipped since. If
   it no longer reproduces, say so and ask the reporter to confirm rather than refactoring speculatively. Do not
   skip this step; it is the cheapest possible outcome.
2. Trace SRTS and Defensive Network specifically. Both add their own shuttle/spaceship types. Determine which
   `IManagedLoadable` adapter they land on, or whether they land on none and fall through to a path with no
   coordination. Read their assemblies; do not infer from the mod description.
3. Settle the "only one pawn loads" question for SRTS. Is it the fair share, the ledger's `FullyClaimed` gate
   short-circuiting other pawns, or a work-giver eligibility issue with a modded building? These have different
   fixes and the symptom does not distinguish them.
4. Examine the no-starvation floor. It exists so no item becomes unclaimable, and its cost is exactly the "one
   item at a time" tail every reporter describes. Decide whether a floor of one *unit* should instead be a floor of
   one *meaningful load*, or whether the floor should apply only when the pool cannot be cleared this trip.

### The refactor

The recurring failure here is that the fair-share rule keeps receiving arguments it cannot use. Consider inverting
the responsibility: rather than the planner computing a budget and the rule clamping it, have the rule own the
whole "how much should this pawn take" decision with every input passed explicitly and totally, so there is no
sentinel to mis-handle and no caller left to get it wrong.

Whatever shape it takes, the multi-trip property must be pinned: **for any pool, crew size and capacity, the sum
of trips equals the pool and no trip carries less than the smaller of capacity and remainder, unless a genuine
crew split explains it.** Extend the existing multi-trip oracles across the axes they currently miss.

### Exit criteria

The multi-trip oracle covers unbounded and bounded budgets crossed with divisors 1..N. #247's exact numbers are
either reproduced and fixed, or shown not to reproduce on the current build with a request for confirmation.

---

## Phase 3: #235, and the attribution that caused it

Two separate deliverables. Do not conflate them.

**3.1 Stop naming mods we cannot place.** The quarantine alert names "the mod responsible", chosen by logic that
skips our own Harmony ID (`WorkGiverBlocklist.cs:286`). With HD excluded, the only other patcher on the frame gets
named. In #235 that was Smarter Construction, and HD has **two postfixes and a finalizer** on the very method that
threw. We would have printed SC's name even if our own postfix threw.

Change the evidence bar so a mod is named only when it can be distinguished from us. When it cannot, say the work
type was disabled and the source is unknown, and point at the log. This needs new/reworded translation keys in all
16 locales. Correct `CHANGELOG.md:85` too; the false claim still ships to Steam from there even though the GitHub
release notes were rewritten.

**3.2 Find what actually throws.** A `NullReferenceException` inside `JobGiver_Work.TryIssueJobPackage`, with no
line number because it is the patched dynamic method. The trace cannot separate vanilla, SC's transpiled IL and
our two postfixes. Start by reproducing with HD's postfixes on that method disabled, since that is the one variable
we control. SC's transpiler redirects `GenClosest.ClosestThing_Global_Reachable` to its own implementation, so a
null from there flowing into unguarded vanilla code is a plausible mechanism that involves no SC crash at all,
which is consistent with dhultgren being unable to reproduce a crash. Treat that as a hypothesis to test, not a
finding to publish. Do not name anyone in public until the evidence distinguishes them.

---

## Phase 4: #243, the Common Sense cede

Lensrub confirmed the remaining half on 2026-08-03: with Common Sense's own gathering turned off, the per-bench
button correctly stops gathering, **but nothing gathers when it is turned on either.**

This is the item flagged during PR #244 as needing a decision, now confirmed in the field. `OwnsDoBillFlow` cedes
the whole DoBill gather flow when *either* CS's cleaning option or its haul-all option is on. With cleaning on and
haul-all off, CS owns the driver but gathers nothing, and HD stays ceded, so nobody gathers.

**Investigation.** Read CS's `DoMakeToils` else-branch and confirm it yields vanilla's
`CollectIngredientsToils` when haul-all is off. Then determine whether HD's gather conversion, which happens at
the work-giver level rather than the toil level, can coexist with CS's `MakeNewToils` prefix returning false.
That coexistence is the actual question and it cannot be answered from the settings alone.

**Constraint.** This changes behaviour for every Common Sense user, and it needs in-game verification the user
must perform. Ship it behind a clear decision, not as a silent widening.

Also close the documented known gap: with cleaning on and haul-all off, nobody gathers and no notice appears, so
the bench description still overclaims.

---

## Phase 5: storage limit and sorting mods

**Covers:** Richard Ramirez's list, and it overlaps Phase 1 more than it first appears.

Mods named: Stack Gap, Stockpile Stack Limit (Continued), Variety Matters Stockpile (Continued), Storage Limits,
Storage Sorting, KanbanStockpileContinue.

His actual goal is stated plainly and is worth keeping in view: *"All I wanna do is spread out a limited and evenly
distributed amount of items."* And his symptom, *"sometimes I just can't get pawns to put the right amount of stuff
in the right place, sometimes wasting time moving things back and forth"*, is the Family A symptom. Phase 1 may fix
a good part of this on its own.

**Investigation.** For each mod, find where it imposes its cap and whether that cap is visible through the seam HD
already consults. `COMPATIBILITY-MAP.md` records that HD's space scan reads through
`NoStorageBlockersIn` / `GetItemStackSpaceLeftFor` and notes storage mods whose caps sit off that seam as a
deliberate safe upper bound. Verify per mod, with the assembly, not the description. KanbanStockpileContinue is
already noted in the map; re-check it against the current version.

Where a cap is invisible to us, HD over-estimates free space, which is exactly the direction that causes the
back-and-forth. Decide per mod: read their cap through a narrow reflection shim, or stand down and let vanilla
haul. **Do not reimplement their feature and do not write to their state.**

Also note: he reports Stack Gap making pre-existing stacks disappear when he lowers the limit. That is Stack Gap's
own bug. Say so plainly and do not work around it.

---

## Phase 6: Survival Tools Reborn

A support request, not a bug. Treat it as a compatibility assessment first and a feature second.

**Investigation.** Read the mod. Determine whether it is a tool-carrying mod in the family HD already handles
(Simple Sidearms, Grab Your Tool), in which case the work is teaching HD's keep-item detection to recognise its
carried tools so HD never hauls away a tool a pawn is meant to keep. Check whether it exposes a stable API or
whether reflection is required, and what happens when it is absent.

Deliver an answer either way. If it already works, say so with evidence. If it needs a shim, size it. If it is not
worth it, say that plainly to the requester.

---

## Phase 7: #250, forbidding an item mid-haul does not stop the walk

**This one is a safety bug, not an efficiency bug.** Treat it accordingly.

The Grand Mugwump, on the current build (1.23.0.0):

> When I forbid an item while a pawn is doing the "haul everything nearby" job that this mod adds, that pawn will
> still move to the forbidden item's location before deciding to do something else. This can lead to unfortunate
> outcomes if I forbid items that are unsafe.

And the follow-up, which states the bar exactly: *"In vanilla, pawns will immediately cancel a hauling job if their
target is forbidden and find something else to do. I'd like something similar."*

Forbidding is what a player reaches for when a spot has become dangerous. A pawn that keeps walking there is
worse than a pawn that hauls inefficiently, so this outranks its apparent size.

### What we already know

`JobDriver_BulkHaul.cs:213` does test `t.IsForbidden(pawn)` and skips to the next queued item
(`loadIndex++; JumpToToil(loadDecide)`), and `:133` tests it again while planning. Neither helps the reporter,
because both happen **at the item**. Nothing re-checks the current target while the pawn is walking to it, so the
decision to abandon it is taken only on arrival, which is precisely the arrival the player was trying to prevent.

### Deep investigation

1. Decompile vanilla's `Toils_Haul`, `JobDriver.AddFailCondition` and `FailOnForbidden` and establish exactly
   when vanilla notices. The reporter says "immediately"; confirm whether that is per-tick during the goto or on
   the next job-driver check, so we match real vanilla rather than the reporter's impression of it.
2. Decide the failure shape, and be careful here: a bulk haul carries a **queue**. Failing the whole job the way
   vanilla fails a single-target haul would strand everything already collected, which is the exact class of bug
   this mod exists to avoid. The right behaviour is almost certainly to drop the *current* target and jump to the
   next queued one, keeping the rest of the load intact, and to end cleanly if the queue empties.
3. Check the same gap across the sibling drivers, not just this one. Self-pickup, the corpse sweep, bulk load and
   bulk refuel all walk to a target chosen earlier. Per this plan's own thesis, fix the seam rather than the
   instance: if there is a shared "walk to the next queued target" toil, the re-check belongs there once.
4. Look at how this interacts with the existing pather-failure handling, which already skips a blocked stack and
   continues. That is the same shape of problem with the same shape of answer, so it may be the right place to
   put the check, or the right code to copy.

### Constraints

Do not make a forbidden target fail the whole job. Do not drop collected cargo on the ground to react quickly.
An explicit player-forced order already carves itself out at `:213` and should keep doing so.

### What the investigation established (2026-08-09)

**Worse than first read.** `JobDriver_BulkHaul` has **no job-level fail conditions at all**. Between `StartPath`
and `PatherArrival` the driver runs no code whatsoever, and with the pickup pause on the pawn then stands at the
item for up to 240 more ticks before skipping. To a player that reads as deliberate.

**Vanilla's mechanism, decompiled.** A global fail condition evaluated at the top of every `DriverTick`, so the
latency is one tick. But note the carve-out: `Pawn_JobTracker.StartJob` sets `job.ignoreForbidden = true` whenever
`pawn.Drafted || job.playerForced`, and `FailOnForbidden` short-circuits on it. **Vanilla does not abandon a
forced haul.** HD's existing `loadIndex == 0 && job.playerForced` exemption is therefore vanilla-exact and stays.
Consequence to state in the PR: forbidding the *anchor* of a forced sweep still walks there; every other item in
that sweep now abandons.

**Two drivers are worse than the reported bug.** `JobDriver_BillPrepGather.cs:111-119` and
`JobDriver_BatchCraft.cs:477-485` walk to a stack **and pocket it** with no arrival re-check at all, because their
takes are vanilla `Toils_Haul.TakeToInventory` whose count getters never re-test forbidden. Neither was reported.
They are in scope.

**Six more drivers share the reported defect**, including `JobDriver_LoadInBulkBase`, which is the base for the
transporter, portal and vehicle bulk loads. Fixing the base covers three drivers at once. This is exactly the
"fixed one instance, left the siblings" pattern this plan exists to stop, so the fix is a shared seam
(`SweepForbidPolicy` in Core plus a `SweepWalk` toil factory) rather than eight edits.

**Hook choice matters.** Use `Toil.AddPreTickAction`, not `tickAction`: `DriverTick` re-tests
`JobChanged() || CurToil != curToil || wantBeginNextToil` after each pre-tick action, so a `JumpToToil` from
inside one is re-entrancy-safe, whereas `tickAction` only re-tests `JobChanged()`, which stays false for a
same-job jump.

**No new strings.** Vanilla is silent when a haul fails on forbidden, and a message here would fire once per
forbidden stack in a sweep.

### Exit criteria

A headless test over the shared policy, a build guard proving no driver can hand-roll a sweep walk that bypasses
the seam (the guard is what makes this stick across nine drivers, not the unit tests), and the four in-game
scenarios A–D written for the user, including the forced-anchor case so it is not mistaken for a miss.

---

## Cross-cutting: closing the verification gap

Eversset quoted our own disclaimer back at us, and was right to. Everything below Phase 0 is still headless.

- **The user runs the game, not us.** So the deliverable is a *test the user can run in ten minutes*: a written
  scenario with a stated expected outcome, per phase. "Build a full high-priority shelf missing four units, put
  five idle haulers near a low-priority pile, watch how many set off."
- **The 0.1 trace is the bridge.** With it, a reporter's attached log answers the question we currently guess at.
  Ask the reporters here to re-run with it on. Three of them (huge, Eversset, Lensrub) have been consistently
  precise and will.
- **Guards over tests where tests structurally cannot see.** The Core suite cannot observe Verse glue arguments.
  Every invariant that lives in the glue needs a build guard, and each guard must be proven to fail by mutating
  the code it protects. A guard that has never been seen to fail is not evidence.

---

## Sequencing

```
Phase 0  ──▶ Phase 1 ──▶ Phase 5 (partly resolved by 1)
         └─▶ Phase 2
Phase 3  (independent, start any time)
Phase 4  (independent, needs a user decision before shipping)
Phase 6  (independent, smallest)
Phase 7  (independent, safety — promote it above the efficiency work)
```

Phase 0 gates 1 and 2 because both have already been fixed three times without it. Phase 7 is small but it is the
only item where the current behaviour can get a colonist hurt, so it should not queue behind the refactors. If its
investigation shows the fix is contained, ship it early on its own.

## Definition of done, per item

1. The recurrence question is answered in writing, including why the previous fix did not hold.
2. Root cause is evidenced with `file:line` and decompiled vanilla, not inferred from a symptom.
3. The fix lives at a seam that cannot be bypassed, with a build guard proving it.
4. A headless test fails before and passes after, and it covers the *arguments*, not only the rule.
5. A ten-minute in-game scenario is written for the user, with the expected outcome stated.
6. Translation keys exist in all 16 locales, and any reworded string was updated in all 16 by hand.
7. `PR #244`'s description is updated. No PR comments.
8. Nothing in the change patches, mutates or blames another mod without evidence that distinguishes it from us.
