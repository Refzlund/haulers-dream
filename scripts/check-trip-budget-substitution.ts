// Static guard on the argument the load planner hands the fair-share rule (the "one item per trip" family).
//
// The rule itself — LoadFairShare.ShareMassBudget — is pure and well covered. It has still shipped the same
// user-visible bug TWICE, because both times the rule was right and the ARGUMENT was wrong:
//
//   1. The planner passed the unbounded sentinel (float.MaxValue) when a pawn had no carry ceiling and the
//      destination no mass cap — a cave exit. The "never split a remainder that already fits in one trip" rule
//      is gated on a real bound, so it became unreachable, the remainder was re-divided every trip, and the
//      no-starvation floor bottomed the decay out at ONE unit per trip.
//   2. The first fix substituted `baseCap - running`. `running` counts worn apparel and equipment and a human's
//      whole capacity is BodySize * 35, so plate armour plus a thump cannon zeroes it — and a zero budget skips
//      the same rule in the same way, reinstating the bug for every geared colonist. Permanently, since gear is
//      never deposited.
//
// The unit tests cannot see this. HaulersDream.Tests references only HaulersDream.Core, so it can observe the
// RULE but never the ARGUMENTS the Verse glue passes it: reinstating either mistake in TransportLoad compiles
// clean and leaves the whole suite green. That is precisely how this shipped twice, so it is pinned here instead.
//
// This script fails the build (exit 1) if:
//   1. TransportLoad stops routing its trip budget through LoadFairShare.AskerTripBudgetKg;
//   2. that call's second argument is anything but the bare base-capacity local — any arithmetic there (the
//      `baseCap - running` shape, or a Math.Max around it) is the mistake this exists to catch;
//   3. ShareMassBudget is called with something other than the substituted local;
//   4. AskerTripBudgetKg goes missing from Core, or stops being total (it must return the pack size, not zero).
//
// KNOWN GAP: this is a text scan. It pins the SHAPE of the call, not its runtime value — a rename of the
// base-capacity local to something equally wrong would satisfy it. It is cheap insurance on a specific,
// twice-made mistake, not a proof.
//
// Run directly to self-check:  bun scripts/check-trip-budget-substitution.ts
import { resolve } from 'node:path'
import { repoRoot } from './lib'

/** Strip line and block comments so prose discussing the banned shape cannot fail the build. */
function blankComments(src: string): string {
	return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '')
}

async function main() {
	const root = repoRoot
	const glue = blankComments(await Bun.file(resolve(root, 'Source/HaulersDream/TransportLoad.cs')).text())
	const core = blankComments(await Bun.file(resolve(root, 'Source/HaulersDream.Core/LoadFairShare.cs')).text())
	const fail = (msg: string) => {
		console.error(`[trip-budget-substitution] FAIL — ${msg}\n`)
		process.exit(1)
	}

	// (4) The substitution must exist in Core, and must yield the pack size rather than a difference.
	if (!/public static float AskerTripBudgetKg\(/.test(core))
		fail(
			`LoadFairShare.AskerTripBudgetKg is gone. It is the only place the unbounded trip-budget sentinel is ` +
				`turned into a usable number; without it the fair-share rule is unreachable and the load decays to ` +
				`one item per trip again.`
		)
	const body = core.match(/public static float AskerTripBudgetKg\([^)]*\)\s*=>([^;]+);/)
	if (!body) fail(`AskerTripBudgetKg is no longer a single expression — re-check it by hand and update this guard.`)
	if (/-/.test(body![1]))
		fail(
			`AskerTripBudgetKg subtracts something: "${body![1].trim()}". What a pawn already carries must NOT ` +
				`shrink a trip it has no ceiling for — that subtraction is zero for an ordinarily-geared colonist ` +
				`and reinstates the one-item-per-trip bug.`
		)

	// (1) + (2) The glue must route through it, passing the base capacity bare.
	const call = glue.match(/LoadFairShare\.AskerTripBudgetKg\(\s*([^,]+),\s*([^)]+)\)/)
	if (!call)
		fail(
			`TransportLoad no longer calls LoadFairShare.AskerTripBudgetKg. The planner must never hand the raw ` +
				`trip budget to ShareMassBudget: it can be the unbounded sentinel, which the rule cannot use.`
		)
	const packArg = call![2].trim()
	if (!/^[A-Za-z_]\w*$/.test(packArg))
		fail(
			`AskerTripBudgetKg's pack argument is an expression, not a bare local: "${packArg}". Any arithmetic ` +
				`here is the twice-made mistake — "baseCap - running" evaluates to 0 for a pawn in plate armour ` +
				`(carried mass includes worn gear; a human's whole capacity is 35 kg) and skips the fit-in-one-trip ` +
				`rule exactly as the unbounded sentinel did.`
		)

	// (3) The share rule must be fed the substituted value, not the raw budget.
	const share = glue.match(/LoadFairShare\.ShareMassBudget\([^)]*\)/)
	if (!share) fail(`TransportLoad no longer calls LoadFairShare.ShareMassBudget — update this guard to match.`)
	const substituted = glue.match(/float\s+(\w+)\s*=\s*LoadFairShare\.AskerTripBudgetKg\(/)
	if (!substituted || !share![0].includes(substituted![1]))
		fail(
			`ShareMassBudget is not being passed the substituted budget. Feeding it the raw trip budget puts the ` +
				`unbounded sentinel back in front of the rule that cannot use one.`
		)

	console.log(
		`[trip-budget-substitution] PASS — the load planner substitutes a full pack for the unbounded trip-budget ` +
			`sentinel (via ${substituted![1]} = AskerTripBudgetKg(…, ${packArg})) and feeds it to the share rule.`
	)
}

await main()
