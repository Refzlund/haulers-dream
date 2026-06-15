// Run the headless unit tests (HaulersDream.Tests — pure decision math, no game install needed).
import { $ } from 'bun'
import { findDotnet, repoRoot } from './lib'

const dotnet = await findDotnet()
const extra = process.argv.slice(2) // forward e.g. --filter TestCategory=Perf (used by test:perf)
await $`${dotnet} test Source/HaulersDream.sln -c Release --nologo ${extra}`.cwd(repoRoot)
