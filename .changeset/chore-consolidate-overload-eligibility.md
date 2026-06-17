---
"haulers-dream": patch
---

Internal hardening (no behavior change): consolidated several pieces of duplicated logic into single sources of truth so they can't drift apart in future edits — the overload capacity-gate and its movement-speed penalty now derive their pawn set from one shared rule (guarded by a test so the "extra capacity costs speed" balance can't silently break), and the various hauling-eligibility job-def sets, carrier-liveness check, and "is Hauler's Dream active on this map" gate are now defined once and reused.
