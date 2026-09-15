using System;
using VampireHunt.Effects;

namespace VampireHunt.Progression
{
    public enum PactTier : byte
    {
        Normal = 0,
        Advanced = 1,
        Ultimate = 2
    }

    [Flags]
    public enum PactTags : uint
    {
        None = 0,
        SwordWave = 1 << 0,
        Damage = 1 << 1,
        Projectile = 1 << 2,
        Fire = 1 << 3,
        Ice = 1 << 4,
        Survival = 1 << 5,
        Economy = 1 << 6
    }

    public sealed class PactDefinition
    {
        public uint PactId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public PactTier Tier { get; }
        public int Rarity { get; }
        public float BaseWeight { get; }
        public PactTags Tags { get; }
        public uint[] Prerequisites { get; }
        public uint[] Exclusions { get; }
        public bool Repeatable { get; }
        public int MaxStacks { get; }
        public EffectDefinition RuntimeEffects { get; }

        public PactDefinition(uint pactId, string displayName, string description, PactTier tier,
            int rarity, float baseWeight, PactTags tags, uint[] prerequisites, uint[] exclusions,
            bool repeatable, int maxStacks, IEffectModuleDescriptor[] effectModules)
        {
            if (pactId == 0) throw new ArgumentOutOfRangeException(nameof(pactId));
            PactId = pactId;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            Tier = tier;
            Rarity = Math.Max(1, rarity);
            BaseWeight = Math.Max(0f, baseWeight);
            Tags = tags;
            Prerequisites = prerequisites ?? Array.Empty<uint>();
            Exclusions = exclusions ?? Array.Empty<uint>();
            Repeatable = repeatable;
            MaxStacks = repeatable ? Math.Max(1, maxStacks) : 1;
            RuntimeEffects = new EffectDefinition(
                EffectSourceKind.Pact, pactId, effectModules ?? Array.Empty<IEffectModuleDescriptor>());
        }
    }
}
