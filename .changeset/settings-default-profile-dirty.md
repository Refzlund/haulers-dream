---
"haulers-dream": patch
---

Fix the settings selector showing "Custom (unsaved)" on a default configuration.

Since the route-planner update, the profile selector at the top of the settings window always read "Custom (unsaved)", even on a brand-new install and even right after choosing "Default (profile, built-in)". Two of the remembered-route stores (the sow and remove-floor route templates) were left out of the code that compares the live settings against the defaults, so the comparison always saw a difference that was not really there. That same gap would also make "Create new profile" fail and "Copy profile" produce a broken code. All of those now work: a default or freshly reset configuration reads "Default" again, creating and copying profiles works, and pasted profile codes carry your remembered sow and remove-floor routes.

There is also a self-check at startup and a build check so this whole class of problem is caught early if a future setting is added without wiring it into the profile system.
