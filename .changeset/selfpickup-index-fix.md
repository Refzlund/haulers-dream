---
"haulers-dream": patch
---

Fix ArgumentOutOfRangeException in self-pickup when stale entries are pruned below a valid one.

TakeNextValidPending tracked the nearest valid pending drop by its list index (bestIndex), then pruned stale entries with RemoveAt(i) inside the same backward scan. Removing an entry below bestIndex shifted every entry above it down by one, so bestIndex silently drifted past the end of the now-shorter list — the post-loop pendingSelfPickups[bestIndex] threw ArgumentOutOfRangeException. This crashed every self-pickup toil init (harvesting, mining, area sweeps) whenever a stale entry sat below a valid one in the queue. Track the Thing reference instead of the index; Remove(thing) is immune to the shift.
