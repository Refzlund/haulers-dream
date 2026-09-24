# F11 baseline role settings correction

Ready for root review. No Prepare, game launch, build, product edit or controller code change was performed.

The original baseline role copied the controller and launch script but inherited the parent role's settings path without copying its XML. Controller line1148 resolves `../../settings-enabled.xml` relative to its own scripts folder, so line1150 found zero matching settings pins. The refusal occurred before settings generation/copy; it was not a difference in generated settings or product behavior. Preserve both original Prepare refusals and the original baseline folder.

This separate role copies the original controller and launch script byte for byte and copies the unchanged settings XML to the path the controller already requires. Only selection fields `controllerSources`, `launch`, and `settings` have new role paths. All hashes within those fields remain identical. The host, original product, providers, source proofs and every other selection field are unchanged. No guard was bypassed. The existing candidate selection and prepared candidate are untouched.

- Selection: `FC87E3BEBFCBCED43EF6EB4D1962A508925A91DA3CCC472407969DC660B17367`.
- Settings: `0050327655E432176331649A086581FF711312F9E5CE2740F5C6AF2BE31E9D26`.
- Audit: 10,323 checks passed; original baseline files and candidate selection preserved; all selected input hashes rechecked; controller-derived settings path now has exactly one matching pin.
- Host remains `6EA76A41BE40D580F050A94E793AE8B777AC7BDA7AD7AA3B8931905615FC117E`; original baseline HD15D6ECE6 / CoreC09E2132.

Root's Prepare uses this role's controller and selection, with `-CaseId F11-FAULTS -ExpectedBehavior satisfied -NegativeControl None -HdSource Built`, `-BuiltModRoot $selection.candidate.root` and `-HarnessAssembly $selection.harness.path`. Use this role's launch script for the separate private-desktop execution. Both roles ask for the same satisfied correctness contract; the baseline's expected physical failure must remain failed and independently reviewed. No later fault coverage may be inferred after an early failure.
