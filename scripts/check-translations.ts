// Translation parity check. English (Languages/English) is the source of truth; every other language
// folder must define EXACTLY the same set of keys, with the SAME placeholders ({0}, {ORIGINAL}, …) in
// each value. Catches the classic localization drift: a key added/renamed in English but missing in a
// translation (shows the raw key in-game), or a translator dropping a {0} placeholder (runtime format
// error). Run: bun scripts/check-translations.ts   (also invoked by the build).
import { resolve } from 'node:path'
import { readdirSync, statSync, existsSync } from 'node:fs'
import { repoRoot } from './lib'

const LANG_DIR = resolve(repoRoot, 'Languages')
const SOURCE = 'English'

// Extract <key>value</key> leaf entries from a RimWorld LanguageData XML file (comments stripped first).
// Values may span lines, so the body match is non-greedy + dotall. Returns a Map<key, value>.
function parseLangData(xml: string): Map<string, string> {
	// Strip the xml declaration, comments, and the <LanguageData> wrapper FIRST — otherwise the leaf
	// regex's non-greedy body would match the whole outer <LanguageData>…</LanguageData> as one entry
	// and never see the inner keys. Values are entity-encoded (&lt;/&amp;), so the body has no raw tags.
	const body = xml
		.replace(/<\?xml[\s\S]*?\?>/g, '')
		.replace(/<!--[\s\S]*?-->/g, '')
		.replace(/<\/?LanguageData[^>]*>/g, '')
	const map = new Map<string, string>()
	const re = /<([A-Za-z0-9_.]+)>([\s\S]*?)<\/\1>/g
	let m: RegExpExecArray | null
	while ((m = re.exec(body)) !== null) map.set(m[1], m[2])
	return map
}

// The placeholder tokens the game fills in — these must be preserved exactly (as a multiset) per value.
function placeholders(value: string): string[] {
	return (value.match(/\{[^}]+\}/g) ?? []).sort()
}

// Collect every *.xml under a language folder (Keyed + DefInjected/**), keyed by their repo-relative
// sub-path so files line up across languages (Keyed/HaulersDream.xml, DefInjected/JobDef/Jobs.xml, …).
function langFiles(langPath: string): Map<string, string> {
	const out = new Map<string, string>()
	function walk(dir: string, rel: string) {
		if (!existsSync(dir)) return
		for (const name of readdirSync(dir)) {
			const full = resolve(dir, name)
			const r = rel ? `${rel}/${name}` : name
			if (statSync(full).isDirectory()) walk(full, r)
			// Only Keyed/ + DefInjected/ hold translatable entries; LanguageInfo.xml is language metadata
			// (friendlyName/canBeTiny), not keys — skip it so it isn't mis-counted as an extra key.
			else if (name.endsWith('.xml') && name !== 'LanguageInfo.xml') out.set(r, full)
		}
	}
	walk(langPath, '')
	return out
}

const enPath = resolve(LANG_DIR, SOURCE)
if (!existsSync(enPath)) {
	console.error(`[translations] no ${SOURCE} folder at ${enPath}`)
	process.exit(1)
}

// Read all files synchronously via Bun.file().text() awaited.
async function readMerged(langPath: string): Promise<Map<string, string>> {
	const merged = new Map<string, string>()
	for (const [rel, full] of langFiles(langPath)) {
		const text = await Bun.file(full).text()
		for (const [k, v] of parseLangData(text)) {
			if (merged.has(k)) console.warn(`[translations] duplicate key ${k} in ${langPath}/${rel}`)
			merged.set(k, v)
		}
	}
	return merged
}

const english = await readMerged(enPath)
if (english.size === 0) {
	console.error('[translations] parsed 0 English keys — parser or source problem')
	process.exit(1)
}

const langs = readdirSync(LANG_DIR).filter(
	(n) => n !== SOURCE && statSync(resolve(LANG_DIR, n)).isDirectory(),
)

let problems = 0
const summary: string[] = []
for (const lang of langs.sort()) {
	const t = await readMerged(resolve(LANG_DIR, lang))
	const missing: string[] = []
	const extra: string[] = []
	const phMismatch: string[] = []
	const untranslated: string[] = []
	for (const [k, env] of english) {
		if (!t.has(k)) { missing.push(k); continue }
		const tv = t.get(k)!
		const ep = placeholders(env).join(',')
		const tp = placeholders(tv).join(',')
		if (ep !== tp) phMismatch.push(`${k} (en:[${ep}] vs [${tp}])`)
	}
	for (const k of t.keys()) if (!english.has(k)) extra.push(k)
	const issues = missing.length + extra.length + phMismatch.length
	problems += issues
	if (issues === 0) {
		summary.push(`  ${lang.padEnd(20)} OK (${t.size} keys)`)
	} else {
		summary.push(`  ${lang.padEnd(20)} ${issues} issue(s): ${missing.length} missing, ${extra.length} extra, ${phMismatch.length} placeholder`)
		for (const k of missing.slice(0, 8)) summary.push(`      MISSING  ${k}`)
		if (missing.length > 8) summary.push(`      …and ${missing.length - 8} more missing`)
		for (const k of extra.slice(0, 8)) summary.push(`      EXTRA    ${k}`)
		for (const k of phMismatch.slice(0, 8)) summary.push(`      PLACEHLD ${k}`)
	}
}

console.log(`[translations] source ${SOURCE}: ${english.size} keys; ${langs.length} translation(s)`)
console.log(summary.join('\n'))
if (problems > 0) {
	console.error(`[translations] FAIL — ${problems} parity issue(s) across ${langs.length} language(s).`)
	process.exit(1)
}
console.log('[translations] PASS — all languages match the English key set + placeholders.')
