---
"haulers-dream": patch
---

Stop the planner right-click options from appearing for colonists that cannot do the work, and add an option to also hide them for colonists not assigned to it (issue #176).

The route and craft planner options could still show up on a colonist incapable of the underlying work in some cases, such as a research bench offering a hauling route, or a sowing route on a colonist below the plant's required sowing skill. Those now stay hidden, matching how the base game gates the same work.

There is also a new setting, "Plan work for unassigned pawns" (under Planning tools, on by default so existing behavior is unchanged). Turn it off to also hide the planner for a colonist who is capable of the work but has that work type unchecked in its Work tab, so only colonists actually assigned to the work are offered it. Colonists who simply cannot do the work are always hidden regardless of this setting. Translations for all supported languages are included.
