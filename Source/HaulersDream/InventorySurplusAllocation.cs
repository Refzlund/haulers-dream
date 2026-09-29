using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace HaulersDream
{
	internal sealed class InventorySurplusAllocation
	{
		private sealed class Entry
		{
			internal readonly Thing Thing;

			internal readonly ThingDef Def;

			internal readonly ThingDef Stuff;

			internal readonly DefStuff Pair;

			internal readonly int OriginalCount;

			internal int Remaining;

			internal Entry(Thing thing, ThingDef def, ThingDef stuff, int count)
			{
				Thing = thing;
				Def = def;
				Stuff = stuff;
				Pair = new DefStuff(def, stuff);
				OriginalCount = (Remaining = count);
			}
		}

		private readonly struct DefStuff : IEquatable<DefStuff>
		{
			private readonly ThingDef def;

			private readonly ThingDef stuff;

			internal DefStuff(ThingDef def, ThingDef stuff)
			{
				this.def = def;
				this.stuff = stuff;
			}

			public bool Equals(DefStuff other)
			{
				if (def == other.def)
				{
					return stuff == other.stuff;
				}
				return false;
			}

			public override bool Equals(object obj)
			{
				if (obj is DefStuff other)
				{
					return Equals(other);
				}
				return false;
			}

			public override int GetHashCode()
			{
				return (RuntimeHelpers.GetHashCode(def) * 397) ^ ((stuff != null) ? RuntimeHelpers.GetHashCode(stuff) : 0);
			}
		}

		private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
		{
			internal static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

			public bool Equals(T first, T second)
			{
				return first == second;
			}

			public int GetHashCode(T value)
			{
				return RuntimeHelpers.GetHashCode(value);
			}
		}

		private readonly Pawn pawn;

		private readonly Pawn_InventoryTracker tracker;

		private readonly ThingOwner<Thing> owner;

		private readonly CompHauledToInventory comp;

		private readonly Entry[] entries;

		private readonly Dictionary<Thing, Entry> byThing = new Dictionary<Thing, Entry>(ReferenceComparer<Thing>.Instance);

		private readonly Dictionary<ThingDef, int> byDef = new Dictionary<ThingDef, int>(ReferenceComparer<ThingDef>.Instance);

		private readonly Dictionary<DefStuff, int> byPair = new Dictionary<DefStuff, int>();

		private string failure;

		private bool allocating;

		private PackedFoodSnapshot packedFood;

		private AnimalFoodSnapshot animalFood;

		private readonly PackedFoodSnapshot.Candidate[] packedCandidates;

		private readonly bool?[] animalCandidates;

		internal IReadOnlyList<Thing> Sources { get; }

		private InventorySurplusAllocation(Pawn pawn, CompHauledToInventory comp)
		{
			this.pawn = pawn;
			this.comp = comp;
			tracker = pawn.inventory;
			owner = tracker.innerContainer;
			ValidateOwnerChain();
			entries = new Entry[owner.Count];
			packedCandidates = new PackedFoodSnapshot.Candidate[entries.Length];
			animalCandidates = new bool?[entries.Length];
			Thing[] array = new Thing[entries.Length];
			for (int i = 0; i < entries.Length; i++)
			{
				Thing thing = owner[i];
				if (thing == null || thing.Destroyed || thing.Spawned || thing.def == null || thing.stackCount <= 0 || thing.holdingOwner != owner || byThing.ContainsKey(thing))
				{
					throw new InvalidOperationException("Inventory allocation requires distinct live inventory members.");
				}
				Entry entry = new Entry(thing, thing.def, thing.Stuff, thing.stackCount);
				entries[i] = entry;
				array[i] = thing;
				byThing.Add(thing, entry);
				byDef.TryGetValue(entry.Def, out var value);
				byPair.TryGetValue(entry.Pair, out var value2);
				checked
				{
					byDef[entry.Def] = value + entry.OriginalCount;
					byPair[entry.Pair] = value2 + entry.OriginalCount;
				}
			}
			Sources = Array.AsReadOnly(array);
			ValidatePhysicalInventory();
		}

		internal static bool TryCreate(Pawn pawn, CompHauledToInventory comp, out InventorySurplusAllocation allocation, out string invalidReason)
		{
			allocation = null;
			invalidReason = null;
			try
			{
				if (pawn?.inventory?.innerContainer == null)
				{
					throw new InvalidOperationException("Pawn inventory is unavailable.");
				}
				CompHauledToInventory compHauledToInventory = pawn.GetComp<CompHauledToInventory>();
				if (comp != null && comp != compHauledToInventory)
				{
					throw new InvalidOperationException("Inventory allocation component is not the pawn's current component.");
				}
				allocation = new InventorySurplusAllocation(pawn, comp ?? compHauledToInventory);
				return true;
			}
			catch (Exception exception)
			{
				invalidReason = DescribeFailure(exception);
				return false;
			}
		}

		internal bool TryAllocate(Thing source, int maximumUnits, out int allocatedUnits, out string invalidReason)
		{
			allocatedUnits = 0;
			invalidReason = failure;
			if (failure != null)
			{
				return false;
			}
			if (allocating)
			{
				failure = (invalidReason = "An inventory allocation reader reentered the same pass.");
				return false;
			}
			allocating = true;
			checked
			{
				try
				{
					ValidatePhysicalInventory();
					ValidateAdvisoryFacts();
					ValidatePhysicalInventory();
					if (failure != null)
					{
						invalidReason = failure;
						return false;
					}
					if (source == null || !byThing.TryGetValue(source, out var value))
					{
						throw new InvalidOperationException("Allocation source is not an original inventory member.");
					}
					if (maximumUnits <= 0 || value.Remaining == 0)
					{
						return true;
					}
					int val = InventorySurplus.SurplusForAllocation(pawn, source, comp, this);
					ValidatePhysicalInventory();
					ValidateAdvisoryFacts();
					ValidatePhysicalInventory();
					if (failure != null)
					{
						invalidReason = failure;
						return false;
					}
					int num = Math.Min(Math.Min(Math.Max(0, val), value.Remaining), maximumUnits);
					if (num == 0)
					{
						return true;
					}
					int num2 = value.Remaining - num;
					int num3 = byDef[value.Def] - num;
					int num4 = byPair[value.Pair] - num;
					if (num2 < 0 || num3 < 0 || num4 < 0)
					{
						throw new InvalidOperationException("Inventory allocation counters disagree.");
					}
					value.Remaining = num2;
					byDef[value.Def] = num3;
					byPair[value.Pair] = num4;
					allocatedUnits = num;
					return true;
				}
				catch (Exception exception)
				{
					failure = failure ?? DescribeFailure(exception);
					invalidReason = failure;
					return false;
				}
				finally
				{
					allocating = false;
				}
			}
		}

		internal int RemainingCount(Thing source)
		{
			return byThing[source].Remaining;
		}

		internal int RemainingCountOfDef(ThingDef def)
		{
			if (!byDef.TryGetValue(def, out var value))
			{
				return 0;
			}
			return value;
		}

		internal int RemainingCountOfPair(ThingDef def, ThingDef stuff)
		{
			if (!byPair.TryGetValue(new DefStuff(def, stuff), out var value))
			{
				return 0;
			}
			return value;
		}

		internal int PackedFoodKeepCount(Thing source)
		{
			int num = IndexOf(source);
			PackedFoodSnapshot.Candidate candidate = new PackedFoodSnapshot.Candidate(pawn, source);
			if (packedCandidates[num] != null && !packedCandidates[num].Same(candidate))
			{
				throw new InvalidOperationException("Packed-food candidate changed at inventory index " + num + ".");
			}
			packedCandidates[num] = candidate;
			if (!candidate.Eligible)
			{
				return 0;
			}
			if (packedFood == null)
			{
				packedFood = new PackedFoodSnapshot(pawn, Sources);
			}
			return packedFood.KeepCount(num, RemainingCounts());
		}

		internal int AnimalFoodKeepCount(Thing source)
		{
			int num = IndexOf(source);
			bool flag = AnimalInteractFood.IsInteractFood(source.def);
			if (animalCandidates[num].HasValue && animalCandidates[num].Value != flag)
			{
				throw new InvalidOperationException("Animal-food candidate changed at inventory index " + num + ".");
			}
			animalCandidates[num] = flag;
			if (!flag)
			{
				return 0;
			}
			if (animalFood == null)
			{
				animalFood = new AnimalFoodSnapshot(pawn, Sources, observeCandidatesOnly: true);
			}
			return animalFood.KeepCount(num, RemainingCounts());
		}

		private int IndexOf(Thing source)
		{
			for (int i = 0; i < entries.Length; i++)
			{
				if (entries[i].Thing == source)
				{
					return i;
				}
			}
			throw new InvalidOperationException("Food source is not an original inventory member.");
		}

		private int[] RemainingCounts()
		{
			int[] array = new int[entries.Length];
			for (int i = 0; i < entries.Length; i++)
			{
				array[i] = entries[i].Remaining;
			}
			return array;
		}

		private void ValidateAdvisoryFacts()
		{
			ValidatePackedFood();
			ValidateAnimalFood();
			if (animalFood != null)
			{
				ValidatePackedFood();
				animalFood.ValidateRequests();
			}
		}

		private void ValidatePackedFood()
		{
			for (int i = 0; i < packedCandidates.Length; i++)
			{
				if (packedCandidates[i] != null && !packedCandidates[i].Same(new PackedFoodSnapshot.Candidate(pawn, Sources[i])))
				{
					throw new InvalidOperationException("Packed-food candidate changed at inventory index " + i + ".");
				}
			}
			packedFood?.Validate();
		}

		private void ValidateAnimalFood()
		{
			for (int i = 0; i < animalCandidates.Length; i++)
			{
				if (animalCandidates[i].HasValue && animalCandidates[i].Value != AnimalInteractFood.IsInteractFood(Sources[i].def))
				{
					throw new InvalidOperationException("Animal-food candidate changed at inventory index " + i + ".");
				}
			}
			animalFood?.Validate();
		}

		private void ValidateOwnerChain()
		{
			if (pawn.inventory != tracker || tracker.pawn != pawn || tracker.innerContainer != owner || owner.Owner != tracker || pawn.GetComp<CompHauledToInventory>() != comp || (comp != null && comp.parent != pawn))
			{
				throw new InvalidOperationException("Inventory allocation owner or component chain changed.");
			}
		}

		private void ValidatePhysicalInventory()
		{
			ValidateOwnerChain();
			if (owner.Count != entries.Length)
			{
				throw new InvalidOperationException("Inventory allocation membership changed.");
			}
			for (int i = 0; i < entries.Length; i++)
			{
				Entry entry = entries[i];
				Thing thing = entry.Thing;
				if (owner[i] != thing || thing.Destroyed || thing.Spawned || thing.holdingOwner != owner || thing.def != entry.Def || thing.Stuff != entry.Stuff || thing.stackCount != entry.OriginalCount)
				{
					throw new InvalidOperationException("Inventory allocation source order, custody or physical quantities changed.");
				}
			}
		}

		private static string DescribeFailure(Exception exception)
		{
			return exception.GetType().FullName + ": " + exception.Message;
		}
	}
}
