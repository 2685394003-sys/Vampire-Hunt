using System;

namespace VampireHunt.Progression
{
    /// <summary>Pure policy for the server-owned Scarlet cost progression.</summary>
    public static class LevelUpCostPolicy
    {
        public static float CalculateRequiredScarlet(
            float baseCost,
            float growthRate,
            int completedLevelUps)
        {
            if (float.IsNaN(baseCost) || float.IsInfinity(baseCost) || baseCost <= 0f) return 0f;

            if (baseCost >= (float)decimal.MaxValue) return float.MaxValue;

            float safeGrowthRate = float.IsNaN(growthRate) || float.IsInfinity(growthRate)
                ? 0f
                : Math.Max(0f, growthRate);
            decimal cost = (decimal)baseCost;
            decimal multiplier = 1m + (decimal)safeGrowthRate;
            int levelUps = Math.Max(0, completedLevelUps);

            for (int i = 0; i < levelUps; i++)
            {
                if (multiplier > 1m && cost > decimal.MaxValue / multiplier)
                    return float.MaxValue;

                cost = decimal.Ceiling(cost * multiplier);
            }

            return (float)decimal.Ceiling(cost);
        }
    }
}
