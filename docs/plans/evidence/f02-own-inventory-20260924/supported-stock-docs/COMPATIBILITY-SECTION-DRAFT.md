## Crafting with carried ingredients

For ordinary crafting bills, enable **Use carried materials for crafting bills** to let colonists
use carried materials tracked by Hauler's Dream, including the crafter's own hauling stock.
Materials collected through HD's hauling and gathering features can supply a bill without a manual
drop first. The bill still needs enough permitted ingredients for its recipe; the pawn can fetch
the remaining ingredients from the map when its carried stock is insufficient.

This setting does not add untracked personal items to an ordinary bill's ingredient search. Food
or supplies placed in inventory by another system may be untracked. If such an item is unavailable
to a bill, put it on the ground in an accessible location and check the bill's ingredient settings.
The building-material feature has a separate, broader inventory policy.

**Keep in inventory controls unloading, not whether crafting may consume tracked stock.** A pawn
carrying 47 eligible units with Keep set to 7 can use 40 in a recipe and retain 7. Use the bill's
ingredient settings to control which materials it may consume.

When Common Sense's ingredient-gathering option is enabled, it handles ordinary ingredient
gathering and HD does not add carried stock to that bill's ingredient search. Common Sense cleaning
alone does not disable HD's gathering. HD's explicit batch command uses a separate crafting job
and its own **Batch even with Common Sense active** setting; its behavior is separate from
ordinary shared-stock selection.
