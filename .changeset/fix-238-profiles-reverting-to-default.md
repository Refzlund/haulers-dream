---
"haulers-dream": patch
---

Fix saved profiles (and freshly changed settings) losing their "Collect work results" choices on every launch.

Every setting under "Collect work results" on the Work & yields tab — harvest, logging, mining, chunks, deep drill, deconstruction, animal products, strip, and uninstall — was being reset to "Drop, then collect" each time the game started, for every saved profile. Applying such a profile then pushed those defaults onto your live settings. The same thing happened to your live settings in two cases: on a brand-new install, if you changed any setting at any point during your first session, and after using "Reset to defaults" or picking "Default (profile, built-in)". There was no error and nothing in the log.

The cause was a one-time upgrade step, meant to run once for configurations saved before these per-category options existed, that could not tell "this configuration is old and needs upgrading" apart from "this configuration simply never recorded which version it was written for". Profile snapshots never recorded it at all, so they were upgraded — and overwritten — on every single load. The upgrade step now looks at the actual saved data instead of at a version marker, so it runs exactly once for a genuinely old configuration and never touches anything else, and every configuration and profile now records its version properly from here on.

Nothing needs to be re-imported and no profile has to be recreated: any values still on disk load correctly again as soon as you update. If a profile had already been written back to disk after a bad load, its nine "Collect work results" rows are gone and will need setting once more — worth a quick check of that list after updating.

Also added a startup self-check that saves and reloads a profile through the game's own settings serializer and reports loudly if anything changes in the process, so this whole class of problem cannot come back unnoticed.
