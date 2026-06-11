// Build the mod and deploy it into your RimWorld Mods folder (RIMWORLD_MODS_DIR in .env).
import { $ } from 'bun'
import { findDotnet, repoRoot, rimworldModsDir } from './lib'
import { existsSync } from 'node:fs'

const mods = rimworldModsDir()
if (!mods) {
	console.error(
		'RIMWORLD_MODS_DIR is not set.\n' +
		'Copy .env.example to .env and point RIMWORLD_MODS_DIR at your RimWorld Mods folder.'
	)
	process.exit(1)
}
if (!existsSync(mods)) {
	console.error(`RIMWORLD_MODS_DIR points to a folder that does not exist: ${mods}`)
	process.exit(1)
}

const dotnet = await findDotnet()
// The csproj's DeployToRimWorld post-build target performs the copy.
await $`${dotnet} build Source/HaulersDream/HaulersDream.csproj -c Release -v q -nologo -p:RimWorldModsDir=${mods}`.cwd(repoRoot)
