using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    /// <summary>Deterministically resolves one loot table without depending on Unity.</summary>
    public sealed class LootRollService
    {
        public LootRollResult[] Roll(LootTableDefinition table, int seed)
        {
            if (table == null) return Array.Empty<LootRollResult>();

            var random = new Random(seed);
            var results = new List<LootRollResult>(
                table.GuaranteedDrops.Length + table.WeightedRollCount);

            for (int i = 0; i < table.GuaranteedDrops.Length; i++)
            {
                LootCandidate candidate = table.GuaranteedDrops[i];
                results.Add(new LootRollResult(candidate.EntryIndex, RollQuantity(random, candidate)));
            }

            for (int i = 0; i < table.WeightedRollCount; i++)
            {
                int selectedIndex = SelectWeightedIndex(random, table.WeightedDrops, table.NothingWeight);
                if (selectedIndex < 0) continue;

                LootCandidate candidate = table.WeightedDrops[selectedIndex];
                results.Add(new LootRollResult(candidate.EntryIndex, RollQuantity(random, candidate)));
            }

            return results.ToArray();
        }

        private static int SelectWeightedIndex(
            Random random,
            LootCandidate[] candidates,
            float nothingWeight)
        {
            double totalWeight = nothingWeight;
            for (int i = 0; i < candidates.Length; i++)
                totalWeight += candidates[i].Weight;

            if (totalWeight <= 0d) return -1;

            double roll = random.NextDouble() * totalWeight;
            if (roll < nothingWeight) return -1;
            roll -= nothingWeight;

            for (int i = 0; i < candidates.Length; i++)
            {
                roll -= candidates[i].Weight;
                if (roll < 0d) return i;
            }

            return -1;
        }

        private static int RollQuantity(Random random, in LootCandidate candidate)
        {
            if (candidate.MinQuantity >= candidate.MaxQuantity) return candidate.MinQuantity;
            int range = candidate.MaxQuantity - candidate.MinQuantity + 1;
            return candidate.MinQuantity + (int)(random.NextDouble() * range);
        }
    }
}
