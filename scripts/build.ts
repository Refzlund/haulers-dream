// Build the whole solution (mod + core + tests) in Release.
// If RIMWORLD_MODS_DIR is set (via .env), the MSBuild post-build step also deploys the mod there.
import { $ } from 'bun'
import { findDotnet, repoRoot, rimworldModsDir } from './lib'

const dotnet = await findDotnet()
const extra: string[] = []
const mods = rimworldModsDir()
if (mods) extra.push(`-p:RimWorldModsDir=${mods}`)

await $`${dotnet} build Source/HaulersDream.sln -c Release -v q -nologo ${extra}`.cwd(repoRoot)
