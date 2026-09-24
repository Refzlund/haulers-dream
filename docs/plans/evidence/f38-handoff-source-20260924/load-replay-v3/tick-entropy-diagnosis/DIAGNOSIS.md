# F38 v3 paired fresh-load diagnosis

The reference consumer `9e01136ad9544f87a7c022937c286bd6` passed 49/49. The comparison consumer `b1d33548a84440dba067443d28043ba9` remains **failed 46/49, U0**, with 64 events. The three failures are exact boundary 3, the consequent scene guard, and host-return. This is not accepted as a successful paired replay.

The complete loaded precommand state and all 3,183 native decompression allocation records match byte for byte. The first three command boundaries match. At actual first pickup tick196, all recorded actor/job/source/physical/reservation/claim/storage state still matches. Only global `nextLogID` differs (reference0, comparison1) and `nextThingID` differs (40725,40726). `boundary3.diff` retains that exact difference. The offline audit's two replacements classify the difference only; no runtime state, reference data, or oracle was changed.

## Proven input omission

The actual `Verse.Rand` static constructor uses `DateTime.Now.GetHashCode()` (native-source/Verse.Rand.txt:103–108). `TickManager.DoSingleTick` runs normal/rare/long things, world, storyteller and map tick work without a seeded scope. Game/World serialization does not restore this static process RNG. The v3 fixture scopes only the four explicit commands. Its `Canonical` method checks that probes leave RNG unchanged, but neither logs ambient RNG nor controls the intervening simulation input. Therefore two fresh processes were never supplied identical native simulation RNG inputs. Identical original save bytes and explicit command seeds alone do not establish the claimed replay premise.

## What is and is not observed

V3 recorded Cloth and actor state, not every new Thing or log entry. No saved failure checkpoint or allocator trace identifies the newly allocated object/log. Their exact identities and call sites are **unobserved**. It would be incorrect to assert that the observed difference definitely came from filth or social interaction.

The pinned native source nevertheless demonstrates relevant RNG-dependent producers: `Pawn_InteractionsTracker` uses unscoped `Rand.MTBEventOccurs` and creates `PlayLogEntry_Interaction`; `LogEntry` obtains a new log ID when Scribe is inactive. `Pawn_FilthTracker.Notify_EnteredNewCell` uses unscoped random checks and `GainFilth` calls `ThingMaker.MakeThing`. These are plausible examples of the missing input's effect, not retrospective attribution. Motes do not allocate Thing IDs (`ThingDef.HasThingIDNumber` excludes Mote), so a social speech mote alone is not an explanation for the Thing counter.

## Bounded correction

Keep v3 unchanged. Create separate v4 record/compare consumers of original E10F save with an explicit deterministic RNG input for every **actual engine DoSingleTick** from load admission through the eleven command boundaries. Each native call gets a nested scope with a guaranteed finalizer pop and checks exact restoration of the surrounding process RNG. No native tick is called by the fixture, no native method result is replaced, and no identity/counter/state is reset. Preserve every original raw boundary comparison, including all counters, and all capacity, original-job, physical190, personal25/Keep7, and stable300 checks.

Add bounded read-only allocator receipts with actual Thing definition/ID or log ID and native call-stack frames, plus complete per-tick RNG input/output and counter receipts. Compare these new deterministic receipts between fresh consumers; retain separately observed ambient RNG values honestly as process inputs that differ. A mismatch still fails; this is a controlled replay input, not a claim of general multiplayer proof.

`actual-failed-pair-audit.json`: 1,520/1,520 custody/actual-difference checks. Native19536/controller27252 joined exit0, Default input desktop only, no remaining owned process. All copied inputs, actual images, and captured raw files match. Verify reports protectedChanges[]; generic not-verified status is retained. Whole native log has no native exception/error stack; known Mono fallback/D3D/allocator diagnostics remain preserved.
