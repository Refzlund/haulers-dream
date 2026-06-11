// Propagate the package.json version (managed by changesets) into the game-facing files:
// About/About.xml <modVersion> and Source/Directory.Build.props <Version> (assembly version).
// Runs as part of `bun run version`, so the release PR carries all three in sync.
import { resolve } from 'node:path'
import { packageVersion, repoRoot } from './lib'

const version = await packageVersion()

async function patch(file: string, pattern: RegExp, replacement: string) {
	const path = resolve(repoRoot, file)
	const text = await Bun.file(path).text()
	if (!pattern.test(text))
		throw new Error(`sync-version: pattern ${pattern} not found in ${file}`)
	await Bun.write(path, text.replace(pattern, replacement))
	console.log(`sync-version: ${file} -> ${version}`)
}

await patch(
	'About/About.xml',
	/<modVersion[^>]*>[^<]*<\/modVersion>/,
	`<modVersion IgnoreIfNoMatchingField="True">${version}</modVersion>`
)
await patch(
	'Source/Directory.Build.props',
	/<Version>[^<]*<\/Version>/,
	`<Version>${version}</Version>`
)
