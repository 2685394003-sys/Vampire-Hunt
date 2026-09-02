using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>闪电连锁（buffs_007）：获得闪电层时向周围敌人连锁，伤害逐跳 ×decay，并动态传导状态。</summary>
    [CreateAssetMenu(fileName = "ChainLightning", menuName = "Vampire Hunt/Effects/Chain Lightning")]
    public sealed class ChainLightningModuleAsset : EffectModuleAsset
    {
        [Tooltip("连锁半径（米，表格闪电 p3 chainRadius，默认 3）。")]
        [SerializeField, Min(0.1f)] private float radius = 3f;
        [Tooltip("每跳伤害衰减（表格闪电 p2 chainDecay，默认 0.7）。第 1 跳不衰减，逐跳 ×此值。")]
        [SerializeField, Range(0f, 1f)] private float decay = 0.7f;
        [Tooltip("连锁伤害基础值：每跳伤害 = 此值 × 武器属性精通(elementMastery) × 逐跳衰减。默认 10 = 玩家基础伤害。")]
        [SerializeField, Min(0f)] private float chainDamage = 10f;
        [Tooltip("基础传导数量：每次连锁的目标数 = 此值 + 当前闪电层数（jumps = baseJumps + 层数）。默认 3。")]
        [SerializeField, Min(0)] private int baseJumps = 3;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new ChainLightningEffectDescriptor(radius, decay, chainDamage, baseJumps);
    }
}
