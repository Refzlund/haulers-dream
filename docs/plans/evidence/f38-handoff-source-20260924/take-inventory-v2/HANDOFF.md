# F38 TakeInventory reservation correction

Ready for root source review and the unchanged capacity/replay oracle. No Prepare, native launch, staging, commit or ledger change was performed here.

Actual capture `../capacity-replay/native/263bf64b7146410a9885013eca5784cb/INDEPENDENT-DIAGNOSIS.md` records the **48/51, U0 failure**. Original bulk job7 genuinely ended InterruptForced; automatic job10 and queued unload9 replaced it. Native TakeInventory reserves with MaxPawns10, while the original guard demanded1. The apparent effective51/free32 came from recovery, so it did not prove preserved sweep identity. Earlier mistaken source-review prose and the failed capture remain unchanged.

The sole product hunk in `source.diff` selects expected MaxPawns10 for the exact native TakeInventory def/driver already admitted by the guard. Every other supported job continues to require1. All existing force, source, default-layer, actual reservation ownership, nonzero count, exact old/new driver and job identity checks remain. Wrapper/transpiler, success postfix, target retirement, cursor/toils, native fallback and storage invalidation are untouched. The workspace patch matches `after.cs`; `before.cs` is the original frozen candidate's exact source.

The isolated source is the original 474 pinned inputs from HEAD137a905 plus the original seven-file F38 slice and this one hunk. It uses the same project/package reference arrangement. Build **24.58s, zero warnings/errors**, process joined exit0, deployment guard absent. The retained scoped compiled comparison shows **2,944/2,945 HD methods identical**, only `ExplicitSourceOwned` changed, identical assembly references and compared surface. Rebuilt Core has **451/451 identical methods** and identical references/surface; its regenerated binary is deliberately not selected. The original tested Core remains the runtime input. No new managed plan tests were run because their code and Core semantics did not change; the existing 25-case result remains scoped evidence, and the native TakeInventory boundary remains pending.

Use **this directory's `selection.json`, controller and launch wrapper** for F38-CAPACITY, followed only after a genuine pass by F38-REPLAY against that new producer's original save/record/boundaries. Scene, host, all controller bytes, exact original-job/cursor/row assertions, 83/76/51/32 arithmetic, C's32 command, physical150+7+25+8 conservation and replay equality are unchanged. The original selected runtime has97 files and no PDBs; **only HaulersDream.dll changes**. Generated PDBs remain in the isolated build evidence. All provider/content bytes and the original HD Core are retained.

| Input | SHA-256 / identity |
| --- | --- |
| Selected HD | `207C6A58E6852838C923012D75095F3B5FC788C474038FD6824F43BB74616D12` |
| HD MVID | `be57dd76-a1b4-4870-8722-d8cb3221942d` |
| Unchanged Core | `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D` |
| Unchanged host | `3390083FC72DB43B7353B9D7E399E66079468E62E58A546E7A34A381AE5C29E6` |
| Selection | `670133EEE73D17BB53AF218B2B074669F0DC4DF28705852510B83E58D340E0B3` |

`input-audit.json`: **1,305/1,305 passed**. Canonical selected product root is recorded in selection; task-owned runtime remains `C:/HDQA/runtime-temp`. The first source-copy attempt stopped before mutation on its CRLF-only text guard, then used the original file's LF correctly. The first packaging audit incorrectly expected a runtime PDB replacement despite the original97-file selection excluding PDBs; that report and audit source are preserved, and the final census correctly requires the DLL-only delta. Neither changed product scope or fixture inputs.
