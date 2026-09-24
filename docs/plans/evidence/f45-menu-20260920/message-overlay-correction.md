# Native rejection-message overlay correction

Run `29ff367fb5b64259ad082802230d133c` remains a failed fixture run. Its first ten cells passed, including native TargetCount rejection with one false guard, one action return and no accepted-selection completion. The following cell stopped before constructing a menu because that rejection produced a native message window. The original run, v2 sources, DLL/PDB and controller inputs are unchanged.

The actual native callback is `Verse.Message+<>c__DisplayClass44_0.<Draw>b__0`, token `100683151`, capturing its `Message` through `<>4__this`. `Message.Draw` derives an ID with `Gen.HashCombineInt(ID, 45574281)`; `WindowStack.ImmediateWindow` normalizes it to `-Math.Abs(...)`. The observed `-1594957246` is one message's ID, not a stable ID for all rejection messages. Native `Messages` caps live entries at 12.

Only `PbMenuScenario.cs` changes in each successor. The new rule admits this exact native callback/module/closure, with a captured exact live `Verse.Message` whose Def is `RejectInput`, matching derived ID and Super layer. All existing nonmodal/input flag checks remain. The upper bound is the two previously admitted overlays plus the native 12-message limit. Unknown windows still fail without being closed.

These native overlays can process pointer actions; admission remains confined to the existing synchronous Layout/Repaint boundary, with no input synthesis. Per-operation reference/order/delegate/window-state equality and removal of only owned FloatMenus are unchanged. Native rejection may legitimately refresh an existing message's timer/flash; the fixture does not reverse that action or expire messages to obtain a pass.

| Successor | Build result | DLL SHA-256 | Actual MVID |
|---|---|---|---|
| Core `src-v3`, `Assemblies-v3`, `controller-v3` | Exit 0; 39 DTO CS0649 warnings; 0 errors | `81E997D3E2947CDD3A647E35F7949DCA7AFA1593F43322A30A7DFC1667CFA428` | `b823ba17-b443-46eb-a0d1-dde564f3a2eb` |
| Shared `f45-shared-menu-20260920/src-v2`, `Assemblies-v2`, `controller-v2` | Exit 0; 40 DTO CS0649 warnings; 0 errors | `2199D35F7A55BEBBA178FE4AC7020EF2B32E5F10D258EE366B0E4330190D2B5D` | `7ec501ae-5348-45e4-9bb0-a56edcfba608` |

Full build logs/results and reflection-only metadata are retained beside the source. The two `message-overlay.diff` files contain the exact correction. All six controller scripts and action contracts are byte-identical to their predecessors. Only their cloned data selections' source/script paths, companion DLL/PDB hashes and measured MVID changed; rosters, products, source trees and private-runtime protections remain identical. Every unrelated selected input was checked against its existing pin.

Core selection/manifest: `7BB6007A49D6FADE0BEE6657E638338A2155A99957D1D225D01F4DF760C0BBF3` / `6508B35AC9DFFED6BB072F62C859755308EA1BDAD8C5AF19F32493DD667E557A`.

Shared selection/manifest: `186E11629DA602953D26722616EBB7C99C00961F91A2A92CE32FB747E9FA7CAE` / `836D23E4188F31A0A350C182C75BA1BD6EC07C9951F5635A2A031F07234A8689`.

Reuse the existing bundled Python and operations, pointing at the successor controller:

```text
python.exe -E -B -X utf8 <core/controller-v3/run.py> Prepare --profile candidate-native --review <actual root review> --review-sha256 <actual hash>
python.exe -E -B -X utf8 <shared/controller-v2/run.py> Prepare --profile candidate-original-ego-cl --review <actual root review> --review-sha256 <actual hash>
```

The executable remains `C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe`. Launch and post-exit Verify retain the same `--run`, `--review` and `--review-sha256` arguments against the actual new run directory. No Prepare or Launch was performed by this correction task, and no new native pass is claimed.
