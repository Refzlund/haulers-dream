using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal sealed class AnimalFetchContext : IExposable
	{
		internal Game game;

		internal Pawn handler;

		internal Pawn animal;

		internal Thing food;

		internal int jobId = -1;

		private int version = 1;

		public AnimalFetchContext()
		{
		}

		internal AnimalFetchContext(Game game, int jobId, Pawn handler, Pawn animal, Thing food)
		{
			this.game = game;
			this.jobId = jobId;
			this.handler = handler;
			this.animal = animal;
			this.food = food;
		}

		internal bool Matches(Job job)
		{
			if (version == 1 && game != null && game == Current.Game && job != null && jobId >= 0 && job.loadID == jobId && job.def == JobDefOf.TakeInventory && handler != null && !handler.Destroyed && animal != null && !animal.Destroyed && food != null && !food.Destroyed)
			{
				return job.targetA.Thing == food;
			}
			return false;
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref version, "version", 1);
			Scribe_Values.Look(ref jobId, "jobId", -1);
			Scribe_References.Look(ref handler, "handler");
			Scribe_References.Look(ref animal, "animal");
			Scribe_References.Look(ref food, "food");
		}
	}
}
