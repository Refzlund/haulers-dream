// Static guard against settings-default DRIFT.
//
// HaulersDreamSettings.cs declares each persisted setting THREE times that must agree:
//   (1) the field-initializer default     ->  public bool foo = true;
//   (2) the Scribe_Values.Look default    ->  Scribe_Values.Look(ref foo, "foo", true);
//   (3) the ResetToDefaults() assignment  ->  foo = true;
// A typo in any copy is a silent save / load / reset bug. This script parses the file with
// pragmatic regexes (the three regions are contiguous slices) and FAILS (exit 1) on any
// missing field, default mismatch, or Scribe key that differs from the field name.
//
// Collection / deep fields (List / Dictionary / StorageBuildingFilter, via Scribe_Collections /
// Scribe_Deep) are handled separately: they reset to a fresh instance, so we only require they
// appear in ResetToDefaults and do NOT string-compare their "default" expression.
//
// Run directly to self-check:  bun scripts/check-settings-drift.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const SETTINGS_PATH = resolve(repoRoot, 'Source/HaulersDream/HaulersDreamSettings.cs')
// The settings GUI was split into a `partial class` file; the soft "referenced in UI" check must
// scan it too (the DrawXxxTab / DoWindowContents bodies live here now, not in the main file).
const SETTINGS_WINDOW_PATH = resolve(repoRoot, 'Source/HaulersDream/HaulersDreamSettings.Window.cs')
// The third file of the `partial class HaulersDreamSettings`. Not part of the drift triple (no fields / no
// ExposeData live here), but the ExposeData side-effect guard resolves helper bodies across ALL the partials —
// a migration helper is just as dangerous sitting in a sibling file.
const SETTINGS_PROFILES_PATH = resolve(repoRoot, 'Source/HaulersDream/SettingsProfiles.cs')

// Scribe families that handle collections / deep objects (reset to a fresh instance, not a
// string-compared scalar default). A serialized field surfacing through one of these is a
// "collection field": verified present in ResetToDefaults, but its default is not compared.
const COLLECTION_SCRIBE = ['Scribe_Collections.Look', 'Scribe_Deep.Look']

interface FieldDecl {
	name: string
	type: string
	defaultExpr: string | null // null = no initializer (e.g. a private cache with no `= ...`)
	nonSerialized: boolean
	profileMeta: boolean // [ProfileMeta]: persisted plumbing/identity, exempt from the field==Scribe==Reset triple
}

/** Slice the body of a method/region between a header marker and its matching brace depth. */
function sliceRegion(src: string, headerRegex: RegExp): string {
	const m = headerRegex.exec(src)
	if (!m) throw new Error(`Could not locate region: ${headerRegex}`)
	// Find the opening brace after the header, then walk to its matching close.
	let i = src.indexOf('{', m.index + m[0].length)
	if (i < 0) throw new Error(`No opening brace after region: ${headerRegex}`)
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
	throw new Error(`Unbalanced braces in region: ${headerRegex}`)
}

/**
 * The field-declaration region = everything from class open up to ExposeData. Field decls are
 * one-per-line `public <type> <name> = <expr>;` (or a `private`/cache decl). We parse line by line
 * so trailing `// comment` and `[System.NonSerialized]` attributes are handled cleanly.
 */
function parseFieldDecls(classBody: string): FieldDecl[] {
	// Everything before the ExposeData method is the field-decl + helper region; methods/properties
	// after the fields are skipped by the per-line decl regex (they don't match `<type> <name> = ...;`
	// or `<type> <name>;` at field shape). Cut at ExposeData to avoid scanning method bodies.
	const exposeIdx = classBody.indexOf('public override void ExposeData()')
	const region = exposeIdx >= 0 ? classBody.slice(0, exposeIdx) : classBody

	const fields: FieldDecl[] = []
	// Match a field declaration line. Captures: access, modifiers, type, name, optional initializer.
	// `type` allows generics/namespaces (List<string>, Dictionary<string, RouteDialogPrefs>).
	// A leading same-line attribute (e.g. `[System.NonSerialized] public ...`) is tolerated.
	// We deliberately exclude lines that look like method decls (a `(` before `;`).
	const declRe =
		/^\s*(?:\[[^\]]*\]\s*)*(public|private|internal|protected)\s+((?:static\s+|readonly\s+)*)([A-Za-z_][\w.]*(?:<[^=;]*?>)?)\s+([A-Za-z_]\w*)\s*(?:=\s*([^;]+?))?\s*;\s*(?:\/\/.*)?$/

	const lines = region.split('\n')
	for (let i = 0; i < lines.length; i++) {
		const line = lines[i]
		const m = declRe.exec(line)
		if (!m) continue
		const [, , , type, name, init] = m
		// A method (has `(...)`) or property never matches declRe (the `(`/`{` breaks the `= expr ;`
		// shape), so any match here is a real field. Determine NonSerialized from this line or a
		// preceding attribute on the same line / the line above.
		const nonSerialized =
			line.includes('[System.NonSerialized]') ||
			(i > 0 && lines[i - 1].trim().startsWith('[System.NonSerialized]'))
		const profileMeta =
			line.includes('[ProfileMeta]') ||
			(i > 0 && lines[i - 1].trim().startsWith('[ProfileMeta]'))
		fields.push({
			name,
			type,
			defaultExpr: init !== undefined ? normalize(init) : null,
			nonSerialized,
			profileMeta,
		})
	}
	return fields
}

/** Parse `Scribe_*.Look(ref <name>, "<key>"[, <default>])` calls from the ExposeData body. */
interface ScribeEntry {
	name: string
	key: string
	defaultExpr: string | null // null for collection/deep families (no scalar default)
	isCollection: boolean
}

function parseScribe(exposeBody: string): ScribeEntry[] {
	const entries: ScribeEntry[] = []
	// Scribe_Values.Look(ref name, "key", default)  — default optional but always present here.
	const valueRe = /Scribe_Values\.Look\(\s*ref\s+([A-Za-z_]\w*)\s*,\s*"([^"]*)"\s*(?:,\s*([^;]+?))?\s*\)\s*;/g
	for (const m of exposeBody.matchAll(valueRe)) {
		entries.push({
			name: m[1],
			key: m[2],
			defaultExpr: m[3] !== undefined ? normalize(m[3]) : null,
			isCollection: false,
		})
	}
	// Scribe_Collections.Look(ref name, "key", ...) and Scribe_Deep.Look(ref name, "key", ...)
	for (const family of COLLECTION_SCRIBE) {
		const re = new RegExp(
			`${family.replace('.', '\\.')}\\(\\s*ref\\s+([A-Za-z_]\\w*)\\s*,\\s*"([^"]*)"`,
			'g'
		)
		for (const m of exposeBody.matchAll(re)) {
			entries.push({ name: m[1], key: m[2], defaultExpr: null, isCollection: true })
		}
	}
	return entries
}

/** Parse `name = <expr>;` assignments from the ResetToDefaults body. Returns first assignment per name. */
function parseReset(resetBody: string): Map<string, string> {
	const out = new Map<string, string>()
	const re = /^\s*([A-Za-z_]\w*)\s*=\s*([^;]+?)\s*;\s*(?:\/\/.*)?$/gm
	for (const m of resetBody.matchAll(re)) {
		if (!out.has(m[1])) out.set(m[1], normalize(m[2]))
	}
	return out
}

/** Collapse whitespace so `1.0f` / ` 1.0f ` / multi-space expressions compare equal. */
function normalize(expr: string): string {
	return expr.replace(/\s+/g, ' ').trim()
}

// ---- ExposeData side-effect guard (issue #238) -----------------------------------------------------------
// A setting must be written by exactly one thing on load: its own Scribe_*.Look. Anything ELSE in ExposeData
// that assigns a setting field runs AFTER Look has already put the saved value there, so it can silently
// overwrite what the player chose — exactly how #238 shipped (a stamp-guarded migration re-ran every launch
// and reset nine yield settings, with every other build guard green).
//
// BE HONEST ABOUT WHAT THIS BUYS: it would NOT have caught #238 on its own. MigrateLegacyYieldSettings assigns
// nine setting fields, so the broken build needed the same allowlist entry the fixed one has. Its value is that
// no NEW such write can appear without someone writing down why it is safe — and that written reason is what a
// reviewer checks. Note the limit precisely: an allowlisted helper is skipped WHOLE (see ALLOWED_EXPOSE_MUTATORS
// below), so one that already has an entry can grow to write more setting fields without tripping anything here.
// Reviewing a change to such a helper's body is a human job. Actual detection is the runtime tripwire's
// (SettingsProfiles.VerifySerializationIntegrity, which round-trips a snapshot held OFF its defaults).
//
// HEURISTIC SCOPE — it reads C# with regexes, so it sees:
//   yes: `foo = …`, compound `foo += …`, and `ref foo` passed to anything that is not a Scribe_*.Look
//   no:  in-place mutation of a collection setting (`itemUnloadRules.Add(x)`, as MigrateLegacyKeepDefNames does)
//   no:  a write behind an alias/reflection, or a helper reached through more than one call hop
// String literals and comments are stripped first, so prose containing `foo = true` cannot fail the build.

/** Assignments to a setting field that ExposeData is allowed to make DIRECTLY (each needs a reason here). */
const ALLOWED_EXPOSE_MUTATIONS = new Set([
	// A hand-edited config with k >= 22 makes the Held-Karp route solver allocate gigabytes (2^k*k) and
	// overflow at k >= 31, so the loaded value is clamped to [1,16]. It only ever narrows an out-of-range
	// value the UI cannot produce; an in-range player choice passes through untouched.
	'routeOrderExactMax',
])

/** C# keywords that read like a call (`if (…)`) — never resolve these to a method body. */
const CALL_LIKE_KEYWORDS = new Set([
	'if', 'else', 'for', 'foreach', 'while', 'do', 'switch', 'case', 'catch', 'lock', 'using', 'fixed',
	'return', 'throw', 'typeof', 'sizeof', 'nameof', 'default', 'checked', 'unchecked', 'when', 'new',
])

/** Helpers ExposeData may call that assign setting fields (each needs a reason here). */
const ALLOWED_EXPOSE_MUTATORS = new Set([
	// One-way pre-1.1.x "never unload" list -> itemUnloadRules. Early-returns when the legacy list is absent,
	// so it is a no-op for every config that does not actually carry the legacy data.
	'MigrateLegacyKeepDefNames',
	// One-way pre-#79 per-work bools + global pickupMode -> the 9 yieldX values. Gated on
	// SettingsSchemaPolicy.ShouldMigrateLegacyYields, which requires a legacy node to actually be PRESENT in
	// the node being loaded (issue #238 — a stamp alone cannot tell "old config" from "never stamped").
	'MigrateLegacyYieldSettings',
])

/** Blank out comments and string literals, preserving line count so "the line above" still lines up. */
function stripNoise(body: string): string {
	return body
		.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
		.replace(/"(?:[^"\\\n]|\\.)*"/g, '""')
		.replace(/\/\/[^\n]*/g, '')
}

/** The `if (x == null) x = new …;` idiom — same line or the line above, and about THIS field. */
function isNullGuarded(name: string, line: string, prevLine: string): boolean {
	const re = new RegExp(`\\b${name}\\s*==\\s*null`)
	return re.test(line) || re.test(prevLine)
}

/**
 * Names of setting fields written somewhere in `body`, ignoring the null-guard idiom
 * (`if (x == null) x = new ...;`) which only fills in an ABSENT node rather than overwriting a loaded one.
 */
function mutatedSettingFields(body: string, settingNames: Set<string>): string[] {
	const hits = new Set<string>()
	// `name =` and compound `name += / |= / <<=` …, but never `==` / `=>` (and never `!=` / `<=` / `>=`, whose
	// operator char is outside the compound class so the `=` can't be reached). The lookbehind drops member
	// access, so `LookMode.Value` / `Mathf.Clamp` never register.
	const assignRe = /(?<![\w.])([A-Za-z_]\w*)\s*(?:[+\-*/%&|^]|<<|>>)?=(?![=>])/g
	// A field handed to a helper BY REF is rewritten just as surely as by an assignment. Scribe_*.Look is the one
	// legitimate by-ref writer — that is the whole point of the guard.
	const refRe = /(?<![\w.])ref\s+([A-Za-z_]\w*)/g
	const lines = stripNoise(body).split('\n')
	for (let i = 0; i < lines.length; i++) {
		const line = lines[i]
		const prev = i > 0 ? lines[i - 1] : ''
		for (const m of line.matchAll(assignRe)) {
			if (!settingNames.has(m[1])) continue
			if (isNullGuarded(m[1], line, prev)) continue
			hits.add(m[1])
		}
		for (const m of line.matchAll(refRe)) {
			if (!settingNames.has(m[1])) continue
			if (line.slice(0, m.index).includes('Scribe_')) continue
			hits.add(m[1])
		}
	}
	return [...hits]
}

/**
 * The body of the method named `name`, or null when it isn't declared in `src`.
 * Requires a return type (at least one token + whitespace) immediately before the name, which is what tells a
 * DECLARATION apart from a statement-position CALL (`Helper();`) — the access modifier is optional, since a
 * method with none is implicitly private and would otherwise be skipped.
 */
function methodBody(src: string, name: string): string | null {
	const declRe = new RegExp(
		`^[ \\t]*[A-Za-z_][\\w.<>\\[\\],?]*(?:\\s+[A-Za-z_][\\w.<>\\[\\],?]*)*\\s+${name}\\s*\\(`,
		'm'
	)
	if (!declRe.test(src)) return null
	try {
		return sliceRegion(src, declRe)
	} catch {
		return null
	}
}

/** Is this field type a collection / deep object (reset to a fresh instance, not a scalar)? */
function isCollectionType(type: string): boolean {
	return (
		type.startsWith('List<') ||
		type.startsWith('Dictionary<') ||
		type.startsWith('HashSet<') ||
		type === 'StorageBuildingFilter'
	)
}

async function main() {
	// Normalize CRLF -> LF so `$`-anchored line regexes (the trailing-comment branch in particular)
	// aren't defeated by a lingering \r at end of line on Windows checkouts.
	const src = (await Bun.file(SETTINGS_PATH).text()).replace(/\r\n/g, '\n')
	// The GUI lives in the `partial class` window file; concatenate it for the UI-reference scan below.
	const winSrc = (await Bun.file(SETTINGS_WINDOW_PATH).text()).replace(/\r\n/g, '\n')
	const profilesSrc = (await Bun.file(SETTINGS_PROFILES_PATH).text()).replace(/\r\n/g, '\n')
	// Every file of the partial class, for resolving a helper ExposeData calls wherever it happens to live.
	const partialSrc = `${src}\n${winSrc}\n${profilesSrc}`

	const classBody = sliceRegion(src, /public (?:partial )?class HaulersDreamSettings\s*:\s*ModSettings/)
	const exposeBody = sliceRegion(src, /public override void ExposeData\(\)/)
	const resetBody = sliceRegion(src, /public void ResetToDefaults\(\)/)

	const fields = parseFieldDecls(classBody)
	const scribe = parseScribe(exposeBody)
	const reset = parseReset(resetBody)

	const scribeByName = new Map<string, ScribeEntry>()
	const dupScribe: string[] = []
	for (const e of scribe) {
		if (scribeByName.has(e.name)) dupScribe.push(e.name)
		else scribeByName.set(e.name, e)
	}

	// UI region for the soft "referenced in UI" check. The settings GUI is the whole partial window file
	// (HaulersDreamSettings.Window.cs) — a 3-pane window with per-category Draw* methods — so scan all of
	// it rather than slicing specific method bodies (which would break whenever the UI is restructured).
	const uiBodies = winSrc

	const errors: string[] = []
	const warnings: string[] = []
	let checkedScalar = 0
	let checkedCollection = 0

	// Profile-management metadata ([ProfileMeta]) is persisted but is NOT a tunable setting: the saved-profile
	// list, active-profile name, reporter identity, and per-install notification cursor are user/plumbing data
	// that ResetToDefaults must NEVER wipe, so they're exempt from the field==Scribe==Reset triple. The exemption
	// is driven by the [ProfileMeta] attribute on the field (so a new such field auto-exempts); a small explicit
	// set covers any meta field that predates / lacks the attribute.
	const META_FIELDS = new Set([
		'savedProfiles',
		'activeProfileName',
		'reporterId',
		'settingsSchemaVersion',
		...fields.filter((f) => f.profileMeta).map((f) => f.name),
	])

	// A serialized field = a field decl that is NOT [System.NonSerialized] AND has an initializer.
	// (A `private` cache without `= ...` and no Scribe entry is not a setting; skip it.)
	const serializedFields = fields.filter((f) => !f.nonSerialized && !META_FIELDS.has(f.name))

	for (const f of serializedFields) {
		const sc = scribeByName.get(f.name)
		const isColl = isCollectionType(f.type)

		// Fields with no initializer and not serialized through Scribe are private caches (e.g. ruleMap
		// would be NonSerialized; this catches any other initializer-less private). Skip if there is no
		// Scribe entry AND no initializer — it isn't a persisted setting.
		if (f.defaultExpr === null && !sc) continue

		if (!sc) {
			errors.push(
				`Field "${f.name}" has a field-init default (${f.defaultExpr}) but NO Scribe_*.Look entry in ExposeData.`
			)
			continue
		}

		// Key string must equal the field name.
		if (sc.key !== f.name) {
			errors.push(
				`Scribe key mismatch for "${f.name}": Scribe_*.Look(ref ${f.name}, "${sc.key}", ...) — key "${sc.key}" != field name "${f.name}".`
			)
		}

		if (isColl || sc.isCollection) {
			// Collection / deep: verify it resets, but do NOT string-compare a default.
			if (!reset.has(f.name)) {
				errors.push(
					`Collection field "${f.name}" (${f.type}) is serialized via ${
						sc.isCollection ? 'Scribe_Collections/Deep' : 'Scribe'
					} but is MISSING from ResetToDefaults().`
				)
			}
			checkedCollection++
		} else {
			// Scalar / enum / numeric / bool / string: all three defaults must agree.
			if (f.defaultExpr === null) {
				errors.push(`Serialized scalar field "${f.name}" has no field-initializer default.`)
			}
			if (sc.defaultExpr === null) {
				errors.push(
					`Scribe_Values.Look for "${f.name}" has no default argument (must pass the field-init default ${f.defaultExpr}).`
				)
			}
			const resetExpr = reset.get(f.name)
			if (resetExpr === undefined) {
				errors.push(`Serialized scalar field "${f.name}" is MISSING from ResetToDefaults().`)
			}

			// Compare the three default expressions textually (after whitespace normalization).
			const fi = f.defaultExpr
			const sd = sc.defaultExpr
			if (fi !== null && sd !== null && fi !== sd) {
				errors.push(
					`Default MISMATCH for "${f.name}": field-init = "${fi}" but Scribe default = "${sd}".`
				)
			}
			if (fi !== null && resetExpr !== undefined && fi !== resetExpr) {
				errors.push(
					`Default MISMATCH for "${f.name}": field-init = "${fi}" but ResetToDefaults = "${resetExpr}".`
				)
			}
			checkedScalar++
		}

		// Soft check: referenced in some UI control (ref <name>, or a bare <name> token in a tab body).
		const refRe = new RegExp(`\\bref\\s+${f.name}\\b|\\b${f.name}\\b`)
		if (!refRe.test(uiBodies)) {
			warnings.push(`Field "${f.name}" is serialized but not referenced in any DrawXxxTab/DoWindowContents UI control.`)
		}
	}

	// Reverse check: a Scribe_Values entry whose field is NonSerialized or absent (stale Scribe line).
	const fieldNames = new Set(fields.map((f) => f.name))
	const nonSerNames = new Set(fields.filter((f) => f.nonSerialized).map((f) => f.name))
	for (const e of scribe) {
		if (!fieldNames.has(e.name)) {
			errors.push(`Scribe_*.Look references "${e.name}" but no such field exists.`)
		} else if (nonSerNames.has(e.name) && !e.isCollection) {
			// A NonSerialized scalar wrongly run through Scribe_Values is a real bug. But a NonSerialized
			// field read once via Scribe_Collections/Deep is the legacy one-way migration idiom
			// (e.g. keepDefNames) — expected, so it's downgraded to a warning, not an error.
			errors.push(`Scribe_Values serializes "${e.name}" but the field is [System.NonSerialized].`)
		} else if (nonSerNames.has(e.name) && e.isCollection) {
			warnings.push(
				`NonSerialized field "${e.name}" is read via ${
					COLLECTION_SCRIBE.find((f) => exposeBody.includes(`${f}(ref ${e.name}`)) ?? 'Scribe'
				} (legacy one-way migration — expected, not persisted).`
			)
		}
	}
	for (const n of dupScribe) {
		errors.push(`Field "${n}" has more than one Scribe_*.Look entry in ExposeData (duplicate key).`)
	}

	// Reverse check: a ResetToDefaults assignment to a name that isn't a serialized field.
	// (Cache invalidations like `ruleMap = null;` ARE expected — those are NonSerialized fields, allowed.)
	for (const [name] of reset) {
		if (!fieldNames.has(name)) {
			warnings.push(`ResetToDefaults assigns "${name}" which is not a declared field.`)
		}
	}

	// ---- ExposeData side-effect guard (issue #238): nothing but Scribe_*.Look may write a setting on load ----
	const settingFieldNames = new Set(
		serializedFields.filter((f) => scribeByName.has(f.name)).map((f) => f.name)
	)
	const mutationNote =
		'A load-time side effect silently overwrites the value that was just loaded — see issue #238. If it is '
		+ 'deliberate, add it to the allowlist with a comment explaining the guard that makes it safe.'

	for (const name of mutatedSettingFields(exposeBody, settingFieldNames)) {
		if (ALLOWED_EXPOSE_MUTATIONS.has(name)) continue
		errors.push(`ExposeData MUTATES setting "${name}" outside a Scribe_*.Look. ${mutationNote}`)
	}

	// ...and the same for every helper ExposeData calls — that is where the #238 migration hid. Any call shape
	// counts (arguments, or nested inside a condition: `Migrate(loadedVersion)` is exactly as dangerous as a
	// bare `Migrate();`), and the body is resolved across ALL THREE files of the partial class, since a helper
	// is no safer for sitting in a sibling file. A name that resolves to no declaration is simply skipped, so
	// over-matching here costs nothing.
	const exposeCalls = stripNoise(exposeBody)
	const seenHelpers = new Set<string>()
	for (const m of exposeCalls.matchAll(/(?<![\w.])([A-Za-z_]\w*)\s*\(/g)) {
		const helper = m[1]
		if (CALL_LIKE_KEYWORDS.has(helper)) continue // `if (`, `foreach (`, `switch (` … not a call
		if (ALLOWED_EXPOSE_MUTATORS.has(helper)) continue
		if (exposeCalls.slice(0, m.index).endsWith('new ')) continue // `new StorageBuildingFilter()` is a ctor
		if (seenHelpers.has(helper)) continue
		seenHelpers.add(helper)
		const body = methodBody(partialSrc, helper)
		if (body === null) continue // not declared here (a Verse/static call, not a settings helper)
		for (const name of mutatedSettingFields(body, settingFieldNames)) {
			errors.push(
				`ExposeData calls ${helper}(), which MUTATES setting "${name}" outside a Scribe_*.Look. ${mutationNote}`
			)
		}
	}

	// The #238 gate only lets MigrateLegacyYieldSettings run when a legacy node is actually PRESENT, and it asks
	// LegacyYieldNodeLabels which nodes those are. If the migration reads a label the probe doesn't list, that
	// config never migrates; if the probe lists one the migration ignores, a modern config can migrate spuriously.
	// Keep the two lists identical.
	const labelsBlock = /string\[\] LegacyYieldNodeLabels\s*=\s*\{([^}]*)\}/.exec(partialSrc)
	const migrateBody = methodBody(partialSrc, 'MigrateLegacyYieldSettings')
	if (!labelsBlock) {
		errors.push('LegacyYieldNodeLabels (the pre-#79 legacy-node probe list) is missing — see issue #238.')
	} else if (migrateBody !== null) {
		const probed = new Set([...labelsBlock[1].matchAll(/"([^"]+)"/g)].map((m) => m[1]))
		const read = new Set(
			[...migrateBody.matchAll(/Scribe_Values\.Look\(\s*ref\s+\w+\s*,\s*"([^"]+)"/g)].map((m) => m[1])
		)
		const missing = [...read].filter((l) => !probed.has(l))
		const extra = [...probed].filter((l) => !read.has(l))
		if (missing.length > 0) {
			errors.push(
				`MigrateLegacyYieldSettings reads legacy node(s) ${missing.map((l) => `"${l}"`).join(', ')} that `
				+ 'LegacyYieldNodeLabels does not list, so a config carrying only those never migrates (issue #238).'
			)
		}
		if (extra.length > 0) {
			errors.push(
				`LegacyYieldNodeLabels lists ${extra.map((l) => `"${l}"`).join(', ')}, which `
				+ 'MigrateLegacyYieldSettings never reads — the presence probe would fire for nothing (issue #238).'
			)
		}
	}

	// ---- report ----
	const total = checkedScalar + checkedCollection
	if (errors.length > 0) {
		console.error(`\n[settings-drift] FAIL — ${errors.length} problem(s) across ${total} serialized fields:\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		if (warnings.length > 0) {
			console.error(`\n  (${warnings.length} warning(s):)`)
			for (const w of warnings) console.error(`    ! ${w}`)
		}
		console.error('')
		process.exit(1)
	}

	console.log(
		`[settings-drift] PASS — ${total} serialized fields checked (${checkedScalar} scalar/enum, ${checkedCollection} collection), 0 drift.`
	)
	if (warnings.length > 0) {
		console.log(`[settings-drift] ${warnings.length} warning(s) (non-fatal):`)
		for (const w of warnings) console.log(`  ! ${w}`)
	}
}

await main()
