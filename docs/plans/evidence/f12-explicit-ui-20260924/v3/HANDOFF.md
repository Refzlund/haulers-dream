# F12 prompt overlay correction and final UI witnesses

Ready for root review and its final bounded native runs. This author changed the product and adapted the observers, so this is an implementation handoff, not independent acceptance. No Prepare or native run was performed here.

The original robot image `../native-robot/native/cc33e23b6c714358b0db882683b657c3/raw/f12-robot-source-target-prompt.png` visibly has the second cancellation line overdrawn by the native lower gizmo row. The original human `../native-ui/native/9508d293b4574c969d97a198f1d39b55/raw/f12-italian-edge-prompt.png` also collides with the lower/right HUD. Both were inspected as actual images; screen-bound geometry alone was insufficient. Root separately accepted the original robot's56 physical/input checks.

`source.diff` is the entire isolated product delta from frozen UIv2: `DrawPrompt` submits the existing clamped/wrapped rectangle to native `ImmediateWindow` at `WindowLayer.Super`; its private `DrawPromptWindow` callback draws the background/label and restores GUI/text state. Native `UIRoot_Play` draws map UI/gizmos before WindowStack. Native `GenUI.DrawMouseAttachment` uses this same layer with no background, no surrounding input absorption and zero shadow. Native `ImmediateWindow` disables focus-on-open, accept/cancel closure and camera-motion prevention. Its request expires through native WindowStack when no longer drawn. The product does not change input, target validity, commands, order state or physical behavior.

Source has694 exact copied inputs and85 exact actual reference pairs. Product build joined0 in21.60s with zero warnings/errors and deployment target absent. `compiled.diff` confirms the actual compiled DrawPrompt/helper change; the rest of the compiled type is exact. Assembly identities/references are unchanged (`compiled-review.json`). The initial packaging assertion correctly stopped on rebuilt Core binary drift: complete Core IL differed only in static-data RVA labels, with all instructions/definitions/data preserved. `rebuilt-core/`, full before/rebuilt IL and `core-rebuilt.diff` retain that evidence. Final packaging reuses the exact original Core DLL/PDB; only HD DLL/PDB differ from frozen UIv2. No F13 additions entered this slice.

| Binding | Exact pin |
|---|---|
| Product HD | `2E158DBC6FF03FCA8F7C6765EA53A8354845FC4142BFF0541BA64A000264A2E3` |
| Product HD MVID | `697c7be1-5a03-4935-9efd-8ec33bd92e8d` |
| Original Core | `2633530022F02B73D09A11254E088E6DE52D6F1B05A30C2E0233790B642BA4AA` |
| Product selection | `62794C0F9C2A9360021A7B64A91756855945CA894C7B68D00BBED86A9746C3E9` |
| Human native-ui-v5 host | `20A543E4FBCBDB481B8BB8C8E17EBEEEF5859BC3CBFF2D0BD485F281F0A4917D` |
| Human selection | `97D3E1D28FCCEC0BD9C0F8F485B14AD71BA55BD67E7241A933DA1414125D3494` |
| Robot native-robot-v2 host | `71740B305F96B274F87456FF3726AF4AB8E4CA4FB63FFDC367D4E6377B266C36` |
| Robot selection | `00438841F7665E92F143B5FB1CEFC65E1588A49CF001E12AE56B30CB066F0C34` |

Human host builds in3.00s, robot2.95s, both zero warnings/errors. Their747/2,121 input-preservation audits pass. Each successor changes only the observer hook from `DrawPrompt` to the actual delayed `DrawPromptWindow`, its receipt label and build path/product binding. Controllers, input worker and physical/command assertions remain byte-identical to their respective predecessors. The human includes the already-reviewed v4 null-map boundary correction and v3 real stale-dialog text-focus click; it does not convert an absent pawn-specific permission query into false. Original failures remain preserved and failed.

Required acceptance: run the closed human `F12-UI` and robot `F12-ROBOT` cases sequentially. Inspect full events/logs/images, actual module pins, real input receipts, physical conservation, original jobs, protected-tree verification and all joined desktop/native/input processes. The Italian edge prompt and the robot source-target cancellation line must be actually readable above native lower UI. Confirm target clicks/right-click/Escape and dialog Return/cancel behavior still work. Keep screenshots and raw events; do not infer visual acceptance from a rectangle assertion.

## Exact runtime procedure for root

Use the reviewed existing private desktop launcher. The following chooses the human profile; substitute `native-robot-v2` and `F12-ROBOT` for the robot run. Never invoke `launch.ps1` directly on the player's desktop.

```powershell
$env:TEMP='C:/HDQA/runtime-temp'
$env:TMP=$env:TEMP
$f12Fixture=Join-Path (Get-Location) 'docs/plans/evidence/f12-explicit-ui-20260924/native-ui-v5'
$f12Selection=Get-Content -Raw -LiteralPath (Join-Path $f12Fixture 'selection.json') | ConvertFrom-Json
$f12Prepared=& (Join-Path $f12Fixture 'controller/scripts/runtime-test.ps1') -Action Prepare -CaseId F12-UI -ExpectedBehavior satisfied -NegativeControl None -HdSource Built -BuiltModRoot $f12Selection.candidate.root -HarnessAssembly $f12Selection.harness.path
$f12Prepared
```

Take the actual returned run directory; do not invent or reuse an ID. With `$f12Run` set to it and `$f12Out` set to this profile's `native/<actual-ID>` evidence directory:

```powershell
& 'C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/run-on-test-desktop.py --output (Join-Path $f12Out 'private-desktop') --cwd (Get-Location).Path --timeout 620 -- 'C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File (Join-Path $f12Fixture 'launch.ps1') -RunDirectory $f12Run -OutputDirectory $f12Out
& (Join-Path $f12Fixture 'controller/scripts/runtime-test.ps1') -Action Verify -RunDirectory $f12Run
```

Capture the complete original evidence and manifest before diagnosing any failure. Keep TEMP/TMP identical for Verify. The short runtime root avoids native Mono long-path XML failures; no focus or input-desktop switch is allowed. No fixture callback may invoke a product command or satisfy input acknowledgements synthetically. Generic Verify manual/log-review flags still require semantic review.

## Status for Part 2

The broader bare-point backend/lifecycle witnesses belong to their already-reviewed evidence folders and are not rerun here. F12 human complete rendered/input acceptance is still pending this final native run; robot physical56 was accepted separately but its corrected rendering remains pending. Original unfocused Return9508 was never proven a product fault;8ce2 was an actual null-map fixture observer fault. Actual network execution and final combined F12/F13/provider integration remain explicitly separate. F40 shuttle/VF authoring is paused and unbuilt; see `../../f40-transporter-20260924/shuttle-vf/STATUS.md`.
