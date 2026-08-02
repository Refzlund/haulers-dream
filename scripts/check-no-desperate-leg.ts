// Static guard against re-introducing vanilla's "desperate" store-cell search (issues #231 and #76).
//
// StoreUtility.TryFindStoreCellNearColonyDesperate looks like the obvious helper for "the unload has nowhere to
// put this", and Hauler's Dream used to call it. Its third leg is the trap:
// RCellFinder.TryFindRandomSpotJustOutsideColony has NO home-area test whatsoever — its FinalValidator wants an
// OUTDOOR district that TOUCHES THE MAP EDGE, and its last pass rolls a random cell over the whole map. Vanilla
// reaches it only behind the rare event-driven UnloadEverything flag, once per job; HD reached it per tagged
// stack, in a loop, for every hauling pawn, re-rolling a fresh cell from each new position. That is issue #231
// ("pawns place items completely outside of the Home area"). The same FinalValidator also dereferences
// c.GetDistrict(map).Room on random cells, which NREs for a degenerate colony — issue #76.
//
// Both bugs are invisible to the compiler and to the unit tests: the replacement (a verbatim re-implementation of
// only the HOME-CONSTRAINED radial leg) has the same shape at the call site, so a future edit that "simplifies"
// it back to the vanilla helper compiles clean, passes every test, and silently regresses both issues at once.
// So we pin it here. This script fails the build (exit 1) if:
//   1. either banned symbol appears in real CODE anywhere in Source/HaulersDream/ (comments and doc-comments are
//      stripped first — the fix's own comments discuss both by name at length, which is intended);
//   2. the home-constrained replacement (InventorySurplus.TryFindDesperateHomeAreaCell) has gone missing;
//   3. the unload driver stops dispatching through UnloadFallbackPolicy.Choose — the enum has no
//      "haul it outside the home area" member, so that dispatch is what makes the bug inexpressible;
//   4. the UnloadPlacement enum grows a member beyond the four sanctioned outcomes.
//
// KNOWN GAP: the code/comment scanner treats an interpolated string's `{...}` holes as string content, so a
// banned call buried inside one would not be seen. Nobody writes that; the check is cheap insurance, not a sandbox.
//
// Run directly to self-check:  bun scripts/check-no-desperate-leg.ts
import { readdirSync, statSync } from 'node:fs'
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const GAME_SRC = resolve(repoRoot, 'Source/HaulersDream')
const SURPLUS_PATH = resolve(repoRoot, 'Source/HaulersDream/InventorySurplus.cs')
const DRIVER_PATH = resolve(repoRoot, 'Source/HaulersDream/JobDriver_UnloadHauledInventory.cs')
const POLICY_PATH = resolve(repoRoot, 'Source/HaulersDream.Core/UnloadFallbackPolicy.cs')

/**
 * The vanilla symbols the unload must never reach for again, with the reason each is banned so a future reader
 * hitting this failure knows what the alternative is rather than just deleting the check.
 */
const BANNED = [
	{
		symbol: 'TryFindStoreCellNearColonyDesperate',
		why: 'its third leg picks a random map-edge cell with no Home-area test (#231) and NREs on a degenerate colony (#76). Use InventorySurplus.TryFindDesperateHomeAreaCell, which reproduces ONLY the home-constrained radial leg.'
	},
	{
		symbol: 'RCellFinder',
		why: 'RCellFinder.TryFindRandomSpotJustOutsideColony is the un-home-constrained leg behind #231; nothing in the unload path should need any RCellFinder helper. If a genuinely different RCellFinder API is wanted, narrow this ban rather than removing it.'
	}
]

/** The complete set of unload outcomes. A fifth member is how "haul it outside the home area" would come back. */
const PLACEMENTS = ['Deliver', 'KeepInInventory', 'PlaceOnNearbyHomeCell', 'DropAtFeet']

/** Every .cs file under `dir`, skipping build output (obj/ and bin/ hold generated + copied sources). */
function csFilesUnder(dir: string): string[] {
	const out: string[] = []
	for (const entry of readdirSync(dir)) {
		if (entry === 'obj' || entry === 'bin') continue
		const full = resolve(dir, entry)
		if (statSync(full).isDirectory()) out.push(...csFilesUnder(full))
		else if (entry.endsWith('.cs')) out.push(full)
	}
	return out
}

/**
 * Strip C# comments and string/char literal CONTENT, preserving line structure so reported line numbers still
 * point at the real source line. Removing comments is the whole point: the #231 fix documents both banned
 * symbols by name in prose, and a guard that tripped on its own explanation would be deleted within a week.
 */
function codeOnly(src: string): string {
	let out = ''
	let i = 0
	const keepNewlines = (s: string) => s.replace(/[^\n]/g, ' ')
	while (i < src.length) {
		const two = src.slice(i, i + 2)
		if (two === '//') {
			const end = src.indexOf('\n', i)
			const stop = end < 0 ? src.length : end
			out += keepNewlines(src.slice(i, stop))
			i = stop
		} else if (two === '/*') {
			const end = src.indexOf('*/', i + 2)
			const stop = end < 0 ? src.length : end + 2
			out += keepNewlines(src.slice(i, stop))
			i = stop
		} else if (src[i] === '@' && src[i + 1] === '"') {
			// Verbatim string: ends at a lone `"` ("" is an escaped quote).
			let j = i + 2
			while (j < src.length) {
				if (src[j] === '"') {
					if (src[j + 1] === '"') j += 2
					else { j++; break }
				} else j++
			}
			out += keepNewlines(src.slice(i, j))
			i = j
		} else if (src[i] === '"' || src[i] === "'") {
			const quote = src[i]
			let j = i + 1
			while (j < src.length) {
				if (src[j] === '\\') j += 2
				else if (src[j] === quote) { j++; break }
				else if (src[j] === '\n') break // unterminated; don't run away
				else j++
			}
			out += keepNewlines(src.slice(i, j))
			i = j
		} else {
			out += src[i]
			i++
		}
	}
	return out
}

async function main(): Promise<void> {
	const errors: string[] = []
	const files = csFilesUnder(GAME_SRC)
	let scanned = 0

	for (const file of files) {
		const code = codeOnly(await Bun.file(file).text())
		scanned++
		const lines = code.split('\n')
		for (const { symbol, why } of BANNED) {
			for (let n = 0; n < lines.length; n++) {
				if (!lines[n].includes(symbol)) continue
				const rel = file.slice(repoRoot.length + 1).replace(/\\/g, '/')
				errors.push(`${rel}:${n + 1} calls banned symbol "${symbol}" — ${why}`)
			}
		}
	}

	// Positive pins: the guard must also fail if the REPLACEMENT is what disappears, otherwise deleting the fix
	// wholesale would pass a ban-only check.
	const surplus = await Bun.file(SURPLUS_PATH).text()
	if (!surplus.includes('TryFindDesperateHomeAreaCell'))
		errors.push('InventorySurplus.cs no longer defines TryFindDesperateHomeAreaCell — the home-constrained replacement for the banned desperate search is gone.')

	const driver = codeOnly(await Bun.file(DRIVER_PATH).text())
	if (!driver.includes('UnloadFallbackPolicy.Choose('))
		errors.push('JobDriver_UnloadHauledInventory.cs no longer dispatches through UnloadFallbackPolicy.Choose — the unload placement decision must stay expressed as UnloadPlacement, which has no "outside the home area" outcome.')
	if (!driver.includes('InventorySurplus.TryFindDesperateHomeAreaCell'))
		errors.push('JobDriver_UnloadHauledInventory.cs no longer calls InventorySurplus.TryFindDesperateHomeAreaCell — the no-storage fallback must stay home-constrained.')

	const policy = await Bun.file(POLICY_PATH).text()
	const enumBody = /enum\s+UnloadPlacement\s*\{([\s\S]*?)\}/.exec(policy)
	if (!enumBody) {
		errors.push('UnloadFallbackPolicy.cs no longer declares the UnloadPlacement enum.')
	} else {
		const members = codeOnly(enumBody[1])
			.split(',')
			.map((m) => m.trim())
			.filter((m) => m.length > 0)
		for (const m of members)
			if (!PLACEMENTS.includes(m))
				errors.push(`UnloadPlacement has an unexpected member "${m}". A new unload outcome must be reviewed against #231 — the enum deliberately cannot express "haul it outside the home area". If the member is legitimate, add it to PLACEMENTS here.`)
		for (const p of PLACEMENTS)
			if (!members.includes(p)) errors.push(`UnloadPlacement is missing the "${p}" outcome.`)
	}

	if (errors.length > 0) {
		console.error(`\n[no-desperate-leg] FAIL — ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		console.error(
			`\n  This guard exists because the vanilla "desperate" store-cell search scatters items far outside ` +
				`the Home area (#231) and NREs on a degenerate colony (#76), while looking like the obvious helper ` +
				`for "nowhere to put this". If you intentionally restructured the unload fallback, update this ` +
				`script to match the new shape — do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[no-desperate-leg] PASS — ${scanned} source files free of ${BANNED.length} banned vanilla symbols, ` +
			`home-constrained fallback + UnloadPlacement dispatch (${PLACEMENTS.length} outcomes) intact.`
	)
}

await main()
