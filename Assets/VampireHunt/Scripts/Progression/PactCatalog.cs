using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public sealed class PactCatalog
    {
        private readonly Dictionary<uint, PactDefinition> m_ById = new Dictionary<uint, PactDefinition>();
        private readonly List<PactDefinition> m_All = new List<PactDefinition>();

        public IReadOnlyList<PactDefinition> All => m_All;

        public PactCatalog(IEnumerable<PactDefinition> definitions)
        {
            if (definitions == null) return;
            foreach (PactDefinition definition in definitions)
            {
                if (definition == null) continue;
                if (m_ById.ContainsKey(definition.PactId))
                    throw new InvalidOperationException($"Duplicate PactId {definition.PactId}.");
                m_ById.Add(definition.PactId, definition);
                m_All.Add(definition);
            }
            m_All.Sort((left, right) => left.PactId.CompareTo(right.PactId));
        }

        public bool TryGet(uint pactId, out PactDefinition definition) => m_ById.TryGetValue(pactId, out definition);
    }
}
