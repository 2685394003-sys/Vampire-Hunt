using System;
using UnityEngine;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Unity.Effects;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "PactDefinition", menuName = "Vampire Hunt/Progression/Pact Definition")]
    public sealed class PactDefinitionAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Min(1)] private uint pactId = 1;
        [SerializeField] private string displayName = "New Pact";
        [SerializeField, TextArea(2, 5)] private string description;
        [SerializeField] private Sprite icon;

        [Header("Roll Rules")]
        [SerializeField] private PactTier tier;
        [SerializeField, Min(1)] private int rarity = 1;
        [SerializeField, Min(0f)] private float baseWeight = 100f;
        [SerializeField] private PactTags tags;
        [SerializeField] private uint[] prerequisites = Array.Empty<uint>();
        [SerializeField] private uint[] exclusions = Array.Empty<uint>();
        [SerializeField] private bool repeatable = true;
        [SerializeField, Min(1)] private int maxStacks = 1;

        [Header("Runtime Effects")]
        [SerializeField] private EffectModuleAsset[] effectModules = Array.Empty<EffectModuleAsset>();

        public uint PactId => pactId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public PactTier Tier => tier;

        public PactDefinition ToDomain()
        {
            var descriptors = new IEffectModuleDescriptor[effectModules != null ? effectModules.Length : 0];
            for (int i = 0; i < descriptors.Length; i++)
                descriptors[i] = effectModules[i] != null ? effectModules[i].ToDescriptor() : null;
            return new PactDefinition(pactId, displayName, description, tier, rarity, baseWeight, tags,
                prerequisites, exclusions, repeatable, maxStacks, descriptors);
        }

        private void OnValidate()
        {
            if (pactId == 0) pactId = 1;
            rarity = Mathf.Max(1, rarity);
            baseWeight = Mathf.Max(0f, baseWeight);
            maxStacks = Mathf.Max(1, maxStacks);
        }
    }
}
