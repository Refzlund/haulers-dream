---
"haulers-dream": patch
---

fix: Hauler's Dream no longer registers temporary cargo as a persistent Combat Extended forced-carry item. CE excess cleanup now waits while a pawn still has genuine HD cargo to unload, then resumes normally. CE generic refill counts are shared across matching items, while drop-only category ceilings do not contribute to HD's personal-stock keep count. Stale records created by older versions may need to be forgotten once in CE's UI.
