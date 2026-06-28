namespace HaulersDream.Core
{
    /// <summary>The kind of work that produced a yield — used for the per-type toggles.</summary>
    public enum HaulSourceType
    {
        Harvest,     // plant harvest / cut — crops, berries (JobDriver_PlantWork)
        Mining,      // mineables: ore / resources specifically — NOT stone chunks (JobDriver_Mine)
        DeepDrill,   // deep drill portions (JobDriver_OperateDeepDrill)
        Deconstruct, // building deconstruction leavings (JobDriver_Deconstruct)
        Animal,      // milk / shear / wool (JobDriver_GatherAnimalBodyResources)
        Strip,       // gear removed by a strip order on a pawn or corpse (JobDriver_Strip)
        Uninstall,   // the minified building dropped by an uninstall order (JobDriver_Uninstall)
        // --- appended (issue #79) — kept after the existing values so their integer positions don't shift ---
        Logging,     // wood / cacti yields from felling trees (split out of Harvest)
        Chunks,      // stone / slag chunks from mining (split out of Mining)
        // --- appended — kept last so existing integer positions don't shift ---
        Fishing      // catch from a colonist fishing spot (JobDriver_Fish; not animal self-feeding) — Odyssey
    }
}
