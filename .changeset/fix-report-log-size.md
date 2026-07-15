---
"haulers-dream": patch
---

Increase Player.log attachment from 400 KB to the backend's 5 MB cap

The report system was attaching only the last 400 KB of Player.log, which truncated
away the first occurrences of critical stack traces in heavily-modded saves (e.g.
issue #207's HD bulk-haul and alert NREs were only visible as "Duplicate stacktrace"
markers). The backend accepts up to 5 MB per log, so this increases the tail to that
cap — capturing the full Player.log for most saves.
