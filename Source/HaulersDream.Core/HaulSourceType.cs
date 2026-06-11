namespace HaulersDream.Core
{
    /// <summary>The kind of work that produced a yield — used for the per-type toggles.</summary>
    public enum HaulSourceType
    {
        Harvest,     // plant harvest / cut (JobDriver_PlantWork)
        Mining,      // mineables (JobDriver_Mine)
        DeepDrill,   // deep drill portions (JobDriver_OperateDeepDrill)
        Deconstruct, // building deconstruction leavings (JobDriver_Deconstruct)
        Animal,      // milk / shear / wool (JobDriver_GatherAnimalBodyResources)
        Strip        // gear removed by a strip order on a pawn or corpse (JobDriver_Strip)
    }
}
