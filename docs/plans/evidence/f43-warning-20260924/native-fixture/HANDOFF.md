# F43 native witness ready for independent source review

Case `F43-WORKSCAN`; role `baseline-gap` selects the matched pre-removal product, role `satisfied` selects the four-file corrected product. Both use the same host and actual native inputs. No Prepare, native launch, staging, commit or ledger update has been performed by this author.

The authoritative selection is **`../selected-inputs.json`**, reached by the controller at `../../../selected-inputs.json`. Final SHA-256: `ADCD4004B42A8DEF389767C543EAF1F5426B6EE2E267CF9CC9B7BA049804CDAA`. It binds 153 runtime files for each product, 83 host source files, 20 controller/launch files, the host and seven common native images. `final-selection-audit.json` verifies all 419 selected file hashes. The product/compiled audit separately passed 1,579 checks. All 22 relevant PowerShell files parse cleanly (`parser-preflight.json`); Prefs uses a literal False pauseOnLoad without an unexpanded expression.

Host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f43-warning-v1-20260924/HarnessBuild-v2/Assemblies/HaulersDream.RuntimeHarness.dll`.

- SHA-256 `5F85391FCED2D7A3429FD3B4F0206F9D4D79292AEF571BBED9E444BCCB1FCDE8`
- MVID `54137fc2-20cb-4cec-8b17-fad048faebbf`
- Build v2 passed in 4.10 seconds, zero warnings/errors, owned process joined exit 0. Failed v1 source/compiler output remain in their original separate build directory.

Products: `.../f43-warning-v1-20260924/baseline-Product` and `candidate-Product`; exact images and four-file source diff are described in `../product-v1/HANDOFF.md`. Private source/build roots are frozen and never overwrite a prior build. Reflection-only metadata-reader receipts are retained.

Review `CONTRACT.md`, the three `src/WarningScene*.cs` files, `host-adapter.diff` and `controller-adapter.diff`. The host calls the observer at real GameComponent ticks; the query sequence and full native scan are not synthetic callbacks. Observer hooks only write private fixture state and never alter arguments/returns or swallow native exceptions. `WarningScene.Environment.cs` reuses the already reviewed finite native climate-input/warmup policy.

Final self-review found a copied Launch admission condition still restricted to satisfied, although Prepare and the host already accepted both roles. `v2-before-role-admission` preserves the prior selection/controller/diff; `controller-role-admission.diff` changes only that condition to the exact two-role allowlist. Host and products are unchanged. This fixes admission consistency without changing any native expectation, protected-file rule, expected error or generic clean-log assertion.

Root should Prepare each role with `-CaseId F43-WORKSCAN -ExpectedBehavior <role> -NegativeControl None -HdSource Built`, the corresponding selected BuiltModRoot, selected HarnessAssembly, and the selected protected player-save root. Use process-local TEMP/TMP `C:/HDQA/runtime-temp` for short runtime paths. Never directly launch the controller on the user's desktop: use the existing `scripts/run-on-test-desktop.py` wrapper around `pwsh -File <this-folder>/launch.ps1 -RunDirectory <prepared> -OutputDirectory <fresh-evidence>`, which owns the inactive private desktop/process cleanup. Preserve both raw captures and Verify output before further changes.

The expected baseline native ErrorOnce deliberately makes the generic clean-log result fail. It must remain visible and be independently classified against the exact observed chain; it is not a clean-runtime pass. Corrected case must pass with zero Unity errors, real 10-Cloth delivery and 300 stable ticks. The controller deliberately leaves semantic acceptance to independent whole-log/source/process review.
