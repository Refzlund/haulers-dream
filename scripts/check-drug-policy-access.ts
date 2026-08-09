// Static guard keeping every drug-policy read behind the one accessor that cannot throw (issue #232).
//
// RimWorld's `DrugPolicy` has TWO indexers. The integer one is a plain list read. The per-`ThingDef` one walks
// the entry list and, on no match, ends in a bare `throw new ArgumentException();` — with no message, so .NET
// supplies "Value does not fall within the expected range." and the player gets an error that names nothing.
// Every vanilla caller assumes an entry always exists (`DrugPolicy.InitializeIfNeeded` builds one per drug def),
// and issue #232 is a report where one did not, on a modded alcohol: `Pawn_DrugPolicyTracker
// .AllowedToTakeScheduledEver` -> `CurrentPolicy[thingDef].allowScheduled` threw out of
// `JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory` while a float menu was being built, killing the
// "Pick up" options. That throw is vanilla's and reaches it before any HD code — but HD had its OWN copy of the
// same unguarded lookup in the #229 withdrawal-access scan, on a def taken from an arbitrary colonist's
// inventory, where it degraded loudly and silently killed that feature for the pawn.
//
// The fix is a single accessor (`DrugPolicyLookup`) that reads only the integer indexer and answers "no entry"
// instead of throwing, plus a pure `DrugAllowancePolicy` that says what a missing entry MEANS. Neither is
// self-enforcing: `policy[def]` is shorter to type than `DrugPolicyLookup.EntryFor(policy, def)`, compiles
// clean, passes every test, and only fails on a save whose policy is missing an entry — i.e. never on the
// developer's machine. So it is pinned here. This script fails the build (exit 1) if:
//   1. any banned drug-policy read appears in real CODE under Source/HaulersDream/ outside the accessor
//      (comments and string literals are stripped first — this file's own header and the fix's doc comments
//      name every banned symbol at length, which is intended);
//   2. any part of the fix has gone missing (the accessor and both of its members, the two call sites routed
//      through it, the Core constant, the no-entry routing gate, its tests, or the containment finalizer at the
//      vanilla seam).
//
// KNOWN GAP (deliberate, same shape as check-no-desperate-leg.ts's): the ban is on identifiers that NAME a drug
// policy, not on indexing by a `ThingDef`. A blanket `[<expr>.def]` ban was considered and rejected — HD has
// roughly fifteen legitimate `Dictionary<ThingDef, …>` reads (`keptCounts[def]`, `counts[t.def]`, …) that it
// would flag, and a guard that cries wolf gets deleted. A future `var p = pawn.drugs.CurrentPolicy;` bound to a
// name with no "drug policy" in it and then indexed by def would slip through. The check is cheap insurance
// against the obvious regression, not a sandbox.
//
// Run directly to self-check:  bun scripts/check-drug-policy-access.ts
import { resolve } from 'node:path'
import { codeOnly, csFilesUnder, repoRoot } from './lib'

const GAME_SRC = resolve(repoRoot, 'Source/HaulersDream')
const LOOKUP_PATH = resolve(repoRoot, 'Source/HaulersDream/DrugPolicyLookup.cs')
const SURPLUS_PATH = resolve(repoRoot, 'Source/HaulersDream/InventorySurplus.cs')
const CHEMICAL_NEED_PATH = resolve(repoRoot, 'Source/HaulersDream/Patch_JobGiver_SatisfyChemicalNeed.cs')
const DROP_UNUSED_PATH = resolve(repoRoot, 'Source/HaulersDream/Patch_JobGiver_DropUnusedInventory.cs')
const ALLOWANCE_POLICY_PATH = resolve(repoRoot, 'Source/HaulersDream.Core/DrugAllowancePolicy.cs')
const ALLOWANCE_TESTS_PATH = resolve(repoRoot, 'Source/HaulersDream.Tests/DrugAllowancePolicyTests.cs')

/** The one file allowed to read a DrugPolicy directly — it is the audited accessor. */
const ACCESSOR_FILE = 'DrugPolicyLookup.cs'

/**
 * The reads that can throw `ArgumentException` for a def the policy has no entry for, each with the reason and
 * the alternative, so a future reader hitting this failure knows what to do instead of deleting the check.
 */
const BANNED = [
	{
		pattern: /\bCurrentPolicy\s*\[/,
		label: 'CurrentPolicy[…]',
		why: "indexing a DrugPolicy directly reaches its per-ThingDef indexer, whose no-match path is a bare `throw new ArgumentException();` (#232). Use DrugPolicyLookup.EntryFor / TakeToInventoryTotal, which read the integer indexer and answer null / 0."
	},
	{
		pattern: /\b\w*[Dd]rug[Pp]olicy\w*\s*\[/,
		label: '<drugPolicy>[…]',
		why: "same throw as above, reached through a local or field named after the policy. Use DrugPolicyLookup.EntryFor / TakeToInventoryTotal."
	},
	{
		pattern: /\bAllowedToTakeScheduledEver\s*\(/,
		label: 'AllowedToTakeScheduledEver(…)',
		why: 'Pawn_DrugPolicyTracker.AllowedToTakeScheduledEver does `CurrentPolicy[thingDef].allowScheduled` unguarded — this is the exact call that threw in #232. It also dereferences CurrentPolicy with no null check, which NREs for a Biotech mutant with disablePolicies. Read the entry via DrugPolicyLookup and decide from its fields.'
	},
	{
		pattern: /\bAllowedToTakeScheduledNow\s*\(/,
		label: 'AllowedToTakeScheduledNow(…)',
		why: 'calls AllowedToTakeScheduledEver, so it carries the same throw. Read the entry via DrugPolicyLookup.'
	},
	{
		pattern: /\bAllowedToTakeToInventory\s*\(/,
		label: 'AllowedToTakeToInventory(…)',
		why: 'another Pawn_DrugPolicyTracker read that indexes CurrentPolicy by ThingDef. Read the entry via DrugPolicyLookup.'
	},
	{
		pattern: /\bShouldTryToTakeScheduledNow\s*\(/,
		label: 'ShouldTryToTakeScheduledNow(…)',
		why: 'the fourth and last Pawn_DrugPolicyTracker site reaching the ThingDef indexer (`CurrentPolicy[ingestible]`), so it carries the same throw. Read the entry via DrugPolicyLookup.'
	},
	{
		pattern: /\bIngestAndTakeToInventoryJob\s*\(/,
		label: 'IngestAndTakeToInventoryJob(…)',
		why: "RimWorld's DrugAIUtility.IngestAndTakeToInventoryJob does `drugPolicy[drug.def]` unguarded. It is the vanilla job builder Patch_JobGiver_SatisfyChemicalNeed's header cites as \"vanilla's own construction\", so a future \"just call vanilla's builder\" simplification would reintroduce #232 through the front door. Keep building the job explicitly (JobMaker.MakeJob) and read the policy via DrugPolicyLookup."
	},
	{
		pattern: /\bShouldKeepDrugInInventory\s*\(/,
		label: 'ShouldKeepDrugInInventory(…)',
		why: "JobGiver_DropUnusedInventory.ShouldKeepDrugInInventory reaches AllowedToTakeScheduledEver in its second clause, so CALLING it can throw (#232). HD patches this method (that is sanctioned, and the [HarmonyPatch(..., nameof(...))] attribute is not a call); it must not invoke it. Decide from DrugPolicyLookup + the pawn's own state instead."
	}
]

/**
 * The body of a method, brace-matched from its DEFINITION (a signature whose parameter list is followed by `{`,
 * not a call site). Null when the method is not defined in `src`.
 */
function sliceMethodBody(src: string, methodName: string): string | null {
	const re = new RegExp(`\\b${methodName}\\s*\\([^)]*\\)\\s*\\{`)
	const m = re.exec(src)
	if (!m) return null
	let depth = 0
	const start = m.index + m[0].length - 1
	for (let i = start; i < src.length; i++) {
		if (src[i] === '{') depth++
		else if (src[i] === '}') {
			depth--
			if (depth === 0) return src.slice(start + 1, i)
		}
	}
	return null
}

/** Read a file, recording a problem instead of throwing when it is gone (its absence IS a finding). */
async function read(path: string, label: string, errors: string[]): Promise<string | null> {
	const file = Bun.file(path)
	if (!(await file.exists())) {
		errors.push(`${label} is missing (${path.slice(repoRoot.length + 1).replace(/\\/g, '/')}).`)
		return null
	}
	return await file.text()
}

async function main(): Promise<void> {
	const errors: string[] = []
	let scanned = 0

	// 1. The bans, over real code only, everywhere but the accessor itself.
	for (const file of csFilesUnder(GAME_SRC)) {
		if (file.endsWith(ACCESSOR_FILE)) continue
		const lines = codeOnly(await Bun.file(file).text()).split('\n')
		scanned++
		for (const { pattern, label, why } of BANNED) {
			for (let n = 0; n < lines.length; n++) {
				if (!pattern.test(lines[n])) continue
				const rel = file.slice(repoRoot.length + 1).replace(/\\/g, '/')
				errors.push(`${rel}:${n + 1} reads a drug policy via "${label}" — ${why}`)
			}
		}
	}

	// 2. Positive pins: deleting the fix wholesale must not pass a ban-only check.
	const lookup = await read(LOOKUP_PATH, 'The DrugPolicyLookup accessor', errors)
	if (lookup) {
		const code = codeOnly(lookup)
		for (const member of ['EntryFor', 'TakeToInventoryTotal']) {
			if (sliceMethodBody(code, member) === null)
				errors.push(
					`DrugPolicyLookup.cs no longer defines ${member} — the two reads are NOT interchangeable ` +
						`(EntryFor takes the FIRST match, matching DrugPolicy's own ThingDef indexer, while ` +
						`TakeToInventoryTotal SUMS duplicate entries, matching what the keep count has always ` +
						`counted). Collapsing them silently changes one of the two behaviours.`
				)
		}
	}

	const surplus = await read(SURPLUS_PATH, 'InventorySurplus.cs', errors)
	if (surplus && !codeOnly(surplus).includes('DrugPolicyLookup.TakeToInventoryTotal('))
		errors.push(
			'InventorySurplus.cs no longer totals the drug-policy keep count through ' +
				'DrugPolicyLookup.TakeToInventoryTotal — the keep count must stay a SUM over every matching entry ' +
				'(a policy can carry duplicates via DrugPolicy.CopyFrom), and it must not reach the throwing indexer.'
		)

	const chemicalNeed = await read(CHEMICAL_NEED_PATH, 'Patch_JobGiver_SatisfyChemicalNeed.cs', errors)
	if (chemicalNeed) {
		const code = codeOnly(chemicalNeed)
		if (!code.includes('DrugPolicyLookup.EntryFor('))
			errors.push(
				'Patch_JobGiver_SatisfyChemicalNeed.cs no longer reads the policy entry through ' +
					'DrugPolicyLookup.EntryFor — the #229 withdrawal scan runs on defs taken from an arbitrary ' +
					'colonist\'s inventory, so the unguarded `drugPolicy[drug.def]` is exactly the #232 throw.'
			)
		if (!code.includes('DrugAllowancePolicy.BlocksAddictionUse('))
			errors.push(
				'Patch_JobGiver_SatisfyChemicalNeed.cs no longer decides through ' +
					'DrugAllowancePolicy.BlocksAddictionUse — the missing-entry answer is then untested and free ' +
					'to drift from the value DrugPolicy.InitializeIfNeeded would have created.'
			)
		if (!code.includes('DrugAllowancePolicy.MayRouteToDrug('))
			errors.push(
				'Patch_JobGiver_SatisfyChemicalNeed.cs no longer narrows the verdict through ' +
					'DrugAllowancePolicy.MayRouteToDrug — the no-entry gate is gone. "The policy permits it" is ' +
					'NOT "vanilla can finish the job": the take moves a dose into the seeker\'s own inventory, ' +
					'where vanilla\'s own DrugValidator re-checks it next think through the unguarded ' +
					'`drugPolicy[drug.def]`. Without the gate the addict is handed a dose it can never ingest and ' +
					'never shed, and loses its whole drug-satisfaction think node every scan.'
			)
	}

	const allowance = await read(ALLOWANCE_POLICY_PATH, 'DrugAllowancePolicy.cs', errors)
	if (allowance && !/MissingEntryAllowedForAddiction\s*=\s*true/.test(codeOnly(allowance)))
		errors.push(
			'DrugAllowancePolicy.cs no longer declares MissingEntryAllowedForAddiction = true. A def the policy ' +
				'has no entry for has no row in the drug-policy dialog, so the player cannot have set it to "not ' +
				'allowed"; answering false would silently arm the rehab lever for a drug nobody marked.'
		)

	await read(ALLOWANCE_TESTS_PATH, 'DrugAllowancePolicyTests.cs (the missing-entry pin)', errors)

	const dropUnused = await read(DROP_UNUSED_PATH, 'Patch_JobGiver_DropUnusedInventory.cs', errors)
	if (dropUnused) {
		const finalizer = sliceMethodBody(codeOnly(dropUnused), 'Finalizer')
		if (finalizer === null) {
			errors.push(
				'Patch_JobGiver_DropUnusedInventory.cs has no Finalizer — the vanilla seam is uncontained again. ' +
					'A throw out of ShouldKeepDrugInInventory leaves __result at default(bool) = false, which is ' +
					'the DESTRUCTIVE answer: the colonist dumps that drug at their feet on every think pass.'
			)
		} else {
			if (!/__result\s*=\s*true/.test(finalizer))
				errors.push(
					'The Finalizer in Patch_JobGiver_DropUnusedInventory.cs no longer forces __result = true. ' +
						'false is what a thrown call already yields and it means "drop it"; true is vanilla\'s own ' +
						'fall-through and is the only stable answer across both callers (the drop loop and the ' +
						'float-menu "Pick up" build).'
				)
			if (!finalizer.includes('HDGuard.SeamContained'))
				errors.push(
					'The Finalizer in Patch_JobGiver_DropUnusedInventory.cs no longer reports through ' +
						'HDGuard.SeamContained — a contained fault must still be logged once, with attribution, or ' +
						'it becomes a silent swallow.'
				)
		}
	}

	if (errors.length > 0) {
		console.error(`\n[drug-policy-access] FAIL — ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		console.error(
			`\n  This guard exists because RimWorld's DrugPolicy[ThingDef] indexer throws a message-less ` +
				`ArgumentException for a def it holds no entry for (#232), which no test and no ordinary save ` +
				`will ever reproduce. If you intentionally restructured the drug-policy access, update this ` +
				`script to match the new shape — do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[drug-policy-access] PASS — ${scanned} source files free of ${BANNED.length} throwing drug-policy ` +
			`reads, accessor + both call sites + missing-entry constant + no-entry routing gate + tests + ` +
			`seam finalizer intact.`
	)
}

await main()
