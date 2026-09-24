# Reporting workflow subjects — L38

Status: initial source/code investigation; no implementation or runtime verification. The independent source audit exposed these subjects separately from the gameplay defects in the same messages.

## Observed current implementation

- `HaulersDreamSettings.Window.cs` opens `Dialog_MyReports` from the settings window. Discoverability must be checked in the actual rendered settings and with relevant UI mods; a source-level button is not proof that C199 can see it.
- `Dialog_MyReports` lists this installation's reports, opens their threads, and submits replies once a report has a GitHub issue number. It does not expose an edit action for the original report.
- `SendComment` calls `ReportApi.BuildCommentJson`, which produces only `{ "body": "..." }`. It does not read or attach current HD/Player logs. Therefore the current answer to T03-R07's question is that an ordinary reply does not attach updated logs. The player-facing composer provides no visible explanation of that distinction in its current strings.
- New reports use a separate `ReportApi.BuildJson` path: HD's debug trail is included, Player.log is conditional, and version/mod metadata is collected. This behavior must not be described as applying to ordinary replies.
- The client exposes list, thread, comment, attachment and status URL helpers. No edit endpoint is present in the inspected client. Backend capabilities have not been inspected; do not invent a working edit endpoint or conclude that editing is technically impossible.
- Network requests are pumped through the UI and scoped by the reporter token. Runtime tests must use a local stub or an explicitly separate test service. **No test submission to the live report service or public GitHub is authorized.**

## Individual obligations

| Source | Required investigation / acceptance |
|---|---|
| GH261 | Preserve the inability-to-edit subject independently of hauling. Inspect backend support and original reporter intent; provide a reviewed usable correction/update workflow, with explicit behavior for original text versus follow-up, before dispositioning it. Do not quietly treat a new duplicate report as the answer. |
| T03-R07 | Make log-attachment behavior clear. Determine whether a follow-up can safely include refreshed diagnostics; if implemented, preserve informed log selection, request ownership, success/failure state and retries. Verify payload and actual UI against a local endpoint without posting live feedback. |
| C199 | Verify and document a discoverable report entry point at the relevant settings layouts and UI scale. Investigate the reported hidden-button interaction if its mod can be identified; do not attribute it from a mod list alone. |
| T01-R01 | Investigate inability to create a Steam discussion as a support/workflow problem. Establish an accessible alternate reporting path and evidence-backed platform/HD disposition. The cause of the Steam restriction is currently unknown. |
| C021 | Preserve the distinct CanGiveJob/JobOnX synchronization warning and removing-mods support question. Inspect exact diagnostic emitter and context; do not merge these automatically into its cloth-loop diagnosis. |

Historical acknowledgements and report usability replies identified by the source audit retain their own source mappings. This document covers only the inspected initial subjects and does not close L38 or the wider startup/UI investigation.
