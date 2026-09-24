# F40 v5 original producer review

**Accept this original checkpoint for the bound RESTART and DISABLED consumers.** Run `f4ec60841bac4561a711e236a8c6b8e3` passes 56/56 assertions, with zero captured Unity errors and 102 contiguous events. This accepts actual producer provenance and saved state only; it does not establish either consumer's behavior or close F40.

Authorship limit: this reviewer authored the earlier persistence contract/base fixture through v3, including its immutable job-start observer. I did not author the v4/v5 queue/spacing changes or the transporter product changes. I reviewed those changes and the actual native result separately; this is not an independent review of my entire earlier fixture authorship. Root separately reviewed the complete source and checkpoint.

## Source and actual admission

The v5 change moves the loader away from its source, requires an actual native walk before changing the live carry setting, and fails immediately if the original loader ends before saving. The existing checkpoint, XML and consumer equality oracles are unchanged. No driver count/progress assignment, health correction, substituted successor or native ref/out observer mutation was added. Start observations use `JobDriver.Notify_Starting` before instant toils and pooling; end observations capture the current job before cleanup.

At tick11, original load19 starts at `(90,0,137)` with Uranium40714 at `(94,0,137)` and plan35. The native immediate-reach check is false, cursor/delivered are zero and no uranium is held. The live setting transition to0.2 is recorded. At tick85, the original75-unit source becomes68 and actual split Uranium40721×7 enters the loader's inventory. At677 those exact7 units enter TransportPod40695, its demand falls140→133, and original19 remains current with count28/cursor0/pass1/delivered7. There is exactly one start and no end for19 before saving. Thus v4's completed-successor problem is absent through a real walk/pickup/deposit boundary.

Inventory and hands unloads54/55 are then issued at677. At701, read-only transfer observations show original Plasteel40701×20 moving into I's inventory and original WoodLog40710×75 moving into H's carry tracker. Their destination owners agree with the physical census and original save. The scene pauses and calls the actual native colony save at that tick.

## Original saved state

The independent reader reconstructs the entire recorded current/queued job, driver, Keep, tag, pod manifest, flag and session string from native XML, then reconstructs the entire physical-custody string. Both match the in-memory record exactly. Native default omission is respected; no XML is edited.

| Role / saved pawn | Position | Actual current and queued state |
| --- | --- | --- |
| I / Human40669 | `(93,0,113)` | Unload54, toil3, eight delay ticks left; cargo Plasteel40701×20, pulled20/delivered0, tagged and physically in inventory. Separate Plasteel40699×7 and Keep7 remain. Pod40693 retains160. |
| H / Human40675 | `(93,0,120)` | Unload55, toil10; WoodLog40710×75 is both actual hands cargo and `hdUtibHandTail`, pending-hands true, pulled75/delivered0. Kept Steel40700×70 stays in inventory. Pod40694 retains125. |
| L / Human40678 | `(130,0,137)` | Original load19, toil2, remaining original-source count28, cursor0/pass1/delivered7; its physical deposited7 remain in pod40695, whose manifest has133 left. |
| Q / Human40681 | `(111,0,128)` | Original non-idle blocker16, start11/expiry2200; queued load17 (Jade40718×10→pod40696), then unload18 (pod40697 Silver10), both forced and not yet started. |
| Z / Human40690 | `(127,0,139)` | Original non-idle blocker20, start11/expiry25000; queued load21 (Uranium40717×1→same group0 pod40695), unstarted; separate kept Steel1 remains. |

Flags are40693/40694/40697; serialized session groups are0/1/2. The nearby Gold group retains its10-unit demand without deposited cargo. All eight recorded current/queued/blocker jobs occur exactly once in the native Job namespace. All five measured pawns have empty saved hediff lists. The native room cache became ready at11 before these actors were created, and actual environment/health guards remain active; there is no medical bypass.

All four physical census changes and the saved XML preserve **Plasteel187, Wood200, Uranium201, Jade10, Silver10, Gold10 and Steel71**. The original saved background transporter contents also match their recorded custody. The original SaveData file, native evidence copy and retained raw copy are identical, SHA256 **57A0C7CA7F6B4BC20043F55CC971B4FB2920027BEF152F556A9392286C1FC9D8**, native tick701. Current/queue and physical state are unchanged by saving. Cleanup subsequently unpatches observers, stops private actors' jobs and restores the private climate input; it does not overwrite this original file or destroy the actors.

## Binding, logs and process custody

[independent-native-audit.json](independent-native-audit.json) passes 2,416 checks: all1,507 copied runtime files against recorded sources,256 selected pins,521 frozen product inputs,83 actual compile inputs, all10 actual images, retained raw bytes, original checkpoint, full XML reconstruction and process receipts. HD is `E62F1C1E…`/MVID `2066e160-2528-44f8-8a02-5813b0cb9c29`; Core `83EB8EB0…`; host `B9C41DF0…`/MVID `831b8caa-8db5-450d-a9aa-b29665c8cf6c`. Actual native Assembly-CSharp is `5CF1B5BE…`; its rev591 versus installed Version.txt rev590 provenance is retained. The four active mods are private Harmony, Core, HD and harness; catalog discovery of other installed mods does not mean they loaded.

The complete1,862-line Player.log was scanned and every non-profiler line, including shutdown allocator statistics, was read alongside the entire29-line HD log. No native exception, XML/reference failure, reservation-release error or repeated-job error appears. Two Mono dynamic-library fallback probes, the single Header texture-size notice, inactive-desktop Direct3D timing notices and allocator diagnostics remain recorded. Native3412 and controller18728 joined exit0; no timeout, desktop switch, cleanup error or surviving owned process was recorded, with Default input throughout.

Verify reports `protectedChanges=[]` and retains its manual-review, generic missing `scenario-observed` and whole-log flags. It is not relabeled automatically verified. The first independent audit attempt is preserved: it needed to expand native Scribe's short core-driver class name and restrict job-ID uniqueness to the Job namespace because unrelated genes/hediffs legitimately reuse numeric IDs. Only the read-only review script was corrected; source, runtime results, save and checkpoint oracles were unchanged.

Next: each fresh consumer must load this exact701 save and record, establish before-first-tick equality and process replacement, then satisfy its distinct productive recovery or disabled-start contract. Neither replay, delivery completion, zero-demand custody nor the subsequent live-policy phases is inferred from this producer pass.
