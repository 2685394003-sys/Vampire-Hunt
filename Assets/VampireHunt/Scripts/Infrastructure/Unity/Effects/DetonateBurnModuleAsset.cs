using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>燃爆（buffs_010）：结算目标身上灼烧的剩余 DoT 总量 × multiplier，并清除灼烧。</summary>
    [CreateAssetMenu(fileName = "DetonateBurn", menuName = "Vampire Hunt/Effects/Detonate Burn")]
    public sealed class DetonateBurnModuleAsset : EffectModuleAsset
    {
        [Tooltip("灼烧剩余伤害倍率（表格燃爆 p1 detonateDamageMult，默认 1.1）。")]
        [SerializeField, Min(0f)] private float multiplier = 1.1f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new DetonateBurnEffectDescriptor(multiplier);
    }
}
