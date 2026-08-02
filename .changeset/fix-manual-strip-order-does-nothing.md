---
"haulers-dream": patch
---

Fix a Strip order you place by hand silently doing nothing on some corpses. A Steam report described colonists that would "neither strip the corpse while moving them nor when you force them to strip". It only happens once you set one of the "Tainted apparel" policies to "leave it on the corpse", and from then on it happens every time, which is why it read as intermittent.

That setting tells Hauler's Dream not to take those pieces off the body, which is exactly what it should do. RimWorld's own "is there anything to strip here?" check knew nothing about it, though, so a body wearing nothing but kept-on-corpse clothing still counted as strippable. You could place the order, the colonist walked over, worked at the body, cleared the designation, removed nothing, and the game recorded a body stripped. Because the body still looked strippable afterwards you could designate it again, and again, with the same result each time.

That check now knows the rule. A body whose remaining clothing is all set to stay on it is no longer offered for stripping: the strip tool will not mark it, an order you already placed hands out no work, and a strip job already under way stops before it can clear its own designation, so the order stays visible instead of quietly disappearing. Bodies with anything worth taking are completely unaffected — a weapon, something in the pockets, or any piece not covered by a "leave it on the corpse" policy still strips exactly as before, and everything except the pieces you asked to leave comes off. Living prisoners are untouched, since leaving clothes on the body has only ever applied to the dead. With the default "take it" settings nothing changes at all, which is why most players never ran into this.

Stripping a corpse before cremating it is deliberately left alone: there the body is about to burn, so it is still worth stripping for the weapons and pocket contents, and the clothes you asked to leave on it go into the fire with it. That is the clean disposal the setting promises.

The "leave it on the corpse" option now says as much in the settings, in every language: the piece is never taken off by any strip, including one you order by hand.
