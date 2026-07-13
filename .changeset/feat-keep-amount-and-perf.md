---
"haulers-dream": minor
---

Choose how much to keep in inventory, set it from the Gear tab, and smoother shuttle/route menus.

"Keep in inventory" now lets you pick an amount. Right-clicking a stack and choosing "Keep in inventory" opens a slider (just like the vanilla "pick up some" dialog) so you can hold an exact amount, such as 50 silver, instead of the whole stack. Hauler's Dream keeps that many and treats the rest as surplus to haul away, and the game's "drop unused inventory" cleanup leaves the kept amount alone.

A new keep control on the Gear tab. Hover any item in a colonist's inventory to set how many of it that pawn should hold onto; items being kept always show the amount, so you can see and change it at a glance. Setting the amount to 0 stops keeping. It can be turned off in the mod options if you would rather not see it. (Kept amounts save with your game and sync in multiplayer.)

Performance. Right-clicking a shuttle or transporter that has a load list no longer re-plans the whole load several times while building the menu; the plan is now reused within the same click, so opening the menu is lighter. The route planner also reuses its per-target work lookup within a click. Closing the mod options window fully restores framerate, as the settings screen only does work while it is open.
