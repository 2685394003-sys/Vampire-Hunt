using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "ResourceCapacityModifier", menuName = "Vampire Hunt/Effects/Resource Capacity Modifier")]
    public sealed class ResourceCapacityModifierModuleAsset : EffectModuleAsset
    {
        [Tooltip("Health or Stamina StatDefinition.")]
        [SerializeField] private StatDefinition resource;
        [SerializeField] private AttributeModifierOperation operation;
        [SerializeField] private float constantValue;
        [Tooltip("Additional capacity modifier for every effect source stack. Use 0.1 for ten percent.")]
        [SerializeField] private float valuePerStack;
        [SerializeField] private CapacityChangePolicy changePolicy = CapacityChangePolicy.PreserveRatio;

        public override IEffectModuleDescriptor ToDescriptor()
        {
            int resourceId = resource != null && !string.IsNullOrWhiteSpace(resource.statName)
                ? Animator.StringToHash(resource.statName)
                : 0;
            return new ResourceCapacityModifierEffectDescriptor(
                resourceId, operation, constantValue, valuePerStack, changePolicy);
        }
    }
}
