# F07 independent producer runtime review

## First attempt — 136ffeff31894541abde074ab4c94ab4

**Retain as failed fixture execution; no F07 completion or product-failure claim.** Reviewed the complete result, all 60 ordered events, whole Player.log (2,093 lines, including 1,564 classified profiler timing rows), HD debug log, process outcomes, Verify findings, and the actual rendered PNG under `native/136ffeff31894541abde074ab4c94ab4/`.

The run has 44 passing and three failing assertions, one captured Unity error, and remains at tick 1. Discovery actually succeeded: event 52 finds exactly one real `HaulersDream_HaulNearby`, and `rimms-workgivers.png` visibly shows the native **Select Work Giver** page with **Haul everything nearby**. It does not show a successful edit or drafted command. No checkpoint save was produced.

The failure is the subsequent fixture-driven edit transition. The retained v3 fixture calls `SettingsPropertyEditPage.transition` from its root GUI observer after the native flow has rendered. Actual RIMMS `SettingsPropertyEditPage.transitionInternal` queues assignment of `flowScope["edit"]` as a post-render callback, then requests navigation. `Flow.DoFlowContents` processes navigation before rendering and only flushes those callbacks after rendering. Entering the next page before that assignment yields the recorded missing-key exception in its `onNavigationHandler`. `NavigateToEditing(properties, null)` successfully reached the selection page; attributing failure to that initial call or claiming discovery failed would be inaccurate. A correction should perform the real transition during the source page's native rendering lifecycle and await the actual next-page transition, without manually inventing its flow scope. The author/root own that correction.

The measured selected package contains RIMMSqol `1152B0C1` (1.0.9591.34971), Priority Queue `3B9ACE1D`, and legacy 0Harmony `E271D22A` (1.2.0.1). Events 44–46 report actual native bindings to RIMMSqol and Priority Queue plus the already loaded **modern Harmony 353DAAFE, version 2.4.1.0**, not a separately loaded legacy Harmony. Selected-file presence is therefore not evidence of a second active Harmony image. Five active packages and all 12 required runtime images passed admission. Actual HD/Core were `D0C7FBE0` / `186D8812`, host `03FB0B5C`. The executing game reports rev591; copied Version.txt says rev590.

Whole-log findings: two startup Mono fallback-handler notices, non-power-of-two RIMMS texture mipmap notices, routine profiling/allocation diagnostics, and the one matching editor exception at Player.log:1775. No additional exception, save-reference error, or HD job failure was found. The two-line HD debug log contains initialization only. Existing Verify correctly says `not-verified`, preserves the failed assertions/error and missing tick advancement, and reports `protectedChanges: []`.

Native PID 19308 and controller PID 2852 both joined with exit 0; this is process cleanup, **not a scenario pass**. The inactive-desktop receipt records `switchedDesktop=false`, sampled input desktop `Default`, no timeout/cleanup error, and no surviving owned process. No physical input claim is made.

Raw anchors: result SHA-256 `000D95D4D6AA7EAF19662A58BC75710F33E2B547A620A2C19996E915BBAA3103`; events `C9EC960EA37517C3A5B2F3D62AFB490B81B74C468B57C93BD35BBF724B931D61`; Player.log `7C530B19C4B150462CFDE13FF14D148B0266ED6F63827008FB25E254DF17F063`; PNG `AE04E3F026C5B65848255CB2431CAB10E6BB8A42D97F9A99759AF0F035D1932E`.

Remaining acceptance is the originally planned successful real editor/command/queue/material sequence and one actual saved in-transit restart. This review adds no matrix or new test obligation.
