---
"haulers-dream": patch
---

fix: bulk/batch jobs no longer send a pawn through a deadly environment for *bonus* targets. When a colonist starts a hauling / loading / construction-supply / crafting job, Hauler's Dream adds nearby items to the same trip — but those extra targets were inheriting the "ignore danger" exemption that only the single, explicitly-clicked target is meant to get (a job becomes danger-exempt while it is player-forced, or while its right-click menu is open). The most visible symptom (Save Our Ship 2 / Odyssey): a suit-less colonist the player set to mine or deconstruct would sweep up scrap sitting in vacuum and walk into space to fetch it.

Now every UNCLICKED extra is held to the pawn's normal danger ceiling — it will never path through vacuum, fire, or deadly temperature for a bonus pickup — while the single target you explicitly ordered still obeys your forced command exactly as before. Your drawn allowed-area zones were always respected; this closes the separate danger-avoidance gap. Existing saves self-heal (an already-queued, now-unreachable self-pickup is dropped and left for normal hauling rather than walked to). On maps with no vacuum or lethal temperatures, behaviour is unchanged.
