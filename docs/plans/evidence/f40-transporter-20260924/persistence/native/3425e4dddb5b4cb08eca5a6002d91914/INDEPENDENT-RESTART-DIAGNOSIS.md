# F40 original-checkpoint restart: genuine delivery failure

Run `3425e4dddb5b4cb08eca5a6002d91914` remains **failed, 58/60 assertions, U0**. The two failed assertions are `saved-queue-physical-delivery` and its aggregate host return. Queue order and serialization are intact; the original unload job fails when another courier fills its selected storage cell. Eventual recovery storage cannot substitute for the required original operation success.

Review authorship: I authored earlier persistence fixture versions through v3, but not the v4/v5 loader-spacing changes or the transporter product. Root separately reviewed full source and the actual producer checkpoint. This is a fresh examination of the native failure and source, with that authorship limitation retained.

## Exact bindings and preserved inputs

The unchanged native producer checkpoint is tick701, SHA `57A0C7CA7F6B4BC20043F55CC971B4FB2920027BEF152F556A9392286C1FC9D8`. The producer copy, original save, consumer Autostart, and recorded hash agree. The producer XML reconstruction audit is retained under `../f4ec60841bac4561a711e236a8c6b8e3/`. Actual loaded current jobs, queues, driver cursors/progress/cargo/hand-tail, flags, sessions, keeps, physical identities and climate agree with that record **before the first tick at701**; native pause is702.

Actual product HD `E62F1C1EB45B31031F8AEF705280550D0DF8C3ACEAD6CB48461BCE0D41BDD307`, Core `83EB8EB0E348A3FB6B0066640BAA1278C2B1ED6844EA31A774D3AFE18DAD43C6`, host `B9C41DF0E2CFE86E3A7FE8F5A3AFB24230AA90184BE319DD76EC5B988D1C1D8F`. Four active mods and ten actual images match the manifest. `review-restart.py` verifies all 1,509 copied inputs against source, 256 selected pins, frozen product/host source, raw copies and native identity. `independent-native-audit.json` distinguishes audit validity from the failed product verdict.

Native21272 and controller4816 joined exit0; input desktop stayed Default, no switch, timeout, cleanup error or owned process left. Verify retains the failed result and manual/generic review flags, with `protectedChanges: []`. No protected save or product input was rewritten.

## Original queued operations

| Actual event | Observation |
|---|---|
|81–84, tick2847–2914|Blocker16 ends. Original queued load17 starts once and succeeds, with actual Jade40718×10 delivered to40696.|
|87, tick2916|Original queued unload18 starts once, after17.|
|89–90, tick2975–2983|Silver40713×10 transfers once from40697 to Q's inventory, then hands.|
|92, tick3490|H stores WoodLog40711×75 at(152,0,110), the cell selected by Q's active18. Silver40713×10 remains in Q's hands.|
|94, tick3674|Original18 ends **Incompletable**, toil11, targetB(152,0,110), pulled10/delivered0, owned Silver hand-tail10.|
|95, tick3674|The same Silver40713×10 is now physically stored at adjacent(153,0,110).|
|98, tick3705|Native recovery HaulToCell296 ends Succeeded at the adjacent destination.|

`StartsInOrder(17,18)` and `EndedSuccess(17)` are true; **only `EndedSuccess(18)` is false** within the queue assertion. Notify_Starting prefix and pre-cleanup end snapshots contain immutable IDs. No pooled reference or double-start artifact explains the failure. A missing start event for native recovery296 does not invalidate the recorded original18 end or physical census, and is not presented as a complete recovery start trace.

The zero and positive cargo entries referencing Silver40713 are expected bookkeeping when an entire inventory stack transfers into hands without changing its Thing identity. `Find(... remaining > 0)` credits the positive entry. This alias is not evidence of a duplicate physical stack: every census has one physical ID per object and exact conserved totals.

Source causality: the custom `HD_Utib_StoreCell` toil ends immediately if `IsGoodStoreCell` rejects the selected destination. The observed occupied cell, active hand cargo, toil11 and delivered0 identify the invalidated-destination branch; the guard's individual return value was not separately instrumented. `StorageCommitments.TryCommit` deliberately promises group capacity rather than an exclusive cell. `BulkUnloadRecovery.Queue` can then enqueue the observed ordinary native storage job. The installed native `Toils_Haul.PlaceHauledThingInCell` supports finding another valid storage destination within its existing job. HD's custom positive-storage credit correctly excludes drop-aside/destruction, but presently omits that continuation.

## Other bounded results

- Saved H unload55 succeeds at5315; all200 wood physically stored. Saved L load19 succeeds at5390 with35 delivered including the producer's7, then fifteen productive7-unit successor visits finish the remaining105. Selected LL holds140 with zero demand at22160.
- Original queued loader21 remains behind its real non-idle blocker20 at zero demand. Session custody remains true and opposite unload intent is refused. The stable phase ends22461, over300 ticks later; unrelated group still has demand10 and no loaded contents while continuous loading is off.
- Original I unload54 is interrupted702. Explicit newer Wait56 starts703, then actual recovery57 starts919. At1868 the physical census already shows20 Plasteel stored and7 kept in inventory; the stored stack's identity is40699 and kept identity40701 because native merges/transfers can replace which physical object represents the kept quantity. Recovery57's Incompletable end does not mean those20 units are missing. Later ordinary incidental Gold pickup/recovery641 is recorded separately. No claim that either general recovery job ended Succeeded is made.
- Native session cleanup at22461 physically drops140 uranium. It is a real drop and session retirement, not a fictitious retained hold. The forbidden phase refuses automatic intake, preserves/toggles the real flag, then explicitly unloads the remaining160; original explicit job1564 succeeds and total Plasteel storage reaches180 with7 kept at25540.
- Continuous enabled: actual selected20 uranium is completed through7+7+6 at26197/26228/26229 before neighbor Gold10 begins27283 completion. The check passes27289. Newer Wait1871 queues behind load1870;1870 delivers7 and succeeds27429, Wait1871 begins27432, and after309 ticks no load successor exists despite33 outstanding selected demand.
- All79 physical censuses conserve Gold10/Jade10/Plasteel187/Silver10/Steel71/Wood200. Uranium starts201 and increases only by the two explicitly authored scene inputs20 and40; their next observed censuses are25552 and27297. Terminal remains failed at27741 despite these accepted individual observations.

Whole logs:1,905 Player.log lines and236 HD debug lines reviewed. No native exception, reservation error, cleanup warning or captured Unity error. Two Mono fallback lines, Header texture mip warning, Direct3D timing notices and allocator statistics remain preserved; they are not relabeled as absent. HD debug includes actual transporter/storage/recovery work and repeated planning probes, not an error stack. Workshop discovery is not active provider loading; the actual run has the four pinned mods only.

Audit-development artifacts are preserved: the first audit incorrectly expected added stock to appear in a census on the exact UI-action tick, and matched `disabled=False` in a successful setup description as a failed assertion. The correction reads the first actual physical census and the check's leading boolean only. Neither changes native evidence or the failed product result.

## Smallest next correction

Preserve original save, host and queue-success oracle. Add a bounded replan only for **this driver's still-owned hand cargo** when its cell becomes invalid: release only its exact native destination claim, retain cargo/progress/queue, use the existing native storage search and group-claim replacement, and continue the same job's delivery toils. Persist its no-progress retry budget; reset only on actual positive credited placement. Missing ownership, exhausted retries or no real alternative must still end Incompletable and use existing recovery. Do not turn eventual recovery into delivery credit, add blanket exclusive cell reservations, or separate the fixture's storage to conceal this real contention.
