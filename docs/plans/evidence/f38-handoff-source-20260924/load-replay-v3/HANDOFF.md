# F38 two fresh consumers: source handoff

Ready for independent source review. This is a separate fixture subtree; original capacity/take-inventory selections, all earlier source and the failed a120 capture remain unchanged. No product edit, Prepare, native launch, staging, commit or ledger change was performed.

`../capacity-replay/load-counter-source/DIAGNOSIS.md` and its read-only audit explain the exact native load allocation: the original E10F save contains 3,183 compressed cells, so `Map.FinalizeLoading → MapFileCompressor → ThingMaker → Thing.PostMake → ThingIDMaker` advances `nextThingID` from37,542 to40,725. Every other captured field and all41 other counters matched. The failed34/36,U0 result remains failed.

The new host supports the existing `F38-REPLAY` case with a controller-supplied `hd-replay-mode=record|compare`. Both roles consume the **same original accepted bd616 save**, never a resave. `NativeLoadAdmission.cs` separately requires the exact save SHA and native Assembly-CSharp SHA/MVID; unchanged saved state/other counters; precisely3,183 decoded entries; and every actual recreated cell's shortHash, definition, position and sequential ID in `[37542,40725)`. It records complete actual state and allocation receipts. It does not write native counters or replace IDs in evidence.

Consumer1 (`record`) records the eleven existing canonical boundary strings. Consumer2 (`compare`) compares consumer1's complete loaded-state/allocation receipts and all eleven boundary strings **byte for byte**, including actual IDs and scoped RNG. Existing command ordering, original A job/cursor preservation, reservation gates, capacity83/76/51/32, physical150+7+25+8 conservation, job-end oracles and300 settled ticks are unchanged. The old producer's boundary file remains protected/copied under an explicitly different name and is never used as the loaded replay reference.

Use this directory's `controller/scripts/runtime-test.ps1`, `selection.json`, and `launch.ps1`. Only F38-REPLAY Prepare is admitted by this revision. Give the original `-CheckpointSave` and `-CheckpointRecord` paths from bd616 to both consumers. Omit `-ReplayReferenceRun` for consumer1. After independently accepting its full native capture, pass its controller-owned run directory with `-ReplayReferenceRun` for consumer2. The original producer has one explicit frozen old-host binding; its other nine images must still match. The reference consumer must have this new host and all ten actual selected image identities, pass every assertion withU0, provide the complete11-boundary/3,184-line allocation receipts, and retain every original copied file/config hash. No hash mismatch is waived. Its manifest/result/log/events/receipts/copied files/config/metadata become protected input for consumer2. Actual runtime config mutation, if encountered, must be diagnosed rather than ignored.

Launch only through the root-owned `scripts/run-on-test-desktop.py` wrapper and this directory's `launch.ps1`. No desktop activation/input behavior changed.

| Artifact | Identity |
| --- | --- |
| Host | `44E7C84A3AA46BCAD14E8A9AD5A468A5A81E0F5C023BFB3CE6F9E08848055D1C` |
| Host MVID | `8187ca36-ea71-4b29-a778-d7d17cf88741` |
| Unchanged product | `207C6A58E6852838C923012D75095F3B5FC788C474038FD6824F43BB74616D12` |
| Unchanged Core | `815AAE0C28D7732318D9F202296A9308F39013BE2F690750086AC2A78378416D` |
| Selection | `8D2D72BAFB3C575AADB80CA999EC6C9E5A40474820FDABF56112DE0571D7F5FD` |
| Source diff | `274FAC722E6D66146482160894F262DF94031268879198924ECE07DD07FE6DFC` |

Build: actual installed native references, **5.69 seconds, zero warnings/errors**, joined exit0. Metadata uses the existing read-only PS5 reader in a hidden bounded child, joined0. `audit.json`:329 checks passed, including original selections/sources/product/provider custody and failed capture preservation. `source.diff` shows the minimal existing scene/host-project/build/controller edits and the two new helpers. Host Bootstrap is byte-identical. Initial controller adaptation stopped before writing on an over-broad text-match assertion; the final adaptation used the unique foreach target. The helper and final controller additionally pin source/build inputs before Prepare. Native pair remains pending; source/build evidence is not runtime acceptance.
