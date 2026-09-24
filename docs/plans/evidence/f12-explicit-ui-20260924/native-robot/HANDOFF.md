# F12 supported-robot UI handoff

Ready for root source review; no Prepare/native run. The human UI fixture and product UIv2 remain frozen. This separate profile adds one actual supported-robot source-targeter flow and two rendered disabled-gizmo controls.

Use `CONTRACT.md`, `src/F12Scene.Robot.cs` and `source.diff`. The actual public Misc. Robots `CreateRobot` factory creates native Omni I and Builder III; its retained source is in `native-source`. No component, role, native permission, inventory behavior or health is replaced. The no-DLC Omni must have its actual Hauling priority2, exactly one HD comp and native CanTakeOrder=false. The product's supported-robot exception must admit the real comp gizmo.

Actual window input first clicks the disabled source gizmo with mech hauling disabled, then the real BuilderIII gizmo without a hauling role. Neither may create a targeter/order/job/reservation/cargo change. After restoring the setting and selecting Omni, actual source-gizmo → source map click → quantity7 → confirm → destination map click must reach one authoritative command and physically deliver7 from15. Source8, personalWood5, unrelated stock, original role and component count remain; the original job must succeed. The deliberately forbidden source prevents unrelated autonomous pickup of its remainder and uses the already supported forced-source rule. Retain the actual robot gizmo/source-prompt/completed-progress screenshots.

This does not rerun the human invalid-input matrix, station UI, robot save lifecycle or Biotech menu profile. It claims no network result. No provider files were downloaded or modified: the full1,366-file packages are the already accepted private F05/F06 inputs, each verified and protected by this controller. The controller admits only `F12-ROBOT`, exactly six mods/twelve images.

- Unchanged UIv2 HD `64D2B8A20A1F42D079EC643F0EA14414B62B6BAAA63CBC07E996D4F94B0B7602`; Core `2633530022F02B73D09A11254E088E6DE52D6F1B05A30C2E0233790B642BA4AA`.
- Host `8F2E6C24D4C451FF9903F57175F36AC875C95999C971426CFEF37BB052913D70`; MVID `ab831fee-cc74-4240-b623-be8d3f05ee97`.
- Selection `519CF81ADC0646EFEBBF2FC4D90004F2C2958F4477C7273CC44F9518D4D57503`.
- Diff `FE2E27B6F89B10C240A2B49BD50F0CBF5A770300EF788ECE2A7DE0362B3703D3`.
- Build3.84s with0warnings/errors;1,852 source/provider/build/preservation checks;21 controller/launch/build parser checks.

Root uses `TEMP/TMP=C:/HDQA/runtime-temp`, `controller/scripts/runtime-test.ps1 -CaseId F12-ROBOT -ExpectedBehavior satisfied -HdSource Built`, and the exact selected candidate/harness paths. The existing `launch.ps1` must run on the owned inactive desktop so its inherited input worker uses that same desktop. There is no activation or input-desktop switch. Full native/input/process/image/log review remains required.
