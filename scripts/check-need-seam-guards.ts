// Static guard against silently REGRESSING the #122 seam boundaries that keep a pawn's think-tree nodes
// alive when an HD enhancement throws.
//
// The bug this pins (issue #122, "pawns read books until they starve to death"): RimWorld's think
// infrastructure (ThinkNode_Priority / ThinkNode_PrioritySorter, decompile-verified) catches a throwing
// child node, logs it (one entry the log window collapses under its repeat counter), and SKIPS it. A
// mid-job pawn's only urgent-need rescue is a node clearing CheckForJobOverride's min priority
// (JobDriver_Reading re-checks every 600 ticks at 9.1; JobGiver_GetFood reports 9.5, while JobGiver_GetRest
// reports at most 8, below the threshold, so the mid-book rescue is FOOD-only). HD postfixes several of
// those nodes, and its enhancements fan out into surplus math, storage scans, the load ledger, and compat
// shims (Simple Sidearms / Grab Your Tool / DBH / CE / Vehicle Framework), plus vanilla calls other mods
// patch (FoodUtility.BestFoodInInventory). A repeatable throw anywhere in that graph used to cost the pawn
// its FOOD node on every think while the JOY node kept issuing "read a book": the pawn read nonstop,
// refused every other task, and starved to death. The same class of failure on the mech CHARGE node
// (JobGiver_GetEnergy_Charger) would drain a mech to forced shutdown.
//
// The fix is a per-seam boundary: each HD postfix catches its OWN enhancement's throw, reports once with
// attribution (HDGuard.SeamDegraded), and leaves vanilla's already-computed result standing, so the pawn
// still eats/sleeps/works/charges. This script fails the build (exit 1) if any layer is weakened:
//   1. Each seam's Postfix keeps its try + HDGuard.SeamDegraded boundary (checked within the Postfix body,
//      so a vestigial try elsewhere in the class cannot mask a removed boundary), and the Postfix contains
//      no rethrow (bare `throw;`, `throw new ...`, or a rethrow of any caught variable, whatever its name).
//   2. The meals-on-wheels catch RESTORES the ref outputs to vanilla's values (foodSource/foodDef/__result).
//   3. HDGuard.SeamDegraded exists, logs via ErrOnce, and does NOT rethrow; SeamThrew still rethrows.
//   4. The food/rest severity gates route through the Core policy (OpportunisticUnloadPolicy), whose
//      boundary categories the NUnit oracle tests pin.
//
// Run directly to self-check:  bun scripts/check-need-seam-guards.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const HARMONY_PATCHES = resolve(repoRoot, 'Source/HaulersDream/HarmonyPatches.cs')
const LOAD_DEPOSIT = resolve(repoRoot, 'Source/HaulersDream/Patch_OpportunisticLoadDeposit.cs')
const EAT_FROM_INVENTORY = resolve(repoRoot, 'Source/HaulersDream/EatFromInventory.cs')
const MECH_CHARGE = resolve(repoRoot, 'Source/HaulersDream/Patch_MechShedCargoBeforeCharge.cs')
const HDGUARD = resolve(repoRoot, 'Source/HaulersDream/HDGuard.cs')
const POLICY = resolve(repoRoot, 'Source/HaulersDream.Core/OpportunisticUnloadPolicy.cs')
const TESTS = resolve(repoRoot, 'Source/HaulersDream.Tests/OpportunisticUnloadPolicyTests.cs')

// The guarded seam postfixes: (file, patch class, the seam-name string the SeamDegraded call must carry).
// The seam string doubles as the ErrOnce dedupe key, so it must stay stable and distinct per seam.
const SEAMS = [
	{
		file: HARMONY_PATCHES,
		cls: 'Patch_JobGiver_UnloadYourInventory',
		seam: 'JobGiver_UnloadYourInventory.TryGiveJob (HD unload substitution)',
	},
	{
		file: HARMONY_PATCHES,
		cls: 'Patch_JobGiver_Work_OpportunisticUnload',
		seam: 'JobGiver_Work.TryIssueJobPackage (HD opportunistic unload)',
	},
	{
		file: HARMONY_PATCHES,
		cls: 'Patch_JobGiver_GetRest_UnloadFirst',
		seam: 'JobGiver_GetRest.TryGiveJob (HD unload-before-sleep)',
	},
	{
		file: HARMONY_PATCHES,
		cls: 'Patch_JobGiver_GetFood_UnloadFirst',
		seam: 'JobGiver_GetFood.TryGiveJob (HD unload-before-eating)',
	},
	{
		file: HARMONY_PATCHES,
		cls: 'Patch_JobGiver_GetJoy_UnloadFirst',
		seam: 'JobGiver_GetJoy.TryGiveJob (HD unload-before-leisure)',
	},
	{
		file: LOAD_DEPOSIT,
		cls: 'Patch_OpportunisticLoadDeposit',
		seam: 'JobGiver_Work.TryIssueJobPackage (HD opportunistic load deposit)',
	},
	{
		file: EAT_FROM_INVENTORY,
		cls: 'Patch_TryFindBestFoodSourceFor',
		seam: 'FoodUtility.TryFindBestFoodSourceFor (HD meals-on-wheels)',
	},
	{
		file: MECH_CHARGE,
		cls: 'Patch_MechShedCargoBeforeCharge',
		seam: 'JobGiver_GetEnergy_Charger.TryGiveJob (HD shed-cargo-before-charge)',
	},
]

const errors: string[] = []

/** Brace-match a block: from the first `{` at/after `from`, return the content between the braces. */
function braceSlice(src: string, from: number): string | null {
	let i = src.indexOf('{', from)
	if (i < 0) return null
	let depth = 0
	const start = i
	for (; i < src.length; i++) {
		const c = src[i]
		if (c === '{') depth++
		else if (c === '}') {
			depth--
			if (depth === 0) return src.slice(start + 1, i)
		}
	}
	return null
}

/** Slice a class body by brace-matching from its `class <name>` declaration. Null if not found. */
function sliceClassBody(src: string, className: string): string | null {
	const sig = new RegExp(`\\bclass ${escapeRe(className)}\\b`).exec(src)
	if (!sig) return null
	return braceSlice(src, sig.index)
}

/** Slice a method body by brace-matching from its definition (signature ending in `{`). Null if not found.
 *  `\b<name>\s*\(` cannot match a longer identifier (e.g. `PostfixInner` when asked for `Postfix`): the
 *  character right after the name must be `(` or whitespace. */
function sliceMethodBody(src: string, methodName: string): string | null {
	const sig = new RegExp(`\\b${escapeRe(methodName)}\\s*\\([^)]*\\)\\s*\\{`).exec(src)
	if (!sig) return null
	return braceSlice(src, sig.index)
}

/** The identifiers bound by `catch (<Type> <name>)` clauses in a body, e.g. ["ex"]. A bare `catch` or a
 *  `catch (<Type>)` without a variable binds nothing and contributes nothing. */
function catchVariables(body: string): string[] {
	const names: string[] = []
	const re = /catch\s*\(\s*[A-Za-z_][\w.]*\s+([A-Za-z_]\w*)\s*\)/g
	let m: RegExpExecArray | null
	while ((m = re.exec(body)) !== null) names.push(m[1])
	return names
}

/** True if `body` contains a rethrow: bare `throw;`, `throw new ...`, or `throw <var>` for any variable
 *  bound by a catch clause in the same body (robust to a renamed catch variable, the QA-flagged gap).
 *  Word-prose like "a throw there breaks" never matches: the token after `throw` must be `;`, `new`, or a
 *  caught variable name. */
function hasRethrow(body: string): boolean {
	if (/\bthrow\s*;/.test(body) || /\bthrow\s+new\b/.test(body)) return true
	for (const name of catchVariables(body)) {
		if (new RegExp(`\\bthrow\\s+${escapeRe(name)}\\b`).test(body)) return true
	}
	return false
}

async function read(path: string, label: string): Promise<string | null> {
	const f = Bun.file(path)
	if (!(await f.exists())) {
		errors.push(`${label} is MISSING (${path}). The need-seam guard cannot verify it.`)
		return null
	}
	return (await f.text()).replace(/\r\n/g, '\n')
}

async function main() {
	// 1. Each seam's Postfix keeps its degrade boundary. All checks are scoped to the Postfix METHOD body
	//    (not the whole class), so a vestigial try in a helper cannot mask a boundary removed from the
	//    postfix itself.
	for (const seam of SEAMS) {
		const src = await read(seam.file, `${seam.cls} source`)
		if (!src) continue
		const cls = sliceClassBody(src, seam.cls)
		if (cls === null) {
			errors.push(`${seam.cls} not found in ${seam.file}. The #122 boundary for "${seam.seam}" is unverifiable.`)
			continue
		}
		const postfix = sliceMethodBody(cls, 'Postfix')
		if (postfix === null) {
			errors.push(`${seam.cls}.Postfix not found. The #122 boundary for "${seam.seam}" is unverifiable.`)
			continue
		}
		// A real `try {` block inside the Postfix (statement, not the word "try"/"retry" in a comment).
		if (!/\btry\s*\{/.test(postfix)) {
			errors.push(
				`${seam.cls}.Postfix has no try block. Its enhancement runs unguarded inside a think node again: ` +
					`a repeatable throw costs the pawn that node on every think (the #122 reading-until-starvation ` +
					`class; vanilla logs one collapsed entry and skips the node). Restore the try/catch + ` +
					`HDGuard.SeamDegraded boundary.`
			)
		}
		// The SeamDegraded call with the exact stable seam string. The first argument is the caught exception
		// variable, whatever it is named (robust to a rename); the second is the pinned seam string.
		const call = new RegExp(`HDGuard\\.SeamDegraded\\(\\s*[A-Za-z_]\\w*\\s*,\\s*"${escapeRe(seam.seam)}"`)
		if (!call.test(postfix)) {
			errors.push(
				`${seam.cls}.Postfix no longer reports through HDGuard.SeamDegraded(<caught>, "${seam.seam}", ...). ` +
					`Either the boundary was removed (re-opens #122) or the seam name drifted (breaks the per-seam ` +
					`dedupe key and this guard). Keep the exact string in both places.`
			)
		}
		// No rethrow anywhere in the Postfix: bare `throw;`, `throw new ...`, or `throw <caught-var>` under
		// any variable name. A rethrow kills the whole think node for the scan and the pawn loses its
		// food/rest/work/charge selection; faults still surface via HDGuard.SeamDegraded's ERROR log.
		if (hasRethrow(postfix)) {
			errors.push(
				`${seam.cls}.Postfix contains a throw statement (bare, new, or a rethrow of a caught variable). ` +
					`The #122 boundary must degrade (keep vanilla's result), never rethrow.`
			)
		}
	}

	// 2. The meals-on-wheels catch restores the ref outputs to vanilla's values. Without the restore, a throw
	//    after a partial write could hand JobGiver_GetFood a torn foodSource/foodDef pair.
	const eat = await read(EAT_FROM_INVENTORY, 'EatFromInventory.cs')
	if (eat) {
		const postfix = sliceMethodBody(eat, 'Postfix')
		if (postfix === null) {
			errors.push('EatFromInventory.cs: Postfix not found; the meals-on-wheels restore check cannot verify it.')
		} else {
			for (const token of ['foodSource = vanillaFoodSource', 'foodDef = vanillaFoodDef', '__result = false']) {
				if (!postfix.includes(token)) {
					errors.push(
						`EatFromInventory.cs Postfix no longer restores "${token}" in its catch. A throw mid-scan must ` +
							`hand vanilla's exact failed-search outputs back to JobGiver_GetFood, or the caller reads a ` +
							`torn result.`
					)
				}
			}
		}
	}

	// 3. HDGuard: SeamDegraded logs once and does not rethrow; SeamThrew still rethrows (the Finalizers rely
	//    on it to keep foreign faults visible).
	const guard = await read(HDGUARD, 'HDGuard.cs')
	if (guard) {
		const degraded = sliceMethodBody(guard, 'SeamDegraded')
		if (degraded === null) {
			errors.push('HDGuard.cs no longer defines SeamDegraded; every #122 boundary call site breaks.')
		} else {
			if (!degraded.includes('ErrOnce'))
				errors.push('HDGuard.SeamDegraded no longer logs via HDLog.ErrOnce; a degraded seam would be silent (suppression).')
			if (/\breturn ex\b/.test(degraded) || hasRethrow(degraded))
				errors.push('HDGuard.SeamDegraded rethrows; it must degrade (log + return void) or the boundary is a no-op.')
		}
		const threw = sliceMethodBody(guard, 'SeamThrew')
		if (threw === null) {
			errors.push('HDGuard.cs no longer defines SeamThrew; the whole-method Finalizers break.')
		} else if (!threw.includes('return ex')) {
			errors.push('HDGuard.SeamThrew no longer returns the exception; a Finalizer returning null SWALLOWS foreign faults.')
		}
	}

	// 4. The food/rest severity gates route through the Core policy the oracle tests pin.
	const harmony = await read(HARMONY_PATCHES, 'HarmonyPatches.cs')
	if (harmony) {
		if (!harmony.includes('OpportunisticUnloadPolicy.MaySwapFoodJobForUnload'))
			errors.push(
				'HarmonyPatches.cs no longer gates the food swap through OpportunisticUnloadPolicy.MaySwapFoodJobForUnload; ' +
					'the Starving stand-down is then untested and free to drift.'
			)
		if (!harmony.includes('OpportunisticUnloadPolicy.MaySwapRestJobForUnload'))
			errors.push(
				'HarmonyPatches.cs no longer gates the rest swap through OpportunisticUnloadPolicy.MaySwapRestJobForUnload; ' +
					'the Exhausted stand-down is then untested and free to drift.'
			)
	}
	const policy = await read(POLICY, 'OpportunisticUnloadPolicy.cs')
	if (policy) {
		for (const token of ['MaySwapFoodJobForUnload', 'MaySwapRestJobForUnload', 'HungerStarving = 3', 'RestExhausted = 3']) {
			if (!policy.includes(token))
				errors.push(`OpportunisticUnloadPolicy.cs is missing "${token}" (the severity boundary the oracle tests assert).`)
		}
	}
	await read(TESTS, 'OpportunisticUnloadPolicyTests.cs') // existence is the assertion

	if (errors.length > 0) {
		console.error(`\n[need-seam-guards] FAIL, ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  x ${e}`)
		console.error(
			`\n  This guard exists because a throwing HD enhancement inside a think node starved pawns to death ` +
				`while they read books (issue #122). If you intentionally restructured a boundary, update this ` +
				`script to match the new shape. Do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[need-seam-guards] PASS, ${SEAMS.length} think-node seams keep their degrade boundary (Postfix-scoped ` +
			`try + SeamDegraded, no rethrow), meals-on-wheels restores vanilla outputs, Core severity gates + ` +
			`oracle tests present.`
	)
}

function escapeRe(s: string): string {
	return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

await main()
