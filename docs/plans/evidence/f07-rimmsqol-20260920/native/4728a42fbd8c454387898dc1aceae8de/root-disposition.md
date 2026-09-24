# Settings-document correction: root disposition

24 September 2026. This run remains failed: 70/73 assertions, 173 events, zero captured Unity errors. The narrow settings-document correction is accepted and committed as `bf54eae`; full F07 remains open.

The same host as baseline `2fee343c1edd4ca4a9de57c8f23762b6` now retains identified Bulk58 behind Goto56 when the actual RIMMS editor writes SettingsBlock at tick2417. Native Goto succeeds at3468; actual CanBeginNow rejects58 twice with the revoked drafted permission. Wait_Combat starts while the blocked order remains pending. Physical cargo remains42/42, keep7. Baseline deleted its waiting command during the settings write and never reached admission.

The later assertion incorrectly required deletion of the blocked queue. Actual native ThinkNode_QueuedJob retains an all-blocked queue for a healthy pawn unless another queued job can begin. The corrected v13 fixture requires this retention and unchanged cargo, then checks replacement through the next offered command. No failed result is reclassified as passed.

Product82198D99/CoreF24B0569 differs from the baseline's frozen source in the save-document guard only. HostCED49D6F is unchanged. Source and actual pair were independently reviewed in `../../settings-save-v12/source-review.md`. True colony-save queue preservation is a separate unfinished change.

Root reviewed the events, failed assertions, Verify, prior complete-log assessment and native receipts. Player.log has two early Mono fallback messages and no gameplay exception outside the reported harness assertion. Native22256/controller13824 joined with exit0; input remained Default, with no desktop switch, cleanup error or remaining owned process. Verify reports protectedChanges=[] and retains its explicit manual-review/failure findings. No accepted checkpoint exists from this run.
