using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "PeriodicDamage", menuName = "Vampire Hunt/Effects/Periodic Damage")]
    public sealed class PeriodicDamageModuleAsset : EffectModuleAsset
    {
        [SerializeField, Min(0.01f)] private float interval = 1f;
        [SerializeField, Min(0f)] private float damagePerStack = 1f;
        [SerializeField] private DamageTags tags = DamageTags.Status | DamageTags.Periodic;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new PeriodicDamageEffectDescriptor(interval, damagePerStack, tags);
    }
}
