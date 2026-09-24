# F43 matched actual-reference compiled comparison

Both products were built from separately frozen full sources against the same actual installed reference set. This comparison is between that matched pre-removal baseline and the four-file correction, not between a rebuilt product and an old Workshop image. Both have the same declared assembly-reference identities. See `actual-reference-inputs.json` for 85 hash-bound reference pairs, `owned-files.json` for the exact four source differences, and the two `compiled-*/comparison.json` files for every compiled difference.

The existing read-only IL reviewer resolves metadata tokens to full type/member signatures, preserves local types and exception handlers, and expresses branch destinations as instruction indices. It does not discard a method difference. The tool's `expected` marker is hardcoded for its earlier F02 use and is **not** an F43 acceptance oracle. All F43 differences were reviewed explicitly below.

| Assembly | Before | After | Unchanged | Complete difference disposition |
| --- | ---: | ---: | ---: | --- |
| HaulersDream | 3,775 methods | 3,773 methods | 3,767 | Ten records listed below |
| HaulersDream.Core | 802 methods | 799 methods | 799 | Only the three obsolete recurrence API methods removed |

HD records 0001, 0002/0003 and 0008/0009 are only the compiler's `CounterFor` closure ordinal changing from DisplayClass15 to DisplayClass14 after an earlier method was removed. The paired lambda and constructor bodies match exactly after that specific generated-name substitution; the audit checks it. Record 0004 removes only initialization of `bulkAnchors`. Record 0005 removes only `bulkAnchors.Clear()` from `Clear`; the lock and exception-handling structure survive. Records 0006/0007 remove `NoteBulkAnchor` and its dictionary-pruning helper. Record 0010 retains `BuildBulkJob` instructions 0–618 and its return, removing only the former trailing `!forced` call to `NoteBulkAnchor` (old instructions 619–622). The recorded surface differences are the removed dictionary/methods and the same compiler closure rename.

Core removes `RecordNetZeroReanchor`, `ShouldBackOffReanchored`, `NetZeroSuppressUntil`, and their two now-unused threshold constants. `NetZeroBackoffTicks` remains 2500 because observed foreign retargets still use it. All 799 retained Core methods match. The actual failed-job, failed-placement and observed foreign-retarget methods in HD also match; no new branch, overload binding, reservation behavior or storage behavior was introduced.

`freeze-audit.json` passed 1,579 checks, including full paired source hashes, build-source copies, actual reference pairs, runtime/compiled identities, retained method differences and 41 passing focused tests. Metadata readers were separate bounded reflection-only processes, all joined with exit 0. Their records are under `../native-fixture/image-metadata-v2` and `image-readers-v2.json`.

This is compiled scope verification, not native gameplay acceptance. The native Has→Job witness remains to be executed and independently reviewed.
