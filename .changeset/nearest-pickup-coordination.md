---
"haulers-dream": patch
---

Fix colonists crossing paths to pick up harvested/mined yields instead of taking whichever is closest to them.

Each colonist queues its own dropped yields (and any nearby loose stacks it sweeps up along the way) into a private pickup list, but until now that list was popped from the wrong end: a colonist walked to the oldest queued drop first rather than the one nearest to where it actually stands right now. With several colonists working a big field at once, this could send the wrong colonist all the way across the field for a stack a much closer colonist had already queued for itself, since nothing coordinated between them.

Two changes fix this together. First, a colonist now picks the nearest still-valid stack out of its own list rather than the oldest one. Second, colonists now share a lightweight registry of who currently has which stack queued: if a colonist claims a stack another colonist already has pending, and the new claimant is actually closer to it right now, the claim (and the walk) transfers to whoever is closer. This self-corrects as everyone keeps working, so the colony as a whole ends up sending the nearest available colonist to each stack instead of crossing paths.
