// Static guard on the storage commitment seam (issues #114, #138, #162, #248).
//
// Hauler's Dream strips vanilla's destination cell reservation so several haulers can share a storage tile.
// That reservation did DOUBLE duty: it hid the cell AND it shrank every other pawn's job.count, because
// HaulAIUtility.HaulToCellStorageJob sums GetItemStackSpaceLeftFor only over cells that pass IsGoodStoreCell.
// For three releases HD gave nothing back, so every concurrent hauler priced the same free cells into its own
// count. StorageCommitments is what replaces it, and every one of the properties below is invisible to the
// compiler and unreachable by the unit suite (HaulersDream.Tests references only HaulersDream.Core, so it can
// observe a decision RULE and never the Verse arguments the glue passes it).
//
// Fails the build (exit 1) when:
//   1. a second capacity oracle appears — GetItemStackSpaceLeftFor called outside StorageCommitments.cs;
//   2. the reservation strip stops being conditional on the commit, or a hand-written unstackable carve-out
//      comes back to any of the three sites it used to be copied across;
//   3. either Harmony adapter goes missing, gains a duplicate, or stops referencing the seam;
//   4. a NEW commit site appears outside the reviewed allowlist;
//   5. the claim ledger becomes a per-tick snapshot again — the exact regression that made the #114 fix
//      correct and its answer still wrong;
//   6. exclude-self turns back into a forgettable boolean flag instead of the possession test;
//   7. any of the three files the seam is made of, or the members they must export, disappears;
//   8. the ledger field is written from outside its own file, or reached by a name rule 4 does not watch —
//      a direct `storageClaims = …` skips the generation bump as well as the allowlist, and a bare
//      `SetStorageClaims(…)` from another partial of the same component, or a `using static`, spells the
//      same call without the qualified prefix rule 4 matches on;
//   9. the startup bind tripwire loses a target, its consequence, or its call.
//
// Run directly to self-check:  bun scripts/check-storage-commit-seam.ts
import { resolve } from 'node:path'
import { codeOnly, csFilesUnder, repoRoot } from './lib'

const GAME_SRC = resolve(repoRoot, 'Source/HaulersDream')
const CORE_SRC = resolve(repoRoot, 'Source/HaulersDream.Core')

const SEAM = resolve(GAME_SRC, 'StorageCommitments.cs')
const ADAPTERS = resolve(GAME_SRC, 'StorageCommitAdapters.cs')
const CLAIMS = resolve(GAME_SRC, 'HaulersDreamGameComponent.StorageClaims.cs')
const HAUL_TO_STACK = resolve(GAME_SRC, 'HaulToStack.cs')
const UNLOAD_DRIVER = resolve(GAME_SRC, 'JobDriver_UnloadHauledInventory.cs')
const MOD = resolve(GAME_SRC, 'HaulersDreamMod.cs')
const LEDGER = resolve(CORE_SRC, 'StorageClaimLedger.cs')
const POLICY = resolve(CORE_SRC, 'StorageCommitPolicy.cs')

/**
 * The ONE file allowed to ask a cell how much of a def it can still take. A second answer to that question,
 * anywhere, is a second unarbitrated capacity oracle — which is what the whole seam exists to make singular.
 */
const CAPACITY_ORACLE = 'GetItemStackSpaceLeftFor'

/**
 * Files permitted to record a commitment, with why each is allowed. A new entry is a deliberate widening of
 * the seam's write surface and must be reviewed: a path that books a claim without also being covered by the
 * gate and the counter is how "several pawns price the same cells" comes back.
 */
const COMMIT_SITES: { file: string; why: string }[] = [
	{
		file: 'StorageCommitments.cs',
		why: 'the seam itself — the janitor adoption pass and the only writer of the ledger field'
	},
	{
		file: 'HaulToStack.cs',
		why: 'Patch_JobDriver_HaulToCell_NoCellReservation — every vanilla HaulMode.ToCellStorage haul'
	},
	{
		file: 'JobDriver_BulkHaul.cs',
		why: "TryMakePreToilReservations — the bulk sweep's planned destinations, per def"
	},
	{
		file: 'JobDriver_UnloadHauledInventory.cs',
		why: 'the delivery and home-area-fallback placements'
	}
]

/**
 * Every way the ledger can be written to. The last two matter as much as the first two: SetStorageClaims is
 * `internal`, so any file in the assembly could replace the whole ledger without ever naming Commit — an
 * allowlist that only watched the polite front door would be a guard that could only ever pass.
 */
const COMMIT_CALLS = [
	'StorageCommitments.Commit(',
	'StorageCommitments.TryCommit(',
	'HaulersDreamGameComponent.SetStorageClaims(',
	'HaulersDreamGameComponent.ClearStorageClaims('
]

/**
 * The ledger's two mutable fields. Both are `internal`, so every file in the assembly can assign them, and a
 * direct `HaulersDreamGameComponent.storageClaims = …` is worse than an unreviewed commit site: it also skips
 * `SetStorageClaims`'s generation bump, which is the ONLY thing invalidating the derived per-tick evidence memo.
 * A write that forgets it reintroduces the same-tick staleness the counter exists to prevent — the fourth root
 * cause of this whole bug family. So the fields may be assigned in exactly one file.
 */
const LEDGER_FIELDS = ['storageClaims', 'storageClaimGeneration']

/**
 * The two seam types whose members rule 4 pins by their QUALIFIED spelling. `using static` would let any file
 * write `Commit(...)` or `SetStorageClaims(...)` unqualified and walk straight past that allowlist, so the
 * import form is banned outright rather than the guard trying to resolve unqualified call sites. Nothing in
 * Source/HaulersDream/ uses `using static` at all today; the whole idiom is confined to the test project.
 */
const NO_USING_STATIC = /\busing\s+static\s+[\w.]*\b(StorageCommitments|HaulersDreamGameComponent)\s*;/

/**
 * Members of `HaulersDreamGameComponent` that rule 4 watches qualified. The component is PARTIAL and spread
 * across seven files, so any of its other partials can call these with no prefix at all — the same back door as
 * `using static`, reachable without importing anything. Only the file that declares them may spell them bare.
 */
const BARE_LEDGER_CALLS = ['SetStorageClaims(', 'ClearStorageClaims(']

/**
 * The startup bind tripwire (HaulersDreamMod.VerifyStorageSeam), duplicated here on purpose — the same
 * runtime-tripwire + build-tripwire pairing DropProtectionTargets gets in check-drop-protection.ts.
 *
 * A build guard cannot see a BIND failure: the reservation strip and its two replacement adapters are separate
 * Harmony patch classes applied by a loop that catches per-class failures, so "strip bound, adapters not" is
 * expressible on a future point release or under a foreign transpiler, and it is the original bug shipped
 * inert. Only startup verification catches that, and only DISABLING the seam makes it safe — an error line
 * nobody reads is what this whole phase exists to stop relying on.
 */
const SEAM_TRIPWIRE = [
	{ method: 'HaulToCellStorageJob', patchClass: 'Patch_HaulToCellStorageJob_ClampToCommitments' },
	{ method: 'IsGoodStoreCell', patchClass: 'Patch_IsGoodStoreCell_HonourCommitments' },
	{ method: 'TryMakePreToilReservations', patchClass: 'Patch_JobDriver_HaulToCell_NoCellReservation' }
]

/**
 * The hand-written unstackable test. It used to be copied across four sites and drifted apart once already
 * (issue #162 — endless pacing in a hospital, because an unreserved one-capacity cell had no arbitration).
 * The ledger subsumes it: one corpse claims one unit of one cell. Its reappearance in a RESERVATION decision
 * means someone re-derived the special case instead of asking the seam.
 */
const CARVE_OUT = /stackLimit\s*<=\s*1/

/** Members each seam file must still export; losing one silently disables a whole half of the fix. */
const REQUIRED_MEMBERS: { path: string; label: string; members: string[] }[] = [
	{
		path: SEAM,
		label: 'StorageCommitments.cs',
		members: [
			'int FreeUnitsFor(',
			'bool TryCommit(',
			'void Commit(',
			'void DropClaim(',
			'void InterruptCommittersTo(',
			'void RunJanitor(',
			'bool AnyClaims',
			'bool InsideSpaceScan'
		]
	},
	{
		path: LEDGER,
		label: 'Core/StorageClaimLedger.cs',
		members: [
			'Add(',
			'DropPawn(',
			'Reconcile(',
			'EffectiveClaim(',
			'ClaimedByOthers(',
			'ClaimedByPawn(',
			'ClaimedTotal(',
			'AnyRows('
		]
	},
	{
		path: POLICY,
		label: 'Core/StorageCommitPolicy.cs',
		members: ['int Commit(HaulSight sight, bool delivering)']
	}
]

/**
 * The brace-balanced body of a C# type, so a rule can ask "does THIS class do X" instead of "does the file
 * mention X somewhere". Returns null when the type is not declared in the source.
 */
function typeBody(code: string, name: string): string | null {
	const decl = new RegExp(`\\b(?:class|struct)\\s+${name}\\b`).exec(code)
	if (!decl) return null
	let i = code.indexOf('{', decl.index)
	if (i < 0) return null
	let depth = 0
	const start = i
	for (; i < code.length; i++) {
		if (code[i] === '{') depth++
		else if (code[i] === '}' && --depth === 0) return code.slice(start, i + 1)
	}
	return null
}

/** Count non-overlapping occurrences of a literal needle. */
function countOf(haystack: string, needle: string): number {
	let n = 0
	let from = 0
	for (;;) {
		const at = haystack.indexOf(needle, from)
		if (at < 0) return n
		n++
		from = at + needle.length
	}
}

/** The 1-based line a character offset falls on, so a whole-file regex can still report a location. */
function lineOf(src: string, index: number): number {
	let line = 1
	for (let i = 0; i < index && i < src.length; i++) if (src[i] === '\n') line++
	return line
}

/**
 * Every ASSIGNMENT to `field` in `src`, as 1-based line numbers. Matched over the whole (comment- and
 * string-stripped) text rather than line by line, so a write split across lines still counts.
 *
 * The operator set is what distinguishes a write from a read: plain `=` but never `==`, the compound forms,
 * and `++`/`--`. `>=` / `<=` / `!=` are excluded by construction — their leading character is not in the
 * compound set and is not `=`. `ref`/`out` count as writes: handing the field to a method that assigns it is
 * the same back door with an extra step.
 */
function assignmentsTo(src: string, field: string): number[] {
	const re = new RegExp(
		`\\b${field}\\b\\s*(?:(?:[-+*/%&|^]|<<|>>)?=(?!=)|\\+\\+|--)` +
			`|\\b(?:ref|out)\\s+(?:[A-Za-z_][\\w.]*\\.)?${field}\\b`,
		'g'
	)
	const lines: number[] = []
	for (let m = re.exec(src); m !== null; m = re.exec(src)) lines.push(lineOf(src, m.index))
	return lines
}

/** Read a file and hand back only its real code. */
async function code(path: string): Promise<string> {
	return codeOnly(await Bun.file(path).text())
}

async function main(): Promise<void> {
	const errors: string[] = []
	const gameFiles = csFilesUnder(GAME_SRC)
	const codeByFile = new Map<string, string>()
	for (const file of gameFiles) codeByFile.set(file, codeOnly(await Bun.file(file).text()))

	const rel = (file: string) => file.slice(repoRoot.length + 1).replace(/\\/g, '/')

	// ── 1. ONE capacity oracle ────────────────────────────────────────────────────────────────────────
	let oracleSites = 0
	for (const [file, src] of codeByFile) {
		if (!src.includes(CAPACITY_ORACLE)) continue
		if (file === SEAM) {
			oracleSites += countOf(src, CAPACITY_ORACLE)
			continue
		}
		const line = src.split('\n').findIndex((l) => l.includes(CAPACITY_ORACLE)) + 1
		errors.push(
			`${rel(file)}:${line} calls ${CAPACITY_ORACLE} — a SECOND, unarbitrated capacity oracle. ` +
				'Every "how much room is left here" answer must come from StorageCommitments.MeasureGroup, or two ' +
				'call sites will price the same cells differently and the over-haul comes straight back.'
		)
	}
	if (oracleSites === 0)
		errors.push(
			`StorageCommitments.cs no longer calls ${CAPACITY_ORACLE} — the seam has stopped measuring storage at all.`
		)

	// ── 2. the reservation strip is conditional on the commit ─────────────────────────────────────────
	const haulToStack = codeByFile.get(HAUL_TO_STACK) ?? ''
	const unload = codeByFile.get(UNLOAD_DRIVER) ?? ''
	for (const [label, src] of [
		['HaulToStack.cs', haulToStack],
		['JobDriver_UnloadHauledInventory.cs', unload]
	] as const) {
		const line = src.split('\n').findIndex((l) => CARVE_OUT.test(l)) + 1
		if (line > 0)
			errors.push(
				`${label}:${line} spells the unstackable carve-out by hand again. That test was copied across ` +
					'four sites, drifted apart once (issue #162), and is subsumed by the ledger: one corpse claims ' +
					'one unit of one cell, so the next hauler is refused by the gate. Ask TryCommit instead.'
			)
	}
	const stripPatch = typeBody(haulToStack, 'Patch_JobDriver_HaulToCell_NoCellReservation')
	if (!stripPatch)
		errors.push('HaulToStack.cs no longer declares Patch_JobDriver_HaulToCell_NoCellReservation.')
	else if (!stripPatch.includes('TryCommit('))
		errors.push(
			'Patch_JobDriver_HaulToCell_NoCellReservation no longer calls TryCommit — it would be stripping ' +
				"vanilla's destination reservation without putting anything in its place, which IS the bug."
		)
	// The unload branches must ask ONE question (did the ledger take this destination on?) rather than
	// re-deriving the feature gate. TryCommit reads the Haul to Stack switch itself, in one place.
	if (/haulToStack/.test(unload))
		errors.push(
			'JobDriver_UnloadHauledInventory.cs reads the haulToStack setting again. The reservation decision ' +
				'must be TryCommit alone; a second predicate beside it is how the four carve-outs drifted apart.'
		)

	// ── 3. exactly one of each adapter, each reaching the seam ────────────────────────────────────────
	const ADAPTER_PINS = [
		{
			attribute: '[HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToCellStorageJob))]',
			role: 'the COUNTER (clamps job.count to what is genuinely free)'
		},
		{
			attribute: '[HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]',
			role: 'the GATE (keeps a pawn from being routed to a group that is fully spoken for)'
		}
	]
	for (const { attribute, role } of ADAPTER_PINS) {
		let found = 0
		for (const src of codeByFile.values()) found += countOf(src, attribute)
		if (found !== 1)
			errors.push(
				`expected exactly ONE ${attribute} in the assembly (${role}); found ${found}. ` +
					'Zero means that half of the seam is gone; more than one means two patches are clamping the ' +
					'same number and will double-subtract.'
			)
	}
	const adapters = codeByFile.get(ADAPTERS) ?? ''
	for (const cls of ['Patch_HaulToCellStorageJob_ClampToCommitments', 'Patch_IsGoodStoreCell_HonourCommitments']) {
		const body = typeBody(adapters, cls)
		if (!body) errors.push(`StorageCommitAdapters.cs no longer declares ${cls}.`)
		else if (!body.includes('StorageCommitments.'))
			errors.push(`${cls} no longer reaches StorageCommitments — the adapter is inert.`)
	}

	// ── 4. commit sites are allowlisted ───────────────────────────────────────────────────────────────
	const allowed = new Set(COMMIT_SITES.map((s) => resolve(GAME_SRC, s.file)))
	let commitCalls = 0
	const seenIn = new Set<string>()
	for (const [file, src] of codeByFile) {
		const calls = COMMIT_CALLS.reduce((n, call) => n + countOf(src, call), 0)
		if (calls === 0) continue
		if (!allowed.has(file)) {
			errors.push(
				`${rel(file)} records a storage commitment but is not on the reviewed allowlist in ` +
					'scripts/check-storage-commit-seam.ts. A path that books a claim without also being covered by ' +
					'the gate and the counter re-opens the over-haul. Add it here once it has been reviewed.'
			)
			continue
		}
		commitCalls += calls
		seenIn.add(file)
	}
	for (const site of COMMIT_SITES)
		if (!seenIn.has(resolve(GAME_SRC, site.file)))
			errors.push(
				`${site.file} no longer records a storage commitment (${site.why}). A path that stopped ` +
					'committing is invisible to every other hauler, which is exactly the reported bug.'
			)

	// ── 5. the ledger is not a per-tick snapshot ──────────────────────────────────────────────────────
	const claims = codeByFile.get(CLAIMS)
	if (claims === undefined) {
		errors.push('HaulersDreamGameComponent.StorageClaims.cs is missing — the claim ledger has no home.')
	} else {
		const field = /storageClaims\b/
		if (!field.test(claims)) errors.push('HaulersDreamGameComponent.StorageClaims.cs no longer declares storageClaims.')
		if (/\[ThreadStatic\]/.test(claims))
			errors.push(
				'the storage claim ledger has become [ThreadStatic]. It is AUTHORITATIVE shared state: a per-thread ' +
					"copy means one hauler's commitment is invisible to a work scan running on another thread."
			)
		if (/TickKeyedMemo/.test(claims))
			errors.push(
				'the storage claim ledger has become a TickKeyedMemo. A per-tick snapshot cannot see a commitment ' +
					'made earlier in the SAME tick, which is precisely why the #114 rule was correct and its answer ' +
					'was still wrong (#248). See TickSnapshot_BreaksTheSameRule in HaulDestinationOverCommitTests.'
			)
		if (!/StorageClaimJanitorTicks\s*=\s*\d+/.test(claims))
			errors.push('the janitor tick constant (StorageClaimJanitorTicks) is gone — nothing reconciles the ledger.')
	}

	// ── 6. exclude-self is structural, never a flag ───────────────────────────────────────────────────
	const seam = codeByFile.get(SEAM)
	if (seam === undefined) {
		errors.push('Source/HaulersDream/StorageCommitments.cs is missing — the seam is gone.')
	} else {
		// Null-conditional tolerant: `carryTracker?.CarriedThing` is the same test, and a guard that only
		// matched one spelling would fail the day someone added a `?`.
		if (!/carryTracker\s*\??\.\s*CarriedThing/.test(seam))
			errors.push(
				'StorageCommitments.cs no longer tests carryTracker.CarriedThing. Whether a pawn is PLANNING a ' +
					'pickup or DELIVERING cargo it already holds must be DERIVED from possession of the subject.'
			)
		if (!/ParentHolder/.test(seam))
			errors.push(
				'StorageCommitments.cs no longer checks the inventory parent. A pawn holding cargo in its ' +
					'INVENTORY (the bulk-haul case) is delivering just as much as one carrying it in its hands.'
			)
		const flagged = /FreeUnitsFor\s*\([^)]*\bbool\s+(delivering|planning)\b/.exec(seam)
		if (flagged)
			errors.push(
				'FreeUnitsFor has grown a bool delivering/planning parameter. That is the forgettable flag the ' +
					'possession test replaced: a caller that passes it wrong makes a pawn compete with itself, and ' +
					'nothing in the type system says which way round it goes.'
			)
	}

	// ── 7. positive pins ──────────────────────────────────────────────────────────────────────────────
	for (const { path, label, members } of REQUIRED_MEMBERS) {
		const src = await code(path).catch(() => null)
		if (src === null) {
			errors.push(`${label} is missing.`)
			continue
		}
		for (const member of members)
			if (!src.includes(member)) errors.push(`${label} no longer declares ${member.replace(/\($/, '')}.`)
	}

	// ── 8. the ledger field has no back door ──────────────────────────────────────────────────────────
	// Rule 4 watches the polite front door by its QUALIFIED spelling. Three other spellings reach the same
	// state: assigning the field directly (which ALSO skips the generation bump), calling the writers bare
	// from another partial of the same component, and importing them with `using static`.
	let fieldWrites = 0
	for (const [file, src] of codeByFile) {
		for (const field of LEDGER_FIELDS) {
			const lines = assignmentsTo(src, field)
			if (lines.length === 0) continue
			if (file === CLAIMS) {
				fieldWrites += lines.length
				continue
			}
			errors.push(
				`${rel(file)}:${lines[0]} assigns ${field} directly. Only ` +
					'HaulersDreamGameComponent.StorageClaims.cs may write the ledger: every other write must go ' +
					'through SetStorageClaims, which is the single place the generation counter is bumped. A ' +
					'write that skips it leaves the per-tick evidence memo serving figures taken BEFORE the ' +
					'claim — the same-tick blindness this whole seam exists to remove — and it walks past the ' +
					'reviewed commit-site allowlist on the way.'
			)
		}
		if (NO_USING_STATIC.test(src))
			errors.push(
				`${rel(file)} imports a seam type with 'using static'. That lets Commit / TryCommit / ` +
					'SetStorageClaims be written UNQUALIFIED, and the commit-site allowlist above matches the ' +
					'qualified spelling — so the allowlist would silently stop covering this file.'
			)
		if (file === CLAIMS) continue
		for (const call of BARE_LEDGER_CALLS) {
			const bare = countOf(src, call) - countOf(src, `HaulersDreamGameComponent.${call}`)
			if (bare > 0)
				errors.push(
					`${rel(file)} calls ${call.replace(/\($/, '')} without naming ` +
						'HaulersDreamGameComponent. The component is partial across seven files, so its other ' +
						'partials can reach the ledger writers with no prefix at all — which the commit-site ' +
						'allowlist, matching the qualified spelling, would never see.'
				)
		}
	}
	if (fieldWrites === 0)
		errors.push(
			'HaulersDreamGameComponent.StorageClaims.cs never assigns the ledger fields — SetStorageClaims has ' +
				'stopped being the write path, so this rule is now watching a door that leads nowhere.'
		)

	// ── 9. the startup bind tripwire ──────────────────────────────────────────────────────────────────
	const mod = codeByFile.get(MOD)
	if (mod === undefined) {
		errors.push('HaulersDreamMod.cs is missing — the storage-seam bind tripwire has no home.')
	} else {
		if (!mod.includes('StorageSeamTargets'))
			errors.push(
				'HaulersDreamMod.cs no longer declares StorageSeamTargets. Nothing then checks at startup that ' +
					'the reservation strip and its two replacement adapters all actually bound — and they are ' +
					'separate patch classes applied by a loop that degrades each on its own, so "strip bound, ' +
					'adapters missing" is exactly the original bug with no symptom.'
			)
		if (!mod.includes('VerifyStorageSeam();'))
			errors.push('HaulersDreamMod.cs declares the bind tripwire but never calls VerifyStorageSeam().')
		if (!mod.includes('StorageCommitments.Disable()'))
			errors.push(
				'the bind tripwire no longer calls StorageCommitments.Disable(). Logging an error is not a ' +
					'consequence: with the adapters unbound and the strip still live, HD removes the vanilla ' +
					'destination reservation and supplies nothing in its place. The strip must not run without ' +
					'its replacement.'
			)
		for (const { method, patchClass } of SEAM_TRIPWIRE) {
			if (!mod.includes(method))
				errors.push(`the bind tripwire no longer names vanilla's ${method} — that seam is unverified at startup.`)
			if (!mod.includes(patchClass))
				errors.push(
					`the bind tripwire no longer names ${patchClass}. It must check the PATCH CLASS, not just ` +
						'that some HD patch is on the method: two other HD classes patch JobDriver_HaulToCell, so a ' +
						'weaker check could pass while the piece that matters is the one that failed.'
				)
		}
	}
	if (seam !== undefined && !/seamDisabled/.test(seam))
		errors.push(
			'StorageCommitments.cs no longer carries the off switch the bind tripwire throws. ActiveOn is what ' +
				'makes Disable() reach every entry point at once — the commits, the janitor and both adapters — ' +
				'and a partial stand-down is worse than either extreme (a bound counter with no gate hands out ' +
				'a job.count of 0, which vanilla answers with a red "Invalid count: 0, setting to 1").'
		)

	// A guard whose passing state is emptiness cannot tell "clean" from "never looked", so say what was
	// looked at and how much of the seam was actually found.
	if (gameFiles.length === 0) errors.push('no source files were scanned at all — the guard is looking in the wrong place.')

	if (errors.length > 0) {
		console.error(`\n[storage-commit-seam] FAIL — ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		console.error(
			`\n  This guard exists because HD removes vanilla's destination cell reservation, which also shrank ` +
				`every other hauler's job.count. The replacement is a single claim ledger behind ` +
				`StorageCommitments, reached through exactly two Harmony adapters. None of that is visible to the ` +
				`compiler, and HaulersDream.Tests references only HaulersDream.Core, so it can observe the ` +
				`decision RULE and never the Verse arguments the glue passes it. If you restructured the seam ` +
				`deliberately, update this script to match the new shape — do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[storage-commit-seam] PASS — ${gameFiles.length} source files scanned, ` +
			`${CAPACITY_ORACLE} confined to 1 file (${oracleSites} call site(s)), ` +
			`2 adapters pinned, ${commitCalls} allowlisted commit call(s) across ${seenIn.size}/${COMMIT_SITES.length} ` +
			`reviewed file(s), ${fieldWrites} ledger field write(s) confined to 1 file, ` +
			`${SEAM_TRIPWIRE.length} startup bind target(s) verified + disabling, ` +
			`ledger not a per-tick snapshot, possession test intact.`
	)
}

await main()
