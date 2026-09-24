# F13 late-shelf host v3 — final walkability-bound pair

Only scene change is [`walkable-receipt-v3.diff`](walkable-receipt-v3.diff): prove actual selected shelf PassThroughOnly, non-Standable, carrier-walkable and OnCell reachable before existing assertions. All capacity225→150, native ordinaryGold7, explicit7/15, blocker/conservation and300-tick oracles remain unchanged. Original v2 host and both failed native runs remain preserved. Product correction/source control are described in [`../v4/HANDOFF.md`](../v4/HANDOFF.md).

Host build2.11seconds, zero warnings/errors. [`v3/audit.json`](v3/audit.json) verifies731 checks,83 compiled source pairs,85 installed references, both exact selectors and identical controller bytes. Host SHA256`004F97682ED05751A2FF5DF8E7DA19177DCA3B89CA9568E1FF73F5CF0361F15F`, MVID`96b0f817-b7cd-45ba-95c7-1037a382e3a2`.

- Candidate selector [`selection.json`](selection.json), SHA256`9F5E49C81DBC9DEFB63D0D12121CECFF6272977ED0ED7B5AFEAFF29B221F258D`; frozen productv4.
- New control selector [`baseline-walkable/selection.json`](baseline-walkable/selection.json), SHA256`F29BC280C543516876E1E5F99CC5C07B7C7D6E9D5B80A00F5F1EDBA56C31EC6E`; original productv2 with the identical walkability hunk.

Use each selector's own adjacent `controller/scripts/runtime-test.ps1` and `launch.ps1`. For either role, set TEMP/TMP to`C:/HDQA/runtime-temp`, read that role's selection and call `-Action Prepare -CaseId F13-LATE -HdSource Built -ExpectedBehavior satisfied -NegativeControl None -BuiltModRoot $selection.candidate.root -HarnessAssembly $selection.harness.path`. No source save or companion profile. Root reviews the fresh manifest, then runs that role's launch through the inactive-desktop wrapper and retains complete Verify/raw/process receipts. No Prepare/native performed by author.

The control must pass the new native cell receipt and actual truncated-zero setup, then fail `late-selected-shelf-now-admitted` with allowance0. Candidate must pass the entire physical case. Any earlier failure is a different setup/admission defect, not this causal witness. Neither old v2 nor old v3 failure is converted into a passing test. This family does not establish provider, UI, save/restart or network acceptance.
