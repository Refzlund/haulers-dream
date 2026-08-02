---
"haulers-dream": patch
---

Fix the "Plan prioritized crafting…" order ignoring a workbench's "Gather ingredients" button, and stop the mod implying it controls gathering that another mod is doing (#243).

Two separate things came out of this report, and only one of them was a bug in Hauler's Dream.

**The part that was ours.** The right-click order "Plan prioritized crafting…" loads every repetition's ingredients into a colonist's pack in one trip. It was asking nobody's permission before doing that: not the per-bench "Gather ingredients" button, not the crafting settings, not the check the automatic version has always made. So a bench you had deliberately switched off still offered you the order, and taking it still had your colonist pocket the lot — the exact thing that button exists to stop. It now answers to the same conditions the automatic version does. The option is no longer offered at a bench you have switched off, and the order is checked once more at the moment you confirm it, because the window does not pause the game and you may have flipped the switch while it sat open. The same check also keeps it away from self-running benches that take their ingredients into the building rather than from a colonist's pack — vanilla's own never offered it, but a modded one could have.

**The part that was not.** If you have Common Sense installed with its option "Pawns are encouraged to pick up all ingredients before hauling them to the crafting place" turned on — and that is its default — then Common Sense is the mod collecting your bill ingredients, not this one. Hauler's Dream detects that at startup and steps aside from crafting-ingredient hauling completely, precisely so the two do not fight over the same colonists. The logs attached to this report and to the one that asked for the per-bench button both show that happening, and neither shows Hauler's Dream gathering anything at all.

That means the per-bench button and the crafting-tab checkboxes could not have changed what you were watching, however you set them, and nothing in the interface said so. That was a fair reason to conclude they were broken. Both now say it plainly: hover the "Gather ingredients" button, or open the Build and Craft tab, and a line at the top names the mod that is doing the gathering and quotes the exact option to turn off — read from that mod's own translations, so it matches the wording you will actually see in your language.

Hauler's Dream does not switch that option off for you, and it will not start overriding it. Common Sense's gathering is Common Sense's feature; you installed it, and it is not a hauling mod's business to reach into another mod's settings and undo them. What was missing was an honest explanation of who was doing what, and that is what has been added.

The button itself keeps working throughout. Even while another mod is gathering, it still governs batch crafting at that bench and still stops Hauler's Dream moving a bill's ingredients to a closer stockpile there.

One related note now appears on the same button: if you have turned "Carry crafting ingredients in inventory (fewer trips)" off in the mod options, the button says so, and reminds you that it still controls batch crafting at that bench. Batch crafting collects ingredients regardless of that setting, which is easy to be caught out by.

Finally, two descriptions have been reworded because they promised more than they could deliver. The button's hover text said colonists *will* gather ingredients at this bench; it now says that is what happens when Hauler's Dream is the one gathering. And the "Carry crafting ingredients in inventory" setting said the mod "stands down (it gathers them instead)", which read as though Hauler's Dream carried on gathering after standing down; it now says it leaves the gathering to the other mod, and that the setting has no effect on it meanwhile. Both are updated in all sixteen languages.
