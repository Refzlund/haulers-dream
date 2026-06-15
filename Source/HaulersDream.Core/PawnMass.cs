namespace HaulersDream.Core
{
    /// <summary>
    /// A pawn's carry <see cref="Capacity"/> and current gear+inventory <see cref="CurrentMass"/> at a point in
    /// time — the two <c>MassUtility</c> numbers every overload decision needs, as a pure immutable value so the
    /// per-(pawn,tick) memo that holds it (<c>PawnMassCache</c>) can be unit-tested without a loaded game.
    /// </summary>
    public readonly struct PawnMass
    {
        /// <summary><c>MassUtility.Capacity(pawn)</c> — the pawn's true max carry capacity (0 = cannot carry).</summary>
        public readonly float Capacity;

        /// <summary><c>MassUtility.GearAndInventoryMass(pawn)</c> — the pawn's current worn+carried mass.</summary>
        public readonly float CurrentMass;

        public PawnMass(float capacity, float currentMass)
        {
            Capacity = capacity;
            CurrentMass = currentMass;
        }
    }
}
