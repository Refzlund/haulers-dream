# Part 2 publication and retained-evidence index

This is the packaging boundary for the paused feedback campaign. The PR includes current product work, plans, findings, failed-attempt explanations and authored test sources. It does not claim that every feedback group is resolved. Read the [ledger](feedback-since-last-update.md), [Part 2 handoff](part-2-handoff.md), [source-to-PR map](part-2-pr-source-map.md) and [final integration obligations](final-integration-checks.md) first.

The publication allowlist is [part-2-evidence-files.json](part-2-evidence-files.json). Its `docsForceAddPaths` is the explicit list eligible for force-add under the ignored `docs/` tree. `docsFiles` records file hashes and sizes; `ordinarySourceFiles` identifies the root runtime harness, runtime controllers, private-desktop wrapper and source-check tools that also belong in the PR. Product source, tests, definitions, translations, changesets and player documentation are reviewed and committed separately by the integration owner. This index does not stage anything.

## What is included

- Direct Markdown plans, scoped feedback, accepted resolution reports, independent reviews, handoffs and failed-attempt diagnoses remain readable in the PR. Historical source deltas are evidence, not instructions to apply every patch. Compact independent audits and final F11 tested-source matching are included.
- [part-2-fixture-sources.zip](part-2-fixture-sources.zip) contains authored source, projects, test definitions, configuration and controller/build scripts for the selected final fixture families. It preserves repository-relative paths. The first packaging audit measured roughly109 MB of source before compression and29 MB compressed; the machine-readable index has the exact final sizes and entry hashes.
- The final archive selection uses current F12 human `native-ui-v7-checkbox` and robot `native-robot-v3`, F13's current basic-v6/late-shelf folders and separate `baseline-walkable` control. It also keeps the distinct producer/consumer and original-product fault inputs needed to understand accepted comparisons. F40's unfinished shuttle draft is included as unfinished work, not a runnable acceptance fixture.
- A final [unfinished-family retention audit](evidence/retained-unfinished-20260924/README.md) adds the unique F09 recovery/live host, corrected data reader and AF1 lifecycle/generation/companion sources missed by the first selection. All205 copied source files match their originals. Two genuinely absent AF1 historical package/controller inputs are recorded, without inventing replacements. BG01–03 and CAP03 already have source coverage; no retained BG04 implementation was found. These additions contribute approximately1.10 MB compressed; exact final package sizes are recorded in the JSON index.
- [Part 1 validation](part-1-validation.md), [all13 guard results](evidence/part-1-final-guards-20260924.json), [ownership-guard mutation results](evidence/part1-permission-guard-review.json) and [the mutation script](evidence/check-final-permission-guard.py) are explicit publication inputs.
- The repository-level `tools/RuntimeHarness/`, `scripts/runtime-*.ps1` and `scripts/run-on-test-desktop.py` are ordinary source files. Keep them directly accessible rather than requiring archive extraction to inspect the central runner.

The archive excludes game/provider binaries, proprietary decompiled sources, raw logs/events/results, saves, screenshots and redundant whole before/after product or fixture snapshots. Original failed attempts remain identified in the authored reports; they are not relabeled as successes. The original raw evidence remains on the test machine.

## Inspecting or rebuilding the fixture source

Use a fresh checkout or separate review directory. Extract the ordinary ZIP at that checkout's root to restore its `docs/plans/evidence/...` paths. Do not extract over an active prepared run or an investigator's working fixture. Verify the ZIP SHA-256 and each entry against `fixtureArchive.file` and `fixtureArchive.entries` in the JSON index before using the extracted files.

This is a source package, not a redistributable RimWorld installation or a turnkey recreation of every historical run. The historical scripts intentionally retain their original source/config/product pins and absolute private-input roots. Those paths are provenance, not portable defaults. On another machine:

1. Obtain the game, DLC and named providers lawfully and identify their actual versions and assembly hashes.
2. Read that family's contract and final handoff. Rebase the build/controller paths into a private working copy; build the current reviewed product and host against the actual game references. Never silently substitute a new binary into an old selection.
3. Generate and independently review fresh input pins, private settings, source/build selection and checkpoint admission. The missing historical runtime tree is not permission to relax admission checks or reconstruct an original saved-state test.
4. Follow the [background-runtime procedure](background-runtime-launch.md). The integration owner controls one native slot on an inactive private desktop; archive extraction itself launches nothing.

Some build and controller helpers use frozen inputs under `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs`, while native temporary runs also used `C:/HDQA/runtime-temp/haulersdream-runtime-tests`. Game references came from `C:/Steam/steamapps/common/RimWorld`. The exact relevant roots and hashes are retained in each family's handoff or machine-local selection. User-specific absolute paths are disclosed provenance; they are not credentials.

## Evidence that remains machine-local

The JSON index's `machineLocalReferences` lists relative links from the included reports to existing excluded files, with a reason and size. Examples include original saves, selected-input manifests, captured process/desktop receipts, screenshots, full event chains, module images and native source excerpts. `missingHistoricalReferences` separately records links already absent at packaging time; it is not a list of evidence that was successfully reviewed. The scanner only resolves ordinary relative Markdown links, so prose paths and absolute external paths also require the family handoff.

Keep the original raw captures and immutable checkpoints on the test machine. Public report summaries and source hashes preserve findings and provenance, but do not replace raw evidence for a new independent runtime audit. Large pin graphs and third-party source excerpts are deliberately not published. The selected archive sources do not replace those historical manifests.

F11's final fault result is specifically [independently accepted](evidence/f11-quantity-lifecycle-20260924/fault-recovery/stability-input-v4/INDEPENDENT-PAIR-REVIEW.md): candidate `a6be59c2f999430bb72361d9d70e58d5` passed135/135 with one exact declared diagnostic and zero unexpected errors, preserving387 units and the original native Wait through302 stable ticks. Baseline `53cd22b724564059bda2b4e092b1f98b` reproduces the detached split failure. [Resolution](evidence/f11-resolution.md) and [tested-source matching](evidence/f11-quantity-lifecycle-20260924/ROOT-FINAL-SOURCE-MATCH.json) close the reported single-player quantity request; actual Multiplayer and assembled-product integration remain separate. The earlier tick37 failure is preserved without inventing its unrecorded state difference.

## Packaging audit and refresh

[package-part-2-evidence.py](package-part-2-evidence.py) creates the source-only ZIP and explicit allowlist locally. It checks every archive entry's SHA-256 and ZIP CRC, rejects game/provider namespace declarations in selected C# files, and scans selected text for common credential/private-key formats. No credential matches were found in the reviewed package. This bounded pattern audit is not a guarantee against every possible secret format. No network, staging, native launch or upload is performed.

Run that script again only after the integration owner has finalized concurrent authored reports and fixture versions. Review the resulting allowlist and changed hashes before force-adding its exact paths. Do not force-add the whole ignored `docs/` directory: it contains proprietary references, binaries, raw captured data and large redundant snapshots that are outside this publication boundary.
