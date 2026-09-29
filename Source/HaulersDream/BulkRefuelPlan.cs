using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HaulersDream
{
	internal sealed class BulkRefuelPlan
	{
		internal sealed class Portion
		{
			internal readonly Thing Source;

			internal readonly int Count;

			internal readonly float UnitMass;

			internal Portion(Thing source, int count, float unitMass)
			{
				Source = source;
				Count = count;
				UnitMass = unitMass;
			}
		}

		internal readonly Pawn Pawn;

		internal readonly Thing Target;

		internal readonly CompRefuelable Channel;

		internal readonly ThingOwner<Thing> Owner;

		internal readonly bool PlayerOrder;

		internal readonly int Demand;

		internal readonly IReadOnlyList<Portion> Held;

		internal readonly int HeldCount;

		internal readonly IReadOnlyList<Portion> Ground;

		internal readonly int GroundCount;

		internal BulkRefuelPlan(Pawn pawn, Thing target, CompRefuelable channel, ThingOwner<Thing> owner, bool playerOrder, int demand, List<Portion> held, int heldCount, List<Portion> ground, int groundCount)
		{
			Pawn = pawn;
			Target = target;
			Channel = channel;
			Owner = owner;
			PlayerOrder = playerOrder;
			Demand = demand;
			Held = Array.AsReadOnly(held.ToArray());
			HeldCount = heldCount;
			Ground = Array.AsReadOnly(ground.ToArray());
			GroundCount = groundCount;
		}

		internal Job CreateJob()
		{
			Job job = JobMaker.MakeJob(HaulersDreamDefOf.HaulersDream_BulkRefuel, Target);
			job.targetQueueB = new List<LocalTargetInfo>(Ground.Count);
			job.countQueue = new List<int>(Ground.Count);
			for (int i = 0; i < Ground.Count; i++)
			{
				job.targetQueueB.Add(new LocalTargetInfo(Ground[i].Source));
				job.countQueue.Add(Ground[i].Count);
			}
			job.count = 1;
			job.playerForced = PlayerOrder;
			return job;
		}
	}
}
