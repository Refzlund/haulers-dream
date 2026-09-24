# F43 source correction ready for independent review

The query-generated recurrence detector is removed. It counted candidate job construction as hauling, then changed automatic eligibility during the same native HasJobOnThing→JobOnThing scan. The removed warning's claims of actual nonmovement and likely foreign interference were unsupported by its inputs.

Only four owned working source files changed: `Source/HaulersDream/BulkHaul.cs`, `Source/HaulersDream/HaulChurnGuard.cs`, `Source/HaulersDream.Core/HaulChurnPolicy.cs`, and `Source/HaulersDream.Tests/HaulChurnPolicyTests.cs`. Exact before/after bytes are pinned by `owned-files.json`; `source.diff` is the complete 4-insertion/285-deletion patch. F38's committed changes at 9549cad remain intact. The complete frozen sources also retain the contemporaneous shared workspace changes identically across the pair; this baseline is not advertised as a pristine checkout or the published image.

The removed behavior is only the unsupported generic inference that repeated successful candidate creation means a physical cycle. No replacement generic recorder is added. The actual failed-job/placement backoff, observed foreign retarget handling, `loopWarned`, and `NetZeroBackoffTicks` remain. F15/F17's underlying executed-loop problems stay separate and open; this change does not claim to solve all successful transport loops.

Both matched actual-reference builds passed with zero warnings/errors: baseline 40.93 seconds, candidate 42.00 seconds. The focused `HaulChurnPolicyTests`, RimIOT and unreachable-destination tests passed 41/41 (`test-results/f43-guards.trx`), preserving real-failure and foreign-retarget protections while deleting tests for the removed inference. `COMPILED-REVIEW.md` accounts for every compiled difference; retained actual guard logic is unchanged. No deployment occurred.

| Product | SHA-256 | MVID |
| --- | --- | --- |
| Baseline HD | CBA64636DC69F9EBD83C8DAD381831A3FA975008E8AB4FA41D48FC016553EAA3 | 59c3437f-5c56-4247-a313-2fcbb6f875c8 |
| Baseline Core | 6D64F41F52B2CA69BB8C633DAB7ED3FF620E29BE3121CF4AEC9D62B185BA6F9D | 1c76312e-f1d1-4b67-9217-acca51c89974 |
| Candidate HD | 5D82F55B00A00EDF6C6A7A5DABA871F9FEF5B893D6B5229A3ED95C51645C1CFD | 6939c01f-6b5f-43c9-840c-1dd41dcaa189 |
| Candidate Core | 5B654D990144002AD60CDB53C2664BC595B17DD95552D8BB22CCFC491A44884A | 8353c00e-0fab-46ef-af84-2d0644c687a1 |

The separate native handoff describes the required baseline/corrected capture. F43 is not marked resolved: native verification and independent whole-log review are pending, and the unavailable historical DropText means the reporter's exact original provider/mod combination cannot be attributed.

Suggested release wording after acceptance: “Fixed repeated haul availability checks incorrectly triggering the ‘bulk-hauled without moving’ warning and backoff. That backoff could make RimWorld's work scan report available work and then receive no job. Existing protections for actual failed hauls, placement failures and observed destination retargeting remain.” No localization/UI setting changes are needed; the removed diagnostic was a developer log message.
