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

// Guard profile-codec coverage: every serialized reference-type setting must appear in all four dispatch sites
// (CloneValue / ValuesEqual / EncodeFieldValue / ParseFieldValue). A missing case is the silent "always Custom
// (unsaved)" bug. Runtime backstop: HaulersDreamSettings.VerifyProfileIntegrity(). See check-profile-codec.ts.
const profileCodec = Bun.spawn(['bun', resolve(import.meta.dir, 'check-profile-codec.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await profileCodec.exited) !== 0) throw new Error('Profile-codec coverage check failed (see output above).')

// Guard the Steam Workshop description against Steam's 8000-character truncation limit.
const steamDesc = Bun.spawn(['bun', resolve(import.meta.dir, 'check-steam-description.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await steamDesc.exited) !== 0) throw new Error('Steam-description length check failed (see output above).')

// Guard translation parity: every non-English Languages/ folder must define the same key set + the
// same {placeholders} as English. Fails the build on missing/extra keys or dropped placeholders.
const translations = Bun.spawn(['bun', resolve(import.meta.dir, 'check-translations.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await translations.exited) !== 0) throw new Error('Translation parity check failed (see output above).')

// Guard the drop-protection defence (issues #62/#81/#87 — pawns dropping HD-scooped inventory cargo). Fails
// the build if any layer of the guard is weakened (un-healed tag read, a dropped seam, the Core policy or the
// startup tripwire removed). See check-drop-protection.ts.
const dropProtection = Bun.spawn(['bun', resolve(import.meta.dir, 'check-drop-protection.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await dropProtection.exited) !== 0) throw new Error('Drop-protection check failed (see output above).')

// Guard the #122 think-node seam boundaries (pawns read books until they starved because a throwing HD
// enhancement cost them their food node every think; vanilla logs one collapsed entry and skips the node).
// Fails the build if a seam postfix loses its degrade boundary (try + SeamDegraded, no rethrow), the
// meals-on-wheels catch stops restoring vanilla's outputs, or the Core severity gates drift.
// See check-need-seam-guards.ts.
const needSeams = Bun.spawn(['bun', resolve(import.meta.dir, 'check-need-seam-guards.ts')], {
	stdout: 'inherit',
	stderr: 'inherit',
	cwd: repoRoot,
})
if ((await needSeams.exited) !== 0) throw new Error('Need-seam guard check failed (see output above).')
