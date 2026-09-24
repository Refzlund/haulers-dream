# Proposed player guidance: crafting with carried ingredients

For a normal crafting bill, Hauler's Dream can use materials it is tracking in the crafter's inventory. Turn on **Use carried materials for crafting bills**. Materials collected through HD's hauling and gathering features can be used without manually dropping them first. A bill still needs enough allowed ingredients to complete its recipe; if the carried amount is insufficient, the cook can collect the remaining ingredients from the map.

This option does not add arbitrary personal items from the pawn's inventory to an ordinary bill's ingredient search. Food or supplies placed there by another system may be untracked. If a bill cannot use such an item, put it on the ground in an accessible location and check that the bill allows it. The building-material feature has a separate, broader inventory source policy.

**Keep in inventory controls unloading. It does not forbid crafting from tracked stock.** For example, a pawn carrying47 eligible units with Keep set to7 can use40 in a recipe and retain the remaining7. Use the bill's ingredient settings to control which materials a recipe may consume.

Common Sense's ingredient-gathering option determines who gathers for ordinary bills. When it is enabled, HD leaves that gathering to Common Sense. Common Sense cleaning by itself does not disable HD's gathering. HD's explicit batch command uses a separate crafting job and its own **Batch even with Common Sense active** setting; ordinary shared-stock behavior and batch behavior are separate paths.

Proposed README shared-inventories replacement:

"A pawn carrying materials tracked by Hauler's Dream works like a walking stockpile: workers can draw on that hauling stock, including their own. Ordinary crafting does not automatically use every personal item carried by a pawn. See [crafting with carried ingredients](COMPATIBILITY.md#crafting-with-carried-ingredients) for sharing, Keep and Common Sense settings. Optional: builders may claim materials from a hauler mid-transit."

Editorial note: this is proposed guidance for root review, not an applied product/documentation change. The direct setting label must be matched to the actual translated label when integrated. Actual batch output-stability acceptance is still pending. Do not include this editorial note in player-facing COMPATIBILITY.md.
