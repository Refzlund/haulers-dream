# PB-only native clone comparison

Ready for root-controlled Prepare/Launch; not executed. The only added source body is `RunCloneProbe` in `src/PbMenuScenario.cs`; `source.diff` compares it with frozen core `src-v3`. The independent source decision is `source-review.md`.

The sole selectable profile `no-hd-pb` retains its original 11 menu cells and adds `pb-only-native-clone-defect-reproduction`. It uses actual PB data and the same sequence as production run `a2f2fabc1bfe4d3e89a8391af302b319`: create a fresh real-ID suspended PB bill, seed only its amount/interval to 7/3, call native `Clone()` twice, call `InitializeAfterClone()`, and add the pasted bill to the actual bench. It records every stage before creating the final PB entry. Expected observations are source 7/3 unchanged, clipboard temporary `-1` key 7/3, final real-ID key missing, then actual `GetOrCreateData` defaults 1/1. No clone result receives fixture field writes.

A passing cell means **the existing PB-only defect was reproduced**, not repaired. Candidate acceptance and whole-F45 acceptance remain false; all 19 pending requirements remain. Failure to reproduce fails the probe rather than silently changing the expected outcome. The existing final cleanup deletes owned bills and invokes PB's actual stale-entry cleanup; the full run must separately prove cleanup. Existing window identity/delegate/state preservation and non-input GUI boundary remain.

Direct compile completed with exit 0, 39 existing JSON DTO `CS0649` warnings and zero errors. Full output is `build.log`; `build-result.json` records the nonexistent deployment guard and certificate generation disabled. `compiled-metadata.json` used reflection-only loading: DLL `46C47380F9A5122E6AD3C40FE2D232206A665DCE734B3FF709842DD1E5AD222A`, MVID `607e6b5b-ac90-418c-9685-6d8a3458bf90`; PDB `879978381CCE7718B0CD0099197A5DD95D1C45D6C8D4BA4B26DB1BF2DC2F4168`.

The six executable controller files and action contracts are unchanged copies from core `controller-v3`. Data selection changes bind the actual new source/DLL/PDB and the 12-cell no-HD/PB roster. Existing source-tree/private-runtime protections remain. No source or data in frozen core/shared successors was changed.

Use the existing controller, supplying a real root review path/hash after inspecting the source and selection. These variables represent actual review/run values, not fabricated identities:

```powershell
$python = 'C:/Users/Arthur/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$controller = 'C:/Users/Arthur/Sync/Projects/RimWorld Mods/HaulersDream/docs/plans/evidence/f45-pb-clone-20260920/controller/run.py'
& $python -E -B -X utf8 $controller Prepare --profile no-hd-pb --review $reviewPath --review-sha256 $reviewHash
# Only after inspecting the actual prepared result, use its returned private run directory:
& $python -E -B -X utf8 $controller Launch --run $actualRunDirectory --review $reviewPath --review-sha256 $reviewHash
# Only after the actual owned native process joins:
& $python -E -B -X utf8 $controller Verify --run $actualRunDirectory --review $reviewPath --review-sha256 $reviewHash
```

The unchanged post-exit reader retains complete evidence and checks structure; the clone observations and whole native log still require semantic review. No Prepare, Launch or Verify was executed by this authoring task.
