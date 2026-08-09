// Static guard on the ONE seam that stops a colonist walking to something the player just forbade (issue #250).
//
// The bug this pins: a player forbids an item because reaching it is UNSAFE — a stack beside a downed
// manhunter, in a room that just caught fire, past the breach the raiders came through. HD's sweep drivers
// tested forbidden when the chain PICKED a stack and again when the pawn ARRIVED, and ran no code at all in
// between: the walk toil's initAction called StartPath and the next thing that executed was the arrival. So
// forbidding mid-walk bought the player nothing — the colonist finished the trip (plus up to 240 more ticks of
// pickup pause standing at the item) before changing its mind. Vanilla has no such gap: its haul drivers hang a
// job-level FailOnForbidden end condition, and JobDriver.DriverTick re-evaluates end conditions through
// CheckCurrentToilEndOrFail before anything else it does, so vanilla reacts within one tick.
//
// The fix is a single shared walk toil (SweepWalk.MakeToil) carrying a per-tick forbidden re-check, plus one
// pure rule (HaulersDream.Core.SweepForbidPolicy) shared by all three checkpoints — decide, walk, take — so the
// walk gate and the take gate cannot drift apart. None of that is visible to the compiler or to a unit test: a
// hand-rolled `pather.StartPath` + PatherArrival toil compiles clean, passes every NUnit test (the tests
// reference only HaulersDream.Core, which cannot see a JobDriver at all), and silently re-opens #250 for that
// one driver. Eight drivers own such a walk, and this repo's recurring failure is fixing one and missing its
// siblings. Hence a static inventory.
//
// This script fails the build (exit 1) if:
//   1. Any file under Source/HaulersDream/ hand-rolls a sweep walk — pairs `pather.StartPath(` with
//      `defaultCompleteMode = ToilCompleteMode.PatherArrival` — outside SweepWalk.cs and the allowlist below;
//      or any driver that owns a sweep walk stops routing it through SweepWalk.MakeToil.
//   2. SweepWalk.MakeToil loses its AddPreTickAction, stops routing that action through
//      SweepForbidPolicy.AbandonWalk, stops completing on PatherArrival, or moves the check to tickAction /
//      tickIntervalAction (both of which are the WRONG hook — see the reasons inline below).
//   3. JobDriver_BulkHaul's decide and take checkpoints stop routing through SweepForbidPolicy, or grow a
//      hand-rolled `loadIndex == 0 && job.playerForced` carve-out again.
// It also checks that the Core policy and its NUnit fixture still exist.
//
// Run directly to self-check:  bun scripts/check-sweep-walk-guard.ts
import { basename, resolve } from 'node:path'
import { codeOnly, csFilesUnder, repoRoot } from './lib'

const GAME_SRC = resolve(repoRoot, 'Source/HaulersDream')
const SEAM_PATH = resolve(GAME_SRC, 'SweepWalk.cs')
const BULK_HAUL_PATH = resolve(GAME_SRC, 'JobDriver_BulkHaul.cs')
const POLICY_PATH = resolve(repoRoot, 'Source/HaulersDream.Core/SweepForbidPolicy.cs')
const TESTS_PATH = resolve(repoRoot, 'Source/HaulersDream.Tests/SweepForbidPolicyTests.cs')

/** The raw-pathing marker. Deliberately broader than `pawn.pather.StartPath(` so `driver.pawn.pather...` and
 *  `actor.pather...` spellings are caught too — a walk toil is a walk toil whichever receiver it names. */
const START_PATH = /\bpather\s*\.\s*StartPath\s*\(/
/** The "this toil finishes when the pawn gets there" marker, tolerant of formatting and of an object-initializer
 *  spelling (`defaultCompleteMode = ToilCompleteMode.PatherArrival,`). */
const ARRIVAL = /\bdefaultCompleteMode\s*=\s*ToilCompleteMode\s*\.\s*PatherArrival\b/

/**
 * Files allowed to pair both markers, each with the reason it is not a sweep walk.
 *
 * An entry that is never exercised is EXPECTED and is reported as such rather than failing: the three legacy
 * non-sweep walks below all path through `Toils_Goto.*` today, so they cannot trip the pattern. They are listed
 * anyway so that if one of them is ever hand-rolled into a raw walk, the author reads this list and makes a
 * deliberate decision instead of hitting an unexplained build break.
 */
const ALLOWED: { file: string; why: string }[] = [
	{
		file: 'JobDriver_InventoryDoBill.cs',
		why: 'retired driver — it ends Incompletable on its first tick and exists only so old saves can load its def. Its toils never run, so its walk needs no forbidden re-check.',
	},
	{
		file: 'JobDriver_UnloadCarrierInBulk.cs',
		why: 'not a sweep — it walks to ONE carrier and already carries a job-level FailOnForbidden, which is vanilla\'s own per-tick mechanism.',
	},
	{
		file: 'JobDriver_ClaimFromHauler.cs',
		why: 'not a sweep — it walks to one hauler and one needer, and already carries a job-level FailOnForbidden.',
	},
	{
		file: 'JobDriver_KeepInInventory.cs',
		why: 'deliberately forbidden-TOLERANT: keeping an item the player forbade is the whole point of the job, so a forbidden re-check would break it.',
	},
]

/**
 * Every driver that owns a sweep walk, and the cursor its abandon path must advance. All eight must route
 * through SweepWalk.MakeToil — this is the "did you fix the siblings?" half of check 1, and the half a
 * hand-rolled walk replaced by `Toils_Goto.GotoThing` (which trips neither marker) would otherwise slip past.
 */
const ROUTED_DRIVERS: { file: string; cursor: string }[] = [
	{ file: 'JobDriver_BulkHaul.cs', cursor: 'loadIndex++' },
	{ file: 'JobDriver_LoadInBulkBase.cs', cursor: 'loadIndex++ (covers transporter, portal and vehicle loads)' },
	{ file: 'JobDriver_LoadPackAnimal.cs', cursor: 'loadIndex++' },
	{ file: 'JobDriver_BulkRefuel.cs', cursor: 'loadIndex++' },
	{ file: 'JobDriver_SelfPickup.cs', cursor: 'none — TakeNextValidPending already popped the drop' },
	{ file: 'JobDriver_OverloadConstructDeliver.cs', cursor: 'none — NextResourceStack already popped the stack' },
	{ file: 'JobDriver_BillPrepGather.cs', cursor: 'loadIndex++' },
	{ file: 'JobDriver_BatchCraft.cs', cursor: 'none — FindNeededStack re-scans and filters forbidden' },
]

const errors: string[] = []
const notes: string[] = []

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

/** Slice a method body by brace-matching from its definition. Null if the method is not found. */
function sliceMethodBody(src: string, methodName: string): string | null {
	const sig = new RegExp(`\\b${methodName}\\s*\\([^)]*\\)[\\s\\S]{0,200}?\\{`).exec(src)
	if (!sig) return null
	return braceSlice(src, sig.index)
}

/** Read a file's code (comments + literal content blanked). Records an error and returns null if missing. */
async function readCode(path: string, label: string): Promise<string | null> {
	const file = Bun.file(path)
	if (!(await file.exists())) {
		errors.push(`${label} is MISSING (${path}). The #250 forbid-mid-walk guard cannot verify it.`)
		return null
	}
	return codeOnly((await file.text()).replace(/\r\n/g, '\n'))
}

async function main(): Promise<void> {
	// ---- 1a. Nobody hand-rolls a sweep walk outside the seam. ----
	const files = csFilesUnder(GAME_SRC)
	const exercised = new Set<string>()
	let paired = 0
	for (const path of files) {
		if (path === SEAM_PATH) continue
		const name = basename(path)
		const code = codeOnly((await Bun.file(path).text()).replace(/\r\n/g, '\n'))
		if (!START_PATH.test(code) || !ARRIVAL.test(code)) continue
		paired++
		const allowed = ALLOWED.find((a) => a.file === name)
		if (allowed) {
			exercised.add(name)
			continue
		}
		errors.push(
			`${name} hand-rolls a walk toil (pather.StartPath + ToilCompleteMode.PatherArrival) outside SweepWalk.cs. ` +
				`Between StartPath and the arrival that toil runs NO code, so a stack the player forbids mid-walk is ` +
				`still walked to in full — issue #250, and the player forbids things that are UNSAFE. Route the walk ` +
				`through SweepWalk.MakeToil, or (if this genuinely is not a sweep) add it to ALLOWED in this script ` +
				`with the reason.`
		)
	}
	for (const a of ALLOWED) {
		if (!exercised.has(a.file))
			notes.push(`allowlist entry ${a.file} not exercised (it paths via Toils_Goto.*, so it cannot trip the pattern)`)
	}

	// ---- 1b. Every driver that owns a sweep walk still routes it through the seam. ----
	//      A driver swapped to Toils_Goto.GotoThing would trip NEITHER marker above and lose its check silently.
	for (const driver of ROUTED_DRIVERS) {
		const code = await readCode(resolve(GAME_SRC, driver.file), driver.file)
		if (code === null) continue
		if (!/\bSweepWalk\s*\.\s*MakeToil\s*\(/.test(code)) {
			errors.push(
				`${driver.file} no longer calls SweepWalk.MakeToil. Its sweep walk has lost the per-tick forbidden ` +
					`re-check (#250) — a colonist will again finish the whole trip to an item the player forbade for ` +
					`safety. Its abandon path must advance: ${driver.cursor}.`
			)
		}
	}

	// ---- 2. The seam itself keeps the mechanism that makes the check fire. ----
	const seam = await readCode(SEAM_PATH, 'SweepWalk.cs')
	if (seam) {
		const body = sliceMethodBody(seam, 'MakeToil')
		if (body === null) {
			errors.push('SweepWalk.cs no longer defines MakeToil; every routed driver above breaks.')
		} else {
			const preTick = body.search(/\bAddPreTickAction\s*\(/)
			const abandon = body.search(/\bSweepForbidPolicy\s*\.\s*AbandonWalk\s*\(/)
			if (preTick < 0) {
				errors.push(
					'SweepWalk.MakeToil no longer calls AddPreTickAction. That call IS the fix: without it the toil ' +
						'runs nothing between StartPath and the arrival and #250 is back, silently, for all eight drivers.'
				)
			}
			if (abandon < 0) {
				errors.push(
					'SweepWalk.MakeToil no longer routes through SweepForbidPolicy.AbandonWalk. The walk gate and the ' +
						'take gate must be the SAME arithmetic, or they drift — and the direction they drift in is a pawn ' +
						'that keeps walking somewhere the player told it not to go.'
				)
			}
			if (preTick >= 0 && abandon >= 0 && abandon < preTick) {
				errors.push(
					'SweepWalk.MakeToil calls SweepForbidPolicy.AbandonWalk BEFORE its AddPreTickAction, so the check is ' +
						'evaluated once at build time instead of every tick. It must live INSIDE the pre-tick action.'
				)
			}
			if (!ARRIVAL.test(body)) {
				errors.push(
					'SweepWalk.MakeToil no longer completes on ToilCompleteMode.PatherArrival. Pre-tick actions do not ' +
						'run at all on an Instant toil (DriverTick returns before the pre-tick loop), so the check would ' +
						'never fire.'
				)
			}
			if (/\btickAction\b|\btickIntervalAction\b/.test(body)) {
				errors.push(
					'SweepWalk.MakeToil uses tickAction / tickIntervalAction. Both are the WRONG hook (decompiled 1.6 ' +
						'JobDriver): after tickAction the driver re-tests only JobChanged(), which stays FALSE for a ' +
						'JumpToToil inside the same job, so the rest of the tick runs against a toil that is no longer ' +
						'current; tickIntervalAction is re-tested not at all and fires only at the pawn\'s throttled ' +
						'update rate. Only preTickActions are re-tested for JobChanged() || CurToil != curToil || ' +
						'wantBeginNextToil after EACH action.'
				)
			}
		}
	}

	// ---- 3. BulkHaul's other two checkpoints share the seam's rule. ----
	const bulk = await readCode(BULK_HAUL_PATH, 'JobDriver_BulkHaul.cs')
	if (bulk) {
		const routed = bulk.match(/\bSweepForbidPolicy\s*\.\s*MayTakeWhileForbidden\s*\(/g)?.length ?? 0
		if (routed < 2) {
			errors.push(
				`JobDriver_BulkHaul routes only ${routed} of its 2 forbidden checkpoints (loadDecide and take) through ` +
					`SweepForbidPolicy.MayTakeWhileForbidden. All three checkpoints — decide, walk, take — must read one ` +
					`rule, or the walk abandons a stack the take would have accepted (or worse, the reverse).`
			)
		}
		if (/loadIndex\s*==\s*0\s*&&\s*job\.playerForced|job\.playerForced\s*&&\s*loadIndex\s*==\s*0/.test(bulk)) {
			errors.push(
				'JobDriver_BulkHaul has a hand-rolled `loadIndex == 0 && job.playerForced` forbidden carve-out again. ' +
					'That is exactly the condition SweepForbidPolicy.MayTakeWhileForbidden exists to single-source: ' +
					'vanilla exempts a forced order (Pawn_JobTracker.StartJob sets ignoreForbidden, FailOnForbidden ' +
					'short-circuits on it), but the exemption covers the ORDERED ANCHOR only — never a stack HD swept ' +
					'into the same trip. Call the policy.'
			)
		}
	}

	// ---- The Core rule and its oracle tests exist. ----
	const policy = await readCode(POLICY_PATH, 'SweepForbidPolicy.cs')
	if (policy) {
		for (const member of ['MayTakeWhileForbidden', 'AbandonWalk']) {
			if (!policy.includes(member))
				errors.push(`SweepForbidPolicy.cs no longer declares ${member}; its call sites cannot compile or have drifted.`)
		}
	}
	await readCode(TESTS_PATH, 'SweepForbidPolicyTests.cs') // existence is the assertion

	if (errors.length > 0) {
		console.error(`\n[sweep-walk-guard] FAIL, ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  x ${e}`)
		console.error(
			`\n  This guard exists because a colonist that keeps walking to an item the player just forbade can get ` +
				`hurt (issue #250), and nothing else can see the regression: a hand-rolled walk compiles clean and the ` +
				`NUnit suite cannot reach a JobDriver at all. If you intentionally restructured the seam, update this ` +
				`script to match the new shape. Do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[sweep-walk-guard] PASS, ${files.length} files scanned, ${paired} allowlisted walk(s) paired, ` +
			`${ROUTED_DRIVERS.length} sweep drivers route through SweepWalk.MakeToil (pre-tick + ` +
			`SweepForbidPolicy.AbandonWalk), BulkHaul's decide + take share the rule, Core policy + oracle tests present.`
	)
	for (const n of notes) console.log(`  - ${n}`)
}

await main()
