# F11 v16: drafted forbidden-state observation

Ready for independent review, then the original-producer restart. One fixture source changed (`source.diff`); build passed in 3.25 seconds with zero warnings/errors. No product, controller, worker, producer save or record changed. No Prepare, native launch, staging or commit was performed.

Run 49a3064327674bc3aa4350741c3a429a remains failed: 36 assertions with two failures (loaded-exact-remainder and its propagated setup failure), 66 events, U0 and protectedChanges[]. Its native process8332 joined0. The exact raw LoadedGame snapshot at saved tick184 proves actor.Drafted=true and original Steel11957×8 held, with Wood11940×5, original sole tag, Keep3, lastYieldTick184 and job14. The native main-thread PauseOnLoad tick185 occurred afterward, as expected.

The assertion asked `dropped.IsForbidden(actor)` while the actor was drafted. The actual installed `ForbidUtility.IsForbidden(Thing,Pawn)` first calls `CaresAboutForbidden`; that returns false immediately for drafted pawns. Thus this term was necessarily false. It does not read the saved flag in that state. The original, untouched XML independently contains Steel11958×7 with forbidden=True, both pawns drafted, actor inventory8+5, other inventory empty and both carry containers empty. `failure-analysis.json` and the three extracted native XML objects retain these observations. Other runtime subpredicates omitted by the original receipt are not retroactively claimed observed.

The two drafted boundaries now require actual `CompForbiddable.Forbidden==true`. Their receipt explicitly includes raw and pawn-aware forbidden values, pawn care/draft state, every physical custody/count term, both inventories/hands/jobs/queues, tags, Keep, auto flag and lastYieldTick. The undrafted final `IsForbidden(actor)` check is unchanged. Exact 7 dropped /8 held →5 stored /3 original kept, native unload end condition, original tag/Keep, 180-tick stability and all previous controls remain unchanged. This is a correction to the fixture's distinction between serialized state and pawn-specific permission, not an allowance for an unproven product discrepancy.

## Frozen identities

- Selection: `8A1131FD16CA0815AB0D9A4A15727F16E2D82C1C47764C0664487E2874DE0024`.
- Host: `095A0F8F73E725A9F9E20CC7A21CF6EF86B968401531B369C30C7FFC7B1EF580`; MVID `c29a2d91-d358-4e33-af96-3c15171f4115`. Read the exact canonical host path from selection.
- Unchanged native reference: `5CF1B5BE399D5B1C9C56CA72C9D35B4ECF307FEACF5859D04AC5A1AA5926356A`, MVID `61e41735-6189-4da4-9d21-0260257b5097`. Fresh native ForbidUtility and CompForbiddable decompilations are pinned.
- Unchanged original producer13680dad781a4d09811ef322de7ce350 and its exact original v13 host remain required by the byte-identical v15 controller. Original save SHA `8C60B105E62BF5A8ECEF04BFE9CC29DB96CAFA30C8FD3CEB5DCB1FCAC7841358`; record SHA `099A3A3C1A34CBDCBAD3BCCE23EBEEC8E509F615726281429A7301B13AFCDB55`.
- `audit.json`: 1,189 checks passed, 89 build/source pairs, 85 actual reference pairs and 151 preserved original files. Metadata reader joined0. The entire v15 failed capture/source/selection remains untouched.

Use this version's separate controller and selection for F11-RESAVED, with the existing exact original producer admission and private-desktop launch. Root owns execution. This restart is not yet accepted. Existing exception/incomplete provider-callback obligations in partial-inventory-drop-runtime.md remain unproved and are a separate bounded investigation; neither successful ordinary/provider runs nor this fixture correction closes them.
