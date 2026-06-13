---
"haulers-dream": patch
---

Stopped hiding errors. Hauler's Dream previously wrapped most of its logic in broad `try/catch` blocks that either swallowed exceptions outright or downgraded them to one-time warnings (or verbose-only debug lines) — which meant real bugs and mod-interaction issues were silently buried instead of being reported. Every one of those has been removed: errors now surface as normal red errors in the log so problems can actually be seen and fixed.

This changes nothing about how the mod behaves when everything is working — it only affects what happens when something goes wrong (you now find out about it). Three deliberate, non-suppressing exceptions remain: the Combat Extended bridge still cleanly detects when CE simply isn't installed (via existence checks, not a catch); a single guard around third-party WorkGivers logs a red error naming the culprit mod and skips just that one (so one broken mod can't break the route menu); and the batch-crafting safety net still restores in-flight items before re-throwing, so a mid-craft failure can never lose items. If you see a new red error after updating, that's by design — please report it.
