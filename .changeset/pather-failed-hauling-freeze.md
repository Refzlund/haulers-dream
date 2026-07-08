---
"haulers-dream": patch
---

Fix the real cause of colonists standing still for about ten seconds while harvesting or mining, worse with several colonists working the same area (#160).

Both the self-pickup job (a colonist scooping its own dropped yields) and the bulk-haul sweep walk a whole list of ground stacks in one trip. In a busy area with several colonists moving around, a stack that was reachable when it got queued can become blocked by the time the colonist actually walks to it. Neither job handled that gracefully, so a single blocked stack ended the entire job, and vanilla's own response to that kind of failure is a hardcoded few-second wait that a freshly queued job cannot interrupt. With several colonists hitting this independently in the same field, the waits stacked up into the reported freeze. Both jobs now just skip a stack they can no longer reach and carry on with the rest of the list, the same way they already skip a stack that got stolen or forbidden.

Also closed a smaller gap in the same area: a colonist's own dropped yields are queued for pickup without checking whether they can actually be reached, unlike every other picker in the mod. They're now checked the same way, so a permanently unreachable drop is left for normal hauling instead of being walked toward at all.
