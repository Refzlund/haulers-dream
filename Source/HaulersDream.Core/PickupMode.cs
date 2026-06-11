namespace HaulersDream.Core
{
    /// <summary>How a freshly-produced yield gets into the producing pawn's inventory.</summary>
    public enum PickupMode
    {
        /// <summary>(Default, realistic) the yield drops on the floor; the producer then picks it up.</summary>
        DropThenHaul,

        /// <summary>The yield goes straight into inventory without ever touching the ground.</summary>
        DirectToInventory
    }
}
