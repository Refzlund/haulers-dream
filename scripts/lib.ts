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
