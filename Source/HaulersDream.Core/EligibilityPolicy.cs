namespace HaulersDream.Core
{
    /// <summary>
    /// Decides whether a pawn may scoop its own yields, given facts extracted from the live Pawn
    /// (race, drafted state, hauling capability) and the relevant settings. Pure so it can be tested
    /// without a loaded game; <c>YieldRouter.IsEligible</c> pulls the primitives and calls this.
    /// </summary>
    public static class EligibilityPolicy
    {
        public static bool IsEligible(
            bool isMechanoid,
            bool isHumanlike,
            bool isDrafted,
            bool incapableOfHauling,
            bool allowMechanoids,
            bool pauseWhileDrafted,
            bool allowIncapable)
        {
            if (isMechanoid)
                return allowMechanoids; // mechs ignore drafted/incapable gating
            if (!isHumanlike)
                return false;           // animals etc. never scoop
            if (pauseWhileDrafted && isDrafted)
                return false;
            if (!allowIncapable && incapableOfHauling)
                return false;
            return true;
        }
    }
}
