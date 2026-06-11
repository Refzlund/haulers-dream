// One-off: create a NEW Steam Workshop item for the mod (publishedfileid 0 = create).
// Writes dist/create_item.vdf with the page title + description (from About/SteamDescription.txt,
// double quotes sanitized — the VDF format has no escaping) and PRIVATE visibility so the page
// can be reviewed before going public. Run steamcmd with the printed command afterwards.
import { resolve } from 'node:path'
import { repoRoot } from './lib'

const description = (await Bun.file(resolve(repoRoot, 'About/SteamDescription.txt')).text())
	.replace(/"/g, "'")
	.replace(/\\/g, '')

const content = resolve(repoRoot, 'dist/HaulersDream').replaceAll('\\', '/')
const preview = resolve(repoRoot, 'About/Preview.png').replaceAll('\\', '/')
const vdf = `"workshopitem"
{
	"appid" "294100"
	"publishedfileid" "0"
	"contentfolder" "${content}"
	"previewfile" "${preview}"
	"visibility" "2"
	"title" "Hauler's Dream"
	"description" "${description}"
	"changenote" "Initial release (v1.0.0)"
}
`
const out = resolve(repoRoot, 'dist/create_item.vdf')
await Bun.write(out, vdf)
console.log(out)
