# Independent bound restart and robot closure review

24 September 2026. **Accepted: the final bound station restart recovers the original robot and cargo through native work. Recommend F05/F06 feedback-specific closure, with final assembled-build/input/multiplayer integration remaining on its existing separate rows.** No new runtime, Prepare, product/index edit or ledger change by this reviewer.

Read all50 passing assertions,91 sequential events, complete HD debug log, full Player.log with startup/load/shutdown candidates distinguished from profiler/allocator text, fixture load/recovery/cleanup source, manifest, process/desktop receipts and Verify. `independent-runtime-audit.json` and reproducible `../../source-review/audit-restart-3ebbb.py` retain exact hashes and checks. All twelve expected image SHA/MVID pairs match runtime results and copied DLL bytes; every retained raw file equals its original private evidence. Exact six-mod profile excludes Biotech. HD `A20FBD9F...`, Core `1A30CF3C...`, host4668E81E/MVID4e49276f and both providers match the preceding accepted lifecycle/menu chain. Installed rev590 and executing rev591 remain distinct.

## Genuine save and exact loaded custody

The restart input is byte-identical across the original upgrade run's SaveData checkpoint, its retained raw checkpoint and this fresh run's Autostart.rws: SHA `A6C191B9AE8BE0CFBAE59C851B1E67D692637FE0E5838FC891EEBCE373FF52C3`. The original/retained/copied checkpoint record likewise agrees at `69D242FCB607BF1C5629FFABD121655E607B1AE52FCA59DCE11F4337D870814A`. XML independently contains one station38287 deep-owning exactly pawn38293, inventory Plasteel38294:7 plus42091:10, external floor42092:10, the two expected tracker references, per-def Keep7, no current/queued job, null station active-robot reference and autospawn false. No save/comp removal or reconstruction substituted for this state.

The preceding upgrade producer5b56 is separately accepted55/55 by root, using genuine published7a51 as its input. Its native recall interrupts partial command Bulk6, returns through AIRobot_GoDespawn7 and saves the contained same pawn at610. The known post-save provider cleanup warnings remain in that producer's record and do not alter the checkpoint.

Fresh process18844 loads that exact native save; LoadedGame occurs at610 and paused first observation at611. Before fixture ownership/settings restoration, native custody, original roles, exact inventory IDs/counts, both tags, Keep7 and exactly one resolved HD component pass. The actual station Spawn callback reactivates that same pawn at611, preserving its inventory and role list. The fresh process starts after the producer joined; this is a full native restart, not an in-process reload.

## Actual recovery and stability

No new explicit hauling command is issued after reactivation. Native work selects HaulToCell10 at613, moves the untouched floor42092:10 into hands at795, and succeeds with those ten stored at1079. Existing inventory remains17. Native AIRobot_GoAndWait12 and Wait24 follow. Automatic unloading queues30 by3001; the existing native wait ends at4403, and Unload30 starts from the native queue at4406. The fixture did not shorten that wait or force an unload.

Unload30 moves original personal stack38294:7 into hands at4409 and merges it into storage at4752, then deposits another three units at4754. The final physical state is **20 stored in42092, seven kept in inventory42091, empty hands/queue, total27**, with the same pawn and original roles. All15 changing physical-state observations independently conserve27. Keep preserves a quantity across same-kind stacks; it does not preserve the original personal stack identity after native transfers/merges.

Unload30 ends Incompletable at4757 with only the kept remainder tagged. As documented in the accepted no-Biotech and Biotech reviews, the pinned driver's no-next-surplus branch gives this enum for a keep-only tracked remainder. Exact physical completion and no subsequent cargo work establish recovery here. Native AIRobot_GoAndWait44 follows. The stable stage4767→5074 spans **307 native ticks**, continuously settled with one component/original roles and no repeated Bulk, unload, HaulToCell or HaulToContainer start.

## Logs and ownership boundary

Zero captured Unity errors. Whole-log review retains the existing English4 translation notice, provider/HD texture dimensions, Mono dynamic-library probes and graphics timing notices. There is no unresolved save reference, serialization failure, gameplay exception, repeated recovery loop, power-net warning or contained-robot cleanup warning in this restart. This is not a clean-whole-log or localization-fix claim. Shutdown allocator counters and profiler labels are separately classified in the audit.

Cleanup passes at5074, removing owned runtime state/observers and restoring settings, selection and parked-pawn ownership. Disposable terrain/Lord duties are explicitly not reconstructed. Native18844 and controller17448 join exit0, with empty stderr, no timeout, no desktop switch, Default-only input samples, no cleanup errors and zero owned processes before controller close. The native process receipt's executable field is null; exact private runtime bindings are independently present in the executing assembly paths, command-line log, manifest and actual process/run ID. Verify reports protectedChanges=[] and only its explicit independent-review/whole-log flags. Its post-exit process lookup absence is consistent with the joined receipt.

## Combined feedback-specific acceptance

The bounded remaining-acceptance audit is now satisfied by the preserved evidence, without expanding the original unspecified robot report into every robot tier or unrelated feature:

- Automatic inventory hauling, native role exclusion, no-Biotech gizmo/Targeter, cancellation, direct/queued/interrupted commands and stable recovery: prior accepted no-Biotech Omni/Builder61/61 run071295, plus the earlier X2 Hauler witness.
- Actual Biotech native menu composition, setting and specialist role negatives, offered callback and productive20+Keep7 delivery/stability: independently accepted6147,52/52.
- Genuine published save with zero robot HD component: independently accepted7a51,49/49; provider cleanup warnings preserved.
- Native component installation into that original contained pawn, actual partial pickup/recall and contained current-product save: root-accepted5b56,55/55; provider cleanup warnings preserved.
- Full bound restart, same-pawn station reactivation, kept/tagged stock and natural recovery: this independently accepted3ebbb,50/50.

Shared human command controls are accepted under F07; Builder construction is separately accepted F14. The focused24-file robot selection is independently reviewed and compiles as HEAD+slice with zero warnings/errors. That isolated commit build has not itself run natively, and final integration retains this distinction. The evidence supports marking the two reported robot feedback items resolved now; the remaining final-PR validation must not erase these accepted results or trigger another broad robot matrix absent a relevant change.
