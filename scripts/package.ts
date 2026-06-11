// Build the mod and stage a clean, distributable copy under dist/HaulersDream
// (exactly what ships to the Steam Workshop), plus a versioned zip for GitHub Releases.
import { $ } from 'bun'
import { cpSync, existsSync, mkdirSync, rmSync } from 'node:fs'
import { resolve } from 'node:path'
import { findDotnet, packageVersion, repoRoot } from './lib'

const dotnet = await findDotnet()
await $`${dotnet} build Source/HaulersDream/HaulersDream.csproj -c Release -v q -nologo`.cwd(repoRoot)

const dist = resolve(repoRoot, 'dist')
const stage = resolve(dist, 'HaulersDream')
rmSync(stage, { recursive: true, force: true })
mkdirSync(stage, { recursive: true })

// Mod content. PDBs are excluded — players don't need them and they bloat the upload.
const trees = ['About', 'Defs', 'Patches', 'Languages']
for (const tree of trees) {
	const src = resolve(repoRoot, tree)
	if (existsSync(src)) cpSync(src, resolve(stage, tree), { recursive: true })
}
cpSync(resolve(repoRoot, 'LoadFolders.xml'), resolve(stage, 'LoadFolders.xml'))
mkdirSync(resolve(stage, '1.6/Assemblies'), { recursive: true })
for (const dll of new Bun.Glob('*.dll').scanSync(resolve(repoRoot, '1.6/Assemblies'))) {
	cpSync(resolve(repoRoot, '1.6/Assemblies', dll), resolve(stage, '1.6/Assemblies', dll))
}

// Versioned zip next to the staged folder (attached to the GitHub Release).
const version = await packageVersion()
const zipName = `HaulersDream-v${version}.zip`
rmSync(resolve(dist, zipName), { force: true })
if (Bun.which('zip')) {
	await $`zip -qr ${zipName} HaulersDream`.cwd(dist)
} else {
	await $`powershell -NoProfile -Command Compress-Archive -Path HaulersDream -DestinationPath ${zipName}`.cwd(dist)
}
console.log(`packaged dist/HaulersDream and dist/${zipName}`)
