using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public sealed class PactRollService
    {
        private readonly List<PactDefinition> m_Eligible = new List<PactDefinition>();
        private readonly List<PactStack> m_Owned = new List<PactStack>();

        /// <summary>
        /// 权重采样抽 3 选牌（无放回）。
        /// <paramref name="pityUnlockIds"/> + <paramref name="pityActive"/> + <paramref name="pityWeightMultiplier"/>
        /// 为「解锁契软保底」通道：激活时名单内仍合格（eligible）的契权重 ×K。
        /// 仅改变权重、不改变随机序，seed 确定性可复现保持不变。
        /// </summary>
        public uint[] Roll(PactCatalog catalog, PactInventory inventory, int optionCount, int seed,
            float luck = 0f, IReadOnlyCollection<uint> pityUnlockIds = null,
            bool pityActive = false, float pityWeightMultiplier = 1f)
        {
            if (catalog == null || inventory == null || optionCount <= 0) return Array.Empty<uint>();
            m_Eligible.Clear();
            for (int i = 0; i < catalog.All.Count; i++)
            {
                PactDefinition candidate = catalog.All[i];
                if (IsEligible(catalog, inventory, candidate)) m_Eligible.Add(candidate);
            }

            HashSet<uint> pitySet = pityActive && pityUnlockIds != null && pityWeightMultiplier > 1f
                ? new HashSet<uint>(pityUnlockIds)
                : null;

            int count = Math.Min(optionCount, m_Eligible.Count);
            var result = new uint[count];
            var random = new Random(seed);
            for (int optionIndex = 0; optionIndex < count; optionIndex++)
            {
                double totalWeight = 0d;
                for (int i = 0; i < m_Eligible.Count; i++)
                    totalWeight += GetWeight(m_Eligible[i], luck, pitySet, pityWeightMultiplier);

                int selectedIndex = 0;
                if (totalWeight > 0d)
                {
                    double roll = random.NextDouble() * totalWeight;
                    for (int i = 0; i < m_Eligible.Count; i++)
                    {
                        roll -= GetWeight(m_Eligible[i], luck, pitySet, pityWeightMultiplier);
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

        private static double GetWeight(PactDefinition definition, float luck,
            HashSet<uint> pitySet, float pityMultiplier)
        {
            float rarityBias = 1f + Math.Max(0f, luck) * Math.Max(0, definition.Rarity - 1) * 0.1f;
            double weight = definition.BaseWeight * rarityBias;
            if (pitySet != null && pitySet.Contains(definition.PactId))
                weight *= pityMultiplier;
            return Math.Max(0.0001d, weight);
        }
    }
}
