using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;

namespace HaulersDream
{
	internal sealed class PackedFoodSnapshot
	{
		internal sealed class Candidate
		{
			private readonly bool colonist;

			private readonly object needs;

			private readonly object foodNeed;

			private readonly bool nutritionGiving;

			private readonly bool drug;

			internal readonly bool Eligible;

			internal Candidate(Pawn pawn, Thing thing)
			{
				colonist = pawn.IsColonist;
				if (!colonist)
				{
					return;
				}
				needs = pawn.needs;
				foodNeed = pawn.needs?.food;
				if (foodNeed == null)
				{
					return;
				}
				nutritionGiving = thing.def.IsNutritionGivingIngestible;
				if (nutritionGiving)
				{
					drug = thing.def.IsDrug;
					if (!drug)
					{
						Eligible = JobGiver_PackFood.IsGoodPackableFoodFor(thing, pawn, checkMass: false);
					}
				}
			}

			internal bool Same(Candidate other)
			{
				if (colonist == other.colonist && needs == other.needs && foodNeed == other.foodNeed && nutritionGiving == other.nutritionGiving && drug == other.drug)
				{
					return Eligible == other.Eligible;
				}
				return false;
			}
		}

		private readonly Pawn pawn;

		private readonly IReadOnlyList<Thing> sources;

		private readonly object needs;

		private readonly object foodNeed;

		private readonly bool colonist;

		private readonly float maxLevel;

		private readonly bool[] packable;

		private readonly bool[] candidateEligible;

		private readonly float[] nutrition;

		internal PackedFoodSnapshot(Pawn pawn, IReadOnlyList<Thing> sources)
		{
			this.pawn = pawn;
			this.sources = sources;
			needs = pawn.needs;
			foodNeed = pawn.needs?.food;
			colonist = pawn.IsColonist;
			maxLevel = ((colonist && foodNeed != null) ? pawn.needs.food.MaxLevel : 0f);
			RequireFinite(maxLevel);
			if (maxLevel < 0f)
			{
				throw new InvalidOperationException("Packed-food need is negative.");
			}
			packable = new bool[sources.Count];
			candidateEligible = new bool[sources.Count];
			nutrition = new float[sources.Count];
			if (colonist && foodNeed != null)
			{
				for (int i = 0; i < sources.Count; i++)
				{
					Thing thing = sources[i];
					packable[i] = JobGiver_PackFood.IsGoodPackableFoodFor(thing, pawn, checkMass: false);
					candidateEligible[i] = thing.def.IsNutritionGivingIngestible && !thing.def.IsDrug && packable[i];
					if (packable[i])
					{
						nutrition[i] = thing.GetStatValue(StatDefOf.Nutrition);
						RequireFinite(nutrition[i]);
					}
				}
			}
			ValidateNeed();
		}

		internal int KeepCount(int candidate, IReadOnlyList<int> counts)
		{
			if (!candidateEligible[candidate])
			{
				return 0;
			}
			float num = 0f;
			for (int i = 0; i < sources.Count; i++)
			{
				if (packable[i])
				{
					num += nutrition[i] * (float)counts[i];
				}
			}
			RequireFinite(num);
			return FoodKeepMath.KeepCount(num, maxLevel, nutrition[candidate], counts[candidate]);
		}

		internal void Validate()
		{
			PackedFoodSnapshot packedFoodSnapshot = new PackedFoodSnapshot(pawn, sources);
			if (needs != packedFoodSnapshot.needs || foodNeed != packedFoodSnapshot.foodNeed || colonist != packedFoodSnapshot.colonist || maxLevel != packedFoodSnapshot.maxLevel)
			{
				throw new InvalidOperationException("Packed-food need or colonist state changed during allocation.");
			}
			for (int i = 0; i < sources.Count; i++)
			{
				if (packable[i] != packedFoodSnapshot.packable[i] || candidateEligible[i] != packedFoodSnapshot.candidateEligible[i] || nutrition[i] != packedFoodSnapshot.nutrition[i])
				{
					throw new InvalidOperationException("Packed-food eligibility or nutrition changed during allocation.");
				}
			}
		}

		private void ValidateNeed()
		{
			if (needs != pawn.needs || foodNeed != pawn.needs?.food || colonist != pawn.IsColonist || (colonist && foodNeed != null && maxLevel != pawn.needs.food.MaxLevel))
			{
				throw new InvalidOperationException("Packed-food need changed during observation.");
			}
		}

		private static void RequireFinite(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				throw new InvalidOperationException("Packed-food input is not finite.");
			}
		}
	}
}
