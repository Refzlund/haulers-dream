namespace HaulersDream.Core
{
    /// <summary>What Hauler's Dream does with a freshly produced work result of a given category.</summary>
    public enum YieldBehavior
    {
        Disabled,          // 0: HD does nothing — vanilla places it on the ground (a hauler may take it later)
        DropThenHaul,      // 1: it drops on the floor, then the producing pawn scoops it into its load
        DirectToInventory  // 2: it goes straight into the producing pawn's inventory, no floor step
    }
}
