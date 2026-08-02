---
"haulers-dream": patch
---

Fix clothing that Hauler's Dream collected staying in a colonist's pack forever when running Compositable Loadouts (#233). Hauler's Dream protects whatever a loadout mod wants a colonist to carry, so it never ships an item off to storage only for the other mod to send the colonist straight back out to fetch it again. It was applying that protection to clothing as well — but Compositable Loadouts never puts clothing in a pack. When a colonist is short a duster it sends them to *wear* one off the floor, so there was nothing to protect and nothing that would ever have been fetched back. The result was that a loadout listing a duster made every spare duster Hauler's Dream had picked up sit in the pack permanently, because Hauler's Dream had put them there and Hauler's Dream was the only thing that would ever have taken them out. Clothing a colonist is hauling now goes to storage like anything else.

The same protection now also notices a weapon the colonist is already holding. A loadout asking for one longsword is satisfied by the longsword in their hands, so a spare one they picked up while hauling is put away instead of being kept as if the loadout still needed it.

Everything Compositable Loadouts genuinely does keep — the items it really does re-fetch — is still protected exactly as before, and nothing changes for anyone not running it.
