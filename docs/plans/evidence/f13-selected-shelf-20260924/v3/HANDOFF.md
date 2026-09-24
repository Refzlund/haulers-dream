# F13 v3: selected shelf after the pooled scan limit

Frozen source/build slice ready for review and native execution by the parent. No Prepare/native execution or closure claim here. Product v2, its review, and all earlier fixture inputs remain retained.

The exact correction is [`v2-to-v3.diff`](v2-to-v3.diff). Both explicit-shelf callers now retain `FreeUnitsFor`'s truncation flag. Only a truncated nonpositive shared answer falls back to a separate conservative measurement of the selected shelf's eligible cells. Positive and complete shared answers are unchanged. The floor does not add overlapping pooled and selected measurements.

`SelectedShelfCapacity` uses the existing `StorageGroupBudget`, claim ledger and commit policy. Compatible resident deficits and vacant native slots remain distinct. Ordinary same-def claims and the caller's outstanding pickup claim retain their deductions; each foreign def is summed and consumes vacant slots once using its real stack limit. Unknown limits refuse conservatively. Exclusive allocations have zero scalar claim because their physical cells are already excluded. Native incoming jobs, existing reservations, real filters, exact shelf identity and current-only allocation still use the original v2 checks. No second ledger, broad scan or generic capacity bypass was introduced.

Four added tests cover own/same-def deductions, aggregated foreign-def slot spending, exclusive/stale claim handling, and full commitments/unknown limits. The focused suite passed **32/32** against the actual installed reference snapshot; tested Core bytes equal the selected runtime Core. Build **25.49 seconds, zero warnings/errors**. The existing storage seam guard passes. [`audit.json`](audit.json) records **1801/1801** checks including preserved compiled v2 copies and the unchanged earlier native host images. Native acceptance remains pending.

Exact selection: [`selection.json`](selection.json), SHA256 `85FB014DAA7F52F630AA3F121B5B20F038A591DA70616B6520A01CF3C2EAD32A`.

Product root: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f13-selected-shelf-v3-20260924/Product`.

- HD SHA256 `1222CD6E7FA78067DFFDCAB1DCEBD7ABF74F1CBD3B36A3E00D3FF5F7F386076B`, MVID `fbcb1fef-2d56-4551-a183-9d200b6f70d0`.
- Core SHA256 `CEBF5A28AB017ED9C82FDEBE0411B1135549DA1E5133F046B815C1AFE6855A01`, MVID `a6708d35-37c5-4562-8d3c-c65c729f0fae`.

[`../native-late-shelf/HANDOFF.md`](../native-late-shelf/HANDOFF.md) supplies the bounded >200-cell physical regression and an identical-host v2 baseline selector. The original basic shelf lifecycle fixture remains a separate chain. F12 prompt overlay changes made after this freeze are excluded; rendered shelf UI, provider profiles and network acceptance remain separate work.
