using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    public sealed class ShopRollService
    {
        private readonly List<ShopProductDefinition> m_Candidates = new List<ShopProductDefinition>();

        public ShopProductDefinition[] Roll(ShopDefinition shop, int seed)
        {
            if (shop == null || shop.Products.Length == 0) return Array.Empty<ShopProductDefinition>();
            m_Candidates.Clear();
            for (int i = 0; i < shop.Products.Length; i++)
                if (shop.Products[i] != null) m_Candidates.Add(shop.Products[i]);
            if (m_Candidates.Count == 0) return Array.Empty<ShopProductDefinition>();

            int count = shop.AllowDuplicateProducts
                ? shop.OfferCount
                : Math.Min(shop.OfferCount, m_Candidates.Count);
            var result = new ShopProductDefinition[count];
            var random = new Random(seed);
            for (int i = 0; i < count; i++)
            {
                int selected = SelectWeightedIndex(random, m_Candidates);
                result[i] = m_Candidates[selected];
                if (!shop.AllowDuplicateProducts) m_Candidates.RemoveAt(selected);
            }
            return result;
        }

        private static int SelectWeightedIndex(Random random, List<ShopProductDefinition> candidates)
        {
            double total = 0d;
            for (int i = 0; i < candidates.Count; i++) total += candidates[i].RollWeight;
            double roll = random.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= candidates[i].RollWeight;
                if (roll <= 0d) return i;
            }
            return candidates.Count - 1;
        }
    }
}
