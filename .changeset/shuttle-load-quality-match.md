---
"haulers-dream": patch
---

Fix pawns loading the wrong quality or variant of an item into a shuttle, transport pod, portal, or vehicle.

When you set a shuttle to load a specific item, say a normal-quality jacket, a pawn could instead grab a different one of the same kind that happened to be nearer, like an excellent-quality jacket sitting right next to the shuttle, and load that. The bulk loader was matching items by their type alone, without confirming that the quality, material, and hit points matched what the manifest actually asked for. It now checks, the same way the base game does: a pawn only picks up an item that matches a requested entry, and only up to the count that entry still needs. Storage hauling, bill ingredients, and pack-animal loading were already doing the right thing and are unchanged.
