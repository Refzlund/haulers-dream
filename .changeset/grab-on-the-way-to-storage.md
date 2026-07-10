---
"haulers-dream": patch
---

Let a colonist grab a loose item it walks over on its way to store things away.

A hauler carrying scooped goods to the shelves would step right over a loose item on the floor, even one on its exact path, and leave it for a second trip. That is because the scoop-on-the-way behavior only kicks in when a colonist sets off toward other work; once it is already on a storage run, it could not pick anything else up.

Now, while a colonist is walking to storage, it grabs a loose haulable that sits on that path, so the item rides along on a trip it was making anyway. By default it only does this for a short detour at most, so the trip is barely affected, and how far it will step out of its way is adjustable (see the new "Grab-on-the-way detour" setting under Routing). It still leaves alone anything reserved by someone else, forbidden, or with nowhere better to go.
