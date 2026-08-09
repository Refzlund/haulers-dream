// Build the whole solution (mod + core + tests) in Release.
// If RIMWORLD_MODS_DIR is set (via .env), the MSBuild post-build step also deploys the mod there.
import { $ } from 'bun'
import { resolve } from 'node:path'
import { findDotnet, repoRoot, rimworldModsDir } from './lib'

const dotnet = await findDotnet()
const extra: string[] = []
const mods = rimworldModsDir()
if (mods) extra.push(`-p:RimWorldModsDir=${mods}`)

await $`${dotnet} build Source/HaulersDream.sln -c Release -v q -nologo ${extra}`.cwd(repoRoot)

// Compile succeeded: run the static settings-drift guard (107 settings declared 3x must agree).
// Fails the build (non-zero) on any missing/mismatched setting default — see check-settings-drift.ts.
const drift = Bun.spawn(['bun', resolve(import.meta.dir, 'check-settings-drift.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await drift.exited) !== 0) throw new Error('Settings-drift check failed (see output above).')

// Guard profile-codec coverage: every serialized reference-type setting must appear in all four dispatch sites
// (CloneValue / ValuesEqual / EncodeFieldValue / ParseFieldValue). A missing case is the silent "always Custom
// (unsaved)" bug. Runtime backstop: HaulersDreamSettings.VerifyProfileIntegrity(). See check-profile-codec.ts.
const profileCodec = Bun.spawn(['bun', resolve(import.meta.dir, 'check-profile-codec.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await profileCodec.exited) !== 0) throw new Error('Profile-codec coverage check failed (see output above).')

// Guard the Steam Workshop description against Steam's 8000-character truncation limit.
const steamDesc = Bun.spawn(['bun', resolve(import.meta.dir, 'check-steam-description.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await steamDesc.exited) !== 0) throw new Error('Steam-description length check failed (see output above).')

// Guard translation parity: every non-English Languages/ folder must define the same key set + the
// same {placeholders} as English. Fails the build on missing/extra keys or dropped placeholders.
const translations = Bun.spawn(['bun', resolve(import.meta.dir, 'check-translations.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await translations.exited) !== 0) throw new Error('Translation parity check failed (see output above).')

// Guard the drop-protection defence (issues #62/#81/#87 — pawns dropping HD-scooped inventory cargo). Fails
// the build if any layer of the guard is weakened (un-healed tag read, a dropped seam, the Core policy or the
// startup tripwire removed). See check-drop-protection.ts.
const dropProtection = Bun.spawn(['bun', resolve(import.meta.dir, 'check-drop-protection.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await dropProtection.exited) !== 0) throw new Error('Drop-protection check failed (see output above).')

// Guard the #122 think-node seam boundaries (pawns read books until they starved because a throwing HD
// enhancement cost them their food node every think; vanilla logs one collapsed entry and skips the node).
// Fails the build if a seam postfix loses its degrade boundary (try + SeamDegraded, no rethrow), the
// meals-on-wheels catch stops restoring vanilla's outputs, or the Core severity gates drift.
// See check-need-seam-guards.ts.
const needSeams = Bun.spawn(['bun', resolve(import.meta.dir, 'check-need-seam-guards.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await needSeams.exited) !== 0) throw new Error('Need-seam guard check failed (see output above).')

// Guard against re-introducing vanilla's "desperate" store-cell search. Its last leg picks a random map-edge
// cell with no Home-area test (issue #231 — pawns scattering items outside the Home area) and NREs on a
// degenerate colony (issue #76), yet it looks like the obvious helper for "the unload has nowhere to put this",
// so a future simplification back to it would compile clean and pass every test. See check-no-desperate-leg.ts.
const desperateLeg = Bun.spawn(['bun', resolve(import.meta.dir, 'check-no-desperate-leg.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await desperateLeg.exited) !== 0) throw new Error('Desperate-leg guard check failed (see output above).')

// Guard the ingredient-gather gate (issue #243). Every entry point that gathers a bill's ingredients into a
// pawn's inventory must consult BillRouteGate — the player-ordered "Plan prioritized crafting…" consulted
// nothing at all, so a workbench whose "Gather ingredients" switch the player had turned off kept gathering.
// A MISSING call compiles clean and passes every test, so only an inventory of the entry points catches it.
// Fails the build if a registered entry point drops its gate, a new gather job appears outside them, or
// MayRouteToInventory stops reading the per-bench switch. See check-bill-route-gate.ts.
const billRouteGate = Bun.spawn(['bun', resolve(import.meta.dir, 'check-bill-route-gate.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await billRouteGate.exited) !== 0) throw new Error('Bill-route-gate check failed (see output above).')

// Guard the trip budget the load planner hands the fair-share rule (the "one item per trip" family). That rule is
// pure and well tested, yet the same user-visible bug shipped TWICE because the ARGUMENT was wrong both times —
// first the unbounded sentinel, then a `baseCap - running` that is 0 for any geared pawn. The unit tests cannot
// see it: HaulersDream.Tests references only HaulersDream.Core, so it observes the rule but never the arguments
// the Verse glue passes. See check-trip-budget-substitution.ts.
const tripBudget = Bun.spawn(['bun', resolve(import.meta.dir, 'check-trip-budget-substitution.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await tripBudget.exited) !== 0) throw new Error('Trip-budget-substitution check failed (see output above).')

// Guard drug-policy access (issue #232). RimWorld's DrugPolicy[ThingDef] indexer throws a message-less
// ArgumentException for a def it holds no entry for — "Value does not fall within the expected range." — and no
// test and no ordinary save reproduces it, so the shorter `policy[def]` spelling compiles clean and regresses
// silently. Fails the build if any read escapes DrugPolicyLookup, or if any part of the fix (the accessor, its
// two call sites, the missing-entry constant, its tests, the seam finalizer) goes missing.
// See check-drug-policy-access.ts.
const drugPolicyAccess = Bun.spawn(['bun', resolve(import.meta.dir, 'check-drug-policy-access.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await drugPolicyAccess.exited) !== 0) throw new Error('Drug-policy access check failed (see output above).')

// Guard the forbid-mid-walk seam (issue #250 — a colonist kept walking all the way to an item the player had
// just forbidden, and players forbid things that are UNSAFE). Eight drivers own a sweep walk; a hand-rolled
// StartPath + PatherArrival toil runs NO code between departure and arrival, compiles clean, and passes every
// unit test (the NUnit suite references only HaulersDream.Core and cannot reach a JobDriver). Fails the build
// if a walk escapes SweepWalk.MakeToil, if that seam loses its per-tick AddPreTickAction / SweepForbidPolicy
// routing, or if BulkHaul's decide+take checkpoints stop sharing the rule. See check-sweep-walk-guard.ts.
const sweepWalk = Bun.spawn(['bun', resolve(import.meta.dir, 'check-sweep-walk-guard.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await sweepWalk.exited) !== 0) throw new Error('Sweep-walk guard check failed (see output above).')

// Guard the storage commitment seam (issues #114/#138/#162/#248 — several haulers each pocketing a full stack
// for three units of room). HD strips vanilla's destination cell reservation, which also shrank every other
// hauler's job.count; a single claim ledger behind StorageCommitments replaces it, reached through exactly two
// Harmony adapters. A second capacity oracle, a lost adapter, an unreviewed commit site or a per-tick memo
// behind the seam all compile clean and pass every unit test — HaulersDream.Tests references only
// HaulersDream.Core, so it observes the decision rule and never the arguments the glue passes it.
// See check-storage-commit-seam.ts.
const storageCommitSeam = Bun.spawn(['bun', resolve(import.meta.dir, 'check-storage-commit-seam.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await storageCommitSeam.exited) !== 0)
	throw new Error('Storage-commit-seam check failed (see output above).')

// Guard "a pawn the colony does not own must not reach this" at the two seams where it matters. HD offered
// "Prioritize bulk unloading" on any pawn whose HostFaction was the player — vanilla's job-time predicate reused
// as an OFFER predicate, which admits every Hospitality guest, rescued wanderer and guest-status quest pawn — and
// the job then raised vanilla's own scribed UnloadEverything flag on the victim, opening vanilla's faction-blind
// unload work-giver on it for every hauler on the map. One shared permission rule now gates all three entry
// points, and the bulk loaders state their faction refusal explicitly instead of borrowing it from a Lord check.
// None of that is visible to a unit test: HaulersDream.Tests references only HaulersDream.Core and cannot see a
// Pawn at all, so an entry point that stops consulting the rule compiles clean and stays green.
// See check-non-colony-pawn-gates.ts.
const nonColonyGates = Bun.spawn(['bun', resolve(import.meta.dir, 'check-non-colony-pawn-gates.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await nonColonyGates.exited) !== 0)
	throw new Error('Non-colony-pawn gate check failed (see output above).')
