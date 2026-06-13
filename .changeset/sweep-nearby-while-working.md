---
"haulers-dream": patch
---

**Pawns now tidy up while they work** — a colonist deconstructing, mining, or harvesting doesn't just
scoop *its own* yields into its inventory; it also picks up *other loose haulable items lying around the
work spot* into its pack, so the whole area is cleared in the one consolidated trip instead of being
left for separate hauls afterwards. This is the bulk-hauling sweep (which already fires on dedicated
haul jobs) extended to work jobs.

The pickup is into **inventory** (never hand-carried), respects the smart-overload ceiling, and only
takes items that genuinely need hauling and have somewhere to go — never another hauler's target and
never stock that's already in storage. Toggle it with **"Tidy up while working — scoop nearby loose
items too"** in the mod settings (on by default).
