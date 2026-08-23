using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "AbilityPlanModifier", menuName = "Vampire Hunt/Effects/Ability Plan Modifier")]
    public sealed class AbilityPlanModifierModuleAsset : EffectModuleAsset
    {
        [SerializeField] private DamageTags requiredTags;
        [SerializeField] private AbilityPlanProperty property;
        [SerializeField] private EffectNumericOperation operation;
        [SerializeField] private float perStackValue;
        [SerializeField] private float constantValue;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new AbilityPlanModifierEffectDescriptor(
                requiredTags, property, operation, perStackValue, constantValue);
    }
}
