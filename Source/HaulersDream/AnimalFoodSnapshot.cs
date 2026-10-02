using System;
using System.Collections.Generic;
using HaulersDream.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal sealed class AnimalFoodSnapshot
	{
		private readonly struct FoodObservation
		{
			internal readonly bool eligible;

			internal readonly float nutrition;

			private readonly bool nutritionRead;

			internal FoodObservation(bool eligible, float nutrition)
			{
				this.eligible = eligible;
				this.nutrition = nutrition;
				nutritionRead = true;
			}

			internal bool Same(FoodObservation other)
			{
				if (eligible == other.eligible && nutritionRead == other.nutritionRead)
				{
					return nutrition == other.nutrition;
				}
				return false;
			}
		}

		private sealed class RequestFacts
		{
			private readonly object tracker;

			private readonly object queue;

			private readonly object driver;

			private readonly object[] queuedEntries;

			internal readonly JobFact[] jobs;

			internal RequestFacts(Pawn pawn)
			{
				Pawn_JobTracker pawn_JobTracker = (Pawn_JobTracker)(tracker = pawn?.jobs);
				queue = pawn_JobTracker?.jobQueue;
				driver = pawn_JobTracker?.curDriver;
				int valueOrDefault = (pawn_JobTracker?.jobQueue?.Count).GetValueOrDefault();
				queuedEntries = new object[valueOrDefault];
				jobs = new JobFact[valueOrDefault + 1];
				jobs[0] = new JobFact(pawn, pawn_JobTracker?.curJob, pawn_JobTracker?.curDriver);
				for (int i = 0; i < valueOrDefault; i++)
				{
					queuedEntries[i] = pawn_JobTracker.jobQueue[i];
					jobs[i + 1] = new JobFact(pawn, pawn_JobTracker.jobQueue[i]?.job, null);
				}
				if (tracker != pawn?.jobs || queue != pawn?.jobs?.jobQueue || driver != pawn?.jobs?.curDriver || valueOrDefault != (pawn?.jobs?.jobQueue?.Count).GetValueOrDefault() || jobs[0].job != pawn?.jobs?.curJob)
				{
					throw new InvalidOperationException("Animal-food job collection changed during observation.");
				}
				for (int j = 0; j < valueOrDefault; j++)
				{
					if (queuedEntries[j] != pawn_JobTracker.jobQueue[j] || jobs[j + 1].job != pawn_JobTracker.jobQueue[j]?.job)
					{
						throw new InvalidOperationException("Animal-food queue changed during observation.");
					}
				}
			}

			internal void Validate(Pawn pawn)
			{
				RequireSame(new RequestFacts(pawn));
			}

			internal void RequireSame(RequestFacts current)
			{
				if (tracker != current.tracker || queue != current.queue || driver != current.driver || jobs.Length != current.jobs.Length)
				{
					throw new InvalidOperationException("Animal-food job tracker or queue changed during allocation.");
				}
				for (int i = 0; i < jobs.Length; i++)
				{
					if (!jobs[i].Same(current.jobs[i]) || (i > 0 && queuedEntries[i - 1] != current.queuedEntries[i - 1]))
					{
						throw new InvalidOperationException("Animal-food job, animal or requirement changed during allocation.");
					}
				}
			}
		}

		private sealed class JobFact
		{
			internal readonly Job job;

			private readonly int loadId;

			private readonly int count;

			private readonly JobDef def;

			private readonly WorkGiverDef giver;

			private readonly Type giverClass;

			private readonly LocalTargetInfo targetA;

			private readonly LocalTargetInfo targetB;

			private readonly LocalTargetInfo targetC;

			private readonly object needs;

			private readonly object foodNeed;

			private readonly float maxLevel;

			internal readonly bool interaction;

			internal readonly Pawn animal;

			internal readonly float reserve;

			internal JobFact(Pawn handler, Job job, JobDriver driver)
			{
				this.job = job;
				if (job == null)
				{
					return;
				}
				loadId = job.loadID;
				count = job.count;
				def = job.def;
				giver = job.workGiverDef;
				giverClass = giver?.giverClass;
				targetA = job.targetA;
				targetB = job.targetB;
				targetC = job.targetC;
				Pawn pawn = null;
				bool flag = def == JobDefOf.TakeInventory && AnimalFetchContexts.TryGetAnimal(handler, job, out pawn);
				interaction = flag || (giverClass != null && typeof(WorkGiver_InteractAnimal).IsAssignableFrom(giverClass)) || def == JobDefOf.Tame || def == JobDefOf.Train || driver is JobDriver_InteractAnimal;
				if (!interaction)
				{
					return;
				}
				animal = (flag ? pawn : (targetA.Thing as Pawn));
				if (animal == null)
				{
					reserve = 2.4f;
					return;
				}
				needs = animal.needs;
				foodNeed = animal.needs?.food;
				if (foodNeed != null)
				{
					maxLevel = animal.needs.food.MaxLevel;
					RequireFinite(maxLevel);
					float num = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal);
					RequireFinite(num);
					if (maxLevel < 0f || num < 0f)
					{
						throw new InvalidOperationException("Animal-food requirement is negative.");
					}
					reserve = Math.Min(2.4f, num * 8f);
					RequireFinite(reserve);
				}
			}

			internal bool Same(JobFact other)
			{
				if (job == other.job && loadId == other.loadId && count == other.count && def == other.def && giver == other.giver && giverClass == other.giverClass && targetA.Equals(other.targetA) && targetB.Equals(other.targetB) && targetC.Equals(other.targetC) && interaction == other.interaction && animal == other.animal && needs == other.needs && foodNeed == other.foodNeed && maxLevel == other.maxLevel)
				{
					return reserve == other.reserve;
				}
				return false;
			}
		}

		private readonly Pawn pawn;

		private readonly IReadOnlyList<Thing> sources;

		private readonly RequestFacts facts;

		private readonly float[][] pools;

		private readonly FoodObservation[][] observations;

		private readonly bool[] unresolvedEligible;

		private readonly float[] unresolvedNutrition;

		private readonly bool observeCandidatesOnly;

		private readonly bool[][] observedCandidates;

		private readonly bool[] unresolvedObserved;

		internal AnimalFoodSnapshot(Pawn pawn, IReadOnlyList<Thing> sources)
			: this(pawn, sources, observeCandidatesOnly: false)
		{
		}

		internal AnimalFoodSnapshot(Pawn pawn, IReadOnlyList<Thing> sources, bool observeCandidatesOnly)
		{
			this.pawn = pawn;
			this.sources = sources;
			this.observeCandidatesOnly = observeCandidatesOnly;
			facts = new RequestFacts(pawn);
			pools = new float[facts.jobs.Length][];
			observations = new FoodObservation[facts.jobs.Length][];
			observedCandidates = new bool[facts.jobs.Length][];
			bool flag = false;
			for (int i = 0; i < facts.jobs.Length; i++)
			{
				JobFact jobFact = facts.jobs[i];
				if (!jobFact.interaction || jobFact.reserve == 0f)
				{
					continue;
				}
				if (jobFact.animal == null)
				{
					flag = true;
					continue;
				}
				if (observeCandidatesOnly)
				{
					observations[i] = new FoodObservation[sources.Count];
					observedCandidates[i] = new bool[sources.Count];
					continue;
				}
				float[] array = new float[sources.Count];
				FoodObservation[] array2 = new FoodObservation[sources.Count];
				for (int j = 0; j < sources.Count; j++)
				{
					array2[j] = ObserveFood(sources[j], pawn, jobFact.animal);
					array[j] = (array2[j].eligible ? Math.Max(0f, array2[j].nutrition) : 0f);
				}
				pools[i] = array;
				observations[i] = array2;
			}
			unresolvedEligible = new bool[sources.Count];
			unresolvedNutrition = new float[sources.Count];
			unresolvedObserved = new bool[sources.Count];
			if (flag && !observeCandidatesOnly)
			{
				for (int k = 0; k < sources.Count; k++)
				{
					unresolvedEligible[k] = AnimalInteractFood.IsInteractFood(sources[k].def);
					if (unresolvedEligible[k])
					{
						float statValue = sources[k].GetStatValue(StatDefOf.Nutrition);
						RequireFinite(statValue);
						unresolvedNutrition[k] = statValue;
					}
				}
			}
			facts.Validate(pawn);
		}

		internal int KeepCount(int candidate, IReadOnlyList<int> counts)
		{
			int num = counts[candidate];
			int num2 = num;
			for (int i = 0; i < facts.jobs.Length; i++)
			{
				JobFact jobFact = facts.jobs[i];
				if (!jobFact.interaction || jobFact.reserve == 0f)
				{
					continue;
				}
				int val;
				if (jobFact.animal == null)
				{
					if (observeCandidatesOnly)
					{
						ObserveUnresolvedCandidate(candidate);
					}
					if (!unresolvedEligible[candidate])
					{
						continue;
					}
					val = AnimalFoodReserveMath.MaximumRemovable(jobFact.reserve, new int[1] { num }, new float[1] { Math.Max(0f, unresolvedNutrition[candidate]) }, 0);
				}
				else
				{
					if (observeCandidatesOnly && pools[i] == null && !ObserveCandidatePool(i, candidate))
					{
						continue;
					}
					val = AnimalFoodReserveMath.MaximumRemovable(jobFact.reserve, counts, pools[i], candidate);
				}
				num2 = Math.Min(num2, val);
			}
			return num - num2;
		}

		internal void Validate()
		{
			if (observeCandidatesOnly)
			{
				ValidateReachedCandidates();
				return;
			}
			AnimalFoodSnapshot animalFoodSnapshot = new AnimalFoodSnapshot(pawn, sources);
			facts.RequireSame(animalFoodSnapshot.facts);
			for (int i = 0; i < pools.Length; i++)
			{
				if (pools[i] == null != (animalFoodSnapshot.pools[i] == null))
				{
					throw new InvalidOperationException("Animal-food request pool changed during allocation.");
				}
				if (pools[i] == null)
				{
					continue;
				}
				for (int j = 0; j < sources.Count; j++)
				{
					if (!observations[i][j].Same(animalFoodSnapshot.observations[i][j]))
					{
						throw new InvalidOperationException("Animal-food eligibility or nutrition changed during allocation.");
					}
				}
			}
			for (int k = 0; k < sources.Count; k++)
			{
				if (unresolvedEligible[k] != animalFoodSnapshot.unresolvedEligible[k] || unresolvedNutrition[k] != animalFoodSnapshot.unresolvedNutrition[k])
				{
					throw new InvalidOperationException("Unresolved animal-food inputs changed during allocation.");
				}
			}
		}

		private bool ObserveCandidatePool(int requestIndex, int candidate)
		{
			Pawn animal = facts.jobs[requestIndex].animal;
			if (!observedCandidates[requestIndex][candidate])
			{
				observations[requestIndex][candidate] = ObserveFood(sources[candidate], pawn, animal);
				observedCandidates[requestIndex][candidate] = true;
			}
			FoodObservation foodObservation = observations[requestIndex][candidate];
			if (!foodObservation.eligible || foodObservation.nutrition <= 0f)
			{
				return false;
			}
			float[] array = new float[sources.Count];
			FoodObservation[] array2 = new FoodObservation[sources.Count];
			for (int i = 0; i < sources.Count; i++)
			{
				array2[i] = ObserveFood(sources[i], pawn, animal);
				if (observedCandidates[requestIndex][i] && !observations[requestIndex][i].Same(array2[i]))
				{
					throw new InvalidOperationException("Animal-food candidate changed for request " + requestIndex + " at inventory index " + i + ".");
				}
				array[i] = (array2[i].eligible ? Math.Max(0f, array2[i].nutrition) : 0f);
			}
			observations[requestIndex] = array2;
			pools[requestIndex] = array;
			return true;
		}

		private void ObserveUnresolvedCandidate(int candidate)
		{
			if (!unresolvedObserved[candidate])
			{
				FoodObservation foodObservation = ObserveUnresolvedFood(sources[candidate]);
				unresolvedEligible[candidate] = foodObservation.eligible;
				unresolvedNutrition[candidate] = foodObservation.nutrition;
				unresolvedObserved[candidate] = true;
			}
		}

		private static FoodObservation ObserveUnresolvedFood(Thing thing)
		{
			if (!AnimalInteractFood.IsInteractFood(thing.def))
			{
				return default(FoodObservation);
			}
			float statValue = thing.GetStatValue(StatDefOf.Nutrition);
			RequireFinite(statValue);
			return new FoodObservation(eligible: true, statValue);
		}

		private void ValidateReachedCandidates()
		{
			facts.Validate(pawn);
			for (int i = 0; i < observations.Length; i++)
			{
				if (observations[i] == null)
				{
					continue;
				}
				for (int j = 0; j < sources.Count; j++)
				{
					if ((pools[i] != null || observedCandidates[i][j]) && !observations[i][j].Same(ObserveFood(sources[j], pawn, facts.jobs[i].animal)))
					{
						throw new InvalidOperationException("Animal-food eligibility or nutrition changed for request " + i + " at inventory index " + j + ".");
					}
				}
			}
			for (int k = 0; k < sources.Count; k++)
			{
				if (unresolvedObserved[k])
				{
					FoodObservation foodObservation = ObserveUnresolvedFood(sources[k]);
					if (unresolvedEligible[k] != foodObservation.eligible || unresolvedNutrition[k] != foodObservation.nutrition)
					{
						throw new InvalidOperationException("Unresolved animal-food inputs changed at inventory index " + k + ".");
					}
				}
			}
			facts.Validate(pawn);
		}

		internal void ValidateRequests()
		{
			facts.Validate(pawn);
		}

		private static FoodObservation ObserveFood(Thing thing, Pawn handler, Pawn animal)
		{
			if (!thing.def.IsNutritionGivingIngestible || !thing.IngestibleNow || !animal.WillEat(thing, handler) || (int)thing.def.ingestible.preferability < 1 || (int)thing.def.ingestible.preferability > 5 || thing.def.IsDrug)
			{
				return default(FoodObservation);
			}
			float num = FoodUtility.NutritionForEater(animal, thing);
			RequireFinite(num);
			return new FoodObservation(num * (float)thing.stackCount >= 0f, num);
		}

		internal static float FreshReserve(Pawn pawn)
		{
			RequestFacts requestFacts = new RequestFacts(pawn);
			float num = 0f;
			JobFact[] jobs = requestFacts.jobs;
			foreach (JobFact jobFact in jobs)
			{
				num = Math.Max(num, jobFact.reserve);
			}
			requestFacts.Validate(pawn);
			return num;
		}

		private static void RequireFinite(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				throw new InvalidOperationException("Animal-food input is not finite.");
			}
		}
	}
}
