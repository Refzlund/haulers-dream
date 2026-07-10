---
"haulers-dream": patch
---

Fix a colonist pacing back and forth forever carrying an item in a cramped room (issue #162).

When an item sits on a spot the game wants cleared for other work (an ingredient cell of a workbench mid-bill, a growing or mining or construction spot), the base game keeps hauling it "aside" to the nearest free tile. In a tight room that nearest free tile is the adjacent spot the game also wants cleared, so the item gets shuffled between two cells endlessly, a fresh short haul every half second, and a colonist can spend all day on it. This is a base-game behaviour rather than something Hauler's Dream causes, but because the mod keeps colonists hauling until their queue runs dry, an idle hauler would reliably fall into it.

The earlier attempts at this bug all watched for the wrong kind of haul (a haul toward storage that fails), while this loop is a haul aside that succeeds every time, so none of them ever saw it. Now, the moment a colonist hauls an item aside, Hauler's Dream stops offering to haul that same item aside again for a while. The one haul that clears the work spot still happens, so the item is relocated a single time, but the back-and-forth shuffle can never start, and the colonist goes on to do something useful with it (often hauling it to storage) instead of pacing. As long as the work spot keeps demanding to be cleared the item stays put, so the shuffle cannot start up again later either. Nothing about normal storage hauling is affected.
