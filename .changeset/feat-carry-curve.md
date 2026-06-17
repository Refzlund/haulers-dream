---
"haulers-dream": minor
---

Carry-weight overhaul + a "keep working when full" option:

- **Colonists carry more freely.** The move-speed penalty for an overloaded inventory is now a gentle *curve* instead of a straight line — a light overload is nearly free, and the slowdown only ramps up as the load gets heavy. At the default ("Fair"), colonists now fill to ~275% of capacity before it stops paying off (up from ~200%), and they're still moving at ~65% speed there instead of crawling. The overload slider scales the whole curve: looser settings carry farther with a gentler slope, stricter settings bite sooner. (The carry ceiling is still derived from the trip-vs-speed break-even, which is distance-independent — far hauls don't change how much it's worth carrying.)
- **New "Keep working when full" option (default off).** When enabled, a pawn doing a job that scoops yields (mining, harvesting, etc.) keeps working when its inventory fills up, instead of breaking off to unload — the overflow is left on the ground for haulers. It only makes an unload trip when it's about to travel farther than its nearest dropoff (so it regains speed before a long haul) or at downtime. Lets a miner keep mining while dedicated haulers move the output. Off by default, so existing behavior is unchanged until you enable it.
