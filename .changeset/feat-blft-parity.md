---
"haulers-dream": minor
---

**Full "Bulk Load for Transport" parity.** Hauler's Dream now covers the remaining behaviors from the Bulk Load for Transport mod, so it works as a complete drop-in replacement. New safety nets are on by default; the more opinionated behaviors are opt-in.

On by default (safety / anti-loss / correctness):
- **Save survives uninstall** — a save written while a colonist is mid-bulk-load no longer leaves a broken job reference if you later remove Hauler's Dream.
- **Softlock auto-drop** — if swept cargo ends up stranded on a pawn that can no longer haul (work disabled, hauling priority 0, or a dormant/charging/shut-down mech), only that tagged cargo is dropped so another hauler reclaims it.
- **In-transit cargo shows in the loading dialog** — items already on their way inside a hauler's pack are counted as accounted-for, so the dialog and vanilla don't think they still need hauling.
- **Shuttle boarding sync** — a colonist boarding a shuttle deposits its manifest cargo into the shuttle instead of flying off with it in their backpack (manifest decrements exactly).
- **All-pawn manifests keep the vanilla option** — when a manifest is only pawns/corpses (which bulk-load can't carry), the normal "load" option is preserved instead of a dead end.
- A small per-frame work-availability cache trims the load-path scan cost on big colonies.

Opt-in (off by default, in settings):
- **Opportunistic loading** — a hauler already carrying matching cargo diverts to top off a nearby needy transporter/portal/vehicle.
- **Hybrid pathfinding** — re-ranks the nearest load targets by real walkable distance instead of straight-line.
- **Continuous loading** — a player-forced load chains to the next group until everything reachable is loaded.

Plus polish: optional auto-open of the contents/gear tab on select, verbose logging gated to dev mode, and a "Reset to defaults" button in settings.
