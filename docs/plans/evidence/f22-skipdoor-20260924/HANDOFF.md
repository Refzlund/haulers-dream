# F22 / C001: Skipdoor Pathing compatibility investigation

Investigation date: 2026-09-24. This is source/package evidence and a focused recommendation, not native acceptance or a ledger closure. No product source, installed modlist, player save or public feedback was changed. No Prepare or RimWorld launch was performed.

## Feedback scope

C001 asks whether Hauler's Dream is compatible with Skipdoor Pathing and respects Stack gap limits (2026-09-05 21:56:49 UTC). It supplies no Skipdoor Workshop ID, failure, save or log. The Stack gap half belongs to F21. No recurrence can be established from this question alone.

The original [VPE Skipdoor Pathing](https://steamcommunity.com/sharedfiles/filedetails/?id=2995157602) is abandoned and lists RimWorld 1.4. Two current 1.6 packages use similar names but materially different integration. Neither was installed in the 60-item local Workshop directory. Both were privately acquired with anonymous SteamCMD and inspected; the downloader exited 0 and was joined. This does not subscribe the player or change their installed mods.

| Inspected provider | Package ID | Workshop manifest | Selected 1.6 DLL SHA-256 |
|---|---|---|---|
| [Skipdoor Pathing (Unlimited)](https://steamcommunity.com/sharedfiles/filedetails/?id=3605522450) | `DCSzar.SkipdoorPathing` | `8282701918123092780` | `A27A45CBABCD32DA3414042E2B60B285C21BF8B1A82D2653DC07F0F470D6D6C9` |
| [VPE Skipdoor Pathing Redux](https://steamcommunity.com/sharedfiles/filedetails/?id=3645096243) | `yahvk.vpeskipdoorpathingredux` | `180871275263157485` | `A3DA58FACFC8147B22A8406F2EC1A771458DB52A0FB87F82C4B62F62ECAD620B` |

The package inventory and evidence hashes are in `evidence-index.json`. Raw packages, actual-DLL decompiles, native `Pawn_PathFollower` decompile and download receipt are retained under `%LOCALAPPDATA%/HaulersDreamQA/acquisition/f22-skipdoor-20260924`. The acquired Redux package is newer than its public GitHub source snapshot; conclusions use the actual Workshop DLL, not repository HEAD.

## Result and limits

**Redux is structurally compatible with HD's ordinary native movement, but has not been run with HD in this investigation.** It replaces the native pathfinder job in both synchronous `FindPathNow` and batched path requests, giving skipdoor cells additional graph connections. It does not replace the HD job, change its target or interrupt the driver. HD's `SweepWalk`, ordinary unloading and other native movement therefore retain the original target/arrival contract. This is an inference from the inspected DLLs; one real pickup-and-delivery witness is still required before publishing tested compatibility.

Redux applies its own faction/intelligence/current-job rules. Mental states and wandering jobs are excluded; animals are limited to supported follow jobs. Both its own description and HD's reachability checks require an ordinary reachable destination. Same-map skipdoor movement does not add cross-map HD hauling. HD still selects nearby stacks and candidate storage using physical distance/radii (`BulkHaul`, `StorageRouting`, early `EnRoutePickup` filters). Skipdoors therefore do not make a distant stack nearby or promise a globally optimal portal route. HD's accurate `EnRoutePickup.PathCost` does call native `FindPathNow`, which Redux patches, but Redux reads the pawn's current job: a planning query made while wandering can differ from execution after the haul starts. None of this requires a new portal-aware optimizer to answer C001.

**Unlimited cannot receive an unrestricted compatibility claim from the inspected code.** Its `HarmonyPatch_Pawn_PathFollower_StartPath.Prefix` rewrites the requested destination to the entrance's interaction cell, `OnCell`, then leaves the current job running. Native `Pawn_PathFollower` calls `PatherArrived()` when reaching that rewritten destination, including immediately inside `StartPath` if the pawn already stands there. Its `StopDead()` clears `curPathJobIsStale`, and `PatherArrived()` then notifies the current driver. There is no corresponding arrival-suppression hook in the inspected Unlimited assembly.

HD's `SweepWalk` completes on `PatherArrival`. `JobDriver_BulkHaul` and `JobDriver_SelfPickup` subsequently take their original target into inventory, relying on that arrival contract. Thus the source contains a concrete path by which entrance arrival can advance pickup before the pawn reaches the target. Starting a real HD haul while already at the entrance is the smallest deterministic reproduction candidate; normal walking to the entrance may expose the same ordering. The actual manifestation still needs a native witness and must not be reported as a reproduced user bug yet.

There is a second independent problem: Unlimited's `SkipdoorTransitManager.MapComponentTick` creates a new player-forced vanilla `Goto` after any drafted teleport, rather than preserving an existing drafted HD command. Its transit plan records no originating job identity. An HD drafted nearby-haul command therefore is not protected from replacement or stale-plan continuation.

Unlimited has special PU&H job recognition, but no HD JobDef or package name matches it. **Do not add HD to that recognition as a quick fix.** That branch starts a separate VEF teleport job with resume enabled. HD bulk pickup, inventory unload, bill gathering and several loading jobs are deliberately non-suspendable; restarting an interrupted driver can lose the intended chain or duplicate gathering, and finish actions can queue unloading. This would exchange the arrival defect for job-lifecycle defects.

## Recommended focused action before editing

1. Use the native slot for the small witnesses below, not a broad compatibility matrix.
2. For Redux, publish tested support only after the ordinary movement witness passes. Otherwise publish the narrower source-reviewed statement in `proposed-player-guidance.md`.
3. For Unlimited, the safe immediate co-installation workaround is its existing **Custom JobDef Exclusions** setting. It accepts exact, comma-separated names; excluded HD jobs walk normally. The accompanying guidance provides the complete current HD names, including the retained legacy definition.
4. If automatic safe coexistence is desired in this PR, the smallest proposed code change is an optional bridge on **Unlimited's own `SkipExclusionUtility.ShouldSkipdoor(Pawn)`**: preserve any existing false result and return false for HD-owned job drivers/defs. Bind only the exact optional provider method/package; do not alter vanilla pathing, user settings, third-party jobs or Redux. Verify the original HD destination/job survives and completes on foot. This supports coexistence with a clearly disclosed walking limitation; it does not implement portal traversal for HD jobs. Recommend this bounded guard over a job interruption shim. No such edit has been made.
5. Full Unlimited portal traversal would instead require preserving the originating job/driver and final arrival through entrance transit, handling cancellation and drafted commands. That is a separate supported behavior, not justified by pretending the PU&H branch already handles HD. If chosen, reproduce first and repair that exact lifecycle with a native regression witness.

## Smallest useful native witness

Reuse the existing real native host/controller arena and evidence receipts. Load Harmony, VEF, actual VPE (`2842502659` from both providers' dependency declarations), HD and exactly one inspected provider. Never activate both providers together. Dependencies must be acquired/selected as actual packages, not mimicked classes; this investigation has not acquired VPE.

Use two real same-map skipdoors, a normal reachable walking route, one colonist, two small haulable stacks and one valid destination. Keep every selected stack within the configured HD sweep radius. Put the entrance at the pawn's starting cell and the far exit near the pickup area, leaving a substantial walk reduction so Unlimited's default minimum-trip gate is satisfied. Use real HD bulk hauling, then the real tracked unload. Record original job identity/def, driver toil, requested target, pawn cell, portal crossing, source/inventory/destination counts and tracked cargo. Assert no source leaves the ground before the pawn actually reaches pickup range; exact conservation and eventual storage delivery; no unintended replacement or leftover cargo. A second target ensures that the current job chain really continues after transit.

For Redux, verify actual portal use and completion in one ordinary case, plus one drafted nearby-haul case if the public answer covers drafted commands. For Unlimited, the entrance-start ordinary case is the first pre-change reproduction; a drafted case verifies the unconditional `Goto` replacement. A bounded exclusion/guard candidate must retain the HD job and complete by walking in those same cases, without stale teleport plans. This is enough to establish the proposed limited coexistence contract. Do not require a full feature or mod matrix to close this compatibility question.

## Evidence boundaries

Source review and package acquisition are complete. The runtime witnesses, any proposed bridge, player-facing publication and ledger disposition remain root-owned. No established prior fix for C001 was identified, so do not label this a recurrence. Preserve F21 as a separate item. No other similarly named providers are claimed supported by this review.
