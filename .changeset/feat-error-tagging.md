---
"haulers-dream": patch
---

fix: every message, warning, and error Hauler's Dream writes to the log now carries the `[Hauler's Dream]` tag from a **single source of truth** (so the tag can be changed in one place), and a universal breadcrumb is attached to **every method the mod patches**. If an exception passes through Hauler's Dream's code, it is now logged with the tag — identifying that the mod is in the call stack, *without* falsely claiming the mod caused it — and then **re-thrown unchanged**. Errors are never swallowed or downgraded; the game still reports them exactly as before. The breadcrumb is logged once per method so a per-tick fault can't flood the log.
