# F40 transporter Core native fixture v3

Ready for independent source/input review. No Prepare, native launch, deployment, commit or public action was performed. F40 remains open. The agreed scope and separate save/provider/MP obligations are in `ACCEPTANCE.md`.

## Selected inputs

The controller reads **`../selected-inputs.json` relative to this fixture folder**, i.e. the F40 evidence-root selection, not a copy inside the fixture. Its SHA256 is `9A666B95DB7748BBECAC0373BFF4065400F546A48D0A915BFF54E9C9EAB1C971`. Prepare checks the exact candidate/harness paths and every selected product, compiled-input and controller hash before making a run. Both Prepare and manifest admission require Built / satisfied / None / four mods. There is no baseline acceptance for this new feature.

- Product root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f40-native-20260924/Product` (119 files). Content is frozen from the working tree; DLL/PDB outputs are the accepted UIv3 build. Its full compiled source closure, including unrelated working-tree inputs, remains pinned in `../ui/final-v3/build-inputs.json`; this is not a newly composed F24/F41 product build.
- Harness: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f40-native-20260924/HarnessBuild-v3/Assemblies/HaulersDream.RuntimeHarness.dll`.
- Harness SHA256: `DF11DAA3035C52585C3E0B118F91F954979E89D95CFCC11DE72F7A0E70D89987`; MVID `fcdb1910-042c-465e-a776-69f8cea9676e`.
- HD SHA256: `CBFE816FE991B53B97803C04171E98C2D8FF92411D11844F7B7635DE76390F10`; MVID `2757b695-456c-4354-a00c-5bd8599d32df`.
- Core SHA256: `B5763AF1B791E8EEB5484CB28F8CD358F29186CE39A11D7825A9327871E2D050`; MVID `b924ffde-8479-462e-bd12-fab48a8dd73a`.
- Actual installed Assembly-CSharp compile reference: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`. The project emits this reference identity into the host, and the existing host checks it against the running image.

`v3-build-inputs.json` records all 96 host/source/project/reference inputs before build; `v3-build-input-verification.json` confirms zero changed inputs afterward. Build v3 succeeded with zero warnings/errors in 5.82 seconds. Its complete logs and joined-process receipt are under the external HarnessBuild-v3 folder. `image-metadata/` and `image-readers.json` retain independent native PS5.1 metadata reads for all three DLLs; every reader joined with exit zero. Failed v1 and passing pre-review v2 are preserved.

## Review corrections

The five `src/TransporterCore*.cs` partials implement ten independently reported phases. `final-review.diff` compares the last source-review snapshot with v3; `final-v3/src` preserves the actual compiled blobs. The three requested corrections are quantity-based keep acceptance only on ordinary load/recovery paths, actual native mental-state and continuous draft/setting races plus ordinary drafted delivery, and a nonempty native group-command witness.

Additional fixture corrections: actual over-capacity cargo supplies a rejected native dialog (empty edits are valid for Core pods); current-manifest/unknown-session setup preserves a real group; original Steel destruction is forbidden until all background Steel is parked; failed partial setup does not falsely require unparked original Steel to disappear. Generated couriers must be healthy and capable. The finite native temperature input follows the independently authored F41 scene and is restored on cleanup. The source inventory and exact conservation observers remain active during productive jobs, not just terminal assertions.

The actual menu method, gizmo toggle action, retained option actions, native mental-state start/recovery, native dialog acceptance/cancel, actual WorkGiver jobs, ordinary job drivers and native ThingOwner transfers are exercised. Observer patches only record boundaries. Test-authored transfers are confined to scene preparation and the explicit live-ownership race, with full physical accounting. Native job-ID counter, complete RNG state/stack, saved ledger/flags/session state, cargo owners/counts and current/queued jobs are included in every purity comparison; only ephemeral menu caches may change.

## Protected execution and review

Use repository `scripts/run-on-test-desktop.py` to launch the bundled PowerShell runtime around `launch.ps1 -RunDirectory <prepared run> -OutputDirectory <native evidence directory>`. This is the same inactive Win32 desktop/job-object boundary used by F41. Never invoke the controller's Launch directly on the default desktop. The wrapper itself is hash-pinned in the selection. `launch.ps1` owns and joins only the controller-returned game process, with a 360-second deadline; the host has its existing 300-second deadline. No graphical execution happened while authoring this fixture.

`controller-preflight.json` records zero PowerShell parse errors and a parsed literal Prefs XML block with `pauseOnLoad=False`, `runInBackground=True`, and no uninterpolated expression. This Core fixture does not yet generate producer/restart preferences.

After the run, review every assertion and all phase/physical/transfer/job events, whole Player.log, runtime image and source identity, process/desktop receipts, cleanup and protected-root Verify output. Verify intentionally remains `not-verified` until independent semantic review; source/build success cannot close F40. Save/restart producer/consumer, installed optional-provider/shared-carrier controls and actual Multiplayer replay remain separate native legs explicitly specified in `ACCEPTANCE.md`.
