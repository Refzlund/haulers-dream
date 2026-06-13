---
"haulers-dream": minor
---

**Caravan / pack-animal loading.** Two related improvements for hauling while away from your base:

- **No more dropping loot on the ground while caravanning.** Previously, when a pawn scooped materials on a temporary map (a bandit camp, an ambush site — anywhere that isn't your base), the mod would try to "unload" them and, finding no stockpile, dump them on the ground — where they were abandoned when the caravan left. Now scooped loot simply accumulates in the pawn's inventory and travels home automatically as caravan inventory; it's never dropped on a temporary map. (At your base, unloading to storage is unchanged.)
- **Pawns load pack animals instead of carrying everything themselves.** When a caravan pawn gets over-encumbered from scooping loot, it now automatically walks to the nearest pack animal and offloads onto it — keeping pawns mobile. And a new right-click order, **"Load nearby items onto pack animal (bulk)"**, makes one colonist sweep several nearby stacks into its inventory and load them onto a pack animal in a single trip (instead of vanilla's one-stack-at-a-time carry — which still works alongside it).
- **Shift-clicking several "Load onto pack animal" orders now makes one trip.** Previously, queuing up multiple vanilla "Load onto pack animal" orders made the pawn carry one stack in its hands per order — a separate round-trip each. Those orders now coalesce: the pawn sweeps all the chosen items into its inventory and walks to the animal once.

Both are on by default and have their own toggles in the mod options ("Auto-load pack animals when heavy" and "Bulk 'load onto pack animal' order"). The "work on caravans / temporary maps" setting now strictly controls whether the mod is active there at all; when it's on, the mod scoops and accumulates loot but never drops it on the ground.
