using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public sealed class EnemyAffixRollService
    {
        private readonly List<EnemyAffixDefinition> m_Eligible = new List<EnemyAffixDefinition>();
        private readonly List<EnemyAffixStack> m_Owned = new List<EnemyAffixStack>();

        public uint[] Roll(EnemyAffixCatalog catalog, EnemyAffixSet set, int optionCount, int seed)
        {
            if (catalog == null || set == null || optionCount <= 0) return Array.Empty<uint>();
            m_Eligible.Clear();
            for (int i = 0; i < catalog.All.Count; i++)
            {
                EnemyAffixDefinition candidate = catalog.All[i];
                if (IsEligible(catalog, set, candidate)) m_Eligible.Add(candidate);
            }

            int count = Math.Min(optionCount, m_Eligible.Count);
            var result = new uint[count];
            var random = new Random(seed);
            for (int optionIndex = 0; optionIndex < count; optionIndex++)
            {
                double totalWeight = 0d;
                for (int i = 0; i < m_Eligible.Count; i++)
                    totalWeight += Math.Max(0.0001d, m_Eligible[i].BaseWeight);

                double roll = random.NextDouble() * totalWeight;
                int selectedIndex = 0;
                for (int i = 0; i < m_Eligible.Count; i++)
                {
                    roll -= Math.Max(0.0001d, m_Eligible[i].BaseWeight);
                    if (roll > 0d) continue;
                    selectedIndex = i;
                    break;
                }
                result[optionIndex] = m_Eligible[selectedIndex].AffixId;
                m_Eligible.RemoveAt(selectedIndex);
            }
            return result;
        }

        public bool IsEligible(
            EnemyAffixCatalog catalog,
            EnemyAffixSet set,
            EnemyAffixDefinition candidate)
        {
            if (catalog == null || set == null || candidate == null || !set.CanAdd(candidate)) return false;
            for (int i = 0; i < candidate.Prerequisites.Length; i++)
                if (!set.Contains(candidate.Prerequisites[i])) return false;
            for (int i = 0; i < candidate.Exclusions.Length; i++)
                if (set.Contains(candidate.Exclusions[i])) return false;

            set.Capture(m_Owned);
            for (int i = 0; i < m_Owned.Count; i++)
            {
                if (!catalog.TryGet(m_Owned[i].AffixId, out EnemyAffixDefinition owned)) continue;
                for (int exclusionIndex = 0; exclusionIndex < owned.Exclusions.Length; exclusionIndex++)
                    if (owned.Exclusions[exclusionIndex] == candidate.AffixId) return false;
            }
            return true;
        }
    }
}
