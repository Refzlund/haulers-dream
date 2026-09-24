# F07 restart observation and settings-gate acceptance

## Native PauseOnLoad is a disclosed observation policy

Retain the current fixture for the declared fresh-process in-transit save obligation, with precise wording: **native PauseOnLoad restart, observed after its one native load tick, then normal native continuation**. This is not an unpaused-default restart test or an observation before all gameplay progress. No product, host or controller change is needed just to remove the native pause option.

The option predates the current correction: original `FIXTURE.md:45` explicitly says that the restart enables native PauseOnLoad. Controller-v12 lines1162–1167 write True for F07-RESTART and False otherwise. Its configuration file is included in the prepared manifest hashes. Bootstrap preserves the loaded speed for F07-RESTART (line215), then the scenario records oldSpeed before explicitly acquiring its own pause (RimmsCommand.cs:108–109). LoadCheckpoint requires that recorded speed already be Paused and ticks equal saved+1 (208–209), so the fixture's subsequent pause assignment cannot manufacture this precondition.

Actual native decompiles retained beside this review establish the ordering:

- `Verse.Game.LoadGame`, lines610–647: Scribe finalization, map finalization, Game.FinalizeInit and component load lifecycle run normally. If Prefs.PauseOnLoad is true, ExecuteWhenFinished schedules **DoSingleTick**, then sets Paused. The one tick is not a synthetic fixture update.
- `Verse.TickManager.DoSingleTick`, lines357–375: map pre-ticks, game tick increment and ordinary normal/rare/long thing ticks run. The saved+1 state can already include legitimate actor movement or job progress.
- `Verse.Game.UpdatePlay`, lines660–680: the regular TickManager update precedes GameComponentUpdate, which is where this host first inspects the scene. Native pausing avoids an arbitrary frame's additional ticks before that inspection.
- `Verse.PrefsData.pauseOnLoad`, line87: absent an override the boolean defaults false. TickManager's speed defaults Normal and is not serialized in ExposeData. Therefore this is deliberately a supported paused-load configuration, not default preference coverage.

No HD source branch on Prefs.PauseOnLoad or a delayed GameComponentUpdate repair was found. The pause does not bypass deserialization, component finalization, the first real native tick, job admission or completion. Source therefore supplies no evidence that this option is masking a product fault. The exact loaded-state checks may fail if even that first tick legitimately advances the chosen in-transit boundary; such a failure should be investigated and retained, not 'fixed' by restoring job/cargo fields.

## Save and configuration provenance

Producer SaveCheckpoint calls real GameDataSaveLoader.SaveGame while the actual generated delivery holds physical cargo. It retains the real RIMMS settings file plus a checkpoint with actor/map/job/zone IDs, held piece/count, inventory roster, manifest, transit receipt, protected quantity, total and config hash. The restart controller requires one successful producer, checks known save-reference warnings and native savegame root, binds the save/record/config to that same private producer directory, copies them byte-verified into one fresh run, and omits quicktest. Native Root_Play loads the sole Autostart save; the host requires its first game lifecycle callback to be LoadedGame.

LoadCheckpoint validates the original loaded permissions, active RIMMS instance and saved cargo/job identities before later fixture setting adjustments and before resuming at normal speed. It does not deserialize the sidecar into product fields, start a replacement job or manufacture cargo. As in the producer, the controlled scene subsequently applies its declared HD test settings; this is not a preservation test for every possible user's HD configuration. Actual loaded checks and native successful settlement, exact conservation and retained protected stock must all pass before acceptance. The ongoing producer/restart chain is not yet accepted by this source review.

The smallest honest acceptance is one successful bound paused-load restart and its normal continuation for the current delivery obligation. Do not label it an unpaused-default test or require an unrelated preference matrix. If a specific default-start timing problem is later observed, a separate unpaused case should observe the saved identities during native LoadedGame and follow live progress, rather than reuse this intentionally strict post-update saved+1 assertion. Pending queued-Bulk/generatedDelivery persistence is still the separately approved true-save correction/witness; future F12 durable intent remains separate.

## Settings gate: independent acceptance and commit scope

The gate-only paired finding is accepted. The same v11 host ran baseline `2fee343c1edd4ca4a9de57c8f23762b6` and candidate `4728a42fbd8c454387898dc1aceae8de`; matched product source inventories differ only in the gate file. Baseline events159–161 show the settings write deleting waiting work immediately. Candidate preserves Bulk58 through the same settings write, lets Goto56 finish, rejects58 at actual CanBeginNow twice, and retains it under native all-blocked queue semantics. The overall candidate remains failed solely at the old deletion oracle; root's Verify retains that failed/manual status and protectedChanges[]. This narrow acceptance does not turn either scenario into a full pass or prove true-game-save queue persistence.

Only these two tracked files are the focused gate commit:

1. `Source/HaulersDream/Patch_ScribeSaver_InitSaving.cs` — SHA-256 `2F61888DABCC4D0EB6731D8229FE5B2B6D75967215141524230172994B31A695`; only the native root-name guard/signature and aligned comment differ from HEAD.
2. `.changeset/preserve-queued-work-when-saving-settings.md` — SHA-256 `EAF94572496559BA3C9B7A731D507C58D025747D0BA2389A1D6D146240C1B7BA`; accurately limits its promise to settings writes.

Exclude `.changeset/rimmsqol-drafted-nearby-hauling.md` and all broader pending product files from that commit. The true-save exception is not present in the selected source. No native action, product/host/controller edit or commit was performed by this review.
