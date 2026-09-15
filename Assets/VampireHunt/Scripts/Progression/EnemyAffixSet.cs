using System;
using System.Collections.Generic;

namespace VampireHunt.Progression
{
    public readonly struct EnemyAffixStack
    {
        public uint AffixId { get; }
        public int Stacks { get; }

        public EnemyAffixStack(uint affixId, int stacks)
        {
            AffixId = affixId;
            Stacks = Math.Max(0, stacks);
        }
    }

    public readonly struct EnemyAffixSpawnEntry
    {
        public EnemyAffixDefinition Definition { get; }
        public int Stacks { get; }

        public EnemyAffixSpawnEntry(EnemyAffixDefinition definition, int stacks)
        {
            Definition = definition;
            Stacks = Math.Max(0, stacks);
        }
    }

    public sealed class EnemyAffixSpawnSnapshot
    {
        public static readonly EnemyAffixSpawnSnapshot Empty =
            new EnemyAffixSpawnSnapshot(Array.Empty<EnemyAffixSpawnEntry>(), 0);

        public EnemyAffixSpawnEntry[] Entries { get; }
        public uint Revision { get; }

        public EnemyAffixSpawnSnapshot(EnemyAffixSpawnEntry[] entries, uint revision)
        {
            Entries = entries ?? Array.Empty<EnemyAffixSpawnEntry>();
            Revision = revision;
        }
    }

    public sealed class EnemyAffixSet
    {
        private readonly Dictionary<uint, int> m_Stacks = new Dictionary<uint, int>();

        public uint Revision { get; private set; }
        public int DistinctCount => m_Stacks.Count;

        public int GetStacks(uint affixId) =>
            m_Stacks.TryGetValue(affixId, out int stacks) ? stacks : 0;

        public bool Contains(uint affixId) => GetStacks(affixId) > 0;

        public bool CanAdd(EnemyAffixDefinition definition)
        {
            if (definition == null) return false;
            int current = GetStacks(definition.AffixId);
            return current == 0 || definition.Repeatable && current < definition.MaxStacks;
        }

        public bool TryAddOrStack(EnemyAffixDefinition definition, out EnemyAffixStack result)
        {
            result = default;
            if (!CanAdd(definition)) return false;
            int next = Math.Min(definition.MaxStacks, GetStacks(definition.AffixId) + 1);
            m_Stacks[definition.AffixId] = next;
            Revision++;
            result = new EnemyAffixStack(definition.AffixId, next);
            return true;
        }

        public bool TryRemoveOne(uint affixId, out int stacks)
        {
            stacks = GetStacks(affixId);
            if (stacks <= 0) return false;
            stacks--;
            if (stacks == 0) m_Stacks.Remove(affixId);
            else m_Stacks[affixId] = stacks;
            Revision++;
            return true;
        }

        public void Restore(uint affixId, int stacks)
        {
            if (affixId == 0 || stacks <= 0) return;
            m_Stacks[affixId] = stacks;
            Revision++;
        }

        public void Capture(List<EnemyAffixStack> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            foreach (KeyValuePair<uint, int> pair in m_Stacks)
                output.Add(new EnemyAffixStack(pair.Key, pair.Value));
            output.Sort((left, right) => left.AffixId.CompareTo(right.AffixId));
        }
    }
}
