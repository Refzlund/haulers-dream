// Print the CHANGELOG.md section for the current package.json version (used by the release
// workflow for the GitHub Release notes and the Steam Workshop change note).
//
// --steam: emit a Steam-safe variant. The workshop upload embeds the change note inside a
// quoted VDF string with no escaping, so double quotes (and backslashes) corrupt the manifest
// and fail the whole upload. Steam renders BBCode rather than markdown, so bold/headings are
// converted while we're at it.
import { resolve } from 'node:path'
import { packageVersion, repoRoot } from './lib'

const steam = process.argv.includes('--steam')
const version = await packageVersion()

function emit(text: string) {
	if (steam) {
		text = text
			.replace(/^### (.+)$/gm, '[b]$1[/b]')
			.replace(/\*\*([^*]+)\*\*/g, '[b]$1[/b]')
			.replace(/"/g, "'")
			.replace(/\\/g, '')
		if (text.length > 7000) text = text.slice(0, 7000) + '\n…'
	}
	console.log(text)
}

const file = Bun.file(resolve(repoRoot, 'CHANGELOG.md'))
if (!(await file.exists())) {
	emit(`v${version}`)
	process.exit(0)
}

const lines = (await file.text()).split('\n')
const start = lines.findIndex(l => l.startsWith(`## ${version}`))
if (start === -1) {
	emit(`v${version}`)
	process.exit(0)
}
let end = lines.length
for (let i = start + 1; i < lines.length; i++) {
	if (lines[i].startsWith('## ')) { end = i; break }
}
emit(lines.slice(start + 1, end).join('\n').trim())
