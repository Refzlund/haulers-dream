---
"haulers-dream": patch
---

**Make disabling Hauler's Dream fully clean — no leftover errors — and remove the manual "prepare for removal" button.**

Hauler's Dream already rewrites any in-progress task in the written save so disabling the mod can't brick the
load. But the save still referenced two of the mod's own components, so removing the mod logged harmless-but-ugly
errors on the next load:

```
Could not find class HaulersDream.HaulersDreamGameComponent ... Can't load abstract class Verse.GameComponent
Could not find class HaulersDream.MapComponent_RoutePreview ... Can't load abstract class Verse.MapComponent
```

Now the automatic on-save protection also omits Hauler's Dream's own game/map components from the written save
(the route-preview overlay always — it stores nothing; the game component whenever it has no active mining-route
state, which is almost always). The running game is untouched — RimWorld re-creates the components on the next
load while the mod is installed — so there's no effect during play, and a save made with this version loads with
**no Hauler's Dream errors at all** after the mod is removed.

The user-facing **"Prepare to safely remove this mod now" button has been removed** — the automatic protection
makes it unnecessary. The "Keep saves safe to disable from" toggle stays (on by default).

Note: this cleans *future* saves. Re-save a game once with this version (mod still installed) before disabling
the mod, so the new save carries no Hauler's Dream component references.
