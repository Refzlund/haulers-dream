---
"haulers-dream": patch
---

Fix the pickup pause and progress bar still showing up on "Haul everything nearby" and a shift-queued second "Prioritize hauling" order.

The recent change that made automatic cleanup instant again missed two of the ways a colonist can end up sweeping several stacks into their pack: clicking "Haul everything nearby", and shift-queuing a second "Prioritize hauling" order near one already in progress (which takes over as one sweep). Both are the same kind of order as plain "Prioritize hauling" and should be just as instant, but they were still pausing on every stack because the code was telling them apart from a genuinely paced order using the wrong signal, one that every deliberate hauling order sets regardless of what kind it is. It now checks the one thing that actually means "pocket this into inventory and hold onto it", the same way vanilla's own delayed pickup does, so only "Pick up X" and "Keep X in inventory" pace, and both bulk-sweep orders are instant again like plain "Prioritize hauling" already was.
