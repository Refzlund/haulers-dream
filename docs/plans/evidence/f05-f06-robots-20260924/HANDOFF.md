# F05/F06 bounded no-Biotech robot witness

24 September 2026. Source/build handoff only. This agent did **not** Prepare or launch a runtime, change production code, update a ledger, or claim F05/F06 passed. Root owns review, inactive-desktop launch, process join, raw copy and Verify.

The accepted X2 Hauler automatic run `da718daf61ff401b945a32259c881b60` and F14 Builder III construction run `906e7cfe4b354851b0f2d15466bf7cc7` remain reused evidence. This scene exercises the distinct Omni role and the supported no-Biotech command surface; it does not rerun those actors' positive paths.

## Prepared scope

One disposable enclosed arena and four successive two-stack batches, totaling80 Plasteel, plus seven explicitly kept units. Actual `X2_Building_AIRobotCreator.CreateRobot` creates Omni I and Builder III without injecting trackers, changing roles, changing skills or granting Hauling. Exact native Omni Hauling priority2 and Builder Construction1/Mining3 are prerequisites. Both require one resolved HD inventory component and native `CanTakeOrder == false` with no Biotech.

1. Actual selected Builder `GetGizmos` must provide exactly one disabled nearby command with its translated unassigned-Hauling reason; the human incapability override must not change that result or native role. Actual Omni gizmo must disable under allowMechanoids=false and re-enable when restored. Queries must preserve job IDs, actor/current/queued jobs, reservations, physical stock and roles.
2. Unassigned Omni starts automatic hauling through the actual X2 work giver. Native selection and job start/end observers must connect the automatic BulkHaul to physical possession of both10-unit stacks, full storage delivery and seven retained units.
3. Actual selected Omni `GetGizmos` command action starts the real pawn-bound Targeter during native Root.OnGUI Repaint. Its actual TargetingParameters and registered validator must accept a real source. Native StopTargeting cancels once without gameplay mutation. Reopen, invoke its actual registered accepted-target callback, stop targeting, then native ticks perform pickup and ordinary unload. This is programmatic callback invocation, **not** pointer/keyboard input or a claim of native context-menu selection.
4. One unrelated non-idle native Goto precedes a queued nearby command. The existing IssueSynced entry receives explicit queue=true; there is no synthetic Shift. Goto must finish naturally, the queued identified BulkHaul must start with native fromQueue=true, succeed and deliver both stacks.
5. A second actual gizmo/Targeter command is interrupted after its first physical pickup. The Pawn.Tick observer pauses at this boundary; an explicit native replacement Goto must end the original with InterruptForced, leave picked stock physically held and retain exactly one ordinary unforced unload behind the replacement Goto. That identified unload must start once from the native queue after Goto succeeds. At its actual end boundary it must have delivered the picked10: stored70, remaining floor10, inventory7, hands0, Keep7 and total87. Only Succeeded or this physically proven keep-only Incompletable may pass. No second unload or new explicit work/NearbyDelivery is allowed during recovery, and no cargo work may restart through the final300 ticks. The actual robot scheduler/unloader subsequently recovers held and remaining floor stock. Final storage80 + inventory7 must stay stable for300 ticks with native roles intact.

Every host Update checks exact physical conservation and the Keep7 quantity. Individual same-def stack identities can merge/split natively. Changed-state events retain actual inventory IDs/counts, hand cargo, floor placements, job authority and queue. Finite bounds are240 seconds,24,000 total ticks and7,000 ticks per stage.

## Pre-execution API audit

- Native robot GetGizmos yields base pawn gizmos. This is the actual supported no-Biotech command route; no direct float-menu provider call is substituted for native context selection.
- Targeter fields are read only after invoking the discovered command. The fixture invokes its registered action only after actual CanTarget/validator approval and records the callback boundary honestly. Cancelling invokes native StopTargeting.
- Native TickManager checks Paused after every DoSingleTick, so pausing from the actor's Pawn.Tick observer prevents another simulated tick before host interruption handling.
- Source positions are ordinary distinct cells three cells apart; all remain within the cleared room and native nearby radius. The Critical stockpile has40 cells. There is no shelf overlap or RNG-dependent source placement.
- X2_AIRobot.Destroy calls salvage generation even for Vanish; when costs exist, new resources may be spawned. The Builder negative therefore remains spawned on a native Wait hold for the scene. Cleanup snapshots robot destruction output and destroys only the newly created salvage after all accepted material observations. This correction was made before execution. The initial build/source are retained as v1; failed v2 source and receipts are preserved in source-review/v2-failed. Failed v3 source/build/selected receipts are preserved in source-review/v3-failed. Selected build is v4; its exact end-boundary and no-repeat correction is detailed in source-review/v4-interruption-review.md. Both failed native runs remain failed.
- The scene restores fixture settings, selected objects, speed, parked pawn placement/world ownership, owned zone/goods/robots and observer patches. Disposable terrain/Home changes and prior Lord duties are not reconstructed, matching the inherited private-scene boundary. No player save/config/runtime is edited.

## Frozen inputs and build

Production package is `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f07-authority-20260924/Product`:

- HD `A20FBD9FB139668D0BD50DBE9496B75C728AB7023CC7277BA24388FE148E8C0F`, MVID `f30c55c6-9106-4cb9-99e9-616c22677778`.
- Core `1A30CF3CBFC96C7BD0980CBE46482C07A58519F4D31FBD9D269A830A6AE9E332`, MVID `36bbf3f3-9ff2-4186-857a-166bf81f74f9`.

The new controller pins both images during Prepare and existing-manifest validation. It retains the F03 controller's exact six-mod/twelve-image, private-path, metadata, protected-tree, hash and process safeguards. Diffs against F03 are in `source-review/`. Its result is intentionally review-required; transport/metadata Verify alone does not accept behavior.

Selected host: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/builds/f05-f06-20260924/build-v4/Assemblies/HaulersDream.RuntimeHarness.dll`.

- SHA256 `428C0DB0A126C2B930FF2D014577E216F7B2A04938EC02ED13C8C61DD4BCF2B9`.
- MVID `45e41a25-1be4-4a10-b6c9-6f6f9abe4b91` from fresh native PS5.1 reflection-only metadata receipt.
- Build succeeded with zero warnings/errors in3.80 seconds. Controller/build/launch scripts parse with zero errors.
- Source `src/RobotCommands.cs`: `F6737B8AD0E69C3BB8125431B3571E87487A414DE84A7C1D3DCD9FF3750A1F5D`.
- Controller `23BF1861ED1D09A3A40842E131661CF9BBBC7C729AEEF73DACA5ED3D032666C1`.
- Complete selected paths/hashes: `selected-inputs.json`; build/source/product/metadata receipts are in `source-review/`.

## Exact robot package recovery

The old F03 robot package roots were absent. Public author commits were reacquired through the agent-reach GitHub/gh route:

- `HaploX1/RimWorld-Miscellaneous_Mods`, commit `9e8471a9086d4f04b5fa644a21bf3db3f30ab413`, `Mods/Miscellaneous_Robots`.
- `cabmoomoo/Rimworld-MiscRobotsPlusPlus`, commit `523b134f1aa2c7f0080083429da99100d6145fd0`.

All1,366 non-.git files exactly match the original F03 manifest hashes:1,006 base and360 R++. For771 text files, original CRLF endings were restored only when the resulting SHA256 equaled the original retained SHA256. No inferred or approximate content was accepted. R++ clone LFS warnings are retained acquisition diagnostics; every staged game file, including each binary/image, passes the original hash comparison.

The original manifest also included42 clone-internal `.git/` files; these are explicitly excluded from this new future-run manifest, including unrecoverable old packfiles. Original evidence is untouched. `.gitattributes`/`.gitignore` remain when present in the original inventory. Recovery and initial-check records are retained under `source-review/`.

New manifest `robot-packages.json`: `8BF676AC19460EA1E8C991D2B166576602DDA9055A61CC861E364487654BA53C`. Package roots are under `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f05-f06-20260924/packages/`. Native1.6 AIRobot hash remains `6EAD86A97FB9F9438000AB4A6DACBD52A478EE41C460D1298AAF47C27526CE46`; R++ hash remains `7A09CEB35FB2FD9BD29E48708EAF00EF0EA2435A994A0EE112F92A669DFDE1F8`.

## Root execution handoff

Use this folder's `controller/scripts/runtime-test.ps1` with Action Prepare, CaseId `F05-F06-ROBOTS`, ExpectedBehavior `satisfied`, NegativeControl `None`, HdSource `Built`, the frozen production root above, selected v4 harness path above and this folder's absolute `robot-packages.json` path. Omit RunDirectory so the controller generates a fresh identity. Launch only through root's existing inactive-desktop wrapper and this folder's `launch.ps1`. Copy/verify the generated run and independently review complete assertions/events, whole Player.log, authority/cargo/queue outcomes, cleanup and owned process joins.

Not covered: station recall/contained save-and-restart, actual Biotech-enabled native context-menu selection, old-save component installation, construction-leftover recovery or final assembled-build integrations. These remain explicit obligations for the ledger owner; this bounded witness must not silently close them.
