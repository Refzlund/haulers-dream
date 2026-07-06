---
"haulers-dream": patch
---

Fix two issues reported from a large modded save: pawns over hauling into a small high priority stockpile, and the settings search dropping the framerate while typing.

Bulk hauling now shares one storage budget per destination stockpile or shelf group across every item type bound for it, instead of letting each type price that group's free space on its own. Before, a pawn moving food up to a small stockpile could pocket the meat and the harvest as if each had the whole stockpile to itself, drop only what fit, then carry the rest back to where it came from. A group's empty cells are now spent once for the whole trip, so a pawn takes only what the destination can actually hold and leaves the rest at the source for the next haul cycle. Hauling a single item type was already handled correctly and is unchanged.

The settings search now shows the top matches (up to 30) with a note when more exist, instead of drawing every match every frame. A short or broad query, including the prefixes you pass through while typing a longer word, could match most of the settings and redraw all of them each frame, which dropped the framerate whenever the search box had text in it. Refining the query narrows the list to reach the rest.
