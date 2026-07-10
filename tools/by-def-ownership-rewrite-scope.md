# By-def ownership rewrite — scoping document

Status: **decision-ready scope, not yet greenlit.** Produced from a deep root-cause round (the recurring
drop/strand/mis-load family: #62, #81, #87, the load-side merge-survivor miss, the surplus/alert under-reports).

## Verdict

**Proceed, with caveats. Greenlight as a staged, hybrid model — not a big-bang rewrite, and not a pure
`Dictionary<ThingDef,int>`.** It is worth doing, but it is debt-reduction (the bug class is currently **held,
not bleeding**), it is **large** (multi-session), and it fixes only one of the two recurrence roots. Confirm
the priority below before committing the effort.

## The root cause this fixes

HD records ownership of scooped cargo as a set of **live `Thing` references** (`CompHauledToInventory.takenToInventory`,
a `HashSet<Thing>` scribed `LookMode.Reference`). RimWorld merges, splits, and destroys stacks constantly, so a
stored reference is wrong the moment a merge happens. HD papers over this with a per-tick self-heal (`GetHashSet`,
which reconstructs ownership **by def**), while the raw `PeekHashSet` is the un-healed view. Every consumer that
reads the un-healed view, or trusts a by-Thing tag across a merge, re-opens the bug at a new seam. That is the
single fault behind every recurrence, including the load-side miss fixed this round.

The fix: make ownership a **by-def quantity** that is invariant across merge/split/destroy, and make the
`HashSet<Thing>` a derived per-tick view rather than the source of truth.

## What it does NOT fix (read this before greenlighting)

This fixes **root cause 1** (ownership *representation* — losing track of *which* cargo). It does **nothing** for
**root cause 2** (the ~8 ungoverned unload triggers and load gates — *when* a pawn scoops/unloads/loads, the
#84 ping-pong / over-eager / never-unload family). Those are orthogonal. **If player pain is mostly about timing
rather than lost-track-of cargo, address the triggers first and defer this.**

And the headline drop bug is currently **held** by the build guard (`scripts/check-drop-protection.ts` fails the
build if any drop guard or load gate reads the un-healed view) plus the per-tick heal. So this is robustness /
debt reduction that can be **scheduled**, not an active fire.

## The model (hybrid, partitioned by stackLimit)

A pure `Dictionary<ThingDef,int>` is **impossible**: HD deliberately scoops `stackLimit == 1` quality/HP/taint
items and tags them by *specific instance* (YieldRouter.cs:574 already branches on this; its comment notes a
by-def relink "would ship the pawn's own 99%-quality sidearm to storage and keep a hauled 3% one"), and Simple
Sidearms `IsRememberedSidearm` is a precise per-`Thing` match. So:

- **`Dictionary<ThingDef,int> owedToStorage`** — units of each `stackLimit > 1` def HD scooped and still owes to
  storage. These are the merge-fragile goods that *are* the bug class. Scribe `LookMode.Def, LookMode.Value`
  (in-repo precedent: `LoadLedgerEntry` already does exactly this, rebuilding in `PostLoadInit`). Strictly more
  robust than today's `LookMode.Reference`, which silently drops unresolved refs.
- **`HashSet<Thing> taggedNonStackable`** — retains the existing by-Thing semantics verbatim for `stackLimit == 1`
  (weapons / apparel / chunks; references are stable because they never merge). Reuse the **old scribe label** so
  the non-stackable subset of an old save round-trips unchanged.

**Load-bearing invariant**, enforced at the single `IncrementOwed` entry: a `stackLimit > 1` def never enters
`taggedNonStackable`; a `stackLimit == 1` def never enters `owedToStorage`. This is a faithful promotion of the
branch the scoop path already has.

**Derived view** (replaces both `GetHashSet`'s heal output and `PeekHashSet`'s raw set): a read-only
`OwnedView(pawn)` = `(taggedNonStackable ∩ live inventory) ∪ (up to owedToStorage[def] units across live stacks
of each owed def, in thingIDNumber order)`. This is exactly what `TagHealPolicy.SelectStacksToTag` already
computes — promoted from ephemeral to the canonical read path. It mutates no game state on read but still drives
CE `NotifyHeld` when it (re)maps owed units onto live stacks.

New comp API: `OwedOf(def)` / `OwnsAnyOf(def)` (reads), `IncrementOwed(thing, count)` / `DecrementOwed(def,
movedCount)` (owe-up / owe-down, branching on stackLimit), `OwnedView(pawn)` (read-only), `ReconcileToInventory(pawn)`
(the heal's successor: clamp `owedToStorage[def]` to live units + re-notify CE per matching live stack).

The riskiest arithmetic (increment / decrement / reconcile / distribute-owed-across-stacks) extracts to a new
Verse-free `HaulersDream.Core.OwnedQuantityPolicy` with oracle tests — the only thing verifiable headlessly.

## Consumer migration (~44 files / ~50 sites, in three bands)

- **Band A — pure by-def reads (~24 sites):** mechanical 1:1 swaps once `OwedOf`/`OwnedView` exist. The hottest is
  `InventorySurplus.SurplusOf` (the unload driver, alert, and gizmo all funnel through it): `PeekHashSet().Contains(thing)`
  becomes `OwedOf(thing.def) > 0` for stackable / `taggedNonStackable.Contains(thing)` for weapons. Same whole-stack
  granularity as today, so no regression.
- **Band B — owe-up (~13 scoop sites):** `RegisterHauledItem(thing[, mergedCount])` becomes `IncrementOwed(thing, count)`.
  Low per-site effort; every site must be found.
- **Band C — owe-down (8 deposit/unload cores):** `Deregister(thing)` becomes `DecrementOwed(thing.def, movedCount)`.
  This is the bulk of the work and the **dominant risk** — every move must subtract the exact moved count or units
  leak (over-owe → re-scoop loop) or vanish (under-owe → black hole). The reconcile clamp self-corrects the safe
  directions; a missed decrement on a partial move is the one dangerous case.

Three genuinely-fragile hand-rolled reconciliations **delete** as a result: the carry-over / `BuildScoopedUnion`
merge-survivor mechanism, the unload merge-survivor relink, and (partially) the BulkHaul absorber-find fold.

## Phased plan (each step build / test / QA gated; in-game pass between clusters)

0. **Extract `OwnedQuantityPolicy` to Core + oracle tests** (~0.5 session, low risk). Pin the increment / decrement
   / reconcile / distribute math before any driver depends on it. Gate: tests + build green.
1. **Add the hybrid fields + one-way migration shim, additive behind the existing heal** (~1 session). Dual-write
   every Register/Deregister; the `HashSet` stays the live source consumers read. Gate: build/test/guard green +
   user loads a pre-rewrite save mid-scoop and confirms it still unloads.
2. **Invert source of truth** (~1 session): `ReconcileToInventory` drives the view *from* the by-def fields; delete
   the carry-over mechanism (now dead, because owed counts are merge-invariant). Gate: scoop → merge → unload, and
   CE-loadout if installed, confirmed no floor-drop / no re-scoop churn.
3. **..N — flip consumers in clusters** (the bulk; 3-5 gated steps): Band A reads, then Band B owe-up, then Band C
   owe-down across the ~10 cores one driver family at a time. Gate per cluster: build + test + `/qa` (decrement
   completeness + determinism) + a byte-equivalence review of each verbatim deposit core + a user in-game pass.
4. **Final:** retire `GetHashSet`/`PeekHashSet` as truth (keep `OwnedView` as sole accessor), remove the now-redundant
   `check-drop-protection.ts` Peek/Get assertions (the hazard no longer exists). Gate: holistic `/qa` + a broad
   in-game pass (mining/deconstruct sweeps, all load targets, old-save load).

## Risk register

| Risk | Mitigation |
|---|---|
| **Decrement completeness** (the dominant risk): every owe-down seam must subtract the exact moved count; a missed decrement on a partial move leaks/strands units. | `ReconcileToInventory` clamp self-corrects the safe directions (over-owe → clamp to held; vanished-owe → 0). Stage Band C one driver family per gated cluster, each in-game verified, so a miss is isolated to one cluster, not a 50-site flag day. |
| **Derived-view correctness** (new invariant): mis-distributing owed units across the wrong live stacks is itself a mis-ship. | Phase-0 oracle test "owed units distributed across N stacks deterministically"; the view is read-only so it can't corrupt the ledger. |
| **Weapon / Simple Sidearms regression** if a `stackLimit == 1` def leaks into `owedToStorage`. | Enforce the partition at the single `IncrementOwed` entry; weapons stay on the unchanged by-Thing path + old scribe label; SS exclusion untouched. |
| **Save-migration cross-ref timing**: the legacy `LookMode.Reference` fold must run in `PostLoadInit`, not `LoadingVars`. | Use the exact in-repo idiom that already ships (`LoadLedgerEntry`); a wrong phase degrades to heal-recovery (self-corrects next tick), not a crash. |
| **MP determinism**: dict-key enumeration is non-deterministic; every ordered-effect site must shortHash/defName-sort keys, and existing thingIDNumber sorts must be preserved. | Net positive at the source layer (commutative quantity removes the HashSet-order desync the code already flags); add one deterministic-key helper used at every dict-iteration site. |
| **Scope/value mismatch**: fixes ownership representation completely, the ungoverned triggers (root cause 2) not at all. | Confirm with the user which root cause hurts most before committing multi-session effort. |

## Effort

**Large.** ~44 files / ~50 sites. The work is dominated not by the model swap or the ~24 trivial Band-A reads,
but by re-expressing the ~10 conservation-critical deposit/unload/drop cores against `DecrementOwed`. Rough:
Phase 0 ~0.5 session, Phase 1 ~1, Phase 2 ~1, Phases 3..N the bulk (3-5 clusters, each needing a user in-game
pass between them). Calendar time is gated by the in-game verification cadence, not raw coding.

## Is it worth it?

Yes, on the merits: the model is already half-built (the heal *is* a by-def reconstruction; the scoop path already
branches on stackLimit), so this promotes an existing ephemeral by-def union to the scribed source of truth rather
than inventing a new model. The payoff is structural and verified: the whole recurring class dissolves at the root
for every stackable consumer at once, the Peek-vs-Get hazard is eliminated by construction, three fragile
reconciliations delete, and the scribe gets strictly more robust. But it is robustness, not an active-fire fix —
schedule it, and only after confirming root cause 1 (lost-track-of cargo) is the bigger player pain versus root
cause 2 (trigger timing).
