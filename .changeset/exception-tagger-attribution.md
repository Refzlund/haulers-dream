---
"haulers-dream": patch
---

Be clearer about which errors are actually Hauler's Dream, and fix a Vehicle Framework warning.

When an error passes through a method Hauler's Dream patches, its note in the log now says plainly whether Hauler's Dream's own code was in the error or whether it only happens to patch that method. A report about a "value does not fall within the expected range" error (#97) is the second kind: the fault is in the game's own drop-unused-inventory check reading a drug that a mod adds but that isn't in the pawn's drug policy, and Hauler's Dream is only a bystander in the call stack. The note now reflects that instead of implying Hauler's Dream caused it.

Separately, with Vehicle Framework installed, Hauler's Dream could log a harmless "could not attach the exception tagger" warning: it was trying to tag a method Vehicle Framework inherits from a shared generic base, which Harmony won't patch in that form. It now tags the method that actually runs, so the warning is gone and the vehicle-loading behaviour is unchanged.
