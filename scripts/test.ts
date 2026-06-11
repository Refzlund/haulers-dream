// Run the headless unit tests (HaulersDream.Tests — pure decision math, no game install needed).
import { $ } from 'bun'
import { findDotnet, repoRoot } from './lib'

const dotnet = await findDotnet()
await $`${dotnet} test Source/HaulersDream.sln -c Release --nologo`.cwd(repoRoot)
