# F12 human UI v3 — ready for source review

Original `9508d293b4574c969d97a198f1d39b55` remains failed61/63,U0. Read its retained `../native-ui/native/9508d293b4574c969d97a198f1d39b55/INPUT-DIAGNOSIS.md`; the unfocused Return route is unresolved. No native game or Prepare was run by this author.

Read the complete 9KB `source.diff`. Only three scene/observer files change behavior: each stale control now requires the actual open stale dialog, Live=false, rendered changed-selection reason and disabled confirmation, then clicks the real text field before sending Return. The same actual control-local Return, closed dialog and unchanged command/order/job/queue/reservation/cargo predicates remain mandatory. Nothing accepts message posting or mere absence of a dialog as success.

Bounded read-only diagnostics record pending native keys at Root, native WindowStack entry/return and owned Window.OnGUI; owned Window.Close records native removal entry. They read actual Event type/rawType, native GUI focus/control IDs and current window/selection state. These observations do not increment `matchedInputs`, alter input or satisfy an acknowledgement. No GUI.FocusControl, event substitution or direct product/window action is added.

Frozen product stays HD64D2B8A2 / Core26335300. Controller, launch, input worker and all remaining scene/backend/physical/queue/progress checks are byte-identical to v2. Robot inputs remain untouched. `input-audit.json` verifies592 preserved/compiled/pinned/snapshot inputs, all passing. Build completed2.73s with zero warnings/errors.

- Host SHA `20441CE638C97528DF2DB3707925C031E77E86A8ED12FE6C8F46E79D1425D2F0`, MVID `2f4aeea0-b905-486b-b7c3-76b1a7f55843`.
- Selection SHA `7641FF12B47DDAF3523F603015FB6F47EF351A5A43D852D85BEFF05E8CB3C54B`.
- Diff SHA `A317F0E5E347F4E87220648D325903CD8D19FBC4E319374736232A24770CD366`.
- Exact source/build snapshots: task-owned `f12-ui-native-20260924/source-v3` and `host-v3`, full canonical paths in selection/audit.

Root preparation uses this folder's `controller/scripts/runtime-test.ps1`, case `F12-UI`, expected behavior `satisfied`, HD source `Built`, exact candidate/harness paths from selection, and `TEMP/TMP=C:/HDQA/runtime-temp`. Use this folder's unchanged private `launch.ps1`. Fresh native result, physical/UI evidence, complete logs and protected/process review remain required.
