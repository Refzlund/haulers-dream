---
"haulers-dream": patch
---

Stop Hauler's Dream adding a hauling trip in front of an order you gave. Shift-click two corpses to strip and the colonist would strip the first, set off to collect and deliver something else, and only come back for the second afterwards, instead of working through your list in the order you set it. Ordering a colonist to tame an animal picked up the same detour on the way, which is also how the animal's food ended up sitting in the job queue long enough to be shipped off (fixed separately in this release).

RimWorld is deliberate about this. Its own "grab something on the way past" behaviour asks whether the job it is about to attach itself to is one you explicitly ordered, and stands down if it is: a job you asked for is not one it will pad out. Hauler's Dream's version of that behaviour copied every other condition RimWorld applies and missed exactly that one, so on precisely the orders RimWorld leaves alone, RimWorld declined and Hauler's Dream stepped in. It now asks the same question, so the two agree again.

Work a colonist finds for itself is untouched, and that is where grabbing something on the way past actually earns its keep. RimWorld's own opportunistic hauling is untouched too: Hauler's Dream only ever adds to it when RimWorld found nothing to grab.
