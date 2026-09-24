# Transporter wording and attribution review

24 September 2026, root, independent of the feature author. I read the 17 English/Polish key pairs covering the two revised loading descriptions, the shared free-backpack threshold and all 14 new transporter keys, plus the release note and texture credit.

The changed descriptions distinguish automatic one-trip work from repeated prioritized work, require a successful delivery before continuation, preserve passengers, and stop continuation for drafting or queued replacement work. They describe the selected manifest before neighboring targets. The free-backpack threshold explicitly applies to automatic transporter unloading. The Polish additions retain those conditions and the same placeholder arguments; there is no substantive disagreement in the new behavior described.

The feature name, toggle, loading conflict, drafted refusal, no-storage refusal and missing synchronization message describe the corresponding user-visible decisions. Existing wording in the older bulk-loading paragraph is outside this new behavior review. Actual English/Polish layout and interactions remain in final integration; source text review is not rendered UI acceptance.

The changeset identifies nullpat's contribution in PR267. BulkUnloadAll-CREDITS.txt retains the contributor and original RimWorld texture provenance. Keep both with the final package. The release note's synchronization statement describes implemented registration, not a passed two-client test; actual Multiplayer remains required before completing the final PR.

The original-save positive restart now passes59/59 and the disabled-start consumer passes52/52, both with zero captured Unity errors. Root has independently read the original job17/18/19/55 endings: queued18 itself succeeds at3691 with pulled10/delivered10 at its replacement cell. In the disabled consumer, the newly issued852 job succeeds at22755 with pulled6/delivered6; its actual ID differs from the earlier failed run and must not be reported as851. Full factual native audits remain separate artifacts, with original failures retained.
