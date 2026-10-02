using System;

namespace HaulersDream.Core
{
    // Quantities are totals across trips. Delivery is a physical delta, never a native return bool.
    public static class ExplicitHaulAmount
    {
        public static int Trip(int remaining, int source, int hands, int destination)
        {
            if (remaining <= 0 || source <= 0 || hands <= 0 || destination <= 0) return 0;
            return Math.Min(Math.Min(remaining, source), Math.Min(hands, destination));
        }

        public static int Credit(int requested, int delivered, int attempted, int retained, int observed)
        {
            if (requested <= 0 || delivered < 0 || delivered > requested || attempted <= 0
                || attempted > requested - delivered || retained < 0 || retained > attempted
                || observed < 0 || observed != attempted - retained)
                throw new InvalidOperationException("Explicit haul placement lacks conserved physical custody.");
            return checked(delivered + observed);
        }
    }
}
