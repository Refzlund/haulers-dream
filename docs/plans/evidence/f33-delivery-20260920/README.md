# F33 / GH259 native delivery intent

**Resolved in commit `14ce252` on 20 September 2026.** Root adopted the independent [final candidate review](candidate-runtime-review.md): all four native scenes passed, with **69 assertions, zero failures, 200 events and zero captured Unity errors**. Native PID 28260 joined normally on the inactive test desktop. No report-specific work remains; one targeted assembled-route/Multiplayer interaction check remains in [final integration](../../final-integration-checks.md). [Compact resolution](../f33-resolution.md).

The product correction changes only `InventoryConstructDelivery.cs` and `RouteExecutor.cs`. The actual scanner distinguishes Hauling from Construction before native menu code assigns the returned job's workGiverDef. Explicit HaulBuild/HaulOnly intent takes precedence. A replacement HaulOnly construction route retires the previous sustained order and uses ordinary native TryTakeOrderedJob with the same tag, rather than setting sustained Construction priority. Append, remaining explicit route jobs, other route kinds and normal autonomous work retain their behavior.

| Retained run | Actual disposition |
|---|---|
| Baseline `c38f3aafaecb4ec898d38530f0069562` | All four scenes completed; the unassigned Hauling order built through HD QueueNext, and HaulOnly separately built through native sustained Construction. Its ten failed assertions remain failed. [Review](runtime-review.md). |
| Candidate `c9d2cdd9e46b47619db368abd6019dc5` | Native delivery-only and Construction observations succeeded, then environmental flee/downed work prevented completion. The partial run remains failed; danger Thing 4244's precise identity was not captured. [Diagnosis](candidate-environment-review.md). |
| Final candidate `9cf11aacd33242cb8809506a7062cb0c` | All four scenes passed in the reviewed owned arena. Exact material/enroute/work observations, cleanup, complete logs, preserved raw evidence and process ownership were independently accepted. [Review](candidate-runtime-review.md). |

The four discriminating outcomes are:

1. Native Hauling with Build unassigned delivered five wood into a real blueprint-to-frame transition, released enroute claims and resumed ordinary work without QueueNext, FinishFrame or frame work. Both actual blueprint/frame scanner queries passed with planner visibility on/off.
2. Native Construction with Build assigned and tether enabled delivered and completed a real native FinishFrame.
3. Explicit HaulBuild with Build unassigned, unassigned planning allowed and global tether disabled still delivered and built.
4. Explicit HaulOnly with Build assigned retired a controlled prior Construction priority. It delivered without HD QueueNext or forced FinishFrame; the pawn later chose a normal nonforced native Construction job, which was recorded and accounted for.

The native provider's actual offered action is invoked in real Root.OnGUI Layout/Repaint. Routes call the actual executor. These are programmatic actions, not physical button/input verification. Read-only observers record the actual jobs, givers, queue provenance, work and completion. Material totals include ground, actor inventory/carry and native frame containers. Each settled scene is observed for 180 further native ticks before cleanup; overall and per-scene bounds remain finite. No direct work/completion call or cleanup manufactures an outcome.

The final fixture uses 80 owned granite perimeter walls around the same cleared 21×21 arena. It parks generated-map pawns through native WorldPawns KeepForever ownership both before clearing and after native destruction could release occupants. It records actual pawn/target/health identities, preserves native AI/damage and fails early if its actor becomes unavailable. Owned objects, observers and jobs are removed before original pawn placement/ownership and captured settings/speed are restored. This does not claim to reverse world-pawn biological ticks. [Arena source/build review](v3-review.md).

## Actual accepted selections

- Product: `%TEMP%/haulersdream-f33-product-v2-build-20260920`; HD `B6F29DE250445F0DC834C4A09232E47A7399DDAC8F00A325798767B3673CD662`, Core `2098CFCE61D612A86EA606EFEBF521DB59954DC60A8A7B847D6B2E99B13ACC00`.
- Host: `%TEMP%/haulersdream-f33-host-v3-build-20260920/Assemblies/HaulersDream.RuntimeHarness.dll`; SHA `CF0A7046B77F3FACDD3B73C87720FBE7A7D64105BD14F728D7660BF32F784C2B`, MVID `fcb5a8c5-a26e-4595-99ec-7f11ae4c8fd2`.
- Final fixture source: `7326AC196C265843047A67B525E60B4FF3E8CFC6855BE8D3F3B775F88FCBD71D`. `role-selection-v3.json` records the actual selected arguments.
- `product-v2-inputs.json` and `product-v2-build.json` record 423 inputs with exactly two changed product sources. Product and final host builds completed with zero warnings/errors. [Two-path source/build review](v2-review.md).

The original one-file build records, baseline host/source, intermediate host-v2 and every failed raw run remain preserved as history. The initial one-file correction alone was insufficient; it is not the accepted final product. Raw results/events/Player.log/debug log plus native outcome, desktop receipt and Verify are retained under each actual `native/<GUID>/` directory.

The existing controller retains four packages, ten measured images, private runtime protections, separate Verify and root-owned finite launch/join. Runtime starts use `scripts/run-on-test-desktop.py`; never switch to the test desktop or fall back to a visible launch. Generic Verify still records manual semantic/log review and the inherited missing scenario marker; the completed independent review supplies report-level acceptance without altering that raw report.

Earlier capability/assignment fixes are related history, not proof that an exact GH259 repair regressed. Final assembled-route/Multiplayer integration remains explicit and can reopen the item if it finds a regression. No broader report-specific matrix or repeat build/run is required by this resolution.
