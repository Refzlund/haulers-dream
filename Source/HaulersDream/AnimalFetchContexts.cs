using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal static class AnimalFetchContexts
	{
		private const string SaveKey = "haulersDreamAnimalFetchContext";

		private static readonly ConditionalWeakTable<Job, AnimalFetchContext> contexts = new ConditionalWeakTable<Job, AnimalFetchContext>();

		internal static void Remember(Job job, Pawn handler, Pawn animal)
		{
			if (job != null && job.def == JobDefOf.TakeInventory && job.loadID >= 0 && handler != null && animal != null && job.targetA.Thing != null && Current.Game != null)
			{
				AnimalFetchContext animalFetchContext = new AnimalFetchContext(Current.Game, job.loadID, handler, animal, job.targetA.Thing);
				if (animalFetchContext.Matches(job))
				{
					Store(job, animalFetchContext);
				}
			}
		}

		internal static bool TryGetAnimal(Pawn handler, Job job, out Pawn animal)
		{
			animal = null;
			if (job == null || !contexts.TryGetValue(job, out var value) || !value.Matches(job) || value.handler != handler)
			{
				return false;
			}
			animal = value.animal;
			return true;
		}

		internal static void Forget(Job job)
		{
			if (job != null)
			{
				contexts.Remove(job);
			}
		}

		internal static void Copy(Job original, Job clone)
		{
			if (original != null && clone != null && original != clone && contexts.TryGetValue(original, out var value) && value.Matches(original))
			{
				AnimalFetchContext animalFetchContext = new AnimalFetchContext(value.game, value.jobId, value.handler, value.animal, value.food);
				if (animalFetchContext.Matches(clone))
				{
					Store(clone, animalFetchContext);
				}
			}
		}

		internal static void Expose(Job job)
		{
			if (job == null)
			{
				return;
			}
			LoadSaveMode mode = Scribe.mode;
			if (mode != LoadSaveMode.Saving && mode != LoadSaveMode.LoadingVars && mode != LoadSaveMode.ResolvingCrossRefs && mode != LoadSaveMode.PostLoadInit)
			{
				return;
			}
			contexts.TryGetValue(job, out var value);
			switch (mode)
			{
			case LoadSaveMode.Saving:
				if (value != null && value.Matches(job))
				{
					Scribe_Deep.Look(ref value, "haulersDreamAnimalFetchContext");
				}
				return;
			case LoadSaveMode.LoadingVars:
				value = null;
				break;
			}
			Scribe_Deep.Look(ref value, "haulersDreamAnimalFetchContext");
			if (value == null)
			{
				Forget(job);
				return;
			}
			if (mode == LoadSaveMode.LoadingVars)
			{
				value.game = Current.Game;
			}
			if (mode == LoadSaveMode.PostLoadInit && !value.Matches(job))
			{
				Forget(job);
			}
			else
			{
				Store(job, value);
			}
		}

		private static void Store(Job job, AnimalFetchContext context)
		{
			contexts.Remove(job);
			contexts.Add(job, context);
		}
	}
}
