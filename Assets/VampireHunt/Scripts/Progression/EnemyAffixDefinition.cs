using System;
using VampireHunt.Effects;
using VampireHunt.Enemies;

namespace VampireHunt.Progression
{
    public sealed class EnemyAffixDefinition
    {
        public uint AffixId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public float BaseWeight { get; }
        public uint[] Prerequisites { get; }
        public uint[] Exclusions { get; }
        public bool Repeatable { get; }
        public int MaxStacks { get; }
        public string[] IncludedArchetypeIds { get; }
        public string[] ExcludedArchetypeIds { get; }
        public EnemyStatModifierDefinition[] StatModifiers { get; }
        public EffectDefinition RuntimeEffects { get; }

        public EnemyAffixDefinition(
            uint affixId,
            string displayName,
            string description,
            float baseWeight,
            uint[] prerequisites,
            uint[] exclusions,
            bool repeatable,
            int maxStacks,
            string[] includedArchetypeIds,
            string[] excludedArchetypeIds,
            EnemyStatModifierDefinition[] statModifiers,
            IEffectModuleDescriptor[] effectModules)
        {
            if (affixId == 0) throw new ArgumentOutOfRangeException(nameof(affixId));
            AffixId = affixId;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            BaseWeight = Math.Max(0f, baseWeight);
            Prerequisites = prerequisites ?? Array.Empty<uint>();
            Exclusions = exclusions ?? Array.Empty<uint>();
            Repeatable = repeatable;
            MaxStacks = repeatable ? Math.Max(1, maxStacks) : 1;
            IncludedArchetypeIds = includedArchetypeIds ?? Array.Empty<string>();
            ExcludedArchetypeIds = excludedArchetypeIds ?? Array.Empty<string>();
            StatModifiers = statModifiers ?? Array.Empty<EnemyStatModifierDefinition>();
            RuntimeEffects = new EffectDefinition(
                EffectSourceKind.EnemyAffix,
                affixId,
                effectModules ?? Array.Empty<IEffectModuleDescriptor>());
        }

        public bool AppliesTo(string archetypeId)
        {
            if (string.IsNullOrWhiteSpace(archetypeId)) return false;
            for (int i = 0; i < ExcludedArchetypeIds.Length; i++)
                if (string.Equals(ExcludedArchetypeIds[i], archetypeId, StringComparison.Ordinal)) return false;
            if (IncludedArchetypeIds.Length == 0) return true;
            for (int i = 0; i < IncludedArchetypeIds.Length; i++)
                if (string.Equals(IncludedArchetypeIds[i], archetypeId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
