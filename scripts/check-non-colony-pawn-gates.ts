// Static guard on the invariant "a pawn the colony does not own must not reach this" — at the two HD seams where
// a foreign pawn is the SUBJECT (the bulk carrier unload, where it is the victim) and where it could be the
// ACTOR (the bulk loaders).
//
// THE BUG THIS PINS. HD offered "Prioritize bulk unloading …" on any pawn whose HostFaction was the player,
// because that line was vanilla's own job-time predicate copied verbatim. Vanilla can afford it for one reason
// that does not survive the copy: it only ever asks that question about a carrier whose
// Pawn_InventoryTracker.UnloadEverything flag game flow ALREADY raised, and no player-facing vanilla action can
// raise it. Reused as an OFFER predicate the flag gate is gone and the host-faction arm alone admits every pawn
// the colony merely HOSTS — a Hospitality visitor, a rescued wanderer, a downed Bestower still carrying the
// psylink neuroformer vanilla deliberately protects. Worse than the menu: the job's first act was to raise that
// scribed flag itself, which opens vanilla's OWN faction-blind WorkGiver_UnloadCarriers on the victim for every
// hauler on the map until its pack is empty. So the hole is wider than the entry point it was reported through,
// and a per-site condition is exactly how it got there.
//
// WHY A BUILD GUARD AND NOT A TEST. The rule is a single boolean expression in HaulersDream.Core and it is fully
// unit-tested. Everything that decides whether it is CONSULTED lives in Verse, which HaulersDream.Tests cannot
// see at all (it references only HaulersDream.Core — no Pawn, no JobDriver, no FloatMenuOptionProvider). An
// entry point that quietly stops calling the seam compiles clean and leaves the whole NUnit suite green.
//
// This script fails the build (exit 1) if:
//   1. The Core rule (BulkUnloadPermissionPolicy.MayBulkUnload) or its NUnit fixture goes missing.
//   2. BulkUnloadGate.PlayerMayUnload — the ONE place live pawns are read for this decision — stops routing
//      through that Core rule.
//   3. Any of the three entry points stops consulting the seam: the float-menu offer, the work-giver takeover
//      (BulkUnloadGate.ShouldHandle), or the driver.
//   4. The driver's UnloadEverything write stops being GATED (the permission call must precede it), or the
//      driver stops turning the same answer into a job-level FailOn — withholding the flag alone still lets the
//      transfer loop empty a carrier the job should never have targeted.
//   5. Any other file in the mod assigns UnloadEverything. There is exactly one legitimate writer.
//   6. Pawn.HostFaction is read anywhere outside the permission seam — that read IS the bug, and it looks
//      perfectly reasonable at every call site that wants it.
//   7. TransportLoad's HasJob/JobOn pair falls out of lockstep on its explicit "player faction, not a quest
//      lodger" refusal — the hardening that stops a guest entering HD's bulk loaders whatever any other mod
//      does with its Lords.
//
// PROSE IS NOT CODE. Every file this guard pins explains the banned spellings at length in its own comments
// (that is the point of them), so all scanning runs over `codeOnly` output, with comment and string bodies
// blanked. The prose-mention count is printed on success as a live control: it is non-zero, which is the proof
// that a raw-text version of this guard would fail on the very explanations it exists to protect.
//
// Run directly to self-check:  bun scripts/check-non-colony-pawn-gates.ts
import { basename, resolve } from 'node:path'
import { codeOnly, csFilesUnder, repoRoot } from './lib'

const GAME_SRC = resolve(repoRoot, 'Source/HaulersDream')
const SEAM_PATH = resolve(GAME_SRC, 'Patch_WorkGiver_UnloadCarriers.cs')
const MENU_PATH = resolve(GAME_SRC, 'FloatMenuOptionProvider_BulkUnloadCarrier.cs')
const DRIVER_PATH = resolve(GAME_SRC, 'JobDriver_UnloadCarrierInBulk.cs')
const LOADERS_PATH = resolve(GAME_SRC, 'TransportLoad.cs')
const POLICY_PATH = resolve(repoRoot, 'Source/HaulersDream.Core/BulkUnloadPermissionPolicy.cs')
const TESTS_PATH = resolve(repoRoot, 'Source/HaulersDream.Tests/BulkUnloadPermissionPolicyTests.cs')

/** The permission call every entry point must make, whatever receiver spelling it uses. */
const PERMISSION_CALL = /\bPlayerMayUnload\s*\(/
/**
 * The permission call used as a GUARD — negated, inside an `if` — rather than merely evaluated.
 *
 * → GOTCHA: a presence check is not enough at the flag write. `PlayerMayUnload(pawn, carrier);` on its own line,
 *   followed by an unconditional write, satisfies "the call precedes the write" and re-opens the wider half of the
 *   bug: the flag is SCRIBED, so vanilla's faction-blind haulers finish the job even though HD's own driver dies
 *   on its fail condition. A QA mutant in exactly that shape passed the presence-only version of this rule.
 */
const PERMISSION_GUARD = /if\s*\(\s*!\s*(?:[\w.]*\.)?PlayerMayUnload\s*\(/
/** An ASSIGNMENT to vanilla's unload flag. `(?!=)` so a comparison (`== true`) is not mistaken for a write. */
const FLAG_WRITE = /\bUnloadEverything\s*=(?!=)/
/** Any read of the host-faction property — the copied-predicate tell. */
const HOST_FACTION = /\bHostFaction\b/
/** The sibling faction refusal the loaders must carry, in the spelling every other HD entry point uses. */
const PLAYER_FACTION_REFUSAL = /Faction\s*!=\s*Faction\s*\.\s*OfPlayerSilentFail/
/** The quest-pawn half of that same refusal. */
const QUEST_LODGER_REFUSAL = /\bIsQuestLodger\s*\(\s*\)/

/**
 * The entry points that can empty a carrier, and the method in each that must consult the permission seam.
 *
 * Three ENTRY POINTS, four call sites: the driver is listed twice because withholding the flag and failing the
 * job are two different protections and dropping either one leaves a real hole (see checks 4 and 5).
 */
const GATED_METHODS: { file: string; method: string; why: string }[] = [
	{
		file: 'FloatMenuOptionProvider_BulkUnloadCarrier.cs',
		method: 'GetOptions',
		why: 'the reported entry — without it the "Prioritize bulk unloading" option is offered on a hosted guest again',
	},
	{
		file: 'Patch_WorkGiver_UnloadCarriers.cs',
		method: 'ShouldHandle',
		why: 'the work-giver takeover — defence in depth for any job HD builds off vanilla\'s own scan',
	},
	{
		file: 'JobDriver_UnloadCarrierInBulk.cs',
		method: 'Notify_Starting',
		why: 'the UnloadEverything write, which also opens vanilla\'s faction-blind unload work-giver on the victim',
	},
	{
		file: 'JobDriver_UnloadCarrierInBulk.cs',
		method: 'MakeNewToils',
		why: 'the job-level end condition — the transfer loop must not run at all on a carrier that fails the rule',
	},
]

/**
 * The one file allowed to assign UnloadEverything, and the one allowed to read HostFaction. Both are inventories
 * of a spelling that reads as harmless everywhere it appears, which is why they are pinned by file rather than
 * left to review.
 */
const FLAG_WRITER = 'JobDriver_UnloadCarrierInBulk.cs'
const HOST_FACTION_READER = 'Patch_WorkGiver_UnloadCarriers.cs'

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

/**
 * The argument text of every `FailOn(...)` call in `body`, paren-matched so a nested call or lambda is kept whole.
 *
 * → GOTCHA: `\bFailOn\s*\(` deliberately does NOT match `FailOnDespawnedOrNull(` / `FailOnForbidden(` — those are
 *   separate helpers taking a TargetIndex, and folding them in would let any driver satisfy this check for free.
 *   That near-miss is the reason this returns the ARGUMENTS rather than a count: the driver already carries an
 *   unrelated FailOn (the competing-claimant scan), so "is there a FailOn?" is satisfied by a driver whose
 *   permission answer is computed and then thrown away — a mutant that survived exactly that weaker check.
 */
function failOnArguments(body: string): string[] {
	const out: string[] = []
	const call = /\bFailOn\s*\(/g
	let hit: RegExpExecArray | null
	while ((hit = call.exec(body)) !== null) {
		let depth = 0
		const start = hit.index + hit[0].length - 1
		for (let i = start; i < body.length; i++) {
			const c = body[i]
			if (c === '(') depth++
			else if (c === ')') {
				depth--
				if (depth === 0) {
					out.push(body.slice(start + 1, i))
					break
				}
			}
		}
	}
	return out
}

/** Slice a method body by brace-matching from its declaration. Null if the method is not found. */
function sliceMethodBody(src: string, methodName: string): string | null {
	const sig = new RegExp(`\\b${methodName}\\s*\\([^)]*\\)[\\s\\S]{0,200}?\\{`).exec(src)
	if (!sig) return null
	return braceSlice(src, sig.index)
}

/**
 * Slice a method body anchored on an explicit signature regex rather than a bare name.
 * Needed wherever a name is OVERLOADED: TransportLoad has a public 2-arg forwarder and the private
 * multi-arg implementation for both of the methods checked here, and a name-only match would slice the
 * one-line forwarder and report a false clean.
 */
function sliceOverload(src: string, signature: RegExp): string | null {
	const sig = signature.exec(src)
	if (!sig) return null
	return braceSlice(src, sig.index)
}

/** Read a file's code (comments + literal content blanked). Records an error and returns null if missing. */
async function readCode(path: string, label: string): Promise<string | null> {
	const file = Bun.file(path)
	if (!(await file.exists())) {
		errors.push(`${label} is MISSING (${path}). The non-colony-pawn gate cannot verify it.`)
		return null
	}
	return codeOnly((await file.text()).replace(/\r\n/g, '\n'))
}

async function main(): Promise<void> {
	const files = csFilesUnder(GAME_SRC)

	// ---- 1. The Core rule and its oracle exist. ----
	const policy = await readCode(POLICY_PATH, 'BulkUnloadPermissionPolicy.cs')
	if (policy && !/\bMayBulkUnload\s*\(/.test(policy)) {
		errors.push(
			'BulkUnloadPermissionPolicy no longer declares MayBulkUnload. That expression IS the rule — "a pawn we ' +
				'own or hold prisoner, and no quest claim on it" — and every seam below calls it by that name.'
		)
	}
	await readCode(TESTS_PATH, 'BulkUnloadPermissionPolicyTests.cs') // existence is the assertion

	// ---- 2. The permission seam routes through the Core rule. ----
	const seam = await readCode(SEAM_PATH, 'Patch_WorkGiver_UnloadCarriers.cs')
	let seamBody: string | null = null
	if (seam) {
		seamBody = sliceMethodBody(seam, 'PlayerMayUnload')
		if (seamBody === null) {
			errors.push(
				'BulkUnloadGate no longer defines PlayerMayUnload. Every entry point below calls it; without one ' +
					'shared seam each site re-derives the permission and they drift — which is precisely how vanilla\'s ' +
					'job-time predicate ended up being used as an offer predicate.'
			)
		} else if (!/\bBulkUnloadPermissionPolicy\s*\.\s*MayBulkUnload\s*\(/.test(seamBody)) {
			errors.push(
				'BulkUnloadGate.PlayerMayUnload no longer calls BulkUnloadPermissionPolicy.MayBulkUnload. The decision ' +
					'has to stay in Core: it is the only part of this fix a unit test can reach at all.'
			)
		}
	}

	// ---- 3. Every entry point consults the seam. ----
	let gated = 0
	for (const site of GATED_METHODS) {
		const code = await readCode(resolve(GAME_SRC, site.file), site.file)
		if (code === null) continue
		const body = sliceMethodBody(code, site.method)
		if (body === null) {
			errors.push(
				`${site.file} no longer defines ${site.method}. It is a gated entry point for the carrier unload ` +
					`(${site.why}); if it was renamed or restructured, update this guard deliberately.`
			)
			continue
		}
		if (!PERMISSION_CALL.test(body)) {
			errors.push(
				`${site.file}.${site.method} no longer calls BulkUnloadGate.PlayerMayUnload — ${site.why}. A pawn the ` +
					`colony merely HOSTS (a Hospitality guest, a rescued wanderer, a downed Bestower) can reach this ` +
					`path again, and vanilla protects those pawns on purpose.`
			)
			continue
		}
		gated++
	}

	// ---- 4 + 5. The driver GATES its write, and refuses the job outright. ----
	const driver = await readCode(DRIVER_PATH, FLAG_WRITER)
	if (driver) {
		const starting = sliceMethodBody(driver, 'Notify_Starting')
		if (starting !== null) {
			// The answer must GUARD the write, not merely be computed before it — see PERMISSION_GUARD.
			const permitAt = starting.search(PERMISSION_GUARD)
			const writeAt = starting.search(FLAG_WRITE)
			if (writeAt < 0) {
				errors.push(
					`${FLAG_WRITE} no longer appears in ${FLAG_WRITER}.Notify_Starting. The flag write lives there ` +
						'deliberately (a click-time write would mutate only the clicking multiplayer client and desync); ' +
						'if it moved, this guard must follow it or it is guarding nothing.'
				)
			} else if (permitAt < 0 || permitAt > writeAt) {
				errors.push(
					`${FLAG_WRITER}.Notify_Starting raises UnloadEverything without an EARLY-RETURN guard of the form ` +
						'`if (!…PlayerMayUnload(…)) return;` ahead of it. That flag is SCRIBED, and raising it also ' +
						"opens vanilla's own faction-blind WorkGiver_UnloadCarriers on the victim for every hauler on " +
						'the map, indefinitely — so an ungated write is a WIDER hole than the float-menu option it ' +
						'normally comes from, and HD\'s own job-level fail condition does not close it. Computing the ' +
						'permission and discarding it satisfies "the call came first" and still ships the bug.'
				)
			}
		}
		const toils = sliceMethodBody(driver, 'MakeNewToils')
		if (toils !== null) {
			// The permission answer must REACH a fail condition, not merely be computed next to one. Accept either
			// spelling: bound to a local that a FailOn then reads (what the driver does, so the quest-parts walk
			// happens once per setup rather than every tick), or called inline inside the FailOn lambda.
			const bound = /\b(\w+)\s*=\s*(?:[\w.]*\.)?PlayerMayUnload\s*\(/.exec(toils)?.[1]
			const carriesPermission = failOnArguments(toils).some(
				(arg) => PERMISSION_CALL.test(arg) || (bound !== undefined && new RegExp(`\\b${bound}\\b`).test(arg))
			)
			if (!carriesPermission) {
				errors.push(
					`${FLAG_WRITER}.MakeNewToils computes the permission but no FailOn consumes it, so the job runs ` +
						'anyway. Withholding the flag stops vanilla\'s haulers but not this driver: its transfer loop ' +
						'would still empty a carrier the job should never have targeted (a foreign caller, or a job ' +
						'queued in a save made before this rule existed). Note the driver carries an UNRELATED FailOn ' +
						'for the competing-claimant scan, which is why this checks what the condition reads.'
				)
			}
		}
	}

	// ---- 6 + 7. Repo-wide: one flag writer, one host-faction reader. ----
	let flagWriters = 0
	let hostFactionReaders = 0
	let proseMentions = 0
	for (const path of files) {
		const name = basename(path)
		const raw = (await Bun.file(path).text()).replace(/\r\n/g, '\n')
		const code = codeOnly(raw)
		// Live control for the codeOnly discipline: count files that name a banned subject ONLY in prose — the
		// word appears in the raw text and nowhere in the code. Every one of them is a file a raw-text version of
		// this guard would have to allowlist, and one of them spells the removed predicate out verbatim
		// (`carrier.HostFaction == pawn.Faction`) because explaining it is the point of the comment.
		const namesInProseOnly =
			(/\bUnloadEverything\b/.test(raw) && !/\bUnloadEverything\b/.test(code)) ||
			(HOST_FACTION.test(raw) && !HOST_FACTION.test(code))
		if (namesInProseOnly) proseMentions++

		if (FLAG_WRITE.test(code)) {
			flagWriters++
			if (name !== FLAG_WRITER) {
				errors.push(
					`${name} assigns Pawn_InventoryTracker.UnloadEverything. That flag is SCRIBED world state and no ` +
						`player-facing vanilla action raises it on a non-colony pawn; raising it also hands the victim to ` +
						`vanilla's own faction-blind unload work-giver indefinitely. Exactly one writer is allowed ` +
						`(${FLAG_WRITER}.Notify_Starting, behind PlayerMayUnload) — route this through it.`
				)
			}
		}
		if (HOST_FACTION.test(code)) {
			hostFactionReaders++
			if (name !== HOST_FACTION_READER) {
				errors.push(
					`${name} reads Pawn.HostFaction. A host faction says the colony HOSTS this pawn, never that it owns ` +
						`it: guests, rescued wanderers and quest pawns all carry the player as host, and treating that as ` +
						`permission is the whole bug. Decide with BulkUnloadGate.PlayerMayUnload (or, for "is this our ` +
						`prisoner", IsPrisoner + the host-faction test it already makes) in ${HOST_FACTION_READER}.`
				)
			}
		}
	}
	if (flagWriters === 0) {
		errors.push(
			'No file in the mod assigns UnloadEverything any more. Either the bulk carrier unload was removed (then ' +
				'retire this guard deliberately) or the write moved somewhere this guard cannot see it.'
		)
	}
	if (hostFactionReaders === 0) {
		errors.push(
			`No file reads HostFaction any more, so ${HOST_FACTION_READER}'s prisoner arm is gone. That arm is the ` +
				'LEGITIMATE half of the predicate this fix narrowed: a prisoner\'s Faction stays its original faction, ' +
				'so only the host-faction test can recognise one, and vanilla lets the colony unload its own prisoners ' +
				'(ITab_Pawn_Gear.CanControl and CanBeStrippedByColony both admit them). Deleting it is a feature loss, ' +
				'not a fix.'
		)
	}

	// ---- 8. The loaders' HasJob/JobOn pair stays in lockstep. ----
	const loaders = await readCode(LOADERS_PATH, 'TransportLoad.cs')
	const lockstep: { label: string; guarded: boolean }[] = []
	if (loaders) {
		const halves: { label: string; signature: RegExp }[] = [
			{
				label: 'HasPotentialBulkWork (the HasJob half)',
				signature: /\bHasPotentialBulkWork\s*\(\s*Pawn[^)]*\bbool\s+featureEnabled\s*\)[\s\S]{0,200}?\{/,
			},
			{
				label: 'TryGiveBulkJob (the JobOn half)',
				signature: /\bTryGiveBulkJob\s*\(\s*Pawn[^)]*\bbool\s+playerOrder\s*\)[\s\S]{0,200}?\{/,
			},
		]
		for (const half of halves) {
			const body = sliceOverload(loaders, half.signature)
			if (body === null) {
				errors.push(
					`TransportLoad's ${half.label} could not be located by its signature. This guard slices by the full ` +
						'parameter list on purpose (both names are overloaded, and the short forwarder would report a ' +
						'false clean) — update the signature here if the method genuinely changed shape.'
				)
				lockstep.push({ label: half.label, guarded: false })
				continue
			}
			const guarded = PLAYER_FACTION_REFUSAL.test(body) && QUEST_LODGER_REFUSAL.test(body)
			lockstep.push({ label: half.label, guarded })
		}
		const missing = lockstep.filter((h) => !h.guarded)
		if (missing.length === lockstep.length && lockstep.length > 0) {
			errors.push(
				'Neither half of TransportLoad carries the explicit "player faction, not a quest lodger" refusal any ' +
					'more. Without it the loaders keep foreign pawns out only because visitor mods happen to keep their ' +
					'guests Lord-owned (YieldRouter.IsEligible stands down a Lord-driven pawn); EligibilityPolicy has no ' +
					'faction dimension by design, so a guest that ever lost its Lord walks straight into HD\'s bulk ' +
					'loaders — CompHauledToInventory and all. Every sibling entry point states this outright ' +
					'(BulkHaul.cs, EnRoutePickup.cs, UrgentHaulBulk.cs, StorageRouting.cs).'
			)
		} else if (missing.length > 0) {
			errors.push(
				`TransportLoad's HasJob/JobOn pair is OUT OF LOCKSTEP: ${missing
					.map((h) => h.label)
					.join(' and ')} lost the "player faction, not a quest lodger" refusal while the other half kept it. ` +
					'A refusal on one side only is worse than none: the work scan asks HasJob, is told there is work, ' +
					'asks JobOn, gets null, and re-asks within the same tick — the "started 10 jobs in one tick" loop the ' +
					'boarding-passenger carve-out in that same file already warns about. Change the two together.'
			)
		}
	}

	if (errors.length > 0) {
		console.error(`\n[non-colony-pawn-gates] FAIL, ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  x ${e}`)
		console.error(
			'\n  This guard exists because vanilla protects a hosted pawn\'s belongings with four separate gates and ' +
				'HD\'s bulk unload consulted none of them — a downed guest could be emptied of a psylink neuroformer. ' +
				'The rule itself is unit-tested; whether it is CONSULTED is invisible to the NUnit suite, which ' +
				'references only HaulersDream.Core and cannot see a Pawn. If you intentionally restructured these ' +
				'seams, update this script to match. Do not just delete the check.\n'
		)
		process.exit(1)
	}

	console.log(
		`[non-colony-pawn-gates] PASS, ${files.length} source files scanned, ${gated}/${GATED_METHODS.length} carrier-unload ` +
			`entry points consult BulkUnloadGate.PlayerMayUnload (routed to Core), UnloadEverything written in ` +
			`${flagWriters} file (gated, + job-level FailOn), HostFaction read in ${hostFactionReaders} file ` +
			`(the prisoner arm), ${lockstep.filter((h) => h.guarded).length}/2 TransportLoad halves carry the explicit ` +
			`faction + quest-lodger refusal.`
	)
	console.log(
		`  - control: ${proseMentions} file(s) name UnloadEverything / HostFaction in COMMENTS ONLY and were ` +
			`correctly ignored (all scanning runs over codeOnly output — a raw-text guard would fail on the very ` +
			`explanations these seams exist to carry, one of which quotes the removed predicate verbatim).`
	)
}

await main()
