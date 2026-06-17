---
"haulers-dream": patch
---

Review fixes:

- **Mixed-quality/material bulk transporter & portal loads now credit the correct manifest entry.** When a transporter or map-portal manifest held several entries of the same item at different quality or material, a bulk deposit could decrement the wrong entry, so that load would never read as "finished". Bulk loading now resolves each deposited item to its manifest entry with the exact same matcher vanilla uses (the vehicle path already did this), and the clamp, work-gate, and decrement all share that one matcher so they can't disagree. Single-entry manifests (the common case) are unchanged.
- Hardening (no behavior change in normal play): the per-tick availability caches are now thread-local and cleared on quickload, matching the rest of the mod's caches; the two job-takeover Harmony patches have an explicit, pinned order; and a few inaccurate code comments were corrected.
