using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "ApplyStatusOnHit", menuName = "Vampire Hunt/Effects/Apply Status On Hit")]
    public sealed class ApplyStatusOnHitModuleAsset : EffectModuleAsset
    {
        [SerializeField] private DamageTags requiredTags;
        [SerializeField, Min(1)] private uint statusId = 1;
        [SerializeField, Min(1)] private int stacksPerEffectStack = 1;
        [SerializeField, Min(0f)] private float duration;
        [SerializeField, Min(0f)] private float magnitude;
        [SerializeField] private ElementId element;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new ApplyStatusOnHitEffectDescriptor(
                requiredTags, statusId, stacksPerEffectStack, duration, magnitude, element);
    }
}
