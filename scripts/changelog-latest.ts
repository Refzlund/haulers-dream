// Print the CHANGELOG.md section for the current package.json version (used by the release
// workflow for the GitHub Release notes and the Steam Workshop change note).
//
// --steam: emit a Steam-safe variant. The workshop upload embeds the change note inside a
// quoted VDF string with no escaping, so double quotes (and backslashes) corrupt the manifest
// and fail the whole upload. Steam renders BBCode rather than markdown, so bold/headings/links
// are converted while we're at it.
import { resolve } from 'node:path'
import { packageVersion, repoRoot } from './lib'

const steam = process.argv.includes('--steam')
const version = await packageVersion()

/**
 * Markdown to the BBCode subset Steam renders.
 *
 * The LINK conversion is load-bearing, not cosmetic: every release note now ends with linked PR and issue
 * references, and Steam does not understand `[text](url)`. Left alone it renders as a broken tag followed by a
 * bare URL — `[PR #241]` is not a BBCode tag Steam knows, and the `(…)` around the address shows literally.
 *
 * Order matters. Links are converted FIRST, because the quote-stripping below would otherwise be free to run
 * inside a URL, and because `[b]` insertion must not sit between a link's brackets.
 */
function toBBCode(text: string): string {
	return (
		text
			// [label](https://…) -> [url=https://…]label[/url]. Kept deliberately narrow: no nested brackets in
			// the label, and only http(s) targets, so a stray "[…](…)" in prose cannot produce a bogus tag.
			.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '[url=$2]$1[/url]')
			.replace(/^### (.+)$/gm, '[b]$1[/b]')
			.replace(/\*\*([^*]+)\*\*/g, '[b]$1[/b]')
			// VDF safety, NOT style: the change note is embedded in a quoted string with no escaping, so a double
			// quote or backslash anywhere corrupts the manifest and fails the upload.
			.replace(/"/g, "'")
			.replace(/\\/g, '')
	)
}

function emit(text: string) {
	if (steam) {
		text = toBBCode(text)
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
