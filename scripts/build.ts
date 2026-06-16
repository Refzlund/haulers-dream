// Build the whole solution (mod + core + tests) in Release.
// If RIMWORLD_MODS_DIR is set (via .env), the MSBuild post-build step also deploys the mod there.
import { $ } from 'bun'
import { resolve } from 'node:path'
import { findDotnet, repoRoot, rimworldModsDir } from './lib'

const dotnet = await findDotnet()
const extra: string[] = []
const mods = rimworldModsDir()
if (mods) extra.push(`-p:RimWorldModsDir=${mods}`)

await $`${dotnet} build Source/HaulersDream.sln -c Release -v q -nologo ${extra}`.cwd(repoRoot)

// Compile succeeded: run the static settings-drift guard (107 settings declared 3x must agree).
// Fails the build (non-zero) on any missing/mismatched setting default — see check-settings-drift.ts.
const drift = Bun.spawn(['bun', resolve(import.meta.dir, 'check-settings-drift.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await drift.exited) !== 0) throw new Error('Settings-drift check failed (see output above).')
