using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public sealed class EnemyAffixCatalog
    {
        private readonly Dictionary<uint, EnemyAffixDefinition> m_ById =
            new Dictionary<uint, EnemyAffixDefinition>();
        private readonly List<EnemyAffixDefinition> m_All = new List<EnemyAffixDefinition>();

        public IReadOnlyList<EnemyAffixDefinition> All => m_All;

        public EnemyAffixCatalog(IEnumerable<EnemyAffixDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (EnemyAffixDefinition definition in definitions)
            {
                if (definition == null) continue;
                if (m_ById.ContainsKey(definition.AffixId))
                    throw new InvalidOperationException($"Duplicate EnemyAffixId {definition.AffixId}.");
                m_ById.Add(definition.AffixId, definition);
                m_All.Add(definition);
            }
            m_All.Sort((left, right) => left.AffixId.CompareTo(right.AffixId));
        }

        public bool TryGet(uint affixId, out EnemyAffixDefinition definition) =>
            m_ById.TryGetValue(affixId, out definition);
    }
}
