using System;
using UnityEngine;
using VampireHunt.Economy;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Unity.Effects;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "AccessoryDefinition", menuName = "Vampire Hunt/Items/Accessory")]
    public sealed class AccessoryDefinitionAsset : ItemDefinitionAsset
    {
        [Header("Stacking")]
        [SerializeField] private AccessoryStackPolicy stackPolicy = AccessoryStackPolicy.Unique;
        [SerializeField, Min(1)] private int maxStacks = 1;

        [Header("Runtime Effects")]
        [SerializeField] private EffectModuleAsset[] effectModules = Array.Empty<EffectModuleAsset>();

        public override ItemKind Kind => ItemKind.Accessory;
        public AccessoryStackPolicy StackPolicy => stackPolicy;
        public int MaxStacks => stackPolicy == AccessoryStackPolicy.Unique
            ? 1
            : stackPolicy == AccessoryStackPolicy.Unlimited
                ? int.MaxValue
                : maxStacks;

        public override ItemDefinition ToDomain()
        {
            var descriptors = new IEffectModuleDescriptor[effectModules != null ? effectModules.Length : 0];
            for (int i = 0; i < descriptors.Length; i++)
                descriptors[i] = effectModules[i] != null ? effectModules[i].ToDescriptor() : null;
            return new AccessoryDefinition(
                ItemId,
                DisplayName,
                Description,
                stackPolicy,
                maxStacks,
                descriptors);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxStacks = stackPolicy == AccessoryStackPolicy.Unique ? 1 : Mathf.Max(1, maxStacks);
        }
    }
}
