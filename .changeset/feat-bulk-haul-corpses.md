---
"haulers-dream": minor
---

Corpse hauls now sweep and batch like every other haul, with a new setting to turn it off. Two Steam reports described the same gap from opposite sides: ordering a haul on a meal picked up a nearby item on the way, while ordering a haul on a corpse picked up the corpse and nothing else; and colonists and mechs were "hauling only single corpse at once".

The cause is that RimWorld runs corpse hauling through a job of its own, separate from everything else it hauls, and Hauler's Dream had only ever hooked the other one. That made bodies invisible to bulk hauling from both directions at once: a haul anchored on a corpse never looked around it, and a corpse lying next to some other haul was never picked up in passing.

Both halves are fixed. A haul on a corpse now sweeps the loose loot around the body into the hauler's pack on the same trip, and a body lying beside another haul rides along with it. What actually comes along is still decided by carry weight, so this is not a change to how much a colonist can carry. A humanlike corpse is around 60 kg against a typical ceiling near 96 kg, so bodies mostly still travel one at a time, and that part was always working as intended. The gain is in small game: a hare is about 24 kg and a squirrel about 12, so a hunter's catch comes home several at a time instead of one trip per animal.

Auto-stripping is unchanged. Every corpse a pawn takes is still stripped exactly as one carried in its hands is.

There is a new "Corpse hauls sweep and batch like any other haul" checkbox on the Hauling tab under Bulk hauling, on by default, if you would rather bodies kept RimWorld's own one-per-trip behaviour.

Two limits are worth knowing about. A bulk haul needs the thing it is anchored on to fit in the hauler's pack, so a corpse heavier than the colonist's remaining carry ceiling — a muffalo, or a human body for a colonist who is already loaded — declines the bulk job and falls back to RimWorld's ordinary hand-carry, which sweeps nothing. The reported case, a human corpse next to a meal with an unloaded colonist, does work. Separately, if you have set auto-stripping to "on disposal hauls only", a body that gets swept into a pack and later buried now arrives at the grave still dressed, because at pick-up time there is no way to know where it will end up. Nothing is destroyed and the gear is buried with the body, recoverable by exhuming it, and the default "on every corpse haul" mode is not affected.

Right-clicking a corpse and choosing "Haul everything nearby" already worked before this change; that order has always been able to anchor on a body. What is new there is that it can now sweep up other bodies too.
