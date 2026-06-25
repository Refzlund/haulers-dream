---
"haulers-dream": patch
---

Fix a periodic stutter and a false log-writer error.

- **Periodic hitch with a shelf + items in inventory (issue #76):** the "cannot unload" alert re-evaluated about once a second and, for a single-pawn colony with the pawn outside the home area carrying tagged surplus, triggered a vanilla NullReferenceException inside `StoreUtility.TryFindStoreCellNearColonyDesperate` (its "spot just outside the colony" search). Catching that exception every recompute was the stutter the dev console deduped, so it looked error-free. Hauler's Dream now uses a safe, non-throwing home-area cell check for this probe instead of the fragile vanilla call. Normal colonies are unaffected.
- **False "disk debug log writer stopped after an I/O error" on quit/restart:** the background log writer was reporting the normal shutdown `ThreadAbortException` as a disk I/O fault. It now recognises the benign teardown signal and stays quiet; genuine I/O faults are still surfaced.
