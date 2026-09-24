# Independent v4 queue-blocker source review

Accepted for a fresh native attempt. This is source readiness, not runtime acceptance. The original v3 failed40/42,U0 capture837f0dd9… remains preserved.

The installed native Wait and Wait_Combat definitions are idle. Pawn_JobTracker.TryTakeOrderedJob deliberately replaces idle current work even when requestQueueing is true. Neither GH267's transport work nor final F07 behavior requires changing that native rule. Final F07 used a real Goto blocker; its old PreservesQueue state no longer has a consumer after authority-v10. The earlier provisional interpretation of this F40 failure as an HD queue defect is withdrawn.

Read all three v4 source hunks and the native Jobs_Misc.xml, JobDriver_WaitMaintainPosture, JobDriver_Wait, Pawn_MindState.IsIdle and tracker start/expiry paths. The correction changes only the two Q/Z blockers to the existing non-idle Wait_MaintainPosture definition, using genuine ordered-job admission and Misc tagging. The subclass only overrides posture initialization. Initial assertions observe the actual non-idle definition/mind-state, current job identity, native startTick and expiryInterval; no job definition, mind-state, native return or driver field is patched.

The exact blocker IDs are recorded with the checkpoint and recovered from its sidecar. ReadyCheckpoint requires both original current blocker IDs and the ordered queue. ZeroDemandCustody requires the original Z blocker plus the actual queued loader. Full state comparison now includes job start/expiry. Q's finite native expiration still determines when the saved load/unload queue executes; Z's longer native wait must remain current until the observed zero-demand custody check. Any inadequate scene timing fails those existing physical/identity requirements instead of fabricating a pending job. I/H and the unrelated interruption waits keep their prior behavior.

Cargo, native reservations, transporter operations, save/current/queue serialization and productive completion oracles are unchanged. No product, provider, controller, setting or F07 queue behavior changes are needed. Root retains Prepare/native ownership.
