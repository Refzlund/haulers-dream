# Independent F24 three-file selection review

Accepted against HEAD `75a2771cb59fa07126067f7bb7662ace6c82db75`. `independent-audit.py` verifies all three selected files byte-for-byte against the isolated build tree and their declared hashes. Both complete selected C# files also match the exact candidate-v5 native-tested source, with only line endings differing. The working C# files contain unrelated changes and must not be staged wholesale.

The complete HEAD-to-selected diff is retained in `independent-head-to-selection.diff`. It contains only the empty-emergency-node return before end-of-work unloading, removal of Wait_MaintainPosture from idle unload checkpoints, explanatory comments and the focused changeset. Existing valid-emergency/protected-work handling, normal work diversion, medical gating, capacity, interval and downtime settings are unchanged. No F38/F40/F11/bill additions are included.

The isolated slice built successfully with zero warnings/errors, 26.91 seconds in the build log; build receipt reports exit 0 and no deployment guard. This is a compile result for HEAD plus the slice, not a claim that this smaller assembly was native-executed. Actual native acceptance belongs to the frozen candidate-v5 product/host binding documented in run `4e2e01ba4d1a4695b02b1f94064ca7c4`. No index mutation or additional test was performed by this reviewer.
