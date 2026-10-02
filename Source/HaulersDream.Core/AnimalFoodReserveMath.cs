using System;
using System.Collections.Generic;

namespace HaulersDream.Core
{
	public static class AnimalFoodReserveMath
	{
		public static int MaximumRemovable(float reserve, IReadOnlyList<int> counts, IReadOnlyList<float> nutrition, int candidate)
		{
			if (counts == null || nutrition == null || counts.Count != nutrition.Count || candidate < 0 || candidate >= counts.Count)
			{
				throw new ArgumentException("Animal-food pool shape is invalid.");
			}
			if (!Finite(reserve) || reserve < 0f)
			{
				throw new ArgumentException("Animal-food reserve must be finite and nonnegative.");
			}
			for (int i = 0; i < counts.Count; i++)
			{
				if (counts[i] < 0 || !Finite(nutrition[i]) || nutrition[i] < 0f)
				{
					throw new ArgumentException("Animal-food observations must be finite and nonnegative.");
				}
			}
			int num = counts[candidate];
			if (num == 0 || reserve == 0f || nutrition[candidate] == 0f)
			{
				return num;
			}
			if (!Covers(reserve, counts, nutrition, candidate, 0))
			{
				return 0;
			}
			int num2 = 0;
			int num3 = num;
			while (num2 < num3)
			{
				int num4 = num2 + (int)(((long)num3 - (long)num2 + 1) / 2);
				if (Covers(reserve, counts, nutrition, candidate, num4))
				{
					num2 = num4;
				}
				else
				{
					num3 = num4 - 1;
				}
			}
			if (!Covers(reserve, counts, nutrition, candidate, num2) || (num2 < num && Covers(reserve, counts, nutrition, candidate, num2 + 1)))
			{
				throw new InvalidOperationException("Animal-food removal boundary changed.");
			}
			return num2;
		}

		private static bool Covers(float reserve, IReadOnlyList<int> counts, IReadOnlyList<float> nutrition, int candidate, int removal)
		{
			double num = 0.0;
			for (int i = 0; i < counts.Count; i++)
			{
				num += (double)nutrition[i] * (double)((i == candidate) ? (counts[i] - removal) : counts[i]);
			}
			return num >= (double)reserve;
		}

		private static bool Finite(float value)
		{
			if (!float.IsNaN(value))
			{
				return !float.IsInfinity(value);
			}
			return false;
		}
	}
}
