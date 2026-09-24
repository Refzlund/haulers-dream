# F08 fourth producer: independent acceptance

**Accept `bfd06365cfae49749406579ffb90dd78` as the producer for one fresh checkpoint restart.** Actual terminal status is passed, zero captured Unity errors and no failed assertion. Runtime host DLL `7A592D21` / MVID `4fc37a3c-8bca-4a26-ac99-985770c1c9c2` matches the accepted `A67850B8` scenario build. Full retained evidence is in `native/bfd06365cfae49749406579ffb90dd78/`; the actual save is that run's private `SaveData/Saves/F08-ActivityAlert.rws` under `%TEMP%/haulersdream-runtime-tests`.

Native PID 21908 and controller PID 9632 joined exit 0 with no deadline. The desktop record shows the native window on the private inactive desktop, input remaining Default, no switch, no cleanup error and no remaining owned process.

## Actual outcomes

Reviewed all 7,351 contiguous events, the result, logs, saved XML and both native PNGs. There are 17 distinct F08 assertion IDs and 460 actual scheduled alert observations. All scheduled observations agree with current job identity: BulkHaul yields active/count one/the actor as culprit; other jobs yield inactive/count zero/no culprit. The 6,851 assertion rows repeatedly include earlier observations on subsequent frames and are not independent test cases.

| Check | Observed result |
| --- | --- |
| Discovery | Native current-action menu defaults to None/standing/ALL OPTIONS. Original all-options and selection actions select exact `HaulersDream_BulkHaul`. |
| Idle and queued | Wait is inactive at tick 607. Goto 28 remains current while bulk 30 is actually queued; scheduled tick 620 stays inactive. |
| Natural handoff | Goto reaches `(138, 0, 112)` and hands off to bulk 30 at tick 1259, before expiry and without forced termination. |
| Activity versus possession | Bulk is active; five units physically enter inventory before interruption. Wait is inactive at tick 1815 despite those five units remaining in inventory. |
| Productive completion | The next bulk operation plus native unloading stores all 20 created units; completed Wait is inactive at tick 2676. Separate unload jobs themselves do not match. |
| Shared-family activity | Actual single-stack pickup also uses BulkHaul, matches while active and stores its five units. |
| Distinct hauling | Actual native HaulToCell does not match and delivers its additional five units. |
| Save | At tick 4049, actor 23369 is in Wait 109 with empty inventory. XML contains one enabled `F08 activity witness`, active Everyone/ChosenMaps Map 0 query and stable `HaulersDream_BulkHaul` refName. Stack 23373 holds 30 stored units; fresh sources 23388/23389 hold five each: total 40. |

Both PNGs were independently viewed. `available-options.png` visibly shows the real editor and three-entry default menu including ALL OPTIONS. `selected-activity.png` shows the selected gathering activity; the narrow native button abbreviates its text, while the recorded full label and definition are exact. These are native rendered states reached through original programmatic actions, not physical input.

The accepted source's all-parked ownership guard completed. This particular generated scene emits no non-null Lord-release event, so it does not independently exercise that branch. Its saved world-pawn identities have no overlap with the remaining map Lord's owned list, and no ownership error was logged. Cleanup finishes without an assertion failure; its claim remains placement/world ownership plus owned fixture cleanup, not restoration of former AI duties or cleared terrain.

Whole-log review finds the two early Mono fallback notices, texture mipmap notices and a Direct3D timing notice. There is no action exception, free-world-pawn ownership error or missing saved-reference diagnostic. The HD log agrees with the real gathering/unload/native hauling sequence. Actual Verify has no protected changes and remains `not-verified` for its fixed independent-review/restart requirement, generic missing `scenario-observed` marker and those two log candidates. The specific complete F08 events supply the semantic evidence; no retrospective event insertion or extra run is needed to satisfy that generic marker.

## Support disposition and remaining scope

The README and COMPATIBILITY F08 additions correctly explain **current action → ALL OPTIONS → gathering items into inventory**, the available-only default, current activity rather than queued work, the shared single-pickup family, and distinct unload/native hauling. The unchanged JobDef identity supports the old-label explanation. No WorkGiver or new integration architecture is needed for this report.

One actual restart from this exact successful producer remains necessary to verify restored query settings, exactly one live registration and scheduled idle/active/completed behavior with the saved physical sources. Producer serialization alone does not establish that. The three earlier producer failures stay failed and excluded from restart input. Final assembled-build physical-input smoke remains separate from this report-specific support disposition.
