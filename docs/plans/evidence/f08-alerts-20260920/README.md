# F08 Custom Alerts activity and saved configuration

**Resolved on 20 September, guidance commit `de9f649`.** Root adopted the complete independent producer and [fresh restart review](restart-runtime-review.md). Run `79c6457ddcd54ab887b0b663dece4967` restored the exact saved query and one registration, then correctly followed productive gathering through conserved completion with zero game errors. Together the accepted pair contains 568 actual scheduled observations. [Report resolution and remaining final-build smoke](../f08-resolution.md). The chronological preparation records below remain preserved.

Producer `bfd06365cfae49749406579ffb90dd78` passed all activity stages with zero captured game errors. Independent review checked all 460 actual scheduled observations, 17 distinct F08 assertion IDs, native queued handoff, physical conservation, both UI pictures and the real saved alert. The actual owned process and launcher joined with exit zero on the inactive desktop; protected inputs were unchanged. Its one bound restart is accepted above. No F08 product behavior change was needed. [Producer review](producer-v4-runtime-review.md).

The three previous producers remain failed and preserved in [earlier reviews](producer-runtime-review.md). The third, `7b5b31b31db940eab70eb814039f731d`, completed activity stages but logged two genuine errors because parked insects retained map-Lord membership while world-owned. Its save is excluded from restart. The corrected fixture calls native Lord removal before world transfer. The passing fourth producer generated no non-null Lord membership to remove; it proves a clean complete activity scenario, not runtime exercise of that correction branch.

Actual packages are `%TEMP%/hd-f08-20260920/{Alerts,TD,TDS}`. Custom Alerts item 3537847307 has 33 files and DLL `A97673FEA3614701C4E4213B1FE98BE934F38D694AD20F49DE977CD459EC9F5C`; TD item 3529443295 has 163 files and DLL `A37691504D7EC62FE7C2ADD34F64512B07ED2FA3B8D5F8A203FD72E59A758D76`; TDS item 3529433984 has 22 files and DLL `54D8A62EA025CDE9D35140AD5D22B88E3A623D1816F7BAD3CC74D8BBC0E76B6B`. The four unconditional TD expansion-support images remain included without fake expansion flags. Independent contract review is in `../f08-alert-integration-review.md`. Current Custom Alerts differs from earlier retained Git commit `bff24986299b7527a16919eabea4395245720ada` / DLL `F90DF896…`; neither identifies the reporter's unknown version.

Private anonymous SteamCMD downloaded these packages at 09:00 UTC on September 20 without changing subscriptions, logging into the user's account or using UI input. Acquisition commands, owned process result and complete output remain in `%TEMP%/hd-f08-20260920/content-*`; short-copy equality is recorded in `package-copies.json`. The bootstrap-only initial attempt is retained separately and is not presented as the download.

## Finite scenario

`F08-ALERTS` opens the real Custom Alerts editor and current-action dropdown during native Repaint. It observes the default available-only absence, invokes the actual all-options and HD selection actions, then notifies the holder as the native option does. Both screenshot states remain unchanged through frame-end capture and require nonempty PNGs. This is programmatic use of native UI, not physical button/keyboard input.

After the normal 600-tick startup gate, observation is restricted to actual `AlertsReadoutUpdate` → own `CheckAddOrRemoveAlert` → own `GetReport`. The fixture never calls GetReport. Fresh scheduled evaluations verify idle false, actually queued BulkHaul false, real gathering true, interruption false, renewed gathering true, completion false, actual explicit one-stack pickup true, and distinct native HaulToCell false. Native/product jobs move plasteel into real storage; quantity conservation and culprit/current-job/queue identities are recorded. No passing condition manufactures pickup/delivery effects.

One actual `F08-ActivityAlert.rws` saves the enabled inactive alert and two real unhauled sources. Parked quicktest pawns are deep-owned by native WorldPawns, preventing the unsaved-reference mistake previously caught in the PB fixture. Full save/log review still matters. Scenario bounds are 240 wall seconds and 18,000 native ticks; existing host and owning launch bounds remain 300/360 seconds.

`F08-RESTART` uses a fresh private process and native Autostart, no quicktest. Its first lifecycle callback must be LoadedGame. Before changing settings or dispatching work it requires exactly native saved-tick +1 and PauseOnLoad, saved stable JobDef, enabled/count/delay/map configuration, one live registration, actor/zone/source IDs and material total. Native PauseOnLoad performs that one tick; no clock reset or field restoration conceals it. The loaded alert must be inactive, activate during real gathering, then become inactive after physical storage.

Cleanup restores captured HD/TD/global-alert settings, speed, owned windows and observers. Producer cleanup removes its own alert/objects/zone and returns parked pawns to map ownership. Terrain/Home setup is disposable scene state, not full rollback. Restart retains the loaded scene in the disposable process. No user save/config is touched.

## Reused build and operation

The F33 host and F45 restart controller are reused. Only Bootstrap/project change among existing host files; controller helpers/About remain unchanged. The main controller adds two cases, seven packages/17 images and producer-bound save input. `integration.diff` and `qa-corrections.diff` show the deltas. The finite owning `launch.ps1` is the existing F33 entry point with only its local variable/message substitutions. No new execution framework was added.

Build: `./docs/plans/evidence/f08-alerts-20260920/build.ps1`. Outputs are `%TEMP%/hd-f08-20260920/build/Assemblies`, using existing SDK/references and accepted HD32D57A64/CoreD623F164. This selection predates the separate F33 repair; final assembled-build smoke checks remain separate. No deployment occurs. The initial missing-namespace compiler failure and first successful QA predecessor are retained in Temp.

Prepare uses the staged `%TEMP%/hd-f08-20260920/Product` content package, not the build workspace. Its 65 files (63 mod content files plus two matching PDBs) are byte-equal to the original frozen content/input pins and actual reviewed build outputs; complete paths/hashes are in `product-content-copy.json`. `prepare-admission-note.md` retains the original path-admission failure and diagnosis. No guard was weakened and no original intermediate was deleted.

**Root alone launches, only through `scripts/run-on-test-desktop.py` on the never-activated desktop. Never invoke Launch directly on Default.** The controller's inherited generic nextAction is not the execution recipe. Serialize all native tests.

```powershell
$f08 = Join-Path (Get-Location) 'docs/plans/evidence/f08-alerts-20260920'
$controller = Join-Path $f08 'controller/scripts/runtime-test.ps1'
$inputs = @{
    HdSource = 'Built'; BuiltModRoot = "$env:TEMP/hd-f08-20260920/Product"
    PlayerSaveDataRoot = 'C:/Users/Arthur/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios'
    HarnessAssembly = "$env:TEMP/hd-f08-20260920/build/Assemblies/HaulersDream.RuntimeHarness.dll"
    ExpectedBehavior = 'satisfied'; NegativeControl = 'None'
}
# Retain the actual returned fresh GUID/result and review prepared state.
$producer = & $controller -Action Prepare -CaseId F08-ALERTS @inputs
# After review, use unique nonexistent output directories:
python ./scripts/run-on-test-desktop.py --output "$env:TEMP/f08-producer-desktop" --cwd (Get-Location).Path --timeout 450 -- C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $f08 'launch.ps1') -RunDirectory $producer.runDirectory -OutputDirectory "$env:TEMP/f08-producer-owned"
& $controller -Action Verify -RunDirectory $producer.runDirectory
# Inspect producer result/events, pictures, whole logs/save references,
# process join and protectedChanges before selecting its actual checkpoint.
$restart = & $controller -Action Prepare -CaseId F08-RESTART @inputs -CheckpointSave (Join-Path $producer.runDirectory 'SaveData/Saves/F08-ActivityAlert.rws') -CheckpointRecord (Join-Path $producer.runDirectory 'evidence/checkpoint.txt')
# Review actual restart state; run the same inactive-desktop command with
# $restart.runDirectory and fresh restart output directories. Then Verify.
```

Verify retains its existing manual-review finding. Actual reports, pictures, whole logs, save/load/one-registration evidence and process/protected outcomes decide acceptance. Source/build approval does not close F08.

## Corrected built checkpoint

- Scenario: `ACAAE24C585FBA14D9156BF71204725CD6066C4046052BF9E304FACD811313EB`.
- Bootstrap: `6C6A1F97A0AA93AF50AC7DBA248043F10D77ECFF532E9BBC4E34A87942275649`.
- Controller: `EF11B2AA26B0B660870AB4F6139A22F9928D628C8D392E767B7A09D095F8AC54`.
- DLL: `1B545A52D928F1ABACCB30B765DBD15AAE7186A5437FF4785EE170F62C0D339A`.
- PDB: `029D8734CD379284A5DAE4E73C1873789777EB0AEC5F2C3A8823C41D909FD80C`.
- Actual reflection-only MVID: `c71e7223-304e-4736-8dcf-cffee8c6484b`.

The narrow QA correction retains UI states through capture, scopes observations to scheduled Update, and requires native LoadedGame first. No product behavior or acceptance scope changed.

## First actual producer and narrow window-check successor

Root's actual producer `3fff9aeeb94544ee9d176b0d773dbced` failed at GUI cleanup after the real all-options/HD selection and both screenshots succeeded. Its retained raw evidence is under `native/3fff9aeeb94544ee9d176b0d773dbced`; it produced no accepted scheduled-job result or checkpoint. The original guard compared the entire WindowStack from Setup with the stack several native Repaints later. Native ImmediateWindow wrappers may change between those frames. The original run did not record the differing window, so no specific window identity is asserted as its cause.

`window-correction.diff` contains the sole scenario change: compare every current unowned window synchronously across closing the owned editor/prompt, and separately preserve the initial non-Immediate windows across the GUI sequence. It does not clear/close/whitelist unowned overlays. A mismatch records actual before/after window type and object identity. Screenshot retention and every scheduled/job/save assertion are unchanged.

The previous scenario and complete build outputs remain in `%TEMP%/hd-f08-20260920/pre-window-fix-3fff`; the failed runtime is untouched. The successor built with zero warnings/errors using the same isolated build. Current scenario is `A533456F449C7F56090B799BF272173D518E7F0117B58BF8699791AFB579DE24`, host DLL `42E4C89EB7477192023152F747E056FA76D8B0569594A2EFD8834A552B5E7932`, PDB `F9FF735FEE7D7F19075C8DD786EAF5C531C170C8F0FE29AA108C2412B7C9B509`, actual MVID `e82572b9-60d6-4e16-bda9-9618977b3e3a`. Product, Bootstrap/project, controller and package inputs are unchanged. Actual successor metadata is `build/window-correction-metadata.json`; the earlier `build/metadata.json` is historical and describes the retained predecessor. Root must use a fresh prepared run after focused review; no successor native result is presumed.

## Second producer and actual queued-order control

Actual producer `e681e9d1d8324f7ba493ba353804d108` passed the GUI/window checks and scheduled idle-negative observation, then failed the queued-only assumption: the idle Wait was replaced immediately by BulkHaul and the real alert became positive at tick 613. This is native queueing behavior, not a demonstrated HD defect. The retained native `Pawn_JobTracker.TryTakeOrderedJob` sets `flag2` for idle/current-isIdle and starts the order despite requestQueueing; Core Jobs_Misc.xml marks Wait as isIdle. Native Goto is not idle.

`queue-correction.diff` changes only the fixture control: order a real finite Goto across the cleared area before requesting queued Sweep, record immediate current and queue IDs, and require the scheduled negative while Goto is actually current and BulkHaul is actually queued. It then waits without changing jobs for native Goto to finish and the exact queued bulk job to start. The earlier forced EndCurrentJob is removed. Goto uses normal walking and a 1,200-tick expiry; existing wall/tick limits remain. No job flags, material effects or query results are fabricated. The handoff records actual position but does not impose an exact arrival-position assertion after potentially multiple native ticks before host Update.

The failed source/full build remain in `%TEMP%/hd-f08-20260920/pre-queue-fix-e681`; root retains the failed runtime/result. Current source is `84F54D7BC0B673399B65D832DA38387D8A40DE7CC72E4C01E9CC33CF980436FC`, DLL `5DDF7BC97BD8EDFB7A88C398267B0E2B05EC0F12F8E357DB188B9A0B01D03F4D`, PDB `89A0D226C259E5F91B3FDA69E18066A819471FED05370E5A3A59A6BD68606EB8`, MVID `fd35eb50-4826-412b-82a5-3215a78c663f`. Build passed zero warnings/errors. `build/queue-correction-final-metadata.json` is the current actual metadata; previous metadata files are historical author checkpoints. Product, controller, Bootstrap/project and package inputs remain unchanged. Scheduled productive stages and the saved-alert restart still require actual successful execution.


## Native Lord ownership correction after producer 7b5b

The full run reached all distinct activity/material outcomes and saved 40 plasteel (30 stored and two five-item sources), but two native LordTick errors correctly made its result fail. A private map Lord cannot own free world pawns. Root inspected the actual native RemovePawn method, then changed only fixture setup to release membership while the pawn is spawned, check both ownership endpoints and all map-Lord lists, and record the pawn/former-Lord identities. No game error or activity assertion is suppressed.

This is a disposable scene: cleanup restores original pawn placement/world ownership and captured settings, but does not reconstruct former Lord memberships/duties or reverse terrain and biological changes. The original source/full build is retained at `%TEMP%/hd-f08-20260920/pre-lord-fix-7b5b`; all three failed raw runs and their logs remain intact. `lord-correction.diff` is the exact source delta.

Current scenario SHA `A67850B8BD305BEBABCDD5627F1B500470B6B9946226E602D0E62EB3AA461160`; actual DLL `7A592D21BD45F518A61E3889E10B03C15F51A16B06C7CA0753C02F04757FE669`, PDB `011EAD91596E9284D5CDB5F0D10B83562E966E753EC9A7121B677790AF628E1E`, MVID `4fc37a3c-8bca-4a26-ac99-985770c1c9c2`. The rebuild passed with zero warnings/errors; selected-source/products/build.log and `lord-correction-metadata.json` are under the existing Temp build directory. Independent source/build approval is appended in `source-review.md`. Product, Bootstrap, controller and every activity criterion are unchanged.
