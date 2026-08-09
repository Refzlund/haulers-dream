import { readdirSync, statSync } from 'node:fs'
import { resolve } from 'node:path'

/** Repository root (this file lives in <root>/scripts/). */
export const repoRoot = resolve(import.meta.dir, '..')

/**
 * Locate the .NET SDK: PATH first, then the user-local install (~/.dotnet),
 * which is where scripted installs (dotnet-install.ps1/sh) put it.
 * A `dotnet` host without any SDK (runtime-only installs) is skipped.
 */
export async function findDotnet(): Promise<string> {
	const home = process.env.USERPROFILE ?? process.env.HOME ?? ''
	const candidates = [
		Bun.which('dotnet'),
		`${home}/.dotnet/dotnet.exe`,
		`${home}/.dotnet/dotnet`,
	].filter((c): c is string => !!c)
	for (const candidate of candidates) {
		if (!(await Bun.file(candidate).exists())) continue
		if (await hasSdk(candidate)) return candidate
	}
	throw new Error(
		'No .NET SDK found. Install the .NET SDK (8.0+) and either put it on PATH or in ~/.dotnet'
	)
}

async function hasSdk(dotnet: string): Promise<boolean> {
	try {
		const proc = Bun.spawn([dotnet, '--list-sdks'], { stdout: 'pipe', stderr: 'ignore' })
		const out = await new Response(proc.stdout).text()
		return (await proc.exited) === 0 && out.trim().length > 0
	} catch {
		return false
	}
}

/**
 * The RimWorld Mods folder to deploy into, from RIMWORLD_MODS_DIR (bun auto-loads .env).
 * Returns null when unset — building still works, only the local deploy step is skipped.
 */
export function rimworldModsDir(): string | null {
	const dir = process.env.RIMWORLD_MODS_DIR?.trim()
	return dir ? dir : null
}

export async function packageVersion(): Promise<string> {
	const pkg = await Bun.file(resolve(repoRoot, 'package.json')).json()
	return pkg.version as string
}

/**
 * Blank out C# comments and the CONTENT of string/char literals, so a build guard can search real code without
 * matching prose. Every removed character becomes a space, which makes the result exactly as long as the input
 * with its line breaks untouched — so an offset or line number taken from the stripped text still points at the
 * real source line, and a caller may index the two texts against each other.
 *
 * Shared by every guard that BANS a symbol, and load-bearing for all of them: each guard's own header, and the
 * doc comments of the fix it pins, name the banned symbols at length in prose. A guard reading raw text would
 * trip on its own explanation and be deleted within a week.
 *
 * Unterminated constructs (a block comment or string running to EOF) blank to the end of input rather than
 * throwing or running away — a guard must survive a half-written file it happens to scan.
 *
 * → GOTCHA: an interpolated string's `{…}` holes count as string content, so a banned call written inside one is
 *   invisible to every caller. Nobody writes that; these guards are cheap insurance, not a sandbox.
 *
 * @param src - C# source text as read from disk. CRLF is preserved verbatim, so a caller that splits on '\n' and
 *   inspects line ends should normalise first.
 * @returns The same text, same length and same line structure, with comment and literal bodies replaced by spaces.
 */
export function codeOnly(src: string): string {
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
					else {
						j++
						break
					}
				} else j++
			}
			out += keepNewlines(src.slice(i, j))
			i = j
		} else if (src[i] === '"' || src[i] === "'") {
			const quote = src[i]
			let j = i + 1
			while (j < src.length) {
				if (src[j] === '\\') j += 2
				else if (src[j] === quote) {
					j++
					break
				} else if (src[j] === '\n') break // unterminated; don't run away
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

/**
 * Every .cs file under `dir`, recursively, as absolute paths — the file set a build guard scans.
 *
 * `obj/` and `bin/` are skipped at every depth: they hold build output, which is a mix of GENERATED sources and
 * verbatim COPIES of the real ones. Scanning them reports each genuine finding twice, the second time against a
 * path nobody can edit.
 *
 * A `dir` that does not exist throws rather than answering with an empty list. That is deliberate: a guard whose
 * passing state is an empty scan cannot tell "clean" from "looked in the wrong place", so a mistyped root has to
 * fail loudly.
 *
 * @param dir - Absolute path to the root to walk. `obj` and `bin` are matched by folder NAME at any depth, not
 *   only directly beneath the root.
 * @returns Absolute paths in filesystem order — sort them if a caller's output order matters.
 */
export function csFilesUnder(dir: string): string[] {
	const out: string[] = []
	for (const entry of readdirSync(dir)) {
		if (entry === 'obj' || entry === 'bin') continue
		const full = resolve(dir, entry)
		if (statSync(full).isDirectory()) out.push(...csFilesUnder(full))
		else if (entry.endsWith('.cs')) out.push(full)
	}
	return out
}
