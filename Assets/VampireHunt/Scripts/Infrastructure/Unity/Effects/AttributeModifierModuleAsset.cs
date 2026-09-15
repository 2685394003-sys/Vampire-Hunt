using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "AttributeModifier", menuName = "Vampire Hunt/Effects/Attribute Modifier")]
    public sealed class AttributeModifierModuleAsset : EffectModuleAsset
    {
        [SerializeField] private StatDefinition attribute;
        [SerializeField] private AttributeModifierOperation operation;
        [Tooltip("Value applied once for this effect source.")]
        [SerializeField] private float constantValue;
        [Tooltip("Additional value for every source stack. Use 0.1 for ten percent.")]
        [SerializeField] private float valuePerStack;

        public override IEffectModuleDescriptor ToDescriptor()
        {
            int attributeId = attribute != null && !string.IsNullOrWhiteSpace(attribute.statName)
                ? Animator.StringToHash(attribute.statName)
                : 0;
            return new AttributeModifierEffectDescriptor(
                attributeId, operation, constantValue, valuePerStack);
        }
    }
}
