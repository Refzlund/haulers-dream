namespace HaulersDream.Core
{
    /// <summary>Maps a yield's source work-type to whether that per-type toggle is enabled.</summary>
    public static class WorkTypePolicy
    {
        public static bool IsTypeEnabled(HaulSourceType type, bool harvest, bool mining, bool deepDrill, bool deconstruct, bool animals, bool strip, bool uninstall)
        {
            switch (type)
            {
                case HaulSourceType.Harvest: return harvest;
                case HaulSourceType.Mining: return mining;
                case HaulSourceType.DeepDrill: return deepDrill;
                case HaulSourceType.Deconstruct: return deconstruct;
                case HaulSourceType.Animal: return animals;
                case HaulSourceType.Strip: return strip;
                case HaulSourceType.Uninstall: return uninstall;
                default: return false;
            }
        }
    }
}
