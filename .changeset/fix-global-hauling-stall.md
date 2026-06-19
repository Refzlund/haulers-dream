---
"haulers-dream": patch
---

fix: colony-wide hauling/cleanup no longer silently stalls after some saves. The pre-save cleanup interrupted a pawn's in-flight bulk-load job *during* save serialization, which could tear the bulk-load claim ledger and leave phantom claims on reload — making the planners believe all work was already taken. The save-time interruption is removed (queued-job cleanup is kept), a load-time validator releases any orphaned claims to self-heal existing affected saves, and the work/haul/rest/eat/strip seams HD hooks now log a clear, attributed error (and still rethrow) instead of failing silently if anything throws there.
