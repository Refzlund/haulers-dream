// Verify the Steam Workshop description fits Steam's hard limit.
//
// Steam truncates a Workshop description at 8000 characters; anything past that is silently dropped
// on publish. About/SteamDescription.txt is the source we paste/ship, so this guards it at build time.
// Line endings are normalised to LF before counting so the number is deterministic across checkouts
// (Steam collapses them too). Run directly to self-check:  bun scripts/check-steam-description.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const LIMIT = 8000
const PATH = 'About/SteamDescription.txt'

const text = (await Bun.file(resolve(repoRoot, PATH)).text()).replace(/\r\n/g, '\n')
const length = text.length

if (length > LIMIT) {
	console.error(`[steam-desc] FAIL — ${PATH} is ${length} characters, ${length - LIMIT} over the ${LIMIT} limit.`)
	process.exit(1)
}

console.log(`[steam-desc] PASS — ${length}/${LIMIT} characters (${LIMIT - length} to spare).`)
