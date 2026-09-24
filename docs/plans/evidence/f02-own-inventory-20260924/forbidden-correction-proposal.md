# Scoped correction proposal: ordinary bill candidate injection

Source review only. No product or frozen-fixture input has been edited. The meaningful forbidden-held native case remains the discriminator; retain its result even if it contradicts this diagnosis.

Add `tagged.IsForbidden(worker)` to the existing exclusion condition in `InventoryShare.AddCarrierStacks`, immediately after actual inventory membership and before bill usability/reservation. Use the actual native worker predicate, not just CompForbiddable.Forbidden and not a new custom policy.

```diff
- if (tagged == null || !owner.Contains(tagged) || !IsUsableForBill(tagged, bill))
+ if (tagged == null || !owner.Contains(tagged) || tagged.IsForbidden(worker)
+     || !IsUsableForBill(tagged, bill))
      continue;
```

Native WorkGiver_DoBill's floor and haul-source ingredient validators both use `!t.IsForbidden(pawn)` before candidate selection. The actual ForbidUtility predicate retains faction-forbidden and ritual/lord restrictions for held items; self ownership bypasses only the position/allowed-area branch. It also preserves native drafted/mental-state policy, where appropriate. A raw CompForbiddable check would have different semantics. `AddSharableStacksForBill` has already required `worker.Map != null`, and its two private AddCarrierStacks calls are the worker itself and eligible same-map carriers, so this additional native read has valid worker/map context.

The call graph is contained:

1. The ordinary bill search stores its actual worker in Patch_WorkGiver_DoBill_TryFindBestBillIngredients.
2. The chooser prefix in SharedBillPatches adds inventory candidates once per search, subject to shareForCrafting, CommonSenseCompat.GathersIngredients and WorkerMayShareCraft.
3. AddSharableStacksForBill calls AddCarrierStacks for self, then for eligible distinct carriers inside the existing radius. Both should use the requesting worker's forbidden policy. Existing membership, recipe filter, reservation, reachability, radius, deduplication and ingredient ordering remain intact.

No other code calls AddSharableStacksForBill, and no code outside that method calls private AddCarrierStacks. `FindSharableStack`/`ConsiderCarrierStack` and `CountSharable` support construction and remain untouched. The pure SharePolicy helper is shared with those paths and has no reason to gain a new parameter. `IsUsableForBill` intentionally has no worker argument and is reused by batch planning, execution, opportunistic pickup and unload decisions; changing it would expand scope and conflate recipe admissibility with a worker's permission.

The later BillGatherContract.UsableTarget already reads `thing.IsForbidden(pawn)` for retained selections at execution. Adding this early gate prevents the ordinary chooser from repeatedly preferring a forbidden held stack over valid floor ingredients; the existing later recheck remains necessary for a flag changed after selection. No Keep amount is read by this gate: Keep controls unloading and is not a recipe-use prohibition. No Common Sense detection, setting default, product custody, save format or synchronization changes are involved.

Validation if the native failure confirms the diagnosis: preserve exact failing frozen candidate, make this narrow product hunk, compile the intended selected candidate and independently review its source/callers. Reuse the same forbidden-held/allowed-floor native oracle; require the original tagged flagged40 to remain untouched, the real allowed-floor gather/DoBill to succeed, exact consumption/products, in-job cleaning and stability. The existing accepted ordinary sharing/Keep results remain relevant regression evidence, with final assembled-product integration still explicit. Do not manufacture a pure unit test around a copied boolean; the real worker/flag/candidate witness is the meaningful check.

References: `boundary-v11/InventoryShare.selected-product.txt` binds the E8FE762B… native image's actual missing gate; `independent-review/NativeForbidUtility.txt` binds installed5CF1B5BE… behavior; `../f41-unfinished-review-20260924/RimWorld.WorkGiver_DoBill.cs.txt` contains the native ingredient validators. The candidate DLL and native assembly hashes are independently pinned in boundary-v11/selection.json.
