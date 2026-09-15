using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public readonly struct PactStack
    {
        public uint PactId { get; }
        public int Stacks { get; }

        public PactStack(uint pactId, int stacks)
        {
            PactId = pactId;
            Stacks = Math.Max(0, stacks);
        }
    }

    public sealed class PactInventory
    {
        private readonly Dictionary<uint, int> m_Stacks = new Dictionary<uint, int>();
        public uint Revision { get; private set; }
        public int DistinctCount => m_Stacks.Count;

        public int GetStacks(uint pactId) => m_Stacks.TryGetValue(pactId, out int stacks) ? stacks : 0;
        public bool Contains(uint pactId) => GetStacks(pactId) > 0;

        public bool CanAdd(PactDefinition definition)
        {
            if (definition == null) return false;
            int current = GetStacks(definition.PactId);
            return current == 0 || definition.Repeatable && current < definition.MaxStacks;
        }

        public bool TryAddOrStack(PactDefinition definition, out PactStack result)
        {
            result = default;
            if (!CanAdd(definition)) return false;
            int next = Math.Min(definition.MaxStacks, GetStacks(definition.PactId) + 1);
            m_Stacks[definition.PactId] = next;
            Revision++;
            result = new PactStack(definition.PactId, next);
            return true;
        }

        public void Restore(uint pactId, int stacks)
        {
            if (pactId == 0 || stacks <= 0) return;
            m_Stacks[pactId] = stacks;
            Revision++;
        }

        public void Capture(List<PactStack> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            foreach (KeyValuePair<uint, int> pair in m_Stacks)
                output.Add(new PactStack(pair.Key, pair.Value));
            output.Sort((left, right) => left.PactId.CompareTo(right.PactId));
        }

        public void Clear()
        {
            if (m_Stacks.Count == 0) return;
            m_Stacks.Clear();
            Revision++;
        }
    }
}
