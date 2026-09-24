# F13 v4 native shelf walkability correction

Ready for root review before fresh Prepare. No native execution by this author. The first late pair is **failed on both products**, as recorded in [author native audit](../native-late-shelf/ROOT-AUTHOR-FIRST-PAIR-REVIEW.md). No gameplay success is inferred from exit0 or managed tests.

[`source.diff`](source.diff) changes one predicate in `StorageCommitments.ExplicitShelf`: native shelves are PassThroughOnly furniture, so their cells are `WalkableBy(map,pawn)` but not `Standable`. Actual installed `Buildings_Furniture.xml`1532 and [`Verse.GenGrid`](../native-source/Verse.GenGrid.cs.txt)121–157 establish the API distinction. Exact native type/slot count, identity, parent/effective filters, forbidden/reach checks, native StoreUtility/blockers, incoming exclusion, no-steal reservations, same-tick accounting and partial progress remain unchanged. Bare-point Standable is unchanged. Native `CanReach(...OnCell)` still guards this driver's chosen movement contract.

V4 was frozen directly from all699 exact v3 source inputs plus this one hunk. It excludes later working F12 prompt-overlay and F11 changes. Build16.54seconds, zero warnings/errors; existing32/32 focused allocation/claim/quantity tests and the storage seam guard pass. [`audit.json`](audit.json) verifies3495 checks across both successors and preserved v2/v3 inputs. The meaningful native regression now asserts actual PassThroughOnly/non-Standable/walkable/reachable shelf cells before the unchanged admission/physical oracles; no new mock test claims to prove game pathing.

- Product selector SHA256`BA3C082DFC4A9E63B37B80EBA6FEEE3857E88B3D78763A8F1060387B5942DCDE`.
- HD SHA256`DBFE452493FEA1625ECF65C8B06A4CFD4588D354B49ADF86549A2D7FF8D26BAE`, MVID`f674a453-71c8-4ac7-bbfd-27b3b79dfacd`.
- Core SHA256`AD9DB128D4BF323ED18E9024C5A245DC6DA754EAE11081529136AAE1427347BF`, MVID`2f7f43c9-c4d5-40c9-89c2-178bcdce609d`.

The separate `v2-walkable-control` source starts from originalv2 plus the identical one-hunk correction. It retains the missing truncated-group fallback on purpose. Build18.24seconds, zero warnings/errors. Its selector SHA256`D34224E60793294A4C97FF1E6C49E5C14B9BC8AC234AD73D058C63BE04A39793`; HD`2E966C9E0BCBF037E13EA77986B0F8A49E947B8BD50CE1CD5BA2D713B22944AF`, Core`E0D6673D29A91D3F0B1A1CA6D2F0F62DC90CB0AEFE3E5B2B5E928BDDEDCCD2E7`. This is a controlled source baseline, not a published-build claim.

Use [`../native-late-shelf/WALKABLE-V3-HANDOFF.md`](../native-late-shelf/WALKABLE-V3-HANDOFF.md) for that same-host pair and [`../native-fixture/WALKABLE-V5-HANDOFF.md`](../native-fixture/WALKABLE-V5-HANDOFF.md) for the original-save chain. Parent owns Prepare/native and evidence acceptance. Earlier prepared productv3/basicv4 inputs are obsolete for execution; retain them unlaunched rather than mutate their manifests.
