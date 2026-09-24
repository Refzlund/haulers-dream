# Nearby hauling command — robot support and shared admission

Status: corrected implementation and exact compiled candidate independently reviewed; focused gameplay and UI verification pending. C424-S02, C424-S01's intake boundaries and C426-S01's preserved construction control remain open. This is part of the single feedback PR; it does not close RIMMSqol drafted discovery or Custom Alerts.

The native robot census established that the actual base-game Misc. Robots family has inventories but lacks HD tracking and cannot pass the native CanTakeOrder selection filter without Biotech. The provider also independently declares MechanoidCanDo=false. Component attachment and role admission are separately implemented and reviewed; this command work supplies the player action. Its first source draft and19 original files are retained in `TEMP/haulersdream-nearby-command-root-20260908`.

## Intended behavior

One command boundary serves the ordinary item menu and an HD hauling targeter for a single selected Misc. Robots robot. Native CanTakeOrder and other providers remain untouched. The menu admits hauling-capable mechanoids to the shared checks; the robot button works with or without Biotech. Specialists without a positive actual robot Hauling role receive an explanatory disabled state. Construction gathering, personal pickup and existing cargo recovery keep their separate routes.

The actual player/faction/host state, current map, live item, hauling/manipulation capability, tracking component, inventory/carry trackers, feature/race/map settings and current storage feasibility must all agree. The same conditions are checked again when issuing the order. Robot metadata failures receive an explicit unsupported reason. The human allowIncapable option cannot grant a native combat mech a new hauling work type.

Explicit player orders retain the existing override of the automatic master and per-pawn auto-haul preference switches. Bulk/nearby/race/map settings still govern this feature. Ordinary forced-work behavior and successful single-stack/capacity fallbacks remain available only after current command permission is established. A role or feature rejection must never fall through to a native haul.

Local UI dispatch validates captured actor/source/map before multiplayer argument serialization. Authoritative synchronized execution independently revalidates the order; it never trusts local selection as shared world state. Registration is separate and unavailable commands fail closed in multiplayer. Queue-key state is captured locally and carried as a bool. A queued explicit nearby order must reach native queue handling; ordinary second-task takeover retains its existing contract.

The target picker is bound to its actual actor, with local current-map/single-selection checks and native cancellation. No target action may silently continue controlling a formerly selected robot. Cancel creates no job or reservation. Queries do not construct a Job or mutate the robot's role lists.

## First independent findings and reviewed corrections

1. The first availability query did not use the forced source priority or existing forced-order scope. It now balances that scope in finally, uses the forced priority overload and retains the non-random storage search. Native forced storage permission and current building filters are preserved.
2. Existing bulk takeover runs before native requestQueueing handling and can immediately merge/interrupt a queued explicit nearby order. An exact pawn/job scope now bypasses takeover only during this command's queued order; finally restores the previous nested scope. Other takeover behavior is unchanged.
3. A spawned Thing on another map can make MP argument serialization fail before IssueSynced reaches its internal checks. A local dispatch wrapper now validates the actor, selection, source and map immediately before the registered method. Replay independently revalidates shared world state.
4. Native Command_Target starts its picker with caster=null. The command now binds the actual pawn to the native targeter and validates local actor/map/single-selection lifetime without client-local selection checks in the synchronized body.
5. The item loop now continues past unavailable overlapping candidates and emits a deferred disabled reason only when no candidate succeeds.
6. The robot command is now offered before the existing tracking-component exit so missing tracking can display a disabled reason. The existing unload/toggle guards remain intact.

These are source findings, not observed native reproduction results. Fresh independent review `26A9A1BB617FAE5CB01AA23C07901BBE1DF63F24CD44D401926D32792D4A7C75` read the complete correction and relevant actual IL, and found all six addressed without a new source blocker. Root read the full review. The frozen report is in `TEMP/haulersdream-nearby-command-corrected-independent-20260908/final-review.md`.

The isolated build finished on 8 September at 15:00:05.5790736 UTC with zero errors and warnings. All359 inputs match their isolated copies and working sources; all356 authored C# inputs match361 PDB documents including five generated documents. HD is `FC67EE36892115FF45E869761C40C609C84703EDC5D3F00A3F3F9DC1CC6FF8EA`, MVID `945044fd-27fc-4ba5-ad62-423f7792dbee`; Core is `9205B96D1D871894EAC491A7CEBD0020381DCB6827356CBA7CEA19086A8DC85F`, MVID `f225b930-c0c5-40e6-adf9-71b42db75756`. The independent review checked1107 preservation pins and actual native call signatures. Working assemblies were preserved and deployment remained disabled. This supports proceeding to focused runtime QA; it does not establish actual patch installation, robot orders, rendered UI or multiplayer replay.

Translation review `731B63EE1ECA917A6F3CDF5C08FB7F8816B5DFFA1AB524A2D8ABA56F8A7F0ACF` covered all128 new values and16 description additions. Root applied its17 small corrections: personal-inventory terminology in Simplified Chinese and neutral source-unavailable wording in all16 languages. Fresh independent correction review `ADA79AE9E39904CA4E6CD9AEA7A30AFD898544E23A6A11DBA2EA6D7600E4D51B` passes274 checks and preserves13,423 other keyed values and surrounding bytes. Root read the full review; the855-key/placeholder guard also passes. Rendered evidence remains pending.

The private candidate content is staged in `TEMP/haulersdream-nearby-runtime-content-20260908`, manifest `67FDBD736CFF15057CF27AB2DC15F1FB8E876078C93D6C5AE56B163A54E7E483`. Independent packaging review `E6E23C4048AFC9D48B767F3E5FEC2A3A5E53B3B29962F90F972F3210669C5789`, read fully by root, confirms63 files/5,128,564 bytes, exact FC67/9205 assemblies, FA61 robot patch, corrected translations and359 build-source joins. All working assemblies remain unchanged. The package has not yet been consumed by a game run; R1 automatic robot work remains in fixture preparation.

## Required evidence

- Actual Misc. Robots base-game selection/button/targeting/order and Biotech-enabled native menu; observe the selected pawn, source, resulting JobDef/driver, real pickups/unload and quantities.
- Hauler/Omni positives, Builder/Cleaner role negatives, native lifter/combat-mech distinction and a human control. Preserve actual robot construction gathering and its leftover recovery separately.
- Change role, component/trackers, settings, faction/host state, mental/downed/draft state, pawn map or source ownership/map between offer and click/replay. Reject without another action or a native fallback bypass.
- Valid overlapping source behind an unavailable one; missing/forbidden/unreachable/full-storage inputs; legitimate single-stack and constrained-capacity fallback. Capture actual scope of the fallback rather than claiming every order swept multiple stacks.
- Existing loading bulk, forced solo haul, unrelated work, idle, noninterruptible and queued work: queued explicit orders follow the native queue and ordinary second-task takeover remains functional. Scope restoration must survive exceptions and nested unrelated calls.
- Targeting cancellation, map/selection changes and stale source movement before multiplayer dispatch. Actual MP command registration/replay and single-player-with/without-MP remain separate tests.
- Current save/reload and full restart, station return/cancellation, reservation release, physical item conservation and subsequent useful work. No broad role change or control override.
- Review the new8 keys in all16 languages and the updated nearby-setting description, then inspect actual rendered labels/reasons/button/help. The855-key parity check passes; it is not rendered or linguistic acceptance.

The source register's original robot and command integration criteria remain authoritative. Independent approval, compilation and a menu row alone are insufficient to close them.
