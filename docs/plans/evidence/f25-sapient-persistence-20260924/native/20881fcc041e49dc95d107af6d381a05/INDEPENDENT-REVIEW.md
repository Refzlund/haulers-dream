# F25 v4 UPGRADE — accepted; exact partial save approved for fresh RESTART

Independent review accepts **83/83 assertions, U0, 191 contiguous events** for legacy upgrade, physical hauling, native partial saving and actual same-process loading. This approves the following original checkpoint for a fresh `F25-RESTART`; it does not claim that full restart/recovery has already passed.

- Original save: `C:\HDQA\runtime-temp\haulersdream-runtime-tests\20881fcc041e49dc95d107af6d381a05\SaveData\Saves\F25-SapientCheckpoint.rws`.
- Save SHA256: `AFBE964F9E9D18183289CBA6244B06541E4E5CC3D781E4769E77CD354BB69C25`.
- Original sidecar: the same run's `evidence\checkpoint.txt`; its exact path/hash are retained in `independent-v4-audit.json`. Pass this original sidecar and save to the controller.

## Original input and actual upgrade

This run loads the unchanged accepted producer `12a8dbd2352442629bbcb7d05a0154a4` save SHA `987F4F711B86B437701B412BB9BEABA4A117059A714C75F65E664CA35BCA12A6` and record SHA `06E8D8F3930C173CADA6EFB9301576934C8C3EF097C4E03EDD547622C4B1D704`. Independent hashes match the actual source save/record and copied Autostart/input files. All fourteen non-host actual images match that original producer by SHA and MVID; the reviewed v4 host is the sole image change, SHA `A795365034AC0BF09BE687DB96EAE76AFD03A292822D9803343C16C8A09A41F8`, MVID `64b58ba0-99ca-4b1f-a4f6-c9e5a648515d`.

The original measured actors remain `HL_Mech_Lifter36147`, `HL_Monkey36152`, and `Human36162` on map 0, deriving from original native preconversion IDs 36144/36145. The original saved converted pawn nodes contain no HD fields. Native reconstruction yields exactly one definition property and one live HD comp with lookup pointing to it before the first loaded tick. Keep=0 and auto=True are explicitly normal legacy defaults, not recovered historical preferences. Native ambient cache warm-up precedes work. Two separate real conversion controls (40081→40083 and 40087→40089) retain their components and do not replace the legacy actors.

The three original actors each actually gather two stacks and deliver five units to their respective stockpiles. Partial plans are exactly jobs 55/62/70 over each actor's own seven- and eleven-unit sources. Each actor physically picks up seven before the actual drafting setter interrupts that original bulk job; the native Keep setter requests two and the synchronized auto setter opts out. The fixture then forbids only the original eleven-unit remainder using the ordinary item setter. This is explicit controlled player policy, not a fabricated reservation, comp, cargo tag, or job result. The partial save intentionally contains cancelled bulk work and drafted pawns; it does not claim to save an active bulk job.

## Native XML and same-process load

The original native XML, retained evidence copy and sidecar all match. At tick **5833**, the exact state is:

| Original actor | Tagged inventory x7 | Stored x5 | Original forbidden remainder x11 | Keep / auto |
|---|---|---|---|---|
| HL_Mech_Lifter36147 | Cloth40105 | Cloth40093 | Cloth40106 at (118,0,115) | 2 / False |
| HL_Monkey36152 | Gold40109 | Gold40098 | Gold40110 at (118,0,124) | 2 / False |
| Human36162 | Steel40123 | Steel40101 | Steel40124 at (118,0,133) | 2 / False |

Independent XML parsing checks unique pawn/item nodes, true draft flags, exact inventory ownership, all three tag references, exact material/value Keep dictionaries, auto=False and each remainder's map/count/position/forbidden=True. XML additionally contains last-yield ticks 4676/5178/5833. The live equality oracle compares identity, physical cargo, tags, Keep, auto, draft and remainder state; it does not separately assert those last-yield values after loading.

The native `GameDataSaveLoader.LoadGame` request is event 169. Event 170 is the actual LoadedGame callback at tick 5833. It resolves the original saved IDs without creating cargo or setting flags, and checks the complete sidecar state before the first tick. The new map replaces the original map in PID 25772. At tick 5834, the ordinary native pause-on-load boundary and complete same-process state equality pass again. The original saved tile mean 17.4 is preserved; transient room temperature differs by native save quantization while remaining medically suitable. No scene reconstruction occurs during load.

## Custody, logs and remaining scope

The independent audit rehashes all **1,969** copied files, checks all fifteen native image/metadata/manifest identities, checks raw evidence against original native files, and examines every recorded physical state for unique item identity and the expected per-material 0→5→23 totals. All three final material totals are 23. Provider sources/packages are the exact previously selected public builds; this is not an arbitrary Workshop-version compatibility claim.

All 2,322 Player.log lines were covered through full non-profile review and profiling/allocator classification, with the retained numbered non-profile extract. There are two familiar dynamic-image fallback probes, fifty provider/header texture dimension messages, Direct3D timing output and ordinary allocator summaries. Actual Autostart and F25-SapientCheckpoint loading are both logged with the exact seven packages. No actionable exception, XML, duplicate/unresolved reference, component or save error was found; U0 and the full debug log agree with the measured job chain.

Native PID 25772 and private controller PID 22832 joined with exit 0. Only `Default` input desktop was sampled, no desktop switch occurred, and no owned process survived. Verify reports `protectedChanges=[]`; its generic manual/event/log flags are retained, not hidden. Prior v3 failures remain failed. Next required acceptance is a **fresh process** loading this exact original partial checkpoint, followed by genuine five-unit surplus recovery, allowing and hauling each exact original eleven-unit remainder, and 300 stable ticks at 21 stored + 2 kept per actor.

Review script: `../../review-v4-native.py`. No source edit, Prepare, native launch or ledger mutation by the reviewer.
