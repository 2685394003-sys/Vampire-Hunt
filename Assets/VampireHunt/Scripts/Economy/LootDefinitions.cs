using System;

namespace VampireHunt.Economy
{
    /// <summary>Unity-free candidate used by the weighted loot roller.</summary>
    public readonly struct LootCandidate
    {
        public int EntryIndex { get; }
        public float Weight { get; }
        public int MinQuantity { get; }
        public int MaxQuantity { get; }

        public LootCandidate(int entryIndex, float weight, int minQuantity, int maxQuantity)
        {
            EntryIndex = entryIndex;
            Weight = Math.Max(0f, weight);
            MinQuantity = Math.Max(1, minQuantity);
            MaxQuantity = Math.Max(MinQuantity, maxQuantity);
        }
    }

    /// <summary>Immutable rules for guaranteed and weighted world drops.</summary>
    public sealed class LootTableDefinition
    {
        public LootCandidate[] GuaranteedDrops { get; }
        public LootCandidate[] WeightedDrops { get; }
        public int WeightedRollCount { get; }
        public float NothingWeight { get; }

        public LootTableDefinition(
            LootCandidate[] guaranteedDrops,
            LootCandidate[] weightedDrops,
            int weightedRollCount,
            float nothingWeight)
        {
            GuaranteedDrops = guaranteedDrops ?? Array.Empty<LootCandidate>();
            WeightedDrops = weightedDrops ?? Array.Empty<LootCandidate>();
            WeightedRollCount = Math.Max(0, weightedRollCount);
            NothingWeight = Math.Max(0f, nothingWeight);
        }
    }

    public readonly struct LootRollResult
    {
        public int EntryIndex { get; }
        public int Quantity { get; }

        public LootRollResult(int entryIndex, int quantity)
        {
            EntryIndex = entryIndex;
            Quantity = Math.Max(1, quantity);
        }
    }
}
