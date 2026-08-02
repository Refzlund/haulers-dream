// Static guard: every way Hauler's Dream can start gathering a bill's ingredients into a pawn's INVENTORY
// must consult BillRouteGate first.
//
// The bug this pins (issue #243): three of the four entry points asked BillRouteGate for permission; the
// fourth — the player-ordered "Plan prioritized crafting…" — asked nobody. It offered its order at a bench
// whose "Gather ingredients" switch the player had explicitly turned OFF, and then pre-loaded a whole batch
// of ingredients into inventory there, which is precisely the behaviour that switch exists to stop. Nothing
// in the compiler, the tests or a code review flags a MISSING call: the feature works, it just ignores a
// setting. Only an inventory of the entry points can catch it, so this script keeps that inventory.
//
// It fails the build (exit 1) when:
//   1. A registered entry point stops calling its BillRouteGate member (the #243 regression, exactly).
//   2. A NEW job-creation site appears outside every registered entry point — the file-and-method registry
//      below is then stale, and the new site is very likely ungated. Adding one means gating it AND listing
//      it here; do not "fix" a failure by deleting the entry.
//   3. BillRouteGate stops declaring a member the registry names (a rename would otherwise leave every
//      entry point calling something else while this guard matched a leftover mention in prose).
//   4. BillRouteGate.MayRouteToInventory stops consulting the per-bench switch (CompBenchGather.Allows).
//      Without that, every entry point would still "call BillRouteGate" while the switch governed nothing —
//      the guard would pass and #243 would be back.
//
// Run directly to self-check:  bun scripts/check-bill-route-gate.ts
import { resolve } from 'node:path'
import { readdirSync, statSync } from 'node:fs'
import { repoRoot } from './lib'

const SOURCE_DIR = resolve(repoRoot, 'Source/HaulersDream')
const GATE = resolve(SOURCE_DIR, 'BillRouteGate.cs')

/** A place where HD decides to gather bill ingredients into inventory, and the gate it must consult. */
type EntryPoint = {
	/** Repo-relative path, so a failure message names the file the way a human would. */
	file: string
	/** The declaring class, sliced first so a same-named method in a sibling class cannot stand in for it. */
	cls: string
	/** The method whose BODY must contain the gate call. Body-scoped: a call elsewhere in the class does not count. */
	method: string
	/** The BillRouteGate member this entry point must call. */
	gate: string
	/** What the entry point does, for the failure message. */
	what: string
}

const ENTRY_POINTS: EntryPoint[] = [
	{
		file: 'Source/HaulersDream/Patch_WorkGiver_DoBill_InventoryRoute.cs',
		cls: 'Patch_WorkGiver_DoBill_InventoryRoute',
		method: 'Postfix',
		gate: 'MayRouteToInventory',
		what: 'the automatic one-sweep gather (creates HaulersDream_BillPrepGather)',
	},
	{
		file: 'Source/HaulersDream/Patch_WorkGiver_DoBill_BatchRoute.cs',
		cls: 'Patch_WorkGiver_DoBill_BatchRoute',
		method: 'Postfix',
		gate: 'MayRouteToInventory',
		what: 'the automatic batch conversion (creates HaulersDream_BatchCraft)',
	},
	{
		file: 'Source/HaulersDream/JobDriver_BatchCraft.cs',
		cls: 'JobDriver_BatchCraft',
		method: 'StartBatchCraftSynced',
		gate: 'MayRouteToInventory',
		what: 'the player-ordered batch craft, and the Multiplayer sync entry (creates HaulersDream_BatchCraft)',
	},
	{
		file: 'Source/HaulersDream/FloatMenuOptionProvider_PlanCraft.cs',
		cls: 'FloatMenuOptionProvider_PlanCraft',
		method: 'GetOptions',
		gate: 'MayRouteToInventory',
		what: 'the "Plan prioritized crafting…" float-menu offer, which leads to the batch craft above',
	},
]

/** The HD job defs whose creation means "ingredients are going into a pawn's inventory". */
const GATHER_JOB_DEFS = ['HaulersDream_BatchCraft', 'HaulersDream_BillPrepGather']

const errors: string[] = []

/**
 * Blank out comments so a doc-comment mentioning `class Foo` or `BillRouteGate.MayRouteToInventory` can never
 * satisfy (or derail) a check. Replaces comment characters with spaces rather than deleting them, so every
 * index into the result still lines up with the original file — the discovery leg compares positions.
 */
function blankComments(src: string): string {
	const out = src.split('')
	let i = 0
	while (i < src.length) {
		const c = src[i]
		if (c === '"' || c === "'") {
			// Skip a string/char literal (and a verbatim string, where "" is an escaped quote).
			const verbatim = c === '"' && i > 0 && src[i - 1] === '@'
			i++
			while (i < src.length) {
				if (!verbatim && src[i] === '\\') i += 2
				else if (src[i] === c) {
					if (verbatim && src[i + 1] === c) i += 2
					else break
				} else i++
			}
			i++
			continue
		}
		if (c === '/' && src[i + 1] === '/') {
			while (i < src.length && src[i] !== '\n') out[i++] = ' '
			continue
		}
		if (c === '/' && src[i + 1] === '*') {
			const end = src.indexOf('*/', i + 2)
			const stop = end < 0 ? src.length : end + 2
			for (; i < stop; i++) if (src[i] !== '\n') out[i] = ' '
			continue
		}
		i++
	}
	return out.join('')
}

/** Brace-match from the first `{` at/after `from`; returns the span BETWEEN the braces, or null. */
function braceSpan(src: string, from: number): { start: number; end: number } | null {
	let i = src.indexOf('{', from)
	if (i < 0) return null
	let depth = 0
	const start = i
	for (; i < src.length; i++) {
		if (src[i] === '{') depth++
		else if (src[i] === '}' && --depth === 0) return { start: start + 1, end: i }
	}
	return null
}

function escapeRe(s: string): string {
	return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** The span of a class body, found from its declaration. Null when the class is not in this source. */
function classSpan(src: string, name: string): { start: number; end: number } | null {
	const sig = new RegExp(`\\bclass\\s+${escapeRe(name)}\\b`).exec(src)
	return sig ? braceSpan(src, sig.index) : null
}

/** The span of a method body inside `[from, to)`. `\b<name>\s*\(` cannot match a longer identifier. */
function methodSpan(src: string, name: string, from: number, to: number): { start: number; end: number } | null {
	const re = new RegExp(`\\b${escapeRe(name)}\\s*\\([^)]*\\)\\s*\\{`, 'g')
	re.lastIndex = from
	const sig = re.exec(src)
	if (!sig || sig.index >= to) return null
	return braceSpan(src, sig.index)
}

/** Every .cs file under Source/HaulersDream, repo-relative. */
function sourceFiles(): string[] {
	const out: string[] = []
	;(function walk(dir: string) {
		for (const name of readdirSync(dir)) {
			const full = resolve(dir, name)
			if (statSync(full).isDirectory()) walk(full)
			else if (name.endsWith('.cs')) out.push(full)
		}
	})(SOURCE_DIR)
	return out
}

async function read(path: string, label: string): Promise<string | null> {
	const f = Bun.file(path)
	if (!(await f.exists())) {
		errors.push(`${label} is MISSING (${path}). The bill-route-gate guard cannot verify it.`)
		return null
	}
	return (await f.text()).replace(/\r\n/g, '\n')
}

async function main() {
	// ---- 1 + 2: each registered entry point calls its gate, and owns every job-creation site in its file ----
	// Ranges are collected per file so the discovery leg can ask "is this MakeJob inside a gated method?".
	const gatedSpans = new Map<string, { start: number; end: number }[]>()

	for (const ep of ENTRY_POINTS) {
		const src = await read(resolve(repoRoot, ep.file), `${ep.cls} source`)
		if (!src) continue
		const code = blankComments(src)

		const cls = classSpan(code, ep.cls)
		if (!cls) {
			errors.push(`${ep.cls} not found in ${ep.file}; the gate on ${ep.what} is unverifiable.`)
			continue
		}
		const body = methodSpan(code, ep.method, cls.start, cls.end)
		if (!body) {
			errors.push(`${ep.cls}.${ep.method} not found; the gate on ${ep.what} is unverifiable.`)
			continue
		}
		const method = code.slice(body.start, body.end)

		if (!new RegExp(`\\bBillRouteGate\\.${escapeRe(ep.gate)}\\s*\\(`).test(method)) {
			errors.push(
				`${ep.cls}.${ep.method} no longer calls BillRouteGate.${ep.gate}(...). It is ${ep.what}, so it ` +
					`would gather a bill's ingredients into a pawn's inventory at a bench whose "Gather ingredients" ` +
					`switch the player turned OFF — issue #243, exactly. Restore the gate.`
			)
		}

		const list = gatedSpans.get(ep.file) ?? []
		list.push(body)
		gatedSpans.set(ep.file, list)
	}

	// ---- 2 (continued): discovery — no gather job may be created outside a registered entry point ----
	const makeJob = new RegExp(
		`JobMaker\\.MakeJob\\(\\s*HaulersDreamDefOf\\.(${GATHER_JOB_DEFS.map(escapeRe).join('|')})\\b`,
		'g'
	)
	let creationSites = 0
	for (const full of sourceFiles()) {
		const src = (await Bun.file(full).text()).replace(/\r\n/g, '\n')
		const code = blankComments(src)
		const rel = full.slice(repoRoot.length + 1).replace(/\\/g, '/')
		const spans = gatedSpans.get(rel) ?? []
		makeJob.lastIndex = 0
		let m: RegExpExecArray | null
		while ((m = makeJob.exec(code)) !== null) {
			creationSites++
			if (!spans.some((s) => m!.index >= s.start && m!.index < s.end)) {
				errors.push(
					`${rel} creates a ${m[1]} job outside every registered entry point (offset ${m.index}). Either ` +
						`it is a new UNGATED gather — the #243 bug class — or the registry in this script is stale. ` +
						`Gate it on BillRouteGate and add its (file, class, method) to ENTRY_POINTS; do not remove ` +
						`this check.`
				)
			}
		}
	}
	if (creationSites === 0)
		errors.push(
			`No JobMaker.MakeJob(HaulersDreamDefOf.<gather job>) site found at all. Either the gather jobs were ` +
				`renamed (update GATHER_JOB_DEFS) or the creation spelling changed, and this guard is now blind.`
		)

	// ---- 3 + 4: the gate itself still exists and still reads the per-bench switch ----
	const gate = await read(GATE, 'BillRouteGate.cs')
	if (gate) {
		const code = blankComments(gate)
		for (const member of new Set(ENTRY_POINTS.map((e) => e.gate))) {
			if (!new RegExp(`\\b${escapeRe(member)}\\s*\\(`).test(code))
				errors.push(
					`BillRouteGate no longer declares ${member}(...). Every entry point above names it, so a rename ` +
						`must be applied there too (and here).`
				)
		}
		const span = classSpan(code, 'BillRouteGate')
		const may = span && methodSpan(code, 'MayRouteToInventory', span.start, span.end)
		// An expression-bodied member has no braces of its own, so fall back to the statement text after `=>`.
		const mayText = may
			? code.slice(may.start, may.end)
			: /MayRouteToInventory\s*\([^)]*\)\s*=>([\s\S]*?);/.exec(code)?.[1] ?? ''
		if (!mayText.includes('CompBenchGather.Allows'))
			errors.push(
				`BillRouteGate.MayRouteToInventory no longer consults CompBenchGather.Allows. The per-bench "Gather ` +
					`ingredients" switch would then govern nothing while every entry point still "calls the gate" — ` +
					`this guard would pass and #243 would be back.`
			)
	}

	if (errors.length > 0) {
		console.error(`\n[bill-route-gate] FAIL, ${errors.length} problem(s):\n`)
		for (const e of errors) console.error(`  x ${e}`)
		console.error(
			`\n  This guard exists because the "Plan prioritized crafting…" order gathered ingredients into ` +
				`inventory while consulting no gate at all, so a workbench the player had switched off kept ` +
				`gathering (issue #243). If you intentionally restructured an entry point, update this script to ` +
				`match the new shape. Do not just delete the check.\n`
		)
		process.exit(1)
	}

	console.log(
		`[bill-route-gate] PASS, ${ENTRY_POINTS.length} inventory-gather entry points call BillRouteGate, ` +
			`${creationSites} gather-job creation site(s) all inside them, and MayRouteToInventory still reads the ` +
			`per-bench switch.`
	)
}

await main()
