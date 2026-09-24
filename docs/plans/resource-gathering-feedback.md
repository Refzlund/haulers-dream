# C423 resource gathering and Harvest and Haul

Status: source investigation; cause unresolved and all linked acceptance criteria open. The authoritative request is C423-S01 under L01/L45/L54 in [the supplied-feedback matrix](vorshlumpf-feedback-2026-09-08.md). Independent report `A853E02D472754D2DDFF8DA37D4F8C8070083E44B6CDB184F8ED2B93EF5C56F3`, retained in `TEMP/haulersdream-c423-gather-investigation-20260908/REPORT.md`, was read in full by root. Its48 inputs remain unchanged. No H&H package, reporter save/settings, selected job or actual C423 failure was observed.

The exact quoted phrase appears in `About/SteamDescription.txt` for gathering construction/cooking resources. A separate line advertises harvested-item collection. Re-adding H&H suggests investigating produced yields, but the comment does not report a controlled successful retest. Keep ingredient acquisition, construction acquisition, already-held stock use and produced-yield collection separate. C425/C426's construction positives remain regression controls.

Current source intentionally distinguishes ordinary versus forced/batched recipes, Common Sense ownership, bench settings, minimum useful floor-stack counts and capacity. Construction gathering also requires enough demand to justify inventory acquisition; a small native hand delivery can be correct. Yield intake separately depends on the actual producer/placement seam, pawn and category eligibility, available storage, capacity and Disabled/Drop/Direct mode. Temporary ground output under Drop is not itself a failure. These source gates must be joined to actual selected and executed jobs before choosing a fix.

## Recurrence checks

- The June merged-food drop-back correction concerns material acquired and subsequently discarded. Distinguish it from never collecting a yield.
- T03-R18 confirms the July path-wait fix. Later replies distinguish clustered/growing-zone collection and intentional pickup timing from that wait. Reproduce stolen, merged, forbidden or unreachable pending targets with another valid target remaining before calling this a recurrence.
- The ordinary-cooking chain through PR244/PR252/GH258 concerns recipe and partial-held-stock exclusions. Existing BG01/BG02/P1 evidence applies only to its exact scenario and build.
- `pendingSelfPickups` is not scribed, despite a stale source comment. Save/load must distinguish outstanding pre-load ground drops from newly produced yields after load; this fact does not establish item loss or perpetual failure in an old save.

## Required discriminating runtime work

| Witness | Required evidence |
|---|---|
| A1: ordinary ingredients | Reuse unchanged accepted BG comparisons within scope; verify two-floor-stack, one-stack and partial-held-stock routes through actual native consumption and useful progress. |
| A2: construction | Demand beyond one hand trip, a small native delivery and sufficient already-held stock as separate controls; observe actual job and quantities. |
| Y1: basic yields | Eligible human, accepting storage, available capacity: isolated designated plant and automatic zone under Disabled/Drop/Direct; record production, collection, continued work and unloading. |
| Y2: batching and delays | Nine or more nearby plants; zone/clustered/forced work, including forced work with ordinary Growing disabled; observe actual section boundaries and delay contexts. |
| Y3: legitimate refusal | No storage, restored storage, capacity ceiling and strict/keep-working policies; preserve ground stock and useful work without scoop/drop or requeue loops. |
| Y4: earlier wait/ownership fixes | Two harvesters and a pending target taken, merged, forbidden or blocked while another remains valid; verify ownership, conservation and continued work. |
| Y5: persistence and raw-food protection | Disposable save/load with pending pickup, then fresh production; separately merge fresh food into tracked/personal inventory and observe keep/unload behavior. |
| H1: actual H&H compatibility | Acquire the actual supported package, inspect its patches, then repeat the specific failure with HD and H&H independently present/absent while keeping other inputs fixed. |

Add genuine mining/deconstruction producer controls if the original path remains ambiguous. Do not expand every setting into a full Cartesian matrix without a discriminating reason. Each positive needs actual started/ended job identity, queue/progress observations and a physical quantity ledger through final custody or legitimate consumption. Pure policy checks and warning-based mod detection do not establish compatibility. No yield behavior should be changed solely from this source investigation.
# 13 September acquired Harvest and Haul evidence

Root has read and adopted the bounded package investigation75B0CEDAA330685EE46A9007E5E664E06179385C41A44643D66EBFDD1352F37D in TEMP/haulersdream-harvest-haul-investigation-20260913/REPORT.md. The actual anonymously acquired item3563060965 has87 files/983,165 bytes, manifest1795465512286430422, update2025-12-07T17:06:19Z. Its1.6 DLL is525309DB8BBA69999ADFAFF5C32B7F5AE0E1D10EF887D7EED9EBB35256DDCB24, MVID0b1acae3-8e8c-4f36-bb80-dd3cd29ef816. This supersedes the package-acquisition gap below. It does not establish the reporter's installed H&H version, original job/settings or cause of C423.

The shipped binary differs from included source: the annotated ThingOwner.TryDrop fallback is absent from all shipped types, while PlantCollected.Prefix has neither a HarmonyPatch attribute nor a discovered registration/caller. The actual placement patch reaches both native GenPlace overloads, including a wrapper which calls the canonical method. Its fallback searches nearby same-def ground items when the original output is no longer spawned. Its tracking records whole merged Thing references; its unload uses whole-stack native TryDrop, with a CE loadout allowance but no HD Keep/surplus call. These are static findings awaiting actual runtime witnesses.

Add two required compatibility investigations to the existing C423/C424 gathering scope. They are discoveries from implementation review, not additional user reports:

- HCOMP1: begin with10 kept units, collect5 through actual H&H into the same stack, then run its real unload with storage available. Observe both mod orders and assert kept10 remains, only5 is available to unload, and total15 is conserved. Repair the demonstrated H&H/HD boundary if this fails; do not count disabling either mod as compatibility support.
- HCOMP2: execute a real HD DirectToInventory yield with a distinct nearby same-def ground stack. Capture both placement overloads, actual patch order, original/out item identities and all custody/count changes. The unrelated stack must not be attributed to the produced yield by either postfix pass. Repeat DropThenHaul to observe pending-claim invalidation and ownership. Make a narrow compatibility correction only after distinguishing the executed failure.

The existing A1/A2 and Y1–Y5 matrix remains mandatory: ordinary ingredients, construction and newly produced harvest/mine resources are distinct routes. Compare HD only, H&H only and both load orders in isolated games; include destinations, capacity and effective per-pawn/category settings. H&H can intentionally collect with no storage or above capacity where HD declines. H&H's unscribed tracking, HD's saved cargo/Keep and native item persistence also require separate save/reload and unload observations. No matrix cell was executed by this investigation. Keep the original raw-food and path-wait recurrence chains distinct until actual traces connect them.
