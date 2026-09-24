# First restart Verify — root-observed tool output

Actual invocation: `controller/scripts/runtime-test.ps1 -Action Verify -RunDirectory C:/Users/Arthur/AppData/Local/Temp/haulersdream-runtime-tests/94f2757e99a4411e96d02482c4103450`, root exec result chunk `3cb0a6`, exit 0. This is a retained summary of the actual tool output, not a serialized copy of its complete return object or an independent second verification.

Returned `status: not-verified`, `processStatus: exited`, `protectedChanges: {}`. The sole process lookup error was `NoProcessFoundForGivenId` for the already joined owned PID 22940. Problems were the standard requirement for independent restart/load/work/conservation/log review and the presence of Player.log review candidates. The two log candidates were lines 106 and 107, Mono fallback-handler dynamic-library notices. No other protection or automatic scenario failure appeared.

Returned result: `status: passed`, `unityErrorsObserved: 0`, case `F45-PB-RESTART`, actual game 1.6.4871 rev591 / installed Version.txt rev590, run 94f2757e99a4411e96d02482c4103450. Actual native ownership/outcome and raw assertions/events/log remain separate evidence. Independent semantic review must use those actual files; this record alone does not accept the run.
