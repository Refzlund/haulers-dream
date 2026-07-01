// Static guard against profile-codec COVERAGE gaps.
//
// The settings-profile system (SettingsProfiles.cs) dispatches every setting by type across FOUR sites that
// must all agree for a serialized REFERENCE-type field (a List / Dictionary / a class like StorageBuildingFilter):
//   (1) CloneValue        -> case <Type>:            deep-copy for capture (a missing case THROWS)
//   (2) ValuesEqual       -> case <Type>:            content compare for the dirty-check (a missing case falls
//                                                     through to reference Equals -> a pristine config reads
//                                                     "Custom (unsaved)" forever)
//   (3) EncodeFieldValue  -> case <Type>:            copy/paste token encode (a missing case emits garbage)
//   (4) ParseFieldValue   -> typeof(<Type>):         copy/paste token decode (a missing case throws)
//
// A reference-type setting added to HaulersDreamSettings without all four is a silent "always Custom" bug — the
// exact regression that shipped when the remembered sow / remove-floor route dictionaries were added. This script
// enumerates the serialized reference-type (collection) fields and FAILS the build (exit 1) if any of the four
// dispatch sites is missing that field's type. Runtime backstop: HaulersDreamSettings.VerifyProfileIntegrity().
//
// Run directly to self-check:  bun scripts/check-profile-codec.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const SETTINGS_PATH = resolve(repoRoot, 'Source/HaulersDream/HaulersDreamSettings.cs')
const PROFILES_PATH = resolve(repoRoot, 'Source/HaulersDream/SettingsProfiles.cs')

/** Collapse whitespace so `Dictionary<string,  RouteDialogPrefs>` matches `Dictionary<string, RouteDialogPrefs>`. */
function normalize(s: string): string {
	return s.replace(/\s+/g, ' ').trim()
}

/** Slice the `{ ... }` body of the first method/region whose header matches, by brace matching. */
function sliceRegion(src: string, headerRegex: RegExp): string {
	const m = headerRegex.exec(src)
	if (!m) throw new Error(`Could not locate region: ${headerRegex}`)
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

/** A serialized reference-type ("collection") field type needs an explicit case in all four dispatch sites. */
function isCollectionType(type: string): boolean {
	return (
		type.startsWith('List<') ||
		type.startsWith('Dictionary<') ||
		type.startsWith('HashSet<') ||
		type === 'StorageBuildingFilter'
	)
}

async function main() {
	const settingsSrc = (await Bun.file(SETTINGS_PATH).text()).replace(/\r\n/g, '\n')
	const profilesSrc = (await Bun.file(PROFILES_PATH).text()).replace(/\r\n/g, '\n')

	// --- collect the serialized reference-type field types (non-NonSerialized, non-ProfileMeta) ---
	const classBody = sliceRegion(settingsSrc, /public (?:partial )?class HaulersDreamSettings\s*:\s*ModSettings/)
	const region = (() => {
		const idx = classBody.indexOf('public override void ExposeData()')
		return idx >= 0 ? classBody.slice(0, idx) : classBody
	})()

	// public/private <type> <name> [= ...]; — type allows generics (Dictionary<string, RouteDialogPrefs>).
	const declRe =
		/^\s*(?:\[[^\]]*\]\s*)*(?:public|private|internal|protected)\s+(?:(?:static|readonly)\s+)*([A-Za-z_][\w.]*(?:<[^=;]*?>)?)\s+([A-Za-z_]\w*)\s*(?:=\s*[^;]+?)?\s*;\s*(?:\/\/.*)?$/

	const types = new Set<string>()
	const lines = region.split('\n')
	for (let i = 0; i < lines.length; i++) {
		const line = lines[i]
		const m = declRe.exec(line)
		if (!m) continue
		const type = m[1]
		if (!isCollectionType(type)) continue
		const nonSerialized =
			line.includes('[System.NonSerialized]') || lines[i - 1]?.trim().startsWith('[System.NonSerialized]')
		const profileMeta = line.includes('[ProfileMeta]') || lines[i - 1]?.trim().startsWith('[ProfileMeta]')
		if (nonSerialized || profileMeta) continue
		types.add(normalize(type))
	}

	if (types.size === 0) throw new Error('check-profile-codec: found no serialized reference-type fields (parser broke?).')

	// --- slice the four dispatch-site bodies ---
	const cloneBody = normalize(sliceRegion(profilesSrc, /private static object CloneValue\(object v\)/))
	const equalBody = normalize(sliceRegion(profilesSrc, /private static bool ValuesEqual\(object x, object y\)/))
	const encodeBody = normalize(sliceRegion(profilesSrc, /private static string EncodeFieldValue\(object v\)/))
	const parseBody = normalize(sliceRegion(profilesSrc, /private static object ParseFieldValue\(Type t, string s\)/))

	// --- assert every reference-type field appears in all four ---
	const errors: string[] = []
	for (const t of [...types].sort()) {
		if (!cloneBody.includes(`case ${t}`))
			errors.push(`CloneValue is missing a "case ${t}:" — capturing a profile snapshot would THROW for this field.`)
		if (!equalBody.includes(`case ${t}`))
			errors.push(
				`ValuesEqual is missing a "case ${t}:" — it falls through to reference equality, so a pristine/reset config reads "Custom (unsaved)" forever.`
			)
		if (!encodeBody.includes(`case ${t}`))
			errors.push(`EncodeFieldValue is missing a "case ${t}:" — copy/paste tokens would encode garbage for this field.`)
		if (!parseBody.includes(`typeof(${t})`))
			errors.push(`ParseFieldValue is missing a "typeof(${t})" branch — pasting a token with this field would throw.`)
	}

	if (errors.length > 0) {
		console.error(`\n[profile-codec] FAIL — ${errors.length} coverage gap(s) across ${types.size} reference-type settings:\n`)
		for (const e of errors) console.error(`  ✗ ${e}`)
		console.error(
			'\n  Fix: add the field type to CloneValue, ValuesEqual, EncodeFieldValue and ParseFieldValue in SettingsProfiles.cs\n'
		)
		process.exit(1)
	}

	console.log(
		`[profile-codec] PASS — ${types.size} reference-type settings covered in CloneValue / ValuesEqual / EncodeFieldValue / ParseFieldValue.`
	)
}

await main()
