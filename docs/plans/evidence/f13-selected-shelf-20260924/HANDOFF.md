# F13 selected shelf — source slice ready for independent review

Historical v2 handoff below. The current frozen correction is [v4/HANDOFF.md](v4/HANDOFF.md); root `selection.json` now selects v4. The original v2/v3 selections and failed native evidence remain unchanged.

2026-09-24. This slice extends the frozen F12 UI v2 implementation to an exact selected native shelf. **No F13 native execution, Prepare, provider support, multiplayer execution, rendered UI acceptance, or ledger closure is claimed.** Parent owns execution and disposition. All F12 frozen families remain unchanged.

## Frozen candidate

- Product: `C:/Users/Arthur/AppData/Local/HaulersDreamQA/inputs/f13-selected-shelf-v2-20260924/Product`.
- Exact owned delta: [`v2/source.diff`](v2/source.diff); 32 files. Full changed sources are under `v2/source/`; [`v2/changed-inputs.json`](v2/changed-inputs.json) lists them.
- Selection: [`v2/selection.json`](v2/selection.json), SHA256 `F90E0F34158F4D3CD5A3E0BAE3C10DCACE274E712DDBE28C510E0DB0D15C17D1`. Root `selection.json` is byte-identical.
- HD SHA256 `01A1A612765C048514C5C43BD4F2EB39A95A0C65B62AEEA55F5073934C8D07D5`, MVID `7fd6efd5-5f57-4b94-9383-b74180c8f045`.
- Core SHA256 `698EE808BF13359E9170869BD61BAE01B88651271C853D32AFF710B45129AF2A`, MVID `a18aa1b6-c143-485b-aa68-7e415283799f`.

`freeze-build.py` starts from the exact frozen F12 UI v2 source and applies only the owned changes. Every changed pre-existing C# and locale file's before snapshot was required to equal that frozen baseline. Other working-tree changes, including the separate F11 recovery slice, are excluded. Original before snapshots and the first successful v1 build remain retained. v2 changes the native competition guard to inspect real reservations (including immediately after load), removes an unnecessary own-cell add-back, stops an old path when changing trip cell, and extends the existing commit-seam guard's explicit allowlist.

## Behavior and accounting

The order saves the exact shelf reference/ID, map, original position/rotation, destination kind, initial selected cell, and current trip cell. The synchronized shelf endpoint revalidates source, actor, amount and destination before creating the order. Current job identity includes the exact shelf in target C. Moving, despawning, minifying, destroying or replacing that shelf cannot select a replacement at the same coordinates. A later eligible cell must belong to that same shelf, including when shelves share storage settings.

Only an actual current explicit driver can acquire capacity. It conservatively reserves one native destination cell for one trip. The allocation metadata resides in the existing `storageClaims` row; there is no second capacity ledger. Physical measurement excludes the reserved cell across item definitions, so that row is deliberately not subtracted again as scalar capacity. Acquiring or releasing the row invalidates the existing capacity memo in the same tick. Queued and preview orders do not allocate capacity. On load, actual reservations provide exclusion before the driver rebuilds the unscribed row; capacity is priced afresh.

The shelf admission path checks exact cell parent, actual native/effective and parent filters, explicit HD deny settings, forbidden state, reach, directional stacking, real slot counts and native maximum slots. Existing incoming native/HD jobs retain priority even if HD previously removed their destination reservation. Conversely, the existing native `HaulToCell_NoCellReservation` gate refuses a cell held by another actual explicit shelf order before its forced-path interruption logic. It never steals the claim or interrupts the other pawn.

The native `IsGoodStoreCell` carrier check calls `CanReserveNew`, which rejects even the caller's own reservation. Only for the exact current owner, this implementation supplies no carrier/faction to that native physical predicate, while retaining separate exact reservation, foreign-reservation, forbidden and reach checks. See [`native-source/StoreUtility.cs.txt`](native-source/StoreUtility.cs.txt), around line 339, and the new `StorageCommitments.ExplicitShelf.cs`.

Actual transfer remains the existing exact F12 parcel path. Each placement releases its allocation. A genuine partial placement can continue to another eligible cell on the same shelf; no eligible cell blocks the order and invokes the existing native cleanup/recovery custody. Cancel and interruption release exact owned reservations and allocation rows. Keep and personal inventory behavior is unchanged.

The existing amount/target/progress UI recognizes shelf targets and dispatches the authoritative shelf endpoint. A rejected shelf target never falls through to a bare-cell order. Sixteen locale files update only the two destination descriptions and add two shelf labels; their other 33 keys are preserved. Existing point destinations remain available. This has source/build acceptance only, not rendered-input acceptance.

## Verification retained

- Actual installed reference snapshot: 85 file pairs; installed `Assembly-CSharp` SHA256 `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`.
- Isolated v2 product build: 17.62 seconds, zero warnings/errors; [`v2/build.log`](v2/build.log), joined process receipt alongside it. Deployment target is a checked nonexistent task-owned path.
- Exact v2 focused tests: **28/28 passed**, zero skipped, covering shared storage claims, exclusive-cell accounting and explicit quantity bounds; [`v2/tests/f13-focused.trx`](v2/tests/f13-focused.trx). Tested Core bytes equal selected runtime Core bytes. v1's earlier passing TRX remains retained.
- Existing storage commit-seam guard passes on the isolated v2 source: 270 source files, 11 allowlisted commit sites across 6 reviewed files, original adapter/writer/startup checks retained. [`v2/storage-commit-seam.log`](v2/storage-commit-seam.log).
- [`v2/audit.json`](v2/audit.json): **945/945** checks, 698 source pairs, 85 reference pairs, 153 exact runtime files, metadata and selection checks. No test/build output is included in the runtime product tree.
- [`locale-audit.json`](locale-audit.json): all 16 key/placeholder sets pass. This checks bindings, not visual layout or linguistic review.
- The metadata helper's initial PowerShell module lookup failure is preserved as `v2/metadata-first-failure*`; the successful helper uses .NET hashing and reflection-only assembly reading. It did not change the candidate.

## Smallest meaningful native follow-up

1. Choose a farther/lower-priority native shelf despite a closer automatic destination and a linked sibling. Fill its first cell and prove actual delivery to another cell of that same shelf, never the sibling. Preserve exact source/hand/destination quantities and personal/Keep identities.
2. Exercise physical capacity while carrying: use actual Steel limits, with a real 72/75 resident and seven incoming units after pickup. Prove three accepted/four retained when other slots are genuinely unavailable, then same-shelf continuation or blocked cleanup/cancel. Do not assume Silver's stack limit is 75; it is 500 in this installation.
3. Exercise competing current actors with same and different definitions, including same-tick claims and a small shared capacity. Prove native incoming-first refusal without its reservation and the reverse preselected native path refusal while the explicit job holds the cell. Check real reservations, shared rows, physical totals and no double subtraction.
4. Exercise forbidden/filter/full and shelf identity changes without fallback. Save a genuine partial/current+queued chain, admit original raw XML and exact job/shelf links, load in a fresh process before ticks, and prove actual continuation/cancellation with no serialized allocation or stale claim.

First bounded support is the exact native `Building_Storage` type with unchanged native slot limits and 1x1 cargo. Storage subclasses, changed-slot providers, named ASF/minified/quantity profiles, actual multiplayer synchronization, and rendered shelf targeting remain required follow-ups; they are deliberately refused or unverified here. This slice is ready for source review and a native fixture, not F13 closure.
