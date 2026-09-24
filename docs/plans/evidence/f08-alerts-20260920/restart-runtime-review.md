# F08 actual restart: independent acceptance

**Accept restart `79c6457ddcd54ab887b0b663dece4967` and the F08 support disposition.** Together with accepted producer `bfd06365cfae49749406579ffb90dd78`, this completes the report-specific discovery, matching and persistence checks. No new hauling architecture or product behavior change is required. The README/COMPATIBILITY setup guidance reviewed with the producer is supported by the actual runs.

The sole restart `Autostart.rws` is byte-identical to the successful producer's actual save (`D92BCECFD638A1FE9BA7CCA8E57E67476621E15E742E4F95992F28E327282838`); copied checkpoint text also matches (`BF4C0C96A03E0F5B926E5074824CBFBD04B3E3932D5D3CC319C8986DF1CFB23D`). All seventeen loaded assembly hashes/MVIDs equal the producer. The native log records loading Autostart, and the first lifecycle callback is LoadedGame at saved tick 4049. The pre-fixture state check occurs paused at tick 4050, the explicitly admitted single native load tick, not at an invented identical tick or a new quicktest scene.

Before fixture setting changes, the actual loaded query passed stable BulkHaul definition, enabled/count/delay/comparison fields, active Everyone/ChosenMaps Map 0 scope and **exactly one live registration**. Pawn 23369, zone 0, source identities 23388/23389 and total 40 matched the checkpoint. The current job remained saved Wait 109. The reviewed load path resolves these saved objects; it does not rebuild the alert, restore query fields directly or create replacement materials.

Complete inspection covers 2,082 contiguous events and 108 actual scheduled reports, all consistent with current job identity. Five distinct F08 assertion IDs cover the restart; 1,956 assertion rows include repeated observations and are not separate tests.

- Scheduled idle at tick 4061: inactive, count zero, no culprit.
- Actual IssueSynced starts bulk job 112 at tick 4062 against saved source 23388; the real two-source job becomes active/count one/the same actor as culprit at tick 4076.
- Native gathering and separate unload job 125 physically deliver the saved ten outstanding units. All 40 units remain conserved and reach native storage with empty actor inventory before completion at tick 4954. Unloading itself does not match the BulkHaul selector.
- Completed Wait 133 is inactive/count zero/no culprit at tick 4959. Cleanup removes owned observation/jobs/settings changes without replacing the loaded private scene.

Terminal status is passed with zero captured Unity errors and no failed assertion. Native PID 14404/controller PID 31376 both joined exit 0 without timeout. The private desktop remained inactive, Default stayed the input desktop, no switch or cleanup error occurred and the owned process job emptied.

Whole Player.log and HD-log review finds the known early Mono fallback notices, texture notices and Direct3D timing notice. Native Autostart loading/real hauling are recorded without missing-reference, Lord/world-pawn ownership or action exceptions. Actual Verify records no protected changes. Its `not-verified` status retains the fixed manual-review requirement, generic missing `scenario-observed` marker and the two Mono candidates; the complete scenario-specific F08 events and this independent review supply the actual semantic decision without altering raw evidence.

The report is therefore resolved as an existing supported activity with clarified discovery instructions: **current action → ALL OPTIONS → gathering items into inventory**. It follows current activity and the shared BulkHaul family, including single pickup; queued jobs, separate unloading and ordinary native hauling do not match. Existing selections retain the stable JobDef identity despite the label change. The three failed producers remain failures and were not restart inputs. Final assembled-build/physical-input smoke remains a separate integration check, not an uncompleted requirement of this scoped report.
