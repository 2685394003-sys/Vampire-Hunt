using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [Serializable]
    public sealed class LootTableEntryAsset
    {
        [SerializeField] private LootPickupDefinitionAsset pickup;
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField, Min(1)] private int minQuantity = 1;
        [SerializeField, Min(1)] private int maxQuantity = 1;

        public LootPickupDefinitionAsset Pickup => pickup;
        public float Weight => Mathf.Max(0f, weight);
        public int MinQuantity => Mathf.Max(1, minQuantity);
        public int MaxQuantity => Mathf.Max(MinQuantity, maxQuantity);
    }

    public readonly struct LootSpawnRequest
    {
        public LootPickupDefinitionAsset Pickup { get; }
        public int Quantity { get; }

        public LootSpawnRequest(LootPickupDefinitionAsset pickup, int quantity)
        {
            Pickup = pickup;
            Quantity = Mathf.Max(1, quantity);
        }
    }

    [CreateAssetMenu(fileName = "LootTable", menuName = "Vampire Hunt/Loot/Loot Table")]
    public sealed class LootTableAsset : ScriptableObject
    {
        [SerializeField] private string stableId = "loot.enemy";
        [SerializeField] private LootTableEntryAsset[] guaranteedDrops =
            Array.Empty<LootTableEntryAsset>();
        [SerializeField, Min(0)] private int weightedRollCount = 1;
        [SerializeField, Min(0f)] private float nothingWeight;
        [SerializeField] private LootTableEntryAsset[] weightedDrops =
            Array.Empty<LootTableEntryAsset>();

        private readonly LootRollService m_RollService = new LootRollService();

        public string StableId => stableId;

        public LootSpawnRequest[] Roll(int seed)
        {
            var resolvedEntries = new List<LootTableEntryAsset>();
            LootCandidate[] guaranteed = BuildCandidates(guaranteedDrops, resolvedEntries, false);
            LootCandidate[] weighted = BuildCandidates(weightedDrops, resolvedEntries, true);
            var definition = new LootTableDefinition(
                guaranteed,
                weighted,
                weightedRollCount,
                nothingWeight);
            LootRollResult[] rolls = m_RollService.Roll(definition, seed);
            var requests = new LootSpawnRequest[rolls.Length];

            for (int i = 0; i < rolls.Length; i++)
            {
                LootRollResult roll = rolls[i];
                LootTableEntryAsset entry = resolvedEntries[roll.EntryIndex];
                requests[i] = new LootSpawnRequest(entry.Pickup, roll.Quantity);
            }

            return requests;
        }

        private static LootCandidate[] BuildCandidates(
            LootTableEntryAsset[] source,
            List<LootTableEntryAsset> resolvedEntries,
            bool useConfiguredWeight)
        {
            if (source == null || source.Length == 0) return Array.Empty<LootCandidate>();

            var candidates = new List<LootCandidate>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                LootTableEntryAsset entry = source[i];
                if (entry == null || entry.Pickup == null || entry.Pickup.PickupPrefab == null) continue;

                int entryIndex = resolvedEntries.Count;
                resolvedEntries.Add(entry);
                candidates.Add(new LootCandidate(
                    entryIndex,
                    useConfiguredWeight ? entry.Weight : 1f,
                    entry.MinQuantity,
                    entry.MaxQuantity));
            }

            return candidates.ToArray();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId)) stableId = name;
            weightedRollCount = Mathf.Max(0, weightedRollCount);
            nothingWeight = Mathf.Max(0f, nothingWeight);
        }
    }
}
