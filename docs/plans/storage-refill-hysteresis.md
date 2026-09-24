# F20 — Storage Refill Hysteresis compatibility

Scope: GH269, Steam C005/C006, August 29 2026. One cross-posted report describes repeated full-stack hopper-shelf top-ups despite the refill mod.

- [x] Read complete scoped feedback and both attached logs; identify actual `VNPERP_HopperShelf` and SRH 0.2.0.
- [x] Acquire/decompile current actual SRH 1.6 package and inspect the reported shelf's current upstream source.
- [x] Repair HD's three demonstrated new-destination search bypasses, including same-tick cached-selection behavior, using SRH's own controller.
- [ ] Independent source review and isolated compile.
- [ ] Actual baseline/candidate selection and native hauling, then the bounded latch/linked-group/assigned-delivery checks in the evidence handoff.
- [ ] Review real logs/material conservation and add accurate support guidance to the final assembled build.

Status: implemented candidate; runtime acceptance pending. No exact prior successful SRH fix is established. SRH owns thresholds, sampling and linked-storage fullness; it permits en-route deliveries and upper-threshold overshoot. Do not replace its policy with a hard item-count limit.

The [focused evidence and acceptance design](evidence/f20-refill-20260920/HANDOFF.md) contains the precise source cause, provenance and report limits. This plan does not close F20 or expand unrelated storage reports.
