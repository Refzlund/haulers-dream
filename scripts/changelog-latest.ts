// Print the CHANGELOG.md section for the current package.json version (used by the release
// workflow for the GitHub Release notes and the Steam Workshop change note).
import { resolve } from 'node:path'
import { packageVersion, repoRoot } from './lib'

const version = await packageVersion()
const file = Bun.file(resolve(repoRoot, 'CHANGELOG.md'))
if (!(await file.exists())) {
	console.log(`v${version}`)
	process.exit(0)
}

const lines = (await file.text()).split('\n')
const start = lines.findIndex(l => l.startsWith(`## ${version}`))
if (start === -1) {
	console.log(`v${version}`)
	process.exit(0)
}
let end = lines.length
for (let i = start + 1; i < lines.length; i++) {
	if (lines[i].startsWith('## ')) { end = i; break }
}
console.log(lines.slice(start + 1, end).join('\n').trim())
