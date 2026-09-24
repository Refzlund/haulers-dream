# F25 — sapient animals and mechs retain inventory hauling

Resolved on 24 September 2026 in commit `70bd490`. Root read and accepted the complete independent evidence chain and adopted the reviewed XML, compatibility guidance and changeset. Final combined-PR integration remains open.

## Report-specific disposition

| Frozen source | Reported requirement | Evidence and disposition |
|---|---|---|
| C023 | Converted animals lose bulk hauling after save/reload. | Original saved `HL_Monkey36152` lacks the component on baseline; the candidate reconstructs exactly one and physically hauls. Candidate-written tags, Keep and opt-out survive same-process load and full restart. Resolved for the selected provider build. |
| C025 | Sapient mechs stop bulk hauling; fresh spawning/race changes affect it. | Actual Sapienator lifter conversion, generated/live component identity, original saved lifter upgrade and postrestart physical hauling are verified. Native work/race flags remain intact. The generated-definition whitelist omission explains race dependence without changing eligibility. These baseline providers fail already after fresh conversion; no claim that this capture reproduced the reporter's initially-working timeline or directly retested their race-swap workaround. Resolved underlying component-lifetime defect. |
| GH263 | Sapient animals and mechs must retain inventory hauling after load and fresh application start. | Both original converted actors, plus an ordinary human, keep their exact serialized state before the first tick in a fresh process. Each then recovers five surplus units, bulk-hauls its original eleven-unit remainder and remains stable at 21 stored + 2 kept. Resolved. |
| GH263-C5360067014 | Reduced HD + Sapient Animals + requirements reproduces with a lifter. | Native tests use the corresponding minimal provider set with actual Biotech lifter support. No large-mod-list assumption is required. Provider-absent startup and ordinary eligibility controls also pass. Resolved. |

This is **RG04, one reproduction campaign across channels**, not four independent regressions. The frozen reports and historical L09 entry contain no established prior fix of this exact sapient race persistence defect. Generic mech-capacity work in PR45/53/198 does not establish a recurrence. Classify it as a newly demonstrated generated-component lifetime omission; the earlier fixes suggest lifecycle coverage worth retaining, not proof this fix regressed.

The correction uses only the provider's supported `HumanlikeAnimalSettings.compWhitelist` extension point, conditional on `RedMattis.BetterPrerequisites`, for the exact `HaulersDream.CompHauledToInventory` type. It neither broadens animal/mech permissions nor injects components through a periodic repair. The selected current and pre-report Framework generator/morpher bodies and relevant whitelist XML were identical in the retained source comparison. The reporter's exact historical Workshop bytes were unavailable, so do not turn that comparison into historical binary equivalence or blanket certification of all Big & Small races/load orders.

## Accepted evidence chain

- [Review](f25-sapient-persistence-20260924/native/12a8dbd2352442629bbcb7d05a0154a4/independent-review.md): original native conversion and legacy checkpoint, 59/59; missing component already observable on fresh converted baseline pawns.
- [Review](f25-sapient-persistence-20260924/native/c33d4879de85422ea051b286ddd36d0c/independent-review.md): genuine baseline missing-component/real-command failures, retained **54/57** result.
- [Review](f25-sapient-persistence-20260924/native/20881fcc041e49dc95d107af6d381a05/INDEPENDENT-REVIEW.md): original legacy-pawn upgrade, fresh conversion controls, real multi-stack delivery, partial cargo native save and actual same-process reload, **83/83**.
- [Review](f25-sapient-persistence-20260924/native/a966470b31f54adbb54a312b166a655b/INDEPENDENT-REVIEW.md): provider-absent XML/startup and human/animal eligibility control, **42/42**.
- [Review](f25-sapient-persistence-20260924/native/f3bbd8cb72914ba1ae011adb187b4f58/INDEPENDENT-REVIEW.md): original partial checkpoint in a fresh native process, retained state and productive recovery, **65/65**, 300 stable ticks.

All accepted captures have zero captured Unity errors. Full reviews retain benign warning/diagnostic output and actual unload `Incompletable` endings after completed physical delivery; do not summarize every job as `Succeeded`. Prior fixture failures and the failed cross-actor nearby-sweep run remain visible. Normal defaults on an old save with no HD fields are not recovery of vanished settings.


The implementation changes only `Defs/Compatibility/BigAndSmall.xml`, `COMPATIBILITY.md` and its patch changeset. The committed XML matches the native-tested SHA-256 `48D55D64A5E77E83D668EBDFC28486ABB2A1E15CBA904CF70548AF21DA57912C`. No product DLL change or new persistence schema is required.
