# F38 exact source integration recommendation

Source reconciliation is complete against HEAD `096edbc2a30d4f248921113a7a77c26ad0e10e92`. **Do not treat this as F38 closure until the two fresh-consumer replay captures pass independent review.** The native counter diagnostic and new `load-replay-v3` fixture replace only the invalid producer-versus-loaded replay comparison. They do not replace or repeat accepted product/capacity evidence.

`audit.json` proves all seven current workspace files equal their frozen accepted source bytes. Six equal the original implementation-v1 after files; the reservation patch equals `take-inventory-v2/after.cs`, which differs only by the reviewed native TakeInventory MaxPawns10 allowance. The three tracked HEAD blobs are byte-identical to their blobs at original base137a905. The subsequent F25/F02 commits therefore introduce no overlap in this slice. No product worktree or index file was changed by this review; only review artifacts were written.

## Exact candidate staging boundary

`head-relative.patch` contains only the following seven paths. Its SHA is `162DEC76D168DC70C93B6BB8F6DA98C983C3B0399A2D1815AED0127446AF49B3`. `staging-tree/` holds their exact accepted after bytes; `audit.json` records HEAD blob IDs and before/after SHA values. Root should recheck HEAD and these hashes immediately before staging, then stage only this patch/tree after native acceptance. Do not stage broad Source or all worktree changes.

| File | Included scope |
| --- | --- |
| `Source/HaulersDream/JobDriver_BulkHaul.cs` | Validate aligned pickup lists, retain original anchor force authority, gate incidental reservations; consume retired current targets on the existing walk/pause pre-ticks. Toil order and saved cursor fields unchanged. |
| `Source/HaulersDream/BulkHaul.cs` | Extract narrow plan-cache invalidation, retaining the existing complete ClearCaches behavior. |
| `Source/HaulersDream/HaulersDreamGameComponent.StorageClaims.cs` | Increment evidence generation without clearing destination rows. |
| `Source/HaulersDream/Patch_ReservationManager_SweepHandoff.cs` | New shape-checked native cancellation wrapper and success-only queued-plan retirement; exact supported native/bulk source ownership. Includes TakeInventory MaxPawns10 correction. |
| `Source/HaulersDream.Core/SweepPickupPlan.cs` | New aligned-plan validation and index-preserving Invalid/zero retirement helper. |
| `Source/HaulersDream.Tests/SweepPickupPlanTests.cs` | New focused plan invariants and held-surplus capacity case. |
| `.changeset/preserve-sweeps-during-haul-handoff.md` | Existing reviewed patch release note describing cooperative handoff and non-stealing extras. |

Explicit exclusions: F01 inventory/crafting work, F11 quantity controls, F40 transporter files and shared driver/claim additions, and F12 Comp/PostExposeData/pre-save/MP work. None is required to express this already-built F38 slice. No HarmonyPatches, settings, DefOf, job XML, shared Comp, MP registration, language or product-binary file belongs in this commit. Root can commit ledger/evidence updates separately or deliberately add their exact accepted records after replay; they are not hidden in this patch.

## GH266 requirement and completion map

The scoped source is the22 August open GH266 report against1.24/rev591: a second manual haul cancels the first nearby sweep, preventing cooperative hauling. Incremental reservation was the reporter's proposed solution; preserving remaining work and cargo is the acceptance requirement. The selected implementation meets that behavior through target-specific handoff without replacing the planner.

| Requirement | Implementation and retained evidence |
| --- | --- |
| Reproduce cancellation of the first whole sweep | Native baseline `ca6…` retains the genuine current-source cancellation failure; setup-only and pooled-job fixture failures remain separate. |
| Second explicit source order preserves the first remaining sweep | Exact reservation wrapper plus current/future retirement. Candidate `c19dba9d9f0b4b79a1710bec0b73f725`,59/59,U0, preserves original job/cursor/unrelated reservation and actual cargo. |
| Queued intent stays ordered and cannot reclaim a relinquished source | Success postfix retires actual reserved and explicitly identified soft JobQueue plans without removal/reordering; producer and original-save restart preserve the exact queue. Empty retired plan completes before the following original Goto. |
| Extra scanned sources cannot cancel another hauler's order | Driver admission gates incidental extras through CanReserve; unchanged stale built-plan control verifies the actual later admission. Failed Reserve/probes and unsupported/direct cancellation controls retain native behavior. |
| Held cargo/Keep/capacity remain protected | Target retirement preserves destination rows and invalidates evidence. Accepted capacity producer `bd616fc07307445084fcdd05a450ff39`,54/54,U0: effective76→51, new actual order32, actual150stored+7kept+25personal+8ground,300 settled ticks. Exact native TakeInventory32 succeeds; the prior wrong-MaxPawns failure remains retained. |
| Saved retired plan resumes without losing/skipping cargo | Original restart `2425bb6f55af4f119538967085b1325a`,45/45,U0, resumes exact saved job/cursor/queue and physically finishes107stored+7kept Cloth and36stored Uranium. The ordinary unload can end Incompletable after delivering surplus because Keep remains; do not claim all unload jobs Succeeded. |
| New mutation produces repeatable command state | Still pending: two fresh consumers of the original E10F save, with exact native compressed-map allocation admission and11 unmodified raw canonical comparisons in load-replay-v3. The old a12034/36 counter-mismatch failure is preserved and explained by normal native map decompression. |

The managed tests remain25/25, with the tested Core exactly selected. The final isolated product `207C6A58…` changes only the single reviewed native MaxPawns guard from the original candidate:2,944/2,945 HD methods unchanged, Core451/451 unchanged and original tested Core selected. Full shared-branch build/integration remains the root's final PR responsibility; the isolated proof does not establish unrelated uncommitted features or all196 mods in the original report.

## Documentation and localization

Keep the reviewed changeset wording. No new user-facing command, setting, UI text or translation key is introduced, so no16-locale rewrite is required. No named compatibility provider is added and no general reservation compatibility promise should be inserted in COMPATIBILITY.md. A README paragraph is optional; the changeset adequately explains this bug fix. Preserve the existing precise source/evidence limits: source admission is supported native haul/take-inventory and original forced bulk-anchor behavior, not arbitrary third-party job drivers or whole-world Multiplayer certification.

After replay acceptance and the exact root commit, a truthful resolution statement is: "A second explicit haul now hands off its selected source without ending the first pawn's remaining nearby sweep. Current, future and queued pickups retain their job/queue identities, carried surplus stays protected, saved orders resume, and incidental extras do not steal another pawn's reservations." Link the accepted native and source records and actual commit then; leave the row open until those conditions are met.
