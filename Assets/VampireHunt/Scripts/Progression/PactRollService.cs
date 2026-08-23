using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public sealed class PactRollService
    {
        private readonly List<PactDefinition> m_Eligible = new List<PactDefinition>();
        private readonly List<PactStack> m_Owned = new List<PactStack>();

        public uint[] Roll(PactCatalog catalog, PactInventory inventory, int optionCount, int seed, float luck = 0f)
        {
            if (catalog == null || inventory == null || optionCount <= 0) return Array.Empty<uint>();
            m_Eligible.Clear();
            for (int i = 0; i < catalog.All.Count; i++)
            {
                PactDefinition candidate = catalog.All[i];
                if (IsEligible(catalog, inventory, candidate)) m_Eligible.Add(candidate);
            }

            int count = Math.Min(optionCount, m_Eligible.Count);
            var result = new uint[count];
            var random = new Random(seed);
            for (int optionIndex = 0; optionIndex < count; optionIndex++)
            {
                double totalWeight = 0d;
                for (int i = 0; i < m_Eligible.Count; i++)
                    totalWeight += GetWeight(m_Eligible[i], luck);

                int selectedIndex = 0;
                if (totalWeight > 0d)
                {
                    double roll = random.NextDouble() * totalWeight;
                    for (int i = 0; i < m_Eligible.Count; i++)
                    {
                        roll -= GetWeight(m_Eligible[i], luck);
                        if (roll > 0d) continue;
                        selectedIndex = i;
                        break;
                    }
                }
                result[optionIndex] = m_Eligible[selectedIndex].PactId;
                m_Eligible.RemoveAt(selectedIndex);
            }
            return result;
        }

        public bool IsEligible(PactCatalog catalog, PactInventory inventory, PactDefinition candidate)
        {
            if (catalog == null || inventory == null || candidate == null || !inventory.CanAdd(candidate)) return false;
            for (int i = 0; i < candidate.Prerequisites.Length; i++)
                if (!inventory.Contains(candidate.Prerequisites[i])) return false;
            for (int i = 0; i < candidate.Exclusions.Length; i++)
                if (inventory.Contains(candidate.Exclusions[i])) return false;

            inventory.Capture(m_Owned);
            for (int i = 0; i < m_Owned.Count; i++)
            {
                if (!catalog.TryGet(m_Owned[i].PactId, out PactDefinition owned)) continue;
                for (int exclusionIndex = 0; exclusionIndex < owned.Exclusions.Length; exclusionIndex++)
                    if (owned.Exclusions[exclusionIndex] == candidate.PactId) return false;
            }
            return true;
        }

        private static double GetWeight(PactDefinition definition, float luck)
        {
            float rarityBias = 1f + Math.Max(0f, luck) * Math.Max(0, definition.Rarity - 1) * 0.1f;
            return Math.Max(0.0001d, definition.BaseWeight * rarityBias);
        }
    }
}
