using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Enemies;
using VampireHunt.Infrastructure.Unity.Effects;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Unity
{
    [Serializable]
    public struct EnemyStatModifierAssetEntry
    {
        [SerializeField] private EnemyStat stat;
        [SerializeField] private AttributeModifierOperation operation;
        [SerializeField] private float constantValue;
        [Tooltip("Additional value for every affix stack. Use 0.1 for ten percent.")]
        [SerializeField] private float valuePerStack;

        public EnemyStatModifierDefinition ToDomain() =>
            new EnemyStatModifierDefinition(stat, operation, constantValue, valuePerStack);
    }

    [CreateAssetMenu(
        fileName = "EnemyAffixDefinition",
        menuName = "Vampire Hunt/Progression/Enemy Affix Definition")]
    public sealed class EnemyAffixDefinitionAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Min(1)] private uint affixId = 1001;
        [SerializeField] private string displayName = "New Enemy Affix";
        [SerializeField, TextArea(2, 5)] private string description;
        [SerializeField] private Sprite icon;

        [Header("Roll Rules")]
        [SerializeField, Min(0f)] private float baseWeight = 100f;
        [SerializeField] private uint[] prerequisites = Array.Empty<uint>();
        [SerializeField] private uint[] exclusions = Array.Empty<uint>();
        [SerializeField] private bool repeatable = true;
        [SerializeField, Min(1)] private int maxStacks = 5;

        [Header("Enemy Filters")]
        [Tooltip("Empty means every archetype is included.")]
        [SerializeField] private string[] includedArchetypeIds = Array.Empty<string>();
        [SerializeField] private string[] excludedArchetypeIds = Array.Empty<string>();

        [Header("Spawn-time Stats")]
        [SerializeField] private EnemyStatModifierAssetEntry[] statModifiers =
            Array.Empty<EnemyStatModifierAssetEntry>();

        [Header("Runtime Effects")]
        [SerializeField] private EffectModuleAsset[] effectModules = Array.Empty<EffectModuleAsset>();

        public uint AffixId => affixId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;

        public EnemyAffixDefinition ToDomain()
        {
            var modifiers = new EnemyStatModifierDefinition[statModifiers != null ? statModifiers.Length : 0];
            for (int i = 0; i < modifiers.Length; i++) modifiers[i] = statModifiers[i].ToDomain();

            var descriptors = new List<IEffectModuleDescriptor>();
            if (effectModules != null)
            {
                for (int i = 0; i < effectModules.Length; i++)
                {
                    if (effectModules[i] == null) continue;
                    IEffectModuleDescriptor descriptor = effectModules[i].ToDescriptor();
                    if (descriptor != null) descriptors.Add(descriptor);
                }
            }

            return new EnemyAffixDefinition(
                affixId,
                displayName,
                description,
                baseWeight,
                prerequisites,
                exclusions,
                repeatable,
                maxStacks,
                includedArchetypeIds,
                excludedArchetypeIds,
                modifiers,
                descriptors.ToArray());
        }

        private void OnValidate()
        {
            if (affixId == 0) affixId = 1;
            baseWeight = Mathf.Max(0f, baseWeight);
            maxStacks = Mathf.Max(1, maxStacks);
        }
    }
}
