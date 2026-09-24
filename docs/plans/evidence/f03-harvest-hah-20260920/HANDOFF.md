# F03 H&H author handoff

Ready for independent source/build review and one root-owned native reproduction. No Prepare/native execution or product/ledger changes by author.

- Scenario `src/HarvestGather.cs`: `36971D2D7EBABAB47E0FA2E6A25F6D7FF1C4D3661865006AF35810260B7557ED`.
- Actual host DLL: `37E39656ADB0D075C8EF30BED8F46FB76907878DC468652AE46030A27004D38D`.
- Actual host PDB: `7BE7F2009782BE99AE04A8DC973A9787069B0A7C229BD359C84438DA59868BD6`.
- Actual host MVID: `b237b894-1a1b-4c43-a772-fe18380dbe75`.
- Actual original H&H DLL: `525309DB8BBA69999ADFAFF5C32B7F5AE0E1D10EF887D7EED9EBB35256DDCB24`, MVID `0b1acae3-8e8c-4f36-bb80-dd3cd29ef816`; original manifest `1795465512286430422`, new full package inventory 87 files / 983,165 bytes.
- Frozen recovered HD/Core: `D0C7FBE0` / `186D8812` (all original 99-file recovery separately verified).

One DropThenHaul cotton harvest, 10 explicitly kept inventory Cloth, 7 distinct nearby Cloth. Actual production count, both placement overloads, H&H attribution/merged tracking, real automatic unload, exact physical accounting and retained kept/nearby boundaries. H&H public load threshold is configured 80→1 and restored; 180-tick grace remains. Behavioral violations finish the real unload trace and remain failed. No helper/toil/job is supplied.

First build passed zero warnings/errors. Complete source/integration delta is `source.diff`; 99 inherited host/controller support files plus launch/NuGet inputs remain byte-identical. Controller/build/launch parse cleanly. `selection.json` binds actual outputs and fresh non-TEMP inputs; README gives the inherited finite Prepare/Launch/Verify recipe. Launch is root-only through `scripts/run-on-test-desktop.py` on an inactive private desktop, with no input or visible fallback.

Limits: this is pre-repair reproduction, one configured threshold/mode/order; no claim of historical reporter reproduction, repair, opposite order, DirectToInventory, field cadence, robots or saves. The accepted four-mod harvest remains untouched. Full actual outcome must decide follow-up, not this source intent.
