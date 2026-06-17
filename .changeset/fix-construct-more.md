---
"haulers-dream": patch
---

Fixed pawns hand-carrying construction material to a single site while ignoring identical nearby sites they could have served from the same inventory load — e.g. right-clicking to build a wall delivered one armful to one wall and skipped six others within reach. Hauler's Dream's multi-site construction delivery relied on vanilla's nearby-needer batch, but vanilla caps that batch at one hand-load of demand (and an 8-tile radius), so it could never load the inventory for more sites than a single armful already covered. Hauler's Dream now discovers the nearby same-material construction cluster itself — scanning blueprints and frames around the site, nearest-first, up to one overloaded trip's worth — and loads the combined material in one go, then delivers to each site. This applies to both right-clicked (prioritized) and automatic construction; planned routes already loaded for the whole route and are unchanged.
