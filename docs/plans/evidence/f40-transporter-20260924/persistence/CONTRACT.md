# F40 bounded persistence contract

This fixture uses the frozen reservation-release product (`E62F1C1E…` / `83EB8EB0…`) and the established private-desktop controller. It adds no product changes and does not repeat the Core phases. Native execution and acceptance remain pending.

Three roles share one **original native producer save**: `F40-PRODUCE`, positive `F40-RESTART`, and a separate `F40-DISABLED` startup. Each consumer receives the identical save and record, with distinct pinned private HD settings. No edited XML, fabricated driver state, compensating component changes, pawn healing, temperature patches or result/out-argument observer writes are permitted.

## Producer

- Inventory courier: a real flagged pod unload has pulled tagged cargo, with cargo references/remaining quantity, progress and a current native job/toil. A separately kept stack remains intact.
- Hands courier: an actual full kept backpack forces the shared native pull into hands. Cargo identity and `handTail` must match the actual carry tracker. Its pod retains additional cargo and standing intent.
- Loader: a real player command plans under the ordinary full carry setting; the actual live carry setting is then lowered before native pickup. This legitimate setting transition leaves an unfinished source count after the first deposit, so the **same current driver** has positive delivered units and a real remaining manifest. No driver fields or source counts are assigned by the fixture. The plan, setting transition and physical first deposit are explicit evidence.
- Queue courier: a genuine explicit Wait is current and real load/unload commands are queued through `TransporterCommand.IssueSynced(requestQueueing:true)`. Save and load must preserve immutable job IDs, queue order and source targets/counts.
- A second queued loader can retain custody of the active loader's group after that group's demand reaches zero. The zero-demand contrast is observed after real deposits, never by clearing the manifest/session fields.
- Save is requested only from a stable main-thread boundary once all predicates hold. Capture all IDs, owners/counts, Keep/tags, driver fields, job/toil/cursors, flags, native group IDs, manifest entries and serialized session groups. Native save must leave that state unchanged; retain exact save bytes and native XML witnesses.

## Positive restart

Before the first native tick, compare the recorded state exactly and prove process replacement. Resume productive physical delivery. Interrupt the restored inventory-unload visit with an actual newer explicit order: held-cargo recovery must follow that order and must not recreate the cancelled pod visit. The saved hands visit completes real storage delivery. The saved loader completes its selected manifest; unrelated nearby demand remains untouched with ContinuousLoad off. The queued load and unload IDs execute in their saved order and move their expected material.

Observe actual zero-demand plus queued-loader custody, refusal of opposite intake, then native `CleanUpLoadingVars` teardown. Append only the missing short controls: forbidden-only automatic refusal/intent retention followed by productive explicit unloading and toggle-off; ContinuousLoad on completes the selected group before the nearby group; a newer queued explicit command prevents a loading continuation. Preserve total units throughout each phase and require a 300-tick terminal interval.

## Disabled startup

Load the same original producer save with `enableBulkUnloadTransporters=False` in the actual private settings file. Check exact serialized state before the first tick, raw flags present but inactive, native loading admission despite dormant flags, and manual toggle-off. Later enable the actual setting without changing Harmony patches and prove real unload admission. Independently observe saved load/session ownership and actual queued-loader custody after zero manifest demand; clear it only through the native loading teardown. This consumer may legitimately terminate the saved unload jobs, so it never substitutes for the positive recovery result.

## Evidence and bounds

Read-only native job start/end and transfer observations retain immutable IDs and real owners; they never rewrite native out/ref results. Native room caches warm through actual ticks before controlled actors exist. Save-owned world pawns replace unsaved parking holders. All source/host/controller/product/provider/settings bytes are frozen, consumers bind to a passed original producer and protected inputs, and complete logs/process receipts need independent review. No provider, container-delivery or network compatibility claim is made here; those remain the distinct rows in `REMAINING-CHECKS.md`.
# Native queue blockers, v4 correction

The failed v3 producer used idle `Wait` for Q/Z; native `TryTakeOrderedJob` legitimately replaces an idle job even when queueing is requested. V4 uses the existing non-idle `Wait_MaintainPosture` driver with real expiry intervals 2200/25000 and `JobTag.Misc` for these two blockers only. It verifies native non-idle state and original IDs before queue admission, retains expiry/start values in the exact saved-state comparison, and requires Z's original blocker plus queued loader at zero demand. Ordinary I/H and later interruption waits are unchanged. No product queue policy, native definition, reservation or job tracker is patched.
