// Static guard against silently REGRESSING the protection that keeps pawns from dropping the work-yields
// Hauler's Dream scoops into their inventory.
//
// This is the single most-reported, most-recurring bug in the mod (issues #62, #81, #87): every recurrence has
// been vanilla JobGiver_DropUnusedInventory dropping HD-tagged cargo because one layer of the guard silently
// stopped doing its job — typically a per-drop check that read the UN-healed tag set and so missed a merged
// stack, or a guard that was refactored away. The bug is invisible to the compiler and only shows up when a
// player on an established save reports dropped crops.
//
// So we pin the WHOLE defence statically. The fix has three runtime layers + a Core policy + a startup
// tripwire, and this script fails the build (exit 1) if any of them is weakened:
//   1. Patch_JobGiver_DropUnusedInventory.cs guards all three vanilla seams, and the two per-drop guards read
//      the HEALED set (GetHashSet, never the un-healed PeekHashSet — that was the recurrence).
//   2. The Layer-1 prefix re-arms the food clock (lastInventoryRawFoodUseTick) via the Core policy.
//   3. DropUnusedFoodPolicy.cs pins vanilla's exact loop (RawFoodDropDelay = 150000, IsRawFoodDropCandidate,
//      FoodLoopWouldRun) — the same members the oracle tests assert.
//   4. HaulersDreamMod.VerifyDropProtection is present, invoked at startup, and lists the same three seams.
//   5. The oracle test file exists.
//
// Run directly to self-check:  bun scripts/check-drop-protection.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const PATCH_PATH = resolve(repoRoot, 'Source/HaulersDream/Patch_JobGiver_DropUnusedInventory.cs')
const POLICY_PATH = resolve(repoRoot, 'Source/HaulersDream.Core/DropUnusedFoodPolicy.cs')
const MOD_PATH = resolve(repoRoot, 'Source/HaulersDream/HaulersDreamMod.cs')
const TESTS_PATH = resolve(repoRoot, 'Source/HaulersDream.Tests/DropUnusedFoodPolicyTests.cs')

// The three vanilla seams HD must guard, expressed the way each source references them. A regression that drops
// one of these (or renames it out of sync) trips the cross-file agreement check below — the same triple-source
// discipline the settings-drift guard uses.
const SEAMS = [
	{ name: 'TryGiveJob', attr: '"TryGiveJob"' },
	{ name: 'Drop', attr: '"Drop"' },
	{ name: 'ShouldKeepDrugInInventory', attr: 'nameof(JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory)' },
]

// The bulk-LOAD deposit GATES (HasDepositable*) decide whether the running load job has anything to put onto the
// transporter/portal/vehicle/pack-animal. They MUST read the same HEALED view (GetHashSet) the deposit driver
// reads — else a scooped stack that merged into a same-def inventory stack after tagging is invisible to the gate,
// the load ends early, and the merge-survivor cargo silently never loads (the #62/#87 stale-view class on the load
// side). This is the generalization of the drop-guard rule to the load gates: DECISIONS read the healed view.
// NOTE: PackAnimalLoad.HasDepositableSurplus is deliberately EXCLUDED — it is a multi-context probe (one caller
// is the Alert on the OnGUI render path, where GetHashSet's mutating self-heal would MP-desync), and the actual
// pack-animal deposit (JobDriver_LoadPackAnimal) already reads the healed view directly, so it has no gate/driver
// split. Only the three bulk-LOAD driver gates below ARE the deposit decision and must read the healed view.
const LOAD_GATES = [
	{ file: 'Source/HaulersDream/JobDriver_LoadTransportersInBulk.cs', method: 'HasDepositableForGroup' },
	{ file: 'Source/HaulersDream/JobDriver_LoadPortalInBulk.cs', method: 'HasDepositableForPortal' },
	{ file: 'Source/HaulersDream/JobDriver_LoadVehicleInBulk.cs', method: 'HasDepositableForVehicle' },
]

const errors: string[] = []

/** Slice a C# method body by brace-matching from its DEFINITION (signature ending in `{`, not a `=> call();`
 *  expression-body or a call site). Null if not found. */
function sliceMethodBody(src: string, methodName: string): string | null {
	// A definition is `<name>(<params>) {` — the matching `)` is followed by `{`. A call is `<name>();` and an
	// expression-bodied delegate is `<name>() => ...;` — both end in `;`, so requiring `{` after `)` skips them.
	const sig = new RegExp(`\\b${escapeRe(methodName)}\\s*\\([^)]*\\)\\s*\\{`).exec(src)
	if (!sig) return null
	let i = src.indexOf('{', sig.index)
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

async function read(path: string, label: string): Promise<string | null> {
	const f = Bun.file(path)
	if (!(await f.exists())) {
		errors.push(`${label} is MISSING (${path}). The drop-protection guard cannot verify it.`)
		return null
	}
	return (await f.text()).replace(/\r\n/g, '\n')
}

function count(haystack: string, needle: string): number {
	return haystack.split(needle).length - 1
}

async function main() {
	const patch = await read(PATCH_PATH, 'Patch_JobGiver_DropUnusedInventory.cs')
	const policy = await read(POLICY_PATH, 'DropUnusedFoodPolicy.cs')
	const mod = await read(MOD_PATH, 'HaulersDreamMod.cs')
	await read(TESTS_PATH, 'DropUnusedFoodPolicyTests.cs') // existence is the assertion

	// 1. Each vanilla seam is guarded by a [HarmonyPatch(typeof(JobGiver_DropUnusedInventory), <seam>)] in the
	//    patch file.
	if (patch) {
		for (const seam of SEAMS) {
			const re = new RegExp(
				`\\[HarmonyPatch\\(\\s*typeof\\(JobGiver_DropUnusedInventory\\)\\s*,\\s*${escapeRe(seam.attr)}\\s*\\)\\]`
			)
			if (!re.test(patch)) {
				errors.push(
					`Patch file no longer guards JobGiver_DropUnusedInventory.${seam.name} — expected a ` +
						`[HarmonyPatch(typeof(JobGiver_DropUnusedInventory), ${seam.attr})] attribute. ` +
						`Removing it re-opens the "${seam.name}" drop path that dumps scooped cargo.`
				)
			}
		}

		// 2. THE recurrence guard: the per-drop guards must read the HEALED set. PeekHashSet (the un-healed view)
		//    is what let a merged tagged stack slip through and drop. It must not appear in this file at all, and
		//    GetHashSet must back all three guards.
		if (patch.includes('PeekHashSet')) {
			errors.push(
				`Patch file uses PeekHashSet — the UN-healed tag view. A scooped stack that merges into another ` +
					`same-def stack loses its tag in that view and gets dropped anyway (the exact #87 recurrence). ` +
					`Use GetHashSet() (healed) in every drop guard.`
			)
		}
		const getHashSetUses = count(patch, 'GetHashSet()')
		if (getHashSetUses < 3) {
			errors.push(
				`Patch file calls GetHashSet() only ${getHashSetUses} time(s); expected at least 3 (the ` +
					`TryGiveJob, Drop, and ShouldKeepDrug guards each read the healed set). A guard that stopped ` +
					`consulting the tag set no longer protects cargo.`
			)
		}

		// 3. Layer 1 re-arms the food clock via the unit-pinned policy predicate.
		if (!patch.includes('lastInventoryRawFoodUseTick')) {
			errors.push(
				`Patch file no longer touches lastInventoryRawFoodUseTick — the Layer-1 prefix that suppresses ` +
					`vanilla's raw-food loop by re-arming its clock is gone.`
			)
		}
		if (!patch.includes('DropUnusedFoodPolicy.IsRawFoodDropCandidate')) {
			errors.push(
				`Patch file no longer routes the raw-food decision through ` +
					`DropUnusedFoodPolicy.IsRawFoodDropCandidate — the dropped-category contract is then untested ` +
					`and free to drift from vanilla.`
			)
		}
	}

	// 4. The Core policy pins vanilla's exact loop. These members are what the oracle tests assert against.
	if (policy) {
		const required = [
			{ token: 'RawFoodDropDelay = 150000', why: 'vanilla raw-food drop delay constant' },
			{ token: 'IsRawFoodDropCandidate', why: 'the per-item dropped-category predicate' },
			{ token: 'FoodLoopWouldRun', why: 'the clock-gate predicate' },
		]
		for (const r of required) {
			if (!policy.includes(r.token)) {
				errors.push(`DropUnusedFoodPolicy.cs is missing "${r.token}" (${r.why}).`)
			}
		}
	}

	// 5. The startup tripwire exists, is invoked, and lists the same three seams (cross-file agreement, so a
	//    seam can never be guarded-but-unverified or verified-but-unguarded).
	if (mod) {
		if (!/private static void VerifyDropProtection\(/.test(mod)) {
			errors.push(`HaulersDreamMod.cs no longer defines VerifyDropProtection — the runtime startup tripwire is gone.`)
		}
		if (!/VerifyDropProtection\(harmony\)\s*;/.test(mod)) {
			errors.push(`HaulersDreamMod.cs no longer CALLS VerifyDropProtection(harmony) at startup — the tripwire never runs.`)
		}
		for (const seam of SEAMS) {
			// The tripwire's target table references each seam by its string method name.
			if (!mod.includes(`"${seam.name}"`)) {
				errors.push(
					`HaulersDreamMod.cs DropProtectionTargets does not list "${seam.name}" — the startup tripwire ` +
						`won't notice if that guard stops binding.`
				)
			}
		}
	}

	// 6. The bulk-LOAD deposit gates must read the HEALED view (GetHashSet), not the un-healed PeekHashSet — the
	//    same stale-view class as the drop guards, on the load side (merge-survivor cargo silently never loaded).
	for (const gate of LOAD_GATES) {
		const src = await read(resolve(repoRoot, gate.file), gate.file)
		if (!src) continue
		const body = sliceMethodBody(src, gate.method)
		if (body === null) {
			errors.push(`${gate.file}: deposit gate ${gate.method}() not found — the load-gate healed-view check can't verify it.`)
			continue
		}
		if (body.includes('PeekHashSet')) {
			errors.push(
				`${gate.file}: ${gate.method}() reads PeekHashSet (the UN-healed view). A scooped stack that merged ` +
					`after tagging is then invisible to the load gate, so the merge-survivor cargo silently never loads ` +
					`onto the transporter/portal/vehicle/animal (the #62/#87 stale-view class on the load side). ` +
					`Use GetHashSet().`
			)
		}
		if (!body.includes('GetHashSet')) {
			errors.push(`${gate.file}: ${gate.method}() no longer reads the tag set via GetHashSet — the deposit gate must consult the healed owned set.`)
		}
	}

	if (errors.length > 0) {
		console.error(`\n[drop-protection] FAIL — ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		console.error(
			`\n  This guard exists because pawns dropping HD-scooped cargo (issues #62/#81/#87) has regressed ` +
				`repeatedly. If you intentionally restructured the protection, update this script to match the new ` +
				`shape — do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[drop-protection] PASS — 3 vanilla drop seams + ${LOAD_GATES.length} bulk-load gates read the healed tag ` +
			`set, Core policy + startup tripwire + oracle tests all present.`
	)
}

function escapeRe(s: string): string {
	return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

await main()
