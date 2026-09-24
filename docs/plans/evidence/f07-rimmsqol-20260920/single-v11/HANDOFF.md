# F07 v11: correct the forced-single-stack oracle

The v10 native run `49fef075e5ed4f35962e8d330e7d6595` remains a failed run with untouched raw evidence. It reached successful undrafted and drafted pickup/delivery, then rejected the intended single-stack behavior at event 145: actual identified forced `HaulersDream_BulkHaul` job 60, while the fixture incorrectly required vanilla hauling. `BulkHaul.cs` around line 976 explicitly retains bulk hauling for a forced `forceSweep` order with one small stack. The v10 author's precondition audit missed that branch. No product correction is appropriate for this fixture error.

`fixture.diff` changes only the scenario oracle and records one additional observation counter. The offered native command must now retain its forced identified job and build an actual bulk queue containing exactly the newly spawned five-unit stack. After native ticking it must settle, report successful pickup, and add exactly one successful scoped drafted delivery. Existing pause-while-drafted, keep7, physical conservation, empty hands and complete storage settlement remain required. No productive method is called manually. This does not claim to test a capacity fallback; that requires a different genuine capacity precondition.

The exact v10 scenario is preserved as `../src/RimmsCommand.v10.cs.txt`, SHA-256 `A4EEEB0664723B2FFFC50F014543E4F2239D33201819D7B667B460A7F953C715`. The new source and `RimmsCommand.v11.cs.txt` are `D4607F051D3188CB6E771A5ED10093242F2E85170759205C7ABC019664367AA6`. Existing v10 build output, source records, controller, launcher and product were not rewritten.

Remaining-stage source review found no analogous forced-single mismatch. The queued scene starts an actual non-idle Goto, invokes queue=true, revokes RIMMS permission before admission and requires Goto to finish naturally; the actual CanBeginNow guard supplies the rejection. The interruption scene waits for inventory growth while bulk pickup is current; the existing 120-tick pause leaves a partial-chain observation interval. It requires InterruptForced without a generated delivery and retains the observed interrupted stock as the next scope's protected baseline. The following save waits for the real successful command's generated delivery to hold cargo away from storage. Restart verifies the recorded identities and quantities before completing the delivery. These later stages remain native obligations, not source-proven passes. Their conditions were left unchanged.

## Build and root selection

The fresh host compile passed once, zero warnings/errors, 2.99 seconds. `../build-v11.ps1` changes only the output directory from the existing v10 build command. Output is `%TEMP%/hd-f07-20260920/build-v11/Assemblies/HaulersDream.RuntimeHarness.dll`:

- SHA-256: `CED49D6F48DB4D056DC4E153FE748B038F3A08A22A945509068FA6DF9B002C49`
- MVID: `77e3ef79-946a-4e95-a32b-6a2bd5acb46b`
- Successful reflection-only metadata receipt: `build-v11/assembly-metadata.json`.

The metadata reader's first invocation rejected forward-slash paths before reading an image or writing a receipt. Canonical absolute paths then passed in a joined PS5 process; no product execution was involved.

`build-inputs.json` and `unchanged-inputs.json` verify all 80 host/project files, 99 frozen v10 product files, 21 controller files and the selected new scenario stayed unchanged across compilation. Product remains `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-authority-20260924/Product`, HD `A20FBD9F...`, Core `1A30CF3C...`.

Root should use the same `controller-v10/scripts/runtime-test.ps1` (`CD45E574...`) and `launch-v10.ps1` (`83435173...`), selecting only this new v11 HarnessAssembly. Other producer and bound-restart arguments remain unchanged. Root still owns diff review, data-only Prepare, inactive-desktop native launch, join, evidence copy, Verify and producer/restart acceptance. None was performed by this fixture author; no ledger change or product edit was made.
