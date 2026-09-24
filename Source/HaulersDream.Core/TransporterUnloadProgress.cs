using System;

namespace HaulersDream.Core
{
    /// <summary>A trip earns another pull only by physically storing every unit it removed from the hold.</summary>
    public sealed class TransporterUnloadProgress
    {
        public int Pulled { get; private set; }
        public int Delivered { get; private set; }
        public bool Complete => Pulled > 0 && Delivered == Pulled;

        public TransporterUnloadProgress(int pulled = 0, int delivered = 0)
        {
            Pulled = Math.Max(0, pulled);
            Delivered = Math.Max(0, Math.Min(Pulled, delivered));
        }

        public void RecordPull(int actualMoved) { if (actualMoved > 0) Pulled = checked(Pulled + actualMoved); }

        // Both facts are required. A dropped/destroyed/stolen stack is not a completed storage delivery.
        public int RecordDelivery(int actualRemovedFromHands, int actualStored)
        {
            int credited = Math.Max(0, Math.Min(Pulled - Delivered, Math.Min(actualRemovedFromHands, actualStored)));
            Delivered += credited;
            return credited;
        }

        public bool BeginNextTrip()
        {
            if (!Complete) return false;
            Pulled = 0; Delivered = 0;
            return true;
        }
    }
}
