# v13 restart config hash finding

Read-only inspection of restart2320b5bd599440f5a919d007044ca80f confirms controller-v12 line556 rejects only mutable RIMMS config. Copied save remains B38AD01E70F70F618FDCE2E18A15FC89C7295FEABF7FF21319C69D5C75889AE4 and copied record remains 1D0383BEA423E21F24CBD4BF9EA4B0D61A7C3D47EBB50405C8522313B4D97569, both matching the original manifest. Expected config B5E0BD85FDF27808C6E618F6B606C71D2D6CB0402AE6A9EB03E30E42E733DAD9 became 2F4BA93264EDB60113AA9D12F5DD7555DDDB7C93ADF4418C618BDA4AB75A23AB with empty workGivers.

The selected v13 scenario explicitly invokes edit.reset(); qol.WriteSettings() after successful loaded delivery, asserts restored defaults and repeats guarded reset in cleanup. Treating this private writable config as immutable after that scenario contradicts its declared observation. This does not authorize modification of copied save, manifest, result or any original evidence.

Recommendation sent to root: preserve the original controller and Verify failure; use a separate read-only postrun verifier retaining exact save/record checks, original source-config hash and prelaunch-copy/launch provenance; separately report the complete postrun config delta. Root created controller-v12-postrun and an exact-XML delta witness. Independent review of these and the actual outcomes follows. This continuation did not edit either controller or an existing run.

Queued-save-v14 does not reset/WriteSettings on restart; its strict postrun config equality stays in place. This specific v13 exception must not become a general waiver of input provenance.
