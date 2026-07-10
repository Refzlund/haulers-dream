---
"haulers-dream": patch
---

Fix colonists looping forever between hauling and unloading at a RimIOT (Logistic Matrix) terminal instead of eating or resting (issue #177).

With RimIOT and a stack-size-increasing mod both installed, a stored item could sit as two partial stacks that can never merge into one (for example 400 and 700 against a limit of 1000). Hauler's Dream would sweep both partials into a colonist's inventory, haul them back, and steer each deposit toward a partial stack again, while RimIOT re-spread them, so the same colonist swept and unloaded the same items every few seconds without end and eventually starved. RimIOT settles these stacks on its own when left alone; the trouble was Hauler's Dream repeatedly picking them back up before it could.

Hauler's Dream now recognizes storage that belongs to a RimIOT network and keeps its bulk sweep and its stack-topping out of it, letting RimIOT manage its own contents. This only activates when RimIOT is installed and changes nothing for anyone not running it. Regular hauling to and from ordinary storage is unaffected, and directly ordered hauls still work on network items.
